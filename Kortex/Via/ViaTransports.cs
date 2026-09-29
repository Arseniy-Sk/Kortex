using System;
using System.Collections.Generic;
using System.Diagnostics;
using Kortex.Hid;
using Kortex.Input;
using Kortex.Models;

namespace Kortex.Via
{
    /// <summary>Транспорт VIA поверх raw-HID интерфейса платы.</summary>
    public sealed class HidViaTransport : IViaTransport
    {
        private readonly HidStream _stream;

        public HidViaTransport(HidStream stream)
        {
            _stream = stream;
        }

        public bool IsOpen => _stream != null && _stream.IsOpen;

        public string Description => _stream == null || _stream.Info == null
            ? null
            : _stream.Info.Product + " · " + _stream.Info.VidPid;

        public byte[] Exchange(byte[] request, int timeoutMs)
        {
            if (!IsOpen) return null;
            if (!_stream.Write(request, Math.Min(timeoutMs, 500))) return null;

            // Плата отвечает эхом команды в первом байте; чужие отчёты пропускаем.
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                var response = _stream.Read(Math.Max(50, timeoutMs - (int)clock.ElapsedMilliseconds));
                if (response == null) break;
                if (response.Length > 0 && response[0] == request[0]) return response;
            }
            return null;
        }

        public void Dispose()
        {
            if (_stream != null) _stream.Dispose();
        }
    }

    /// <summary>
    /// Программная модель платы с прошивкой VIA. Позволяет работать с разделом раскладки
    /// без подключённого железа и используется для проверки протокола.
    /// </summary>
    public sealed class MockViaDevice : IViaTransport
    {
        public const int Rows = 6;
        public const int Cols = 17;
        public const int Layers = 4;
        public const int MacroSlots = 16;
        public const int MacroBufferSize = 900;

        private readonly byte[] _keymap = new byte[Layers * Rows * Cols * 2];
        private readonly byte[] _macros = new byte[MacroBufferSize];
        private readonly Dictionary<byte, byte[]> _lighting = new Dictionary<byte, byte[]>
        {
            { ViaCommand.FlatRgbBrightness, new byte[] { 180 } },
            { ViaCommand.FlatRgbEffect,     new byte[] { 3 } },
            { ViaCommand.FlatRgbSpeed,      new byte[] { 128 } },
            { ViaCommand.FlatRgbColor,      new byte[] { 170, 255 } },
            { ViaCommand.FlatBacklightBrightness, new byte[] { 120 } },
            { ViaCommand.FlatBacklightEffect,     new byte[] { 1 } },
        };
        private readonly byte[] _defaults;
        private readonly Stopwatch _uptime = Stopwatch.StartNew();

        public bool IsOpen { get; private set; } = true;
        public string Description => "KBD8X MKIII (demo) · VID 4B42 · PID 8001";

        public MockViaDevice()
        {
            LoadFactoryKeymap();
            _defaults = (byte[])_keymap.Clone();
        }

        /// <summary>Заводская раскладка: слой 0 повторяет физическую схему TKL, слой 1 — медиа.</summary>
        private void LoadFactoryKeymap()
        {
            var keys = KeyboardLayout.Build(new LayoutOptions());
            var rows = new Dictionary<double, List<KeyDef>>();
            foreach (var k in keys)
            {
                if (k.IsBlocker) continue;
                List<KeyDef> bucket;
                if (!rows.TryGetValue(k.Y, out bucket)) { bucket = new List<KeyDef>(); rows[k.Y] = bucket; }
                bucket.Add(k);
            }

            var yValues = new List<double>(rows.Keys);
            yValues.Sort();

            for (int r = 0; r < yValues.Count && r < Rows; r++)
            {
                var bucket = rows[yValues[r]];
                bucket.Sort((a, b) => a.X.CompareTo(b.X));
                for (int c = 0; c < bucket.Count && c < Cols; c++)
                {
                    var def = bucket[c];
                    ushort code = def.IsFirmware
                        ? (ushort)(QmkKeycodes.QK_MOMENTARY | 1)
                        : QmkKeycodes.FromVirtualKey(def.Id);
                    Write(0, r, c, code);
                    Write(1, r, c, QmkKeycodes.KC_TRANSPARENT);
                    Write(2, r, c, QmkKeycodes.KC_TRANSPARENT);
                    Write(3, r, c, QmkKeycodes.KC_TRANSPARENT);
                }
            }

            // Слой 1: медиа на F-ряду
            ushort[] media = { 0xA8, 0xAA, 0xA9, 0xAC, 0xAE, 0xAB };
            for (int i = 0; i < media.Length; i++) Write(1, 0, i + 1, media[i]);
        }

        private void Write(int layer, int row, int col, ushort code)
        {
            int offset = ((layer * Rows + row) * Cols + col) * 2;
            if (offset + 1 >= _keymap.Length) return;
            _keymap[offset] = (byte)(code >> 8);
            _keymap[offset + 1] = (byte)(code & 0xFF);
        }

        public byte[] Exchange(byte[] request, int timeoutMs)
        {
            var response = new byte[ViaCommand.PacketSize];
            Array.Copy(request, response, request.Length);
            byte command = request[0];

            switch (command)
            {
                case ViaCommand.GetProtocolVersion:
                    response[1] = 0x00; response[2] = 0x09;   // протокол 9, как у актуальных сборок VIA
                    break;

                case ViaCommand.DynamicKeymapGetLayerCount:
                    response[1] = Layers;
                    break;

                case ViaCommand.MacroGetCount:
                    response[1] = MacroSlots;
                    break;

                case ViaCommand.MacroGetBufferSize:
                    response[1] = (byte)(MacroBufferSize >> 8);
                    response[2] = (byte)(MacroBufferSize & 0xFF);
                    break;

                case ViaCommand.GetKeyboardValue:
                    FillKeyboardValue(request, response);
                    break;

                case ViaCommand.DynamicKeymapGetKeycode:
                    {
                        int offset = ((request[1] * Rows + request[2]) * Cols + request[3]) * 2;
                        if (offset + 1 < _keymap.Length)
                        {
                            response[4] = _keymap[offset];
                            response[5] = _keymap[offset + 1];
                        }
                        break;
                    }

                case ViaCommand.DynamicKeymapSetKeycode:
                    {
                        int offset = ((request[1] * Rows + request[2]) * Cols + request[3]) * 2;
                        if (offset + 1 < _keymap.Length)
                        {
                            _keymap[offset] = request[4];
                            _keymap[offset + 1] = request[5];
                        }
                        break;
                    }

                case ViaCommand.DynamicKeymapGetBuffer:
                    CopyOut(_keymap, request, response);
                    break;

                case ViaCommand.DynamicKeymapSetBuffer:
                    CopyIn(_keymap, request);
                    break;

                case ViaCommand.MacroGetBuffer:
                    CopyOut(_macros, request, response);
                    break;

                case ViaCommand.MacroSetBuffer:
                    CopyIn(_macros, request);
                    break;

                case ViaCommand.DynamicKeymapReset:
                    Array.Copy(_defaults, _keymap, _keymap.Length);
                    break;

                case ViaCommand.MacroReset:
                    Array.Clear(_macros, 0, _macros.Length);
                    break;

                case ViaCommand.CustomGetValue:
                    {
                        byte[] stored;
                        if (_lighting.TryGetValue(request[1], out stored))
                            for (int i = 0; i < stored.Length && 2 + i < response.Length; i++)
                                response[2 + i] = stored[i];
                        break;
                    }

                case ViaCommand.CustomSetValue:
                    {
                        byte[] stored;
                        if (_lighting.TryGetValue(request[1], out stored))
                            for (int i = 0; i < stored.Length; i++) stored[i] = request[2 + i];
                        break;
                    }

                case ViaCommand.CustomSave:
                case ViaCommand.EepromReset:
                    break;
            }
            return response;
        }

        private void FillKeyboardValue(byte[] request, byte[] response)
        {
            switch (request[1])
            {
                case ViaCommand.ValueUptime:
                    uint ms = (uint)_uptime.ElapsedMilliseconds;
                    response[2] = (byte)(ms >> 24); response[3] = (byte)(ms >> 16);
                    response[4] = (byte)(ms >> 8); response[5] = (byte)ms;
                    break;
                case ViaCommand.ValueFirmwareVersion:
                    response[2] = 0; response[3] = 0; response[4] = 0; response[5] = 3;
                    break;
                case ViaCommand.ValueLayoutOptions:
                    response[4] = 0; response[5] = 0;
                    break;
                case ViaCommand.ValueSwitchMatrixState:
                    for (int i = 2; i < response.Length; i++) response[i] = 0;
                    break;
            }
        }

        private static void CopyOut(byte[] source, byte[] request, byte[] response)
        {
            int offset = (request[1] << 8) | request[2];
            int size = Math.Min((int)request[3], ViaCommand.BufferChunk);
            for (int i = 0; i < size; i++)
            {
                int at = offset + i;
                response[4 + i] = at < source.Length ? source[at] : (byte)0;
            }
        }

        private static void CopyIn(byte[] target, byte[] request)
        {
            int offset = (request[1] << 8) | request[2];
            int size = Math.Min((int)request[3], ViaCommand.BufferChunk);
            for (int i = 0; i < size; i++)
            {
                int at = offset + i;
                if (at < target.Length) target[at] = request[4 + i];
            }
        }

        public void Dispose() => IsOpen = false;
    }
}
