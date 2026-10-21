namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuZishengSkill = "wumiao:zisheng";
    private const string WuZishengMovementBinding = global::CardGame.Core.RuntimeSkills.RuntimeRules_zhang_fei_wumiao_WuZhangFeiMovement.WuZishengMovementBinding;
    private bool HasWuZishengMovementCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWuZhangFeiMovementDamage);
    private static bool IsWuZishengMovementTrigger(SkillProgramTrigger t) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhang_fei_wumiao_WuZhangFeiMovement.IsWuZishengMovementTrigger(t);
    private WuZishengMovementSource[] CaptureWuZishengMovementSources() => !HasWuZishengMovementCapability || !_setupComplete ? [] :
        _players.Where(p => p.IsAlive).SelectMany(p => GetSkillBindingShard(p).ProgramInstances
            .Where(i => i.SkillId == WuZishengSkill && i.Program.Triggers.Any(IsWuZishengMovementTrigger))
            .Select(i => new WuZishengMovementSource(new(i.SkillId, WuZishengMovementBinding, p.Seat, i.SkillInstanceId), i.Program.GameplayHash)))
            .OrderBy(s => s.Source.OwnerSeat).ThenBy(s => s.Source.SkillInstanceId, StringComparer.Ordinal).ToArray();
    private bool ValidWuZishengMovementSource(WuZishengMovementSource s) => s.Source.SkillId == WuZishengSkill &&
        s.Source.BindingId == WuZishengMovementBinding && IsValidPlayerSeat(s.Source.OwnerSeat) &&
        _contentRegistry.GetSkill(WuZishengSkill).Program is { } p && p.GameplayHash == s.GameplayHash && p.Triggers.Any(IsWuZishengMovementTrigger);

    // These facts qualify the existing native invoice. They are not a second
    // discard operation or an executable pending object.
    private NativeLogicalDiscardInvocationDraft? AdmitWuZishengNativeDiscard(NativeLogicalDiscardInvocationDraft? draft)
    {
        if (draft is null || !HasWuZishengMovementCapability) return draft;
        foreach (var source in CaptureWuZishengMovementSources().Where(s => s.Source.OwnerSeat == draft.ExecutorSeat))
            AdvanceEventRulesAndQueueFact(new WuZishengDiscardAdmittedEvent(draft.InvocationId, draft.ParentFrameId,
                draft.TurnNumber, draft.InitiatorSeat, draft.ExecutorSeat, draft.SequenceBefore, draft.Reason, source));
        AdmitWuZishengDiscardMaterials(draft);
        return draft;
    }
    private void AdmitWuZishengDiscardMaterials(NativeLogicalDiscardInvocationDraft draft)
    {
        if (!HasWuZishengMovementCapability || !ProgramEventHistory<WuZishengDiscardAdmittedEvent>().Any(e => e.InvocationId == draft.InvocationId)) return;
        for (var i = 0; i < draft.Materials.Count; i++)
        {
            var m = draft.Materials[i];
            var existing = ProgramEventHistory<WuZishengDiscardMaterialAdmittedEvent>().Where(e => e.InvocationId == draft.InvocationId && e.Index == i).ToArray();
            if (existing is [var prior])
            { if (prior.CardId != m.CardId || prior.From != m.From) throw new InvalidOperationException("Zisheng's native primary material changed."); continue; }
            if (existing.Length != 0) throw new InvalidOperationException("Zisheng cannot qualify one primary material twice.");
            var producer = _resolutionStack.LastOrDefault();
            AdvanceEventRulesAndQueueFact(new WuZishengDiscardMaterialAdmittedEvent(draft.InvocationId, i, m.CardId,
                GetAdvancedCard(m.CardId).Kind, m.From, producer?.Id, (producer as ProgramSkillFrame)?.InstructionIndex));
        }
    }
    private void CloseWuZishengNativeDiscard(NativeLogicalDiscardInvocationDraft? draft)
    {
        if (draft is null || !HasWuZishengMovementCapability ||
            !ProgramEventHistory<WuZishengDiscardAdmittedEvent>().Any(e => e.InvocationId == draft.InvocationId)) return;
        if (ProgramEventHistory<WuZishengDiscardClosedEvent>().Any(e => e.InvocationId == draft.InvocationId))
            throw new InvalidOperationException("A Zisheng native discard invoice cannot close twice.");
        AdvanceEventRulesAndQueueFact(new WuZishengDiscardClosedEvent(draft.InvocationId, _movementSequence));
    }
    private CardMovementBatchContext CaptureWuZishengMovementBatch(CardMovementBatchContext batch, IReadOnlyList<WuZishengMovementSource> sources)
    {
        if (!HasWuZishengMovementCapability) return batch;
        var qualified = new List<(WuZishengMovementSource Source, int Victim, WuZishengMovementMaterial Material)>();
        foreach (var admission in ProgramEventHistory<WuZishengDiscardAdmittedEvent>())
        {
            if (admission.TurnNumber != batch.TurnNumber || !ValidWuZishengMovementSource(admission.Source)) continue;
            var closed = ProgramEventHistory<WuZishengDiscardClosedEvent>().SingleOrDefault(e => e.InvocationId == admission.InvocationId);
            foreach (var material in ProgramEventHistory<WuZishengDiscardMaterialAdmittedEvent>().Where(e => e.InvocationId == admission.InvocationId))
            {
                if (material.From.OwnerSeat is not { } victim || victim == admission.ExecutorSeat || !IsOwnedDiscardSource(material.From)) continue;
                foreach (var move in batch.Movements.Where(m => m.CardId == material.CardId && m.CardKind == material.PrintedKind &&
                    m.Sequence > admission.SequenceBefore && (closed is null || m.Sequence <= closed.SequenceAfter)))
                {
                    var directEntry = move.From == material.From && move.Reason == admission.Reason && batch.ParentFrameId == material.ProducerFrameId;
                    if (directEntry && (move.To == CardLocation.Processing || move.To == CardLocation.DiscardPile ||
                        move.To == CardLocation.OutsideGame && move.From.Zone == CardZoneKind.Equipment))
                    {
                        var bound = ProgramEventHistory<WuZishengDiscardEntryBoundEvent>().Where(e => e.InvocationId == admission.InvocationId && e.MaterialIndex == material.Index).ToArray();
                        if (bound.Length == 0) AdvanceEventRulesAndQueueFact(new WuZishengDiscardEntryBoundEvent(admission.InvocationId,
                            material.Index, batch.Id, move.Sequence, material.ProducerFrameId));
                        else if (bound is not [var first] || first.MovementSequence != move.Sequence) directEntry = false;
                    }
                    var terminal = directEntry && (move.To == CardLocation.DiscardPile ||
                        move.To == CardLocation.OutsideGame && move.From.Zone == CardZoneKind.Equipment);
                    if (!terminal && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile &&
                        (move.Reason == admission.Reason || move.Reason.Value == admission.Reason.Value + "-finished" || move.Reason.Value == admission.Reason.Value + "-discard"))
                    {
                        var entry = _cardMovements.LastOrDefault(m => m.CardId == move.CardId && m.Sequence < move.Sequence);
                        terminal = entry is not null && entry.From == material.From && entry.To == CardLocation.Processing &&
                            entry.Reason == admission.Reason && ProgramEventHistory<WuZishengDiscardEntryBoundEvent>().Any(e =>
                                e.InvocationId == admission.InvocationId && e.MaterialIndex == material.Index && e.MovementSequence == entry.Sequence &&
                                e.ProducerFrameId == material.ProducerFrameId) && batch.ParentFrameId == material.ProducerFrameId;
                    }
                    if (terminal) qualified.Add((admission.Source, victim,
                        new(move.CardId, move.CardKind, material.From, move.Sequence, true, admission.InvocationId)));
                }
            }
        }
        foreach (var move in batch.Movements.Where(m => m.To.Zone == CardZoneKind.Hand && m.To.OwnerSeat is not null && m.From != m.To))
        {
            var original = WuZishengObtainOriginalSource(batch, move);
            if (original?.OwnerSeat is not { } victim || victim == move.To.OwnerSeat || !IsValidPlayerSeat(victim)) continue;
            foreach (var source in sources.Where(s => s.Source.OwnerSeat == move.To.OwnerSeat && ValidWuZishengMovementSource(s)))
                qualified.Add((source, victim, new(move.CardId, move.CardKind, original.Value, move.Sequence, false)));
        }
        var list = qualified.GroupBy(e => (e.Source, e.Victim)).OrderBy(g => g.Key.Source.Source.OwnerSeat)
            .ThenBy(g => RelativeTableOrder(_currentSeat, g.Key.Victim))
            .ThenBy(g => g.Key.Source.Source.SkillInstanceId, StringComparer.Ordinal)
            .Select(g => new WuZishengMovementQualification(g.Key.Source, batch.Id, g.Key.Victim)
            { Materials = g.Select(e => e.Material).GroupBy(m => m.CardId).Select(g => g.OrderBy(m => m.TerminalSequence).First()).ToArray() }).ToArray();
        foreach (var q in list) AdvanceEventRulesAndQueueFact(new WuZishengMovementQualifiedEvent(batch.Id, q));
        return list.Length == 0 ? batch : batch with { WuZisheng = list };
    }
    private CardLocation? WuZishengObtainOriginalSource(CardMovementBatchContext batch, CardMovementRecord exit)
    {
        if (exit.From.OwnerSeat is not null) return exit.From;
        if (exit.From != CardLocation.Processing || batch.ParentFrameId is not { } parent) return null;
        var entry = _cardMovements.LastOrDefault(m => m.CardId == exit.CardId && m.Sequence < exit.Sequence);
        if (entry is null || entry.To != CardLocation.Processing || entry.From.OwnerSeat is not { } originalOwner || originalOwner == exit.To.OwnerSeat) return null;
        var entryBatch = _pendingCardsMovedBatches.SingleOrDefault(b => b.Id != batch.Id && b.Movements.Contains(entry) &&
            b.ParentFrameId == parent && b.ParentBatchId == batch.ParentBatchId && b.TurnNumber == batch.TurnNumber);
        if (entryBatch is null) return null;
        if (LifecycleCardUse(parent) is { CardKind: CardKind.Snatch } use && exit.To == CardLocation.Hand(use.SourceSeat) &&
            use.TargetSeats.Contains(originalOwner) && entry.Reason == CardMoveReasons.Snatch && exit.Reason == CardMoveReasons.SnatchFinished &&
            ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == parent && e.SourceSeat == use.SourceSeat && e.CardKind == CardKind.Snatch) &&
            ProgramEventHistory<TargetCardTakenEvent>().Any(e => e.ResolutionId == parent && e.SourceSeat == use.SourceSeat &&
                e.TargetSeat == originalOwner && e.FromZone == entry.From.Zone && (e.PublicCardId is null || e.PublicCardId == exit.CardId))) return entry.From;
        if (_resolutionStack.SingleOrDefault(f => f.Id == parent) is not ProgramSkillFrame program || program.InstructionIndex < 1 ||
            batch.OriginOwnerSeat != program.OwnerSeat || batch.OriginSkillId != program.SkillId || batch.OriginSkillInstanceId != program.SkillInstanceId ||
            entryBatch.OriginOwnerSeat != program.OwnerSeat || entryBatch.OriginSkillId != program.SkillId || entryBatch.OriginSkillInstanceId != program.SkillInstanceId ||
            _contentRegistry.GetSkill(program.SkillId).Program?.GameplayHash != program.GameplayHash) return null;
        var effect = ProgramInstructionResolver.Default.Resolve(program, _contentRegistry.GetSkill(program.SkillId).Program!).GetPausedInstruction(program.InstructionIndex).Effect;
        return effect is { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard, Destination: SkillProgramCardDestination.SelectedTargetHand,
                CardOwnerRef: { } ownerRef, TargetReference: { } targetRef } && entry.Reason == exit.Reason && effect.Zones.Contains(entry.From.Zone) &&
            ResolveProgramParticipant(program, ownerRef) == originalOwner && exit.To == CardLocation.Hand(ResolveProgramParticipant(program, targetRef)) ? entry.From : null;
    }
    private IEnumerable<ProgramTriggerCandidate> CollectWuZishengMovementCandidates(CardMovementBatchContext batch)
    {
        for (var i = 0; i < (batch.WuZisheng?.Count ?? 0); i++)
        {
            var q = batch.WuZisheng![i];
            if (!ValidWuZishengQualification(q, batch) || !_players[q.Source.Source.OwnerSeat].IsAlive ||
                !_players[q.VictimSeat].IsAlive || !_players[q.VictimSeat].HasHpValue || _players[q.VictimSeat].Hp <= 0) continue;
            var trigger = _contentRegistry.GetSkill(WuZishengSkill).Program!.Triggers.Single(IsWuZishengMovementTrigger);
            yield return new(q.Source.Source.OwnerSeat, WuZishengSkill, WuZishengMovementBinding, q.Source.Source.SkillInstanceId, q.Source.GameplayHash, trigger.Priority, i);
        }
    }
    private bool ValidWuZishengQualification(WuZishengMovementQualification q, CardMovementBatchContext batch) =>
        q.BatchId == batch.Id && ValidWuZishengMovementSource(q.Source) && IsValidPlayerSeat(q.VictimSeat) && q.VictimSeat != q.Source.Source.OwnerSeat &&
        q.Materials.Count > 0 && q.Materials.Select(m => m.CardId).Distinct().Count() == q.Materials.Count &&
        q.Materials.All(m => m.OriginalFrom.OwnerSeat == q.VictimSeat && batch.Movements.Any(e => e.CardId == m.CardId &&
            e.CardKind == m.PrintedKind && e.Sequence == m.TerminalSequence)) &&
        ProgramEventHistory<WuZishengMovementQualifiedEvent>().Count(e => e.BatchId == batch.Id && e.Qualification.Source == q.Source &&
            e.Qualification.VictimSeat == q.VictimSeat && e.Qualification.Materials.SequenceEqual(q.Materials)) == 1;
    private ProgramSkillWindowContext CreateWuZishengMovementContext(CardsMovedTriggerWindowFrame parent, ProgramTriggerCandidate candidate)
    {
        var q = parent.Batch.WuZisheng![candidate.OccurrenceIndex];
        if (!ValidWuZishengQualification(q, parent.Batch) || q.Source.Source != new CardConversionSource(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId))
            throw new InvalidOperationException("Zisheng requires its exact causal victim and physical movement batch.");
        return new(SkillProgramTriggerWindow.CardsMoved, parent.Id, candidate.OwnerSeat, SourceSeat: candidate.OwnerSeat, TargetSeat: q.VictimSeat,
            Amount: q.Materials.Count, OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]) with { MovedCardCount = q.Materials.Count }, MovementBatch: parent.Batch);
    }
    private bool IsIssuedWuZishengMovementCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId != WuZishengSkill || candidate.BindingId != WuZishengMovementBinding ||
            !IsValidPlayerSeat(candidate.OwnerSeat) || !_players[candidate.OwnerSeat].IsAlive ||
            context.Window != SkillProgramTriggerWindow.CardsMoved || context.MovementBatch is not { } batch ||
            _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not CardsMovedTriggerWindowFrame parent ||
            parent.Batch.Id != batch.Id || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            parent.Candidates[parent.CandidateIndex] != candidate || candidate.OccurrenceIndex < 0 || candidate.OccurrenceIndex >= (batch.WuZisheng?.Count ?? 0)) return false;
        var q = batch.WuZisheng![candidate.OccurrenceIndex];
        return ValidWuZishengQualification(q, batch) && q.Source.Source == new CardConversionSource(candidate.SkillId, candidate.BindingId,
            candidate.OwnerSeat, candidate.SkillInstanceId) && q.Source.GameplayHash == candidate.GameplayHash &&
            context.OwnerSeat == candidate.OwnerSeat && context.SourceSeat == candidate.OwnerSeat && context.TargetSeat == q.VictimSeat &&
            context.Amount == q.Materials.Count && _players[q.VictimSeat].IsAlive && _players[q.VictimSeat].HasHpValue && _players[q.VictimSeat].Hp > 0;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29062, SkillProgramEffectOp.ResolveWuZhangFeiMovementDamage)]
    private bool CanOfferWuZishengMovement(ProgramTriggerLegalityContext gate) => IsIssuedWuZishengMovementCandidate(gate.Candidate, gate.Context);
    private bool ExactWuZishengMovementParent(ProgramSkillFrame f, out WuZishengMovementQualification q)
    {
        q = null!;
        if (f.SkillId != WuZishengSkill || f.TriggerId != WuZishengMovementBinding || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardsMoved, MovementBatch: { } batch } c ||
            _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not CardsMovedTriggerWindowFrame parent || parent.Batch.Id != batch.Id ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex])) return false;
        var i = parent.Candidates[parent.CandidateIndex].OccurrenceIndex;
        if (i < 0 || i >= (batch.WuZisheng?.Count ?? 0)) return false;
        q = batch.WuZisheng![i];
        return ValidWuZishengQualification(q, batch) && q.Source.Source == new CardConversionSource(f.SkillId, f.TriggerId, f.OwnerSeat, f.SkillInstanceId) &&
            q.Source.GameplayHash == f.GameplayHash && c.SourceSeat == f.OwnerSeat && c.TargetSeat == q.VictimSeat && c.Amount == q.Materials.Count;
    }
    [ProgramCapability]
    private sealed class WuZhangFeiMovementCapability(GameEngine engine) : IWuZhangFeiMovementHost
    { public SkillProgramStepOutcome ResolveMovementDamage(ProgramSkillFrame frame) => engine.ResolveWuZishengMovement(frame); }
    private SkillProgramStepOutcome ResolveWuZishengMovement(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!ExactWuZishengMovementParent(f, out var q) || f.WuZishengMovement is not null)
            throw new InvalidOperationException("Zisheng damage lost its original causal movement mother.");
        var victim = _players[q.VictimSeat];
        var amount = victim.IsAlive && victim.HasHpValue && _players[f.OwnerSeat].IsAlive && _winner == Winner.None
            ? Math.Min(q.Materials.Count, Math.Max(0, victim.Hp)) : 0;
        var context = f.WindowContext!;
        var suspended = amount > 0 ? CaptureWuZishengAncestorDying(f) : null;
        ReplaceRuntimeTop(f = f with { WuZishengMovement = new(context.ParentFrameId, q, amount, victim.Hp, amount > 0) { SuspendedDying = suspended } });
        if (suspended is not null) AdvanceEventRulesAndQueueFact(new WuZishengSuspendedDyingIssuedEvent(f.Id, suspended));
        AdvanceEventRulesAndQueueFact(new WuZishengMovementDamageIssuedEvent(f.Id, context.ParentFrameId, q.BatchId,
            q.Source.Source, q.Source.GameplayHash, q.VictimSeat, q.Materials.Count, victim.Hp, amount));
        if (amount == 0) { CompleteWuZishengMovement(f); return SkillProgramStepOutcome.AwaitChild; }
        BeginProgramSkillDamage(f, q.VictimSeat, amount);
        return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29062)]
    private bool ResumeWuZishengMovement(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuZishengMovement: not null } f || f.Id != id) return false;
        if (f.AttackAttempt is not null) { CompleteDamageAttack(new ProgramAttackHandle(this, id)); return true; }
        CompleteWuZishengMovement(f); return true;
    }
    private void CompleteWuZishengMovement(ProgramSkillFrame f)
    {
        if (!ExactWuZishengMovementParent(f, out _) || f.WuZishengMovement is not { } r) throw new InvalidOperationException("Zisheng lost its paid damage return.");
        if (r.SuspendedDying is { } suspended)
        {
            if (!WuZishengSuspendsDying(f, suspended) || CurrentDamageAttempt?.ResolutionId != suspended.AttackOwnerFrameId)
                throw new InvalidOperationException("Zisheng cannot return to a changed original dying or attack cursor.");
            AdvanceEventRulesAndQueueFact(new WuZishengSuspendedDyingReturnedEvent(f.Id, suspended.DyingFrameId, suspended.AttackOwnerFrameId));
        }
        AdvanceEventRulesAndQueueFact(new WuZishengMovementCompletedEvent(f.Id, r.Qualification.BatchId, r.Qualification.VictimSeat, r.RequestedDamage));
        ReplaceRuntimeTop(f = f with { WuZishengMovement = null }); FinishProgramSkill(f, true);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29062)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29062)]
    private bool ContinueIssuedWuZishengMovement(ProgramSkillFrame f) =>
        f.InstructionIndex is 0 or 1 && ExactWuZishengMovementParent(f with { InstructionIndex = 1 }, out _);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 20)]
    private bool AllowsWuZishengNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess) =>
        source is null && nature is null && !sourceLess && _resolutionStack.LastOrDefault()?.Id == f.Id &&
        f.WuZishengMovement is { Issued: true } r && r.RequestedDamage == amount && r.Qualification.VictimSeat == target &&
        ExactWuZishengMovementParent(f, out _) && CurrentDamageAttempt is { } original &&
        _resolutionStack.FindIndex(x => x.Id == original.ResolutionId) is var ownerIndex && ownerIndex >= 0 &&
        ownerIndex < _resolutionStack.FindIndex(x => x.Id == r.ParentWindowId);

    private bool WuZishengMovementFirstChild(ProgramSkillFrame f, ResolutionFrame child) =>
        f.WuZishengMovement is { Issued: true } && ExactWuZishengMovementParent(f, out _) &&
        f.AttackAttempt is not null && DyingSuitsStructuralEdge(f, child);
    private bool WuZhangFeiMovementObserverSuffix(int index) => HasObserverSuffix(index,
        static (game, child) => game.IsMovementObserverEdge(child) ||
            game.TypedDamagePostEventEdge(game._resolutionStack[child - 1], game._resolutionStack[child]) ||
            game._resolutionStack[child - 1] is ProgramSkillFrame parent &&
                (game.WuZishengMovementFirstChild(parent, game._resolutionStack[child]) || game.WuHaoxianFirstChild(parent, game._resolutionStack[child])) ||
            NativeDrawAttemptRidesOn(game._resolutionStack[child], game._resolutionStack[child - 1]) ||
            game.RecoveryReplacementFrameRidesOn(game._resolutionStack[child], game._resolutionStack[child - 1]),
        static (game, child, dying) => game.IsSuspendedOrPaidRescueObserverSuffix(child, dying));
    private ProgramSkillFrame? WuZhangFeiMovementObserverRoot() => FindProgramObserverRoot(
        static (game, f, child) => game.WuZishengMovementFirstChild(f, child) || game.WuHaoxianFirstChild(f, child),
        static (game, index) => game.WuZhangFeiMovementObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1390)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1500)]
    private bool HasWuZhangFeiMovementDamageObserver(long id) => WuZhangFeiMovementObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1520)]
    private bool HasWuZhangFeiMovementBeforeDamageObserver(long id) => WuZhangFeiMovementObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1350)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 770)]
    private bool IsWuZhangFeiMovementDying() => ActiveDying is { } dying && WuZhangFeiMovementObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 30)]
    private bool AllowsWuZhangFeiObserverNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            WuZhangFeiMovementObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29062)]
    private bool AdvanceWuZhangFeiObserverChildren() => _pendingDecision is null && WuZhangFeiMovementObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 29062)]
    private void AssertWuZishengMovementReceipt(ProgramSkillFrame f)
    {
        if (f.WuZishengMovement is not { } r) return;
        if (!ExactWuZishengMovementParent(f, out var q) || q.Source != r.Qualification.Source || q.BatchId != r.Qualification.BatchId ||
            q.VictimSeat != r.Qualification.VictimSeat || !q.Materials.SequenceEqual(r.Qualification.Materials) || r.ParentWindowId != f.WindowContext!.ParentFrameId ||
            r.RequestedDamage != (r.Issued ? Math.Min(q.Materials.Count, Math.Max(0, r.VictimHpBefore)) : 0) ||
            ProgramEventHistory<WuZishengMovementDamageIssuedEvent>().Count(e => e.FrameId == f.Id && e.ParentWindowId == r.ParentWindowId &&
                e.BatchId == q.BatchId && e.Source == q.Source.Source && e.GameplayHash == q.Source.GameplayHash && e.VictimSeat == q.VictimSeat &&
                e.CardCount == q.Materials.Count && e.HpBefore == r.VictimHpBefore && e.RequestedDamage == r.RequestedDamage) != 1)
            throw new InvalidOperationException("Zisheng lost its frozen causal movement damage invoice.");
        if (r.SuspendedDying is { } suspended && !WuZishengSuspendsDying(f, suspended))
            throw new InvalidOperationException("Zisheng's exact suspended ancestor dying token or native cursor changed.");
    }

    private static string WuZishengCursorHash(ResolutionFrame frame) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhang_fei_wumiao_WuZhangFeiMovement.WuZishengCursorHash(frame);
    private WuZishengSuspendedDying? CaptureWuZishengAncestorDying(ProgramSkillFrame f)
    {
        if (ActiveDying is not { } dying) return null;
        var parent = _resolutionStack.SingleOrDefault(x => x.Id == dying.ParentFrameId);
        if (parent is null || !WuZishengDyingPrefix(f, dying) || !DyingSuitsStructuralEdge(parent, dying))
            throw new InvalidOperationException("Zisheng requires the exact native Dying-to-movement producer ancestry before nesting damage.");
        var attackOwner = CurrentDamageAttempt is { } attack ? _resolutionStack.Single(x => x.Id == attack.ResolutionId) : null;
        return new(dying.Id, parent.Id, WuZishengCursorHash(dying), WuZishengCursorHash(parent),
            attackOwner?.Id, attackOwner is null ? null : WuZishengCursorHash(attackOwner));
    }
    // Pure native ancestry proof: called by ActiveDying, so it deliberately does
    // not query that getter, CurrentDamageAttempt or the mature observer roots.
    private bool WuZishengDyingPrefix(ProgramSkillFrame f, DyingFrame dying)
    {
        if (!ExactWuZishengMovementParent(f, out _) || f.WindowContext is not { } context) return false;
        var di = _resolutionStack.FindIndex(x => x.Id == dying.Id);
        var fi = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (di < 1 || fi <= di + 1 || _resolutionStack[di - 1].Id != dying.ParentFrameId ||
            _resolutionStack[fi - 1].Id != context.ParentFrameId ||
            _resolutionStack.Skip(di + 1).Take(fi - di - 1).Any(x => x is DyingFrame) ||
            ProgramEventHistory<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat && e.KillerSeat == dying.KillerSeat) != 1)
            return false;
        for (var i = di + 1; i < fi; i++)
            if (!WuZishengSuspensionEdge(_resolutionStack[i - 1], _resolutionStack[i])) return false;
        return true;
    }
    private bool WuZishengSuspensionEdge(ResolutionFrame parent, ResolutionFrame child) =>
        DyingSuitsStructuralEdge(parent, child) || TypedDamagePostEventEdge(parent, child) ||
        NativeDrawAttemptRidesOn(child, parent) || RecoveryReplacementFrameRidesOn(child, parent);
    private bool WuZishengSuspendsDying(ProgramSkillFrame f, WuZishengSuspendedDying s)
    {
        if (f.WuZishengMovement is not { Issued: true, SuspendedDying: { } claimed } r || claimed != s ||
            _resolutionStack.SingleOrDefault(x => x.Id == s.DyingFrameId) is not DyingFrame dying ||
            _resolutionStack.SingleOrDefault(x => x.Id == s.ParentFrameId) is not { } parent || parent.Id != dying.ParentFrameId ||
            !WuZishengDyingPrefix(f, dying) || WuZishengCursorHash(dying) != s.DyingHash || WuZishengCursorHash(parent) != s.ParentHash ||
            s.AttackOwnerFrameId is { } owner && (_resolutionStack.SingleOrDefault(x => x.Id == owner) is not { } attackOwner ||
                WuZishengCursorHash(attackOwner) != s.AttackOwnerHash) ||
            ProgramEventHistory<WuZishengSuspendedDyingIssuedEvent>().Count(e => e.FrameId == f.Id && e.Suspension == s) != 1 ||
            ProgramEventHistory<WuZishengMovementDamageIssuedEvent>().Count(e => e.FrameId == f.Id && e.ParentWindowId == r.ParentWindowId &&
                e.BatchId == r.Qualification.BatchId && e.Source == r.Qualification.Source.Source && e.RequestedDamage == r.RequestedDamage) != 1) return false;
        var fi = _resolutionStack.FindIndex(x => x.Id == f.Id);
        for (var i = fi + 1; i < _resolutionStack.Count; i++)
            if (!WuZishengSuspensionEdge(_resolutionStack[i - 1], _resolutionStack[i])) return false;
        return true;
    }
    private bool IsOriginalDyingSuspendedByWuZishengMovement(DyingFrame dying) => _resolutionStack.OfType<ProgramSkillFrame>().Any(f =>
        f.WuZishengMovement?.SuspendedDying is { } suspended && suspended.DyingFrameId == dying.Id && WuZishengSuspendsDying(f, suspended));
}
