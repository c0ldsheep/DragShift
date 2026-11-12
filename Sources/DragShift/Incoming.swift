import AppKit
import DragShiftCore
import UniformTypeIdentifiers

/// What's being dragged: real files (Finder, Desktop), promised files (Photos, Mail,
/// Safari downloads) or raw image data (an image dragged out of a web page).
enum Incoming {
    static let imageTypes: [NSPasteboard.PasteboardType] = [.png, .tiff, NSPasteboard.PasteboardType(UTType.jpeg.identifier)]

    static var dragTypes: [NSPasteboard.PasteboardType] {
        [.fileURL] + NSFilePromiseReceiver.readableDraggedTypes.map { NSPasteboard.PasteboardType($0) } + imageTypes
    }

    struct Peek {
        var urls: [URL] = []
        /// Type identifiers of promised files (we know what they are before they exist).
        var promised: [String] = []
        var imageData = false

        var isEmpty: Bool { urls.isEmpty && promised.isEmpty && !imageData }

        /// Stand-in URLs so the ring can pick slices before promised files arrive.
        var catalogURLs: [URL] {
            if !urls.isEmpty { return urls }
            if !promised.isEmpty {
                return promised.enumerated().map { i, uti in
                    let ext = UTType(uti)?.preferredFilenameExtension ?? "jpg"
                    return URL(fileURLWithPath: "/tmp/promised-\(i).\(ext)")
                }
            }
            return imageData ? [URL(fileURLWithPath: "/tmp/image.png")] : []
        }
    }

    static func peek(_ pb: NSPasteboard) -> Peek {
        var p = Peek()
        p.urls = (pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]) ?? []
        if p.urls.isEmpty,
           let receivers = pb.readObjects(forClasses: [NSFilePromiseReceiver.self], options: nil) as? [NSFilePromiseReceiver] {
            p.promised = receivers.flatMap(\.fileTypes)
        }
        if p.urls.isEmpty && p.promised.isEmpty {
            p.imageData = pb.availableType(from: imageTypes) != nil
        }
        return p
    }

    /// Where temporary copies live: one folder per drop or paste, inside the app's cache folder.
    static var inboxRoot: URL {
        rootForChecks ?? FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0].appendingPathComponent("DragShift/Incoming")
    }
    /// Set only by `--inbox-check`, so the check never touches the real copies.
    nonisolated(unsafe) static var rootForChecks: URL?

    /// A new, empty folder for one drop or paste.
    static var inbox: URL {
        let u = inboxRoot.appendingPathComponent(UUID().uuidString)
        try? FileManager.default.createDirectory(at: u, withIntermediateDirectories: true)
        return u
    }

    /// The inbox folder a temporary copy is in. nil for a file the person chose themselves: those are never touched.
    static func inboxFolder(of file: URL) -> URL? {
        let root = inboxRoot.standardizedFileURL.path + "/"
        let path = file.standardizedFileURL.path
        guard path.hasPrefix(root), let name = path.dropFirst(root.count).split(separator: "/").first else { return nil }
        return inboxRoot.appendingPathComponent(String(name))
    }

    /// Keeps the temporary copies among `files` until what comes back is called; when nothing holds a copy any
    /// more, it is deleted. Files that aren't temporary copies are ignored.
    @MainActor static func hold(_ files: [URL]) -> () -> Void {
        InboxLeases.shared.hold(Set(files.compactMap(inboxFolder)))
    }

    /// Copy, Copy Path and sharing hand a file to another app that reads it later, so a temporary copy first gets a
    /// lasting home in Downloads (what comes back is where each file is now). Chosen files come back as they are.
    static func keep(_ files: [URL], in folder: URL = Naming.downloads) -> [URL] {
        files.map { f in
            guard inboxFolder(of: f) != nil else { return f }
            let to = Naming.unique(folder.appendingPathComponent(f.lastPathComponent))
            return (try? FileManager.default.moveItem(at: f, to: to)) != nil ? to : f
        }
    }

    /// At launch and at quit nothing can be using a temporary copy: remove them all.
    static func sweepAll() {
        try? FileManager.default.removeItem(at: inboxRoot)
    }

    /// While running: remove copies older than a day that nothing holds (a safety net; they normally go when the
    /// action ends).
    @MainActor static func sweepOld() {
        let fm = FileManager.default
        let folders = ((try? fm.contentsOfDirectory(at: inboxRoot, includingPropertiesForKeys: [.creationDateKey])) ?? [])
            .map { (url: $0, made: (try? $0.resourceValues(forKeys: [.creationDateKey]))?.creationDate ?? Date()) }
        for old in InboxSweep.expired(folders, now: Date()) where !InboxLeases.shared.isHeld(old) {
            try? fm.removeItem(at: old)
        }
    }

    /// Turns the drop into file URLs. `temporary` is true when we had to create the files.
    static func receive(_ pb: NSPasteboard, done: @escaping @MainActor ([URL], Bool) -> Void) {
        let p = peek(pb)
        if !p.urls.isEmpty {
            Task { @MainActor in done(p.urls, false) }
            return
        }
        if !p.promised.isEmpty,
           let receivers = pb.readObjects(forClasses: [NSFilePromiseReceiver.self], options: nil) as? [NSFilePromiseReceiver] {
            let dest = inbox
            let queue = OperationQueue()
            let group = DispatchGroup()
            let lock = NSLock()
            var got: [URL] = []
            for r in receivers {
                for _ in r.fileTypes { group.enter() }
                r.receivePromisedFiles(atDestination: dest, options: [:], operationQueue: queue) { url, error in
                    if error == nil { lock.lock(); got.append(url); lock.unlock() }
                    group.leave()
                }
            }
            DispatchQueue.global().async {
                _ = group.wait(timeout: .now() + 60)
                lock.lock(); let urls = got.sorted(by: Naming.finderOrder); lock.unlock()
                if urls.isEmpty { try? FileManager.default.removeItem(at: dest) } // nothing arrived: no empty folder stays
                Task { @MainActor in done(urls, true) }
            }
            return
        }
        if let type = pb.availableType(from: imageTypes), let data = pb.data(forType: type) {
            let name = (pb.string(forType: .URL).flatMap { URL(string: $0)?.deletingPathExtension().lastPathComponent })
                .flatMap { $0.isEmpty ? nil : $0 } ?? "Image"
            let ext = type == .png ? "png" : (type == .tiff ? "tiff" : "jpg")
            let url = inbox.appendingPathComponent(name).appendingPathExtension(ext)
            try? data.write(to: url)
            Task { @MainActor in done([url], true) }
            return
        }
        Task { @MainActor in done([], false) }
    }
}

/// Who is still using which temporary copy. A copy is deleted when its last user lets go: the job that was
/// reading it has finished or was cancelled, the editor window it was open in has closed, the ring it was shown
/// in has gone.
@MainActor
final class InboxLeases {
    static let shared = InboxLeases()
    private var held: [URL: Int] = [:]
    private var waiting: Set<URL> = []
    private var paused = 0

    func isHeld(_ folder: URL) -> Bool { held[folder.standardizedFileURL] != nil }

    func hold(_ folders: Set<URL>) -> () -> Void {
        let folders = Set(folders.map(\.standardizedFileURL))
        for f in folders { held[f, default: 0] += 1 }
        var released = false
        return { [self] in
            guard !released else { return }
            released = true
            for f in folders {
                let n = (held[f] ?? 1) - 1
                held[f] = n > 0 ? n : nil
                if n <= 0 { waiting.insert(f) }
            }
            // On the next turn of the main loop, not now: a window that closes and then starts its job (the size
            // window, the print window) lets go of the file a moment before the job takes hold of it.
            DispatchQueue.main.async { self.removeWaiting() }
        }
    }

    /// An editor's window has closed and its work is running: nothing is deleted until what comes back is called.
    func pause() -> () -> Void {
        paused += 1
        var resumed = false
        return { [self] in
            guard !resumed else { return }
            resumed = true
            paused -= 1
            removeWaiting()
        }
    }

    private func removeWaiting() {
        guard paused == 0 else { return }
        for f in waiting where held[f] == nil { try? FileManager.default.removeItem(at: f) }
        waiting.removeAll()
    }
}

extension Incoming {
    /// Developer check (`DragShift --inbox-check`): the life of a temporary copy, in a folder of its own.
    @MainActor static func selfCheck() async -> (ok: Bool, text: String) {
        let fm = FileManager.default
        let base = fm.temporaryDirectory.appendingPathComponent("dragshift-inbox-check-\(UUID().uuidString)")
        rootForChecks = base.appendingPathComponent("Incoming")
        defer { rootForChecks = nil; try? fm.removeItem(at: base) }
        var lines: [String] = [], ok = true
        func check(_ name: String, _ pass: Bool) { lines.append((pass ? "PASS " : "FAIL ") + name); if !pass { ok = false } }
        func copy(_ name: String = "Clipboard Image.png") -> URL {
            let u = inbox.appendingPathComponent(name)
            try? Data("x".utf8).write(to: u)
            return u
        }
        func exists(_ u: URL) -> Bool { fm.fileExists(atPath: u.path) }
        func turn() async { try? await Task.sleep(nanoseconds: 60_000_000) }

        let chosen = base.appendingPathComponent("My own file.png")
        try? fm.createDirectory(at: base, withIntermediateDirectories: true)
        try? Data("mine".utf8).write(to: chosen)

        let a = copy()
        check("a temporary copy is known by its folder", inboxFolder(of: a) == a.deletingLastPathComponent())
        check("a file the person chose is not a temporary copy", inboxFolder(of: chosen) == nil)

        let r = hold([a]); await turn()
        check("held: the copy stays", exists(a))
        r(); r(); await turn()
        check("let go: the copy and its folder are gone", !exists(a) && !exists(a.deletingLastPathComponent()))

        let b = copy(), r1 = hold([b]), r2 = hold([b])
        r1(); await turn()
        check("two users, one lets go: the copy stays", exists(b))
        r2(); await turn()
        check("the last user lets go: gone", !exists(b))

        let c = copy(), window = hold([c])
        window()                       // the size window closes...
        let job = hold([c])            // ...and starts its job in the same moment
        await turn()
        check("window closes, then its job starts: the copy is still there for the job", exists(c))
        job(); await turn()
        check("the job ends: gone", !exists(c))

        let d = copy(), editor = hold([d])
        let resume = InboxLeases.shared.pause()   // an editor's Save: the window closes, the work runs
        editor(); await turn()
        check("editor window closed, its work still running: the copy stays", exists(d))
        resume(); await turn()
        check("the editor's work ends: gone", !exists(d))

        let mine = hold([chosen])
        mine(); await turn()
        check("a file the person chose is never deleted", exists(chosen))

        let e = copy("Photo.jpg")
        let downloads = base.appendingPathComponent("Downloads")
        try? fm.createDirectory(at: downloads, withIntermediateDirectories: true)
        try? Data("taken".utf8).write(to: downloads.appendingPathComponent("Photo.jpg"))
        let kept = keep([e, chosen], in: downloads)
        check("Copy and Share: the temporary copy moves to a lasting place under a free name",
              kept[0].lastPathComponent == "Photo 2.jpg" && exists(kept[0]) && !exists(e) && kept[1] == chosen)
        hold([e])(); await turn()
        check("and the lasting copy stays after the action ends", exists(kept[0]))

        let old = copy(), oldHeld = copy(), fresh = copy()
        let twoDaysAgo = Date().addingTimeInterval(-2 * 86400)
        for u in [old, oldHeld] { try? fm.setAttributes([.creationDate: twoDaysAgo], ofItemAtPath: u.deletingLastPathComponent().path) }
        let stillUsed = hold([oldHeld])
        sweepOld()
        check("hourly sweep: an old forgotten copy goes", !exists(old))
        check("hourly sweep: an old copy still in use stays, and so does a new one", exists(oldHeld) && exists(fresh))
        stillUsed(); await turn()

        _ = copy()
        sweepAll()
        check("launch and quit: nothing is left", !exists(inboxRoot))
        check("the person's own file was never touched", (try? String(contentsOf: chosen, encoding: .utf8)) == "mine")
        return (ok, (lines + [ok ? "ALL PASSED" : "SOMETHING FAILED"]).joined(separator: "\n"))
    }
}
