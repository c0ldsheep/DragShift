import AVFoundation
import Foundation

/// Settings the engine needs. The app fills this from the user's preferences.
public struct Options: Sendable {
    /// nil = save next to the original.
    public var outputFolder: URL?
    public var quality: Double = 0.85
    public var compressQuality: Double = 0.7
    public var compressMaxEdge: Int = 3000
    public var pdfDPI: Double = 200
    public var gifFPS: Double = 12
    public var gifWidth: Int = 480
    public var gifMaxSeconds: Double = 20
    public var watermark = WatermarkStyle()
    public var photo = PhotoSpec()
    public init() {}
}

/// What happened, for the progress bubble and Undo.
public struct RunReport: Sendable {
    public var outputs: [URL] = []
    public var failures: [String] = []
    public var notes: [String] = []
    public var bytesBefore: Int64 = 0
    public var bytesAfter: Int64 = 0
    /// Text read by OCR / PDF text, so the app can put it on the clipboard.
    public var text: String?
    public init() {}

    public var summary: String {
        if outputs.isEmpty, let f = failures.first { return f }
        if outputs.isEmpty, let n = notes.first { return n }
        var s = outputs.count == 1 ? "Saved \(outputs[0].lastPathComponent)" : "Saved \(outputs.count) files"
        if bytesBefore > 0, bytesAfter > 0, bytesAfter < bytesBefore {
            let pct = Int((1 - Double(bytesAfter) / Double(bytesBefore)) * 100)
            s += " · \(ByteFormat.string(bytesBefore)) → \(ByteFormat.string(bytesAfter)) (−\(pct)%)"
        }
        return s
    }
}

/// Lets the blocking steps add to the report from the work queue.
final class ReportBox: @unchecked Sendable {
    var report = RunReport()
}

public enum Engine {
    /// Where blocking file work runs (OCR, image encoding, zip). Kept off Swift's cooperative
    /// threads: those are few, and a pile of blocked ones can freeze everything else.
    static let work = DispatchQueue(label: "dragshift.work", qos: .userInitiated, attributes: .concurrent)

    /// Runs blocking work on `work` and waits for it without holding a thread.
    public static func offload<T>(_ body: @escaping () throws -> T) async throws -> T {
        try await withCheckedThrowingContinuation { (c: CheckedContinuation<T, Error>) in
            work.async { c.resume(with: Result { try body() }) }
        }
    }

    public typealias Progress = @Sendable (_ done: Int, _ total: Int, _ fraction: Double) -> Void

    /// Runs a one-drop action on every input. Inputs are processed one by one so a huge batch
    /// can't eat all memory; a failure on one file doesn't stop the rest.
    public static func run(_ action: Action, inputs: [URL], options: Options, progress: Progress? = nil) async -> RunReport {
        var report = RunReport()
        let o = options
        func dir(_ u: URL) -> URL { Naming.folder(for: u, override: o.outputFolder) }
        let sorted = inputs

        // Actions that take all inputs at once.
        switch action.id {
        case "img.to.pdf" where inputs.count > 1, "folder.pdf":
            let images = action.id == "folder.pdf" ? inputs.flatMap(FileInspector.images(in:)) : inputs.sorted(by: Naming.finderOrder)
            return await batchOne(&report, total: 1, progress) {
                guard let first = images.first else { throw DragShiftError("No images found.") }
                // Named after the folder, or after the first image; saved beside it.
                let name = action.id == "folder.pdf" ? inputs[0] : first
                let out = Naming.output(for: name, ext: "pdf", suffix: images.count > 1 ? "(\(images.count) pages)" : nil,
                                        in: dir(name))
                try ImageOps.makePDF(images, to: out, quality: o.quality)
                return [out]
            }
        case "pdf.merge", "mixed.merge":
            return await batchOne(&report, total: 1, progress) {
                [try PDFOps.merge(inputs.sorted(by: Naming.finderOrder), dir: dir(inputs[0]), quality: o.quality)]
            }
        case "img.gif", "folder.gif":
            let images = action.id == "folder.gif" ? inputs.flatMap(FileInspector.images(in:)) : inputs.sorted(by: Naming.finderOrder)
            return await batchOne(&report, total: 1, progress) {
                guard let first = images.first else { throw DragShiftError("No images found.") }
                let frames = try images.map { ImageOps.fit(try ImageOps.load($0, maxPixel: 1200), longEdge: 800) }
                let target = action.id == "folder.gif" ? inputs[0] : first
                let out = Naming.output(for: target, ext: "gif", suffix: action.id == "folder.gif" ? nil : "Animated",
                                        in: dir(target))
                try ImageOps.makeGIF(frames, to: out, delay: 0.5)
                return [out]
            }
        case "img.stitch":
            return await batchOne(&report, total: 1, progress) {
                let images = inputs.sorted(by: Naming.finderOrder)
                return [try ImageOps.stitch(images, quality: max(o.quality, 0.85), dir: dir(images[0]))]
            }
        case "file.zip" where inputs.count > 1:
            return await batchOne(&report, total: 1, progress) { [try Archive.zip(inputs, dir: dir(inputs[0]))] }
        default:
            break
        }

        for (i, url) in sorted.enumerated() {
            progress?(i, sorted.count, Double(i) / Double(sorted.count))
            do {
                let outs = try await runOne(action, url, o, dir(url), &report) { frac in
                    progress?(i, sorted.count, (Double(i) + frac) / Double(sorted.count))
                }
                report.outputs += outs
            } catch {
                report.failures.append("\(url.lastPathComponent): \(error.localizedDescription)")
            }
        }
        progress?(sorted.count, sorted.count, 1)
        return report
    }

    static func batchOne(_ report: inout RunReport, total: Int, _ progress: Progress?,
                         _ body: @escaping () throws -> [URL]) async -> RunReport {
        progress?(0, total, 0.05)
        do { report.outputs = try await offload(body) } catch { report.failures.append(error.localizedDescription) }
        progress?(total, total, 1)
        return report
    }

    /// Runs one step on one file. Video/audio and documents are truly async; the rest is blocking
    /// work that goes to its own queue so it can never starve Swift's shared async threads.
    static func runOne(_ action: Action, _ url: URL, _ o: Options, _ dir: URL, _ report: inout RunReport,
                       _ frac: @escaping @Sendable (Double) -> Void) async throws -> [URL] {
        let id = action.id
        let parts = id.split(separator: ".").map(String.init)
        // Document → document (AppKit's readers want the main thread)
        if parts.count == 3, parts[0] == "doc", parts[1] == "to", let f = DocFormat(rawValue: parts[2]) {
            let out = try await MainActor.run { try DocOps.convert(url, to: f, dir: dir) }
            return [out]
        }
        if parts.count == 3, parts[0] == "media", parts[1] == "to", let f = MediaFormat(rawValue: parts[2]) {
            return [try await MediaOps.convert(url, to: f, dir: dir, gifFPS: o.gifFPS, gifWidth: o.gifWidth,
                                               gifMaxSeconds: o.gifMaxSeconds, progress: frac)]
        }
        if parts.count == 3, parts[0] == "media", parts[1] == "speed", let f = Double(parts[2]) {
            return [try await MediaOps.speed(url, factor: f, dir: dir, progress: frac)]
        }
        switch id {
        case "doc.qr":
            let text = try await MainActor.run { try DocOps.read(url).string }
            return try await offload {
                let img = try QR.make(text.trimmingCharacters(in: .whitespacesAndNewlines))
                let out = Naming.unique(dir.appendingPathComponent(QR.name(for: text)).appendingPathExtension("png"))
                try ImageOps.write(img, to: out, format: .png, quality: 1)
                return [out]
            }
        case "media.vertical":
            return [try await MediaOps.reframe(url, size: CGSize(width: 1080, height: 1920), suffix: "9x16", dir: dir, progress: frac)]
        case "media.square":
            return [try await MediaOps.reframe(url, size: CGSize(width: 1080, height: 1080), suffix: "1x1", dir: dir, progress: frac)]
        case "media.compress":
            return tally(try await MediaOps.compress(url, dir: dir, progress: frac), url, &report)
        case "media.mute":
            return [try await MediaOps.mute(url, dir: dir)]
        case "media.still":
            let img = try await MediaOps.still(url)
            let out = Naming.output(for: url, ext: "jpg", suffix: "Still", in: dir)
            try await offload { try ImageOps.write(img, to: out, format: .jpg, quality: o.quality) }
            return [out]
        default:
            let box = ReportBox()
            defer {
                if let t = box.report.text { report.text = [report.text, t].compactMap { $0 }.joined(separator: "\n\n") }
                report.failures += box.report.failures
                report.notes += box.report.notes
                report.bytesBefore += box.report.bytesBefore
                report.bytesAfter += box.report.bytesAfter
            }
            return try await offload { try runSync(action, url, o, dir, box, frac) }
        }
    }

    /// Everything that isn't video/audio or a Word-style document: plain blocking work,
    /// run on `Engine.work` (see `offload`).
    static func runSync(_ action: Action, _ url: URL, _ o: Options, _ dir: URL, _ box: ReportBox,
                        _ frac: @escaping @Sendable (Double) -> Void) throws -> [URL] {
        let id = action.id
        let parts = id.split(separator: ".").map(String.init)

        // Image → image formats
        if parts.count == 3, parts[0] == "img", parts[1] == "to", let f = ImageFormat(rawValue: parts[2]) {
            return [try ImageOps.convert(url, to: f, quality: o.quality, dir: dir)]
        }
        // PDF → images
        if parts.count == 3, parts[0] == "pdf", parts[1] == "to", let f = ImageFormat(rawValue: parts[2]) {
            return try PDFOps.toImages(url, format: f, dpi: o.pdfDPI, quality: o.quality, dir: dir, progress: frac)
        }
        // PDF → text documents
        if parts.count == 3, parts[0] == "pdf", parts[1] == "to", let f = DocFormat(rawValue: parts[2]) {
            let doc = try PDFOps.open(url)
            let out = Naming.output(for: url, ext: f.ext, in: dir)
            if f == .txt {
                let text = try PDFOps.text(doc)
                box.report.text = text
                try DocOps.write(DocOps.plain(text), as: .txt, to: out)
            } else {
                let attr = try PDFOps.attributedText(doc)
                try DocOps.write(attr, as: f, to: out)
            }
            return [out]
        }
        // Steps with numbers in them (used by recipes): img.size.1600, img.fill.1080x1350, media.speed.2
        if parts.count == 3, parts[0] == "img", parts[1] == "size", let edge = Int(parts[2]) {
            let img = ImageOps.fit(try ImageOps.load(url), longEdge: edge)
            return [try ImageOps.saveEdited(img, like: url, suffix: "\(img.width)x\(img.height)", dir: dir, quality: o.quality)]
        }
        if parts.count == 3, parts[0] == "img", parts[1] == "fill" || parts[1] == "fit", let size = Engine.size(parts[2]) {
            let src = try ImageOps.load(url)
            let img = parts[1] == "fill" ? ImageOps.fill(src, to: size) : ImageOps.fitStory(src, to: size)
            return [try ImageOps.saveEdited(img, like: url, suffix: parts[2], dir: dir, quality: o.quality,
                                            forceFormat: parts[1] == "fit" ? .jpg : nil)]
        }
        if let kb = SizeLimit.kb(fromStep: id) {
            switch FileInspector.kind(of: url) {
            case .image:
                return tally(try SizeLimit.fitImage(url, kb: kb, dir: dir), url, &box.report,
                             already: "\(url.lastPathComponent) is already under \(SizeLimit.title(kb: kb)).")
            case .pdf:
                let r = try SizeLimit.fitPDF(url, kb: kb, dir: dir, progress: frac)
                if r.flattened { box.report.notes.append("Pages were saved as pictures to fit, so text can't be selected.") }
                return tally(r.result, url, &box.report,
                             already: "\(url.lastPathComponent) is already under \(SizeLimit.title(kb: kb)).")
            default:
                throw DragShiftError("Size limits work on pictures and PDFs.")
            }
        }
        switch id {
        case "img.passport":
            let r = try PassportPhoto.save(url, spec: o.photo, dir: dir)
            if let n = r.note { box.report.notes.append(n) }
            box.report.bytesBefore += FileInspector.size(of: url)
            box.report.bytesAfter += FileInspector.size(of: r.url)
            return [r.url]
        case "img.watermark":
            let img = ImageOps.watermarked(try ImageOps.load(url), style: o.watermark)
            return [try ImageOps.saveEdited(img, like: url, suffix: "Watermarked", dir: dir, quality: o.quality)]
        case "img.qr.read":
            let codes = try QR.read(try ImageOps.load(url))
            guard !codes.isEmpty else { throw DragShiftError("No QR code or barcode found in \(url.lastPathComponent).") }
            box.report.text = [box.report.text, codes.joined(separator: "\n")].compactMap { $0 }.joined(separator: "\n")
            return []
        case "pdf.searchable":
            return [try PDFOps.makeSearchable(url, dir: dir, progress: frac)]
        case "img.to.pdf":
            let out = Naming.output(for: url, ext: "pdf", in: dir)
            try ImageOps.makePDF([url], to: out, quality: o.quality)
            return [out]
        case "img.to.text":
            let text = try VisionOps.text(try ImageOps.load(url))
            guard !text.isEmpty else { throw DragShiftError("No text found in the image.") }
            box.report.text = [box.report.text, text].compactMap { $0 }.joined(separator: "\n\n")
            let out = Naming.output(for: url, ext: "txt", in: dir)
            try DocOps.write(DocOps.plain(text), as: .txt, to: out)
            return [out]
        case "img.rotate":
            return [try ImageOps.rotate(url, quarterTurns: 1, quality: o.quality, dir: dir)]
        case "img.flip":
            return [try ImageOps.flip(url, quality: o.quality, dir: dir)]
        case "img.enhance":
            return [try ImageOps.enhance(url, quality: o.quality, dir: dir)]
        case "img.clean":
            return [try ImageOps.stripMetadata(url, dir: dir)]
        case "img.removebg":
            guard #available(macOS 14, *) else { throw DragShiftError("Cut Out needs macOS 14 or newer.") }
            let cut = try VisionOps.removeBackground(try ImageOps.load(url))
            return [try ImageOps.saveEdited(cut, like: url, suffix: "Cut Out", dir: dir, quality: o.quality, forceFormat: .png)]
        case "img.shop":
            guard #available(macOS 14, *) else { throw DragShiftError("Shop Photo needs macOS 14 or newer.") }
            let photo = try VisionOps.shopPhoto(try ImageOps.load(url))
            return [try ImageOps.saveEdited(photo, like: url, suffix: "Shop", dir: dir, quality: 0.92, keepMetadata: false, forceFormat: .jpg)]
        case "img.compress":
            let r = try ImageOps.compress(url, quality: o.compressQuality, maxEdge: o.compressMaxEdge, dir: dir)
            return tally(r, url, &box.report)
        case "pdf.compress":
            return tally(try PDFOps.compress(url, dir: dir), url, &box.report)
        case "pdf.split":
            return [try PDFOps.split(url, dir: dir)]
        case "pdf.rotate":
            return [try PDFOps.rotate(url, degrees: 90, dir: dir)]
        case "file.zip":
            return [try Archive.zip([url], dir: dir)]
        case "file.unzip":
            return [try Archive.unzip(url, dir: dir)]
        case "folder.jpg", "folder.png":
            let f: ImageFormat = id == "folder.jpg" ? .jpg : .png
            let images = FileInspector.images(in: url)
            guard !images.isEmpty else { throw DragShiftError("No images in \(url.lastPathComponent).") }
            let folder = try Naming.newFolder(named: "\(url.lastPathComponent) (\(f.title))", in: dir)
            for (i, img) in images.enumerated() {
                do { _ = try ImageOps.convert(img, to: f, quality: o.quality, dir: folder) }
                catch { box.report.failures.append("\(img.lastPathComponent): \(error.localizedDescription)") }
                frac(Double(i + 1) / Double(images.count))
            }
            return [folder]
        case "folder.compress":
            let images = FileInspector.images(in: url)
            guard !images.isEmpty else { throw DragShiftError("No images in \(url.lastPathComponent).") }
            let folder = try Naming.newFolder(named: "\(url.lastPathComponent) (Compressed)", in: dir)
            for (i, img) in images.enumerated() {
                if let r = try? ImageOps.compress(img, quality: o.compressQuality, maxEdge: o.compressMaxEdge, dir: folder) {
                    box.report.bytesBefore += r.before
                    box.report.bytesAfter += r.after
                    if r.url == nil { try? FileManager.default.copyItem(at: img, to: Naming.unique(folder.appendingPathComponent(img.lastPathComponent))) }
                }
                frac(Double(i + 1) / Double(images.count))
            }
            return [folder]
        default:
            throw DragShiftError("\(action.title) isn't available for \(url.lastPathComponent).")
        }
    }

    static func size(_ s: String) -> CGSize? {
        let p = s.split(separator: "x").compactMap { Double($0) }
        return p.count == 2 && p[0] > 0 && p[1] > 0 ? CGSize(width: p[0], height: p[1]) : nil
    }

    /// Runs a recipe's steps in order. Each file goes through the chain on its own, unless a
    /// step joins files (put in a PDF, zip...), then the whole group goes through together.
    /// Only the final file is kept, named after the original + the recipe ("Photo Shop Ready.jpg").
    public static func runRecipe(_ recipe: Recipe, inputs: [URL], options: Options, progress: Progress? = nil) async -> RunReport {
        var report = RunReport()
        guard !recipe.steps.isEmpty else { report.failures.append("\(recipe.name) has no steps yet."); return report }
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("dragshift-recipe-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: root) }
        let grouped = inputs.count > 1 && recipe.steps.contains { Recipe.manyToOne.contains($0) }
        let batches: [[URL]] = grouped ? [inputs.sorted(by: Naming.finderOrder)] : inputs.map { [$0] }
        let total = batches.count * recipe.steps.count
        var done = 0

        for (bi, batch) in batches.enumerated() {
            var current = batch
            var failed = false
            for (si, step) in recipe.steps.enumerated() {
                let stepDir = root.appendingPathComponent("\(bi)-\(si)")
                try? FileManager.default.createDirectory(at: stepDir, withIntermediateDirectories: true)
                var o = options
                o.outputFolder = stepDir
                let action = Action(step, RecipeSteps.title(step), "gearshape", verb: RecipeSteps.title(step))
                let r = await run(action, inputs: current, options: o)
                if let t = r.text { report.text = t }
                done += 1
                progress?(bi, batches.count, Double(done) / Double(total))
                if !r.failures.isEmpty {
                    report.failures += r.failures.map { "\(recipe.name) · \(RecipeSteps.title(step)): \($0)" }
                    failed = true
                    break
                }
                if !r.outputs.isEmpty { current = r.outputs } // "already small" just passes the file on
            }
            guard !failed else { continue }
            let original = batch[0]
            let dir = Naming.folder(for: original, override: options.outputFolder)
            for (k, file) in current.enumerated() where file.path.hasPrefix(root.path) {
                let suffix = current.count > 1 ? "\(recipe.name) \(k + 1)" : recipe.name
                let dest = Naming.output(for: original, ext: file.pathExtension, suffix: suffix, in: dir)
                do {
                    try FileManager.default.moveItem(at: file, to: dest)
                    report.outputs.append(dest)
                } catch {
                    report.failures.append("\(original.lastPathComponent): \(error.localizedDescription)")
                }
            }
            report.bytesBefore += batch.reduce(0) { $0 + FileInspector.size(of: $1) }
        }
        report.bytesAfter = report.outputs.reduce(0) { $0 + FileInspector.size(of: $1) }
        progress?(batches.count, batches.count, 1)
        return report
    }

    static func tally(_ r: ImageOps.CompressResult, _ url: URL, _ report: inout RunReport, already: String? = nil) -> [URL] {
        report.bytesBefore += r.before
        report.bytesAfter += r.after
        guard let out = r.url else {
            report.notes.append(already ?? "\(url.lastPathComponent) is already as small as it gets.")
            return []
        }
        return [out]
    }
}

public enum Archive {
    static let ditto = URL(fileURLWithPath: "/usr/bin/ditto")

    /// One item -> "Name.zip"; several -> "Archive.zip" (like Finder's Compress).
    public static func zip(_ urls: [URL], dir: URL) throws -> URL {
        guard let first = urls.first else { throw DragShiftError("Nothing to zip.") }
        if urls.count == 1 {
            let out = Naming.unique(dir.appendingPathComponent(first.lastPathComponent).appendingPathExtension("zip"))
            // a folder keeps its name inside the zip; a single file goes in on its own (with --keepParent ditto put
            // it inside its parent's folder: zipping Desktop/Photo.jpg unpacked to Desktop/Photo.jpg)
            let isFolder = (try? first.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == true
            try Naming.atomically(out) { tmp in
                try Shell.run(ditto, ["-c", "-k", "--sequesterRsrc"] + (isFolder ? ["--keepParent"] : []) + [first.path, tmp.path])
            }
            return out
        }
        let stage = FileManager.default.temporaryDirectory.appendingPathComponent("dragshift-\(UUID().uuidString)/Archive")
        try FileManager.default.createDirectory(at: stage, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: stage.deletingLastPathComponent()) }
        for u in urls {
            try FileManager.default.copyItem(at: u, to: Naming.unique(stage.appendingPathComponent(u.lastPathComponent)))
        }
        let out = Naming.unique(dir.appendingPathComponent("Archive.zip"))
        try Naming.atomically(out) { tmp in
            try Shell.run(ditto, ["-c", "-k", "--sequesterRsrc", stage.path, tmp.path])
        }
        return out
    }

    /// Extracts into a new folder named after the archive. ditto keeps every entry inside that folder ("../" and
    /// absolute names land inside it; tested). A "zip bomb", a small file that unpacks to fill the disk, is refused
    /// first: the sizes the zip lists must fit in the free space (keeping 1 GB) and under `limit`.
    public static func unzip(_ url: URL, dir: URL, limit: Int64 = 20 * 1024 * 1024 * 1024) throws -> URL {
        if let (files, bytes) = listedSize(url) {
            let free = (try? dir.resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey]))?
                .volumeAvailableCapacityForImportantUsage ?? Int64.max
            if files > 100_000 { throw DragShiftError("\(url.lastPathComponent) has \(files) files inside. That's too many to unpack safely.") }
            if bytes > min(limit, free - 1024 * 1024 * 1024) {
                throw DragShiftError("\(url.lastPathComponent) would unpack to \(ByteFormat.string(bytes)), more than is safe here, so it wasn't unpacked.")
            }
        }
        let folder = Naming.unique(dir.appendingPathComponent(Naming.base(url)))
        try Shell.run(ditto, ["-x", "-k", url.path, folder.path])
        return folder
    }

    /// The file count and total unpacked size a zip lists ("zipinfo -t"), or nil if it can't be read that way.
    static func listedSize(_ url: URL) -> (files: Int, bytes: Int64)? {
        guard let out = try? Shell.run(URL(fileURLWithPath: "/usr/bin/zipinfo"), ["-t", url.path]) else { return nil }
        // "3 files, 1638400 bytes uncompressed, 1600 bytes compressed:  99.9%"
        let words = out.split(whereSeparator: { $0 == " " || $0 == "," || $0 == "\n" })
        guard let f = words.firstIndex(of: "files") ?? words.firstIndex(of: "file"), f > 0, let files = Int(words[f - 1]),
              let b = words.firstIndex(of: "bytes"), b > 0, let bytes = Int64(words[b - 1]) else { return nil }
        return (files, bytes)
    }
}
