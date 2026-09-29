using System;
using System.Collections.Generic;
using Kortex.Hid;

namespace Kortex.Devices
{
    public enum FormFactor
    {
        Unknown = 0,
        Compact60,
        Compact65,
        Compact75,
        Tkl,
        FullSize
    }

    /// <summary>Что Kortex знает о конкретной модели.</summary>
    public sealed class BoardProfile
    {
        public string Model { get; set; }
        public string Vendor { get; set; }
        public FormFactor Form { get; set; }
        public int MatrixRows { get; set; }
        public int MatrixCols { get; set; }
        /// <summary>Плата семейства, для которого Kortex заменяет VIA/Vial целиком.</summary>
        public bool FirstClassSupport { get; set; }
    }

    /// <summary>
    /// Определение модели. Основной источник — то, что плата сообщает о себе по USB;
    /// каталог лишь уточняет форм-фактор и размер матрицы для известных семейств.
    /// </summary>
    public static class DeviceCatalog
    {
        private static readonly Dictionary<ushort, string> Vendors = new Dictionary<ushort, string>
        {
            { 0x4B42, "KBDfans" },
            { 0xFEED, "QMK" },
            { 0x3434, "Keychron" },
            { 0x320F, "Evision / Akko" },
            { 0x046D, "Logitech" },
            { 0x045E, "Microsoft" },
            { 0x1532, "Razer" },
            { 0x1B1C, "Corsair" },
            { 0x1038, "SteelSeries" },
            { 0x05AC, "Apple" },
            { 0x04D9, "Holtek" },
            { 0x0C45, "SONiX" },
            { 0x413C, "Dell" },
            { 0x03F0, "HP" },
        };

        public static string VendorName(ushort vid, string reported)
        {
            if (!string.IsNullOrWhiteSpace(reported)) return reported;
            string name;
            if (Vendors.TryGetValue(vid, out name)) return name;
            return "VID " + vid.ToString("X4");
        }

        /// <summary>Определяет модель по данным USB-дескриптора.</summary>
        public static BoardProfile Resolve(ushort vid, ushort pid, string product, string manufacturer)
        {
            string name = (product ?? string.Empty).Trim();
            string upper = name.ToUpperInvariant();

            var profile = new BoardProfile
            {
                Model = name.Length > 0 ? name : "PID " + pid.ToString("X4"),
                Vendor = VendorName(vid, manufacturer),
                Form = FormFactor.Unknown
            };

            // Семейство KBD8X — то, ради чего Kortex задуман как замена VIA/Vial
            bool kbdfans = vid == 0x4B42 || upper.Contains("KBDFANS") || upper.Contains("KBD8X") ||
                           upper.Contains("KBD67") || upper.Contains("KBD75") || upper.Contains("TOFU");
            if (upper.Contains("KBD8X"))
            {
                profile.Form = FormFactor.Tkl;
                profile.FirstClassSupport = true;
                if (profile.Vendor == "VID " + vid.ToString("X4")) profile.Vendor = "KBDfans";
                if (name.Length == 0) profile.Model = "KBD8X MKIII";
            }
            else if (kbdfans)
            {
                profile.FirstClassSupport = true;
                if (profile.Vendor.StartsWith("VID ")) profile.Vendor = "KBDfans";
            }

            if (profile.Form == FormFactor.Unknown) profile.Form = GuessForm(upper);
            ApplyMatrixDefaults(profile);
            return profile;
        }

        private static FormFactor GuessForm(string upper)
        {
            if (upper.Contains("TKL") || upper.Contains("87") || upper.Contains("8X")) return FormFactor.Tkl;
            if (upper.Contains("FULL") || upper.Contains("104") || upper.Contains("108") ||
                upper.Contains("1800") || upper.Contains("96")) return FormFactor.FullSize;
            if (upper.Contains("75")) return FormFactor.Compact75;
            if (upper.Contains("65") || upper.Contains("67")) return FormFactor.Compact65;
            if (upper.Contains("60") || upper.Contains("61") || upper.Contains("64")) return FormFactor.Compact60;
            return FormFactor.Unknown;
        }

        private static void ApplyMatrixDefaults(BoardProfile profile)
        {
            switch (profile.Form)
            {
                case FormFactor.Compact60: profile.MatrixRows = 5; profile.MatrixCols = 14; break;
                case FormFactor.Compact65: profile.MatrixRows = 5; profile.MatrixCols = 15; break;
                case FormFactor.Compact75: profile.MatrixRows = 6; profile.MatrixCols = 15; break;
                case FormFactor.Tkl: profile.MatrixRows = 6; profile.MatrixCols = 17; break;
                case FormFactor.FullSize: profile.MatrixRows = 6; profile.MatrixCols = 21; break;
                default: profile.MatrixRows = 6; profile.MatrixCols = 17; break;
            }
        }

        public static string FormName(FormFactor form, bool english)
        {
            switch (form)
            {
                case FormFactor.Compact60: return "60%";
                case FormFactor.Compact65: return "65%";
                case FormFactor.Compact75: return "75%";
                case FormFactor.Tkl: return english ? "TKL (87 keys)" : "TKL (87 клавиш)";
                case FormFactor.FullSize: return english ? "Full size" : "Полноразмерная";
                default: return english ? "Not determined" : "Не определён";
            }
        }

        /// <summary>Похоже ли это устройство на клавиатуру (а не мышь, хаб и т.п.).</summary>
        public static bool LooksLikeKeyboard(IEnumerable<HidInterfaceInfo> interfaces)
        {
            foreach (var i in interfaces)
                if (i.IsKeyboard || i.IsRawHid) return true;
            return false;
        }
    }
}
