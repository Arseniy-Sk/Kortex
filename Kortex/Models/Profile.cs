using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Kortex.Input;

namespace Kortex.Models
{
    public enum RemapTargetKind
    {
        Key = 0,
        Macro = 1,
        Disabled = 2,
        Text = 3
    }

    /// <summary>Правило переназначения одной физической клавиши.</summary>
    public sealed class RemapRule
    {
        /// <summary>Исходная клавиша в формате KeyId ("65" / "37e").</summary>
        public string From { get; set; }
        public RemapTargetKind Kind { get; set; }
        public string To { get; set; }
        public string MacroId { get; set; }
        public string Text { get; set; }

        [JsonIgnore] public KeyId FromKey => KeyId.Parse(From);
        [JsonIgnore] public KeyId ToKey => KeyId.Parse(To);
    }

    public enum MacroStepKind
    {
        Tap = 0,
        Down = 1,
        Up = 2,
        Delay = 3,
        Text = 4,
        /// <summary>Запуск программы, файла, папки или ссылки.</summary>
        Launch = 5,
        /// <summary>Щелчок мышью.</summary>
        MouseClick = 6,
        /// <summary>Перемещение курсора.</summary>
        MouseMove = 7,
        /// <summary>Прокрутка колесом.</summary>
        Scroll = 8,
        /// <summary>Положить текст в буфер обмена.</summary>
        Clipboard = 9,
        /// <summary>Вставить текущую дату или время.</summary>
        Stamp = 10
    }

    public enum MouseButton
    {
        Left = 0,
        Right = 1,
        Middle = 2
    }

    public sealed class MacroStep : System.ComponentModel.INotifyPropertyChanged
    {
        private MacroStepKind _kind;
        private string _key;
        private int _delayMs;
        private string _text;
        private string _arg;
        private int _amount;
        private int _x;
        private int _y;

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            var h = PropertyChanged;
            if (h == null) return;
            h(this, new System.ComponentModel.PropertyChangedEventArgs(name));
            h(this, new System.ComponentModel.PropertyChangedEventArgs("Display"));
            h(this, new System.ComponentModel.PropertyChangedEventArgs("Glyph"));
        }

        public MacroStepKind Kind { get => _kind; set { _kind = value; Raise("Kind"); } }
        public string Key { get => _key; set { _key = value; Raise("Key"); } }
        public int DelayMs { get => _delayMs; set { _delayMs = value; Raise("DelayMs"); } }
        /// <summary>Текст, путь к программе, содержимое буфера или формат даты.</summary>
        public string Text { get => _text; set { _text = value; Raise("Text"); } }
        /// <summary>Аргументы запуска или режим перемещения курсора.</summary>
        public string Arg { get => _arg; set { _arg = value; Raise("Arg"); } }
        /// <summary>Кнопка мыши, число щелчков или величина прокрутки.</summary>
        public int Amount { get => _amount; set { _amount = value; Raise("Amount"); } }
        public int X { get => _x; set { _x = value; Raise("X"); } }
        public int Y { get => _y; set { _y = value; Raise("Y"); } }

        [JsonIgnore] public KeyId KeyId => Input.KeyId.Parse(Key);

        /// <summary>Перемещать курсор в абсолютные координаты экрана, а не относительно текущих.</summary>
        [JsonIgnore] public bool IsAbsoluteMove => string.Equals(Arg, "abs", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public MouseButton Button => Amount == 1 ? MouseButton.Right
                                   : Amount == 2 ? MouseButton.Middle
                                   : MouseButton.Left;

        /// <summary>Принудительно обновить вычисляемые подписи (например, после смены языка).</summary>
        public void Touch() => Raise("Display");

        public MacroStep Clone() => new MacroStep
        {
            Kind = Kind, Key = Key, DelayMs = DelayMs, Text = Text,
            Arg = Arg, Amount = Amount, X = X, Y = Y
        };

        [JsonIgnore]
        public string Display
        {
            get
            {
                switch (Kind)
                {
                    case MacroStepKind.Tap: return Localization.Loc.T("Macro.Step.Tap") + "  " + KeyId.Name;
                    case MacroStepKind.Down: return Localization.Loc.T("Macro.Step.Down") + "  " + KeyId.Name;
                    case MacroStepKind.Up: return Localization.Loc.T("Macro.Step.Up") + "  " + KeyId.Name;
                    case MacroStepKind.Delay: return Localization.Loc.T("Macro.Step.Delay") + "  " + DelayMs + " " + Localization.Loc.T("Macro.Ms");
                    case MacroStepKind.Text: return Localization.Loc.T("Macro.Step.Text") + "  «" + (Text ?? string.Empty) + "»";
                    case MacroStepKind.Launch:
                        return Localization.Loc.T("Macro.Step.Launch") + "  " + (Text ?? string.Empty) +
                               (string.IsNullOrWhiteSpace(Arg) ? string.Empty : "  " + Arg);
                    case MacroStepKind.MouseClick:
                        return Localization.Loc.T("Macro.Step.Click") + "  " + Localization.Loc.T("Mouse." + Button);
                    case MacroStepKind.MouseMove:
                        return Localization.Loc.T("Macro.Step.Move") + "  " + X + ", " + Y +
                               "  " + Localization.Loc.T(IsAbsoluteMove ? "Mouse.Absolute" : "Mouse.Relative");
                    case MacroStepKind.Scroll:
                        return Localization.Loc.T("Macro.Step.Scroll") + "  " + Amount;
                    case MacroStepKind.Clipboard:
                        return Localization.Loc.T("Macro.Step.Clipboard") + "  «" + (Text ?? string.Empty) + "»";
                    case MacroStepKind.Stamp:
                        return Localization.Loc.T("Macro.Step.Stamp") + "  " + (Text ?? "dd.MM.yyyy");
                }
                return string.Empty;
            }
        }

        [JsonIgnore]
        public string Glyph
        {
            get
            {
                switch (Kind)
                {
                    case MacroStepKind.Tap: return "";
                    case MacroStepKind.Down: return "";
                    case MacroStepKind.Up: return "";
                    case MacroStepKind.Delay: return "";
                    case MacroStepKind.Text: return "";
                    case MacroStepKind.Launch: return "";
                    case MacroStepKind.MouseClick: return "";
                    case MacroStepKind.MouseMove: return "";
                    case MacroStepKind.Scroll: return "";
                    case MacroStepKind.Clipboard: return "";
                    case MacroStepKind.Stamp: return "";
                }
                return "";
            }
        }
    }

    public sealed class Macro
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Новый макрос";
        public List<MacroStep> Steps { get; set; } = new List<MacroStep>();
        public int Repeat { get; set; } = 1;
        /// <summary>Множитель скорости воспроизведения: 1.0 — как записано.</summary>
        public double Speed { get; set; } = 1.0;

        [JsonIgnore]
        public int TotalMs
        {
            get
            {
                int sum = 0;
                foreach (var s in Steps) if (s.Kind == MacroStepKind.Delay) sum += s.DelayMs;
                return sum * Math.Max(1, Repeat);
            }
        }

        public Macro Clone()
        {
            var m = new Macro { Id = Guid.NewGuid().ToString("N"), Name = Name + " (копия)", Repeat = Repeat, Speed = Speed };
            foreach (var s in Steps) m.Steps.Add(s.Clone());
            return m;
        }
    }

    /// <summary>Профиль: раскладка платы + переназначения + макросы.</summary>
    public sealed class Profile
    {
        public string Name { get; set; } = "По умолчанию";
        public LayoutOptions Layout { get; set; } = new LayoutOptions();
        public List<RemapRule> Remaps { get; set; } = new List<RemapRule>();
        public List<Macro> Macros { get; set; } = new List<Macro>();
    }

    /// <summary>Настройки приложения целиком (то, что лежит в %AppData%).</summary>
    public sealed class AppState
    {
        public List<Profile> Profiles { get; set; } = new List<Profile> { new Profile() };
        public int ActiveProfile { get; set; }
        public bool RemapEnabled { get; set; }
        public bool BlockSystemKeys { get; set; } = true;
        public int ChatterThresholdMs { get; set; } = 25;
        public Localization.AppLanguage Language { get; set; } = Localization.AppLanguage.Russian;
        public bool ShowWelcome { get; set; } = true;
        public bool Animations { get; set; } = true;
        public bool Heatmap { get; set; }
        /// <summary>Сопоставление физических клавиш с матрицей платы: ключ устройства → uid клавиши → [row, col].</summary>
        public Dictionary<string, Dictionary<string, int[]>> Matrices { get; set; }
            = new Dictionary<string, Dictionary<string, int[]>>();

        [JsonIgnore]
        public Profile Active
        {
            get
            {
                if (Profiles == null || Profiles.Count == 0) Profiles = new List<Profile> { new Profile() };
                if (ActiveProfile < 0 || ActiveProfile >= Profiles.Count) ActiveProfile = 0;
                return Profiles[ActiveProfile];
            }
        }
    }
}
