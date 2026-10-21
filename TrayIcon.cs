using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Shotto
{
    // Notification-area icon. The menu is a WPF ContextMenu so it picks up the Windows 11 (Fluent) styling.
    sealed class TrayIcon : IDisposable
    {
        readonly App app;
        readonly Forms.NotifyIcon icon;
        readonly ContextMenu menu;
        Window menuHost;
        bool hintShown;

        public TrayIcon(App app)
        {
            this.app = app;
            System.Drawing.Icon ico;
            using (var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Shotto.ico")).Stream)
                ico = new System.Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);

            icon = new Forms.NotifyIcon { Icon = ico, Text = "Shotto", Visible = true };
            icon.MouseClick += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) app.ShowSettings();
                else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
            };

            menu = new ContextMenu();
            menu.Items.Add(Item("Capture an area", "", () => app.Capture(CaptureMode.Region, false)));
            menu.Items.Add(Item("Capture this screen", "", () => app.Capture(CaptureMode.Monitor, false)));
            menu.Items.Add(Item("Capture all screens", "", () => app.Capture(CaptureMode.AllScreens, false)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Open Shotto", "", app.ShowSettings));
            menu.Items.Add(Item("Exit", "", app.Quit));
            menu.Closed += delegate { if (menuHost != null) menuHost.Hide(); };
        }

        static MenuItem Item(string text, string glyph, Action click)
        {
            var item = new MenuItem
            {
                Header = text,
                Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 16 }
            };
            item.Click += delegate { click(); };
            return item;
        }

        void ShowMenu()
        {
            // A popup only dismisses on outside clicks if our process owns the foreground window,
            // so park an invisible window in front first.
            if (menuHost == null)
            {
                menuHost = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    Width = 0,
                    Height = 0,
                    Left = -32000,
                    Top = -32000,
                    Opacity = 0
                };
            }
            menuHost.Show();
            Native.SetForegroundWindow(new WindowInteropHelper(menuHost).Handle);
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        public void ShowError(string title, string message)
        {
            icon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Error);
        }

        // Shown once, the first time the window is closed, so people know the app is still around.
        public void ShowStillRunningHint()
        {
            if (hintShown) return;
            hintShown = true;
            icon.ShowBalloonTip(5000, "Shotto is still running",
                "Press " + app.Settings.RegionHotkey + " to capture. Find Shotto in the notification area.", Forms.ToolTipIcon.None);
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
        }
    }
}
