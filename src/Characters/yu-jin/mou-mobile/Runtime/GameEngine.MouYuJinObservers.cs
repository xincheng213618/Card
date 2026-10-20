namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MouYuJinPaid(ProgramSkillFrame f) =>
        f.MouYuJinXiayuan is not null && ValidMouXiayuan(f) && MouXiayuanPayment(f) is not null ||
        f.MouYuJinJieyue is not null && ValidMouJieyue(f) && MouJieyueArmorGranted(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29300)]
    private bool KeepPaidMouYuJin(ProgramSkillFrame f) => MouYuJinPaid(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29300)]
    private void AssertMouYuJin(ProgramSkillFrame f)
    {
        if (f.MouYuJinXiayuan is not null && !ValidMouXiayuan(f) || f.MouYuJinJieyue is not null && !ValidMouJieyue(f))
            throw new InvalidOperationException("Mobile Yu Jin lost its original damage invoice, actual Hand2 payment or native Ending grant/gift receipt.");
        if (f.MouYuJinJieyue is not { Stage: MouYuJinJieyueStage.GiftChoice } r || _resolutionStack.LastOrDefault()?.Id != f.Id) return;
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p || p.PlayerSeat != r.RecipientSeat ||
            p.TargetSeat != r.RecipientSeat || !AssistedChoicesEqual(p.Choices, MouJieyueGiftChoices(f)))
            throw new InvalidOperationException("Jieyue's actual beneficiary must retain the canonical independent private gift choice.");
    }
    private bool MouYuJinFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!MouYuJinPaid(f)) return false;
        if (RecoveryReplacementFrameRidesOn(child, f)) return true;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is not CardsMovedTriggerWindowFrame moved || !ProgramMovementReturnMatches(f, moved)) return false;
        if (f.MouYuJinXiayuan is { } cost && f.InstructionIndex == 3 && MouXiayuanPayment(f) is { } paid &&
            f.CardSetBindings.SingleOrDefault(b => b.Name == "xiayuan-cost") is { } bind)
            return moved.Batch.Movements.All(m => _cardMovements.Contains(m) && bind.CardIds.Contains(m.CardId) &&
                m.Sequence > cost.MovementBefore && m.Sequence <= paid.SequenceAfter &&
                (m.From == CardLocation.Hand(f.OwnerSeat) || GetProgramDiscardSource(m) == CardLocation.Hand(f.OwnerSeat)) &&
                (m.Reason.Value == MouXiayuanCostReason || m.Reason.Value == MouXiayuanCostReason + "-finished" || m.Reason.Value == MouXiayuanCostReason + "-discard"));
        if (f.MouYuJinJieyue is not { Stage: MouYuJinJieyueStage.GiftChildren, GiftCardId: { } id,
                GiftFrom: { } from, GiftTo: { } to, GiftMovementSequence: { } sequence } gift) return false;
        return moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
            (m.CardId == id && m.Sequence == sequence && m.From == from && m.To == to && m.Reason.Value == MouJieyueGiftReason ||
             from.Zone == CardZoneKind.Equipment && GetAdvancedCard(id).Kind == CardKind.WoodenOx && m.Sequence > sequence &&
             m.From == new CardLocation(CardZoneKind.WoodenOxGrain, gift.RecipientSeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard));
    }
    private bool MouYuJinObserverSuffix(int index) => HasObserverSuffix(index, static (g, child) =>
        g.IsMovementObserverEdge(child) || g.IsSameNameHandObserverEdge(child) || g.IsYanPuDyingExitEdge(child) ||
        g.TypedDamagePostEventEdge(g._resolutionStack[child - 1], g._resolutionStack[child]) ||
        g._resolutionStack[child - 1] is ProgramSkillFrame parent && g.MouYuJinFirstChild(parent, g._resolutionStack[child]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]),
        static (g, child, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(child, dying));
    private ProgramSkillFrame? MouYuJinObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouYuJinFirstChild(f, child),
        static (g, index) => g.MouYuJinObserverSuffix(index));
    private bool HasMouYuJinDamageObserver(long id) => MouYuJinObserverRoot() is { } root &&
        (root.MouYuJinXiayuan?.OriginalDamageWindowId == id ||
         _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id));
    private bool HasMouYuJinBeforeDamageObserver(long id) => MouYuJinObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsMouYuJinDying() => ActiveDying is { } dying && MouYuJinObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsMouYuJinNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouYuJinObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsMouYuJinRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!MouYuJinPaid(f) || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && MouYuJinFirstChild(f, _resolutionStack[parent + 1]) &&
            MouYuJinObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29300)]
    private bool AdvanceMouYuJinChildren() => _pendingDecision is null && MouYuJinObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
