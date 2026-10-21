using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Shotto
{
    public partial class EditorWindow : Window
    {
        enum Tool { Select, Pen, Highlighter, Rectangle, Ellipse, Arrow }

        // One undoable step: markup added, or everything erased at once.
        class Edit
        {
            public List<UIElement> Added = new List<UIElement>();
            public List<UIElement> Removed = new List<UIElement>();
        }

        const double HighlighterOpacity = 0.45;
        static readonly double[] PresetSizes = { 2, 4, 8, 12, 20, 32 };
        static readonly double[] ZoomSteps = { 0.1, 0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.25, 1.5, 2, 3, 4, 6, 8 };

        readonly Settings settings = App.Current.Settings;
        readonly BitmapSource source;
        readonly Stack<Edit> undo = new Stack<Edit>(), redo = new Stack<Edit>();
        readonly ColorPicker picker = new ColorPicker();

        Tool tool = Tool.Select;
        Color penColor, highlighterColor;
        UIElement drawing;
        Point dragStart, panOffset;
        bool panning, dirty, fitMode = true;
        DateTime popupClosedAt;
        string lastSavedPath;

        public EditorWindow(BitmapSource image)
        {
            InitializeComponent();
            source = image;
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Shotto.ico"));

            Photo.Source = image;
            Photo.Width = Ink.Width = image.PixelWidth;
            Photo.Height = Ink.Height = image.PixelHeight;
            SizeText.Text = image.PixelWidth + " × " + image.PixelHeight + " px";

            penColor = Settings.ParseColor(settings.PenColor, Colors.Red);
            highlighterColor = Settings.ParseColor(settings.HighlighterColor, Colors.Yellow);
            picker.ColorChanged += OnPickerColor;
            ColorHost.Content = picker;

            foreach (double size in PresetSizes)
            {
                double s = size;
                var b = new Button { Style = (Style)FindResource("SubtleButton"), Width = 36, ToolTip = s + " px", Margin = new Thickness(0, 0, 4, 0) };
                var dot = new Ellipse { Width = Math.Min(s, 22), Height = Math.Min(s, 22) };
                dot.SetResourceReference(Shape.FillProperty, "TextFillColorPrimaryBrush");
                b.Content = dot;
                b.Click += delegate { SizeSlider.Value = s; };
                SizePresets.Children.Add(b);
            }

            SizeToImage();
            Loaded += delegate { FitToWindow(); };
            Scroller.SizeChanged += delegate { if (fitMode) FitToWindow(); };
            Scroller.PreviewMouseWheel += Scroller_PreviewMouseWheel;
            UpdateToolUi();
            UpdateHistoryButtons();
        }

        // Opens the window at a comfortable size for the image, within the screen.
        void SizeToImage()
        {
            Rect work = SystemParameters.WorkArea;
            Width = Math.Max(MinWidth, Math.Min(work.Width * 0.85, source.PixelWidth + 96));
            Height = Math.Max(MinHeight, Math.Min(work.Height * 0.85, source.PixelHeight + 200));
        }

        // ---- Tools ----

        void Tool_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            tool = sender == PenTool ? Tool.Pen
                 : sender == HighlighterTool ? Tool.Highlighter
                 : sender == RectangleTool ? Tool.Rectangle
                 : sender == EllipseTool ? Tool.Ellipse
                 : sender == ArrowTool ? Tool.Arrow
                 : Tool.Select;
            UpdateToolUi();
        }

        bool IsHighlighter { get { return tool == Tool.Highlighter; } }

        Color CurrentColor
        {
            get { return IsHighlighter ? highlighterColor : penColor; }
            set
            {
                if (IsHighlighter) { highlighterColor = value; settings.HighlighterColor = ColorPicker.Hex(value); }
                else { penColor = value; settings.PenColor = ColorPicker.Hex(value); }
                settings.Save();
            }
        }

        double CurrentSize
        {
            get { return IsHighlighter ? settings.HighlighterSize : settings.PenSize; }
            set
            {
                if (IsHighlighter) settings.HighlighterSize = value;
                else settings.PenSize = value;
                settings.Save();
            }
        }

        void UpdateToolUi()
        {
            bool draws = tool != Tool.Select;
            ColorButton.IsEnabled = SizeButton.IsEnabled = draws;
            Ink.Cursor = draws ? Cursors.Cross : Cursors.Hand;

            ColorDot.Fill = new SolidColorBrush(CurrentColor);
            double dot = Math.Max(3, Math.Min(18, CurrentSize));
            SizeDot.Width = SizeDot.Height = dot;
            SizeSlider.Value = CurrentSize;
            picker.Title = IsHighlighter ? "Highlighter color" : "Pen and shape color";
            SizeTitle.Text = IsHighlighter ? "Highlighter size" : "Pen and shape size";
        }

        void OnPickerColor(Color c)
        {
            CurrentColor = c;
            ColorDot.Fill = new SolidColorBrush(c);
        }

        void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsInitialized) return;
            SizeValue.Text = SizeSlider.Value + " px";
            if (CurrentSize != SizeSlider.Value) CurrentSize = SizeSlider.Value;
            double dot = Math.Max(3, Math.Min(18, SizeSlider.Value));
            SizeDot.Width = SizeDot.Height = dot;
        }

        // A StaysOpen=false popup closes on the mouse-down that also clicks its button; don't reopen it.
        void Popup_Closed(object sender, EventArgs e) { popupClosedAt = DateTime.Now; }
        bool JustClosed { get { return (DateTime.Now - popupClosedAt).TotalMilliseconds < 250; } }

        void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (JustClosed) return;
            picker.Color = CurrentColor;
            ColorPopup.IsOpen = true;
        }

        void SizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (JustClosed) return;
            SizeSlider.Value = CurrentSize;
            SizeValue.Text = CurrentSize + " px";
            SizePopup.IsOpen = true;
        }

        // ---- Drawing ----

        void Ink_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(Ink);
            Ink.CaptureMouse();

            if (tool == Tool.Select)
            {
                panning = true;
                dragStart = e.GetPosition(Scroller);
                panOffset = new Point(Scroller.HorizontalOffset, Scroller.VerticalOffset);
                return;
            }

            dragStart = p;
            var brush = new SolidColorBrush(CurrentColor);
            double size = CurrentSize;
            switch (tool)
            {
                case Tool.Pen:
                case Tool.Highlighter:
                    var line = new Polyline
                    {
                        Stroke = brush,
                        StrokeThickness = size,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        // Opacity on the whole element so overlapping parts of one stroke don't get darker.
                        Opacity = IsHighlighter ? HighlighterOpacity : 1
                    };
                    line.Points.Add(p);
                    line.Points.Add(p);
                    drawing = line;
                    break;
                case Tool.Rectangle:
                    drawing = new Rectangle { Stroke = brush, StrokeThickness = size, StrokeLineJoin = PenLineJoin.Round };
                    break;
                case Tool.Ellipse:
                    drawing = new Ellipse { Stroke = brush, StrokeThickness = size };
                    break;
                case Tool.Arrow:
                    drawing = new Path
                    {
                        Stroke = brush,
                        Fill = brush,
                        StrokeThickness = size,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    break;
            }
            Ink.Children.Add(drawing);
            UpdateShape(p, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }

        void Ink_MouseMove(object sender, MouseEventArgs e)
        {
            if (panning)
            {
                Point now = e.GetPosition(Scroller);
                Scroller.ScrollToHorizontalOffset(panOffset.X - (now.X - dragStart.X));
                Scroller.ScrollToVerticalOffset(panOffset.Y - (now.Y - dragStart.Y));
                return;
            }
            if (drawing == null) return;
            UpdateShape(e.GetPosition(Ink), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }

        void Ink_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Ink.ReleaseMouseCapture();
            if (panning) { panning = false; return; }
            if (drawing == null) return;

            // A fast flick can release somewhere the last move event never reported.
            UpdateShape(e.GetPosition(Ink), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            UIElement done = drawing;
            drawing = null;
            var shape = done as FrameworkElement;
            bool tooSmall = (done is Rectangle || done is Ellipse) && (shape.Width < 2 || shape.Height < 2)
                         || (done is Path && (e.GetPosition(Ink) - dragStart).Length < 4);
            if (tooSmall)
            {
                Ink.Children.Remove(done);
                return;
            }
            Commit(new Edit { Added = { done } });
        }

        void UpdateShape(Point p, bool constrain)
        {
            var line = drawing as Polyline;
            if (line != null)
            {
                if ((p - line.Points[line.Points.Count - 1]).Length >= 1) line.Points.Add(p);
                return;
            }

            if (drawing is Path)
            {
                ((Path)drawing).Data = ArrowGeometry(dragStart, p, ((Path)drawing).StrokeThickness);
                return;
            }

            double w = p.X - dragStart.X, h = p.Y - dragStart.Y;
            if (constrain)
            {
                double side = Math.Max(Math.Abs(w), Math.Abs(h));
                w = Math.Sign(w == 0 ? 1 : w) * side;
                h = Math.Sign(h == 0 ? 1 : h) * side;
            }
            var fe = (FrameworkElement)drawing;
            Canvas.SetLeft(fe, Math.Min(dragStart.X, dragStart.X + w));
            Canvas.SetTop(fe, Math.Min(dragStart.Y, dragStart.Y + h));
            fe.Width = Math.Abs(w);
            fe.Height = Math.Abs(h);
        }

        static Geometry ArrowGeometry(Point from, Point to, double thickness)
        {
            Vector v = to - from;
            double length = v.Length;
            var g = new StreamGeometry();
            if (length < 0.5) return g;
            v.Normalize();
            double headLength = Math.Min(length, Math.Max(12, thickness * 4));
            double headHalf = headLength * 0.45;
            Vector normal = new Vector(-v.Y, v.X);
            Point baseCenter = to - v * headLength;

            using (StreamGeometryContext ctx = g.Open())
            {
                // Shaft stops inside the head so the round cap doesn't poke out of the tip.
                ctx.BeginFigure(from, false, false);
                ctx.LineTo(baseCenter + v * Math.Min(headLength * 0.5, thickness), true, false);
                ctx.BeginFigure(to, true, true);
                ctx.LineTo(baseCenter + normal * headHalf, true, true);
                ctx.LineTo(baseCenter - normal * headHalf, true, true);
            }
            g.Freeze();
            return g;
        }

        // ---- History ----

        void Commit(Edit edit)
        {
            undo.Push(edit);
            redo.Clear();
            dirty = true;
            UpdateHistoryButtons();
        }

        void Apply(Edit edit, bool forward)
        {
            foreach (UIElement el in forward ? edit.Removed : edit.Added) Ink.Children.Remove(el);
            foreach (UIElement el in forward ? edit.Added : edit.Removed) Ink.Children.Add(el);
            dirty = true;
            UpdateHistoryButtons();
        }

        void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (undo.Count == 0) return;
            Edit edit = undo.Pop();
            Apply(edit, false);
            redo.Push(edit);
            UpdateHistoryButtons();
        }

        void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (redo.Count == 0) return;
            Edit edit = redo.Pop();
            Apply(edit, true);
            undo.Push(edit);
            UpdateHistoryButtons();
        }

        void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (Ink.Children.Count == 0) return;
            var edit = new Edit();
            foreach (UIElement el in Ink.Children) edit.Removed.Add(el);
            Apply(edit, true);
            Commit(edit);
        }

        void UpdateHistoryButtons()
        {
            UndoButton.IsEnabled = undo.Count > 0;
            RedoButton.IsEnabled = redo.Count > 0;
            ClearButton.IsEnabled = Ink.Children.Count > 0;
        }

        // ---- Output ----

        // The screenshot with all markup burned in, at the original pixel size.
        BitmapSource Flatten()
        {
            int w = source.PixelWidth, h = source.PixelHeight;
            var bounds = new Rect(0, 0, w, h);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawImage(source, bounds);
                if (Ink.Children.Count > 0)
                {
                    var ink = new VisualBrush(Ink)
                    {
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top,
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = bounds,
                        ViewportUnits = BrushMappingMode.Absolute,
                        Viewport = bounds
                    };
                    dc.DrawRectangle(ink, null, bounds);
                }
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var opaque = new FormatConvertedBitmap(rtb, PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            return opaque;
        }

        void SetStatus(string text, string savedPath = null)
        {
            StatusText.Text = text;
            lastSavedPath = savedPath;
            ShowFileButton.Visibility = savedPath != null ? Visibility.Visible : Visibility.Collapsed;
        }

        void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (ImageIO.CopyToClipboard(Flatten()))
            {
                dirty = false;
                SetStatus("Copied to clipboard");
            }
            else
            {
                SetStatus("The clipboard is busy. Try again.");
            }
        }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = ImageIO.SaveToFolder(Flatten(), settings);
                dirty = false;
                SetStatus("Saved to " + path, path);
            }
            catch (Exception ex)
            {
                SetStatus("Couldn't save: " + ex.Message);
            }
        }

        void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = ImageIO.SaveAs(Flatten(), settings, this);
                if (path == null) return;
                dirty = false;
                SetStatus("Saved to " + path, path);
            }
            catch (Exception ex)
            {
                SetStatus("Couldn't save: " + ex.Message);
            }
        }

        void ShowFile_Click(object sender, RoutedEventArgs e)
        {
            if (lastSavedPath != null) ImageIO.ShowInExplorer(lastSavedPath);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!dirty || Ink.Children.Count == 0) return;
            MessageBoxResult answer = MessageBox.Show(this, "Do you want to keep your changes? Copy them to the clipboard or save them first.",
                "Shotto", MessageBoxButton.YesNoCancel, MessageBoxImage.None, MessageBoxResult.Cancel);
            // Yes = copy and close, No = discard, Cancel = stay.
            if (answer == MessageBoxResult.Yes) ImageIO.CopyToClipboard(Flatten());
            else if (answer == MessageBoxResult.Cancel) e.Cancel = true;
        }

        // ---- Zoom ----

        // Zoom 1.0 = one image pixel per DIP; "actual size" is one image pixel per screen pixel.
        double ActualSizeZoom { get { return 1 / VisualTreeHelper.GetDpi(this).DpiScaleX; } }

        void SetZoom(double z, bool fit = false)
        {
            fitMode = fit;
            z = Math.Max(0.05, Math.Min(16, z));
            Zoom.ScaleX = Zoom.ScaleY = z;
            ZoomText.Text = Math.Round(z / ActualSizeZoom * 100) + "%";
            // Crisp pixels when magnified, smooth when shrunk.
            RenderOptions.SetBitmapScalingMode(Photo, z > ActualSizeZoom ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        }

        void FitToWindow()
        {
            double availW = Scroller.ActualWidth - 48, availH = Scroller.ActualHeight - 48;
            if (availW <= 0 || availH <= 0) return;
            double fit = Math.Min(availW / source.PixelWidth, availH / source.PixelHeight);
            SetZoom(Math.Min(ActualSizeZoom, fit), true);
        }

        void StepZoom(int direction)
        {
            double current = Zoom.ScaleX / ActualSizeZoom;
            double next = current;
            if (direction > 0) { foreach (double s in ZoomSteps) if (s > current + 0.001) { next = s; break; } }
            else { for (int i = ZoomSteps.Length - 1; i >= 0; i--) if (ZoomSteps[i] < current - 0.001) { next = ZoomSteps[i]; break; } }
            SetZoom(next * ActualSizeZoom);
        }

        void ZoomIn_Click(object sender, RoutedEventArgs e) { StepZoom(1); }
        void ZoomOut_Click(object sender, RoutedEventArgs e) { StepZoom(-1); }
        void Fit_Click(object sender, RoutedEventArgs e) { FitToWindow(); }
        void ActualSize_Click(object sender, RoutedEventArgs e) { SetZoom(ActualSizeZoom); }

        void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            StepZoom(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        // ---- Keyboard ----

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.OriginalSource is TextBox) return;
            ModifierKeys mods = Keyboard.Modifiers;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool handled = true;

            if (mods == ModifierKeys.Control)
            {
                switch (key)
                {
                    case Key.Z: Undo_Click(null, null); break;
                    case Key.Y: Redo_Click(null, null); break;
                    case Key.C: Copy_Click(null, null); break;
                    case Key.S: Save_Click(null, null); break;
                    case Key.D0: case Key.NumPad0: FitToWindow(); break;
                    case Key.D1: case Key.NumPad1: SetZoom(ActualSizeZoom); break;
                    case Key.OemPlus: case Key.Add: StepZoom(1); break;
                    case Key.OemMinus: case Key.Subtract: StepZoom(-1); break;
                    default: handled = false; break;
                }
            }
            else if (mods == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (key == Key.S) SaveAs_Click(null, null);
                else if (key == Key.Z) Redo_Click(null, null);
                else handled = false;
            }
            else if (mods == ModifierKeys.None)
            {
                switch (key)
                {
                    case Key.V: SelectTool.IsChecked = true; break;
                    case Key.P: PenTool.IsChecked = true; break;
                    case Key.H: HighlighterTool.IsChecked = true; break;
                    case Key.R: RectangleTool.IsChecked = true; break;
                    case Key.E: EllipseTool.IsChecked = true; break;
                    case Key.A: ArrowTool.IsChecked = true; break;
                    default: handled = false; break;
                }
            }
            else
            {
                handled = false;
            }
            e.Handled = handled;
        }
    }
}
