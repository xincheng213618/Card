using System.ComponentModel;
using System.Windows.Threading;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private ParticipantPromptHost? _participantHost;

    private void AttachParticipantPromptHost()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var host = ParticipantPromptHost.Attach(_game,
            action => { if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(DispatcherPriority.Background, action); },
            command =>
            {
                var result = SubmitCommand(command);
                RefreshCommandResult(result.State);
                return result;
            },
            () => CanUseAutomaticProgress);
        _participantHost = host;
        host.Updated += RefreshParticipantStepAvailability;
        host.Failed += OnParticipantHostFailed;
        PropertyChanged += OnParticipantPresentationChanged;
    }

    private void DetachParticipantPromptHost()
    {
        PropertyChanged -= OnParticipantPresentationChanged;
        if (_participantHost is not { } host) return;
        _participantHost = null;
        host.Updated -= RefreshParticipantStepAvailability;
        host.Failed -= OnParticipantHostFailed;
        host.Dispose();
    }

    private void OnParticipantPresentationChanged(object? sender, PropertyChangedEventArgs args) =>
        _participantHost?.RequestProgress();

    private void RefreshParticipantStepAvailability()
    {
        if (_participantHost is { IsAcceptingInput: true } host)
            CanStepAi = host.HasReadyResponse;
    }

    private void OnParticipantHostFailed(Exception error) =>
        ExecuteSafely(() => throw new InvalidOperationException("独立响应处理失败，请读取存档恢复。", error));

    private bool TryStepParticipantResponse()
    {
        if (_participantHost is not { IsAcceptingInput: true } host) return false;
        host.PumpOne();
        RefreshParticipantStepAvailability();
        return true;
    }
}
