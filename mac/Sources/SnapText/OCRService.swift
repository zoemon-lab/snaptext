import Cocoa
import Vision

/// 画像から文字を読み取る（すべてMacの中で処理。外部には送らない）
/// macOS 15 以降の RecognizeTextRequest を使う。旧APIでは読めなかった縦書きも読める。
enum OCRService {
    static func recognize(_ cgImage: CGImage) async -> String? {
        var request = RecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        request.recognitionLanguages = [
            Locale.Language(identifier: "ja-JP"),
            Locale.Language(identifier: "en-US"),
        ]

        let image = upscaledIfSmall(cgImage)
        do {
            let observations = try await request.perform(on: image)
            let lines = observations.compactMap { $0.topCandidates(1).first?.string }
            let text = lines.joined(separator: "\n")
            return text.isEmpty ? nil : text
        } catch {
            return nil
        }
    }

    /// 小さな範囲を選んだときは文字が細かすぎるので、2倍に拡大してから読む
    private static func upscaledIfSmall(_ image: CGImage) -> CGImage {
        guard max(image.width, image.height) < 800 else { return image }
        let w = image.width * 2, h = image.height * 2
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return image }
        ctx.setFillColor(CGColor(gray: 1, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        ctx.interpolationQuality = .high
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        return ctx.makeImage() ?? image
    }

    /// 改行をとる：日本語どうしはそのままつなぎ、英単語どうしは空白をはさむ
    static func joinLines(_ text: String) -> String {
        let lines = text.components(separatedBy: "\n").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        var result = ""
        for line in lines {
            if let last = result.last, let first = line.first,
               last.isASCII, !last.isWhitespace, first.isASCII, !first.isPunctuation {
                result += " "
            }
            result += line
        }
        return result
    }
}
