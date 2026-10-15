using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed partial record JudgmentFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ZhenyiBeforeResultCompleted { get; init; }
}
public sealed partial record ProgramJudgmentTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ZhenyiBeforeResult { get; init; }
}

public sealed partial class GameEngine
{
    private bool ZhenyiJudgmentSucceeded(JudgmentFrame frame, Suit suit, int rank) => frame.Continuation switch
    {
        JudgmentContinuationKind.Lightning => suit == Suit.Spade && rank is >= 2 and <= 9,
        JudgmentContinuationKind.Indulgence => suit == Suit.Heart,
        JudgmentContinuationKind.SupplyShortage => suit == Suit.Club,
        _ => IsRedSuit(suit)
    };

    private SkillProgramTriggerWindow CurrentZhenyiJudgmentWindowKind(long judgmentId) =>
        _resolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().LastOrDefault() is { ZhenyiBeforeResult: true } frame &&
        frame.Judgment.JudgmentFrameId == judgmentId
            ? SkillProgramTriggerWindow.JudgmentBeforeResult : SkillProgramTriggerWindow.JudgmentFinalized;

    private bool IsExactZhenyiBeforeResultProgramChild(ProgramJudgmentTriggerWindowFrame window, ProgramSkillFrame child)
    {
        if (window.ZhenyiBeforeResult != true || !window.Activated ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            child.WindowContext is not { Window: SkillProgramTriggerWindow.JudgmentBeforeResult, Judgment: { } result } context ||
            context.ParentFrameId != window.Id || result != window.Judgment ||
            _resolutionStack.OfType<JudgmentFrame>().SingleOrDefault(f => f.Id == window.ParentFrameId) is not { } pending ||
            pending.ZhenyiBeforeResultCompleted == true || pending.Id != result.JudgmentFrameId ||
            GetJudgmentCard(pending)?.Id != result.CardId)
            return false;
        var candidate = window.Candidates[window.CandidateIndex];
        return candidate.OwnerSeat == child.OwnerSeat && candidate.SkillId == child.SkillId &&
            candidate.TriggerId == child.TriggerId && candidate.SkillInstanceId == child.SkillInstanceId &&
            candidate.GameplayHash == child.GameplayHash && GetProgramTrigger(child).Effects is
                [{ Op: SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei }];
    }

    private bool TryBeginZhenyiBeforeResultWindow(JudgmentFrame pending, Card card, Suit suit)
    {
        if (pending.ZhenyiBeforeResultCompleted == true ||
            !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei)) return false;
        var candidates = _players.Where(p => p.IsAlive && p.Markers.GetValueOrDefault(PlayerMarkerKind.Ziwei) == 1)
            .OrderBy(p => p.Seat)
            .SelectMany(owner => EnabledUniqueProgramTriggers(owner, SkillProgramTriggerWindow.JudgmentBeforeResult)
                .Where(b => ProgramInstructionResolver.Default.Features(b.Trigger).HasOperation(SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei))
                .OrderByDescending(b => b.Trigger.Priority).ThenBy(b => b.SkillId, StringComparer.Ordinal)
                .ThenBy(b => b.Trigger.Id, StringComparer.Ordinal)
                .Select(b => new ProgramJudgmentTriggerCandidate(owner.Seat, b.SkillId, b.Trigger.Id,
                    b.SkillInstanceId, b.Program.GameplayHash))).ToArray();
        if (candidates.Length == 0) return false;
        var rank = EffectiveZhenyiJudgmentRank(pending, card.Rank);
        var effectiveSuit = EffectiveZhenyiJudgmentSuit(pending, suit);
        var context = new JudgmentFinalizedContext(pending.Id, pending.TargetSeat, pending.Reason, card.Id,
            card.Kind, effectiveSuit, rank, ZhenyiJudgmentSucceeded(pending, effectiveSuit, rank), pending.SourceSeat);
        PushRuntimeFrame(new ProgramJudgmentTriggerWindowFrame(++_resolutionSequence, pending.Id, context,
            Array.AsReadOnly(candidates)) { ZhenyiBeforeResult = true });
        return true;
    }

    private ProgramJudgmentTriggerWindowFrame RefreshZhenyiBeforeResultWindow(
        ProgramJudgmentTriggerWindowFrame frame, JudgmentFrame pending)
    {
        if (frame.ZhenyiBeforeResult != true || frame.ParentFrameId != pending.Id ||
            frame.Judgment.JudgmentFrameId != pending.Id || GetJudgmentCard(pending) is not { } card ||
            card.Id != frame.Judgment.CardId || card.Kind != frame.Judgment.CardKind)
            throw new InvalidOperationException("The before-result judgment lost its exact native physical card and parent.");
        var suit = EffectiveZhenyiJudgmentSuit(pending, EffectiveSuit(_players[pending.TargetSeat], card));
        var rank = EffectiveZhenyiJudgmentRank(pending, card.Rank);
        var updated = frame with { Judgment = frame.Judgment with
            { Suit = suit, Rank = rank, Succeeded = ZhenyiJudgmentSucceeded(pending, suit, rank) } };
        if (updated != frame) ReplaceRuntimeFrame(frame.Id, updated);
        return updated;
    }

    private void CompleteZhenyiBeforeResultWindow(ProgramJudgmentTriggerWindowFrame frame, JudgmentFrame pending)
    {
        if (frame.ZhenyiBeforeResult != true || _resolutionStack.LastOrDefault()?.Id != frame.Id ||
            frame.ParentFrameId != pending.Id || pending.ZhenyiBeforeResultCompleted == true)
            throw new InvalidOperationException("The before-result judgment must return to its exact native parent once.");
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramJudgmentTriggerWindow);
        pending = GetJudgmentFrame(pending.Id) with { ZhenyiBeforeResultCompleted = true };
        ReplaceJudgmentFrame(pending);
        var completed = FinalizeJudgment(pending);
        if (completed is { } result)
            ResumeCompletedJudgment(new JudgmentCompletionReceipt(GetJudgmentFrameOrCompletedZhenyi(pending), result));
    }

    // Finalize may legitimately pop a ProgramSkill judgment before its typed return.
    private JudgmentFrame GetJudgmentFrameOrCompletedZhenyi(JudgmentFrame fallback) =>
        _resolutionStack.OfType<JudgmentFrame>().SingleOrDefault(f => f.Id == fallback.Id) ?? fallback;
}
