using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Shotto
{
    static class ImageIO
    {
        // Grabs a physical-pixel area of the desktop.
        public static BitmapSource CaptureScreen(Int32Rect area)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            IntPtr mem = Native.CreateCompatibleDC(screen);
            IntPtr bmp = Native.CreateCompatibleBitmap(screen, area.Width, area.Height);
            IntPtr old = Native.SelectObject(mem, bmp);
            try
            {
                Native.BitBlt(mem, 0, 0, area.Width, area.Height, screen, area.X, area.Y, Native.SRCCOPY | Native.CAPTUREBLT);
                Native.SelectObject(mem, old);
                BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                Native.DeleteObject(bmp);
                Native.DeleteDC(mem);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        // Copies out a sub-area so the crop doesn't keep the full multi-monitor capture alive.
        public static BitmapSource Crop(BitmapSource src, Int32Rect area)
        {
            var wb = new WriteableBitmap(new CroppedBitmap(src, area));
            wb.Freeze();
            return wb;
        }

        public static bool CopyToClipboard(BitmapSource image)
        {
            var data = new DataObject();
            data.SetImage(image);
            // Browsers, chat apps and Office prefer PNG over the legacy DIB.
            var png = new MemoryStream();
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(image));
            enc.Save(png);
            data.SetData("PNG", png);

            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(data, true);
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(50); // Another app has the clipboard open.
                }
            }
            return false;
        }

        public static string NewFileName(string format)
        {
            return "Shotto " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + "." + (format == "jpg" ? "jpg" : "png");
        }

        // Saves into the configured folder with a timestamped name; returns the full path.
        public static string SaveToFolder(BitmapSource image, Settings settings)
        {
            Directory.CreateDirectory(settings.SaveDirectory);
            string path = Path.Combine(settings.SaveDirectory, NewFileName(settings.SaveFormat));
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(settings.SaveDirectory, Path.GetFileNameWithoutExtension(NewFileName(settings.SaveFormat)) + " (" + i + ")." + settings.SaveFormat);
            Save(image, path);
            return path;
        }

        // Shows the system Save dialog; returns the chosen path or null.
        public static string SaveAs(BitmapSource image, Settings settings, Window owner)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save screenshot",
                Filter = "PNG image|*.png|JPEG image|*.jpg|Bitmap image|*.bmp",
                FilterIndex = settings.SaveFormat == "jpg" ? 2 : 1,
                FileName = NewFileName(settings.SaveFormat),
                InitialDirectory = Directory.Exists(settings.SaveDirectory) ? settings.SaveDirectory : Settings.DownloadsFolder,
                AddExtension = true
            };
            if (dlg.ShowDialog(owner) != true) return null;
            Save(image, dlg.FileName);
            return dlg.FileName;
        }

        public static void Save(BitmapSource image, string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            BitmapEncoder enc = ext == ".jpg" || ext == ".jpeg" ? new JpegBitmapEncoder { QualityLevel = 92 }
                              : ext == ".bmp" ? new BmpBitmapEncoder()
                              : (BitmapEncoder)new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(image));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        public static void ShowInExplorer(string path)
        {
            System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }

        public static void OpenFolder(string dir)
        {
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
        }
    }
}
