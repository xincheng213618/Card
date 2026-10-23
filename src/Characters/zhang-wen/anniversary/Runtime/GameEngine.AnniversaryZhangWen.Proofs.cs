namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidZhangWenSibian(ProgramSkillFrame f)
    {
        if (f.ZhangWenSibian is not { } r || f.SkillId != AnniversaryZhangWenComposition.Sibian || f.TriggerId != AnniversaryZhangWenComposition.Replacement ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.Source != ZhangWenSource(f) || r.Hash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.Hash || r.Turn != _turnNumber || !Enum.IsDefined(r.Stage) ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.DrawPhaseStarting } context || context.ParentFrameId != r.ParentId ||
            _resolutionStack.SingleOrDefault(p => p.Id == r.ParentId) is not ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.DrawPhaseStarting } parent ||
            parent.OwnerSeat != f.OwnerSeat || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) ||
            ProgramEventHistory<ZhangWenSibianStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started != new ZhangWenSibianStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn) || r.Entities.Count > 4 ||
            r.Entities.Select(e => e.CardId).Distinct().Count() != r.Entities.Count || r.Extrema.Distinct().Count() != r.Extrema.Count) return false;
        var revealed = ProgramEventHistory<ZhangWenSibianEntityEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (revealed.Length != r.Entities.Count || revealed.Where((e, i) => e.Index != i || e.Entity != r.Entities[i] || e.Extreme != r.Extrema.Contains(e.Entity.CardId)).Any() ||
            r.Entities.Any(e => e.RevealSequence <= 0 || !_cardMovements.Any(m => m.Sequence == e.RevealSequence && m.CardId == e.CardId && m.CardKind == e.Kind && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing))) return false;
        var expected = r.Entities.Count == 0 ? [] : r.Entities.Where(e => e.Rank == r.Entities.Min(x => x.Rank) || e.Rank == r.Entities.Max(x => x.Rank)).Select(e => e.CardId).ToArray();
        if (!expected.SequenceEqual(r.Extrema)) return false;
        var recordedSteps = ProgramEventHistory<ZhangWenSibianStepEvent>().Where(e => e.FrameId == f.Id).Select(e => e.Step).ToArray();
        if (!recordedSteps.SequenceEqual(r.Steps) || r.Steps.Count is < 1 or > 3 || r.Steps.Select(s => s.Stage).Distinct().Count() != r.Steps.Count ||
            r.Steps[0].Stage != ZhangWenSibianStage.RevealChildren ||
            r.Steps.Where((s, i) => s.Before < 0 || s.After < s.Before || s.After > _movementSequence || i > 0 && s.Before < r.Steps[i - 1].After).Any()) return false;
        foreach (var step in r.Steps)
        {
            var moves = _cardMovements.Where(m => m.Sequence > step.Before && m.Sequence <= step.After).ToArray();
            if (step.Stage == ZhangWenSibianStage.RevealChildren)
            {
                if (r.Entities.Any(e => e.RevealSequence <= step.Before || e.RevealSequence > step.After)) return false;
            }
            else if (step.Stage == ZhangWenSibianStage.GainChildren)
            {
                if (moves.Where(m => m.Reason == ZhangWenGain).Any(m => !r.Extrema.Contains(m.CardId) || m.From != CardLocation.Processing ||
                    m.To != CardLocation.Hand(f.OwnerSeat) && m.To != CardLocation.OutsideGame && !IsHappyWeiYanRedirectedGain(m, CardLocation.Hand(f.OwnerSeat)))) return false;
            }
            else if (step.Stage == ZhangWenSibianStage.FinishChildren)
            {
                var target = r.Recipient is { } seat && seat >= 0 ? CardLocation.Hand(seat) : CardLocation.DiscardPile;
                if (moves.Where(m => m.Reason == ZhangWenGift || m.Reason == ZhangWenCleanup).Any(m => !r.Entities.Any(e => e.CardId == m.CardId) ||
                    m.From != CardLocation.Processing || m.To != target && m.To != CardLocation.OutsideGame && !IsHappyWeiYanRedirectedGain(m, target))) return false;
            }
            else return false;
        }
        var choices = ProgramEventHistory<ZhangWenSibianRecipientEvent>().Where(e => e.FrameId == f.Id).ToArray();
        return r.Recipient is null ? choices.Length == 0 : choices is [var choice] && choice.Recipient == r.Recipient && (choice.Recipient == -1 || IsValidPlayerSeat(choice.Recipient));
    }
    private void RequireZhangWenSibian(ProgramSkillFrame f)
    { if (!ValidZhangWenSibian(f)) throw new InvalidOperationException("Sibian lost its original draw parent, public top-four entities, extrema or once-paid movement return."); }
    private bool HasAnniversaryZhangWenPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { ZhangWenSibian: { Stage: ZhangWenSibianStage.Recipient } } f &&
        f.Step == ResolutionFrameStep.AwaitingResponse && p.PlayerSeat == f.OwnerSeat && ValidZhangWenSibian(f) && AssistedChoicesEqual(p.Choices, ZhangWenSibianChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 45100)] private void AssertZhangWen(ProgramSkillFrame f)
    {
        if (f.ZhangWenSibian is not null)
        {
            RequireZhangWenSibian(f);
            if (f.PendingMovementContinuation is { } pending && !ZhangWenMovement(f, GetPausedPrivateOfferEffect(f), pending)) throw new InvalidOperationException("Sibian lost its exact movement cursor.");
            if (_resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is { } p && f.ZhangWenSibian.Stage == ZhangWenSibianStage.Recipient &&
                (p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, ZhangWenSibianChoices(f)))) throw new InvalidOperationException("Sibian's real minimum-hand recipient menu changed.");
        }
        if (f.ZhangWenSongshu is { } r && (r.Source != ZhangWenSource(f) || r.Hash != f.GameplayHash || f.SkillId != AnniversaryZhangWenComposition.Songshu ||
            ProgramEventHistory<ZhangWenSongshuAcceptedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.Hash == r.Hash && e.Turn == r.Turn && e.PlayPhase == r.PlayPhase && e.UsageGroup == r.UsageGroup && e.TurnUses == r.TurnUses && e.PhaseUses == r.PhaseUses) != 1 ||
            ProgramEventHistory<ZhangWenSongshuRefundedEvent>().Count(e => e.FrameId == f.Id) != (r.Refunded ? 1 : 0))) throw new InvalidOperationException("Songshu lost its accepted original Play debit.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 45100)] private bool ZhangWenMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.ZhangWenSibian is { Stage: ZhangWenSibianStage.RevealChildren or ZhangWenSibianStage.GainChildren or ZhangWenSibianStage.FinishChildren } && ValidZhangWenSibian(f) &&
        e?.Op == SkillProgramEffectOp.ResolveAnniversaryZhangWenSibian && f.PendingMovementContinuation == p && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool ZhangWenFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.ZhangWenSibian is not { } r || f.PendingMovementContinuation is null || !ValidZhangWenSibian(f) || r.Steps.Count == 0) return false;
        var step = r.Steps[^1];
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.Movements.Count > 0 &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > step.Before && m.Sequence <= step.After);
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? ZhangWenObserverRoot() => FindProgramObserverRoot(static (engine, root, child) => engine.ZhangWenFirstChild(root, child), static (engine, index) => engine.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 45100)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 45100)]
    private bool ZhangWenDamageObserver(long id) => ZhangWenObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 45100)] private bool ZhangWenBeforeDamageObserver(long id) => ZhangWenObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 45100)] private bool ZhangWenDyingObserver() => ActiveDying is { } dying && ZhangWenObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 45100)] private bool AdvanceZhangWenSubtree() => _pendingDecision is null && ZhangWenObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 45100)]
    private bool ZhangWenNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || ZhangWenObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
}
