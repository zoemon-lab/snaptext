using System.Text;
using System.Text.RegularExpressions;

namespace SnapText;

/// <summary>読み取った文字の整形（Windows に依存しない部分。Mac でも試験できる）</summary>
public static class TextUtil
{
    /// <summary>Windows の文字認識が返す「単語」1つ分</summary>
    public readonly record struct Word(string Text, double X, double Y, double Width, double Height);

    /// <summary>日本語・中国語の文字か（かな・漢字・全角記号）</summary>
    public static bool IsCjk(char c) =>
        (c >= '　' && c <= 'ヿ') ||   // 和文記号・ひらがな・カタカナ
        (c >= '㐀' && c <= '䶿') ||   // 漢字（拡張A）
        (c >= '一' && c <= '鿿') ||   // 漢字
        (c >= '豈' && c <= '﫿') ||   // 互換漢字
        (c >= '＀' && c <= '￯');     // 全角英数・半角カナ

    /// <summary>
    /// 1行分の単語をつなぐ。Windows の日本語認識は1文字ずつ区切って返すので、
    /// そのまま空白でつなぐと「一 次 関 数」になってしまう。
    /// 英数字どうし、または間がはっきり空いているときだけ空白を入れる。
    /// </summary>
    public static string JoinWords(IReadOnlyList<Word> words)
    {
        var sb = new StringBuilder();
        Word? prev = null;
        foreach (var w in words)
        {
            if (string.IsNullOrEmpty(w.Text)) continue;
            if (prev is Word p && sb.Length > 0)
            {
                char a = sb[sb.Length - 1], b = w.Text[0];
                double gap = w.X - (p.X + p.Width);
                double h = Math.Max(1, Math.Max(p.Height, w.Height));
                bool space;
                if (!IsCjk(a) && !IsCjk(b))
                    space = gap > h * 0.15;          // 英数字どうし：単語の間だけ（「1 2」のように数字を割らない）
                else if (IsCjkPunct(a) || IsCjkPunct(b))
                    space = false;                   // 「、」「。」「（」のまわりには入れない
                else
                    space = gap > h * 0.9;           // 日本語がからむ：はっきり離れているときだけ
                if (space) sb.Append(' ');
            }
            sb.Append(w.Text);
            prev = w;
        }
        return sb.ToString();
    }

    /// <summary>和文の句読点・かっこ（字形の中に余白があるので、間が空いて見える）</summary>
    public static bool IsCjkPunct(char c) =>
        (c >= '\u3000' && c <= '\u303F') || "（）［］｛｝｟｠｢｣、。，．・：；！？".IndexOf(c) >= 0;

    /// <summary>日本語の文字と文字のあいだに入った余分な空白を消す（縦書き部品の出力用）</summary>
    public static string RemoveCjkSpaces(string s)
    {
        var lines = s.Replace("\r\n", "\n").Split('\n')
            .Select(l => Regex.Replace(l, @"(?<=[　-ヿ㐀-䶿一-鿿豈-﫿＀-￯])[ 　]+(?=[　-ヿ㐀-䶿一-鿿豈-﫿＀-￯])", ""))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    /// <summary>「改行をとる」：日本語どうしはそのままつなぎ、英単語どうしは空白をはさむ</summary>
    public static string JoinLines(string text)
    {
        var sb = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (sb.Length > 0)
            {
                char a = sb[sb.Length - 1], b = line[0];
                if (a < 128 && !char.IsWhiteSpace(a) && b < 128 && !char.IsPunctuation(b)) sb.Append(' ');
            }
            sb.Append(line);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Windows 標準の結果から「縦書き用の部品で読み直したほうがよい」と判断する。
    /// Windows 標準は、きれいな縦書きなら読める（行が縦長になる）。読めないときは1〜2文字ずつの細切れの行になるか、何も読めない。
    /// </summary>
    /// <param name="lineCharCounts">各行の文字数（空白を除く）</param>
    public static bool LooksVertical(IReadOnlyList<int> lineCharCounts, int imageWidth, int imageHeight)
    {
        if (lineCharCounts.Count == 0)
            return imageHeight > imageWidth * 1.2;           // 何も読めず、画像が縦長
        if (lineCharCounts.Count < 3) return false;
        var sorted = lineCharCounts.OrderBy(n => n).ToList();
        return sorted[sorted.Count / 2] <= 2;                // ほとんどの行が1〜2文字
    }

    /// <summary>英字・数字・記号（半角）がほとんどの行か。英語の文字認識で読み直す対象</summary>
    public static bool IsMostlyLatin(string line)
    {
        int latin = 0, total = 0;
        foreach (var c in line)
        {
            if (char.IsWhiteSpace(c)) continue;
            total++;
            if (c < 128) latin++;
        }
        return total >= 4 && latin * 10 >= total * 8;
    }
}
