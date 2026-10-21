using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Shotto
{
    class CaptureResult
    {
        public BitmapSource Image;
        public MonitorInfo Monitor; // Where the capture happened; the preview pops up there.
    }

    // Shows one frozen, dimmed overlay per monitor and lets the user drag out a region.
    // A click without dragging captures that whole monitor.
    class RegionSelector
    {
        readonly List<OverlayWindow> windows = new List<OverlayWindow>();
        readonly TaskCompletionSource<CaptureResult> done = new TaskCompletionSource<CaptureResult>();

        public static RegionSelector Active { get; private set; }

        public static Task<CaptureResult> Run(BitmapSource desktop, Int32Rect virtualScreen)
        {
            var selector = new RegionSelector();
            Active = selector;
            MonitorInfo underCursor = Native.MonitorFromCursor();
            foreach (MonitorInfo m in Native.GetMonitors())
            {
                var crop = new Int32Rect(m.Bounds.X - virtualScreen.X, m.Bounds.Y - virtualScreen.Y, m.Bounds.Width, m.Bounds.Height);
                MonitorInfo monitor = m;
                var w = new OverlayWindow(monitor, new CroppedBitmap(desktop, crop), r => selector.Finish(monitor, r));
                Native.PlaceWindow(w, m.Bounds, true);
                selector.windows.Add(w);
                w.Show();
                if (m.Bounds.Equals(underCursor.Bounds)) w.Activate();
            }
            return selector.done.Task;
        }

        public void Cancel() { Finish(null, null); }

        // area is in the monitor's physical pixels; null means cancelled.
        void Finish(MonitorInfo monitor, Int32Rect? area)
        {
            if (done.Task.IsCompleted) return;
            Active = null;
            CaptureResult result = null;
            if (area.HasValue)
            {
                OverlayWindow source = windows.Find(w => w.Monitor == monitor);
                result = new CaptureResult { Image = ImageIO.Crop(source.Screenshot, area.Value), Monitor = monitor };
            }
            foreach (OverlayWindow w in windows) w.Close();
            done.SetResult(result);
        }
    }

    class OverlayWindow : Window
    {
        public readonly MonitorInfo Monitor;
        public readonly BitmapSource Screenshot;
        readonly Action<Int32Rect?> finish;
        readonly Grid root = new Grid();
        readonly RectangleGeometry hole = new RectangleGeometry();
        readonly Rectangle outline;
        readonly Border sizeLabel, hint;
        readonly TextBlock sizeText;
        readonly Rectangle guideX, guideY;
        Point start;
        bool dragging;

        public OverlayWindow(MonitorInfo monitor, BitmapSource screenshot, Action<Int32Rect?> finish)
        {
            Monitor = monitor;
            Screenshot = screenshot;
            this.finish = finish;

            Style = new Style(typeof(Window)); // Opt out of the Fluent window style/backdrop.
            ThemeMode = ThemeMode.None;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Cursor = Crosshair(monitor.Scale);
            Background = Brushes.Black;
            UseLayoutRounding = true;
            Title = "Shotto capture";

            var dimGeometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
            dimGeometry.Children.Add(new RectangleGeometry(new Rect(-10, -10, 100000, 100000)));
            dimGeometry.Children.Add(hole);

            outline = new Rectangle
            {
                Stroke = new SolidColorBrush(AccentColor()),
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };

            sizeText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontFamily = UiFont };
            sizeLabel = Pill(sizeText, new Thickness(8, 3, 8, 3));
            sizeLabel.Visibility = Visibility.Collapsed;

            var hintText = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 14,
                FontFamily = UiFont,
                Text = "Drag to capture an area   ·   Click to capture this screen   ·   Esc to cancel"
            };
            hint = Pill(hintText, new Thickness(16, 9, 16, 9));
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.Margin = new Thickness(0, 28, 0, 0);

            // Full-length guides through the pointer so it's easy to find and line up on the dimmed screen.
            var guideBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
            guideX = new Rectangle { Height = 1, Fill = guideBrush, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            guideY = new Rectangle { Width = 1, Fill = guideBrush, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, Visibility = Visibility.Collapsed };

            root.Children.Add(new Image { Source = screenshot, Stretch = Stretch.Fill });
            root.Children.Add(new Path { Data = dimGeometry, Fill = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), IsHitTestVisible = false });
            root.Children.Add(guideX);
            root.Children.Add(guideY);
            root.Children.Add(outline);
            root.Children.Add(sizeLabel);
            root.Children.Add(hint);
            Content = root;
        }

        static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Text, Segoe UI");

        // The system cross cursor is thin and black, which vanishes on the dimmed overlay.
        // This one is white with a dark outline, sized for the monitor's scale.
        static Cursor Crosshair(double scale)
        {
            int size = (int)Math.Round(32 * scale);
            double c = size / 2.0, arm = size / 2.0 - 1, gap = 3 * scale;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                foreach (Pen pen in new[] { new Pen(Brushes.Black, 3 * scale), new Pen(Brushes.White, 1 * scale) })
                {
                    pen.StartLineCap = pen.EndLineCap = PenLineCap.Square;
                    dc.DrawLine(pen, new Point(c - arm, c), new Point(c - gap, c));
                    dc.DrawLine(pen, new Point(c + gap, c), new Point(c + arm, c));
                    dc.DrawLine(pen, new Point(c, c - arm), new Point(c, c - gap));
                    dc.DrawLine(pen, new Point(c, c + gap), new Point(c, c + arm));
                }
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var png = new System.IO.MemoryStream();
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            enc.Save(png);

            // .cur file: header, one directory entry carrying the hotspot, then the PNG image.
            var cur = new System.IO.MemoryStream();
            var w = new System.IO.BinaryWriter(cur);
            w.Write((short)0); w.Write((short)2); w.Write((short)1);
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)(size / 2)); w.Write((short)(size / 2));
            w.Write((int)png.Length); w.Write(22);
            w.Write(png.ToArray());
            cur.Position = 0;
            return new Cursor(cur, false);
        }

        void MoveGuides(Point p)
        {
            guideX.Visibility = guideY.Visibility = dragging ? Visibility.Collapsed : Visibility.Visible;
            guideX.Margin = new Thickness(0, Math.Floor(p.Y), 0, 0);
            guideY.Margin = new Thickness(Math.Floor(p.X), 0, 0, 0);
        }

        // Show the guides right away when the pointer starts on this monitor, before it moves.
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            Point p = Native.CursorPosition;
            Int32Rect b = Monitor.Bounds;
            if (p.X >= b.X && p.X < b.X + b.Width && p.Y >= b.Y && p.Y < b.Y + b.Height)
                MoveGuides(new Point((p.X - b.X) / PixelsPerDip, (p.Y - b.Y) / PixelsPerDip));
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            guideX.Visibility = guideY.Visibility = Visibility.Collapsed;
        }

        static Border Pill(UIElement child, Thickness padding)
        {
            return new Border
            {
                Child = child,
                Padding = padding,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };
        }

        static Color AccentColor()
        {
            object c = Application.Current.TryFindResource("SystemAccentColor");
            return c is Color ? (Color)c : Color.FromRgb(0, 120, 212);
        }

        double PixelsPerDip { get { return Screenshot.PixelWidth / root.ActualWidth; } }

        Rect Clamp(Rect r)
        {
            r.Intersect(new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            return r.IsEmpty ? new Rect() : r;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            start = e.GetPosition(root);
            dragging = true;
            CaptureMouse();
            hint.Visibility = Visibility.Collapsed;
            MoveGuides(start);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            MoveGuides(e.GetPosition(root));
            if (!dragging) return;
            Rect sel = Clamp(new Rect(start, e.GetPosition(root)));
            hole.Rect = sel;
            outline.Visibility = sizeLabel.Visibility = Visibility.Visible;
            outline.Margin = new Thickness(sel.X - 1, sel.Y - 1, 0, 0);
            outline.Width = sel.Width + 2;
            outline.Height = sel.Height + 2;

            double px = PixelsPerDip;
            sizeText.Text = Math.Round(sel.Width * px) + " × " + Math.Round(sel.Height * px);
            sizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double labelY = sel.Y - sizeLabel.DesiredSize.Height - 6;
            sizeLabel.Margin = new Thickness(sel.X, labelY >= 0 ? labelY : sel.Y + 6, 0, 0);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            ReleaseMouseCapture();

            double px = PixelsPerDip;
            Rect sel = Clamp(new Rect(start, e.GetPosition(root)));
            int x = (int)Math.Round(sel.X * px), y = (int)Math.Round(sel.Y * px);
            int w = Math.Min((int)Math.Round(sel.Width * px), Screenshot.PixelWidth - x);
            int h = Math.Min((int)Math.Round(sel.Height * px), Screenshot.PixelHeight - y);

            if (w < 4 || h < 4) finish(new Int32Rect(0, 0, Screenshot.PixelWidth, Screenshot.PixelHeight));
            else finish(new Int32Rect(x, y, w, h));
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            finish(null);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) finish(null);
        }
    }
}
