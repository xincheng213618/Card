namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasTangTangZhiyi => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveZhiyiGainColorBenefit) == true;
    private static bool IsTangTangZhiyiTrigger(SkillProgramTrigger t) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_tang_tang_ol_Zhiyi_Gains.IsTangTangZhiyiTrigger(t);
    private bool ZhiyiAvailable(int owner) => !ProgramEventHistory<ZhiyiOpportunityPaidEvent>().Any(e => e.OwnerSeat == owner && e.ActualTurn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat);
    private IEnumerable<ProgramTriggerCandidate> CollectTangTangZhiyiCandidates(CardMovementBatchContext batch) => batch.TangTangGains is not { Count: > 0 } gains ? [] :
        gains.GroupBy(g => g.OwnerSeat).Where(g => _players[g.Key].IsAlive && ZhiyiAvailable(g.Key)).SelectMany(g => CollectProgramTriggerCandidates(_players[g.Key], SkillProgramTriggerWindow.CardsMoved)
            .Where(c => IsTangTangZhiyiTrigger(GetProgramTrigger(c))).Select(c => c with { OccurrenceIndex = TangTangGainOccurrence(batch, g) })).ToArray();
    private ProgramSkillWindowContext CreateTangTangZhiyiContext(CardsMovedTriggerWindowFrame frame, ProgramTriggerCandidate c) =>
        new(SkillProgramTriggerWindow.CardsMoved, frame.Id, c.OwnerSeat, SourceSeat: c.OwnerSeat, TargetSeat: c.OwnerSeat, OccurrenceIndex: c.OccurrenceIndex,
            Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]) with { MovedCardCount = frame.Batch.TangTangGains!.Count(g => g.OwnerSeat == c.OwnerSeat) }, MovementBatch: frame.Batch, MovementIndex: c.OccurrenceIndex);
    private IReadOnlyList<TangTangGain>? CurrentTangTangGains(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!IsTangTangZhiyiTrigger(GetProgramTrigger(c)) || context.Window != SkillProgramTriggerWindow.CardsMoved || context.OwnerSeat != c.OwnerSeat || context.SourceSeat != c.OwnerSeat || context.TargetSeat != c.OwnerSeat || context.OccurrenceIndex != c.OccurrenceIndex || context.MovementIndex != c.OccurrenceIndex ||
            context.MovementBatch is not { TangTangGains: { Count: > 0 } gains } batch || batch.Id != context.ParentFrameId || c.OccurrenceIndex < 0 || c.OccurrenceIndex >= batch.Movements.Count ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == batch.Id) is not { } parent || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c ||
            !parent.Batch.Movements.SequenceEqual(batch.Movements) || parent.Batch.TangTangGains is not { } original || !original.SequenceEqual(gains)) return null;
        var selected = gains.Where(g => g.OwnerSeat == c.OwnerSeat).ToArray();
        if (selected.Length == 0 || c.OccurrenceIndex != TangTangGainOccurrence(batch, selected) || selected.Any(g => !ProgramEventHistory<ZhiyiGainFrozenEvent>().Any(e => e.Gain == g) ||
            (g.NativeDrawId == 0 ? !batch.Movements.Any(m => m.CardId == g.CardId && m.Sequence == g.GainSequence && m.To == CardLocation.Hand(g.RecipientSeat)) :
                batch.NativeDrawInvocation is not { } draw || draw.InvocationId != g.NativeDrawId || draw.LastBatchId != batch.Id || draw.OwnerSeat != g.OwnerSeat || !ValidNativeDrawInvocation(draw) || !draw.Materials.Any(m => m.CardId == g.CardId && m.MovementSequence == g.GainSequence)))) return null;
        return Array.AsReadOnly(selected);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27531)]
    private bool QualifyTangTangZhiyi(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) => !IsTangTangZhiyiTrigger(t) || ZhiyiAvailable(c.OwnerSeat) && CurrentTangTangGains(c, context) is not null;
}
