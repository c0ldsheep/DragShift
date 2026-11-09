import AppKit
import DragShiftCore
import SwiftUI
#if canImport(Translation)
import Translation
#endif

/// Batch rename with a live before → after list. Undo puts the old names back.
struct RenameSheet: View {
    let files: [URL]

    enum Mode: String, CaseIterable { case number = "Number", date = "Date", replace = "Replace", textCase = "Case" }
    @ViewState private var mode: Mode = .number
    @ViewState private var base = ""
    @ViewState private var start = 1
    @ViewState private var find = ""
    @ViewState private var with = ""
    @ViewState private var caseMode = 0

    private var renameMode: RenameMode {
        switch mode {
        case .number: return .numbered(base: base, start: start)
        case .date: return .datePrefix
        case .replace: return .replace(find: find, with: with)
        case .textCase: return [RenameMode.lowercase, .uppercase, .titlecase][caseMode]
        }
    }

    private var plan: [Renamer.Item] { Renamer.plan(files, mode: renameMode) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            EditorHeader(files: files, note: "Renames in place · Undo puts the names back")
            Picker("", selection: $mode) { ForEach(Mode.allCases, id: \.self) { Text($0.rawValue).tag($0) } }
                .pickerStyle(.segmented).labelsHidden()
            Group {
                switch mode {
                case .number:
                    HStack {
                        TextField("Name", text: $base).textFieldStyle(.roundedBorder)
                        Stepper("from \(start)", value: $start, in: 0...9999).fixedSize()
                    }
                case .date:
                    Text("Adds the date the photo was taken (or the file was made) in front: 2026-10-22 Name.jpg")
                        .font(.callout).foregroundStyle(.secondary)
                case .replace:
                    HStack {
                        TextField("Find", text: $find).textFieldStyle(.roundedBorder)
                        Image(systemName: "arrow.right").foregroundStyle(.secondary)
                        TextField("Replace with", text: $with).textFieldStyle(.roundedBorder)
                    }
                case .textCase:
                    Picker("", selection: $caseMode) { Text("lower case").tag(0); Text("UPPER CASE").tag(1); Text("Title Case").tag(2) }
                        .pickerStyle(.segmented).labelsHidden()
                }
            }
            List(plan) { item in
                HStack(spacing: 6) {
                    Text(item.from.lastPathComponent).lineLimit(1).truncationMode(.middle).foregroundStyle(.secondary)
                    Image(systemName: "arrow.right").font(.caption).foregroundStyle(.secondary)
                    Text(item.to.lastPathComponent).lineLimit(1).truncationMode(.middle)
                        .foregroundStyle(item.problem == nil ? Color.primary : Color.red)
                    if let p = item.problem { Text(p).font(.caption).foregroundStyle(.red) }
                }
                .font(.callout)
            }
            .listStyle(.inset)
            HStack {
                Spacer()
                Button("Cancel") { Windows.close("rename") }.keyboardShortcut(.cancelAction)
                Button("Rename \(files.count) File\(files.count == 1 ? "" : "s")") { apply() }
                    .buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction)
                    .disabled(plan.contains { $0.problem != nil })
            }
        }
        .padding(18).padding(.top, 14)
        .onAppear { base = Self.suggestedBase(files) }
    }

    /// "IMG_2617.jpg, IMG_2618.jpg" → "IMG"; a single file keeps its name.
    static func suggestedBase(_ files: [URL]) -> String {
        let stems = files.map { $0.deletingPathExtension().lastPathComponent }
        guard var prefix = stems.first else { return "File" }
        for s in stems.dropFirst() { while !s.hasPrefix(prefix) { prefix.removeLast() } }
        let trimmed = prefix.trimmingCharacters(in: CharacterSet(charactersIn: " _-0123456789"))
        if !trimmed.isEmpty { return trimmed }
        return FileInspector.kind(of: files) == .image ? "Photo" : "File"
    }

    private func apply() {
        do {
            let pairs = try Renamer.apply(plan)
            Windows.close("rename")
            Runner.shared.didRename(pairs)
        } catch {
            HUD.shared.fail("Couldn't rename", error.localizedDescription, near: NSEvent.mouseLocation)
        }
    }
}

/// Reads the words in a picture and opens Apple's on-device translation panel for them.
struct TranslateView: View {
    let file: URL
    @ViewState private var text = ""
    @ViewState private var reading = true
    @ViewState private var showTranslation = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            EditorHeader(files: [file], note: "Translation runs on this Mac (language packs download once)")
            ZStack {
                TextEditor(text: $text).font(.body)
                    .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.secondary.opacity(0.25)))
                if reading { ProgressView("Reading the picture…") }
            }
            HStack {
                Button("Copy Text") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(text, forType: .string)
                }
                Spacer()
                Button("Close") { Windows.close("translate") }.keyboardShortcut(.cancelAction)
                translateButton
            }
        }
        .padding(18).padding(.top, 14)
        .task { await read() }
    }

    @ViewBuilder private var translateButton: some View {
        #if canImport(Translation)
        if #available(macOS 14.4, *) {
            Button("Translate") { showTranslation = true }
                .buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction)
                .disabled(text.isEmpty)
                .translationPresentation(isPresented: $showTranslation, text: text) { translated in text = translated }
        }
        #endif
    }

    private func read() async {
        let f = file
        let found = (try? await Engine.offload { try VisionOps.text(try ImageOps.load(f)) }) ?? ""
        text = found.isEmpty ? "No text found in this picture." : found
        reading = false
        if !found.isEmpty { showTranslation = true }
    }
}
