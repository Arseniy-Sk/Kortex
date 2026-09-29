using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kortex.Hid
{
    /// <summary>
    /// Открытый канал к raw-HID интерфейсу. Обмен идёт перекрывающимся вводом-выводом,
    /// чтобы чтение можно было прервать по таймауту, а не висеть вечно.
    /// </summary>
    public sealed class HidStream : IDisposable
    {
        private IntPtr _handle = HidNative.INVALID_HANDLE_VALUE;
        private IntPtr _readEvent = IntPtr.Zero;
        private IntPtr _writeEvent = IntPtr.Zero;
        private IntPtr _readOverlapped = IntPtr.Zero;
        private IntPtr _writeOverlapped = IntPtr.Zero;
        private readonly object _sync = new object();

        public HidInterfaceInfo Info { get; private set; }
        public int InputLength { get; private set; }
        public int OutputLength { get; private set; }
        public bool IsOpen => _handle != HidNative.INVALID_HANDLE_VALUE;

        private HidStream() { }

        public static HidStream Open(HidInterfaceInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.Path)) return null;

            IntPtr handle = HidNative.CreateFile(info.Path,
                HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero, HidNative.OPEN_EXISTING, HidNative.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == HidNative.INVALID_HANDLE_VALUE) return null;

            HidNative.HidD_SetNumInputBuffers(handle, 64);

            var stream = new HidStream
            {
                _handle = handle,
                Info = info,
                InputLength = info.InputReportLength > 0 ? info.InputReportLength : 33,
                OutputLength = info.OutputReportLength > 0 ? info.OutputReportLength : 33,
                _readEvent = HidNative.CreateEvent(IntPtr.Zero, true, false, null),
                _writeEvent = HidNative.CreateEvent(IntPtr.Zero, true, false, null)
            };
            stream._readOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
            stream._writeOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
            return stream;
        }

        /// <summary>Отправляет отчёт. Данные — полезная нагрузка без ведущего байта отчёта.</summary>
        public bool Write(byte[] payload, int timeoutMs = 500)
        {
            if (!IsOpen) return false;
            var buffer = new byte[OutputLength];
            buffer[0] = 0x00; // report id
            int copy = Math.Min(payload.Length, buffer.Length - 1);
            Array.Copy(payload, 0, buffer, 1, copy);

            lock (_sync)
            {
                PrepareOverlapped(_writeOverlapped, _writeEvent);
                int written;
                if (HidNative.WriteFile(_handle, buffer, buffer.Length, out written, _writeOverlapped))
                    return true;

                if (Marshal.GetLastWin32Error() != HidNative.ERROR_IO_PENDING) return false;
                uint wait = HidNative.WaitForSingleObject(_writeEvent, (uint)timeoutMs);
                if (wait != HidNative.WAIT_OBJECT_0)
                {
                    HidNative.CancelIo(_handle);
                    return false;
                }
                return HidNative.GetOverlappedResult(_handle, _writeOverlapped, out written, false);
            }
        }

        /// <summary>Читает отчёт. Возвращает полезную нагрузку без ведущего байта отчёта или null по таймауту.</summary>
        public byte[] Read(int timeoutMs = 500)
        {
            if (!IsOpen) return null;
            var buffer = new byte[InputLength];

            lock (_sync)
            {
                PrepareOverlapped(_readOverlapped, _readEvent);
                int read;
                if (!HidNative.ReadFile(_handle, buffer, buffer.Length, out read, _readOverlapped))
                {
                    if (Marshal.GetLastWin32Error() != HidNative.ERROR_IO_PENDING) return null;
                    uint wait = HidNative.WaitForSingleObject(_readEvent, (uint)timeoutMs);
                    if (wait != HidNative.WAIT_OBJECT_0)
                    {
                        HidNative.CancelIo(_handle);
                        return null;
                    }
                    if (!HidNative.GetOverlappedResult(_handle, _readOverlapped, out read, false)) return null;
                }

                if (read <= 1) return null;
                var payload = new byte[read - 1];
                Array.Copy(buffer, 1, payload, 0, payload.Length);
                return payload;
            }
        }

        private static void PrepareOverlapped(IntPtr overlapped, IntPtr eventHandle)
        {
            var ov = new NativeOverlapped { EventHandle = eventHandle };
            Marshal.StructureToPtr(ov, overlapped, false);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_handle != HidNative.INVALID_HANDLE_VALUE)
                {
                    HidNative.CancelIo(_handle);
                    HidNative.CloseHandle(_handle);
                    _handle = HidNative.INVALID_HANDLE_VALUE;
                }
                if (_readEvent != IntPtr.Zero) { HidNative.CloseHandle(_readEvent); _readEvent = IntPtr.Zero; }
                if (_writeEvent != IntPtr.Zero) { HidNative.CloseHandle(_writeEvent); _writeEvent = IntPtr.Zero; }
                if (_readOverlapped != IntPtr.Zero) { Marshal.FreeHGlobal(_readOverlapped); _readOverlapped = IntPtr.Zero; }
                if (_writeOverlapped != IntPtr.Zero) { Marshal.FreeHGlobal(_writeOverlapped); _writeOverlapped = IntPtr.Zero; }
            }
        }
    }
}
