using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace Shotto
{
    public enum CaptureMode { Region, Monitor, AllScreens }

    public class Hotkey
    {
        public int Vk { get; set; }
        public bool Ctrl { get; set; }
        public bool Shift { get; set; }
        public bool Alt { get; set; }
        public bool Win { get; set; }

        public Hotkey() { }
        public Hotkey(int vk, bool ctrl = false, bool shift = false, bool alt = false, bool win = false)
        {
            Vk = vk; Ctrl = ctrl; Shift = shift; Alt = alt; Win = win;
        }

        [JsonIgnore] public bool IsEmpty { get { return Vk == 0; } }
        bool HasModifier { get { return Ctrl || Shift || Alt || Win; } }

        // Keys that are safe to bind on their own without hijacking normal typing.
        [JsonIgnore] public bool IsValid
        {
            get
            {
                if (IsEmpty) return false;
                if (HasModifier) return true;
                return Vk == VK_SNAPSHOT || Vk == 0x13 /* Pause */ || Vk == 0x91 /* Scroll Lock */
                    || (Vk >= 0x70 && Vk <= 0x87) /* F1-F24 */;
            }
        }

        public bool Matches(Hotkey other)
        {
            return other != null && Vk == other.Vk && Ctrl == other.Ctrl && Shift == other.Shift
                && Alt == other.Alt && Win == other.Win;
        }

        public const int VK_SNAPSHOT = 0x2C;

        public IEnumerable<string> Parts()
        {
            if (Win) yield return "Win";
            if (Ctrl) yield return "Ctrl";
            if (Alt) yield return "Alt";
            if (Shift) yield return "Shift";
            if (!IsEmpty) yield return KeyLabel(Vk);
        }

        public override string ToString()
        {
            return IsEmpty ? "None" : string.Join(" + ", Parts());
        }

        static string KeyLabel(int vk)
        {
            switch (vk)
            {
                case VK_SNAPSHOT: return "Print Screen";
                case 0x13: return "Pause";
                case 0x91: return "Scroll Lock";
                case 0x20: return "Space";
            }
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return ((char)vk).ToString();
            return Native.KeyName(vk) ?? ("Key " + vk);
        }

        public Hotkey Clone() { return (Hotkey)MemberwiseClone(); }
    }

    // Global hotkeys via a low-level keyboard hook rather than RegisterHotKey: it can take Print Screen
    // away from the Windows Snipping Tool and lets the settings page record any chord, Print Screen included.
    sealed class KeyboardHook : IDisposable
    {
        readonly Native.LowLevelKeyboardProc proc;
        readonly Dispatcher dispatcher;
        readonly HashSet<int> swallowedDown = new HashSet<int>();
        IntPtr hook;

        // Return true to swallow the key. Runs inside the hook, so handlers must be quick.
        public Func<Hotkey, bool> KeyPressed;
        // When set, the next non-modifier key press is delivered here and swallowed (used to record shortcuts).
        public Action<Hotkey> Recorder;

        public KeyboardHook()
        {
            proc = HookProc;
            dispatcher = Dispatcher.CurrentDispatcher;
            hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new InvalidOperationException("Could not install keyboard hook (" + Marshal.GetLastWin32Error() + ").");
        }

        static bool IsModifier(int vk)
        {
            return (vk >= 0x10 && vk <= 0x12) || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
        }

        static bool Down(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

        IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();
                int vk = info.vkCode;
                bool isDown = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                bool isUp = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;

                if (isUp && swallowedDown.Remove(vk)) return new IntPtr(1);

                if (isDown && !IsModifier(vk) && vk != 0xE8)
                {
                    var key = new Hotkey(vk, Down(0x11), Down(0x10), Down(0x12), Down(0x5B) || Down(0x5C));
                    bool swallow = false;
                    if (Recorder != null)
                    {
                        Action<Hotkey> rec = Recorder;
                        Recorder = null;
                        dispatcher.BeginInvoke(rec, key);
                        swallow = true;
                    }
                    else if (KeyPressed != null)
                    {
                        swallow = KeyPressed(key);
                    }

                    if (swallow)
                    {
                        swallowedDown.Add(vk);
                        if (key.Win || key.Alt) Native.SendDummyKey();
                        return new IntPtr(1);
                    }
                }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
    }
}
