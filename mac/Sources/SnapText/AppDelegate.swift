import Cocoa
import Carbon.HIToolbox

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private var hotKeyManager: HotKeyManager?
    private var isCapturing = false
    private lazy var resultWindow: ResultWindowController = {
        let wc = ResultWindowController()
        wc.onCapture = { [weak self] in self?.startCapture() }
        wc.onImage = { [weak self] in self?.recognize(nsImage: $0) }
        return wc
    }()

    func applicationDidFinishLaunching(_ notification: Notification) {
        setupMainMenu()
        setupStatusItem()
        hotKeyManager = HotKeyManager(
            keyCode: UInt32(kVK_ANSI_9),
            modifiers: UInt32(cmdKey | shiftKey)
        ) { [weak self] in
            self?.startCapture()
        }
        // 起動したことがわかるように、最初は窓を出す
        resultWindow.present()
    }

    /// 画像ファイルをアプリのアイコンにドロップ／「このアプリケーションで開く」で開いたとき
    func application(_ application: NSApplication, open urls: [URL]) {
        guard let url = urls.first, let image = NSImage(contentsOf: url) else { return }
        recognize(nsImage: image)
    }

    /// アプリをもう一度ダブルクリックしたら窓を出す
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        resultWindow.present()
        return true
    }

    /// メニューバー常駐アプリでも ⌘C・⌘V・⌘A・⌘Z が効くように、編集メニューを用意する
    private func setupMainMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem()
        main.addItem(appItem)
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "窓を閉じる", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        appMenu.addItem(withTitle: "SnapText を終了", action: #selector(quit), keyEquivalent: "q").target = self
        appItem.submenu = appMenu

        let editItem = NSMenuItem()
        main.addItem(editItem)
        let edit = NSMenu(title: "編集")
        edit.addItem(withTitle: "取り消す", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: "やり直す", action: Selector(("redo:")), keyEquivalent: "Z")
        edit.addItem(.separator())
        edit.addItem(withTitle: "カット", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "コピー", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "ペースト", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "すべてを選択", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        NSApp.mainMenu = main
    }

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem.button {
            let icon = Bundle.main.image(forResource: "MenuBarIcon")
                ?? NSImage(systemSymbolName: "text.viewfinder", accessibilityDescription: "SnapText")
            icon?.isTemplate = true
            button.image = icon
        }

        let menu = NSMenu()
        menu.addItem(withTitle: "範囲を選んで文字を読む (⌘⇧9)", action: #selector(triggerCapture), keyEquivalent: "")
            .target = self
        menu.addItem(withTitle: "コピーした画像の文字を読む", action: #selector(readClipboard), keyEquivalent: "")
            .target = self
        menu.addItem(withTitle: "SnapText の窓を開く", action: #selector(openWindow), keyEquivalent: "")
            .target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "終了", action: #selector(quit), keyEquivalent: "q")
            .target = self
        statusItem.menu = menu
    }

    @objc private func triggerCapture() { startCapture() }
    @objc private func openWindow() { resultWindow.present() }
    @objc private func quit() { NSApplication.shared.terminate(nil) }

    @objc private func readClipboard() {
        if let image = ImagePasteboard.image(from: NSPasteboard.general) {
            recognize(nsImage: image)
        } else {
            resultWindow.showMessage("コピーされた画像がありません（⌘⌃⇧4 でスクショをコピーできます）")
        }
    }

    // MARK: - 読み取り

    private func recognize(nsImage: NSImage) {
        guard let cg = nsImage.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
            resultWindow.showMessage("この画像は読み込めませんでした")
            return
        }
        recognize(cgImage: cg)
    }

    private func recognize(cgImage: CGImage) {
        resultWindow.showWorking()
        Task { @MainActor in
            let text = await OCRService.recognize(cgImage)
            self.resultWindow.show(text: text)
        }
    }

    private func startCapture() {
        guard !isCapturing else { return }
        isCapturing = true

        // 撮影は Mac 標準の screencapture が行うが、呼び出し元の SnapText に「画面収録」の許可が必要。
        // 許可がないと撮影が黙って失敗するので、撮れなかったときに案内を出す（下の terminationHandler）。

        // 窓がじゃまにならないよう、選ぶあいだは隠す
        let wasVisible = resultWindow.window?.isVisible ?? false
        resultWindow.hideForCapture()

        let tempURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("snaptext-\(UUID().uuidString).png")

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        process.arguments = ["-i", "-x", tempURL.path]

        process.terminationHandler = { [weak self] _ in
            DispatchQueue.main.async {
                guard let self else { return }
                self.isCapturing = false
                defer { try? FileManager.default.removeItem(at: tempURL) }

                guard let image = NSImage(contentsOf: tempURL),
                      let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
                    if !CGPreflightScreenCaptureAccess() {
                        // 許可がオフで撮影に失敗した
                        self.resultWindow.showPermissionHelp()
                    } else if wasVisible {
                        self.resultWindow.present() // esc でキャンセルした
                    }
                    return
                }
                self.recognize(cgImage: cg)
            }
        }

        do {
            try process.run()
        } catch {
            isCapturing = false
            resultWindow.showMessage("スクリーンショットを起動できませんでした")
        }
    }
}
