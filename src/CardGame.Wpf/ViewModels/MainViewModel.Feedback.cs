using System.Collections.ObjectModel;
using System.Windows;
using CardGame.Core;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private int _feedbackEventCursor;
    private long _feedbackRevision;
    private bool _isMotionEnabled = SystemParameters.ClientAreaAnimation;

    public ObservableCollection<BattleCue> BattleCues { get; } = [];
    public long FeedbackRevision
    {
        get => _feedbackRevision;
        private set => SetProperty(ref _feedbackRevision, value);
    }
    public bool IsMotionEnabled
    {
        get => _isMotionEnabled;
        set { if (SetProperty(ref _isMotionEnabled, value)) { QueueAutoSave(); QueuePreferencesSave(); } }
    }

    private void ResetBattleFeedback()
    {
        BattleCues.Clear();
        FeedbackRevision = _game.Revision;
        _feedbackEventCursor = _game.Events.Count;
        ResetAudioFeedback();
    }

    private void CaptureBattleFeedback(GameSnapshot snapshot)
    {
        var events = _game.Events;
        var start = _feedbackEventCursor;
        _feedbackEventCursor = events.Count;
        if (_initializing) return;
        FeedbackRevision = snapshot.Revision;
        var committed = events.Skip(start).ToArray();
        RecordPublicPlays(committed, snapshot);
        var cues = BattleCueProjector.Project(committed, snapshot);
        foreach (var cue in cues)
        {
            BattleCues.Add(cue);
            while (BattleCues.Count > 32) BattleCues.RemoveAt(0);
        }
        PublishGameSounds(cues, snapshot);
        if (Audio.GeneralVoiceProjector.Project(committed, snapshot.Players, id => GetGeneralPortrait(id).SkinId) is { } voice)
            VoiceRequested?.Invoke(this, new(voice));
    }

    private void RecordPublicPlays(IEnumerable<EventEnvelope> events, GameSnapshot? snapshot = null)
    {
        // The current use stays at the left while committed responses arrive on its right.
        var plays = BattleCueProjector.ProjectCardPlays(events, snapshot ?? _snapshot);
        var lastUse = plays.ToList().FindLastIndex(cue => cue.Kind == BattleCueKind.Card && !cue.IsUseCompletion);
        foreach (var cue in plays.Skip(Math.Max(0, lastUse)))
        {
            if (cue.Kind == BattleCueKind.Card)
            {
                // A rescue completion can arrive after its declaration in another command.
                if (cue.IsUseCompletion && RecentPlays.Any(play => play.PublicCardId == cue.PublicCardId &&
                    play.SourceSeat == cue.SourceSeat && play.Kind == cue.CardKind && play.ActionLabel == cue.CardActionLabel)) continue;
                RecentPlays.Clear();
            }
            RecentPlays.Add(new TablePlayViewModel(cue.Sequence,
                cue.CardKind is { } kind ? CardCatalog.Get(kind).DisplayName : cue.Label, cue.ActorName)
            {
                Kind = cue.CardKind, SourceSeat = cue.SourceSeat,
                PublicCardId = cue.PublicCardId, ActionLabel = cue.CardActionLabel
            });
            while (RecentPlays.Count > 5) RecentPlays.RemoveAt(RecentPlays[0].ActionLabel == "打出" ? 0 : 1);
        }
    }
}
