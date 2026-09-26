using System.Collections.ObjectModel;
using System.Windows;
using CardGame.Core;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private int _feedbackEventCursor;
    private bool _isMotionEnabled = SystemParameters.ClientAreaAnimation;

    public ObservableCollection<BattleCue> BattleCues { get; } = [];
    public bool IsMotionEnabled
    {
        get => _isMotionEnabled;
        set { if (SetProperty(ref _isMotionEnabled, value)) { QueueAutoSave(); QueuePreferencesSave(); } }
    }

    private void ResetBattleFeedback()
    {
        BattleCues.Clear();
        _feedbackEventCursor = _game.Events.Count;
        ResetAudioFeedback();
    }

    private void CaptureBattleFeedback()
    {
        var events = _game.Events;
        var start = _feedbackEventCursor;
        _feedbackEventCursor = events.Count;
        if (_initializing) return;
        var committed = events.Skip(start).ToArray();
        RecordPublicPlays(committed);
        var cues = BattleCueProjector.Project(committed, _snapshot, _game.RulesVersion);
        foreach (var cue in cues)
        {
            BattleCues.Add(cue);
            while (BattleCues.Count > 32) BattleCues.RemoveAt(0);
        }
        PublishGameSounds(cues);
        if (Audio.GeneralVoiceProjector.Project(committed, _snapshot.Players, id => GetGeneralPortrait(id).SkinId) is { } voice)
            VoiceRequested?.Invoke(this, new(voice));
    }

    private void RecordPublicPlays(IEnumerable<EventEnvelope> events)
    {
        // Card names come from the effective declared kind, including conversions and recovery cards.
        var publicEvents = events.Where(item => item.Payload is CardUseDeclaredEvent or CardRecastEvent).TakeLast(3).ToArray();
        foreach (var cue in BattleCueProjector.Project(publicEvents, _snapshot))
        {
            var recast = publicEvents.Single(item => item.Sequence == cue.Sequence).Payload as CardRecastEvent;
            RecentPlays.Insert(0, new TablePlayViewModel(cue.Sequence,
                recast is null ? cue.Label : CardCatalog.Get(recast.CardKind).DisplayName,
                recast is null ? cue.ActorName : $"{cue.ActorName} · 重铸"));
            while (RecentPlays.Count > 3) RecentPlays.RemoveAt(RecentPlays.Count - 1);
        }
    }
}
