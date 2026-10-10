namespace CardGame.Core;

public sealed partial class GameEngine
{
    private long GiftedSlashSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static string GiftedSlashGiftReason(string skill) => $"skill-program.{skill}.{SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward}.gift";
    private static string GiftedSlashDrawReason(string skill) => $"skill-program.{skill}.{SkillProgramEffectOp.RewardGiftedSlashUse}.draw";
    private bool TracksGiftedSlash => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RewardGiftedSlashUse);

    private bool ExactGiftedSlashGiftActivation(ProgramSkillFrame frame)
    {
        var program = _contentRegistry.GetSkill(frame.SkillId).Program;
        var activation = program?.Activations.SingleOrDefault(a => a.Id == frame.ActivationId);
        if (program?.GameplayHash != frame.GameplayHash || activation is null || frame.TriggerId is not null || frame.WindowContext is not null ||
            frame.InstructionIndex != 3 || frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats is not [var recipient] ||
            !IsValidPlayerSeat(recipient) || recipient == frame.OwnerSeat || frame.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            activation.Effects is not [_, _, { Op: SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward }] ||
            CompleteProgramEventHistory().OfType<ProgramSkillStartedEvent>().Count(e => e.FrameId == frame.Id &&
                e.OwnerSeat == frame.OwnerSeat && e.SkillId == frame.SkillId && e.ActivationId == frame.ActivationId) != 1) return false;
        GiftedSlashRewardContract.ValidateActivation(frame.SkillId, activation);
        return _skillRuntimeState.GetUsage(frame.OwnerSeat, frame.SkillId, $"{activation.Id}:target:{recipient}", SkillUsageScope.Phase) == 1;
    }

    private SkillProgramStepOutcome BeginGiftedSlashGift(SkillProgramEffect effect, ProgramSkillFrame input, int recipient)
    {
        var frame = GetActiveProgramFrame(input.Id);
        AssertGiftedSlashPrograms(frame);
        if (!ExactGiftedSlashGiftActivation(frame) || effect != _contentRegistry.GetSkill(frame.SkillId).Program!.Activations.Single(a => a.Id == frame.ActivationId).Effects[2] ||
            frame.SelectedTargetSeats.Single() != recipient || frame.PendingMovementContinuation is not null ||
            frame.GiftedSlashGiftReceipt is not null || frame.GiftedSlashRewardReceipt is not null ||
            CompleteProgramEventHistory().OfType<GiftedSlashGiftStartedEvent>().Any(e => e.FrameId == frame.Id))
            throw new InvalidOperationException("A gifted Slash transfer requires its exact unpaid activation, target ledger and instruction.");
        var bound = GetProgramCardSet(frame, effect.SourceBind!);
        if (bound.CardIds is not [var id] || bound.SourceLocations is not [var from] || from != CardLocation.Hand(frame.OwnerSeat) ||
            bound.Visibility != SkillProgramCardSetVisibility.Private || bound.SelectionActorSeat is { } selector && selector != frame.OwnerSeat)
            throw new InvalidOperationException("A gifted Slash transfer requires exactly one privately selected original owner Hand card.");
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive || !_players[recipient].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) || _cardZones.GetLocation(id) != from)
        { CancelProgramBindingAndCleanup(frame, "原赠牌来源或受赠者已失效，未重复支付。"); return SkillProgramStepOutcome.AwaitChild; }
        var card = GetAdvancedCard(id);
        if (card.IsGeneralWeapon)
        { CancelProgramBindingAndCleanup(frame, "武将武器不能成为受赠手牌政策。"); return SkillProgramStepOutcome.AwaitChild; }
        var before = GiftedSlashSequence;
        var policy = new GiftedSlashHandPolicy(frame.Id, frame.InstructionIndex,
            new(frame.SkillId, frame.ActivationId, frame.OwnerSeat, frame.SkillInstanceId), frame.GameplayHash,
            recipient, id, 0, 0, _turnNumber, _cardUseDebitPhaseInstanceId);
        var material = new CardActionCost(id, card.Kind, from, CapturePhysicalCardColor(frame.OwnerSeat, card));
        ReplaceRuntimeTop(frame = frame with { GiftedSlashGiftReceipt = new(frame.InstructionIndex, policy, material, before, before),
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new GiftedSlashGiftStartedEvent(frame.Id, policy, material, before));
        MoveProgramCardsFromMultipleSources([id], CardLocation.Hand(recipient), new(GiftedSlashGiftReason(frame.SkillId)), (batchId, records) =>
        {
            var active = GetActiveProgramFrame(input.Id);
            var movement = records.Single();
            var paid = active.GiftedSlashGiftReceipt! with
            { Policy = policy with { GiftMovementSequence = movement.Sequence, GiftBatchId = batchId }, After = movement.Sequence };
            ReplaceRuntimeTop(active with { GiftedSlashGiftReceipt = paid });
            AdvanceEventRulesAndQueueFact(new GiftedSlashHandPolicyGrantedEvent(paid.Policy, material));
        });
        AdvanceRuntimeProgram(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    // The issued fact and real departure ledger define one continuous hand interval.
    // There is no parallel active-policy cache, turn timer or use-id lookup dictionary.
    private GiftedSlashHandPolicy? ActiveGiftedSlashPolicy(int recipient, int cardId)
    {
        if (!TracksGiftedSlash || _cardZones.GetLocation(cardId) != CardLocation.Hand(recipient)) return null;
        var policies = ProgramEventHistory<GiftedSlashHandPolicyGrantedEvent>();
        if (policies.Count == 0) return null;
        var policy = policies
            .Select(e => e.Policy).Where(p => p.RecipientSeat == recipient && p.CardId == cardId)
            .OrderByDescending(p => p.GiftMovementSequence).FirstOrDefault();
        if (policy is null || _cardMovements.Any(move => move.CardId == cardId && move.Sequence > policy.GiftMovementSequence &&
            move.From == CardLocation.Hand(recipient))) return null;
        AssertGiftedSlashPolicyOrigin(policy);
        return policy;
    }

    private ProgramCardIdentityMatch? GetGiftedSlashIdentityMatch(CharacterState owner, Card card)
    {
        if (ActiveGiftedSlashPolicy(owner.Seat, card.Id) is not { } policy) return null;
        var program = _contentRegistry.GetSkill(policy.Source.SkillId).Program!;
        return new(program, new SkillProgramCardIdentity(policy.Source.BindingId, [CardZoneKind.Hand], [], [], CardKind.Slash,
            new(SkillProgramConditionKind.Always, 0, [])), policy.Source);
    }

    private void IssueGiftedSlashUseBenefit(long useId, CardActionContext? action)
    {
        if (!TracksGiftedSlash || action is not { Type: CardActionType.Use } || !IsSlashCard(action.EffectiveKind) ||
            action.ActorSeat != action.ProviderSeat || action.PhysicalCards is not [var material] ||
            material.From != CardLocation.Hand(action.ActorSeat) || ActiveGiftedSlashPolicy(action.ActorSeat, material.CardId) is not { } policy ||
            !action.ConversionChain.Contains(policy.Source)) return;
        var use = LifecycleCardUse(useId) ?? throw new InvalidOperationException("A gifted Slash benefit requires its real owning use.");
        if (use.GiftedSlashUseBenefit is not null || use.Action?.ActionId != action.ActionId || use.SourceSeat != action.ActorSeat ||
            use.CardId != material.CardId || use.CardKind != action.EffectiveKind || use.PhysicalCardIds is not [var id] || id != material.CardId ||
            CompleteProgramEventHistory().OfType<GiftedSlashUseBenefitIssuedEvent>().Any(e => e.Benefit.CardUseFrameId == useId))
            throw new InvalidOperationException("A gifted Slash benefit cannot replace or duplicate its accepted single-entity issuance.");
        var benefit = new GiftedSlashUseBenefit(policy, useId, action.ActionId, action.ActorSeat, action.ProviderSeat, material);
        UpdateLifecycleCardUse(useId, frame => frame with { GiftedSlashUseBenefit = benefit });
        AdvanceEventRulesAndQueueFact(new GiftedSlashUseBenefitIssuedEvent(benefit));
    }

    private void AssertGiftedSlashPolicyOrigin(GiftedSlashHandPolicy policy)
    {
        var program = _contentRegistry.GetSkill(policy.Source.SkillId).Program;
        var activation = program?.Activations.SingleOrDefault(a => a.Id == policy.Source.BindingId);
        var history = CompleteProgramEventHistory().ToArray();
        if (program?.GameplayHash != policy.GameplayHash || activation?.Effects is not [_, _, { Op: SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward }] ||
            policy.ProgramFrameId <= 0 || policy.InstructionIndex != 3 || policy.GiftMovementSequence <= 0 || policy.GiftBatchId <= policy.ProgramFrameId ||
            policy.ActualTurnNumber < 1 || policy.ActualPlayPhaseSerial < 1 || string.IsNullOrWhiteSpace(policy.Source.SkillInstanceId) ||
            !IsValidPlayerSeat(policy.Source.OwnerSeat) || !IsValidPlayerSeat(policy.RecipientSeat) || policy.RecipientSeat == policy.Source.OwnerSeat ||
            history.OfType<GiftedSlashHandPolicyGrantedEvent>().Where(e => e.Policy.ProgramFrameId == policy.ProgramFrameId).ToArray() is not [var granted] ||
            granted.Policy != policy || granted.Material.CardId != policy.CardId || granted.Material.From != CardLocation.Hand(policy.Source.OwnerSeat) ||
            history.OfType<GiftedSlashGiftStartedEvent>().Where(e => e.FrameId == policy.ProgramFrameId).ToArray() is not [var started] ||
            started.Policy != (policy with { GiftMovementSequence = 0, GiftBatchId = 0 }) || started.Material != granted.Material ||
            started.SequenceBefore < 0 || started.SequenceBefore >= policy.GiftMovementSequence ||
            history.OfType<ProgramSkillStartedEvent>().Count(e => e.FrameId == policy.ProgramFrameId && e.OwnerSeat == policy.Source.OwnerSeat &&
                e.SkillId == policy.Source.SkillId && e.ActivationId == policy.Source.BindingId) != 1 ||
            _cardMovements.Count(move => move.Sequence == policy.GiftMovementSequence && move.CardId == policy.CardId &&
                move.CardKind == granted.Material.CardKind && move.From == granted.Material.From && move.To == CardLocation.Hand(policy.RecipientSeat) &&
                move.TurnNumber == policy.ActualTurnNumber && move.Reason.Value == GiftedSlashGiftReason(policy.Source.SkillId)) != 1)
            throw new InvalidOperationException("A gifted Slash policy lost its original paid activation, exact source instance or real Hand transfer.");
        GiftedSlashRewardContract.ValidateActivation(policy.Source.SkillId, activation);
        var startIndex = Array.FindIndex(history, e => e is ProgramSkillStartedEvent fact && fact.FrameId == policy.ProgramFrameId);
        var paidIndex = Array.FindIndex(history, e => e is GiftedSlashHandPolicyGrantedEvent fact && fact.Policy == policy);
        if (startIndex < 0 || paidIndex <= startIndex || history.Skip(startIndex + 1).Take(paidIndex - startIndex - 1).OfType<SkillUsageConsumedEvent>()
                .Count(e => e.SkillOwnerSeat == policy.Source.OwnerSeat && e.SkillId == policy.Source.SkillId &&
                    e.UsageId == $"{policy.Source.BindingId}:target:{policy.RecipientSeat}" && e.Scope == SkillUsageScope.Phase && e.Count == 1) != 1)
            throw new InvalidOperationException("A gifted Slash policy lost its original per-target phase payment.");
    }

    private void AssertGiftedSlashUseBenefit(CardUseFrame use)
    {
        if (use.GiftedSlashUseBenefit is not { } benefit)
        {
            if (TracksGiftedSlash && CompleteProgramEventHistory().OfType<GiftedSlashUseBenefitIssuedEvent>().Any(e => e.Benefit.CardUseFrameId == use.Id))
                throw new InvalidOperationException("An issued gifted Slash use cannot lose its owning benefit.");
            return;
        }
        if (!TracksGiftedSlash || use.Action is not { Type: CardActionType.Use } action || !IsSlashCard(use.CardKind) ||
            action.EffectiveKind != use.CardKind || benefit.CardUseFrameId != use.Id || benefit.ActionId != action.ActionId ||
            benefit.OriginalActorSeat != benefit.Policy.RecipientSeat || benefit.ProviderSeat != benefit.OriginalActorSeat ||
            !ShownEntityUseActorMatches(use, benefit.OriginalActorSeat, benefit.ProviderSeat) ||
            action.PhysicalCards is not [var material] || material != benefit.Material || use.CardId != material.CardId ||
            use.PhysicalCardIds is not [var id] || id != material.CardId || material.CardId != benefit.Policy.CardId ||
            material.From != CardLocation.Hand(benefit.Policy.RecipientSeat) || !action.ConversionChain.Contains(benefit.Policy.Source) ||
            CompleteProgramEventHistory().OfType<GiftedSlashUseBenefitIssuedEvent>().Count(e => e.Benefit == benefit) != 1)
            throw new InvalidOperationException("A gifted Slash use lost its accepted recipient/provider, original material or frozen policy.");
        AssertGiftedSlashPolicyOrigin(benefit.Policy);
    }

    private void CollectIssuedGiftedSlashRewardCandidates(CardActionContext action, SkillProgramTriggerWindow window,
        List<ProgramCardTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.CardUseCompleted || _resolutionStack.LastOrDefault() is not CardUseFrame use ||
            use.Action?.ActionId != action.ActionId || use.GiftedSlashUseBenefit is not { } benefit ||
            !_players[benefit.Policy.Source.OwnerSeat].IsAlive || _winner != Winner.None) return;
        AssertGiftedSlashUseBenefit(use);
        var source = benefit.Policy.Source;
        var program = _contentRegistry.GetSkill(source.SkillId).Program!;
        foreach (var trigger in program.Triggers.Where(t => t.Window == window && t.Effects.Any(e => e.Op == SkillProgramEffectOp.RewardGiftedSlashUse)))
        {
            if (result.Any(c => c.OwnerSeat == source.OwnerSeat && c.SkillId == source.SkillId && c.SkillInstanceId == source.SkillInstanceId && c.TriggerId == trigger.Id)) continue;
            var eventTarget = action.TargetSeats.Count == 1 ? action.TargetSeats.Single() : -1;
            var facts = CaptureProgramTriggerFacts(_players[source.OwnerSeat], action) with { CardUseCausedDamage = use.CausedDamage };
            var context = CreateCardActionProgramContext(action, window, 0, source.OwnerSeat, eventTarget, facts);
            result.Add(new(source.OwnerSeat, eventTarget, source.SkillId, trigger.Id, program.GameplayHash, source.SkillInstanceId, trigger.Priority, context));
        }
    }

    private bool HasIssuedGiftedSlashRewardCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        ExactIssuedGiftedSlashRewardCandidate(candidate, context, false);

    private bool ExactIssuedGiftedSlashRewardCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context, bool paid)
    {
        if (context is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } card } ||
            !IsValidPlayerSeat(candidate.OwnerSeat) || !paid && (!_players[candidate.OwnerSeat].IsAlive || _winner != Winner.None) ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            window.Continuation != ProgramCardContinuation.CompletedSlash || window.CompletedResponseReturn is not null ||
            window.ParentFrameId != card.ParentCardUseFrameId || window.Action.ActionId != card.CardActionId ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == window.ParentFrameId) is not { GiftedSlashUseBenefit: { } benefit } use ||
            use.Action is not { Type: CardActionType.Use } action || action.ActionId != card.CardActionId ||
            window.Action.ActionId != action.ActionId || window.Action.ActorSeat != action.ActorSeat || window.Action.ProviderSeat != action.ProviderSeat ||
            !window.Action.PhysicalCards.SequenceEqual(action.PhysicalCards) || card.ActorSeat != action.ActorSeat || card.EffectiveKind != action.EffectiveKind ||
            context.SourceSeat != action.ActorSeat || context.OwnerSeat != candidate.OwnerSeat ||
            benefit.Policy.Source.OwnerSeat != candidate.OwnerSeat || benefit.Policy.Source.SkillId != candidate.SkillId ||
            benefit.Policy.Source.SkillInstanceId != candidate.SkillInstanceId || benefit.Policy.GameplayHash != candidate.GameplayHash ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count) return false;
        var current = window.Candidates[window.CandidateIndex];
        var program = _contentRegistry.GetSkill(candidate.SkillId).Program;
        var trigger = program?.Triggers.SingleOrDefault(t => t.Id == candidate.BindingId);
        if (current.OwnerSeat != candidate.OwnerSeat || current.SkillId != candidate.SkillId || current.TriggerId != candidate.BindingId ||
            current.SkillInstanceId != candidate.SkillInstanceId || current.GameplayHash != candidate.GameplayHash ||
            CreateCardActionProgramContext(window, current) != context || program?.GameplayHash != candidate.GameplayHash ||
            trigger?.Effects is not [{ Op: SkillProgramEffectOp.RewardGiftedSlashUse }]) return false;
        var wi = _resolutionStack.FindIndex(f => f.Id == window.Id);
        if (wi <= 0 || _resolutionStack[wi - 1].Id != use.Id ||
            context.Facts?.CardUseCausedDamage != use.CausedDamage) return false;
        AssertGiftedSlashUseBenefit(use);
        return true;
    }

    private bool CanContinueIssuedGiftedSlashReward(ProgramSkillFrame frame) =>
        frame.GiftedSlashGiftReceipt is not null && ValidGiftedSlashGift(frame) || frame.GiftedSlashRewardReceipt is not null && ValidGiftedSlashReward(frame) ||
        frame.TriggerId is { } binding && frame.WindowContext is { } context && HasIssuedGiftedSlashRewardCandidate(
            new(frame.OwnerSeat, frame.SkillId, binding, frame.SkillInstanceId, frame.GameplayHash, 0), context);

    private bool CanOfferGiftedSlashUseReward(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RewardGiftedSlashUse) || HasIssuedGiftedSlashRewardCandidate(candidate, context) &&
            !CompleteProgramEventHistory().OfType<GiftedSlashRewardDrawIssuedEvent>().Any(e => e.Benefit.CardUseFrameId == context.CardUse!.ParentCardUseFrameId);

    private bool ExactGiftedSlashRewardParent(ProgramSkillFrame frame)
    {
        if (frame.InstructionIndex != 1 || frame.TriggerId is null || frame.ActivationId != frame.TriggerId || frame.WindowContext is not { } context ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            !ExactIssuedGiftedSlashRewardCandidate(new(frame.OwnerSeat, frame.SkillId, frame.TriggerId, frame.SkillInstanceId, frame.GameplayHash, 0), context,
                frame.GiftedSlashRewardReceipt is not null)) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        return index > 0 && _resolutionStack[index - 1].Id == context.ParentFrameId &&
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == frame.Id && e.OwnerSeat == frame.OwnerSeat &&
                e.SkillId == frame.SkillId && e.BindingId == frame.TriggerId && e.SkillInstanceId == frame.SkillInstanceId && e.Window == context.Window) == 1;
    }

    private SkillProgramStepOutcome BeginGiftedSlashUseReward(SkillProgramEffect effect, ProgramSkillFrame input)
    {
        var frame = GetActiveProgramFrame(input.Id);
        AssertGiftedSlashPrograms(frame);
        if (effect.Op != SkillProgramEffectOp.RewardGiftedSlashUse || !ExactGiftedSlashRewardParent(frame) || frame.PendingMovementContinuation is not null ||
            frame.GiftedSlashGiftReceipt is not null || frame.GiftedSlashRewardReceipt is not null)
            throw new InvalidOperationException("A gifted Slash reward requires its exact completed use, frozen giver candidate and unpaid instruction.");
        var use = _resolutionStack.OfType<CardUseFrame>().Single(u => u.Id == frame.WindowContext!.CardUse!.ParentCardUseFrameId);
        var benefit = use.GiftedSlashUseBenefit!;
        if (CompleteProgramEventHistory().OfType<GiftedSlashRewardDrawIssuedEvent>().Any(e => e.Benefit.CardUseFrameId == use.Id))
            throw new InvalidOperationException("A gifted Slash settlement cannot issue its reward twice.");
        var count = use.CausedDamage ? 2 : 1;
        var before = GiftedSlashSequence;
        ReplaceRuntimeTop(frame = frame with { GiftedSlashRewardReceipt = new(frame.InstructionIndex, benefit, count, 0, before, before),
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        var actual = DrawCards(_players[frame.OwnerSeat], count, true, new(GiftedSlashDrawReason(frame.SkillId))).Count;
        frame = GetActiveProgramFrame(input.Id);
        var receipt = frame.GiftedSlashRewardReceipt! with { DrawActual = actual, After = GiftedSlashSequence };
        ReplaceRuntimeTop(frame with { GiftedSlashRewardReceipt = receipt });
        AdvanceEventRulesAndQueueFact(new GiftedSlashRewardDrawIssuedEvent(frame.Id, benefit, count, actual, before, receipt.After));
        AdvanceRuntimeProgram(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool DrainGiftedSlashChildren(ProgramSkillFrame frame) =>
        TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id);

    private bool ResumeGiftedSlashUseReward(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != id ||
            frame.GiftedSlashGiftReceipt is null && frame.GiftedSlashRewardReceipt is null) return false;
        AssertGiftedSlashPrograms(frame);
        if (PreparationGameEnded()) return true;
        if (DrainGiftedSlashChildren(frame)) return true;
        frame = GetActiveProgramFrame(id);
        if (frame.GiftedSlashGiftReceipt is { } gift)
            AdvanceEventRulesAndQueueFact(new GiftedSlashGiftResolvedEvent(id, gift.Policy));
        else
        {
            var reward = frame.GiftedSlashRewardReceipt!;
            AdvanceEventRulesAndQueueFact(new GiftedSlashRewardResolvedEvent(id, reward.Benefit, reward.FrozenDrawCount, reward.DrawActual));
        }
        ReplaceRuntimeTop(frame = frame with { GiftedSlashGiftReceipt = null, GiftedSlashRewardReceipt = null, PendingMovementContinuation = null });
        FinishProgramSkill(frame, true);
        return true;
    }

    private bool ReturnGiftedSlashUseRewardMovement(ProgramSkillFrame frame)
    {
        if (frame.GiftedSlashGiftReceipt is null && frame.GiftedSlashRewardReceipt is null || frame.PendingMovementContinuation is null) return false;
        AssertGiftedSlashPrograms(frame);
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    private bool GiftedSlashPendingMatches(ProgramSkillFrame frame) =>
        frame.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending && pending.SubjectSeat == frame.OwnerSeat;

    private bool ValidGiftedSlashGift(ProgramSkillFrame frame)
    {
        if (frame.GiftedSlashGiftReceipt is not { } receipt || frame.GiftedSlashRewardReceipt is not null || !ExactGiftedSlashGiftActivation(frame) ||
            receipt.InstructionIndex != frame.InstructionIndex || receipt.Policy.ProgramFrameId != frame.Id || receipt.Policy.Source !=
                new CardConversionSource(frame.SkillId, frame.ActivationId, frame.OwnerSeat, frame.SkillInstanceId) ||
            receipt.Policy.GameplayHash != frame.GameplayHash || receipt.Policy.ActualTurnNumber != _turnNumber ||
            receipt.Policy.ActualPlayPhaseSerial != _cardUseDebitPhaseInstanceId || receipt.Policy.RecipientSeat != frame.SelectedTargetSeats.Single() ||
            receipt.Material.CardId != receipt.Policy.CardId || receipt.Material.From != CardLocation.Hand(frame.OwnerSeat) ||
            receipt.Before < 0 || receipt.After <= receipt.Before || receipt.After != receipt.Policy.GiftMovementSequence || receipt.After > GiftedSlashSequence ||
            !GiftedSlashPendingMatches(frame) || CompleteProgramEventHistory().OfType<GiftedSlashGiftResolvedEvent>().Any(e => e.FrameId == frame.Id)) return false;
        var bound = GetProgramCardSet(frame, _contentRegistry.GetSkill(frame.SkillId).Program!.Activations.Single(a => a.Id == frame.ActivationId).Effects[2].SourceBind!);
        if (bound.CardIds is not [var id] || id != receipt.Material.CardId || bound.SourceLocations is not [var from] || from != receipt.Material.From ||
            bound.Visibility != SkillProgramCardSetVisibility.Private || bound.SelectionActorSeat is { } actor && actor != frame.OwnerSeat) return false;
        AssertGiftedSlashPolicyOrigin(receipt.Policy);
        return CompleteProgramEventHistory().OfType<GiftedSlashGiftStartedEvent>().Single(e => e.FrameId == frame.Id).SequenceBefore == receipt.Before &&
            _cardMovements.Count(move => move.Sequence > receipt.Before && move.Sequence <= receipt.After && move.Reason.Value == GiftedSlashGiftReason(frame.SkillId)) == 1;
    }

    private bool ValidGiftedSlashReward(ProgramSkillFrame frame)
    {
        if (frame.GiftedSlashRewardReceipt is not { } receipt || frame.GiftedSlashGiftReceipt is not null || !ExactGiftedSlashRewardParent(frame) ||
            receipt.InstructionIndex != frame.InstructionIndex || receipt.Benefit.Policy.Source.OwnerSeat != frame.OwnerSeat ||
            receipt.Benefit.Policy.Source.SkillId != frame.SkillId || receipt.Benefit.Policy.Source.SkillInstanceId != frame.SkillInstanceId ||
            receipt.Benefit.Policy.GameplayHash != frame.GameplayHash || frame.WindowContext?.CardUse?.ParentCardUseFrameId != receipt.Benefit.CardUseFrameId ||
            frame.WindowContext.CardUse.CardActionId != receipt.Benefit.ActionId || !GiftedSlashPendingMatches(frame) ||
            receipt.FrozenDrawCount != (frame.WindowContext.Facts?.CardUseCausedDamage == true ? 2 : 1) || receipt.DrawActual < 0 || receipt.DrawActual > receipt.FrozenDrawCount ||
            receipt.Before < 0 || receipt.After < receipt.Before || receipt.After > GiftedSlashSequence) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<GiftedSlashRewardDrawIssuedEvent>().Where(e => e.Benefit.CardUseFrameId == receipt.Benefit.CardUseFrameId).ToArray() is not [var issued] ||
            issued != new GiftedSlashRewardDrawIssuedEvent(frame.Id, receipt.Benefit, receipt.FrozenDrawCount, receipt.DrawActual, receipt.Before, receipt.After) ||
            history.OfType<GiftedSlashRewardResolvedEvent>().Any(e => e.Benefit.CardUseFrameId == receipt.Benefit.CardUseFrameId) ||
            _resolutionStack.OfType<CardUseFrame>().Single(u => u.Id == receipt.Benefit.CardUseFrameId).GiftedSlashUseBenefit != receipt.Benefit) return false;
        var invoice = _cardMovements.Where(move => move.Sequence > receipt.Before && move.Sequence <= receipt.After &&
            move.Reason.Value == GiftedSlashDrawReason(frame.SkillId)).ToArray();
        return invoice.Length == receipt.DrawActual && invoice.Select(move => move.CardId).Distinct().Count() == receipt.DrawActual &&
            invoice.All(move => move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(frame.OwnerSeat)) &&
            (receipt.DrawActual > 0 || receipt.Before == receipt.After);
    }

    private void AssertGiftedSlashPrograms(ProgramSkillFrame frame)
    {
        if (frame.GiftedSlashGiftReceipt is null && frame.GiftedSlashRewardReceipt is null)
        {
            if (!TracksGiftedSlash) return;
            var history = CompleteProgramEventHistory().ToArray();
            if (history.OfType<GiftedSlashGiftStartedEvent>().Any(e => e.FrameId == frame.Id) &&
                    !history.OfType<GiftedSlashGiftResolvedEvent>().Any(e => e.FrameId == frame.Id) ||
                history.OfType<GiftedSlashRewardDrawIssuedEvent>().Any(e => e.FrameId == frame.Id) &&
                    !history.OfType<GiftedSlashRewardResolvedEvent>().Any(e => e.FrameId == frame.Id))
                throw new InvalidOperationException("A paid gifted Slash instruction cannot lose its owning movement receipt.");
            return;
        }
        if (frame.GiftedSlashGiftReceipt is not null ? !ValidGiftedSlashGift(frame) : !ValidGiftedSlashReward(frame))
            throw new InvalidOperationException("A gifted Slash instruction lost its exact paid source, physical invoice or once-issued reward.");
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index + 1 < _resolutionStack.Count && !GiftedSlashFirstChild(frame, _resolutionStack[index + 1]))
            throw new InvalidOperationException("A gifted Slash instruction retained an unrelated native movement child.");
    }

    private bool IsGiftedSlashAwaitedMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        frame.PendingMovementContinuation == pending &&
        (frame.GiftedSlashGiftReceipt is not null && effect?.Op == SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward && ValidGiftedSlashGift(frame) ||
         frame.GiftedSlashRewardReceipt is not null && effect?.Op == SkillProgramEffectOp.RewardGiftedSlashUse && ValidGiftedSlashReward(frame));

    private bool GiftedSlashFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (frame.GiftedSlashGiftReceipt is not null ? !ValidGiftedSlashGift(frame) : !ValidGiftedSlashReward(frame)) return false;
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == frame.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == frame.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                state.CandidateIndex >= 0 && state.CandidateIndex <= state.Candidates.Count &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id &&
                    e.Change.ParentFrameId == frame.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        var gift = frame.GiftedSlashGiftReceipt;
        var before = gift?.Before ?? frame.GiftedSlashRewardReceipt!.Before;
        var after = gift?.After ?? frame.GiftedSlashRewardReceipt!.After;
        var reason = gift is not null ? GiftedSlashGiftReason(frame.SkillId) : GiftedSlashDrawReason(frame.SkillId);
        return child is CardsMovedTriggerWindowFrame moved && moved.ResumeProgramFrameId is null && moved.Batch.ParentFrameId == frame.Id &&
            moved.Batch.AwaitingProgramFrameId == frame.Id && moved.Batch.OriginOwnerSeat == frame.OwnerSeat && moved.Batch.OriginSkillId == frame.SkillId &&
            moved.Batch.OriginSkillInstanceId == frame.SkillInstanceId && (gift is null || moved.Batch.Id == gift.Policy.GiftBatchId) &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(move => _cardMovements.Contains(move) &&
                move.Sequence > before && move.Sequence <= after && move.Reason.Value == reason);
    }

    private ProgramSkillFrame? GiftedSlashObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !GiftedSlashFirstChild(root, _resolutionStack[index + 1])) continue;
            var exact = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (_resolutionStack[child] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[child - 1] is ProgramSkillFrame owner &&
                    changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
                    changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count) continue;
                if (!DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !HalfHandPaidDamageObserverEdge(child) && !PaidTargetObserverEdge(child)) { exact = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    ((IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)) || IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) ||
                     IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying))) break;
            }
            if (exact) return root;
        }
        return null;
    }

    private bool IsGiftedSlashProgramDying() => ActiveDying is { } dying && GiftedSlashObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasGiftedSlashDamageObserver(long id) =>
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) && GiftedSlashObserverRoot() is not null;
    private bool AllowsGiftedSlashNestedDamage(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || frame.AttackAttempt is not null || frame.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != frame.Id ||
            frame.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            GiftedSlashObserverRoot() is not { } root || root.Id == frame.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(frame, reference) : ResolveProgramEffectTarget(frame, effect.Target));
    }

    private sealed partial class ProgramSkillHost : IGiftedSlashUseRewardHost
    {
        public SkillProgramStepOutcome BeginGiftedSlashGift(SkillProgramEffect effect, ProgramSkillFrame frame, int recipientSeat) =>
            engine.BeginGiftedSlashGift(effect, frame, recipientSeat);
        public SkillProgramStepOutcome BeginGiftedSlashUseReward(SkillProgramEffect effect, ProgramSkillFrame frame) =>
            engine.BeginGiftedSlashUseReward(effect, frame);
        public bool CanContinueIssuedGiftedSlashReward(ProgramSkillFrame frame) => engine.CanContinueIssuedGiftedSlashReward(frame);
    }
}
