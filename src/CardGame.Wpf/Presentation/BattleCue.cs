using CardGame.Core;

namespace CardGame.Wpf.Presentation;

public enum BattleCueKind { Card, Response, Damage, Recovery, Turn, Dying, Death }

/// <summary>Presentation data containing only actions and values that are already public.</summary>
public sealed record BattleCue(long Sequence, BattleCueKind Kind, int SourceSeat,
    IReadOnlyList<int> TargetSeats, string Label, string ActorName, DamageNature Nature = DamageNature.Normal);

public static class BattleCueProjector
{
    public static IReadOnlyList<BattleCue> Project(IEnumerable<EventEnvelope> source, GameSnapshot playerView)
    {
        var events = source.ToArray();
        var targets = events.Select(item => item.Payload).OfType<TargetsConfirmedEvent>()
            .GroupBy(item => item.ResolutionId).ToDictionary(group => group.Key, group => group.Last().TargetSeats);
        var cues = new List<BattleCue>();
        var responseCards = new HashSet<int>();
        string Name(int seat) => playerView.Players.FirstOrDefault(player => player.Seat == seat)?.GeneralName ?? "武将";
        IReadOnlyList<int> Seats(IEnumerable<int> seats) => Array.AsReadOnly(seats.Where(seat => seat >= 0 && seat < playerView.Players.Count).Distinct().ToArray());

        foreach (var envelope in events)
        {
            BattleCue? cue = envelope.Payload switch
            {
                CardRecastEvent recast => new(envelope.Sequence, BattleCueKind.Card, recast.ActorSeat, [],
                    $"重铸{CardCatalog.Get(recast.CardKind).DisplayName}", Name(recast.ActorSeat)),
                CardUseDeclaredEvent card => new(envelope.Sequence, BattleCueKind.Card, card.SourceSeat,
                    Seats(targets.GetValueOrDefault(card.ResolutionId) ?? []), CardCatalog.Get(card.CardKind).DisplayName, Name(card.SourceSeat)),
                DamageAppliedEvent damage when damage.Amount > 0 => new(envelope.Sequence, BattleCueKind.Damage, damage.SourceSeat,
                    Seats([damage.TargetSeat]), $"−{damage.Amount}", Name(damage.TargetSeat), damage.Nature),
                RecoveryAppliedEvent recovery when recovery.Amount > 0 => new(envelope.Sequence, BattleCueKind.Recovery, recovery.SourceSeat,
                    Seats([recovery.TargetSeat]), $"+{recovery.Amount}", Name(recovery.TargetSeat)),
                TurnStartedEvent turn => new(envelope.Sequence, BattleCueKind.Turn, turn.ActorSeat, [],
                    turn.ActorSeat == playerView.HumanSeat ? "轮 到 你 了" : $"{Name(turn.ActorSeat)}的回合", $"回合 {turn.TurnNumber}"),
                PlayerDyingEvent dying => new(envelope.Sequence, BattleCueKind.Dying, dying.VictimSeat, Seats([dying.VictimSeat]), "濒死 · 等待救援", Name(dying.VictimSeat)),
                PlayerDiedEvent died => new(envelope.Sequence, BattleCueKind.Death, died.VictimSeat, Seats([died.VictimSeat]), "阵 亡", Name(died.VictimSeat)),
                _ => null
            };

            // Do not project hand movements, private skill candidates, draws, or setup choices.
            // A response card is included only after the corresponding public response was committed.
            (int? Card, int Seat, CardKind? Kind) response = envelope.Payload switch
            {
                CardRespondedEvent card => (card.CardId, card.ResponderSeat, card.EffectiveCardKind),
                DuelResponseEvent { UsedSlash: true } duel => (duel.SlashCardId, duel.ResponderSeat, duel.ResponseCardKind ?? CardKind.Slash),
                GroupResponseEvent { UsedResponse: true } group => (group.ResponseCardId, group.ResponderSeat, group.ResponseCardKind ?? group.RequiredCardKind),
                DyingResponseEvent { UsedPeach: true } dying =>
                    (dying.PeachCardId, dying.ResponderSeat, dying.UsedPeachPhysicalCardKind ?? CardKind.Peach),
                DyingResponseEvent { UsedAlcohol: true } dying => (dying.AlcoholCardId, dying.ResponderSeat, CardKind.Alcohol),
                _ => (null, -1, null)
            };
            if (response.Card is { } cardId && responseCards.Add(cardId))
                cue = new(envelope.Sequence, BattleCueKind.Response, response.Seat, [],
                    response.Kind is { } kind ? $"打出{CardCatalog.Get(kind).DisplayName}" : "打出响应牌", Name(response.Seat));
            if (cue is not null) cues.Add(cue);
        }

        // A bulk run can commit many turns. Show the latest useful actions, keeping playback bounded.
        return cues.TakeLast(12).ToArray();
    }
}
