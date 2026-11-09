import Foundation
import ImageIO

/// Batch rename with a preview. Renames happen in two steps through temporary names,
/// so swapping names (a→b, b→a) is safe, and a failure puts everything back.
public enum RenameMode: Hashable, Sendable {
    case numbered(base: String, start: Int)
    case datePrefix
    case replace(find: String, with: String)
    case lowercase, uppercase, titlecase
}

public enum Renamer {
    public struct Item: Identifiable, Hashable, Sendable {
        public var id: String { from.path }
        public let from: URL
        public let to: URL
        public let problem: String?
    }

    public static func plan(_ urls: [URL], mode: RenameMode) -> [Item] {
        let files = urls.sorted(by: Naming.finderOrder)
        let digits: Int = {
            if case .numbered(_, let start) = mode { return files.count + start - 1 >= 10 ? String(files.count + start - 1).count : 1 }
            return 1
        }()
        var names: [String] = []
        for (i, url) in files.enumerated() {
            let stem = url.deletingPathExtension().lastPathComponent
            let ext = url.pathExtension
            var newStem: String
            switch mode {
            case .numbered(let base, let start):
                let n = String(format: "%0\(digits)d", start + i)
                newStem = base.trimmingCharacters(in: .whitespaces).isEmpty ? n : "\(base.trimmingCharacters(in: .whitespaces)) \(n)"
            case .datePrefix:
                newStem = "\(dateString(url)) \(stem)"
            case .replace(let find, let with):
                newStem = find.isEmpty ? stem : stem.replacingOccurrences(of: find, with: with)
            case .lowercase: newStem = stem.lowercased()
            case .uppercase: newStem = stem.uppercased()
            case .titlecase: newStem = stem.capitalized
            }
            names.append(ext.isEmpty ? newStem : "\(newStem).\(ext)")
        }
        var counts: [String: Int] = [:]
        for n in names { counts[n.lowercased(), default: 0] += 1 }
        let originals = Set(files.map { $0.path.lowercased() })
        return zip(files, names).map { url, name in
            let to = url.deletingLastPathComponent().appendingPathComponent(name)
            var problem: String?
            if name.hasPrefix(".") || name.replacingOccurrences(of: ".\(url.pathExtension)", with: "").trimmingCharacters(in: .whitespaces).isEmpty {
                problem = "Empty name"
            } else if name.contains("/") || name.contains(":") {
                problem = "Can't use / or :"
            } else if (counts[name.lowercased()] ?? 0) > 1 {
                problem = "Two files would get this name"
            } else if FileManager.default.fileExists(atPath: to.path) && !originals.contains(to.path.lowercased()) {
                problem = "Already exists"
            }
            return Item(from: url, to: to, problem: problem)
        }
    }

    /// Photo date for pictures (when it was taken), otherwise the file's creation date.
    static func dateString(_ url: URL) -> String {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd"
        if let exif = ImageOps.properties(url)[kCGImagePropertyExifDictionary] as? [CFString: Any],
           let s = exif[kCGImagePropertyExifDateTimeOriginal] as? String, s.count >= 10 {
            return s.prefix(10).replacingOccurrences(of: ":", with: "-")
        }
        let d = (try? url.resourceValues(forKeys: [.creationDateKey]))?.creationDate ?? Date()
        return f.string(from: d)
    }

    /// Applies the plan. Returns (old, new) pairs for Undo.
    @discardableResult
    public static func apply(_ items: [Item]) throws -> [(from: URL, to: URL)] {
        let todo = items.filter { $0.problem == nil && $0.from.path != $0.to.path }
        guard items.allSatisfy({ $0.problem == nil }) else { throw DragShiftError("Fix the names marked in red first.") }
        let fm = FileManager.default
        var parked: [(tmp: URL, item: Item)] = []
        do {
            for item in todo {
                let tmp = item.from.deletingLastPathComponent().appendingPathComponent(".dragshift-rename-\(UUID().uuidString)")
                try fm.moveItem(at: item.from, to: tmp)
                parked.append((tmp, item))
            }
            var done: [(URL, URL)] = []
            for (tmp, item) in parked {
                try fm.moveItem(at: tmp, to: item.to)
                done.append((item.from, item.to))
            }
            return done
        } catch {
            for (tmp, item) in parked where fm.fileExists(atPath: tmp.path) { try? fm.moveItem(at: tmp, to: item.from) }
            throw DragShiftError("Renaming stopped: \(error.localizedDescription)")
        }
    }

    /// Undo for `apply`.
    public static func revert(_ pairs: [(from: URL, to: URL)]) throws {
        let back = pairs.map { Item(from: $0.to, to: $0.from, problem: nil) }
        try apply(back)
    }
}

/// "When a file like this lands in that folder, do this." Watched by the app with no polling.
public struct WatchRule: Codable, Identifiable, Hashable, Sendable {
    public enum Match: String, Codable, CaseIterable, Sendable {
        case heic, screenshots, images, pdfs, videos, audio, any
        public var title: String {
            switch self {
            case .heic: return "HEIC photos"
            case .screenshots: return "Screenshots"
            case .images: return "Any image"
            case .pdfs: return "PDFs"
            case .videos: return "Videos"
            case .audio: return "Audio"
            case .any: return "Any file"
            }
        }
        /// A sample file, so the rule editor can list the actions that fit.
        public var sample: URL {
            switch self {
            case .heic: return URL(fileURLWithPath: "/tmp/a.heic")
            case .screenshots, .images: return URL(fileURLWithPath: "/tmp/a.png")
            case .pdfs: return URL(fileURLWithPath: "/tmp/a.pdf")
            case .videos: return URL(fileURLWithPath: "/tmp/a.mov")
            case .audio: return URL(fileURLWithPath: "/tmp/a.wav")
            case .any: return URL(fileURLWithPath: "/tmp/a.bin")
            }
        }
    }

    public var id: String
    public var folder: String
    public var match: Match
    public var actionID: String
    public var actionTitle: String
    public var enabled: Bool
    public var trashOriginal: Bool
    public var since: Date

    public init(folder: String, match: Match, actionID: String, actionTitle: String, trashOriginal: Bool = false) {
        id = UUID().uuidString; self.folder = folder; self.match = match; self.actionID = actionID
        self.actionTitle = actionTitle; enabled = true; self.trashOriginal = trashOriginal; since = Date()
    }

    public func matches(_ url: URL) -> Bool {
        let name = url.lastPathComponent
        if name.hasPrefix(".") || name.hasSuffix(".download") || name.hasSuffix(".crdownload") || name.hasSuffix(".part") { return false }
        let kind = FileInspector.kind(of: url)
        switch match {
        case .heic: return ["heic", "heif"].contains(url.pathExtension.lowercased())
        case .screenshots: return kind == .image && (name.hasPrefix("Screenshot") || name.hasPrefix("Screen Shot"))
        case .images: return kind == .image
        case .pdfs: return kind == .pdf
        case .videos: return kind == .video
        case .audio: return kind == .audio
        case .any: return kind != .folder
        }
    }

    /// Converting a file into its own format would loop forever; skip those.
    public func wouldLoop(_ url: URL) -> Bool {
        let parts = actionID.split(separator: ".")
        guard parts.count == 3, parts[1] == "to" else { return false }
        let target = String(parts[2])
        let ext = url.pathExtension.lowercased()
        return ext == target || (target == "jpg" && ext == "jpeg") || (target == "tiff" && ext == "tif")
    }
}
