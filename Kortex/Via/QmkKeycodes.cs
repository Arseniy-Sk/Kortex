using System;
using System.Collections.Generic;
using System.Linq;
using Kortex.Input;

namespace Kortex.Via
{
    public sealed class KeycodeInfo
    {
        public ushort Code { get; set; }
        /// <summary>Имя в терминах QMK, например KC_ESCAPE.</summary>
        public string Name { get; set; }
        /// <summary>Короткая подпись для колпачка.</summary>
        public string Label { get; set; }
        public string Category { get; set; }
        public override string ToString() => Label + "   " + Name;
    }

    /// <summary>
    /// Коды клавиш QMK/VIA: базовый набор, квантовые диапазоны (слои, модификаторы, макросы)
    /// и перевод в виртуальные коды Windows для сопоставления с физической схемой.
    /// </summary>
    public static class QmkKeycodes
    {
        public const ushort KC_NO = 0x0000;
        public const ushort KC_TRANSPARENT = 0x0001;

        // Квантовые диапазоны (нумерация VIA-совместимых прошивок QMK)
        public const ushort QK_MODS = 0x0100;
        public const ushort QK_MACRO = 0x3000;
        public const ushort QK_LAYER_TAP = 0x4000;
        public const ushort QK_TO = 0x5000;
        public const ushort QK_MOMENTARY = 0x5100;
        public const ushort QK_DEF_LAYER = 0x5200;
        public const ushort QK_TOGGLE_LAYER = 0x5300;
        public const ushort QK_ONESHOT_LAYER = 0x5400;

        public const ushort MOD_LCTL = 0x0100, MOD_LSFT = 0x0200, MOD_LALT = 0x0400, MOD_LGUI = 0x0800;
        public const ushort MOD_RCTL = 0x1100, MOD_RSFT = 0x1200, MOD_RALT = 0x1400, MOD_RGUI = 0x1800;

        private sealed class Entry
        {
            public string Name;
            public string Label;
            public string Category;
            public int Vk;
            public bool Ext;
        }

        private static readonly Dictionary<ushort, Entry> Basic = new Dictionary<ushort, Entry>();
        private static readonly Dictionary<long, ushort> ByVk = new Dictionary<long, ushort>();
        private static List<KeycodeInfo> _catalog;

        static QmkKeycodes()
        {
            Add(0x0000, "KC_NO", "—", "Служебные", 0, false);
            Add(0x0001, "KC_TRANSPARENT", "▽", "Служебные", 0, false);

            // Буквы
            for (int i = 0; i < 26; i++)
            {
                char c = (char)('A' + i);
                Add((ushort)(0x04 + i), "KC_" + c, c.ToString(), "Буквы", 0x41 + i, false);
            }
            // Цифры: 1..9, 0
            string[] digits = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
            int[] digitVk = { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30 };
            for (int i = 0; i < 10; i++)
                Add((ushort)(0x1E + i), "KC_" + digits[i], digits[i], "Цифры", digitVk[i], false);

            Add(0x28, "KC_ENTER", "Enter", "Управление", KeyCodes.VK_RETURN, false);
            Add(0x29, "KC_ESCAPE", "Esc", "Управление", KeyCodes.VK_ESCAPE, false);
            Add(0x2A, "KC_BACKSPACE", "Bksp", "Управление", KeyCodes.VK_BACK, false);
            Add(0x2B, "KC_TAB", "Tab", "Управление", KeyCodes.VK_TAB, false);
            Add(0x2C, "KC_SPACE", "Space", "Управление", KeyCodes.VK_SPACE, false);
            Add(0x2D, "KC_MINUS", "-", "Символы", 0xBD, false);
            Add(0x2E, "KC_EQUAL", "=", "Символы", 0xBB, false);
            Add(0x2F, "KC_LEFT_BRACKET", "[", "Символы", 0xDB, false);
            Add(0x30, "KC_RIGHT_BRACKET", "]", "Символы", 0xDD, false);
            Add(0x31, "KC_BACKSLASH", "\\", "Символы", 0xDC, false);
            Add(0x32, "KC_NONUS_HASH", "#", "Символы", 0xDC, false);
            Add(0x33, "KC_SEMICOLON", ";", "Символы", 0xBA, false);
            Add(0x34, "KC_QUOTE", "'", "Символы", 0xDE, false);
            Add(0x35, "KC_GRAVE", "`", "Символы", 0xC0, false);
            Add(0x36, "KC_COMMA", ",", "Символы", 0xBC, false);
            Add(0x37, "KC_DOT", ".", "Символы", 0xBE, false);
            Add(0x38, "KC_SLASH", "/", "Символы", 0xBF, false);
            Add(0x39, "KC_CAPS_LOCK", "Caps", "Управление", KeyCodes.VK_CAPITAL, false);

            for (int i = 0; i < 12; i++)
                Add((ushort)(0x3A + i), "KC_F" + (i + 1), "F" + (i + 1), "F-ряд", 0x70 + i, false);

            Add(0x46, "KC_PRINT_SCREEN", "PrtSc", "Навигация", KeyCodes.VK_SNAPSHOT, true);
            Add(0x47, "KC_SCROLL_LOCK", "ScrLk", "Навигация", KeyCodes.VK_SCROLL, false);
            Add(0x48, "KC_PAUSE", "Pause", "Навигация", KeyCodes.VK_PAUSE, false);
            Add(0x49, "KC_INSERT", "Ins", "Навигация", KeyCodes.VK_INSERT, true);
            Add(0x4A, "KC_HOME", "Home", "Навигация", KeyCodes.VK_HOME, true);
            Add(0x4B, "KC_PAGE_UP", "PgUp", "Навигация", KeyCodes.VK_PRIOR, true);
            Add(0x4C, "KC_DELETE", "Del", "Навигация", KeyCodes.VK_DELETE, true);
            Add(0x4D, "KC_END", "End", "Навигация", KeyCodes.VK_END, true);
            Add(0x4E, "KC_PAGE_DOWN", "PgDn", "Навигация", KeyCodes.VK_NEXT, true);
            Add(0x4F, "KC_RIGHT", "→", "Навигация", KeyCodes.VK_RIGHT, true);
            Add(0x50, "KC_LEFT", "←", "Навигация", KeyCodes.VK_LEFT, true);
            Add(0x51, "KC_DOWN", "↓", "Навигация", KeyCodes.VK_DOWN, true);
            Add(0x52, "KC_UP", "↑", "Навигация", KeyCodes.VK_UP, true);

            Add(0x53, "KC_NUM_LOCK", "Num", "Нумпад", KeyCodes.VK_NUMLOCK, true);
            Add(0x54, "KC_KP_SLASH", "Num /", "Нумпад", 0x6F, true);
            Add(0x55, "KC_KP_ASTERISK", "Num *", "Нумпад", 0x6A, false);
            Add(0x56, "KC_KP_MINUS", "Num -", "Нумпад", 0x6D, false);
            Add(0x57, "KC_KP_PLUS", "Num +", "Нумпад", 0x6B, false);
            Add(0x58, "KC_KP_ENTER", "Num Ent", "Нумпад", KeyCodes.VK_RETURN, true);
            for (int i = 0; i < 9; i++)
                Add((ushort)(0x59 + i), "KC_KP_" + (i + 1), "Num " + (i + 1), "Нумпад", 0x61 + i, false);
            Add(0x62, "KC_KP_0", "Num 0", "Нумпад", 0x60, false);
            Add(0x63, "KC_KP_DOT", "Num .", "Нумпад", 0x6E, false);

            Add(0x64, "KC_NONUS_BACKSLASH", "\\ ISO", "Символы", 0xE2, false);
            Add(0x65, "KC_APPLICATION", "Menu", "Управление", KeyCodes.VK_APPS, true);
            for (int i = 0; i < 12; i++)
                Add((ushort)(0x68 + i), "KC_F" + (i + 13), "F" + (i + 13), "F-ряд", 0, false);

            // Модификаторы
            Add(0xE0, "KC_LEFT_CTRL", "Ctrl", "Модификаторы", KeyCodes.VK_LCONTROL, false);
            Add(0xE1, "KC_LEFT_SHIFT", "Shift", "Модификаторы", KeyCodes.VK_LSHIFT, false);
            Add(0xE2, "KC_LEFT_ALT", "Alt", "Модификаторы", KeyCodes.VK_LMENU, false);
            Add(0xE3, "KC_LEFT_GUI", "Win", "Модификаторы", KeyCodes.VK_LWIN, true);
            Add(0xE4, "KC_RIGHT_CTRL", "Ctrl R", "Модификаторы", KeyCodes.VK_RCONTROL, true);
            Add(0xE5, "KC_RIGHT_SHIFT", "Shift R", "Модификаторы", KeyCodes.VK_RSHIFT, false);
            Add(0xE6, "KC_RIGHT_ALT", "Alt R", "Модификаторы", KeyCodes.VK_RMENU, true);
            Add(0xE7, "KC_RIGHT_GUI", "Win R", "Модификаторы", KeyCodes.VK_RWIN, true);

            // Мультимедиа и система
            Add(0xA5, "KC_SYSTEM_POWER", "Power", "Система", 0, false);
            Add(0xA6, "KC_SYSTEM_SLEEP", "Sleep", "Система", 0, false);
            Add(0xA7, "KC_SYSTEM_WAKE", "Wake", "Система", 0, false);
            Add(0xA8, "KC_AUDIO_MUTE", "Mute", "Медиа", 0xAD, true);
            Add(0xA9, "KC_AUDIO_VOL_UP", "Vol +", "Медиа", 0xAF, true);
            Add(0xAA, "KC_AUDIO_VOL_DOWN", "Vol -", "Медиа", 0xAE, true);
            Add(0xAB, "KC_MEDIA_NEXT_TRACK", "Next", "Медиа", 0xB0, true);
            Add(0xAC, "KC_MEDIA_PREV_TRACK", "Prev", "Медиа", 0xB1, true);
            Add(0xAD, "KC_MEDIA_STOP", "Stop", "Медиа", 0xB2, true);
            Add(0xAE, "KC_MEDIA_PLAY_PAUSE", "Play", "Медиа", 0xB3, true);
            Add(0xAF, "KC_MEDIA_SELECT", "Media", "Медиа", 0xB5, true);
            Add(0xB0, "KC_MAIL", "Mail", "Система", 0xB4, true);
            Add(0xB1, "KC_CALCULATOR", "Calc", "Система", 0, false);
            Add(0xB2, "KC_MY_COMPUTER", "PC", "Система", 0, false);
            Add(0xB3, "KC_WWW_SEARCH", "Search", "Система", 0xAA, true);
            Add(0xB4, "KC_WWW_HOME", "Home www", "Система", 0xAC, true);
            Add(0xB5, "KC_WWW_BACK", "Back", "Система", 0xA6, true);
            Add(0xB6, "KC_WWW_FORWARD", "Fwd", "Система", 0xA7, true);
        }

        private static void Add(ushort code, string name, string label, string category, int vk, bool ext)
        {
            Basic[code] = new Entry { Name = name, Label = label, Category = category, Vk = vk, Ext = ext };
            if (vk != 0)
            {
                long key = VkKey(vk, ext);
                if (!ByVk.ContainsKey(key)) ByVk[key] = code;
            }
        }

        private static long VkKey(int vk, bool ext) => ((long)vk << 1) | (ext ? 1 : 0);

        /// <summary>Код QMK по виртуальной клавише Windows.</summary>
        public static ushort FromVirtualKey(KeyId id)
        {
            ushort code;
            if (ByVk.TryGetValue(VkKey(id.Vk, id.Extended), out code)) return code;
            if (ByVk.TryGetValue(VkKey(id.Vk, !id.Extended), out code)) return code;
            return KC_NO;
        }

        /// <summary>Короткая подпись для колпачка.</summary>
        public static string Label(ushort code)
        {
            Entry e;
            if (Basic.TryGetValue(code, out e)) return e.Label;

            int layer;
            if (TryLayerCode(code, out layer, QK_MOMENTARY)) return "MO" + layer;
            if (TryLayerCode(code, out layer, QK_TOGGLE_LAYER)) return "TG" + layer;
            if (TryLayerCode(code, out layer, QK_DEF_LAYER)) return "DF" + layer;
            if (TryLayerCode(code, out layer, QK_ONESHOT_LAYER)) return "OSL" + layer;
            if ((code & 0xFFF0) == 0x5010) return "TO" + (code & 0x0F);
            if ((code & 0xF000) == QK_LAYER_TAP)
            {
                int lt = (code >> 8) & 0x0F;
                ushort inner = (ushort)(code & 0xFF);
                return "LT" + lt + " " + Label(inner);
            }
            if ((code & 0xFF00) == QK_MACRO && (code & 0xFF) < 64) return "M" + (code & 0xFF);
            if (code >= QK_MODS && code < 0x2000)
            {
                ushort inner = (ushort)(code & 0xFF);
                return ModPrefix(code) + Label(inner);
            }
            return "0x" + code.ToString("X4");
        }

        /// <summary>Полное имя в терминах QMK.</summary>
        public static string Name(ushort code)
        {
            Entry e;
            if (Basic.TryGetValue(code, out e)) return e.Name;

            int layer;
            if (TryLayerCode(code, out layer, QK_MOMENTARY)) return "MO(" + layer + ")";
            if (TryLayerCode(code, out layer, QK_TOGGLE_LAYER)) return "TG(" + layer + ")";
            if (TryLayerCode(code, out layer, QK_DEF_LAYER)) return "DF(" + layer + ")";
            if (TryLayerCode(code, out layer, QK_ONESHOT_LAYER)) return "OSL(" + layer + ")";
            if ((code & 0xFFF0) == 0x5010) return "TO(" + (code & 0x0F) + ")";
            if ((code & 0xF000) == QK_LAYER_TAP)
                return "LT(" + ((code >> 8) & 0x0F) + ", " + Name((ushort)(code & 0xFF)) + ")";
            if ((code & 0xFF00) == QK_MACRO && (code & 0xFF) < 64) return "MACRO(" + (code & 0xFF) + ")";
            if (code >= QK_MODS && code < 0x2000) return ModPrefix(code) + Name((ushort)(code & 0xFF));
            return "0x" + code.ToString("X4");
        }

        private static bool TryLayerCode(ushort code, out int layer, ushort baseCode)
        {
            layer = 0;
            if ((code & 0xFF00) != baseCode) return false;
            int low = code & 0xFF;
            if (low > 15) return false;
            layer = low;
            return true;
        }

        private static string ModPrefix(ushort code)
        {
            int mods = (code >> 8) & 0x1F;
            bool right = (mods & 0x10) != 0;
            string prefix = string.Empty;
            if ((mods & 0x01) != 0) prefix += right ? "RCtl+" : "Ctl+";
            if ((mods & 0x02) != 0) prefix += right ? "RSft+" : "Sft+";
            if ((mods & 0x04) != 0) prefix += right ? "RAlt+" : "Alt+";
            if ((mods & 0x08) != 0) prefix += right ? "RGui+" : "Gui+";
            return prefix;
        }

        public static bool IsKnown(ushort code) => Basic.ContainsKey(code);

        private static Dictionary<string, ushort> _byName;

        /// <summary>Разбирает имя кода QMK: KC_A, MO(1), LT(2, KC_SPACE), MACRO(0), 0x1234.</summary>
        public static ushort Parse(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return KC_NO;
            name = name.Trim();

            if (_byName == null)
            {
                _byName = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in Basic)
                    if (!_byName.ContainsKey(pair.Value.Name)) _byName[pair.Value.Name] = pair.Key;
            }

            ushort direct;
            if (_byName.TryGetValue(name, out direct)) return direct;

            if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                ushort raw;
                if (ushort.TryParse(name.Substring(2), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out raw)) return raw;
                return KC_NO;
            }

            int open = name.IndexOf('(');
            int close = name.LastIndexOf(')');
            if (open <= 0 || close <= open) return KC_NO;

            string head = name.Substring(0, open).Trim().ToUpperInvariant();
            string body = name.Substring(open + 1, close - open - 1).Trim();

            if (head == "LT")
            {
                int comma = body.IndexOf(',');
                if (comma <= 0) return KC_NO;
                int layer;
                if (!int.TryParse(body.Substring(0, comma).Trim(), out layer)) return KC_NO;
                ushort inner = Parse(body.Substring(comma + 1));
                return (ushort)(QK_LAYER_TAP | ((layer & 0x0F) << 8) | (inner & 0xFF));
            }

            int value;
            if (!int.TryParse(body, out value))
            {
                // Модификаторы вида LCTL(KC_A)
                ushort innerCode = Parse(body);
                switch (head)
                {
                    case "LCTL": return (ushort)(MOD_LCTL | innerCode);
                    case "LSFT": return (ushort)(MOD_LSFT | innerCode);
                    case "LALT": return (ushort)(MOD_LALT | innerCode);
                    case "LGUI": return (ushort)(MOD_LGUI | innerCode);
                    case "RCTL": return (ushort)(MOD_RCTL | innerCode);
                    case "RSFT": return (ushort)(MOD_RSFT | innerCode);
                    case "RALT": return (ushort)(MOD_RALT | innerCode);
                    case "RGUI": return (ushort)(MOD_RGUI | innerCode);
                }
                return KC_NO;
            }

            switch (head)
            {
                case "MO": return (ushort)(QK_MOMENTARY | (value & 0xFF));
                case "TO": return (ushort)(0x5010 | (value & 0x0F));
                case "TG": return (ushort)(QK_TOGGLE_LAYER | (value & 0xFF));
                case "DF": return (ushort)(QK_DEF_LAYER | (value & 0xFF));
                case "OSL": return (ushort)(QK_ONESHOT_LAYER | (value & 0xFF));
                case "MACRO": return (ushort)(QK_MACRO | (value & 0xFF));
            }
            return KC_NO;
        }

        /// <summary>Обратное преобразование: код QMK в виртуальную клавишу Windows.</summary>
        public static int ToVirtualKey(ushort code)
        {
            Entry e;
            return Basic.TryGetValue(code, out e) ? e.Vk : 0;
        }

        /// <summary>Нужен ли флаг extended при отправке этого кода в Windows.</summary>
        public static bool IsExtended(ushort code)
        {
            Entry e;
            return Basic.TryGetValue(code, out e) && e.Ext;
        }

        /// <summary>Каталог для выпадающего списка редактора раскладки.</summary>
        public static List<KeycodeInfo> Catalog(int layerCount, int macroCount)
        {
            if (_catalog != null && _catalogLayers == layerCount && _catalogMacros == macroCount) return _catalog;

            var list = new List<KeycodeInfo>();
            foreach (var kv in Basic.OrderBy(k => k.Value.Category).ThenBy(k => k.Key))
                list.Add(new KeycodeInfo
                {
                    Code = kv.Key,
                    Name = kv.Value.Name,
                    Label = kv.Value.Label,
                    Category = kv.Value.Category
                });

            for (int layer = 0; layer < Math.Max(1, layerCount); layer++)
            {
                list.Add(Quantum((ushort)(QK_MOMENTARY | layer), "MO(" + layer + ")", "MO" + layer, "Слои"));
                list.Add(Quantum((ushort)(0x5010 | layer), "TO(" + layer + ")", "TO" + layer, "Слои"));
                list.Add(Quantum((ushort)(QK_TOGGLE_LAYER | layer), "TG(" + layer + ")", "TG" + layer, "Слои"));
                list.Add(Quantum((ushort)(QK_DEF_LAYER | layer), "DF(" + layer + ")", "DF" + layer, "Слои"));
                list.Add(Quantum((ushort)(QK_ONESHOT_LAYER | layer), "OSL(" + layer + ")", "OSL" + layer, "Слои"));
            }

            for (int m = 0; m < Math.Max(0, macroCount); m++)
                list.Add(Quantum((ushort)(QK_MACRO | m), "MACRO(" + m + ")", "M" + m, "Макросы"));

            foreach (var mod in new[]
            {
                new object[] { MOD_LCTL, "LCTL" }, new object[] { MOD_LSFT, "LSFT" },
                new object[] { MOD_LALT, "LALT" }, new object[] { MOD_LGUI, "LGUI" }
            })
            {
                ushort baseMod = (ushort)(int)(ushort)mod[0];
                string name = (string)mod[1];
                foreach (ushort inner in new ushort[] { 0x04, 0x06, 0x16, 0x1D, 0x29, 0x2B, 0x4C })
                    list.Add(Quantum((ushort)(baseMod | inner), name + "(" + Name(inner) + ")",
                        ModPrefix((ushort)(baseMod | inner)) + Label(inner), "Сочетания"));
            }

            _catalog = list;
            _catalogLayers = layerCount;
            _catalogMacros = macroCount;
            return list;
        }

        private static int _catalogLayers = -1;
        private static int _catalogMacros = -1;

        private static KeycodeInfo Quantum(ushort code, string name, string label, string category) =>
            new KeycodeInfo { Code = code, Name = name, Label = label, Category = category };
    }
}
