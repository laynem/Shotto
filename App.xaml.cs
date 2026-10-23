using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Shotto
{
    public partial class App : Application
    {
        const string InstanceName = "Local\\Shotto.SingleInstance";
        const string ShowEventName = "Local\\Shotto.ShowSettings";

        Mutex instanceMutex;
        EventWaitHandle showEvent;
        KeyboardHook hook;
        TrayIcon tray;
        SettingsWindow settingsWindow;
        bool capturing, quitting;

        public Settings Settings { get; private set; }
        public static new App Current { get { return (App)Application.Current; } }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            bool silent = e.Args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

            // A tray app has nowhere to show a crash, so record it. UI-thread errors (a failed capture,
            // a busy clipboard) are logged and survived rather than taking down the hotkeys.
            AppDomain.CurrentDomain.UnhandledException += (s, args) => LogCrash(args.ExceptionObject as Exception);
            DispatcherUnhandledException += (s, args) =>
            {
                LogCrash(args.Exception);
                args.Handled = true;
                if (tray != null) tray.ShowError("Something went wrong", args.Exception.Message);
            };

            // Second launch: ask the running instance to show its window, then quit.
            bool first;
            instanceMutex = new Mutex(true, InstanceName, out first);
            if (!first)
            {
                if (!silent)
                {
                    // We were just launched by the user, so we may hand foreground rights to the running instance.
                    Native.AllowSetForegroundWindow(-1 /* ASFW_ANY */);
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
                    catch (WaitHandleCannotBeOpenedException) { }
                }
                Shutdown();
                return;
            }

            Settings = Settings.Load();
            try { Settings.ApplyPrintScreenTakeover(); }
            catch (Exception) { } // Not worth failing startup over; the hook still owns Print Screen while we run.

            // Launch at sign-in is on by default. Rewriting the entry on every run keeps it pointing at
            // this exe if Shotto has been moved, and in sync with the silent-start option.
            try
            {
                if (Settings.IsFirstRun || Settings.LaunchAtStartup) Settings.ApplyStartup(true);
                if (Settings.IsFirstRun) Settings.Save();
            }
            catch (Exception) { }
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { Dispatcher.BeginInvoke(new Action(ShowSettings)); }, null, -1, false);

            hook = new KeyboardHook { KeyPressed = OnKeyPressed };
            tray = new TrayIcon(this);

            if (!silent) ShowSettings();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (hook != null) hook.Dispose();
            if (tray != null) tray.Dispose();
            base.OnExit(e);
        }

        static void LogCrash(Exception ex)
        {
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shotto");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception) { } // Never let logging itself crash the app.
        }

        public void Quit()
        {
            quitting = true;
            Shutdown();
        }

        public void ShowSettings()
        {
            if (settingsWindow == null)
            {
                settingsWindow = new SettingsWindow();
                settingsWindow.Closed += delegate { settingsWindow = null; };
            }
            settingsWindow.Show();
            if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
            settingsWindow.Activate();
        }

        public void SettingsHiddenToTray() { if (!quitting) tray.ShowStillRunningHint(); }

        public void StartRecording(Action<Hotkey> onKey) { hook.Recorder = onKey; }
        public void StopRecording() { hook.Recorder = null; }

        // Runs inside the keyboard hook: decide quickly, defer the work.
        bool OnKeyPressed(Hotkey key)
        {
            if (RegionSelector.Active != null)
            {
                if (key.Vk == 0x1B && !key.Ctrl && !key.Alt && !key.Shift && !key.Win)
                {
                    Dispatcher.BeginInvoke(new Action(RegionSelector.Active.Cancel));
                    return true;
                }
                return false;
            }

            foreach (CaptureMode mode in new[] { CaptureMode.Region, CaptureMode.Monitor, CaptureMode.AllScreens })
            {
                if (Settings.HotkeyFor(mode).Matches(key))
                {
                    Dispatcher.BeginInvoke(new Action(() => Capture(mode, false)));
                    return true;
                }
            }
            return false;
        }

        public async void Capture(CaptureMode mode, bool hideSettingsFirst)
        {
            if (capturing) return;
            capturing = true;
            bool restoreSettings = false;
            try
            {
                if (hideSettingsFirst && settingsWindow != null && settingsWindow.IsVisible)
                {
                    settingsWindow.Hide();
                    restoreSettings = true;
                    await Task.Delay(250); // Let the window finish fading out.
                }

                Int32Rect vs = Native.VirtualScreen;
                BitmapSource desktop = ImageIO.CaptureScreen(vs);
                CaptureResult result;
                if (mode == CaptureMode.Region)
                {
                    result = await RegionSelector.Run(desktop, vs);
                }
                else if (mode == CaptureMode.Monitor)
                {
                    MonitorInfo m = Native.MonitorFromCursor();
                    var area = new Int32Rect(m.Bounds.X - vs.X, m.Bounds.Y - vs.Y, m.Bounds.Width, m.Bounds.Height);
                    result = new CaptureResult { Image = ImageIO.Crop(desktop, area), Monitor = m };
                }
                else
                {
                    result = new CaptureResult { Image = desktop, Monitor = Native.MonitorFromCursor() };
                }

                if (restoreSettings) settingsWindow.Show();
                if (result != null) Deliver(result);
            }
            catch (Exception ex)
            {
                if (restoreSettings && settingsWindow != null) settingsWindow.Show();
                tray.ShowError("Capture failed", ex.Message);
            }
            finally
            {
                capturing = false;
            }
        }

        void Deliver(CaptureResult result)
        {
            bool? copied = Settings.CopyToClipboard ? ImageIO.CopyToClipboard(result.Image) : (bool?)null;
            string savedPath = null;
            if (Settings.AutoSave)
            {
                try { savedPath = ImageIO.SaveToFolder(result.Image, Settings); }
                catch (Exception ex) { tray.ShowError("Couldn't save screenshot", ex.Message); }
            }

            if (Settings.OpenEditorAfterCapture) OpenEditor(result.Image);
            else if (Settings.ShowPreview) PreviewToast.Present(result, copied, savedPath);
        }

        public void OpenEditor(BitmapSource image)
        {
            var editor = new EditorWindow(image);
            editor.Show();
            editor.Activate();
        }
    }
}
