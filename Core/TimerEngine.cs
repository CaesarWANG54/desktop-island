namespace DesktopIsland.Core;

public class TimerState
{
    public double CountdownSeconds { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public bool Alarm { get; set; }
    public double StopwatchSeconds { get; set; }
    public DateTimeOffset? StopwatchStarted { get; set; }
}

public sealed class TimerEngine
{
    private readonly Func<DateTimeOffset> _now;
    public TimerState State { get; }
    public event Action? Changed;
    public event Action? Elapsed;
    public TimerEngine(TimerState state, Func<DateTimeOffset>? now = null) { State = state; _now = now ?? (() => DateTimeOffset.UtcNow); }
    public double Remaining => Math.Max(0, State.Deadline is { } d ? (d - _now()).TotalSeconds : State.CountdownSeconds);
    public double StopwatchElapsed => State.StopwatchSeconds + (State.StopwatchStarted is { } s ? Math.Max(0, (_now() - s).TotalSeconds) : 0);
    public bool CountdownActive => State.Deadline != null || State.CountdownSeconds > 0 || State.Alarm;
    public void StartCountdown(double seconds)
    {
        State.CountdownSeconds = Math.Clamp(seconds, 1, 7 * 86400);
        State.Deadline = _now().AddSeconds(State.CountdownSeconds); State.Alarm = false; Changed?.Invoke();
    }
    public void PauseCountdown()
    {
        State.CountdownSeconds = Remaining; State.Deadline = null; Changed?.Invoke();
    }
    public void ResumeCountdown() { if (State.CountdownSeconds > 0) StartCountdown(State.CountdownSeconds); }
    public void ResetCountdown() { State.CountdownSeconds = 0; State.Deadline = null; State.Alarm = false; Changed?.Invoke(); }
    public void ToggleStopwatch()
    {
        if (State.StopwatchStarted == null) State.StopwatchStarted = _now();
        else { State.StopwatchSeconds = StopwatchElapsed; State.StopwatchStarted = null; }
        Changed?.Invoke();
    }
    public void ResetStopwatch() { State.StopwatchSeconds = 0; State.StopwatchStarted = null; Changed?.Invoke(); }
    public void Tick()
    {
        if (State.Deadline != null && Remaining <= 0)
        {
            State.Deadline = null; State.CountdownSeconds = 0; State.Alarm = true;
            Changed?.Invoke(); Elapsed?.Invoke();
        }
    }
    public static string Format(double seconds)
    {
        var s = (long)Math.Ceiling(Math.Max(0, seconds));
        return s >= 3600 ? $"{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
    }
}
