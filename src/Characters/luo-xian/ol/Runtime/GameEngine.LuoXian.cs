namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource DailiSource(ProgramSkillFrame frame) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_luo_xian_ol_LuoXian.DailiSource(frame);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26420)]
    private bool CanOfferDaili(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveDailiTurnEnd) ||
        context.Window == SkillProgramTriggerWindow.AfterTurnEnded && PubliclyRevealedHandCount(candidate.OwnerSeat) % 2 == 0;

    private SkillProgramStepOutcome ResolveDailiTurnEnd(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.Daili is not null || frame.InstructionIndex != 1 || frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterTurnEnded } context ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not DeferredTurnEndFrame parent || parent.Id != context.ParentFrameId ||
            !MatchesAfterTurnEndedChild(parent, frame, context) || !CanOfferDaili(new(frame.OwnerSeat, frame.SkillId, frame.TriggerId!, frame.SkillInstanceId, frame.GameplayHash, 0), GetProgramTrigger(frame), context))
            throw new InvalidOperationException("Daili requires its exact unissued actual completed-turn entitlement.");
        var receipt = new DailiTurnEndReceipt(DailiSource(frame), frame.GameplayHash, parent.Id, parent.TurnNumber, parent.OwnerSeat,
            frame.InstructionIndex, PubliclyRevealedHandCount(frame.OwnerSeat), _players[frame.OwnerSeat].IsFaceDown);
        ReplaceRuntimeTop(frame = frame with { Daili = new(receipt, DailiStage.FlipChildren) });
        AdvanceEventRulesAndQueueFact(new DailiTurnEndIssuedEvent(frame.Id, receipt));
        TurnOverProgramTarget(frame.Id, frame.OwnerSeat, frame.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new DailiTurnedOverEvent(frame.Id, receipt.WasFaceDown, _players[frame.OwnerSeat].IsFaceDown));
        AdvanceRuntimeProgram(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainDailiChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);

    [ProgramContinuation(ProgramContinuationKind.Resume, 26420)]
    private bool ResumeDaili(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Daili: { } pending } frame || frame.Id != id) return false;
        RequireDaili(frame);
        if (DrainDailiChildren(id)) return true;
        if (pending.Stage == DailiStage.FlipChildren && _players[frame.OwnerSeat].IsAlive)
        {
            var before = _movementSequence;
            ReplaceRuntimeTop(frame = frame with { Daili = pending with { Stage = DailiStage.DrawChildren, Before = before, After = before }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
            var drawn = DrawCards(_players[frame.OwnerSeat], 3, true, new("skill-program.daili.draw"));
            frame = GetActiveProgramFrame(id);
            ReplaceRuntimeTop(frame = frame with { Daili = frame.Daili! with { After = _movementSequence, Count = drawn.Count } });
            foreach (var cardId in drawn)
            {
                var movement = _cardMovements.Single(m => m.Sequence > before && m.Sequence <= _movementSequence && m.CardId == cardId &&
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(frame.OwnerSeat) && m.Reason.Value == "skill-program.daili.draw");
                AdvanceEventRulesAndQueueFact(new DailiCardDrawnEvent(id, frame.OwnerSeat, movement.Sequence));
            }
            AdvanceEventRulesAndQueueFact(new DailiDrawPaidEvent(id, before, _movementSequence, drawn.Count));
            AdvanceRuntimeProgram(id);
            return true;
        }
        var reveals = pending.Stage == DailiStage.DrawChildren && _players[frame.OwnerSeat].IsAlive
            ? DailiDrawnMovements(frame).Where(m => CurrentHandEntry(frame.OwnerSeat, m.CardId) == m.Sequence).Select(m => ToSnapshot(GetAdvancedCard(m.CardId))).ToArray()
            : [];
        if (reveals.Length > 0) AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(frame.Id, Array.AsReadOnly(reveals)));
        AdvanceEventRulesAndQueueFact(new DailiTurnEndCompletedEvent(id, pending.Count, reveals.Length));
        ReplaceRuntimeTop(frame = frame with { Daili = null, PendingMovementContinuation = null });
        FinishProgramSkill(frame, true);
        return true;
    }
    private IEnumerable<CardMovementRecord> DailiDrawnMovements(ProgramSkillFrame frame) =>
        CompleteProgramEventHistory().OfType<DailiCardDrawnEvent>().Where(e => e.FrameId == frame.Id)
            .Select(e => _cardMovements.Single(m => m.Sequence == e.MovementSequence));
    [ProgramContinuation(ProgramContinuationKind.Movement, 26420)]
    private bool ReturnDailiMovement(ProgramSkillFrame frame) => frame.Daili is not null && ResumeDaili(frame.Id);
    [ProgramCapability]
    private sealed class LuoXianCapability(GameEngine engine) : ILuoXianProgramHost
    { public SkillProgramStepOutcome ResolveDailiTurnEnd(ProgramSkillFrame frame) => engine.ResolveDailiTurnEnd(frame); }
}
