namespace BlackHoleSim.Core.Rendering;

/// <summary>
/// Writes RGB images in Netpbm PPM (P3) format.
/// </summary>
public static class PPMWriter
{
    public static void WriteHeader(StreamWriter sw, int width, int height)
    {
        sw.WriteLine("P3");
        sw.WriteLine($"{width} {height}");
        sw.WriteLine("255");
    }

    public static void WritePixel(StreamWriter sw, byte r, byte g, byte b)
        => sw.WriteLine($"{r} {g} {b}");
}
