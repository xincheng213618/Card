using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static string LastDamageSourceReason(ProgramSkillFrame f, bool draw) =>
        $"skill-program.{f.SkillId}.last-damage-source.{(draw ? "draw" : "discard")}";
    private long LastDamageSourceMovementSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static bool IsLastDamageSourceTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.LastDamageSourceReciprocity }];
    private static LastDamageSourceReciprocityDirection LastDamageSourceDirection(SkillProgramTrigger trigger) =>
        trigger.Subject == SkillProgramTriggerSubject.DamageSource
            ? LastDamageSourceReciprocityDirection.DrawOwner : LastDamageSourceReciprocityDirection.DiscardSource;

    private int? RecordedLastDamageSource(int owner, string skillId, string stateId) =>
        CompleteProgramEventHistory().OfType<LastDamageSourceRecordedEvent>().LastOrDefault(e =>
            e.OwnerSeat == owner && e.SkillId == skillId && e.StateId == stateId)?.SourceSeat;

    private void ObserveLastDamageSourceReciprocity(IGameEvent payload)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.LastDamageSourceReciprocity) ||
            payload is not DamageAppliedEvent { Amount: > 0, SourceLess: false } applied ||
            applied.SourceSeat == applied.TargetSeat || !IsValidPlayerSeat(applied.SourceSeat) || !IsValidPlayerSeat(applied.TargetSeat) ||
            _resolutionStack.LastOrDefault() is not DamageFrame damage || CurrentDamageAttempt is not { } attack ||
            !attack.DamageWasApplied || attack.IsSourceLess || damage.ParentFrameId != attack.ResolutionId ||
            damage.SourceSeat != applied.SourceSeat || damage.TargetSeat != applied.TargetSeat || damage.Amount != applied.Amount ||
            damage.Nature != applied.Nature || attack.SourceSeat != applied.SourceSeat || attack.TargetSeat != applied.TargetSeat ||
            attack.DamageAmount != applied.Amount || !CompleteProgramEventHistory().OfType<DamageRequestedEvent>().Any(e =>
                e.ResolutionId == damage.Id && e.SourceSeat == applied.SourceSeat && e.TargetSeat == applied.TargetSeat &&
                e.Amount == applied.Amount && e.Nature == applied.Nature && !e.SourceLess && e.SourceCard == attack.EffectiveCardKind)) return;

        foreach (var ownerSeat in new[] { applied.TargetSeat, applied.SourceSeat })
        {
            var owner = _players[ownerSeat];
            if (!owner.IsAlive) continue;
            var bindings = GetSkillBindingShard(owner).ProgramInstances.SelectMany(instance => instance.Program.Triggers
                .Where(IsLastDamageSourceTrigger).Select(trigger => (Instance: instance, Trigger: trigger)))
                .GroupBy(x => (x.Instance.SkillId, StateId: x.Trigger.Effects[0].StateId!)).ToArray();
            foreach (var group in bindings)
            {
                var previous = RecordedLastDamageSource(ownerSeat, group.Key.SkillId, group.Key.StateId);
                var incoming = ownerSeat == applied.TargetSeat;
                if (incoming && !CompleteProgramEventHistory().OfType<LastDamageSourceRecordedEvent>().Any(e =>
                        e.OwnerSeat == ownerSeat && e.SkillId == group.Key.SkillId && e.StateId == group.Key.StateId && e.DamageFrameId == damage.Id))
                    AdvanceEventRulesAndQueueFact(new LastDamageSourceRecordedEvent(ownerSeat, group.Key.SkillId, group.Key.StateId,
                        applied.SourceSeat, previous, damage.Id, damage.ParentFrameId, applied.Amount, applied.Nature, _turnNumber, _currentSeat));
                if (!incoming && previous != applied.TargetSeat) continue;
                var subject = incoming ? SkillProgramTriggerSubject.Owner : SkillProgramTriggerSubject.DamageSource;
                var binding = group.Where(x => x.Trigger.Subject == subject)
                    .OrderBy(x => x.Instance.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
                if (binding.Instance is null) continue;
                var direction = LastDamageSourceDirection(binding.Trigger);
                if (CompleteProgramEventHistory().OfType<LastDamageSourceReciprocityEligibleEvent>().Any(e =>
                        e.OwnerSeat == ownerSeat && e.SkillId == group.Key.SkillId && e.StateId == group.Key.StateId &&
                        e.DamageFrameId == damage.Id && e.Direction == direction)) continue;
                AdvanceEventRulesAndQueueFact(new LastDamageSourceReciprocityEligibleEvent(ownerSeat, group.Key.SkillId,
                    group.Key.StateId, binding.Instance.SkillInstanceId, binding.Instance.Program.GameplayHash, binding.Trigger.Id,
                    direction, damage.Id, damage.ParentFrameId, applied.SourceSeat, applied.TargetSeat, applied.Amount, applied.Nature,
                    incoming ? applied.SourceSeat : previous!.Value));
            }
        }
    }

    private LastDamageSourceReciprocityEligibleEvent? LastDamageSourceEligibility(int owner, string skillId,
        SkillProgramTrigger trigger, long damageId) => CompleteProgramEventHistory().OfType<LastDamageSourceReciprocityEligibleEvent>()
        .SingleOrDefault(e => e.OwnerSeat == owner && e.SkillId == skillId && e.StateId == trigger.Effects[0].StateId &&
            e.BindingId == trigger.Id && e.DamageFrameId == damageId && e.Direction == LastDamageSourceDirection(trigger));
    private bool LastDamageSourceAlreadyIssued(LastDamageSourceReciprocityEligibleEvent e) =>
        CompleteProgramEventHistory().OfType<LastDamageSourceReciprocityStartedEvent>().Any(s => s.Source.OwnerSeat == e.OwnerSeat &&
            s.Source.SkillId == e.SkillId && s.StateId == e.StateId && s.Direction == e.Direction && s.DamageFrameId == e.DamageFrameId);

    private bool CanOfferLastDamageSourceReciprocity(int ownerSeat, string skillId, SkillProgramTrigger trigger, IDamageAttempt attack)
    {
        if (!IsLastDamageSourceTrigger(trigger)) return true;
        if (attack.IsSourceLess || !attack.DamageWasApplied || attack.DamageAmount <= 0 || attack.SourceSeat == attack.TargetSeat ||
            _resolutionStack.LastOrDefault() is not DamageFrame damage || damage.ParentFrameId != attack.ResolutionId) return false;
        return CanOfferLastDamageSourceEligibility(LastDamageSourceEligibility(ownerSeat, skillId, trigger, damage.Id));
    }
    private bool CanOfferLastDamageSourceReciprocity(int ownerSeat, string skillId, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!IsLastDamageSourceTrigger(trigger)) return true;
        if (context is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, DamageFrameId: { } damageId,
                SourceSeat: { } source, TargetSeat: { } target } || source == target || context.OwnerSeat != ownerSeat) return false;
        var e = LastDamageSourceEligibility(ownerSeat, skillId, trigger, damageId);
        return e is not null && e.SourceSeat == source && e.TargetSeat == target && e.Amount == context.Amount && CanOfferLastDamageSourceEligibility(e);
    }
    private bool CanOfferLastDamageSourceEligibility(LastDamageSourceReciprocityEligibleEvent? e) => e is not null &&
        !LastDamageSourceAlreadyIssued(e) && IsValidPlayerSeat(e.OwnerSeat) && _players[e.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[e.OwnerSeat], e.SkillId, e.SkillInstanceId) &&
        (e.Direction == LastDamageSourceReciprocityDirection.DrawOwner ||
            _players[e.SourceSeat].IsAlive && LastDamageSourceLegalMaterials(e.SourceSeat).Length > 0);

    private LastDamageSourceReciprocityMaterial[] LastDamageSourceLegalMaterials(int seat) =>
        new[] { CardLocation.Hand(seat), CardLocation.Equipment(seat) }.SelectMany(from => _cardZones.CardsAt(from)
            .Where(card => !IsForeignEquipmentDiscardPrevented(seat, card, from, OwnedCardMoveIntent.Discard))
            .Select(card => new LastDamageSourceReciprocityMaterial(card.Id, card.Kind, from, card.IsGeneralWeapon))).ToArray();

    private SkillProgramStepOutcome BeginLastDamageSourceReciprocity(ProgramSkillFrame supplied, string stateId)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        var trigger = plan.Trigger;
        if (trigger is null || !IsLastDamageSourceTrigger(trigger) || f.InstructionIndex != 1 || f.LastDamageSourceReciprocity is not null ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || trigger.Effects[0].StateId != stateId ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, DamageFrameId: { } damageId } context ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not DamageTriggerWindowFrame window || window.Id != context.ParentFrameId ||
            window.ParentFrameId != damageId || window.TriggerWindow != context.Window || window.CandidateIndex < 0 ||
            window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].ToProgramCandidate()))
            throw new InvalidOperationException("Last-source reciprocity lost its exact applied-damage candidate or first instruction.");
        var e = LastDamageSourceEligibility(f.OwnerSeat, f.SkillId, trigger, damageId);
        if (e is null || e.SkillInstanceId != f.SkillInstanceId || !CanOfferLastDamageSourceReciprocity(f.OwnerSeat, f.SkillId, trigger, context))
            return SkillProgramStepOutcome.Continue;
        var attack = GetDamageTriggerAttack(window);
        if (attack.ResolutionId != e.AttackFrameId || attack.IsSourceLess || !attack.DamageWasApplied ||
            attack.SourceSeat != e.SourceSeat || attack.TargetSeat != e.TargetSeat || attack.DamageAmount != e.Amount || GetDamageNature(attack) != e.Nature)
            throw new InvalidOperationException("Last-source reciprocity lost its real positive damage producer.");
        var participant = e.Direction == LastDamageSourceReciprocityDirection.DrawOwner ? f.OwnerSeat : e.SourceSeat;
        var r = new ProgramLastDamageSourceReceipt { InstructionIndex = f.InstructionIndex,
            Source = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), GameplayHash = f.GameplayHash,
            StateId = stateId, Direction = e.Direction, DamageWindowId = window.Id, DamageFrameId = damageId,
            AttackFrameId = e.AttackFrameId, SourceSeat = e.SourceSeat, TargetSeat = e.TargetSeat, Amount = e.Amount, Nature = e.Nature,
            ParticipantSeat = participant, RecordedSourceSeat = e.RecordedSourceSeat,
            Stage = e.Direction == LastDamageSourceReciprocityDirection.DrawOwner ? LastDamageSourceReciprocityStage.MovementChildren :
                LastDamageSourceReciprocityStage.ChoosingDiscard,
            EligibleMaterials = e.Direction == LastDamageSourceReciprocityDirection.DiscardSource ? LastDamageSourceLegalMaterials(participant) : [] };
        ReplaceRuntimeTop(f = f with { LastDamageSourceReciprocity = r });
        AdvanceEventRulesAndQueueFact(new LastDamageSourceReciprocityStartedEvent(f.Id, r.Source, r.GameplayHash, stateId, r.Direction,
            r.DamageWindowId, r.DamageFrameId, r.AttackFrameId, r.SourceSeat, r.TargetSeat, r.Amount, r.Nature, participant, r.RecordedSourceSeat));
        if (r.Direction == LastDamageSourceReciprocityDirection.DiscardSource)
        { PublishLastDamageSourceDiscard(f); return SkillProgramStepOutcome.AwaitChoice; }
        var before = LastDamageSourceMovementSequence;
        ReplaceRuntimeTop(f = f with { LastDamageSourceReciprocity = r with { MovementIssued = true,
            SequenceBefore = before, SequenceAfter = before }, PendingMovementContinuation = new(participant, 0, null) });
        var actual = DrawCards(_players[participant], 1, true, new(LastDamageSourceReason(f, true))).Count;
        var current = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(current with { LastDamageSourceReciprocity = current.LastDamageSourceReciprocity! with {
            ActualCount = actual, SequenceAfter = LastDamageSourceMovementSequence } });
        QueueLastDamageSourceMovementIssued(GetActiveProgramFrame(f.Id));
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private IReadOnlyList<PromptChoice> LastDamageSourceDiscardChoices(ProgramSkillFrame f) =>
        Array.AsReadOnly(f.LastDamageSourceReciprocity!.EligibleMaterials.Select(m => new PromptChoice(
            new($"last-damage-source.{f.Id}.{m.CardId}"), $"弃置【{GetAdvancedCard(m.CardId).DisplayName}】", [m.CardId], [],
            new Dictionary<string, string> { ["program-action"] = "last-damage-source-discard",
                ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
    private void PublishLastDamageSourceDiscard(ProgramSkillFrame f) => PublishParticipantHandChoice(f,
        f.LastDamageSourceReciprocity!.ParticipantSeat, LastDamageSourceDiscardChoices(f),
        "选择并弃置一张自己的手牌或装备牌。", f.LastDamageSourceReciprocity.ParticipantSeat);

    private void ResolveLastDamageSourceReciprocityChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Last-source discard lost its owning program.");
        AssertLastDamageSourceReciprocity(f); var r = f.LastDamageSourceReciprocity!;
        if (r.Stage != LastDamageSourceReciprocityStage.ChoosingDiscard || r.MovementIssued ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != r.ParticipantSeat ||
            choice.Parameters.GetValueOrDefault("program-action") != "last-damage-source-discard" ||
            choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) ||
            choice.Cards is not [var id] || choice.Targets.Count != 0 || !LastDamageSourceDiscardChoices(f).Any(c =>
                c.Id == choice.Id && c.Cards.SequenceEqual(choice.Cards) && c.Targets.SequenceEqual(choice.Targets) &&
                c.Parameters.OrderBy(x => x.Key).SequenceEqual(choice.Parameters.OrderBy(x => x.Key))))
            throw new InvalidOperationException("Last-source discard changed its private chooser, frozen entity or owning frame.");
        ClearPendingDecision();
        if (!_players[f.OwnerSeat].IsAlive || !_players[r.ParticipantSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { CancelProgramBindingAndCleanup(f, "伤害后弃牌在付款前失去原技能资格或实际参与者。"); return; }
        var material = r.EligibleMaterials.Single(m => m.CardId == id);
        if (!LastDamageSourceLegalMaterials(r.ParticipantSeat).Contains(material))
            throw new InvalidOperationException("Last-source discard must pay the exact still-owned eligible entity.");
        var before = LastDamageSourceMovementSequence;
        ReplaceRuntimeTop(f = f with { LastDamageSourceReciprocity = r with { Stage = LastDamageSourceReciprocityStage.MovementChildren,
            MovementIssued = true, SequenceBefore = before, SequenceAfter = before, PaidMaterial = material },
            PendingMovementContinuation = new(r.ParticipantSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([id], CardLocation.DiscardPile, new(LastDamageSourceReason(f, false)), (batch, records) =>
        {
            if (records is not [var paid] || paid.CardId != material.CardId || paid.CardKind != material.PrintedKind ||
                paid.From != material.From || paid.To != (material.IsGeneralWeapon && material.From.Zone == CardZoneKind.Equipment
                    ? CardLocation.OutsideGame : CardLocation.DiscardPile))
                throw new InvalidOperationException("Last-source discard did not produce its single actual payment.");
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { LastDamageSourceReciprocity = current.LastDamageSourceReciprocity! with {
                ActualCount = 1, SequenceAfter = paid.Sequence, BatchId = batch } });
            QueueLastDamageSourceMovementIssued(GetActiveProgramFrame(f.Id));
        });
        AdvanceRuntimeProgram(f.Id);
    }

    private void QueueLastDamageSourceMovementIssued(ProgramSkillFrame f)
    {
        var r = f.LastDamageSourceReciprocity!;
        AdvanceEventRulesAndQueueFact(new LastDamageSourceReciprocityMovementIssuedEvent(f.Id, r.ParticipantSeat, r.Direction,
            r.ActualCount, r.SequenceBefore, r.SequenceAfter, r.BatchId));
    }
    private bool ResumeLastDamageSourceReciprocity(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != frameId || f.LastDamageSourceReciprocity is not { } r) return false;
        AssertLastDamageSourceReciprocity(f);
        if (r.Stage == LastDamageSourceReciprocityStage.ChoosingDiscard)
        { if (_pendingDecision is null) PublishLastDamageSourceDiscard(f); return true; }
        if (r.Stage == LastDamageSourceReciprocityStage.MovementChildren)
        {
            if (TryBeginQueuedRecoveryReplacement(frameId, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginHpChangedProgramWindow(frameId, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(frameId)) return true;
            ReplaceRuntimeTop(f = f with { LastDamageSourceReciprocity = r with { Stage = LastDamageSourceReciprocityStage.Complete },
                PendingMovementContinuation = null });
            AdvanceEventRulesAndQueueFact(new LastDamageSourceReciprocityCompletedEvent(f.Id, r.ActualCount));
        }
        FinishProgramSkill(f, true); return true;
    }
    private bool ReturnLastDamageSourceReciprocityMovement(ProgramSkillFrame f)
    {
        if (f.LastDamageSourceReciprocity is not { MovementIssued: true } || f.PendingMovementContinuation is null) return false;
        AssertLastDamageSourceReciprocity(f); AdvanceRuntimeProgram(f.Id); return true;
    }
    private bool IsLastDamageSourceReciprocityMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.LastDamageSourceReciprocity is { } receipt && effect is { Op: SkillProgramEffectOp.LastDamageSourceReciprocity } && effect.StateId == receipt.StateId &&
        f.PendingMovementContinuation == pending && pending.SubjectSeat == receipt.ParticipantSeat && ValidLastDamageSourceReceipt(f);
    private bool CanContinueLastDamageSourceReciprocity(ProgramSkillFrame f) =>
        f.LastDamageSourceReciprocity is { MovementIssued: true } && ValidLastDamageSourceReceipt(f);
    private PromptChoice SelectAiLastDamageSourceReciprocity(PendingDecision decision, ProgramSkillFrame f) => decision.Choices
        .OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.LastDamageSourceReciprocity!.ParticipantSeat]))
        .ThenBy(c => c.Cards.Single()).First();

    private sealed partial class ProgramSkillHost : ILastDamageSourceReciprocityProgramHost
    {
        public SkillProgramStepOutcome LastDamageSourceReciprocity(ProgramSkillFrame f, string stateId) => engine.BeginLastDamageSourceReciprocity(f, stateId);
        public bool CanContinueLastDamageSourceReciprocity(ProgramSkillFrame f) => engine.CanContinueLastDamageSourceReciprocity(f);
    }
}
