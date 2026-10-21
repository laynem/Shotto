using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Shotto
{
    // Snipping Tool-style preview that pops up in the corner after a capture.
    public partial class PreviewToast : Window
    {
        static PreviewToast current;
        readonly BitmapSource image;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };

        // copied: true/false for the clipboard result, null when copying is turned off.
        PreviewToast(BitmapSource image, bool? copied, string savedPath)
        {
            InitializeComponent();
            this.image = image;
            DataContext = image;
            AppIcon.Source = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Shotto.ico"));
            if (copied == false)
            {
                Headline.Text = "Couldn't copy to clipboard";
                Detail.Text = "Another app is using the clipboard. Open the editor to copy again.";
            }
            else
            {
                if (copied == null) Headline.Text = "Screenshot captured";
                if (savedPath != null) Detail.Text = "Saved as " + Path.GetFileName(savedPath);
            }
            timer.Tick += delegate { Close(); };
        }

        internal static void Present(CaptureResult result, bool? copied, string savedPath)
        {
            if (current != null) current.Close();
            current = new PreviewToast(result.Image, copied, savedPath);
            current.Closed += (s, e) => { if (current == s) current = null; };

            MonitorInfo m = result.Monitor;
            double scale = m.Scale;
            int w = (int)Math.Round(current.Width * scale), h = (int)Math.Round(current.Height * scale);
            int margin = (int)Math.Round(12 * scale);
            Native.PlaceWindow(current, new Int32Rect(m.WorkArea.X + m.WorkArea.Width - w - margin, m.WorkArea.Y + m.WorkArea.Height - h - margin, w, h), true);
            Native.RoundCorners(current);
            current.Show();
            current.timer.Start();
        }

        protected override void OnMouseEnter(MouseEventArgs e) { timer.Stop(); }
        protected override void OnMouseLeave(MouseEventArgs e) { timer.Start(); }

        void Dismiss_Click(object sender, RoutedEventArgs e) { Close(); }

        void Edit_Click(object sender, RoutedEventArgs e)
        {
            Close();
            App.Current.OpenEditor(image);
        }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            timer.Stop();
            try
            {
                string path = ImageIO.SaveToFolder(image, App.Current.Settings);
                Headline.Text = "Saved";
                Detail.Text = path;
                Detail.ToolTip = path;
            }
            catch (Exception ex)
            {
                Headline.Text = "Couldn't save";
                Detail.Text = ex.Message;
            }
            timer.Start();
        }

        void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            timer.Stop();
            Topmost = false;
            string path = ImageIO.SaveAs(image, App.Current.Settings, this);
            if (path != null)
            {
                Headline.Text = "Saved";
                Detail.Text = path;
                Detail.ToolTip = path;
            }
            Topmost = true;
            timer.Start();
        }
    }
}
