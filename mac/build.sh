#!/bin/bash
set -e
cd "$(dirname "$0")"

echo "Building SnapText..."
# Intel と Apple シリコン（M1 以降）の両方で動くように作る（Xcode が必要）
if [ -d /Applications/Xcode.app ] && [ -z "$DEVELOPER_DIR" ]; then
  export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
fi
swift build -c release --arch arm64 --arch x86_64
BIN=".build/apple/Products/Release/SnapText"

APP="SnapText.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
mkdir -p "$APP/Contents/Resources"

cp "$BIN" "$APP/Contents/MacOS/SnapText"
cp "Info.plist" "$APP/Contents/Info.plist"
cp "AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
cp "Sources/SnapText/Resources/MenuBarIcon.png" "$APP/Contents/Resources/MenuBarIcon.png"
cp "Sources/SnapText/Resources/MenuBarIcon@2x.png" "$APP/Contents/Resources/MenuBarIcon@2x.png"

# 作り直しても「同じアプリ」と認識されるよう、識別名だけで本人確認する署名にする
# （ふつうのad-hoc署名だと作り直すたびに別アプリ扱いになり、画面収録の許可が毎回求められる）
codesign --force --deep --sign - -r='designated => identifier "io.github.zoemon-lab.snaptext"' "$APP"

echo "Done: $(pwd)/$APP"
