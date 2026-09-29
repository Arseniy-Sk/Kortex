using System;
using System.Collections.Generic;
using System.Linq;

namespace Kortex.Input
{
    /// <summary>Виртуальная клавиша: код + признак extended (для различения Right Alt, стрелок и т.п.).</summary>
    public readonly struct KeyId : IEquatable<KeyId>
    {
        public int Vk { get; }
        public bool Extended { get; }

        public KeyId(int vk, bool extended = false)
        {
            Vk = vk;
            Extended = extended;
        }

        public bool Equals(KeyId other) => Vk == other.Vk && Extended == other.Extended;
        public override bool Equals(object obj) => obj is KeyId k && Equals(k);
        public override int GetHashCode() => (Vk << 1) | (Extended ? 1 : 0);
        public override string ToString() => Extended ? Vk + "e" : Vk.ToString();

        public static KeyId Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return default;
            bool ext = s.EndsWith("e", StringComparison.OrdinalIgnoreCase);
            if (ext) s = s.Substring(0, s.Length - 1);
            int vk;
            int.TryParse(s, out vk);
            return new KeyId(vk, ext);
        }

        public string Name => KeyCodes.Name(this);
    }

    /// <summary>Таблица виртуальных кодов, человекочитаемых имён и служебных признаков.</summary>
    public static class KeyCodes
    {
        // Часто используемые коды
        public const int VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_PAUSE = 0x13, VK_CAPITAL = 0x14;
        public const int VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23;
        public const int VK_HOME = 0x24, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
        public const int VK_SNAPSHOT = 0x2C, VK_INSERT = 0x2D, VK_DELETE = 0x2E;
        public const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_APPS = 0x5D;
        public const int VK_NUMLOCK = 0x90, VK_SCROLL = 0x91;
        public const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
        public const int VK_LMENU = 0xA4, VK_RMENU = 0xA5;

        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            { 0x08, "Backspace" }, { 0x09, "Tab" }, { 0x0C, "Clear" }, { 0x0D, "Enter" },
            { 0x13, "Pause" }, { 0x14, "Caps Lock" }, { 0x1B, "Esc" }, { 0x20, "Space" },
            { 0x21, "Page Up" }, { 0x22, "Page Down" }, { 0x23, "End" }, { 0x24, "Home" },
            { 0x25, "Left" }, { 0x26, "Up" }, { 0x27, "Right" }, { 0x28, "Down" },
            { 0x2C, "Print Screen" }, { 0x2D, "Insert" }, { 0x2E, "Delete" },
            { 0x5B, "Left Win" }, { 0x5C, "Right Win" }, { 0x5D, "Menu" },
            { 0x90, "Num Lock" }, { 0x91, "Scroll Lock" },
            { 0xA0, "Left Shift" }, { 0xA1, "Right Shift" },
            { 0xA2, "Left Ctrl" }, { 0xA3, "Right Ctrl" },
            { 0xA4, "Left Alt" }, { 0xA5, "Right Alt" },
            { 0x10, "Shift" }, { 0x11, "Ctrl" }, { 0x12, "Alt" },
            { 0xBA, ";" }, { 0xBB, "=" }, { 0xBC, "," }, { 0xBD, "-" }, { 0xBE, "." },
            { 0xBF, "/" }, { 0xC0, "`" }, { 0xDB, "[" }, { 0xDC, "\\" }, { 0xDD, "]" }, { 0xDE, "'" },
            { 0xE2, "\\ (ISO)" },
            { 0x60, "Num 0" }, { 0x61, "Num 1" }, { 0x62, "Num 2" }, { 0x63, "Num 3" }, { 0x64, "Num 4" },
            { 0x65, "Num 5" }, { 0x66, "Num 6" }, { 0x67, "Num 7" }, { 0x68, "Num 8" }, { 0x69, "Num 9" },
            { 0x6A, "Num *" }, { 0x6B, "Num +" }, { 0x6D, "Num -" }, { 0x6E, "Num ." }, { 0x6F, "Num /" },
            { 0xAD, "Mute" }, { 0xAE, "Volume -" }, { 0xAF, "Volume +" },
            { 0xB0, "Next Track" }, { 0xB1, "Prev Track" }, { 0xB2, "Stop Media" }, { 0xB3, "Play/Pause" },
            { 0xB4, "Mail" }, { 0xB5, "Media Select" }, { 0xB6, "App 1" }, { 0xB7, "App 2" },
            { 0xA6, "Browser Back" }, { 0xA7, "Browser Forward" }, { 0xA8, "Browser Refresh" },
            { 0xA9, "Browser Stop" }, { 0xAA, "Browser Search" }, { 0xAB, "Browser Favorites" },
            { 0xAC, "Browser Home" },
        };

        /// <summary>Клавиши, которым при отправке требуется флаг extended.</summary>
        private static readonly HashSet<int> ExtendedVks = new HashSet<int>
        {
            VK_INSERT, VK_DELETE, VK_HOME, VK_END, VK_PRIOR, VK_NEXT,
            VK_LEFT, VK_UP, VK_RIGHT, VK_DOWN,
            VK_RCONTROL, VK_RMENU, VK_LWIN, VK_RWIN, VK_APPS,
            VK_NUMLOCK, VK_SNAPSHOT, 0x6F /* Num / */, 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3
        };

        public static bool NeedsExtendedFlag(int vk) => ExtendedVks.Contains(vk);

        public static string Name(KeyId id) => Name(id.Vk, id.Extended);

        public static string Name(int vk, bool extended)
        {
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            string n;
            if (Names.TryGetValue(vk, out n)) return n;
            return "VK " + vk.ToString("X2");
        }

        /// <summary>Короткая подпись для колпачка клавиши.</summary>
        public static string ShortName(int vk, bool extended)
        {
            switch (vk)
            {
                case VK_LEFT: return "←";
                case VK_UP: return "↑";
                case VK_RIGHT: return "→";
                case VK_DOWN: return "↓";
                case VK_BACK: return "⌫";
                case VK_RETURN: return "↵";
                case VK_TAB: return "↹";
                case VK_LSHIFT:
                case VK_RSHIFT: return "⇧";
                case VK_SPACE: return "";
            }
            return Name(vk, extended);
        }

        public sealed class KeyOption
        {
            public KeyId Id { get; set; }
            public string Name { get; set; }
            public string Group { get; set; }
            public override string ToString() => Name;
        }

        private static List<KeyOption> _all;

        /// <summary>Полный каталог клавиш, доступных как цель переназначения.</summary>
        public static List<KeyOption> AllKeys
        {
            get
            {
                if (_all != null) return _all;
                var list = new List<KeyOption>();
                Action<int, bool, string> add = (vk, ext, group) =>
                    list.Add(new KeyOption { Id = new KeyId(vk, ext), Name = Name(vk, ext), Group = group });

                for (int vk = 0x41; vk <= 0x5A; vk++) add(vk, false, "Буквы");
                for (int vk = 0x30; vk <= 0x39; vk++) add(vk, false, "Цифры");
                for (int vk = 0x70; vk <= 0x7B; vk++) add(vk, false, "F-ряд");
                foreach (var vk in new[] { 0xC0, 0xBD, 0xBB, 0xDB, 0xDD, 0xDC, 0xBA, 0xDE, 0xBC, 0xBE, 0xBF })
                    add(vk, false, "Символы");
                foreach (var vk in new[] { VK_ESCAPE, VK_TAB, VK_CAPITAL, VK_SPACE, VK_RETURN, VK_BACK })
                    add(vk, false, "Управление");
                foreach (var vk in new[] { VK_LSHIFT, VK_RSHIFT, VK_LCONTROL, VK_LMENU })
                    add(vk, false, "Модификаторы");
                foreach (var vk in new[] { VK_RCONTROL, VK_RMENU, VK_LWIN, VK_RWIN, VK_APPS })
                    add(vk, true, "Модификаторы");
                foreach (var vk in new[] { VK_INSERT, VK_DELETE, VK_HOME, VK_END, VK_PRIOR, VK_NEXT,
                                           VK_LEFT, VK_UP, VK_RIGHT, VK_DOWN })
                    add(vk, true, "Навигация");
                foreach (var vk in new[] { VK_SNAPSHOT })
                    add(vk, true, "Навигация");
                add(VK_SCROLL, false, "Навигация");
                add(VK_PAUSE, false, "Навигация");
                for (int vk = 0x60; vk <= 0x69; vk++) add(vk, false, "Нумпад");
                foreach (var vk in new[] { 0x6A, 0x6B, 0x6D, 0x6E }) add(vk, false, "Нумпад");
                add(0x6F, true, "Нумпад");
                add(VK_NUMLOCK, true, "Нумпад");
                foreach (var vk in new[] { 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3 })
                    add(vk, true, "Медиа");
                foreach (var vk in new[] { 0xA6, 0xA7, 0xA8, 0xAA, 0xAC, 0xB4, 0xB5 })
                    add(vk, true, "Приложения");

                _all = list;
                return _all;
            }
        }

        public static IEnumerable<IGrouping<string, KeyOption>> Grouped() => AllKeys.GroupBy(k => k.Group);
    }
}
