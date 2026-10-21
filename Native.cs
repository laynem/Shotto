using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Shotto
{
    // Win32 interop. Everything here is in physical pixels (the process is per-monitor DPI aware).
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public Int32Rect ToInt32Rect() { return new Int32Rect(Left, Top, Right - Left, Bottom - Top); }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public int vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public int type;
            public KEYBDINPUT ki;
            long padding; // INPUT is sized for its largest union member (MOUSEINPUT).
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data);

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        public const int LLKHF_INJECTED = 0x10;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern int GetKeyNameText(int lParam, [Out] char[] buf, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc fn, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, int flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hMonitor, int type, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

        // Tells running apps (Explorer included) that a user setting changed.
        public static void BroadcastSettingChange()
        {
            IntPtr result;
            SendMessageTimeout(new IntPtr(0xFFFF), 0x1A /* WM_SETTINGCHANGE */, IntPtr.Zero, null, 2 /* SMTO_ABORTIFHUNG */, 1000, out result);
        }

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        public const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

        public static Int32Rect VirtualScreen
        {
            get { return new Int32Rect(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)); }
        }

        public static Point CursorPosition
        {
            get { POINT p; GetCursorPos(out p); return new Point(p.X, p.Y); }
        }

        public static List<MonitorInfo> GetMonitors()
        {
            var list = new List<MonitorInfo>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr h, IntPtr hdc, ref RECT rc, IntPtr d)
            {
                list.Add(GetMonitor(h));
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static MonitorInfo MonitorFromCursor()
        {
            POINT p;
            GetCursorPos(out p);
            return GetMonitor(MonitorFromPoint(p, 2 /* MONITOR_DEFAULTTONEAREST */));
        }

        static MonitorInfo GetMonitor(IntPtr h)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(h, ref mi);
            uint dx, dy;
            double scale = GetDpiForMonitor(h, 0, out dx, out dy) == 0 ? dx / 96.0 : 1.0;
            return new MonitorInfo
            {
                Bounds = mi.rcMonitor.ToInt32Rect(),
                WorkArea = mi.rcWork.ToInt32Rect(),
                Scale = scale,
                IsPrimary = (mi.dwFlags & 1) != 0
            };
        }

        // Positions a window in physical pixels. Run twice because moving onto a monitor with a different
        // DPI makes WPF resize the window to its own suggestion; the second call restores our bounds.
        public static void PlaceWindow(Window w, Int32Rect r, bool topmost)
        {
            IntPtr hwnd = new WindowInteropHelper(w).EnsureHandle();
            IntPtr after = topmost ? new IntPtr(-1) : IntPtr.Zero;
            const uint SWP_NOACTIVATE = 0x10, SWP_NOZORDER = 0x4;
            uint flags = SWP_NOACTIVATE | (topmost ? 0 : SWP_NOZORDER);
            SetWindowPos(hwnd, after, r.X, r.Y, r.Width, r.Height, flags);
            SetWindowPos(hwnd, after, r.X, r.Y, r.Width, r.Height, flags);
        }

        // Windows 11 rounded corners for borderless windows.
        public static void RoundCorners(Window w)
        {
            IntPtr hwnd = new WindowInteropHelper(w).EnsureHandle();
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
        }

        // Sends an unassigned key so a swallowed Win/Alt chord doesn't open Start or an app's menu bar.
        public static void SendDummyKey()
        {
            const ushort VK_NONAME = 0xE8;
            var inputs = new INPUT[2];
            inputs[0].type = inputs[1].type = 1;
            inputs[0].ki.wVk = inputs[1].ki.wVk = VK_NONAME;
            inputs[1].ki.dwFlags = 2; // KEYEVENTF_KEYUP
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static string KeyName(int vk)
        {
            uint scan = MapVirtualKey((uint)vk, 0);
            // Extended keys need bit 24 so e.g. arrows aren't reported as numpad keys.
            bool extended = (vk >= 0x21 && vk <= 0x2E) || (vk >= 0x5B && vk <= 0x5D) || vk == 0x6F || vk == 0x90;
            int lParam = (int)(scan << 16) | (extended ? 1 << 24 : 0);
            var buf = new char[64];
            int n = GetKeyNameText(lParam, buf, buf.Length);
            return n > 0 ? new string(buf, 0, n) : null;
        }
    }

    class MonitorInfo
    {
        public Int32Rect Bounds;
        public Int32Rect WorkArea;
        public double Scale;
        public bool IsPrimary;
    }
}
