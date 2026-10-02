namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasCardEffectBeforeApplyPrograms => _contentRegistry.ProgramDependencies.HasTriggerWindow(SkillProgramTriggerWindow.CardEffectBeforeApply);
    private bool TryBeginOrdinaryCardEffectPrograms(NullificationWindowFrame pending)
    {
        if (!HasCardEffectBeforeApplyPrograms || IsDelayedCard(pending.EffectCardKind)) return false;
        var use = _resolutionStack.OfType<CardUseFrame>().Single(f => f.Id == pending.ParentFrameId);
        var action = use.Action ?? throw new InvalidOperationException("An actual trick effect needs its frozen action.");
        var designated = action.EffectiveKind == CardKind.BorrowedSword
            ? action.TargetSeats.Where((_,i) => i % 2 == 0).Distinct().ToArray()
            : action.EffectiveDesignatedTargetSeats.Distinct().ToArray();
        if (designated.Length != 1) return false;
        return BeginCardEffectPrograms(action,designated,pending,null);
    }
    private bool TryBeginDelayedCardEffectPrograms(CharacterState owner,Card card,CardKind kind)
    {
        if (!HasCardEffectBeforeApplyPrograms) return false;
        var parent = _resolutionStack.LastOrDefault();
        var continuation = new DelayedEffectContinuation(card.Id,kind,owner.Seat,_turnNumber,_phase,parent?.Id ?? 0,parent?.Kind,parent?.Step);
        // This describes the delayed effect boundary; it does not accept another card use.
        var action = new CardActionContext(0,null,CardActionType.Use,owner.Seat,owner.Seat,null,null,null,kind,[owner.Seat],[],[],[owner.Seat]);
        return BeginCardEffectPrograms(action,[owner.Seat],null,continuation);
    }
    private bool BeginCardEffectPrograms(CardActionContext action,IReadOnlyList<int> designated,NullificationWindowFrame? ordinary,DelayedEffectContinuation? delayed)
    {
        var candidates = CollectSharedCardActionCandidates(action,SkillProgramTriggerWindow.CardEffectBeforeApply,designated,null)
            .OrderByDescending(c => c.Priority).ThenBy(c => c.OwnerSeat).ThenBy(c => c.SkillId,StringComparer.Ordinal)
            .ThenBy(c => c.SkillInstanceId,StringComparer.Ordinal).ThenBy(c => c.TriggerId,StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return false;
        var id = ++_resolutionSequence; var parentId = ordinary?.ParentFrameId ?? delayed!.ParentFrameId;
        candidates = candidates.Select(c => c with { FrozenContext = c.FrozenContext! with {
            ParentFrameId = id, CardUse = c.FrozenContext!.CardUse! with { ParentCardUseFrameId = parentId } } }).ToArray();
        PushRuntimeFrame(new CardEffectBeforeApplyFrame(id,parentId,action,Array.AsReadOnly(designated.ToArray()),Array.AsReadOnly(candidates),ordinary,delayed));
        AdvanceRuntimeFrame(id); return true;
    }
    private bool CanRunCardEffectCandidate(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        var frame = _resolutionStack.OfType<CardEffectBeforeApplyFrame>().LastOrDefault();
        return frame is not null && frame.Id == context.ParentFrameId && frame.CandidateIndex < frame.Candidates.Count &&
            ToSharedCandidate(frame.Candidates[frame.CandidateIndex]) == candidate && frame.Candidates[frame.CandidateIndex].FrozenContext == context &&
            frame.FinalDesignatedTargetSeats.Count == 1 && frame.FinalDesignatedTargetSeats[0] == candidate.OwnerSeat &&
            (frame.DelayedReturn is not null || frame.Action.ActorSeat != candidate.OwnerSeat);
    }
    private void ContinueCardEffectBeforeApply()
    {
        while (_resolutionStack.LastOrDefault() is CardEffectBeforeApplyFrame frame)
        {
            if (frame.CandidateIndex == frame.Candidates.Count)
            {
                PopResolutionFrame(frame.Id,ResolutionFrameKind.CardEffectBeforeApply);
                if (frame.OrdinaryReturn is {} ordinary)
                {
                    var parent = _resolutionStack.LastOrDefault();
                    if (parent is not CardUseFrame use || use.Id != ordinary.ParentFrameId || use.Action?.ActionId != frame.Action.ActionId)
                        throw new InvalidOperationException("Actual trick effect lost its exact use parent.");
                    // A paid effect child may kill its unique target or end the match.
                    // Finish this exact use; never reopen nullification or enter a dead response.
                    var targetSeat = frame.FinalDesignatedTargetSeats[0];
                    if (_winner != Winner.None || !_players[targetSeat].IsAlive)
                        CompleteIneffectiveTrickTarget(ordinary,targetSeat);
                    else ResolveNullifiableEffectCore(ordinary);
                }
                else ContinueDelayedCardEffect(frame.DelayedReturn ?? throw new InvalidOperationException("Missing delayed effect return."));
                return;
            }
            var candidate = frame.Candidates[frame.CandidateIndex]; var context = candidate.FrozenContext!;
            var shared = ToSharedCandidate(candidate);
            if (!CanRunProgramTrigger(shared,context)) { ReplaceRuntimeTop(frame with { CandidateIndex = frame.CandidateIndex + 1 }); continue; }
            var trigger = _contentRegistry.GetSkill(candidate.SkillId).Program!.Triggers.Single(t => t.Id == candidate.TriggerId);
            if (trigger.Optional) ExposeProgramTriggerDecision(shared,context); else BeginProgramBinding(shared,context);
            return;
        }
    }
    private void CompleteCardEffectCandidate(ProgramSkillWindowContext context)
    {
        if (_resolutionStack.LastOrDefault() is not CardEffectBeforeApplyFrame frame || frame.Id != context.ParentFrameId ||
            frame.CandidateIndex >= frame.Candidates.Count || frame.Candidates[frame.CandidateIndex].FrozenContext != context)
            throw new InvalidOperationException("The actual effect program lost its owning cursor.");
        ReplaceRuntimeTop(frame with { CandidateIndex = frame.CandidateIndex + 1 }); AdvanceRuntimeFrame(frame.Id);
    }
    private void ContinueDelayedCardEffect(DelayedEffectContinuation delayed)
    {
        var parent = _resolutionStack.LastOrDefault();
        if (_turnNumber != delayed.TurnNumber || _phase != delayed.Phase || _currentSeat != delayed.OwnerSeat ||
            (parent?.Id ?? 0) != delayed.ParentFrameId || parent?.Kind != delayed.ParentKind || parent?.Step != delayed.ParentStep)
            throw new InvalidOperationException("Delayed effect lost its exact turn/phase/parent.");
        var owner = _players[delayed.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None) return;
        var card = _cardZones.CardsAt(CardLocation.Judgment(owner.Seat)).SingleOrDefault(c => c.Id == delayed.CardId);
        if (card is null) { BeginDelayedJudgmentOrTurnStart(owner); return; }
        if (GetJudgmentEffectiveCardKind(card) != delayed.EffectiveKind) throw new InvalidOperationException("Delayed effect identity changed while suspended.");
        BeginActualDelayedJudgment(owner,card,delayed.EffectiveKind);
    }
    private void BeginActualDelayedJudgment(CharacterState current,Card card,CardKind kind)
    {
        var info = GetDelayedJudgmentInfo(kind);
        var judgment = BeginJudgment(attack:null,targetSeat:current.Seat,reason:info.Reason,parentFrameId:0,
            sourceCard:kind,continuation:info.Continuation,delayedCard:card,sourceSeat:current.Seat);
        if (judgment is null) AdvanceRulesAndPublishState();
    }
    private void AssertCardEffectProgramState()
    {
        foreach (var frame in _resolutionStack.OfType<CardEffectBeforeApplyFrame>())
        {
            var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
            var candidate = frame.CandidateIndex >= 0 && frame.CandidateIndex < frame.Candidates.Count ? frame.Candidates[frame.CandidateIndex] : null;
            var context = candidate?.FrozenContext;
            if (frame.FinalDesignatedTargetSeats.Count != 1 || frame.FinalDesignatedTargetSeats.Distinct().Count() != 1 ||
                (frame.OrdinaryReturn is null) == (frame.DelayedReturn is null) || candidate is null ||
                context?.Window != SkillProgramTriggerWindow.CardEffectBeforeApply || context.ParentFrameId != frame.Id ||
                context.CardUse?.ParentCardUseFrameId != frame.ParentFrameId || context.CardUse.CardActionId != frame.Action.ActionId)
                throw new InvalidOperationException("Actual effect window lost its exact frozen candidate or return.");
            if (frame.OrdinaryReturn is {} ordinary)
            {
                if (index < 1 || _resolutionStack[index-1] is not CardUseFrame use || use.Id != frame.ParentFrameId ||
                    ordinary.ParentFrameId != use.Id || use.Action?.ActionId != frame.Action.ActionId || IsDelayedCard(ordinary.EffectCardKind) ||
                    ordinary.EffectCardKind != frame.Action.EffectiveKind ||
                    frame.Action.PhysicalCards.Any(c => _cardZones.GetLocation(c.CardId) != CardLocation.Processing))
                    throw new InvalidOperationException("Ordinary effect window lost its exact suspended use.");
            }
            else
            {
                var delayed = frame.DelayedReturn!;
                var parent = index == 0 ? null : _resolutionStack[index-1];
                if ((parent?.Id ?? 0) != delayed.ParentFrameId || parent?.Kind != delayed.ParentKind || parent?.Step != delayed.ParentStep ||
                    delayed.TurnNumber != _turnNumber || delayed.OwnerSeat != _currentSeat || delayed.Phase != _phase ||
                    delayed.EffectiveKind != frame.Action.EffectiveKind || !IsDelayedCard(delayed.EffectiveKind) ||
                    frame.FinalDesignatedTargetSeats[0] != delayed.OwnerSeat)
                    throw new InvalidOperationException("Delayed effect window lost its exact turn/parent.");
            }
            var topPrompt = index == _resolutionStack.Count-1 && _pendingDecision is {Kind:DecisionKind.ProgramTrigger} prompt &&
                prompt.PlayerSeat == candidate.OwnerSeat && prompt.SkillPrompt?.SkillId == candidate.SkillId;
            var childMatches = index+1 < _resolutionStack.Count && _resolutionStack[index+1] is ProgramSkillFrame child &&
                child.WindowContext == context && child.OwnerSeat == candidate.OwnerSeat && child.SkillId == candidate.SkillId &&
                child.SkillInstanceId == candidate.SkillInstanceId && child.TriggerId == candidate.TriggerId && child.GameplayHash == candidate.GameplayHash;
            if (!topPrompt && !childMatches) throw new InvalidOperationException("Actual effect candidate has no exact prompt or program child.");
        }
    }
}
