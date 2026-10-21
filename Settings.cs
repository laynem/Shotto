using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using Microsoft.Win32;

namespace Shotto
{
    public class Settings
    {
        public Hotkey RegionHotkey { get; set; }
        public Hotkey MonitorHotkey { get; set; }
        public Hotkey AllScreensHotkey { get; set; }

        public bool CopyToClipboard { get; set; } = true;
        public bool ShowPreview { get; set; } = true;
        public bool OpenEditorAfterCapture { get; set; }
        public bool AutoSave { get; set; }

        public string SaveDirectory { get; set; }
        public string SaveFormat { get; set; } = "png";

        public bool StartSilently { get; set; } = true;

        public bool TakeOverPrintScreen { get; set; } = true;
        // The Windows value Shotto replaced, so turning the option off can put it back. Null = never changed.
        public int? OriginalSnippingPrintScreen { get; set; }

        // Editor tool state, remembered between sessions.
        public string PenColor { get; set; } = "#E81123";
        public string HighlighterColor { get; set; } = "#FFF100";
        public double PenSize { get; set; } = 4;
        public double HighlighterSize { get; set; } = 18;

        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shotto");
        static readonly string FilePath = Path.Combine(Dir, "settings.json");

        public static Settings Load()
        {
            Settings s = null;
            try
            {
                if (File.Exists(FilePath)) s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
            }
            catch (Exception) { }
            s = s ?? new Settings();
            s.FillDefaults();
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        void FillDefaults()
        {
            if (RegionHotkey == null) RegionHotkey = DefaultRegion;
            if (MonitorHotkey == null) MonitorHotkey = DefaultMonitor;
            if (AllScreensHotkey == null) AllScreensHotkey = DefaultAllScreens;
            if (string.IsNullOrWhiteSpace(SaveDirectory)) SaveDirectory = DownloadsFolder;
        }

        public void ResetHotkeys()
        {
            RegionHotkey = DefaultRegion;
            MonitorHotkey = DefaultMonitor;
            AllScreensHotkey = DefaultAllScreens;
        }

        public static Hotkey DefaultRegion { get { return new Hotkey(Hotkey.VK_SNAPSHOT); } }
        public static Hotkey DefaultMonitor { get { return new Hotkey(Hotkey.VK_SNAPSHOT, shift: true); } }
        public static Hotkey DefaultAllScreens { get { return new Hotkey(Hotkey.VK_SNAPSHOT, ctrl: true); } }

        public Hotkey HotkeyFor(CaptureMode mode)
        {
            return mode == CaptureMode.Region ? RegionHotkey : mode == CaptureMode.Monitor ? MonitorHotkey : AllScreensHotkey;
        }

        public void SetHotkey(CaptureMode mode, Hotkey key)
        {
            if (mode == CaptureMode.Region) RegionHotkey = key;
            else if (mode == CaptureMode.Monitor) MonitorHotkey = key;
            else AllScreensHotkey = key;
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch (Exception) { return fallback; }
        }

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        public static string DownloadsFolder
        {
            get
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                {
                    string path = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    return path;
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        // Windows 11's "Use the Print screen key to open screen capture". While Shotto runs its hook wins anyway;
        // turning this off keeps Print Screen from opening Snipping Tool when Shotto isn't running.
        const string KeyboardKey = @"Control Panel\Keyboard";
        const string SnippingValue = "PrintScreenKeyForSnippingEnabled";

        static int SnippingPrintScreen
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(KeyboardKey))
                {
                    object v = k == null ? null : k.GetValue(SnippingValue);
                    return v is int ? (int)v : 1; // Missing means the Windows default, which is on.
                }
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(KeyboardKey))
                    k.SetValue(SnippingValue, value, RegistryValueKind.DWord);
                Native.BroadcastSettingChange();
            }
        }

        // Turns the Windows setting off when TakeOverPrintScreen is on, or restores what it was when it's off.
        public void ApplyPrintScreenTakeover()
        {
            if (TakeOverPrintScreen)
            {
                int current = SnippingPrintScreen;
                if (current == 0) return;
                if (OriginalSnippingPrintScreen == null) OriginalSnippingPrintScreen = current;
                SnippingPrintScreen = 0;
            }
            else
            {
                if (OriginalSnippingPrintScreen == null) return;
                SnippingPrintScreen = OriginalSnippingPrintScreen.Value;
                OriginalSnippingPrintScreen = null;
            }
            Save();
        }

        // Launch at sign-in lives in the registry rather than the settings file so it always reflects reality.
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool LaunchAtStartup
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("Shotto") != null;
            }
        }

        public void ApplyStartup(bool enabled)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue("Shotto", "\"" + Environment.ProcessPath + "\"" + (StartSilently ? " --silent" : ""));
                else k.DeleteValue("Shotto", false);
            }
        }
    }
}
