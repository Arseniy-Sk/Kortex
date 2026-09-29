using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;

namespace Kortex.Localization
{
    public enum AppLanguage
    {
        Russian = 0,
        English = 1
    }

    /// <summary>
    /// Словарь интерфейса. Строки берутся через индексатор, поэтому смена языка
    /// обновляет уже отрисованный интерфейс без перезапуска.
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        public static Loc I { get; } = new Loc();

        private AppLanguage _language = AppLanguage.Russian;

        public event PropertyChangedEventHandler PropertyChanged;

        public static event Action LanguageChanged;

        public AppLanguage Language
        {
            get => _language;
            set
            {
                if (_language == value) return;
                _language = value;
                var h = PropertyChanged;
                if (h != null)
                {
                    h(this, new PropertyChangedEventArgs("Item[]"));
                    h(this, new PropertyChangedEventArgs(nameof(Language)));
                    h(this, new PropertyChangedEventArgs(nameof(IsRussian)));
                    h(this, new PropertyChangedEventArgs(nameof(IsEnglish)));
                }
                var l = LanguageChanged;
                if (l != null) l();
            }
        }

        public bool IsRussian => _language == AppLanguage.Russian;
        public bool IsEnglish => _language == AppLanguage.English;

        public string this[string key]
        {
            get
            {
                string[] pair;
                if (!Strings.Table.TryGetValue(key, out pair)) return key;
                return pair[(int)_language] ?? pair[0];
            }
        }

        /// <summary>Статический доступ для кода моделей представления.</summary>
        public static string T(string key) => I[key];

        /// <summary>Строка с подстановкой: T("Key", 3) → "Правил: 3".</summary>
        public static string T(string key, params object[] args)
        {
            string format = I[key];
            try { return string.Format(format, args); }
            catch { return format; }
        }
    }

    /// <summary>Разметка: Text="{loc:Tr Nav.Tester}" — обновляется при смене языка.</summary>
    public sealed class TrExtension : MarkupExtension
    {
        public string Key { get; set; }

        public TrExtension() { }
        public TrExtension(string key) { Key = key; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding("[" + Key + "]")
            {
                Source = Loc.I,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
