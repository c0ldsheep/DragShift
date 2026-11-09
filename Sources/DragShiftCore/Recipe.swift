import Foundation

/// A one-drop chain of steps, e.g. "Shop Ready" = Enhance → Shop Photo.
/// Each step's result feeds the next; only the final file is kept, named after the recipe.
public struct Recipe: Codable, Hashable, Identifiable, Sendable {
    public var id: String
    public var name: String
    public var symbol: String
    public var steps: [String]

    public init(id: String = "recipe.\(UUID().uuidString.prefix(8).lowercased())", name: String, symbol: String = "wand.and.stars",
                steps: [String]) {
        self.id = id; self.name = name; self.symbol = symbol; self.steps = steps
    }

    /// What the first step works on decides which files the recipe shows up for.
    public var kinds: Set<FileKind> {
        guard let first = steps.first else { return [] }
        if SizeLimit.kb(fromStep: first) != nil { return [.image, .pdf] }
        switch first.split(separator: ".").first.map(String.init) ?? "" {
        case "img": return [.image]
        case "pdf": return [.pdf]
        case "doc": return [.document]
        case "media": return first.hasPrefix("media.to.m") || first == "media.compress" ? [.video, .audio] : [.video]
        default: return Set(FileKind.allCases)
        }
    }

    public var action: Action {
        Action(id, name, symbol, verb: "Running \(name)")
    }

    /// Steps that turn many files into one (so the chain runs once for the whole group).
    static let manyToOne: Set<String> = ["img.to.pdf", "pdf.merge", "mixed.merge", "img.gif", "file.zip"]

    public static let builtIns: [Recipe] = [
        Recipe(id: "recipe.shop-ready", name: "Shop Ready", symbol: "bag", steps: ["img.enhance", "img.shop"]),
        Recipe(id: "recipe.insta-post", name: "Insta Post", symbol: "camera.aperture", steps: ["img.fill.1080x1350", "img.to.jpg"]),
        Recipe(id: "recipe.insta-story", name: "Insta Story", symbol: "rectangle.portrait", steps: ["img.fit.1080x1920"]),
        Recipe(id: "recipe.whatsapp", name: "WhatsApp Small", symbol: "paperplane", steps: ["img.size.1600", "img.compress"]),
        Recipe(id: "recipe.web", name: "Web Image", symbol: "globe", steps: ["img.size.2048", "img.to.webp"]),
        Recipe(id: "recipe.watermark", name: "Watermark", symbol: "signature", steps: ["img.watermark", "img.to.jpg"]),
        Recipe(id: "recipe.email-pdf", name: "Email PDF", symbol: "envelope.badge", steps: ["img.to.pdf", "pdf.compress"]),
    ]
}

/// Everything a recipe step can be, grouped for the recipe editor.
public enum RecipeSteps {
    public struct Step: Identifiable, Hashable, Sendable {
        public let id: String
        public let title: String
        public let group: String
    }

    public static let all: [Step] = {
        var s: [Step] = []
        func add(_ group: String, _ items: [(String, String)]) { s += items.map { Step(id: $0.0, title: $0.1, group: group) } }
        add("Image", [
            ("img.enhance", "Enhance"), ("img.passport", "Passport photo"), ("img.removebg", "Cut Out (remove background)"), ("img.shop", "Shop Photo (white square)"),
            ("img.clean", "Clean Info (remove location)"), ("img.watermark", "Watermark"), ("img.rotate", "Rotate right"),
            ("img.flip", "Flip"), ("img.size.1080", "Resize to 1080 px"), ("img.size.1600", "Resize to 1600 px"),
            ("img.size.2048", "Resize to 2048 px"), ("img.size.3840", "Resize to 3840 px"),
            ("img.fill.1080x1350", "Crop to 4:5 (1080×1350)"), ("img.fill.1080x1080", "Crop to square (1080×1080)"),
            ("img.fill.1920x1080", "Crop to 16:9 (1920×1080)"), ("img.fit.1080x1920", "Fit in 9:16 story (blurred edges)"),
            ("img.compress", "Compress"), ("img.to.jpg", "Save as JPG"), ("img.to.png", "Save as PNG"),
            ("img.to.heic", "Save as HEIC"), ("img.to.webp", "Save as WebP"), ("img.to.avif", "Save as AVIF"),
            ("img.to.pdf", "Put in a PDF"),
        ])
        add("Size limit (pictures and PDFs)", SizeLimit.presets.map { (SizeLimit.stepID(kb: $0), "Under \(SizeLimit.title(kb: $0))") })
        add("PDF", [("pdf.compress", "Compress"), ("pdf.searchable", "Make searchable (OCR)"), ("pdf.rotate", "Rotate pages"),
                    ("pdf.to.jpg", "Pages to JPG"), ("pdf.to.docx", "To Word")])
        add("Video", [("media.compress", "Compress"), ("media.vertical", "Crop to 9:16"), ("media.square", "Crop to 1:1"),
                      ("media.speed.2", "Speed 2×"), ("media.mute", "Mute"), ("media.to.mp4", "Save as MP4"),
                      ("media.to.gif", "Save as GIF"), ("media.to.m4a", "Audio only (M4A)")])
        add("Any file", [("file.zip", "Zip")])
        return s
    }()

    public static func title(_ id: String) -> String {
        if let t = all.first(where: { $0.id == id })?.title { return t }
        if let kb = SizeLimit.kb(fromStep: id) { return "Under \(SizeLimit.title(kb: kb))" }
        return id
    }
}
