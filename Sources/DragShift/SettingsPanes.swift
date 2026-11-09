import AppKit
import DragShiftCore
import SwiftUI

/// Every action and recipe, with a group name, for pickers.
enum ActionDirectory {
    static func group(_ id: String) -> String {
        switch id.split(separator: ".").first.map(String.init) ?? "" {
        case "img": return "Images"
        case "pdf": return "PDF"
        case "doc": return "Documents"
        case "media": return "Video & Audio"
        case "folder": return "Folders"
        case "recipe": return "Recipes"
        case "print": return "Printing"
        case "mixed": return "Mixed files"
        default: return "Any file"
        }
    }

    /// For a favourite: every file type it works on ("Images · PDF · Folders" for JPG).
    static func covers(_ id: String) -> String {
        guard let fam = Catalog.families.first(where: { $0.contains(id) }) else { return group(id) }
        var seen: [String] = []
        for f in fam { let g = group(f); if !seen.contains(g) { seen.append(g) } }
        return seen.joined(separator: " · ")
    }

    static var all: [Action] { Catalog.everyAction + Prefs.recipes.map(\.action) + Prefs.allPrintPresets.map(\.action) }
    static func find(_ id: String) -> Action? { all.first { $0.id == id } }
    static var groups: [String] { ["Images", "PDF", "Documents", "Video & Audio", "Folders", "Any file", "Recipes", "Printing"] }
}

// MARK: Favorites

struct FavoritesPane: View {
    @ViewState private var favorites = Prefs.favorites
    @AppStorage(Key.shelf) private var shelf = "right"
    @ViewState private var showHidden = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Your go-to actions. They're the first page of the ring (★ Faves) and the shelf at the screen edge. Only the ones that fit the file you're dragging show up, and one “Compress” covers photos, PDFs and videos.")
                .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            List {
                ForEach(favorites, id: \.self) { id in
                    HStack {
                        Image(systemName: ActionDirectory.find(id)?.symbol ?? "questionmark").frame(width: 22)
                        Text(ActionDirectory.find(id)?.title ?? id)
                        Text(ActionDirectory.covers(id)).font(.caption).foregroundStyle(.secondary)
                        Spacer()
                        Button { remove(id) } label: { Image(systemName: "minus.circle.fill").foregroundStyle(.red) }
                            .buttonStyle(.plain)
                    }
                }
                .onMove { from, to in favorites.move(fromOffsets: from, toOffset: to); Prefs.favorites = favorites }
            }
            .frame(minHeight: 200)
            HStack {
                Menu("Add a Favorite") {
                    ForEach(ActionDirectory.groups, id: \.self) { g in
                        Menu(g) {
                            ForEach(ActionDirectory.all.filter { ActionDirectory.group($0.id) == g && !favorites.contains($0.id) }) { a in
                                Button(a.title) { favorites.append(a.id); Prefs.favorites = favorites }
                            }
                        }
                    }
                }
                .fixedSize()
                Button("Reset") { favorites = Catalog.defaultFavorites; Prefs.favorites = favorites }
                Spacer()
                Text("Tip: right-click a slice in the ring to ★ it.").font(.caption).foregroundStyle(.secondary)
            }
            Divider()
            Picker("Favorites shelf while dragging", selection: $shelf) {
                Text("Right edge").tag("right")
                Text("Left edge").tag("left")
                Text("Off").tag("off")
            }
            .pickerStyle(.segmented)
            Text("Push a dragged file against that edge and your favorites slide out. Drop it on one.")
                .font(.caption).foregroundStyle(.secondary)
            DisclosureGroup("Hide slices you never use", isExpanded: $showHidden) {
                HiddenSlicesListWrapper().frame(height: 200)
            }
        }
        .padding()
    }

    private func remove(_ id: String) {
        favorites.removeAll { $0 == id }
        Prefs.favorites = favorites
    }
}

// MARK: Recipes

struct RecipesPane: View {
    @ViewState private var recipes = Prefs.recipes
    @ViewState private var selected: String?
    @ViewState private var watermark = Prefs.watermark
    @ViewState private var preview: NSImage?

    private let symbols = ["wand.and.stars", "bag", "camera.aperture", "rectangle.portrait", "paperplane", "globe", "signature",
                           "envelope.badge", "sparkles", "bolt", "heart", "star", "photo", "film"]

    var body: some View {
        HStack(alignment: .top, spacing: 14) {
            VStack(alignment: .leading, spacing: 6) {
                List(selection: $selected) {
                    ForEach(recipes) { r in
                        Label(r.name, systemImage: r.symbol).tag(Optional(r.id))
                    }
                }
                .frame(width: 190)
                HStack {
                    Button { add() } label: { Image(systemName: "plus") }
                    Button { delete() } label: { Image(systemName: "minus") }.disabled(selected == nil)
                    Spacer()
                    Button("Reset") { recipes = Recipe.builtIns; save() }.controlSize(.small)
                }
            }
            if let i = recipes.firstIndex(where: { $0.id == selected }) {
                editor(i)
            } else {
                VStack(alignment: .leading, spacing: 10) {
                    Text("A recipe runs several steps with one drop: Shop Ready = Enhance → white-background Shop Photo.")
                        .foregroundStyle(.secondary)
                    Text("Pick one on the left to change it, or press + to make your own. Recipes show up in ★ Faves when you favorite them.")
                        .foregroundStyle(.secondary)
                    watermarkBox
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .padding()
        .onAppear { renderPreview() }
    }

    private func editor(_ i: Int) -> some View {
        let r = recipes[i]
        return VStack(alignment: .leading, spacing: 10) {
            HStack {
                Menu { ForEach(symbols, id: \.self) { s in Button { recipes[i].symbol = s; save() } label: { Image(systemName: s) } } }
                    label: { Image(systemName: r.symbol) }
                    .fixedSize()
                TextField("Name", text: Binding(get: { recipes[i].name }, set: { recipes[i].name = $0; save() }))
                    .textFieldStyle(.roundedBorder)
                Toggle("★", isOn: Binding(get: { Prefs.favorites.contains(r.id) }, set: { _ in _ = Prefs.toggleFavorite(r.id) }))
                    .toggleStyle(.button).help("Show in ★ Faves and on the shelf")
            }
            Text("Steps").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
            List {
                ForEach(Array(r.steps.enumerated()), id: \.offset) { k, step in
                    HStack {
                        Text("\(k + 1).").foregroundStyle(.secondary).monospacedDigit()
                        Text(RecipeSteps.title(step))
                        Spacer()
                        Button { recipes[i].steps.remove(at: k); save() } label: {
                            Image(systemName: "minus.circle.fill").foregroundStyle(.red)
                        }.buttonStyle(.plain)
                    }
                }
                .onMove { from, to in recipes[i].steps.move(fromOffsets: from, toOffset: to); save() }
            }
            .frame(minHeight: 150)
            Menu("Add a Step") {
                ForEach(["Image", "PDF", "Video", "Any file"], id: \.self) { g in
                    Menu(g) {
                        ForEach(RecipeSteps.all.filter { $0.group == g }) { st in
                            Button(st.title) { recipes[i].steps.append(st.id); save() }
                        }
                    }
                }
            }
            .fixedSize()
            Text("Works on: \(r.kinds.map(\.label).sorted().joined(separator: ", "))").font(.caption).foregroundStyle(.secondary)
            if r.steps.contains("img.watermark") { watermarkBox }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var watermarkBox: some View {
        GroupBox("Watermark") {
            HStack(alignment: .top, spacing: 12) {
                VStack(alignment: .leading, spacing: 8) {
                    TextField("Text (e.g. your shop name)", text: $watermark.text).textFieldStyle(.roundedBorder)
                    HStack {
                        Button(watermark.logoPath == nil ? "Add Logo…" : "Change Logo…") { pickLogo() }
                        if watermark.logoPath != nil { Button("Remove Logo") { watermark.logoPath = nil } }
                    }
                    .controlSize(.small)
                    Picker("Where", selection: $watermark.position) {
                        ForEach(WatermarkStyle.Position.allCases, id: \.self) { Text($0.title).tag($0) }
                    }
                    .fixedSize()
                    HStack { Text("Size").font(.caption); Slider(value: $watermark.size, in: 0.02...0.12) }
                    HStack { Text("Strength").font(.caption); Slider(value: $watermark.opacity, in: 0.2...1) }
                }
                if let preview {
                    Image(nsImage: preview).resizable().aspectRatio(contentMode: .fit).frame(width: 130, height: 100)
                        .clipShape(RoundedRectangle(cornerRadius: 6))
                }
            }
        }
        .onChange(of: watermark) { w in Prefs.watermark = w; renderPreview() }
    }

    private func renderPreview() {
        let w = watermark
        Task {
            let img = try? await Engine.offload { () -> NSImage? in
                let src = try ImageOps.load(Practice.file, maxPixel: 400)
                let out = ImageOps.watermarked(src, style: w)
                return NSImage(cgImage: out, size: NSSize(width: out.width, height: out.height))
            }
            preview = img ?? nil
        }
    }

    private func pickLogo() {
        let p = NSOpenPanel()
        p.allowedContentTypes = [.image]
        if p.runModal() == .OK, let u = p.url { watermark.logoPath = u.path }
    }

    private func add() {
        let r = Recipe(name: "My Recipe", steps: ["img.enhance", "img.to.jpg"])
        recipes.append(r)
        selected = r.id
        save()
        _ = Prefs.toggleFavorite(r.id)
    }

    private func delete() {
        guard let id = selected else { return }
        recipes.removeAll { $0.id == id }
        if Prefs.favorites.contains(id) { _ = Prefs.toggleFavorite(id) }
        selected = nil
        save()
    }

    private func save() { Prefs.recipes = recipes }
}

// MARK: Watch folders

struct WatchPane: View {
    @ViewState private var rules = Prefs.watchRules
    @ViewState private var folder = Naming.downloads.path
    @ViewState private var match: WatchRule.Match = .heic
    @ViewState private var actionID = "img.to.jpg"
    @ViewState private var trash = false

    private var choices: [Action] {
        let base = Catalog.pages(for: [match.sample]).flatMap(\.actions).filter { $0.editor == nil && !$0.isShare }
        let recipes = Prefs.recipes.filter { $0.kinds.contains(FileInspector.kind(of: match.sample)) }.map(\.action)
        return base + recipes
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("When a file lands in a folder, DragShift handles it by itself. Example: every HEIC photo AirDropped into Downloads becomes a JPG.")
                .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            List {
                if rules.isEmpty { Text("No folders watched yet.").foregroundStyle(.secondary) }
                ForEach($rules) { $r in
                    HStack {
                        Toggle("", isOn: $r.enabled).labelsHidden()
                        VStack(alignment: .leading) {
                            Text("\(r.match.title) in \(URL(fileURLWithPath: r.folder).lastPathComponent) → \(r.actionTitle)")
                            Text(r.trashOriginal ? "Original goes to the Bin" : "Original is kept").font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button { rules.removeAll { $0.id == r.id }; Prefs.watchRules = rules } label: {
                            Image(systemName: "minus.circle.fill").foregroundStyle(.red)
                        }.buttonStyle(.plain)
                    }
                }
            }
            .frame(minHeight: 150)
            .onChange(of: rules) { r in Prefs.watchRules = r }
            GroupBox("New rule") {
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        Text("Folder").frame(width: 60, alignment: .leading)
                        Text(URL(fileURLWithPath: folder).lastPathComponent).lineLimit(1)
                        Spacer()
                        Button("Choose…") { chooseFolder() }
                    }
                    Picker("When", selection: $match) { ForEach(WatchRule.Match.allCases, id: \.self) { Text($0.title).tag($0) } }
                        .onChange(of: match) { _ in actionID = choices.first?.id ?? "" }
                    Picker("Do", selection: $actionID) { ForEach(choices) { Text($0.title).tag($0.id) } }
                    Toggle("Move the original to the Bin afterwards", isOn: $trash)
                    HStack {
                        Spacer()
                        Button("Add Rule") { addRule() }.buttonStyle(HotButtonStyle()).disabled(actionID.isEmpty)
                    }
                }
                .padding(4)
            }
        }
        .padding()
    }

    private func chooseFolder() {
        let p = NSOpenPanel()
        p.canChooseDirectories = true
        p.canChooseFiles = false
        p.prompt = "Watch This Folder"
        if p.runModal() == .OK, let u = p.url { folder = u.path }
    }

    private func addRule() {
        let title = choices.first { $0.id == actionID }?.title ?? actionID
        rules.append(WatchRule(folder: folder, match: match, actionID: actionID, actionTitle: title, trashOriginal: trash))
        Prefs.watchRules = rules
    }
}
