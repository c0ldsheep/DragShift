import AppKit
import CoreText
import Foundation

/// Word, RTF, ODT, HTML, Markdown and plain text, via AppKit's own document readers and writers.
public enum DocFormat: String, Sendable, CaseIterable {
    case pdf, docx, doc, rtf, odt, html, txt, md

    public var title: String { rawValue.uppercased() }
    public var ext: String { rawValue }

    public static func from(ext: String) -> DocFormat? {
        switch ext.lowercased() {
        case "docx": return .docx
        case "doc": return .doc
        case "rtf", "rtfd": return .rtf
        case "odt": return .odt
        case "html", "htm", "webarchive": return .html
        case "txt", "text": return .txt
        case "md", "markdown": return .md
        case "pdf": return .pdf
        default: return nil
        }
    }

    var documentType: NSAttributedString.DocumentType? {
        switch self {
        case .docx: return .officeOpenXML
        case .doc: return .docFormat
        case .rtf: return .rtf
        case .odt: return .openDocument
        case .html: return .html
        case .txt, .md: return .plain
        case .pdf: return nil
        }
    }
}

public enum DocOps {
    static let bodyFont = NSFont.systemFont(ofSize: 12)

    public static func plain(_ s: String) -> NSAttributedString {
        NSAttributedString(string: s, attributes: [.font: bodyFont, .foregroundColor: NSColor.black])
    }

    /// Reads any supported document. Must run on the main thread (AppKit's HTML reader needs it).
    @MainActor
    public static func read(_ url: URL) throws -> NSAttributedString {
        let ext = url.pathExtension.lowercased()
        if ext == "md" || ext == "markdown" {
            let md = try String(contentsOf: url, encoding: .utf8)
            return try html(Markdown.html(md))
        }
        if ext == "txt" || ext == "text" {
            let s = try (try? String(contentsOf: url, encoding: .utf8)) ?? String(contentsOf: url, encoding: .isoLatin1)
            return plain(s)
        }
        do {
            return try NSAttributedString(url: url, options: [:], documentAttributes: nil)
        } catch {
            throw DragShiftError("Couldn't read \(url.lastPathComponent).")
        }
    }

    @MainActor
    static func html(_ html: String) throws -> NSAttributedString {
        let styled = """
        <html><head><meta charset="utf-8"><style>
        body { font-family: -apple-system, Helvetica; font-size: 12pt; color: #111; }
        code, pre { font-family: Menlo, monospace; font-size: 10.5pt; }
        </style></head><body>\(html)</body></html>
        """
        guard let data = styled.data(using: .utf8),
              let a = try? NSAttributedString(data: data, options: [.documentType: NSAttributedString.DocumentType.html,
                                                                    .characterEncoding: String.Encoding.utf8.rawValue],
                                              documentAttributes: nil)
        else { throw DragShiftError("Couldn't read the text.") }
        return a
    }

    public static func write(_ text: NSAttributedString, as format: DocFormat, to url: URL) throws {
        try Naming.atomically(url) { tmp in
            switch format {
            case .pdf:
                try pdf(text, to: tmp)
            case .md:
                try Markdown.from(text).write(to: tmp, atomically: false, encoding: .utf8)
            case .txt:
                try text.string.write(to: tmp, atomically: false, encoding: .utf8)
            default:
                guard let type = format.documentType else { return }
                var attrs: [NSAttributedString.DocumentAttributeKey: Any] = [.documentType: type]
                if format == .html { attrs[.characterEncoding] = String.Encoding.utf8.rawValue }
                let data = try text.data(from: NSRange(location: 0, length: text.length), documentAttributes: attrs)
                try data.write(to: tmp)
            }
        }
    }

    /// Lays text out on A4 pages with CoreText (works off-screen, no print dialog).
    static func pdf(_ text: NSAttributedString, to url: URL) throws {
        var media = CGRect(x: 0, y: 0, width: 595.2, height: 841.8)
        guard let ctx = CGContext(url as CFURL, mediaBox: &media, nil) else { throw DragShiftError("Couldn't create the PDF.") }
        let body = text.length > 0 ? text : plain(" ")
        let setter = CTFramesetterCreateWithAttributedString(body)
        let frameRect = media.insetBy(dx: 56, dy: 60)
        var start = 0
        repeat {
            ctx.beginPDFPage(nil)
            let frame = CTFramesetterCreateFrame(setter, CFRange(location: start, length: 0),
                                                 CGPath(rect: frameRect, transform: nil), nil)
            CTFrameDraw(frame, ctx)
            ctx.endPDFPage()
            let shown = CTFrameGetVisibleStringRange(frame).length
            if shown == 0 { break }
            start += shown
        } while start < body.length
        ctx.closePDF()
    }

    /// Convert one document to another format.
    @MainActor
    public static func convert(_ url: URL, to format: DocFormat, dir: URL) throws -> URL {
        let text = try read(url)
        let out = Naming.output(for: url, ext: format.ext, in: dir)
        try write(text, as: format, to: out)
        return out
    }
}

/// Small Markdown support: headings, bold, italics, code, links, lists, quotes, rules.
/// Enough for README-style notes; tables and footnotes come through as plain text.
public enum Markdown {
    public static func html(_ md: String) -> String {
        var out: [String] = []
        var list: String? = nil
        var inCode = false
        var para: [String] = []

        func flushPara() {
            if !para.isEmpty { out.append("<p>" + inline(para.joined(separator: " ")) + "</p>"); para = [] }
        }
        func closeList() { if let l = list { out.append("</\(l)>"); list = nil } }

        for raw in md.components(separatedBy: .newlines) {
            let line = raw
            let t = line.trimmingCharacters(in: .whitespaces)
            if t.hasPrefix("```") {
                flushPara(); closeList()
                out.append(inCode ? "</code></pre>" : "<pre><code>")
                inCode.toggle()
                continue
            }
            if inCode { out.append(escape(line)); continue }
            if t.isEmpty { flushPara(); closeList(); continue }
            if let h = t.firstIndex(where: { $0 != "#" }), t.hasPrefix("#"), t[h] == " " {
                let level = min(6, t.distance(from: t.startIndex, to: h))
                flushPara(); closeList()
                out.append("<h\(level)>\(inline(String(t[h...]).trimmingCharacters(in: .whitespaces)))</h\(level)>")
                continue
            }
            if t == "---" || t == "***" { flushPara(); closeList(); out.append("<hr>"); continue }
            if t.hasPrefix("> ") { flushPara(); closeList(); out.append("<blockquote>\(inline(String(t.dropFirst(2))))</blockquote>"); continue }
            let bullet = t.hasPrefix("- ") || t.hasPrefix("* ") || t.hasPrefix("+ ")
            let numbered = t.range(of: #"^\d+[.)] "#, options: .regularExpression)
            if bullet || numbered != nil {
                flushPara()
                let kind = bullet ? "ul" : "ol"
                if list != kind { closeList(); out.append("<\(kind)>"); list = kind }
                let item = bullet ? String(t.dropFirst(2)) : String(t[numbered!.upperBound...])
                out.append("<li>\(inline(item))</li>")
                continue
            }
            closeList()
            para.append(t)
        }
        flushPara(); closeList()
        if inCode { out.append("</code></pre>") }
        return out.joined(separator: "\n")
    }

    static func escape(_ s: String) -> String {
        s.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
    }

    static func inline(_ s: String) -> String {
        var t = escape(s)
        let rules: [(String, String)] = [
            (#"`([^`]+)`"#, "<code>$1</code>"),
            (#"\*\*([^*]+)\*\*"#, "<b>$1</b>"),
            (#"__([^_]+)__"#, "<b>$1</b>"),
            (#"\*([^*]+)\*"#, "<i>$1</i>"),
            (#"(?<![A-Za-z0-9])_([^_]+)_(?![A-Za-z0-9])"#, "<i>$1</i>"),
            (#"\[([^\]]+)\]\(([^)\s]+)\)"#, "<a href=\"$2\">$1</a>"),
        ]
        for (p, r) in rules { t = t.replacingOccurrences(of: p, with: r, options: .regularExpression) }
        return t
    }

    /// Rich text back to Markdown: bold, italics, links and lists survive; headings are
    /// paragraphs whose font is clearly bigger than the body text.
    public static func from(_ text: NSAttributedString) -> String {
        var out = ""
        let ns = text.string as NSString
        let all = NSRange(location: 0, length: ns.length)

        // Body size = the size most characters use.
        var sizes: [CGFloat: Int] = [:]
        text.enumerateAttribute(.font, in: all) { f, r, _ in
            sizes[(f as? NSFont)?.pointSize ?? 12, default: 0] += r.length
        }
        let body = sizes.max { $0.value < $1.value }?.key ?? 12

        ns.enumerateSubstrings(in: all, options: .byParagraphs) { para, range, _, _ in
            guard let para else { return }
            if para.trimmingCharacters(in: .whitespaces).isEmpty { out += "\n"; return }
            var line = ""
            var biggest: CGFloat = 0
            text.enumerateAttributes(in: range) { attrs, r, _ in
                var piece = ns.substring(with: r)
                let font = attrs[.font] as? NSFont
                biggest = max(biggest, font?.pointSize ?? body)
                let traits = font?.fontDescriptor.symbolicTraits ?? []
                let trimmed = piece.trimmingCharacters(in: .whitespaces)
                if !trimmed.isEmpty {
                    if traits.contains(.bold) { piece = piece.replacingOccurrences(of: trimmed, with: "**\(trimmed)**") }
                    if traits.contains(.italic) { piece = piece.replacingOccurrences(of: trimmed, with: "*\(trimmed)*") }
                    if let link = attrs[.link] { piece = "[\(trimmed)](\(link))" }
                }
                line += piece
            }
            // AppKit writes list items as "\t•\tText" or "\t1.\tText".
            let bare = line.trimmingCharacters(in: .whitespaces)
            if let m = bare.range(of: #"^[•◦▪︎\-–]\s+"#, options: .regularExpression) {
                out += "- " + bare[m.upperBound...] + "\n"
                return
            }
            if let m = bare.range(of: #"^\d+[.)]\s+"#, options: .regularExpression) {
                out += bare[bare.startIndex..<m.upperBound].trimmingCharacters(in: .whitespaces) + " " + bare[m.upperBound...] + "\n"
                return
            }
            if biggest >= body * 1.45 { line = "# " + line.replacingOccurrences(of: "**", with: "") }
            else if biggest >= body * 1.2 { line = "## " + line.replacingOccurrences(of: "**", with: "") }
            out += line + "\n\n"
        }
        return out.replacingOccurrences(of: "\n{3,}", with: "\n\n", options: .regularExpression)
    }
}
