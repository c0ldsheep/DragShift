import AppKit
import DragShiftCore
import PDFKit
import SwiftUI

/// Lays its children out left to right and wraps to a new row when one doesn't fit.
struct Flow: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? .infinity
        var x: CGFloat = 0, y: CGFloat = 0, row: CGFloat = 0, widest: CGFloat = 0
        for v in subviews {
            let s = v.sizeThatFits(.unspecified)
            if x > 0 && x + s.width > width { x = 0; y += row + spacing; row = 0 }
            x += s.width + spacing
            row = max(row, s.height)
            widest = max(widest, x - spacing)
        }
        return CGSize(width: min(widest, width), height: y + row)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX, y = bounds.minY, row: CGFloat = 0
        for v in subviews {
            let s = v.sizeThatFits(.unspecified)
            if x > bounds.minX && x + s.width > bounds.maxX { x = bounds.minX; y += row + spacing; row = 0 }
            v.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(s))
            x += s.width + spacing
            row = max(row, s.height)
        }
    }
}

/// The printer, paper and options for one preset. Used by the print window and by Settings → Printing.
struct PrintOptions: View {
    @Binding var preset: PrintPreset
    var showFill = true
    var labelWidth: CGFloat = 64
    var controlWidth: CGFloat = 230
    private let printers = Printer.names
    private let defaultPrinter = Printer.defaultName

    var body: some View {
        Grid(alignment: .leading, horizontalSpacing: 10, verticalSpacing: 9) {
            GridRow {
                label("Printer")
                Picker("Printer", selection: $preset.printer) {
                    Text("Default (\(defaultPrinter ?? "none set"))").tag(String?.none)
                    if !printers.isEmpty { Divider() }
                    ForEach(printers, id: \.self) { Text($0).tag(String?.some($0)) }
                }
                .labelsHidden().frame(width: controlWidth)
            }
            GridRow {
                label("Paper")
                Picker("Paper", selection: $preset.paper) {
                    ForEach(PrintPreset.Paper.allCases, id: \.self) { Text($0.title).tag($0) }
                }
                .labelsHidden().frame(width: controlWidth)
            }
            GridRow {
                label("Copies")
                Stepper(value: $preset.copies, in: 1...99) {
                    Text("\(preset.copies)").monospacedDigit().frame(minWidth: 22, alignment: .leading)
                }
            }
            GridRow {
                label("Colour")
                Picker("Colour", selection: $preset.color) {
                    Text("Colour").tag(true)
                    Text("Black & white").tag(false)
                }
                .pickerStyle(.segmented).labelsHidden().frame(width: controlWidth)
            }
            GridRow {
                label("Sides")
                Toggle("Print on both sides", isOn: $preset.twoSided)
            }
            GridRow {
                label("Pictures")
                Picker("Pictures", selection: $preset.fit) {
                    ForEach(PrintPreset.Fit.allCases, id: \.self) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented).labelsHidden().frame(width: controlWidth)
                .disabled(preset.fillSheet)
            }
            if showFill {
                GridRow {
                    Color.clear.frame(width: labelWidth, height: 1)
                    Toggle("Fill the sheet with copies (passport photos)", isOn: $preset.fillSheet)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private func label(_ s: String) -> some View {
        Text(s).foregroundStyle(.secondary).frame(width: labelWidth, alignment: .trailing)
    }
}

/// Print, in two steps. First a short list of your presets (DragShift's and the ones saved in the macOS print
/// window): click one (or Return) and it prints, no settings to go through. "Choose…" picks which presets the list
/// (and the ring's Print page) shows. "More options…" opens the full view (printer, paper, copies, colour, sides, fit,
/// preview) only when you ask for it. Whatever was printed becomes "Last used" (and "Print again" for double-tap repeat).
struct PrintSheet: View {
    let files: [URL]
    let temporary: Bool

    @ViewState private var presets: [PrintPreset]
    /// DragShift's presets and the macOS ones.
    @ViewState private var all: [PrintPreset]
    /// Switched-off ring slices; a preset in here is left out of the list.
    @ViewState private var off: Set<String>
    @ViewState private var choosing = false
    @ViewState private var p: PrintPreset
    @ViewState private var full: Bool
    @ViewState private var highlight = 0
    @ViewState private var monitor: Any?
    @ViewState private var preview: NSImage?
    @ViewState private var sheetSize = CGSize(width: 210, height: 297)
    @ViewState private var pages = 0
    @ViewState private var notes: [String] = []
    @ViewState private var naming = false
    @ViewState private var newName = ""
    @ViewState private var build = 0
    private let noPrinters = Printer.names.isEmpty

    static let fullSize = CGSize(width: 600, height: 500)
    /// The list fits every preset (up to 8 before it scrolls).
    static func quickSize(rows: Int) -> CGSize { CGSize(width: 440, height: 150 + CGFloat(min(max(rows, 1), 8)) * 56) }
    static var quickRows: Int { Prefs.shownPrintPresets.count + (customLast == nil ? 0 : 1) }

    /// The last settings, when they aren't one of the saved presets (so they get their own row).
    private static var customLast: PrintPreset? {
        guard let last = Prefs.printLast, !Prefs.allPrintPresets.contains(where: { $0.sameSettings(as: last) }) else { return nil }
        var l = last
        l.name = "Last settings"
        return l
    }

    /// `start`: the settings to open with (normally what was printed last). `full`: open on all the options.
    /// `choosing`: open on the tick boxes (snapshots).
    init(files: [URL], temporary: Bool, start: PrintPreset? = nil, full: Bool = false, choosing: Bool = false) {
        self.files = files
        self.temporary = temporary
        _presets = ViewState(initialValue: Prefs.printPresets)
        let shown = Prefs.shownPrintPresets
        _all = ViewState(initialValue: Prefs.allPrintPresets)
        _off = ViewState(initialValue: Prefs.hidden)
        let first = start ?? Prefs.printLast ?? shown.first ?? PrintPreset.builtIns[0]
        _p = ViewState(initialValue: first)
        _full = ViewState(initialValue: full)
        _choosing = ViewState(initialValue: choosing)
        let rows = (Self.customLast.map { [$0] } ?? []) + shown
        _highlight = ViewState(initialValue: max(0, rows.firstIndex { $0.sameSettings(as: first) } ?? 0))
    }

    private var shown: [PrintPreset] { all.filter { !off.contains($0.stepID) } }
    /// While choosing, every preset (ticked or not); otherwise the shown ones, after "Last settings" if any.
    private var rows: [PrintPreset] { choosing ? all : (Self.customLast.map { [$0] } ?? []) + shown }
    private var chosen: PrintPreset? { all.first { $0.sameSettings(as: p) } }
    private var hasPictures: Bool { files.contains { FileInspector.kind(of: $0) == .image } }

    var body: some View {
        Group {
            if full { fullView } else { quickView }
        }
        .padding(18).padding(.top, 14)
        .onAppear(perform: listenForArrows)
        .task { if full { rebuild() } }
        .onDisappear { if let m = monitor { NSEvent.removeMonitor(m) }; monitor = nil }
    }

    // MARK: The list

    private var quickView: some View {
        VStack(alignment: .leading, spacing: 12) {
            EditorHeader(files: files, note: choosing ? "Tick the presets to show here and on the ring's Print page"
                         : "Prints on \(printerName(rows.indices.contains(highlight) ? rows[highlight] : p))")
            ScrollView {
                VStack(spacing: 4) {
                    ForEach(Array(rows.enumerated()), id: \.offset) { i, preset in
                        if choosing { tick(preset) } else { row(i, preset) }
                    }
                    if rows.isEmpty {
                        Text("Every preset is switched off. Choose… brings them back.")
                            .font(.callout).foregroundStyle(.secondary).padding(.vertical, 18)
                    }
                }
            }
            if noPrinters {
                Text("No printer is set up on this Mac yet.").font(.caption).foregroundStyle(.orange)
            }
            HStack(spacing: 10) {
                if choosing {
                    Text("\(shown.count) of \(all.count) shown").font(.caption).foregroundStyle(.secondary)
                    Spacer()
                    Button("Done") { choose(false) }
                        .buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction)
                } else {
                    Button("More options…") { openFull() }
                        .help("Printer, paper, copies, colour, sides and a preview")
                    Button("Choose…") { choose(true) }
                        .help("Pick which presets show in this list and on the ring's Print page")
                    Spacer()
                    Button("Cancel") { Windows.close("print") }.keyboardShortcut(.cancelAction)
                    Button("Print") { if rows.indices.contains(highlight) { printWith(rows[highlight]) } }
                        .buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction)
                        .disabled(noPrinters || rows.isEmpty)
                }
            }
        }
    }

    /// Choosing: one preset with a tick box; clicking anywhere on it shows or hides it.
    private func tick(_ preset: PrintPreset) -> some View {
        let on = !off.contains(preset.stepID)
        return Button {
            if on { off.insert(preset.stepID) } else { off.remove(preset.stepID) }
            Prefs.hidden = off
        } label: {
            HStack(spacing: 12) {
                Image(systemName: on ? "checkmark.square.fill" : "square")
                    .font(.system(size: 17)).foregroundStyle(on ? Palette.hotB : Color.secondary).frame(width: 28)
                VStack(alignment: .leading, spacing: 2) {
                    Text(preset.name).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                    Text(short(preset)).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer(minLength: 8)
            }
            .padding(.horizontal, 12).padding(.vertical, 9)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 10).fill(Color.primary.opacity(0.04)))
            .contentShape(RoundedRectangle(cornerRadius: 10))
        }
        .buttonStyle(.plain)
    }

    private func choose(_ on: Bool) {
        choosing = on
        if !on { highlight = min(highlight, max(0, rows.count - 1)) }
        Windows.resize("print", to: Self.quickSize(rows: rows.count))
    }

    /// One preset: click prints it. The highlighted one (hover, arrows, or what you used last) prints on Return.
    private func row(_ i: Int, _ preset: PrintPreset) -> some View {
        let on = i == highlight
        let last = Prefs.printLast.map { $0.sameSettings(as: preset) } ?? false
        return Button { printWith(preset) } label: {
            HStack(spacing: 12) {
                Image(systemName: icon(preset))
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundStyle(on ? Color.white : Palette.hotB)
                    .frame(width: 28)
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: 6) {
                        Text(preset.name).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                        if last {
                            Text("Last used").font(.system(size: 10, weight: .semibold))
                                .padding(.horizontal, 6).padding(.vertical, 1)
                                .background(Capsule().fill(on ? Color.white.opacity(0.25) : Color.primary.opacity(0.08)))
                        }
                    }
                    Text(short(preset)).font(.caption).lineLimit(1)
                        .foregroundStyle(on ? Color.white.opacity(0.85) : Color.secondary)
                }
                Spacer(minLength: 8)
                if on {
                    Image(systemName: "return").font(.system(size: 12, weight: .semibold)).foregroundStyle(Color.white.opacity(0.85))
                }
            }
            .foregroundStyle(on ? Palette.onHot : Color.primary)
            .padding(.horizontal, 12).padding(.vertical, 9)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background {
                if on { RoundedRectangle(cornerRadius: 10).fill(Palette.hot) } else { RoundedRectangle(cornerRadius: 10).fill(Color.primary.opacity(0.04)) }
            }
            .contentShape(RoundedRectangle(cornerRadius: 10))
        }
        .buttonStyle(.plain)
        .onHover { if $0 { highlight = i } }
        .help(preset.summary(defaultPrinter: Printer.defaultName))
    }

    /// "A4 · 2 copies · Black & white · Two-sided": what makes this preset different, without the printer unless set.
    private func short(_ preset: PrintPreset) -> String {
        var parts: [String] = []
        if let printer = preset.printer { parts.append(printer) }
        if preset.system != nil { return (parts + ["Saved in the macOS print window"]).joined(separator: " · ") }
        parts.append(preset.paper.title)
        if preset.copies > 1 { parts.append("\(preset.copies) copies") }
        parts.append(preset.color ? "Colour" : "Black & white")
        if preset.twoSided { parts.append("Two-sided") }
        parts.append(preset.fillSheet ? "Fills the sheet with copies" : preset.fit.title)
        return parts.joined(separator: " · ")
    }

    private func printerName(_ preset: PrintPreset) -> String { preset.printer ?? Printer.defaultName ?? "no printer" }

    /// Up and down move through the list (Return prints, Escape closes; those are the buttons' shortcuts).
    private func listenForArrows() {
        guard monitor == nil else { return }
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { e in
            guard !full, !choosing, e.window === Windows.window("print") else { return e }
            switch e.keyCode {
            case 125: highlight = min(rows.count - 1, highlight + 1); return nil
            case 126: highlight = max(0, highlight - 1); return nil
            default: return e
            }
        }
    }

    private func openFull() {
        if rows.indices.contains(highlight) { p = rows[highlight] }
        full = true
        Windows.resize("print", to: Self.fullSize)
        rebuild()
    }

    private func backToList() {
        full = false
        naming = false
        highlight = max(0, rows.firstIndex { $0.sameSettings(as: p) } ?? highlight)
        Windows.resize("print", to: Self.quickSize(rows: rows.count))
    }

    // MARK: All the options

    private var fullView: some View {
        VStack(alignment: .leading, spacing: 14) {
            EditorHeader(files: files, note: status)
            presetRow
            HStack(alignment: .top, spacing: 18) {
                previewBox
                VStack(alignment: .leading, spacing: 10) {
                    if p.system != nil {
                        Text("“\(p.name)” comes from the macOS print window and prints exactly as it was saved there: pages per sheet, orientation, quality and paper type.")
                            .fixedSize(horizontal: false, vertical: true)
                        Text("To change it, click System dialog…, set it up, then Presets → Save Current Settings as Preset under the same name.")
                            .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    } else {
                        PrintOptions(preset: $p, showFill: hasPictures)
                    }
                    if noPrinters {
                        Text("No printer is set up on this Mac yet.").font(.caption).foregroundStyle(.orange)
                        Button("Open Printers & Scanners") {
                            NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.Print-Scan-Settings.extension")!)
                        }
                        .controlSize(.small)
                    }
                }
            }
            if !notes.isEmpty {
                Text(notes.joined(separator: "\n")).font(.caption).foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
            HStack(spacing: 10) {
                Button { backToList() } label: { Label("Presets", systemImage: "chevron.left") }
                    .help("Back to the short list")
                Button("System dialog…") { systemDialog() }
                    .help("The macOS print window: page ranges, paper trays, Save as PDF")
                if naming {
                    TextField("Name it", text: $newName)
                        .textFieldStyle(.roundedBorder).frame(width: 130)
                        .onSubmit(save)
                    Button("Save", action: save).disabled(newName.trimmingCharacters(in: .whitespaces).isEmpty)
                } else if chosen == nil {
                    Button("Save as preset…") { naming = true; newName = "" }
                }
                Spacer()
                Button("Cancel") { Windows.close("print") }.keyboardShortcut(.cancelAction)
                Button("Print") { printWith(p) }
                    .buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction)
                    .disabled(pages == 0 || noPrinters)
            }
        }
        .onChange(of: p) { _ in rebuild() }
    }

    private var status: String {
        guard pages > 0 else { return "Getting it ready…" }
        return "\(pages) page\(pages == 1 ? "" : "s") · \(p.system != nil ? "macOS preset" : p.paper.title) · \(printerName(p))"
    }

    /// Every saved preset as a button; they wrap onto more rows instead of running off the edge.
    private var presetRow: some View {
        Flow(spacing: 8) {
            ForEach(shown) { preset in
                let on = preset.sameSettings(as: p)
                Button { p = preset } label: {
                    HStack(spacing: 6) {
                        Image(systemName: icon(preset)).font(.system(size: 12, weight: .semibold))
                        Text(preset.name).font(.system(size: 13, weight: .semibold)).lineLimit(1)
                    }
                    .padding(.horizontal, 12).padding(.vertical, 7)
                    .foregroundStyle(on ? Palette.onHot : Color.primary)
                    .background {
                        if on { Capsule().fill(Palette.hot) } else { Capsule().fill(Color.primary.opacity(0.07)) }
                    }
                    .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .help(preset.summary(defaultPrinter: Printer.defaultName))
            }
        }
    }

    private func icon(_ preset: PrintPreset) -> String {
        if preset.system != nil { return "slider.horizontal.3" }
        if preset.fillSheet { return "person.crop.rectangle.stack" }
        if !preset.color { return "circle.lefthalf.filled" }
        return "printer"
    }

    /// The first sheet as it will print, on a little piece of paper.
    private var previewBox: some View {
        let box = CGSize(width: 190, height: 250)
        let s = min(box.width / sheetSize.width, box.height / sheetSize.height)
        return ZStack {
            Rectangle().fill(Color.white)
                .shadow(color: .black.opacity(0.25), radius: 6, y: 2)
            if let preview {
                Image(nsImage: preview).resizable().interpolation(.high)
            } else {
                ProgressView().controlSize(.small)
            }
        }
        .frame(width: sheetSize.width * s, height: sheetSize.height * s)
        .overlay(alignment: .bottomTrailing) {
            if pages > 1 {
                Text("1 of \(pages)").font(.caption2.weight(.semibold)).padding(.horizontal, 6).padding(.vertical, 2)
                    .background(.thinMaterial, in: Capsule()).padding(5)
            }
        }
        .frame(width: box.width, height: box.height)
    }

    /// Lays the files out again for the preview (shortly after the last change).
    private func rebuild() {
        build += 1
        let id = build
        let preset = p
        let paper = Printer.paper(for: preset)
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 150_000_000)
            guard id == build else { return }
            let dir = FileManager.default.temporaryDirectory.appendingPathComponent("DragShift Print Preview", isDirectory: true)
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            do {
                let job = try await PrintDocument.make(files, preset: preset, paper: paper, dir: dir)
                defer { try? FileManager.default.removeItem(at: job.pdf) }
                guard id == build, let page = PDFDocument(url: job.pdf)?.page(at: 0) else { return }
                let b = page.bounds(for: .mediaBox)
                sheetSize = b.size
                preview = page.thumbnail(of: CGSize(width: b.width * 1.4, height: b.height * 1.4), for: .mediaBox)
                pages = job.pages
                notes = job.notes
            } catch {
                guard id == build else { return }
                preview = nil
                pages = 0
                notes = [error.localizedDescription]
            }
        }
    }

    private func save() {
        let name = newName.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return }
        var n = p
        n.id = UUID().uuidString
        n.name = name
        presets.append(n)
        Prefs.printPresets = presets
        all = Prefs.allPrintPresets
        naming = false
    }

    /// Prints now with these settings; a saved preset prints as itself, anything else as "Print again".
    private func printWith(_ settings: PrintPreset) {
        let saved = all.first { $0.sameSettings(as: settings) }
        var last = settings
        last.id = PrintPreset.lastID
        last.name = saved?.name ?? "Print again"
        Prefs.printLast = last
        Windows.close("print")
        Runner.shared.perform(saved?.action ?? last.action, files, at: NSEvent.mouseLocation, temporary: temporary)
    }

    private func systemDialog() {
        Prefs.printLast = { var l = p; l.id = PrintPreset.lastID; l.name = "Print again"; return l }()
        Windows.close("print")
        Printer.run(files, preset: p, at: NSEvent.mouseLocation, panel: true)
    }
}
