using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Kortex.Input;
using Kortex.Localization;
using Kortex.Models;
using Kortex.Services;

namespace Kortex.ViewModels
{
    /// <summary>Строка в списке активных переназначений.</summary>
    public sealed class RemapRowVm
    {
        public RemapRule Rule { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Glyph { get; set; }
        public string Badge { get; set; }
    }

    public sealed class TargetKindOption : ObservableObject
    {
        public RemapTargetKind Kind { get; set; }
        public string NameKey { get; set; }
        public string Name => Loc.T(NameKey);
        public void Refresh() => Raise(nameof(Name));
        public override string ToString() => Name;
    }

    /// <summary>Экран «Переназначение клавиш».</summary>
    public sealed class RemapVm : ObservableObject
    {
        private readonly Action _persist;

        public KeyboardVm Board { get; } = new KeyboardVm();
        public ObservableCollection<RemapRowVm> Rules { get; } = new ObservableCollection<RemapRowVm>();
        public ObservableCollection<KeyCodes.KeyOption> TargetKeys { get; } = new ObservableCollection<KeyCodes.KeyOption>();
        public ObservableCollection<Macro> Macros { get; } = new ObservableCollection<Macro>();

        public List<TargetKindOption> Kinds { get; } = new List<TargetKindOption>
        {
            new TargetKindOption { Kind = RemapTargetKind.Key, NameKey = "Remap.Kind.Key" },
            new TargetKindOption { Kind = RemapTargetKind.Macro, NameKey = "Remap.Kind.Macro" },
            new TargetKindOption { Kind = RemapTargetKind.Text, NameKey = "Remap.Kind.Text" },
            new TargetKindOption { Kind = RemapTargetKind.Disabled, NameKey = "Remap.Kind.Off" },
        };

        public RelayCommand ApplyCommand { get; }
        public RelayCommand ClearCommand { get; }
        public RelayCommand ClearAllCommand { get; }
        public RelayCommand CaptureSourceCommand { get; }
        public RelayCommand CaptureTargetCommand { get; }
        public RelayCommand<RemapRowVm> RemoveRuleCommand { get; }

        public RemapVm(Action persist)
        {
            _persist = persist;
            foreach (var k in KeyCodes.AllKeys) TargetKeys.Add(k);
            SelectedKind = Kinds[0];

            ApplyCommand = new RelayCommand(Apply, () => SelectedCap != null);
            ClearCommand = new RelayCommand(ClearSelected, () => SelectedCap != null);
            ClearAllCommand = new RelayCommand(ClearAll);
            CaptureSourceCommand = new RelayCommand(() => BeginCapture(true));
            CaptureTargetCommand = new RelayCommand(() => BeginCapture(false));
            RemoveRuleCommand = new RelayCommand<RemapRowVm>(RemoveRule);
        }

        // ---------- Состояние ----------
        private bool _enabled;
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (!Set(ref _enabled, value)) return;
                AppServices.Remap.Enabled = value;
                AppServices.State.RemapEnabled = value;
                _persist();
                Raise(nameof(StatusText));
            }
        }

        public string StatusText => _enabled ? Loc.T("Remap.On") : Loc.T("Remap.Off");

        private KeyCapVm _selected;
        public KeyCapVm SelectedCap
        {
            get => _selected;
            set
            {
                // Клавишу выбрали мышью — режим «нажмите клавишу» больше не нужен,
                // иначе движок остаётся на паузе и переназначения молча не работают
                if (_capturing) CancelCapture();
                if (_selected != null) _selected.IsSelected = false;
                Set(ref _selected, value);
                if (_selected != null) _selected.IsSelected = true;
                Raise(nameof(SelectedName));
                Raise(nameof(HasSelection));
                LoadExisting();
                RelayCommand.Refresh();
            }
        }

        public bool HasSelection => _selected != null;
        public string SelectedName => _selected == null ? Loc.T("Remap.NoSelection") : _selected.Id.Name;

        private TargetKindOption _selectedKind;
        public TargetKindOption SelectedKind
        {
            get => _selectedKind;
            set
            {
                if (!Set(ref _selectedKind, value)) return;
                if (_capturing) CancelCapture();
                Status = null;
                Raise(nameof(IsKeyTarget));
                Raise(nameof(IsMacroTarget));
                Raise(nameof(IsTextTarget));
            }
        }

        public bool IsKeyTarget => SelectedKind != null && SelectedKind.Kind == RemapTargetKind.Key;
        public bool IsMacroTarget => SelectedKind != null && SelectedKind.Kind == RemapTargetKind.Macro;
        public bool IsTextTarget => SelectedKind != null && SelectedKind.Kind == RemapTargetKind.Text;

        private KeyCodes.KeyOption _targetKey;
        public KeyCodes.KeyOption TargetKey { get => _targetKey; set => Set(ref _targetKey, value); }

        private Macro _targetMacro;
        public Macro TargetMacro { get => _targetMacro; set => Set(ref _targetMacro, value); }

        private string _targetText = string.Empty;
        public string TargetText { get => _targetText; set => Set(ref _targetText, value); }

        private bool _capturing;
        public bool IsCapturing { get => _capturing; private set { if (Set(ref _capturing, value)) Raise(nameof(CaptureHint)); } }

        private string _status;
        /// <summary>Что произошло после последнего действия — видно рядом с кнопками.</summary>
        public string Status
        {
            get => _status;
            private set { if (Set(ref _status, value)) Raise(nameof(HasStatus)); }
        }

        public bool HasStatus => !string.IsNullOrEmpty(_status);

        private bool _captureIsSource;
        public string CaptureHint => !_capturing ? null
            : Loc.T(_captureIsSource ? "Remap.CaptureSource" : "Remap.CaptureTarget");

        // ---------- Перехват физической клавиши ----------
        public void BeginCapture(bool forSource)
        {
            _captureIsSource = forSource;
            IsCapturing = true;
            AppServices.Remap.Paused = true;
        }

        public void CancelCapture()
        {
            IsCapturing = false;
            AppServices.Remap.Paused = false;
        }

        /// <summary>Приходит из хука. Возвращает true, если событие нужно поглотить.</summary>
        public bool HandleCapture(HookKeyEventArgs e)
        {
            if (!IsCapturing || e.FromUs) return false;
            if (!e.IsDown) return true;

            var id = new KeyId(e.Vk, e.Extended);
            if (_captureIsSource)
            {
                var caps = Board.Find(id);
                if (caps != null && caps.Count > 0) SelectedCap = caps[0];
            }
            else
            {
                var option = TargetKeys.FirstOrDefault(k => k.Id.Equals(id))
                          ?? TargetKeys.FirstOrDefault(k => k.Id.Vk == id.Vk);
                if (option != null) { SelectedKind = Kinds[0]; TargetKey = option; }
            }
            CancelCapture();
            return true;
        }

        // ---------- Правила ----------
        private void LoadExisting()
        {
            if (_selected == null) return;
            var profile = AppServices.State.Active;
            var rule = profile.Remaps.FirstOrDefault(r => r.FromKey.Equals(_selected.Id));
            if (rule == null)
            {
                SelectedKind = Kinds[0];
                TargetKey = null;
                TargetMacro = null;
                TargetText = string.Empty;
                return;
            }
            SelectedKind = Kinds.First(k => k.Kind == rule.Kind);
            TargetKey = TargetKeys.FirstOrDefault(k => k.Id.Equals(rule.ToKey));
            TargetMacro = Macros.FirstOrDefault(m => m.Id == rule.MacroId);
            TargetText = rule.Text ?? string.Empty;
        }

        public void Apply()
        {
            if (_capturing) CancelCapture();

            if (_selected == null || SelectedKind == null)
            {
                Status = Loc.T("Remap.Status.NoKey");
                return;
            }

            var profile = AppServices.State.Active;
            var kind = SelectedKind.Kind;

            if (kind == RemapTargetKind.Key && TargetKey == null)
            {
                Status = Loc.T("Remap.Status.NoTarget");
                return;
            }
            if (kind == RemapTargetKind.Macro && TargetMacro == null)
            {
                Status = Loc.T(Macros.Count == 0 ? "Remap.Status.NoMacros" : "Remap.Status.NoMacro");
                return;
            }
            if (kind == RemapTargetKind.Text && string.IsNullOrEmpty(TargetText))
            {
                Status = Loc.T("Remap.Status.NoText");
                return;
            }

            profile.Remaps.RemoveAll(r => r.FromKey.Equals(_selected.Id));
            profile.Remaps.Add(new RemapRule
            {
                From = _selected.Id.ToString(),
                Kind = kind,
                To = kind == RemapTargetKind.Key ? TargetKey.Id.ToString() : null,
                MacroId = kind == RemapTargetKind.Macro ? TargetMacro.Id : null,
                Text = kind == RemapTargetKind.Text ? TargetText : null
            });

            string targetName = kind == RemapTargetKind.Key ? TargetKey.Name
                              : kind == RemapTargetKind.Macro ? TargetMacro.Name
                              : kind == RemapTargetKind.Text ? TargetText
                              : Loc.T("Remap.Disabled");
            string done = Loc.T("Remap.Status.Done", _selected.Id.Name, targetName);

            // Правило без включённого движка ничего не делает — включаем сами
            if (!Enabled)
            {
                Enabled = true;
                done += " · " + Loc.T("Remap.Status.AutoOn");
            }

            Commit();
            Status = done;
        }

        private void ClearSelected()
        {
            if (_capturing) CancelCapture();
            if (_selected == null) { Status = Loc.T("Remap.Status.NoKey"); return; }
            AppServices.State.Active.Remaps.RemoveAll(r => r.FromKey.Equals(_selected.Id));
            Commit();
            Status = Loc.T("Remap.Status.Cleared");
        }

        private void RemoveRule(RemapRowVm row)
        {
            if (row == null) return;
            AppServices.State.Active.Remaps.Remove(row.Rule);
            Commit();
        }

        private void ClearAll()
        {
            if (_capturing) CancelCapture();
            AppServices.State.Active.Remaps.Clear();
            Commit();
            Status = Loc.T("Remap.Status.Cleared");
        }

        private void Commit()
        {
            AppServices.ReloadProfile();
            _persist();
            Refresh();
        }

        // ---------- Обновление представления ----------
        public void Refresh()
        {
            var profile = AppServices.State.Active;

            Macros.Clear();
            foreach (var m in profile.Macros) Macros.Add(m);

            Rules.Clear();
            foreach (var rule in profile.Remaps)
            {
                string to;
                string glyph;
                string badge;
                switch (rule.Kind)
                {
                    case RemapTargetKind.Disabled:
                        to = Loc.T("Remap.Disabled"); glyph = ""; badge = "OFF"; break;
                    case RemapTargetKind.Macro:
                        var macro = profile.Macros.FirstOrDefault(m => m.Id == rule.MacroId);
                        to = macro != null ? macro.Name : Loc.T("Remap.MacroDeleted");
                        glyph = ""; badge = "MACRO"; break;
                    case RemapTargetKind.Text:
                        to = rule.Text; glyph = ""; badge = "TEXT"; break;
                    default:
                        to = rule.ToKey.Name; glyph = ""; badge = "KEY"; break;
                }
                Rules.Add(new RemapRowVm
                {
                    Rule = rule,
                    From = rule.FromKey.Name,
                    To = to,
                    Glyph = glyph,
                    Badge = badge
                });
            }

            foreach (var cap in Board.Keys) cap.Mapping = null;
            foreach (var row in Rules)
            {
                var caps = Board.Find(row.Rule.FromKey);
                if (caps == null) continue;
                foreach (var c in caps) c.Mapping = row.To;
            }

            Raise(nameof(RulesSummary));
            Raise(nameof(HasRules));
            LoadExisting();
        }

        public bool HasRules => Rules.Count > 0;
        public string RulesSummary => Rules.Count == 0
            ? Loc.T("Remap.NoRules")
            : Loc.T("Remap.RulesCount", Rules.Count);

        /// <summary>Обновляет строки после смены языка.</summary>
        public void RefreshTexts()
        {
            foreach (var k in Kinds) k.Refresh();
            Raise(nameof(StatusText));
            Raise(nameof(SelectedName));
            Raise(nameof(RulesSummary));
            Raise(nameof(CaptureHint));
            Raise(nameof(Status));
            Refresh();
        }

        public void SyncEnabledFromState() =>
            Set(ref _enabled, AppServices.Remap.Enabled, nameof(Enabled));
    }

    /// <summary>Типизированная команда для элементов списка.</summary>
    public sealed class RelayCommand<T> : System.Windows.Input.ICommand
    {
        private readonly Action<T> _execute;
        public RelayCommand(Action<T> execute) { _execute = execute; }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute(parameter is T t ? t : default(T));
        public event EventHandler CanExecuteChanged
        {
            add { System.Windows.Input.CommandManager.RequerySuggested += value; }
            remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
        }
    }
}
