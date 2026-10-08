namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? CappedConversionObserverRoot(long? damageWindowId = null)
    {
        for (var index = 1; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root ||
                root.CappedConversionBenefit is not { AwaitingMovement: true, ActualDrawAttempted: true } r ||
                !ValidCappedConversionBenefit(root) || _resolutionStack[index - 1] is not DamageTriggerWindowFrame original ||
                original.Id != r.DamageWindowFrameId || damageWindowId is { } requested &&
                    original.Id != requested && !_resolutionStack.Skip(index + 1).Any(w => w.Id == requested)) continue;
            if (_resolutionStack[index + 1] is not CardsMovedTriggerWindowFrame movement || movement.Batch.ParentFrameId != root.Id ||
                movement.Batch.AwaitingProgramFrameId != root.Id ||
                movement.ResumeProgramFrameId is { } resume && resume != root.Id || movement.Batch.OriginOwnerSeat != root.OwnerSeat ||
                movement.Batch.OriginSkillId != root.SkillId || movement.Batch.OriginSkillInstanceId != root.SkillInstanceId ||
                movement.Batch.Movements is not [var actual] || !_cardMovements.Contains(actual) ||
                actual.Sequence <= r.SequenceBefore || actual.Sequence > r.SequenceAfter ||
                actual.From != CardLocation.DrawPile || actual.To != CardLocation.Hand(root.OwnerSeat) ||
                actual.Reason.Value != $"skill-program.{root.SkillId}.capped-conversion-draw") continue;
            var aligned = true;
            for (var child = index + 1; child < _resolutionStack.Count; child++)
            {
                // The incoming edge must be proved before a full rescue suffix
                // can cover the remainder. No mere Dying/CardUse presence.
                if (!HalfHandPaidDamageObserverEdge(child)) { aligned = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    (IsPaidHandRepaymentRescueRide(child, dying) || IsPaidHandRepaymentProgramAlcoholRide(child, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying) ||
                     (TieredRoundZeroDyingRescueRide(child, dying) || DrawFundedDistinctBasicDyingRescueRide(child, dying)))) break;
            }
            if (aligned) return root;
        }
        return null;
    }

    private bool HasCappedConversionBenefitObserver(long damageWindowId) => CappedConversionObserverRoot(damageWindowId) is not null;
    private bool IsCappedConversionBenefitProgramDying() => ActiveDying is not null && CappedConversionObserverRoot() is not null;

    private bool AllowsCappedConversionNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (_resolutionStack.LastOrDefault()?.Id != observer.Id || observer.AttackAttempt is not null || observer.InstructionIndex < 1 ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost) || CappedConversionObserverRoot() is null) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            !sourceLess && target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
}
