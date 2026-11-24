# SnapText Windows版

画面の一部を囲むと、写っている文字を読み取って**自動でコピー**するアプリです。
Mac 版 SnapText（`../SnapText`）と同じ使い方です。画像も文字も PC の外には送りません。

## 使い方

1. `SnapText.exe` を好きな場所（例: ドキュメント）に置いてダブルクリック
   - インストールは不要です。管理者権限もいりません
   - 「Windows によって PC が保護されました」と出たら「詳細情報」→「実行」
   - 初回の起動は少し時間がかかります（部品を展開するため）
2. **Ctrl + Shift + 9** を押す → 画面が暗くなるので、読みたい部分をドラッグで囲む（Esc でやめる）
3. 窓に文字が出て、すでにコピーされています。そのまま Ctrl + V で貼り付け

ほかの読ませ方：
- 画像ファイルを窓にドラッグ
- 窓で Ctrl + V（Windows + Shift + S で撮ったスクショなど）
- 「コピーした画像を読む」ボタン／右下のアイコンのメニュー

窓の「改行をとる」で改行を除いた文章にしてコピーし直せます。「×」で閉じても右下（通知領域）に残ります。
右下アイコンのメニューで「Windows の起動時に自動で起動」を選べます。

## 縦書き

- 横書き・英語は Windows 標準の文字認識で読みます
- 縦書きらしいときは、自動で縦書き用の部品（Tesseract）に切り替えます
- 判定をまちがえたときは、窓の「縦書きとして読み直す」「横書きとして読み直す」を押してください
- 縦書きと横書きが混ざった範囲は、どちらか一方しか読めません。分けて囲んでください

## うまくいかないとき

| 表示 | 対処 |
|---|---|
| 日本語の文字認識が入っていません | 設定 → 時刻と言語 → 言語と地域 → 日本語の「…」→ 言語のオプション → 「光学式文字認識」を追加 |
| 縦書きを読むには「Visual C++ 再頒布可能パッケージ」が必要です | Microsoft 公式の `vc_redist.x64.exe` を入れる（横書きはそのままで読めます） |
| Ctrl+Shift+9 はほかのアプリが使っています | ボタンか右下のアイコンから読む |

## 作り方（Mac から）

```bash
./build.sh
```

`.NET 8 SDK`（`~/.dotnet/dotnet`）が必要です。`dist/SnapText.exe`（約 98MB、単体で動く）ができます。
文字の整形処理の試験は `~/.dotnet/dotnet run --project tests`（Mac で動く）。

## 使っている部品とライセンス

- Windows.Media.Ocr（Windows 標準）
- [Tesseract](https://github.com/tesseract-ocr/tesseract)（Apache License 2.0）、.NET 用ラッパー [charlesw/tesseract](https://github.com/charlesw/tesseract)（Apache License 2.0）
- 縦書きの学習データ `tessdata/jpn_vert.traineddata`：[tesseract-ocr/tessdata](https://github.com/tesseract-ocr/tessdata)（Apache License 2.0）
