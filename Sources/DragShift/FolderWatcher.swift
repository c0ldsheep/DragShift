import AppKit
import DragShiftCore

/// Watch Folders: when a matching file lands in a folder, run an action on it.
/// Uses the kernel's folder-change notice (no polling), waits until the file stops growing,
/// and remembers what it made so it never processes its own results.
@MainActor
final class FolderWatcher {
    static let shared = FolderWatcher()

    private var sources: [String: DispatchSourceFileSystemObject] = [:]
    private var pending: [String: DispatchWorkItem] = [:]
    private var inFlight = Set<String>()
    private var handled: [String] = Prefs.d.stringArray(forKey: "watch.handled") ?? []
    private lazy var handledSet = Set(handled)

    var activeCount: Int { sources.count }

    func reload() {
        for (_, s) in sources { s.cancel() }
        sources.removeAll()
        for rule in Prefs.watchRules where rule.enabled {
            watch(rule)
            scan(rule) // catch files that arrived while DragShift wasn't running
        }
        Log.line("watch: \(sources.count) folder rule(s) active")
    }

    private func watch(_ rule: WatchRule) {
        let fd = open(rule.folder, O_EVTONLY)
        guard fd >= 0 else { Log.line("watch: can't open \(rule.folder)"); return }
        let src = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd, eventMask: [.write, .rename, .link], queue: .main)
        src.setEventHandler { [weak self] in
            MainActor.assumeIsolated { self?.schedule(rule) }
        }
        src.setCancelHandler { close(fd) }
        src.resume()
        sources[rule.id] = src
    }

    /// Bursts of changes (a download writing) settle into one scan.
    private func schedule(_ rule: WatchRule) {
        pending[rule.id]?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.scan(rule) }
        pending[rule.id] = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.0, execute: work)
    }

    private func scan(_ rule: WatchRule) {
        let folder = URL(fileURLWithPath: rule.folder)
        let items = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.creationDateKey, .fileSizeKey],
                                                                  options: [.skipsHiddenFiles])) ?? []
        for url in items {
            guard rule.matches(url), !rule.wouldLoop(url), !handledSet.contains(url.path), !inFlight.contains(url.path) else { continue }
            let created = (try? url.resourceValues(forKeys: [.creationDateKey]))?.creationDate ?? .distantPast
            guard created >= rule.since else { continue }
            inFlight.insert(url.path)
            whenSettled(url) { [weak self] in self?.run(rule, url) }
        }
    }

    /// Waits until the size stops changing (downloads and AirDrops arrive in pieces).
    private func whenSettled(_ url: URL, tries: Int = 0, _ then: @escaping @MainActor () -> Void) {
        let a = FileInspector.size(of: url)
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) { [weak self] in
            let b = FileInspector.size(of: url)
            if a == b && b > 0 { then() }
            else if tries < 30 { self?.whenSettled(url, tries: tries + 1, then) }
            else { self?.inFlight.remove(url.path) }
        }
    }

    private func run(_ rule: WatchRule, _ url: URL) {
        let recipe = Prefs.recipes.first { $0.id == rule.actionID }
        let action = recipe?.action ?? Catalog.favoriteActions([rule.actionID], files: [url], recipes: []).first
        guard let action, action.editor == nil, !action.isShare else {
            inFlight.remove(url.path)
            return
        }
        Log.line("watch: \(action.id) on \(url.lastPathComponent)")
        let options = Prefs.options()
        Task {
            let report = recipe != nil
                ? await Engine.runRecipe(recipe!, inputs: [url], options: options)
                : await Engine.run(action, inputs: [url], options: options)
            self.remember([url.path] + report.outputs.map(\.path))
            self.inFlight.remove(url.path)
            if rule.trashOriginal && !report.outputs.isEmpty { NSWorkspace.shared.recycle([url], completionHandler: nil) }
            Runner.shared.noteWatch(report, folder: rule.folder)
        }
    }

    private func remember(_ paths: [String]) {
        handled += paths
        if handled.count > 600 { handled.removeFirst(handled.count - 600) }
        handledSet = Set(handled)
        Prefs.d.set(handled, forKey: "watch.handled")
    }
}
