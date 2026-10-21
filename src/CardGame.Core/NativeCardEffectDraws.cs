using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum NativeCardEffectDrawStage { Requesting, DrawingChildren }
public sealed record NativeCardEffectDrawReceipt(long NullificationFrameId, int EffectCardId,
    CardKind EffectCardKind, int RecipientSeat, int TargetIndex, NativeCardEffectDrawStage Stage)
{
    public int RequestedCount { get; init; } = 2;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? AwaitingProgramFrameId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeDrawOutcome? Outcome { get; init; }
}

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeCardEffectDrawReceipt? NativeCardEffectDraw { get; init; }
}

public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeNativeCardEffectDrawFrameId { get; init; }
}

public sealed record NativeCardEffectDrawStartedEvent(long UseFrameId, long NullificationFrameId,
    int EffectCardId, CardKind EffectCardKind, int RecipientSeat, int TargetIndex,
    long? AwaitingProgramFrameId = null) : IGameEvent;
public sealed record NativeCardEffectDrawAppliedEvent(long UseFrameId, long NullificationFrameId,
    int RecipientSeat, int ActualDrawCount, long SequenceBefore, long SequenceAfter) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TryBeginNativeDrawTwo(NullificationWindowFrame pending)
    {
        if (!HasNativeDrawAttemptCapability) return false;
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || use.Id != pending.ParentFrameId ||
            pending.ActionKind != LegalActionKind.DrawTwo || pending.EffectCardKind != CardKind.DrawTwo ||
            use.NativeDraw is not null || use.NativeCardEffectDraw is not null || _pendingDecision is not null)
            throw new InvalidOperationException("DrawTwo requires its actual resolved effect and owning CardUse.");
        var recipient = GetNullificationTargetSeat(pending) ?? pending.SourceSeat;
        var receipt = new NativeCardEffectDrawReceipt(pending.Id, pending.EffectCardId,
            pending.EffectCardKind, recipient, use.TargetIndex, NativeCardEffectDrawStage.Requesting)
        { RequestedCount = WushuangDrawTwoCount(pending.SourceSeat), AwaitingProgramFrameId = _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault() is { } parent &&
                IsAwaitingProgramMovement(parent) ? parent.Id : null };
        ReplaceRuntimeTop(use with { NativeCardEffectDraw = receipt,
            NativeDraw = new NativeDrawProducerReceipt(0, receipt.RequestedCount, null, SkillProgramCardSetVisibility.Private, CardMoveReasons.Draw)
            { Recipients = [recipient], ReturnKind = NativeDrawAttemptReturnKind.CardUseDrawTwo } });
        AdvanceEventRulesAndQueueFact(new NativeCardEffectDrawStartedEvent(use.Id, pending.Id,
            receipt.EffectCardId, receipt.EffectCardKind, recipient, use.TargetIndex, receipt.AwaitingProgramFrameId));
        ContinueNativeDrawTwo(use.Id);
        return true;
    }

    [NativeFrameRoute(typeof(CardUseFrame), 30, typeof(NativeFrameRouteConditions), nameof(NativeFrameRouteConditions.NativeCardEffectDraw))]
    private void ContinueNativeDrawTwo(long id)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame { NativeCardEffectDraw: { } receipt } use || use.Id != id ||
            _pendingDecision is not null)
            throw new InvalidOperationException("A native card draw resumed without its exact quiet owning effect.");
        AssertNativeCardEffectDraw(use);
        if (receipt.Stage == NativeCardEffectDrawStage.Requesting)
        {
            var paid = use.NativeDraw!;
            if (paid.ActiveAttemptId is not null && !paid.AttemptReturned)
                throw new InvalidOperationException("A native card draw resumed before its issued attempt returned.");
            if (!paid.AttemptReturned && TryBeginNativeDrawAttempt(use)) return;
            var before = OutsidePhaseMovementSequence;
            var ids = paid.Replacement is null && _players[receipt.RecipientSeat].IsAlive && _winner == Winner.None
                ? DrawCards(_players[receipt.RecipientSeat], receipt.RequestedCount, true, CardMoveReasons.Draw)
                : Array.AsReadOnly(Array.Empty<int>());
            var outcome = new NativeDrawOutcome(receipt.RecipientSeat, receipt.RequestedCount, paid.ActiveAttemptId,
                before, OutsidePhaseMovementSequence, paid.Replacement) { ActualDrawIds = ids };
            use = (CardUseFrame)_resolutionStack.Last();
            ReplaceRuntimeTop(use = use with { NativeDraw = null, NativeCardEffectDraw = receipt = receipt with
                { Stage = NativeCardEffectDrawStage.DrawingChildren, Outcome = outcome } });
            AdvanceEventRulesAndQueueFact(new NativeCardEffectDrawAppliedEvent(id, receipt.NullificationFrameId,
                receipt.RecipientSeat, ids.Count, outcome.SequenceBefore, outcome.SequenceAfter));
        }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.NativeCardEffectDraw) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.NativeCardEffectDraw) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.NativeCardEffectDraw) ||
            TryBeginCardsMovedProgramWindow(id)) return;
        use = (CardUseFrame)_resolutionStack.Last();
        AssertNativeCardEffectDraw(use);
        receipt = use.NativeCardEffectDraw!;
        // Retire this exact effect before FinishCardUse can repeat the card or
        // advance to its next target. Each genuine later Draw2 gets a new request.
        ReplaceRuntimeTop(use with { NativeCardEffectDraw = null, NativeDraw = null });
        var card = GetTrickRepresentation(id, receipt.EffectCardId);
        MoveFinishedTrickCard(id, card);
        AddLog("CardEffect", $"{_players[receipt.RecipientSeat].Name} 的【无中生有】结算完毕。", receipt.RecipientSeat);
        FinishCardUse(id, card, receipt.EffectCardKind);
    }

    private bool IsNativeCardEffectDrawMovement(CardUseFrame use, CardMovementBatchContext batch) =>
        use.NativeCardEffectDraw is { Stage: NativeCardEffectDrawStage.DrawingChildren, Outcome: { } outcome } receipt &&
        batch.ParentFrameId == use.Id && batch.AwaitingProgramFrameId == receipt.AwaitingProgramFrameId && batch.Movements.Count > 0 &&
        batch.Movements.All(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter &&
            (outcome.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(outcome.RecipientSeat) && m.Reason == CardMoveReasons.Draw ||
             m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle));

    private bool IsNativeCardEffectDrawMovementReturn(CardUseFrame use, CardsMovedTriggerWindowFrame frame) =>
        frame.ResumeNativeCardEffectDrawFrameId == use.Id && frame.Id == frame.Batch.Id &&
        frame.ResumeProgramFrameId is null &&
        !HasAlternateProgramMovementReturn(frame with { ResumeNativeCardEffectDrawFrameId = null }) &&
        IsNativeCardEffectDrawMovement(use, frame.Batch);

    private bool IsNativeCardEffectDrawStateReturn(CardUseFrame use, ProgramLifecycleTriggerWindowFrame frame) =>
        use.NativeCardEffectDraw is { Stage: NativeCardEffectDrawStage.DrawingChildren } &&
        frame.ResumeProgramFrameId == use.Id && frame.CharacterStateContinuation == CharacterStateContinuation.NativeCardEffectDraw &&
        frame.ResumeCardId is null && frame.ResumeCardKind is null &&
        ProgramEventHistory<CharacterStateChangedEvent>().Count(e => e.Change.Id == frame.Id && e.Change.ParentFrameId == use.Id) == 1;

    private bool HasNativeCardEffectDrawProcessing(IReadOnlyList<Card> processing)
    {
        var uses = _resolutionStack.OfType<CardUseFrame>().ToArray();
        var draws = uses.Where(use => use.NativeCardEffectDraw is not null).ToArray();
        if (draws.Length == 0) return false;
        foreach (var use in draws) AssertNativeCardEffectDraw(use);
        // A pre-draw optional offer can be open before it creates a ProgramSkillFrame.
        // The original card and any real ancestor use still own their Processing entities.
        var materials = uses.SelectMany(use => use.PhysicalCardIds ?? [use.CardId]).ToHashSet();
        return processing.All(card => materials.Contains(card.Id));
    }

    private void AssertNativeCardEffectDraw(CardUseFrame use)
    {
        if (use.NativeCardEffectDraw is not { } receipt) return;
        if (!HasNativeDrawAttemptCapability || receipt.RequestedCount is < 2 or > 3 || receipt.NullificationFrameId <= 0 ||
            receipt.EffectCardKind != CardKind.DrawTwo || LeChengyanNativeEffectKind(use) != CardKind.DrawTwo ||
            !IsValidPlayerSeat(receipt.RecipientSeat) || receipt.TargetIndex != use.TargetIndex || !Enum.IsDefined(receipt.Stage) ||
            ProgramEventHistory<NativeCardEffectDrawStartedEvent>().Count(e => e.UseFrameId == use.Id &&
                e.NullificationFrameId == receipt.NullificationFrameId && e.EffectCardId == receipt.EffectCardId &&
                e.EffectCardKind == receipt.EffectCardKind && e.RecipientSeat == receipt.RecipientSeat &&
                e.TargetIndex == receipt.TargetIndex && e.AwaitingProgramFrameId == receipt.AwaitingProgramFrameId) != 1 ||
            receipt.AwaitingProgramFrameId is { } programId && !_resolutionStack.OfType<ProgramSkillFrame>()
                .Any(parent => parent.Id == programId && IsAwaitingProgramMovement(parent)))
            throw new InvalidOperationException("A native card draw lost its once-issued resolved effect or recipient.");
        if (receipt.Stage == NativeCardEffectDrawStage.Requesting)
        {
            if (receipt.Outcome is not null || use.NativeDraw is not { } paid ||
                paid.ReturnKind != NativeDrawAttemptReturnKind.CardUseDrawTwo || paid.InstructionIndex != 0 ||
                paid.Recipients is not [var seat] || seat != receipt.RecipientSeat || paid.RecipientIndex != 0 ||
                paid.RequestedCount != receipt.RequestedCount || paid.ResultBind is not null || paid.Operation is not null || paid.RecipientApplied ||
                paid.Reason != CardMoveReasons.Draw || paid.Replacement is not null && !paid.AttemptReturned ||
                paid.ActiveAttemptId is null && paid.AttemptReturned)
                throw new InvalidOperationException("DrawTwo changed its exact pre-draw producer contract.");
            return;
        }
        if (use.NativeDraw is not null || receipt.Outcome is not { } result || result.RecipientSeat != receipt.RecipientSeat ||
            result.RequestedCount != receipt.RequestedCount || result.SequenceBefore > result.SequenceAfter || result.SequenceAfter > OutsidePhaseMovementSequence ||
            result.ActualDrawIds.Count > receipt.RequestedCount || result.ActualDrawIds.Distinct().Count() != result.ActualDrawIds.Count ||
            result.ActualDrawIds is not System.Collections.IList { IsReadOnly: true } ||
            result.Replacement is not null && result.ActualDrawIds.Count != 0 ||
            !_cardMovements.Where(m => m.Sequence > result.SequenceBefore && m.Sequence <= result.SequenceAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(receipt.RecipientSeat) && m.Reason == CardMoveReasons.Draw)
                .OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(result.ActualDrawIds) ||
            ProgramEventHistory<NativeCardEffectDrawAppliedEvent>().Count(e => e.UseFrameId == use.Id &&
                e.NullificationFrameId == receipt.NullificationFrameId && e.RecipientSeat == receipt.RecipientSeat &&
                e.ActualDrawCount == result.ActualDrawIds.Count && e.SequenceBefore == result.SequenceBefore &&
                e.SequenceAfter == result.SequenceAfter) != 1 ||
            result.AttemptFrameId is { } attempt && ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e =>
                e.AttemptFrameId == attempt && e.ParentFrameId == use.Id && e.RecipientSeat == receipt.RecipientSeat &&
                e.RequestedCount == receipt.RequestedCount && e.Replaced == (result.Replacement is not null)) != 1)
            throw new InvalidOperationException("DrawTwo lost its original draw materials or exact replacement return.");
    }
}
