namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasRecipientDamageCardColorHealth(int seat) => IsValidPlayerSeat(seat) && _players[seat].IsAlive &&
        GetSkillBindingShard(_players[seat]).GetInstanceTriggers(SkillProgramTriggerWindow.BeforeDamageApplied)
            .Any(binding => ResolveDamageCardColorHealthDescriptor.IsTrigger(binding.Trigger));

    // A real native use can be zero-entity. Raw Program Damage has neither a card nor a native card-use origin.
    private bool TryGetDamageHealthCard(IDamageAttempt attack, out CardKind kind, out Suit suit, out bool red,
        out CardUseFrame? use)
    {
        use = LifecycleCardUse(attack.ResolutionId);
        var action = use?.Action;
        kind = action?.EffectiveKind ?? attack.EffectiveCardKind ?? attack.Card?.Kind ?? default;
        suit = action?.EffectiveSuit ?? attack.Card?.Suit ?? Suit.None;
        red = action?.EffectiveIsRed ?? (suit is Suit.Heart or Suit.Diamond);
        if (use is { } nativeUse)
            return nativeUse.CardKind == kind && (action is null || action.Type == CardActionType.Use && action.EffectiveKind == kind) &&
                ProgramEventHistory<CardUseDeclaredEvent>().Count(e => e.ResolutionId == nativeUse.Id && e.CardId == nativeUse.CardId) == 1;
        if (attack.Card is not null) return true;
        return attack is CardAttackHandle cardAttack && kind == CardKind.Duel &&
            IsProgramVirtualDuelAttackConsistent(cardAttack, Array.Empty<Card>());
    }

    private bool ExactDamageColorHealthParent(ProgramSkillFrame f, out BeforeDamageProgramWindowFrame window, out IDamageAttempt attack)
    {
        window = null!; attack = null!;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 1 || f.InstructionIndex != 1 || f.TriggerId is null ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, Amount: > 0 } context ||
            _resolutionStack[index - 1] is not BeforeDamageProgramWindowFrame before || before.Id != context.ParentFrameId ||
            before.TargetSeat != f.OwnerSeat || context.OwnerSeat != f.OwnerSeat || context.TargetSeat != f.OwnerSeat ||
            before.Amount != context.Amount || before.Prevented || before.RedirectedTargetSeat is not null ||
            before.CandidateIndex < 0 || before.CandidateIndex >= before.Candidates.Count ||
            !MountObserverCandidateMatches(f, before.Candidates[before.CandidateIndex].Candidate) ||
            !ResolveDamageCardColorHealthDescriptor.IsTrigger(GetProgramTrigger(f))) return false;
        var native = LastDamageSourceOriginalAttack(before.ContinuationAttackResolutionId ?? before.ParentFrameId);
        if (native is null || native.TargetSeat != before.TargetSeat || native.SourceSeat != before.SourceSeat ||
            context.SourceSeat != (native.IsSourceLess ? null : native.SourceSeat) || GetDamageNature(native) != before.Nature)
            return false;
        window = before; attack = native; return true;
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 2403, SkillProgramEffectOp.ResolveDamageCardColorHealth)]
    private bool CanOfferDamageCardColorHealth(ProgramTriggerLegalityContext c) =>
        c.WindowContext is { Amount: > 0, TargetSeat: { } target } && target == c.Owner.Seat && c.Owner.HasHpValue &&
        _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(w => w.Id == c.Context.ParentFrameId) is { } before &&
        LastDamageSourceOriginalAttack(before.ContinuationAttackResolutionId ?? before.ParentFrameId) is { } attack &&
        TryGetDamageHealthCard(attack, out _, out _, out _, out _);

    private SkillProgramStepOutcome ResolveDamageCardColorHealth(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.DamageCardColorHealth is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            !ExactDamageColorHealthParent(f, out var before, out var attack) ||
            !TryGetDamageHealthCard(attack, out var kind, out var suit, out var red, out var use))
            throw new InvalidOperationException("Damage-card color health lost its exact positive native before-damage candidate.");
        var owner = _players[f.OwnerSeat];
        if (!owner.IsAlive || !owner.HasHpValue || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var receipt = new ProgramDamageCardColorHealthReceipt(f.Id, f.InstructionIndex,
            new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), f.GameplayHash,
            before.Id, attack.ResolutionId, use?.Id, use?.Action?.ActionId, attack.Card?.Id, f.OwnerSeat,
            attack.IsSourceLess ? null : attack.SourceSeat, before.Amount, kind, suit, red,
            owner.Hp, owner.MaxHp, red ? checked(owner.MaxHp + before.Amount) : owner.MaxHp);
        ReplaceRuntimeTop(f = f with { DamageCardColorHealth = receipt });
        if (red) ChangeProgramMaximumHp(f, receipt.Amount);
        else new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, receipt.Amount, null, null);
        AdvanceEventRulesAndQueueFact(new ProgramDamageCardColorHealthPaidEvent(receipt));
        AdvanceRuntimeProgram(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool ValidDamageCardColorHealth(ProgramSkillFrame f)
    {
        if (f.DamageCardColorHealth is not { } r || r.ProgramFrameId != f.Id || r.InstructionIndex != f.InstructionIndex ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.PendingMovementContinuation is not null ||
            !ExactDamageColorHealthParent(f, out var before, out var attack) ||
            !TryGetDamageHealthCard(attack, out var kind, out var suit, out var red, out var use)) return false;
        return r.BeforeDamageWindowId == before.Id && r.AttackResolutionId == attack.ResolutionId && r.CardUseFrameId == use?.Id &&
            r.CardActionId == use?.Action?.ActionId && r.PhysicalCardId == attack.Card?.Id && r.OwnerSeat == f.OwnerSeat &&
            r.DamageSourceSeat == (attack.IsSourceLess ? null : attack.SourceSeat) && r.Amount == before.Amount && r.Amount > 0 &&
            r.EffectiveCardKind == kind && r.EffectiveSuit == suit && r.IsRed == red && r.MaxHpBefore > 0 &&
            r.MaxHpAfter == (red ? checked(r.MaxHpBefore + r.Amount) : r.MaxHpBefore) &&
            ProgramEventHistory<ProgramDamageCardColorHealthPaidEvent>().Count(e => e.Receipt == r) == 1 &&
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.SkillId == f.SkillId && e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId &&
                e.Window == SkillProgramTriggerWindow.BeforeDamageApplied) == 1;
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 2403)]
    private bool ResumeDamageCardColorHealth(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.DamageCardColorHealth is null) return false;
        AssertDamageCardColorHealth(f);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        AdvanceEventRulesAndQueueFact(new ProgramDamageCardColorHealthCompletedEvent(id));
        FinishProgramSkill(f, true); return true;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 2403)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 2403)]
    private bool CanContinueDamageCardColorHealth(ProgramSkillFrame f) => f.DamageCardColorHealth is not null && ValidDamageCardColorHealth(f);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 2403)]
    private void AssertDamageCardColorHealth(ProgramSkillFrame f)
    {
        if (f.DamageCardColorHealth is not null && !ValidDamageCardColorHealth(f))
            throw new InvalidOperationException("Damage-card color health lost its issued amount, causing-card or exact native parent.");
    }

    private bool DamageCardColorHealthFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (!ValidDamageCardColorHealth(root)) return false;
        var r = root.DamageCardColorHealth!;
        var history = CompleteProgramEventHistory().ToArray();
        if (child is RecoveryReplacementFrame recovery)
            return !r.IsRed && ProgramRecoveryReplacementReceiptMatches(root, recovery, history) &&
                recovery.Attempt.SourceSeat == root.OwnerSeat && recovery.Attempt.TargetSeat == root.OwnerSeat &&
                recovery.Attempt.Amount == (TracksRecoveryAmountBonuses ? r.Amount : Math.Min(r.Amount, r.MaxHpBefore - r.HpBefore));
        if (child is HpChangedTriggerWindowFrame hp)
            return !r.IsRed && ProgramRecoveryReceiptMatches(root, hp, history) && hp.Change.SourceSeat == root.OwnerSeat &&
                hp.Change.TargetSeat == root.OwnerSeat && hp.Change.HpBefore == r.HpBefore && hp.Change.Amount == Math.Min(r.Amount, r.MaxHpBefore - r.HpBefore);
        return OrderedPrintedSkillLossSkillsChangedEdge(root, child);
    }

    private bool DamageCardColorHealthObserverEdge(int index)
    {
        var parent = _resolutionStack[index - 1]; var child = _resolutionStack[index];
        if (parent is ProgramSkillFrame root && root.DamageCardColorHealth is not null) return DamageCardColorHealthFirstChild(root, child);
        if (parent is RecoveryReplacementFrame recovery)
        {
            var history = CompleteProgramEventHistory().ToArray();
            if (child is HpChangedTriggerWindowFrame hp) return TypedRecoveryHpReturnMatches(recovery, hp, history);
            if (child is CardsMovedTriggerWindowFrame moved) return TypedRecoveryRewardReturnMatches(recovery, moved, history);
        }
        return NativeDrawAttemptRidesOn(child, parent) || OrderedPrintedSkillLossSkillsChangedEdge(parent, child) ||
            TypedDamagePostEventEdge(parent, child) || HalfHandPaidDamageObserverEdge(index) || PreventionDrawObserverEdge(index);
    }

    private ProgramSkillFrame? DamageCardColorHealthObserverRoot() => FindProgramObserverRoot(
        static (g, root, child) => root.DamageCardColorHealth is not null && g.DamageCardColorHealthFirstChild(root, child),
        static (g, index) => g.HasObserverSuffix(index, static (host, child) => host.DamageCardColorHealthObserverEdge(child),
            static (host, child, dying) => host.IsSuspendedOrPaidRescueObserverSuffix(child, dying)));

    private bool HasDamageCardColorHealthBeforeDamage(long id) => DamageCardColorHealthObserverRoot() is { } root &&
        (root.DamageCardColorHealth!.BeforeDamageWindowId == id ||
         _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id));
    private bool HasDamageCardColorHealthDamageObserver(long id) => DamageCardColorHealthObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsDamageCardColorHealthDying() => ActiveDying is { } dying && DamageCardColorHealthObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);

    private bool AllowsDamageCardColorHealthNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            DamageCardColorHealthObserverRoot() is not { } root || root.Id == f.Id ||
            f.WindowContext?.Window is not (SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or
                SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved)) return false;
        var effect = TypedRecoveryPausedEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source &&
            effect.DamageNature == nature && target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) :
                ResolveProgramEffectTarget(f, effect.Target));
    }

    private bool IsDamageCardColorHealthRecoveryParent(ProgramSkillFrame frame, RecoveryReplacementFrame recovery)
    {
        if (frame.DamageCardColorHealth is null || !ValidDamageCardColorHealth(frame) || recovery.ParentFrameId == frame.Id)
            return false;
        var parent = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        var child = _resolutionStack.FindIndex(f => f.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && DamageCardColorHealthFirstChild(frame, _resolutionStack[parent + 1]) &&
            HasObserverSuffix(parent, static (host, index) => host.DamageCardColorHealthObserverEdge(index),
                static (host, index, dying) => host.IsSuspendedOrPaidRescueObserverSuffix(index, dying)) &&
            RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 2403)]
    private bool AdvanceDamageCardColorHealthChildren() => _pendingDecision is null && DamageCardColorHealthObserverRoot() is not null &&
        TryAdvanceObserverSubtree(true);

    [ProgramCapability]
    private sealed class DamageCardColorHealthCapability(GameEngine engine) : IDamageCardColorHealthHost
    {
        public SkillProgramStepOutcome ResolveDamageCardColorHealth(ProgramSkillFrame frame) => engine.ResolveDamageCardColorHealth(frame);
    }
}
