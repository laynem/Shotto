using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shotto
{
    public partial class SettingsWindow : Window
    {
        readonly Settings settings = App.Current.Settings;
        Button recordingButton;
        bool loading = true;

        public SettingsWindow()
        {
            InitializeComponent();
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Shotto.ico"));
            Logo.Source = Icon;

            CopyToggle.IsChecked = settings.CopyToClipboard;
            PreviewToggle.IsChecked = settings.ShowPreview;
            EditorToggle.IsChecked = settings.OpenEditorAfterCapture;
            AutoSaveToggle.IsChecked = settings.AutoSave;
            StartupToggle.IsChecked = Settings.LaunchAtStartup;
            SilentToggle.IsChecked = settings.StartSilently;
            PrintScreenToggle.IsChecked = settings.TakeOverPrintScreen;
            FormatBox.SelectedIndex = settings.SaveFormat == "jpg" ? 1 : 0;
            FolderText.Text = settings.SaveDirectory;
            FolderText.ToolTip = settings.SaveDirectory;
            RefreshShortcuts();
            loading = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            StopRecording();
            base.OnClosing(e);
            App.Current.SettingsHiddenToTray();
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            StopRecording();
        }

        // ---- Capture tiles ----

        void CaptureRegion_Click(object sender, RoutedEventArgs e) { App.Current.Capture(CaptureMode.Region, true); }
        void CaptureMonitor_Click(object sender, RoutedEventArgs e) { App.Current.Capture(CaptureMode.Monitor, true); }
        void CaptureAll_Click(object sender, RoutedEventArgs e) { App.Current.Capture(CaptureMode.AllScreens, true); }

        // ---- Shortcuts ----

        static CaptureMode ModeOf(Button b) { return (CaptureMode)Enum.Parse(typeof(CaptureMode), (string)b.Tag); }

        static string NameOf(CaptureMode mode)
        {
            return mode == CaptureMode.Region ? "Capture an area" : mode == CaptureMode.Monitor ? "Capture this screen" : "Capture all screens";
        }

        void RefreshShortcuts()
        {
            foreach (Button b in new[] { RegionKey, MonitorKey, AllScreensKey })
                b.Content = ShortcutContent(settings.HotkeyFor(ModeOf(b)));
        }

        UIElement ShortcutContent(Hotkey key)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            if (key.IsEmpty)
            {
                panel.Children.Add(new TextBlock { Text = "Not set", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            }
            else
            {
                foreach (string part in key.Parts())
                {
                    var cap = new Border { Style = (Style)FindResource("Keycap") };
                    cap.Child = new TextBlock
                    {
                        Text = part,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = (Brush)FindResource("TextOnAccentFillColorPrimaryBrush")
                    };
                    panel.Children.Add(cap);
                }
            }
            panel.Children.Add(new TextBlock { Style = (Style)FindResource("Glyph"), Text = "", FontSize = 14, Margin = new Thickness(6, 0, 0, 0) });
            return panel;
        }

        void Shortcut_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            bool wasRecording = recordingButton == button;
            StopRecording();
            if (wasRecording) return;

            recordingButton = button;
            button.Content = new TextBlock { Text = "Press a shortcut…", Foreground = (Brush)FindResource("AccentTextFillColorPrimaryBrush") };
            ShortcutMessage.Text = "Press the keys you want to use. Esc cancels, Backspace removes the shortcut.";
            App.Current.StartRecording(key => OnRecorded(button, key));
        }

        void OnRecorded(Button button, Hotkey key)
        {
            if (recordingButton != button) return;
            recordingButton = null;
            CaptureMode mode = ModeOf(button);
            bool bare = !key.Ctrl && !key.Alt && !key.Shift && !key.Win;

            if (bare && key.Vk == 0x1B)
            {
                ShortcutMessage.Text = "Select a shortcut to change it.";
            }
            else if (bare && (key.Vk == 0x08 || key.Vk == 0x2E))
            {
                settings.SetHotkey(mode, new Hotkey());
                settings.Save();
                ShortcutMessage.Text = NameOf(mode) + " no longer has a shortcut.";
            }
            else if (!key.IsValid)
            {
                ShortcutMessage.Text = key + " on its own would get in the way of typing. Add Ctrl, Alt, Shift or Win, or use Print Screen or an F key.";
            }
            else
            {
                CaptureMode? clash = null;
                foreach (CaptureMode other in new[] { CaptureMode.Region, CaptureMode.Monitor, CaptureMode.AllScreens })
                    if (other != mode && settings.HotkeyFor(other).Matches(key)) clash = other;

                if (clash.HasValue)
                {
                    ShortcutMessage.Text = key + " is already used for " + NameOf(clash.Value).ToLowerInvariant() + ".";
                }
                else
                {
                    settings.SetHotkey(mode, key);
                    settings.Save();
                    ShortcutMessage.Text = NameOf(mode) + " is now " + key + ".";
                }
            }
            RefreshShortcuts();
        }

        void StopRecording()
        {
            if (recordingButton == null) return;
            recordingButton = null;
            App.Current.StopRecording();
            RefreshShortcuts();
            ShortcutMessage.Text = "Select a shortcut to change it.";
        }

        void ResetShortcuts_Click(object sender, RoutedEventArgs e)
        {
            StopRecording();
            settings.ResetHotkeys();
            settings.Save();
            RefreshShortcuts();
            ShortcutMessage.Text = "Shortcuts restored to their defaults.";
        }

        // ---- Toggles ----

        void Toggle_Click(object sender, RoutedEventArgs e)
        {
            settings.CopyToClipboard = CopyToggle.IsChecked == true;
            settings.ShowPreview = PreviewToggle.IsChecked == true;
            settings.OpenEditorAfterCapture = EditorToggle.IsChecked == true;
            settings.AutoSave = AutoSaveToggle.IsChecked == true;
            settings.Save();
        }

        void Startup_Click(object sender, RoutedEventArgs e)
        {
            settings.StartSilently = SilentToggle.IsChecked == true;
            settings.Save();
            try
            {
                settings.ApplyStartup(StartupToggle.IsChecked == true);
            }
            catch (Exception ex)
            {
                StartupToggle.IsChecked = Settings.LaunchAtStartup;
                MessageBox.Show(this, "Couldn't change the startup setting.\n\n" + ex.Message, "Shotto", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void PrintScreen_Click(object sender, RoutedEventArgs e)
        {
            settings.TakeOverPrintScreen = PrintScreenToggle.IsChecked == true;
            try
            {
                settings.ApplyPrintScreenTakeover();
                settings.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't change the Windows Print Screen setting.\n\n" + ex.Message, "Shotto", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---- Saving ----

        void ChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose where Shotto saves screenshots",
                InitialDirectory = settings.SaveDirectory
            };
            if (dlg.ShowDialog(this) != true) return;
            settings.SaveDirectory = dlg.FolderName;
            settings.Save();
            FolderText.Text = settings.SaveDirectory;
            FolderText.ToolTip = settings.SaveDirectory;
        }

        void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            ImageIO.OpenFolder(settings.SaveDirectory);
        }

        void Format_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (loading) return;
            settings.SaveFormat = (string)((ComboBoxItem)FormatBox.SelectedItem).Tag;
            settings.Save();
        }

        void Exit_Click(object sender, RoutedEventArgs e)
        {
            App.Current.Quit();
        }
    }
}
