
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Text.Json;
using MpvNet.Windows.Modern;

using MpvNet.Windows.WPF;
using MpvNet.Windows.UI;
using MpvNet.Help;
using MpvNet.Extensions;
using MpvNet.MVVM;
using MpvNet.Windows.WPF.MsgBox;

using WpfControls = System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;

using static MpvNet.Windows.Native.WinApi;
using static MpvNet.Windows.Help.WinApiHelp;

namespace MpvNet.Windows.WinForms;

public partial class MainForm : Form
{
    public SnapManager SnapManager = new SnapManager();
    public IntPtr MpvWindowHandle { get; set; }
    public bool WasShown { get; set; }
    public static MainForm? Instance { get; set; }
    WpfControls.ContextMenu ContextMenu { get; } = new WpfControls.ContextMenu();
    AutoResetEvent MenuAutoResetEvent { get; } = new AutoResetEvent(false);
    Point _lastCursorPosition;
    Taskbar? _taskbar;
    Point _mouseDownLocation;
    List<Binding>? _confBindings;

    int _lastCursorChanged;
    int _lastCycleFullscreen;
    int _taskbarButtonCreatedMessage;
    int _cursorAutohide = 1000;

    bool _contextMenuIsReady;
    bool _maxSizeSet;
    bool _isCursorVisible = true;

    public MainForm()
    {
        InitializeComponent();

        InitializeModernUi();

        UpdateDarkMode();

        try
        {
            Instance = this;

            Player.FileLoaded += Player_FileLoaded;
            Player.Pause += Player_Pause;
            Player.PlaylistPosChanged += Player_PlaylistPosChanged;
            Player.Seek += UpdateProgressBar;
            Player.Shutdown += Player_Shutdown;
            Player.VideoSizeChanged += Player_VideoSizeChanged;
            Player.ClientMessage += Player_ClientMessage;

            GuiCommand.Current.ScaleWindow += GuiCommand_ScaleWindow;
            GuiCommand.Current.MoveWindow += GuiCommand_MoveWindow;
            GuiCommand.Current.WindowScaleNet += GuiCommand_WindowScaleNet;
            GuiCommand.Current.ShowMenu += GuiCommand_ShowMenu;

            Player.Init(_videoSurface.Handle, true, true);
            InitializeModernPlayer();

            // mpv is embedded in VideoSurface, whose native window state is not
            // the state of this top-level form. A surface resize can report
            // window-maximized=false even while this form remains maximized.
            Player.ObserveProperty("cursor-autohide", PropChangeCursorAutohide);

            Player.ObservePropertyBool("border", PropChangeBorder);
            Player.ObservePropertyBool("fullscreen", PropChangeFullscreen);
            Player.ObservePropertyBool("keepaspect-window", value => Player.KeepaspectWindow = value);
            Player.ObservePropertyBool("ontop", PropChangeOnTop);
            Player.ObservePropertyBool("title-bar", PropChangeTitleBar);

            Player.ObservePropertyString("sid", PropChangeSid);
            Player.ObservePropertyString("aid", PropChangeAid);
            Player.ObservePropertyString("vid", PropChangeVid);

            Player.ObservePropertyString("title", PropChangeTitle);

            Player.ObservePropertyInt("edition", PropChangeEdition);

            Player.ObservePropertyDouble("window-scale", PropChangeWindowScale);

            CommandLine.ProcessCommandLineArgsPostInit();
            Player.SetPropertyBool("resume-playback", false);
            Player.SetPropertyBool("save-position-on-quit", false);
            // Startup files are opened in OnShown after the chrome's first paint.

            _taskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");

            if (Player.Screen > -1)
            {
                int targetIndex = Player.Screen;
                Screen[] screens = Screen.AllScreens;

                if (targetIndex < 0)
                    targetIndex = 0;

                if (targetIndex > screens.Length - 1)
                    targetIndex = screens.Length - 1;

                Screen screen = screens[Array.IndexOf(screens, screens[targetIndex])];
                Rectangle target = screen.Bounds;
                Left = target.X + (target.Width - Width) / 2;
                Top = target.Y + (target.Height - Height) / 2;
            }

            if (!Player.Border)
                FormBorderStyle = FormBorderStyle.None;

            Point pos = App.Settings.WindowPosition;

            if ((pos.X != 0 || pos.Y != 0) && App.RememberWindowPosition)
            {
                Left = pos.X - Width / 2;
                Top = pos.Y - Height / 2;

                Point location = App.Settings.WindowLocation;

                if (location.X == -1) Left = pos.X;
                if (location.X ==  1) Left = pos.X - Width;
                if (location.Y == -1) Top = pos.Y;
                if (location.Y ==  1) Top = pos.Y - Height;
            }

            if (Player.WindowMaximized)
            {
                SetFormPosAndSize(true);
                WindowState = FormWindowState.Maximized;
            }

            if (Player.WindowMinimized)
            {
                SetFormPosAndSize(true);
                WindowState = FormWindowState.Minimized;
            }
        }
        catch (Exception ex)
        {
            Msg.ShowException(ex);
        }
    }

    void UpdateDarkMode()
    {
        if (Environment.OSVersion.Version >= new Version(10, 0, 18985))
            DwmSetWindowAttribute(Handle, 20, new[] { Theme.DarkMode ? 1 : 0 }, 4);  // DWMWA_USE_IMMERSIVE_DARK_MODE = 20
    }

    void Player_ClientMessage(string[] args)
    {
        if (args.Length >= 3 && args[0] == "key-binding" && args[1] is "modern-hold-back" or "modern-hold-forward" or "modern-hold-temporary") {
            Ui(() => HandleHeldNavigation(args[1] == "modern-hold-temporary" ? 2 : args[1] == "modern-hold-back" ? -1 : 1, args[2]));
            return;
        }
        if (args.Length > 0 && args[0] == "modern-playlist") {
            Ui(() => {
                if (args.Length >= 3 && args[1] == "mode" && args[2] is "sequence" or "single-loop" or "list-loop" or "random" or "stop-current")
                    ApplyPlaylistMode(args[2]);
                else if (args.Length >= 2 && args[1] == "refresh-folder") RefreshCurrentFolderPlaylist();
            });
            return;
        }
        if (args.Length > 0 && args[0] == "modern-window")
        {
            Ui(() => HandleModernWindowMessage(args));
            return;
        }
        if (args.Length > 0 && args[0] == "modern-tool")
        {
            Ui(() => HandleModernToolMessage(args));
            return;
        }
        if (args.Length == 0) return;
        if (Command.Current.Commands.ContainsKey(args[0]))
            Command.Current.Commands[args[0]].Invoke(new ArraySegment<string>(args, 1, args.Length - 1));
        else if (GuiCommand.Current.Commands.ContainsKey(args[0]))
            BeginInvoke(() => GuiCommand.Current.Commands[args[0]].Invoke(new ArraySegment<string>(args, 1, args.Length - 1)));
    }

    void Player_PlaylistPosChanged(int pos)
    {
        if (pos == -1)
            SetTitle();
    }

    void PropChangeWindowScale(double scale)
    {
        if (!WasShown)
            return;

        BeginInvoke(() => {
            SetSize(
                (int)(Player.VideoSize.Width * scale),
                (int)Math.Floor(Player.VideoSize.Height * scale),
                Screen.FromControl(this), false);
        });
    }

    void Player_Shutdown() => BeginInvoke(Close);

    void Player_VideoSizeChanged(Size value) => BeginInvoke(() =>
    {
        if (!KeepSize())
            SetFormPosAndSize();
    });

    void GuiCommand_ScaleWindow(float scale)
    {
        BeginInvoke(() => {
            int w, h;

            if (KeepSize())
            {
                w = (int)(ClientSize.Width * scale);
                h = (int)(ClientSize.Height * scale);
            }
            else
            {
                w = (int)(ClientSize.Width * scale);
                h = (int)Math.Floor(w * Player.VideoSize.Height / (double)Player.VideoSize.Width);
            }

            SetSize(w, h, Screen.FromControl(this), false);
        });
    }

    void GuiCommand_MoveWindow(string direction)
    {
        BeginInvoke(() => {
            Screen screen = Screen.FromControl(this);
            Rectangle workingArea = GetWorkingArea(Handle, screen.WorkingArea);

            switch (direction)
            {
                case "left":
                    Left = workingArea.Left;
                    break;
                case "top":
                    Top = 0;
                    break;
                case "right":
                    Left = workingArea.Width - Width + workingArea.Left;
                    break;
                case "bottom":
                    Top = workingArea.Height - Height;
                    break;
                case "center":
                    Left = (screen.Bounds.Width - Width) / 2;
                    Top = (screen.Bounds.Height - Height) / 2;
                    break;
            }
        });
    }

    void GuiCommand_WindowScaleNet(float scale)
    {
        BeginInvoke(() => {
            SetSize(
                (int)(Player.VideoSize.Width * scale),
                (int)Math.Floor(Player.VideoSize.Height * scale),
                Screen.FromControl(this), false);
            Player.Command($"show-text \"window-scale {scale.ToString(CultureInfo.InvariantCulture)}\"");
        });
    }

    void GuiCommand_ShowMenu()
    {
        BeginInvoke(() => {
            if (IsMouseInOsc())
                return;

            ShowCursor();
            UpdateMenu();
            ContextMenu.IsOpen = true;
        });
    }

    void PropChangeFullscreen(bool value) => BeginInvoke(() => CycleFullscreen(value));

    bool IsFullscreen => Player.Fullscreen;

    bool KeepSize() => App.StartSize == "session" || App.StartSize == "always";

    bool IsMouseInOsc()
    {
        Point pos = PointToClient(MousePosition);
        float top = 0;

        if (!Player.Border)
            top = ClientSize.Height * 0.1f;

        return pos.X < ClientSize.Width * 0.1 ||
               pos.X > ClientSize.Width * 0.9 ||
               pos.Y < top ||
               pos.Y > ClientSize.Height * 0.78;
    }

    void UpdateMenu()
    {
        Player.UpdateExternalTracks();

        lock (Player.MediaTracksLock)
        {
            var trackMenuItem = FindMenuItem(_("Track"), "Track");

            if (trackMenuItem != null)
            {
                trackMenuItem.Items.Clear();

                var audTracks = Player.MediaTracks.Where(track => track.Type == "a");
                var subTracks = Player.MediaTracks.Where(track => track.Type == "s");
                var vidTracks = Player.MediaTracks.Where(track => track.Type == "v");
                var ediTracks = Player.MediaTracks.Where(track => track.Type == "e");

                foreach (MediaTrack track in vidTracks)
                {
                    var menuItem = new WpfControls.MenuItem() { Header = track.Text.Replace("_", "__") };
                    menuItem.Click += (sender, args) => Player.CommandV("set", "vid", track.ID.ToString());
                    menuItem.IsChecked = Player.VID == track.ID.ToString();
                    trackMenuItem.Items.Add(menuItem);
                }

                if (vidTracks.Any())
                    trackMenuItem.Items.Add(new WpfControls.Separator());

                foreach (MediaTrack track in audTracks)
                {
                    var menuItem = new WpfControls.MenuItem() { Header = track.Text.Replace("_", "__") };
                    menuItem.Click += (sender, args) => Player.CommandV("set", "aid", track.ID.ToString());
                    menuItem.IsChecked = Player.AID == track.ID.ToString();
                    trackMenuItem.Items.Add(menuItem);
                }

                if (subTracks.Any())
                    trackMenuItem.Items.Add(new WpfControls.Separator());

                foreach (MediaTrack track in subTracks)
                {
                    var menuItem = new WpfControls.MenuItem() { Header = track.Text.Replace("_", "__") };
                    menuItem.Click += (sender, args) => Player.CommandV("set", "sid", track.ID.ToString());
                    menuItem.IsChecked = Player.SID == track.ID.ToString();
                    trackMenuItem.Items.Add(menuItem);
                }

                if (subTracks.Any())
                {
                    var menuItem = new WpfControls.MenuItem() { Header = "S: No subtitles" };
                    menuItem.Click += (sender, args) => Player.CommandV("set", "sid", "no");
                    menuItem.IsChecked = Player.SID == "no";
                    trackMenuItem.Items.Add(menuItem);
                }

                if (ediTracks.Any())
                    trackMenuItem.Items.Add(new WpfControls.Separator());

                foreach (MediaTrack track in ediTracks)
                {
                    var menuItem = new WpfControls.MenuItem() { Header = track.Text.Replace("_", "__") };
                    menuItem.Click += (sender, args) => Player.CommandV("set", "edition", track.ID.ToString());
                    menuItem.IsChecked = Player.Edition == track.ID;
                    trackMenuItem.Items.Add(menuItem);
                }
            }
        }

        var chaptersMenuItem = FindMenuItem(_("Chapter"), "Chapters");

        if (chaptersMenuItem != null)
        {
            chaptersMenuItem.Items.Clear();

            foreach (Chapter chapter in Player.GetChapters())
            {
                var menuItem = new WpfControls.MenuItem
                {
                    Header = chapter.Title,
                    InputGestureText = chapter.TimeDisplay
                };
                
                menuItem.Click += (sender, args) =>
                    Player.CommandV("seek", chapter.Time.ToString(CultureInfo.InvariantCulture), "absolute");

                chaptersMenuItem.Items.Add(menuItem);
            }
        }

        var recentMenuItem = FindMenuItem(_("Recent Files"), "Recent");

        if (recentMenuItem != null)
        {
            recentMenuItem.Items.Clear();

            foreach (string path in App.Settings.RecentFiles)
            {
                var file = AppClass.GetTitleAndPath(path);
                var menuItem = MenuHelp.Add(recentMenuItem.Items, file.Title.ShortPath(100));

                if (menuItem != null)
                    menuItem.Click += (sender, args) => Player.LoadFiles(new[] { file.Path }, true, false);
            }

            recentMenuItem.Items.Add(new WpfControls.Separator());
            var clearMenuItem = new WpfControls.MenuItem() { Header = _("Clear List") };
            clearMenuItem.Click += (sender, args) => App.Settings.RecentFiles.Clear();
            recentMenuItem.Items.Add(clearMenuItem);
        }

        var titlesMenuItem = FindMenuItem(_("Title"), "Titles");

        if (titlesMenuItem != null)
        {
            titlesMenuItem.Items.Clear();

            lock (Player.BluRayTitles)
            {
                List<(int Index, TimeSpan Length)> items = new List<(int, TimeSpan)>(); 

                for (int i = 0; i < Player.BluRayTitles.Count; i++)
                    items.Add((i, Player.BluRayTitles[i]));

                var titleItems = items.OrderByDescending(item => item.Length)
                                      .Take(20)
                                      .OrderBy(item => item.Index);

                foreach (var item in titleItems)
                {
                    if (item.Length != TimeSpan.Zero)
                    {
                        var menuItem = MenuHelp.Add(titlesMenuItem.Items, $"Title {item.Index + 1}");

                        if (menuItem != null)
                        {
                            menuItem.InputGestureText = item.Length.ToString();
                            menuItem.Click += (sender, args) => Player.SetBluRayTitle(item.Index);
                        }
                    }
                }
            }
        }

        var profilesMenuItem = FindMenuItem(_("Profile"), "Profile");

        if (profilesMenuItem != null && !profilesMenuItem.HasItems)
        {
            foreach (string profile in Player.ProfileNames)
            {
                if (!profile.StartsWith("extension."))
                {
                    var menuItem = MenuHelp.Add(profilesMenuItem.Items, profile);

                    if (menuItem != null)
                    {
                        menuItem.Click += (sender, args) =>
                        {
                            Player.CommandV("show-text", profile);
                            Player.CommandV("apply-profile", profile);
                        };
                    }
                }
            }

            profilesMenuItem.Items.Add(new WpfControls.Separator());
            var showProfilesMenuItem = new WpfControls.MenuItem() { Header = _("Show Profiles") };
            showProfilesMenuItem.Click += (sender, args) => Player.Command("script-message-to mpvnet show-profiles");
            profilesMenuItem.Items.Add(showProfilesMenuItem);
        }

        var audioDevicesMenuItem = FindMenuItem(_("Audio Device"), "Audio Device");

        if (audioDevicesMenuItem != null)
        {
            audioDevicesMenuItem.Items.Clear();

            foreach (var pair in Player.AudioDevices)
            {
                var menuItem = MenuHelp.Add(audioDevicesMenuItem.Items, pair.Value);

                if (menuItem != null)
                {
                    menuItem.IsChecked = pair.Name == Player.GetPropertyString("audio-device");

                    menuItem.Click += (sender, args) =>
                    {
                        Player.SetPropertyString("audio-device", pair.Name);
                        Player.CommandV("show-text", pair.Value);
                        App.Settings.AudioDevice = pair.Name;
                    };
                }
            }
        }

        var customMenuItem = FindMenuItem(_("Custom"), "Custom");

        if (customMenuItem != null && !customMenuItem.HasItems)
        {
            var customBindings = _confBindings!.Where(it => it.IsCustomMenu);

            if (customBindings.Any())
            {
                foreach (Binding binding in customBindings)
                {
                    var menuItem = MenuHelp.Add(customMenuItem.Items, binding.Comment);

                    if (menuItem != null)
                    {
                        menuItem.Click += (sender, args) => Player.Command(binding.Command);
                    }
                }
            }
            else
            {
                if (ContextMenu.Items.Contains(customMenuItem))
                    ContextMenu.Items.Remove(customMenuItem);
            }
        }
    }

    public WpfControls.MenuItem? FindMenuItem(string text, string text2 = "") {
        var ret = FindMenuItem(text, ContextMenu.Items);

        if (ret == null && text2 != "")
            return FindMenuItem(text2, ContextMenu.Items);

        return ret;
    }

    WpfControls.MenuItem? FindMenuItem(string text, WpfControls.ItemCollection? items)
    {
        foreach (object item in items!)
        {
            if (item is WpfControls.MenuItem mi)
            {
                if (mi.Header.ToString().StartsWithEx(text) && mi.Header.ToString().TrimEx() == text)
                    return mi;

                if (mi.Items.Count > 0)
                {
                    WpfControls.MenuItem? val = FindMenuItem(text, mi.Items);

                    if (val != null)
                        return val;
                }
            }
        }

        return null;
    }

    void SetFormPosAndSize(bool force = false, bool checkAutofit = true, bool load = false)
    {
        // The desktop shell owns the host size. Video reconfiguration must not
        // run the former aspect/autofit routine over a user's resized window.
        if (_modernSettings != null) {
            if (Player.Fullscreen && !_modernFullscreen) CycleFullscreen(true);
            Player.WasInitialSizeSet = true;
            return;
        }
        if (!force)
        {
            if (WindowState != FormWindowState.Normal)
                return;

            if (Player.Fullscreen)
            {
                CycleFullscreen(true);
                return;
            }
        }

        Screen screen = Screen.FromControl(this);
        Rectangle workingArea = GetWorkingArea(Handle, screen.WorkingArea);
        int autoFitHeight = Convert.ToInt32(workingArea.Height * Player.Autofit);

        if (App.AutofitAudio > 1)
            App.AutofitAudio = 1;

        if (App.AutofitImage > 1)
            App.AutofitImage = 1;

        bool isAudio = FileTypes.IsAudio(Player.Path.Ext);
        
        if (isAudio)
            autoFitHeight = Convert.ToInt32(workingArea.Height * App.AutofitAudio);

        if (FileTypes.IsImage(Player.Path.Ext))
            autoFitHeight = Convert.ToInt32(workingArea.Height * App.AutofitImage);

        if (Player.VideoSize.Height == 0 || Player.VideoSize.Width == 0)
            Player.VideoSize = new Size((int)(autoFitHeight * (16 / 9f)), autoFitHeight);

        float minAspectRatio = isAudio ? App.MinimumAspectRatioAudio : App.MinimumAspectRatio;

        if (minAspectRatio != 0 && Player.VideoSize.Width / (float)Player.VideoSize.Height < minAspectRatio)
            Player.VideoSize = new Size((int)(autoFitHeight * minAspectRatio), autoFitHeight);

        Size videoSize = Player.VideoSize;

        int height = videoSize.Height;
        int width  = videoSize.Width;

        if (App.StartSize == "previous")
            App.StartSize = "height-session";

        if (Player.WasInitialSizeSet)
        {
            if (KeepSize())
            {
                width = ClientSize.Width;
                height = ClientSize.Height;
            }
            else if (App.StartSize == "height-always" || App.StartSize == "height-session")
            {
                height = ClientSize.Height;
                width = (int)Math.Ceiling(height * videoSize.Width / (double)videoSize.Height);
            }
            else if (App.StartSize == "width-always" || App.StartSize == "width-session")
            {
                width = ClientSize.Width;
                height = (int)Math.Floor(width * videoSize.Height / (double)videoSize.Width);
            }
        }
        else
        {
            Size windowSize = App.Settings.WindowSize;

            if (App.StartSize == "height-always" && windowSize.Height != 0)
            {
                height = windowSize.Height;
                width = (int)Math.Ceiling(height * videoSize.Width / (double)videoSize.Height);
            }
            else if (App.StartSize == "height-session" || App.StartSize == "session")
            {
                height = autoFitHeight;
                width = (int)Math.Ceiling(height * videoSize.Width / (double)videoSize.Height);
            }
            else if(App.StartSize == "width-always" && windowSize.Height != 0)
            {
                width = windowSize.Width;
                height = (int)Math.Floor(width * videoSize.Height / (double)videoSize.Width);
            }
            else if (App.StartSize == "width-session")
            {
                width = autoFitHeight / 9 * 16;
                height = (int)Math.Floor(width * videoSize.Height / (double)videoSize.Width);
            }
            else if (App.StartSize == "always" && windowSize.Height != 0)
            {
                height = windowSize.Height;
                width = windowSize.Width;
            }
            
            Player.WasInitialSizeSet = true;
        }

        SetSize(width, height, screen, checkAutofit, load);
    }

    void SetSize(int width, int height, Screen screen, bool checkAutofit = true, bool load = false)
    {
        Rectangle workingArea = GetWorkingArea(Handle, screen.WorkingArea);

        int maxHeight = workingArea.Height - (Height - ClientSize.Height) - 2;
        int maxWidth = workingArea.Width - (Width - ClientSize.Width);

        int startWidth = width;
        int startHeight = height;

        if (checkAutofit)
        {
            if (height < maxHeight * Player.AutofitSmaller)
            {
                height = (int)(maxHeight * Player.AutofitSmaller);
                width = (int)Math.Ceiling(height * startWidth / (double)startHeight);
            }

            if (height > maxHeight * Player.AutofitLarger)
            {
                height = (int)(maxHeight * Player.AutofitLarger);
                width = (int)Math.Ceiling(height * startWidth / (double)startHeight);
            }
        }

        if (width > maxWidth)
        {
            width = maxWidth;
            height = (int)Math.Floor(width * startHeight / (double)startWidth);
        }

        if (height > maxHeight)
        {
            height = maxHeight;
            width = (int)Math.Ceiling(height * startWidth / (double)startHeight);
        }

        if (height < maxHeight * 0.1)
        {
            height = (int)(maxHeight * 0.1);
            width = (int)Math.Ceiling(height * startWidth / (double)startHeight);
        }

        Point middlePos = new Point(Left + Width / 2, Top + Height / 2);
        var rect = new RECT(new Rectangle(screen.Bounds.X, screen.Bounds.Y, width, height));

        AddWindowBorders(Handle, ref rect, GetDpi(Handle), !Player.TitleBar);

        width = rect.Width;
        height = rect.Height;

        int left = Convert.ToInt32(middlePos.X - width / 2.0);
        int top = Convert.ToInt32(middlePos.Y - height / 2.0);

        if (!Player.TitleBar)
            top -= Convert.ToInt32(GetTitleBarHeight(Handle, GetDpi(Handle)) / 2.0);

        Rectangle currentRect = new Rectangle(Left, Top, Width, Height);

        if (GetHorizontalLocation(screen) == -1) left = Left;
        if (GetHorizontalLocation(screen) ==  1) left = currentRect.Right - width;

        if (GetVerticalLocation(screen) == -1) top = Top;
        if (GetVerticalLocation(screen) ==  1) top = currentRect.Bottom - height;

        Screen[] screens = Screen.AllScreens;

        int minLeft   = screens.Select(val => GetWorkingArea(Handle, val.WorkingArea).X).Min();
        int maxRight  = screens.Select(val => GetWorkingArea(Handle, val.WorkingArea).Right).Max();
        int minTop    = screens.Select(val => GetWorkingArea(Handle, val.WorkingArea).Y).Min();
        int maxBottom = screens.Select(val => GetWorkingArea(Handle, val.WorkingArea).Bottom).Max();

        if (load)
        {
            string geometryString = Player.GetPropertyString("geometry");

            if (!string.IsNullOrEmpty(geometryString))
            {
                var pos = ParseGeometry(geometryString, width, height);

                if (pos.X != int.MaxValue)
                    left = pos.X;

                if (pos.Y != int.MaxValue)
                    top = pos.Y;
            }
        }

        if (left < minLeft)
            left = minLeft;

        if (left + width > maxRight)
            left = maxRight - width;

        if (top < minTop)
            top = minTop;

        if (top + height > maxBottom)
            top = maxBottom - height;

        uint SWP_NOACTIVATE = 0x0010;
        SetWindowPos(Handle, IntPtr.Zero, left, top, width, height, SWP_NOACTIVATE);
    }

    Point ParseGeometry(string input, int width, int height)
    {
        int x = int.MaxValue;
        int y = int.MaxValue;

        Match match = Regex.Match(input, @"^\+(\d+)%?\+(\d+)%?$");

        if (match.Success)
        {
            Rectangle workingArea = GetWorkingArea(Handle, Screen.FromHandle(Handle).WorkingArea);

            x = int.Parse(match.Groups[1].Value);
            y = int.Parse(match.Groups[2].Value);
            
            x = workingArea.Left + Convert.ToInt32((workingArea.Width - width) / 100.0 * x);
            y = workingArea.Top + Convert.ToInt32((workingArea.Height - height) / 100.0 * y);
        }

        return new Point(x, y);
    }

    public void CycleFullscreen(bool enabled)
    {
        SetModernFullscreen(enabled);
    }

    public int GetHorizontalLocation(Screen screen)
    {
        Rectangle workingArea = GetWorkingArea(Handle, screen.WorkingArea);
        Rectangle rect = new Rectangle(Left - workingArea.X, Top - workingArea.Y, Width, Height);

        if (workingArea.Width / (float)Width < 1.1)
            return 0;

        if (rect.X * 3 < workingArea.Width - rect.Right)
            return -1;

        if (rect.X > (workingArea.Width - rect.Right) * 3)
            return 1;

        return 0;
    }

    public int GetVerticalLocation(Screen screen)
    {
        Rectangle workingArea = GetWorkingArea(Handle, screen.WorkingArea);
        Rectangle rect = new Rectangle(Left - workingArea.X, Top - workingArea.Y, Width, Height);

        if (workingArea.Height / (float)Height < 1.1)
            return 0;

        if (rect.Y * 3 < workingArea.Height - rect.Bottom)
            return -1;

        if (rect.Y > (workingArea.Height - rect.Bottom) * 3)
            return 1;

        return 0;
    }

    public void InitAndBuildContextMenu()
    {
        ContextMenu.Closed += ContextMenu_Closed;
        ContextMenu.UseLayoutRounding = true;

        var (menuBindings, confBindings) = App.InputConf.GetBindings();
        _confBindings = confBindings;

        foreach (Binding binding in menuBindings)
        {
            Binding tempBinding = binding;

            if (!binding.IsMenu)
                continue;

            var menuItem = MenuHelp.Add(ContextMenu.Items, tempBinding.Comment);

            if (menuItem != null)
            {
                menuItem.Click += (sender, args) => {
                    try {
                        TaskHelp.Run(() => {
                            MenuAutoResetEvent.WaitOne();
                            System.Windows.Application.Current.Dispatcher.Invoke(
                                DispatcherPriority.Background, new Action(delegate { }));
                            if (!string.IsNullOrEmpty(tempBinding.Command))
                                Player.Command(tempBinding.Command);                
                        });
                    }
                    catch (Exception ex) {
                        Msg.ShowException(ex);
                    }
                };

            }
        }

        // Add the quick action even when the user's input.conf predates it.
        var resetWindowItem = new WpfControls.MenuItem { Header = "还原初始窗口大小" };
        resetWindowItem.Click += (_, _) => ResetModernWindowSize();
        var resetVideoItem = new WpfControls.MenuItem { Header = "还原初始画面大小和位置" };
        resetVideoItem.Click += (_, _) => ResetVideoView();
        ContextMenu.Items.Insert(0, resetWindowItem);
        ContextMenu.Items.Insert(1, resetVideoItem);
        var pictureItem = new WpfControls.MenuItem { Header = "画面调节（亮度 / 夜间护眼）" };
        pictureItem.Click += (_, _) => ShowPicture();
        var nightItem = new WpfControls.MenuItem { Header = "夜间护眼", IsCheckable = true };
        nightItem.Click += (_, _) => SetModernNightMode(!_modernSettings.NightMode);
        ContextMenu.Opened += (_, _) => nightItem.IsChecked = _modernSettings.NightMode;
        ContextMenu.Items.Insert(2, pictureItem);
        ContextMenu.Items.Insert(3, nightItem);
        ContextMenu.Items.Insert(4, new WpfControls.Separator());
        _contextMenuIsReady = true;
    }
    
    void Player_FileLoaded()
    {
        BeginInvoke(() => {
            SetTitleInternal();

            int interval = (int)(Player.Duration.TotalMilliseconds / 100);

            if (interval < 100)
                interval = 100;

            if (interval > 1000)
                interval = 1000;

            ProgressTimer.Interval = interval;
            UpdateProgressBar();
        });

        string path = Player.GetPropertyString("path");

        path = MainPlayer.ConvertFilePath(path);

        if (path.Contains("://"))
        {
            string title = Player.GetPropertyString("media-title");

            if (!string.IsNullOrEmpty(title) && path != title)
                path = path + "|" + title;
        }

        if (!string.IsNullOrEmpty(path) && path != "-" && path != @"bd://" && path != @"dvd://")
        {
            if (App.Settings.RecentFiles.Contains(path))
                App.Settings.RecentFiles.Remove(path);

            App.Settings.RecentFiles.Insert(0, path);

            while (App.Settings.RecentFiles.Count > App.RecentCount)
                App.Settings.RecentFiles.RemoveAt(App.RecentCount);
        }
    }

    void SetTitle() => BeginInvoke(SetTitleInternal);

    void SetTitleInternal()
    {
        string? title = Title;

        if (title == "${filename}" && Player.Path.ContainsEx("://"))
            title = "${media-title}";

        string text = Player.Expand(title);

        if (text == "(unavailable)" || Player.PlaylistPos == -1)
            text = AppInfo.Name;

        Text = text;
    }

    void SaveWindowProperties()
    {
        if (WindowState == FormWindowState.Normal && WasShown && !_modernFullscreen && !_modernWindowTransition)
        {
            SavePosition();
            App.Settings.WindowSize = ClientSize;
            RememberModernWindowSize();
        }
    }

    void SavePosition()
    {
        Point pos = new Point(Left + Width / 2, Top + Height / 2);
        Screen screen = Screen.FromControl(this);

        int x = GetHorizontalLocation(screen);
        int y = GetVerticalLocation(screen);

        if (x == -1) pos.X = Left;
        if (x ==  1) pos.X = Left + Width;
        if (y == -1) pos.Y = Top;
        if (y ==  1) pos.Y = Top + Height;

        App.Settings.WindowPosition = pos;
        App.Settings.WindowLocation = new Point(x, y);
    }

    protected override CreateParams CreateParams {
        get {
            CreateParams cp = base.CreateParams;
            cp.Style |= 0x00020000 /* WS_MINIMIZEBOX */ | 0x00010000 /* WS_MAXIMIZEBOX */ |
                0x02000000 /* WS_CLIPCHILDREN */;
            return cp;
        }
    }

    string? _title;

    public string? Title {
        get => _title;
        set {
            if (string.IsNullOrEmpty(value))
                return;

            if (value.EndsWith("} - mpv"))
                value = value.Replace("} - mpv", "} - " + AppInfo.Name);

            _title = value;
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case 0x0021: // WM_MOUSEACTIVATE
                // Child default procedures ask the top-level window to activate.
                // Sending this back to mpv recurses through its parent again.
                m.Result = new IntPtr(1); // MA_ACTIVATE: keep the first click
                return;
            case 0x0007: // WM_SETFOCUS
            case 0x0008: // WM_KILLFOCUS
            case 0x0100: // WM_KEYDOWN
            case 0x0101: // WM_KEYUP
            case 0x0104: // WM_SYSKEYDOWN
            case 0x0105: // WM_SYSKEYUP
            case 0x0201: // WM_LBUTTONDOWN
            case 0x0202: // WM_LBUTTONUP
            case 0x0204: // WM_RBUTTONDOWN
            case 0x0205: // WM_RBUTTONUP
            case 0x0206: // WM_RBUTTONDBLCLK
            case 0x0207: // WM_MBUTTONDOWN
            case 0x0208: // WM_MBUTTONUP
            case 0x0209: // WM_MBUTTONDBLCLK
            case 0x020b: // WM_XBUTTONDOWN
            case 0x020c: // WM_XBUTTONUP
            case 0x020e: // WM_MOUSEHWHEEL
            case 0x0280: // WM_IME_REPORT
            case 0x0281: // WM_IME_SETCONTEXT
            case 0x0282: // WM_IME_NOTIFY
            case 0x0283: // WM_IME_CONTROL
            case 0x0284: // WM_IME_COMPOSITIONFULL
            case 0x0285: // WM_IME_SELECT
            case 0x0286: // WM_IME_CHAR
            case 0x0288: // WM_IME_REQUEST
            case 0x0290: // WM_IME_KEYDOWN
            case 0x0291: // WM_IME_KEYUP
            case 0x02a3: // WM_MOUSELEAVE
                ForwardVideoInput(this, ref m);
                break;
            case 0x020a: // WM_MOUSEWHEEL: never forward a handled step twice
                if (ForwardVideoInput(this, ref m)) return;
                break;
            case 0x001A: // WM_SETTINGCHANGE
                UpdateDarkMode();
                break;
            case 0x51: // WM_INPUTLANGCHANGE
                ActivateKeyboardLayout(m.LParam, 0x00000100u /*KLF_SETFORPROCESS*/);
                break;
            case 0x319: // WM_APPCOMMAND
                {
                    string? key = MpvHelp.WM_APPCOMMAND_to_mpv_key((int)(m.LParam.ToInt64() >> 16 & ~0xf000));
                    bool inputMediaKeys = Player.GetPropertyBool("input-media-keys");

                    if (key != null && inputMediaKeys)
                    {
                        Player.Command("keypress " + key);
                        m.Result = new IntPtr(1);
                        return;
                    }
                }
                break;
            case 0x312: // WM_HOTKEY
                GlobalHotkey.Execute(m.WParam.ToInt32());
                break;
            case 0x200: // WM_MOUSEMOVE
                ForwardVideoInput(this, ref m);
                break;
            case 0x203: // WM_LBUTTONDBLCLK
                ForwardVideoInput(this, ref m);
                break;
            case 0xA3: // WM_NCLBUTTONDBLCLK
                if (m.WParam.ToInt32() == 2 && _modernSettings != null) { // HTCAPTION
                    ExecuteDoubleClickAction(_modernSettings.TitleBarDoubleClickAction);
                    return;
                }
                break;
            case 0x2E0: // WM_DPICHANGED
                {
                    if (!WasShown)
                        break;

                    RECT rect = Marshal.PtrToStructure<RECT>(m.LParam);
                    SetWindowPos(Handle, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, 0);
                }
                break;
            case 0x0112: // WM_SYSCOMMAND
                {
                    // with title-bar=no when the window is restored from minimizing the height is too high  
                    if (!Player.TitleBar)
                    {
                        int SC_MINIMIZE = 0xF020;

                        if (m.WParam == (nint)SC_MINIMIZE)
                        {
                            MaximumSize = Size;
                            _maxSizeSet = true;
                        }
                    }
                }
                break;
            case 0x0083: // WM_NCCALCSIZE
                // The custom chrome owns the entire client area.
                // Preserve the native top-left child alignment. Pixel reuse is
                // disabled separately through SWP_NOCOPYBITS during resizing.
                m.Result = IntPtr.Zero;
                return;
            case 0x0046: // WM_WINDOWPOSCHANGING
                DiscardModernResizePixels(m.LParam);
                break;
            case 0x0024: // WM_GETMINMAXINFO
                base.WndProc(ref m);
                if (!_modernFullscreen) SetModernMaximizedWorkArea(m.LParam);
                return;
            case 0x0232: // WM_EXITSIZEMOVE
                EndModernWindowResize();
                RememberModernWindowSize();
                SaveModernSettings();
                PublishModernWindowState();
                break;
            case 0x231: // WM_ENTERSIZEMOVE
            case 0x005: // WM_SIZE
                if (Player.SnapWindow)
                    SnapManager.OnSizeAndEnterSizeMove(this);
                break;
            case 0x214: // WM_SIZING
                BeginModernWindowResize();
                if (Player.KeepaspectWindow)
                {
                    RECT rc = Marshal.PtrToStructure<RECT>(m.LParam);
                    RECT r = rc;
                    SubtractWindowBorders(Handle, ref r, GetDpi(Handle), !Player.TitleBar);

                    int c_w = r.Right - r.Left, c_h = r.Bottom - r.Top;
                    Size videoSize = Player.VideoSize;

                    if (videoSize == Size.Empty)
                        videoSize = new Size(16, 9);

                    double aspect = videoSize.Width / (double)videoSize.Height;
                    int d_w = (int)Math.Ceiling(c_h * aspect - c_w);
                    int d_h = (int)Math.Floor(c_w / aspect - c_h);

                    int[] d_corners = { d_w, d_h, -d_w, -d_h };
                    int[] corners = { rc.Left, rc.Top, rc.Right, rc.Bottom };
                    int corner = GetResizeBorder(m.WParam.ToInt32());

                    if (corner >= 0)
                        corners[corner] -= d_corners[corner];

                    Marshal.StructureToPtr(new RECT(corners[0], corners[1], corners[2], corners[3]), m.LParam, false);
                    m.Result = new IntPtr(1);
                }
                return;
            case 0x84: // WM_NCHITTEST
                // The eight client grips handle resizing without a native frame.
                m.Result = new IntPtr(1); // HTCLIENT
                return;
            case 0x4A: // WM_COPYDATA
                {
                    var copyData = (CopyDataStruct)m.GetLParam(typeof(CopyDataStruct))!;
                    if (copyData.lpData.StartsWith('{')) {
                        try {
                            var request = JsonSerializer.Deserialize<ForwardedOpenRequest>(copyData.lpData);
                            if (request == null || request.Protocol != 1 || request.Identity != InstanceRouting.Identity) return;
                            SaveCurrentPosition();
                            if (request.Mode == "command" && request.Command != null) Player.Command(request.Command);
                            else Player.LoadFiles(request.Files, true, request.Mode == "queue", request.Options);
                            Activate();
                            m.Result = new IntPtr(1);
                        } catch (Exception ex) { Terminal.WriteError(ex.Message); }
                        return;
                    }
                    string[] args = copyData.lpData.Split('\n');
                    string mode = args[0];
                    args = args.Skip(1).ToArray();

                    switch (mode)
                    {
                        case "single":
                            LoadModernFiles(args);
                            break;
                        case "queue":
                            foreach (string file in args)
                                Player.CommandV("loadfile", file, "append");
                            break;
                        case "command":
                            Player.Command(args[0]);
                            break;
                    }

                    Activate();
                }
                return;
            case 0x216: // WM_MOVING
                if (Player.SnapWindow)
                    SnapManager.OnMoving(ref m);
                break;
        }

        if (m.Msg == _taskbarButtonCreatedMessage && Player.TaskbarProgress)
        {
            _taskbar = new Taskbar(Handle);
            ProgressTimer.Start();
        }

        // beep sound when closed using taskbar due to exception
        if (!IsDisposed)
            base.WndProc(ref m);
    }

    bool ForwardVideoInput(Control source, ref Message m)
    {
        bool clientMouse = m.Msg is 0x0200 or 0x0201 or 0x0202 or 0x0203 or
            0x0204 or 0x0205 or 0x0206 or 0x0207 or 0x0208 or 0x0209 or 0x020b or 0x020c;
        bool otherInput = m.Msg is 0x0007 or 0x0008 or 0x0100 or 0x0101 or 0x0104 or 0x0105 or
            0x020a or 0x020e or 0x0280 or 0x0281 or 0x0282 or 0x0283 or 0x0284 or
            0x0285 or 0x0286 or 0x0288 or 0x0290 or 0x0291 or 0x02a3;
        if ((!clientMouse && !otherInput) || Player.Handle == IntPtr.Zero || _videoSurface == null)
            return false;

        // A screenshot preview owns keyboard/mouse input, including remote messages
        // addressed to the disabled player behind it. Do not run playback shortcuts.
        if (_screenshotDialog is { Visible: true } &&
            (clientMouse || m.Msg is 0x0100 or 0x0101 or 0x0104 or 0x0105 or 0x020a or 0x020e))
            return false;

        if (HandleVideoViewInput(source, ref m)) return true;
        if (HandleVideoWheelInput(ref m)) return true;
        if (m.Msg is 0x0100 or 0x0104) ShowFullscreenFooter();

        if (m.Msg == 0x0100 && (Keys)(int)m.WParam == Keys.Escape && _contextMenuIsReady && ContextMenu.IsOpen)
        {
            ContextMenu.IsOpen = false;
            return true;
        }

        IntPtr coordinates = m.LParam;
        if (clientMouse)
        {
            long packed = m.LParam.ToInt64();
            var point = new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
            if (source != _videoSurface)
                point = _videoSurface.PointToClient(source.PointToScreen(point));
            if (source == this && !_videoSurface.ClientRectangle.Contains(point))
                return false;

            coordinates = new IntPtr((point.X & 0xffff) | ((point.Y & 0xffff) << 16));
            _lastCursorPosition = _videoSurface.PointToScreen(point);
            _lastCursorChanged = Environment.TickCount;
            ShowCursor();
            RecordFullscreenPointer(_videoSurface.PointToScreen(point));
            if (m.Msg == 0x0203)
            {
                if (_modernSettings.VideoDoubleClickAction == "input-conf")
                    Player.CommandV("mouse", point.X.ToString(CultureInfo.InvariantCulture),
                        point.Y.ToString(CultureInfo.InvariantCulture), "0", "double");
                else
                    ExecuteDoubleClickAction(_modernSettings.VideoDoubleClickAction);
                return true;
            }
        }

        if (MpvWindowHandle == IntPtr.Zero)
            MpvWindowHandle = FindWindowEx(_videoSurface.Handle, IntPtr.Zero, "mpv", null);
        if (MpvWindowHandle == IntPtr.Zero) return false;
        if (m.Msg is 0x0100 or 0x0104 or 0x020a or 0x020e) AllowNavigationInput();
        m.Result = SendMessage(MpvWindowHandle, m.Msg, m.WParam, coordinates);
        return true;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

        if (_maxSizeSet)
        {
            TaskHelp.Run(() => {
                Thread.Sleep(200);
                BeginInvoke(() => {
                    if (!IsDisposed && !Disposing)
                    {
                        MaximumSize = new Size(int.MaxValue, int.MaxValue);
                        _maxSizeSet = false;
                    }
                });
            });
        }
    }

    void CursorTimer_Tick(object sender, EventArgs e)
    {
        if (!_modernSettings.AutoHideCursor || _videoPanning ||
            !IsVideoViewPoint(_videoSurface.PointToClient(MousePosition)) ||
            ActiveForm != this || ContextMenu.IsVisible)
        {
            ShowCursor();
            return;
        }

        if (IsCursorPosDifferent(_lastCursorPosition))
        {
            _lastCursorPosition = MousePosition;
            _lastCursorChanged = Environment.TickCount;
        }
        else if (Environment.TickCount - _lastCursorChanged > _cursorAutohide)
            HideCursor();
    }

    void ProgressTimer_Tick(object sender, EventArgs e) => UpdateProgressBar();

    void UpdateProgressBar()
    {
        if (Player.TaskbarProgress && _taskbar != null)
            _taskbar.SetValue(Player.GetPropertyDouble("time-pos", false), Player.Duration.TotalSeconds);
    }

    void PropChangeOnTop(bool value) => Ui(() => {
        TopMost = value;
        UpdatePinButton();
        PublishModernWindowState();
    });
    
    void PropChangeAid(string value) => Player.AID = value;

    void PropChangeSid(string value) => Player.SID = value;

    void PropChangeVid(string value) => Player.VID = value;

    void PropChangeTitle(string value) { Title = value; SetTitle(); }
    
    void PropChangeEdition(int value) => Player.Edition = value;
    
    void PropChangeCursorAutohide()
    {
        string strValue = Player.GetPropertyString("cursor-autohide");

        if (strValue == "no")
            _cursorAutohide = 0;
        else if (strValue == "always")
            _cursorAutohide = -1;
        else if (int.TryParse(strValue, out var intValue))
            _cursorAutohide = intValue;
    }

    void PropChangeBorder(bool enabled) {
        Player.Border = enabled;

        BeginInvoke(() => {
            if (!IsFullscreen)
            {
                if (Player.Border && FormBorderStyle == FormBorderStyle.None)
                    FormBorderStyle = FormBorderStyle.Sizable;

                if (!Player.Border && FormBorderStyle == FormBorderStyle.Sizable)
                    FormBorderStyle = FormBorderStyle.None;
            }
        });
    }

    void PropChangeTitleBar(bool enabled)
    {
        if (enabled == Player.TitleBar)
            return;

        Player.TitleBar = enabled;

        // Client chrome needs no legacy native title-bar height jiggle.
        if (_modernHeader != null) return;

        BeginInvoke(() => {
            SetSize(ClientSize.Width, ClientSize.Height, Screen.FromControl(this), false);
            Height += 1;
            Height -= 1;
        });
    }

    void Player_Pause()
    {
        if (_taskbar != null && Player.TaskbarProgress)
            _taskbar.SetState(Player.Paused ? TaskbarStates.Paused : TaskbarStates.Normal);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RestoreModernWindowSize();
        _lastCycleFullscreen = Environment.TickCount;
        SetFormPosAndSize(false, true, true);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        ShowCursor();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        EndHeldNavigation(false);
        base.OnDeactivate(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        InstanceRouting.Register(Handle);

        // Paint before cold WPF/menu work and before the GPU opens its first file.
        RepaintModernWindow();

        WpfApplication.Init();
        Theme.UpdateWpfColors();
        MessageBoxEx.MessageForeground = Theme.Current?.GetBrush("heading");
        MessageBoxEx.MessageBackground = Theme.Current?.GetBrush("background");
        MessageBoxEx.ButtonBackground  = Theme.Current?.GetBrush("highlight");
        InitAndBuildContextMenu();
        GlobalHotkey.RegisterGlobalHotkeys(Handle);
        StrongReferenceMessenger.Default.Send(new MainWindowIsLoadedMessage());
        WasShown = true;
        CommandLine.ProcessCommandLineFiles();
        _startupResumeRequest = false;
        PublishModernWindowState();
    }

    void ContextMenu_Closed(object sender, System.Windows.RoutedEventArgs e) => MenuAutoResetEvent.Set();

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutWindowResizeGrips();
        LayoutFullscreenChrome();
        SaveWindowProperties();
        if (WasShown && !_modernInteractiveResize) PublishModernWindowState();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        ShutdownModernUi();
        InstanceRouting.Unregister();

        if (Player.IsQuitNeeded)
            Player.CommandV("quit");

        if (!Player.ShutdownAutoResetEvent.WaitOne(10000))
            Msg.ShowError(_("Shutdown thread failed to complete within 10 seconds."));

        Player.Destroy();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _mouseDownLocation = PointToScreen(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_videoPanning && IsCursorPosDifferent(_mouseDownLocation) &&
            WindowState == FormWindowState.Normal &&
            e.Button == MouseButtons.Left && !IsMouseInOsc() &&
            Player.GetPropertyBool("window-dragging"))
        {
            var HTCAPTION = new IntPtr(2);
            var WM_NCLBUTTONDOWN = 0xA1;
            ReleaseCapture();
            PostMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
        }
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        SaveWindowProperties();
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);

        if (e.Data!.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            e.Effect = DragDropEffects.Copy;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);

        bool append = ModifierKeys == Keys.Shift;

        if (e.Data!.GetDataPresent(DataFormats.FileDrop))
            Player.LoadFiles(e.Data.GetData(DataFormats.FileDrop) as string[], true, append);
        else if (e.Data.GetDataPresent(DataFormats.Text))
            Player.LoadFiles(new[] { e.Data.GetData(DataFormats.Text)!.ToString()! }, true, append);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Tab || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // prevent annoying beep using alt key
        if (ModifierKeys == Keys.Alt)
            e.SuppressKeyPress = true;

        base.OnKeyDown(e);
    }

    void ShowCursor()
    {
        if (!_isCursorVisible)
        {
            Cursor.Show();
            _isCursorVisible = true;
        }
    }

    void HideCursor()
    {
        if (_isCursorVisible && _cursorAutohide != 0)
        {
            Cursor.Hide();
            _isCursorVisible = false;
        }
    }

    bool IsCursorPosDifferent(Point screenPos)
    {
        float len = 5 * (GetDpi(Handle) / 96f);
        return Math.Abs(screenPos.X - MousePosition.X) > len || Math.Abs(screenPos.Y - MousePosition.Y) > len;
    }

    public static int GetDpi(IntPtr hwnd)
    {
        if (Environment.OSVersion.Version >= WindowsTen1607 && hwnd != IntPtr.Zero)
            return GetDpiForWindow(hwnd);
        else
            using (Graphics gx = Graphics.FromHwnd(hwnd))
                return GetDeviceCaps(gx.GetHdc(), 88 /*LOGPIXELSX*/);
    }

    [DllImport("DwmApi")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, int[] attrValue, int attrSize);
}
