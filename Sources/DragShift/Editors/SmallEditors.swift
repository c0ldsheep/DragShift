import AppKit
import AVFoundation
import AVKit
import DragShiftCore
import SwiftUI

/// Resize presets, with the resulting size shown before you commit.
struct ResizeSheet: View {
    let files: [URL]
    let options: Options

    enum Choice: Hashable { case percent(Int), edge(Int), custom }
    @ViewState private var choice: Choice = .percent(50)
    @ViewState private var custom = "1200"
    @ViewState private var size = CGSize(width: 1, height: 1)

    private let choices: [(String, Choice)] = [
        ("25%", .percent(25)), ("50%", .percent(50)), ("75%", .percent(75)),
        ("1080 px", .edge(1080)), ("1600 px", .edge(1600)), ("2048 px", .edge(2048)), ("3840 px (4K)", .edge(3840)),
        ("Custom", .custom),
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            EditorHeader(files: files, note: "Now \(Int(size.width)) × \(Int(size.height)) px")
            Text("Longest side / scale").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
            VStack(alignment: .leading, spacing: 6) {
                ForEach(choices, id: \.0) { name, c in
                    HStack {
                        Image(systemName: choice == c ? "largecircle.fill.circle" : "circle")
                            .foregroundStyle(choice == c ? Palette.hotB : Color.secondary)
                        Text(name)
                        if c == .custom {
                            TextField("", text: $custom).frame(width: 70).textFieldStyle(.roundedBorder)
                            Text("px").foregroundStyle(.secondary)
                        }
                        Spacer()
                    }
                    .contentShape(Rectangle())
                    .onTapGesture { choice = c }
                }
            }
            Text("→ \(Int(target.width)) × \(Int(target.height)) px").font(.callout.monospacedDigit().weight(.semibold))
            Spacer()
            HStack {
                Button("Cancel") { Windows.close("resize") }.keyboardShortcut(.cancelAction)
                Spacer()
                Button("Resize") { apply() }.buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction)
            }
        }
        .padding(18).padding(.top, 14)
        .task { size = files.first.flatMap(ImageOps.pixelSize) ?? size }
    }

    private var target: CGSize {
        let longest = max(size.width, size.height)
        switch choice {
        case .percent(let p): return CGSize(width: size.width * Double(p) / 100, height: size.height * Double(p) / 100)
        case .edge(let e): return CGSize(width: size.width * Double(e) / longest, height: size.height * Double(e) / longest)
        case .custom:
            let e = Double(custom) ?? longest
            return CGSize(width: size.width * e / longest, height: size.height * e / longest)
        }
    }

    private func apply() {
        var edge: Int? = nil, scale: Double? = nil
        switch choice {
        case .percent(let p): scale = Double(p) / 100
        case .edge(let e): edge = e
        case .custom: edge = Int(custom)
        }
        let files = files, o = options, e = edge, sc = scale
        Editors.work("Resizing", count: files.count, close: "resize") {
            try files.map { try ImageOps.resize($0, longEdge: e, scale: sc, quality: o.quality,
                                                dir: Naming.folder(for: $0, override: o.outputFolder)) }
        }
    }
}

/// Lock adds a password to a copy; Unlock saves a copy without one (you need the password).
struct PasswordSheet: View {
    let files: [URL]
    let lock: Bool
    let options: Options
    @ViewState private var password = ""
    @ViewState private var again = ""

    init(files: [URL], lock: Bool, options: Options, typed: String = "") {
        self.files = files
        self.lock = lock
        self.options = options
        _password = ViewState(initialValue: typed) // `typed` only for snapshots of the strength meter
        _again = ViewState(initialValue: typed)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            EditorHeader(files: files, note: lock ? "Anyone opening the copy will need this password" : "Type the current password")
            SecureField("Password", text: $password).textFieldStyle(.roundedBorder)
            if lock, let rating = PasswordStrength.rate(password) {
                StrengthMeter(strength: rating.strength, hint: rating.hint)
            }
            if lock {
                SecureField("Type it again", text: $again).textFieldStyle(.roundedBorder)
                if !again.isEmpty && again != password {
                    Text("The two passwords don't match").font(.caption).foregroundStyle(.red)
                }
            }
            Spacer()
            HStack {
                Button("Cancel") { Windows.close("password") }.keyboardShortcut(.cancelAction)
                Spacer()
                Button(lock ? "Lock" : "Unlock") { apply() }
                    .buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction)
                    .disabled(password.isEmpty || (lock && again != password))
            }
        }
        .padding(18).padding(.top, 14)
    }

    private func apply() {
        let pw = password, files = files, o = options, lock = lock
        Editors.work(lock ? "Adding a password" : "Removing the password", count: files.count, close: "password") {
            try files.map { f in
                let dir = Naming.folder(for: f, override: o.outputFolder)
                return lock ? try PDFOps.lock(f, password: pw, dir: dir) : try PDFOps.unlock(f, password: pw, dir: dir)
            }
        }
    }
}

/// Three short bars and a line under the Lock password: red, orange or green (PasswordStrength). A hint only:
/// Lock still works with any password.
struct StrengthMeter: View {
    let strength: PasswordStrength
    let hint: String
    private var colour: Color { [Color.red, .orange, .green][strength.rawValue] }
    var body: some View {
        HStack(spacing: 8) {
            HStack(spacing: 3) {
                ForEach(0..<3, id: \.self) { i in
                    Capsule().fill(i <= strength.rawValue ? colour : Color.secondary.opacity(0.2)).frame(width: 22, height: 4)
                }
            }
            Text(hint).font(.caption).foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Password strength: \(["weak", "fair", "strong"][strength.rawValue]). \(hint)")
    }
}

/// Apple's own trim bar (the one in QuickTime), then a lossless cut.
struct TrimEditor: View {
    let file: URL
    let options: Options
    var body: some View {
        TrimPlayer(file: file, options: options).padding(.top, 28)
    }
}

struct TrimPlayer: NSViewRepresentable {
    let file: URL
    let options: Options

    func makeNSView(context: Context) -> AVPlayerView {
        let v = AVPlayerView()
        v.controlsStyle = .inline
        let item = AVPlayerItem(url: file)
        v.player = AVPlayer(playerItem: item)
        context.coordinator.observe(item, view: v, file: file, options: options)
        return v
    }

    func updateNSView(_ nsView: AVPlayerView, context: Context) {}
    func makeCoordinator() -> Coordinator { Coordinator() }

    final class Coordinator {
        private var token: NSKeyValueObservation?

        func observe(_ item: AVPlayerItem, view: AVPlayerView, file: URL, options: Options) {
            token = item.observe(\.status, options: [.initial, .new]) { [weak view] item, _ in
                guard item.status == .readyToPlay else { return }
                DispatchQueue.main.async {
                    guard let view, view.canBeginTrimming else { return }
                    view.beginTrimming { result in
                        MainActor.assumeIsolated {
                            guard result == .okButton else { Windows.close("trim"); return }
                            let start = item.reversePlaybackEndTime.isValid ? item.reversePlaybackEndTime : .zero
                            let end = item.forwardPlaybackEndTime.isValid ? item.forwardPlaybackEndTime : item.duration
                            let range = CMTimeRange(start: start, end: end)
                            view.player?.pause()
                            Editors.workAsync("Trimming", count: 1, close: "trim") {
                                [try await MediaOps.trim(file, range: range, dir: Naming.folder(for: file, override: options.outputFolder))]
                            }
                        }
                    }
                }
            }
        }
    }
}
