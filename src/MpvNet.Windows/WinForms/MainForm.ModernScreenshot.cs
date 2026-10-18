using System.Drawing;
using System.Windows.Forms;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    Form? _screenshotDialog;

    void ShowScreenshot(bool includeSubtitles)
    {
        if (_screenshotDialog is { IsDisposed: false }) { _screenshotDialog.Activate(); return; }
        if (Player.GetPropertyInt("video-params/w") <= 0) { ShowToast("请先打开有画面的视频"); return; }
        using var image = ScreenshotImage.CaptureBitmap(Player,includeSubtitles?"subtitles":"video");
        using var form = ModernTheme.Dialog("截图", 780, 560);
        // This dialog is assembled after creating the themed form. Scale its layout once,
        // explicitly, so WinForms cannot scale fonts but leave the later-added rows unscaled.
        int Scale(int value) => (int)Math.Ceiling(value * DeviceDpi / 96d);
        form.AutoScaleMode = AutoScaleMode.None;
        var area = Screen.FromControl(this).WorkingArea;
        form.ClientSize = new Size(Math.Min(Scale(780), area.Width - Scale(40)), Math.Min(Scale(560), area.Height - Scale(70)));
        form.MinimumSize = new Size(Scale(480), Scale(330));
        form.SuspendLayout();
        _screenshotDialog = form;
        try
        {
            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = Scale(68),
                ColumnCount = 1, RowCount = 2, Padding = new Padding(Scale(20), Scale(8), Scale(20), Scale(4)) };
            var title = ModernTheme.Label("截图预览", 16);
            var details = ModernTheme.Label($"{image.Width} × {image.Height}  ·  {(includeSubtitles ? "含字幕" : "不含字幕")}  ·  {TimeText(Player.GetPropertyDouble("time-pos"))}", 9, true);
            title.Margin = Padding.Empty; details.Margin = Padding.Empty;
            header.Controls.Add(title, 0, 0); header.Controls.Add(details, 0, 1);
            var previewArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Scale(20), 0, Scale(20), Scale(8)) };
            var preview = new PictureBox { Dock = DockStyle.Fill, Image = image,
                SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, AccessibleName = "视频截图预览" };
            previewArea.Controls.Add(preview);
            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Scale(94),
                ColumnCount = 2, RowCount = 2, Padding = new Padding(Scale(18), Scale(6), Scale(18), Scale(12)) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(30)));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var status = ModernTheme.Label("选择保存位置，或复制图片到剪贴板。", 9, true);
            status.AutoSize = false; status.Dock = DockStyle.Fill; status.AutoEllipsis = true;
            status.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(status, 0, 0); footer.SetColumnSpan(status, 2);
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            var save = new ModernButton("保存到…", "\uE74E", Scale(120)) { Accent = true, TabStop = true };
            var copy = new ModernButton("复制到剪贴板", "\uE8C8", Scale(160)) { TabStop = true };
            var close = new ModernButton("关闭", width: Scale(76)) { DialogResult = DialogResult.Cancel, TabStop = true };
            foreach (var button in new[] { save, copy, close })
            {
                button.Height = Scale(36); button.Margin = new Padding(Scale(2));
            }
            string suggestedName = ScreenshotImage.FileName(Player.GetPropertyString("path"), Player.GetPropertyDouble("time-pos"));
            save.Click += (_, _) => {
                using var picker = new SaveFileDialog { Title = "保存截图", FileName = suggestedName,
                    Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg",
                    DefaultExt = "png", AddExtension = true, OverwritePrompt = true, RestoreDirectory = true };
                if (picker.ShowDialog(form) != DialogResult.OK) return;
                try { ScreenshotImage.Save(image, picker.FileName); status.Text = "已保存：" + picker.FileName; }
                catch (Exception ex) { status.Text = "保存失败：" + ex.Message; }
            };
            copy.Click += (_, _) => {
                try { ScreenshotImage.Copy(image); status.Text = "图片已复制到剪贴板，可以直接粘贴。"; }
                catch (Exception) { status.Text = "复制失败，剪贴板可能被其他程序占用，请重试。"; }
            };
            actions.Controls.AddRange([save, copy, close]); footer.Controls.Add(actions, 1, 1);
            form.Controls.Add(previewArea); form.Controls.Add(footer); form.Controls.Add(header);
            form.AcceptButton = save; form.CancelButton = close;
            form.ResumeLayout(true);
            form.ShowDialog(this);
        }
        finally { _screenshotDialog = null; }
    }
}
