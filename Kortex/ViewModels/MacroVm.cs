using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Kortex.Input;
using Kortex.Localization;
using Kortex.Models;
using Kortex.Services;

namespace Kortex.ViewModels
{
    /// <summary>Обёртка над макросом для редактирования в UI.</summary>
    public sealed class MacroItemVm : ObservableObject
    {
        public Macro Model { get; }
        public ObservableCollection<MacroStep> Steps { get; } = new ObservableCollection<MacroStep>();

        public MacroItemVm(Macro model)
        {
            Model = model;
            foreach (var s in model.Steps) Steps.Add(s);
            Steps.CollectionChanged += (s, e) =>
            {
                Model.Steps = Steps.ToList();
                Raise(nameof(Summary));
                Raise(nameof(IsEmpty));
                Raise(nameof(HasSteps));
            };
        }

        public string Id => Model.Id;

        public string Name
        {
            get => Model.Name;
            set { Model.Name = value; Raise(); }
        }

        public int Repeat
        {
            get => Model.Repeat;
            set { Model.Repeat = Math.Max(1, value); Raise(); Raise(nameof(Summary)); }
        }

        public double Speed
        {
            get => Model.Speed;
            set { Model.Speed = Math.Max(0.1, Math.Min(10, value)); Raise(); Raise(nameof(SpeedText)); }
        }

        public string SpeedText => Model.Speed.ToString("0.0") + "×";

        public string Summary
        {
            get
            {
                int keys = Steps.Count(s => s.Kind != MacroStepKind.Delay);
                return Loc.T("Macro.Summary", keys, Model.TotalMs) + (Repeat > 1 ? " · x" + Repeat : "");
            }
        }

        public void RefreshTexts()
        {
            Raise(nameof(Summary));
            foreach (var s in Steps) s.Touch();
        }

        public bool IsEmpty => Steps.Count == 0;
        public bool HasSteps => Steps.Count > 0;

        public void NotifyStepsChanged()
        {
            Model.Steps = Steps.ToList();
            Raise(nameof(Summary));
            Raise(nameof(IsEmpty));
            Raise(nameof(HasSteps));
        }
    }

    /// <summary>Тип шага в конструкторе.</summary>
    public sealed class StepKindOption : ObservableObject
    {
        public MacroStepKind Kind { get; set; }
        public string NameKey { get; set; }
        public string Name => Loc.T(NameKey);
        public void Refresh() => Raise(nameof(Name));
        public override string ToString() => Name;
    }

    /// <summary>Экран «Макросы».</summary>
    public sealed class MacroVm : ObservableObject
    {
        private readonly Action _persist;

        public ObservableCollection<MacroItemVm> Macros { get; } = new ObservableCollection<MacroItemVm>();
        public ObservableCollection<KeyCodes.KeyOption> Keys { get; } = new ObservableCollection<KeyCodes.KeyOption>();

        public RelayCommand NewCommand { get; }
        public RelayCommand PresetsCommand { get; }
        public RelayCommand DuplicateCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand RecordCommand { get; }
        public RelayCommand PlayCommand { get; }
        public RelayCommand CompactCommand { get; }
        public RelayCommand AddStepCommand { get; }
        public RelayCommand BrowseCommand { get; }
        public RelayCommand WriteToBoardCommand { get; }
        public RelayCommand ReadFromBoardCommand { get; }
        public RelayCommand<MacroStep> RemoveStepCommand { get; }
        public RelayCommand<MacroStep> MoveUpCommand { get; }
        public RelayCommand<MacroStep> MoveDownCommand { get; }

        /// <summary>Библиотека макросов изменилась — списки в других разделах устарели.</summary>
        public event Action LibraryChanged;

        public MacroVm(Action persist)
        {
            _persist = persist;
            foreach (var k in KeyCodes.AllKeys) Keys.Add(k);
            NewKey = Keys.FirstOrDefault();
            NewStepKind = StepKinds[0];

            NewCommand = new RelayCommand(CreateMacro);
            PresetsCommand = new RelayCommand(AddPresets);
            DuplicateCommand = new RelayCommand(Duplicate, () => Selected != null);
            DeleteCommand = new RelayCommand(Delete, () => Selected != null);
            RecordCommand = new RelayCommand(ToggleRecord, () => Selected != null);
            PlayCommand = new RelayCommand(Play, () => Selected != null && Selected.HasSteps);
            CompactCommand = new RelayCommand(Compact, () => Selected != null && Selected.HasSteps);
            AddStepCommand = new RelayCommand(AddStep, () => Selected != null);
            BrowseCommand = new RelayCommand(Browse);
            WriteToBoardCommand = new RelayCommand(WriteToBoard, () => HasBoard);
            ReadFromBoardCommand = new RelayCommand(ReadFromBoard, () => HasBoard);
            RemoveStepCommand = new RelayCommand<MacroStep>(RemoveStep);
            MoveUpCommand = new RelayCommand<MacroStep>(s => Move(s, -1));
            MoveDownCommand = new RelayCommand<MacroStep>(s => Move(s, 1));

            AppServices.Recorder.Changed += OnRecorderChanged;
        }

        private MacroItemVm _selected;
        public MacroItemVm Selected
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;
                Raise(nameof(HasSelection));
                RelayCommand.Refresh();
            }
        }

        public bool HasSelection => _selected != null;

        private bool _isRecording;
        public bool IsRecording
        {
            get => _isRecording;
            private set
            {
                if (!Set(ref _isRecording, value)) return;
                Raise(nameof(RecordButtonText));
                Raise(nameof(RecordHint));
            }
        }

        public string RecordButtonText => Loc.T(_isRecording ? "Macro.Stop" : "Macro.Record");
        public string RecordHint => Loc.T(_isRecording ? "Macro.RecordHintLive" : "Macro.RecordHintIdle");

        /// <summary>Обновляет строки после смены языка.</summary>
        public void RefreshTexts()
        {
            Raise(nameof(RecordButtonText));
            Raise(nameof(RecordHint));
            foreach (var k in StepKinds) k.Refresh();
            foreach (var m in Macros) m.RefreshTexts();
        }

        private bool _captureDelays = true;
        public bool CaptureDelays
        {
            get => _captureDelays;
            set { if (Set(ref _captureDelays, value)) AppServices.Recorder.CaptureDelays = value; }
        }

        private KeyCodes.KeyOption _newKey;
        public KeyCodes.KeyOption NewKey { get => _newKey; set => Set(ref _newKey, value); }

        private int _newDelay = 50;
        public int NewDelay { get => _newDelay; set => Set(ref _newDelay, Math.Max(0, value)); }

        private string _newText = string.Empty;
        public string NewText { get => _newText; set => Set(ref _newText, value); }

        private string _newArg = string.Empty;
        public string NewArg { get => _newArg; set => Set(ref _newArg, value); }

        private int _newAmount = 3;
        public int NewAmount { get => _newAmount; set => Set(ref _newAmount, value); }

        private int _newX;
        public int NewX { get => _newX; set => Set(ref _newX, value); }

        private int _newY;
        public int NewY { get => _newY; set => Set(ref _newY, value); }

        private bool _newAbsolute;
        public bool NewAbsolute { get => _newAbsolute; set => Set(ref _newAbsolute, value); }

        private int _newButton;
        public int NewButton { get => _newButton; set => Set(ref _newButton, value); }

        public List<StepKindOption> StepKinds { get; } = new List<StepKindOption>
        {
            new StepKindOption { Kind = MacroStepKind.Tap,        NameKey = "Macro.Step.Tap" },
            new StepKindOption { Kind = MacroStepKind.Down,       NameKey = "Macro.Step.Down" },
            new StepKindOption { Kind = MacroStepKind.Up,         NameKey = "Macro.Step.Up" },
            new StepKindOption { Kind = MacroStepKind.Delay,      NameKey = "Macro.Step.Delay" },
            new StepKindOption { Kind = MacroStepKind.Text,       NameKey = "Macro.Step.Text" },
            new StepKindOption { Kind = MacroStepKind.Launch,     NameKey = "Macro.Step.Launch" },
            new StepKindOption { Kind = MacroStepKind.MouseClick, NameKey = "Macro.Step.Click" },
            new StepKindOption { Kind = MacroStepKind.MouseMove,  NameKey = "Macro.Step.Move" },
            new StepKindOption { Kind = MacroStepKind.Scroll,     NameKey = "Macro.Step.Scroll" },
            new StepKindOption { Kind = MacroStepKind.Clipboard,  NameKey = "Macro.Step.Clipboard" },
            new StepKindOption { Kind = MacroStepKind.Stamp,      NameKey = "Macro.Step.Stamp" },
        };

        private StepKindOption _newStepKind;
        public StepKindOption NewStepKind
        {
            get => _newStepKind;
            set
            {
                if (!Set(ref _newStepKind, value)) return;
                Raise(nameof(IsKeyStep)); Raise(nameof(IsDelayStep)); Raise(nameof(IsTextStep));
                Raise(nameof(IsLaunchStep)); Raise(nameof(IsClickStep)); Raise(nameof(IsMoveStep));
                Raise(nameof(IsScrollStep));
            }
        }

        private MacroStepKind Kind => _newStepKind == null ? MacroStepKind.Tap : _newStepKind.Kind;

        public bool IsKeyStep => Kind == MacroStepKind.Tap || Kind == MacroStepKind.Down || Kind == MacroStepKind.Up;
        public bool IsDelayStep => Kind == MacroStepKind.Delay;
        public bool IsTextStep => Kind == MacroStepKind.Text || Kind == MacroStepKind.Clipboard || Kind == MacroStepKind.Stamp;
        public bool IsLaunchStep => Kind == MacroStepKind.Launch;
        public bool IsClickStep => Kind == MacroStepKind.MouseClick;
        public bool IsMoveStep => Kind == MacroStepKind.MouseMove;
        public bool IsScrollStep => Kind == MacroStepKind.Scroll;

        // ---------- Операции ----------
        private void CreateMacro()
        {
            var macro = new Macro { Name = Loc.T("Nav.Macro") + " " + (Macros.Count + 1) };
            AppServices.State.Active.Macros.Add(macro);
            var vm = new MacroItemVm(macro);
            Macros.Add(vm);
            Selected = vm;
            Commit();
        }

        /// <summary>Добавляет готовые заготовки, пропуская уже существующие.</summary>
        private void AddPresets()
        {
            var profile = AppServices.State.Active;
            int added = MacroPresets.EnsureIn(profile);
            Refresh();
            if (added > 0) Selected = Macros.FirstOrDefault();
            Commit();
        }

        private void Duplicate()
        {
            if (Selected == null) return;
            var copy = Selected.Model.Clone();
            AppServices.State.Active.Macros.Add(copy);
            var vm = new MacroItemVm(copy);
            Macros.Add(vm);
            Selected = vm;
            Commit();
        }

        private void Delete()
        {
            if (Selected == null) return;
            var profile = AppServices.State.Active;
            profile.Macros.RemoveAll(m => m.Id == Selected.Id);
            profile.Remaps.RemoveAll(r => r.Kind == RemapTargetKind.Macro && r.MacroId == Selected.Id);
            Macros.Remove(Selected);
            Selected = Macros.FirstOrDefault();
            Commit();
        }

        private void ToggleRecord()
        {
            if (Selected == null) return;
            if (AppServices.Recorder.IsRecording)
            {
                var steps = AppServices.Recorder.Stop();
                IsRecording = false;
                Selected.Steps.Clear();
                foreach (var s in MacroRecorder.Compact(steps)) Selected.Steps.Add(s);
                Commit();
            }
            else
            {
                AppServices.Recorder.CaptureDelays = CaptureDelays;
                AppServices.Recorder.Start();
                IsRecording = true;
            }
            RelayCommand.Refresh();
        }

        public void StopRecordingIfActive()
        {
            if (AppServices.Recorder.IsRecording) ToggleRecord();
        }

        private void OnRecorderChanged()
        {
            IsRecording = AppServices.Recorder.IsRecording;
            Raise(nameof(LiveStepCount));
        }

        public int LiveStepCount => AppServices.Recorder.Steps.Count;

        private void Play()
        {
            if (Selected == null) return;
            AppServices.Remap.Play(Selected.Model);
        }

        private void Compact()
        {
            if (Selected == null) return;
            var compacted = MacroRecorder.Compact(Selected.Steps.ToList());
            Selected.Steps.Clear();
            foreach (var s in compacted) Selected.Steps.Add(s);
            Commit();
        }

        // ---------------- Конструктор шагов ----------------

        private void AddStep()
        {
            if (Selected == null || NewStepKind == null) return;
            var step = new MacroStep { Kind = NewStepKind.Kind };

            switch (NewStepKind.Kind)
            {
                case MacroStepKind.Tap:
                case MacroStepKind.Down:
                case MacroStepKind.Up:
                    if (NewKey == null) return;
                    step.Key = NewKey.Id.ToString();
                    break;
                case MacroStepKind.Delay:
                    step.DelayMs = NewDelay;
                    break;
                case MacroStepKind.Text:
                case MacroStepKind.Clipboard:
                    if (string.IsNullOrEmpty(NewText)) return;
                    step.Text = NewText;
                    break;
                case MacroStepKind.Launch:
                    if (string.IsNullOrWhiteSpace(NewText)) return;
                    step.Text = NewText;
                    step.Arg = NewArg;
                    break;
                case MacroStepKind.MouseClick:
                    step.Amount = NewButton;
                    break;
                case MacroStepKind.MouseMove:
                    step.X = NewX;
                    step.Y = NewY;
                    step.Arg = NewAbsolute ? "abs" : "rel";
                    break;
                case MacroStepKind.Scroll:
                    step.Amount = NewAmount == 0 ? 3 : NewAmount;
                    break;
                case MacroStepKind.Stamp:
                    step.Text = string.IsNullOrWhiteSpace(NewText) ? "dd.MM.yyyy" : NewText;
                    break;
            }

            Selected.Steps.Add(step);
            Commit();
        }

        /// <summary>Выбор программы или файла для шага запуска.</summary>
        private void Browse()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { CheckFileExists = false };
            if (dlg.ShowDialog() == true) NewText = dlg.FileName;
        }

        // ---------------- Макросы в прошивке платы ----------------

        public bool HasBoard => AppServices.Devices != null && AppServices.Devices.Via != null;

        private string _firmwareStatus;
        public string FirmwareStatus
        {
            get => _firmwareStatus;
            private set { if (Set(ref _firmwareStatus, value)) Raise(nameof(HasFirmwareStatus)); }
        }

        public bool HasFirmwareStatus => !string.IsNullOrEmpty(_firmwareStatus);

        /// <summary>Записывает макросы в память платы — они работают без запущенной программы.</summary>
        private void WriteToBoard()
        {
            var device = AppServices.Devices.Connected;
            var client = AppServices.Devices.Via;
            if (device == null || client == null) return;

            int slots = Math.Max(1, device.MacroCount);
            int size = Math.Max(1, device.MacroBufferSize);
            var source = AppServices.State.Active.Macros;

            var encoded = Via.ViaMacroCodec.Encode(source, slots, size);
            if (!client.WriteMacroBuffer(0, encoded.Buffer))
            {
                FirmwareStatus = Loc.T("Device.ViaNo");
                return;
            }

            string status = Loc.T("Macro.Firmware.Written", encoded.Written, slots);
            if (encoded.Overflow) status += " · " + Loc.T("Macro.Firmware.Overflow");
            if (encoded.Skipped.Count > 0)
                status += " · " + Loc.T("Macro.Firmware.Skipped", encoded.Skipped.Count);
            FirmwareStatus = status;
        }

        /// <summary>Читает макросы, уже записанные в плату, и добавляет их в библиотеку.</summary>
        private void ReadFromBoard()
        {
            var device = AppServices.Devices.Connected;
            var client = AppServices.Devices.Via;
            if (device == null || client == null) return;

            int slots = Math.Max(1, device.MacroCount);
            int size = Math.Max(1, device.MacroBufferSize);
            var raw = client.ReadMacroBuffer(0, size);
            if (raw == null) { FirmwareStatus = Loc.T("Device.ViaNo"); return; }

            var read = Via.ViaMacroCodec.Decode(raw, slots);
            if (read.Count == 0) { FirmwareStatus = Loc.T("Macro.Firmware.Empty"); return; }

            var profile = AppServices.State.Active;
            foreach (var macro in read)
            {
                macro.Name = Loc.T("Macro.Firmware.Prefix") + " " + macro.Name;
                profile.Macros.Add(macro);
            }
            Refresh();
            Commit();
            FirmwareStatus = Loc.T("Macro.Firmware.ReadDone", read.Count);
        }

        private void RemoveStep(MacroStep step)
        {
            if (Selected == null || step == null) return;
            Selected.Steps.Remove(step);
            Commit();
        }

        private void Move(MacroStep step, int delta)
        {
            if (Selected == null || step == null) return;
            int i = Selected.Steps.IndexOf(step);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= Selected.Steps.Count) return;
            Selected.Steps.Move(i, j);
            Commit();
        }

        private void Commit()
        {
            if (Selected != null) Selected.NotifyStepsChanged();
            AppServices.ReloadProfile();
            _persist();
            RelayCommand.Refresh();
            var h = LibraryChanged;
            if (h != null) h();
        }

        public void Refresh()
        {
            string keepId = Selected != null ? Selected.Id : null;
            Macros.Clear();
            foreach (var m in AppServices.State.Active.Macros) Macros.Add(new MacroItemVm(m));
            Selected = Macros.FirstOrDefault(m => m.Id == keepId) ?? Macros.FirstOrDefault();
        }
    }
}
