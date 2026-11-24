using SnapText;
using W = SnapText.TextUtil.Word;
int fail = 0;
void Check(string name, string got, string want) { var ok = got == want; if (!ok) fail++; Console.WriteLine($"{(ok ? "OK " : "NG ")} {name}: 「{got}」" + (ok ? "" : $" ← 期待「{want}」")); }
void CheckB(string name, bool got, bool want) => Check(name, got.ToString(), want.ToString());

// Windows の日本語認識は1文字ずつ返す（文字の間はほぼ詰まっている）
var jp = "一次関数のグラフ".Select((c, i) => new W(c.ToString(), i * 20, 0, 19, 20)).ToList();
Check("日本語の文字は空白なしでつなぐ", TextUtil.JoinWords(jp), "一次関数のグラフ");
var en = new List<W> { new("The", 0, 0, 30, 12), new("slope", 36, 0, 45, 12), new("is", 86, 0, 14, 12), new("2.", 105, 0, 12, 12) };
Check("英語は空白でつなぐ", TextUtil.JoinWords(en), "The slope is 2.");
var mix = new List<W> { new("問", 0, 0, 20, 20), new("題", 20, 0, 20, 20), new("3x", 60, 0, 20, 20), new("+", 85, 0, 10, 20), new("2y", 100, 0, 20, 20), new("を", 122, 0, 20, 20) };
Check("離れていれば日本語と英数字の間に空白", TextUtil.JoinWords(mix), "問題 3x + 2yを");
Check("縦書き部品の余分な空白を消す", TextUtil.RemoveCjkSpaces("春 は あ け ぼ の 。\n\n な り ゆ く \n"), "春はあけぼの。\nなりゆく");
Check("改行をとる（日本語）", TextUtil.JoinLines("春はあけぼの。やうやう白く\r\nなりゆく山ぎは"), "春はあけぼの。やうやう白くなりゆく山ぎは");
Check("改行をとる（英語）", TextUtil.JoinLines("Please bring your\nnotebook."), "Please bring your notebook.");
CheckB("1文字ずつの行が並ぶ→縦書き部品で読み直す", TextUtil.LooksVertical(new[] { 1, 1, 2, 1, 1 }, 300, 600), true);
CheckB("ふつうの横書き→そのまま", TextUtil.LooksVertical(new[] { 17, 16, 30 }, 1200, 360), false);
CheckB("Windowsが縦書きを読めた（長い行）→そのまま", TextUtil.LooksVertical(new[] { 13, 15, 16 }, 400, 760), false);
CheckB("何も読めず縦長→縦書き部品を試す", TextUtil.LooksVertical(Array.Empty<int>(), 400, 760), true);
CheckB("何も読めず横長→そのまま", TextUtil.LooksVertical(Array.Empty<int>(), 800, 200), false);
CheckB("見出し1行だけ→そのまま", TextUtil.LooksVertical(new[] { 9 }, 600, 80), false);
// 試験用の Windows で見つかった問題
var comma = new List<W> { new("一", 0, 0, 20, 20), new("定", 20, 0, 20, 20), new("で", 40, 0, 20, 20), new("、", 60, 0, 20, 20), new("傾", 95, 0, 20, 20), new("き", 115, 0, 20, 20) };
Check("「、」のあとに空白を入れない", TextUtil.JoinWords(comma), "一定で、傾き");
var digits = new List<W> { new("=", 0, 0, 10, 20), new("1", 16, 0, 10, 20), new("2", 27, 0, 10, 20), new(",", 38, 0, 4, 20) };
Check("くっついた数字を割らない", TextUtil.JoinWords(digits), "= 12,");
CheckB("英語の行を見分ける", TextUtil.IsMostlyLatin("Please bring your textbOOk and notebOOk."), true);
CheckB("日本語の行は英語扱いしない", TextUtil.IsMostlyLatin("問題3x + 2y = 12 を解きなさい。"), false);
CheckB("縦横まじり（長い行＋細切れ）を見分ける", TextUtil.IsMixedLayout(new[] { 1, 1, 12, 1, 2, 30, 1 }), true);
CheckB("縦書きだけ（細切れのみ）はまじりではない", TextUtil.IsMixedLayout(new[] { 1, 1, 2, 1, 1 }), false);
CheckB("横書きだけはまじりではない", TextUtil.IsMixedLayout(new[] { 17, 16, 30 }), false);
Console.WriteLine(fail == 0 ? "すべて合格" : $"{fail} 件不合格");
return fail;
