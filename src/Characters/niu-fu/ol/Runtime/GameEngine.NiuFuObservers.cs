namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27050)]
    private void AssertNiuFu(ProgramSkillFrame f)
    {
        if (f.NiuFu is not { } r) return;
        if (r.Source != NiuFuSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != 1 || f.InstructionIndex != 1 || r.ActualTurn != _turnNumber ||
            GetPausedPrivateOfferEffect(f)?.Op != r.Op || !NiuFuComposition.Handles(r.Op) || f.TriggerId is null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !Enum.IsDefined(r.Stage) ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } c || c.ParentFrameId != r.WindowId || c.DamageFrameId != r.DamageId || c.Amount != r.DamageAmount || c.TargetSeat != r.TargetSeat || r.DamageAmount < 1 ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.WindowId) is not { } w || w.ParentFrameId != r.DamageId || w.TriggerWindow != c.Window || w.TargetSeat != r.TargetSeat ||
            w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex].ToProgramCandidate()) ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == r.DamageId) is not { } d || d.ParentFrameId != r.AttackId || d.TargetSeat != r.TargetSeat || d.Amount != r.DamageAmount ||
            r.DamageSourceSeat is { } source && d.SourceSeat != source || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash ||
            ProgramEventHistory<NiuFuStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] || start.Source != r.Source || start.GameplayHash != r.GameplayHash || start.Op != r.Op ||
            start.ActualTurn != r.ActualTurn || start.WindowId != r.WindowId || start.DamageId != r.DamageId || start.DamageAmount != r.DamageAmount || start.TargetSeat != r.TargetSeat ||
            start.DamageSourceSeat != r.DamageSourceSeat || start.AttackId != r.AttackId || start.ActionId != r.ActionId || start.PhysicalCount != r.Physical.Count)
            throw new InvalidOperationException("NiuFu lost its exact original whole-damage candidate, source, instruction or immutable start invoice.");
        var shisuan = r.Op == SkillProgramEffectOp.ResolveShisuanRecastSourceChoice;
        if (shisuan ? r.TargetSeat != f.OwnerSeat || r.Physical.Count != 0 || r.ActionId is not null || r.Stage is NiuFuStage.ZoneChoice or NiuFuStage.ZoneChildren || r.NextZone != 0 :
            r.DamageSourceSeat != f.OwnerSeat || r.Stage is not (NiuFuStage.ZoneChoice or NiuFuStage.ZoneChildren) || r.NextZone is < 0 or > 3 || r.Recast is not null || r.SourceBranch is not null ||
            r.Physical.Count > 0 && r.Physical.All(card => IsSlashCard(card.Kind)))
            throw new InvalidOperationException("NiuFu mixed its mandatory owner recast with the exact converted/zero-entity Slash target receipt.");
        var entities = ProgramEventHistory<NiuFuSlashEntityEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (entities.Length != r.Physical.Count || r.Physical.Select(e => e.CardId).Distinct().Count() != r.Physical.Count || entities.Where((e, i) => e.Index != i || e.CardId != r.Physical[i].CardId || e.Kind != r.Physical[i].Kind).Any())
            throw new InvalidOperationException("Zonglue lost an individually frozen original physical entity.");
        if (!shisuan)
        {
            if (r.ActionId is { } actionId)
            {
                if (!ProgramEventHistory<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == actionId && e.Action.Type == CardActionType.Use && IsSlashCard(e.Action.EffectiveKind) &&
                    e.Action.PhysicalCards.Select(p => new NiuFuEntity(p.CardId, p.CardKind, p.From)).SequenceEqual(r.Physical)))
                    throw new InvalidOperationException("Zonglue must retain the original accepted use's unchanged physical payment, independently of later native actor changes.");
            }
            else if (r.Physical.Count != 0 || !ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == r.AttackId && e.CardId == 0 && IsSlashCard(e.CardKind) && e.SourceSeat == f.OwnerSeat))
                throw new InvalidOperationException("Zonglue zero-entity legacy use requires its exact declared native Slash.");
        }
        var children = r.Stage is NiuFuStage.RecastCostChildren or NiuFuStage.RecastDrawChildren or NiuFuStage.SourceGiftChildren or NiuFuStage.ZoneChildren;
        if (children ? f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } p || p.SubjectSeat != f.OwnerSeat || r.Steps.Count == 0 : f.PendingMovementContinuation is not null)
            throw new InvalidOperationException("NiuFu movement children lost their sole owning paid continuation.");
        var issued = ProgramEventHistory<NiuFuStepIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (issued.Length != r.Steps.Count || r.Steps.Where((step, i) => step.Index != i || issued[i].Step != step || step.Before < 0 || step.After < step.Before || step.After > _movementSequence || step.ActualCount is < 0 or > 1 || i > 0 && step.Before < r.Steps[i - 1].After).Any())
            throw new InvalidOperationException("NiuFu lost an ordered once-issued native movement step.");
        foreach (var step in r.Steps)
        {
            var moves = _cardMovements.Where(m => m.Sequence > step.Before && m.Sequence <= step.After && m.Reason.Value == step.Reason).ToArray();
            if (moves.Length != step.ActualCount || (step.Stage == NiuFuStage.RecastDrawChildren ? step.Reason != CardMoveReasons.RecastDraw.Value || step.CardId is not null || step.From is not null || step.To is not null || moves.Any(m => m.From != CardLocation.DrawPile || m.To != CardLocation.Hand(f.OwnerSeat)) :
                step.ActualCount != 1 || step.CardId is not { } id || step.From is not { } from || step.To is not { } to || moves is not [var moved] || moved.CardId != id || moved.CardKind != step.Kind || moved.From != from || moved.To != to ||
                (step.Stage == NiuFuStage.RecastCostChildren ? step.Reason != CardMoveReasons.RecastDiscard.Value || r.Recast is not { } recast || recast.CardId != id || recast.Kind != step.Kind || recast.From != from || from.OwnerSeat != f.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || to != (recast.IsGeneralWeapon && from.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile) :
                step.Stage == NiuFuStage.SourceGiftChildren ? step.Reason != ShisuanGiftReason || from != CardLocation.Equipment(r.DamageSourceSeat!.Value) || to != CardLocation.Hand(f.OwnerSeat) || moved.CardKind == CardKind.GeneralWeapon :
                step.Stage != NiuFuStage.ZoneChildren || step.Reason != ZonglueTakeReason || from.OwnerSeat != r.TargetSeat || !NiuFuZones.Contains(from.Zone) || to != (moved.CardKind == CardKind.GeneralWeapon && from.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.Hand(f.OwnerSeat)))))
                throw new InvalidOperationException("NiuFu cannot fabricate a recast, source gift, draw or one-per-zone original entity movement.");
        }
        if (r.Steps.Count(s => s.Stage == NiuFuStage.RecastCostChildren) > 1 || r.Steps.Count(s => s.Stage == NiuFuStage.RecastDrawChildren) > 1 || r.Steps.Count(s => s.Stage == NiuFuStage.SourceGiftChildren) > 1 ||
            r.Steps.Where(s => s.Stage == NiuFuStage.ZoneChildren).Select(s => s.From!.Value.Zone).Distinct().Count() != r.Steps.Count(s => s.Stage == NiuFuStage.ZoneChildren))
            throw new InvalidOperationException("NiuFu cannot repeat a paid cost, draw, gift or victim zone.");
        var branches = ProgramEventHistory<NiuFuSourceChoiceEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.SourceBranch is null ? branches.Length != 0 || r.SourceHpBefore is not null || r.SourceWasFaceDown is not null :
            !shisuan || r.SourceBranch is not ("lose-hp" or "turn-over" or "gift") || r.DamageSourceSeat is not { } sourceSeat || branches is not [var branch] || branch.SourceSeat != sourceSeat || branch.Branch != r.SourceBranch || branch.HpBefore != r.SourceHpBefore || branch.WasFaceDown != r.SourceWasFaceDown)
            throw new InvalidOperationException("Shisuan source choice lost its original actual chooser or paid branch.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27050)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27050)]
    private bool ContinuePaidNiuFu(ProgramSkillFrame f) { if (f.NiuFu is not { } r || r.Steps.Count == 0 && r.SourceBranch is null) return false; AssertNiuFu(f); return true; }
    private bool IsNiuFuMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p)
    { if (f.NiuFu is not { Steps.Count: > 0 } r || e?.Op != r.Op || p != f.PendingMovementContinuation) return false; AssertNiuFu(f); return true; }
    private bool NiuFuLionProof(ProgramSkillFrame f, int seat, CardMoveReason? reason)
    { var step = f.NiuFu!.Steps[^1]; return reason is not null && _cardMovements.Any(m => m.Sequence > step.Before && m.Sequence <= step.After && m.From == CardLocation.Equipment(seat) && m.CardKind == CardKind.SilverLion && m.Reason == reason); }
    private bool IsNiuFuRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.NiuFu is not { Steps.Count: > 0 } || f.PendingMovementContinuation is null) return false; AssertNiuFu(f);
        return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.SourceSeat == recovery.Attempt.TargetSeat && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && NiuFuLionProof(f, recovery.Attempt.TargetSeat, recovery.Attempt.Completion.MoveReason);
    }
    private bool NiuFuFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.NiuFu is not { } r) return false; AssertNiuFu(f);
        if (r.Stage == NiuFuStage.SourceHpChildren)
        {
            if (child is DyingFrame dying) return dying.ParentFrameId == f.Id && dying.VictimSeat == r.DamageSourceSeat && dying.Continuation == DyingContinuationKind.ProgramSkill;
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.Program && hp.Change.Kind == HpChangeKind.Loss && hp.Change.TargetSeat == r.DamageSourceSeat && hp.Change.Amount == 1 && hp.Change.SourceSeat is null;
        }
        if (child is ProgramLifecycleTriggerWindowFrame lifecycle && lifecycle.ResumeProgramFrameId == f.Id)
            return lifecycle.Window == SkillProgramTriggerWindow.SkillsChanged && lifecycle.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
                r.Stage == NiuFuStage.SourceTurnChildren && lifecycle.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && lifecycle.CharacterStateContinuation == CharacterStateContinuation.Program && lifecycle.OwnerSeat == r.DamageSourceSeat &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == lifecycle.Id && e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == r.DamageSourceSeat && e.Change.Window == lifecycle.Window);
        if (f.PendingMovementContinuation is null || r.Steps.Count == 0) return false; var step = r.Steps[^1];
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > step.Before && m.Sequence <= step.After);
        if (child is RecoveryReplacementFrame recovery) return IsNiuFuRecoveryParent(f, recovery);
        return child is HpChangedTriggerWindowFrame recoveryHp && recoveryHp.Change.ParentFrameId == f.Id && recoveryHp.ResumeFrameId == f.Id && recoveryHp.Continuation == PostEventContinuation.AwaitedProgramMovement && recoveryHp.Change.Kind == HpChangeKind.Recovery &&
            recoveryHp.Change.Amount == 1 && recoveryHp.Change.SourceSeat == recoveryHp.Change.TargetSeat &&
            _cardMovements.Any(m => m.Sequence > step.Before && m.Sequence <= step.After && m.From == CardLocation.Equipment(recoveryHp.Change.TargetSeat) && m.CardKind == CardKind.SilverLion);
    }
    private ProgramSkillFrame? NiuFuObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.NiuFuFirstChild(f, c), static (e, i) => e.SameNameHandObserverSuffix(i));
    private bool HasNiuFuDamageObserver(long id) => NiuFuObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasNiuFuBeforeDamageObserver(long id) => NiuFuObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is BeforeDamageProgramWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsNiuFuDying() => ActiveDying is { } dying && NiuFuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsNiuFuNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || NiuFuObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target ==
            (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27050)]
    private bool AdvanceNiuFuSubtree() => _pendingDecision is null && NiuFuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
