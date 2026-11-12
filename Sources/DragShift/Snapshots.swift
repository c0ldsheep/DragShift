import AppKit
import DragShiftCore
import SwiftUI

/// Developer check: `DragShift --snapshot <folder> [file]` draws the ring, the bubble and the
/// windows to PNGs (no screen-recording permission needed), then quits.
@MainActor
enum Snapshots {
    static func run(into dir: URL, sample: URL?) {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let file = sample ?? Practice.file
        let model = RingModel()
        model.pages = Catalog.pages(for: [file])
        model.title = file.lastPathComponent
        model.subtitle = ByteFormat.string(FileInspector.size(of: file))
        model.thumbnail = NSImage(contentsOf: file)
        model.visible = true

        for (name, appearance) in [("light", NSAppearance.Name.aqua), ("dark", .darkAqua)] {
            for (page, hovered) in [(0, 1), (1, 3), (2, 0), (3, nil)] as [(Int, Int?)] where page < model.pages.count {
                model.page = page
                model.hovered = hovered
                save(RingView(model: model), size: G.size, appearance: appearance,
                     background: true, to: dir.appendingPathComponent("ring-\(name)-p\(page).png"))
            }
        }
        // The Print page: drop the file on a preset (DragShift's and the macOS ones) and it prints.
        model.pages = Catalog.pages(for: [file], hidden: Prefs.hidden, printPresets: Prefs.allPrintPresets)
        if let i = model.pages.firstIndex(where: { $0.id == "print" }) {
            model.page = i
            model.hovered = 4
            save(RingView(model: model), size: G.size, appearance: .aqua, background: true, to: dir.appendingPathComponent("ring-print.png"))
            save(RingView(model: model), size: G.size, appearance: .darkAqua, background: true, to: dir.appendingPathComponent("ring-print-dark.png"))
        }
        model.pages = Catalog.pages(for: [file], favorites: Catalog.defaultFavorites, recipes: Recipe.builtIns)
        model.mode = .pinned
        model.page = 0
        model.hovered = 2
        save(RingView(model: model), size: G.size, appearance: .aqua, background: true, to: dir.appendingPathComponent("ring-pinned-faves.png"))
        model.mode = .drag
        model.hovered = nil
        model.armed = model.pages[1].actions[0]
        save(RingView(model: model), size: G.size, appearance: .darkAqua, background: true, to: dir.appendingPathComponent("ring-armed.png"))
        model.armed = nil

        // "Your usual": brightest moment of the breath, on a slice and (when it's on another page) on a tab.
        PulseClock.frozen = 1
        model.pages = Catalog.pages(for: [file], favorites: Catalog.defaultFavorites, recipes: Recipe.builtIns)
        model.page = 0
        model.usual = model.pages[0].actions[3].id
        for (name, appearance) in [("light", NSAppearance.Name.aqua), ("dark", .darkAqua)] {
            save(RingView(model: model), size: G.size, appearance: appearance, background: true,
                 to: dir.appendingPathComponent("ring-usual-\(name).png"))
        }
        model.usual = model.pages.last?.actions.first?.id
        save(RingView(model: model), size: G.size, appearance: .aqua, background: true, to: dir.appendingPathComponent("ring-usual-tab.png"))
        model.usual = nil
        PulseClock.frozen = nil

        let shelf = FavoritesShelf.shared.model
        shelf.actions = Catalog.favoriteActions(Catalog.defaultFavorites, files: [file], recipes: Recipe.builtIns)
        shelf.expanded = true
        shelf.visible = true
        shelf.hovered = 1
        save(ShelfView(model: shelf), size: CGSize(width: ShelfG.width, height: ShelfG.height(shelf.actions.count)), appearance: .aqua,
             background: true, to: dir.appendingPathComponent("shelf.png"))

        let hud = HUD.shared
        hud.model.visible = true
        hud.model.state = .working
        hud.model.title = "Converting to JPG"
        hud.model.detail = "1 of 3 done"
        hud.model.progress = 0.4
        save(HUDView(model: hud.model, hud: hud), size: CGSize(width: 360, height: 96), appearance: .aqua, background: true,
             to: dir.appendingPathComponent("hud-working.png"))
        hud.model.state = .done
        hud.model.title = "Compressed"
        hud.model.detail = "Saved Page 001 Compressed.jpg · 1.2 MB → 310 KB (−74%)"
        hud.model.outputs = [file]
        save(HUDView(model: hud.model, hud: hud), size: CGSize(width: 360, height: 96), appearance: .darkAqua, background: true,
             to: dir.appendingPathComponent("hud-done.png"))
        // The longest title, beside Show and Undo: the bubble grows so it isn't cut off.
        hud.model.title = "Stitched into one long picture"
        hud.model.detail = "Saved Chat 1 Stitched.jpg"
        hud.model.width = HUD.width(for: hud.model)
        save(HUDView(model: hud.model, hud: hud), size: CGSize(width: hud.model.width, height: 96), appearance: .aqua, background: true,
             to: dir.appendingPathComponent("hud-long-title.png"))
        hud.model.width = CGFloat(BubbleLayout.normal)

        let opts = Prefs.options()
        save(CropEditor(files: [file], options: opts), size: CGSize(width: 640, height: 640), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("crop.png"), wait: 0.5)
        save(BackdropEditor(files: [file], options: opts), size: CGSize(width: 820, height: 600), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("backdrop.png"), wait: 1.5)
        save(ResizeSheet(files: [file], options: opts), size: CGSize(width: 380, height: 420), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("resize.png"), wait: 0.3)
        save(FitSheet(files: [file], temporary: false), size: CGSize(width: 420, height: 340), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("fit.png"), wait: 0.3)
        save(FitSheet(files: [file], temporary: false), size: CGSize(width: 420, height: 340), appearance: .darkAqua, background: false,
             to: dir.appendingPathComponent("fit-dark.png"), wait: 0.3)
        save(SavingPane(), size: CGSize(width: 640, height: 540), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("settings-saving.png"), wait: 0.3)
        save(TriggersPane(), size: CGSize(width: 640, height: 540), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("settings-triggers.png"), wait: 0.3)
        // About, at the size the tab really gets (the window is 640 x 540 with the tab bar on top).
        for (name, look) in [("light", NSAppearance.Name.aqua), ("dark", .darkAqua)] {
            save(AboutPane(fixed: 214), size: CGSize(width: 640, height: 480), appearance: look, background: false,
                 to: dir.appendingPathComponent("about-\(name).png"), wait: 0.3)
            save(AboutPane(fixed: 214, frozenCup: (CGSize(width: 2, height: 62), true)), size: CGSize(width: 640, height: 480),
                 appearance: look, background: false, to: dir.appendingPathComponent("about-ring-\(name).png"), wait: 0.3)
            save(TipCard(size: Tip.sizes[1]), size: CGSize(width: 260, height: 360), appearance: look, background: false,
                 to: dir.appendingPathComponent("tip-card-\(name).png"), wait: 0.3)
        }
        save(AboutPane(fixed: 0), size: CGSize(width: 640, height: 480), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("about-new.png"), wait: 0.3)
        save(PasswordSheet(files: [file], lock: true, options: opts), size: CGSize(width: 380, height: 250), appearance: .aqua,
             background: false, to: dir.appendingPathComponent("password.png"), wait: 0.2)
        for (name, typed) in [("weak", "15081995"), ("fair", "mango4Tree"), ("strong", "mango4Tree!rain")] {
            save(PasswordSheet(files: [file], lock: true, options: opts, typed: typed), size: CGSize(width: 380, height: 270),
                 appearance: .aqua, background: false, to: dir.appendingPathComponent("password-\(name).png"), wait: 0.2)
        }
        save(RedactEditor(file: file, options: opts), size: CGSize(width: 860, height: 640), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("redact.png"), wait: 3)
        let demo = RingModel()
        demo.pages = Catalog.pages(for: [URL(fileURLWithPath: "/tmp/Photo.heic")])
        demo.title = "Photo.heic"; demo.subtitle = "2.4 MB"; demo.visible = true; demo.hovered = 2
        save(WelcomeView(demo: demo, practice: Practice.file), size: CGSize(width: 820, height: 600), appearance: .aqua,
             background: false, to: dir.appendingPathComponent("welcome.png"), wait: 0.3)
        save(FavoritesPane(), size: CGSize(width: 640, height: 540), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("settings-favorites.png"), wait: 0.3)
        // Print: the short list first (light and dark), then all the options, and a 300 dpi passport photo on a 4×6.
        let quick = PrintSheet.quickSize(rows: PrintSheet.quickRows)
        save(PrintSheet(files: [file], temporary: false), size: quick, appearance: .aqua, background: false,
             to: dir.appendingPathComponent("print.png"), wait: 0.4)
        save(PrintSheet(files: [file], temporary: false), size: quick, appearance: .darkAqua, background: false,
             to: dir.appendingPathComponent("print-dark.png"), wait: 0.4)
        save(PrintSheet(files: [file], temporary: false, start: PrintPreset.builtIns[1], full: true), size: PrintSheet.fullSize,
             appearance: .aqua, background: false, to: dir.appendingPathComponent("print-full.png"), wait: 2)
        if let photo = Self.passportSample(in: dir) {
            save(PrintSheet(files: [photo], temporary: false, start: PrintPreset.builtIns[2], full: true), size: PrintSheet.fullSize,
                 appearance: .aqua, background: false, to: dir.appendingPathComponent("print-passport.png"), wait: 2)
            try? FileManager.default.removeItem(at: photo)
        }
        save(PrintSheet(files: [file], temporary: false, choosing: true), size: PrintSheet.quickSize(rows: Prefs.allPrintPresets.count),
             appearance: .aqua, background: false, to: dir.appendingPathComponent("print-choose.png"), wait: 0.4)
        if let mac = SystemPresets.all.first {
            save(PrintSheet(files: [file], temporary: false, start: mac, full: true), size: PrintSheet.fullSize,
                 appearance: .aqua, background: false, to: dir.appendingPathComponent("print-mac-preset.png"), wait: 2)
        }
        save(PrintingPane(), size: CGSize(width: 640, height: 540), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("settings-printing.png"), wait: 0.3)
        // Lots of presets: the buttons wrap instead of running off the edge.
        let saved = Prefs.d.data(forKey: Key.printPresets)
        Prefs.printPresets = PrintPreset.builtIns + ["Office laser", "Mini printer labels", "Photo 5×7 glossy", "Draft"].map {
            PrintPreset(name: $0, color: false)
        }
        save(PrintSheet(files: [file], temporary: false), size: PrintSheet.quickSize(rows: PrintSheet.quickRows),
             appearance: .aqua, background: false, to: dir.appendingPathComponent("print-many.png"), wait: 0.4)
        Prefs.d.set(saved, forKey: Key.printPresets)
        save(RecipesPane(), size: CGSize(width: 640, height: 540), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("settings-recipes.png"), wait: 0.8)
        save(WatchPane(), size: CGSize(width: 640, height: 540), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("settings-watch.png"), wait: 0.3)
        save(RenameSheet(files: [file, file.deletingLastPathComponent().appendingPathComponent("IMG_2618.jpg")]),
             size: CGSize(width: 560, height: 520), appearance: .aqua, background: false, to: dir.appendingPathComponent("rename.png"), wait: 0.3)
        save(AnnotateEditor(file: file, options: opts), size: CGSize(width: 860, height: 680), appearance: .aqua, background: false,
             to: dir.appendingPathComponent("annotate.png"), wait: 0.5)
        // Annotation check: an arrow pointing at the top-left, a box, text and step numbers.
        if let img = try? ImageOps.load(file, maxPixel: 800) {
            let w = CGFloat(img.width) * 0.008
            let marks = [
                Mark(tool: .arrow, color: .systemRed, width: w, points: [CGPoint(x: 400, y: 300), CGPoint(x: 120, y: 100)]),
                Mark(tool: .box, color: .systemYellow, width: w, points: [CGPoint(x: 450, y: 450), CGPoint(x: 700, y: 650)]),
                Mark(tool: .highlight, color: .systemYellow, width: w, points: [CGPoint(x: 60, y: 600), CGPoint(x: 380, y: 680)]),
                Mark(tool: .text, color: .white, width: w, points: [CGPoint(x: 80, y: 520)], text: "Top text reads normally"),
                Mark(tool: .number, color: .systemBlue, width: w, points: [CGPoint(x: 600, y: 120)], text: "1"),
                Mark(tool: .pen, color: .systemGreen, width: w, points: (0..<40).map { CGPoint(x: 100 + Double($0) * 8, y: 420 + sin(Double($0) / 3) * 30) }),
            ]
            if let out = try? AnnotateEditor.render(marks, on: img) {
                try? ImageOps.write(out, to: dir.appendingPathComponent("annotate-render.png"), format: .png, quality: 1)
            }
        }
        print("snapshots in \(dir.path)")
    }

    /// Developer check: `DragShift --cup-drag-check` holds the About tab's coffee cup with a made-up mouse, moves it
    /// the way a hand does and reads from the drawn picture where the cup really is after every move. A cup that
    /// shakes shows up as a cup that isn't under the pointer.
    static func cupDragCheck() -> (ok: Bool, text: String) {
        let size = CGSize(width: 640, height: 480)
        let window = NSWindow(contentRect: NSRect(origin: NSPoint(x: -5000, y: -5000), size: size), styleMask: .borderless,
                              backing: .buffered, defer: false)
        window.appearance = NSAppearance(named: .aqua)
        let host = NSHostingView(rootView: AboutPane(fixed: 214).frame(width: size.width, height: size.height))
        host.frame = NSRect(origin: .zero, size: size)
        window.contentView = host
        window.orderFrontRegardless()
        defer { window.orderOut(nil) }
        func wait(_ seconds: Double) { RunLoop.main.run(until: Date().addingTimeInterval(seconds)) }

        /// The middle of everything gold in the picture, in points from the top-left.
        func cup() -> CGPoint? {
            host.layoutSubtreeIfNeeded()
            guard let rep = host.bitmapImageRepForCachingDisplay(in: host.bounds) else { return nil }
            host.cacheDisplay(in: host.bounds, to: rep)
            guard let px = rep.bitmapData, rep.samplesPerPixel >= 3 else { return nil }
            let first = rep.bitmapFormat.contains(.alphaFirst) ? 1 : 0, step = rep.bitsPerPixel / 8
            var sx = 0.0, sy = 0.0, n = 0.0
            for y in 0..<rep.pixelsHigh {
                let row = px + y * rep.bytesPerRow
                for x in 0..<rep.pixelsWide {
                    let r = Int(row[x * step + first]), g = Int(row[x * step + first + 1]), b = Int(row[x * step + first + 2])
                    if r > 215, g > 180, b < 120 { sx += Double(x); sy += Double(y); n += 1 }
                }
            }
            let scale = Double(rep.pixelsWide) / Double(size.width)
            return n > 200 ? CGPoint(x: sx / n / scale, y: sy / n / scale) : nil
        }

        var number = 0
        func mouse(_ type: NSEvent.EventType, _ p: CGPoint) {
            number += 1
            guard let e = NSEvent.mouseEvent(with: type, location: NSPoint(x: p.x, y: size.height - p.y), modifierFlags: [],
                                             timestamp: ProcessInfo.processInfo.systemUptime, windowNumber: window.windowNumber,
                                             context: nil, eventNumber: number, clickCount: 1,
                                             pressure: type == .leftMouseUp ? 0 : 1) else { return }
            window.sendEvent(e)
            wait(0.04)
        }

        wait(0.5)
        guard let home = cup() else { return (false, "cup drag: no cup found in the About tab") }
        // Out toward the lower right, back again, then a quick left-right shake.
        var path: [CGPoint] = (1...30).map { CGPoint(x: home.x + Double($0) * 1.5, y: home.y + Double($0) * 1.5) }
        path += (0..<30).reversed().map { CGPoint(x: home.x + Double($0) * 1.5, y: home.y + Double($0) * 1.5) }
        path += (1...24).map { CGPoint(x: home.x + ($0 % 2 == 0 ? 18 : -18), y: home.y + 30) }
        mouse(.leftMouseDown, home)
        var worst = 0.0, moved = 0.0, slack = 0.0, started = false
        for p in path {
            mouse(.leftMouseDragged, p)
            guard let seen = cup() else { return (false, "cup drag: the cup vanished while it was held") }
            let want = CoffeeCorner.held(CGSize(width: p.x - home.x, height: p.y - home.y))
            // The first few points of movement are the pause before a press counts as a drag (so a click stays a
            // click). From the moment the cup moves, it must sit exactly under the pointer.
            if !started, hypot(seen.x - home.x, seen.y - home.y) < 0.5 { slack = hypot(p.x - home.x, p.y - home.y); continue }
            started = true
            worst = max(worst, hypot(seen.x - home.x - want.width, seen.y - home.y - want.height))
            if CommandLine.arguments.contains("--verbose") {
                print(String(format: "  pointer %+6.1f %+6.1f   cup %+6.1f %+6.1f", p.x - home.x, p.y - home.y, seen.x - home.x, seen.y - home.y))
            }
            moved = max(moved, hypot(seen.x - home.x, seen.y - home.y))
        }
        mouse(.leftMouseUp, path[path.count - 1])
        wait(1)
        let back = cup().map { hypot($0.x - home.x, $0.y - home.y) } ?? 99
        let ok = moved > 40 && slack <= 6 && worst <= 1 && back <= 1
        return (ok, String(format: "cup drag: %d moves, started after %.1f pt, went %.0f pt from its saucer, never more than "
                           + "%.1f pt from the pointer, came back to within %.1f pt: %@", path.count, slack, moved, worst, back,
                           ok ? "steady" : "NOT steady"))
    }

    /// A 3.5 × 4.5 cm "passport photo" at 300 dpi, for the print snapshot.
    private static func passportSample(in dir: URL) -> URL? {
        guard let ctx = CGContext(data: nil, width: 413, height: 531, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return nil }
        ctx.setFillColor(CGColor(gray: 0.98, alpha: 1)); ctx.fill(CGRect(x: 0, y: 0, width: 413, height: 531))
        ctx.setFillColor(CGColor(red: 0.18, green: 0.30, blue: 0.48, alpha: 1)); ctx.fillEllipse(in: CGRect(x: 40, y: -160, width: 333, height: 330))
        ctx.setFillColor(CGColor(red: 0.79, green: 0.55, blue: 0.39, alpha: 1)); ctx.fillEllipse(in: CGRect(x: 126, y: 190, width: 160, height: 210))
        ctx.setFillColor(CGColor(red: 0.11, green: 0.08, blue: 0.06, alpha: 1)); ctx.fillEllipse(in: CGRect(x: 118, y: 320, width: 176, height: 110))
        guard let img = ctx.makeImage(), let data = try? SizeLimit.jpeg(img, maxBytes: 50_000, dpi: 300) else { return nil }
        let url = dir.appendingPathComponent("Sample Passport.jpg")
        return (try? data.write(to: url)) != nil ? url : nil
    }

    private static func save<V: View>(_ view: V, size: CGSize, appearance: NSAppearance.Name, background: Bool, to url: URL,
                                      wait: Double = 0.15) {
        let window = NSWindow(contentRect: NSRect(origin: NSPoint(x: -5000, y: -5000), size: size), styleMask: .borderless,
                              backing: .buffered, defer: false)
        window.appearance = NSAppearance(named: appearance)
        let host = NSHostingView(rootView: view.frame(width: size.width, height: size.height))
        host.frame = NSRect(origin: .zero, size: size)
        let root = NSView(frame: host.frame)
        if background {
            let bg = NSImageView(frame: root.bounds)
            bg.image = wallpaper(size)
            bg.imageScaling = .scaleAxesIndependently
            root.addSubview(bg)
        } else {
            root.wantsLayer = true
            root.appearance = NSAppearance(named: appearance)
            // Resolve the window colour in the snapshot's own appearance (dark stays dark).
            NSAppearance(named: appearance)?.performAsCurrentDrawingAppearance {
                root.layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor
            }
        }
        root.addSubview(host)
        window.contentView = root
        window.orderFrontRegardless()
        RunLoop.main.run(until: Date().addingTimeInterval(wait))
        root.layoutSubtreeIfNeeded()
        guard let rep = root.bitmapImageRepForCachingDisplay(in: root.bounds) else { return }
        root.cacheDisplay(in: root.bounds, to: rep)
        try? rep.representation(using: .png, properties: [:])?.write(to: url)
        window.orderOut(nil)
    }

    /// A stand-in desktop so glass and shadows read the way they will on screen.
    private static func wallpaper(_ size: CGSize) -> NSImage {
        NSImage(size: size, flipped: false) { r in
            NSGradient(colors: [NSColor(srgbRed: 0.98, green: 0.62, blue: 0.3, alpha: 1),
                                NSColor(srgbRed: 0.93, green: 0.3, blue: 0.4, alpha: 1),
                                NSColor(srgbRed: 0.5, green: 0.45, blue: 0.85, alpha: 1)])?.draw(in: r, angle: -60)
            return true
        }
    }
}
