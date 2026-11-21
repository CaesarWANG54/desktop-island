using System.Diagnostics;

namespace DesktopIsland.Core;

// Successful presents only. Count intervals on the busiest current swapchain;
// never add secondary swapchains or reuse a chain from an earlier scene.
public sealed class FrameWindow(Func<double>? clock = null)
{
    private readonly Func<double> _clock = clock ?? (() => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency);
    private readonly object _gate = new();
    private readonly Dictionary<ulong, Queue<double>> _frames = [];
    private double _lastReceived, _lastTimestamp;
    public void Add(ulong chain, double timestamp)
    {
        if (!double.IsFinite(timestamp)) return;
        lock (_gate)
        {
            if (!_frames.TryGetValue(chain, out var queue)) _frames[chain] = queue = new();
            if (queue.Count > 0 && timestamp <= queue.Last()) return;
            queue.Enqueue(timestamp);
            while (queue.Count > 2 && queue.Peek() < timestamp - 1500) queue.Dequeue();
            _lastReceived = _clock();
            _lastTimestamp = timestamp;
        }
    }
    public double? Sample()
    {
        lock (_gate)
        {
            var cutoff = _lastTimestamp + Math.Max(0, _clock() - _lastReceived) - 1500;
            foreach (var pair in _frames.ToArray())
            {
                while (pair.Value.TryPeek(out var time) && time < cutoff) pair.Value.Dequeue();
                if (pair.Value.Count == 0) _frames.Remove(pair.Key);
            }
            var best = _frames.Values.OrderByDescending(q => q.Count).FirstOrDefault();
            if (best == null || best.Count < 3) return null;
            var span = best.Last() - best.Peek();
            return span > 0 ? 1000 * (best.Count - 1) / span : null;
        }
    }
}
