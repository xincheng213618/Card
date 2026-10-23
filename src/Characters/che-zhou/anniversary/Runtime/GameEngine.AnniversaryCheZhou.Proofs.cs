namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidCheZhou(ProgramSkillFrame f)
    {
        if (f.AnniversaryCheZhou is not { } r || !ExactCheZhouDamage(f) || r.Source != CheZhouSource(f) || r.Hash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || f.WindowContext is not { } c || r.WindowId != c.ParentFrameId ||
            r.DamageId != c.DamageFrameId || r.Victim != c.TargetSeat || r.Turn != _turnNumber || r.TurnSeat != _turnProgression.OwnerSeat ||
            r.Round <= 0 || r.Round != _roundNumber || !Enum.IsDefined(r.Stage) || r.Selected.Count > r.Round || r.Selected.Distinct().Count() != r.Selected.Count ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId &&
                e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.Window == c.Window) != 1 ||
            ProgramEventHistory<AnniversaryCheZhouPiguaStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start != new AnniversaryCheZhouPiguaStartedEvent(f.Id, r.Source, r.Hash, r.WindowId, r.DamageId, r.Turn, r.TurnSeat, r.Victim, r.Round) ||
            !ProgramEventHistory<AnniversaryCheZhouPiguaSelectedEvent>().Where(e => e.FrameId == f.Id).Select(e => e.Count).SequenceEqual(Enumerable.Range(1, r.Selected.Count))) return false;
        var paid = ProgramEventHistory<AnniversaryCheZhouPiguaPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == AnniversaryCheZhouStage.Selecting || r.Stage == AnniversaryCheZhouStage.Complete && r.Selected.Count == 0)
            return r.Before == 0 && r.After == 0 && r.BatchId == 0 && r.Moves.Count == 0 && paid.Length == 0 &&
                f.PendingMovementContinuation is null && r.Selected.All(id => _cardZones.GetLocation(id) == CardLocation.Hand(r.Victim));
        return r.Selected.Count > 0 && r.BatchId > 0 && r.Moves.Count == r.Selected.Count &&
            r.Moves.Select(m => m.CardId).Order().SequenceEqual(r.Selected.Order()) && paid is [var invoice] &&
            invoice == new AnniversaryCheZhouPiguaPaidEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnSeat, r.Victim, r.Moves.Count, r.BatchId, r.Before, r.After) &&
            r.Moves.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After && m.TurnNumber == r.Turn &&
                m.From == CardLocation.Hand(r.Victim) && m.Reason.Value == CheZhouObtainReason &&
                (m.To == CardLocation.Hand(f.OwnerSeat) || IsHappyWeiYanRedirectedGain(m, CardLocation.Hand(f.OwnerSeat))));
    }
    private bool IsAnniversaryCheZhouHandExempt(CharacterState owner, Card card)
    {
        if (!HasAnniversaryCheZhouContent || _cardZones.GetLocation(card.Id) != CardLocation.Hand(owner.Seat)) return false;
        foreach (var e in ProgramEventHistory<AnniversaryCheZhouPiguaPaidEvent>().Where(e => e.Source.OwnerSeat == owner.Seat &&
            e.Turn == _turnNumber && e.TurnSeat == _turnProgression.OwnerSeat && e.Source.SkillId == AnniversaryCheZhouComposition.Pigua &&
            e.Source.BindingId == AnniversaryCheZhouComposition.Binding && _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.Hash))
        {
            if (ProgramEventHistory<AnniversaryCheZhouPiguaStartedEvent>().Count(s => s.FrameId == e.FrameId && s.Source == e.Source &&
                s.Hash == e.Hash && s.Turn == e.Turn && s.TurnSeat == e.TurnSeat && s.Victim == e.Victim) != 1) continue;
            if (_cardMovements.Any(m => m.CardId == card.Id && m.Sequence > e.Before && m.Sequence <= e.After && m.TurnNumber == e.Turn &&
                m.From == CardLocation.Hand(e.Victim) && m.To == CardLocation.Hand(owner.Seat) && m.Reason.Value == CheZhouObtainReason)) return true;
        }
        return false;
    }
    private bool HasAnniversaryCheZhouPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { Step: ResolutionFrameStep.AwaitingResponse, AnniversaryCheZhou.Stage: AnniversaryCheZhouStage.Selecting } f &&
        p.PlayerSeat == f.OwnerSeat && ValidCheZhou(f) && AssistedChoicesEqual(p.Choices, CheZhouChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 46400)] private void AssertCheZhou(ProgramSkillFrame f)
    {
        if (f.AnniversaryCheZhou is null) return;
        if (!ValidCheZhou(f) || f.PendingMovementContinuation is { } p && !CheZhouMovement(f, GetPausedPrivateOfferEffect(f), p))
            throw new InvalidOperationException("Pigua lost its exact damage, immutable Hand draft or original paid movement.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 46400)]
    private bool CheZhouMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.AnniversaryCheZhou is { Stage: AnniversaryCheZhouStage.MovementChildren } && e?.Op == SkillProgramEffectOp.ResolveAnniversaryCheZhouPigua &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && ValidCheZhou(f);
    private bool CheZhouFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryCheZhou is not { Stage: AnniversaryCheZhouStage.MovementChildren } r || !ValidCheZhou(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => r.Moves.Contains(m));
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills &&
            skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? CheZhouObserverRoot() => FindProgramObserverRoot(static (engine, root, child) => engine.CheZhouFirstChild(root, child),
        static (engine, index) => engine.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 46400)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 46400)]
    private bool CheZhouDamageObserver(long id) => CheZhouObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 46400)] private bool CheZhouBeforeDamageObserver(long id) =>
        CheZhouObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 46400)] private bool CheZhouDyingObserver() =>
        ActiveDying is { } dying && CheZhouObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 46400)] private bool AdvanceCheZhouSubtree() =>
        _pendingDecision is null && CheZhouObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
