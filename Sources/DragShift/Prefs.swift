import AppKit
import DragShiftCore
import Foundation

/// UserDefaults keys. SwiftUI views use the same keys through @AppStorage.
enum Key {
    static let triggerShift = "trigger.shift"
    static let triggerFinger = "trigger.finger"
    static let triggerShake = "trigger.shake"
    static let triggerHover = "trigger.hover"
    static let triggerMenuBar = "trigger.menubar"
    static let outputMode = "output.mode"          // beside | downloads | folder
    static let outputFolder = "output.folder"
    static let quality = "quality"
    static let compressQuality = "compress.quality"
    static let compressMaxEdge = "compress.maxEdge"
    static let pdfDPI = "pdf.dpi"
    static let copyText = "after.copyText"
    static let reveal = "after.reveal"
    static let haptics = "feedback.haptics"
    static let sound = "feedback.sound"
    static let hidden = "ring.hidden"
    static let welcomeDone = "welcome.done"
    static let paused = "paused"
    static let backdrop = "backdrop.style"
    static let redactStyle = "redact.style"
    static let favorites = "favorites"
    static let recipes = "recipes"
    static let watch = "watch.rules"
    static let watermark = "watermark"
    static let hotkey = "hotkey.clipboard"      // off | ctrlOptV | ctrlShiftV | optShiftV
    static let shelf = "shelf.side"            // right | left | off
    static let lastActions = "last.actions"     // [kind: action id]
    static let palm = "trigger.palm"
    static let triggerMouse = "trigger.mouse"   // middle or side mouse button while dragging
    static let triggerMouseButtons = "trigger.mouse.buttons" // which ones: "middle,back,forward" (nothing saved = all)
    static let fitLast = "fit.last"             // last "Under ___ KB" size
    static let photo = "photo.spec"             // passport photo size, KB limit, background
    static let habits = "habits"                 // what you pick most per kind (Habits, JSON)
    static let usualGlow = "ring.usual"          // the usual slice glows when the ring opens
    static let printPresets = "print.presets"    // saved print preferences ([PrintPreset], JSON)
    static let printLast = "print.last"          // the settings used for the last print (PrintPreset, JSON)
    static let printQuick = "print.quick"        // Print prints straight away with the top preset
}

enum Prefs {
    static let d = UserDefaults.standard

    static func registerDefaults() {
        d.register(defaults: [
            Key.triggerShift: true,
            Key.triggerFinger: true,
            Key.triggerShake: false,
            Key.triggerHover: false,
            Key.triggerMenuBar: true,
            Key.outputMode: "beside",
            Key.quality: 0.85,
            Key.compressQuality: 0.7,
            Key.compressMaxEdge: 3000,
            Key.pdfDPI: 200.0,
            Key.copyText: true,
            Key.reveal: false,
            Key.haptics: true,
            Key.sound: true,
            Key.welcomeDone: false,
            Key.paused: false,
            Key.favorites: Catalog.defaultFavorites,
            Key.hotkey: "ctrlOptV",
            Key.shelf: "right",
            Key.palm: true,
            Key.triggerMouse: true,
            Key.fitLast: 100,
            Key.usualGlow: true,
        ])
    }

    static var favorites: [String] {
        get { d.stringArray(forKey: Key.favorites) ?? Catalog.defaultFavorites }
        set { d.set(newValue, forKey: Key.favorites); NotificationCenter.default.post(name: .dragShiftPrefs, object: nil) }
    }

    static func toggleFavorite(_ id: String) -> Bool {
        var f = favorites
        if let i = f.firstIndex(of: id) { f.remove(at: i); favorites = f; return false }
        f.append(id)
        favorites = f
        return true
    }

    /// Built-in recipes plus the user's own (built-ins can be edited; deleting one hides it).
    static var recipes: [Recipe] {
        get {
            if let data = d.data(forKey: Key.recipes), let r = try? JSONDecoder().decode([Recipe].self, from: data) { return r }
            return Recipe.builtIns
        }
        set { d.set(try? JSONEncoder().encode(newValue), forKey: Key.recipes); NotificationCenter.default.post(name: .dragShiftPrefs, object: nil) }
    }

    static var watchRules: [WatchRule] {
        get {
            guard let data = d.data(forKey: Key.watch), let r = try? JSONDecoder().decode([WatchRule].self, from: data) else { return [] }
            return r
        }
        set { d.set(try? JSONEncoder().encode(newValue), forKey: Key.watch); NotificationCenter.default.post(name: .dragShiftWatch, object: nil) }
    }

    static var watermark: WatermarkStyle {
        get {
            guard let data = d.data(forKey: Key.watermark), let w = try? JSONDecoder().decode(WatermarkStyle.self, from: data) else {
                return WatermarkStyle()
            }
            return w
        }
        set { d.set(try? JSONEncoder().encode(newValue), forKey: Key.watermark) }
    }

    /// Last thing done to each kind of file, for "double-tap Shift = do it again".
    static func lastAction(for kind: FileKind) -> String? {
        (d.dictionary(forKey: Key.lastActions) as? [String: String])?[kind.rawValue]
    }

    static func setLastAction(_ id: String, for kind: FileKind) {
        var all = (d.dictionary(forKey: Key.lastActions) as? [String: String]) ?? [:]
        all[kind.rawValue] = id
        d.set(all, forKey: Key.lastActions)
    }

    static var habits: Habits {
        get {
            guard let data = d.data(forKey: Key.habits), let h = try? JSONDecoder().decode(Habits.self, from: data) else { return Habits() }
            return h
        }
        set { d.set(try? JSONEncoder().encode(newValue), forKey: Key.habits) }
    }

    /// The slice to glow for these files, if you have a habit for them and want the glow.
    static func usual(for kind: FileKind) -> String? {
        d.bool(forKey: Key.usualGlow) ? habits.usual(for: kind) : nil
    }

    /// Saved print preferences, in the order the person put them (the top one is the quick one).
    static var printPresets: [PrintPreset] {
        get {
            guard let data = d.data(forKey: Key.printPresets), let p = try? JSONDecoder().decode([PrintPreset].self, from: data) else {
                return PrintPreset.builtIns
            }
            return p
        }
        set { d.set(try? JSONEncoder().encode(newValue), forKey: Key.printPresets); NotificationCenter.default.post(name: .dragShiftPrefs, object: nil) }
    }

    static var printLast: PrintPreset? {
        get { d.data(forKey: Key.printLast).flatMap { try? JSONDecoder().decode(PrintPreset.self, from: $0) } }
        set { d.set(newValue.flatMap { try? JSONEncoder().encode($0) }, forKey: Key.printLast) }
    }

    /// DragShift's own presets, then the ones saved in the macOS print window.
    static var allPrintPresets: [PrintPreset] { printPresets + SystemPresets.all }

    /// The presets in the Print list and on the ring's Print page: all of them, minus the ones switched off
    /// (Print → Choose…, or Settings → Ring), which share the ring's hidden set.
    static var shownPrintPresets: [PrintPreset] {
        let off = hidden
        return allPrintPresets.filter { !off.contains($0.stepID) }
    }

    /// A ring step's preset: a saved one (DragShift's or macOS's), or "last" (what the print window sent last time).
    static func printPreset(step: String) -> PrintPreset? {
        guard let id = PrintPreset.id(fromStep: step) else { return nil }
        return id == PrintPreset.lastID ? printLast : allPrintPresets.first { $0.id == id }
    }

    static var photo: PhotoSpec {
        get {
            guard let data = d.data(forKey: Key.photo), let s = try? JSONDecoder().decode(PhotoSpec.self, from: data) else {
                return PhotoSpec()
            }
            return s
        }
        set { d.set(try? JSONEncoder().encode(newValue), forKey: Key.photo) }
    }

    static var paused: Bool {
        get { d.bool(forKey: Key.paused) }
        set { d.set(newValue, forKey: Key.paused) }
    }

    static var hidden: Set<String> {
        get { Set(d.stringArray(forKey: Key.hidden) ?? []) }
        set { d.set(Array(newValue).sorted(), forKey: Key.hidden) }
    }

    /// Engine options. `temporary` inputs (received from Photos, Mail, a browser) live in a
    /// cache folder, so their results go to Downloads instead of next to them.
    static func options(temporary: Bool = false) -> Options {
        var o = Options()
        o.quality = d.double(forKey: Key.quality)
        o.compressQuality = d.double(forKey: Key.compressQuality)
        o.compressMaxEdge = d.integer(forKey: Key.compressMaxEdge)
        o.pdfDPI = d.double(forKey: Key.pdfDPI)
        o.watermark = watermark
        o.photo = photo
        switch d.string(forKey: Key.outputMode) ?? "beside" {
        case "downloads":
            o.outputFolder = Naming.downloads
        case "folder":
            if let p = d.string(forKey: Key.outputFolder), FileManager.default.fileExists(atPath: p) {
                o.outputFolder = URL(fileURLWithPath: p)
            }
        default:
            o.outputFolder = temporary ? Naming.downloads : nil
        }
        return o
    }

    static var backdrop: BackdropStyle {
        get {
            guard let data = d.data(forKey: Key.backdrop),
                  let s = try? JSONDecoder().decode(BackdropStyle.self, from: data) else { return BackdropStyle() }
            return s
        }
        set { d.set(try? JSONEncoder().encode(newValue), forKey: Key.backdrop) }
    }
}

/// A tiny log for "it didn't work" reports: ~/Library/Logs/DragShift.log (kept under 512 KB).
enum Log {
    static let url = FileManager.default.urls(for: .libraryDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("Logs/DragShift.log")
    private static let queue = DispatchQueue(label: "dragshift.log")
    private static let stamp: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd HH:mm:ss.SSS"
        return f
    }()

    static func start() {
        let size = (try? url.resourceValues(forKeys: [.fileSizeKey]))?.fileSize ?? 0
        if size > 512_000 { try? FileManager.default.removeItem(at: url) }
        line("DragShift \(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev") started")
    }

    static func line(_ s: String) {
        let text = "\(stamp.string(from: Date())) \(s)\n"
        queue.async {
            if let h = try? FileHandle(forWritingTo: url) {
                h.seekToEndOfFile()
                h.write(Data(text.utf8))
                try? h.close()
            } else {
                try? Data(text.utf8).write(to: url)
            }
        }
    }
}

/// Live status the Welcome screen and menu show (drag seen, ring open, finger count...).
@MainActor
final class Live: ObservableObject {
    static let shared = Live()
    @Published var dragging = false
    @Published var ringOpen = false
    @Published var fingers = 0
    @Published var touchAvailable = false
    @Published var touchSeen = false
    @Published var lastResult: RunReport?
    @Published var lastAction: String = ""
}

extension Notification.Name {
    static let dragShiftPrefs = Notification.Name("DragShiftPrefsChanged")
    static let dragShiftWatch = Notification.Name("DragShiftWatchChanged")
}
