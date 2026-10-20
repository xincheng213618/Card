namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26610)]
    private void AssertZhangShiPing(ProgramSkillFrame f)
    {
        if (f.ZhangShiPingHongji is { } h)
        {
            if (f.SkillId != "ol:hongji" || h.Source != ZhangShiPingSource(f) || h.GameplayHash != f.GameplayHash || h.InstructionIndex != f.InstructionIndex ||
                f.InstructionIndex != 1 || h.ActualTurn != _turnNumber || !IsValidPlayerSeat(h.RecipientSeat) || h.RecipientSeat != _currentSeat || !Enum.IsDefined(h.Stage) ||
                f.WindowContext is not { } context || h.ParentWindowId != context.ParentFrameId)
                throw new InvalidOperationException("Hongji lost its exact source, recipient, actual turn or owning lifecycle instruction.");
            if (h.Stage == HongjiStage.Choose)
            {
                if (context.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || !h.DrawEligible && !h.PlayEligible || h.Promise is not null ||
                    h.SuspendedSchedule is not null || ProgramEventHistory<ZhangShiPingHongjiGrantedEvent>().Any(e => e.PreparationParentId == h.ParentWindowId && e.Source.OwnerSeat == f.OwnerSeat))
                    throw new InvalidOperationException("Hongji cannot duplicate one source's choice in the same actual preparation.");
            }
            else if (h.Promise is not { } p || p.Source != h.Source || p.GameplayHash != h.GameplayHash || p.ActualTurn != h.ActualTurn || p.RecipientSeat != h.RecipientSeat ||
                p.Phase != h.Phase || h.Phase is not (TurnPhase.Draw or TurnPhase.Play) ||
                context.Window != (h.Phase == TurnPhase.Draw ? SkillProgramTriggerWindow.DrawPhaseEnded : SkillProgramTriggerWindow.PlayEnding) ||
                !ProgramEventHistory<ZhangShiPingHongjiGrantedEvent>().Contains(p) ||
                ProgramEventHistory<ZhangShiPingHongjiConsumedEvent>().Count(e => e.PromiseFrameId == p.FrameId && e.FrameId == f.Id && e.Source == h.Source &&
                    e.ActualTurn == h.ActualTurn && e.RecipientSeat == h.RecipientSeat && e.Phase == h.Phase && e.ParentWindowId == h.ParentWindowId) != 1 ||
                h.ReturnActualPhase is not { } actual || actual.TurnNumber != h.ActualTurn || actual.ActorSeat != h.RecipientSeat ||
                actual.Kind != (h.Phase == TurnPhase.Draw ? ActualDiscardRecoveryPhaseKind.Draw : ActualDiscardRecoveryPhaseKind.Play))
                throw new InvalidOperationException("Hongji lost its one consumed promise and exact native phase return identity.");
        }
        if (f.ZhangShiPingXingGu is { } r)
        {
            if (f.SkillId != "ol:xinggu" || r.Source != ZhangShiPingSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex ||
                f.InstructionIndex != 1 || r.ActualTurn != _turnNumber || !Enum.IsDefined(r.Stage) || r.PileLocation.Zone != CardZoneKind.PublicPersistentPile ||
                r.PileLocation.OwnerSeat != f.OwnerSeat || f.WindowContext is not { } context || r.ParentWindowId != context.ParentFrameId ||
                r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > _movementSequence ||
                ProgramEventHistory<ZhangShiPingXingGuStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                    e.ActualTurn == r.ActualTurn && e.ParentWindowId == r.ParentWindowId && e.PileLocation == r.PileLocation) != 1)
                throw new InvalidOperationException("Xinggu lost its exact public-pile source, actual lifecycle parent or physical movement boundary.");
            if (r.Stage == XingGuStage.InitialChildren && (context.Window != SkillProgramTriggerWindow.GameStarting || r.CardId is not null ||
                ProgramEventHistory<ZhangShiPingXingGuCardStoredEvent>().Where(e => e.FrameId == f.Id).Any(e =>
                    !_cardMovements.Any(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.From == CardLocation.DrawPile && m.To == r.PileLocation &&
                        m.Reason.Value == XingGuStoreReason && IsXingGuMount(GetAdvancedCard(e.CardId))))))
                throw new InvalidOperationException("Xinggu's initial pile must contain actual random draw-pile mounts.");
            if (r.CardId is { } cardId && (r.RecipientSeat is not { } target || target == f.OwnerSeat || !IsValidPlayerSeat(target) || !IsXingGuMount(GetAdvancedCard(cardId)) ||
                ProgramEventHistory<ZhangShiPingXingGuPlacementIssuedEvent>().Count(e => e.FrameId == f.Id && e.CardId == cardId && e.RecipientSeat == target) != 1))
                throw new InvalidOperationException("Xinggu lost its one paid physical mount and other recipient.");
            if (r.Stage == XingGuStage.DiamondChildren && ProgramEventHistory<ZhangShiPingXingGuDiamondIssuedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.CardId == r.DiamondCardId && e.SequenceBefore == r.SequenceBefore && e.SequenceAfter == r.SequenceAfter) != 1)
                throw new InvalidOperationException("Xinggu cannot replay or replace its paid random diamond acquisition.");
        }
    }
    private bool ZhangShiPingHasMovement(ProgramSkillFrame f, CardsMovedTriggerWindowFrame moved)
    {
        if (f.ZhangShiPingXingGu is not { } r || moved.ResumeProgramFrameId != f.Id || moved.Batch.ParentFrameId != f.Id || moved.Batch.OriginOwnerSeat != f.OwnerSeat ||
            moved.Batch.OriginSkillId != f.SkillId || moved.Batch.OriginSkillInstanceId != f.SkillInstanceId || moved.Batch.Movements.Count == 0) return false;
        return moved.Batch.Movements.All(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && _cardMovements.Contains(m) && (r.Stage switch
        {
            XingGuStage.InitialChildren => m.From == CardLocation.DrawPile && m.To == r.PileLocation && m.Reason.Value == XingGuStoreReason &&
                ProgramEventHistory<ZhangShiPingXingGuCardStoredEvent>().Any(e => e.FrameId == f.Id && e.CardId == m.CardId && e.MovementSequence == m.Sequence),
            XingGuStage.PlacementChildren => m.CardId == r.CardId && m.From == r.PileLocation && m.To == CardLocation.Equipment(r.RecipientSeat!.Value) && m.Reason == CardMoveReasons.EquipmentEnter ||
                m.From == CardLocation.Equipment(r.RecipientSeat!.Value) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.EquipmentReplace,
            XingGuStage.DiamondChildren => m.CardId == r.DiamondCardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == XingGuDiamondReason,
            _ => false
        }));
    }
    private bool ZhangShiPingFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.ZhangShiPingXingGu is not { } r) return false;
        AssertZhangShiPing(f);
        if (child is CardsMovedTriggerWindowFrame moved) return ZhangShiPingHasMovement(f, moved);
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills &&
            skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private bool ZhangShiPingObserverSuffix(int root) => HasObserverSuffix(root,
        static (e, i) => e.IsMovementObserverEdge(i) || e.RecoveryReplacementFrameRidesOn(e._resolutionStack[i], e._resolutionStack[i - 1]),
        static (e, i, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? ZhangShiPingObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.ZhangShiPingFirstChild(f, child), static (e, i) => e.ZhangShiPingObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 460)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 570)]
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 600)]
    private bool HasZhangShiPingDamageObserver(long id) => ZhangShiPingObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 430)]
    private bool IsZhangShiPingDying() => ActiveDying is { } dying && ZhangShiPingObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1070)]
    private bool AllowsZhangShiPingNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || ZhangShiPingObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26610)]
    private bool AdvanceZhangShiPingSubtree() => _pendingDecision is null && ZhangShiPingObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
