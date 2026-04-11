using BlackHoleSim.ConsoleApp.UI;
using BlackHoleSim.Core.Rendering;

namespace BlackHoleSim.ConsoleApp;

internal static class Program
{
    private static void Main(string[] args)
    {
        string path   = args.Length > 0 ? args[0] : "blackhole.ppm";
        int    width  = args.Length > 1 && int.TryParse(args[1], out int w) ? w : 800;
        int    height = args.Length > 2 && int.TryParse(args[2], out int h) ? h : 600;
        double bMax   = args.Length > 3 && double.TryParse(args[3], out double b) ? b : 10.0;

        Console.WriteLine($"BlackHoleSim — Schwarzschild raytracer");
        Console.WriteLine($"Resolution : {width}x{height}   bMax={bMax}   → {path}");
        Console.WriteLine();

        using var bar = new ConsoleProgressBar();
        bar.Reset();

        Raytracer.RenderPPM(path, width, height, bMax, bar);

        bar.Complete();
        Console.WriteLine($"Done. Saved: {Path.GetFullPath(path)}");
    }
}
