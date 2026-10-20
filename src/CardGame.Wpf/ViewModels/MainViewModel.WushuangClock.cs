using System.Diagnostics;
using System.Windows.Threading;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Stopwatch _wushuangClockElapsed = new();
    private DispatcherTimer? _wushuangClockTimer;
    private long _wushuangClockCommittedMilliseconds;
    private bool _wushuangClockSubmitting;
    private long _wushuangShopOpenedAt;

    private bool ShouldRunWushuangClock => !_initializing && !_disposed && _sessionActivated &&
        _game is { IsFaulted: false } && _game.IsWushuangClockRunning;
    private long WushuangVisibleRemainingMilliseconds => Math.Max(0,
        (_game?.WushuangRemainingMilliseconds ?? WushuangMode.TimeLimitMilliseconds) -
        Math.Max(0, _wushuangClockElapsed.ElapsedMilliseconds - _wushuangClockCommittedMilliseconds));
    public int WushuangShopSecondsRemaining => Math.Max(0, (IsWushuangOpening ? 15 : 20) - (int)Stopwatch.GetElapsedTime(_wushuangShopOpenedAt).TotalSeconds);
    public string WushuangShopTimeoutText => $"剩余 {WushuangShopSecondsRemaining} 秒";
    private static string FormatWushuangTime(long milliseconds)
    {
        var seconds = (long)Math.Ceiling(milliseconds / 1000d);
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private void InitializeWushuangClock()
    {
        _wushuangClockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _wushuangClockTimer.Tick += OnWushuangClockTick;
        SyncWushuangClockAfterCommand();
    }

    private void ResetWushuangClockForEngine()
    {
        _wushuangClockTimer?.Stop();
        _wushuangClockElapsed.Reset();
        _wushuangClockCommittedMilliseconds = 0;
        CloseWushuangShop();
        SyncWushuangClockAfterCommand();
    }

    private void SyncWushuangClockAfterCommand()
    {
        if (_wushuangClockSubmitting) return;
        if (!ShouldRunWushuangClock)
        {
            _wushuangClockTimer?.Stop();
            _wushuangClockElapsed.Stop();
            return;
        }
        _wushuangClockElapsed.Start();
        if (_wushuangClockTimer is { } timer)
            timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(WushuangVisibleRemainingMilliseconds, 1, 1000));
        if (_wushuangClockTimer is { IsEnabled: false }) _wushuangClockTimer.Start();
    }

    private void OnWushuangClockTick(object? sender, EventArgs args) => ExecuteSafely(() =>
    {
        // Redraw every second; only journal an unattended clock debit every five seconds.
        if (_wushuangClockElapsed.ElapsedMilliseconds - _wushuangClockCommittedMilliseconds >= 5000 || WushuangVisibleRemainingMilliseconds <= 0)
            FlushWushuangClock();
        if (IsWushuangShopOpen && IsWushuangShopInteractive && WushuangShopSecondsRemaining == 0)
        {
            if (IsWushuangOpening && WushuangOffers.FirstOrDefault(offer => offer.IsEnabled) is { } offer) BuyWushuangOffer(offer);
            else FinishWushuangShopping();
        }
        RaisePropertyChanged(nameof(WushuangClockText));
        RaisePropertyChanged(nameof(WushuangClockCaption));
        RaisePropertyChanged(nameof(WushuangShopTimeoutText));
        RaisePropertyChanged(nameof(WushuangShopSecondsRemaining));
    });

    private GameCommand PrepareWushuangClockCommand(GameCommand command)
    {
        if (_wushuangClockSubmitting || command is TickWushuangClockCommand || !ShouldRunWushuangClock ||
            command.ExpectedRevision != _game.Revision) return command;
        FlushWushuangClock();
        return command with { ExpectedRevision = _game.Revision };
    }

    private void FlushWushuangClock()
    {
        if (_wushuangClockSubmitting) return;
        SyncWushuangClockAfterCommand();
        if (!ShouldRunWushuangClock || _game.WushuangRemainingMilliseconds is not { } remaining || remaining <= 0) return;
        var now = _wushuangClockElapsed.ElapsedMilliseconds;
        var pending = now - _wushuangClockCommittedMilliseconds;
        if (pending <= 0) return;
        var previousBaseline = _wushuangClockCommittedMilliseconds;
        _wushuangClockCommittedMilliseconds = now;
        _wushuangClockSubmitting = true;
        try
        {
            var result = SubmitCommand(new TickWushuangClockCommand((int)Math.Min(pending, remaining), _game.Revision));
            if (!result.Accepted) _wushuangClockCommittedMilliseconds = previousBaseline;
            RefreshCommandResult(result.State);
        }
        finally
        {
            _wushuangClockSubmitting = false;
            SyncWushuangClockAfterCommand();
        }
    }

    private void DisposeWushuangClock()
    {
        _wushuangClockElapsed.Stop();
        if (_wushuangClockTimer is null) return;
        _wushuangClockTimer.Stop();
        _wushuangClockTimer.Tick -= OnWushuangClockTick;
        _wushuangClockTimer = null;
    }
}
