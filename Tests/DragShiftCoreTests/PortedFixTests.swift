import AppKit
import Foundation
import PDFKit
import Testing
@testable import DragShiftCore

// Fixes that came out of testing the Windows app on a real PC, checked here for the Mac.

@Suite struct PassportCrownTests {
    static let spec: PhotoSpec = { var s = PhotoSpec(); s.background = .keep; return s }()

    /// No measurement: the frame is what it always was (head 72%, 8% above it, face centred, 35:45).
    @Test func withoutAMeasurementNothingChanges() {
        let face = CGRect(x: 450, y: 420, width: 300, height: 360)
        let f = PassportPhoto.frame(face: face, spec: Self.spec)
        let crown = face.minY - face.height * PassportPhoto.crownAbove
        #expect(abs((face.maxY - crown) / f.height - 0.72) < 0.001)
        #expect(abs((crown - f.minY) / f.height - 0.08) < 0.001)
        #expect(abs(f.midX - face.midX) < 0.001)
        #expect(abs(f.width / f.height - 35.0 / 45.0) < 0.001)
        #expect(PassportPhoto.frame(face: face, spec: Self.spec, measuredCrown: nil) == f)
    }

    /// Tall hair: the head is counted from the real top of the hair, so none of it is cut off.
    @Test func tallHairMovesTheFrameUp() {
        let face = CGRect(x: 450, y: 520, width: 300, height: 300)
        let crown = face.minY - 0.9 * face.height
        let plain = PassportPhoto.frame(face: face, spec: Self.spec)
        let f = PassportPhoto.frame(face: face, spec: Self.spec, measuredCrown: crown)
        #expect(abs((face.maxY - crown) / f.height - 0.72) < 0.001, "crown to chin is 72% of the photo")
        #expect(abs((crown - f.minY) / f.height - 0.08) < 0.001, "8% of the photo is above the real crown")
        #expect(f.minY < plain.minY, "the frame reaches higher than the guess did")
        #expect(abs(f.midX - face.midX) < 0.001)
    }

    /// A low fringe never takes away room above the head, and a line far above the head isn't hair.
    @Test func aLowerLineIsIgnoredAndAnAbsurdOneIsCapped() {
        let face = CGRect(x: 450, y: 520, width: 300, height: 300)
        let plain = PassportPhoto.frame(face: face, spec: Self.spec)
        #expect(PassportPhoto.frame(face: face, spec: Self.spec, measuredCrown: face.minY - 0.2 * face.height) == plain)
        #expect(PassportPhoto.frame(face: face, spec: Self.spec, measuredCrown: .nan) == plain)
        let capped = PassportPhoto.frame(face: face, spec: Self.spec, measuredCrown: -9000)
        #expect(capped == PassportPhoto.frame(face: face, spec: Self.spec, measuredCrown: face.minY - 1.2 * face.height))
    }

    /// A cut-out drawn by hand: `rects` are the solid parts (top-left origin).
    static func mask(_ w: Int, _ h: Int, _ rects: [CGRect]) -> VisionOps.Mask {
        var a = [UInt8](repeating: 0, count: w * h)
        for r in rects {
            for y in max(0, Int(r.minY))..<min(h, Int(r.maxY)) {
                for x in max(0, Int(r.minX))..<min(w, Int(r.maxX)) { a[y * w + x] = 255 }
            }
        }
        return VisionOps.Mask(width: w, height: h, alpha: a)
    }

    @Test func theCrownIsTheTopOfWhatIsJoinedToTheFace() {
        let face = CGRect(x: 150, y: 200, width: 100, height: 120)
        let head = CGRect(x: 140, y: 190, width: 120, height: 140), body = CGRect(x: 60, y: 330, width: 280, height: 170)
        // tall hair from row 80, and a poster higher up that doesn't touch it
        let hair = CGRect(x: 135, y: 80, width: 130, height: 115), poster = CGRect(x: 120, y: 10, width: 160, height: 40)
        #expect(PassportPhoto.crown(in: Self.mask(400, 500, [head, body, hair, poster]), face: face) == 80)
        // a thin spur above the hair (one stray lock, a finger) isn't the crown
        let spur = CGRect(x: 198, y: 40, width: 4, height: 40)
        #expect(PassportPhoto.crown(in: Self.mask(400, 500, [head, body, hair, spur]), face: face) == 80)
        // a shine that breaks the hair for two rows doesn't stop the walk
        var m = Self.mask(400, 500, [head, body, hair])
        var a = m.alpha
        for y in 120..<122 { for x in 0..<400 { a[y * 400 + x] = 0 } }
        m = VisionOps.Mask(width: 400, height: 500, alpha: a)
        #expect(PassportPhoto.crown(in: m, face: face) == 80)
        // hair that runs off the top of the picture: the crown is the top edge
        #expect(PassportPhoto.crown(in: Self.mask(400, 500, [head, body, CGRect(x: 135, y: 0, width: 130, height: 195)]), face: face) == 0)
        // the cut-out is somewhere else (not this person): no measurement
        #expect(PassportPhoto.crown(in: Self.mask(400, 500, [CGRect(x: 0, y: 0, width: 60, height: 60)]), face: face) == nil)
    }

    /// The whole path on a picture: with a measured crown every row of the hair is inside the photo.
    @Test func tallHairEndsUpInsideThePhoto() throws {
        let w = 1200, h = 1600
        let face = CGRect(x: 450, y: 760, width: 300, height: 300)      // top-left origin
        let hairTop = 400.0                                              // 1.2 face heights above the brows
        let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)!
        ctx.setFillColor(CGColor(srgbRed: 0.8, green: 0.8, blue: 0.8, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        ctx.setFillColor(CGColor(srgbRed: 0, green: 0, blue: 0.9, alpha: 1))                     // hair: blue
        ctx.fill(CGRect(x: 430, y: Double(h) - face.minY, width: 340, height: face.minY - hairTop))
        ctx.setFillColor(CGColor(srgbRed: 0.9, green: 0.1, blue: 0.1, alpha: 1))                 // face: red
        ctx.fill(CGRect(x: face.minX, y: Double(h) - face.maxY, width: face.width, height: face.height))
        let img = ctx.makeImage()!

        func blueRows(_ out: CGImage) -> (first: Int, count: Int) {
            let data = out.dataProvider!.data! as Data, bpr = out.bytesPerRow, x = out.width / 2
            var first = -1, count = 0
            for y in 0..<out.height {
                let i = y * bpr + x * 4
                if data[i + 2] > 150 && data[i] < 90 { if first < 0 { first = y }; count += 1 }
            }
            return (first, count)
        }
        let guessed = try PassportPhoto.make(img, spec: Self.spec, face: face).image
        #expect(blueRows(guessed).first == 0, "with the old guess the hair runs off the top of the photo")
        let out = try PassportPhoto.make(img, spec: Self.spec, face: face, crown: hairTop).image
        let rows = blueRows(out)
        let scale = Double(out.height) / PassportPhoto.frame(face: face, spec: Self.spec, measuredCrown: hairTop).height
        #expect(abs(Double(rows.first) - 0.08 * Double(out.height)) < 3, "8% of the photo above the hair, got row \(rows.first)")
        #expect(abs(Double(rows.count) - (face.minY - hairTop) * scale) < 3, "every row of the hair is in the photo")
    }
}

extension PassportCrownTests {
    /// Opt-in check of the real cut-out on a real picture (nothing is saved):
    /// DRAGSHIFT_SUBJECT=photo.jpg ./scripts/test.sh --filter cutOutOfARealPicture
    /// With a portrait, add DRAGSHIFT_FACE=photo.jpg DRAGSHIFT_OUT=folder to get the passport photo itself.
    @Test func cutOutOfARealPicture() throws {
        guard let path = ProcessInfo.processInfo.environment["DRAGSHIFT_SUBJECT"] else { return }
        guard #available(macOS 14, *) else { return }
        let img = try ImageOps.load(URL(fileURLWithPath: path), maxPixel: 1024)
        let centre = CGPoint(x: img.width / 2, y: img.height / 2)
        let mask = try VisionOps.subjectMask(img, at: centre)
        let solid = mask.alpha.filter { $0 >= 128 }.count
        #expect(mask.width == img.width && mask.height == img.height, "mask \(mask.width)x\(mask.height), picture \(img.width)x\(img.height)")
        #expect(solid > 0 && solid < mask.alpha.count, "the cut-out is part of the picture, not all or nothing")
        // a made-up "face" in the middle of the subject: the crown is above it and joined to it
        let face = CGRect(x: centre.x - Double(img.width) / 12, y: centre.y - Double(img.height) / 12,
                          width: Double(img.width) / 6, height: Double(img.height) / 6)
        let crown = PassportPhoto.crown(in: mask, face: face)
        print("cut-out covers \(solid * 100 / mask.alpha.count)% of \(mask.width)x\(mask.height); crown row \(crown.map { String(Int($0)) } ?? "none"), face top \(Int(face.minY))")
        #expect(crown != nil && crown! <= face.minY)
        let measured = PassportPhoto.measuredCrown(img, face: face)
        #expect(measured != nil && abs(measured! - crown!) <= 2, "the app's own path gives the same row: \(String(describing: measured))")
    }
}

@Suite struct PDFPageSizeTests {
    @Test func brokenAndGiantPageSizesNeverReachMemory() throws {
        #expect(try PDFOps.pixels(CGSize(width: 595, height: 842), dpi: 200)?.w == 1653)
        #expect(try PDFOps.pixels(CGSize(width: Double.nan, height: 842), dpi: 200) == nil)
        #expect(try PDFOps.pixels(CGSize(width: Double.infinity, height: 842), dpi: 200) == nil)
        #expect(try PDFOps.pixels(CGSize(width: 0, height: 842), dpi: 200) == nil)
        #expect(try PDFOps.pixels(CGSize(width: -595, height: 842), dpi: 200) == nil)
        do {
            _ = try PDFOps.pixels(CGSize(width: 14_400, height: 14_400), dpi: 300)
            Issue.record("a 3,600 megapixel page was allowed")
        } catch {
            #expect(error.localizedDescription.contains("3600 megapixels, too big to open safely"), "\(error.localizedDescription)")
        }
        #expect(throws: DragShiftError.self) { try PDFOps.pixels(CGSize(width: 1e200, height: 1e200), dpi: 300) }
    }

    /// A real PDF whose one page is 200 × 200 inches (the biggest the format allows): PDF → JPG refuses it with a
    /// plain message and leaves nothing; an ordinary PDF beside it still converts.
    @Test func aPdfWithAnEnormousPageIsRefusedNotRendered() async throws {
        let s = try Sandbox()
        let huge = s.url("Huge.pdf")
        var box = CGRect(x: 0, y: 0, width: 14_400, height: 14_400)
        let ctx = CGContext(huge as CFURL, mediaBox: &box, nil)!
        ctx.beginPDFPage(nil)
        ctx.setFillColor(CGColor(gray: 0.5, alpha: 1))
        ctx.fill(CGRect(x: 100, y: 100, width: 500, height: 500))
        ctx.endPDFPage()
        ctx.closePDF()

        var o = Options()
        o.pdfDPI = 300
        let action = Action("pdf.to.jpg", "JPG", "photo", verb: "Converting to JPG")
        let r = await Engine.run(action, inputs: [huge], options: o)
        #expect(r.outputs.isEmpty)
        #expect(r.failures.first?.contains("too big to open safely") == true, "\(r.failures)")
        #expect(s.files() == ["Huge.pdf"], "nothing was made: \(s.files())")

        let ok = try s.pdf("Letter.pdf", pages: ["one page"])
        #expect(await Engine.run(action, inputs: [ok], options: o).outputs.count == 1)
    }
}

@Suite struct UnzipAsideTests {
    @Test func aZipThatFailsHalfWayLeavesNoFolder() throws {
        let s = try Sandbox()
        let broken = s.url("Broken.zip")
        try Data("PK\u{03}\u{04} this is not really a zip".utf8).write(to: broken)
        #expect(throws: (any Error).self) { try Archive.unzip(broken, dir: s.dir) }
        #expect(s.files() == ["Broken.zip"], "nothing left, not even a hidden folder: \(s.files())")
        let hidden = try FileManager.default.contentsOfDirectory(atPath: s.dir.path).filter { $0.hasPrefix(".") }
        #expect(hidden.isEmpty, "\(hidden)")
    }

    @Test func aGoodZipGetsItsFolderAndATakenNameGetsTheNextOne() throws {
        let s = try Sandbox()
        let note = s.url("Note.txt")
        try Data("hello".utf8).write(to: note)
        let zip = try Archive.zip([note], dir: s.dir)                    // "Note.txt.zip"
        let a = try Archive.unzip(zip, dir: s.dir), b = try Archive.unzip(zip, dir: s.dir)
        #expect(a != b, "the second one got its own name: \(a.lastPathComponent), \(b.lastPathComponent)")
        for f in [a, b] { #expect(FileManager.default.fileExists(atPath: f.appendingPathComponent("Note.txt").path)) }
        let hidden = try FileManager.default.contentsOfDirectory(atPath: s.dir.path).filter { $0.hasPrefix(".dragshift") }
        #expect(hidden.isEmpty, "the hidden build folder is gone: \(hidden)")
    }
}

@Suite struct CancelTests {
    /// A tool that would run for a minute stops when Cancel is pressed.
    @Test func cancelStopsARunningTool() async throws {
        let token = CancelToken()
        let start = Date()
        _ = Task { try await Task.sleep(nanoseconds: 300_000_000); token.cancel() }
        await #expect(throws: Cancelled.self) {
            try await CancelToken.$current.withValue(token) {
                try await Engine.offload { try Shell.run(URL(fileURLWithPath: "/bin/sleep"), ["60"]) }
            }
        }
        #expect(Date().timeIntervalSince(start) < 5, "it took \(Date().timeIntervalSince(start)) s")
        // already cancelled: nothing is even started
        #expect(throws: Cancelled.self) {
            try CancelToken.$current.withValue(token) { try Shell.run(URL(fileURLWithPath: "/bin/sleep"), ["60"]) }
        }
        // no Cancel button at all (tests, watched folders): tools run as before
        #expect(try Shell.run(URL(fileURLWithPath: "/bin/echo"), ["hi"]) == "hi\n")
    }

    /// Cancel during a video export: the encoder stops, and neither a finished nor a half-written file stays.
    @Test func cancelDuringAVideoExportLeavesNothing() async throws {
        let s = try Sandbox()
        let clip = try await s.video("Long.mov", seconds: 40)
        let before = s.files()
        let token = CancelToken()
        let action = Action("media.to.hevc", "HEVC", "film", verb: "Converting")
        _ = Task { try await Task.sleep(nanoseconds: 150_000_000); token.cancel() }
        let start = Date()
        let r = await CancelToken.$current.withValue(token) {
            await Engine.run(action, inputs: [clip], options: Options())
        }
        #expect(token.isCancelled)
        #expect(r.outputs.isEmpty && r.failures.isEmpty, "outputs \(r.outputs.map(\.lastPathComponent)) failures \(r.failures)")
        #expect(Date().timeIntervalSince(start) < 10)
        #expect(s.files() == before, "\(s.files())")
        let hidden = try FileManager.default.contentsOfDirectory(atPath: s.dir.path).filter { $0.hasPrefix(".") }
        #expect(hidden.isEmpty, "no half-written file: \(hidden)")
    }

    /// Cancel half-way through a PDF with many pages: the half-filled folder of pictures is removed, and the
    /// files after it in the batch aren't started.
    @Test func cancelBetweenPagesRemovesTheHalfFilledFolder() async throws {
        let s = try Sandbox()
        let long = try s.pdf("Book.pdf", pages: (1...60).map { "page \($0)" })
        let second = try s.pdf("Second.pdf", pages: ["a", "b"])
        let token = CancelToken()
        let action = Action("pdf.to.png", "PNG", "photo", verb: "Converting to PNG")
        var o = Options()
        o.pdfDPI = 300
        let r = await CancelToken.$current.withValue(token) {
            await Engine.run(action, inputs: [long, second], options: o) { _, _, frac in
                if frac > 0.02 { token.cancel() }        // a few pages in
            }
        }
        #expect(r.outputs.isEmpty && r.failures.isEmpty, "\(r.outputs) \(r.failures)")
        #expect(s.files() == ["Book.pdf", "Second.pdf"], "\(s.files())")
    }
}

@Suite struct SmallRuleTests {
    @Test func mouseButtonsSurviveTyposAndOldSettings() {
        #expect(MouseButtons(saved: nil) == .all)
        #expect(MouseButtons(saved: "") == [])
        #expect(MouseButtons(saved: " , ") == [])
        #expect(MouseButtons(saved: "middle,back") == [.middle, .back])
        #expect(MouseButtons(saved: " Back , FORWARD ") == [.back, .forward])
        #expect(MouseButtons(saved: "mdidle") == .all, "a typo can't switch the mouse off")
        #expect(MouseButtons(saved: "middle,bogus") == [.middle])
        #expect(MouseButtons(saved: "right,middle,back,forward") == .all, "the Windows list reads fine; right isn't used here")
        for b: MouseButtons in [[], .middle, [.back, .forward], .all] { #expect(MouseButtons(saved: b.saved) == b) }
        #expect(MouseButtons.all.saved == "middle,back,forward")
    }

    @Test func onlyTheChosenButtonsCount() {
        let left = 1, right = 2, wheel = 4, back = 8, forward = 16, sixth = 32
        #expect(!MouseButtons.all.matches(pressed: left))
        #expect(!MouseButtons.all.matches(pressed: left | right), "the right button never opens the ring on the Mac")
        #expect(MouseButtons.all.matches(pressed: left | wheel))
        #expect(MouseButtons.all.matches(pressed: left | back) && MouseButtons.all.matches(pressed: left | forward))
        #expect(MouseButtons.all.matches(pressed: left | sixth), "extra buttons work as before when nothing is unticked")
        #expect(MouseButtons.middle.matches(pressed: left | wheel))
        #expect(!MouseButtons.middle.matches(pressed: left | back) && !MouseButtons.middle.matches(pressed: left | sixth))
        #expect(!MouseButtons([]).matches(pressed: left | wheel | back | forward | sixth))
    }

    @Test func theBubbleGrowsOnlyWhenTheTitleNeedsIt() {
        #expect(BubbleLayout.width(title: 70, buttons: 110) == 360, "a short title keeps the usual bubble")
        #expect(BubbleLayout.width(title: 0, buttons: 0) == 360)
        let long = BubbleLayout.width(title: 205, buttons: 110)
        #expect(long > 360 && long <= 560 && long >= 100 + 205 + 110, "\(long)")
        #expect(BubbleLayout.width(title: 5000, buttons: 110) == 560, "never wider than the cap")
        #expect(BubbleLayout.width(title: .infinity, buttons: 0) == 560 && BubbleLayout.width(title: .nan, buttons: 0) == 560)
        // the real title, measured the way the app measures it, doesn't fit the usual bubble beside two buttons
        let font = NSFont.systemFont(ofSize: 13, weight: .semibold)
        let title = NSAttributedString(string: "Stitched into one long picture", attributes: [.font: font]).size().width
        #expect(BubbleLayout.chrome + title + 100 > BubbleLayout.normal, "title is \(title) pt wide")
    }

    @Test func onlyOldTemporaryCopiesAreSwept() {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        func f(_ n: String) -> URL { URL(fileURLWithPath: "/tmp/Incoming/\(n)") }
        let folders = [(url: f("fresh"), made: now.addingTimeInterval(-60)),
                       (url: f("yesterday"), made: now.addingTimeInterval(-23 * 3600)),
                       (url: f("old"), made: now.addingTimeInterval(-25 * 3600)),
                       (url: f("ancient"), made: now.addingTimeInterval(-90 * 86400)),
                       (url: f("clock-went-back"), made: now.addingTimeInterval(3600))]
        #expect(InboxSweep.expired(folders, now: now) == [f("old"), f("ancient")])
        #expect(InboxSweep.expired([], now: now).isEmpty)
    }
}
