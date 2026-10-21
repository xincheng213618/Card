namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidShenGuoJia(ProgramSkillFrame frame)
    {
        if (frame.ShenGuoJia is not { } receipt || receipt.Source != ShenGuoJiaSource(frame) || receipt.GameplayHash != frame.GameplayHash ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash != receipt.GameplayHash || frame.InstructionIndex != 1 || frame.TriggerId is not null ||
            receipt.Turn != _turnNumber || receipt.TurnOwner != _turnProgression.OwnerSeat || receipt.Parent != 0 || !Enum.IsDefined(receipt.Stage) ||
            receipt.Before < 0 || receipt.After < receipt.Before || receipt.After > _movementSequence || receipt.Judgments.Count > 5 ||
            receipt.Cards.Count > receipt.Judgments.Count || receipt.Cards.Distinct().Count() != receipt.Cards.Count || receipt.Judgments.Select(value => value.Id).Distinct().Count() != receipt.Judgments.Count ||
            ProgramEventHistory<ShenGuoJiaStartedEvent>().Where(value => value.FrameId == frame.Id).ToArray() is not [var start] ||
            start != new ShenGuoJiaStartedEvent(frame.Id, receipt.Source, frame.GameplayHash, receipt.Operation, receipt.Turn, receipt.TurnOwner, 0,
                receipt.Operation == SkillProgramEffectOp.ResolveShenGuoJiaHuishiGrant ? frame.SelectedTargetSeats.SingleOrDefault(-1) : -1) ||
            ProgramEventHistory<ShenGuoJiaCompletedEvent>().Any(value => value.FrameId == frame.Id)) return false;
        if (receipt.Operation == SkillProgramEffectOp.ResolveShenGuoJiaHuishi)
        {
            if (frame.SkillId != ShenGuoJiaHuishi || frame.SelectedTargetSeats.Count != 0 || receipt.Plan is not null || receipt.Benefactor != -1 ||
                receipt.Stage is ShenGuoJiaStage.AwakeningChoice or ShenGuoJiaStage.DrawAttempt or ShenGuoJiaStage.DrawChildren or ShenGuoJiaStage.UseChoice or ShenGuoJiaStage.IssuingUse or ShenGuoJiaStage.NativeUse or ShenGuoJiaStage.ReturnedUse) return false;
            var returned = ProgramEventHistory<ShenGuoJiaJudgmentReturnedEvent>().Where(value => value.FrameId == frame.Id).OrderBy(value => value.Index).ToArray();
            if (returned.Length != receipt.Judgments.Count || returned.Where((value, index) => value.Index != index || value.Outcome != receipt.Judgments[index]).Any()) return false;
            for (var index = 0; index < receipt.Judgments.Count; index++)
            {
                var outcome = receipt.Judgments[index];
                if (ProgramEventHistory<JudgmentResolvedEvent>().Count(value => value.ResolutionId == outcome.Id && value.ParentResolutionId == frame.Id && value.TargetSeat == frame.OwnerSeat &&
                    value.Reason == "mobile-huishi" && value.CardId == outcome.CardId && value.Suit == outcome.Suit && value.Rank == outcome.Rank) != 1) return false;
                if (outcome.Handoff is { } handoff && (outcome.CardId is not { } card || !receipt.Cards.Contains(card) ||
                    !_cardMovements.Any(movement => movement.Sequence == handoff && movement.CardId == card && movement.From == CardLocation.Judgment(frame.OwnerSeat) && movement.To == CardLocation.Processing && movement.Reason.Value == "skill-program.judgment.result"))) return false;
                if (index > 0 && !ProgramEventHistory<ShenGuoJiaMaximumPaidEvent>().Any(value => value.FrameId == frame.Id && value.Purpose == "huishi-grow" && value.Index == index - 1 && value.After == value.Before + 1)) return false;
            }
            if (receipt.Judgments.Count > 0)
            {
                var last = receipt.Judgments[^1]; var fresh = last.Suit is { } suit && !receipt.Judgments.Take(receipt.Judgments.Count - 1).Any(value => value.Suit == suit);
                if (receipt.MayRepeat && !fresh) return false;
            }
            return true;
        }
        if (receipt.Operation == SkillProgramEffectOp.ResolveShenGuoJiaHuishiGrant)
            return frame.SkillId == ShenGuoJiaHuiShi && frame.SelectedTargetSeats is [var target] && target == receipt.Target &&
                receipt.Judgments.Count == 0 && receipt.Cards.Count == 0 && receipt.Plan is null && receipt.Benefactor == -1 &&
                receipt.Stage is ShenGuoJiaStage.AwakeningChoice or ShenGuoJiaStage.DrawAttempt or ShenGuoJiaStage.DrawChildren or ShenGuoJiaStage.CostChildren &&
                (receipt.Stage != ShenGuoJiaStage.CostChildren ||
                    ProgramEventHistory<ShenGuoJiaMaximumPaidEvent>().Count(value => value.FrameId == frame.Id && value.Seat == frame.OwnerSeat && value.Purpose == "huishi-gift") == 1 &&
                    (receipt.DrawOutcome is not null || ProgramEventHistory<ShenGuoJiaAwakeningOverrideEvent>().Count(value => value.FrameId == frame.Id && value.Source == receipt.Source && value.Recipient == receipt.Target) == 1));
        if (receipt.Operation != SkillProgramEffectOp.ResolveShenGuoJiaZuoxing || frame.SkillId != ShenGuoJiaZuoxing || frame.SelectedTargetSeats.Count != 0 || receipt.Judgments.Count != 0 || receipt.Cards.Count != 0 ||
            receipt.Stage is not (ShenGuoJiaStage.UseChoice or ShenGuoJiaStage.CostChildren or ShenGuoJiaStage.IssuingUse or ShenGuoJiaStage.NativeUse or ShenGuoJiaStage.ReturnedUse)) return false;
        if (receipt.Stage == ShenGuoJiaStage.UseChoice) return receipt.Plan is null && receipt.Benefactor == -1;
        return receipt.Plan is not null && IsValidPlayerSeat(receipt.Benefactor) &&
            ProgramEventHistory<ShenGuoJiaBenefactorEvent>().Any(value => value.Benefactor == receipt.Benefactor && value.Recipient == frame.OwnerSeat && frame.SkillInstanceId == $"acquired:{value.GrantSource}:{ShenGuoJiaZuoxing}") &&
            ProgramEventHistory<ShenGuoJiaMaximumPaidEvent>().Count(value => value.FrameId == frame.Id && value.Seat == receipt.Benefactor && value.Purpose == "zuoxing") == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 38000)]
    private void AssertShenGuoJia(ProgramSkillFrame frame)
    {
        if (frame.ShenGuoJia is null) return;
        if (!ValidShenGuoJia(frame)) throw new InvalidOperationException("Shen Guo Jia lost his native owner, source instance, once-paid cost, judgment outcome or frame-owned continuation.");
        var receipt = frame.ShenGuoJia;
        if (receipt.Judgments is not System.Collections.IList { IsReadOnly: true } || receipt.Cards is not System.Collections.IList { IsReadOnly: true } ||
            receipt.Plan is { Targets: not System.Collections.IList { IsReadOnly: true } }) throw new InvalidOperationException("Shen Guo Jia receipt collections must remain immutable.");
        if (frame.PendingMovementContinuation is { } pending && !IsShenGuoJiaMovement(frame, GetPausedPrivateOfferEffect(frame), pending)) throw new InvalidOperationException("Shen Guo Jia lost his own movement parent.");
        if (_resolutionStack.LastOrDefault()?.Id == frame.Id && _pendingDecision is { } prompt &&
            receipt.Stage is ShenGuoJiaStage.Repeat or ShenGuoJiaStage.Recipient or ShenGuoJiaStage.AwakeningChoice or ShenGuoJiaStage.UseChoice &&
            (prompt.PlayerSeat != frame.OwnerSeat || !AssistedChoicesEqual(prompt.Choices, ShenGuoJiaChoices(frame)))) throw new InvalidOperationException("Shen Guo Jia changed a published owner menu.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 38000)]
    private bool IsShenGuoJiaMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        frame.ShenGuoJia is { } receipt && effect?.Op == receipt.Operation && frame.PendingMovementContinuation == pending &&
        pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        receipt.Stage is ShenGuoJiaStage.JudgmentChildren or ShenGuoJiaStage.GrowChildren or ShenGuoJiaStage.GainChildren or ShenGuoJiaStage.CleanupChildren or ShenGuoJiaStage.DrawChildren or ShenGuoJiaStage.CostChildren;
    private bool ShenGuoJiaFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (!ValidShenGuoJia(frame) || frame.ShenGuoJia is not { } receipt) return false;
        if (child is JudgmentFrame judgment) return IsShenGuoJiaJudgment(judgment, frame);
        if (child is CardUseFrame use) return use.VirtualOrdinaryTrickOrigin?.ParentProgramFrameId == frame.Id && IsShenGuoJiaVirtualUse(use);
        if (child is NativeDrawAttemptFrame draw) return receipt.Stage == ShenGuoJiaStage.DrawAttempt && draw.Context.ParentFrameId == frame.Id && draw.Context.RecipientSeat == receipt.Target && draw.Context.RequestedCount == 4;
        if (child is HpChangedTriggerWindowFrame health) return frame.PendingMovementContinuation is not null && health.ResumeFrameId == frame.Id && health.Change.ParentFrameId == frame.Id &&
            health.Change.Kind == HpChangeKind.MaximumHp && health.Change.TargetSeat == (receipt.Operation == SkillProgramEffectOp.ResolveShenGuoJiaZuoxing ? receipt.Benefactor : frame.OwnerSeat) &&
            ProgramEventHistory<ShenGuoJiaMaximumPaidEvent>().Any(fact => fact.FrameId == frame.Id && fact.Seat == health.Change.TargetSeat && Math.Abs(fact.After - fact.Before) == health.Change.Amount);
        if (child is DeathFrame death) return receipt.Stage == ShenGuoJiaStage.CostChildren && receipt.MaximumAfter == 0 && death.ParentFrameId == frame.Id && death.VictimSeat == frame.OwnerSeat && death.ReturnKind == DeathReturnKind.ProgramSkill;
        if (child is CardsMovedTriggerWindowFrame moved) return frame.PendingMovementContinuation is not null && ProgramMovementReturnMatches(frame, moved) && moved.Batch.Movements.Count > 0 &&
            moved.Batch.Movements.All(movement => _cardMovements.Contains(movement) && movement.Sequence > receipt.Before && movement.Sequence <= receipt.After &&
                (movement.Reason.Value is "skill-program.mobile:huishi.give" or "skill-program.mobile:huishi.cleanup" or "skill-program.mobile:huishi-gift.draw" or "skill-program.judgment.result" || movement.Reason == CardMoveReasons.Reshuffle));
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == frame.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? ShenGuoJiaObserverRoot() => FindProgramObserverRoot(static (engine, frame, child) => engine.ShenGuoJiaFirstChild(frame, child), static (engine, index) => engine.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 38000)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 38000)]
    private bool HasShenGuoJiaDamageObserver(long id) => ShenGuoJiaObserverRoot() is { } root && _resolutionStack.FindIndex(frame => frame.Id == id && frame is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(frame => frame.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 38000)]
    private bool HasShenGuoJiaBeforeDamageObserver(long id) => ShenGuoJiaObserverRoot() is { } root && _resolutionStack.FindIndex(frame => frame.Id == id && frame is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(frame => frame.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 38000)]
    private bool IsShenGuoJiaDying() => ActiveDying is { } dying && ShenGuoJiaObserverRoot() is { } root && _resolutionStack.FindIndex(frame => frame.Id == dying.Id) > _resolutionStack.FindIndex(frame => frame.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 38000)]
    private bool AdvanceShenGuoJiaSubtree() => _pendingDecision is null && ShenGuoJiaObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 38000)]
    private bool AllowsShenGuoJiaNestedDamage(ProgramSkillFrame frame, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (frame.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != frame.Id || ShenGuoJiaObserverRoot() is null) return false;
        var effect = GetPausedPrivateOfferEffect(frame);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature && !sourceLess &&
            target == (effect.TargetReference is { } participant ? ResolveProgramParticipant(frame, participant) : ResolveProgramEffectTarget(frame, effect.Target));
    }
}
