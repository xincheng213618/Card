namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27070)]
    private void AssertMaMiDi(ProgramSkillFrame f)
    {
        if (f.MaMiDi is not { } r) return;
        if (r.Source != MaMiDiSource(f) || r.GameplayHash != f.GameplayHash || r.Instruction != 1 || f.InstructionIndex != 1 || f.TriggerId is null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            r.Turn != _turnNumber || r.TurnOwner != _currentSeat || !Enum.IsDefined(r.Stage) || GetPausedPrivateOfferEffect(f)?.Op != r.Op || !MaMiDiComposition.Handles(r.Op) || f.WindowContext is not { } c || c.ParentFrameId != r.WindowId ||
            ProgramEventHistory<MaMiDiStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started != new MaMiDiStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.WindowId, r.Turn, r.TurnOwner, r.UseId, r.ActionId, r.Grants.Count, r.Targets.Count))
            throw new InvalidOperationException("Ma Mi Di lost its exact source, owning native instruction, actual turn or immutable start invoice.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex);
        if (r.Op != SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards)
        {
            if (r.Stage != MaMiDiStage.MaximumChildren || f.PendingMovementContinuation is not null || r.Grants.Count != 0 || r.Targets.Count != 0 || r.Steps.Count != 0 || r.UseId is not null ||
                r.MaximumBefore < 1 || r.MaximumAfter != r.MaximumBefore + (r.Op == SkillProgramEffectOp.PayBingjieTurnMaximumHp ? -1 : 1) || r.MaximumAfter < 0 || r.Op == SkillProgramEffectOp.GrowZhengdingMatchingResponse && r.MaximumAfter < 1 || r.HpAfter != Math.Min(r.HpBefore, r.MaximumAfter) ||
                ProgramEventHistory<MaMiDiMaximumIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var maximum] || maximum != new MaMiDiMaximumIssuedEvent(f.Id, r.Op, f.OwnerSeat, r.MaximumBefore, r.MaximumAfter, r.HpBefore, r.HpAfter) ||
                !ProgramEventHistory<MaximumHpChangedEvent>().Any(e => e.PlayerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.MaximumHp == r.MaximumAfter && e.Delta == r.MaximumAfter - r.MaximumBefore) ||
                (r.Op == SkillProgramEffectOp.PayBingjieTurnMaximumHp ? r.Response is not null || r.ActionId is not null || !ExactMaMiDiPlay(candidate, c) :
                    r.Response is not { } response || r.ActionId != response.ResponseActionId || !ProgramEventHistory<MaMiDiResponseLinkedEvent>().Contains(response) || response.ResponseActor != f.OwnerSeat || response.ResponseIsRed is not { } red || response.IncomingIsRed != red || response.TurnOwner == f.OwnerSeat ||
                    _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.WindowId) is not { ResponseCompletion: { CostsDrained: true } } w || !ValidResponseCompletionWindow(w) || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, ToSharedCandidate(w.Candidates[w.CandidateIndex]))))
                throw new InvalidOperationException("Bingjie/Zhengding lost its once-paid native maximum-HP mutation or exact response/color pair.");
            return;
        }
        if (r.Response is not null || r.UseId is not { } useId || LifecycleCardUse(useId) is not { MaMiDiBingjie: { Issued: true } useReceipt } use || useReceipt.ActionId != r.ActionId || useReceipt.ActorSeat != f.OwnerSeat || useReceipt.UseId != useId ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.WindowId) is not { } window || window.ParentFrameId != useId || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
            !r.Grants.SequenceEqual(useReceipt.Grants.Where(g => g.Source.SkillId == f.SkillId && g.Source.SkillInstanceId == f.SkillInstanceId)) || r.Grants.Count == 0 ||
            r.Targets.Any(s => s == f.OwnerSeat || !IsValidPlayerSeat(s)) || r.Targets.Distinct().Count() != r.Targets.Count || r.Cursor < 0 || r.Cursor > r.Grants.Count * r.Targets.Count ||
            !ProgramEventHistory<MaMiDiFrozenTargetEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.TargetSeat).SequenceEqual(r.Targets) ||
            !ProgramEventHistory<MaMiDiFrozenGrantEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.GrantFrameId).SequenceEqual(r.Grants.Select(g => g.FrameId)) ||
            r.Grants.Any(g => !ProgramEventHistory<BingjieGrantedEvent>().Contains(g) || g.Turn != r.Turn || g.TurnOwner != r.TurnOwner || g.Source.OwnerSeat != f.OwnerSeat || g.GameplayHash != r.GameplayHash ||
                ProgramEventHistory<BingjieUseBoundEvent>().Count(e => e.UseId == useId && e.ActionId == r.ActionId && e.ActorSeat == f.OwnerSeat && e.GrantFrameId == g.FrameId && e.Turn == r.Turn && e.TurnOwner == r.TurnOwner) != 1))
            throw new InvalidOperationException("Bingjie lost its original accepted Use, issued grants or frozen actual target order.");
        if (r.Stage == MaMiDiStage.TargetChildren ? f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } p || p.SubjectSeat != MaMiDiCurrentTarget(r).Target || r.Steps.Count == 0 || r.Steps[^1].Index != r.Cursor : f.PendingMovementContinuation is not null)
            throw new InvalidOperationException("Bingjie lost its single owning target-payment continuation.");
        var issued = ProgramEventHistory<MaMiDiDiscardIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (issued.Length != r.Steps.Count || r.Steps.Select(s => s.Index).Distinct().Count() != r.Steps.Count) throw new InvalidOperationException("Bingjie cannot pay the same target/grant twice.");
        for (var i = 0; i < r.Steps.Count; i++)
        {
            var step = r.Steps[i];
            if (step.Index < 0 || step.Index >= r.Grants.Count * r.Targets.Count || step.GrantFrameId != r.Grants[step.Index / r.Targets.Count].FrameId || step.TargetSeat != r.Targets[step.Index % r.Targets.Count] ||
                step.Before < 0 || step.After <= step.Before || step.After > _movementSequence || i > 0 && step.Before < r.Steps[i - 1].After ||
                issued[i] != new MaMiDiDiscardIssuedEvent(f.Id, step.Index, step.GrantFrameId, step.TargetSeat, step.CardId, step.Kind, step.From, step.To, step.Before, step.After) ||
                step.From.OwnerSeat != step.TargetSeat || step.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || step.To != (step.Kind == CardKind.GeneralWeapon && step.From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile) ||
                _cardMovements.Count(m => m.Sequence > step.Before && m.Sequence <= step.After && m.CardId == step.CardId && m.CardKind == step.Kind && m.From == step.From && m.To == step.To && m.Reason.Value == MaMiDiDiscardReason) != 1 ||
                _cardMovements.Where(m => m.Sequence > step.Before && m.Sequence <= step.After).Any(m => !IsMaMiDiStepMovement(step, m)))
                throw new InvalidOperationException("Bingjie lost a real target-owned HE discard or exact native Wooden Ox spill.");
        }
    }
    private bool IsMaMiDiStepMovement(MaMiDiDiscardStep step, CardMovementRecord m) => _cardMovements.Contains(m) && m.Sequence > step.Before && m.Sequence <= step.After &&
        (m.CardId == step.CardId && m.CardKind == step.Kind && m.From == step.From && m.To == step.To && m.Reason.Value == MaMiDiDiscardReason ||
         step.Kind == CardKind.WoodenOx && step.From == CardLocation.Equipment(step.TargetSeat) && step.WoodenOxGrain.Contains(m.CardId) && m.From == CardLocation.WoodenOxGrain(step.TargetSeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard);
    private bool IsMaMiDiMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.MaMiDi is { Stage: MaMiDiStage.TargetChildren } r && e?.Op == r.Op &&
        p == f.PendingMovementContinuation && p.BeforeCount == 0 && p.CoverageResultBind is null && p.SubjectSeat == MaMiDiCurrentTarget(r).Target;
    private bool MaMiDiLion(ProgramSkillFrame f) => f.MaMiDi is { Stage: MaMiDiStage.TargetChildren, Steps.Count: > 0 } r && r.Steps[^1] is { Kind: CardKind.SilverLion, From.Zone: CardZoneKind.Equipment };
    private bool IsMaMiDiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => MaMiDiLion(f) && RecoveryReplacementFrameRidesOn(recovery, f) &&
        recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == f.MaMiDi!.Steps[^1].TargetSeat && recovery.Attempt.TargetSeat == recovery.Attempt.SourceSeat && recovery.Attempt.Amount == 1 &&
        recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == MaMiDiDiscardReason;
    private bool MaMiDiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MaMiDi is not { } r) return false; AssertMaMiDi(f);
        if (child is DeathFrame death) return r.Op == SkillProgramEffectOp.PayBingjieTurnMaximumHp && r.Stage == MaMiDiStage.MaximumChildren && r.MaximumBefore == 1 && r.MaximumAfter == 0 && r.HpAfter <= 0 &&
            death.ParentFrameId == f.Id && death.VictimSeat == f.OwnerSeat && death.KillerSeat is null && death.ReturnKind == DeathReturnKind.ProgramSkill &&
            ProgramEventHistory<MaMiDiMaximumIssuedEvent>().Count(e => e.FrameId == f.Id && e.Op == r.Op && e.MaximumBefore == 1 && e.MaximumAfter == 0) == 1;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && (r.Stage == MaMiDiStage.MaximumChildren ?
                hp.Continuation == PostEventContinuation.Program && hp.Change.Kind == HpChangeKind.MaximumHp && hp.Change.SourceSeat is null && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Amount == 1 && hp.Change.HpAfter == r.HpAfter :
                hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount == 1 && hp.Change.SourceSeat == r.Steps[^1].TargetSeat && hp.Change.TargetSeat == hp.Change.SourceSeat && MaMiDiLion(f));
        if (r.Stage != MaMiDiStage.TargetChildren || f.PendingMovementContinuation is null || r.Steps.Count == 0) return false;
        if (child is RecoveryReplacementFrame recovery) return IsMaMiDiRecoveryParent(f, recovery);
        return child is CardsMovedTriggerWindowFrame moved && (moved.ResumeProgramFrameId == f.Id || moved.Batch.AwaitingProgramFrameId == f.Id) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => IsMaMiDiStepMovement(r.Steps[^1], m));
    }
    private ProgramSkillFrame? MaMiDiObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.MaMiDiFirstChild(f, child), static (e, i) => e.SameNameHandObserverSuffix(i));
    private bool HasMaMiDiDamageObserver(long id) => MaMiDiObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasMaMiDiBeforeDamageObserver(long id) => MaMiDiObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is BeforeDamageProgramWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsMaMiDiDying() => ActiveDying is { } dying && MaMiDiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsMaMiDiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MaMiDiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27070)] private bool AdvanceMaMiDiSubtree() => _pendingDecision is null && MaMiDiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
