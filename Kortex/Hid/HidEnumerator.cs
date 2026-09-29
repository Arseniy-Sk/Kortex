using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Kortex.Hid
{
    /// <summary>Описание одного HID-интерфейса устройства.</summary>
    public sealed class HidInterfaceInfo
    {
        public string Path { get; set; }
        public ushort VendorId { get; set; }
        public ushort ProductId { get; set; }
        public ushort Version { get; set; }
        public ushort UsagePage { get; set; }
        public ushort Usage { get; set; }
        public int InputReportLength { get; set; }
        public int OutputReportLength { get; set; }
        public string Manufacturer { get; set; }
        public string Product { get; set; }
        public string Serial { get; set; }

        public string VidPid => "VID " + VendorId.ToString("X4") + " · PID " + ProductId.ToString("X4");
        public string UsageText => "0x" + UsagePage.ToString("X4") + " / 0x" + Usage.ToString("X2");

        /// <summary>Интерфейс сырых отчётов, на котором работают VIA и Vial.</summary>
        public bool IsRawHid => UsagePage == 0xFF60 && Usage == 0x61;

        /// <summary>Обычный клавиатурный интерфейс (Windows не отдаёт его на чтение).</summary>
        public bool IsKeyboard => UsagePage == 0x01 && (Usage == 0x06 || Usage == 0x07);
    }

    /// <summary>Перечисление HID-устройств системы.</summary>
    public static class HidEnumerator
    {
        public static List<HidInterfaceInfo> Enumerate()
        {
            var result = new List<HidInterfaceInfo>();
            Guid hidGuid;
            HidNative.HidD_GetHidGuid(out hidGuid);

            IntPtr set = HidNative.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);
            if (set == HidNative.INVALID_HANDLE_VALUE) return result;

            try
            {
                var did = new HidNative.SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(typeof(HidNative.SP_DEVICE_INTERFACE_DATA));

                for (int index = 0; ; index++)
                {
                    if (!HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref did))
                        break;

                    string path = GetDevicePath(set, ref did);
                    if (string.IsNullOrEmpty(path)) continue;

                    var info = Describe(path);
                    if (info != null) result.Add(info);
                }
            }
            finally
            {
                HidNative.SetupDiDestroyDeviceInfoList(set);
            }
            return result;
        }

        private static string GetDevicePath(IntPtr set, ref HidNative.SP_DEVICE_INTERFACE_DATA did)
        {
            int required;
            HidNative.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out required, IntPtr.Zero);
            if (required <= 0) return null;

            IntPtr buffer = Marshal.AllocHGlobal(required);
            try
            {
                // cbSize структуры SP_DEVICE_INTERFACE_DETAIL_DATA: 8 на x64, 6 на x86
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                int unused;
                if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref did, buffer, required, out unused, IntPtr.Zero))
                    return null;
                return Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>Читает атрибуты интерфейса. Открытие без прав доступа работает и для клавиатур.</summary>
        private static HidInterfaceInfo Describe(string path)
        {
            IntPtr handle = HidNative.CreateFile(path, 0,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == HidNative.INVALID_HANDLE_VALUE) return null;

            try
            {
                var attributes = new HidNative.HIDD_ATTRIBUTES();
                attributes.Size = Marshal.SizeOf(typeof(HidNative.HIDD_ATTRIBUTES));
                if (!HidNative.HidD_GetAttributes(handle, ref attributes)) return null;

                var info = new HidInterfaceInfo
                {
                    Path = path,
                    VendorId = attributes.VendorID,
                    ProductId = attributes.ProductID,
                    Version = attributes.VersionNumber,
                    Manufacturer = ReadString(handle, HidNative.HidD_GetManufacturerString),
                    Product = ReadString(handle, HidNative.HidD_GetProductString),
                    Serial = ReadString(handle, HidNative.HidD_GetSerialNumberString)
                };

                IntPtr preparsed;
                if (HidNative.HidD_GetPreparsedData(handle, out preparsed))
                {
                    try
                    {
                        var caps = new HidNative.HIDP_CAPS();
                        if (HidNative.HidP_GetCaps(preparsed, ref caps) == 0x00110000) // HIDP_STATUS_SUCCESS
                        {
                            info.UsagePage = caps.UsagePage;
                            info.Usage = caps.Usage;
                            info.InputReportLength = caps.InputReportByteLength;
                            info.OutputReportLength = caps.OutputReportByteLength;
                        }
                    }
                    finally
                    {
                        HidNative.HidD_FreePreparsedData(preparsed);
                    }
                }
                return info;
            }
            catch
            {
                return null;
            }
            finally
            {
                HidNative.CloseHandle(handle);
            }
        }

        private delegate bool StringGetter(IntPtr device, byte[] buffer, int length);

        private static string ReadString(IntPtr handle, StringGetter getter)
        {
            var buffer = new byte[254];
            try
            {
                if (!getter(handle, buffer, buffer.Length)) return null;
                string s = Encoding.Unicode.GetString(buffer);
                int zero = s.IndexOf('\0');
                if (zero >= 0) s = s.Substring(0, zero);
                s = s.Trim();
                return s.Length == 0 ? null : s;
            }
            catch
            {
                return null;
            }
        }
    }
}
