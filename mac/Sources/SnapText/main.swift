import Cocoa

// 自動テスト用：SnapText --selftest 画像1 画像2 … で、読み取った文字を表示して終わる（窓は出さない）
if CommandLine.arguments.count > 2, CommandLine.arguments[1] == "--selftest" {
    let sema = DispatchSemaphore(value: 0)
    var failed = false
    Task {
        for path in CommandLine.arguments.dropFirst(2) {
            guard let cg = NSImage(contentsOfFile: path)?.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
                print("=== \(path)\n(画像を開けません)"); failed = true; continue
            }
            let text = await OCRService.recognize(cg)
            print("=== \(path)\n\(text ?? "(文字なし)")")
            if text == nil { failed = true }
        }
        sema.signal()
    }
    sema.wait()
    exit(failed ? 1 : 0)
}

let delegate = AppDelegate()
NSApplication.shared.delegate = delegate
NSApplication.shared.setActivationPolicy(.accessory)
NSApplication.shared.run()
