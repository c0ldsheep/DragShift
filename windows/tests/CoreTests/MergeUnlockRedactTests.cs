using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DragShift.Core;

namespace DragShift.Tests
{
    /// <summary>What Redact looks for in a picture's text: the Mac's patterns, the Luhn check, the padding and the overlap rule.</summary>
    static class RedactFinderTests
    {
        static List<string> Found(string text, string label) =>
            Redaction.Matches(text).Where(h => h.Label == label).Select(h => text.Substring(h.Start, h.Length)).ToList();

        static List<string> Labels(string text) => Redaction.Matches(text).Select(h => h.Label).ToList();

        /// <summary>The first thing of this kind found in the text, or null.</summary>
        static string First(string text, string label) => Found(text, label).FirstOrDefault();

        static void Near(double expected, double actual, string what) =>
            Expect.True(Math.Abs(expected - actual) < 1e-9, what + ": expected " + expected + ", got " + actual);

        /// <summary>A line of text laid out as if a reader had found it in a picture: 10 pixels a letter, 30 high, 10 between words.</summary>
        static ReadLine Laid(string text)
        {
            var line = new ReadLine(text);
            double x = 10;
            foreach (var word in text.Split(' '))
            {
                line.Words.Add(new ReadWord(word, new Box(x, 10, word.Length * 10, 30)));
                x += word.Length * 10 + 10;
            }
            return line;
        }

        /// <summary>The boxes that end up over a line of text, as "Label:covered words".</summary>
        static string Boxes(string text)
        {
            var line = Laid(text);
            return string.Join(" | ", Redaction.FindText(new[] { line }).Select(f =>
                f.Label + ":" + string.Join(" ", line.Words.Where(w => Redaction.Shared(f.Rect, w.Rect) > 0.5 * w.Rect.Width * w.Rect.Height).Select(w => w.Text))));
        }

        static ReadLine Line(params (string Text, double X, double Y, double W, double H)[] words)
        {
            var line = new ReadLine(string.Join(" ", words.Select(w => w.Text)));
            foreach (var w in words) line.Words.Add(new ReadWord(w.Text, new Box(w.X, w.Y, w.W, w.H)));
            return line;
        }

        [Test]
        static void EmailIsFound()
        {
            Expect.Equal("test@example.com", string.Join("|", Found("Email: test@example.com", "Email")), "plain email");
            Expect.Equal("first.last+bills@mail.example.co.in", string.Join("|", Found("Write to first.last+bills@mail.example.co.in today", "Email")), "dots, plus and a longer domain");
            Expect.Equal(0, Found("rahul.k@okhdfcbank", "Email").Count, "no dot after the @ is not an email (that is a UPI ID)");
        }

        [Test]
        static void IndianMobileWithAndWithoutCountryCode()
        {
            // The first phone pattern finds the whole number. (The Mac's second pattern, any "+" number, also fires inside a
            // "+91" number; that second box lies over the first and the overlap rule drops it, which the last checks show.)
            Expect.Equal("98765 43210", First("Mobile 98765 43210", "Phone"), "ten digits in two groups");
            Expect.Equal("9876543210", First("Mobile 9876543210", "Phone"), "ten digits together");
            Expect.Equal("+91 98765 43210", First("Phone: +91 98765 43210", "Phone"), "with +91 and spaces");
            Expect.Equal("+91-98765-43210", First("Call +91-98765-43210 now", "Phone"), "with +91 and dashes");
            Expect.Equal(0, Found("Order 12345 67890", "Phone").Count, "a number that does not start 6 to 9 is not a mobile");
            Expect.Equal(0, Found("Order 12345 67890", "Phone").Count + Found("Order 5678901234", "Phone").Count, "nor is a ten digit number that starts with 5");

            Expect.Equal("Phone:98765 43210", Boxes("Mobile 98765 43210"), "one box over both groups");
            Expect.Equal("Phone:9876543210", Boxes("Mobile 9876543210"), "one box over the number");
            Expect.Equal("Phone:+91 98765 43210", Boxes("Phone: +91 98765 43210"), "one box over +91 and both groups");
            Expect.Equal("Phone:+91-98765-43210", Boxes("Call +91-98765-43210 now"), "one box over the dashed number");
            // Written without any gaps, the Mac's patterns see twelve digits first (so the box carries another label); the box is the same.
            var glued = Boxes("Call +919876543210 now");
            Expect.True(glued.EndsWith(":+919876543210", StringComparison.Ordinal) && !glued.Contains("|"), "one box over a +91 number written without any gaps, got: " + glued);
            Expect.Equal("", Boxes("Order 12345 67890"), "nothing over an order number");
        }

        [Test]
        static void TwelveDigitIdWithSpaces()
        {
            Expect.Equal("2345 6789 0123", string.Join("|", Found("ID 2345 6789 0123", "Aadhaar")), "three groups of four");
            Expect.Equal("2345-6789-0123", string.Join("|", Found("ID 2345-6789-0123", "Aadhaar")), "dashes work too");
            Expect.Equal(0, Found("ID 1345 6789 0123", "Aadhaar").Count, "it never starts with 0 or 1");
            Expect.Equal(0, Found("2345 6789 0123", "Card").Count, "twelve digits are too few for a card");
        }

        [Test]
        static void PanAndIfscCodes()
        {
            Expect.Equal("ABCDE1234F", string.Join("|", Found("PAN: ABCDE1234F", "PAN")), "PAN");
            Expect.Equal(0, Found("abcde1234f", "PAN").Count, "PAN is capital letters");
            Expect.Equal(0, Found("ABCDE12345", "PAN").Count, "PAN ends in a letter");
            Expect.Equal("HDFC0001234", string.Join("|", Found("IFSC HDFC0001234", "IFSC")), "IFSC");
            Expect.Equal(0, Found("HDFC1001234", "IFSC").Count, "IFSC has a 0 in the fifth place");
        }

        [Test]
        static void CardOnlyWhenTheDigitsPassLuhn()
        {
            Expect.Equal("4111 1111 1111 1111", string.Join("|", Found("Card 4111 1111 1111 1111", "Card")), "a card that passes Luhn");
            Expect.Equal("4111111111111111", string.Join("|", Found("Card 4111111111111111", "Card")), "the same card without spaces");
            Expect.Equal(0, Found("Card 4111 1111 1111 1112", "Card").Count, "one digit changed: not a card any more");
            Expect.Equal(0, Found("Card 4111111111111112", "Card").Count, "one digit changed, no spaces");
            Expect.True(Redaction.Luhn("4111 1111 1111 1111") && Redaction.Luhn("4111111111111111"), "Luhn passes the test card");
            Expect.True(!Redaction.Luhn("4111111111111112"), "Luhn fails a changed digit");
            Expect.True(!Redaction.Luhn("1234") && !Redaction.Luhn("") && !Redaction.Luhn(null), "too few digits");
            Expect.True(!Redaction.Luhn("411111111111111111111"), "too many digits");
        }

        [Test]
        static void UpiIdIsFound()
        {
            Expect.Equal("rahul.k@okhdfcbank", string.Join("|", Found("Pay to rahul.k@okhdfcbank now", "UPI ID")), "UPI ID");
            Expect.Equal(0, Found("Pay to rahul.k@okhdfcbank now", "Email").Count, "and it is not an email");
        }

        [Test]
        static void AccountNumbersAndNothingElse()
        {
            Expect.Equal("123456789012", string.Join("|", Found("Account 123456789012", "Bank account")), "nine to eighteen digits");
            Expect.Equal(0, Labels("Account 12345678").Count, "eight digits are not an account number");
            Expect.Equal(0, Labels("The quick brown fox bought 3 apples and 12 pears for 450 rupees on 12/05/2024 at 10:30.").Count, "ordinary text");
            Expect.Equal(0, Labels("Invoice 100245").Count, "a short number");
            Expect.Equal(0, Labels("").Count + Labels(null).Count, "nothing at all");
        }

        [Test]
        static void TheNinePatternsAreTheMacsInTheMacsOrder()
        {
            Expect.Equal("Email|UPI ID|Aadhaar|PAN|IFSC|Card|Phone|Phone|Bank account", string.Join("|", Redaction.Patterns.Select(p => p.Label)), "labels in order");
            Expect.Equal(@"\b(?:\d[ \-]?){13,19}\b", Redaction.Patterns[5].Expression, "the card pattern, character for character");
            Expect.Equal(@"(?:\+?91[ \-]?)?\b[6-9]\d{4}[ \-]?\d{5}\b", Redaction.Patterns[6].Expression, "the first phone pattern");
            Expect.Equal(@"\+\d{1,3}[ \-]?\(?\d{2,4}\)?[ \-]?\d{3,4}[ \-]?\d{3,4}", Redaction.Patterns[7].Expression, "the second phone pattern");
        }

        [Test]
        static void OverlapRuleDropsBoxesMostlyOverAnEarlierOne()
        {
            var found = new List<Finding>();
            Expect.True(Redaction.Add(found, "Email", new Box(0, 0, 100, 10)), "the first box is kept");
            Expect.True(!Redaction.Add(found, "UPI ID", new Box(10, 0, 50, 10)), "a box inside it is dropped");
            Expect.True(!Redaction.Add(found, "Phone", new Box(39, 0, 100, 10)), "61% of its own area over the first: dropped");
            Expect.True(Redaction.Add(found, "Phone", new Box(41, 0, 100, 10)), "59% of its own area over the first: kept");
            Expect.True(Redaction.Add(found, "Card", new Box(0, 50, 100, 10)), "a box that is somewhere else is kept");
            Expect.True(Redaction.Add(found, "Card", new Box(0, 10, 100, 10)), "a box that only touches is kept");
            Expect.Equal(4, found.Count, "four boxes in the end");

            var small = new List<Finding>();
            Redaction.Add(small, "PAN", new Box(0, 0, 10, 10));
            Expect.True(Redaction.Add(small, "Email", new Box(0, 0, 100, 10)), "a big box over a small earlier one is kept (the earlier one covers only 10% of it)");
        }

        [Test]
        static void PaddingIsTwelvePercentOfTheLinesHeight()
        {
            var line = Line(("Email:", 10, 100, 80, 40), ("test@example.com", 100, 100, 300, 40));
            var found = Redaction.FindText(new[] { line });
            Expect.Equal(1, found.Count, "one email in the line (the shorter UPI match lies inside it and is dropped)");
            Expect.Equal("Email", found[0].Label, "its type");
            Near(95.2, found[0].Rect.X, "left");
            Near(95.2, found[0].Rect.Y, "top");
            Near(309.6, found[0].Rect.Width, "width");
            Near(49.6, found[0].Rect.Height, "height");
        }

        [Test]
        static void PaddingFollowsTheWholeLineNotJustTheMatchedWords()
        {
            // The number's own box is only 24 high, but the line is 40 high (the other word is taller): 12% of 40 is 4.8.
            var line = Line(("Phone:", 10, 100, 90, 40), ("9876543210", 110, 108, 200, 24));
            var found = Redaction.FindText(new[] { line });
            Expect.Equal(1, found.Count, "one phone number");
            Near(110 - 4.8, found[0].Rect.X, "left");
            Near(108 - 4.8, found[0].Rect.Y, "top");
            Near(200 + 9.6, found[0].Rect.Width, "width");
            Near(24 + 9.6, found[0].Rect.Height, "height");
        }

        [Test]
        static void AMatchCoversTheWordsItTouches()
        {
            var line = Line(("Call", 10, 10, 50, 30), ("+91", 70, 10, 40, 30), ("98765", 120, 10, 90, 30), ("43210", 220, 10, 90, 30), ("today", 320, 10, 70, 30));
            var found = Redaction.FindText(new[] { line });
            var phone = found.First(f => f.Label == "Phone");
            Near(70 - 3.6, phone.Rect.X, "starts at the +91");
            Near(310 + 3.6, phone.Rect.Right, "ends after the last digits");
            Expect.True(phone.Rect.Right < 320, "and stops before the next word");
        }

        [Test]
        static void FindTextGivesOneBoxPerThingInPatternOrder()
        {
            var a = Line(("Email:", 10, 10, 80, 30), ("test@example.com", 100, 10, 300, 30));
            var b = Line(("Phone:", 10, 60, 80, 30), ("+91", 100, 60, 40, 30), ("98765", 150, 60, 90, 30), ("43210", 250, 60, 90, 30));
            var found = Redaction.FindText(new[] { a, b });
            Expect.Equal("Email|Phone", string.Join("|", found.Select(f => f.Label)), "an email, then a phone number");
            Expect.Equal(0, Redaction.FindText(new[] { Line(("Nothing", 10, 10, 90, 30), ("here", 110, 10, 60, 30)) }).Count, "plain text finds nothing");
            Expect.Equal(0, Redaction.FindText(new ReadLine[0]).Count, "no lines");
        }

        [Test]
        static void FacesAreWiderAndTaller()
        {
            var b = Redaction.FaceBox(new Box(100, 100, 200, 300));
            Near(80, b.X, "10% of the width to the left");
            Near(55, b.Y, "15% of the height above");
            Near(240, b.Width, "10% each side");
            Near(390, b.Height, "15% each side");
        }

        [Test]
        static void FacesFollowTheSameOverlapRuleAndComeLast()
        {
            var text = new List<Finding> { new Finding("Email", new Box(0, 0, 400, 50)) };
            var all = Redaction.AddFaces(text, new[] { new Box(500, 100, 100, 100), new Box(10, 5, 100, 20) });
            Expect.Equal("Email|Face", string.Join("|", all.Select(f => f.Label)), "the face over the text is dropped, the other one is added last");
            Expect.Equal(1, text.Count, "the list that was passed in is left as it was");
        }

        [Test]
        static void StylesKeepTheMacsNames()
        {
            Expect.Equal("Black bar|Pixelate|Blur", string.Join("|", new[] { RedactStyle.Bar, RedactStyle.Pixelate, RedactStyle.Blur }.Select(s => s.Title())), "titles");
            Expect.Equal("box|pixelate|blur", string.Join("|", new[] { RedactStyle.Bar, RedactStyle.Pixelate, RedactStyle.Blur }.Select(s => s.Key())), "saved names");
            Expect.Equal(RedactStyle.Pixelate, RedactStyles.Parse("pixelate"), "pixelate back");
            Expect.Equal(RedactStyle.Blur, RedactStyles.Parse("blur"), "blur back");
            Expect.Equal(RedactStyle.Bar, RedactStyles.Parse("box"), "bar back");
            Expect.Equal(RedactStyle.Bar, RedactStyles.Parse("garbage"), "anything else is the black bar");
            Expect.Equal(RedactStyle.Bar, RedactStyles.Parse(null), "nothing saved is the black bar");
            Near(8, Redaction.Amount(100, 40), "a fifth of 40 is 8");
            Near(8, Redaction.Amount(20, 20), "never under 8");
            Near(60, Redaction.Amount(400, 300), "a fifth of the shorter side");
            Expect.True(Redaction.BarRed == 13 && Redaction.BarGreen == 13 && Redaction.BarBlue == 15, "the bar is the Mac's near-black");
        }

        [Test]
        static void ClipKeepsTheBoxInsideThePicture()
        {
            Expect.True(Redaction.Clip(new Box(-5, -5, 20, 20), 100, 100, out int x, out int y, out int w, out int h), "partly outside");
            Expect.Equal("0,0,15,15", x + "," + y + "," + w + "," + h, "cut at the corner");
            Expect.True(Redaction.Clip(new Box(90.4, 10.2, 30, 5), 100, 100, out x, out y, out w, out h), "past the right edge");
            Expect.Equal("90,10,10,6", x + "," + y + "," + w + "," + h, "whole pixels, cut at the edge");
            Expect.True(!Redaction.Clip(new Box(200, 200, 10, 10), 100, 100, out _, out _, out _, out _), "wholly outside gives nothing");
        }

        [Test]
        static void RedactStyleIsRememberedUnderTheMacsKey()
        {
            using (var s = new Sandbox())
            {
                var path = s.Path_("settings.ini");
                var a = new Settings(path);
                Expect.Equal(RedactStyle.Bar, a.RedactStyle, "the black bar the first time");
                Expect.Equal("redact.style", Settings.Keys.RedactStyle, "the same key as the Mac");
                a.RedactStyle = RedactStyle.Pixelate;
                Expect.Equal("pixelate", new Settings(path).Get("redact.style"), "saved as pixelate");
                Expect.Equal(RedactStyle.Pixelate, new Settings(path).RedactStyle, "and read back");
                a.Set(Settings.Keys.RedactStyle, "nonsense");
                Expect.Equal(RedactStyle.Bar, new Settings(path).RedactStyle, "a damaged value gives the black bar");
            }
        }
    }

    /// <summary>Where Merge, Unlock, One PDF and Redact sit in the ring, and what they are called.</summary>
    static class MergeUnlockRedactCatalogTests
    {
        static List<string> Ids(params string[] files) =>
            Catalog.Pages(files, Capabilities.All).SelectMany(p => p.Slices).Select(s => s.Id).ToList();

        static RingPage Page(string id, params string[] files) =>
            Catalog.Pages(files, Capabilities.All).FirstOrDefault(p => p.Id == id);

        [Test]
        static void NamesVerbsAndWindowsAreTheMacs()
        {
            Expect.Equal("pdf.merge|Merge|Merging", Catalog.PdfMerge.Id + "|" + Catalog.PdfMerge.Title + "|" + Catalog.PdfMerge.Verb, "Merge");
            Expect.Equal("pdf.unlock|Unlock|Removing the password", Catalog.PdfUnlock.Id + "|" + Catalog.PdfUnlock.Title + "|" + Catalog.PdfUnlock.Verb, "Unlock");
            Expect.Equal("mixed.merge|One PDF|Merging into one PDF", Catalog.OnePdf.Id + "|" + Catalog.OnePdf.Title + "|" + Catalog.OnePdf.Verb, "One PDF");
            Expect.Equal("img.redact|Redact|Redacting", Catalog.Redact.Id + "|" + Catalog.Redact.Title + "|" + Catalog.Redact.Verb, "Redact");
            Expect.Equal(Editor.Password, Catalog.PdfUnlock.Editor, "Unlock asks for the password");
            Expect.Equal(Editor.Redact, Catalog.Redact.Editor, "Redact opens the picture");
            Expect.Equal(Editor.None, Catalog.PdfMerge.Editor, "Merge runs straight away");
            Expect.Equal(Editor.None, Catalog.OnePdf.Editor, "One PDF runs straight away");
        }

        [Test]
        static void MergeOnlyForSeveralPdfsAndUnlockForAny()
        {
            Expect.True(!Ids("C:/a/Bank.pdf").Contains("pdf.merge"), "one PDF: nothing to merge");
            Expect.True(Ids("C:/a/Bank.pdf").Contains("pdf.unlock"), "one PDF: Unlock");
            var two = Ids("C:/a/A.pdf", "C:/a/B.pdf");
            Expect.True(two.Contains("pdf.merge") && two.Contains("pdf.unlock"), "two PDFs: Merge and Unlock");
            var edit = Page("edit", "C:/a/A.pdf", "C:/a/B.pdf");
            Expect.Equal("Edit", edit.Title, "they sit on the PDF's Edit page");
            Expect.Equal("pdf.merge,pdf.unlock", string.Join(",", edit.Slices.Select(s => s.Id)), "Merge first, then Unlock");
            Expect.Equal("pdf.unlock", string.Join(",", Page("edit", "C:/a/A.pdf").Slices.Select(s => s.Id)), "one PDF: only Unlock");
        }

        [Test]
        static void ThePdfPageThatWasThereBeforeIsUntouched()
        {
            var convert = Page("convert", "C:/a/Bank.pdf");
            Expect.Equal("PDF", convert.Title, "still called PDF");
            Expect.Equal("pdf.to.jpg,pdf.to.png,pdf.to.txt,fit.kb,pdf.compress", string.Join(",", convert.Slices.Select(s => s.Id)), "the same slices in the same places");
        }

        [Test]
        static void RedactIsForOnePictureAndComesLast()
        {
            Expect.True(Ids("C:/a/Photo.jpg").Contains("img.redact"), "one picture: Redact");
            Expect.True(!Ids("C:/a/1.jpg", "C:/a/2.jpg").Contains("img.redact"), "several pictures: no Redact (it opens one at a time)");
            var edit = Page("edit", "C:/a/Photo.jpg");
            Expect.Equal("img.redact", edit.Slices.Last().Id, "last on the Edit page, so nothing else moves");
            Expect.Equal("fit.kb,img.passport,img.compress,img.resize,img.rotate,img.flip,img.watermark,img.redact", string.Join(",", edit.Slices.Select(s => s.Id)),
                         "everything that was there stays where it was");
            Expect.Equal(Catalog.MaxSlices, edit.Slices.Count, "and the page is exactly full");
            var withoutFaces = Catalog.Pages(new[] { "C:/a/Photo.jpg" }, new Capabilities { Faces = false }).First(p => p.Id == "edit");
            Expect.True(withoutFaces.Slices.Last().Id == "img.redact" && withoutFaces.Slices.Count == 7, "still there on a PC that cannot find faces");
        }

        [Test]
        static void OnePdfOnlyForPicturesAndPdfsTogether()
        {
            var mixed = Catalog.Pages(new[] { "C:/a/Scan.jpg", "C:/a/Bank.pdf" }, Capabilities.All);
            var files = mixed.First(p => p.Id == "share");
            Expect.Equal("mixed.merge", files.Slices.Last().Id, "One PDF, last on the Files page");
            Expect.Equal(1, mixed.SelectMany(p => p.Slices).Count(s => s.Id == "mixed.merge"), "once");
            Expect.True(!Ids("C:/a/1.jpg", "C:/a/2.jpg").Contains("mixed.merge"), "two pictures already have PDF");
            Expect.True(!Ids("C:/a/1.pdf", "C:/a/2.pdf").Contains("mixed.merge"), "two PDFs have Merge");
            Expect.True(!Ids("C:/a/Scan.jpg", "C:/a/Clip.mp4").Contains("mixed.merge"), "a picture and a video cannot");
            Expect.True(!Ids("C:/a/Scan.jpg", "C:/a/Bank.pdf", "C:/a/Notes.zip").Contains("mixed.merge"), "nor can a zip be in it");
            Expect.True(Catalog.PicturesAndPdfs(new[] { "C:/a/Scan.jpg", "C:/a/Bank.pdf", "C:/a/Another.png" }), "several of each");
            Expect.True(!Catalog.PicturesAndPdfs(new[] { "C:/a/Scan.jpg" }) && !Catalog.PicturesAndPdfs(new string[0]), "one kind, or none");
        }

        [Test]
        static void NoPageHasMoreThanEightSlices()
        {
            foreach (var set in new[]
            {
                new[] { "C:/a/Photo.jpg" }, new[] { "C:/a/1.jpg", "C:/a/2.jpg" }, new[] { "C:/a/Bank.pdf" }, new[] { "C:/a/A.pdf", "C:/a/B.pdf" },
                new[] { "C:/a/Scan.jpg", "C:/a/Bank.pdf" },
            })
                foreach (var caps in new[] { Capabilities.All, new Capabilities() })
                    foreach (var p in Catalog.Pages(set, caps))
                        Expect.True(p.Slices.Count <= Catalog.MaxSlices, string.Join("+", set) + " · " + p.Title + " has at most 8 slices");
        }

        [Test]
        static void SettingsKnowsTheNewSlicesAndNothingRepeats()
        {
            var every = Catalog.Everything(Capabilities.All).Select(s => s.Id).ToList();
            foreach (var id in new[] { "pdf.merge", "pdf.unlock", "mixed.merge", "img.redact" })
                Expect.True(every.Contains(id), id + " is in Settings' list");
            Expect.Equal(every.Count, every.Distinct().Count(), "no slice twice");
        }

        [Test]
        static void HidingAndFavouritesWorkForTheNewSlices()
        {
            var hidden = Catalog.Pages(new[] { "C:/a/Photo.jpg" }, Capabilities.All, hidden: new HashSet<string> { "img.redact" });
            Expect.True(!hidden.SelectMany(p => p.Slices).Any(s => s.Id == "img.redact"), "hidden means gone");
            var fav = Catalog.Pages(new[] { "C:/a/Bank.pdf" }, Capabilities.All, favorites: new[] { "pdf.unlock" })[0];
            Expect.Equal("favorites", fav.Id, "a favourite goes first");
            Expect.Equal("pdf.unlock", fav.Slices[0].Id, "Unlock as a favourite");
            var one = Catalog.Pages(new[] { "C:/a/Scan.jpg", "C:/a/Bank.pdf" }, Capabilities.All, favorites: new[] { "img.to.pdf" })[0];
            Expect.Equal("mixed.merge", one.Slices[0].Id, "the PDF favourite means One PDF for a picture and a PDF together");
        }

        [Test]
        static void BothMergesTurnManyFilesIntoOneInARecipe()
        {
            Expect.True(Recipe.ManyToOne.Contains("pdf.merge") && Recipe.ManyToOne.Contains("mixed.merge"), "many to one");
            Expect.True(Recipe.ManyToOne.Contains("img.to.pdf") && Recipe.ManyToOne.Contains("img.stitch") && Recipe.ManyToOne.Contains("file.zip"), "the ones that were there");
        }
    }

    /// <summary>The names Merge, Unlock and Redact give their results, and the streaming PDF writer and safe save they rely on.</summary>
    static class MergeFilesTests
    {
        // The writer only copies the picture's bytes into the PDF, so any bytes will do for comparing two writers.
        static byte[] TinyJpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x02, 0x03, 0xFF, 0xD9 };

        static PdfWriter.Page[] ThreePages() => new[]
        {
            new PdfWriter.Page { Jpeg = TinyJpeg(), PixelWidth = 8, PixelHeight = 8, Gray = true, Width = 595.28, Height = 841.89 },
            new PdfWriter.Page { Jpeg = TinyJpeg(), PixelWidth = 8, PixelHeight = 8, Gray = false, Width = 200, Height = 100 },
            new PdfWriter.Page { Jpeg = TinyJpeg(), PixelWidth = 8, PixelHeight = 8, Gray = true, Width = 612, Height = 792 },
        };

        [Test]
        static void MergedNamesFollowExplorerAndNeverTakeATakenName()
        {
            using (var s = new Sandbox())
            {
                var files = new[] { s.Path_("B.pdf"), s.Path_("A.pdf") };
                var ordered = files.OrderBy(p => Path.GetFileName(p), Naming.NaturalOrder).ToList();
                var first = Naming.Output(ordered[0], "pdf", "Merged", s.Dir);
                Expect.Equal("A Merged.pdf", Path.GetFileName(first), "named after the first file in Explorer's order");
                File.WriteAllText(first, "x");
                Expect.Equal("A Merged 2.pdf", Path.GetFileName(Naming.Output(ordered[0], "pdf", "Merged", s.Dir)), "a taken name gets the next free one");
                File.WriteAllText(Path.Combine(s.Dir, "A Merged 2.pdf"), "x");
                Expect.Equal("A Merged 3.pdf", Path.GetFileName(Naming.Output(ordered[0], "pdf", "Merged", s.Dir)), "and then the next");
                Expect.Equal("A Unlocked.pdf", Path.GetFileName(Naming.Output(s.Path_("A.pdf"), "pdf", "Unlocked", s.Dir)), "Unlock names it the same way");
                Expect.Equal("Photo Redacted.png", Path.GetFileName(Naming.Output(s.Path_("Photo.png"), "png", "Redacted", s.Dir)), "and so does Redact");
                var many = new[] { "Page 10.pdf", "Page 2.pdf", "page 1.pdf" }.OrderBy(x => x, Naming.NaturalOrder).ToArray();
                Expect.Equal("page 1.pdf|Page 2.pdf|Page 10.pdf", string.Join("|", many), "Page 2 comes before Page 10");
            }
        }

        [Test]
        static void StreamedPdfIsByteForByteThePdfWriters()
        {
            var pages = ThreePages();
            var expected = PdfWriter.Make(pages);
            var ms = new MemoryStream();
            var stream = new PdfStream(ms, pages.Length);
            foreach (var p in pages) stream.Add(p);
            stream.Finish();
            Expect.True(ms.ToArray().SequenceEqual(expected), "the streamed PDF equals the one made in memory");
            Expect.Equal(3, stream.Added, "three pages added");

            var none = new MemoryStream();
            new PdfStream(none, 0).Finish();
            Expect.True(none.ToArray().SequenceEqual(PdfWriter.Make(new PdfWriter.Page[0])), "and so does a PDF with no pages");
        }

        [Test]
        static void StreamedPdfInsistsOnTheNumberOfPagesItPromised()
        {
            var pages = ThreePages();
            var a = new PdfStream(new MemoryStream(), 2);
            a.Add(pages[0]);
            Expect.Throws<InvalidOperationException>(() => a.Finish(), "one page of two is not a PDF");
            a.Add(pages[1]);
            Expect.Throws<InvalidOperationException>(() => a.Add(pages[2]), "a third page was not promised");
            a.Finish();
            Expect.Throws<InvalidOperationException>(() => a.Finish(), "finished once");
            Expect.Throws<InvalidOperationException>(() => a.Add(pages[0]), "nothing can be added to a finished PDF");
        }

        [Test]
        static void StreamedPdfCanBeReadBackByItsOwnTable()
        {
            var ms = new MemoryStream();
            var stream = new PdfStream(ms, 3);
            foreach (var p in ThreePages()) stream.Add(p);
            stream.Finish();
            var text = System.Text.Encoding.Latin1.GetString(ms.ToArray());
            int xref = int.Parse(text.Substring(text.LastIndexOf("startxref", StringComparison.Ordinal) + 10).Split('\n')[0]);
            var lines = text.Substring(xref).Split('\n');
            int count = int.Parse(lines[1].Split(' ')[1]);
            Expect.Equal(12, count, "catalog + pages + 3 objects per page + the free entry");
            for (int i = 1; i < count; i++)
                Expect.True(text.Substring(int.Parse(lines[2 + i].Substring(0, 10))).StartsWith(i + " 0 obj", StringComparison.Ordinal), "object " + i + " is where the table says");
            Expect.True(text.Contains("/Count 3") && text.Contains("/MediaBox [0 0 200.00 100.00]"), "three pages, each its own size");
        }

        [Test]
        static void SlowSaveIsAllOrNothing()
        {
            using (var s = new Sandbox())
            {
                var dest = s.Path_("Merged.pdf");
                Naming.AtomicallyAsync(dest, async tmp =>
                {
                    await Task.Yield();
                    File.WriteAllText(tmp, "whole file");
                    Expect.True(!File.Exists(dest), "nothing is published while it is being written");
                }).GetAwaiter().GetResult();
                Expect.Equal("whole file", File.ReadAllText(dest), "published when finished");
                Expect.Equal(1, Directory.GetFiles(s.Dir).Length, "no hidden temporary file left");

                var failing = s.Path_("Failed.pdf");
                Expect.Throws<InvalidOperationException>(() => Naming.AtomicallyAsync(failing, async tmp =>
                {
                    await Task.Yield();
                    File.WriteAllText(tmp, "half");
                    throw new InvalidOperationException("boom");
                }).GetAwaiter().GetResult(), "the error comes out");
                Expect.True(!File.Exists(failing), "nothing published");
                Expect.Equal(1, Directory.GetFiles(s.Dir).Length, "and nothing, hidden or not, left behind");
            }
        }

        [Test]
        static void CancellingASlowSaveLeavesNothingBehind()
        {
            using (var s = new Sandbox())
            {
                var cts = new CancellationTokenSource();
                var dest = s.Path_("Merged.pdf");
                Expect.Throws<OperationCanceledException>(() => Naming.AtomicallyAsync(dest, async tmp =>
                {
                    using (var fs = new FileStream(tmp, FileMode.CreateNew))
                    {
                        fs.WriteByte(1);
                        cts.Cancel();
                        await Task.Delay(1);
                        cts.Token.ThrowIfCancellationRequested();
                    }
                }).GetAwaiter().GetResult(), "cancelling stops the save");
                Expect.Equal(0, Directory.GetFiles(s.Dir).Length, "no new file and no hidden one");
            }
        }

        [Test]
        static void SlowSaveNeverReplacesAFile()
        {
            using (var s = new Sandbox())
            {
                var dest = s.Path_("Merged.pdf");
                File.WriteAllText(dest, "mine");
                Expect.Throws<IOException>(() => Naming.AtomicallyAsync(dest, async tmp =>
                {
                    await Task.Yield();
                    File.WriteAllText(tmp, "theirs");
                }).GetAwaiter().GetResult(), "an existing file is never overwritten");
                Expect.Equal("mine", File.ReadAllText(dest), "it is untouched");
                Expect.Equal(1, Directory.GetFiles(s.Dir).Length, "and no temporary file is left");
            }
        }
    }
}
