import AppKit
import Carbon

/// One global shortcut (default ⌃⌥V: open the clipboard in the ring). Uses the old Carbon
/// hot-key call, which needs no Accessibility or Input Monitoring permission.
@MainActor
final class HotKey {
    static let shared = HotKey()

    struct Choice {
        let id: String
        let title: String
        let modifiers: UInt32
    }

    static let choices: [Choice] = [
        Choice(id: "ctrlOptV", title: "⌃⌥V", modifiers: UInt32(controlKey | optionKey)),
        Choice(id: "ctrlShiftV", title: "⌃⇧V", modifiers: UInt32(controlKey | shiftKey)),
        Choice(id: "optShiftV", title: "⌥⇧V", modifiers: UInt32(optionKey | shiftKey)),
        Choice(id: "off", title: "Off", modifiers: 0),
    ]

    var onPress: (() -> Void)?
    private var ref: EventHotKeyRef?
    private var handler: EventHandlerRef?

    static var currentTitle: String? {
        let id = Prefs.d.string(forKey: Key.hotkey) ?? "ctrlOptV"
        return choices.first { $0.id == id && $0.id != "off" }?.title
    }

    func apply() {
        if let ref { UnregisterEventHotKey(ref); self.ref = nil }
        let id = Prefs.d.string(forKey: Key.hotkey) ?? "ctrlOptV"
        guard let choice = Self.choices.first(where: { $0.id == id }), choice.id != "off" else { return }
        if handler == nil {
            var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
                DispatchQueue.main.async { MainActor.assumeIsolated { HotKey.shared.onPress?() } }
                return noErr
            }, 1, &spec, nil, &handler)
        }
        let hotID = EventHotKeyID(signature: OSType(0x4453_4654), id: 1) // "DSFT"
        let status = RegisterEventHotKey(UInt32(kVK_ANSI_V), choice.modifiers, hotID, GetApplicationEventTarget(), 0, &ref)
        if status != noErr { Log.line("hotkey: \(choice.title) is taken by another app (\(status))") }
    }
}
