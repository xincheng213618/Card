namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidGaoXiang(ProgramSkillFrame f)
    {
        if (f.GaoXiangChiying is not { } r || f.SkillId != GaoXiangComposition.Skill || f.ActivationId != GaoXiangComposition.Activation || f.TriggerId is not null ||
            f.WindowContext is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            r.Source != GaoXiangSource(f) || r.Hash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            !Enum.IsDefined(r.Stage) || r.Turn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat || r.TurnOwner != f.OwnerSeat ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Index < 0 || r.Index > r.Victims.Count ||
            r.Victims.Distinct().Count() != r.Victims.Count || r.Victims.Any(s => !IsValidPlayerSeat(s) || s == r.Actor) ||
            ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.ActivationId == f.ActivationId) != 1 ||
            ProgramEventHistory<GaoXiangChiyingStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started != new GaoXiangChiyingStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner)) return false;
        var actors = ProgramEventHistory<GaoXiangChiyingActorEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var plans = ProgramEventHistory<GaoXiangChiyingCommittedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var victims = ProgramEventHistory<GaoXiangChiyingVictimEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Ordinal).ToArray();
        var moves = ProgramEventHistory<GaoXiangChiyingMovedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage is GaoXiangChiyingStage.HealthQueries or GaoXiangChiyingStage.Actor)
            return r.Actor == -1 && !r.Committed && r.Index == 0 && r.Victims.Count == 0 && r.Discards.Count == 0 && r.Gains.Count == 0 && actors.Length == 0 && plans.Length == 0 && victims.Length == 0 && moves.Length == 0;
        if (!IsValidPlayerSeat(r.Actor) || r.ActorHp > r.OwnerHp || actors is not [var actor] || actor != new GaoXiangChiyingActorEvent(f.Id, r.Actor, r.OwnerHp, r.ActorHp)) return false;
        if (!r.Committed) return r.Stage == GaoXiangChiyingStage.Victims && r.Index == 0 && r.Discards.Count == 0 && r.Gains.Count == 0 && plans.Length == 0 && victims.Length == 0 && moves.Length == 0;
        if (plans is not [var plan] || plan != new GaoXiangChiyingCommittedEvent(f.Id, r.Actor, r.Victims.Count) || victims.Length != r.Victims.Count ||
            victims.Where((e, index) => e != new GaoXiangChiyingVictimEvent(f.Id, index, r.Actor, r.Victims[index])).Any() ||
            r.Discards.Select(m => m.Ordinal).Distinct().Count() != r.Discards.Count || r.Discards.Select(m => m.CardId).Distinct().Count() != r.Discards.Count ||
            r.Gains.Select(m => m.CardId).Distinct().Count() != r.Gains.Count || moves.Length != r.Discards.Count + r.Gains.Count ||
            r.Discards.Any(m => m.Ordinal < 0 || m.Ordinal >= r.Victims.Count || m.Ordinal > r.Index || m.Victim != r.Victims[m.Ordinal] || m.From.OwnerSeat != m.Victim ||
                m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || !GaoXiangMoveExists(f, m, false)) ||
            r.Gains.Any(m => !r.Discards.Any(d => d.Ordinal == m.Ordinal && d.Victim == m.Victim && d.CardId == m.CardId && d.PrintedKind == m.PrintedKind && d.To == CardLocation.DiscardPile) ||
                r.Actor == f.OwnerSeat || GetProgramCardCategory(m.PrintedKind) != SkillProgramCardCategory.Basic || m.From != CardLocation.DiscardPile || !GaoXiangMoveExists(f, m, true))) return false;
        if (r.Stage == GaoXiangChiyingStage.Card) return r.Gains.Count == 0 && r.Discards.All(m => m.Ordinal < r.Index);
        if (r.Stage == GaoXiangChiyingStage.DiscardChildren) return r.Index < r.Victims.Count && r.Gains.Count == 0 && r.Discards.Count(m => m.Ordinal == r.Index && m.Sequence > r.Before && m.Sequence <= r.After) == 1;
        return r.Stage == GaoXiangChiyingStage.GainChildren && r.Index == r.Victims.Count && r.Gains.Count > 0 && r.Gains.All(m => m.Sequence > r.Before && m.Sequence <= r.After);
    }
    private bool GaoXiangMoveExists(ProgramSkillFrame f, GaoXiangChiyingMove receipt, bool gain)
    {
        var r = f.GaoXiangChiying!;
        if (receipt.Sequence <= 0 || receipt.BatchId <= 0 || ProgramEventHistory<GaoXiangChiyingMovedEvent>().Count(e => e == GaoXiangMovedEvent(f.Id, gain, receipt)) != 1) return false;
        return _cardMovements.Count(m => m.Sequence == receipt.Sequence && m.CardId == receipt.CardId && m.CardKind == receipt.PrintedKind && m.From == receipt.From && m.To == receipt.To &&
            m.Reason == (gain ? GaoXiangGainReason : GaoXiangDiscardReason) && (gain ? m.To == CardLocation.Hand(r.Actor) ||
                IsHappyWeiYanRedirectedGain(m, CardLocation.Hand(r.Actor)) : m.To == CardLocation.DiscardPile ||
                IsRedirectedPaidDiscardMovement(m, receipt.From, GaoXiangDiscardReason) || m.To == CardLocation.OutsideGame &&
                (IsEphemeralCardDestroyedMaterial(m.CardId, m.From, m.To) || GetAdvancedCard(m.CardId).IsGeneralWeapon))) == 1;
    }
    private void RequireGaoXiang(ProgramSkillFrame f)
    { if (!ValidGaoXiang(f)) throw new InvalidOperationException("Chiying lost its original skill/frame, frozen actor/participant queue or actual once-paid discard/gain receipt."); }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 44900)] private void AssertGaoXiang(ProgramSkillFrame f)
    {
        if (f.GaoXiangChiying is not { } r) return;
        RequireGaoXiang(f);
        if (f.PendingMovementContinuation is { } pending && !GaoXiangMovement(f, GetPausedPrivateOfferEffect(f), pending))
            throw new InvalidOperationException("Chiying lost its original paid movement cursor.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is { } p && r.Stage is GaoXiangChiyingStage.Actor or GaoXiangChiyingStage.Victims or GaoXiangChiyingStage.Card &&
            (p.PlayerSeat != GaoXiangChooser(f) || !AssistedChoicesEqual(p.Choices, GaoXiangChoices(f)))) throw new InvalidOperationException("Chiying's exact participant/opaque-card menu changed.");
    }
    private bool HasAnniversaryGaoXiangPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { GaoXiangChiying: { Stage: GaoXiangChiyingStage.Actor or GaoXiangChiyingStage.Victims or GaoXiangChiyingStage.Card } } f &&
        f.Step == ResolutionFrameStep.AwaitingResponse && p.PlayerSeat == GaoXiangChooser(f) && ValidGaoXiang(f) && AssistedChoicesEqual(p.Choices, GaoXiangChoices(f));
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 44900)] private bool GaoXiangMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.GaoXiangChiying is { Stage: GaoXiangChiyingStage.DiscardChildren or GaoXiangChiyingStage.GainChildren } && ValidGaoXiang(f) &&
        e?.Op == SkillProgramEffectOp.ResolveAnniversaryGaoXiangChiying && f.PendingMovementContinuation == p && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool GaoXiangFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.GaoXiangChiying is not { Stage: GaoXiangChiyingStage.DiscardChildren or GaoXiangChiyingStage.GainChildren } r || !ValidGaoXiang(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.Movements.Count > 0 &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (r.Stage != GaoXiangChiyingStage.DiscardChildren || !r.Discards.Any(m => m.Ordinal == r.Index && m.PrintedKind == CardKind.SilverLion && m.From.Zone == CardZoneKind.Equipment)) return false;
        var target = r.Victims[r.Index];
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id &&
            recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.AwaitedProgramMovement, f.Id) && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.Completion.MoveReason == GaoXiangDiscardReason && recovery.Attempt.TargetSeat == target && recovery.Attempt.SourceSeat == target && recovery.Attempt.Amount == 1;
        return child is HpChangedTriggerWindowFrame hp && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == target && hp.Change.TargetSeat == target && hp.Change.Amount == 1;
    }
    private ProgramSkillFrame? GaoXiangObserverRoot() => FindProgramObserverRoot(static (engine, root, child) => engine.GaoXiangFirstChild(root, child), static (engine, index) => engine.SameNameHandObserverSuffix(index));
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 44900)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 44900)]
    private bool GaoXiangRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => GaoXiangFirstChild(f, recovery);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 44900)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 44900)]
    private bool GaoXiangDamageObserver(long id) => GaoXiangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 44900)] private bool GaoXiangBeforeDamageObserver(long id) =>
        GaoXiangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 44900)] private bool GaoXiangDyingObserver() =>
        ActiveDying is { } dying && GaoXiangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 44900)] private bool AdvanceGaoXiangSubtree() =>
        _pendingDecision is null && GaoXiangObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 44900)]
    private bool GaoXiangNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || GaoXiangObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
}
