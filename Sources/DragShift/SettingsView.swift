import AppKit
import DragShiftCore
import ServiceManagement
import SwiftUI

struct SettingsView: View {
    var body: some View {
        TabView {
            GeneralPane().tabItem { Label("General", systemImage: "gearshape") }
            TriggersPane().tabItem { Label("Triggers", systemImage: "hand.tap") }
            FavoritesPane().tabItem { Label("Favorites", systemImage: "star") }
            RecipesPane().tabItem { Label("Recipes", systemImage: "wand.and.stars") }
            WatchPane().tabItem { Label("Watch", systemImage: "eye") }
            PrintingPane().tabItem { Label("Printing", systemImage: "printer") }
            SavingPane().tabItem { Label("Saving", systemImage: "folder") }
            AboutPane().tabItem { Label("About", systemImage: "info.circle") }
        }
        .frame(width: 640, height: 540)
    }
}

private struct GeneralPane: View {
    @AppStorage(Key.haptics) private var haptics = true
    @AppStorage(Key.sound) private var sound = true
    @AppStorage(Key.copyText) private var copyText = true
    @AppStorage(Key.reveal) private var reveal = false
    @AppStorage(Key.usualGlow) private var usual = true
    @ViewState private var login = SMAppService.mainApp.status == .enabled
    @ViewState private var loginError = ""

    var body: some View {
        Form {
            Section {
                Toggle("Open DragShift when I log in", isOn: $login)
                    .onChange(of: login) { on in setLogin(on) }
                if !loginError.isEmpty { Text(loginError).font(.caption).foregroundStyle(.orange) }
            }
            Section("Feel") {
                Toggle("Trackpad tick when pointing at a slice", isOn: $haptics)
                Toggle("Soft sound when a file is ready", isOn: $sound)
                Toggle("Make what I pick most glow when the ring opens", isOn: $usual)
            }
            Section("After it's done") {
                Toggle("Put text it reads (OCR, PDF → Text) on the clipboard", isOn: $copyText)
                Toggle("Show the new file in Finder", isOn: $reveal)
            }
            Section {
                Button("Show the welcome tour again") { App.shared.showWelcome() }
            }
        }
        .formStyle(.grouped)
    }

    private func setLogin(_ on: Bool) {
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            loginError = ""
        } catch {
            loginError = "macOS said no: \(error.localizedDescription). Move DragShift into Applications and try again."
            login = SMAppService.mainApp.status == .enabled
        }
    }
}

struct TriggersPane: View {
    @AppStorage(Key.triggerShift) private var shift = true
    @AppStorage(Key.triggerFinger) private var finger = true
    @AppStorage(Key.triggerMouse) private var mouse = true
    @AppStorage(Key.triggerShake) private var shake = false
    @AppStorage(Key.triggerHover) private var hover = false
    @AppStorage(Key.palm) private var palm = true
    @AppStorage(Key.hotkey) private var hotkey = "ctrlOptV"
    @ObservedObject private var live = Live.shared

    var body: some View {
        Form {
            Section {
                Toggle(isOn: $shift) {
                    VStack(alignment: .leading) {
                        Text("Press Shift while dragging")
                        Text("Or hold Shift first, then grab the file. Press Shift again for the next page.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                Toggle(isOn: $finger) {
                    VStack(alignment: .leading) {
                        Text("Tap one more finger on the trackpad")
                        Text("Drag with one finger, touch a second one down. Tap again for the next page.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                if finger { TrackpadStatus(live: live) }
                Toggle(isOn: $mouse) {
                    VStack(alignment: .leading) {
                        Text("Click the mouse wheel while dragging")
                        Text("For mice: hold the file with the left button, click the wheel (or a side button). Shift works with a mouse too.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                Toggle(isOn: $shake) {
                    VStack(alignment: .leading) {
                        Text("Shake the file")
                        Text("Wiggle left-right quickly while dragging.").font(.caption).foregroundStyle(.secondary)
                    }
                }
                Toggle(isOn: $hover) {
                    VStack(alignment: .leading) {
                        Text("Hold still")
                        Text("Stop moving for a moment while dragging. Off by default: it can get in the way of folders that spring open.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                Toggle(isOn: $palm) {
                    VStack(alignment: .leading) {
                        Text("Ignore a resting palm")
                        Text("Big contacts (a palm or a flat thumb) don't count as the extra finger.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
            Section {
                Picker("Open the clipboard in the ring", selection: $hotkey) {
                    ForEach(HotKey.choices, id: \.id) { Text($0.title).tag($0.id) }
                }
                .onChange(of: hotkey) { _ in HotKey.shared.apply() }
                Text("Copy files in Finder, take a screenshot to the clipboard (⌃⇧⌘4), or copy a link or text, then press the shortcut.")
                    .font(.caption).foregroundStyle(.secondary)
                Text("Double-tap Shift (or the extra finger) while dragging to repeat the last thing you did to that kind of file.")
                    .font(.caption).foregroundStyle(.secondary)
            } header: {
                Text("Shortcuts")
            } footer: {
                Text("Once the ring is open you only need one finger: push the file toward a slice and let go. Or let go in the middle and the ring stays open: lift your fingers, glide to a slice and tap it. Esc or a click elsewhere closes it.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}

struct TrackpadStatus: View {
    @ObservedObject var live: Live
    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: live.touchSeen ? "checkmark.circle.fill" : (live.touchAvailable ? "hand.point.up.left" : "xmark.circle"))
                .foregroundStyle(live.touchSeen ? Color.green : (live.touchAvailable ? Color.secondary : Color.orange))
            if !live.touchAvailable {
                Text("No trackpad found. Shift still works.")
            } else if live.touchSeen {
                Text("Trackpad working · fingers down now: \(live.fingers)")
            } else {
                Text("Touch the trackpad to test…")
            }
        }
        .font(.caption)
    }
}

struct SavingPane: View {
    @AppStorage(Key.outputMode) private var mode = "beside"
    @AppStorage(Key.outputFolder) private var folder = ""
    @AppStorage(Key.quality) private var quality = 0.85
    @AppStorage(Key.compressQuality) private var compressQuality = 0.7
    @AppStorage(Key.compressMaxEdge) private var maxEdge = 3000
    @AppStorage(Key.pdfDPI) private var dpi = 200.0
    @ViewState private var photo = Prefs.photo

    var body: some View {
        Form {
            Section("Save new files") {
                Picker("Where", selection: $mode) {
                    Text("Next to the original").tag("beside")
                    Text("Downloads").tag("downloads")
                    Text(folder.isEmpty ? "A folder I choose…" : URL(fileURLWithPath: folder).lastPathComponent).tag("folder")
                }
                .onChange(of: mode) { m in if m == "folder" && folder.isEmpty { choose() } }
                if mode == "folder" {
                    HStack {
                        Text(folder).font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.head)
                        Spacer()
                        Button("Change…") { choose() }
                    }
                }
                Text("Originals are never changed. If a name is taken, DragShift adds 2, 3… like Finder does.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("Quality") {
                LabeledContent("JPG / HEIC / WebP / AVIF") {
                    HStack { Slider(value: $quality, in: 0.5...1); Text("\(Int(quality * 100))").monospacedDigit().frame(width: 30) }
                }
                LabeledContent("Compress") {
                    HStack { Slider(value: $compressQuality, in: 0.4...0.9); Text("\(Int(compressQuality * 100))").monospacedDigit().frame(width: 30) }
                }
                Picker("Compress shrinks photos to", selection: $maxEdge) {
                    Text("2048 px").tag(2048)
                    Text("3000 px").tag(3000)
                    Text("4096 px").tag(4096)
                    Text("Keep size").tag(100_000)
                }
                Picker("PDF pages as images", selection: $dpi) {
                    Text("150 dpi (small)").tag(150.0)
                    Text("200 dpi").tag(200.0)
                    Text("300 dpi (print)").tag(300.0)
                }
            }
            Section {
                Picker("Size", selection: Binding(get: { presetIndex }, set: { i in
                    var p = PhotoSpec.presets[i].1
                    p.background = photo.background
                    photo = p
                })) {
                    ForEach(PhotoSpec.presets.indices, id: \.self) { i in Text(PhotoSpec.presets[i].0).tag(i) }
                }
                Picker("Keep it under", selection: $photo.maxKB) {
                    ForEach([20, 50, 100, 200, 240, 500], id: \.self) { Text(SizeLimit.title(kb: $0)).tag($0) }
                    Text("No limit").tag(0)
                }
                Picker("Background", selection: $photo.background) {
                    ForEach(PhotoSpec.Background.allCases, id: \.self) { Text($0.title).tag($0) }
                }
            } header: {
                Text("Passport photo")
            } footer: {
                Text("Finds the face, centres it the way passport rules ask, puts the person on the colour you pick, and saves a JPG at 300 dpi so it prints at exactly \(photo.sizeTitle).")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .onChange(of: photo) { p in Prefs.photo = p }
    }

    private var presetIndex: Int {
        PhotoSpec.presets.firstIndex { $0.1.widthMM == photo.widthMM && $0.1.heightMM == photo.heightMM } ?? 0
    }

    private func choose() {
        let p = NSOpenPanel()
        p.canChooseDirectories = true
        p.canChooseFiles = false
        p.canCreateDirectories = true
        p.prompt = "Use This Folder"
        if p.runModal() == .OK, let u = p.url { folder = u.path } else if folder.isEmpty { mode = "beside" }
    }
}

struct HiddenSlicesListWrapper: View {
    @ViewState private var hidden = Prefs.hidden
    private let all = Catalog.everyAction

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Untick slices you never use. Fewer slices = bigger targets.")
                .font(.callout).foregroundStyle(.secondary)
            List {
                ForEach(all) { a in
                    Toggle(isOn: Binding(get: { !hidden.contains(a.id) }, set: { on in
                        if on { hidden.remove(a.id) } else { hidden.insert(a.id) }
                        Prefs.hidden = hidden
                    })) {
                        Label(a.title, systemImage: a.symbol)
                    }
                }
            }
        }
    }
}

struct AboutPane: View {
    /// Only the snapshot pictures set this; the app shows the live count.
    var fixed: Int? = nil
    var frozenCup: (offset: CGSize, open: Bool)? = nil

    var body: some View {
        VStack(spacing: 8) {
            Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 56, height: 56)
            Text("DragShift").font(.title2.weight(.bold))
            Text("Version \(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev")")
                .foregroundStyle(.secondary)
            Text("Everything happens on this Mac. No internet, no account, no tracking. Text reading, background removal and redaction use Apple's on-device Vision.")
                .multilineTextAlignment(.center).font(.callout).padding(.horizontal, 30)
            VStack(alignment: .leading, spacing: 4) {
                tool("Smaller (lossy) WebP", Tools.cwebp, "brew install webp; lossless WebP is built in")
                tool("MP3, FLAC, WebM", Tools.ffmpeg, "brew install ffmpeg")
            }
            .font(.caption)
            HStack {
                Button("Open Log") { NSWorkspace.shared.open(Log.url) }
                Button("Check Trackpad") { App.shared.showWelcome() }
            }
            Divider().padding(.horizontal, 60).padding(.vertical, 2)
            CoffeeCorner(fixedForSnapshot: fixed, frozen: frozenCup)
        }
        .padding()
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private func tool(_ name: String, _ url: URL?, _ how: String) -> some View {
        HStack(spacing: 6) {
            Image(systemName: url == nil ? "minus.circle" : "checkmark.circle.fill")
                .foregroundStyle(url == nil ? Color.secondary : Color.green)
            Text(name)
            Text(url == nil ? "— optional: \(how)" : "— on").foregroundStyle(.secondary)
        }
    }
}
