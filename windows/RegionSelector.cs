using System.Drawing;
using System.Drawing.Imaging;

namespace SnapText;

/// <summary>
/// 画面を暗くして、マウスで囲んだ範囲を切り取る。Esc か右クリックでやめる。
/// モニターごとに1枚ずつ重ねる（拡大率がちがうモニターでもずれないように）。
/// </summary>
public static class RegionSelector
{
    public static Task<Bitmap?> SelectAsync()
    {
        var tcs = new TaskCompletionSource<Bitmap?>();
        var overlays = new List<Overlay>();

        void Finish(Bitmap? result)
        {
            if (tcs.Task.IsCompleted) { result?.Dispose(); return; }
            tcs.TrySetResult(result);
            // マウス操作の処理中に閉じないよう、あとで閉じる（Close で破棄もされる）
            foreach (var o in overlays) o.BeginInvoke(new Action(o.Close));
        }

        foreach (var screen in Screen.AllScreens)
        {
            var b = screen.Bounds;
            var shot = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(shot))
                g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size, CopyPixelOperation.SourceCopy);
            var overlay = new Overlay(shot, b);
            overlay.Selected += Finish;
            overlays.Add(overlay);
        }
        foreach (var o in overlays) o.Show();
        // マウスがあるモニターの重ね窓を前に出し、Esc を受け取れるようにする
        var current = overlays.FirstOrDefault(o => o.ScreenBounds.Contains(Cursor.Position)) ?? overlays[0];
        current.Activate();
        return tcs.Task;
    }

    private sealed class Overlay : Form
    {
        private const int WM_DPICHANGED = 0x02E0;
        private readonly Bitmap _shot;
        public Rectangle ScreenBounds { get; }
        private Point? _start;
        private Rectangle _sel;
        public event Action<Bitmap?>? Selected;

        public Overlay(Bitmap shot, Rectangle bounds)
        {
            _shot = shot;
            ScreenBounds = bounds;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            Bounds = bounds;
        }

        protected override void OnLoad(EventArgs e) { base.OnLoad(e); Bounds = ScreenBounds; }

        // 拡大率のちがうモニターに置いたとき、窓の大きさを勝手に変えさせない
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(_shot, 0, 0);
            using var dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
            if (_sel.Width > 0 && _sel.Height > 0)
            {
                using var region = new Region(ClientRectangle);
                region.Exclude(_sel);
                g.FillRegion(dim, region);
                using var pen = new Pen(Color.FromArgb(0, 150, 255), 2);
                g.DrawRectangle(pen, _sel);
            }
            else
            {
                g.FillRectangle(dim, ClientRectangle);
                const string hint = "読みたい部分をドラッグで囲んでください（Esc でやめる）";
                using var font = new Font("Yu Gothic UI", 16, FontStyle.Bold, GraphicsUnit.Point);
                var size = g.MeasureString(hint, font);
                var p = new PointF((Width - size.Width) / 2, Height * 0.12f);
                using var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
                g.FillRectangle(bg, p.X - 16, p.Y - 10, size.Width + 32, size.Height + 20);
                g.DrawString(hint, font, Brushes.White, p);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Selected?.Invoke(null); return; }
            if (e.Button == MouseButtons.Left) { _start = e.Location; _sel = Rectangle.Empty; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_start is not Point s) return;
            _sel = Rectangle.FromLTRB(Math.Min(s.X, e.X), Math.Min(s.Y, e.Y), Math.Max(s.X, e.X), Math.Max(s.Y, e.Y));
            _sel.Intersect(ClientRectangle);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _start == null) return;
            _start = null;
            if (_sel.Width < 6 || _sel.Height < 6) { _sel = Rectangle.Empty; Invalidate(); return; }
            var crop = _shot.Clone(_sel, PixelFormat.Format32bppArgb);
            Selected?.Invoke(crop);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Selected?.Invoke(null);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _shot.Dispose();
            base.Dispose(disposing);
        }
    }
}
