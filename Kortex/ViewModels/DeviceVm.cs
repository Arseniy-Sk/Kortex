using System;
using System.Collections.ObjectModel;
using System.Linq;
using Kortex.Devices;
using Kortex.Localization;
using Kortex.Services;

namespace Kortex.ViewModels
{
    /// <summary>Строка списка найденных клавиатур.</summary>
    public sealed class DeviceRowVm : ObservableObject
    {
        public DetectedKeyboard Model { get; }

        public DeviceRowVm(DetectedKeyboard model)
        {
            Model = model;
        }

        public string Name => Model.DisplayName;
        public string Vendor => Model.VendorName;
        public string VidPid => Model.VidPid;
        public bool ViaCapable => Model.ViaCapable;
        public bool IsDemo => Model.IsDemo;
        public string Form => DeviceCatalog.FormName(
            Model.Profile != null ? Model.Profile.Form : FormFactor.Unknown, Loc.I.IsEnglish);

        public string Badge => Model.IsDemo
            ? Loc.T("Device.Demo")
            : Model.ViaCapable ? "VIA " + Model.ProtocolVersion : Loc.T("Common.Software");

        public string Interfaces => Model.Interfaces.Count + " × HID";

        private bool _isConnected;
        public bool IsConnected { get => _isConnected; set => Set(ref _isConnected, value); }

        public void Refresh()
        {
            Raise(nameof(Name)); Raise(nameof(Vendor)); Raise(nameof(Form));
            Raise(nameof(Badge)); Raise(nameof(ViaCapable));
        }
    }

    /// <summary>Экран «Устройство»: поиск плат, подключение и сводка возможностей.</summary>
    public sealed class DeviceVm : ObservableObject
    {
        private readonly DeviceService _service;

        public ObservableCollection<DeviceRowVm> Devices { get; } = new ObservableCollection<DeviceRowVm>();

        public RelayCommand ScanCommand { get; }
        public RelayCommand ConnectCommand { get; }
        public RelayCommand DisconnectCommand { get; }
        public RelayCommand DemoCommand { get; }
        public RelayCommand SaveLightingCommand { get; }
        public RelayCommand EepromResetCommand { get; }
        public RelayCommand BootloaderCommand { get; }

        public DeviceVm(DeviceService service)
        {
            _service = service;
            ScanCommand = new RelayCommand(() => _service.Scan(), () => !_service.IsScanning);
            ConnectCommand = new RelayCommand(Connect, () => Selected != null && Selected.ViaCapable && !Selected.IsConnected);
            DisconnectCommand = new RelayCommand(() => _service.Disconnect(), () => _service.Connected != null);
            DemoCommand = new RelayCommand(() => { _pendingDemo = true; _service.EnableDemo(); });
            SaveLightingCommand = new RelayCommand(SaveLighting, () => HasLighting);
            EepromResetCommand = new RelayCommand(EepromReset, () => IsConnected);
            BootloaderCommand = new RelayCommand(Bootloader, () => IsConnected);

            _service.DevicesChanged += OnDevicesChanged;
            _service.ConnectionChanged += OnConnectionChanged;
            Loc.LanguageChanged += RefreshTexts;
        }

        private bool _pendingDemo;

        private DeviceRowVm _selected;
        public DeviceRowVm Selected
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;
                RaiseDetails();
                RelayCommand.Refresh();
            }
        }

        public bool IsScanning => _service.IsScanning;
        public bool HasDevices => Devices.Count > 0;
        public bool HasSelection => _selected != null;

        public bool IsConnected => _service.Connected != null;

        public string HeaderTitle => IsConnected
            ? Loc.T("Device.Connected")
            : _service.IsScanning ? Loc.T("Device.Searching") : Loc.T("Device.NotFound");

        public string HeaderName => _service.Connected != null
            ? _service.Connected.DisplayName
            : (_selected != null ? _selected.Name : Loc.T("Device.Unknown"));

        // Карточки характеристик
        public string ModelText => _selected != null ? _selected.Name : "—";
        public string VendorText => _selected != null ? _selected.Vendor : "—";
        public string IdsText => _selected != null ? _selected.VidPid : "—";
        public string FormText => _selected != null ? _selected.Form : "—";
        public string ProtocolText => _selected == null ? "—"
            : _selected.ViaCapable ? Loc.T("Device.ViaYes") + " · v" + _selected.Model.ProtocolVersion
                                   : Loc.T("Device.ViaNo");
        public string LayersText => _selected != null && _selected.ViaCapable
            ? _selected.Model.LayerCount.ToString() : "—";
        public string MatrixText => _selected != null && _selected.Model.Profile != null
            ? _selected.Model.Profile.MatrixRows + " × " + _selected.Model.Profile.MatrixCols : "—";
        public string MacroSlotsText => _selected != null && _selected.ViaCapable
            ? _selected.Model.MacroCount + " · " + _selected.Model.MacroBufferSize + " B" : "—";

        public bool ShowSoftwareNote => _selected != null && !_selected.ViaCapable;

        private void OnDevicesChanged()
        {
            string keep = _selected != null ? _selected.Model.Key : null;
            Devices.Clear();
            foreach (var d in _service.Devices) Devices.Add(new DeviceRowVm(d));

            foreach (var row in Devices)
                row.IsConnected = _service.Connected != null && _service.Connected.Key == row.Model.Key;

            var demoRow = _pendingDemo ? Devices.FirstOrDefault(d => d.IsDemo) : null;
            Selected = demoRow
                       ?? Devices.FirstOrDefault(d => d.IsConnected)
                       ?? Devices.FirstOrDefault(d => d.Model.Key == keep)
                       ?? Devices.FirstOrDefault(d => d.ViaCapable)
                       ?? Devices.FirstOrDefault();

            if (demoRow != null)
            {
                _pendingDemo = false;
                if (!demoRow.IsConnected) _service.Connect(demoRow.Model);
            }

            Raise(nameof(IsScanning));
            Raise(nameof(HasDevices));
            Raise(nameof(HeaderTitle));
            RelayCommand.Refresh();
        }

        // ---------------- Подсветка ----------------

        private bool _hasLighting;
        public bool HasLighting
        {
            get => _hasLighting;
            private set { if (Set(ref _hasLighting, value)) RelayCommand.Refresh(); }
        }

        private bool _loadingLighting;

        private int _brightness, _effect, _speed, _hue, _sat;

        public int Brightness
        {
            get => _brightness;
            set { if (Set(ref _brightness, value)) Push(Via.ViaCommand.ValueBrightness, Via.ViaCommand.FlatRgbBrightness, (byte)value); }
        }

        public int Effect
        {
            get => _effect;
            set { if (Set(ref _effect, value)) Push(Via.ViaCommand.ValueEffect, Via.ViaCommand.FlatRgbEffect, (byte)value); }
        }

        public int Speed
        {
            get => _speed;
            set { if (Set(ref _speed, value)) Push(Via.ViaCommand.ValueSpeed, Via.ViaCommand.FlatRgbSpeed, (byte)value); }
        }

        public int Hue
        {
            get => _hue;
            set { if (Set(ref _hue, value)) PushColor(); }
        }

        public int Sat
        {
            get => _sat;
            set { if (Set(ref _sat, value)) PushColor(); }
        }

        private string _lightingStatus;
        public string LightingStatus { get => _lightingStatus; private set => Set(ref _lightingStatus, value); }

        private void Push(byte channelValue, byte flatValue, byte data)
        {
            if (_loadingLighting) return;
            var client = _service.Via;
            if (client == null) return;
            client.SetLightingValue(Via.ViaCommand.ChannelRgbLight, channelValue, flatValue, data);
            LightingStatus = null;
        }

        private void PushColor()
        {
            if (_loadingLighting) return;
            var client = _service.Via;
            if (client == null) return;
            client.SetLightingValue(Via.ViaCommand.ChannelRgbLight, Via.ViaCommand.ValueColor,
                Via.ViaCommand.FlatRgbColor, (byte)_hue, (byte)_sat);
            LightingStatus = null;
        }

        /// <summary>Спрашивает плату о подсветке: не ответила — раздел скрыт.</summary>
        private void ProbeLighting()
        {
            var client = _service.Via;
            HasLighting = false;
            LightingStatus = null;
            if (client == null) return;

            var brightness = client.GetLightingValue(Via.ViaCommand.ChannelRgbLight,
                Via.ViaCommand.ValueBrightness, Via.ViaCommand.FlatRgbBrightness, 1);
            if (brightness == null) return;

            _loadingLighting = true;
            try
            {
                Brightness = brightness[0];

                var effect = client.GetLightingValue(Via.ViaCommand.ChannelRgbLight,
                    Via.ViaCommand.ValueEffect, Via.ViaCommand.FlatRgbEffect, 1);
                if (effect != null) Effect = effect[0];

                var speed = client.GetLightingValue(Via.ViaCommand.ChannelRgbLight,
                    Via.ViaCommand.ValueSpeed, Via.ViaCommand.FlatRgbSpeed, 1);
                if (speed != null) Speed = speed[0];

                var color = client.GetLightingValue(Via.ViaCommand.ChannelRgbLight,
                    Via.ViaCommand.ValueColor, Via.ViaCommand.FlatRgbColor, 2);
                if (color != null) { Hue = color[0]; Sat = color[1]; }
            }
            finally
            {
                _loadingLighting = false;
            }

            HasLighting = true;
            RaiseLighting();
        }

        private void RaiseLighting()
        {
            Raise(nameof(Brightness)); Raise(nameof(Effect)); Raise(nameof(Speed));
            Raise(nameof(Hue)); Raise(nameof(Sat));
        }

        private void SaveLighting()
        {
            var client = _service.Via;
            if (client == null) return;
            client.SaveLighting(Via.ViaCommand.ChannelRgbLight);
            LightingStatus = Loc.T("Light.Saved");
        }

        // ---------------- Обслуживание ----------------

        private void EepromReset()
        {
            var client = _service.Via;
            if (client == null) return;
            if (System.Windows.MessageBox.Show(Loc.T("Device.EepromAsk"), "Kortex",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.OK) return;
            client.EepromReset();
        }

        private void Bootloader()
        {
            var client = _service.Via;
            if (client == null) return;
            if (System.Windows.MessageBox.Show(Loc.T("Device.BootloaderAsk"), "Kortex",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.OK) return;
            client.BootloaderJump();
            _service.Disconnect();
        }

        public bool VialDetected => _selected != null && _selected.Model.VialDetected;

        private void OnConnectionChanged()
        {
            foreach (var row in Devices)
                row.IsConnected = _service.Connected != null && _service.Connected.Key == row.Model.Key;
            Raise(nameof(IsConnected));
            Raise(nameof(HeaderTitle));
            Raise(nameof(HeaderName));
            ProbeLighting();
            RelayCommand.Refresh();
        }

        private void Connect()
        {
            if (_selected == null) return;
            _service.Connect(_selected.Model);
        }

        public void RefreshTexts()
        {
            foreach (var row in Devices) row.Refresh();
            Raise(nameof(HeaderTitle));
            Raise(nameof(HeaderName));
            RaiseDetails();
        }

        private void RaiseDetails()
        {
            Raise(nameof(HasSelection));
            Raise(nameof(ModelText)); Raise(nameof(VendorText)); Raise(nameof(IdsText));
            Raise(nameof(FormText)); Raise(nameof(ProtocolText)); Raise(nameof(LayersText));
            Raise(nameof(MatrixText)); Raise(nameof(MacroSlotsText));
            Raise(nameof(ShowSoftwareNote)); Raise(nameof(HeaderName)); Raise(nameof(VialDetected));
        }
    }
}
