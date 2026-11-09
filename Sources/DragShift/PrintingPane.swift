import AppKit
import DragShiftCore
import SwiftUI

/// Settings → Printing: the saved print presets, DragShift's (edited here) and the ones saved in the macOS print window
/// (edited there). Switch one off to keep it out of the Print list and the ring's Print page, ★ one to put it in the
/// ring's Favorites (drop a file on it and it prints), or let Print print straight away with the first one.
struct PrintingPane: View {
    @ViewState private var presets = Prefs.printPresets
    @ViewState private var mac = SystemPresets.all
    @ViewState private var selected: String? = Prefs.printPresets.first?.id
    @ViewState private var favorites = Prefs.favorites
    @ViewState private var off = Prefs.hidden
    @AppStorage(Key.printQuick) private var quick = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Your print presets: the list in the Print window and the ring's Print page (drop a file on one and it prints). ★ one to put it in Favorites too.")
                .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            HStack(alignment: .top, spacing: 16) {
                VStack(alignment: .leading, spacing: 6) {
                    List(selection: $selected) {
                        Section("DragShift") {
                            ForEach(presets) { p in row(p) }
                                .onMove { from, to in presets.move(fromOffsets: from, toOffset: to); save() }
                        }
                        if !mac.isEmpty {
                            Section("From the macOS print window") {
                                ForEach(mac) { p in row(p) }
                            }
                        }
                    }
                    HStack(spacing: 6) {
                        Button { add() } label: { Image(systemName: "plus") }.help("New preset")
                        Button { remove() } label: { Image(systemName: "minus") }
                            .disabled(!presets.contains { $0.id == selected }).help("Delete the selected preset")
                        Spacer()
                        Button("Reset") { presets = PrintPreset.builtIns; selected = presets.first?.id; save() }
                    }
                    .controlSize(.small)
                }
                .frame(width: 240)
                if let i = presets.firstIndex(where: { $0.id == selected }) {
                    VStack(alignment: .leading, spacing: 12) {
                        HStack(spacing: 10) {
                            Text("Name").foregroundStyle(.secondary).frame(width: 64, alignment: .trailing)
                            TextField("Name", text: binding(i).name).textFieldStyle(.roundedBorder).frame(width: 230)
                        }
                        PrintOptions(preset: binding(i))
                        switches(presets[i]).padding(.leading, 74)
                    }
                } else if let p = mac.first(where: { $0.id == selected }) {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(p.name).font(.title3.weight(.semibold))
                        Text("Saved in the macOS print window for \(p.printer ?? "all printers"). It prints exactly as saved there: pages per sheet, orientation, quality and paper type.")
                            .fixedSize(horizontal: false, vertical: true)
                        Text("To change it, print from any app (or System dialog… in DragShift's Print window), set it up, then Presets → Save Current Settings as Preset under the same name.")
                            .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                        switches(p)
                    }
                    .frame(maxWidth: 320, alignment: .leading)
                } else {
                    Text("Pick a preset to change it.").foregroundStyle(.secondary)
                }
            }
            Divider()
            Toggle("Print straight away when I pick Print (uses the first preset shown, no window)", isOn: $quick)
            Text("Double-tap Shift on a file prints it again with whatever you used last.")
                .font(.caption).foregroundStyle(.secondary)
        }
        .padding()
    }

    private func row(_ p: PrintPreset) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 6) {
                Text(p.name).fontWeight(.semibold).lineLimit(1)
                if favorites.contains(p.stepID) {
                    Image(systemName: "star.fill").font(.caption).foregroundStyle(Palette.hotA)
                }
            }
            Text(off.contains(p.stepID) ? "Hidden from the Print list" : p.summary(defaultPrinter: Printer.defaultName))
                .font(.caption).foregroundStyle(.secondary).lineLimit(2)
        }
        .padding(.vertical, 2)
        .tag(p.id)
    }

    private func switches(_ p: PrintPreset) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Toggle(isOn: shownBinding(p)) { Label("In the Print list and on the ring", systemImage: "list.bullet") }
            Toggle(isOn: favoriteBinding(p)) { Label("In the ring's Favorites", systemImage: "star") }
        }
    }

    private func shownBinding(_ p: PrintPreset) -> Binding<Bool> {
        Binding(get: { !off.contains(p.stepID) }, set: { on in
            if on { off.remove(p.stepID) } else { off.insert(p.stepID) }
            Prefs.hidden = off
        })
    }

    private func binding(_ i: Int) -> Binding<PrintPreset> {
        Binding(get: { presets.indices.contains(i) ? presets[i] : PrintPreset.builtIns[0] },
                set: { if presets.indices.contains(i) { presets[i] = $0; save() } })
    }

    private func favoriteBinding(_ p: PrintPreset) -> Binding<Bool> {
        Binding(get: { favorites.contains(p.stepID) }, set: { on in
            favorites.removeAll { $0 == p.stepID }
            if on { favorites.append(p.stepID) }
            Prefs.favorites = favorites
        })
    }

    private func add() {
        var n = presets.first(where: { $0.id == selected }) ?? PrintPreset.builtIns[0]
        n.id = UUID().uuidString
        n.name = "My preset"
        presets.append(n)
        selected = n.id
        save()
    }

    private func remove() {
        guard let id = selected, let i = presets.firstIndex(where: { $0.id == id }) else { return }
        favorites.removeAll { $0 == presets[i].stepID }
        Prefs.favorites = favorites
        presets.remove(at: i)
        selected = presets.first?.id
        save()
    }

    private func save() { Prefs.printPresets = presets }
}
