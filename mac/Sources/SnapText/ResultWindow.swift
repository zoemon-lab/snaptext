import Cocoa
import UniformTypeIdentifiers

/// 読み取った文字を表示する窓。画像のドラッグ・⌘V 貼り付けもここで受け付ける。
final class ResultWindowController: NSWindowController, NSWindowDelegate {
    var onCapture: (() -> Void)?
    var onImage: ((NSImage) -> Void)?

    private let textView = ImageAwareTextView()
    private let statusLabel = NSTextField(labelWithString: "")
    private let joinCheckbox = NSButton(checkboxWithTitle: "改行をとる", target: nil, action: nil)
    private var originalText = ""

    init() {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 520, height: 420),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered, defer: false
        )
        window.title = "SnapText"
        window.minSize = NSSize(width: 420, height: 300)
        window.isReleasedWhenClosed = false
        super.init(window: window)
        window.delegate = self
        buildUI()
        window.center()
        showHint()
    }

    required init?(coder: NSCoder) { fatalError() }

    // MARK: - 画面の組み立て

    private func buildUI() {
        guard let window else { return }
        let root = DropView()
        root.onImage = { [weak self] in self?.onImage?($0) }
        window.contentView = root

        let captureButton = NSButton(title: "範囲を選んで読む  ⌘⇧9", target: self, action: #selector(captureTapped))
        captureButton.bezelStyle = .rounded
        captureButton.controlSize = .large

        let pasteButton = NSButton(title: "コピーした画像を読む", target: self, action: #selector(pasteTapped))
        pasteButton.bezelStyle = .rounded
        pasteButton.controlSize = .large

        let topRow = NSStackView(views: [captureButton, pasteButton])
        topRow.spacing = 8

        let hint = NSTextField(labelWithString: "画像ファイルをこの窓にドラッグ、または ⌘V で貼り付けても読めます")
        hint.font = .systemFont(ofSize: 12)
        hint.textColor = .secondaryLabelColor

        textView.isRichText = false
        textView.font = .systemFont(ofSize: 15)
        textView.textContainerInset = NSSize(width: 8, height: 8)
        textView.isAutomaticQuoteSubstitutionEnabled = false
        textView.allowsUndo = true
        textView.onImage = { [weak self] in self?.onImage?($0) }
        textView.minSize = NSSize(width: 0, height: 0)
        textView.maxSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        textView.isVerticallyResizable = true
        textView.autoresizingMask = [.width]
        textView.textContainer?.widthTracksTextView = true

        let scroll = NSScrollView()
        scroll.documentView = textView
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder

        joinCheckbox.target = self
        joinCheckbox.action = #selector(joinToggled)

        statusLabel.font = .systemFont(ofSize: 13, weight: .medium)
        statusLabel.textColor = .systemGreen
        statusLabel.lineBreakMode = .byTruncatingTail
        statusLabel.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        let copyButton = NSButton(title: "コピー", target: self, action: #selector(copyTapped))
        copyButton.bezelStyle = .rounded
        copyButton.controlSize = .large
        copyButton.keyEquivalent = "\r"
        copyButton.keyEquivalentModifierMask = [.command]

        let spacer = NSView()
        spacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        let bottomRow = NSStackView(views: [statusLabel, spacer, joinCheckbox, copyButton])
        bottomRow.spacing = 12

        let stack = NSStackView(views: [topRow, hint, scroll, bottomRow])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: 16, left: 16, bottom: 16, right: 16)
        stack.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            stack.topAnchor.constraint(equalTo: root.topAnchor),
            stack.bottomAnchor.constraint(equalTo: root.bottomAnchor),
            scroll.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -32),
            bottomRow.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -32),
            scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 160),
        ])
    }

    // MARK: - 表示

    func present() {
        NSApp.activate(ignoringOtherApps: true)
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
        window?.makeFirstResponder(textView)
    }

    func hideForCapture() {
        window?.orderOut(nil)
    }

    private func showHint() {
        setStatus("ここに読み取った文字が出ます", color: .secondaryLabelColor)
    }

    func showWorking() {
        setStatus("読み取り中…", color: .secondaryLabelColor)
        present()
    }

    /// 読み取り結果を表示し、自動でコピーする
    func show(text: String?) {
        guard let text, !text.isEmpty else {
            setStatus("文字が見つかりませんでした", color: .systemOrange)
            present()
            return
        }
        originalText = text
        textView.string = displayedText()
        copyCurrent()
        present()
    }

    /// 「画面収録」の許可がオフで撮れなかったときの案内。設定の画面も開く
    func showPermissionHelp() {
        setStatus("撮れませんでした：設定の「画面収録」で SnapText をオンにしてください", color: .systemRed)
        present()
        if let url = URL(string: "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    func showMessage(_ message: String) {
        setStatus(message, color: .systemOrange)
        present()
    }

    private func displayedText() -> String {
        joinCheckbox.state == .on ? OCRService.joinLines(originalText) : originalText
    }

    private func copyCurrent() {
        let text = textView.string
        guard !text.isEmpty else { return }
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(text, forType: .string)
        setStatus("✓ \(text.count)文字をコピーしました", color: .systemGreen)
    }

    private func setStatus(_ s: String, color: NSColor) {
        statusLabel.stringValue = s
        statusLabel.textColor = color
    }

    // MARK: - ボタン

    @objc private func captureTapped() { onCapture?() }

    @objc private func pasteTapped() {
        if let image = ImagePasteboard.image(from: NSPasteboard.general) {
            onImage?(image)
        } else {
            showMessage("コピーされた画像がありません（⌘⌃⇧4 でスクショをコピーできます）")
        }
    }

    @objc private func copyTapped() { copyCurrent() }

    @objc private func joinToggled() {
        guard !originalText.isEmpty else { return }
        textView.string = displayedText()
        copyCurrent()
    }
}

/// 窓全体で画像のドラッグを受ける
final class DropView: NSView {
    var onImage: ((NSImage) -> Void)?

    override init(frame: NSRect) {
        super.init(frame: frame)
        registerForDraggedTypes([.fileURL, .png, .tiff])
    }
    required init?(coder: NSCoder) { fatalError() }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        ImagePasteboard.image(from: sender.draggingPasteboard) != nil ? .copy : []
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        guard let image = ImagePasteboard.image(from: sender.draggingPasteboard) else { return false }
        onImage?(image)
        return true
    }
}

/// 文字欄：画像が貼り付け・ドロップされたら、文字として入れずに読み取りに回す
final class ImageAwareTextView: NSTextView {
    var onImage: ((NSImage) -> Void)?

    override func paste(_ sender: Any?) {
        if let image = ImagePasteboard.image(from: NSPasteboard.general) {
            onImage?(image)
        } else {
            pasteAsPlainText(sender)
        }
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        if let image = ImagePasteboard.image(from: sender.draggingPasteboard) {
            onImage?(image)
            return true
        }
        return super.performDragOperation(sender)
    }
}

/// クリップボード／ドラッグの中から画像を取り出す
enum ImagePasteboard {
    static func image(from pb: NSPasteboard) -> NSImage? {
        // 1) 画像ファイル（Finderからのドラッグやコピー）
        let options: [NSPasteboard.ReadingOptionKey: Any] = [
            .urlReadingFileURLsOnly: true,
            .urlReadingContentsConformToTypes: [UTType.image.identifier],
        ]
        if let url = (pb.readObjects(forClasses: [NSURL.self], options: options) as? [URL])?.first {
            return NSImage(contentsOf: url)
        }
        // 2) 画像そのもの（スクショのコピーなど）。文字も入っている場合は文字の貼り付けを優先
        if pb.string(forType: .string) == nil, NSImage.canInit(with: pb) {
            return NSImage(pasteboard: pb)
        }
        return nil
    }
}
