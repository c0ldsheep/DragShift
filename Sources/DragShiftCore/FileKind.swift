import Foundation
import ImageIO
import UniformTypeIdentifiers

/// What kind of thing the user is dragging. The ring shows different slices for each.
public enum FileKind: String, Sendable, CaseIterable {
    case image, pdf, document, video, audio, archive, folder, other

    public var label: String {
        switch self {
        case .image: return "Image"
        case .pdf: return "PDF"
        case .document: return "Document"
        case .video: return "Video"
        case .audio: return "Audio"
        case .archive: return "Archive"
        case .folder: return "Folder"
        case .other: return "File"
        }
    }
}

/// Image formats DragShift can write. `available` is checked against ImageIO at runtime,
/// so a format the OS can't encode never shows up on the ring.
public enum ImageFormat: String, Sendable, CaseIterable {
    case jpg, png, heic, webp, avif, tiff, gif, bmp

    public var ext: String { self == .tiff ? "tiff" : rawValue }
    public var title: String { rawValue.uppercased() }

    public var uti: String {
        switch self {
        case .jpg: return UTType.jpeg.identifier
        case .png: return UTType.png.identifier
        case .heic: return UTType.heic.identifier
        case .webp: return "org.webmproject.webp"
        case .avif: return "public.avif"
        case .tiff: return UTType.tiff.identifier
        case .gif: return UTType.gif.identifier
        case .bmp: return UTType.bmp.identifier
        }
    }

    public var supportsAlpha: Bool {
        switch self {
        case .jpg, .bmp: return false
        default: return true
        }
    }

    /// Lossy formats honour the quality setting; the rest ignore it.
    public var isLossy: Bool { [.jpg, .heic, .webp, .avif].contains(self) }

    public static func from(ext: String) -> ImageFormat? {
        switch ext.lowercased() {
        case "jpg", "jpeg", "jpe", "jfif": return .jpg
        case "png": return .png
        case "heic", "heif": return .heic
        case "webp": return .webp
        case "avif": return .avif
        case "tif", "tiff": return .tiff
        case "gif": return .gif
        case "bmp": return .bmp
        default: return nil
        }
    }

    /// True when this Mac can write the format (ImageIO natively, or cwebp for WebP).
    public var available: Bool {
        if self == .webp { return true } // cwebp if installed, else the built-in lossless writer
        return ImageFormat.writable.contains(uti)
    }

    static let writable: Set<String> = {
        Set((CGImageDestinationCopyTypeIdentifiers() as? [String]) ?? [])
    }()
}

public enum FileInspector {
    static let documentExts: Set<String> = [
        "docx", "doc", "rtf", "rtfd", "odt", "txt", "text", "md", "markdown", "html", "htm", "webarchive",
    ]
    static let archiveExts: Set<String> = ["zip"]

    public static func kind(of url: URL) -> FileKind {
        var isDir: ObjCBool = false
        if FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir), isDir.boolValue {
            let isPackage = (try? url.resourceValues(forKeys: [.isPackageKey]))?.isPackage ?? false
            if !isPackage { return .folder }
            return url.pathExtension.lowercased() == "rtfd" ? .document : .other
        }
        let ext = url.pathExtension.lowercased()
        if documentExts.contains(ext) { return .document }
        if archiveExts.contains(ext) { return .archive }
        guard let type = UTType(filenameExtension: ext) else { return .other }
        if type.conforms(to: .pdf) { return .pdf }
        if type.conforms(to: .image) { return .image }
        if type.conforms(to: .movie) || type.conforms(to: .video) { return .video }
        if type.conforms(to: .audio) { return .audio }
        return .other
    }

    /// One kind for the whole drag. Mixed drags fall back to the actions every file shares.
    public static func kind(of urls: [URL]) -> FileKind {
        let kinds = Set(urls.map { kind(of: $0) })
        if kinds.count == 1, let k = kinds.first { return k }
        return .other
    }

    public static func size(of url: URL) -> Int64 {
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir) else { return 0 }
        if !isDir.boolValue {
            return Int64((try? url.resourceValues(forKeys: [.fileSizeKey]))?.fileSize ?? 0)
        }
        var total: Int64 = 0
        let e = FileManager.default.enumerator(at: url, includingPropertiesForKeys: [.fileSizeKey])
        while let f = e?.nextObject() as? URL {
            total += Int64((try? f.resourceValues(forKeys: [.fileSizeKey]))?.fileSize ?? 0)
        }
        return total
    }

    /// Images directly inside a folder (not recursive, hidden files skipped), in Finder order.
    public static func images(in folder: URL) -> [URL] {
        let items = (try? FileManager.default.contentsOfDirectory(
            at: folder, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles])) ?? []
        return items.filter { kind(of: $0) == .image }.sorted(by: Naming.finderOrder)
    }
}

/// Optional command-line helpers. Used only if already installed (Homebrew); never downloaded.
public enum Tools {
    public static let cwebp: URL? = find("cwebp")
    public static let ffmpeg: URL? = find("ffmpeg")

    static func find(_ name: String) -> URL? {
        for dir in ["/opt/homebrew/bin", "/usr/local/bin"] {
            let u = URL(fileURLWithPath: dir).appendingPathComponent(name)
            if FileManager.default.isExecutableFile(atPath: u.path) { return u }
        }
        return nil
    }
}

public enum ByteFormat {
    public static func string(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}
