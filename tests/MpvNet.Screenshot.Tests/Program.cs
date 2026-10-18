using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using MpvNet;
using MpvNet.Windows.Modern;
using static MpvNet.Native.LibMpv;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args is ["--clipboard", var output])
        {
            using var image = Clipboard.GetImage() ?? throw new Exception("Clipboard has no image.");
            image.Save(output, ImageFormat.Png);
            Console.WriteLine(JsonSerializer.Serialize(new { image.Width, image.Height, Output = output }));
            return;
        }
        string root = Path.GetFullPath(args[0]);
        string outputDirectory = Path.Combine(root, "test-results/screenshot");
        Directory.CreateDirectory(outputDirectory);
        NativeLibrary.SetDllImportResolver(typeof(MpvClient).Assembly, (name, _, _) =>
            name == "libmpv-2.dll" ? NativeLibrary.Load(Path.Combine(root, "dist/FrameFlow/libmpv-2.dll")) : IntPtr.Zero);
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            checks++;
            Console.WriteLine("PASS: " + name);
        }
        var client = new MpvClient { Handle = mpv_create() };
        try
        {
            foreach (var option in new[] { ("config", "no"), ("terminal", "no"), ("vo", "gpu"),
                ("hwdec", "no"), ("pause", "yes"), ("osd-level", "0"), ("screenshot-high-bit-depth", "no"),
                ("screenshot-directory", outputDirectory), ("geometry", "640x360+30+30") })
                mpv_set_option_string(client.Handle, GetUtf8Bytes(option.Item1), GetUtf8Bytes(option.Item2));
            Check(mpv_initialize(client.Handle) >= 0, "real libmpv initialized");
            bool failedWithoutVideo = false;
            try { client.CaptureScreenshot(false); }
            catch (InvalidOperationException) { failedWithoutVideo = true; }
            Check(failedWithoutVideo, "no video reports a capture error");
            client.CommandV("loadfile", Path.Combine(root, "test-results/frame-test.mp4"));
            var deadline = DateTime.UtcNow.AddSeconds(12);
            RawScreenshot? raw = null;
            while (DateTime.UtcNow < deadline)
            {
                try { raw = client.CaptureScreenshot(false); break; }
                catch (InvalidOperationException) { Thread.Sleep(100); }
            }
            if (raw == null) throw new Exception("No video frame decoded.");
            Check(raw.Width == 640 && raw.Height == 360 && raw.Pixels.Length == 640 * 360 * 4,
                "native frame has original video resolution and complete pixels");
            using(var direct=ScreenshotImage.CaptureBitmap(client,"video"))
            using(var directReference=ScreenshotImage.CreateBitmap(raw)) {
                Check(direct.Width==directReference.Width&&direct.Height==directReference.Height&&
                    Enumerable.Range(0,30).All(i=>direct.GetPixel(i*17,i*9)==directReference.GetPixel(i*17,i*9)),
                    "direct native raster capture preserves pixels after mpv releases the borrowed memory");
            }
            using(var reduced=ScreenshotImage.CaptureBitmap(client,"video",320))
                Check(reduced.Width==320&&reduced.Height==180&&reduced.GetPixel(20,20).A==255,"temporary previews have a bounded opaque raster and preserve aspect ratio");
            long allocatedBefore=GC.GetAllocatedBytesForCurrentThread();
            var dimensions=client.CaptureScreenshotDimensions("video");
            Check(dimensions.Width==640&&dimensions.Height==360&&GC.GetAllocatedBytesForCurrentThread()-allocatedBefore<100000,
                "render-size verification does not allocate a complete managed frame array");
            GC.Collect();
            using var bitmap = ScreenshotImage.CreateBitmap(raw);
            Check(bitmap.GetPixel(20, 20).A == 255, "padding bytes are treated as opaque RGB, not random transparency");
            string reference = Path.Combine(outputDirectory, "native-reference.png");
            client.CommandV("screenshot-to-file", reference, "video");
            deadline = DateTime.UtcNow.AddSeconds(8);
            while (!File.Exists(reference) && DateTime.UtcNow < deadline) Thread.Sleep(100);
            using (var native = new Bitmap(reference))
            {
                bool same = native.Width == bitmap.Width && native.Height == bitmap.Height;
                for (int y = 0; same && y < bitmap.Height; y += 11)
                    for (int x = 0; same && x < bitmap.Width; x += 13)
                        same &= native.GetPixel(x, y).ToArgb() == bitmap.GetPixel(x, y).ToArgb();
                Check(same, "in-memory capture matches native PNG colors and orientation after native result is freed");
            }
            ScreenshotImage.Save(bitmap, Path.Combine(outputDirectory, "chosen.png"));
            ScreenshotImage.Save(bitmap, Path.Combine(outputDirectory, "chosen.jpg"));
            using (var png = new Bitmap(Path.Combine(outputDirectory, "chosen.png")))
            using (var jpeg = new Bitmap(Path.Combine(outputDirectory, "chosen.jpg")))
                Check(png.RawFormat.Equals(ImageFormat.Png) && jpeg.RawFormat.Equals(ImageFormat.Jpeg) &&
                    png.Width == 640 && jpeg.Width == 640 && png.Height == 360 && jpeg.Height == 360,
                    "chosen PNG and JPEG use matching encodings and full resolution");
            if (Environment.GetEnvironmentVariable("MPVNET_TEST_SKIP_CLIPBOARD")!="1") {
            using (var copySource = new Bitmap(bitmap)) ScreenshotImage.Copy(copySource);
            GC.Collect();
            using (var pasted = Clipboard.GetImage())
                Check(pasted != null && pasted.Width == 640 && pasted.Height == 360,
                    "clipboard holds a real image after the source bitmap is disposed");
            using (var pngClipboard = Clipboard.GetData("PNG") as MemoryStream)
            {
                Check(pngClipboard != null && pngClipboard.Length > 8,
                    "clipboard also offers lossless PNG for compatible applications");
            }
            }
            Check(ScreenshotImage.FileName("课程:电源.mp4", 3661.234) == "课程_电源_01-01-01-234.png",
                "suggested filename preserves Chinese and encodes hours, minutes, seconds and milliseconds");
            string subtitle = Path.Combine(outputDirectory, "capture.srt");
            File.WriteAllText(subtitle, "1\n00:00:00,000 --> 00:01:00,000\nScreenshot subtitle test\n");
            client.CommandV("sub-add", subtitle, "select");
            Thread.Sleep(350);
            var withSubtitles = client.CaptureScreenshot(true);
            var withoutSubtitles = client.CaptureScreenshot(false);
            Check(!withSubtitles.Pixels.SequenceEqual(withoutSubtitles.Pixels), "include/exclude subtitles captures different real frames");
            for (int i = 0; i < 6; i++) client.CaptureScreenshot(false);
            Check(!Directory.GetFiles(outputDirectory, "mpv-shot*").Any(), "capture alone creates no default-directory screenshot files");
            Check(client.GetPropertyBool("pause"), "capture keeps paused playback paused");
            string old = "s async screenshot # menu: Video > Screenshot\r\nS screenshot video\r\n";
            string explicitCommands = "x screenshot-to-file custom.png\r\ny screenshot each-frame\r\nz screenshot; cycle pause\r\n# screenshot\r\n";
            string normalized = InputHelp.RerouteScreenshotBindings(old + explicitCommands);
            Check(normalized.Contains("s script-message-to mpvnet modern-tool screenshot # menu:") &&
                normalized.Contains("S script-message-to mpvnet modern-tool screenshot-video") && normalized.EndsWith(explicitCommands),
                "legacy bindings route to preview while explicit commands and comments are preserved");
            Console.WriteLine($"All {checks} checks passed.");
        }
        finally { client.CommandV("quit"); mpv_destroy(client.Handle); }
    }
}
