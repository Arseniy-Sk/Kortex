using System;
using System.Collections.Generic;
using Kortex.Input;

namespace Kortex.Models
{
    /// <summary>Описание одного колпачка на схеме платы.</summary>
    public sealed class KeyDef
    {
        public KeyId Id { get; set; }
        public string Label { get; set; }
        /// <summary>Верхняя (шифтовая) легенда.</summary>
        public string Sub { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; } = 1;
        public double H { get; set; } = 1;
        /// <summary>Заглушка стэпа (Stepped Caps) — не клавиша.</summary>
        public bool IsBlocker { get; set; }
        /// <summary>Клавиша обрабатывается прошивкой (Fn) и не доходит до Windows.</summary>
        public bool IsFirmware { get; set; }

        public string Uid => (IsBlocker ? "blocker@" : IsFirmware ? "fn@" : Id.ToString() + "@") +
                             X.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                             Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Варианты сборки PCB KBD8X MKIII.</summary>
    public sealed class LayoutOptions
    {
        public bool Iso { get; set; }
        public bool SplitBackspace { get; set; }
        public bool SteppedCaps { get; set; }
        public bool SplitRightShift { get; set; }
        public bool SevenUnitSpace { get; set; }

        public LayoutOptions Clone() => (LayoutOptions)MemberwiseClone();
    }

    /// <summary>
    /// Генератор раскладки TKL (87 клавиш) для плат семейства KBDfans KBD8X MKIII.
    /// Координаты — в юнитах (1u = один стандартный колпачок).
    /// </summary>
    public static class KeyboardLayout
    {
        public const double TotalWidth = 18.25;
        public const double TotalHeight = 6.5;

        public static List<KeyDef> Build(LayoutOptions o)
        {
            o = o ?? new LayoutOptions();
            var k = new List<KeyDef>();

            // --------- F-ряд ---------
            Add(k, VK(0x1B), "Esc", null, 0, 0);
            double[] fx = { 2, 3, 4, 5, 6.5, 7.5, 8.5, 9.5, 11, 12, 13, 14 };
            for (int i = 0; i < 12; i++)
                Add(k, VK(0x70 + i), "F" + (i + 1), null, fx[i], 0);
            Add(k, new KeyId(KeyCodes.VK_SNAPSHOT, true), "PrtSc", null, 15.25, 0);
            Add(k, VK(KeyCodes.VK_SCROLL), "ScrLk", null, 16.25, 0);
            Add(k, VK(KeyCodes.VK_PAUSE), "Pause", null, 17.25, 0);

            // --------- Цифровой ряд ---------
            const double R1 = 1.5;
            Add(k, VK(0xC0), "`", "~", 0, R1);
            string[] digits = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
            string[] shifted = { "!", "@", "#", "$", "%", "^", "&", "*", "(", ")" };
            for (int i = 0; i < 10; i++)
                Add(k, VK(i < 9 ? 0x31 + i : 0x30), digits[i], shifted[i], 1 + i, R1);
            Add(k, VK(0xBD), "-", "_", 11, R1);
            Add(k, VK(0xBB), "=", "+", 12, R1);
            if (o.SplitBackspace && !o.Iso)
            {
                Add(k, VK(0xDC), "\\", "|", 13, R1);
                Add(k, VK(0x08), "Bksp", null, 14, R1);
            }
            else
            {
                Add(k, VK(0x08), "Backspace", null, 13, R1, 2);
            }
            Add(k, new KeyId(KeyCodes.VK_INSERT, true), "Ins", null, 15.25, R1);
            Add(k, new KeyId(KeyCodes.VK_HOME, true), "Home", null, 16.25, R1);
            Add(k, new KeyId(KeyCodes.VK_PRIOR, true), "PgUp", null, 17.25, R1);

            // --------- Ряд Q ---------
            const double R2 = 2.5;
            Add(k, VK(0x09), "Tab", null, 0, R2, 1.5);
            string[] qrow = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" };
            for (int i = 0; i < qrow.Length; i++)
                Add(k, VK(qrow[i][0]), qrow[i], null, 1.5 + i, R2);
            Add(k, VK(0xDB), "[", "{", 11.5, R2);
            if (o.Iso)
            {
                Add(k, VK(0xDD), "]", "}", 12.5, R2, 1.25);
                Add(k, VK(0x0D), "Enter", null, 13.75, R2, 1.25, 2); // ISO Enter
            }
            else
            {
                Add(k, VK(0xDD), "]", "}", 12.5, R2);
                Add(k, VK(0xDC), "\\", "|", 13.5, R2, 1.5);
            }
            Add(k, new KeyId(KeyCodes.VK_DELETE, true), "Del", null, 15.25, R2);
            Add(k, new KeyId(KeyCodes.VK_END, true), "End", null, 16.25, R2);
            Add(k, new KeyId(KeyCodes.VK_NEXT, true), "PgDn", null, 17.25, R2);

            // --------- Ряд A ---------
            const double R3 = 3.5;
            if (o.SteppedCaps)
            {
                Add(k, VK(KeyCodes.VK_CAPITAL), "Caps", null, 0, R3, 1.25);
                k.Add(new KeyDef { Label = "", X = 1.25, Y = R3, W = 0.5, H = 1, IsBlocker = true });
            }
            else
            {
                Add(k, VK(KeyCodes.VK_CAPITAL), "Caps Lock", null, 0, R3, 1.75);
            }
            string[] arow = { "A", "S", "D", "F", "G", "H", "J", "K", "L" };
            for (int i = 0; i < arow.Length; i++)
                Add(k, VK(arow[i][0]), arow[i], null, 1.75 + i, R3);
            Add(k, VK(0xBA), ";", ":", 10.75, R3);
            Add(k, VK(0xDE), "'", "\"", 11.75, R3);
            if (o.Iso)
                Add(k, VK(0xDC), "#", "~", 12.75, R3);
            else
                Add(k, VK(0x0D), "Enter", null, 12.75, R3, 2.25);

            // --------- Ряд Z ---------
            const double R4 = 4.5;
            if (o.Iso)
            {
                Add(k, VK(KeyCodes.VK_LSHIFT), "Shift", null, 0, R4, 1.25);
                Add(k, VK(0xE2), "\\", "|", 1.25, R4);
            }
            else
            {
                Add(k, VK(KeyCodes.VK_LSHIFT), "Shift", null, 0, R4, 2.25);
            }
            string[] zrow = { "Z", "X", "C", "V", "B", "N", "M" };
            for (int i = 0; i < zrow.Length; i++)
                Add(k, VK(zrow[i][0]), zrow[i], null, 2.25 + i, R4);
            Add(k, VK(0xBC), ",", "<", 9.25, R4);
            Add(k, VK(0xBE), ".", ">", 10.25, R4);
            Add(k, VK(0xBF), "/", "?", 11.25, R4);
            if (o.SplitRightShift)
            {
                Add(k, VK(KeyCodes.VK_RSHIFT), "Shift", null, 12.25, R4, 1.75);
                k.Add(new KeyDef { Label = "Fn", X = 14, Y = R4, W = 1, H = 1, IsFirmware = true });
            }
            else
            {
                Add(k, VK(KeyCodes.VK_RSHIFT), "Shift", null, 12.25, R4, 2.75);
            }
            Add(k, new KeyId(KeyCodes.VK_UP, true), "↑", null, 16.25, R4);

            // --------- Нижний ряд ---------
            const double R5 = 5.5;
            if (o.SevenUnitSpace)
            {
                Add(k, VK(KeyCodes.VK_LCONTROL), "Ctrl", null, 0, R5, 1.5);
                Add(k, new KeyId(KeyCodes.VK_LWIN, true), "Win", null, 1.5, R5);
                Add(k, VK(KeyCodes.VK_LMENU), "Alt", null, 2.5, R5, 1.5);
                Add(k, VK(KeyCodes.VK_SPACE), "", null, 4, R5, 7);
                Add(k, new KeyId(KeyCodes.VK_RMENU, true), "Alt", null, 11, R5, 1.5);
                k.Add(new KeyDef { Label = "Fn", X = 12.5, Y = R5, W = 1, H = 1, IsFirmware = true });
                Add(k, new KeyId(KeyCodes.VK_RCONTROL, true), "Ctrl", null, 13.5, R5, 1.5);
            }
            else
            {
                Add(k, VK(KeyCodes.VK_LCONTROL), "Ctrl", null, 0, R5, 1.25);
                Add(k, new KeyId(KeyCodes.VK_LWIN, true), "Win", null, 1.25, R5, 1.25);
                Add(k, VK(KeyCodes.VK_LMENU), "Alt", null, 2.5, R5, 1.25);
                Add(k, VK(KeyCodes.VK_SPACE), "", null, 3.75, R5, 6.25);
                Add(k, new KeyId(KeyCodes.VK_RMENU, true), "Alt", null, 10, R5, 1.25);
                Add(k, new KeyId(KeyCodes.VK_RWIN, true), "Win", null, 11.25, R5, 1.25);
                k.Add(new KeyDef { Label = "Fn", X = 12.5, Y = R5, W = 1.25, H = 1, IsFirmware = true });
                Add(k, new KeyId(KeyCodes.VK_RCONTROL, true), "Ctrl", null, 13.75, R5, 1.25);
            }
            Add(k, new KeyId(KeyCodes.VK_LEFT, true), "←", null, 15.25, R5);
            Add(k, new KeyId(KeyCodes.VK_DOWN, true), "↓", null, 16.25, R5);
            Add(k, new KeyId(KeyCodes.VK_RIGHT, true), "→", null, 17.25, R5);

            return k;
        }

        private static KeyId VK(int vk) => new KeyId(vk, false);
        private static KeyId VK(char c) => new KeyId(c, false);

        private static void Add(List<KeyDef> list, KeyId id, string label, string sub,
                                double x, double y, double w = 1, double h = 1)
        {
            list.Add(new KeyDef { Id = id, Label = label, Sub = sub, X = x, Y = y, W = w, H = h });
        }
    }
}
