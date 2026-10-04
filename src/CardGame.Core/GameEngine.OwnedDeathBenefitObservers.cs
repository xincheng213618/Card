namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool OwnedDeathBenefitObserverPrefix(ProgramSkillFrame root)
    {
        if (!ExactOwnedDeathBenefitReturn(root) || !ValidFixedRecipientReceipt(root)) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == root.Id);
        if (index == _resolutionStack.Count - 1) return true;
        if (!PairBenefitFirstChild(root, _resolutionStack[index + 1])) return false;
        for (var child = index + 1; child < _resolutionStack.Count; child++)
        {
            // Each helper below proves the complete exact rescue suffix. Do not
            // subsequently reinterpret bound/virtual Alcohol as native Peach.
            if (_resolutionStack[child - 1] is DyingFrame rescued &&
                (IsPaidHandRepaymentProgramAlcoholRide(child - 1, rescued) || IsPaidHandRepaymentRescueRide(child - 1, rescued) ||
                 PolicyCounterspellVirtualAlcoholRide(child - 1, rescued) || PaidObserverDamageVirtualAlcoholRide(child - 1, rescued))) return true;
            if (!HalfHandPaidDamageObserverEdge(child) && !OwnedDeathBenefitDeadDyingEdge(child)) return false;
        }
        return true;
    }

    private ProgramSkillFrame? OwnedDeathBenefitObserverRoot() => _resolutionStack.OfType<ProgramSkillFrame>()
        .LastOrDefault(f => f.OwnedDeathBenefitReturn is not null && OwnedDeathBenefitObserverPrefix(f));

    private bool AllowsOwnedDeathBenefitNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            ActiveDying is not null || observer.WindowContext?.Window is not
                (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or
                 SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost)) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.Damage || amount != effect.Amount || source != effect.ActorReference || nature != effect.DamageNature ||
            target != (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target))) return false;
        var root = OwnedDeathBenefitObserverRoot();
        return root is not null && root.Id != observer.Id && root.OwnedDeathBenefitReturn!.OriginalAttackOwnerFrameId == CurrentDamageAttempt?.ResolutionId;
    }

    private bool IsOwnedDeathBenefitProgramDying() => ActiveDying is { } dying && OwnedDeathBenefitObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);

    private bool OwnedDeathBenefitDeadDyingEdge(int index)
    {
        if (_resolutionStack[index] is not DyingFrame dying || !IsOriginalDyingSuspendedByOwnedDeathBenefit(dying)) return false;
        var parent = _resolutionStack[index - 1];
        return dying.Continuation switch
        {
            DyingContinuationKind.ProgramSkill => parent is ProgramSkillFrame program && program.Id == dying.ParentFrameId,
            DyingContinuationKind.Damage => parent is DamageFrame damage && damage.Id == dying.ParentFrameId && damage.TargetSeat == dying.VictimSeat &&
                _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == damage.ParentFrameId) is { AttackAttempt: { } attack, AttackReturn: not null } &&
                attack.TargetSeat == dying.VictimSeat && dying.KillerSeat == (attack.SourceLess ? null : attack.SourceSeat),
            DyingContinuationKind.AttackHpLoss => parent is ProgramSkillFrame { AttackAttempt: { } attack, AttackReturn: not null } program &&
                program.Id == dying.ParentFrameId && attack.TargetSeat == dying.VictimSeat && dying.KillerSeat is null,
            _ => false
        };
    }

    private bool TryAdvanceOwnedDeathBenefitSubtree()
    {
        if (_pendingDecision is not null || OwnedDeathBenefitObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attackProgram)
        {
            if (attackProgram.AttackReturn is null || CurrentDamageAttempt?.ResolutionId != attackProgram.Id || ActiveDying is not null ||
                _resolutionStack.Any(f => f is DamageFrame damage && damage.ParentFrameId == attackProgram.Id ||
                    f is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attackProgram.Id))
                throw new InvalidOperationException("Owned death-benefit damage must finish its actual damage/Dying children before its program tail.");
            // Native damage-window and Dying completion normally reach this
            // synchronously. A restored pending attempt still returns through
            // the mature attack completion, never through the plain executor.
            CompleteDamageAttack(new ProgramAttackHandle(this, attackProgram.Id));
            AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame)
        {
            AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true;
        }
        // A real nested death without configured programs can return normally;
        // its Dying stays visible until that typed child has completed.
        if (top is DeathFrame death)
        { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }

    private void AssertOwnedDeathBenefitReturns()
    {
        var roots = _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OwnedDeathBenefitReturn is not null).ToArray();
        if (roots.Length == 0) return;
        foreach (var root in roots)
            if (!OwnedDeathBenefitObserverPrefix(root))
                throw new InvalidOperationException("Owned death benefit lost its frozen original cursor or exact paid descendant.");
        AssertProgramAttackState();
        AssertProgramSkillState();
    }
}
