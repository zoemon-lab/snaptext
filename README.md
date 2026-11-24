# SnapText

画面の一部を囲むと、写っている文字を読み取って自動でコピーするアプリです。縦書き・横書きの日本語と英語に対応しています。画像も文字も PC の外には送りません。

**ダウンロードと使い方：https://zoemon-lab.github.io/snaptext/**

| | Mac 版 | Windows 版 |
|---|---|---|
| 必要なもの | macOS 15 以降（Intel / Apple シリコン） | Windows 10 / 11（64 ビット） |
| 範囲を選ぶキー | ⌘ + shift + 9 | Ctrl + Shift + 9 |
| 文字の読み取り | macOS 標準（Vision）。縦書きも読める | 横書きは Windows 標準（Windows.Media.Ocr）、縦書きは同梱の Tesseract |

## フォルダ

- `mac/` … Mac 版（Swift）。`./build.sh` で `SnapText.app`（Xcode が必要）
- `windows/` … Windows 版（C# / .NET 8）。`./build.sh` で `dist/SnapText.exe`
- `site/` … ダウンロードページ
- `testdata/` … 自動試験に使う画像（架空・著作権の切れた文章）
- `licenses/` … 使っている部品のライセンス

`main` に送ると、GitHub の自動処理が Mac 版と Windows 版を作り、試験用の画像で文字が読めることを確かめてから、リリースに置きます。

## ライセンス

使っている部品のライセンスは [licenses/THIRD_PARTY_NOTICES.txt](licenses/THIRD_PARTY_NOTICES.txt) を見てください。
