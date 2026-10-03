namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? AppliedDamageBenefitRoot(long damageWindowId)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == damageWindowId);
        if (index < 0 || index + 1 >= _resolutionStack.Count ||
            _resolutionStack[index] is not DamageTriggerWindowFrame damage ||
            damage.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied ||
            damage.CandidateIndex < 0 || damage.CandidateIndex >= damage.Candidates.Count ||
            _resolutionStack[index + 1] is not ProgramSkillFrame root || root.AppliedDamageBenefit is not { } receipt ||
            receipt.DamageWindowId != damage.Id || receipt.DamageFrameId != damage.ParentFrameId ||
            receipt.OwnerSeat != root.OwnerSeat || receipt.TargetSeat != damage.TargetSeat ||
            root.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context ||
            context.ParentFrameId != damage.Id || context.DamageFrameId != receipt.DamageFrameId ||
            context.TargetSeat != receipt.TargetSeat || context.SourceSeat != receipt.SourceSeat ||
            !MountObserverCandidateMatches(root, damage.Candidates[damage.CandidateIndex].ToProgramCandidate())) return null;
        var instructions = ProgramInstructionResolver.Default.Resolve(root, AppliedDamageProgramDefinition(root.SkillId)).Instructions;
        if (receipt.InstructionIndex < 1 || receipt.InstructionIndex > instructions.Count ||
            root.InstructionIndex < receipt.InstructionIndex || root.InstructionIndex > receipt.InstructionIndex + 1 ||
            instructions[receipt.InstructionIndex - 1] is not { Op: SkillProgramEffectOp.DrawOwnerAtAppliedDamage } draw ||
            draw.Amount != receipt.Amount || receipt.DrawCount < 0 || receipt.DrawCount > receipt.Amount ||
            _cardMovements.Count(m => m.Sequence > receipt.FirstMovementSequence && m.Sequence <= receipt.LastMovementSequence &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(root.OwnerSeat) &&
                m.Reason.Value == "program.applied-damage-benefit.draw") != receipt.DrawCount) return null;
        for (var child = index + 2; child < _resolutionStack.Count; child++)
        {
            if (child == index + 2 && _resolutionStack[child] is CardsMovedTriggerWindowFrame first)
            {
                if (!AppliedDamageBenefitFirstBatch(root, receipt, first)) return null;
            }
            else if (child == index + 2 && _resolutionStack[child] is HpChangedTriggerWindowFrame firstHp)
            {
                if (!AppliedDamageFieldPayment(root, receipt) || firstHp.ResumeFrameId != root.Id ||
                    firstHp.Change.ParentFrameId != root.Id || firstHp.Change.TargetSeat != receipt.TargetSeat ||
                    firstHp.Change.Kind != HpChangeKind.Recovery || firstHp.Continuation is not
                        (PostEventContinuation.AwaitedProgramMovement or PostEventContinuation.Program)) return null;
            }
            else if (child == index + 2 && _resolutionStack[child] is RecoveryReplacementFrame recovery)
            {
                if (!AppliedDamageFieldPayment(root, receipt) || !RecoveryReplacementFrameRidesOn(recovery, root)) return null;
            }
            else if (!PreventionDrawObserverEdge(child)) return null;
            // A source-owned Dying is already connected by the strict edge above;
            // these proofs own the entire frozen native/converted/provider rescue.
            if (_resolutionStack[child] is DyingFrame dying &&
                (IsPaidHandRepaymentRescueRide(child, dying) || IsPaidHandRepaymentProgramAlcoholRide(child, dying))) break;
        }
        return root;
    }

    private bool AppliedDamageBenefitFirstBatch(ProgramSkillFrame root, ProgramAppliedDamageBenefit receipt,
        CardsMovedTriggerWindowFrame movement)
    {
        var batch = movement.Batch;
        if (batch.ParentFrameId != root.Id ||
            batch.OriginOwnerSeat != root.OwnerSeat || batch.OriginSkillId != root.SkillId ||
            batch.OriginSkillInstanceId != root.SkillInstanceId || batch.Movements.Count == 0 ||
            batch.Movements.Any(m => !_cardMovements.Contains(m))) return false;
        if (root.InstructionIndex == receipt.InstructionIndex)
            return (batch.AwaitingProgramFrameId is null || batch.AwaitingProgramFrameId == root.Id) &&
                root.PendingMovementContinuation is { CoverageResultBind: null } pending && pending.SubjectSeat == root.OwnerSeat &&
                batch.Movements.Count <= receipt.DrawCount && batch.Movements.All(m =>
                    m.Sequence > receipt.FirstMovementSequence && m.Sequence <= receipt.LastMovementSequence && m.From == CardLocation.DrawPile &&
                    m.To == CardLocation.Hand(root.OwnerSeat) && m.Reason.Value == "program.applied-damage-benefit.draw");
        var plan = ProgramInstructionResolver.Default.Resolve(root, AppliedDamageProgramDefinition(root.SkillId));
        return batch.AwaitingProgramFrameId == root.Id && root.InstructionIndex == receipt.InstructionIndex + 1 &&
            plan.Instructions[root.InstructionIndex - 1] is { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard,
                AwaitMovementTriggers: true, Destination: SkillProgramCardDestination.DiscardPile } effect &&
            effect.CardOwnerRef?.Kind == ProgramParticipantRef.EventTarget &&
            effect.ChooserRef?.Kind == ProgramParticipantRef.Owner &&
            effect.Zones.All(z => z is CardZoneKind.Equipment or CardZoneKind.Judgment) &&
            batch.Movements is [var paid] && paid.From.OwnerSeat == receipt.TargetSeat &&
            effect.Zones.Contains(paid.From.Zone) && paid.Reason.Value == $"skill-program.{root.SkillId}.SelectAndMoveOwnedCard" &&
            paid.To.Zone is CardZoneKind.DiscardPile or CardZoneKind.OutsideGame;
    }

    private bool AppliedDamageFieldPayment(ProgramSkillFrame root, ProgramAppliedDamageBenefit receipt) =>
        root.InstructionIndex == receipt.InstructionIndex + 1 &&
        _cardMovements.Count(m => m.Sequence > receipt.LastMovementSequence && m.From.OwnerSeat == receipt.TargetSeat &&
            m.From.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment &&
            m.To.Zone is CardZoneKind.DiscardPile or CardZoneKind.OutsideGame &&
            m.Reason.Value == $"skill-program.{root.SkillId}.SelectAndMoveOwnedCard") == 1;

    private bool HasAppliedDamageBenefitObserver(long damageWindowId) => AppliedDamageBenefitRoot(damageWindowId) is not null;
    private void AssertAppliedDamageBenefitReceipt(ProgramSkillFrame frame)
    {
        if (frame.AppliedDamageBenefit is { } receipt && AppliedDamageBenefitRoot(receipt.DamageWindowId)?.Id != frame.Id)
            throw new InvalidOperationException("An applied-damage benefit lost its actual once-issued draw or exact owning producer subtree.");
    }
    private bool IsAppliedDamageBenefitDying() => ActiveDamageTrigger is { } damage && ActiveDying is { } dying &&
        AppliedDamageBenefitRoot(damage.Id) is not null &&
        _resolutionStack.OfType<DyingFrame>().Any(d => d.Id == dying.FrameId && d.ParentFrameId == dying.ParentFrameId);
}
