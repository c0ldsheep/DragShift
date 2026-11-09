import AppKit
import AVFoundation
import CoreText
import Foundation
import ImageIO
import PDFKit
import UniformTypeIdentifiers

/// Real files made on the fly: pictures, scans, PDFs, video with sound, audio. Used by the tests (in a fresh temp
/// folder that's deleted afterwards) and by the app's `--selftest` (in a folder you choose, kept so you can look).
public final class SampleFiles {
    public let dir: URL
    private let keep: Bool

    public init() throws {
        dir = FileManager.default.temporaryDirectory.appendingPathComponent("dragshift-tests-\(UUID().uuidString)")
        keep = false
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    }

    /// Files go into `dir`, and stay there.
    public init(in dir: URL) throws {
        self.dir = dir
        keep = true
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    }

    deinit { if !keep { try? FileManager.default.removeItem(at: dir) } }

    public func url(_ name: String) -> URL { dir.appendingPathComponent(name) }

    public func files() -> [String] {
        ((try? FileManager.default.contentsOfDirectory(atPath: dir.path)) ?? []).filter { !$0.hasPrefix(".") }.sorted()
    }

    /// Left half red, right half blue, optional transparent border.
    public static func image(_ w: Int, _ h: Int, transparentBorder: Int = 0, noise: Bool = false) -> CGImage {
        let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!,
                            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.clear(CGRect(x: 0, y: 0, width: w, height: h))
        let inner = CGRect(x: 0, y: 0, width: w, height: h).insetBy(dx: CGFloat(transparentBorder), dy: CGFloat(transparentBorder))
        ctx.setFillColor(CGColor(srgbRed: 0.9, green: 0.1, blue: 0.1, alpha: 1))
        ctx.fill(CGRect(x: inner.minX, y: inner.minY, width: inner.width / 2, height: inner.height))
        ctx.setFillColor(CGColor(srgbRed: 0.1, green: 0.2, blue: 0.9, alpha: 1))
        ctx.fill(CGRect(x: inner.midX, y: inner.minY, width: inner.width / 2, height: inner.height))
        if noise, let data = ctx.data {
            let px = data.bindMemory(to: UInt8.self, capacity: ctx.bytesPerRow * h)
            var seed: UInt32 = 7
            for i in 0..<(ctx.bytesPerRow * h) where i % 4 != 3 {
                seed = seed &* 1_664_525 &+ 1_013_904_223
                px[i] = UInt8(truncatingIfNeeded: Int(px[i]) / 2 + Int(seed >> 25))
            }
        }
        return ctx.makeImage()!
    }

    public static func textImage(_ lines: [String], width: Int = 1400, fontSize: CGFloat = 44) -> CGImage {
        let h = Int(fontSize * 2) * lines.count + 80
        let ctx = CGContext(data: nil, width: width, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!,
                            bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)!
        ctx.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: width, height: h))
        let font = CTFontCreateWithName("Helvetica" as CFString, fontSize, nil)
        for (i, s) in lines.enumerated() {
            let a = NSAttributedString(string: s, attributes: [.font: font, .foregroundColor: NSColor.black])
            let line = CTLineCreateWithAttributedString(a)
            ctx.textPosition = CGPoint(x: 40, y: CGFloat(h) - 40 - fontSize - CGFloat(i) * fontSize * 2)
            CTLineDraw(line, ctx)
        }
        return ctx.makeImage()!
    }

    @discardableResult
    public func writeImage(_ img: CGImage, _ name: String, type: UTType, props: [CFString: Any] = [:]) throws -> URL {
        let u = url(name)
        let dest = CGImageDestinationCreateWithURL(u as CFURL, type.identifier as CFString, 1, nil)!
        CGImageDestinationAddImage(dest, img, props as CFDictionary)
        guard CGImageDestinationFinalize(dest) else { throw DragShiftError("fixture write failed") }
        return u
    }

    /// A phone-style JPEG: stored sideways with EXIF orientation 6, plus GPS and camera tags.
    public func phonePhoto(_ name: String = "Photo.jpg") throws -> URL {
        try writeImage(SampleFiles.image(400, 200, noise: true), name, type: .jpeg, props: [
            kCGImagePropertyOrientation: 6,
            kCGImageDestinationLossyCompressionQuality: 1.0,
            kCGImagePropertyGPSDictionary: [kCGImagePropertyGPSLatitude: 19.076, kCGImagePropertyGPSLatitudeRef: "N",
                                            kCGImagePropertyGPSLongitude: 72.877, kCGImagePropertyGPSLongitudeRef: "E"],
            kCGImagePropertyExifDictionary: [kCGImagePropertyExifDateTimeOriginal: "2026:10:01 10:00:00",
                                             kCGImagePropertyExifLensModel: "Test Lens"],
            kCGImagePropertyTIFFDictionary: [kCGImagePropertyTIFFMake: "Apple", kCGImagePropertyTIFFModel: "iPhone 12 mini"],
        ])
    }

    public func pdf(_ name: String, pages: [String]) throws -> URL {
        let text = NSMutableAttributedString()
        for (i, p) in pages.enumerated() {
            text.append(NSAttributedString(string: p, attributes: [.font: NSFont.systemFont(ofSize: 14)]))
            if i < pages.count - 1 {
                // Enough blank lines to push the next block onto a new page.
                text.append(NSAttributedString(string: String(repeating: "\n", count: 60),
                                               attributes: [.font: NSFont.systemFont(ofSize: 14)]))
            }
        }
        let u = url(name)
        try DocOps.write(text, as: .pdf, to: u)
        return u
    }

    /// 2 seconds of 320x240 video at 30 fps, with a 440 Hz tone.
    public func video(_ name: String = "Clip.mov", seconds: Double = 2) async throws -> URL {
        let u = url(name)
        let writer = try AVAssetWriter(outputURL: u, fileType: .mov)
        let vIn = AVAssetWriterInput(mediaType: .video, outputSettings: [
            AVVideoCodecKey: AVVideoCodecType.h264, AVVideoWidthKey: 320, AVVideoHeightKey: 240,
        ])
        let adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: vIn, sourcePixelBufferAttributes: [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
            kCVPixelBufferWidthKey as String: 320, kCVPixelBufferHeightKey as String: 240,
        ])
        let aIn = AVAssetWriterInput(mediaType: .audio, outputSettings: [
            AVFormatIDKey: kAudioFormatMPEG4AAC, AVSampleRateKey: 44_100, AVNumberOfChannelsKey: 1, AVEncoderBitRateKey: 64_000,
        ])
        vIn.expectsMediaDataInRealTime = false
        aIn.expectsMediaDataInRealTime = false
        writer.add(vIn)
        writer.add(aIn)
        writer.startWriting()
        writer.startSession(atSourceTime: .zero)

        // Audio: mono 16-bit PCM sine, handed to the AAC encoder in 1024-frame chunks.
        let rate = 44_100.0
        var asbd = AudioStreamBasicDescription(mSampleRate: rate, mFormatID: kAudioFormatLinearPCM,
                                               mFormatFlags: kLinearPCMFormatFlagIsSignedInteger | kLinearPCMFormatFlagIsPacked,
                                               mBytesPerPacket: 2, mFramesPerPacket: 1, mBytesPerFrame: 2,
                                               mChannelsPerFrame: 1, mBitsPerChannel: 16, mReserved: 0)
        var fmt: CMAudioFormatDescription?
        CMAudioFormatDescriptionCreate(allocator: nil, asbd: &asbd, layoutSize: 0, layout: nil, magicCookieSize: 0,
                                       magicCookie: nil, extensions: nil, formatDescriptionOut: &fmt)
        let frames = Int(seconds * 30)
        let total = Int(rate * seconds)
        var f = 0, done = 0
        // The writer interleaves tracks, so feed whichever track is further behind in time.
        while f < frames || done < total {
            let videoTime = Double(f) / 30, audioTime = Double(done) / rate
            let videoReady = f < frames && vIn.isReadyForMoreMediaData
            let audioReady = done < total && aIn.isReadyForMoreMediaData
            if !videoReady && !audioReady { try await Task.sleep(nanoseconds: 1_000_000); continue }
            if videoReady && (!audioReady || videoTime <= audioTime) {
                var pb: CVPixelBuffer?
                CVPixelBufferPoolCreatePixelBuffer(nil, adaptor.pixelBufferPool!, &pb)
                let buf = pb!
                CVPixelBufferLockBaseAddress(buf, [])
                let row = CVPixelBufferGetBytesPerRow(buf)
                let base = CVPixelBufferGetBaseAddress(buf)!.bindMemory(to: UInt8.self, capacity: 240 * row)
                for y in 0..<240 {
                    for x in 0..<320 {
                        let i = y * row + x * 4
                        base[i] = UInt8((x + f * 4) % 256); base[i + 1] = UInt8(y % 256); base[i + 2] = UInt8((f * 8) % 256); base[i + 3] = 255
                    }
                }
                CVPixelBufferUnlockBaseAddress(buf, [])
                adaptor.append(buf, withPresentationTime: CMTime(value: CMTimeValue(f), timescale: 30))
                f += 1
                if f == frames { vIn.markAsFinished() }
            } else {
                let n = min(1024, total - done)
                var samples = [Int16](repeating: 0, count: n)
                for i in 0..<n { samples[i] = Int16(sin(Double(done + i) * 2 * .pi * 440 / rate) * 12_000) }
                var block: CMBlockBuffer?
                let bytes = n * 2
                CMBlockBufferCreateWithMemoryBlock(allocator: nil, memoryBlock: nil, blockLength: bytes, blockAllocator: nil,
                                                   customBlockSource: nil, offsetToData: 0, dataLength: bytes, flags: 0,
                                                   blockBufferOut: &block)
                samples.withUnsafeBytes { raw in
                    _ = CMBlockBufferReplaceDataBytes(with: raw.baseAddress!, blockBuffer: block!, offsetIntoDestination: 0, dataLength: bytes)
                }
                var sb: CMSampleBuffer?
                CMAudioSampleBufferCreateReadyWithPacketDescriptions(allocator: nil, dataBuffer: block!, formatDescription: fmt!,
                                                                     sampleCount: n,
                                                                     presentationTimeStamp: CMTime(value: CMTimeValue(done), timescale: CMTimeScale(rate)),
                                                                     packetDescriptions: nil, sampleBufferOut: &sb)
                aIn.append(sb!)
                done += n
                if done == total { aIn.markAsFinished() }
            }
        }
        await writer.finishWriting()
        guard writer.status == .completed else { throw writer.error ?? DragShiftError("video fixture failed") }
        return u
    }

    public func wav(_ name: String = "Tone.wav", seconds: Double = 1) throws -> URL {
        let u = url(name)
        let format = AVAudioFormat(standardFormatWithSampleRate: 44_100, channels: 2)!
        let file = try AVAudioFile(forWriting: u, settings: [
            AVFormatIDKey: kAudioFormatLinearPCM, AVSampleRateKey: 44_100, AVNumberOfChannelsKey: 2,
            AVLinearPCMBitDepthKey: 16, AVLinearPCMIsFloatKey: false,
        ])
        let frames = AVAudioFrameCount(44_100 * seconds)
        let buf = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: frames)!
        buf.frameLength = frames
        for c in 0..<2 {
            for i in 0..<Int(frames) { buf.floatChannelData![c][i] = Float(sin(Double(i) * 2 * .pi * 440 / 44_100) * 0.4) }
        }
        try file.write(from: buf)
        return u
    }
}
