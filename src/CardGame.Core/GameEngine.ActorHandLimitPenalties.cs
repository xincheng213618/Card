namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Issued rule contributions, not pending resolution state. Command replay
    // recreates them from the same native use and actual-turn boundary facts.
    private readonly List<ActorHandLimitPenalty> _actorHandLimitPenalties = [];
    private long _actorHandLimitPenaltySequence;

    private bool HasActorHandLimitPenaltyCapability =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GrantActorHandLimitPenalty);

    private sealed partial class ProgramSkillHost : IActorHandLimitPenaltyHost
    {
        public void GrantActorHandLimitPenalty(ProgramSkillFrame frame, int amount) =>
            engine.GrantProgramActorHandLimitPenalty(frame, amount);
    }

    private bool TryGetActorHandLimitPenaltyUse(int ownerSeat, ProgramSkillWindowContext context,
        out CardUseFrame use, out ProgramCardTriggerWindowFrame window)
    {
        use = null!; window = null!;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } ||
            context.OwnerSeat != ownerSeat || context.TargetSeat != ownerSeat || identity.EventTargetSeat != ownerSeat ||
            context.Facts?.CardActionTargetIsOwner != true || identity.ActorSeat == ownerSeat ||
            context.SourceSeat != identity.ActorSeat || !IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive ||
            !IsValidPlayerSeat(identity.ActorSeat) || !_players[identity.ActorSeat].IsAlive || _winner != Winner.None ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(item => item.Id == context.ParentFrameId) is not { } found ||
            found.ParentFrameId != identity.ParentCardUseFrameId || found.CompletedResponseReturn is not null ||
            found.ResponseCompletion is not null || found.CardSupplyCompletion is not null ||
            found.Continuation is not (ProgramCardContinuation.Slash or ProgramCardContinuation.FinalizedTrick or ProgramCardContinuation.FinalizedSimpleCard) ||
            GetCardActionWindow(found) != context.Window ||
            LifecycleCardUse(found.ParentFrameId) is not { Action: { Type: CardActionType.Use } action } actual ||
            actual.ActorHandLimitAnnouncedTargets?.Contains(ownerSeat) != true ||
            action.ActionId != identity.CardActionId || action.ActorSeat != identity.ActorSeat || actual.SourceSeat != identity.ActorSeat ||
            action.EffectiveKind != actual.CardKind || identity.EffectiveKind != actual.CardKind ||
            found.Action.Type != CardActionType.Use || found.Action.ActionId != action.ActionId ||
            found.Action.ActorSeat != action.ActorSeat || found.Action.EffectiveKind != action.EffectiveKind ||
            action.ResponderSeat is not null || action.OpponentSeat is not null ||
            found.Action.ResponderSeat is not null || found.Action.OpponentSeat is not null || actual.DyingResponse is not null ||
            GetProgramCardCategory(actual.CardKind) != SkillProgramCardCategory.Basic && !IsOrdinaryTrick(actual.CardKind) ||
            action.PhysicalCards.Any(cost => cost.CardId <= 0) ||
            action.PhysicalCards.DistinctBy(cost => cost.CardId).Count() != action.PhysicalCards.Count ||
            !found.Action.PhysicalCards.Select(cost => cost.CardId).SequenceEqual(action.PhysicalCards.Select(cost => cost.CardId))) return false;
        var windowIndex = _resolutionStack.FindIndex(item => item.Id == found.Id);
        if (windowIndex < 1 || _resolutionStack[windowIndex - 1].Id != actual.Id) return false;

        // The original designation window may retain its original target list
        // after a signed native target adjustment. Its exact frozen candidate
        // proves this owner's designation; only use identity and paid materials
        // must remain equal here.
        if (action.PhysicalCards.Count > 0)
        {
            if (IsForeignPublicPileSlashUse(actual.Id))
            {
                if (actual.PhysicalCardIds is not { Count: 0 } ||
                    action.PhysicalCards.Any(cost => _cardZones.GetLocation(cost.CardId) != CardLocation.DiscardPile)) return false;
            }
            else if (!action.PhysicalCards.Select(cost => cost.CardId).SequenceEqual(actual.PhysicalCardIds ?? [actual.CardId]) ||
                action.PhysicalCards.Where(cost => !IsCurrentUsePhysicalCardClaim(actual.Id, cost.CardId) &&
                    !IsSelectedForeignCardSlashRemovedMaterial(actual.Id, cost.CardId) &&
                    !IsExchangedCardClaim(action.ActionId, cost.CardId)).Any(cost => IsProgramAlternativeCost(action, cost.CardId)
                        ? _cardZones.GetLocation(cost.CardId) == CardLocation.Processing
                        : _cardZones.GetLocation(cost.CardId) != CardLocation.Processing)) return false;
        }
        else if (actual.CardId != 0 || actual.PhysicalCardIds is not { Count: 0 } ||
            !IsChainedStateBasicUse(actual.Id) && !IsTieredRoundZeroUse(actual.Id) && !IsProgramVirtualOrdinaryTrickUse(actual.Id) &&
            actual.VirtualBasicReturn is null && actual.CardAttack?.ProgramSkillCardUseFrameId is null &&
            actual.AdjustedSlashReturn is null && !IsIssuedZeroEntityDuel(actual.Id)) return false;
        use = actual; window = found;
        return true;
    }

    private bool CanOfferActorHandLimitPenalty(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger,
        ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.GrantActorHandLimitPenalty)) return true;
        ActorHandLimitPenaltyContract.ValidateTrigger(candidate.SkillId, trigger);
        if (!TryGetActorHandLimitPenaltyUse(candidate.OwnerSeat, context, out var use, out var window) ||
            !DesignatedExtraTargetCandidateMatches(candidate, context, window) || trigger.Id != candidate.BindingId ||
            _contentRegistry.GetSkill(candidate.SkillId).Program is not { } program || program.GameplayHash != candidate.GameplayHash ||
            !HasRuntimeSkillInstance(_players[candidate.OwnerSeat], candidate.SkillId, candidate.SkillInstanceId)) return false;
        var source = new CardConversionSource(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId);
        return !CompleteProgramEventHistory().OfType<ProgramActorHandLimitPenaltyGrantedEvent>().Any(issued =>
            issued.Penalty.ActionId == use.Action!.ActionId && issued.Penalty.CardUseFrameId == use.Id &&
            issued.Penalty.TargetSeat == candidate.OwnerSeat && issued.Penalty.Source.OwnerSeat == source.OwnerSeat &&
            issued.Penalty.Source.SkillId == source.SkillId && issued.Penalty.Source.BindingId == source.BindingId);
    }

    private void GrantProgramActorHandLimitPenalty(ProgramSkillFrame supplied, int amount)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame != supplied || frame.TriggerId is not { } binding || frame.WindowContext is not { } context ||
            frame.InstructionIndex <= 0 || frame.PendingMovementContinuation is not null ||
            frame.SelectedCardPayment is not null && frame.SelectedCardPaymentResult is null || amount <= 0 ||
            !TryGetActorHandLimitPenaltyUse(frame.OwnerSeat, context, out var use, out var window) ||
            !DesignatedExtraTargetCandidateMatches(new(frame.OwnerSeat, frame.SkillId, binding, frame.SkillInstanceId, frame.GameplayHash, 0), context, window) ||
            !window.Activated || _contentRegistry.GetSkill(frame.SkillId).Program is not { } program || program.GameplayHash != frame.GameplayHash)
            throw new InvalidOperationException("An actor hand-limit penalty lost its exact native use, frozen designated target, active candidate or instruction.");
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (index < 2 || _resolutionStack[index - 1].Id != window.Id || _resolutionStack[index - 2].Id != use.Id)
            throw new InvalidOperationException("An actor hand-limit penalty requires its immediate native designation parent.");
        var trigger = GetProgramTrigger(frame);
        ActorHandLimitPenaltyContract.ValidateTrigger(frame.SkillId, trigger);
        var effectIndex = frame.InstructionIndex - 1;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, program).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.GrantActorHandLimitPenalty || effect.Amount != amount ||
            effect.Target != SkillProgramEffectTarget.Owner || effect.Condition.Kind != SkillProgramConditionKind.Always ||
            effect.TargetReference is not null)
            throw new InvalidOperationException("An actor hand-limit penalty differs from its exact configured positive instruction.");
        var history = CompleteProgramEventHistory();
        if (history.OfType<ProgramBindingStartedEvent>().Count(started => started.FrameId == frame.Id &&
                started.OwnerSeat == frame.OwnerSeat && started.SkillId == frame.SkillId && started.BindingId == binding &&
                started.SkillInstanceId == frame.SkillInstanceId && started.Window == context.Window) != 1)
            throw new InvalidOperationException("An actor hand-limit penalty lost its original binding start.");
        var source = new CardConversionSource(frame.SkillId, binding, frame.OwnerSeat, frame.SkillInstanceId);
        var issued = history.OfType<ProgramActorHandLimitPenaltyGrantedEvent>().Where(item =>
            item.Penalty.ProgramFrameId == frame.Id && item.Penalty.EffectIndex == effectIndex).ToArray();
        if (issued.Length != 0)
        {
            if (issued is not [var previous] || previous.Penalty.Source != source || previous.Penalty.GameplayHash != frame.GameplayHash ||
                previous.Penalty.CardUseFrameId != use.Id || previous.Penalty.ActionId != use.Action!.ActionId ||
                previous.Penalty.ActorSeat != context.CardUse!.ActorSeat || previous.Penalty.TargetSeat != frame.OwnerSeat || previous.Penalty.Amount != amount)
                throw new InvalidOperationException("An issued actor hand-limit penalty cannot be replaced or paid twice.");
            return;
        }
        if (_turnNumber <= 0 || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            history.OfType<ProgramActorHandLimitPenaltyGrantedEvent>().Any(item =>
                item.Penalty.Source.OwnerSeat == source.OwnerSeat && item.Penalty.Source.SkillId == source.SkillId &&
                item.Penalty.Source.BindingId == source.BindingId &&
                item.Penalty.ActionId == use.Action!.ActionId && item.Penalty.CardUseFrameId == use.Id && item.Penalty.TargetSeat == frame.OwnerSeat))
            throw new InvalidOperationException("A native use can issue its exact target penalty only once from a live source instance.");
        var penalty = new ActorHandLimitPenalty(checked(++_actorHandLimitPenaltySequence), frame.Id, effectIndex,
            source, frame.GameplayHash, use.Id, use.Action!.ActionId, context.CardUse!.ActorSeat, frame.OwnerSeat, _turnNumber, amount);
        _actorHandLimitPenalties.Add(penalty);
        AdvanceEventRulesAndQueueFact(new ProgramActorHandLimitPenaltyGrantedEvent(penalty));
    }

    private IEnumerable<RuleQueryContribution> ActorHandLimitPenaltyContributions(CharacterState player)
    {
        foreach (var penalty in _actorHandLimitPenalties.Where(item => item.ActorSeat == player.Seat))
            yield return new FiniteRuleQueryContribution($"actor-hand-limit-penalty:{penalty.GrantSequence}", SkillRuleOperation.Add, -penalty.Amount);
    }

    private void ExpireActorHandLimitPenalties(TurnEndedEvent ended)
    {
        if (!HasActorHandLimitPenaltyCapability || _actorHandLimitPenalties.Count == 0 ||
            !_actorHandLimitPenalties.Any(item => item.ActorSeat == ended.ActorSeat)) return;
        var history = CompleteProgramEventHistory().ToArray();
        var endIndex = Array.FindLastIndex(history, item => item is TurnEndedEvent actual && actual == ended);
        if (ended.TurnNumber <= 0 || !IsValidPlayerSeat(ended.ActorSeat) || endIndex < 0 ||
            history.OfType<TurnEndedEvent>().LastOrDefault() != ended)
            throw new InvalidOperationException("An actor hand-limit penalty expires only after its exact actual turn-ended fact.");
        var due = _actorHandLimitPenalties.Where(penalty => penalty.ActorSeat == ended.ActorSeat &&
            Array.FindIndex(history, item => item is ProgramActorHandLimitPenaltyGrantedEvent grant && grant.Penalty == penalty) is var grantIndex &&
            grantIndex >= 0 && grantIndex < endIndex).ToArray();
        foreach (var penalty in due)
        {
            _actorHandLimitPenalties.Remove(penalty);
            AdvanceEventRulesAndQueueFact(new ProgramActorHandLimitPenaltyExpiredEvent(penalty, ended.TurnNumber, ended.ActorSeat, "actor-turn-ended"));
        }
    }

    private void ExpireAllActorHandLimitPenalties(string reason)
    {
        if (!HasActorHandLimitPenaltyCapability || _actorHandLimitPenalties.Count == 0) return;
        if (string.IsNullOrWhiteSpace(reason) || !CompleteProgramEventHistory().OfType<GameEndedEvent>().Any())
            throw new InvalidOperationException("Terminal actor hand-limit cleanup requires a real game-ended fact and explicit reason.");
        foreach (var penalty in _actorHandLimitPenalties.ToArray())
        {
            _actorHandLimitPenalties.Remove(penalty);
            AdvanceEventRulesAndQueueFact(new ProgramActorHandLimitPenaltyExpiredEvent(penalty, null, null, reason));
        }
    }
}
