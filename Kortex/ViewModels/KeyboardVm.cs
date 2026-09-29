using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Kortex.Input;
using Kortex.Models;

namespace Kortex.ViewModels
{
    /// <summary>Один колпачок на схеме.</summary>
    public sealed class KeyCapVm : ObservableObject
    {
        public const double Unit = 46;
        public const double Gap = 5;

        public KeyDef Def { get; }

        public KeyCapVm(KeyDef def)
        {
            Def = def;
            Left = def.X * Unit;
            Top = def.Y * Unit;
            Width = def.W * Unit - Gap;
            Height = def.H * Unit - Gap;
        }

        public double Left { get; }
        public double Top { get; }
        public double Width { get; }
        public double Height { get; }

        public string Label => Def.Label;
        public string Sub => Def.Sub;

        private string _override;
        /// <summary>Подпись поверх заводской: значение клавиши в прошивке.</summary>
        public string OverrideLabel
        {
            get => _override;
            set
            {
                if (!Set(ref _override, value)) return;
                Raise(nameof(DisplayLabel));
                Raise(nameof(ShowSub));
            }
        }

        public string DisplayLabel => string.IsNullOrEmpty(_override) ? Def.Label : _override;
        public bool ShowSub => string.IsNullOrEmpty(_override) && !string.IsNullOrEmpty(Def.Sub);

        private bool _isDimmed;
        /// <summary>Клавиша не сопоставлена с матрицей платы.</summary>
        public bool IsDimmed { get => _isDimmed; set => Set(ref _isDimmed, value); }

        private bool _pulse = true;
        /// <summary>Показывать волну при нажатии (фирменная анимация Kortex Pulse).</summary>
        public bool PulseEnabled { get => _pulse; set => Set(ref _pulse, value); }

        private double _heat;
        /// <summary>Доля нажатий относительно самой активной клавиши: 0..1.</summary>
        public double Heat { get => _heat; set => Set(ref _heat, value); }
        public bool IsBlocker => Def.IsBlocker;
        public bool IsFirmware => Def.IsFirmware;
        public bool IsActive => !Def.IsBlocker && !Def.IsFirmware;
        public KeyId Id => Def.Id;

        private bool _isDown;
        public bool IsDown { get => _isDown; set => Set(ref _isDown, value); }

        private bool _isTested;
        public bool IsTested { get => _isTested; set => Set(ref _isTested, value); }

        private bool _hasChatter;
        public bool HasChatter { get => _hasChatter; set => Set(ref _hasChatter, value); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

        private int _pressCount;
        public int PressCount
        {
            get => _pressCount;
            set { if (Set(ref _pressCount, value)) Raise(nameof(PressCountText)); }
        }
        public string PressCountText => _pressCount > 0 ? _pressCount.ToString() : string.Empty;

        private double _lastHoldMs;
        public double LastHoldMs { get => _lastHoldMs; set => Set(ref _lastHoldMs, value); }

        private string _mapping;
        /// <summary>Подпись переназначения (то, что реально отправится в систему).</summary>
        public string Mapping
        {
            get => _mapping;
            set { if (Set(ref _mapping, value)) Raise(nameof(IsMapped)); }
        }
        public bool IsMapped => !string.IsNullOrEmpty(_mapping);

        public string Tooltip
        {
            get
            {
                if (IsBlocker) return "Заглушка стэпа — не клавиша";
                if (IsFirmware) return "Fn — обрабатывается прошивкой платы и не доходит до Windows";
                return Id.Name + "   ·   VK 0x" + Id.Vk.ToString("X2") + (Id.Extended ? "  (extended)" : "");
            }
        }

        public void ResetTest()
        {
            IsDown = false;
            IsTested = false;
            HasChatter = false;
            PressCount = 0;
            LastHoldMs = 0;
            Heat = 0;
        }
    }

    /// <summary>Схема платы: коллекция колпачков + быстрый поиск по коду клавиши.</summary>
    public sealed class KeyboardVm : ObservableObject
    {
        private readonly Dictionary<KeyId, List<KeyCapVm>> _byId = new Dictionary<KeyId, List<KeyCapVm>>();

        public ObservableCollection<KeyCapVm> Keys { get; } = new ObservableCollection<KeyCapVm>();

        public double BoardWidth => KeyboardLayout.TotalWidth * KeyCapVm.Unit;
        public double BoardHeight => KeyboardLayout.TotalHeight * KeyCapVm.Unit;

        public int TestableCount { get; private set; }

        public event Action Rebuilt;

        public void Rebuild(LayoutOptions options)
        {
            Keys.Clear();
            _byId.Clear();
            TestableCount = 0;

            foreach (var def in KeyboardLayout.Build(options))
            {
                var vm = new KeyCapVm(def) { PulseEnabled = _pulse };
                Keys.Add(vm);
                if (!vm.IsActive) continue;
                TestableCount++;

                List<KeyCapVm> bucket;
                if (!_byId.TryGetValue(vm.Id, out bucket))
                {
                    bucket = new List<KeyCapVm>();
                    _byId[vm.Id] = bucket;
                }
                bucket.Add(vm);
            }

            Raise(nameof(TestableCount));
            var h = Rebuilt;
            if (h != null) h();
        }

        /// <summary>Найти колпачки по коду (учитывая, что extended может не совпасть).</summary>
        public List<KeyCapVm> Find(KeyId id)
        {
            List<KeyCapVm> list;
            if (_byId.TryGetValue(id, out list)) return list;
            if (_byId.TryGetValue(new KeyId(id.Vk, !id.Extended), out list)) return list;
            return null;
        }

        public void ResetAll()
        {
            foreach (var k in Keys) k.ResetTest();
        }

        private bool _pulse = true;
        /// <summary>Единый переключатель волны нажатий для всей схемы.</summary>
        public bool PulseEnabled
        {
            get => _pulse;
            set
            {
                if (!Set(ref _pulse, value)) return;
                foreach (var k in Keys) k.PulseEnabled = value;
            }
        }
    }
}
