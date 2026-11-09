import Foundation

public struct DragShiftError: LocalizedError, Sendable {
    public let message: String
    public init(_ message: String) { self.message = message }
    public var errorDescription: String? { message }
}

/// Where results go and what they are called. Nothing is ever overwritten:
/// "Page 001.jpg" becomes "Page 001 2.jpg" if the name is taken, the way Finder does it.
public enum Naming {
    /// Folder for results. `override` is the user's chosen folder (nil = next to the original).
    /// Falls back to Downloads when the original's folder is read-only.
    public static func folder(for input: URL, override: URL?) -> URL {
        if let override { return override }
        let dir = input.deletingLastPathComponent()
        if FileManager.default.isWritableFile(atPath: dir.path) { return dir }
        return downloads
    }

    public static var downloads: URL {
        FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Downloads")
    }

    public static func base(_ url: URL) -> String { url.deletingPathExtension().lastPathComponent }

    /// "Page 001" + suffix "Cropped" + ext "jpg" -> "Page 001 Cropped.jpg", made unique in `dir`.
    public static func output(for input: URL, ext: String, suffix: String? = nil, in dir: URL) -> URL {
        var name = base(input)
        if let suffix, !suffix.isEmpty { name += " " + suffix }
        return unique(dir.appendingPathComponent(name).appendingPathExtension(ext))
    }

    public static func unique(_ url: URL) -> URL {
        let fm = FileManager.default
        if !fm.fileExists(atPath: url.path) { return url }
        let dir = url.deletingLastPathComponent()
        let ext = url.pathExtension
        let stem = url.deletingPathExtension().lastPathComponent
        var n = 2
        while true {
            var candidate = dir.appendingPathComponent("\(stem) \(n)")
            if !ext.isEmpty { candidate.appendPathExtension(ext) }
            if !fm.fileExists(atPath: candidate.path) { return candidate }
            n += 1
        }
    }

    /// A new, empty folder for multi-file results (pages of a PDF, a converted folder).
    public static func newFolder(named name: String, in dir: URL) throws -> URL {
        let url = unique(dir.appendingPathComponent(name))
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    public static func finderOrder(_ a: URL, _ b: URL) -> Bool {
        a.lastPathComponent.localizedStandardCompare(b.lastPathComponent) == .orderedAscending
    }

    /// Writes go to a hidden temp file first, then move into place, so a crash never
    /// leaves a half-written file with the real name.
    public static func atomically(_ dest: URL, _ write: (URL) throws -> Void) throws {
        let tmp = dest.deletingLastPathComponent()
            .appendingPathComponent(".dragshift-\(UUID().uuidString)")
            .appendingPathExtension(dest.pathExtension)
        do {
            try write(tmp)
            try FileManager.default.moveItem(at: tmp, to: dest)
        } catch {
            try? FileManager.default.removeItem(at: tmp)
            throw error
        }
    }

    public static func atomically(_ dest: URL, _ write: (URL) async throws -> Void) async throws {
        let tmp = dest.deletingLastPathComponent()
            .appendingPathComponent(".dragshift-\(UUID().uuidString)")
            .appendingPathExtension(dest.pathExtension)
        do {
            try await write(tmp)
            try FileManager.default.moveItem(at: tmp, to: dest)
        } catch {
            try? FileManager.default.removeItem(at: tmp)
            throw error
        }
    }
}

/// Runs a system or Homebrew tool with arguments (never through a shell).
public enum Shell {
    @discardableResult
    public static func run(_ tool: URL, _ args: [String]) throws -> String {
        let p = Process()
        p.executableURL = tool
        p.arguments = args
        let out = Pipe(), err = Pipe()
        p.standardOutput = out
        p.standardError = err
        try p.run()
        // Drain stderr on a private queue (always gets its own thread) so a chatty tool
        // can't fill the pipe and stall.
        var errData = Data()
        let group = DispatchGroup()
        group.enter()
        DispatchQueue(label: "dragshift.shell.stderr").async {
            errData = err.fileHandleForReading.readDataToEndOfFile()
            group.leave()
        }
        let outData = out.fileHandleForReading.readDataToEndOfFile()
        group.wait()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else {
            let msg = String(data: errData, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            throw DragShiftError("\(tool.lastPathComponent) failed: \(msg.suffix(300))")
        }
        return String(data: outData, encoding: .utf8) ?? ""
    }
}
