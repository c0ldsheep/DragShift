import AppKit
import ApplicationServices
import DragShiftCore
import PDFKit

/// Sends DragShift's print-ready PDF (laid out by `PrintDocument`) to a printer: the preset's printer, paper,
/// copies, two-sided and colour, with no print dialog unless you ask for "More options…".
@MainActor
enum Printer {
    static var names: [String] { NSPrinter.printerNames }
    static var defaultName: String? { NSPrintInfo.defaultPrinter?.name }

    /// The paper for a preset, portrait: its own size, or whatever the printer is set to.
    static func paper(for preset: PrintPreset) -> CGSize {
        if let s = preset.paper.size { return s }
        let s = info(for: preset).paperSize
        guard s.width > 0, s.height > 0 else { return PrintPreset.Paper.a4.size! }
        return s.width > s.height ? CGSize(width: s.height, height: s.width) : s
    }

    /// Print settings for a preset. Our PDF already has the margins, so the page is printed 1:1, centred.
    static func info(for preset: PrintPreset, paper: CGSize? = nil) -> NSPrintInfo {
        let info = NSPrintInfo()
        if let name = preset.printer, let p = NSPrinter(name: name) { info.printer = p }
        if let paper { info.paperSize = paper }
        info.orientation = .portrait
        info.topMargin = 0
        info.bottomMargin = 0
        info.leftMargin = 0
        info.rightMargin = 0
        info.isHorizontallyCentered = true
        info.isVerticallyCentered = true
        info.dictionary()[NSPrintInfo.AttributeKey.copies] = max(1, min(99, preset.copies))
        let settings = OpaquePointer(info.pmPrintSettings())
        PMSetDuplex(settings, PMDuplexMode(preset.twoSided ? kPMDuplexNoTumble : kPMDuplexNone))
        if !preset.color {
            // The pages are already grey; these ask the printer to use black ink only, where its driver listens.
            PMPrintSettingsSetValue(settings, "ColorModel" as CFString, "Gray" as CFString, false)
            PMPrintSettingsSetValue(settings, "print-color-mode" as CFString, "monochrome" as CFString, false)
        }
        info.updateFromPMPrintSettings()
        return info
    }

    /// A macOS preset: exactly what the macOS print window saved (pages per sheet, orientation, quality, paper type…),
    /// on the preset's printer. nil when the preset has since been deleted in macOS.
    static func info(system preset: PrintPreset) -> NSPrintInfo? {
        guard let saved = SystemPresets.settings(for: preset) else { return nil }
        let info = NSPrintInfo()
        if let name = preset.printer, let p = NSPrinter(name: name) { info.printer = p }
        let settings = OpaquePointer(info.pmPrintSettings())
        for (key, value) in saved { PMPrintSettingsSetValue(settings, key as CFString, value as AnyObject, false) }
        info.updateFromPMPrintSettings()
        if let o = saved["com.apple.print.PageFormat.PMOrientation"] as? Int ?? saved["com.apple.print.preset.Orientation"] as? Int {
            info.orientation = o == 2 || o == 4 ? .landscape : .portrait
        }
        return info
    }

    /// Lays the files out with the progress bubble, then sends them. `panel`: show the macOS print dialog first.
    /// `saveTo`: write what would be printed to a PDF instead (checks, and "print to PDF").
    static func run(_ files: [URL], preset: PrintPreset, at point: NSPoint, panel: Bool = false, saveTo: URL? = nil,
                    done: ((Bool) -> Void)? = nil) {
        let printable = files.filter(PrintDocument.canPrint)
        guard !printable.isEmpty else {
            HUD.shared.fail("Nothing to print", "Pictures, PDFs and documents can be printed", near: point)
            done?(false)
            return
        }
        HUD.shared.begin("Printing", count: printable.count, near: point)
        let sheet = paper(for: preset)
        Task { @MainActor in
            do {
                let dir = FileManager.default.temporaryDirectory.appendingPathComponent("DragShift Print", isDirectory: true)
                try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
                let job = try await PrintDocument.make(printable, preset: preset, paper: sheet, dir: dir)
                defer { try? FileManager.default.removeItem(at: job.pdf) }
                let sent = try send(job, preset: preset, paper: sheet, title: files[0].lastPathComponent, panel: panel, saveTo: saveTo)
                let skipped = (files.count - printable.count) + job.notes.count
                if sent {
                    let printer = saveTo == nil ? (preset.printer ?? defaultName ?? "the printer") : "a PDF"
                    var detail = "\(job.pages) page\(job.pages == 1 ? "" : "s")"
                    if preset.copies > 1 { detail += " · \(preset.copies) copies" }
                    if skipped > 0 { detail += " · skipped \(skipped)" }
                    HUD.shared.flash("Sent to \(printer)", detail, near: point)
                    Log.line("print: \(job.pages) page(s) to \(printer), preset \(preset.id)")
                } else {
                    HUD.shared.flash("Not printed", panel ? "Cancelled" : "The printer didn't take it", near: point)
                }
                done?(sent)
            } catch {
                HUD.shared.fail("Couldn't print", error.localizedDescription, near: point)
                Log.line("print failed: \(error.localizedDescription)")
                done?(false)
            }
        }
    }

    static func send(_ job: PrintJob, preset: PrintPreset, paper: CGSize, title: String, panel: Bool, saveTo: URL?) throws -> Bool {
        guard let doc = PDFDocument(url: job.pdf) else { throw CocoaError(.fileReadCorruptFile) }
        let settings: NSPrintInfo
        if preset.system != nil {
            guard let saved = info(system: preset) else {
                throw NSError(domain: "DragShift", code: 2, userInfo: [NSLocalizedDescriptionKey:
                    "“\(preset.name)” isn't in the macOS print window any more"])
            }
            settings = saved
        } else {
            settings = info(for: preset, paper: paper)
        }
        if let saveTo {
            settings.jobDisposition = .save
            settings.dictionary()[NSPrintInfo.AttributeKey.jobSavingURL] = saveTo
        }
        // Sideways pages (a 4×6 passport sheet) turn to fit the paper. A macOS preset's pages are as they came, so
        // they're fitted to the paper the way Preview does.
        let scaling: PDFPrintScalingMode = preset.system != nil ? .pageScaleToFit : .pageScaleNone
        guard let op = doc.printOperation(for: settings, scalingMode: scaling, autoRotate: true) else {
            throw CocoaError(.featureUnsupported)
        }
        op.jobTitle = title
        op.showsProgressPanel = false
        op.showsPrintPanel = panel
        if panel {
            op.printPanel.options.formUnion([.showsCopies, .showsPaperSize, .showsPreview, .showsPageRange])
            NSApp.activate(ignoringOtherApps: true)
        }
        return op.run()
    }
}
