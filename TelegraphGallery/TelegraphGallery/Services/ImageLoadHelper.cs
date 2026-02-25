using System.IO;
using ImageMagick;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TelegraphGallery.Services;

/// <summary>
/// Loads images using ImageSharp, falling back to Magick.NET for formats
/// ImageSharp does not support (e.g. HEIC/HEIF).
/// </summary>
internal static class ImageLoadHelper
{
    /// <summary>
    /// Loads an image from <paramref name="filePath"/>.
    /// Tries ImageSharp first; on <see cref="UnknownImageFormatException"/>
    /// converts the file via Magick.NET and retries.
    /// </summary>
    internal static Image Load(string filePath)
    {
        try
        {
            return Image.Load(filePath);
        }
        catch (UnknownImageFormatException)
        {
            return LoadViaMagick(filePath);
        }
    }

    private static Image LoadViaMagick(string filePath)
    {
        using var magickImage = new MagickImage(filePath);
        magickImage.Format = MagickFormat.Png;

        using var ms = new MemoryStream();
        magickImage.Write(ms);
        ms.Position = 0;

        return Image.Load<Rgba32>(ms);
    }
}
