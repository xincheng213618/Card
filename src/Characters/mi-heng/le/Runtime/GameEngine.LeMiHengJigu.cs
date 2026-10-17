namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string JiguDrawReason = "skill-program.mi-heng.jigu.draw";
    private bool HasLeMiHengJiguCapability => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.InitializeJiguInitialEntities) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveJiguDamageEquipmentDraw);
    private static CardConversionSource JiguSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static bool IsJiguDamageTrigger(SkillProgramTrigger t) =>
        t.Effects is [{ Op: SkillProgramEffectOp.ResolveJiguDamageEquipmentDraw }];
    private bool HasJiguEntityMark(int cardId) => HasLeMiHengJiguCapability &&
        ProgramEventHistory<JiguInitialEntityEvent>().Any(e => e.CardId == cardId);
    private bool HasJiguEntityMark(int ownerSeat, int cardId) =>
        IsValidPlayerSeat(ownerSeat) && _cardZones.GetLocation(cardId) == CardLocation.Hand(ownerSeat) &&
        HasJiguEntityMark(cardId);
    private bool IsJiguHandLimitExempt(CharacterState owner, int cardId) =>
        HasRuntimeSkill(owner, "le:jigu") && HasJiguEntityMark(owner.Seat, cardId);
    private int JiguMarkedHandCount(int owner) => GetHand(_players[owner]).Count(c => HasJiguEntityMark(owner, c.Id));
    private int JiguStartedSeatCount => ProgramEventHistory<ActualDiscardRecoveryPhaseStartedEvent>()
        .Where(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Preparation &&
            e.Phase.ActorSeat == e.Phase.ActualTurnOwnerSeat)
        .Select(e => e.Phase.ActorSeat).Distinct().Count();
    private int JiguEmptySlotCount(CharacterState owner) => Enum.GetValues<EquipmentSlot>()
        .Sum(slot => Math.Max(0, owner.EquipmentSlotCapacity(slot) -
            GetEquipment(owner).Count(c => EquipmentCatalog.Get(c.Kind).Slot == slot)));
    private int JiguUseCount(int owner, int round) => ProgramEventHistory<JiguActivationPaidEvent>()
        .Count(e => e.Source.OwnerSeat == owner && e.Source.SkillId == "le:jigu" && e.RoundNumber == round);
    private bool JiguRoundDisabled(int owner, int round) => ProgramEventHistory<JiguActivationPaidEvent>()
        .Any(e => e.Source.OwnerSeat == owner && e.Source.SkillId == "le:jigu" &&
            e.RoundNumber == round && e.DisabledForRound);

    private ProgramLifecycleTriggerWindowFrame CaptureJiguInitialEntities(ProgramLifecycleTriggerWindowFrame window)
    {
        if (!HasLeMiHengJiguCapability || window.Window != SkillProgramTriggerWindow.GameStarting ||
            window.Continuation != ProgramLifecycleContinuation.CompleteGameStarting) return window;
        if (window.JiguInitialEntities is not null)
            throw new InvalidOperationException("Jigu's actual opening Hand can only be frozen once.");
        var owners = window.Candidates.Where(c => GetProgramTrigger(c).Effects is
            [{ Op: SkillProgramEffectOp.InitializeJiguInitialEntities }]).Select(c => c.OwnerSeat).Distinct().Order();
        var entities = owners.SelectMany(owner => GetHand(_players[owner]).Select(c =>
        {
            var paid = _cardMovements.LastOrDefault(m => m.CardId == c.Id && m.To == CardLocation.Hand(owner));
            if (paid is null || paid.CardKind != c.Kind)
                throw new InvalidOperationException("A Jigu initial entity requires its actual opening Hand entry.");
            return new JiguInitialEntity(owner, c.Id, c.Kind, paid.Sequence);
        })).ToArray();
        return window with { JiguInitialEntities = entities };
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28210,
        SkillProgramEffectOp.InitializeJiguInitialEntities, SkillProgramEffectOp.ResolveJiguDamageEquipmentDraw)]
    private bool CanOfferJiguGate(ProgramTriggerLegalityContext gate) =>
        CanOfferJigu(gate.Candidate, gate.Trigger, gate.Context);

    private bool CanOfferJigu(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger,
        ProgramSkillWindowContext context)
    {
        if (trigger.Effects is [{ Op: SkillProgramEffectOp.InitializeJiguInitialEntities }])
            return context.Window == SkillProgramTriggerWindow.GameStarting &&
                !ProgramEventHistory<JiguInitializedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat);
        if (!IsJiguDamageTrigger(trigger)) return true;
        if (context.Window != SkillProgramTriggerWindow.AfterDamageApplied || context.OwnerSeat != candidate.OwnerSeat ||
            context.OccurrenceIndex != 0 || candidate.OccurrenceIndex != 0 || context.Amount <= 0 ||
            context.DamageFrameId is not { } damageId ||
            context.TargetSeat != candidate.OwnerSeat && context.SourceSeat != candidate.OwnerSeat ||
            !_players[candidate.OwnerSeat].IsAlive ||
            JiguRoundDisabled(candidate.OwnerSeat, _roundNumber) ||
            JiguMarkedHandCount(candidate.OwnerSeat) != GetEquipment(_players[candidate.OwnerSeat]).Count ||
            ProgramEventHistory<JiguActivationPaidEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat && e.DamageFrameId == damageId) ||
            _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is not DamageTriggerWindowFrame w ||
            w.TriggerWindow != context.Window || w.ParentFrameId != damageId ||
            w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count ||
            w.Candidates[w.CandidateIndex].ToProgramCandidate() != candidate ||
            _resolutionStack.SingleOrDefault(x => x.Id == damageId) is not DamageFrame d ||
            d.TargetSeat != context.TargetSeat || d.Amount != context.Amount) return false;
        return GetDamageTriggerAttack(w) is { } attack && d.ParentFrameId == attack.ResolutionId &&
            (attack.IsSourceLess ? context.SourceSeat is null : context.SourceSeat == attack.SourceSeat);
    }

    [ProgramCapability]
    private sealed class LeMiHengJiguCapability(GameEngine engine) : ILeMiHengJiguHost
    {
        public SkillProgramStepOutcome Initialize(ProgramSkillFrame frame) => engine.InitializeJigu(frame);
        public SkillProgramStepOutcome Draw(ProgramSkillFrame frame) => engine.BeginJiguDraw(frame);
    }

    private SkillProgramStepOutcome InitializeJigu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != "le:jigu" || f.TriggerId != "mark-initial-entities" || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !ExactOriginalHandLifecycleParent(f, SkillProgramTriggerWindow.GameStarting) ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            ProgramEventHistory<JiguInitializedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat))
            throw new InvalidOperationException("Jigu initialization requires its original game-start binding exactly once.");
        // The native game-start mother freezes the final opening Hand before
        // any game-start skill can move it, including real opening replacements.
        var parent = (ProgramLifecycleTriggerWindowFrame)_resolutionStack.Single(x => x.Id == f.WindowContext!.ParentFrameId);
        var entities = parent.JiguInitialEntities?.Where(e => e.OwnerSeat == f.OwnerSeat).ToArray() ??
            throw new InvalidOperationException("Jigu requires the native mother's frozen initial entities.");
        if (entities.Select(e => e.CardId).Distinct().Count() != entities.Length || entities.Any(e =>
            !_cardMovements.Any(m => m.Sequence == e.DealSequence && m.CardId == e.CardId &&
                m.CardKind == e.PrintedKind && m.To == CardLocation.Hand(e.OwnerSeat))))
            throw new InvalidOperationException("Jigu cannot mark an entity without its exact opening-Hand provenance.");
        AdvanceEventRulesAndQueueFact(new JiguInitializedEvent(f.Id, JiguSource(f), f.GameplayHash, entities.Length));
        foreach (var e in entities)
            AdvanceEventRulesAndQueueFact(new JiguInitialEntityEvent(f.Id, f.OwnerSeat, e.CardId, e.PrintedKind, e.DealSequence));
        return SkillProgramStepOutcome.Continue;
    }

    private SkillProgramStepOutcome BeginJiguDraw(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Jigu is not null || f.SkillId != "le:jigu" || f.TriggerId != "damage-equipment-draw" ||
            f.InstructionIndex != 1 || f.WindowContext is not { } c || f.SelectedCardIds.Count != 0 ||
            f.SelectedTargetSeats.Count != 0 || !CanOfferJigu(new(f.OwnerSeat, f.SkillId, f.TriggerId,
                f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), GetProgramTrigger(f), c))
            throw new InvalidOperationException("Jigu requires one unspent native source-or-target damage occurrence.");
        var threshold = JiguStartedSeatCount;
        var use = JiguUseCount(f.OwnerSeat, _roundNumber) + 1;
        var r = new JiguReceipt(JiguSource(f), f.GameplayHash, c.DamageFrameId!.Value, c.ParentFrameId,
            _roundNumber, _turnNumber, _turnProgression.OwnerSeat, JiguMarkedHandCount(f.OwnerSeat),
            GetEquipment(_players[f.OwnerSeat]).Count, JiguEmptySlotCount(_players[f.OwnerSeat]), threshold,
            use, use >= threshold, JiguStage.Drawing) { Paid = true };
        ReplaceRuntimeTop(f = f with { Jigu = r });
        AdvanceEventRulesAndQueueFact(JiguPayment(f.Id, r));
        AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 发动【激鼓】，摸 {r.EmptySlotCount} 张牌。", f.OwnerSeat);
        ContinueJigu(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private static JiguActivationPaidEvent JiguPayment(long id, JiguReceipt r) => new(id, r.Source,
        r.GameplayHash, r.DamageFrameId, r.DamageWindowId, r.RoundNumber, r.ActualTurn, r.TurnOwner,
        r.MarkedHandCount, r.EquipmentCount, r.EmptySlotCount, r.StartedSeatCount, r.UseNumber, r.DisabledForRound);

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveJiguDamageEquipmentDraw)]
    private void ReturnJiguDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.Jigu is not { Paid: true, Stage: JiguStage.Drawing } r ||
            outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.EmptySlotCount ||
            ProgramEventHistory<JiguDrawReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Jigu cannot repeat or replace its paid native draw request.");
        ReplaceRuntimeTop(f = f with { Jigu = r with { Stage = JiguStage.DrawChildren, DrawOutcome = outcome },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new JiguDrawReturnedEvent(f.Id, outcome.ActualDrawIds.Count,
            outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueJigu(f.Id);
    }

    private bool DrainJigu(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private void ContinueJigu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Jigu: { } r } f || f.Id != id) return;
        AssertJigu(f);
        if (r.Stage == JiguStage.Drawing)
        {
            if (f.NativeDraw is null) BeginOperationProgramDraw(f, f.OwnerSeat, r.EmptySlotCount, new(JiguDrawReason));
            return;
        }
        if (DrainJigu(f)) return;
        f = GetActiveProgramFrame(id);
        AdvanceEventRulesAndQueueFact(new JiguCompletedEvent(f.Id, r.DamageFrameId));
        ReplaceRuntimeTop(f = f with { Jigu = null, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 28211)]
    private bool ResumeJigu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Jigu: not null } f || f.Id != id) return false;
        ContinueJigu(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28211)]
    private bool ReturnJiguMovement(ProgramSkillFrame f)
    {
        if (f.Jigu is null) return false;
        ContinueJigu(f.Id); return true;
    }
}
