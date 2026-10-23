namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 44700)]
    private void RequireAnniversaryFuPair(ProgramSkillFrame f)
    {
        if (f.AnniversaryFuRongFuQian is not { } r) return;
        var index = _resolutionStack.FindIndex(p => p.Id == f.Id);
        if (f.WindowContext is not { } context || r.Source != AnniversaryFuPairSource(f) || r.Hash != f.GameplayHash ||
            r.ActualTurn != _turnNumber || r.ParentId != context.ParentFrameId || index < 1 || _resolutionStack[index - 1].Id != r.ParentId ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !Enum.IsDefined(r.Stage) ||
            !AnniversaryFuRongFuQianComposition.Handles(r.Operation) || GetPausedPrivateOfferEffect(f)?.Op != r.Operation ||
            r.Targets.Count != r.Targets.Distinct().Count() || r.Targets.Any(s => !IsValidPlayerSeat(s)) || r.Cursor < 0 || r.Cursor > r.Targets.Count ||
            ProgramEventHistory<AnniversaryFuRongFuQianStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started != new AnniversaryFuRongFuQianStartedEvent(f.Id, r.Source, r.Hash, r.Operation, r.ParentId, r.ActualTurn,
                r.Grant?.FrameId ?? 0, r.ProtectedSeat, r.PreventedAmount))
            throw new InvalidOperationException("Fu Rong Fu Qian lost his original source, native parent, actual turn or owning instruction.");
        if (r.Operation == SkillProgramEffectOp.ArmAnniversaryFuRongFuQianXuewei)
        {
            if (r.Stage != AnniversaryFuRongFuQianStage.ArmChoice || r.Grant is not null || r.Targets.Count != 0 || r.Draws.Count != 0 ||
                _resolutionStack[index - 1] is not TurnEndingBoundaryFrame ending || ending.OwnerSeat != f.OwnerSeat ||
                ending.ItemIndex < 0 || ending.ItemIndex >= ending.Items.Count || ending.Items[ending.ItemIndex].Candidate is not { } candidate ||
                !MountObserverCandidateMatches(f, candidate))
                throw new InvalidOperationException("Xuewei lost its owner's actual ending or unpaid target choice.");
            return;
        }
        if (r.Operation == SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei)
        {
            if (_resolutionStack[index - 1] is not BeforeDamageProgramWindowFrame window || AnniversaryXueweiWitness(AnniversaryFuPairCandidate(f), context, true) != r.Grant ||
                r.Grant is null || r.ProtectedSeat != window.TargetSeat || r.PreventedAmount != window.Amount || r.PreventedAmount < 1 ||
                r.Stage is AnniversaryFuRongFuQianStage.ArmChoice or AnniversaryFuRongFuQianStage.MaximumChildren or AnniversaryFuRongFuQianStage.TargetsChoice ||
                !window.Prevented || ProgramEventHistory<ProgramDamagePreventedEvent>().Count(e => e.FrameId == window.Id && e.SkillId == f.SkillId &&
                    e.BindingId == f.TriggerId && e.OwnerSeat == f.OwnerSeat && e.TargetSeat == r.ProtectedSeat && e.Amount == r.PreventedAmount && e.SourceSeat == context.SourceSeat) != 1)
                throw new InvalidOperationException("Xuewei lost its exact original whole-damage prevention and paid protection grant.");
            if (r.HpPaid && (r.ActualLoss != 1 || (long)r.HpAfter != (long)r.HpBefore - 1 ||
                ProgramEventHistory<AnniversaryXueweiHpPaidEvent>().Count(e => e == new AnniversaryXueweiHpPaidEvent(f.Id, r.HpBefore, r.HpAfter, 1)) != 1 ||
                ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.TargetSeat == f.OwnerSeat && e.Amount == 1 && e.RemainingHp == r.HpAfter) != 1) ||
                !r.HpPaid && r.Stage != AnniversaryFuRongFuQianStage.HpChildren ||
                r.Stage != AnniversaryFuRongFuQianStage.HpChildren && !r.Targets.SequenceEqual(new[] { f.OwnerSeat, r.ProtectedSeat }.Distinct()))
                throw new InvalidOperationException("Xuewei lost its single real HP-loss payment or distinct draw recipients.");
        }
        else
        {
            if (_resolutionStack[index - 1] is not DeferredTurnEndFrame ended || !IsActualAfterTurnEndedParent(ended) ||
                !MountObserverCandidateMatches(f, AfterTurnEndedCandidate(ended)) || r.Grant is not null || r.Stage is AnniversaryFuRongFuQianStage.ArmChoice or AnniversaryFuRongFuQianStage.HpChildren ||
                r.MaximumBefore <= 0 || r.MaximumAfter < 0 || r.HpAfterMaximum != Math.Min(r.HpBeforeMaximum, r.MaximumAfter) ||
                r.LostHp != Math.Max(0, r.MaximumAfter - r.HpAfterMaximum) || r.TargetCount < 0 || r.TargetCount > r.LostHp ||
                r.Targets.Count > r.TargetCount || ProgramEventHistory<AnniversaryYuguanMaximumPaidEvent>().Count(e => e == new AnniversaryYuguanMaximumPaidEvent(
                    f.Id, r.MaximumBefore, r.MaximumAfter, r.HpBeforeMaximum, r.HpAfterMaximum, r.LostHp, r.TargetCount)) != 1 ||
                !ProgramEventHistory<MaximumHpChangedEvent>().Any(e => e.PlayerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.MaximumHp == r.MaximumAfter && e.Delta == r.MaximumAfter - r.MaximumBefore) ||
                !ProgramEventHistory<AnniversaryFuRongFuQianTargetChosenEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.TargetSeat).SequenceEqual(r.Targets))
                throw new InvalidOperationException("Yuguan lost its actual turn end, maximum-HP payment, frozen post-payment lost HP or target order.");
        }
        if (r.Draws.Select(d => d.Index).Distinct().Count() != r.Draws.Count || r.Draws.Any(d => d.Index < 0 || d.Index >= r.Targets.Count || d.Index > r.Cursor ||
            d.RecipientSeat != r.Targets[d.Index] || d.Requested < 1 || d.Outcome.RecipientSeat != d.RecipientSeat || d.Outcome.RequestedCount != d.Requested ||
            d.Outcome.SequenceBefore < 0 || d.Outcome.SequenceAfter < d.Outcome.SequenceBefore || d.Outcome.SequenceAfter > _movementSequence ||
            ProgramEventHistory<AnniversaryFuRongFuQianDrawReturnedEvent>().Count(e => e == new AnniversaryFuRongFuQianDrawReturnedEvent(f.Id, d.Index, d.RecipientSeat,
                d.Requested, d.Outcome.ActualDrawIds.Count, d.Outcome.AttemptFrameId, d.Outcome.SequenceBefore, d.Outcome.SequenceAfter)) != 1 ||
            d.Outcome.ActualDrawIds.Any(id => _cardMovements.Count(m => m.CardId == id && m.Sequence > d.Outcome.SequenceBefore && m.Sequence <= d.Outcome.SequenceAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(d.RecipientSeat) && m.Reason == AnniversaryFuPairDrawReason(r.Operation)) != 1)))
            throw new InvalidOperationException("Fu Rong Fu Qian lost an exact typed native draw or its actual physical materials.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 44700)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 44700)]
    private bool KeepPaidAnniversaryFuPair(ProgramSkillFrame f)
    {
        if (f.AnniversaryFuRongFuQian is not null) { RequireAnniversaryFuPair(f); return true; }
        return f.InstructionIndex is 0 or 1 && f.WindowContext is { } w && AdmitAnniversaryXuewei(AnniversaryFuPairCandidate(f), w);
    }
    private bool HasAnniversaryFuRongFuQianPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } prompt &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { Step: ResolutionFrameStep.AwaitingResponse,
            AnniversaryFuRongFuQian.Stage: AnniversaryFuRongFuQianStage.ArmChoice or AnniversaryFuRongFuQianStage.TargetsChoice } f &&
        prompt.PlayerSeat == f.OwnerSeat && AssistedChoicesEqual(prompt.Choices, AnniversaryFuPairChoices(f));

    private bool AnniversaryFuPairFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryFuRongFuQian is not { } r) return false;
        RequireAnniversaryFuPair(f);
        if (r.Stage == AnniversaryFuRongFuQianStage.Drawing && NativeDrawAttemptRidesOn(child, f)) return true;
        if (child is DyingFrame dying) return r.Operation == SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei && r.HpPaid && r.HpAfter <= 0 &&
            r.Stage == AnniversaryFuRongFuQianStage.HpChildren && dying.ParentFrameId == f.Id && dying.VictimSeat == f.OwnerSeat && dying.KillerSeat is null && dying.ResumesProgramSkill;
        if (child is DeathFrame death) return r.Operation == SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianYuguan &&
            r.Stage == AnniversaryFuRongFuQianStage.MaximumChildren && r.MaximumAfter == 0 && death.ParentFrameId == f.Id && death.VictimSeat == f.OwnerSeat && death.ReturnKind == DeathReturnKind.ProgramSkill;
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.Program &&
            hp.Change.TargetSeat == f.OwnerSeat && hp.Change.SourceSeat is null &&
            (r.Stage == AnniversaryFuRongFuQianStage.HpChildren && r.HpPaid && hp.Change.Kind == HpChangeKind.Loss && hp.Change.Amount == 1 && hp.Change.HpAfter == r.HpAfter ||
             r.Stage == AnniversaryFuRongFuQianStage.MaximumChildren && hp.Change.Kind == HpChangeKind.MaximumHp && hp.Change.Amount == 1 && hp.Change.HpAfter == r.HpAfterMaximum);
        if (child is CardsMovedTriggerWindowFrame moved) return r.Stage == AnniversaryFuRongFuQianStage.DrawChildren &&
            moved.Batch.ParentFrameId == f.Id && (moved.ResumeProgramFrameId == f.Id || moved.Batch.AwaitingProgramFrameId == f.Id) &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && r.Draws.Any(d =>
                m.Sequence > d.Outcome.SequenceBefore && m.Sequence <= d.Outcome.SequenceAfter));
        return child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId == f.Id &&
            skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? AnniversaryFuPairObserverRoot() => FindProgramObserverRoot(
        static (e, root, child) => e.AnniversaryFuPairFirstChild(root, child), static (e, index) => e.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 44700)]
    private bool HasAnniversaryFuPairBeforeDamageObserver(long id) => AnniversaryFuPairObserverRoot() is { AnniversaryFuRongFuQian.Operation: SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei } root &&
        root.WindowContext?.ParentFrameId == id && _resolutionStack.Any(f => f.Id == id && f is BeforeDamageProgramWindowFrame);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 44700, group: 10)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 44700)]
    private bool HasAnniversaryFuPairDying() => ActiveDying is { } dying && AnniversaryFuPairObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 44700)]
    private bool AdvanceAnniversaryFuPairObserver() => _pendingDecision is null && AnniversaryFuPairObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
