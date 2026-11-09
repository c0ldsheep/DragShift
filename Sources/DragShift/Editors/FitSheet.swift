import AppKit
import DragShiftCore
import SwiftUI

/// "Under ___ KB": one click on a size and it's done. Remembers the last size for Return
/// and for double-tap repeat.
struct FitSheet: View {
    let files: [URL]
    let temporary: Bool

    @AppStorage(Key.fitLast) private var last = 100
    @ViewState private var custom = ""
    @ViewState private var total: Int64 = 0

    private let hints: [Int: String] = [20: "signatures", 50: "photos", 100: "photos", 200: "documents", 500: "documents", 1000: "email"]

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            EditorHeader(files: files, note: "Now \(ByteFormat.string(total)) · makes a JPG or PDF copy")
            VStack(spacing: 10) {
                ForEach([Array(SizeLimit.presets.prefix(3)), Array(SizeLimit.presets.suffix(3))], id: \.self) { row in
                    HStack(spacing: 10) {
                        ForEach(row, id: \.self) { kb in chip(kb) }
                    }
                }
            }
            HStack(spacing: 8) {
                Text("Other size")
                TextField("75", text: $custom)
                    .textFieldStyle(.roundedBorder).frame(width: 70)
                    .onSubmit { if let kb = customKB { go(kb) } }
                Text("KB").foregroundStyle(.secondary)
                Spacer()
                Button("Go") { if let kb = customKB { go(kb) } }.disabled(customKB == nil)
            }
            Text("Keeps it as sharp as the size allows. It aims just under the limit (1 KB = 1,000 bytes), so it passes on every website, even ones that also ask for a minimum.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            HStack {
                Button("Cancel") { Windows.close("fit") }.keyboardShortcut(.cancelAction)
                Spacer()
                Button("Under \(SizeLimit.title(kb: last))") { go(last) }
                    .buttonStyle(HotButtonStyle()).keyboardShortcut(.defaultAction)
            }
        }
        .padding(18).padding(.top, 14)
        .task { total = files.reduce(0) { $0 + FileInspector.size(of: $1) } }
    }

    private var customKB: Int? {
        guard let n = Int(custom.trimmingCharacters(in: .whitespaces)), n >= 5, n <= 100_000 else { return nil }
        return n
    }

    /// Every file already smaller: picking it would do nothing, so it's shown as done.
    private func alreadyUnder(_ kb: Int) -> Bool {
        total > 0 && files.allSatisfy { FileInspector.size(of: $0) <= Int64(SizeLimit.bytes(kb: kb)) }
    }

    private func chip(_ kb: Int) -> some View {
        let done = alreadyUnder(kb)
        let picked = kb == last
        return Button { go(kb) } label: {
            VStack(spacing: 2) {
                Text(SizeLimit.title(kb: kb)).font(.system(size: 17, weight: .semibold)).monospacedDigit()
                Text(done ? "already under" : (hints[kb] ?? "")).font(.caption2)
                    .foregroundStyle(picked && !done ? Color.white.opacity(0.85) : Color.secondary)
            }
            .frame(maxWidth: .infinity, minHeight: 54)
            .foregroundStyle(picked && !done ? Palette.onHot : Color.primary)
            .background {
                if picked && !done {
                    RoundedRectangle(cornerRadius: 12).fill(Palette.hot)
                } else {
                    RoundedRectangle(cornerRadius: 12).fill(Color.primary.opacity(0.06))
                }
            }
            .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Color.primary.opacity(0.08)))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(done)
        .opacity(done ? 0.5 : 1)
    }

    private func go(_ kb: Int) {
        last = kb
        Windows.close("fit")
        Runner.shared.perform(SizeLimit.action(kb: kb), files, at: NSEvent.mouseLocation, temporary: temporary)
    }
}
