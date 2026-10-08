namespace CardGame.Core;

internal sealed partial class ProgramAiEstimateContext
{
    internal void PairedColorDispositionValue(SkillProgramEffect effect) =>
        _otherAdjustment += _publicContext.PairedColorDispositionScore;
}

public sealed partial class GameEngine
{
    private double GetPairedColorDispositionAiScore(SkillProgramTrigger trigger, int owner,
        ProgramCardTriggerWindowFrame window)
    {
        if (trigger.Effects is not [{ Op: SkillProgramEffectOp.OfferPairedColorCardDisposition } effect] ||
            ExactPairedColorResponseLink(window) is not { } pair ||
            pair.ResponseIsRed is not { } red || pair.PairedIsRed != red || pair.ResponseSeat == pair.PairedSeat ||
            owner != pair.ResponseSeat && owner != pair.PairedSeat) return 0d;
        var counterpart = owner == pair.ResponseSeat ? pair.PairedSeat : pair.ResponseSeat;
        // Unknown hand faces have no place in an optional-trigger estimate.
        if (!_players[counterpart].IsAlive || GetHand(_players[counterpart]).Count + GetEquipment(_players[counterpart]).Count == 0) return 0d;
        var candidate = window.CandidateIndex < window.Candidates.Count ? window.Candidates[window.CandidateIndex] : null;
        return candidate is { } source && source.OwnerSeat == owner &&
            HasDamageCounterpartAcquisition(owner, source.SkillId, effect.StateId!, counterpart) ? 12d : 8d;
    }
}
