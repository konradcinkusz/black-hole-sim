namespace BlackHoleSim.ConsoleApp.UI;

/// <summary>
/// Thread-safe console progress bar. Implements <see cref="IProgress{T}"/> of double (0.0–1.0).
/// </summary>
public sealed class ConsoleProgressBar : IProgress<double>, IDisposable
{
    private const int BarWidth = 40;

    private readonly object _lock = new();
    private double _lastValue;
    private bool _done;

    public void Reset()
    {
        lock (_lock)
        {
            _lastValue = 0;
            _done      = false;
            Console.Write($"\r[{new string(' ', BarWidth)}]   0%");
        }
    }

    public void Report(double value)
    {
        lock (_lock)
        {
            if (_done) return;
            _lastValue = value;
            int filled = (int)(value * BarWidth);
            int pct    = (int)(value * 100);
            Console.Write($"\r[{new string('#', filled)}{new string(' ', BarWidth - filled)}] {pct,3}%");
        }
    }

    public void Complete()
    {
        lock (_lock)
        {
            _done = true;
            Console.Write($"\r[{new string('#', BarWidth)}] 100%");
            Console.WriteLine();
        }
    }

    public void Dispose() { }
}
