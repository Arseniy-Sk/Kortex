using System;
using Kortex.Devices;
using Kortex.Input;
using Kortex.Localization;
using Kortex.Models;

namespace Kortex.Services
{
    /// <summary>Единая точка сборки сервисов приложения.</summary>
    public static class AppServices
    {
        public static AppState State { get; private set; }
        public static GlobalKeyboardHook Hook { get; private set; }
        public static RemapEngine Remap { get; private set; }
        public static MacroRecorder Recorder { get; private set; }
        public static DeviceService Devices { get; private set; }

        /// <summary>Все события клавиатуры — для тестера и индикаторов.</summary>
        public static event EventHandler<HookKeyEventArgs> KeyObserved;

        /// <summary>Условие блокировки системных сочетаний (задаётся окном).</summary>
        public static Func<bool> ShouldBlockSystemKeys;

        public static event Action StateChanged;

        public static void Init()
        {
            State = ProfileStore.Load();
            Loc.I.Language = State.Language;
            Devices = new DeviceService(action =>
            {
                var app = System.Windows.Application.Current;
                if (app == null) action();
                else app.Dispatcher.BeginInvoke(action);
            });
            Remap = new RemapEngine { Enabled = State.RemapEnabled };
            Remap.Load(State.Active);
            Recorder = new MacroRecorder();

            Hook = new GlobalKeyboardHook();
            Hook.KeyEvent += OnHookKey;
            Hook.Install();
        }

        public static void Shutdown()
        {
            if (Devices != null) Devices.Disconnect();

            if (Hook != null)
            {
                Hook.KeyEvent -= OnHookKey;
                Hook.Dispose();
                Hook = null;
            }
            Save();
        }

        public static void Save()
        {
            if (State == null) return;
            State.RemapEnabled = Remap != null && Remap.Enabled;
            State.Language = Loc.I.Language;
            ProfileStore.Save(State);
        }

        public static void ReloadProfile()
        {
            if (Remap != null) Remap.Load(State.Active);
            var h = StateChanged;
            if (h != null) h();
        }

        private static void OnHookKey(object sender, HookKeyEventArgs e)
        {
            bool handled = false;

            if (Recorder != null && Recorder.IsRecording && !e.FromUs)
            {
                Recorder.Feed(e);
                handled = true;
            }
            else if (Remap != null && Remap.Process(e))
            {
                handled = true;
            }

            var observers = KeyObserved;
            if (observers != null) observers(sender, e);

            if (handled) e.Suppress = true;
            else if (!e.FromUs && IsSystemCombo(e) && ShouldBlockSystemKeys != null && ShouldBlockSystemKeys())
                e.Suppress = true;
        }

        /// <summary>Системные сочетания, мешающие тестированию (Win, Menu, PrtSc, Alt+Tab, Ctrl+Esc).</summary>
        private static bool IsSystemCombo(HookKeyEventArgs e)
        {
            switch (e.Vk)
            {
                case KeyCodes.VK_LWIN:
                case KeyCodes.VK_RWIN:
                case KeyCodes.VK_APPS:
                case KeyCodes.VK_SNAPSHOT:
                    return true;
            }

            bool alt = (Interop.NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool ctrl = (Interop.NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;
            if (alt && (e.Vk == KeyCodes.VK_TAB || e.Vk == KeyCodes.VK_ESCAPE)) return true;
            if (ctrl && e.Vk == KeyCodes.VK_ESCAPE) return true;
            return false;
        }
    }
}
