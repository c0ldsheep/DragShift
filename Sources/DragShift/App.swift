import AppKit
import DragShiftCore
import SwiftUI

@MainActor
final class App: NSObject, NSApplicationDelegate {
    static let shared = App()

    private let watcher = DragWatcher()
    private let status = StatusBar()
    private let demo = RingModel()
    private var demoTimer: Timer?
    private var isTheOnlyCopy = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        Prefs.registerDefaults()
        NSApp.setActivationPolicy(.accessory)
        if let i = CommandLine.arguments.firstIndex(of: "--snapshot"), i + 1 < CommandLine.arguments.count {
            let rest = CommandLine.arguments[(i + 2)...]
            Snapshots.run(into: URL(fileURLWithPath: CommandLine.arguments[i + 1]), sample: rest.first.map { URL(fileURLWithPath: $0) })
            exit(0)
        }
        // Developer check: `DragShift --cup-drag-check` drags the About tab's coffee cup with a made-up mouse and says
        // how far the cup strayed from the pointer (a cup that shakes strays a lot).
        if CommandLine.arguments.contains("--cup-drag-check") {
            let r = Snapshots.cupDragCheck()
            print(r.text)
            exit(r.ok ? 0 : 1)
        }
        // Developer check: `DragShift --inbox-check` walks a temporary copy through its whole life (held, let go,
        // swept) in a folder of its own.
        if CommandLine.arguments.contains("--inbox-check") {
            Task { @MainActor in
                let r = await Incoming.selfCheck()
                print(r.text)
                exit(r.ok ? 0 : 1)
            }
            return
        }
        // Developer check: `DragShift --print-test <out.pdf> <preset id> <files…>` runs a real print job through macOS
        // printing, saved to a PDF instead of sent to a printer.
        if let i = CommandLine.arguments.firstIndex(of: "--print-test"), i + 2 < CommandLine.arguments.count {
            let args = CommandLine.arguments
            let preset = Prefs.allPrintPresets.first { $0.id == args[i + 2] } ?? PrintPreset.builtIns[0]
            Printer.run(args[(i + 3)...].map { URL(fileURLWithPath: $0) }, preset: preset, at: NSPoint(x: 200, y: 200),
                        saveTo: URL(fileURLWithPath: args[i + 1])) { ok in
                print(ok ? "printed" : "not printed")
                exit(ok ? 0 : 1)
            }
            return
        }
        // `DragShift --selftest <folder> [photo]`: every slice on realistic files made in a new folder inside
        // <folder>, each result opened and checked; the report is saved as selftest.txt next to them. Works on any
        // Mac; on Apple silicon, `arch -x86_64 …/DragShift --selftest …` runs it as an Intel Mac would.
        if let i = CommandLine.arguments.firstIndex(of: "--selftest"), i + 1 < CommandLine.arguments.count {
            let args = CommandLine.arguments
            let stamp = ISO8601DateFormatter().string(from: Date()).replacingOccurrences(of: ":", with: "-")
            let dir = URL(fileURLWithPath: args[i + 1]).appendingPathComponent("DragShift self-test \(stamp)")
            let photo = i + 2 < args.count ? URL(fileURLWithPath: args[i + 2]) : nil
            Task {
                do {
                    let r = try await SelfCheck.run(in: dir, photo: photo)
                    #if arch(arm64)
                    let chip = "Apple silicon"
                    #else
                    let chip = "Intel"
                    #endif
                    let head = "DragShift \(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev") self-test, \(chip), macOS \(ProcessInfo.processInfo.operatingSystemVersionString)"
                    let text = ([head, ""] + r.lines + ["", r.summary]).joined(separator: "\n") + "\n"
                    try? text.write(to: dir.appendingPathComponent("selftest.txt"), atomically: true, encoding: .utf8)
                    print(text)
                    exit(r.failed.isEmpty ? 0 : 1)
                } catch {
                    print("self-test couldn't run: \(error.localizedDescription)")
                    exit(2)
                }
            }
            return
        }
        if CommandLine.arguments.contains("--space-test") {
            exit(Floating.selfTest() ? 0 : 1)
        }
        Log.start()

        if let id = Bundle.main.bundleIdentifier,
           NSRunningApplication.runningApplications(withBundleIdentifier: id).count > 1 {
            Log.line("another copy is running; quitting this one")
            NSApp.terminate(nil)
            return
        }
        // Temporary copies left by a crash or a force quit: nothing is using them now.
        isTheOnlyCopy = true
        Incoming.sweepAll()
        let sweep = Timer(timeInterval: 3600, repeats: true) { _ in Task { @MainActor in Incoming.sweepOld() } }
        sweep.tolerance = 600
        RunLoop.main.add(sweep, forMode: .common)

        status.setup()
        status.onConvertFiles = { [weak self] in self?.chooseFiles() }
        status.onSettings = { [weak self] in self?.showSettings() }
        status.onWelcome = { [weak self] in self?.showWelcome() }

        let touch = TouchMonitor.shared
        Live.shared.touchAvailable = touch.start()
        touch.onFirstFrame = {
            Live.shared.touchSeen = true
            Log.line("touch: first frame received")
        }
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { Live.shared.touchAvailable = TouchMonitor.shared.start() }
        }

        touch.palmFilter = Prefs.d.bool(forKey: Key.palm)
        watcher.onTrigger = { [weak self] t in self?.trigger(t) }
        watcher.onBegin = { peek in FavoritesShelf.shared.dragBegan(peek) }
        watcher.onMove = { loc in FavoritesShelf.shared.moved(loc) }
        watcher.onEnd = { [weak self] in
            guard let self else { return }
            RingController.shared.dragEnded(peek: self.watcher.peek)
            FavoritesShelf.shared.dragEnded()
        }
        watcher.start()

        // Picks from the ring and the shelf teach "your usual"; the size and resize windows don't
        // count again, they finish a pick that was already counted.
        let perform: (Action, [URL], NSPoint, Bool) -> Void = { action, files, point, temp in
            Prefs.habits.record(action.id, kind: FileInspector.kind(of: files))
            Runner.shared.perform(action, files, at: point, temporary: temp)
        }
        RingController.shared.onPerform = perform
        FavoritesShelf.shared.onPerform = perform

        HotKey.shared.onPress = { [weak self] in self?.openClipboard() }
        HotKey.shared.apply()
        FolderWatcher.shared.reload()
        NotificationCenter.default.addObserver(forName: .dragShiftWatch, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { FolderWatcher.shared.reload() }
        }
        NotificationCenter.default.addObserver(forName: UserDefaults.didChangeNotification, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { TouchMonitor.shared.palmFilter = Prefs.d.bool(forKey: Key.palm) }
        }
        status.onClipboard = { [weak self] in self?.openClipboard() }
        status.onClipboardQR = { [weak self] in self?.clipboardQR() }
        NSApp.servicesProvider = self
        NSUpdateDynamicServices()

        if CommandLine.arguments.contains("--welcome") || !Prefs.d.bool(forKey: Key.welcomeDone) {
            showWelcome()
        }
        if let i = CommandLine.arguments.firstIndex(of: "--ring") {
            let files = CommandLine.arguments[(i + 1)...].map { URL(fileURLWithPath: $0) }
            if !files.isEmpty { showRing(files) }
        }
    }

    private func trigger(_ t: DragWatcher.Trigger) {
        let ring = RingController.shared
        if ring.isShown {
            guard t != .hover else { return }
            // A second tap right after the first = repeat the last action; later taps flip pages.
            if (t == .shift || t == .finger || t == .mouse) && Date().timeIntervalSince(ring.openedAt) < 0.45 && ring.model.mode == .drag {
                ring.repeatLast()
            } else {
                ring.nextPage()
            }
        } else {
            ring.showForDrag(watcher.peek, at: NSEvent.mouseLocation)
        }
    }

    // MARK: Clipboard

    /// ⌃⌥V: whatever you copied (files in Finder, a screenshot, a link, some text) opens in the ring.
    func openClipboard() {
        let types = (NSPasteboard.general.types ?? []).map(\.rawValue).prefix(4).joined(separator: ", ")
        Log.line("clipboard shortcut: \(NSPasteboard.general.pasteboardItems?.count ?? 0) item(s) [\(types)]")
        guard let (files, temp) = clipboardFiles() else {
            HUD.shared.flash("Clipboard is empty", "Copy a file, a screenshot or some text first", near: NSEvent.mouseLocation)
            return
        }
        RingController.shared.showForClick(files, temporary: temp, at: NSEvent.mouseLocation)
    }

    /// Menu → QR Code from Clipboard: straight to a QR image in Downloads.
    func clipboardQR() {
        guard let text = NSPasteboard.general.string(forType: .string), !text.isEmpty else {
            HUD.shared.flash("Nothing to turn into a QR code", "Copy a link or some text first", near: NSEvent.mouseLocation)
            return
        }
        do {
            let img = try QR.make(text.trimmingCharacters(in: .whitespacesAndNewlines))
            let out = Naming.unique(Naming.downloads.appendingPathComponent(QR.name(for: text)).appendingPathExtension("png"))
            try ImageOps.write(img, to: out, format: .png, quality: 1)
            var r = RunReport()
            r.outputs = [out]
            Runner.shared.finish(r, title: "Made a QR code")
        } catch {
            HUD.shared.fail("Couldn't make the QR code", error.localizedDescription, near: NSEvent.mouseLocation)
        }
    }

    private func clipboardFiles() -> ([URL], Bool)? {
        let pb = NSPasteboard.general
        if let urls = pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL], !urls.isEmpty {
            return (urls, false)
        }
        if let type = pb.availableType(from: Incoming.imageTypes), let data = pb.data(forType: type) {
            let url = Incoming.inbox.appendingPathComponent("Clipboard Image").appendingPathExtension(type == .tiff ? "tiff" : "png")
            if (try? data.write(to: url)) != nil { return ([url], true) }
        }
        if let text = pb.string(forType: .string), !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            let name = QR.name(for: text).replacingOccurrences(of: "QR ", with: "")
            let url = Incoming.inbox.appendingPathComponent(name.isEmpty ? "Clipboard Text" : name).appendingPathExtension("txt")
            if (try? text.write(to: url, atomically: true, encoding: .utf8)) != nil { return ([url], true) }
        }
        return nil
    }

    // MARK: Other ways in

    /// Menu → Convert Files…
    func chooseFiles() {
        let p = NSOpenPanel()
        p.allowsMultipleSelection = true
        p.canChooseDirectories = true
        p.prompt = "Choose"
        p.message = "Pick files, then pick what to do with them."
        NSApp.activate(ignoringOtherApps: true)
        if p.runModal() == .OK, !p.urls.isEmpty { showRing(p.urls) }
    }

    func showRing(_ files: [URL]) {
        let screen = NSScreen.main?.visibleFrame ?? .zero
        RingController.shared.showForClick(files, at: NSPoint(x: screen.midX, y: screen.midY + 40))
    }

    /// Finder: right-click → Open With → DragShift.
    func application(_ application: NSApplication, open urls: [URL]) {
        showRing(urls)
    }

    /// Finder: right-click → Services / Quick Actions → DragShift.
    @objc func openInRing(_ pboard: NSPasteboard, userData: String?, error: AutoreleasingUnsafeMutablePointer<NSString?>) {
        let urls = (pboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]) ?? []
        if !urls.isEmpty { showRing(urls) }
    }

    /// Quit: no temporary copy stays behind. (A second copy of the app that quits itself at launch must not
    /// remove the files the first one is using.)
    func applicationWillTerminate(_ notification: Notification) {
        if isTheOnlyCopy { Incoming.sweepAll() }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showSettings()
        return true
    }

    // MARK: Windows

    func showSettings() {
        Windows.show("settings", title: "DragShift Settings", size: CGSize(width: 520, height: 470), SettingsView())
    }

    func showWelcome() {
        demo.pages = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/Photo.heic")])
        demo.title = "Photo.heic"
        demo.subtitle = "2.4 MB"
        demo.thumbnail = NSImage(systemSymbolName: "photo.fill", accessibilityDescription: nil)
        demo.mode = .drag
        demo.visible = true
        demoTimer?.invalidate()
        var step = 0
        demoTimer = Timer.scheduledTimer(withTimeInterval: 0.9, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                let order: [Int?] = [nil, 0, 1, 2, nil, 4, 6]
                self.demo.hovered = order[step % order.count]
                step += 1
            }
        }
        Windows.show("welcome", title: "Welcome to DragShift", size: CGSize(width: 820, height: 600),
                     WelcomeView(demo: demo, practice: Practice.file))
    }
}
