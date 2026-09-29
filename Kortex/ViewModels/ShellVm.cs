using System;
using System.Collections.ObjectModel;
using System.Linq;
using Kortex.Input;
using Kortex.Localization;
using Kortex.Models;
using Kortex.Services;

namespace Kortex.ViewModels
{
    public static class Pages
    {
        public const int Welcome = 0;
        public const int Tester = 1;
        public const int Keymap = 2;
        public const int Remap = 3;
        public const int Macro = 4;
        public const int Device = 5;
        public const int Settings = 6;
    }

    /// <summary>Корневая модель окна: навигация, профили, язык, устройство.</summary>
    public sealed class ShellVm : ObservableObject
    {
        public TesterVm Tester { get; }
        public RemapVm Remap { get; }
        public MacroVm Macros { get; }
        public DeviceVm Device { get; }
        public KeymapVm Keymap { get; }

        public ObservableCollection<Profile> Profiles { get; } = new ObservableCollection<Profile>();

        public RelayCommand NewProfileCommand { get; }
        public RelayCommand DeleteProfileCommand { get; }
        public RelayCommand ExportCommand { get; }
        public RelayCommand ImportCommand { get; }
        public RelayCommand OpenFolderCommand { get; }
        public RelayCommand<string> GoCommand { get; }
        public RelayCommand StartCommand { get; }
        public RelayCommand<string> SetLanguageCommand { get; }

        public ShellVm()
        {
            Action<Action> toUi = action =>
            {
                var app = System.Windows.Application.Current;
                if (app == null) action();
                else app.Dispatcher.BeginInvoke(action);
            };

            Tester = new TesterVm();
            Remap = new RemapVm(Persist);
            Macros = new MacroVm(Persist);
            Device = new DeviceVm(AppServices.Devices);
            Keymap = new KeymapVm(AppServices.Devices, toUi);

            NewProfileCommand = new RelayCommand(NewProfile);
            DeleteProfileCommand = new RelayCommand(DeleteProfile, () => Profiles.Count > 1);
            ExportCommand = new RelayCommand(Export);
            ImportCommand = new RelayCommand(Import);
            OpenFolderCommand = new RelayCommand(OpenFolder);
            GoCommand = new RelayCommand<string>(target =>
            {
                int page;
                if (int.TryParse(target, out page)) Page = page;
            });
            StartCommand = new RelayCommand(() => Page = Pages.Tester);
            SetLanguageCommand = new RelayCommand<string>(code =>
            {
                Language = code == "en" ? AppLanguage.English : AppLanguage.Russian;
            });

            RefreshProfileList();
            _activeProfile = AppServices.State.Active;
            RebuildBoards();
            Remap.SyncEnabledFromState();
            Remap.Refresh();
            Macros.Refresh();

            ApplyPulse();
            Tester.HeatmapEnabled = AppServices.State.Heatmap;
            _page = AppServices.State.ShowWelcome ? Pages.Welcome : Pages.Tester;

            Macros.LibraryChanged += () => Remap.Refresh();

            AppServices.KeyObserved += OnKey;
            AppServices.Devices.ConnectionChanged += OnDeviceConnectionChanged;
            Loc.LanguageChanged += OnLanguageChanged;

            AppServices.Devices.Scan();
        }

        // ---------------- Навигация ----------------
        private int _page;
        public int Page
        {
            get => _page;
            set
            {
                if (!Set(ref _page, value)) return;
                RaisePageFlags();
                if (value != Pages.Remap && Remap.IsCapturing) Remap.CancelCapture();
                if (value != Pages.Macro) Macros.StopRecordingIfActive();
                // Список макросов в переназначении мог устареть, пока правили библиотеку
                if (value == Pages.Remap) Remap.Refresh();
            }
        }

        private void RaisePageFlags()
        {
            Raise(nameof(IsWelcomePage));
            Raise(nameof(IsTesterPage));
            Raise(nameof(IsKeymapPage));
            Raise(nameof(IsRemapPage));
            Raise(nameof(IsMacroPage));
            Raise(nameof(IsDevicePage));
            Raise(nameof(IsSettingsPage));
            Raise(nameof(PageTitle));
            Raise(nameof(PageSubtitle));
            Raise(nameof(ShowPageHeader));
        }

        public bool IsWelcomePage => _page == Pages.Welcome;
        public bool IsTesterPage => _page == Pages.Tester;
        public bool IsKeymapPage => _page == Pages.Keymap;
        public bool IsRemapPage => _page == Pages.Remap;
        public bool IsMacroPage => _page == Pages.Macro;
        public bool IsDevicePage => _page == Pages.Device;
        public bool IsSettingsPage => _page == Pages.Settings;

        public bool ShowPageHeader => _page != Pages.Welcome;

        public string PageTitle
        {
            get
            {
                switch (_page)
                {
                    case Pages.Welcome: return Loc.T("Page.Welcome");
                    case Pages.Keymap: return Loc.T("Page.Keymap");
                    case Pages.Remap: return Loc.T("Page.Remap");
                    case Pages.Macro: return Loc.T("Page.Macro");
                    case Pages.Device: return Loc.T("Page.Device");
                    case Pages.Settings: return Loc.T("Page.Settings");
                    default: return Loc.T("Page.Tester");
                }
            }
        }

        public string PageSubtitle
        {
            get
            {
                switch (_page)
                {
                    case Pages.Welcome: return Loc.T("Page.Welcome.Sub");
                    case Pages.Keymap: return Loc.T("Page.Keymap.Sub");
                    case Pages.Remap: return Loc.T("Page.Remap.Sub");
                    case Pages.Macro: return Loc.T("Page.Macro.Sub");
                    case Pages.Device: return Loc.T("Page.Device.Sub");
                    case Pages.Settings: return Loc.T("Page.Settings.Sub");
                    default: return Loc.T("Page.Tester.Sub");
                }
            }
        }

        // ---------------- Язык ----------------
        public AppLanguage Language
        {
            get => Loc.I.Language;
            set
            {
                if (Loc.I.Language == value) return;
                Loc.I.Language = value;
                AppServices.State.Language = value;
                Persist();
            }
        }

        public bool IsRussian => Loc.I.IsRussian;
        public bool IsEnglish => Loc.I.IsEnglish;

        private void OnLanguageChanged()
        {
            Raise(nameof(Language));
            Raise(nameof(IsRussian));
            Raise(nameof(IsEnglish));
            RaisePageFlags();
            Raise(nameof(LayoutSummary));
            Raise(nameof(KeyCountText));
            Raise(nameof(DeviceBadge));
            Tester.RefreshTexts();
            Remap.RefreshTexts();
            Macros.RefreshTexts();
            Device.RefreshTexts();
        }

        // ---------------- Устройство ----------------
        private void OnDeviceConnectionChanged()
        {
            Raise(nameof(DeviceBadge));
            Raise(nameof(HasBoard));
        }

        public bool HasBoard => AppServices.Devices.Connected != null;

        public string DeviceBadge
        {
            get
            {
                var connected = AppServices.Devices.Connected;
                if (connected == null) return Loc.T("Device.NotFound");
                return connected.DisplayName;
            }
        }

        // ---------------- Варианты сборки платы ----------------
        private LayoutOptions Options =>
            AppServices.State.Active.Layout ?? (AppServices.State.Active.Layout = new LayoutOptions());

        public bool Iso { get => Options.Iso; set { Options.Iso = value; OnLayoutChanged(); } }
        public bool SplitBackspace { get => Options.SplitBackspace; set { Options.SplitBackspace = value; OnLayoutChanged(); } }
        public bool SteppedCaps { get => Options.SteppedCaps; set { Options.SteppedCaps = value; OnLayoutChanged(); } }
        public bool SplitRightShift { get => Options.SplitRightShift; set { Options.SplitRightShift = value; OnLayoutChanged(); } }
        public bool SevenUnitSpace { get => Options.SevenUnitSpace; set { Options.SevenUnitSpace = value; OnLayoutChanged(); } }

        private void OnLayoutChanged()
        {
            RebuildBoards();
            Persist();
            Raise(nameof(Iso));
            Raise(nameof(SplitBackspace));
            Raise(nameof(SteppedCaps));
            Raise(nameof(SplitRightShift));
            Raise(nameof(SevenUnitSpace));
            Raise(nameof(LayoutSummary));
        }

        public string LayoutSummary
        {
            get
            {
                var parts = new System.Collections.Generic.List<string> { Options.Iso ? "ISO" : "ANSI" };
                if (Options.SplitBackspace && !Options.Iso) parts.Add("split backspace");
                if (Options.SteppedCaps) parts.Add("stepped caps");
                if (Options.SplitRightShift) parts.Add("split right shift");
                if (Options.SevenUnitSpace) parts.Add("7u space");
                return "TKL · " + string.Join(" · ", parts);
            }
        }

        private void RebuildBoards()
        {
            Tester.Board.Rebuild(Options);
            Remap.Board.Rebuild(Options);
            Keymap.RebuildBoard(Options);
            Tester.RefreshStats();
            Tester.ApplyMappingLabels(AppServices.State.Active);
            Remap.Refresh();
            Raise(nameof(KeyCountText));
        }

        public string KeyCountText => Loc.T("Tester.Keys", Tester.Board.TestableCount);

        // ---------------- Параметры ----------------
        public bool BlockSystemKeys
        {
            get => AppServices.State.BlockSystemKeys;
            set { AppServices.State.BlockSystemKeys = value; Raise(); Persist(); }
        }

        public int ChatterThreshold
        {
            get => AppServices.State.ChatterThresholdMs;
            set { AppServices.State.ChatterThresholdMs = Math.Max(1, Math.Min(200, value)); Raise(); Persist(); }
        }

        public bool ShowWelcomeAtStart
        {
            get => AppServices.State.ShowWelcome;
            set { AppServices.State.ShowWelcome = value; Raise(); Persist(); }
        }

        public bool Animations
        {
            get => AppServices.State.Animations;
            set
            {
                AppServices.State.Animations = value;
                ApplyPulse();
                Raise();
                Persist();
            }
        }

        private void ApplyPulse()
        {
            bool on = AppServices.State.Animations;
            Tester.Board.PulseEnabled = on;
            Remap.Board.PulseEnabled = on;
            Keymap.Board.PulseEnabled = on;
        }

        public bool Heatmap
        {
            get => AppServices.State.Heatmap;
            set
            {
                AppServices.State.Heatmap = value;
                Tester.HeatmapEnabled = value;
                Raise();
                Persist();
            }
        }

        // ---------------- Профили ----------------
        private Profile _activeProfile;
        private bool _listReloading;

        public Profile ActiveProfile
        {
            get => _activeProfile;
            set
            {
                if (_listReloading || value == null || ReferenceEquals(value, _activeProfile)) return;
                SwitchTo(value);
            }
        }

        public string ProfileNameEditable
        {
            get => _activeProfile != null ? _activeProfile.Name : string.Empty;
            set
            {
                if (_activeProfile == null || string.IsNullOrWhiteSpace(value) || value == _activeProfile.Name) return;
                _activeProfile.Name = value;
                Persist();
                RefreshProfileList();
                Raise(nameof(ActiveProfile));
                Raise();
            }
        }

        private void RefreshProfileList()
        {
            _listReloading = true;
            Profiles.Clear();
            foreach (var p in AppServices.State.Profiles) Profiles.Add(p);
            _listReloading = false;
        }

        private void SwitchTo(Profile profile)
        {
            int index = AppServices.State.Profiles.IndexOf(profile);
            if (index < 0) return;

            AppServices.State.ActiveProfile = index;
            _activeProfile = profile;

            AppServices.ReloadProfile();
            Remap.SyncEnabledFromState();
            Macros.Refresh();
            RebuildBoards();
            Tester.Reset();
            Persist();

            Raise(nameof(ActiveProfile));
            Raise(nameof(ProfileNameEditable));
            RelayCommand.Refresh();
        }

        private void NewProfile()
        {
            var profile = new Profile { Name = Loc.T("Settings.Profile") + " " + (AppServices.State.Profiles.Count + 1) };
            AppServices.State.Profiles.Add(profile);
            RefreshProfileList();
            SwitchTo(profile);
        }

        private void DeleteProfile()
        {
            if (AppServices.State.Profiles.Count <= 1 || _activeProfile == null) return;
            AppServices.State.Profiles.Remove(_activeProfile);
            _activeProfile = null;
            RefreshProfileList();
            SwitchTo(AppServices.State.Profiles[0]);
        }

        private void OpenFolder()
        {
            System.IO.Directory.CreateDirectory(ProfileStore.Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ProfileStore.Folder,
                UseShellExecute = true
            });
        }

        private void Export()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Kortex profile (*.json)|*.json",
                FileName = (_activeProfile != null ? _activeProfile.Name : "profile") + ".json"
            };
            if (dlg.ShowDialog() == true) ProfileStore.Export(_activeProfile, dlg.FileName);
        }

        private void Import()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Kortex profile (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var profile = ProfileStore.Import(dlg.FileName);
                if (profile == null) return;
                AppServices.State.Profiles.Add(profile);
                RefreshProfileList();
                SwitchTo(profile);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Kortex",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        public void Persist() => AppServices.Save();

        // ---------------- Поток клавиатурных событий ----------------
        private void OnKey(object sender, HookKeyEventArgs e)
        {
            if (Remap.IsCapturing && Remap.HandleCapture(e))
            {
                e.Suppress = true;
                return;
            }
            Tester.Handle(e);

            var caps = Remap.Board.Find(e.Key);
            if (caps != null) foreach (var c in caps) c.IsDown = e.IsDown;

            var firmware = Keymap.Board.Find(e.Key);
            if (firmware != null) foreach (var c in firmware) c.IsDown = e.IsDown;
        }
    }
}
