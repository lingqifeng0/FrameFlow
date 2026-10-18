using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MpvNet.Windows.Modern;

public static class ScreenshotImage
{
    public static Bitmap CaptureBitmap(MpvClient player,string mode,int maximumDimension=0) => player.ReadScreenshotFrame(mode,frame => {
        // The temporary image only wraps mpv's pixels; the returned image owns
        // its raster. There is no full-frame managed byte[] between the two.
        using var source=new Bitmap(frame.Width,frame.Height,frame.Stride,PixelFormat.Format32bppRgb,frame.Pixels);
        double scale=maximumDimension>0 ? Math.Min(1,maximumDimension/(double)Math.Max(frame.Width,frame.Height)) : 1;
        var owned=new Bitmap(Math.Max(1,(int)Math.Round(frame.Width*scale)),Math.Max(1,(int)Math.Round(frame.Height*scale)),PixelFormat.Format32bppRgb);
        try {
            using var graphics=Graphics.FromImage(owned);
            graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(source,new Rectangle(0,0,owned.Width,owned.Height),0,0,source.Width,source.Height,GraphicsUnit.Pixel);
            return owned;
        } catch {owned.Dispose();throw;}
    });

    public static Bitmap CreateBitmap(RawScreenshot frame)
    {
        var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppRgb);
        try
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                int rowBytes = checked(frame.Width * 4);
                for (int y = 0; y < frame.Height; y++)
                    Marshal.Copy(frame.Pixels, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    public static string FileName(string videoPath, double seconds)
    {
        string name = Path.GetFileNameWithoutExtension(videoPath);
        if (string.IsNullOrWhiteSpace(name)) name = "视频截图";
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        name = name.TrimEnd(' ', '.');
        if (name.Length > 120) name = name[..120];
        double safeSeconds = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        var time = TimeSpan.FromMilliseconds(Math.Round(safeSeconds * 1000));
        string stamp = ((int)time.TotalHours).ToString("00", CultureInfo.InvariantCulture) +
            time.ToString(@"\-mm\-ss\-fff", CultureInfo.InvariantCulture);
        return $"{name}_{stamp}.png";
    }

    public static void Save(Bitmap bitmap, string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        var format = extension is ".jpg" or ".jpeg" ? ImageFormat.Jpeg : ImageFormat.Png;
        bitmap.Save(path, format);
    }

    public static void Copy(Bitmap bitmap)
    {
        // Preserve an actual image, and flush it so it survives closing the preview/player.
        var data = new DataObject();
        data.SetData(DataFormats.Bitmap, true, bitmap);
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        png.Position = 0;
        data.SetData("PNG", false, png);
        Clipboard.SetDataObject(data, true, 5, 100);
    }
}
