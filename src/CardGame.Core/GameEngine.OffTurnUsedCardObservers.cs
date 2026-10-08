namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanContinueOffTurnUsedCardGift(ProgramSkillFrame frame)
    {
        if (frame.OffTurnUsedCardGift is not { Stage: OffTurnUsedCardGiftStage.GiftChildren }) return false;
        AssertOffTurnUsedCardGift(frame);
        return true;
    }

    private bool IsOffTurnUsedCardGiftMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.GiveOffTurnUsedCards && pending.SubjectSeat == frame.OwnerSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && CanContinueOffTurnUsedCardGift(frame);

    private bool OffTurnUsedCardGiftFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.OffTurnUsedCardGift is { Stage: OffTurnUsedCardGiftStage.GiftChildren } &&
            OffTurnUsedCardGiftSkillsChangedEdge(root, child)) return true;
        if (root.OffTurnUsedCardGift is not { Stage: OffTurnUsedCardGiftStage.GiftChildren } r ||
            child is not CardsMovedTriggerWindowFrame movement || movement.Batch.Id != r.GiftBatchId ||
            movement.Batch.ParentFrameId != root.Id || movement.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
            movement.Batch.OriginSkillId != root.SkillId || movement.Batch.OriginSkillInstanceId != root.SkillInstanceId ||
            movement.Batch.OriginOwnerSeat != root.OwnerSeat || r.PaidCardIds is null || r.RecipientSeat is null ||
            !movement.Batch.Movements.Select(m => m.CardId).SequenceEqual(r.PaidCardIds) ||
            movement.Batch.Movements.Any(m => !_cardMovements.Contains(m) || m.Sequence <= r.Before || m.Sequence > r.After ||
                m.From != CardLocation.DiscardPile || m.To != CardLocation.Hand(r.RecipientSeat.Value) ||
                m.Reason.Value != $"skill-program.{root.SkillId}.off-turn-used-card-gift")) return false;
        return true;
    }

    // This predicate is used only below an already validated paid gift root.
    // A real revision observer can belong to any changed player, but its return
    // and active candidate must still name the exact native parent and instance.
    private bool OffTurnUsedCardGiftSkillsChangedEdge(ResolutionFrame parent, ResolutionFrame child)
    {
        if (parent is ProgramSkillFrame program && child is ProgramLifecycleTriggerWindowFrame window)
            return window.Window == SkillProgramTriggerWindow.SkillsChanged &&
                window.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                window.ResumeProgramFrameId == program.Id && IsValidPlayerSeat(window.OwnerSeat) &&
                window.Candidates.Count > 0 && window.CandidateIndex >= 0 && window.CandidateIndex <= window.Candidates.Count &&
                window.Candidates.All(c => c.OwnerSeat == window.OwnerSeat &&
                    GetProgramTrigger(c).Window == SkillProgramTriggerWindow.SkillsChanged);
        return parent is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged &&
            skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
            skills.CandidateIndex >= 0 && skills.CandidateIndex < skills.Candidates.Count &&
            child is ProgramSkillFrame observer && observer.WindowContext is { Window: SkillProgramTriggerWindow.SkillsChanged } context &&
            context.ParentFrameId == skills.Id && context.OwnerSeat == observer.OwnerSeat &&
            context.SourceSeat == skills.OwnerSeat && context.TargetSeat == skills.OwnerSeat &&
            MountObserverCandidateMatches(observer, skills.Candidates[skills.CandidateIndex]) &&
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == observer.Id &&
                e.OwnerSeat == observer.OwnerSeat && e.SkillId == observer.SkillId && e.BindingId == observer.TriggerId &&
                e.SkillInstanceId == observer.SkillInstanceId && e.Window == context.Window) == 1;
    }

    private bool OffTurnUsedCardGiftStructuralEdge(ResolutionFrame parent, ResolutionFrame child)
    {
        if (parent is ProgramSkillFrame root && root.OffTurnUsedCardGift is not null)
        {
            AssertOffTurnUsedCardGift(root);
            return OffTurnUsedCardGiftFirstChild(root, child);
        }
        if (child is not ProgramSkillFrame observer || observer.OffTurnUsedCardGift is not { } r ||
            parent is not CardsMovedTriggerWindowFrame window || window.Id != r.WindowFrameId ||
            window.Batch.Id != r.AnchorBatchId) return false;
        AssertOffTurnUsedCardGift(observer);
        return true;
    }

    private ProgramSkillFrame? OffTurnUsedCardGiftObserverRoot(long id)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == id);
        if (index < 0 || _resolutionStack[index] is not ProgramSkillFrame root || root.OffTurnUsedCardGift is not { } r) return null;
        AssertOffTurnUsedCardGift(root);
        if (index == _resolutionStack.Count - 1) return root;
        if (!OffTurnUsedCardGiftFirstChild(root, _resolutionStack[index + 1])) return null;
        for (var child = index + 2; child < _resolutionStack.Count; child++)
        {
            if (!OffTurnUsedCardGiftSkillsChangedEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !HalfHandPaidDamageObserverEdge(child)) return null;
            if (_resolutionStack[child] is DyingFrame dying && (IsPaidHandRepaymentRescueRide(child, dying) ||
                IsPaidHandRepaymentProgramAlcoholRide(child, dying) || PolicyCounterspellVirtualAlcoholRide(child, dying) ||
                PaidObserverDamageVirtualAlcoholRide(child, dying))) break;
        }
        return root;
    }

    private bool IsOffTurnUsedCardGiftProgramDying() => ActiveDying is { } dying &&
        _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.OffTurnUsedCardGift is not null &&
            OffTurnUsedCardGiftObserverRoot(f.Id) is not null &&
            _resolutionStack.FindIndex(x => x.Id == dying.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id));

    private bool HasOffTurnUsedCardGiftDamageObserver(long windowId) => OffTurnUsedCardGiftDamageObserver(windowId, false);
    private bool HasOffTurnUsedCardGiftBeforeDamageObserver(long windowId) => OffTurnUsedCardGiftDamageObserver(windowId, true);

    private bool OffTurnUsedCardGiftDamageObserver(long windowId, bool before)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == windowId);
        if (index < 0) return false;
        if (before ? _resolutionStack[index] is not BeforeDamageProgramWindowFrame : _resolutionStack[index] is not DamageTriggerWindowFrame) return false;
        if (_resolutionStack.Take(index).OfType<ProgramSkillFrame>().Any(f => f.OffTurnUsedCardGift is not null &&
            OffTurnUsedCardGiftObserverRoot(f.Id) is not null)) return true;
        ProgramTriggerCandidate? candidate = _resolutionStack[index] switch
        {
            BeforeDamageProgramWindowFrame b when b.CandidateIndex >= 0 && b.CandidateIndex < b.Candidates.Count => b.Candidates[b.CandidateIndex].Candidate,
            DamageTriggerWindowFrame d when d.CandidateIndex >= 0 && d.CandidateIndex < d.Candidates.Count => d.Candidates[d.CandidateIndex].ToProgramCandidate(),
            _ => null
        };
        if (candidate is null || index + 1 >= _resolutionStack.Count || _resolutionStack[index + 1] is not ProgramSkillFrame producer ||
            producer.WindowContext?.ParentFrameId != windowId || !MountObserverCandidateMatches(producer, candidate)) return false;
        foreach (var root in _resolutionStack.Skip(index + 2).OfType<ProgramSkillFrame>().Where(f => f.OffTurnUsedCardGift is not null))
        {
            if (OffTurnUsedCardGiftObserverRoot(root.Id) is null) continue;
            var end = _resolutionStack.FindIndex(f => f.Id == root.Id); var okay = true;
            for (var edge = index + 2; edge <= end; edge++) if (!HalfHandPaidDamageObserverEdge(edge)) { okay = false; break; }
            if (okay) return true;
        }
        return false;
    }

    private bool AllowsOffTurnUsedCardGiftNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or
                SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) || CurrentDamageAttempt is not { } original) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.Damage || amount != effect.Amount || source != effect.ActorReference || nature != effect.DamageNature ||
            target != (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target))) return false;
        var originalIndex = _resolutionStack.FindIndex(f => f.Id == original.ResolutionId);
        return originalIndex >= 0 && _resolutionStack.Skip(originalIndex + 1).OfType<ProgramSkillFrame>()
            .Any(f => f.OffTurnUsedCardGift is not null && OffTurnUsedCardGiftObserverRoot(f.Id) is not null);
    }

    private void AssertOffTurnUsedCardGiftObservers()
    {
        foreach (var root in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OffTurnUsedCardGift is not null))
            if (OffTurnUsedCardGiftObserverRoot(root.Id) is null)
                throw new InvalidOperationException("Off-turn used-card gift lost its exact paid batch and native observer subtree.");
    }

    private sealed partial class ProgramSkillHost
    {
        public bool CanContinueOffTurnUsedCardGift(ProgramSkillFrame f) => engine.CanContinueOffTurnUsedCardGift(f);
    }
}
