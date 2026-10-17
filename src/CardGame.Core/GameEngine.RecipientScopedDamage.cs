namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasShenwuRecipientDamageModifiers =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.PreventShenwuNitaiOwnTurnDamage) ||
        _contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.ShenwuArmedSourceDamageBonus);
    private bool NeedsRecipientScopedDamageBase => HasSignedDamagePayments || HasShenwuRecipientDamageModifiers || IsGuanduActivity;
    private bool SupportsIssuedRecipientDamageScope(RecipientScopedDamageState scope) =>
        NeedsRecipientScopedDamageBase || scope.PublicEnemyObserver && HasPublicEnemyParticipantDamageModifiers;
    private bool HasPublicEnemyRecipientDamageScope(IDamageAttempt attack) =>
        (attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).RecipientDamageScope :
            GetCardAttackState(attack.ResolutionId).RecipientDamageScope)?.PublicEnemyObserver == true;
    private void CaptureRecipientScopedDamageBase(IDamageAttempt attack, IReadOnlyList<int> targets)
    {
        if (targets.Count == 0) return;
        // Only an observer eligible for this source and at least one possible
        // recipient needs a new base. Its first eligible recipient may be later
        // in the chain; subsequent death or suppression cannot revoke issuance.
        var publicEnemyObserver = HasEligiblePublicEnemyDamageObserver(attack, targets);
        if (!NeedsRecipientScopedDamageBase && !publicEnemyObserver) return;
        if (!SameAttackOwner(attack, CurrentDamageAttempt) || !attack.DamageAmountFinalized || attack.IsChainPropagation || attack.DamageAmount <= 0)
            throw new InvalidOperationException("Recipient damage base must originate from the finalized first elemental recipient.");
        var scope = new RecipientScopedDamageState(attack.TargetSeat, attack.TargetSeat, attack.DamageAmount)
            { PublicEnemyObserver = publicEnemyObserver, ProgramUseInstructionIndex = DamageProgramUseInstructionIndex(attack) };
        if (attack is ProgramAttackHandle)
            UpdateProgramAttackState(attack.ResolutionId, state => state with { RecipientDamageScope = scope });
        else UpdateCardAttackState(attack.ResolutionId, state => state! with { RecipientDamageScope = scope });
        AdvanceEventRulesAndQueueFact(new RecipientScopedDamageBaseEvent(attack.ResolutionId, attack.TargetSeat, attack.DamageAmount)
            { PublicEnemyObserver = publicEnemyObserver, ProgramUseInstructionIndex = scope.ProgramUseInstructionIndex });
    }
    private void RestoreRecipientScopedDamageBase(IDamageAttempt attack, int fromSeat)
    {
        var scope = attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).RecipientDamageScope : GetCardAttackState(attack.ResolutionId).RecipientDamageScope;
        if (scope is null) return;
        if (!SupportsIssuedRecipientDamageScope(scope) || scope.ProgramUseInstructionIndex != DamageProgramUseInstructionIndex(attack) ||
            !attack.IsChainPropagation || scope.CurrentTargetSeat != fromSeat || scope.PropagationBaseAmount <= 0 ||
            CompleteProgramEventHistory().OfType<RecipientScopedDamageBaseEvent>().Count(e => e.AttackFrameId == attack.ResolutionId &&
                e.FirstTargetSeat == scope.FirstTargetSeat && e.Amount == scope.PropagationBaseAmount &&
                e.PublicEnemyObserver == scope.PublicEnemyObserver && e.ProgramUseInstructionIndex == scope.ProgramUseInstructionIndex) != 1)
            throw new InvalidOperationException("Recipient damage propagation lost its exact frozen base.");
        if (attack is ProgramAttackHandle)
            UpdateProgramAttackState(attack.ResolutionId, state => state with
                { DamageAmount = scope.PropagationBaseAmount, RecipientDamageScope = scope with { CurrentTargetSeat = attack.TargetSeat } });
        else UpdateCardAttackState(attack.ResolutionId, state => state! with
                { DamageAmount = scope.PropagationBaseAmount, RecipientDamageScope = scope with { CurrentTargetSeat = attack.TargetSeat } });
        AdvanceEventRulesAndQueueFact(new RecipientScopedDamageAdvancedEvent(attack.ResolutionId, fromSeat, attack.TargetSeat, scope.PropagationBaseAmount)
            { ProgramUseInstructionIndex = scope.ProgramUseInstructionIndex });
    }
    private void AdjustExactRecipientDamage(ProgramSkillFrame f, ProgramSignedDamagePaymentReceipt receipt, int after)
    {
        if (!IsValidSignedDamagePayment(f, true) || receipt.Stage != SignedDamagePaymentStage.Paid ||
            CurrentDamageAttempt is not { IsSourceLess: false, DamageAmountFinalized: true } attack ||
            attack.ResolutionId != receipt.OriginalAttackFrameId || attack.SourceSeat != receipt.SourceSeat || attack.TargetSeat != receipt.TargetSeat ||
            attack.DamageAmount != receipt.OriginalAmount || GetDamageNature(attack) != receipt.Nature || after != Math.Max(0, checked(receipt.OriginalAmount + receipt.Delta)))
            throw new InvalidOperationException("Recipient damage adjustment lost its paid original attack and current target.");
        // Do not use the older aggregate DamageWasApplied guard: it protects the
        // entire use, while this new receipt proves the current chained recipient.
        if (attack is ProgramAttackHandle)
            UpdateProgramAttackState(attack.ResolutionId, state => state with { DamageAmount = after });
        else UpdateCardAttackState(attack.ResolutionId, state => state! with { DamageAmount = after });
    }
    private void UpdateRecipientScopedDamageRedirect(IDamageAttempt attack)
    {
        if (attack is ProgramAttackHandle)
            UpdateProgramAttackState(attack.ResolutionId, state => state.RecipientDamageScope is { } scope
                ? state with { RecipientDamageScope = scope with { CurrentTargetSeat = attack.TargetSeat } } : state);
        else if (GetCardAttackState(attack.ResolutionId).RecipientDamageScope is { } scope)
            UpdateCardAttackState(attack.ResolutionId, state => state! with { RecipientDamageScope = scope with { CurrentTargetSeat = attack.TargetSeat } });
    }
    private void AssertRecipientScopedDamageStates()
    {
        foreach (var frame in _resolutionStack)
        {
            var scope = frame switch { ProgramSkillFrame { AttackAttempt: { } a } => a.RecipientDamageScope,
                CardUseFrame { CardAttack: { } a } => a.RecipientDamageScope,
                ProgramSkillFrame { CardAttack: { } a } => a.RecipientDamageScope,
                JudgmentFrame { CardAttack: { } a } => a.RecipientDamageScope, _ => null };
            if (scope is null) continue;
            var target = frame is ProgramSkillFrame { AttackAttempt: { } attempt } ? attempt.TargetSeat : GetCardAttackState(frame.Id).TargetSeat;
            var chain = frame is ProgramSkillFrame { AttackAttempt: { } attempt2 } ? attempt2.IsChainPropagation : GetCardAttackState(frame.Id).IsChainPropagation;
            var instructionIndex = frame is ProgramSkillFrame { AttackAttempt: not null } ? null : FrozenProgramDuelInstructionIndex(frame.Id);
            if (!SupportsIssuedRecipientDamageScope(scope) || scope.ProgramUseInstructionIndex != instructionIndex ||
                scope.CurrentTargetSeat != target || scope.PropagationBaseAmount <= 0 ||
                CompleteProgramEventHistory().OfType<RecipientScopedDamageBaseEvent>().Count(e => e.AttackFrameId == frame.Id &&
                    e.FirstTargetSeat == scope.FirstTargetSeat && e.Amount == scope.PropagationBaseAmount &&
                    e.PublicEnemyObserver == scope.PublicEnemyObserver && e.ProgramUseInstructionIndex == scope.ProgramUseInstructionIndex) != 1 ||
                chain && !CompleteProgramEventHistory().OfType<RecipientScopedDamageAdvancedEvent>().Any(e => e.AttackFrameId == frame.Id && e.TargetSeat == target && e.Amount == scope.PropagationBaseAmount &&
                    e.ProgramUseInstructionIndex == scope.ProgramUseInstructionIndex) &&
                    !CompleteProgramEventHistory().OfType<ProgramDamageTransferredEvent>().Any(e => e.ResolutionId == frame.Id && e.TargetSeat == target))
                throw new InvalidOperationException("Recipient scoped damage state lost its issued base or actual propagation target.");
        }
    }
}
