using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SnapText;

public enum ReadMode { Auto, Horizontal, Vertical }

public sealed record OcrOutcome(string? Text, ReadMode UsedMode, string? Error);

/// <summary>
/// 画像から文字を読む。すべて PC の中で処理し、外部には送らない。
/// 横書き：Windows 標準の文字認識（Windows.Media.Ocr）
/// 縦書き：Tesseract（jpn_vert）。Windows 標準は縦書きを読めないため
/// </summary>
public static class OcrService
{
    private static readonly object TessLock = new();
    private static Tesseract.TesseractEngine? _tess;

    public static async Task<OcrOutcome> RecognizeAsync(Bitmap source, ReadMode mode)
    {
        if (mode == ReadMode.Vertical)
            return await RecognizeVerticalAsync(source);

        using var bmp = Prepare(source);
        var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ja"))
                     ?? OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
        {
            if (mode == ReadMode.Auto && source.Height > source.Width)
                return await RecognizeVerticalAsync(source);
            return new OcrOutcome(null, ReadMode.Horizontal,
                "Windows の日本語の文字認識が入っていません（設定 → 時刻と言語 → 言語 → 日本語 → 言語のオプション → 光学式文字認識）");
        }

        var soft = await ToSoftwareBitmapAsync(bmp);
        var lines = ReadLines(await engine.RecognizeAsync(soft));

        // 英語の行は、英語の文字認識で読み直す（日本語の認識だと「textbOOk」のように大文字小文字が乱れるため）
        if (lines.Any(l => TextUtil.IsMostlyLatin(l.Text)) && !engine.RecognizerLanguage.LanguageTag.StartsWith("en"))
        {
            var en = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
            if (en != null)
            {
                var enLines = ReadLines(await en.RecognizeAsync(soft));
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!TextUtil.IsMostlyLatin(lines[i].Text)) continue;
                    var best = enLines.OrderByDescending(e => Overlap(e.Box, lines[i].Box)).FirstOrDefault();
                    if (best.Text != null && Overlap(best.Box, lines[i].Box) > 0.5 && TextUtil.IsMostlyLatin(best.Text))
                        lines[i] = lines[i] with { Text = best.Text };
                }
            }
        }

        // Windows 標準で読めなかった縦書き（細切れになった）は、縦書き用の部品で読み直す
        var counts = lines.Select(l => CharCount(l.Text)).ToList();
        if (mode == ReadMode.Auto && TextUtil.LooksVertical(counts, bmp.Width, bmp.Height))
        {
            // 縦書きと横書きが混ざっているときは、細切れになった部分だけを切り出して読み直し、横書きの行はそのまま使う
            if (TextUtil.IsMixedLayout(counts))
            {
                var mixed = await RecognizeMixedAsync(source, bmp, lines);
                if (mixed != null) return mixed;
            }
            var vertical = await RecognizeVerticalAsync(source);
            if (!string.IsNullOrWhiteSpace(vertical.Text)) return vertical;
        }

        var joined = string.Join("\n", lines.Select(l => l.Text));
        return new OcrOutcome(joined.Length > 0 ? joined : null, ReadMode.Horizontal, null);
    }

    private static int CharCount(string s) => s.Count(c => !char.IsWhiteSpace(c));

    private static async Task<OcrOutcome?> RecognizeMixedAsync(Bitmap source, Bitmap scaled, List<Line> lines)
    {
        var fragments = lines.Where(l => CharCount(l.Text) <= 2).ToList();
        var rest = lines.Where(l => CharCount(l.Text) > 2).ToList();
        if (fragments.Count == 0 || rest.Count == 0) return null;

        // 縦書きの部分を切り出す。Windows 標準は縦書きの一部の文字しか拾わないことがあるので、
        // 細切れの行の範囲ではなく「横書きの行がない側」を上から下まで全部使う
        var longLines = rest.Where(l => CharCount(l.Text) >= 6).ToList();
        if (longLines.Count == 0) longLines = rest;
        var area = fragments.Select(f => f.Box).Aggregate(RectangleF.Union);
        float hLeft = longLines.Min(l => l.Box.Left), hRight = longLines.Max(l => l.Box.Right);
        float gap = longLines.Select(l => l.Box.Height).OrderBy(h => h).ElementAt(longLines.Count / 2) * 0.3f;
        RectangleF cut;
        if (area.Left + area.Width / 2 < hLeft)                       // 縦書きが左側
            cut = RectangleF.FromLTRB(0, 0, Math.Max(area.Right, hLeft - gap), scaled.Height);
        else if (area.Left + area.Width / 2 > hRight)                 // 縦書きが右側
            cut = RectangleF.FromLTRB(Math.Min(area.Left, hRight + gap), 0, scaled.Width, scaled.Height);
        else                                                          // 上下に並んでいるなど：細切れの範囲に余白をつける
        {
            cut = area;
            float pad = fragments.Select(f => f.Box.Height).OrderBy(h => h).ElementAt(fragments.Count / 2) * 0.6f;
            cut.Inflate(pad, pad);
        }
        float k = (float)source.Width / scaled.Width;            // 拡大前の座標にもどす
        var rect = Rectangle.Round(new RectangleF(cut.X * k, cut.Y * k, cut.Width * k, cut.Height * k));
        rect.Intersect(new Rectangle(0, 0, source.Width, source.Height));
        if (rect.Width < 8 || rect.Height < 8) return null;

        using var crop = source.Clone(rect, PixelFormat.Format32bppArgb);
        var vertical = await RecognizeVerticalAsync(crop);
        if (string.IsNullOrWhiteSpace(vertical.Text)) return null;

        // 左にあるほうから並べる
        var horizontalText = string.Join("\n", rest.Select(l => l.Text));
        float horizontalLeft = rest.Min(l => l.Box.Left);
        var text = area.Left <= horizontalLeft
            ? vertical.Text + "\n" + horizontalText
            : horizontalText + "\n" + vertical.Text;
        return new OcrOutcome(text, ReadMode.Vertical, null);
    }

    private readonly record struct Line(string Text, RectangleF Box);

    private static List<Line> ReadLines(OcrResult result)
    {
        var list = new List<Line>();
        foreach (var line in result.Lines)
        {
            var words = line.Words
                .Select(w => new TextUtil.Word(w.Text, w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height))
                .ToList();
            if (words.Count == 0) continue;
            float left = (float)words.Min(w => w.X), top = (float)words.Min(w => w.Y);
            float right = (float)words.Max(w => w.X + w.Width), bottom = (float)words.Max(w => w.Y + w.Height);
            list.Add(new Line(TextUtil.JoinWords(words), RectangleF.FromLTRB(left, top, right, bottom)));
        }
        return list;
    }

    /// <summary>2つの枠の重なり（小さいほうの面積に対する割合）</summary>
    private static double Overlap(RectangleF a, RectangleF b)
    {
        var i = RectangleF.Intersect(a, b);
        if (i.IsEmpty) return 0;
        double small = Math.Min(a.Width * a.Height, b.Width * b.Height);
        return small <= 0 ? 0 : i.Width * i.Height / small;
    }

    private static Task<OcrOutcome> RecognizeVerticalAsync(Bitmap source)
    {
        // 縦書き部品は、拡大すると読みまちがいが増える。とても小さい画像のときだけ2倍にする
        byte[] png;
        using (var bmp = Prepare(source, upscaleBelow: 400))
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); png = ms.ToArray(); }

        return Task.Run(() =>
        {
            try
            {
                lock (TessLock)
                {
                    _tess ??= new Tesseract.TesseractEngine(
                        Path.Combine(AppContext.BaseDirectory, "tessdata"), "jpn_vert", Tesseract.EngineMode.LstmOnly);
                    using var pix = Tesseract.Pix.LoadFromMemory(png);
                    using var page = _tess.Process(pix, Tesseract.PageSegMode.SingleBlockVertText);
                    var text = TextUtil.RemoveCjkSpaces(page.GetText() ?? "");
                    return new OcrOutcome(text.Length > 0 ? text : null, ReadMode.Vertical, null);
                }
            }
            catch (Exception ex)
            {
                // 縦書き部品は Microsoft Visual C++ ランタイムを使う。入っていない PC では読み込みに失敗する
                var msg = ex is DllNotFoundException || ex.InnerException is DllNotFoundException
                          || ex.Message.Contains("library", StringComparison.OrdinalIgnoreCase)
                    ? "縦書きを読むには「Visual C++ 再頒布可能パッケージ」が必要です（Microsoft の vc_redist.x64.exe を入れてください）"
                    : "縦書きの読み取り部品を起動できませんでした：" + ex.Message;
                return new OcrOutcome(null, ReadMode.Vertical, msg);
            }
        });
    }

    /// <summary>白背景にそろえ、小さすぎる画像は2倍に、大きすぎる画像は縮めてから読む</summary>
    private static Bitmap Prepare(Bitmap src, int upscaleBelow = 800)
    {
        double scale = 1;
        int longSide = Math.Max(src.Width, src.Height);
        if (longSide < upscaleBelow) scale = 2;
        int max = (int)OcrEngine.MaxImageDimension;
        if (longSide * scale > max) scale = (double)max / longSide;

        int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, new Rectangle(0, 0, w, h));
        return dst;
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bmp)
    {
        var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
