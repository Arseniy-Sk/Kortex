using System;
using System.Collections.Generic;
using System.Text;
using Kortex.Models;

namespace Kortex.Via
{
    /// <summary>Результат подготовки макроса к записи в плату.</summary>
    public sealed class MacroEncodeResult
    {
        public byte[] Buffer { get; set; }
        /// <summary>Сколько макросов поместилось.</summary>
        public int Written { get; set; }
        /// <summary>Шаги, которые прошивка выполнить не умеет, — они пропущены.</summary>
        public List<string> Skipped { get; } = new List<string>();
        public bool Overflow { get; set; }
    }

    /// <summary>
    /// Кодирование макросов в формат динамических макросов VIA.
    /// Буфер платы содержит подряд идущие последовательности, каждая закрыта нулевым байтом.
    /// Спецдействия начинаются с префикса 0x01.
    /// </summary>
    public static class ViaMacroCodec
    {
        private const byte Prefix = 0x01;
        private const byte TapCode = 0x01;
        private const byte DownCode = 0x02;
        private const byte UpCode = 0x03;
        private const byte DelayCode = 0x04;

        /// <summary>Может ли шаг жить в прошивке платы.</summary>
        public static bool IsFirmwareStep(MacroStep step)
        {
            switch (step.Kind)
            {
                case MacroStepKind.Tap:
                case MacroStepKind.Down:
                case MacroStepKind.Up:
                case MacroStepKind.Delay:
                    return true;
                case MacroStepKind.Text:
                    return IsAscii(step.Text);
                default:
                    return false;   // запуск программ, мышь, буфер обмена — только программный режим
            }
        }

        public static bool IsFirmwareMacro(Macro macro)
        {
            foreach (var step in macro.Steps)
                if (!IsFirmwareStep(step)) return false;
            return true;
        }

        private static bool IsAscii(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            foreach (char c in text)
                if (c < 0x20 || c > 0x7E) return false;
            return true;
        }

        public static MacroEncodeResult Encode(IList<Macro> macros, int slots, int bufferSize)
        {
            var result = new MacroEncodeResult();
            var buffer = new List<byte>();

            for (int i = 0; i < slots; i++)
            {
                if (i < macros.Count)
                {
                    var body = EncodeOne(macros[i], result);
                    if (buffer.Count + body.Count + 1 > bufferSize)
                    {
                        result.Overflow = true;
                        buffer.Add(0);           // пустой слот вместо не влезшего макроса
                        continue;
                    }
                    buffer.AddRange(body);
                    result.Written++;
                }
                buffer.Add(0);                   // конец последовательности
            }

            while (buffer.Count < bufferSize) buffer.Add(0);
            result.Buffer = buffer.ToArray();
            return result;
        }

        private static List<byte> EncodeOne(Macro macro, MacroEncodeResult result)
        {
            var bytes = new List<byte>();
            int repeat = Math.Max(1, macro.Repeat);

            for (int r = 0; r < repeat; r++)
            {
                foreach (var step in macro.Steps)
                {
                    if (!IsFirmwareStep(step))
                    {
                        string note = macro.Name + ": " + step.Display;
                        if (!result.Skipped.Contains(note)) result.Skipped.Add(note);
                        continue;
                    }

                    switch (step.Kind)
                    {
                        case MacroStepKind.Tap:
                            Emit(bytes, TapCode, step.KeyId);
                            break;
                        case MacroStepKind.Down:
                            Emit(bytes, DownCode, step.KeyId);
                            break;
                        case MacroStepKind.Up:
                            Emit(bytes, UpCode, step.KeyId);
                            break;
                        case MacroStepKind.Delay:
                            EmitDelay(bytes, step.DelayMs);
                            break;
                        case MacroStepKind.Text:
                            foreach (char c in step.Text ?? string.Empty) bytes.Add((byte)c);
                            break;
                    }
                }
            }
            return bytes;
        }

        private static void Emit(List<byte> bytes, byte action, Input.KeyId key)
        {
            ushort code = QmkKeycodes.FromVirtualKey(key);
            if (code == 0 || code > 0xFF) return;    // в прошивку уходят только базовые коды
            bytes.Add(Prefix);
            bytes.Add(action);
            bytes.Add((byte)code);
        }

        /// <summary>Задержка кодируется двумя байтами со сдвигом на единицу, чтобы не было нулей.</summary>
        private static void EmitDelay(List<byte> bytes, int ms)
        {
            int value = Math.Max(0, Math.Min(255 * 254, ms));
            bytes.Add(Prefix);
            bytes.Add(DelayCode);
            bytes.Add((byte)((value % 255) + 1));
            bytes.Add((byte)((value / 255) + 1));
        }

        /// <summary>Разбирает буфер платы обратно в макросы.</summary>
        public static List<Macro> Decode(byte[] buffer, int slots)
        {
            var macros = new List<Macro>();
            if (buffer == null) return macros;

            int at = 0;
            for (int i = 0; i < slots && at < buffer.Length; i++)
            {
                var macro = new Macro { Name = "M" + i, Repeat = 1, Speed = 1.0 };
                var text = new StringBuilder();

                while (at < buffer.Length && buffer[at] != 0)
                {
                    if (buffer[at] == Prefix && at + 2 < buffer.Length)
                    {
                        FlushText(macro, text);
                        byte action = buffer[at + 1];
                        if (action == DelayCode && at + 3 < buffer.Length)
                        {
                            int ms = (buffer[at + 2] - 1) + (buffer[at + 3] - 1) * 255;
                            macro.Steps.Add(new MacroStep { Kind = MacroStepKind.Delay, DelayMs = ms });
                            at += 4;
                        }
                        else
                        {
                            var kind = action == DownCode ? MacroStepKind.Down
                                     : action == UpCode ? MacroStepKind.Up
                                     : MacroStepKind.Tap;
                            macro.Steps.Add(new MacroStep
                            {
                                Kind = kind,
                                Key = KeyIdFromQmk(buffer[at + 2])
                            });
                            at += 3;
                        }
                        continue;
                    }

                    text.Append((char)buffer[at]);
                    at++;
                }

                FlushText(macro, text);
                at++;                       // пропускаем нулевой разделитель
                if (macro.Steps.Count > 0) macros.Add(macro);
            }
            return macros;
        }

        private static void FlushText(Macro macro, StringBuilder text)
        {
            if (text.Length == 0) return;
            macro.Steps.Add(new MacroStep { Kind = MacroStepKind.Text, Text = text.ToString() });
            text.Clear();
        }

        private static string KeyIdFromQmk(byte code)
        {
            int vk = QmkKeycodes.ToVirtualKey(code);
            return new Input.KeyId(vk, QmkKeycodes.IsExtended(code)).ToString();
        }
    }
}
