using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BestInScript.API.Overlay
{
    /// <summary>
    /// Decodes a build card's image file into a frozen <see cref="ImageSource"/>, downscaled
    /// at decode time to the size it will actually render at.
    ///
    /// Split out of <see cref="BuildCardWindow"/> because it needs no window, no HWND and no
    /// dispatcher — so unlike the rest of the overlay it is unit-testable, and the WPF
    /// imaging API has enough sharp edges (see the remarks) to be worth pinning.
    /// </summary>
    public static class BuildCardImageLoader
    {
        /// <summary>
        /// Load <paramref name="path"/>, downscaling to <paramref name="maxWidth"/> only if the
        /// source is wider. Returns a frozen image safe to hand to any thread. Throws on an
        /// unreadable or unsupported file — callers decide how to surface that.
        /// </summary>
        /// <remarks>
        /// Two WPF constraints are load-bearing here:
        /// <list type="bullet">
        /// <item><description><c>CacheOption</c> must be assigned BEFORE <c>StreamSource</c>;
        /// WPF reads it when the source is set, and OnLoad is what lets us drop the stream
        /// and <c>Freeze()</c> afterwards.</description></item>
        /// <item><description><c>BitmapCreateOptions.IgnoreImageCache</c> must NOT be set. WPF's
        /// image cache is keyed by URI, and a <c>StreamSource</c> has no URI — the flag makes
        /// <c>EndInit()</c> throw <c>ArgumentNullException("key")</c>. It is also pointless
        /// here: a stream source bypasses the URI cache entirely.</description></item>
        /// </list>
        /// </remarks>
        public static ImageSource Load(string path, int maxWidth)
        {
            var bytes = File.ReadAllBytes(path);
            var target = Math.Max(1, maxWidth);

            // Header-only read for the source width. Decode-time scaling must only ever
            // shrink — DecodePixelWidth would happily blow a small gear table up to the
            // panel's max width and render it soft.
            int sourceWidth;
            using (var probe = new MemoryStream(bytes, writable: false))
            {
                var decoder = BitmapDecoder.Create(
                    probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                sourceWidth = decoder.Frames[0].PixelWidth;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes, writable: false);
            if (sourceWidth > target) bmp.DecodePixelWidth = target;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}
