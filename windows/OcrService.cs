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
        using var bmp = Prepare(source);

        if (mode == ReadMode.Vertical)
            return await RecognizeVerticalAsync(bmp);

        var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ja"))
                     ?? OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
        {
            if (mode == ReadMode.Auto && bmp.Height > bmp.Width)
                return await RecognizeVerticalAsync(bmp);
            return new OcrOutcome(null, ReadMode.Horizontal,
                "Windows の日本語の文字認識が入っていません（設定 → 時刻と言語 → 言語 → 日本語 → 言語のオプション → 光学式文字認識）");
        }

        var result = await engine.RecognizeAsync(await ToSoftwareBitmapAsync(bmp));
        var lines = new List<string>();
        var counts = new List<int>();
        var aspects = new List<double>();
        foreach (var line in result.Lines)
        {
            var words = line.Words
                .Select(w => new TextUtil.Word(w.Text, w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height))
                .ToList();
            if (words.Count == 0) continue;
            var text = TextUtil.JoinWords(words);
            lines.Add(text);
            counts.Add(text.Count(c => !char.IsWhiteSpace(c)));
            double left = words.Min(w => w.X), right = words.Max(w => w.X + w.Width);
            double top = words.Min(w => w.Y), bottom = words.Max(w => w.Y + w.Height);
            aspects.Add((bottom - top) / Math.Max(1, right - left));
        }

        if (mode == ReadMode.Auto && TextUtil.LooksVertical(counts, aspects, bmp.Width, bmp.Height))
        {
            var vertical = await RecognizeVerticalAsync(bmp);
            if (!string.IsNullOrWhiteSpace(vertical.Text)) return vertical;
        }

        var joined = string.Join("\n", lines);
        return new OcrOutcome(joined.Length > 0 ? joined : null, ReadMode.Horizontal, null);
    }

    private static Task<OcrOutcome> RecognizeVerticalAsync(Bitmap bmp)
    {
        byte[] png;
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
    private static Bitmap Prepare(Bitmap src)
    {
        double scale = 1;
        int longSide = Math.Max(src.Width, src.Height);
        if (longSide < 800) scale = 2;
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
