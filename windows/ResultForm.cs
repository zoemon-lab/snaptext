using System.Drawing;

namespace SnapText;

/// <summary>読み取った文字を表示する窓。画像のドラッグ・Ctrl+V の貼り付けもここで受け付ける。</summary>
public sealed class ResultForm : Form
{
    public event Action? CaptureRequested;
    public event Action<Bitmap, ReadMode>? ImageReceived;
    public event Action<ReadMode>? RereadRequested;
    public bool AllowClose { get; set; }

    private readonly TextBox _text = new();
    private readonly Label _status = new();
    private readonly CheckBox _join = new();
    private readonly Button _reread = new();
    private string _original = "";
    private ReadMode _lastMode = ReadMode.Horizontal;

    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    public ResultForm(Icon? icon)
    {
        Text = "SnapText";
        if (icon != null) Icon = icon;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = new Font("Yu Gothic UI", 10F);
        ClientSize = new Size(560, 440);
        MinimumSize = new Size(460, 340);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;

        var capture = MakeButton("範囲を選んで読む   Ctrl+Shift+9", (_, _) => CaptureRequested?.Invoke());
        var paste = MakeButton("コピーした画像を読む", (_, _) => ReadClipboard());
        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        top.Controls.AddRange(new Control[] { capture, paste });

        var hint = new Label
        {
            Text = "画像ファイルをこの窓にドラッグ、または Ctrl+V で貼り付けても読めます",
            AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 4, 3, 6),
        };

        _text.Multiline = true;
        _text.ScrollBars = ScrollBars.Vertical;
        _text.Dock = DockStyle.Fill;
        _text.Font = new Font("Yu Gothic UI", 12F);
        _text.AllowDrop = true;
        _text.KeyDown += TextKeyDown;

        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        _status.Font = new Font("Yu Gothic UI", 10F, FontStyle.Bold);

        _reread.AutoSize = true;
        _reread.Visible = false;
        _reread.Click += (_, _) => RereadRequested?.Invoke(_lastMode == ReadMode.Vertical ? ReadMode.Horizontal : ReadMode.Vertical);

        _join.Text = "改行をとる";
        _join.AutoSize = true;
        _join.Anchor = AnchorStyles.Left;
        _join.CheckedChanged += (_, _) => { if (_original.Length > 0) { _text.Text = Displayed(); CopyCurrent(); } };

        var copy = MakeButton("コピー", (_, _) => CopyCurrent());

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 6, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(_reread, 1, 0);
        bottom.Controls.Add(_join, 2, 0);
        bottom.Controls.Add(copy, 3, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(hint, 0, 1);
        root.Controls.Add(_text, 0, 2);
        root.Controls.Add(bottom, 0, 3);
        Controls.Add(root);

        foreach (Control c in new Control[] { this, _text })
        {
            c.DragEnter += (_, e) => e.Effect = ImageFromData(e.Data) is Bitmap b ? Dispose(b, DragDropEffects.Copy) : DragDropEffects.None;
            c.DragDrop += (_, e) => { if (ImageFromData(e.Data) is Bitmap b) ImageReceived?.Invoke(b, ReadMode.Auto); };
        }

        SetStatus("ここに読み取った文字が出ます", SystemColors.GrayText);
    }

    private static DragDropEffects Dispose(Bitmap b, DragDropEffects e) { b.Dispose(); return e; }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 3, 8, 3) };
        b.Click += onClick;
        return b;
    }

    // ---------- 表示 ----------

    public void Present()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        // 他のアプリの前に出す
        TopMost = true; TopMost = false;
        Activate();
        _text.Focus();
    }

    public void ShowWorking()
    {
        SetStatus("読み取り中…", SystemColors.GrayText);
        Present();
    }

    /// <summary>読み取り結果を表示し、自動でコピーする</summary>
    public void ShowResult(OcrOutcome r)
    {
        _lastMode = r.UsedMode;
        _reread.Text = r.UsedMode == ReadMode.Vertical ? "横書きとして読み直す" : "縦書きとして読み直す";
        _reread.Visible = true;

        if (r.Error != null) { SetStatus(r.Error, Color.Firebrick); Present(); return; }
        if (string.IsNullOrWhiteSpace(r.Text))
        {
            SetStatus("文字が見つかりませんでした", Color.DarkOrange);
            Present();
            return;
        }
        _original = r.Text;
        _text.Text = Displayed();
        CopyCurrent(r.UsedMode == ReadMode.Vertical ? "（縦書き）" : "");
        Present();
    }

    public void ShowMessage(string message)
    {
        SetStatus(message, Color.DarkOrange);
        Present();
    }

    private string Displayed()
    {
        var t = _join.Checked ? TextUtil.JoinLines(_original) : _original;
        return t.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private void CopyCurrent(string note = "")
    {
        var t = _text.Text;
        if (t.Length == 0) return;
        try
        {
            Clipboard.SetText(t);
            SetStatus($"✓ {t.Replace("\r\n", "").Length}文字をコピーしました{note}", Color.SeaGreen);
        }
        catch (Exception)
        {
            SetStatus("コピーできませんでした。もう一度「コピー」を押してください", Color.Firebrick);
        }
    }

    private void SetStatus(string s, Color c) { _status.Text = s; _status.ForeColor = c; }

    // ---------- 画像の受け取り ----------

    public void ReadClipboard()
    {
        if (ImageFromClipboard() is Bitmap b) ImageReceived?.Invoke(b, ReadMode.Auto);
        else ShowMessage("コピーされた画像がありません（Windows + Shift + S でスクショをコピーできます）");
    }

    private void TextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.V && ImageFromClipboard() is Bitmap b)
        {
            e.SuppressKeyPress = true;
            ImageReceived?.Invoke(b, ReadMode.Auto);
        }
        else if (e.Control && e.KeyCode == Keys.A)
        {
            _text.SelectAll();
            e.SuppressKeyPress = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 文字欄の外で Ctrl+V を押したときも画像を読む
        if (!_text.Focused && e.Control && e.KeyCode == Keys.V && ImageFromClipboard() is Bitmap b)
        {
            e.SuppressKeyPress = true;
            ImageReceived?.Invoke(b, ReadMode.Auto);
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>クリップボードに画像がある（文字だけのときは null。ふつうの貼り付けにする）</summary>
    private static Bitmap? ImageFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
                foreach (var f in Clipboard.GetFileDropList())
                    if (f != null && LoadImageFile(f) is Bitmap fb) return fb;
            if (!Clipboard.ContainsText() && Clipboard.GetImage() is Image img) return new Bitmap(img);
        }
        catch (Exception) { }
        return null;
    }

    private static Bitmap? ImageFromData(IDataObject? data)
    {
        if (data == null) return null;
        try
        {
            if (data.GetData(DataFormats.FileDrop) is string[] files)
                foreach (var f in files)
                    if (LoadImageFile(f) is Bitmap fb) return fb;
            if (data.GetData(DataFormats.Bitmap) is Image img) return new Bitmap(img);
        }
        catch (Exception) { }
        return null;
    }

    private static Bitmap? LoadImageFile(string path)
    {
        if (!ImageExts.Contains(Path.GetExtension(path).ToLowerInvariant())) return null;
        try
        {
            using var fs = File.OpenRead(path);
            using var img = Image.FromStream(fs);
            return new Bitmap(img);
        }
        catch (Exception) { return null; }
    }

    // 「×」で閉じてもアプリは終わらない（通知領域に残る）
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
