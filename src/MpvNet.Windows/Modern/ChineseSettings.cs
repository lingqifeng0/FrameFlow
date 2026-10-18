using System.Reflection;

namespace MpvNet.Windows.Modern;

public static class ChineseSettings
{
    static readonly Dictionary<string, string> Categories = new(StringComparer.OrdinalIgnoreCase) {
        ["General"] = "常规", ["Video"] = "视频", ["Render Options"] = "渲染",
        ["Scaling"] = "缩放", ["libplacebo"] = "libplacebo 渲染", ["Debanding"] = "去色带",
        ["Sigmoidization"] = "S 形曲线", ["Color Adjustment"] = "色彩调整",
        ["HDR Peak Detection"] = "HDR 峰值检测", ["Color Mapping"] = "色彩映射",
        ["Screenshot"] = "截图", ["Audio"] = "音频", ["Subtitle"] = "字幕",
        ["Window"] = "窗口", ["Playback"] = "播放", ["Program Behavior"] = "切换文件",
        ["Input"] = "输入与外部控制", ["Appearance"] = "外观", ["Cache"] = "缓存", ["Demuxer"] = "解复用"
    };
    static readonly Dictionary<string, string> Options = new(StringComparer.OrdinalIgnoreCase) {
        ["yes"] = "启用", ["no"] = "关闭", ["auto"] = "自动", ["info"] = "自动选择",
        ["none"] = "不使用 / 沿用", ["default"] = "默认预设", ["fast"] = "快速", ["high_quality"] = "高质量",
        ["multi"] = "独立进程", ["single"] = "单进程", ["queue"] = "加入队列", ["neutral"] = "中性预设",
        ["system"] = "跟随系统", ["always"] = "始终", ["never"] = "从不", ["once"] = "仅首次空闲",
        ["exact"] = "同名匹配", ["fuzzy"] = "模糊匹配", ["all"] = "加载全部", ["filter"] = "按类型筛选", ["same"] = "同类型文件",
        ["gpu"] = "GPU 渲染", ["gpu-next"] = "libplacebo 渲染", ["direct3d"] = "Direct3D 渲染",
        ["auto-copy"] = "自动，复制回内存", ["auto-safe"] = "兼容硬件解码", ["dxva2-copy"] = "DXVA2，复制回内存",
        ["d3d11va-copy"] = "D3D11，复制回内存", ["cuda-copy"] = "CUDA，复制回内存", ["nvdec-copy"] = "NVDEC，复制回内存",
        ["dxva2"] = "DXVA2 硬件解码", ["d3d11va"] = "D3D11 硬件解码", ["cuda"] = "CUDA 硬件解码", ["nvdec"] = "NVDEC 硬件解码",
        ["d3d11"] = "Direct3D 11", ["opengl"] = "OpenGL", ["vulkan"] = "Vulkan", ["angle"] = "ANGLE 转换层",
        ["win"] = "Windows OpenGL", ["dxinterop"] = "OpenGL 与 Direct3D 互操作", ["winvk"] = "Windows Vulkan",
        ["nearest"] = "最近邻", ["bilinear"] = "双线性", ["bicubic"] = "双三次", ["gaussian"] = "高斯",
        ["box"] = "盒式平均", ["hermite"] = "Hermite 加权", ["oversample"] = "过采样", ["linear"] = "线性",
        ["cubic"] = "三次样条", ["spline"] = "样条", ["spline36"] = "Spline36 样条", ["catmull_rom"] = "Catmull-Rom 样条",
        ["mitchell"] = "Mitchell 样条", ["lanczos"] = "Lanczos 重建", ["ewa_lanczos"] = "EWA Lanczos 重建",
        ["ewa_lanczossharp"] = "EWA Lanczos 锐化", ["ewa_lanczos4sharpest"] = "EWA Lanczos 强锐化",
        ["clip"] = "直接裁剪", ["perceptual"] = "感知映射", ["softclip"] = "柔和裁剪", ["relative"] = "相对映射",
        ["saturation"] = "饱和度映射", ["absolute"] = "绝对映射", ["desaturate"] = "去饱和", ["darken"] = "压暗",
        ["highlight"] = "高亮提示", ["gamma"] = "伽马", ["linearlight"] = "线性光", ["any"] = "自动选择元数据",
        ["hdr10"] = "HDR10 元数据", ["hdr10plus"] = "HDR10+ 元数据", ["cie_y"] = "CIE 亮度",
        ["audio"] = "跟随音频时钟", ["display-resample"] = "跟随显示器并重采样音频",
        ["display-resample-vdrop"] = "显示同步，重采样并丢视频帧", ["display-resample-desync"] = "显示同步，重采样但放松同步",
        ["display-vdrop"] = "显示同步，丢视频帧", ["display-adrop"] = "显示同步，丢音频帧",
        ["display-desync"] = "显示同步，放松音画同步", ["desync"] = "关闭音画同步",
        ["width-session"] = "本次运行记住宽度", ["width-always"] = "始终记住宽度",
        ["height-session"] = "本次运行记住高度", ["height-always"] = "始终记住高度",
        ["video"] = "视频分辨率", ["session"] = "本次运行记住尺寸",
        ["jpg"] = "JPEG 图片", ["png"] = "PNG 图片", ["chinese-china"] = "简体中文", ["english"] = "英语",
        ["bulgarian"] = "保加利亚语", ["french"] = "法语", ["german"] = "德语", ["japanese"] = "日语",
        ["korean"] = "韩语", ["polish"] = "波兰语", ["russian"] = "俄语", ["turkish"] = "土耳其语"
    };

    public static string Category(string path) => string.Join('/', path.Split('/').Select(p => Categories.GetValueOrDefault(p, p)));
    public static string Search(string text) => text.EndsWith(':') ? Category(text[..^1]) + ":" : text;

    public static void Apply(IReadOnlyList<Setting> settings)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MpvNet.Windows.Resources.editor_conf_zh.tsv")
            ?? throw new InvalidOperationException("缺少中文设置资源。");
        using var reader = new StreamReader(stream);
        var translations = new Dictionary<string, (string Title, string Help)>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is string line) {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\t', 3);
            if (parts.Length == 3) translations.Add(parts[0], (parts[1], parts[2]));
        }
        foreach (var setting in settings) {
            string key = setting.File + ":" + setting.Name;
            if (!translations.TryGetValue(key, out var text)) throw new InvalidOperationException("缺少设置翻译：" + key);
            setting.DisplayTitle = text.Title;
            setting.Directory = Category(setting.Directory ?? "");
            if (setting.Name is "input-default-bindings" or "input-doubleclick-time" or "input-ar-delay" or
                "input-ar-rate" or "input-vo-keyboard" or "input-drag-and-drop")
                setting.Directory = "快捷键与鼠标";
            string defaultText = string.IsNullOrEmpty(setting.Default) ? "" : "\n默认值：" + setting.Default;
            setting.Help = "参数：" + setting.Name + "  ·  " + (setting.File == "libplacebo" ? "libplacebo" : setting.File + ".conf") +
                "\n" + text.Help + defaultText;
            if (setting is OptionSetting choice) {
                choice.OptionNameWidth = Math.Max(choice.OptionNameWidth, 190);
                foreach (var option in choice.Options) {
                    string value = option.Name ?? "";
                    string label = Options.GetValueOrDefault(value, value);
                    if (setting.Name == "hwdec" && value == "no") label = "软件解码";
                    if (setting.Name == "hwdec" && value is "auto" or "yes") label = "自动硬件解码";
                    if (setting.Name == "keep-open") label = value switch { "yes" => "继续下一项，保留最后一帧", "no" => "播放结束后关闭", _ => "始终停留当前文件" };
                    if (setting.Name == "hr-seek" && value == "absolute") label = "仅绝对位置精确跳转";
                    if (setting.Name == "start-size" && value == "always") label = "始终记住尺寸";
                    option.Text = label == value ? value : label + "（" + value + "）";
                    if (value == setting.Default) option.Text += "（默认）";
                    option.Help = null; // The Chinese setting description explains the choices.
                }
            }
        }
    }
}
