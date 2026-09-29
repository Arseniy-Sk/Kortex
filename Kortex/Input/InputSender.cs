using System;
using System.Collections.Generic;
using Kortex.Interop;

namespace Kortex.Input
{
    /// <summary>Отправка клавиатурных событий через SendInput с нашей подписью в dwExtraInfo.</summary>
    public static class InputSender
    {
        private static readonly IntPtr Signature = new IntPtr(GlobalKeyboardHook.InjectionSignature);

        public static void Send(KeyId key, bool down) => Send(key.Vk, key.Extended, down);

        public static void Send(int vk, bool extended, bool down)
        {
            var input = BuildKey(vk, extended, down);
            NativeMethods.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        public static void SendBatch(IEnumerable<Tuple<KeyId, bool>> events)
        {
            var list = new List<NativeMethods.INPUT>();
            foreach (var e in events)
                list.Add(BuildKey(e.Item1.Vk, e.Item1.Extended, e.Item2));
            if (list.Count == 0) return;
            NativeMethods.SendInput((uint)list.Count, list.ToArray(),
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        public static void Tap(KeyId key)
        {
            Send(key, true);
            Send(key, false);
        }

        /// <summary>Печать произвольного текста через Unicode-события (раскладка не важна).</summary>
        public static void SendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var list = new List<NativeMethods.INPUT>(text.Length * 2);
            foreach (char ch in text)
            {
                list.Add(BuildUnicode(ch, false));
                list.Add(BuildUnicode(ch, true));
            }
            NativeMethods.SendInput((uint)list.Count, list.ToArray(),
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        // ---------- Мышь ----------

        /// <summary>Щелчок указанной кнопкой мыши.</summary>
        public static void MouseClick(int button)
        {
            uint down, up;
            switch (button)
            {
                case 1: down = NativeMethods.MOUSEEVENTF_RIGHTDOWN; up = NativeMethods.MOUSEEVENTF_RIGHTUP; break;
                case 2: down = NativeMethods.MOUSEEVENTF_MIDDLEDOWN; up = NativeMethods.MOUSEEVENTF_MIDDLEUP; break;
                default: down = NativeMethods.MOUSEEVENTF_LEFTDOWN; up = NativeMethods.MOUSEEVENTF_LEFTUP; break;
            }
            SendMouse(down, 0, 0, 0);
            SendMouse(up, 0, 0, 0);
        }

        /// <summary>Переместить курсор: абсолютно в точку экрана или относительно текущего места.</summary>
        public static void MouseMove(int x, int y, bool absolute)
        {
            if (absolute)
            {
                NativeMethods.SetCursorPos(x, y);
                return;
            }
            NativeMethods.POINT current;
            if (NativeMethods.GetCursorPos(out current))
                NativeMethods.SetCursorPos(current.x + x, current.y + y);
            else
                SendMouse(NativeMethods.MOUSEEVENTF_MOVE, x, y, 0);
        }

        /// <summary>Прокрутка колесом: положительное значение — вверх.</summary>
        public static void Scroll(int notches)
        {
            if (notches == 0) return;
            SendMouse(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, notches * NativeMethods.WHEEL_DELTA);
        }

        private static void SendMouse(uint flags, int dx, int dy, int data)
        {
            var input = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                u = new NativeMethods.INPUTUNION
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = unchecked((uint)data),
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = Signature
                    }
                }
            };
            NativeMethods.SendInput(1, new[] { input },
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        private static NativeMethods.INPUT BuildKey(int vk, bool extended, bool down)
        {
            ushort scan = (ushort)NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_VSC);
            uint flags = 0;
            if (extended || KeyCodes.NeedsExtendedFlag(vk)) flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            if (!down) flags |= NativeMethods.KEYEVENTF_KEYUP;

            return new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = (ushort)vk,
                        wScan = scan,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = Signature
                    }
                }
            };
        }

        private static NativeMethods.INPUT BuildUnicode(char ch, bool up)
        {
            uint flags = NativeMethods.KEYEVENTF_UNICODE | (up ? NativeMethods.KEYEVENTF_KEYUP : 0);
            return new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = Signature
                    }
                }
            };
        }
    }
}
