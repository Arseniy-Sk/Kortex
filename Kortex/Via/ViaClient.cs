using System;
using System.Collections.Generic;

namespace Kortex.Via
{
    /// <summary>Команды протокола VIA (совместимы с прошивками QMK с включённым VIA).</summary>
    public static class ViaCommand
    {
        public const byte GetProtocolVersion = 0x01;
        public const byte GetKeyboardValue = 0x02;
        public const byte SetKeyboardValue = 0x03;
        public const byte DynamicKeymapGetKeycode = 0x04;
        public const byte DynamicKeymapSetKeycode = 0x05;
        public const byte DynamicKeymapReset = 0x06;
        public const byte CustomSetValue = 0x07;
        public const byte CustomGetValue = 0x08;
        public const byte CustomSave = 0x09;
        public const byte EepromReset = 0x0A;
        public const byte BootloaderJump = 0x0B;
        public const byte MacroGetCount = 0x0C;
        public const byte MacroGetBufferSize = 0x0D;
        public const byte MacroGetBuffer = 0x0E;
        public const byte MacroSetBuffer = 0x0F;
        public const byte MacroReset = 0x10;
        public const byte DynamicKeymapGetLayerCount = 0x11;
        public const byte DynamicKeymapGetBuffer = 0x12;
        public const byte DynamicKeymapSetBuffer = 0x13;
        public const byte DynamicKeymapGetEncoder = 0x14;
        public const byte DynamicKeymapSetEncoder = 0x15;
        public const byte VialPrefix = 0xFE;

        // Каналы пользовательских значений (протокол VIA 3 и новее)
        public const byte ChannelBacklight = 0x00;
        public const byte ChannelRgbLight = 0x01;
        public const byte ChannelRgbMatrix = 0x02;

        // Значения подсветки в плоской нумерации (протокол VIA 2)
        public const byte FlatBacklightBrightness = 0x09;
        public const byte FlatBacklightEffect = 0x0A;
        public const byte FlatRgbBrightness = 0x80;
        public const byte FlatRgbEffect = 0x81;
        public const byte FlatRgbSpeed = 0x82;
        public const byte FlatRgbColor = 0x83;

        // Те же значения в канальной нумерации
        public const byte ValueBrightness = 0x01;
        public const byte ValueEffect = 0x02;
        public const byte ValueSpeed = 0x03;
        public const byte ValueColor = 0x04;

        // Значения для GetKeyboardValue
        public const byte ValueUptime = 0x01;
        public const byte ValueLayoutOptions = 0x02;
        public const byte ValueSwitchMatrixState = 0x03;
        public const byte ValueFirmwareVersion = 0x04;

        public const int PacketSize = 32;
        /// <summary>Сколько байт буфера влезает в один пакет (4 байта занимает заголовок).</summary>
        public const int BufferChunk = 28;
    }

    /// <summary>Канал обмена пакетами с платой. Реализуется поверх HID или эмулятора.</summary>
    public interface IViaTransport : IDisposable
    {
        bool IsOpen { get; }
        string Description { get; }
        byte[] Exchange(byte[] request, int timeoutMs);
    }

    /// <summary>Высокоуровневые операции VIA поверх транспорта.</summary>
    public sealed class ViaClient : IDisposable
    {
        private readonly IViaTransport _transport;
        private readonly object _sync = new object();

        public ViaClient(IViaTransport transport)
        {
            _transport = transport;
        }

        public bool IsOpen => _transport != null && _transport.IsOpen;
        public string Description => _transport == null ? null : _transport.Description;

        private byte[] Send(int timeoutMs, params byte[] payload)
        {
            if (_transport == null || !_transport.IsOpen) return null;
            var packet = new byte[ViaCommand.PacketSize];
            Array.Copy(payload, packet, Math.Min(payload.Length, packet.Length));
            lock (_sync)
            {
                try { return _transport.Exchange(packet, timeoutMs); }
                catch { return null; }
            }
        }

        /// <summary>Версия протокола VIA или -1, если плата не отвечает.</summary>
        public int GetProtocolVersion()
        {
            var r = Send(400, ViaCommand.GetProtocolVersion);
            if (r == null || r.Length < 3 || r[0] != ViaCommand.GetProtocolVersion) return -1;
            return (r[1] << 8) | r[2];
        }

        public int GetLayerCount()
        {
            var r = Send(400, ViaCommand.DynamicKeymapGetLayerCount);
            if (r == null || r.Length < 2 || r[0] != ViaCommand.DynamicKeymapGetLayerCount) return -1;
            return r[1];
        }

        public int GetMacroCount()
        {
            var r = Send(400, ViaCommand.MacroGetCount);
            if (r == null || r.Length < 2) return -1;
            return r[1];
        }

        public int GetMacroBufferSize()
        {
            var r = Send(400, ViaCommand.MacroGetBufferSize);
            if (r == null || r.Length < 3) return -1;
            return (r[1] << 8) | r[2];
        }

        public uint GetUptime()
        {
            var r = Send(400, ViaCommand.GetKeyboardValue, ViaCommand.ValueUptime);
            if (r == null || r.Length < 6) return 0;
            return (uint)((r[2] << 24) | (r[3] << 16) | (r[4] << 8) | r[5]);
        }

        public string GetFirmwareVersion()
        {
            var r = Send(400, ViaCommand.GetKeyboardValue, ViaCommand.ValueFirmwareVersion);
            if (r == null || r.Length < 6) return null;
            uint v = (uint)((r[2] << 24) | (r[3] << 16) | (r[4] << 8) | r[5]);
            return v == 0 ? null : v.ToString();
        }

        public int GetLayoutOptions()
        {
            var r = Send(400, ViaCommand.GetKeyboardValue, ViaCommand.ValueLayoutOptions);
            if (r == null || r.Length < 6) return -1;
            return (r[4] << 8) | r[5];
        }

        /// <summary>Состояние матрицы: по биту на пересечение. Используется для обучения матрицы.</summary>
        public byte[] GetSwitchMatrixState()
        {
            var r = Send(300, ViaCommand.GetKeyboardValue, ViaCommand.ValueSwitchMatrixState);
            if (r == null || r.Length < 3) return null;
            var state = new byte[r.Length - 2];
            Array.Copy(r, 2, state, 0, state.Length);
            return state;
        }

        public ushort GetKeycode(int layer, int row, int col)
        {
            var r = Send(400, ViaCommand.DynamicKeymapGetKeycode, (byte)layer, (byte)row, (byte)col);
            if (r == null || r.Length < 6) return QmkKeycodes.KC_NO;
            return (ushort)((r[4] << 8) | r[5]);
        }

        public bool SetKeycode(int layer, int row, int col, ushort code)
        {
            var r = Send(600, ViaCommand.DynamicKeymapSetKeycode, (byte)layer, (byte)row, (byte)col,
                (byte)(code >> 8), (byte)(code & 0xFF));
            return r != null && r[0] == ViaCommand.DynamicKeymapSetKeycode;
        }

        public bool SetLayoutOptions(int value)
        {
            var r = Send(600, ViaCommand.SetKeyboardValue, ViaCommand.ValueLayoutOptions,
                0, 0, (byte)(value >> 8), (byte)(value & 0xFF));
            return r != null;
        }

        // ---------------- Энкодеры ----------------

        public ushort GetEncoder(int layer, int index, bool clockwise)
        {
            var r = Send(400, ViaCommand.DynamicKeymapGetEncoder, (byte)layer, (byte)index,
                (byte)(clockwise ? 1 : 0));
            if (r == null || r.Length < 6) return QmkKeycodes.KC_NO;
            return (ushort)((r[4] << 8) | r[5]);
        }

        public bool SetEncoder(int layer, int index, bool clockwise, ushort code)
        {
            var r = Send(600, ViaCommand.DynamicKeymapSetEncoder, (byte)layer, (byte)index,
                (byte)(clockwise ? 1 : 0), (byte)(code >> 8), (byte)(code & 0xFF));
            return r != null && r[0] == ViaCommand.DynamicKeymapSetEncoder;
        }

        // ---------------- Подсветка ----------------

        /// <summary>С третьей версии протокола значения адресуются каналом, раньше — плоским номером.</summary>
        public bool UsesChannels => ProtocolVersion >= 0x000B;

        /// <summary>Версия протокола, полученная при подключении.</summary>
        public int ProtocolVersion { get; set; } = -1;

        public int[] GetLightingValue(byte channel, byte channelValue, byte flatValue, int count)
        {
            byte[] r = UsesChannels
                ? Send(400, ViaCommand.CustomGetValue, channel, channelValue)
                : Send(400, ViaCommand.CustomGetValue, flatValue);
            if (r == null) return null;

            int offset = UsesChannels ? 3 : 2;
            if (r.Length < offset + count) return null;
            var values = new int[count];
            for (int i = 0; i < count; i++) values[i] = r[offset + i];
            return values;
        }

        public bool SetLightingValue(byte channel, byte channelValue, byte flatValue, params byte[] data)
        {
            var packet = new List<byte> { ViaCommand.CustomSetValue };
            if (UsesChannels) { packet.Add(channel); packet.Add(channelValue); }
            else packet.Add(flatValue);
            packet.AddRange(data);

            var r = Send(600, packet.ToArray());
            return r != null;
        }

        /// <summary>Сохраняет настройки подсветки в память платы.</summary>
        public bool SaveLighting(byte channel)
        {
            var r = UsesChannels
                ? Send(900, ViaCommand.CustomSave, channel)
                : Send(900, ViaCommand.CustomSave);
            return r != null;
        }

        // ---------------- Сервисные команды ----------------

        /// <summary>Полный сброс настроек платы к заводским.</summary>
        public bool EepromReset()
        {
            var r = Send(2000, ViaCommand.EepromReset);
            return r != null;
        }

        /// <summary>Перевод платы в режим прошивки. Клавиатура отключится от системы.</summary>
        public bool BootloaderJump()
        {
            Send(500, ViaCommand.BootloaderJump);
            return true;      // плата уходит в загрузчик и ответить уже не успевает
        }

        /// <summary>Отвечает ли плата на расширенные команды Vial.</summary>
        public bool DetectVial()
        {
            var r = Send(400, ViaCommand.VialPrefix, 0x00);
            if (r == null || r.Length < 4) return false;
            // Прошивка без Vial возвращает команду нетронутой либо помечает её как неизвестную
            if (r[0] == 0xFF) return false;
            for (int i = 0; i < 4; i++) if (r[i] != 0) return true;
            return false;
        }

        public bool ResetKeymap()
        {
            var r = Send(1500, ViaCommand.DynamicKeymapReset);
            return r != null;
        }

        public bool ResetMacros()
        {
            var r = Send(1500, ViaCommand.MacroReset);
            return r != null;
        }

        /// <summary>Читает произвольный участок буфера раскладки.</summary>
        public byte[] ReadKeymapBuffer(int offset, int size) =>
            ReadBuffer(ViaCommand.DynamicKeymapGetBuffer, offset, size);

        public bool WriteKeymapBuffer(int offset, byte[] data) =>
            WriteBuffer(ViaCommand.DynamicKeymapSetBuffer, offset, data);

        public byte[] ReadMacroBuffer(int offset, int size) =>
            ReadBuffer(ViaCommand.MacroGetBuffer, offset, size);

        public bool WriteMacroBuffer(int offset, byte[] data) =>
            WriteBuffer(ViaCommand.MacroSetBuffer, offset, data);

        private byte[] ReadBuffer(byte command, int offset, int size)
        {
            var result = new byte[size];
            int done = 0;
            while (done < size)
            {
                int chunk = Math.Min(ViaCommand.BufferChunk, size - done);
                int at = offset + done;
                var r = Send(500, command, (byte)(at >> 8), (byte)(at & 0xFF), (byte)chunk);
                if (r == null || r.Length < 4 + chunk) return null;
                Array.Copy(r, 4, result, done, chunk);
                done += chunk;
            }
            return result;
        }

        private bool WriteBuffer(byte command, int offset, byte[] data)
        {
            int done = 0;
            while (done < data.Length)
            {
                int chunk = Math.Min(ViaCommand.BufferChunk, data.Length - done);
                int at = offset + done;
                var packet = new List<byte> { command, (byte)(at >> 8), (byte)(at & 0xFF), (byte)chunk };
                for (int i = 0; i < chunk; i++) packet.Add(data[done + i]);
                var r = Send(700, packet.ToArray());
                if (r == null) return false;
                done += chunk;
            }
            return true;
        }

        /// <summary>Читает весь слой целиком одним потоком чтений буфера.</summary>
        public ushort[,] ReadLayer(int layer, int rows, int cols)
        {
            int cells = rows * cols;
            int offset = layer * cells * 2;
            var raw = ReadKeymapBuffer(offset, cells * 2);
            if (raw == null) return null;

            var map = new ushort[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int i = (r * cols + c) * 2;
                    map[r, c] = (ushort)((raw[i] << 8) | raw[i + 1]);
                }
            return map;
        }

        public void Dispose()
        {
            if (_transport != null) _transport.Dispose();
        }
    }
}
