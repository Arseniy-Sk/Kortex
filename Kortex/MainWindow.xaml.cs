using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Kortex.Interop;
using Kortex.Services;
using Kortex.ViewModels;

namespace Kortex
{
    public partial class MainWindow : Window
    {
        private ShellVm _vm;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _vm = new ShellVm();
            DataContext = _vm;

            AppServices.ShouldBlockSystemKeys = () =>
                AppServices.State.BlockSystemKeys && IsActive && _vm != null && _vm.IsTesterPage;

            if (Environment.GetEnvironmentVariable("KORTEX_PORTFOLIO") == "1")
                Portfolio.Run(this, _vm);
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            AppServices.ShouldBlockSystemKeys = null;
            if (_vm != null) _vm.Persist();
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            var source = HwndSource.FromHwnd(helper.Handle);
            if (source != null) source.AddHook(WndProc);

            FitToWorkArea(helper.Handle);

            int dark = 1;
            NativeMethods.DwmSetWindowAttribute(helper.Handle,
                NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int corner = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(helper.Handle,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }

        /// <summary>
        /// Ужимает окно под рабочую область монитора. На экранах с крупным масштабом
        /// окно иначе оказывается выше экрана, и нижние панели уезжают за край.
        /// </summary>
        private void FitToWorkArea(IntPtr handle)
        {
            IntPtr monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return;

            var info = new NativeMethods.MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            double scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
            double scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;

            double workWidth = (info.rcWork.right - info.rcWork.left) / scaleX;
            double workHeight = (info.rcWork.bottom - info.rcWork.top) / scaleY;
            double workLeft = info.rcWork.left / scaleX;
            double workTop = info.rcWork.top / scaleY;

            double width = Math.Min(Width, workWidth);
            double height = Math.Min(Height, workHeight);
            if (width >= Width && height >= Height) return;

            Width = width;
            Height = height;
            Left = workLeft + (workWidth - width) / 2;
            Top = workTop + (workHeight - height) / 2;
        }

        /// <summary>Не даём развёрнутому окну без рамки перекрывать панель задач.</summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != NativeMethods.WM_GETMINMAXINFO) return IntPtr.Zero;

            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return IntPtr.Zero;

            var info = new NativeMethods.MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

            var mmi = (NativeMethods.MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MINMAXINFO));
            mmi.ptMaxPosition.x = info.rcWork.left - info.rcMonitor.left;
            mmi.ptMaxPosition.y = info.rcWork.top - info.rcMonitor.top;
            mmi.ptMaxSize.x = info.rcWork.right - info.rcWork.left;
            mmi.ptMaxSize.y = info.rcWork.bottom - info.rcWork.top;
            Marshal.StructureToPtr(mmi, lParam, true);

            handled = true;
            return IntPtr.Zero;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void DeviceChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_vm != null) _vm.Page = ViewModels.Pages.Device;
        }
    }
}
