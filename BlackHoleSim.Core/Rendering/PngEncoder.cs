using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;

namespace BlackHoleSim.Core.Rendering;

/// <summary>
/// Encodes a raw RGB24 byte buffer (row-major) to PNG bytes using ImageSharp.
/// </summary>
public static class PngEncoder
{
    /// <summary>
    /// Encodes <paramref name="rgb"/> (length = width * height * 3, R G B order) to a PNG byte array.
    /// </summary>
    public static byte[] EncodeRgb24(byte[] rgb, int width, int height)
    {
        using var image = Image.LoadPixelData<Rgb24>(rgb, width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return ms.ToArray();
    }
}
