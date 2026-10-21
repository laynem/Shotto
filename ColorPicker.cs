using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shotto
{
    // Swatch palette plus a hue/saturation/value picker and hex entry.
    class ColorPicker : StackPanel
    {
        public static readonly Color[] Palette =
        {
            Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x7A, 0x75, 0x74), Color.FromRgb(0xE8, 0x11, 0x23),
            Color.FromRgb(0xF7, 0x63, 0x0C), Color.FromRgb(0xFF, 0xF1, 0x00), Color.FromRgb(0x16, 0xC6, 0x0C), Color.FromRgb(0x00, 0xB7, 0xC3),
            Color.FromRgb(0x00, 0x78, 0xD4), Color.FromRgb(0x88, 0x6C, 0xE4), Color.FromRgb(0xE3, 0x00, 0x8C), Color.FromRgb(0x8E, 0x56, 0x2E),
        };

        const double AreaWidth = 248, AreaHeight = 140;

        readonly Rectangle hueFill = new Rectangle();
        readonly Canvas area = new Canvas { Width = AreaWidth, Height = AreaHeight, ClipToBounds = true, Cursor = Cursors.Cross };
        readonly Ellipse areaThumb = new Ellipse { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
        readonly Canvas hueBar = new Canvas { Width = AreaWidth, Height = 14, Cursor = Cursors.Hand };
        readonly Border hueThumb = new Border { Width = 8, Height = 18, CornerRadius = new CornerRadius(4), BorderBrush = Brushes.White, BorderThickness = new Thickness(2), IsHitTestVisible = false };
        readonly TextBox hexBox = new TextBox { Width = 110, MaxLength = 7 };
        readonly Border preview = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        readonly TextBlock title = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };

        double hue, sat, val;
        Color color;
        bool updating;

        public event Action<Color> ColorChanged;

        public string Title { set { title.Text = value; } }

        public Color Color
        {
            get { return color; }
            set
            {
                color = value;
                RgbToHsv(value, out hue, out sat, out val);
                Sync(true);
            }
        }

        public ColorPicker()
        {
            Width = AreaWidth;
            Children.Add(title);

            var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (Color c in Palette)
            {
                Color swatchColor = c;
                var b = new Button
                {
                    Width = 28, Height = 28, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(0),
                    MinWidth = 0, MinHeight = 0, ToolTip = Hex(c), Cursor = Cursors.Hand,
                    Template = SwatchTemplate(),
                    Background = new SolidColorBrush(c)
                };
                b.Click += delegate { Color = swatchColor; Raise(); };
                swatches.Children.Add(b);
            }
            Children.Add(swatches);

            var white = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0);
            var black = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90);
            foreach (Rectangle r in new[] { hueFill, new Rectangle { Fill = white }, new Rectangle { Fill = black } })
            {
                r.Width = AreaWidth; r.Height = AreaHeight;
                area.Children.Add(r);
            }
            area.Children.Add(areaThumb);
            var areaFrame = new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = area };
            Children.Add(areaFrame);

            var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            for (int i = 0; i <= 6; i++) rainbow.GradientStops.Add(new GradientStop(HsvToRgb(i * 60 % 360, 1, 1), i / 6.0));
            hueBar.Children.Add(new Border { Width = AreaWidth, Height = 14, CornerRadius = new CornerRadius(7), Background = rainbow });
            hueBar.Children.Add(hueThumb);
            var shadow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.6 };
            shadow.Freeze();
            hueThumb.Effect = areaThumb.Effect = shadow;
            Children.Add(new Border { Margin = new Thickness(0, 12, 0, 0), Child = hueBar });

            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            preview.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            row.Children.Add(preview);
            hexBox.Margin = new Thickness(10, 0, 0, 0);
            hexBox.VerticalAlignment = VerticalAlignment.Center;
            hexBox.LostFocus += delegate { CommitHex(); };
            hexBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CommitHex(); e.Handled = true; } };
            row.Children.Add(hexBox);
            Children.Add(row);

            area.MouseLeftButtonDown += (s, e) => { area.CaptureMouse(); PickArea(e.GetPosition(area)); };
            area.MouseMove += (s, e) => { if (area.IsMouseCaptured) PickArea(e.GetPosition(area)); };
            area.MouseLeftButtonUp += delegate { area.ReleaseMouseCapture(); };
            hueBar.MouseLeftButtonDown += (s, e) => { hueBar.CaptureMouse(); PickHue(e.GetPosition(hueBar)); };
            hueBar.MouseMove += (s, e) => { if (hueBar.IsMouseCaptured) PickHue(e.GetPosition(hueBar)); };
            hueBar.MouseLeftButtonUp += delegate { hueBar.ReleaseMouseCapture(); };
        }

        static ControlTemplate SwatchTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetResourceReference(Border.BorderBrushProperty, "ControlStrongStrokeColorDefaultBrush");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.Name = "Swatch";
            t.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Swatch"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentFillColorDefaultBrush"), "Swatch"));
            t.Triggers.Add(hover);
            return t;
        }

        void PickArea(Point p)
        {
            sat = Math.Max(0, Math.Min(1, p.X / AreaWidth));
            val = 1 - Math.Max(0, Math.Min(1, p.Y / AreaHeight));
            color = HsvToRgb(hue, sat, val);
            Sync(true);
            Raise();
        }

        void PickHue(Point p)
        {
            hue = Math.Max(0, Math.Min(359.9, p.X / AreaWidth * 360));
            color = HsvToRgb(hue, sat, val);
            Sync(true);
            Raise();
        }

        void CommitHex()
        {
            if (updating) return;
            string text = hexBox.Text.Trim();
            if (!text.StartsWith("#")) text = "#" + text;
            int rgb;
            if (text.Length == 7 && int.TryParse(text.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
            {
                Color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                Raise();
            }
            else
            {
                Sync(true);
            }
        }

        void Sync(bool updateHex)
        {
            updating = true;
            hueFill.Fill = new SolidColorBrush(HsvToRgb(hue, 1, 1));
            Canvas.SetLeft(areaThumb, sat * AreaWidth - 7);
            Canvas.SetTop(areaThumb, (1 - val) * AreaHeight - 7);
            areaThumb.Stroke = val > 0.6 && sat < 0.4 ? Brushes.Black : Brushes.White;
            Canvas.SetLeft(hueThumb, hue / 360 * AreaWidth - 4);
            Canvas.SetTop(hueThumb, -2);
            preview.Background = new SolidColorBrush(color);
            if (updateHex) hexBox.Text = Hex(color);
            updating = false;
        }

        void Raise()
        {
            if (ColorChanged != null) ColorChanged(color);
        }

        public static string Hex(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B); }

        static Color HsvToRgb(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        static void RgbToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
            s = max == 0 ? 0 : d / max;
            v = max;
        }
    }
}
