import AVFoundation
import CoreMedia
import Foundation

/// Video and audio through AVFoundation (hardware encoders on Apple Silicon).
/// MP3, FLAC, OGG and WebM use ffmpeg only if it's already installed.
public enum MediaFormat: String, Sendable, CaseIterable {
    case mp4, mov, hevc, gif, m4a, wav, aiff, caf, mp3, flac, webm

    public var title: String { self == .hevc ? "HEVC" : rawValue.uppercased() }
    public var ext: String {
        switch self {
        case .hevc: return "mp4"
        case .aiff: return "aif"
        default: return rawValue
        }
    }
    public var needsFFmpeg: Bool { [.mp3, .flac, .webm].contains(self) }
    public var available: Bool { !needsFFmpeg || Tools.ffmpeg != nil }
    public var isAudio: Bool { [.m4a, .wav, .aiff, .caf, .mp3, .flac].contains(self) }
}

public enum MediaOps {

    public static func hasVideo(_ url: URL) async -> Bool {
        let tracks = (try? await AVURLAsset(url: url).loadTracks(withMediaType: .video)) ?? []
        return !tracks.isEmpty
    }

    public static func duration(_ url: URL) async -> Double {
        let d = (try? await AVURLAsset(url: url).load(.duration)) ?? .zero
        return d.seconds.isFinite ? d.seconds : 0
    }

    /// Export with one of Apple's presets. `progress` gets 0...1.
    public static func export(_ asset: AVAsset, preset: String, type: AVFileType, to url: URL,
                              range: CMTimeRange? = nil, progress: ((Double) -> Void)? = nil,
                              configure: ((AVAssetExportSession) -> Void)? = nil) async throws {
        guard let session = AVAssetExportSession(asset: asset, presetName: preset) else {
            throw DragShiftError("This file can't be exported that way.")
        }
        if let range { session.timeRange = range }
        configure?(session)
        session.shouldOptimizeForNetworkUse = true
        let ticker = Task {
            while !Task.isCancelled {
                progress?(Double(session.progress))
                try? await Task.sleep(nanoseconds: 200_000_000)
            }
        }
        defer { ticker.cancel() }
        try await Naming.atomically(url) { tmp in
            if #available(macOS 15, *) {
                try await session.export(to: tmp, as: type)
            } else {
                session.outputURL = tmp
                session.outputFileType = type
                await session.export()
                if session.status != .completed {
                    throw session.error ?? DragShiftError("Export failed.")
                }
            }
        }
    }

    /// The main conversion entry point for video and audio files.
    public static func convert(_ url: URL, to format: MediaFormat, dir: URL, gifFPS: Double = 12,
                               gifWidth: Int = 480, gifMaxSeconds: Double = 20,
                               progress: ((Double) -> Void)? = nil) async throws -> URL {
        let asset = AVURLAsset(url: url)
        let out = Naming.output(for: url, ext: format.ext, suffix: format == .hevc ? "HEVC" : nil, in: dir)
        switch format {
        case .mp4:
            try await export(asset, preset: AVAssetExportPresetHighestQuality, type: .mp4, to: out, progress: progress)
        case .mov:
            try await export(asset, preset: AVAssetExportPresetHighestQuality, type: .mov, to: out, progress: progress)
        case .hevc:
            try await export(asset, preset: AVAssetExportPresetHEVCHighestQuality, type: .mp4, to: out, progress: progress)
        case .m4a:
            try await export(asset, preset: AVAssetExportPresetAppleM4A, type: .m4a, to: out, progress: progress)
        case .gif:
            try await gif(url, to: out, fps: gifFPS, width: gifWidth, maxSeconds: gifMaxSeconds, progress: progress)
        case .wav, .aiff, .caf:
            try await pcm(asset, to: out, format: format)
        case .mp3, .flac, .webm:
            guard let ffmpeg = Tools.ffmpeg else { throw DragShiftError("\(format.title) needs ffmpeg (brew install ffmpeg).") }
            var args = ["-hide_banner", "-loglevel", "error", "-y", "-i", url.path]
            switch format {
            case .mp3: args += ["-vn", "-codec:a", "libmp3lame", "-q:a", "2"]
            case .flac: args += ["-vn", "-codec:a", "flac"]
            default: args += ["-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "33", "-row-mt", "1", "-c:a", "libopus"]
            }
            try Naming.atomically(out) { tmp in try Shell.run(ffmpeg, args + [tmp.path]) }
        }
        return out
    }

    /// Smaller video: 1080p HEVC for anything bigger than 1080p, otherwise 720p/medium H.264.
    /// Returns nil url when the result wasn't actually smaller.
    public static func compress(_ url: URL, dir: URL, progress: ((Double) -> Void)? = nil) async throws -> ImageOps.CompressResult {
        let asset = AVURLAsset(url: url)
        let before = FileInspector.size(of: url)
        var height: Double = 0
        if let track = try await asset.loadTracks(withMediaType: .video).first {
            let size = try await track.load(.naturalSize)
            let t = try await track.load(.preferredTransform)
            let s = size.applying(t)
            height = min(abs(s.width), abs(s.height))
        }
        let preset: String
        if height > 1080 { preset = AVAssetExportPresetHEVC1920x1080 }
        else if height > 720 { preset = AVAssetExportPreset1280x720 }
        else if height > 0 { preset = AVAssetExportPresetMediumQuality }
        else { preset = AVAssetExportPresetAppleM4A }
        let isAudio = height == 0
        let out = Naming.output(for: url, ext: isAudio ? "m4a" : "mp4", suffix: "Compressed", in: dir)
        try await export(asset, preset: preset, type: isAudio ? .m4a : .mp4, to: out, progress: progress)
        let after = FileInspector.size(of: out)
        if after >= before {
            try? FileManager.default.removeItem(at: out)
            return .init(url: nil, before: before, after: before)
        }
        return .init(url: out, before: before, after: after)
    }

    /// Same video, no sound. Copies the video stream as-is (fast, no quality loss).
    public static func mute(_ url: URL, dir: URL) async throws -> URL {
        let asset = AVURLAsset(url: url)
        guard let vt = try await asset.loadTracks(withMediaType: .video).first else {
            throw DragShiftError("No video in \(url.lastPathComponent).")
        }
        let comp = AVMutableComposition()
        let duration = try await asset.load(.duration)
        guard let track = comp.addMutableTrack(withMediaType: .video, preferredTrackID: kCMPersistentTrackID_Invalid) else {
            throw DragShiftError("Couldn't build the video.")
        }
        try track.insertTimeRange(CMTimeRange(start: .zero, duration: duration), of: vt, at: .zero)
        track.preferredTransform = try await vt.load(.preferredTransform)
        let ext = url.pathExtension.lowercased() == "mov" ? "mov" : "mp4"
        let out = Naming.output(for: url, ext: ext, suffix: "Muted", in: dir)
        try await export(comp, preset: AVAssetExportPresetPassthrough, type: ext == "mov" ? .mov : .mp4, to: out)
        return out
    }

    /// Cut to a time range without re-encoding.
    public static func trim(_ url: URL, range: CMTimeRange, dir: URL, progress: ((Double) -> Void)? = nil) async throws -> URL {
        let asset = AVURLAsset(url: url)
        let video = await hasVideo(url)
        let ext = url.pathExtension.lowercased()
        let type: AVFileType
        let outExt: String
        if video {
            outExt = ext == "mov" ? "mov" : "mp4"
            type = outExt == "mov" ? .mov : .mp4
        } else {
            outExt = "m4a"
            type = .m4a
        }
        let out = Naming.output(for: url, ext: outExt, suffix: "Trimmed", in: dir)
        let preset = video ? AVAssetExportPresetPassthrough : AVAssetExportPresetAppleM4A
        try await export(asset, preset: preset, type: type, to: out, range: range, progress: progress)
        return out
    }

    /// A full-resolution still from 10% into the video (skips black first frames).
    public static func still(_ url: URL, at fraction: Double = 0.1) async throws -> CGImage {
        let asset = AVURLAsset(url: url)
        let gen = AVAssetImageGenerator(asset: asset)
        gen.appliesPreferredTrackTransform = true
        gen.requestedTimeToleranceBefore = .zero
        gen.requestedTimeToleranceAfter = .zero
        let d = try await asset.load(.duration)
        let t = CMTime(seconds: max(0, d.seconds * fraction), preferredTimescale: 600)
        return try await gen.image(at: t).image
    }

    public static func gif(_ url: URL, to out: URL, fps: Double, width: Int, maxSeconds: Double,
                           progress: ((Double) -> Void)? = nil) async throws {
        let asset = AVURLAsset(url: url)
        let gen = AVAssetImageGenerator(asset: asset)
        gen.appliesPreferredTrackTransform = true
        gen.maximumSize = CGSize(width: width, height: width * 4)
        let tol = CMTime(seconds: 0.5 / fps, preferredTimescale: 600)
        gen.requestedTimeToleranceBefore = tol
        gen.requestedTimeToleranceAfter = tol
        let total = min(try await asset.load(.duration).seconds, maxSeconds)
        guard total > 0 else { throw DragShiftError("The video is empty.") }
        let times = stride(from: 0.0, to: total, by: 1 / fps).map { CMTime(seconds: $0, preferredTimescale: 600) }
        var frames: [CGImage] = []
        for await result in gen.images(for: times) {
            if case .success(_, let image, _) = result { frames.append(image) }
            progress?(Double(frames.count) / Double(times.count) * 0.9)
        }
        try ImageOps.makeGIF(frames, to: out, delay: 1 / fps)
    }

    /// Uncompressed audio (WAV/AIFF/CAF) via AVAssetReader -> AVAssetWriter.
    static func pcm(_ asset: AVAsset, to out: URL, format: MediaFormat) async throws {
        guard let track = try await asset.loadTracks(withMediaType: .audio).first else {
            throw DragShiftError("There's no sound in this file.")
        }
        var rate = 44_100.0, channels = 2
        if let desc = try await track.load(.formatDescriptions).first,
           let asbd = CMAudioFormatDescriptionGetStreamBasicDescription(desc)?.pointee {
            rate = asbd.mSampleRate > 0 ? asbd.mSampleRate : rate
            channels = max(1, min(2, Int(asbd.mChannelsPerFrame)))
        }
        let bigEndian = format == .aiff
        let settings: [String: Any] = [
            AVFormatIDKey: kAudioFormatLinearPCM,
            AVSampleRateKey: rate,
            AVNumberOfChannelsKey: channels,
            AVLinearPCMBitDepthKey: 16,
            AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: bigEndian,
            AVLinearPCMIsNonInterleaved: false,
        ]
        let type: AVFileType = format == .wav ? .wav : (format == .aiff ? .aiff : .caf)
        try await Naming.atomically(out) { tmp in
            let reader = try AVAssetReader(asset: asset)
            let output = AVAssetReaderTrackOutput(track: track, outputSettings: settings)
            reader.add(output)
            let writer = try AVAssetWriter(outputURL: tmp, fileType: type)
            let input = AVAssetWriterInput(mediaType: .audio, outputSettings: settings)
            input.expectsMediaDataInRealTime = false
            writer.add(input)
            guard reader.startReading(), writer.startWriting() else {
                throw reader.error ?? writer.error ?? DragShiftError("Couldn't read the audio.")
            }
            writer.startSession(atSourceTime: .zero)
            let queue = DispatchQueue(label: "dragshift.pcm")
            await withCheckedContinuation { (done: CheckedContinuation<Void, Never>) in
                input.requestMediaDataWhenReady(on: queue) {
                    while input.isReadyForMoreMediaData {
                        if let buf = output.copyNextSampleBuffer() {
                            input.append(buf)
                        } else {
                            input.markAsFinished()
                            done.resume()
                            return
                        }
                    }
                }
            }
            await writer.finishWriting()
            if writer.status != .completed { throw writer.error ?? DragShiftError("Couldn't save the audio.") }
        }
    }

    /// Crops the middle of the video to a new shape (9:16 for Reels/Shorts, 1:1 for posts) at `size`.
    public static func reframe(_ url: URL, size: CGSize, suffix: String, dir: URL,
                               progress: ((Double) -> Void)? = nil) async throws -> URL {
        let asset = AVURLAsset(url: url)
        guard let track = try await asset.loadTracks(withMediaType: .video).first else {
            throw DragShiftError("No video in \(url.lastPathComponent).")
        }
        let natural = try await track.load(.naturalSize)
        let t = try await track.load(.preferredTransform)
        let upright = natural.applying(t)
        let shown = CGSize(width: abs(upright.width), height: abs(upright.height))
        let duration = try await asset.load(.duration)
        let fps = try await track.load(.nominalFrameRate)

        let scale = max(size.width / shown.width, size.height / shown.height)
        let move = CGAffineTransform(translationX: (size.width - shown.width * scale) / 2,
                                     y: (size.height - shown.height * scale) / 2)
        let layer = AVMutableVideoCompositionLayerInstruction(assetTrack: track)
        layer.setTransform(t.concatenating(CGAffineTransform(scaleX: scale, y: scale)).concatenating(move), at: .zero)
        let instruction = AVMutableVideoCompositionInstruction()
        instruction.timeRange = CMTimeRange(start: .zero, duration: duration)
        instruction.layerInstructions = [layer]
        let comp = AVMutableVideoComposition()
        comp.renderSize = size
        comp.frameDuration = CMTime(value: 1, timescale: CMTimeScale(fps > 1 ? fps.rounded() : 30))
        comp.instructions = [instruction]

        let out = Naming.output(for: url, ext: "mp4", suffix: suffix, in: dir)
        try await export(asset, preset: AVAssetExportPresetHighestQuality, type: .mp4, to: out, progress: progress) { s in
            s.videoComposition = comp
        }
        return out
    }

    /// Plays `factor` times faster; the voice keeps its pitch.
    public static func speed(_ url: URL, factor: Double, dir: URL, progress: ((Double) -> Void)? = nil) async throws -> URL {
        let asset = AVURLAsset(url: url)
        let duration = try await asset.load(.duration)
        let comp = AVMutableComposition()
        let full = CMTimeRange(start: .zero, duration: duration)
        for type in [AVMediaType.video, .audio] {
            for src in try await asset.loadTracks(withMediaType: type) {
                guard let dst = comp.addMutableTrack(withMediaType: type, preferredTrackID: kCMPersistentTrackID_Invalid) else { continue }
                try dst.insertTimeRange(full, of: src, at: .zero)
                if type == .video { dst.preferredTransform = try await src.load(.preferredTransform) }
            }
        }
        comp.scaleTimeRange(full, toDuration: CMTimeMultiplyByFloat64(duration, multiplier: 1 / factor))
        let label = factor == factor.rounded() ? "\(Int(factor))x" : String(format: "%.1fx", factor)
        let out = Naming.output(for: url, ext: "mp4", suffix: label, in: dir)
        try await export(comp, preset: AVAssetExportPresetHighestQuality, type: .mp4, to: out, progress: progress) { s in
            s.audioTimePitchAlgorithm = .spectral
        }
        return out
    }
}
