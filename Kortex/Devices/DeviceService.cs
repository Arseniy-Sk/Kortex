using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Kortex.Hid;
using Kortex.Via;

namespace Kortex.Devices
{
    /// <summary>Обнаруженная клавиатура: набор HID-интерфейсов одного физического устройства.</summary>
    public sealed class DetectedKeyboard
    {
        public ushort VendorId { get; set; }
        public ushort ProductId { get; set; }
        public BoardProfile Profile { get; set; }
        public List<HidInterfaceInfo> Interfaces { get; } = new List<HidInterfaceInfo>();
        public HidInterfaceInfo RawInterface { get; set; }
        public bool IsDemo { get; set; }

        /// <summary>Плата ответила на команду VIA.</summary>
        public bool ViaCapable { get; set; }
        public int ProtocolVersion { get; set; } = -1;
        /// <summary>Плата отвечает на расширенные команды Vial.</summary>
        public bool VialDetected { get; set; }
        public int LayerCount { get; set; }
        public int MacroCount { get; set; }
        public int MacroBufferSize { get; set; }

        public string Key => IsDemo ? "demo" : VendorId.ToString("X4") + ":" + ProductId.ToString("X4");
        public string VidPid => "VID " + VendorId.ToString("X4") + " · PID " + ProductId.ToString("X4");
        public string DisplayName => Profile != null ? Profile.Model : VidPid;
        public string VendorName => Profile != null ? Profile.Vendor : "—";
    }

    /// <summary>
    /// Поиск клавиатур, опрос их по протоколу VIA и удержание активного соединения.
    /// Вся работа с железом идёт в фоне, события приходят в поток интерфейса.
    /// </summary>
    public sealed class DeviceService
    {
        private readonly object _sync = new object();
        private readonly Action<Action> _toUi;

        public DeviceService(Action<Action> marshalToUi)
        {
            _toUi = marshalToUi ?? (a => a());
        }

        public List<DetectedKeyboard> Devices { get; private set; } = new List<DetectedKeyboard>();
        public DetectedKeyboard Connected { get; private set; }
        public ViaClient Via { get; private set; }
        public bool IsScanning { get; private set; }
        /// <summary>Подключаться к найденной VIA-плате без участия пользователя.</summary>
        public bool AutoConnect { get; set; } = true;

        public event Action DevicesChanged;
        public event Action ConnectionChanged;

        /// <summary>Асинхронный поиск: перечисление HID и проверка поддержки VIA.</summary>
        public void Scan()
        {
            lock (_sync)
            {
                if (IsScanning) return;
                IsScanning = true;
            }
            Raise(DevicesChanged);

            var thread = new Thread(() =>
            {
                List<DetectedKeyboard> found;
                try { found = ScanCore(); }
                catch { found = new List<DetectedKeyboard>(); }

                DetectedKeyboard autoConnect = null;
                lock (_sync)
                {
                    // Демо-плата, если включена, остаётся в списке
                    var demo = Devices.FirstOrDefault(d => d.IsDemo);
                    if (demo != null) found.Insert(0, demo);
                    Devices = found;
                    IsScanning = false;
                    if (AutoConnect && Connected == null)
                        autoConnect = found.FirstOrDefault(d => d.ViaCapable && !d.IsDemo);
                }
                Raise(DevicesChanged);

                // Плата с поддержкой VIA подхватывается сама — как в VIA и Vial
                if (autoConnect != null) _toUi(() => Connect(autoConnect));
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private List<DetectedKeyboard> ScanCore()
        {
            var interfaces = HidEnumerator.Enumerate();
            var groups = new Dictionary<string, DetectedKeyboard>();

            foreach (var i in interfaces)
            {
                string key = i.VendorId.ToString("X4") + ":" + i.ProductId.ToString("X4");
                DetectedKeyboard device;
                if (!groups.TryGetValue(key, out device))
                {
                    device = new DetectedKeyboard { VendorId = i.VendorId, ProductId = i.ProductId };
                    groups[key] = device;
                }
                device.Interfaces.Add(i);
                if (i.IsRawHid && device.RawInterface == null) device.RawInterface = i;
            }

            var result = new List<DetectedKeyboard>();
            foreach (var device in groups.Values)
            {
                if (!DeviceCatalog.LooksLikeKeyboard(device.Interfaces)) continue;

                var named = device.Interfaces.FirstOrDefault(x => !string.IsNullOrEmpty(x.Product))
                            ?? device.Interfaces[0];
                device.Profile = DeviceCatalog.Resolve(device.VendorId, device.ProductId,
                    named.Product, named.Manufacturer);

                if (device.RawInterface != null) Probe(device);
                result.Add(device);
            }

            // Сначала платы с VIA, затем остальные — по имени
            result.Sort((a, b) =>
            {
                if (a.ViaCapable != b.ViaCapable) return a.ViaCapable ? -1 : 1;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture);
            });
            return result;
        }

        /// <summary>Короткий опрос платы: отвечает ли она по VIA и что умеет.</summary>
        private static void Probe(DetectedKeyboard device)
        {
            HidStream stream = null;
            try
            {
                stream = HidStream.Open(device.RawInterface);
                if (stream == null) return;
                var client = new ViaClient(new HidViaTransport(stream));
                int version = client.GetProtocolVersion();
                if (version < 0) return;

                client.ProtocolVersion = version;
                device.ViaCapable = true;
                device.ProtocolVersion = version;
                device.VialDetected = client.DetectVial();
                device.LayerCount = Math.Max(1, client.GetLayerCount());
                device.MacroCount = Math.Max(0, client.GetMacroCount());
                device.MacroBufferSize = Math.Max(0, client.GetMacroBufferSize());
            }
            catch
            {
                // недоступное устройство просто остаётся без поддержки VIA
            }
            finally
            {
                if (stream != null) stream.Dispose();
            }
        }

        /// <summary>Добавляет виртуальную плату для знакомства с интерфейсом без железа.</summary>
        public void EnableDemo()
        {
            lock (_sync)
            {
                if (Devices.Any(d => d.IsDemo)) return;
                var demo = new DetectedKeyboard
                {
                    IsDemo = true,
                    VendorId = 0x4B42,
                    ProductId = 0x8001,
                    ViaCapable = true,
                    ProtocolVersion = 9,
                    LayerCount = MockViaDevice.Layers,
                    MacroCount = MockViaDevice.MacroSlots,
                    MacroBufferSize = MockViaDevice.MacroBufferSize,
                    Profile = new BoardProfile
                    {
                        Model = "KBD8X MKIII",
                        Vendor = "KBDfans",
                        Form = FormFactor.Tkl,
                        MatrixRows = MockViaDevice.Rows,
                        MatrixCols = MockViaDevice.Cols,
                        FirstClassSupport = true
                    }
                };
                var list = new List<DetectedKeyboard>(Devices);
                list.Insert(0, demo);
                Devices = list;
            }
            Raise(DevicesChanged);
        }

        public bool Connect(DetectedKeyboard device)
        {
            Disconnect();
            if (device == null) return false;

            IViaTransport transport;
            if (device.IsDemo)
            {
                transport = new MockViaDevice();
            }
            else
            {
                if (device.RawInterface == null) return false;
                var stream = HidStream.Open(device.RawInterface);
                if (stream == null) return false;
                transport = new HidViaTransport(stream);
            }

            var client = new ViaClient(transport);
            int protocol = client.GetProtocolVersion();
            if (protocol < 0)
            {
                client.Dispose();
                return false;
            }
            client.ProtocolVersion = protocol;

            lock (_sync)
            {
                Via = client;
                Connected = device;
                device.LayerCount = Math.Max(1, client.GetLayerCount());
                device.MacroCount = Math.Max(0, client.GetMacroCount());
                device.MacroBufferSize = Math.Max(0, client.GetMacroBufferSize());
            }
            Raise(ConnectionChanged);
            return true;
        }

        public void Disconnect()
        {
            bool had;
            lock (_sync)
            {
                had = Via != null;
                if (Via != null) { Via.Dispose(); Via = null; }
                Connected = null;
            }
            if (had) Raise(ConnectionChanged);
        }

        private void Raise(Action handler)
        {
            if (handler == null) return;
            _toUi(() => handler());
        }
    }
}
