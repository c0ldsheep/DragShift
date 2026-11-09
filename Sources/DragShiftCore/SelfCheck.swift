import AVFoundation
import Foundation
import ImageIO
import PDFKit
import UniformTypeIdentifiers

/// Every slice the ring offers, run the way the ring runs it (Engine.run with the default options), on realistic
/// files made on the spot: a photo (yours, if you give one), a 12-megapixel iPhone-style HEIC with location data, a
/// transparent graphic, a scan with text, a QR code, PDFs, a video with sound, audio, documents, a folder of
/// pictures and a zip. Each result must open as what it claims to be. Slices that need a choice in a window (crop,
/// lock, rename...) run their operation with a sample choice.
///
/// The tests run it (EveryActionTests), and so does the app on any Mac: `DragShift --selftest <folder> [photo]`.
/// On an Apple-silicon Mac, `arch -x86_64 DragShift.app/Contents/MacOS/DragShift --selftest …` runs it as Intel.
public enum SelfCheck {
    public struct Report: Sendable {
        public var lines: [String] = []
        public var failed: [String] = []
        public var worked: Int { lines.filter { $0.hasPrefix("✓") }.count }
        public var explained: Int { lines.filter { $0.hasPrefix("·") }.count }
        public var summary: String { "\(worked) worked, \(explained) explained why not, \(failed.count) failed" }
    }

    /// Messages that correctly explain there's nothing for this slice in this file (no text in a photo, no face in
    /// a QR code): the person is told why, which is right.
    static let nothingToDo = ["No text found", "Couldn't find a face", "Couldn't find a clear subject", "No QR code",
                              "already searchable", "only has one page"]

    /// Opens a result as what its name says it is. nil = fine, else what's wrong.
    public static func problem(_ url: URL) async -> String? {
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir) else { return "missing" }
        if isDir.boolValue {
            let n = (try? FileManager.default.contentsOfDirectory(atPath: url.path).count) ?? 0
            return n > 0 ? nil : "empty folder"
        }
        let size = (try? FileManager.default.attributesOfItem(atPath: url.path)[.size] as? Int) ?? 0
        guard size > 0 else { return "empty file" }
        switch url.pathExtension.lowercased() {
        case "jpg", "jpeg", "png", "heic", "heif", "tiff", "tif", "gif", "bmp", "webp", "avif":
            guard let src = CGImageSourceCreateWithURL(url as CFURL, nil), CGImageSourceGetCount(src) > 0,
                  let img = CGImageSourceCreateImageAtIndex(src, 0, nil), img.width > 0 else { return "not a readable image" }
            return nil
        case "pdf":
            guard let doc = PDFDocument(url: url), doc.pageCount > 0 || doc.isEncrypted else { return "not a readable PDF" }
            return nil
        case "mov", "mp4", "m4v", "m4a", "wav", "aiff", "aif", "caf", "mp3", "flac":
            let seconds = await MediaOps.duration(url)
            return seconds > 0.1 ? nil : "plays for \(seconds) s"
        case "zip", "docx", "odt":
            let head = (try? FileHandle(forReadingFrom: url).read(upToCount: 2)) ?? Data()
            return head == Data([0x50, 0x4B]) ? nil : "not a zip-based file"
        default:
            return nil // text, rtf, html, md: a non-empty file is the check
        }
    }

    /// Runs every engine slice the ring offers for these files.
    static func runAll(_ files: [URL], label: String, into report: inout Report) async {
        var seen = Set<String>()
        for a in Catalog.pages(for: files).flatMap(\.actions) where !a.isShare && a.editor == nil && seen.insert(a.id).inserted {
            let r = await Engine.run(a, inputs: files, options: Options())
            let said = r.failures.filter { f in nothingToDo.contains { f.contains($0) } }
            var problems = r.failures.filter { f in !nothingToDo.contains { f.contains($0) } }
            for o in r.outputs { if let p = await problem(o) { problems.append("\(o.lastPathComponent): \(p)") } }
            if r.outputs.isEmpty && r.failures.isEmpty && r.text == nil && r.notes.isEmpty { problems.append("no result and no message") }
            let line = "\(label) · \(a.title) (\(a.id))"
            if !problems.isEmpty {
                report.lines.append("✗ \(line): \(problems.joined(separator: "; "))")
                report.failed.append("\(line): \(problems.joined(separator: "; "))")
            } else if !said.isEmpty {
                report.lines.append("· \(line) says: \(said[0])")
            } else if r.outputs.isEmpty && r.text == nil {
                report.lines.append("· \(line) says: \(r.notes.joined(separator: " "))")
            } else {
                report.lines.append("✓ \(line)")
            }
        }
    }

    /// A check of a slice that needs a choice in a window.
    static func window(_ name: String, into report: inout Report, _ body: () async throws -> String?) async {
        do {
            if let bad = try await body() { report.lines.append("✗ \(name): \(bad)"); report.failed.append("\(name): \(bad)") }
            else { report.lines.append("✓ \(name) (window)") }
        } catch {
            report.lines.append("✗ \(name): \(error.localizedDescription)")
            report.failed.append("\(name): \(error.localizedDescription)")
        }
    }

    /// Makes the files in `dir` (kept there) and runs everything. `photo`: a real photo to use, else a made one.
    public static func run(in dir: URL, photo: URL? = nil) async throws -> Report {
        let s = try SampleFiles(in: dir)
        var report = Report()

        // pictures
        let pic = s.url("Photo.jpg")
        if let photo { try? FileManager.default.removeItem(at: pic); try FileManager.default.copyItem(at: photo, to: pic) }
        else { try s.writeImage(SampleFiles.image(2400, 1600, noise: true), "Photo.jpg", type: .jpeg) }
        await runAll([pic], label: "photo", into: &report)
        let heic = try s.writeImage(SampleFiles.image(4032, 3024, noise: true), "IMG_4821.HEIC", type: .heic, props: [
            kCGImagePropertyOrientation: 6,
            kCGImagePropertyGPSDictionary: [kCGImagePropertyGPSLatitude: 19.076, kCGImagePropertyGPSLatitudeRef: "N",
                                            kCGImagePropertyGPSLongitude: 72.877, kCGImagePropertyGPSLongitudeRef: "E"],
            kCGImagePropertyExifDictionary: [kCGImagePropertyExifDateTimeOriginal: "2026:11:09 23:41:00"],
            kCGImagePropertyTIFFDictionary: [kCGImagePropertyTIFFMake: "Apple", kCGImagePropertyTIFFModel: "iPhone"],
        ])
        await runAll([heic], label: "12 MP HEIC", into: &report)
        let logo = try s.writeImage(SampleFiles.image(900, 600, transparentBorder: 80), "Logo.png", type: .png)
        await runAll([logo], label: "transparent PNG", into: &report)
        let scan = try s.writeImage(SampleFiles.textImage(["Invoice number 4821", "Total due 1,250.00", "Thank you"]), "Scan.png", type: .png)
        await runAll([scan], label: "scan with text", into: &report)
        await runAll([try s.writeImage(try QR.make("https://example.com/order/4821"), "Code.png", type: .png)], label: "QR code", into: &report)
        let three = try (1...3).map { try s.writeImage(SampleFiles.image(1200, 900, noise: true), "Shot \($0).jpg", type: .jpeg) }
        await runAll(three, label: "3 photos", into: &report)

        // PDFs
        let doc = try s.pdf("Statement.pdf", pages: ["Account statement page 1", "Page 2: transactions", "Page 3: summary"])
        await runAll([doc], label: "PDF", into: &report)
        await runAll([doc, try s.pdf("Letter.pdf", pages: ["A one-page letter"])], label: "2 PDFs", into: &report)

        // video and audio
        let clip = try await s.video("Clip.mov", seconds: 3)
        await runAll([clip], label: "video", into: &report)
        await runAll([try s.wav("Voice.wav", seconds: 2)], label: "audio", into: &report)

        // documents
        let txt = s.url("Notes.txt")
        try "Things to upload before midnight:\n1. Photo under 50 KB\n2. Bank statement\n".write(to: txt, atomically: true, encoding: .utf8)
        await runAll([txt], label: "text file", into: &report)
        let rtf = s.url("Letter.rtf")
        let attr = NSAttributedString(string: "Dear Sir, please find my documents attached.")
        try attr.rtf(from: NSRange(location: 0, length: attr.length), documentAttributes: [:])?.write(to: rtf)
        await runAll([rtf], label: "RTF document", into: &report)

        // a folder of pictures, and a zip
        let folder = s.url("Holiday")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        for i in 1...3 { try s.writeImage(SampleFiles.image(800, 600, noise: true), "Holiday/Beach \(i).jpg", type: .jpeg) }
        await runAll([folder], label: "folder", into: &report)
        await runAll([try Archive.zip(three, dir: s.dir)], label: "zip", into: &report)

        // slices that need a choice in a window: their operations, with sample choices
        await window("Crop", into: &report) {
            let px = ImageOps.pixelSize(pic) ?? .zero
            let out = try ImageOps.crop(pic, rect: CGRect(x: px.width * 0.2, y: px.height * 0.2, width: px.width * 0.5, height: px.height * 0.5), quality: 0.9, dir: s.dir)
            let c = ImageOps.pixelSize(out) ?? .zero
            return abs(c.width - px.width * 0.5) <= 1 && abs(c.height - px.height * 0.5) <= 1 ? nil : "got \(c) from \(px)"
        }
        await window("Resize", into: &report) {
            let out = try ImageOps.resize(heic, longEdge: 1600, scale: nil, quality: 0.85, dir: s.dir)
            let c = ImageOps.pixelSize(out) ?? .zero
            return max(c.width, c.height) == 1600 ? nil : "long edge \(max(c.width, c.height))"
        }
        await window("Add BG", into: &report) {
            guard let img = Backdrop.render(try ImageOps.load(logo), style: BackdropStyle(fill: .solid(.white), padding: 0.1, corner: 0, shadow: 0, ratio: .square)) else { return "nothing drawn" }
            return img.width == img.height ? nil : "not square"
        }
        await window("Lock + Unlock", into: &report) {
            let locked = try PDFOps.lock(doc, password: "mango4Tree!rain", dir: s.dir)
            guard PDFDocument(url: locked)?.isLocked == true else { return "the copy isn't locked" }
            if (try? PDFOps.unlock(locked, password: "wrong", dir: s.dir)) != nil { return "a wrong password unlocked it" }
            let open = try PDFOps.unlock(locked, password: "mango4Tree!rain", dir: s.dir)
            let d = PDFDocument(url: open)
            return d?.isEncrypted == false && d?.pageCount == 3 ? nil : "unlocked copy is wrong"
        }
        await window("Trim", into: &report) {
            let out = try await MediaOps.trim(clip, range: CMTimeRange(start: CMTime(seconds: 0.5, preferredTimescale: 600), duration: CMTime(seconds: 1.5, preferredTimescale: 600)), dir: s.dir)
            let d = await MediaOps.duration(out)
            return abs(d - 1.5) < 0.2 ? nil : "\(d) s long"
        }
        await window("Rename", into: &report) {
            let copies = try (1...3).map { i -> URL in
                let u = s.url("copy \(i).jpg")
                try? FileManager.default.removeItem(at: u)
                try FileManager.default.copyItem(at: three[i - 1], to: u)
                return u
            }
            let plan = Renamer.plan(copies, mode: .numbered(base: "Trip", start: 1))
            guard plan.allSatisfy({ $0.problem == nil }) else { return "plan has problems" }
            _ = try Renamer.apply(plan)
            return ["Trip 1.jpg", "Trip 2.jpg", "Trip 3.jpg"].allSatisfy { FileManager.default.fileExists(atPath: s.url($0).path) } ? nil : "not renamed"
        }
        report.lines.append("· Redact, Annotate, Under KB, Print: covered by the engine tests")
        return report
    }
}
