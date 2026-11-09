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

    static var inbox: URL {
        let u = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("DragShift/Incoming/\(UUID().uuidString)")
        try? FileManager.default.createDirectory(at: u, withIntermediateDirectories: true)
        return u
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
