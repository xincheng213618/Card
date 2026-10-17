using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>An issued original pile entity, never a temporary Hand substitute.</summary>
public sealed record PindianOwnedPileSource(long OriginProgramFrameId,
    CardConversionSource Origin, PublicPersistentPileSource Pile, int CardId,
    int PrintedRank, long SequenceBefore);

public sealed partial record PindianFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PindianOwnedPileSource? OwnedPileSource { get; init; }
}

public sealed partial class GameEngine
{
    private bool HasPaidOwnedPilePindianSource(PindianFrame frame) => frame.OwnedPileSource is { } source &&
        _cardMovements.Count(m => m.Sequence > source.SequenceBefore && m.CardId == source.CardId &&
            m.From == source.Pile.Location && m.To == CardLocation.Processing &&
            m.Reason == CardMoveReasons.PindianReveal) == 1;

    private void AssertOwnedPilePindianSource(PindianFrame frame)
    {
        if (frame.OwnedPileSource is not { } source) return;
        if (source.OriginProgramFrameId != frame.ParentFrameId || source.Origin.OwnerSeat != frame.SourceSeat ||
            source.Pile.OwnerSeat != frame.SourceSeat || source.CardId != frame.SourceCardId ||
            frame.SourceUsesDrawPileTop || source.SequenceBefore < 0 || source.SequenceBefore > OutsidePhaseMovementSequence ||
            frame.OpponentSeat is null || frame.PindianStep is PindianStep.ChooseParticipants or PindianStep.ChooseSourceCard ||
            !IsGuanHaiPindianSource(frame, HasPaidOwnedPilePindianSource(frame)))
            throw new InvalidOperationException("A pile Pindian lost its issued original source, entity or native payment.");
    }
}
