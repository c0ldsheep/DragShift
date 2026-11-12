using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DragShift.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace DragShift.Engine
{
    /// <summary>Video and audio through Windows' own Media Foundation encoders (hardware-accelerated where the PC has it).</summary>
    static class Media
    {
        static uint Even(double v) => (uint)Math.Max(2, Math.Round(v / 2) * 2);

        /// <summary>Converts to MP4 / M4A / MP3 / WAV. Compress = 720p MP4 at a modest bitrate; mute = no sound.</summary>
        public static async Task<string> Convert(string path, string ext, string suffix, bool compress, bool mute, string dir,
                                                 IProgress<double> progress, CancellationToken ct)
        {
            var input = await StorageFile.GetFileFromPathAsync(path);
            MediaEncodingProfile profile;
            switch (ext)
            {
                case "m4a": profile = MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High); break;
                case "mp3": profile = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High); break;
                case "wav": profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High); break;
                default:
                {
                    profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
                    MediaEncodingProfile source = null;
                    try { source = await MediaEncodingProfile.CreateFromFileAsync(input); } catch (Exception) { }
                    var v = source?.Video;
                    if (v != null && v.Width > 0 && v.Height > 0)
                    {
                        Images.RequireSafeSize(v.Width, v.Height, Path.GetFileName(path) + "'s video frame");
                        // Keep the shape (phones film portrait); compress caps the long side at 1280.
                        double s = compress ? Math.Min(1, 1280.0 / Math.Max(v.Width, v.Height)) : 1;
                        profile.Video.Width = Even(v.Width * s);
                        profile.Video.Height = Even(v.Height * s);
                        Images.RequireSafeSize(profile.Video.Width, profile.Video.Height, "The converted video frame");
                        if (v.FrameRate.Numerator > 0 && v.FrameRate.Denominator > 0)
                        {
                            profile.Video.FrameRate.Numerator = v.FrameRate.Numerator;
                            profile.Video.FrameRate.Denominator = v.FrameRate.Denominator;
                        }
                        double fps = v.FrameRate.Denominator > 0 ? v.FrameRate.Numerator / (double)v.FrameRate.Denominator : 30;
                        double bitsPerPixel = compress ? 0.07 : 0.15;
                        uint target = (uint)Math.Min(40_000_000, profile.Video.Width * profile.Video.Height * Math.Max(1, fps) * bitsPerPixel);
                        profile.Video.Bitrate = v.Bitrate > 0 ? Math.Min(target, compress ? (uint)(v.Bitrate * 0.6) : v.Bitrate) : target;
                    }
                    else if (compress)
                    {
                        profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
                    }
                    if (mute) profile.Audio = null;
                    break;
                }
            }
            var outPath = Naming.Output(path, ext, suffix, dir);
            var folder = await StorageFolder.GetFolderFromPathAsync(dir);
            var output = await folder.CreateFileAsync(Path.GetFileName(outPath), CreationCollisionOption.GenerateUniqueName);
            try
            {
                var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
                var prepared = await transcoder.PrepareFileTranscodeAsync(input, output, profile);
                if (!prepared.CanTranscode)
                    throw new UserError("Windows can't convert " + Path.GetFileName(path) + " (" + prepared.FailureReason + ").");
                var op = prepared.TranscodeAsync();
                op.Progress = (info, percent) => progress?.Report(percent / 100);
                await op.AsTask(ct);
                return output.Path;
            }
            catch (Exception)
            {
                // The half-written file is ours and useless: remove it for good. (Plain DeleteAsync() sends it to the Recycle Bin.)
                try { await output.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch (Exception) { }
                throw;
            }
        }

        /// <summary>A frame from about a second in (or the middle of a short clip), as a JPG.</summary>
        public static async Task<string> Still(string path, string dir, double quality)
        {
            var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
            var video = clip.GetVideoEncodingProperties();
            Images.RequireSafeSize(video?.Width ?? 0, video?.Height ?? 0, Path.GetFileName(path) + "'s video frame");
            var composition = new MediaComposition();
            composition.Clips.Add(clip);
            var at = TimeSpan.FromSeconds(Math.Min(1, clip.OriginalDuration.TotalSeconds / 2));
            using (var stream = await composition.GetThumbnailAsync(at, 0, 0, VideoFramePrecision.NearestFrame))
            {
                var dec = await BitmapDecoder.CreateAsync(stream);
                Images.RequireSafeSize(dec.PixelWidth, dec.PixelHeight, "This video frame");
                var data = await dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
                                                       ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                using (Bitmap frame = Images.FromBgra(data.DetachPixelData(), (int)dec.PixelWidth, (int)dec.PixelHeight))
                {
                    var outPath = Naming.Output(path, "jpg", "Still", dir);
                    await Images.Save(frame, outPath, Core.ImageFormat.Jpg, quality);
                    return outPath;
                }
            }
        }
    }
}
