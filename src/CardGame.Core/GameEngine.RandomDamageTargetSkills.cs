namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 1242)]
    private bool CanOfferRandomDamageTargetSkill(SkillProgramTrigger trigger, ProgramSkillWindowContext context, int ownerSeat)
    {
        if (!AcquireRandomDamageTargetSkillDescriptor.IsTrigger(trigger)) return true;
        if (context is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0,
                SourceSeat: { } source, TargetSeat: { } target } || source != ownerSeat || target == ownerSeat ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window)
            return false;
        return TryGetRandomSkillDirectSlashUse(GetDamageTriggerAttack(window), out _);
    }

    private bool TryGetRandomSkillDirectSlashUse(IDamageAttempt attack, out CardUseFrame use)
    {
        use = null!;
        if (attack.IsSourceLess || attack.IsChainPropagation || !attack.DamageWasApplied || attack.DamageAmount <= 0 ||
            attack.SourceSeat == attack.TargetSeat || attack.SourceSeat != attack.CardUserSeat ||
            attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) ||
            LifecycleCardUse(attack.ResolutionId) is not { Action: { Type: CardActionType.Use } action } original ||
            original.SourceSeat != attack.SourceSeat || action.ActorSeat != attack.SourceSeat ||
            original.CardKind != kind || action.EffectiveKind != kind || !original.TargetSeats.Contains(attack.TargetSeat))
            return false;
        use = original;
        return true;
    }

    private HashSet<string> OwnedAndRestorableRandomSkillIds(CharacterState owner)
    {
        var ids = owner.SkillGrants.Grants.Select(g => g.SkillId).ToHashSet(StringComparer.Ordinal);
        // A temporarily removed grant is still owned; a later restoration must not create a duplicate instance.
        foreach (var lease in _orderedPrintedSkillLossLeases.Where(l => l.Source.OwnerSeat == owner.Seat))
        foreach (var grant in lease.RemovedGrants)
        {
            var general = lease.TemplateSourceId == CharacterState.SecondarySkillSource ? owner.SecondaryGeneral : owner.General;
            var loss = ProgramEventHistory<OrderedPrintedSkillGrantRemovedEvent>().Single(e => e.FrameId == lease.ProgramFrameId && e.Grant == grant);
            if (general?.Id == lease.GeneralId && !WasGrantPermanentlyRevokedAfterLoss(owner.Seat, grant, loss) &&
                !RandomDamageTargetSkillGrantExpired(grant))
                ids.Add(grant.SkillId);
        }
        foreach (var loss in HellSoulOutstandingLosses(owner.Seat))
            if (owner.General.Id == loss.GeneralId && owner.SecondaryGeneral?.Id == loss.SecondaryGeneralId &&
                !WasGrantPermanentlyRevokedAfterLoss(owner.Seat, loss.Grant, loss) && loss.Grant.PhaseExpiry is null &&
                !RandomDamageTargetSkillGrantExpired(loss.Grant))
                ids.Add(loss.Grant.SkillId);
        return ids;
    }

    private bool RandomDamageTargetSkillGrantExpired(SkillGrant grant) => HasNextActualTurnStartSkillGrantExpired(grant) ||
        grant.TurnExpiry is { } turn && ProgramEventHistory<TurnEndedEvent>().Any(e =>
            e.TurnNumber == turn.TurnNumber && e.ActorSeat == turn.TurnOwnerSeat);

    private SkillProgramStepOutcome AcquireRandomDamageTargetSkill(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (_resolutionStack.LastOrDefault()?.Id != f.Id || f.RandomDamageTargetSkill is not null || f.InstructionIndex != 1 ||
            !AcquireRandomDamageTargetSkillDescriptor.IsTrigger(GetProgramTrigger(f)) ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out var attack) ||
            attack.SourceSeat != f.OwnerSeat || !TryGetRandomSkillDirectSlashUse(attack, out var use))
            throw new InvalidOperationException("Random skill acquisition requires its exact already-paid direct native Slash damage candidate.");
        var owner = _players[f.OwnerSeat];
        if (!owner.IsAlive || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var owned = OwnedAndRestorableRandomSkillIds(owner);
        var candidates = EnabledContentSkillIds(_players[damage.TargetSeat])
            .Where(id => !owned.Contains(id) && !_contentRegistry.GetSkill(id).Tags.HasFlag(SkillTag.Locked))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return SkillProgramStepOutcome.Continue;
        var before = _random.State;
        var selected = candidates[_random.Next(candidates.Length)];
        var sourceId = RandomDamageTargetSkillGrantSource(f);
        var grantId = sourceId + ":" + selected;
        var receipt = new ProgramRandomDamageTargetSkillReceipt
        {
            ProgramFrameId = f.Id, InstructionIndex = f.InstructionIndex,
            Source = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), GameplayHash = f.GameplayHash,
            DamageWindowId = window.Id, DamageFrameId = damage.Id, CardUseFrameId = use.Id, CardActionId = use.Action!.ActionId,
            TargetSeat = damage.TargetSeat, DamageAmount = damage.Amount, Nature = damage.Nature,
            Candidates = candidates, SelectedSkillId = selected, RandomBefore = before, RandomAfter = _random.State,
            GrantedGrant = new SkillGrant(grantId, selected, grantId, sourceId)
        };
        ReplaceRuntimeTop(f = f with { RandomDamageTargetSkill = receipt });
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        if (!owner.SkillGrants.Grant(receipt.GrantedGrant))
            throw new InvalidOperationException("Random damage-target skill acquisition cannot issue the same grant twice.");
        AdvanceEventRulesAndQueueFact(new ProgramRandomDamageTargetSkillAcquiredEvent(receipt));
        RegisterTaggedConversionSkill(owner, selected);
        AdvanceEventRulesAndQueueFact(new SkillsAcquiredEvent(owner.Seat, f.SkillId, Array.AsReadOnly(new[] { selected })));
        AdvanceRuntimeProgram(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private static string RandomDamageTargetSkillGrantSource(ProgramSkillFrame f) =>
        $"acquired:damage-target-skill:{f.OwnerSeat}:{f.SkillId}:{f.SkillInstanceId}:{f.Id}";

    private bool ValidRandomDamageTargetSkillReceipt(ProgramSkillFrame f)
    {
        if (f.RandomDamageTargetSkill is not { } r || r.ProgramFrameId != f.Id || r.InstructionIndex != 1 || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.PendingMovementContinuation is not null ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash ||
            !AcquireRandomDamageTargetSkillDescriptor.IsTrigger(GetProgramTrigger(f)) ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out var attack) ||
            attack.SourceSeat != f.OwnerSeat || !TryGetRandomSkillDirectSlashUse(attack, out var use) ||
            r.DamageWindowId != window.Id || r.DamageFrameId != damage.Id || r.CardUseFrameId != use.Id || r.CardActionId != use.Action!.ActionId ||
            r.TargetSeat != damage.TargetSeat || r.DamageAmount != damage.Amount || r.Nature != damage.Nature ||
            r.Candidates.Count == 0 || !r.Candidates.SequenceEqual(r.Candidates.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) ||
            r.Candidates.Any(id => !_contentRegistry.Skills.TryGetValue(id, out var skill) || skill.Tags.HasFlag(SkillTag.Locked)))
            return false;
        var replay = new DeterministicRandom(unchecked((int)r.RandomBefore));
        var selected = r.Candidates[replay.Next(r.Candidates.Count)];
        var source = RandomDamageTargetSkillGrantSource(f);
        var grantId = source + ":" + selected;
        return r.RandomBefore != 0 && r.SelectedSkillId == selected && r.RandomAfter == replay.State &&
            r.GrantedGrant == new SkillGrant(grantId, selected, grantId, source) &&
            ProgramEventHistory<ProgramRandomDamageTargetSkillAcquiredEvent>().Where(e => e.Receipt.ProgramFrameId == f.Id).ToArray() is [var issued] &&
            SameRandomDamageTargetSkillReceipt(r, issued.Receipt) &&
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.SkillId == f.SkillId && e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId &&
                e.Window == SkillProgramTriggerWindow.AfterDamageApplied) == 1;
    }

    private static bool SameRandomDamageTargetSkillReceipt(ProgramRandomDamageTargetSkillReceipt a, ProgramRandomDamageTargetSkillReceipt b) =>
        a.ProgramFrameId == b.ProgramFrameId && a.InstructionIndex == b.InstructionIndex && a.Source == b.Source &&
        a.GameplayHash == b.GameplayHash && a.DamageWindowId == b.DamageWindowId && a.DamageFrameId == b.DamageFrameId &&
        a.CardUseFrameId == b.CardUseFrameId && a.CardActionId == b.CardActionId && a.TargetSeat == b.TargetSeat &&
        a.DamageAmount == b.DamageAmount && a.Nature == b.Nature && a.SelectedSkillId == b.SelectedSkillId &&
        a.RandomBefore == b.RandomBefore && a.RandomAfter == b.RandomAfter && a.GrantedGrant == b.GrantedGrant &&
        a.Candidates.SequenceEqual(b.Candidates);

    [ProgramContinuation(ProgramContinuationKind.Resume, 1242)]
    private bool ResumeRandomDamageTargetSkill(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.RandomDamageTargetSkill is null) return false;
        AssertRandomDamageTargetSkill(f);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        AdvanceEventRulesAndQueueFact(new ProgramRandomDamageTargetSkillCompletedEvent(id));
        FinishProgramSkill(f, true);
        return true;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 1242)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 1242)]
    private bool CanContinueRandomDamageTargetSkill(ProgramSkillFrame f) =>
        f.RandomDamageTargetSkill is not null && ValidRandomDamageTargetSkillReceipt(f);

    private ProgramSkillFrame? RandomDamageTargetSkillDamageCursorRoot(DamageTriggerWindowFrame window, out long? dyingId)
    {
        dyingId = null;
        var index = _resolutionStack.FindIndex(frame => frame.Id == window.Id);
        if (index < 0 || index + 1 >= _resolutionStack.Count ||
            _resolutionStack[index + 1] is not ProgramSkillFrame { RandomDamageTargetSkill: { } receipt } root ||
            receipt.DamageWindowId != window.Id || !DamageProgramMatchesWindow(root, window) || !ValidRandomDamageTargetSkillReceipt(root)) return null;
        if (index + 1 == _resolutionStack.Count - 1) return root;
        if (OrderedPrintedSkillLossObserverRoot(root.Id)?.Id != root.Id) return null;
        if (ActiveDying is { } dying && _resolutionStack.FindIndex(frame => frame.Id == dying.Id) > index + 1) dyingId = dying.Id;
        return root;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 1242)]
    private void AssertRandomDamageTargetSkill(ProgramSkillFrame f)
    {
        if (f.RandomDamageTargetSkill is not null && !ValidRandomDamageTargetSkillReceipt(f))
            throw new InvalidOperationException("Random damage-target skill acquisition lost its exact native damage, frozen random choice or issued grant.");
    }

    [ProgramCapability]
    private sealed class RandomDamageTargetSkillCapability(GameEngine engine) : IRandomDamageTargetSkillHost
    {
        public SkillProgramStepOutcome AcquireRandomDamageTargetSkill(ProgramSkillFrame frame) => engine.AcquireRandomDamageTargetSkill(frame);
    }
}
