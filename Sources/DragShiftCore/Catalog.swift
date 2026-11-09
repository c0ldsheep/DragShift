import Foundation

/// One slice on the ring.
public struct Action: Identifiable, Hashable, Sendable {
    public enum Editor: String, Sendable {
        case crop, backdrop, redact, resize, lockPDF, unlockPDF, trim, annotate, rename, translate, fitKB, print
    }

    public let id: String
    public let title: String
    public let symbol: String
    /// Shown in the hub and the progress bubble: "Converting to JPG".
    public let verb: String
    public let editor: Editor?
    /// Runs in the app (clipboard, AirDrop, Mail...) instead of the file engine.
    public let isShare: Bool

    public init(_ id: String, _ title: String, _ symbol: String, verb: String, editor: Editor? = nil, share: Bool = false) {
        self.id = id; self.title = title; self.symbol = symbol; self.verb = verb; self.editor = editor; self.isShare = share
    }

    public var isRecipe: Bool { id.hasPrefix("recipe.") }
}

/// A page of slices. Shift or a finger tap flips between pages.
public struct RingPage: Identifiable, Hashable, Sendable {
    public let id: String
    public let title: String
    public let actions: [Action]
}

public enum Catalog {
    public static let maxSlices = 8

    // MARK: Building blocks

    static func toImage(_ f: ImageFormat) -> Action {
        Action("img.to.\(f.rawValue)", f.title, symbol(for: f), verb: "Converting to \(f.title)")
    }

    static func symbol(for f: ImageFormat) -> String {
        switch f {
        case .jpg: return "photo"
        case .png: return "photo.on.rectangle"
        case .heic: return "camera"
        case .webp: return "globe"
        case .avif: return "sparkles"
        case .tiff: return "square.stack"
        case .gif: return "play.square.stack"
        case .bmp: return "square.grid.3x3"
        }
    }

    static func toDoc(_ f: DocFormat, from kind: String) -> Action {
        let symbols: [DocFormat: String] = [.pdf: "doc.richtext", .docx: "doc.text", .doc: "doc", .rtf: "textformat",
                                            .odt: "doc.plaintext", .html: "chevron.left.forwardslash.chevron.right",
                                            .txt: "text.alignleft", .md: "number"]
        let title = f == .txt ? "TEXT" : (f == .docx ? "WORD" : f.title)
        return Action("\(kind).to.\(f.rawValue)", title, symbols[f] ?? "doc", verb: "Converting to \(title)")
    }

    static func toMedia(_ f: MediaFormat) -> Action {
        let symbols: [MediaFormat: String] = [.mp4: "film", .mov: "film.stack", .hevc: "bolt", .gif: "play.square.stack",
                                              .m4a: "music.note", .wav: "waveform", .aiff: "waveform.path",
                                              .caf: "waveform.badge.plus", .mp3: "music.note.list", .flac: "hifispeaker",
                                              .webm: "globe"]
        let title = f == .gif ? "GIF" : f.title
        return Action("media.to.\(f.rawValue)", title, symbols[f] ?? "film", verb: "Converting to \(title)")
    }

    // Image tools
    static let crop = Action("img.crop", "Crop", "crop", verb: "Cropping", editor: .crop)
    static let resize = Action("img.resize", "Resize", "arrow.up.left.and.arrow.down.right", verb: "Resizing", editor: .resize)
    static let rotate = Action("img.rotate", "Rotate", "rotate.right", verb: "Rotating")
    static let removeBG = Action("img.removebg", "Cut Out", "person.crop.rectangle", verb: "Removing background")
    static let addBG = Action("img.backdrop", "Add BG", "photo.artframe", verb: "Adding background", editor: .backdrop)
    static let enhance = Action("img.enhance", "Enhance", "wand.and.stars", verb: "Enhancing")
    static let redact = Action("img.redact", "Redact", "eye.slash", verb: "Redacting", editor: .redact)
    static let compressImg = Action("img.compress", "Compress", "arrow.down.right.and.arrow.up.left", verb: "Compressing")
    static let shopPhoto = Action("img.shop", "Shop Photo", "bag", verb: "Making a white-background product photo")
    static let clean = Action("img.clean", "Clean Info", "location.slash", verb: "Removing location and camera info")
    static let flip = Action("img.flip", "Flip", "arrow.left.and.right.righttriangle.left.righttriangle.right", verb: "Flipping")
    static let stitch = Action("img.stitch", "Stitch", "rectangle.split.1x2", verb: "Stitching into one long picture")
    static let gifFromImages = Action("img.gif", "Make GIF", "play.square.stack", verb: "Making a GIF")
    static let ocr = Action("img.to.text", "TEXT", "text.viewfinder", verb: "Reading text")
    static let annotate = Action("img.annotate", "Annotate", "pencil.tip.crop.circle", verb: "Annotating", editor: .annotate)
    static let watermark = Action("img.watermark", "Watermark", "signature", verb: "Adding the watermark")
    static let readQR = Action("img.qr.read", "Read QR", "qrcode.viewfinder", verb: "Reading the code")
    static let translate = Action("img.translate", "Translate", "character.bubble", verb: "Translating", editor: .translate)
    static let docQR = Action("doc.qr", "QR Code", "qrcode", verb: "Making a QR code")
    static let passport = Action("img.passport", "Passport", "person.crop.square", verb: "Making a passport photo")
    /// Opens the "Under ___ KB" chooser; the sizes themselves are `fit.kb.50` and so on.
    static let fitKB = Action("fit.kb", "Under KB", "scalemass", verb: "Fitting under a size", editor: .fitKB)

    // PDF tools
    static let pdfCompress = Action("pdf.compress", "Compress", "arrow.down.right.and.arrow.up.left", verb: "Compressing")
    static let pdfSplit = Action("pdf.split", "Split", "rectangle.split.3x1", verb: "Splitting pages")
    static let pdfMerge = Action("pdf.merge", "Merge", "rectangle.stack.badge.plus", verb: "Merging")
    static let pdfRotate = Action("pdf.rotate", "Rotate", "rotate.right", verb: "Rotating pages")
    static let pdfLock = Action("pdf.lock", "Lock", "lock", verb: "Adding a password", editor: .lockPDF)
    static let pdfUnlock = Action("pdf.unlock", "Unlock", "lock.open", verb: "Removing the password", editor: .unlockPDF)
    static let pdfSearchable = Action("pdf.searchable", "Searchable", "text.magnifyingglass", verb: "Making it searchable")

    // Media tools
    static let mediaCompress = Action("media.compress", "Compress", "arrow.down.right.and.arrow.up.left", verb: "Compressing")
    static let trim = Action("media.trim", "Trim", "scissors", verb: "Trimming", editor: .trim)
    static let mute = Action("media.mute", "Mute", "speaker.slash", verb: "Removing sound")
    static let still = Action("media.still", "Still", "camera.viewfinder", verb: "Saving a still")
    static let vertical = Action("media.vertical", "9:16", "rectangle.portrait", verb: "Cropping to 9:16")
    static let square = Action("media.square", "1:1", "square", verb: "Cropping to square")
    static let speed2 = Action("media.speed.2", "Speed 2×", "forward", verb: "Speeding up")

    // Files and folders
    public static let zip = Action("file.zip", "Zip", "doc.zipper", verb: "Zipping")
    static let unzip = Action("file.unzip", "Extract", "archivebox", verb: "Extracting")
    static let folderJPG = Action("folder.jpg", "All → JPG", "photo", verb: "Converting every image to JPG")
    static let folderPNG = Action("folder.png", "All → PNG", "photo.on.rectangle", verb: "Converting every image to PNG")
    static let folderPDF = Action("folder.pdf", "Images → PDF", "doc.richtext", verb: "Putting the images in one PDF")
    static let folderCompress = Action("folder.compress", "Shrink All", "arrow.down.right.and.arrow.up.left", verb: "Compressing every image")
    static let folderGIF = Action("folder.gif", "Make GIF", "play.square.stack", verb: "Making a GIF")

    // Files page (handled by the app, except Zip)
    static let rename = Action("file.rename", "Rename", "character.cursor.ibeam", verb: "Renaming", editor: .rename)
    /// Opens the print window (printer, saved presets, preview); a preset itself is `print.preset.<id>`.
    public static let print = Action("file.print", "Print", "printer", verb: "Printing", editor: .print)
    /// The last slice of the Print page: the print window, for everything the presets don't cover.
    public static let printMore = Action("file.print", "More…", "ellipsis.circle", verb: "Printing", editor: .print)
    public static let sharePage = RingPage(id: "share", title: "Files", actions: [
        Action("share.copy", "Copy", "doc.on.doc", verb: "Copied", share: true),
        Action("share.airdrop", "AirDrop", "airplayaudio", verb: "AirDrop", share: true),
        Action("share.mail", "Mail", "envelope", verb: "Mail", share: true),
        Action("share.messages", "Messages", "message", verb: "Messages", share: true),
        zip,
        rename,
        Action("share.path", "Copy Path", "link", verb: "Copied the path", share: true),
        Action("share.more", "Share…", "square.and.arrow.up", verb: "Share", share: true),
    ])

    /// Same idea for different file types, so one favourite ("Compress") works for all of them.
    public static let families: [[String]] = [
        ["img.compress", "pdf.compress", "media.compress", "folder.compress"],
        ["img.to.jpg", "pdf.to.jpg", "folder.jpg"],
        ["img.to.png", "pdf.to.png", "folder.png"],
        ["img.to.pdf", "doc.to.pdf", "folder.pdf", "mixed.merge"],
        ["img.to.text", "pdf.to.txt", "doc.to.txt"],
        ["img.rotate", "pdf.rotate"],
        ["img.gif", "media.to.gif", "folder.gif"],
        ["pdf.to.docx", "doc.to.docx"],
    ]

    public static let defaultFavorites = ["img.to.jpg", "img.to.pdf", "img.compress", "img.removebg", "recipe.shop-ready",
                                          "media.vertical", "file.zip", "share.airdrop"]

    // MARK: Pages per kind

    /// `hidden` lets the user switch off slices they never use; `favorites` (action or recipe ids)
    /// become the first page, showing only the ones that fit these files.
    public static func pages(for urls: [URL], hidden: Set<String> = [], favorites: [String] = [],
                             recipes: [Recipe] = [], printPresets: [PrintPreset] = []) -> [RingPage] {
        var pages = basePages(for: urls, hidden: [])
        let faves = favoriteActions(favorites, available: pages.flatMap(\.actions), files: urls, recipes: recipes,
                                    printPresets: printPresets)
        pages = pages.map { RingPage(id: $0.id, title: $0.title, actions: $0.actions.filter { !hidden.contains($0.id) }) }
            .filter { !$0.actions.isEmpty }
        // Print page: drop a file on a preset and it prints. Hidden presets stay off it.
        let shown = printPresets.filter { !hidden.contains($0.stepID) }
        if !shown.isEmpty, !urls.isEmpty, urls.allSatisfy(PrintDocument.canPrint) {
            let page = RingPage(id: "print", title: "Print", actions: shown.prefix(maxSlices - 1).map(\.action)
                                                                         + (hidden.contains(printMore.id) ? [] : [printMore]))
            pages.insert(page, at: pages.firstIndex { $0.id == sharePage.id } ?? pages.endIndex)
        }
        if !faves.isEmpty { pages.insert(RingPage(id: "favorites", title: "Favorites", actions: faves), at: 0) }
        return pages
    }

    /// Favourites that apply to these files, in the user's order (max 8).
    public static func favoriteActions(_ favorites: [String], available: [Action]? = nil, files: [URL],
                                       recipes: [Recipe], printPresets: [PrintPreset] = []) -> [Action] {
        let avail = available ?? basePages(for: files, hidden: []).flatMap(\.actions)
        let byID = Dictionary(avail.map { ($0.id, $0) }, uniquingKeysWith: { a, _ in a })
        let kind = FileInspector.kind(of: files)
        var out: [Action] = []
        var seen = Set<String>()
        for f in favorites {
            var found: Action?
            if f.hasPrefix("recipe.") {
                if let r = recipes.first(where: { $0.id == f }), r.kinds.contains(kind) { found = r.action }
            } else if let a = byID[f] {
                found = a
            } else if let kb = SizeLimit.kb(fromStep: f) {
                if kind == .image || kind == .pdf { found = SizeLimit.action(kb: kb) }
            } else if let pid = PrintPreset.id(fromStep: f) {
                if !files.isEmpty, files.allSatisfy(PrintDocument.canPrint), let p = printPresets.first(where: { $0.id == pid }) {
                    found = p.action
                }
            } else if let fam = families.first(where: { $0.contains(f) }) {
                found = fam.lazy.compactMap { byID[$0] }.first
            }
            if let a = found, seen.insert(a.id).inserted { out.append(a) }
            if out.count == maxSlices { break }
        }
        return out
    }

    static func basePages(for urls: [URL], hidden: Set<String>) -> [RingPage] {
        let kind = FileInspector.kind(of: urls)
        let many = urls.count > 1
        var pages: [RingPage] = []

        func page(_ id: String, _ title: String, _ actions: [Action]) {
            let list = actions.filter { !hidden.contains($0.id) }.prefix(maxSlices)
            if !list.isEmpty { pages.append(RingPage(id: id, title: title, actions: Array(list))) }
        }
        let cutOut: [Action] = VisionOps.canCutOut ? [removeBG] : []

        switch kind {
        case .image:
            let own = Set(urls.compactMap { ImageFormat.from(ext: $0.pathExtension) })
            var convert: [Action] = ImageFormat.allCases
                .filter { $0.available && $0 != .bmp && $0 != .gif && !(own.count == 1 && own.contains($0)) }
                .map(toImage)
            convert.insert(Action("img.to.pdf", "PDF", "doc.richtext", verb: many ? "Putting the images in one PDF" : "Converting to PDF"),
                           at: min(5, convert.count))
            convert.append(ocr)
            page("convert", "Convert", convert)
            // Annotate, Redact and Translate open one picture at a time, so they only show for one; Stitch needs several.
            page("edit", "Edit", [crop] + (many ? [stitch] : [annotate]) + [resize, rotate] + cutOut + [addBG, enhance, compressImg])
            page("more", "More", [passport, fitKB] + (VisionOps.canCutOut ? [shopPhoto] : []) + (many ? [] : [redact])
                + [clean, watermark, readQR] + (Translator.available && !many ? [translate] : []) + (many ? [gifFromImages] : []))
        case .pdf:
            page("convert", "Convert", [toImage(.jpg).with(id: "pdf.to.jpg"), toImage(.png).with(id: "pdf.to.png"),
                                         toDoc(.txt, from: "pdf"), toDoc(.docx, from: "pdf"), toDoc(.rtf, from: "pdf"),
                                         toDoc(.html, from: "pdf"), toDoc(.md, from: "pdf")])
            page("edit", "Edit", [pdfCompress, fitKB, pdfSearchable, pdfSplit] + (many ? [pdfMerge] : []) + [pdfRotate, pdfLock, pdfUnlock])
        case .document:
            let own = Set(urls.compactMap { DocFormat.from(ext: $0.pathExtension) })
            let targets: [DocFormat] = [.pdf, .docx, .rtf, .txt, .html, .md, .odt, .doc]
            page("convert", "Convert", targets.filter { !(own.count == 1 && own.contains($0)) }.map { toDoc($0, from: "doc") }
                + [docQR])
        case .video:
            let own = urls.first.map { $0.pathExtension.lowercased() } ?? ""
            let targets: [MediaFormat] = [.mp4, .mov, .gif, .hevc, .m4a, .mp3, .wav, .webm]
            page("convert", "Convert", targets.filter { $0.available && !(own == $0.ext && $0 != .hevc) }.map(toMedia))
            page("edit", "Edit", [mediaCompress, trim, vertical, square, speed2, mute, still])
        case .audio:
            let own = urls.first.map { $0.pathExtension.lowercased() } ?? ""
            let targets: [MediaFormat] = [.m4a, .mp3, .wav, .aiff, .flac, .caf]
            page("convert", "Convert", (targets.filter { $0.available && $0.ext != own }.map(toMedia)) + [trim, mediaCompress])
        case .archive:
            page("files", "Archive", [unzip])
        case .folder:
            page("folder", "Folder", [zip, folderJPG, folderPNG, folderPDF, folderCompress, folderGIF])
        case .other:
            let kinds = Set(urls.map { FileInspector.kind(of: $0) })
            if kinds.isSubset(of: [.image, .pdf]) {
                page("files", "Files", [Action("mixed.merge", "One PDF", "rectangle.stack.badge.plus", verb: "Merging into one PDF")])
            }
        }
        let shown = Set(pages.flatMap(\.actions).map(\.id))
        // Things you can print get Print here, in place of Messages (which "Share…" still reaches).
        let printable = !urls.isEmpty && urls.allSatisfy(PrintDocument.canPrint)
        let files = sharePage.actions.map { printable && $0.id == "share.messages" ? Catalog.print : $0 }
        page("share", "Files", files.filter { !shown.contains($0.id) })
        return pages
    }

    /// Every slice that can ever appear, for the Settings list.
    public static var everyAction: [Action] {
        var seen = Set<String>()
        let samples = ["a.jpg", "a.png", "a.pdf", "a.docx", "a.mov", "a.wav", "a.zip"].map { URL(fileURLWithPath: "/tmp/\($0)") }
            + [FileManager.default.temporaryDirectory]
        var all: [Action] = []
        for s in samples {
            for p in basePages(for: [s], hidden: []) + basePages(for: [s, s], hidden: []) {
                for a in p.actions where seen.insert(a.id).inserted { all.append(a) }
            }
        }
        return all
    }
}

extension Action {
    func with(id: String) -> Action {
        Action(id, title, symbol, verb: verb, editor: editor, share: isShare)
    }
}
