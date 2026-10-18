using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using MpvNet.Windows.Modern;

static class Program
{
    const int Move = 0x200, Down = 0x201, Up = 0x202;
    static readonly List<string> passed = [];

    [DllImport("user32.dll")]
    static extern IntPtr SendMessageW(IntPtr hwnd, int message, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(Point point);

    static void Send(Control control, int message, int wp = 0, int x = 15, int y = 15) =>
        SendMessageW(control.Handle, message, new IntPtr(wp), new IntPtr((x & 0xffff) | ((y & 0xffff) << 16)));

    static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
        passed.Add(name);
        Console.WriteLine("PASS: " + name);
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        using var form = new Form
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000),
            ClientSize = new Size(400, 180), ShowInTaskbar = false
        };
        var button = new ModernButton("test", width: 80) { Location = new Point(10, 10) };
        var other = new TextBox { Location = new Point(100, 10), Width = 80 };
        var seek = new ModernSeek { Location = new Point(10, 60), Width = 300, Duration = 100 };
        form.Controls.AddRange([button, other, seek]);
        int clicks = 0, mouseClicks = 0;
        button.Click += (_, _) => clicks++;
        button.MouseClick += (_, _) => mouseClicks++;
        form.Show();
        Application.DoEvents();
        using var cover = new OverlayForm
        {
            StartPosition = FormStartPosition.Manual, Location = form.Location,
            Size = form.Size, TopMost = true, ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None
        };
        cover.Show(); Application.DoEvents();
        Check(WindowFromPoint(button.PointToScreen(new Point(15, 15))) != button.Handle,
            "远程测试条件：消息坐标有效，桌面命中判定不指向按钮");

        Send(button, Move); Send(button, Down, 1); Send(button, Up);
        Check(clicks == 1 && mouseClicks == 1, "远程消息点击恰好触发一次");
        Send(button, Up);
        Check(clicks == 1, "只有抬起，没有按下，不触发");
        Send(button, Down, 1); Send(button, Move, 1, 90); Send(button, Up, 0, 90);
        Check(clicks == 1, "拖出按钮再松开，不触发");
        Send(button, Down, 1); Send(button, Move, 1, 90); Send(button, Move, 1); Send(button, Up);
        Check(clicks == 2, "拖出后回到按钮再松开，触发一次");
        Send(button, Down, 1); other.Capture = true; Send(button, Up); other.Capture = false;
        Check(clicks == 2, "失去鼠标捕获后取消点击");
        Send(button, Down, 1); Send(button, 0x1f); Send(button, Up);
        Check(clicks == 2, "取消鼠标操作后不触发");
        Send(button, 0x204, 2); Send(button, 0x205);
        Check(clicks == 2, "右键不触发左键操作");
        Send(button, Down, 1); button.Enabled = false; Send(button, Up); button.Enabled = true;
        Check(clicks == 2, "按下后禁用按钮，不触发");

        CancelEventHandler cancel = (_, e) => e.Cancel = true;
        other.Validating += cancel;
        form.ActiveControl = other;
        Send(button, Down, 1); Send(button, Up);
        Check(clicks == 2, "远程点击仍遵守表单输入校验");
        other.Validating -= cancel;

        int seeks = 0;
        seek.SeekRequested += _ => seeks++;
        Send(seek, Down, 1, 36); Send(seek, Move, 1, 80); Send(seek, Up, 0, 120);
        Check(Math.Abs(seek.Position - 100.0 * (120 - 6) / (300 - 12)) < .0001 && seeks == 3,
            "进度条使用抬起消息的最终坐标");
        Send(seek, Down, 1, 36); other.Capture = true;
        int before = seeks; Send(seek, Move, 1, 160); other.Capture = false;
        Check(seeks == before, "进度条失去捕获后停止拖动");

        // Exercise the normal desktop hit-test path as well, to catch duplicate clicks.
        cover.Hide();
        form.Location = Screen.PrimaryScreen!.WorkingArea.Location + new Size(25, 25);
        form.TopMost = true;
        form.Activate(); Application.DoEvents();
        Check(WindowFromPoint(button.PointToScreen(new Point(15, 15))) == button.Handle,
            "本地测试条件：桌面命中判定指向按钮");
        Send(button, Down, 1); Send(button, Up);
        Check(clicks == 3 && mouseClicks == 3, "正常桌面点击也只触发一次");
        form.Hide();
        if (args.Length > 0)
            File.WriteAllText(args[0], JsonSerializer.Serialize(passed, new JsonSerializerOptions { WriteIndented = true }));
    }

    sealed class OverlayForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }
}
