using System.Diagnostics;
using System.Windows.Threading;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Stopwatch _nianClockElapsed = new();
    private DispatcherTimer? _nianClockTimer;
    private long _nianClockCommittedMilliseconds;
    private bool _nianClockSubmitting;

    private bool ShouldRunNianClock => !_initializing && !_disposed && _sessionActivated &&
        _game is { IsFaulted: false } && _game.IsNianClockRunning;

    private void InitializeNianClock()
    {
        if (_nianClockTimer is not null) return;
        _nianClockTimer = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromSeconds(5) };
        _nianClockTimer.Tick += OnNianClockTick;
        SyncNianClockAfterCommand();
    }

    private void ResetNianClockForEngine()
    {
        _nianClockTimer?.Stop();
        _nianClockElapsed.Reset();
        _nianClockCommittedMilliseconds = 0;
        SyncNianClockAfterCommand();
    }

    private void SyncNianClockAfterCommand()
    {
        if (_nianClockSubmitting) return;
        if (!ShouldRunNianClock)
        {
            _nianClockTimer?.Stop();
            _nianClockElapsed.Stop();
            return;
        }
        _nianClockElapsed.Start();
        UpdateNianClockInterval();
        if (_nianClockTimer is { IsEnabled: false }) _nianClockTimer.Start();
    }

    private void UpdateNianClockInterval()
    {
        if (_nianClockTimer is null || _game.NianRemainingMilliseconds is not { } remaining) return;
        var uncommitted = Math.Max(0, _nianClockElapsed.ElapsedMilliseconds - _nianClockCommittedMilliseconds);
        // Do not postpone the deadline by a full five-second interval after a partial save flush.
        var interval = TimeSpan.FromMilliseconds(Math.Clamp(remaining - uncommitted, 1, 5000));
        if (_nianClockTimer.Interval != interval) _nianClockTimer.Interval = interval;
    }

    private void OnNianClockTick(object? sender, EventArgs args) => ExecuteSafely(FlushNianClock);

    private GameCommand PrepareNianClockCommand(GameCommand command)
    {
        // Stale user input must remain stale; only our own accepted clock debit may rebase it.
        if (_nianClockSubmitting || command is TickNianClockCommand || !ShouldRunNianClock ||
            command.ExpectedRevision != _game.Revision) return command;
        FlushNianClock();
        return command with { ExpectedRevision = _game.Revision };
    }

    private void FlushNianClock()
    {
        if (_nianClockSubmitting) return;
        SyncNianClockAfterCommand();
        if (!ShouldRunNianClock || _game.NianRemainingMilliseconds is not { } remaining || remaining <= 0) return;
        var now = _nianClockElapsed.ElapsedMilliseconds;
        var pending = now - _nianClockCommittedMilliseconds;
        if (pending <= 0) return;
        var amount = (int)Math.Min(pending, remaining);
        var previousBaseline = _nianClockCommittedMilliseconds;
        // SubmitCommand and StateChanged may call clock synchronization. Freeze the debit first.
        _nianClockCommittedMilliseconds = now;
        _nianClockSubmitting = true;
        try
        {
            var result = SubmitCommand(new TickNianClockCommand(amount, _game.Revision));
            if (!result.Accepted) _nianClockCommittedMilliseconds = previousBaseline;
            RefreshCommandResult(result.State);
        }
        finally
        {
            _nianClockSubmitting = false;
            SyncNianClockAfterCommand();
        }
    }

    private void DisposeNianClock()
    {
        _nianClockElapsed.Stop();
        if (_nianClockTimer is null) return;
        _nianClockTimer.Stop();
        _nianClockTimer.Tick -= OnNianClockTick;
        _nianClockTimer = null;
    }
}
