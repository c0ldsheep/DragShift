import AppKit
import DragShiftCore
import SwiftUI

/// Carries out a picked slice: runs the engine with a progress bubble, opens an editor,
/// or shares. Keeps Recent and Undo.
@MainActor
final class Runner {
    static let shared = Runner()

    /// What Undo will do: bin new files, or put renamed files' names back.
    enum UndoStep {
        case trash([URL])
        case rename([(from: URL, to: URL)])
    }

    private(set) var recent: [URL] = []
    private(set) var undoStep: UndoStep?
    var canUndo: Bool { undoStep != nil }
    private var job = 0
    private var cancelled: Set<Int> = []

    private init() {
        HUD.shared.onUndo = { [weak self] in self?.undo() }
        HUD.shared.onCancel = { [weak self] in
            guard let self else { return }
            self.cancelled.insert(self.job)
            HUD.shared.flash("Cancelled", "Anything already made goes to the Bin", near: NSEvent.mouseLocation)
        }
    }

    func perform(_ action: Action, _ files: [URL], at point: NSPoint, temporary: Bool) {
        Log.line("run: \(action.id) on \(files.count) file(s)")
        Live.shared.lastAction = action.id
        Prefs.setLastAction(action.id, for: FileInspector.kind(of: files))
        if let preset = Prefs.printPreset(step: action.id) {
            Printer.run(files, preset: preset, at: point)
        } else if action.isRecipe {
            guard let recipe = Prefs.recipes.first(where: { $0.id == action.id }) else { return }
            runRecipe(recipe, files, at: point, temporary: temporary)
        } else if action.isShare {
            share(action, files, at: point)
        } else if let editor = action.editor {
            Editors.open(editor, action: action, files: files, temporary: temporary)
        } else {
            run(action, files, at: point, temporary: temporary)
        }
    }

    func run(_ action: Action, _ files: [URL], at point: NSPoint, temporary: Bool) {
        job += 1
        let id = job
        HUD.shared.begin(action.verb, count: files.count, near: point)
        let options = Prefs.options(temporary: temporary)
        Task {
            let report = await Engine.run(action, inputs: files, options: options) { done, total, frac in
                Task { @MainActor in if self.job == id { HUD.shared.update(done: done, total: total, fraction: frac) } }
            }
            self.finish(report, title: action.verb, job: id)
        }
    }

    func runRecipe(_ recipe: Recipe, _ files: [URL], at point: NSPoint, temporary: Bool) {
        job += 1
        let id = job
        HUD.shared.begin(recipe.name, count: files.count, near: point)
        let options = Prefs.options(temporary: temporary)
        Task {
            let report = await Engine.runRecipe(recipe, inputs: files, options: options) { done, total, frac in
                Task { @MainActor in if self.job == id { HUD.shared.update(done: done, total: total, fraction: frac) } }
            }
            self.finish(report, title: recipe.name, job: id)
        }
    }

    /// Renames can be undone too (names go back, nothing is binned).
    func didRename(_ pairs: [(from: URL, to: URL)]) {
        undoStep = .rename(pairs)
        recent = Array((pairs.map(\.to) + recent).prefix(10))
        var report = RunReport()
        report.outputs = pairs.map(\.to)
        HUD.shared.finish(report, title: "Renamed \(pairs.count) file\(pairs.count == 1 ? "" : "s")")
    }

    /// Editors and the engine both end here.
    func finish(_ report: RunReport, title: String, job id: Int? = nil) {
        if let id, cancelled.contains(id) {
            if !report.outputs.isEmpty { NSWorkspace.shared.recycle(report.outputs) }
            return
        }
        for f in report.failures { Log.line("failed: \(f)") }
        if !report.outputs.isEmpty {
            Tally.add(report.outputs.count) // for the About tab's "fixed N files for you"
            undoStep = .trash(report.outputs)
            recent = Array((report.outputs + recent.filter { !report.outputs.contains($0) }).prefix(10))
        }
        if let text = report.text, Prefs.d.bool(forKey: Key.copyText) {
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(text, forType: .string)
        }
        HUD.shared.finish(report, title: pastTense(title))
        if Prefs.d.bool(forKey: Key.reveal), !report.outputs.isEmpty {
            NSWorkspace.shared.activateFileViewerSelecting(report.outputs)
        }
        Live.shared.lastResult = report
    }

    /// "Converting to JPG" -> "Converted to JPG", "Removing background" -> "Removed background".
    private func pastTense(_ verb: String) -> String {
        let first = verb.split(separator: " ").first.map(String.init) ?? verb
        let table = ["Converting": "Converted", "Cropping": "Cropped", "Resizing": "Resized", "Rotating": "Rotated",
                     "Removing": "Removed", "Adding": "Added", "Enhancing": "Enhanced", "Redacting": "Redacted",
                     "Compressing": "Compressed", "Making": "Made", "Flipping": "Flipped", "Reading": "Read",
                     "Splitting": "Split", "Merging": "Merged", "Trimming": "Trimmed", "Saving": "Saved",
                     "Zipping": "Zipped", "Extracting": "Extracted", "Putting": "Put", "Running": "Finished",
                     "Speeding": "Sped", "Stitching": "Stitched", "Annotating": "Annotated", "Renaming": "Renamed"]
        guard let past = table[first] else { return verb }
        return past + verb.dropFirst(first.count)
    }

    func undo() {
        guard let step = undoStep else { return }
        undoStep = nil
        switch step {
        case .trash(let gone):
            NSWorkspace.shared.recycle(gone) { _, error in
                Task { @MainActor in
                    guard error == nil else { return }
                    self.recent.removeAll { gone.contains($0) }
                    HUD.shared.flash("Moved to Bin", gone.count == 1 ? gone[0].lastPathComponent : "\(gone.count) files",
                                     near: NSEvent.mouseLocation)
                }
            }
        case .rename(let pairs):
            do {
                try Renamer.revert(pairs)
                recent.removeAll { u in pairs.contains { $0.to == u } }
                HUD.shared.flash("Names put back", "\(pairs.count) file\(pairs.count == 1 ? "" : "s")", near: NSEvent.mouseLocation)
            } catch {
                HUD.shared.fail("Couldn't undo the rename", error.localizedDescription, near: NSEvent.mouseLocation)
            }
        }
    }

    /// A watched folder handled a file: quiet note in the corner, plus Recent and Undo.
    func noteWatch(_ report: RunReport, folder: String) {
        for f in report.failures { Log.line("watch failed: \(f)") }
        guard !report.outputs.isEmpty else { return }
        Tally.add(report.outputs.count)
        undoStep = .trash(report.outputs)
        recent = Array((report.outputs + recent).prefix(10))
        let screen = NSScreen.main?.visibleFrame ?? .zero
        HUD.shared.flash("\(URL(fileURLWithPath: folder).lastPathComponent): done", report.summary,
                         near: NSPoint(x: screen.maxX - 200, y: screen.maxY - 70), outputs: report.outputs)
    }

    // MARK: Share page

    private func share(_ action: Action, _ files: [URL], at point: NSPoint) {
        switch action.id {
        case "share.copy":
            NSPasteboard.general.clearContents()
            NSPasteboard.general.writeObjects(files as [NSURL])
            HUD.shared.flash("Copied", files.count == 1 ? files[0].lastPathComponent : "\(files.count) files — paste anywhere",
                             near: point)
        case "share.path":
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(files.map(\.path).joined(separator: "\n"), forType: .string)
            HUD.shared.flash("Copied the path", files.first?.path ?? "", near: point)
        case "share.desktop":
            let desk = FileManager.default.urls(for: .desktopDirectory, in: .userDomainMask)[0]
            var outs: [URL] = []
            for f in files {
                let to = Naming.unique(desk.appendingPathComponent(f.lastPathComponent))
                if (try? FileManager.default.copyItem(at: f, to: to)) != nil { outs.append(to) }
            }
            undoStep = .trash(outs)
            HUD.shared.flash("Copied to Desktop", "\(outs.count) file\(outs.count == 1 ? "" : "s")", near: point, outputs: outs)
        case "share.airdrop":
            service(.sendViaAirDrop, files)
        case "share.mail":
            service(.composeEmail, files)
        case "share.messages":
            service(.composeMessage, files)
        default:
            picker(files, at: point)
        }
    }

    private func service(_ name: NSSharingService.Name, _ files: [URL]) {
        NSApp.activate(ignoringOtherApps: true)
        guard let s = NSSharingService(named: name), s.canPerform(withItems: files) else {
            HUD.shared.fail("Not available", "This Mac can't share that way right now", near: NSEvent.mouseLocation)
            return
        }
        s.perform(withItems: files)
    }

    /// The standard Share menu, shown where the ring was.
    private var pickerWindow: NSWindow?
    private func picker(_ files: [URL], at point: NSPoint) {
        let w = NSWindow(contentRect: NSRect(x: point.x - 1, y: point.y - 1, width: 2, height: 2),
                         styleMask: .borderless, backing: .buffered, defer: false)
        w.isOpaque = false
        w.backgroundColor = .clear
        w.level = .popUpMenu
        pickerWindow = w
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
        if let v = w.contentView {
            NSSharingServicePicker(items: files).show(relativeTo: v.bounds, of: v, preferredEdge: .minY)
        }
    }
}
