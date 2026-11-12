using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DragShift.Core;

namespace DragShift.Tests
{
    static class NamingPreservationTests
    {
        [Test]
        static void NewOutputPreservesDestinationCreatedDuringWriting()
        {
            using (var s = new Sandbox())
            {
                var dest = Naming.Unique(s.Path_("result.txt"));
                var sentinel = new byte[] { 0, 1, 127, 128, 255 };
                Expect.Throws<IOException>(() => Naming.Atomically(dest, tmp =>
                {
                    File.WriteAllText(tmp, "converted output");
                    File.WriteAllBytes(dest, sentinel);
                }), "a competing writer owns the destination");
                Expect.True(File.ReadAllBytes(dest).SequenceEqual(sentinel), "the competing file stays byte-for-byte unchanged");
                Expect.Equal(1, Directory.GetFiles(s.Dir).Length, "only the competing file remains; temporary output is removed");
            }
        }

        [Test]
        static void NewOutputNeverReplacesExistingFile()
        {
            using (var s = new Sandbox())
            {
                var dest = s.Path_("original.txt");
                File.WriteAllText(dest, "original contents");
                Expect.Throws<IOException>(() => Naming.Atomically(dest, tmp => File.WriteAllText(tmp, "replacement")),
                    "new outputs cannot replace an existing file");
                Expect.Equal("original contents", File.ReadAllText(dest), "the existing file is unchanged");
                Expect.Equal(1, Directory.GetFiles(s.Dir).Length, "failed output leaves no temporary file");
            }
        }

        [Test]
        static void NewOutputPublishesCompleteContents()
        {
            using (var s = new Sandbox())
            {
                var dest = s.Path_("result.txt");
                Naming.Atomically(dest, tmp =>
                {
                    File.WriteAllText(tmp, "complete output");
                    Expect.True(!File.Exists(dest), "destination is unpublished while conversion writes the temporary file");
                });
                Expect.Equal("complete output", File.ReadAllText(dest), "the complete output is published");
                Expect.Equal(1, Directory.GetFiles(s.Dir).Length, "successful output leaves no temporary file");
            }
        }

        [Test]
        static void FailedOutputIsNotPublished()
        {
            using (var s = new Sandbox())
            {
                var dest = s.Path_("result.txt");
                Expect.Throws<InvalidOperationException>(() => Naming.Atomically(dest, tmp =>
                {
                    File.WriteAllText(tmp, "partial output");
                    throw new InvalidOperationException("conversion failed");
                }), "failed conversion is reported");
                Expect.True(!File.Exists(dest), "a failed conversion publishes no output");
                Expect.Equal(0, Directory.GetFiles(s.Dir).Length, "partial temporary output is removed");
            }
        }

        [Test]
        static void FailedReplacementPreservesPreviousContents()
        {
            using (var s = new Sandbox())
            {
                var dest = s.Path_("settings.txt");
                File.WriteAllText(dest, "previous settings");
                Expect.Throws<InvalidOperationException>(() => Naming.ReplaceAtomically(dest, tmp =>
                {
                    File.WriteAllText(tmp, "partial settings");
                    throw new InvalidOperationException("settings write failed");
                }), "failed replacement is reported");
                Expect.Equal("previous settings", File.ReadAllText(dest), "previous settings survive a failed write");
                Expect.Equal(1, Directory.GetFiles(s.Dir).Length, "failed replacement leaves no temporary file");
            }
        }

        [Test]
        static void SettingsCanSaveTwiceAndReloadTheLatestValue()
        {
            using (var s = new Sandbox())
            {
                var path = s.Path_("settings.txt");
                var settings = new Settings(path);
                settings.Set("sample", "first value");
                Expect.Equal("first value", new Settings(path).Get("sample"), "first save creates the settings file");
                settings.Set("sample", "second value");
                Expect.Equal("second value", new Settings(path).Get("sample"), "second save replaces the settings file");
                Expect.Equal(1, Directory.GetFiles(s.Dir).Length, "settings saves leave no temporary files");
            }
        }
    }

    static class CatalogTests
    {
        static List<string> Ids(params string[] files) =>
            Catalog.Pages(files, Capabilities.All).SelectMany(p => p.Slices).Select(s => s.Id).ToList();

        [Test]
        static void PicturesGetSizeLimitAndPassport()
        {
            var ids = Ids("C:/a/Photo.jpg");
            Expect.True(ids.Contains("fit.kb") && ids.Contains("img.passport") && ids.Contains("img.compress"), "edit slices");
            Expect.True(!ids.Contains("img.to.jpg"), "no 'convert to its own format'");
            Expect.True(ids.Contains("img.to.heic"), "HEIC when this PC can write it");
            var noHeic = Catalog.Pages(new[] { "C:/a/Photo.jpg" }, new Capabilities()).SelectMany(p => p.Slices).Select(s => s.Id);
            Expect.True(!noHeic.Contains("img.to.heic"), "no HEIC slice without the extension");
            foreach (var p in Catalog.Pages(new[] { "C:/a/Photo.jpg" }, Capabilities.All))
                Expect.True(p.Slices.Count <= Catalog.MaxSlices, p.Title + " has at most 8 slices");
        }

        [Test]
        static void StitchShowsOnlyForSeveralPictures()
        {
            Expect.True(!Ids("C:/a/GST 1.jpg").Contains("img.stitch"), "one picture: nothing to stitch");
            Expect.True(Ids("C:/a/GST 1.jpg", "C:/a/GST 2.jpg", "C:/a/GST 3.jpg").Contains("img.stitch"), "three pictures: Stitch");
            foreach (var p in Catalog.Pages(new[] { "C:/a/1.jpg", "C:/a/2.jpg" }, Capabilities.All))
                Expect.True(p.Slices.Count <= Catalog.MaxSlices, p.Title + " has at most 8 slices");
        }

        [Test]
        static void StitchPlanSharesOneWidthAndStaysSaveable()
        {
            var same = Stitch.Plan(new[] { (1653, 2339), (1653, 2339), (1653, 2339) });
            Expect.Equal(1653, same.Width, "three A4 scans keep their width");
            Expect.Equal(7017, same.Heights.Sum(), "and stack to three pages tall");
            var mixed = Stitch.Plan(new[] { (1000, 500), (2000, 500) });
            Expect.Equal(2000, mixed.Width, "the widest sets the width");
            Expect.Equal(1000, mixed.Heights[0], "the narrow one grows to match, keeping its shape");
            var huge = Stitch.Plan(Enumerable.Repeat((4000, 6000), 30).ToList());
            Expect.True(huge.Heights.Sum() <= Stitch.JpegMaxSide, "30 phone photos still fit a JPG's tallest side");
            Expect.True((long)huge.Width * huge.Heights.Sum() <= Stitch.MaxPixels + huge.Width * 30, "and fit in memory");
        }

        [Test]
        static void PdfAndMediaPages()
        {
            var pdf = Ids("C:/a/Marksheet.pdf");
            Expect.True(pdf.Contains("fit.kb") && pdf.Contains("pdf.to.jpg") && pdf.Contains("pdf.compress"), "pdf slices");
            var video = Ids("C:/a/Clip.mp4");
            Expect.True(!video.Contains("media.to.mp4") && video.Contains("media.to.mp3") && video.Contains("media.still"), "video slices");
            var none = Catalog.Pages(new[] { "C:/a/Clip.mp4" }, new Capabilities { Media = false });
            Expect.Equal("share", none[0].Id, "only Files without media support");
        }

        [Test]
        static void FilesPageHasNoRepeats()
        {
            var pages = Catalog.Pages(new[] { "C:/a/Folder.zip" }, Capabilities.All);
            var all = pages.SelectMany(p => p.Slices).Select(s => s.Id).ToList();
            Expect.Equal(all.Count, all.Distinct().Count(), "no slice twice");
        }

        [Test]
        static void FavoritesFollowTheFileType()
        {
            var favs = new List<string> { "img.compress", "fit.kb.50", "recipe.whatsapp", "share.share" };
            var forPdf = Catalog.Pages(new[] { "C:/a/b.pdf" }, Capabilities.All, favorites: favs)[0];
            Expect.Equal("favorites", forPdf.Id, "favourites come first");
            Expect.Equal("pdf.compress,fit.kb.50,share.share", string.Join(",", forPdf.Slices.Select(s => s.Id)),
                         "Compress means PDF compress for a PDF; recipes for pictures don't show");
            Expect.Equal("Under 50 KB", forPdf.Slices[1].Title, "size favourite title");
            var forVideo = Catalog.Pages(new[] { "C:/a/b.mp4" }, Capabilities.All, favorites: new[] { "fit.kb.50" });
            Expect.True(forVideo[0].Id != "favorites", "a size limit doesn't apply to video");
            var hidden = Catalog.Pages(new[] { "C:/a/b.jpg" }, Capabilities.All, hidden: new HashSet<string> { "img.flip" });
            Expect.True(!hidden.SelectMany(p => p.Slices).Any(s => s.Id == "img.flip"), "hidden slices are left out");
        }

        [Test]
        static void RecipeKindsAndTitles()
        {
            Expect.True(new Recipe("r", "R", "", "fit.kb.200").Kinds.SetEquals(new[] { FileKind.Image, FileKind.Pdf }), "size-first recipe");
            Expect.True(new Recipe("r", "R", "", "img.size.1600", "fit.kb.50").Kinds.SetEquals(new[] { FileKind.Image }), "picture recipe");
            Expect.Equal("Under 75 KB", Recipe.StepTitle("fit.kb.75"), "custom size title");
            Expect.Equal("Under 1 MB", Recipe.StepTitle("fit.kb.1000"), "MB title");
            Expect.Equal("Crop to 1080×1350", Recipe.StepTitle("img.fill.1080x1350"), "fill title");
        }
    }

    static class GeometryTests
    {
        [Test]
        static void SlicesByDirection()
        {
            double cx = Geometry.CenterX, cy = Geometry.CenterY;
            Expect.Equal("Slice 0", Geometry.HitTest(cx, cy - 100, 8, 0, true).ToString(), "up");
            Expect.Equal("Slice 2", Geometry.HitTest(cx + 100, cy, 8, 0, true).ToString(), "right");
            Expect.Equal("Slice 4", Geometry.HitTest(cx, cy + 100, 8, 0, true).ToString(), "down");
            Expect.Equal("Slice 6", Geometry.HitTest(cx - 100, cy, 8, 0, true).ToString(), "left");
            Expect.Equal("Hub", Geometry.HitTest(cx + 10, cy + 10, 8, 0, true).ToString(), "middle");
            Expect.Equal("Slice 2", Geometry.HitTest(cx + 400, cy, 8, 0, true).ToString(), "far away still picks by direction");
            Expect.Equal("None", Geometry.HitTest(cx + 400, cy, 8, 0, false).ToString(), "clicks must land on the ring");
            var tab = Geometry.TabRects(3)[1];
            Expect.Equal("Tab 1", Geometry.HitTest(tab.MidX, tab.Y + 5, 8, 3, true).ToString(), "tab");
        }
    }

    static class SizeLimitTests
    {
        /// <summary>Pretend JPEG: bytes grow with pixels and quality, like the real thing.</summary>
        static Func<double, double, byte[]> Fake(int w, int h, List<string> log) => (scale, q) =>
        {
            log?.Add(scale.ToString("0.00") + "@" + q.ToString("0.00"));
            int bytes = (int)(w * scale * h * scale * (0.05 + q * q * 0.6));
            return new byte[bytes];
        };

        [Test]
        static void PicksTheHighestQualityThatFits()
        {
            var log = new List<string>();
            var data = SizeLimit.Fit(Fake(1000, 800, log), 1000, 800, SizeLimit.Bytes(200));
            Expect.True(data.Length <= 200_000, "fits");
            Expect.True(data.Length > 185_000, "uses most of the allowance, got " + data.Length);
            Expect.True(log.All(s => s.StartsWith("1.00", StringComparison.Ordinal)), "no shrinking needed");
        }

        [Test]
        static void ShrinksWhenEvenLowQualityIsTooBig()
        {
            var log = new List<string>();
            var data = SizeLimit.Fit(Fake(4000, 3000, log), 4000, 3000, SizeLimit.Bytes(50));
            Expect.True(data.Length <= 50_000, "fits");
            Expect.True(log.Any(s => !s.StartsWith("1.00", StringComparison.Ordinal)), "made it smaller");
        }

        [Test]
        static void SmallEnoughStaysAtTopQuality()
        {
            var data = SizeLimit.Fit(Fake(100, 100, null), 100, 100, SizeLimit.Bytes(500));
            Expect.Equal((int)(100 * 100 * (0.05 + 0.95 * 0.95 * 0.6)), data.Length, "quality 95");
        }

        [Test]
        static void ImpossibleLimitsSaySo()
        {
            Expect.Throws<UserError>(() => SizeLimit.Fit((s, q) => new byte[100_000], 4000, 3000, 10), "nothing fits");
        }

        [Test]
        static void StepIds()
        {
            Expect.Equal(50, SizeLimit.KbFromStep("fit.kb.50") ?? 0, "parse");
            Expect.True(SizeLimit.KbFromStep("fit.kb") == null && SizeLimit.KbFromStep("fit.kb.x") == null, "bad ids");
            Expect.Equal("1 MB", SizeLimit.Title(1000), "title");
            Expect.Equal("48 KB", Bytes.Format(48_000), "bytes");
            Expect.Equal("2.4 MB", Bytes.Format(2_400_000), "bytes");
        }
    }

    static class PassportTests
    {
        [Test]
        static void FramesTheFaceThePassportWay()
        {
            var spec = new PhotoSpec();
            Expect.Equal(413, spec.PixelWidth, "3.5 cm at 300 dpi");
            Expect.Equal(531, spec.PixelHeight, "4.5 cm at 300 dpi");
            var face = new Box(450, 420, 300, 360);
            var f = Passport.Frame(face, spec);
            double crown = face.Y - face.Height * Passport.CrownAbove;
            Expect.True(Math.Abs((face.Bottom - crown) / f.Height - 0.72) < 1e-9, "head is 72% of the height");
            Expect.True(Math.Abs((crown - f.Y) / f.Height - 0.08) < 1e-9, "8% above the head");
            Expect.True(Math.Abs(f.MidX - face.MidX) < 1e-9, "face centred");
            Expect.True(Math.Abs(f.Width / f.Height - 35.0 / 45) < 1e-9, "3.5 × 4.5 shape");
        }

        [Test]
        static void TallHairMovesTheFrameUp()
        {
            var spec = new PhotoSpec();
            var face = new Box(450, 520, 300, 300); // brows to chin
            double measured = face.Y - face.Height * 0.9; // hair well above the usual estimate (0.55)
            var f = Passport.Frame(face, spec, measured);
            Expect.True(Math.Abs((face.Bottom - measured) / f.Height - 0.72) < 1e-9, "the real head is 72% of the height");
            Expect.True(Math.Abs((measured - f.Y) / f.Height - 0.08) < 1e-9, "8% above the real top of the hair");
            Expect.True(f.Y < Passport.Frame(face, spec).Y, "the frame reaches higher than the estimate");
            Expect.True(Math.Abs(f.MidX - face.MidX) < 1e-9, "face still centred");
        }

        [Test]
        static void AHairLineBelowTheEstimateChangesNothing()
        {
            var spec = new PhotoSpec();
            var face = new Box(450, 520, 300, 300);
            var estimate = Passport.Frame(face, spec).ToString();
            Expect.Equal(estimate, Passport.Frame(face, spec, null).ToString(), "no measurement, no change");
            Expect.Equal(estimate, Passport.Frame(face, spec, face.Y - face.Height * 0.2).ToString(), "a lower line (a low fringe) never shrinks the room above the head");
        }

        [Test]
        static void ALineFarAboveTheHeadIsNotTheHair()
        {
            var spec = new PhotoSpec();
            var face = new Box(450, 520, 300, 300);
            double reach = face.Y - face.Height * Passport.MaxCrownAbove;
            var f = Passport.Frame(face, spec, -9000);
            Expect.True(Math.Abs((face.Bottom - reach) / f.Height - 0.72) < 1e-9, "hair is only believed up to " + Passport.MaxCrownAbove + " face-heights above the brows");
        }

        [Test]
        static void SpecsRoundTrip()
        {
            var us = PhotoSpec.Presets[1].Spec.Copy();
            us.Background = PhotoBackground.LightBlue;
            var back = PhotoSpec.Parse(us.ToString());
            Expect.Equal(us.ToString(), back.ToString(), "round trip");
            Expect.Equal("2 × 2 in", back.SizeTitle, "US title");
            Expect.Equal("3.5 × 4.5 cm", new PhotoSpec().SizeTitle, "passport title");
            Expect.Equal(new PhotoSpec().ToString(), PhotoSpec.Parse("garbage").ToString(), "bad text falls back");
        }
    }

    static class NamingTests
    {
        [Test]
        static void UniqueNamesLikeExplorer()
        {
            using (var s = new Sandbox())
            {
                var a = Naming.Output(s.Path_("Photo.png"), "jpg", null, s.Dir);
                Expect.Equal("Photo.jpg", Path.GetFileName(a), "first");
                File.WriteAllText(a, "x");
                Expect.Equal("Photo 2.jpg", Path.GetFileName(Naming.Output(s.Path_("Photo.png"), "jpg", null, s.Dir)), "taken");
                Expect.Equal("Photo Under 50 KB.jpg", Path.GetFileName(Naming.Output(s.Path_("Photo.png"), "jpg", "Under 50 KB", s.Dir)), "suffix");
                var sorted = new[] { "Page 10.jpg", "Page 2.jpg", "page 1.jpg" }.OrderBy(x => x, Naming.NaturalOrder).ToArray();
                Expect.Equal("page 1.jpg|Page 2.jpg|Page 10.jpg", string.Join("|", sorted), "natural order");
            }
        }

        [Test]
        static void AtomicWriteLeavesNoTempFiles()
        {
            using (var s = new Sandbox())
            {
                var dest = s.Path_("out.txt");
                Naming.Atomically(dest, tmp => File.WriteAllText(tmp, "done"));
                Expect.Equal("done", File.ReadAllText(dest), "content");
                try { Naming.Atomically(s.Path_("bad.txt"), tmp => { File.WriteAllText(tmp, "half"); throw new IOException("boom"); }); }
                catch (IOException) { }
                Expect.Equal(1, Directory.GetFiles(s.Dir).Length, "the failed write left nothing behind");
            }
        }
    }

    static class SettingsTests
    {
        [Test]
        static void SavesAndReloads()
        {
            using (var s = new Sandbox())
            {
                var path = s.Path_("settings.ini");
                var a = new Settings(path);
                Expect.True(a.Shift && a.Mouse && !a.Paused, "defaults");
                Expect.True(a.Favorites.SequenceEqual(Catalog.DefaultFavorites), "default favourites");
                Expect.True(!a.ToggleFavorite("img.compress"), "removed");
                Expect.True(a.ToggleFavorite("fit.kb.50"), "added");
                a.Set(Settings.Keys.Shift, false);
                a.SetLastAction(FileKind.Image, "img.to.jpg");
                a.SetLastAction(FileKind.Pdf, "pdf.compress");
                a.SetLastAction(FileKind.Image, "fit.kb.50");
                var b = new Settings(path);
                Expect.True(!b.Shift, "bool persisted");
                Expect.True(b.Favorites.Contains("fit.kb.50") && !b.Favorites.Contains("img.compress"), "favourites persisted");
                Expect.Equal("fit.kb.50", b.LastAction(FileKind.Image), "last action per kind");
                Expect.Equal("pdf.compress", b.LastAction(FileKind.Pdf), "other kind kept");
                Expect.True(File.ReadAllText(path).StartsWith("# DragShift settings", StringComparison.Ordinal), "readable file");
            }
        }
    }

    static class MouseTriggerTests
    {
        [Test]
        static void EveryButtonUntilTheyChoose()
        {
            using (var s = new Sandbox())
            {
                var settings = new Settings(s.Path_("settings.ini"));
                Expect.Equal(MouseTrigger.All, settings.TriggerButtons, "right, wheel and both side buttons by default");
                Expect.Equal(MouseTrigger.All, settings.ActiveTriggerButtons, "and they are active");
                Expect.True((settings.TriggerButtons & MouseTrigger.Right) != 0, "the right button is one of them");
                Expect.Equal("right,middle,back,forward", MouseTriggers.Format(MouseTrigger.All), "written in a fixed order");
                Expect.Equal("", MouseTriggers.Format(MouseTrigger.None), "none is an empty list");
            }
        }

        [Test]
        static void ChosenButtonsSurviveARestart()
        {
            using (var s = new Sandbox())
            {
                var path = s.Path_("settings.ini");
                new Settings(path).TriggerButtons = MouseTrigger.Right | MouseTrigger.Back;
                var again = new Settings(path);
                Expect.Equal(MouseTrigger.Right | MouseTrigger.Back, again.TriggerButtons, "reloaded");
                Expect.True(File.ReadAllText(path).Contains("trigger.mouse.buttons = right,back"), "plain words in settings.ini");
                again.TriggerButtons = MouseTrigger.None;
                var none = new Settings(path);
                Expect.Equal(MouseTrigger.None, none.TriggerButtons, "no button at all is remembered, not mistaken for 'not chosen yet'");
                Expect.Equal(MouseTrigger.None, none.ActiveTriggerButtons, "and nothing is active");
            }
        }

        [Test]
        static void HandEditedListsAreForgiving()
        {
            using (var s = new Sandbox())
            {
                var path = s.Path_("settings.ini");
                File.WriteAllText(path, "trigger.mouse.buttons =  Right , FORWARD ,banana\r\n");
                Expect.Equal(MouseTrigger.Right | MouseTrigger.Forward, new Settings(path).TriggerButtons, "case and spaces don't matter, unknown words are skipped");
                File.WriteAllText(path, "trigger.mouse.buttons = banana\r\n");
                Expect.Equal(MouseTrigger.All, new Settings(path).TriggerButtons, "a typo never switches the trigger off");
                File.WriteAllText(path, "trigger.mouse.buttons = ,\r\n");
                Expect.Equal(MouseTrigger.All, new Settings(path).TriggerButtons, "only separators is a typo too");
                File.WriteAllText(path, "trigger.mouse.buttons =\r\n");
                Expect.Equal(MouseTrigger.None, new Settings(path).TriggerButtons, "an empty list means none");
            }
        }

        [Test]
        static void SwitchedOffOrPausedTakesNoButtonButKeepsTheTicks()
        {
            using (var s = new Sandbox())
            {
                var settings = new Settings(s.Path_("settings.ini"));
                settings.TriggerButtons = MouseTrigger.Right | MouseTrigger.Middle;
                settings.Set(Settings.Keys.Mouse, false);
                Expect.Equal(MouseTrigger.None, settings.ActiveTriggerButtons, "the mouse switch is off: no button is touched");
                Expect.Equal(MouseTrigger.Right | MouseTrigger.Middle, settings.TriggerButtons, "the ticks are kept");
                settings.Set(Settings.Keys.Mouse, true);
                settings.Set(Settings.Keys.Paused, true);
                Expect.Equal(MouseTrigger.None, settings.ActiveTriggerButtons, "paused: no button is touched");
                settings.Set(Settings.Keys.Paused, false);
                Expect.Equal(MouseTrigger.Right | MouseTrigger.Middle, settings.ActiveTriggerButtons, "back on, with the same ticks");
            }
        }
    }

    static class PdfWriterTests
    {
        static byte[] TinyJpeg()
        {
            // A real 8×8 grey baseline JPEG, so viewers can decode the page.
            return Convert.FromBase64String(
                "/9j/4AAQSkZJRgABAQAAAAAAAAD/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYnKSopGR8tMC" +
                "0oMCUoKSj/wAALCAAIAAgBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAAAf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AH//Z");
        }

        [Test]
        static void XrefPointsAtEveryObject()
        {
            var jpeg = TinyJpeg();
            var pdf = PdfWriter.Make(new[]
            {
                new PdfWriter.Page { Jpeg = jpeg, PixelWidth = 8, PixelHeight = 8, Gray = true, Width = 595.28, Height = 841.89 },
                new PdfWriter.Page { Jpeg = jpeg, PixelWidth = 8, PixelHeight = 8, Gray = true, Width = 200, Height = 100 },
            });
            var text = Encoding.ASCII.GetString(pdf);
            Expect.True(text.StartsWith("%PDF-1.4", StringComparison.Ordinal), "header");
            int xref = int.Parse(text.Substring(text.LastIndexOf("startxref", StringComparison.Ordinal) + 10).Split('\n')[0]);
            var lines = text.Substring(xref).Split('\n');
            Expect.Equal("xref", lines[0], "xref table where startxref says");
            int count = int.Parse(lines[1].Split(' ')[1]);
            Expect.Equal(9, count, "catalog + pages + 3 objects per page + free entry");
            for (int i = 1; i < count; i++)
            {
                int off = int.Parse(lines[2 + i].Substring(0, 10));
                Expect.True(text.Substring(off).StartsWith(i + " 0 obj", StringComparison.Ordinal), "object " + i + " at its offset");
            }
            Expect.True(text.Contains("/Count 2") && text.Contains("/MediaBox [0 0 200.00 100.00]"), "pages");
        }

        [Test]
        static void MacPreviewCanOpenIt()
        {
            if (!File.Exists("/usr/bin/sips")) return; // only checked on a Mac
            using (var s = new Sandbox())
            {
                var path = s.Path_("t.pdf");
                File.WriteAllBytes(path, PdfWriter.Make(new[]
                {
                    new PdfWriter.Page { Jpeg = TinyJpeg(), PixelWidth = 8, PixelHeight = 8, Gray = true, Width = 300, Height = 300 },
                }));
                var png = s.Path_("t.png");
                Expect.Equal(0, Tools.Run("/usr/bin/sips", "-s", "format", "png", path, "--out", png), "sips read the PDF");
                Expect.True(File.Exists(png), "rendered");
            }
        }
    }

    static class WebPTests
    {
        static uint[] Noise(int w, int h, bool alpha)
        {
            var r = new Random(7);
            var px = new uint[w * h];
            for (int i = 0; i < px.Length; i++)
            {
                uint a = alpha ? (uint)(i % 3 == 0 ? 255 : (i % 3 == 1 ? 0 : 128)) : 255u;
                uint c = (uint)r.Next(0, 1 << 24);
                px[i] = a == 0 ? 0 : (a << 24) | c;
            }
            return px;
        }

        static uint[] Flat(int w, int h)
        {
            var px = new uint[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = x < w / 2 ? 0xFFF5F5F7u : (y < h / 3 ? 0xFF3366E6u : 0xFFFF4D66u);
            return px;
        }

        [Test]
        static void PrefixCodesMatchTheSpec()
        {
            var p = WebPLossless.Prefix(1); Expect.Equal(0, p.Code, "1");
            p = WebPLossless.Prefix(5); Expect.True(p.Code == 4 && p.Bits == 1 && p.Extra == 0, "5");
            p = WebPLossless.Prefix(4096); Expect.True(p.Code == 23 && p.Bits == 10, "4096");
        }

        [Test]
        static void FlatPicturesShrink()
        {
            var data = WebPLossless.Encode(Flat(1200, 800), 1200, 800);
            Expect.True(data.Length < 1200 * 800 / 20, "far below 4 bytes per pixel, got " + data.Length);
            Expect.Equal("RIFF", Encoding.ASCII.GetString(data, 0, 4), "RIFF");
            Expect.Equal("WEBPVP8L", Encoding.ASCII.GetString(data, 8, 8), "VP8L");
        }

        /// <summary>On a Mac, macOS decodes our WebP and every pixel must come back exactly.</summary>
        [Test]
        static void MacDecodesEveryPixelExactly()
        {
            if (!File.Exists("/usr/bin/sips")) return;
            var cases = new (string, int, int, uint[])[]
            {
                ("noise", 97, 61, Noise(97, 61, false)), ("flat", 640, 400, Flat(640, 400)),
                ("tiny", 1, 1, new[] { 0xFF102030u }), ("alpha", 37, 23, Noise(37, 23, true)),
            };
            using (var s = new Sandbox())
            {
                foreach (var (name, w, h, px) in cases)
                {
                    var webp = s.Path_(name + ".webp");
                    File.WriteAllBytes(webp, WebPLossless.Encode(px, w, h));
                    var tiff = s.Path_(name + ".tiff");
                    Expect.Equal(0, Tools.Run("/usr/bin/sips", "-s", "format", "tiff", "-s", "formatOptions", "none", webp, "--out", tiff), name + " decoded");
                    var back = Tools.ReadUncompressedTiff(tiff, out int bw, out int bh);
                    Expect.True(bw == w && bh == h, name + " size");
                    for (int i = 0; i < px.Length; i++)
                    {
                        uint a = px[i] >> 24, b = back[i] >> 24;
                        if (a == 0 && b == 0) continue; // fully clear: colour doesn't matter
                        if (a != b || ((px[i] ^ back[i]) & 0xFFFFFF) != 0 && a == 255)
                            throw new Exception(name + " pixel " + i + ": " + px[i].ToString("X8") + " → " + back[i].ToString("X8"));
                    }
                }
            }
        }
    }

    static class Tools
    {
        public static int Run(string exe, params string[] args)
        {
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        /// <summary>Reads the uncompressed RGB/RGBA TIFF sips writes (chunky, 8 bits), as 0xAARRGGBB.</summary>
        public static uint[] ReadUncompressedTiff(string path, out int w, out int h)
        {
            var d = File.ReadAllBytes(path);
            bool le = d[0] == 'I';
            int U16(int o) => le ? d[o] | d[o + 1] << 8 : d[o] << 8 | d[o + 1];
            int U32(int o) => le ? d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24 : d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3];
            int ifd = U32(4), n = U16(ifd);
            w = h = 0;
            int spp = 3, rowsPerStrip = int.MaxValue, extra = 0;
            var strips = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int e = ifd + 2 + i * 12, tag = U16(e), type = U16(e + 2), count = U32(e + 4);
                int Val(int k) => type == 3 ? (count <= 2 ? U16(e + 8 + k * 2) : U16(U32(e + 8) + k * 2))
                                            : (count == 1 ? U32(e + 8) : U32(U32(e + 8) + k * 4));
                switch (tag)
                {
                    case 256: w = Val(0); break;
                    case 257: h = Val(0); break;
                    case 277: spp = Val(0); break;
                    case 278: rowsPerStrip = Val(0); break;
                    case 273: for (int k = 0; k < count; k++) strips.Add(Val(k)); break;
                    case 338: extra = Val(0); break; // 1 = premultiplied alpha, 2 = straight
                }
            }
            var px = new uint[w * h];
            int row = 0;
            foreach (var start in strips)
            {
                for (int r = 0; r < rowsPerStrip && row < h; r++, row++)
                    for (int x = 0; x < w; x++)
                    {
                        int o = start + (r * w + x) * spp;
                        uint R = d[o], G = d[o + 1], B = d[o + 2], A = spp == 4 ? d[o + 3] : 255u;
                        if (spp == 4 && extra == 1 && A > 0 && A < 255)
                        {
                            R = Math.Min(255, (R * 255 + A / 2) / A); G = Math.Min(255, (G * 255 + A / 2) / A); B = Math.Min(255, (B * 255 + A / 2) / A);
                        }
                        px[row * w + x] = A << 24 | R << 16 | G << 8 | B;
                    }
            }
            return px;
        }
    }
}

namespace DragShift.Tests
{
    static class HabitTests
    {
        [Test]
        static void ThreePicksInARowMakeAHabit()
        {
            var h = new Habits();
            h.Record(FileKind.Image, "img.to.jpg");
            h.Record(FileKind.Image, "img.to.jpg");
            Expect.Equal(null, h.Usual(FileKind.Image), "two picks are not a habit yet");
            h.Record(FileKind.Image, "img.to.jpg");
            Expect.Equal("img.to.jpg", h.Usual(FileKind.Image), "three are");
            Expect.Equal(null, h.Usual(FileKind.Pdf), "each kind of file has its own habit");
        }

        [Test]
        static void ANewHabitTakesOver()
        {
            var h = new Habits();
            for (int i = 0; i < 30; i++) h.Record(FileKind.Pdf, "pdf.to.jpg");
            int picks = 0;
            while (h.Usual(FileKind.Pdf) != "fit.kb" && picks < 20) { h.Record(FileKind.Pdf, "fit.kb.100"); picks++; }
            Expect.Equal("fit.kb", h.Usual(FileKind.Pdf), "sizes count for the Under KB slice");
            Expect.True(picks <= 8, "an old habit gives way within 8 new picks, took " + picks);
        }

        [Test]
        static void SurvivesTheSettingsFile()
        {
            var h = new Habits();
            for (int i = 0; i < 3; i++) h.Record(FileKind.Image, "img.size.1600");
            h.Record(FileKind.Video, "media.to.mp3");
            var back = Habits.Parse(h.ToString());
            Expect.Equal("img.resize", back.Usual(FileKind.Image), "resize steps count for Resize");
            Expect.Equal(h.ToString(), back.ToString(), "round trip");
            Expect.Equal(null, Habits.Parse("junk;Nope:a=1;Image:x").Usual(FileKind.Image), "bad lines are ignored");

            using (var box = new Sandbox())
            {
                var s = new Settings(Path.Combine(box.Dir, "settings.ini"));
                for (int i = 0; i < 3; i++) s.RecordPick(FileKind.Pdf, "pdf.compress");
                Expect.Equal("pdf.compress", new Settings(Path.Combine(box.Dir, "settings.ini")).Usual(FileKind.Pdf), "saved to disk");
                s.Set(Settings.Keys.UsualGlow, false);
                Expect.Equal(null, s.Usual(FileKind.Pdf), "the glow can be switched off");
            }
        }
    }
}

namespace DragShift.Tests
{
    static class PrintTests
    {
        const double Mm = 72 / 25.4;
        static readonly Size2 A4 = PrintPreset.Size(Paper.A4).Value;

        [Test]
        static void FitTurnsALandscapePhotoOnPortraitPaper()
        {
            var r = PrintLayout.Place(new Size2(4000, 3000), A4, PrintFit.Fit, out bool rotate);
            Expect.True(rotate, "turned to use the page");
            Expect.True(r.X >= PrintLayout.Margin - 0.01 && r.Right <= A4.Width - PrintLayout.Margin + 0.01, "inside the margins");
            Expect.True(Math.Abs(r.Height / r.Width - 4.0 / 3.0) < 0.001, "keeps the shape, turned");
            var actual = PrintLayout.Place(new Size2(35 * Mm, 45 * Mm), A4, PrintFit.Actual, out bool turned);
            Expect.True(!turned && Math.Abs(actual.Width - 35 * Mm) < 0.01, "actual size stays real");
        }

        [Test]
        static void PassportPhotosPerSheet()
        {
            var photo = PrintLayout.RealSize(413, 531, 300, 300);
            Expect.True(Math.Abs(photo.Width / Mm - 35) < 0.2 && Math.Abs(photo.Height / Mm - 45) < 0.2, "413 × 531 at 300 dpi is 3.5 × 4.5 cm");
            var six = PrintLayout.Grid(photo, PrintPreset.Size(Paper.Photo4x6).Value, out Size2 sheet);
            Expect.Equal(8, six.Count, "eight on a 4×6");
            Expect.True(sheet.Width > sheet.Height, "the 4×6 turns sideways for eight");
            foreach (var b in six) Expect.True(b.X >= 0 && b.Y >= 0 && b.Right <= sheet.Width && b.Bottom <= sheet.Height, "on the sheet");
            Expect.Equal(30, PrintLayout.Grid(photo, A4, out _).Count, "thirty on A4");
            Expect.Equal(0, PrintLayout.Grid(new Size2(500, 700), A4, out _).Count, "a big picture doesn't repeat");
        }

        [Test]
        static void PresetsSurviveTheSettingsFile()
        {
            var mine = PrintPreset.BuiltIns[1].Copy();
            mine.Id = "x";
            mine.Name = "Office | laser; 2nd floor";
            mine.Printer = "HP LaserJet (Room 2)";
            mine.Copies = 3;
            var back = PrintPreset.ParseList(PrintPreset.Join(new[] { PrintPreset.BuiltIns[2], mine }));
            Expect.Equal(2, back.Count, "both come back");
            Expect.Equal("Office | laser; 2nd floor", back[1].Name, "odd characters in names survive");
            Expect.True(back[1].SameSettings(mine) && back[0].SameSettings(PrintPreset.BuiltIns[2]), "same settings");
            Expect.Equal("x", PrintPreset.IdFromStep(mine.StepId), "step id");
            Expect.Equal("EPSON (default) · 4 × 6 in photo · Colour · Fill the sheet", PrintPreset.BuiltIns[2].Summary("EPSON"), "summary");
            using (var box = new Sandbox())
            {
                var s = new Settings(Path.Combine(box.Dir, "settings.ini"));
                Expect.Equal(4, s.PrintPresets.Count, "built-ins until changed");
                s.PrintPresets = new List<PrintPreset> { mine };
                var again = new Settings(Path.Combine(box.Dir, "settings.ini"));
                Expect.Equal("Office | laser; 2nd floor", again.PrintPresetFor("print.preset.x").Name, "saved and found by step");
            }
        }

        [Test]
        static void PrintSitsOnTheFilesPage()
        {
            List<string> Ids(string f) => Catalog.Pages(new[] { f }, Capabilities.All).SelectMany(p => p.Slices).Select(s => s.Id).ToList();
            Expect.True(Ids("C:/a/Photo.jpg").Contains("file.print") && Ids("C:/a/Marks.pdf").Contains("file.print"), "pictures and PDFs");
            Expect.True(!Ids("C:/a/Clip.mp4").Contains("file.print"), "not videos");
            var preset = PrintPreset.BuiltIns[2];
            var fav = Catalog.Pages(new[] { "C:/a/Photo.jpg" }, Capabilities.All, favorites: new[] { preset.StepId }, printPresets: PrintPreset.BuiltIns);
            Expect.Equal("favorites", fav[0].Id, "a preset can be a favourite");
            Expect.Equal("Passport 4×6", fav[0].Slices[0].Title, "named after the preset");
            foreach (var f in new[] { "C:/a/Photo.jpg", "C:/a/Marks.pdf" })
                foreach (var p in Catalog.Pages(new[] { f }, Capabilities.All)) Expect.True(p.Slices.Count <= Catalog.MaxSlices, "at most 8");
        }

        [Test]
        static void PrintPageHoldsThePresetsToDropOn()
        {
            var mine = Enumerable.Range(1, 9).Select(i => new PrintPreset { Id = "p" + i, Name = "Preset " + i }).ToList();
            var pages = Catalog.Pages(new[] { "C:/a/Marks.pdf" }, Capabilities.All, new HashSet<string> { "print.preset.p1" }, printPresets: mine);
            var ids = pages.Select(p => p.Id).ToList();
            var print = pages.FirstOrDefault(p => p.Id == "print");
            Expect.True(print != null, "a Print page");
            Expect.Equal(ids.IndexOf("share") - 1, ids.IndexOf("print"), "just before Files");
            Expect.Equal(Catalog.MaxSlices, print.Slices.Count, "full");
            Expect.Equal("Preset 2", print.Slices[0].Title, "hidden presets stay off it");
            Expect.Equal("file.print", print.Slices[print.Slices.Count - 1].Id, "the last slice opens the print window");
            Expect.True(!Catalog.Pages(new[] { "C:/a/Clip.mp4" }, Capabilities.All, printPresets: mine).Any(p => p.Id == "print"), "not for videos");
            using (var box = new Sandbox())
            {
                var s = new Settings(Path.Combine(box.Dir, "settings.ini"));
                s.ShowPreset(PrintPreset.BuiltIns[1], false);
                Expect.Equal(3, s.ShownPrintPresets.Count, "switched off in the list");
                Expect.True(s.Hidden.Contains("print.preset.bw"), "shares the ring's hidden set");
                s.ShowPreset(PrintPreset.BuiltIns[1], true);
                Expect.Equal(4, new Settings(Path.Combine(box.Dir, "settings.ini")).ShownPrintPresets.Count, "back on, and saved");
            }
        }
    }

    static class TipTests
    {
        static ulong Rol(ulong x, int n) => n == 0 ? x : (x << n) | (x >> (64 - n));

        /// <summary>Keccak-256 (Ethereum's hash; not the same as SHA3-256), only to run the address's own spelling check.</summary>
        static byte[] Keccak256(byte[] data)
        {
            ulong[] rc = { 0x1, 0x8082, 0x800000000000808A, 0x8000000080008000, 0x808B, 0x80000001, 0x8000000080008081,
                           0x8000000000008009, 0x8A, 0x88, 0x80008009, 0x8000000A, 0x8000808B, 0x800000000000008B,
                           0x8000000000008089, 0x8000000000008003, 0x8000000000008002, 0x8000000000000080, 0x800A,
                           0x800000008000000A, 0x8000000080008081, 0x8000000000008080, 0x80000001, 0x8000000080008008 };
            int[,] rot = { { 0, 36, 3, 41, 18 }, { 1, 44, 10, 45, 2 }, { 62, 6, 43, 15, 61 }, { 28, 55, 25, 21, 56 }, { 27, 20, 39, 8, 14 } };
            var p = new List<byte>(data) { 1 };
            while (p.Count % 136 != 0) p.Add(0);
            p[p.Count - 1] |= 0x80;
            var a = new ulong[5, 5];
            for (int block = 0; block < p.Count; block += 136)
            {
                for (int i = 0; i < 17; i++)
                {
                    ulong word = 0;
                    for (int b = 0; b < 8; b++) word |= (ulong)p[block + 8 * i + b] << (8 * b);
                    a[i % 5, i / 5] ^= word;
                }
                foreach (var round in rc)
                {
                    var c = new ulong[5]; var d = new ulong[5]; var bb = new ulong[5, 5];
                    for (int x = 0; x < 5; x++) c[x] = a[x, 0] ^ a[x, 1] ^ a[x, 2] ^ a[x, 3] ^ a[x, 4];
                    for (int x = 0; x < 5; x++) d[x] = c[(x + 4) % 5] ^ Rol(c[(x + 1) % 5], 1);
                    for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++) a[x, y] ^= d[x];
                    for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++) bb[y, (2 * x + 3 * y) % 5] = Rol(a[x, y], rot[x, y]);
                    for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++) a[x, y] = bb[x, y] ^ (~bb[(x + 1) % 5, y] & bb[(x + 2) % 5, y]);
                    a[0, 0] ^= round;
                }
            }
            var hash = new byte[32];
            for (int i = 0; i < 4; i++) for (int b = 0; b < 8; b++) hash[8 * i + b] = (byte)(a[i % 5, i / 5] >> (8 * b));
            return hash;
        }

        static string Checksummed(string address)
        {
            var hex = address.Substring(2).ToLowerInvariant();
            var hash = string.Concat(Keccak256(Encoding.ASCII.GetBytes(hex)).Select(b => b.ToString("x2")));
            return "0x" + string.Concat(hex.Select((ch, i) => Convert.ToInt32(hash[i].ToString(), 16) >= 8 ? char.ToUpperInvariant(ch) : ch));
        }

        [Test]
        static void TheTipAddressPassesItsOwnSpellingCheck()
        {
            Expect.Equal("c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470",
                         string.Concat(Keccak256(new byte[0]).Select(b => b.ToString("x2"))), "the hash itself is right");
            Expect.Equal("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed", Checksummed("0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed"), "the check itself is right");
            Expect.Equal(42, Tip.Address.Length, "address length");
            Expect.Equal(Tip.Address, Checksummed(Tip.Address), "a letter in the tip address has changed");
        }

        [Test]
        static void TheLineAlwaysShowsTheCountAndAsksOnlyAfterTheAppHasWorked()
        {
            Expect.Equal("DragShift has fixed 0 files for you so far. When it earns a coffee…", Tip.Line(0), "nothing made yet");
            Expect.Equal("DragShift has fixed 1 file for you. If that was worth a coffee…", Tip.Line(1), "one file");
            Expect.Equal("DragShift has fixed 214 files for you. If that was worth a coffee…", Tip.Line(214), "214 files");
            Expect.True(Tip.Line(12345).Contains("12,345 files") && Tip.Line(-3).Contains("0 files"), "big and bad numbers");
        }

        [Test]
        static void TheLineComesInThreePiecesSoTheCountCanBeBold()
        {
            var none = Tip.LineParts(0);
            Expect.Equal("DragShift has fixed ", none.Before, "the start of the line");
            Expect.Equal("0 files", none.Count, "the count piece, even at 0");
            Expect.Equal(" for you so far. When it earns a coffee…", none.After, "the end of the line before any work");
            var many = Tip.LineParts(12345);
            Expect.Equal("12,345 files", many.Count, "the count piece with its thousands comma");
            Expect.Equal(" for you. If that was worth a coffee…", many.After, "the end of the line after some work");
            Expect.Equal("1 file", Tip.LineParts(1).Count, "one file, not files");
            Expect.Equal("0 files", Tip.LineParts(-3).Count, "a bad number counts as 0");
            foreach (long n in new long[] { -3, 0, 1, 214, 12345 })
                Expect.Equal(Tip.Line(n), Tip.LineParts(n).Before + Tip.LineParts(n).Count + Tip.LineParts(n).After, "the pieces put back together are the line, for " + n);
        }

        [Test]
        static void TheCupPicksTheSliceItIsOverAndTheTallyAddsUp()
        {
            Expect.Equal(-1, Tip.Slice(0, 10), "still on the saucer");
            Expect.True(Tip.Slice(-55, 25) == 0 && Tip.Slice(0, 60) == 1 && Tip.Slice(55, 25) == 2, "small, medium, large");
            Expect.Equal(-1, Tip.Slice(0, -60), "above the ring");
            Expect.Equal("Medium coffee · $5", Tip.Title(1), "title");
            var path = Path.Combine(Path.GetTempPath(), "dragshift-tally-" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                var s = new Settings(path);
                s.AddFixed(3); s.AddFixed(0); s.AddFixed(-5); s.AddFixed(1);
                Expect.Equal(4L, new Settings(path).Fixed, "the count is kept");
            }
            finally { File.Delete(path); }
        }
    }
}
