using System;
using System.Diagnostics;
using Kortex.Interop;

namespace Kortex.Input
{
    public sealed class HookKeyEventArgs : EventArgs
    {
        public KeyId Key { get; internal set; }
        public int Vk { get; internal set; }
        public int ScanCode { get; internal set; }
        public bool Extended { get; internal set; }
        public bool IsDown { get; internal set; }
        /// <summary>Событие сгенерировано программно (в т.ч. нашим движком макросов).</summary>
        public bool Injected { get; internal set; }
        /// <summary>Событие отправлено именно этим приложением.</summary>
        public bool FromUs { get; internal set; }
        /// <summary>Автоповтор от удержания клавиши.</summary>
        public bool IsRepeat { get; internal set; }
        /// <summary>Метка времени от системы, мс.</summary>
        public uint Time { get; internal set; }
        /// <summary>Точное время получения события.</summary>
        public double TimestampMs { get; internal set; }
        /// <summary>Установите true, чтобы не пропускать событие дальше в систему.</summary>
        public bool Suppress { get; set; }
    }

    /// <summary>
    /// Глобальный низкоуровневый хук клавиатуры (WH_KEYBOARD_LL).
    /// Ставится на UI-поток — у него уже есть цикл сообщений.
    /// </summary>
    public sealed class GlobalKeyboardHook : IDisposable
    {
        /// <summary>Подпись в dwExtraInfo, по которой мы узнаём собственный ввод.</summary>
        public const long InjectionSignature = 0x4B54_5354; // "KTST"

        private IntPtr _hook = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc _proc;   // держим ссылку, иначе GC соберёт делегат
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly bool[] _down = new bool[512];

        public event EventHandler<HookKeyEventArgs> KeyEvent;

        public bool IsInstalled => _hook != IntPtr.Zero;

        public void Install()
        {
            if (_hook != IntPtr.Zero) return;
            _proc = HookProc;
            IntPtr hMod = NativeMethods.GetModuleHandle(null);
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
        }

        public void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _proc = null;
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode != NativeMethods.HC_ACTION)
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

            var data = (NativeMethods.KBDLLHOOKSTRUCT)System.Runtime.InteropServices
                .Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));

            int msg = wParam.ToInt32();
            bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
            int vk = (int)data.vkCode;
            bool ext = (data.flags & NativeMethods.LLKHF_EXTENDED) != 0;

            bool repeat = false;
            if (vk >= 0 && vk < _down.Length)
            {
                if (isDown)
                {
                    repeat = _down[vk];
                    _down[vk] = true;
                }
                else _down[vk] = false;
            }

            var args = new HookKeyEventArgs
            {
                Key = new KeyId(vk, ext),
                Vk = vk,
                ScanCode = (int)data.scanCode,
                Extended = ext,
                IsDown = isDown,
                Injected = (data.flags & NativeMethods.LLKHF_INJECTED) != 0,
                FromUs = data.dwExtraInfo.ToInt64() == InjectionSignature,
                IsRepeat = repeat,
                Time = data.time,
                TimestampMs = _clock.Elapsed.TotalMilliseconds
            };

            var handler = KeyEvent;
            if (handler != null)
            {
                try { handler(this, args); }
                catch { /* обработчик не должен ронять хук */ }
            }

            if (args.Suppress) return new IntPtr(1);
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose() => Uninstall();
    }
}
