namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsAlcoholSlashSkillReplacementTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.ReplaceOwnSkillsAfterAlcoholSlashDamage }];

    private static string AlcoholSlashReplacementGrantSource(int owner, string skill, string instance) =>
        $"acquired:alcohol-slash-replacement:{owner}:{skill}:{instance}";

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 941)]
    private bool CanOfferAlcoholSlashSkillReplacement(SkillProgramTrigger trigger, ProgramSkillWindowContext context, int ownerSeat)
    {
        if (!IsAlcoholSlashSkillReplacementTrigger(trigger)) return true;
        if (!IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive || context.OwnerSeat != ownerSeat ||
            context is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, SourceSeat: { } source } || source != ownerSeat ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(f => f.Id == context.ParentFrameId) is not { } window)
            return false;
        var attack = GetDamageTriggerAttack(window);
        return TryGetPermanentAlcoholSlashUse(attack, out _);
    }

    private bool TryGetPermanentAlcoholSlashUse(IDamageAttempt attack, out CardUseFrame use)
    {
        use = null!;
        if (attack.IsSourceLess || !attack.DamageWasApplied || attack.DamageAmount <= 0 ||
            attack.SourceSeat != attack.CardUserSeat || attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) ||
            LifecycleCardUse(attack.ResolutionId) is not { ConsumedAlcoholBoost: true, ConsumedAlcoholBonus: > 0,
                Action: { Type: CardActionType.Use } action } original || action.ActorSeat != attack.SourceSeat ||
            original.SourceSeat != attack.SourceSeat || original.CardKind != kind || action.EffectiveKind != kind)
            return false;
        if (ProgramEventHistory<CardUseAlcoholConsumptionCapturedEvent>().Where(e => e.CardUseFrameId == original.Id).ToArray() is not [var fact] ||
            fact.ActionId != action.ActionId || fact.ActorSeat != attack.SourceSeat || fact.AlcoholBonus != original.ConsumedAlcoholBonus)
            return false;
        use = original;
        return true;
    }

    private SkillProgramStepOutcome ReplaceOwnSkillsAfterAlcoholSlashDamage(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (_resolutionStack.LastOrDefault()?.Id != f.Id || f.AlcoholSlashSkillReplacement is not null || f.InstructionIndex != 1 ||
            !IsAlcoholSlashSkillReplacementTrigger(GetProgramTrigger(f)) || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out var attack) ||
            attack.SourceSeat != f.OwnerSeat || !TryGetPermanentAlcoholSlashUse(attack, out var use))
            throw new InvalidOperationException("Permanent Alcohol Slash replacement requires the exact live source and already paid native damage segment.");
        var effect = GetProgramTrigger(f).Effects.Single();
        var owner = _players[f.OwnerSeat];
        var sourceId = AlcoholSlashReplacementGrantSource(f.OwnerSeat, f.SkillId, f.SkillInstanceId);
        var grantId = sourceId + ":" + effect.SourceBind;
        var existing = owner.SkillGrants.Grants.SingleOrDefault(g => g.GrantId == grantId);
        var removed = owner.SkillGrants.Grants.Where(g => !g.IsModeRule && effect.SkillIds.Contains(g.SkillId, StringComparer.Ordinal)).ToArray();
        var pendingOrdered = _orderedPrintedSkillLossLeases.Where(l => l.Source.OwnerSeat == f.OwnerSeat)
            .SelectMany(lease => lease.RemovedGrants.Where(g => !g.IsModeRule && effect.SkillIds.Contains(g.SkillId, StringComparer.Ordinal))
                .Where(g => !WasGrantPermanentlyRevokedAfterLoss(f.OwnerSeat, g,
                    CompleteProgramEventHistory().OfType<OrderedPrintedSkillGrantRemovedEvent>()
                        .Single(e => e.FrameId == lease.ProgramFrameId && e.Grant == g))));
        var pendingHell = HellSoulOutstandingLosses(f.OwnerSeat)
            .Where(loss => !loss.Grant.IsModeRule && effect.SkillIds.Contains(loss.Grant.SkillId, StringComparer.Ordinal) &&
                !WasGrantPermanentlyRevokedAfterLoss(f.OwnerSeat, loss.Grant, loss)).Select(loss => loss.Grant);
        var revoked = removed.Concat(pendingOrdered).Concat(pendingHell)
            .DistinctBy(g => (g.GrantId, g.SkillId, g.SkillInstanceId, g.SourceId)).ToArray();
        // Later chain recipients share the original paid use but do not issue an already completed exchange again.
        if (existing is not null && revoked.Length == 0) return SkillProgramStepOutcome.Continue;
        var priorIssuance = ProgramEventHistory<ProgramAlcoholSlashSkillsReplacedEvent>().Select(e => e.Receipt)
            .FirstOrDefault(r => r.Source.OwnerSeat == f.OwnerSeat && r.GrantAdded && r.GrantedGrant.GrantId == grantId);
        var shared = existing is null
            ? GetSkillBindingShard(owner).ActiveGrants.Where(g => g.SkillId == effect.SourceBind)
                .OrderBy(g => g.SourceId.StartsWith("turn:", StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(g => g.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault()
            : priorIssuance?.SharedInstanceGrant;
        var instance = shared?.SkillInstanceId ?? grantId;
        var granted = existing ?? new SkillGrant(grantId, effect.SourceBind!, instance, sourceId);
        if (granted.SkillId != effect.SourceBind || granted.SkillInstanceId != instance || granted.SourceId != sourceId ||
            granted.IsModeRule || granted.TurnExpiry is not null || granted.PhaseExpiry is not null || granted.NextActualTurnStartExpiry is not null)
            throw new InvalidOperationException("A permanent Alcohol Slash grant changed its independently owned source.");
        var r = new AlcoholSlashSkillReplacementReceipt { ProgramFrameId = f.Id, InstructionIndex = f.InstructionIndex,
            Source = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), GameplayHash = f.GameplayHash,
            DamageWindowId = window.Id, DamageFrameId = damage.Id, CardUseFrameId = use.Id, CardActionId = use.Action!.ActionId,
            TargetSeat = attack.TargetSeat, DamageAmount = damage.Amount, Nature = damage.Nature, AlcoholBonus = use.ConsumedAlcoholBonus!.Value,
            LostSkillIds = effect.SkillIds, RemovedGrants = removed, RevokedGrants = revoked, GrantedGrant = granted,
            SharedInstanceGrant = shared, GrantAdded = existing is null };
        ReplaceRuntimeTop(f = f with { AlcoholSlashSkillReplacement = r });
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        foreach (var grant in removed)
            if (!owner.SkillGrants.RemoveGrant(grant.GrantId)) throw new InvalidOperationException("Permanent skill loss was paid twice or changed its grant.");
        if (existing is null) owner.SkillGrants.Grant(granted);
        AdvanceEventRulesAndQueueFact(new ProgramAlcoholSlashSkillsReplacedEvent(r));
        if (existing is null)
        {
            RegisterTaggedConversionSkill(owner, granted.SkillId);
            AdvanceEventRulesAndQueueFact(new SkillsAcquiredEvent(owner.Seat, f.SkillId, Array.AsReadOnly(new[] { granted.SkillId })));
        }
        AdvanceRuntimeProgram(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool ValidAlcoholSlashSkillReplacementReceipt(ProgramSkillFrame f)
    {
        if (f.AlcoholSlashSkillReplacement is not { } r || f.InstructionIndex != 1 || r.InstructionIndex != 1 || r.ProgramFrameId != f.Id ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.PendingMovementContinuation is not null ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash ||
            !IsAlcoholSlashSkillReplacementTrigger(GetProgramTrigger(f)) ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out var attack) ||
            attack.SourceSeat != f.OwnerSeat || !TryGetPermanentAlcoholSlashUse(attack, out var use) ||
            r.DamageWindowId != window.Id || r.DamageFrameId != damage.Id || r.CardUseFrameId != use.Id || r.CardActionId != use.Action!.ActionId ||
            r.TargetSeat != damage.TargetSeat || r.DamageAmount != damage.Amount || r.Nature != damage.Nature || r.AlcoholBonus != use.ConsumedAlcoholBonus)
            return false;
        var effect = GetProgramTrigger(f).Effects.Single();
        var source = AlcoholSlashReplacementGrantSource(f.OwnerSeat, f.SkillId, f.SkillInstanceId);
        var expectedGrantId = source + ":" + effect.SourceBind;
        var facts = ProgramEventHistory<ProgramAlcoholSlashSkillsReplacedEvent>().Where(e => e.Receipt.ProgramFrameId == f.Id).ToArray();
        var expectedInstance = r.SharedInstanceGrant?.SkillInstanceId ?? expectedGrantId;
        var earlierIssuance = r.GrantAdded || CompleteProgramEventHistory().TakeWhile(e => e is not ProgramAlcoholSlashSkillsReplacedEvent issued || issued.Receipt.ProgramFrameId != f.Id)
            .OfType<ProgramAlcoholSlashSkillsReplacedEvent>().Any(e => e.Receipt.GrantAdded && e.Receipt.Source == r.Source &&
                SamePermanentGrantIdentity(e.Receipt.GrantedGrant, r.GrantedGrant) && e.Receipt.SharedInstanceGrant == r.SharedInstanceGrant);
        return r.LostSkillIds.SequenceEqual(effect.SkillIds) && r.RemovedGrants.All(g => !g.IsModeRule && r.LostSkillIds.Contains(g.SkillId, StringComparer.Ordinal)) &&
            r.RevokedGrants.All(g => !g.IsModeRule && r.LostSkillIds.Contains(g.SkillId, StringComparer.Ordinal)) &&
            r.RemovedGrants.All(g => r.RevokedGrants.Any(revoked => SamePermanentGrantIdentity(g, revoked))) &&
            r.RevokedGrants.Select(g => (g.GrantId, g.SkillId, g.SkillInstanceId, g.SourceId)).Distinct().Count() == r.RevokedGrants.Count &&
            r.RemovedGrants.Select(g => g.GrantId).Distinct(StringComparer.Ordinal).Count() == r.RemovedGrants.Count &&
            (r.SharedInstanceGrant is null || r.SharedInstanceGrant.IsEnabled && r.SharedInstanceGrant.SkillId == effect.SourceBind) && earlierIssuance &&
            (r.GrantedGrant with { IsEnabled = true }) == new SkillGrant(expectedGrantId, effect.SourceBind!, expectedInstance, source) &&
            (!r.GrantAdded || r.GrantedGrant.IsEnabled) &&
            facts is [var issued] && SameAlcoholSlashReplacementReceipt(r, issued.Receipt) &&
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.SkillId == f.SkillId && e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId &&
                e.Window == SkillProgramTriggerWindow.AfterDamageApplied) == 1;
    }

    private static bool SamePermanentGrantIdentity(SkillGrant left, SkillGrant right) => left.GrantId == right.GrantId &&
        left.SkillId == right.SkillId && left.SkillInstanceId == right.SkillInstanceId && left.SourceId == right.SourceId;
    private static bool SameAlcoholSlashReplacementReceipt(AlcoholSlashSkillReplacementReceipt left, AlcoholSlashSkillReplacementReceipt right) =>
        left.ProgramFrameId == right.ProgramFrameId && left.InstructionIndex == right.InstructionIndex && left.Source == right.Source &&
        left.GameplayHash == right.GameplayHash && left.DamageWindowId == right.DamageWindowId && left.DamageFrameId == right.DamageFrameId &&
        left.CardUseFrameId == right.CardUseFrameId && left.CardActionId == right.CardActionId && left.TargetSeat == right.TargetSeat &&
        left.DamageAmount == right.DamageAmount && left.Nature == right.Nature && left.AlcoholBonus == right.AlcoholBonus &&
        left.GrantedGrant == right.GrantedGrant && left.SharedInstanceGrant == right.SharedInstanceGrant && left.GrantAdded == right.GrantAdded &&
        left.LostSkillIds.SequenceEqual(right.LostSkillIds) && left.RemovedGrants.SequenceEqual(right.RemovedGrants) && left.RevokedGrants.SequenceEqual(right.RevokedGrants);

    // This is a chronological revocation of an OLD grant, not a forever ban on the skill ID.
    private bool WasGrantPermanentlyRevokedAfterLoss(int ownerSeat, SkillGrant grant, IGameEvent originalLoss)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ReplaceOwnSkillsAfterAlcoholSlashDamage) &&
            !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolvePrepPoisonOutcomeByRound)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var lossIndex = Array.FindIndex(history, e => ReferenceEquals(e, originalLoss));
        if (lossIndex < 0) throw new InvalidOperationException("Permanent grant restoration validation lost its original issued loss fact.");
        return history.Skip(lossIndex + 1).OfType<ProgramAlcoholSlashSkillsReplacedEvent>().Any(e => e.Receipt.Source.OwnerSeat == ownerSeat &&
            e.Receipt.RevokedGrants.Any(revoked => SamePermanentGrantIdentity(grant, revoked))) ||
            history.Skip(lossIndex + 1).OfType<PrepPoisonGrantRevokedEvent>().Any(e => e.OwnerSeat == ownerSeat && SameRoundSkillIdentity(grant, e.Grant));
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 3902)]
    private bool ResumeAlcoholSlashSkillReplacement(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.AlcoholSlashSkillReplacement is null) return false;
        AssertAlcoholSlashSkillReplacement(f);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        AdvanceEventRulesAndQueueFact(new ProgramAlcoholSlashSkillReplacementCompletedEvent(id));
        FinishProgramSkill(f, true);
        return true;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 3902)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 3902)]
    private bool CanContinueAlcoholSlashSkillReplacement(ProgramSkillFrame f) =>
        f.AlcoholSlashSkillReplacement is not null && ValidAlcoholSlashSkillReplacementReceipt(f);

    // SkillsChanged is a typed return to the paid exchange, not an arbitrary
    // lifecycle ancestor. Project this one proven subtree to the original
    // damage candidate without changing ordinary post-damage cursor rules.
    private ProgramSkillFrame? AlcoholSlashReplacementDamageCursorRoot(DamageTriggerWindowFrame window, out long? dyingId)
    {
        dyingId = null;
        var windowIndex = _resolutionStack.FindIndex(frame => frame.Id == window.Id);
        if (windowIndex < 0 || windowIndex + 1 >= _resolutionStack.Count ||
            _resolutionStack[windowIndex + 1] is not ProgramSkillFrame { AlcoholSlashSkillReplacement: { } receipt } root ||
            receipt.DamageWindowId != window.Id || root.WindowContext?.ParentFrameId != window.Id ||
            !DamageProgramMatchesWindow(root, window) || !ValidAlcoholSlashSkillReplacementReceipt(root)) return null;
        var rootIndex = windowIndex + 1;
        if (rootIndex == _resolutionStack.Count - 1) return root;
        if (OrderedPrintedSkillLossObserverRoot(root.Id)?.Id != root.Id) return null;
        if (ActiveDying is { } dying && _resolutionStack.FindIndex(frame => frame.Id == dying.Id) > rootIndex)
            dyingId = dying.Id;
        return root;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 3902)]
    private void AssertAlcoholSlashSkillReplacement(ProgramSkillFrame f)
    {
        if (f.AlcoholSlashSkillReplacement is not null && !ValidAlcoholSlashSkillReplacementReceipt(f))
            throw new InvalidOperationException("Permanent Alcohol Slash replacement lost its exact paid damage, original use or issued grant receipt.");
    }

    [ProgramCapability]
    private sealed class AlcoholSlashSkillReplacementProgramCapability(GameEngine engine) : IAlcoholSlashSkillReplacementProgramHost
    {
        public SkillProgramStepOutcome ReplaceOwnSkillsAfterAlcoholSlashDamage(ProgramSkillFrame frame) => engine.ReplaceOwnSkillsAfterAlcoholSlashDamage(frame);
    }
}
