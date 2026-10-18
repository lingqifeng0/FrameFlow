using System.Runtime.InteropServices;
using static MpvNet.Native.LibMpv;

namespace MpvNet;

// Tightly packed B8G8R8X8 rows, owned by managed code after the mpv result is freed.
public sealed record RawScreenshot(int Width, int Height, byte[] Pixels);
// Pixels are borrowed from mpv and valid only inside the ReadScreenshotFrame
// callback. Callers must copy/draw before it returns; native memory is freed.
public readonly record struct NativeScreenshotFrame(int Width,int Height,int Stride,nint Pixels);

public partial class MpvClient
{
    public RawScreenshot CaptureScreenshot(bool includeSubtitles) => CaptureScreenshot(includeSubtitles ? "subtitles" : "video");

    public RawScreenshot CaptureScreenshot(string mode) => ReadScreenshotFrame(mode, frame => {
        int rowBytes=checked(frame.Width*4);
        var pixels=new byte[checked(rowBytes*frame.Height)];
        for(int y=0;y<frame.Height;y++) Marshal.Copy(frame.Pixels+checked(y*frame.Stride),pixels,y*rowBytes,rowBytes);
        return new RawScreenshot(frame.Width,frame.Height,pixels);
    });

    public (int Width,int Height) CaptureScreenshotDimensions(string mode) =>
        ReadScreenshotFrame(mode, frame => (frame.Width,frame.Height));

    public T ReadScreenshotFrame<T>(string mode,Func<NativeScreenshotFrame,T> consume)
    {
        if (mode is not ("window" or "video" or "subtitles") || Handle == IntPtr.Zero)
            throw new InvalidOperationException("截图模式或播放器状态无效。");
        string[] args = ["screenshot-raw", mode, "bgr0"];
        var pointers = new nint[args.Length + 1];
        nint arguments = 0, result = 0;
        bool hasResult = false;
        try
        {
            for (int i = 0; i < args.Length; i++)
                pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            arguments = Marshal.AllocHGlobal(pointers.Length * IntPtr.Size);
            Marshal.Copy(pointers, 0, arguments, pointers.Length);
            result = Marshal.AllocHGlobal(Marshal.SizeOf<mpv_node>());
            Marshal.StructureToPtr(new mpv_node(), result, false);
            var error = mpv_command_ret(Handle, arguments, result);
            if (error < 0)
                throw new InvalidOperationException("无法截取当前画面，请确认视频已加载。" + "\n" + GetError(error));
            hasResult = true;
            return consume(ReadScreenshot(Marshal.PtrToStructure<mpv_node>(result)));
        }
        finally
        {
            if (hasResult) mpv_free_node_contents(result);
            if (result != 0) Marshal.FreeHGlobal(result);
            if (arguments != 0) Marshal.FreeHGlobal(arguments);
            foreach (nint pointer in pointers)
                if (pointer != 0) Marshal.FreeCoTaskMem(pointer);
        }
    }

    static NativeScreenshotFrame ReadScreenshot(mpv_node node)
    {
        if (node.format != mpv_format.MPV_FORMAT_NODE_MAP || node.list == 0)
            throw new InvalidOperationException("播放器没有返回有效的截图。");
        var list = Marshal.PtrToStructure<mpv_node_list>(node.list);
        if (list.num is < 1 or > 32 || list.keys == 0 || list.values == 0)
            throw new InvalidOperationException("截图数据不完整。");
        var fields = new Dictionary<string, mpv_node>();
        for (int i = 0; i < list.num; i++)
        {
            string key = ConvertFromUtf8(Marshal.ReadIntPtr(list.keys, i * IntPtr.Size));
            fields[key] = Marshal.PtrToStructure<mpv_node>(list.values + i * Marshal.SizeOf<mpv_node>());
        }
        mpv_node Field(string name, mpv_format format)
        {
            if (!fields.TryGetValue(name, out var field) || field.format != format)
                throw new InvalidOperationException("截图数据不完整：" + name);
            return field;
        }
        int width = checked((int)Field("w", mpv_format.MPV_FORMAT_INT64).int64);
        int height = checked((int)Field("h", mpv_format.MPV_FORMAT_INT64).int64);
        int stride = checked((int)Field("stride", mpv_format.MPV_FORMAT_INT64).int64);
        string formatName = ConvertFromUtf8(Field("format", mpv_format.MPV_FORMAT_STRING).str);
        var bytes = Marshal.PtrToStructure<mpv_byte_array>(Field("data", mpv_format.MPV_FORMAT_BYTE_ARRAY).ba);
        if (width <= 0 || height <= 0 || formatName != "bgr0" || bytes.data == 0)
            throw new InvalidOperationException("截图的尺寸或像素格式无效。");
        int rowBytes = checked(width * 4);
        long requiredBytes = checked((height - 1L) * Math.Abs((long)stride) + rowBytes);
        if (Math.Abs((long)stride) < rowBytes || (ulong)bytes.size < (ulong)requiredBytes)
            throw new InvalidOperationException("截图像素数据不完整。");
        return new NativeScreenshotFrame(width,height,stride,bytes.data);
    }
}
