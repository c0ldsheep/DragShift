import AppKit
import ApplicationServices
import DragShiftCore

/// The presets you saved in the macOS print window ("Amazon Packing", "Flipkart"…), so they show up in DragShift's
/// Print list and ring and print with exactly the settings macOS saved. macOS keeps them in its preferences: one domain
/// per printer queue (com.apple.print.custompresets.forprinter.<queue>) and one for "All Printers"
/// (com.apple.print.custompresets); `com.apple.print.customPresetsInfo` lists them in menu order.
enum SystemPresets {
    private static let allPrinters = "com.apple.print.custompresets"
    private static let infoKey = "com.apple.print.customPresetsInfo"
    private static let settingsKey = "com.apple.print.preset.settings"
    private static let lock = NSLock()
    nonisolated(unsafe) private static var cache: (at: Date, list: [PrintPreset])?

    /// Every saved macOS preset, each printer's in its menu order, then the All Printers ones. Re-read every few
    /// seconds, so one saved a moment ago in Preview turns up on the next print.
    static var all: [PrintPreset] {
        lock.lock()
        defer { lock.unlock() }
        if let c = cache, Date().timeIntervalSince(c.at) < 3 { return c.list }
        var list: [PrintPreset] = []
        for (queue, name) in printers() {
            list += presets(in: allPrinters + ".forprinter." + queue, idPrefix: "mac.\(queue).", printer: name)
        }
        list += presets(in: allPrinters, idPrefix: "mac.all.", printer: nil)
        cache = (Date(), list)
        return list
    }

    /// What macOS saved for this preset (nil once it's been deleted in macOS).
    static func settings(for preset: PrintPreset) -> [String: Any]? {
        guard let name = preset.system else { return nil }
        let domain = preset.id.hasPrefix("mac.all.") ? allPrinters
            : allPrinters + ".forprinter." + (printers().first { $0.name == preset.printer }?.queue ?? "")
        CFPreferencesAppSynchronize(domain as CFString)
        let entry = CFPreferencesCopyAppValue(name as CFString, domain as CFString) as? [String: Any]
        return entry?[settingsKey] as? [String: Any]
    }

    private static func presets(in domain: String, idPrefix: String, printer: String?) -> [PrintPreset] {
        CFPreferencesAppSynchronize(domain as CFString)
        guard let info = CFPreferencesCopyAppValue(infoKey as CFString, domain as CFString) as? [[String: Any]] else { return [] }
        return info.compactMap { $0["PresetName"] as? String }
            .filter { CFPreferencesCopyAppValue($0 as CFString, domain as CFString) != nil }
            .map { key in
                let name = key.trimmingCharacters(in: .whitespaces)
                return PrintPreset(id: idPrefix + name, name: name, printer: printer, system: key)
            }
    }

    /// (CUPS queue, the name people see) for every printer on this Mac.
    private static func printers() -> [(queue: String, name: String)] {
        var list: Unmanaged<CFArray>?
        guard PMServerCreatePrinterList(nil, &list) == noErr, let array = list?.takeRetainedValue() else { return [] }
        var out: [(queue: String, name: String)] = []
        for i in 0..<CFArrayGetCount(array) {
            guard let raw = CFArrayGetValueAtIndex(array, i) else { continue }
            let printer = PMPrinter(raw)
            guard let queue = PMPrinterGetID(printer), let name = PMPrinterGetName(printer) else { continue }
            out.append((queue as String, name as String))
        }
        return out
    }
}
