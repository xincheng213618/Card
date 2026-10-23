namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Callers prove their own root, first child, payment and complete observer
    // suffix before reaching this executor. Frame kinds here describe how to
    // advance a proven child; they never grant admission to an unrelated child.
    private bool TryAdvanceObserverSubtree(bool advanceRecoveryReplacement,
        string? incompleteAttackError = null, bool routeUnreturnedAttackThroughRuntime = false)
    {
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack &&
            (!routeUnreturnedAttackThroughRuntime || attack.AttackReturn is not null))
        {
            if (attack.AttackReturn is null || CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null ||
                _resolutionStack.Any(frame => frame is DamageFrame damage && damage.ParentFrameId == attack.Id ||
                    frame is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attack.Id))
            {
                if (incompleteAttackError is not null) throw new InvalidOperationException(incompleteAttackError);
                return false;
            }

            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id));
        }
        else if (top is HappyQiaoHealthChoiceFrame or ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or
            ProgramLifecycleTriggerWindowFrame or ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or
            ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame ||
            advanceRecoveryReplacement && top is RecoveryReplacementFrame)
        {
            AdvanceRuntimeFrame(top.Id);
        }
        else if (top is DeathFrame death)
        {
            ContinueDeathResolution(death.Id);
        }
        else
        {
            // Card uses (including Dying-suit Peach) and other native frames
            // retain their own dispatch and typed return.
            return false;
        }

        AdvanceRulesAndPublishState();
        return true;
    }

    private ProgramSkillFrame? FindProgramObserverRoot(
        Func<GameEngine, ProgramSkillFrame, ResolutionFrame, bool> firstChild,
        Func<GameEngine, int, bool> suffix, long? requiredRootId = null)
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
            if (_resolutionStack[index] is ProgramSkillFrame root &&
                (requiredRootId is null || root.Id == requiredRootId) &&
                firstChild(this, root, _resolutionStack[index + 1]) && suffix(this, index)) return root;
        return null;
    }

    // Policies remain explicit: an edge must be proved before a Dying helper
    // may certify the remainder. No default edge set or default rescue bypass
    // is supplied to a newly added mechanism.
    private bool HasObserverSuffix(int rootIndex, Func<GameEngine, int, bool> edge,
        Func<GameEngine, int, DyingFrame, bool>? completedDyingSuffix)
    {
        for (var child = rootIndex + 2; child < _resolutionStack.Count; child++)
        {
            if (!edge(this, child)) return false;
            if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                completedDyingSuffix?.Invoke(this, child, dying) == true) return true;
        }
        return true;
    }

    private static bool IsObserverSkillsChangedEdge(ResolutionFrame parent, ResolutionFrame child) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ObserverSubtrees.IsObserverSkillsChangedEdge(parent,child);

    private bool IsPaidRescueObserverSuffix(int child, DyingFrame dying) =>
        IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
        PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying);

    private bool IsSuspendedOrPaidRescueObserverSuffix(int child, DyingFrame dying) =>
        IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying) ||
        IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) || IsOriginalDyingSuspendedByHeJinYanhuo(dying) || IsOriginalDyingSuspendedByDiou(dying) ||
        IsOriginalDyingSuspendedByWuZishengMovement(dying) || IsPaidRescueObserverSuffix(child, dying);

    private bool HasMovementObserverSuffix(int rootIndex) => HasObserverSuffix(rootIndex,
        static (engine, child) => engine.IsMovementObserverEdge(child),
        static (engine, child, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(child, dying));

    // Paired-hand recast and outside-phase draw/discard share this exact native
    // suffix. Their separate FirstChild methods still prove distinct receipts.
    private bool IsMovementObserverEdge(int child)
    {
        var parent = _resolutionStack[child - 1];
        var current = _resolutionStack[child];
        return HappyQiaoHealthChoiceRidesOn(current, parent) || HappyQiaoShutuDeclarationEdge(child) || IsObserverSkillsChangedEdge(parent, current) || DyingSuitsStructuralEdge(parent, current) ||
            SameNameHandStructuralEdge(parent, current) || CompletedUndamagedTargetRevealStructuralEdge(parent, current) ||
            RecipientCategoryMarkStructuralEdge(parent, current) || OffTurnUsedCardGiftStructuralEdge(parent, current) ||
            CardSupplyCompletionStructuralEdge(parent, current) || ResponseCompletionStructuralEdge(parent, current) ||
            OriginalHandEntityStructuralEdge(parent, current) || OutsidePhaseDrawDiscardStructuralEdge(parent, current) ||
            HalfHandPaidDamageObserverEdge(child) || PaidTargetObserverEdge(child) || IsYanPuDyingExitEdge(child) || HappyDianWeiStructuralEdge(current, parent) || HappyPangTongStructuralEdge(current, parent) || HappyDiaoChanStructuralEdge(current, parent);
    }

    private bool HasHandGainDebtObserverSuffix(int rootIndex) => HasObserverSuffix(rootIndex,
        static (engine, child) => IsObserverSkillsChangedEdge(engine._resolutionStack[child - 1], engine._resolutionStack[child]) ||
            engine.HalfHandPaidDamageObserverEdge(child) || engine.PaidTargetObserverEdge(child) || engine.IsYanPuDyingExitEdge(child) ||
            engine.DyingSuitsStructuralEdge(engine._resolutionStack[child - 1], engine._resolutionStack[child]),
        static (engine, child, dying) => engine.IsPaidRescueObserverSuffix(child, dying) ||
            engine.TieredRoundZeroDyingRescueRide(child, dying) || (engine.DrawFundedDistinctBasicDyingRescueRide(child, dying) || engine.HuanpeiDyingRescueRide(child, dying)));

    // Draw advice, gifted-Slash rewards, pile cash-out and Liejie admit this
    // same suffix. Each caller still proves its own first paid child.
    private bool HasPaidDamageObserverSuffix(int rootIndex) => HasObserverSuffix(rootIndex,
        static (engine, child) => IsObserverSkillsChangedEdge(engine._resolutionStack[child - 1], engine._resolutionStack[child]) ||
            engine.DyingSuitsStructuralEdge(engine._resolutionStack[child - 1], engine._resolutionStack[child]) ||
            engine.HalfHandPaidDamageObserverEdge(child) || engine.PaidTargetObserverEdge(child) || engine.IsYanPuDyingExitEdge(child),
        static (engine, child, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(child, dying));
}
