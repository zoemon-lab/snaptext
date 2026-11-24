using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SnapText;

internal static class Program
{
    private const string InstanceName = "Local\\SnapText-zoemon-lab";
    private const string ShowEventName = "Local\\SnapText-zoemon-lab-show";

    [STAThread]
    private static int Main(string[] args)
    {
        // 自動テスト用：SnapText.exe --selftest 結果.txt 画像1 画像2 …
        // 窓は出さず、横書き・縦書き・自動の3通りで読んだ結果をファイルに書いて終わる
        if (args.Length >= 3 && args[0] == "--selftest") return SelfTest(args[1], args.Skip(2));

        // 2つ目を起動したら、すでに動いている SnapText の窓を出して終わる
        using var mutex = new Mutex(true, InstanceName, out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            return 0;
        }

        ApplicationConfiguration.Initialize();
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        Application.Run(new TrayApp(showEvent));
        return 0;
    }

    private static int SelfTest(string outPath, IEnumerable<string> images)
    {
        var sb = new System.Text.StringBuilder();
        bool failed = false;
        foreach (var path in images)
        {
            foreach (var mode in new[] { ReadMode.Auto, ReadMode.Horizontal, ReadMode.Vertical })
            {
                try
                {
                    using var bmp = new Bitmap(path);
                    var r = OcrService.RecognizeAsync(bmp, mode).GetAwaiter().GetResult();
                    sb.AppendLine($"=== {Path.GetFileName(path)} [{mode} → {r.UsedMode}]");
                    sb.AppendLine(r.Error ?? r.Text ?? "(文字なし)");
                    if (mode == ReadMode.Auto && (r.Error != null || r.Text == null)) failed = true;
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"=== {Path.GetFileName(path)} [{mode}] 例外: {ex}");
                    failed = true;
                }
            }
        }
        File.WriteAllText(outPath, sb.ToString(), new System.Text.UTF8Encoding(false));
        return failed ? 1 : 0;
    }
}

/// <summary>通知領域（画面右下）に常駐し、Ctrl+Shift+9 で範囲を選んで文字を読む</summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly NotifyIcon _tray;
    private readonly ResultForm _form;
    private readonly HotKeyWindow _hotKey;
    private readonly SynchronizationContext _ui;
    private bool _busy;
    private Bitmap? _lastImage;

    public TrayApp(EventWaitHandle showEvent)
    {
        var icon = LoadIcon();
        _form = new ResultForm(icon);
        _form.CaptureRequested += () => _ = CaptureAsync();
        _form.ImageReceived += (bmp, mode) => _ = ReadAsync(bmp, mode);
        _form.RereadRequested += mode => { if (_lastImage != null) _ = ReadAsync(new Bitmap(_lastImage), mode); };
        _ = _form.Handle; // UI スレッドで窓を先に作っておく
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        var menu = new ContextMenuStrip();
        menu.Items.Add("範囲を選んで文字を読む (Ctrl+Shift+9)", null, (_, _) => _ = CaptureAsync());
        menu.Items.Add("コピーした画像の文字を読む", null, (_, _) => _form.ReadClipboard());
        menu.Items.Add("SnapText の窓を開く", null, (_, _) => _form.Present());
        menu.Items.Add(new ToolStripSeparator());
        var startup = new ToolStripMenuItem("Windows の起動時に自動で起動") { Checked = IsStartupEnabled(), CheckOnClick = true };
        startup.CheckedChanged += (_, _) => SetStartup(startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Quit());

        _tray = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Text = "SnapText（Ctrl+Shift+9 で文字を読む）",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _form.Present(); };

        _hotKey = new HotKeyWindow(() => _ = CaptureAsync());
        if (!_hotKey.Registered)
            _form.ShowMessage("Ctrl+Shift+9 はほかのアプリが使っています。ボタンか右下のアイコンから読んでください");

        // ほかの場所から2回目に起動されたら窓を出す
        ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => _ui.Post(_ => _form.Present(), null), null, -1, false);

        // 起動したことがわかるように、最初は窓を出す（Windows 起動時の自動起動では出さない）
        if (!Environment.GetCommandLineArgs().Contains("--background")) _form.Present();
    }

    private async Task CaptureAsync()
    {
        if (_busy) return;
        _busy = true;
        bool wasVisible = _form.Visible;
        try
        {
            _form.Hide();                 // 窓がじゃまにならないよう、選ぶあいだは隠す
            await Task.Delay(150);         // 窓が消えきるのを待ってから画面を撮る
            var bmp = await RegionSelector.SelectAsync();
            if (bmp == null) { if (wasVisible) _form.Present(); return; }
            await ReadAsync(bmp, ReadMode.Auto);
        }
        catch (Exception ex)
        {
            _form.ShowMessage("範囲を選べませんでした：" + ex.Message);
        }
        finally { _busy = false; }
    }

    private async Task ReadAsync(Bitmap bmp, ReadMode mode)
    {
        if (!ReferenceEquals(bmp, _lastImage))
        {
            _lastImage?.Dispose();
            _lastImage = new Bitmap(bmp);
        }
        _form.ShowWorking();
        try
        {
            var result = await OcrService.RecognizeAsync(bmp, mode);
            _form.ShowResult(result);
        }
        catch (Exception ex)
        {
            _form.ShowMessage("読み取りに失敗しました：" + ex.Message);
        }
        finally { bmp.Dispose(); }
    }

    private void Quit()
    {
        _tray.Visible = false;
        _hotKey.Dispose();
        _form.AllowClose = true;
        _form.Close();
        ExitThread();
    }

    private static Icon? LoadIcon()
    {
        using var s = typeof(TrayApp).Assembly.GetManifestResourceStream("SnapText.AppIcon.ico");
        return s == null ? null : new Icon(s);
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("SnapText") != null;
    }

    private static void SetStartup(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue("SnapText", $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue("SnapText", false);
    }
}

/// <summary>どのアプリを使っていても Ctrl+Shift+9 を受け取る</summary>
internal sealed class HotKeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    private const uint VK_9 = 0x39;
    private readonly Action _onPressed;
    public bool Registered { get; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotKeyWindow(Action onPressed)
    {
        _onPressed = onPressed;
        CreateHandle(new CreateParams());
        Registered = RegisterHotKey(Handle, 1, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_9);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) _onPressed();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            UnregisterHotKey(Handle, 1);
            DestroyHandle();
        }
    }
}
