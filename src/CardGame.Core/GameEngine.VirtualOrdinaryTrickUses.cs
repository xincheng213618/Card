using System.Globalization;

namespace CardGame.Core;

public sealed record ProgramVirtualOrdinaryTrickOrigin(long ParentProgramFrameId, int InstructionIndex,
    long LifecycleFrameId, int OwnerSeat, int InitialActorSeat, int InitialTargetSeat,
    string SkillId, string BindingId, string SkillInstanceId, string GameplayHash, int TurnNumber,
    CardKind OutputKind, SkillProgramConditionKind ConditionKind, string? PindianBind, bool? FrozenSourceWon,
    long CardActionId = 0);
public sealed record ProgramVirtualOrdinaryAttackReturnedEvent(long UseId, ProgramVirtualOrdinaryTrickOrigin Origin) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class VirtualOrdinaryTrickProgramCapability(GameEngine engine) : IVirtualOrdinaryTrickProgramHost
    {
        public SkillProgramStepOutcome UseVirtualOrdinaryTrick(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BeginProgramVirtualOrdinaryTrick(frame, effect);
    }

    private bool IsExactOwnPlayEndingProgram(ProgramSkillFrame frame)
    {
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (index < 1 || frame.TriggerId is null || frame.SelectedCardIds.Count != 0 ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.PlayEnding } context ||
            _resolutionStack[index - 1] is not ProgramLifecycleTriggerWindowFrame window ||
            window.Id != context.ParentFrameId || window.Window != context.Window ||
            window.OwnerSeat != frame.OwnerSeat || context.OwnerSeat != frame.OwnerSeat ||
            window.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            _turnProgression.OwnerSeat != _currentSeat || _turnProgression.TurnNumber != _turnNumber ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count)
            return false;
        var candidate = window.Candidates[window.CandidateIndex];
        return candidate.OwnerSeat == frame.OwnerSeat && candidate.SkillId == frame.SkillId &&
            candidate.BindingId == frame.TriggerId && frame.ActivationId == frame.TriggerId &&
            candidate.SkillInstanceId == frame.SkillInstanceId && candidate.GameplayHash == frame.GameplayHash &&
            candidate.OccurrenceIndex == context.OccurrenceIndex;
    }

    // A virtual use retains the exact candidate paused in its own actual phase
    // boundary. A matching context name without that parent is insufficient.
    private bool IsExactOwnVirtualOrdinaryTrickProgram(ProgramSkillFrame frame)
    {
        if (frame.WindowContext?.Window == SkillProgramTriggerWindow.GameStarting) return IsExactHellArrivalProgram(frame);
        if (frame.WindowContext?.Window == SkillProgramTriggerWindow.PlayEnding)
            return IsExactOwnPlayEndingProgram(frame);
        if (frame.WindowContext?.Window is SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.AfterTurnEnded)
            return IsExactOwnVirtualTrickTurnBoundary(frame);
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (index < 1 || frame.TriggerId is null || frame.SelectedCardIds.Count != 0 ||
            frame.WindowContext is not { } context || context.OwnerSeat != frame.OwnerSeat ||
            frame.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            _turnProgression.OwnerSeat != _currentSeat || _turnProgression.TurnNumber != _turnNumber)
            return false;
        if (!(context.Window == SkillProgramTriggerWindow.PlayPhaseStarting &&
            context.SourceSeat == frame.OwnerSeat && context.TargetSeat == frame.OwnerSeat &&
            _resolutionStack[index - 1] is PlayPhaseStartingBoundaryFrame starting &&
            starting.Id == context.ParentFrameId && starting.OwnerSeat == frame.OwnerSeat &&
            starting.ItemIndex >= 0 && starting.ItemIndex < starting.Items.Count &&
            starting.Items[starting.ItemIndex] is { Kind: TurnEndingBoundaryItemKind.Program, Candidate: { } candidate }))
            return false;
        return candidate.OwnerSeat == frame.OwnerSeat && candidate.SkillId == frame.SkillId &&
            candidate.BindingId == frame.TriggerId && frame.ActivationId == frame.TriggerId &&
            candidate.SkillInstanceId == frame.SkillInstanceId && candidate.GameplayHash == frame.GameplayHash &&
            candidate.OccurrenceIndex == context.OccurrenceIndex;
    }

    private SkillProgramEffect ValidateVirtualOrdinaryTrickInstruction(ProgramSkillFrame frame)
    {
        if (!IsExactOwnVirtualOrdinaryTrickProgram(frame) || frame.InstructionIndex <= 0)
            throw new InvalidOperationException("A virtual ordinary trick lost its exact owning phase program.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        var choulue = IsGuanduChoulueVirtualOrdinaryProgram(frame, effect);
        if (effect.Op != SkillProgramEffectOp.UseVirtualOrdinaryTrick ||
            effect.Target != SkillProgramEffectTarget.Owner ||
            !choulue && (
            !UseVirtualOrdinaryTrickProgramOperationDescriptor.HasValidReferences(effect.OutputKind,
                effect.ActorReference?.Kind, effect.TargetReference?.Kind) ||
            !UseVirtualOrdinaryTrickProgramOperationDescriptor.AllowsWindow(effect.OutputKind, frame.WindowContext!.Window) ||
            UseVirtualOrdinaryTrickProgramOperationDescriptor.IsGroupOutput(effect.OutputKind) && effect.Condition.Kind != SkillProgramConditionKind.Always && !IsHellRandomAssaultBranch(frame, effect) ||
            effect.Condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.PindianWon or SkillProgramConditionKind.PindianNotWon) && !(effect.OutputKind == CardKind.Duel && effect.Condition.Kind == SkillProgramConditionKind.ChoiceIs) && !IsHellRandomAssaultBranch(frame, effect) ||
            (effect.ActorReference?.Kind == ProgramParticipantRef.SelectedTarget ||
             effect.TargetReference?.Kind == ProgramParticipantRef.SelectedTarget) && frame.SelectedTargetSeats.Count != 1)
            )
            throw new InvalidOperationException("A virtual ordinary trick lost its exact references or output instruction.");
        if (effect.Condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.ChoiceIs) && !IsHellRandomAssaultBranch(frame, effect))
        {
            var result = frame.PindianResultBindings.SingleOrDefault(item => item.Name == effect.Condition.SourceBind);
            if (result is null || result.SourceSeat != frame.OwnerSeat ||
                frame.SelectedTargetSeats is not [var opponent] || result.OpponentSeat != opponent ||
                result.SourceWon != (effect.Condition.Kind == SkillProgramConditionKind.PindianWon))
                throw new InvalidOperationException("A virtual ordinary trick lost its frozen named Pindian branch.");
        }
        return effect;
    }

    private bool IsExactOwnVirtualTrickTurnBoundary(ProgramSkillFrame frame)
    {
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (index < 1 || frame.TriggerId is null || frame.SelectedCardIds.Count != 0 ||
            frame.WindowContext is not { } context || context.OwnerSeat != frame.OwnerSeat ||
            context.SourceSeat != frame.OwnerSeat || context.TargetSeat != frame.OwnerSeat ||
            frame.OwnerSeat != _currentSeat || _turnProgression.OwnerSeat != _currentSeat ||
            _turnProgression.TurnNumber != _turnNumber) return false;
        ProgramTriggerCandidate? candidate = null;
        if (context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && _phase == TurnPhase.Draw &&
            _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame start && start.Id == context.ParentFrameId &&
            start.Window == context.Window && start.OwnerSeat == frame.OwnerSeat &&
            start.CandidateIndex >= 0 && start.CandidateIndex < start.Candidates.Count)
            candidate = start.Candidates[start.CandidateIndex];
        if (context.Window == SkillProgramTriggerWindow.AfterTurnEnded &&
            _resolutionStack[index - 1] is DeferredTurnEndFrame ending && ending.OwnerSeat == frame.OwnerSeat &&
            MatchesAfterTurnEndedChild(ending, frame, context))
            candidate = AfterTurnEndedCandidate(ending);
        return candidate is not null && candidate.OwnerSeat == frame.OwnerSeat && candidate.SkillId == frame.SkillId &&
            candidate.BindingId == frame.TriggerId && frame.ActivationId == frame.TriggerId &&
            candidate.SkillInstanceId == frame.SkillInstanceId && candidate.GameplayHash == frame.GameplayHash &&
            candidate.OccurrenceIndex == context.OccurrenceIndex;
    }

    private IReadOnlyList<ProgramOrdinaryTrickUseOption> VirtualOrdinaryTrickOptions(ProgramSkillFrame frame,
        SkillProgramEffect effect)
    {
        var actorSeat = ResolveProgramParticipant(frame, effect.ActorReference!);
        var targetSeat = ResolveProgramParticipant(frame, effect.TargetReference!);
        if (!IsValidPlayerSeat(actorSeat) || !IsValidPlayerSeat(targetSeat) ||
            !_players[actorSeat].IsAlive || !_players[targetSeat].IsAlive || _winner != Winner.None)
            return [];
        var actor = _players[actorSeat];
        var options = BuildProgramOrdinaryTrickUseOptions(actor, effect.OutputKind, Suit.None,
            enforceUsePermission: true, beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null,
            hasActualColor: true, physicalCardIds: [])
            .Where(option => IsGuanduChoulueVirtualOrdinaryProgram(frame, effect) || (UseVirtualOrdinaryTrickProgramOperationDescriptor.IsGroupOutput(effect.OutputKind)
                ? actorSeat == targetSeat && option.TargetSeats.Count > 0
                : effect.OutputKind == CardKind.DrawTwo
                    ? actorSeat == targetSeat && option.TargetSeats.Count == 0
                    : option.TargetSeats.SequenceEqual([targetSeat]))).ToArray();
        return NextActualUseOrdinaryTrickOptions(actor, Array.AsReadOnly(options), Suit.None, null, Suit.None, false);
    }

    private SkillProgramStepOutcome BeginProgramVirtualOrdinaryTrick(ProgramSkillFrame input, SkillProgramEffect supplied)
    {
        var frame = GetActiveProgramFrame(input.Id);
        var effect = ValidateVirtualOrdinaryTrickInstruction(frame);
        if (frame.OwnerSeat != input.OwnerSeat || frame.SkillId != input.SkillId ||
            frame.SkillInstanceId != input.SkillInstanceId || effect != supplied ||
            _resolutionStack.LastOrDefault()?.Id != frame.Id || HasPendingProgramBoundCards ||
            _cardZones.Count(CardLocation.Processing) != 0)
            throw new InvalidOperationException("A virtual ordinary trick requires its unpaid active program instruction.");
        var options = VirtualOrdinaryTrickOptions(frame, effect);
        if (options.Count == 0) return SkillProgramStepOutcome.Continue;
        if (options.Count == 1)
        {
            IssueProgramVirtualOrdinaryTrick(frame, effect, options[0]);
            return SkillProgramStepOutcome.AwaitChild;
        }
        var actorSeat = ResolveProgramParticipant(frame, effect.ActorReference!);
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, actorSeat,
            $"【{skill.Name}】请选择【{CardCatalog.Get(effect.OutputKind!.Value).DisplayName}】的合法用法。",
            [], options.SelectMany(option => option.TargetSeats).Distinct().ToArray(), SourceSeat: actorSeat)
        {
            PromptId = CreatePromptId(),
            SkillPrompt = new(frame.SkillId, skill.Name, $"{skill.Name} · 使用锦囊", skill.Description),
            Choices = options.Select(option => VirtualOrdinaryTrickChoice(frame, option)).ToArray()
        };
        _status = _players[actorSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private static PromptChoice VirtualOrdinaryTrickChoice(ProgramSkillFrame frame, ProgramOrdinaryTrickUseOption option) =>
        new(option.Id, option.Description, [], option.TargetSeats, new Dictionary<string, string>
        {
            ["program-action"] = "use-virtual-ordinary-trick",
            ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture)
        });

    [ProgramAnswer("use-virtual-ordinary-trick")]
    private void ResolveProgramVirtualOrdinaryTrickChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("A virtual ordinary-trick choice lost its owning program.");
        var effect = ValidateVirtualOrdinaryTrickInstruction(frame);
        var actorSeat = ResolveProgramParticipant(frame, effect.ActorReference!);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision || decision.PlayerSeat != actorSeat ||
            selected.Parameters.GetValueOrDefault("program-action") != "use-virtual-ordinary-trick" ||
            selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(CultureInfo.InvariantCulture))
            throw new InvalidOperationException("A virtual ordinary-trick choice lost its exact actor or instruction.");
        var option = VirtualOrdinaryTrickOptions(frame, effect).SingleOrDefault(option => option.Id == selected.Id);
        if (option is null || !option.TargetSeats.SequenceEqual(selected.Targets))
            throw new InvalidOperationException("A virtual ordinary-trick choice is no longer legal.");
        ClearPendingDecision();
        IssueProgramVirtualOrdinaryTrick(frame, effect, option);
    }

    private void IssueProgramVirtualOrdinaryTrick(ProgramSkillFrame frame, SkillProgramEffect effect,
        ProgramOrdinaryTrickUseOption option)
    {
        var actorSeat = ResolveProgramParticipant(frame, effect.ActorReference!);
        var targetSeat = ResolveProgramParticipant(frame, effect.TargetReference!);
        var frozen = effect.Condition.Kind is SkillProgramConditionKind.Always or SkillProgramConditionKind.ChoiceIs ? (bool?)null :
            frame.PindianResultBindings.Single(item => item.Name == effect.Condition.SourceBind).SourceWon;
        var origin = new ProgramVirtualOrdinaryTrickOrigin(frame.Id, frame.InstructionIndex,
            frame.WindowContext!.ParentFrameId, frame.OwnerSeat, actorSeat, targetSeat, frame.SkillId,
            frame.TriggerId!, frame.SkillInstanceId, frame.GameplayHash, _turnNumber, effect.OutputKind!.Value,
            effect.Condition.Kind, effect.Condition.SourceBind, frozen);
        var card = new Card(0, effect.OutputKind.Value, Suit.None, 0);
        if (option.NextActualUseAdjusted) _selectedNextCardTargetSeats = option.TargetSeats;
        var targets = effect.OutputKind == CardKind.DrawTwo && option.TargetSeats.Count == 0
            ? new[] { targetSeat } : option.TargetSeats;
        var id = BeginCardUse(card, actorSeat, targets, effect.OutputKind, physicalCardIds: [],
            virtualOrdinaryTrickOrigin: origin);
        BeginJizhiOrNullificationWindow(id, card, actorSeat, LifecycleCardUse(id)!.TargetSeats,
            option.ActionKind, option.TargetCardId, option.RequiredCardKind, effect.OutputKind);
    }

    [NativeUseValidation(100)]
    private void ValidateVirtualOrdinaryTrickReturn(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not null && !ValidProgramVirtualOrdinaryTrickUse(use))
            throw new InvalidOperationException("A virtual ordinary trick lost its issuance before completion.");
    }
    private bool ValidProgramVirtualOrdinaryTrickUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { } origin || use.CardId != 0 ||
            use.CardKind != origin.OutputKind || use.PhysicalCardIds is not { Count: 0 } ||
            use.SelectedActorDuelOrigin is not null || use.DamageTargetDuelOrigin is not null ||
            use.DualColorDuelOrigin is not null || use.ConditionalDiscardDuelOrigin is not null ||
            use.TieredRoundConversionUse is not null || use.VirtualBasicReturn is not null ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0,
                EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: not true, RequesterSeat: null,
                ResponderSeat: null, OpponentSeat: null } action ||
            action.EffectiveKind != origin.OutputKind || action.ActionId != origin.CardActionId ||
            !action.TargetSeats.SequenceEqual(use.TargetSeats) || use.TargetSeats.Count == 0 && !(HasHuaManSouyingCancellation(use) || HasOverflowTargetCancellation(use)) ||
            use.TargetSeats.Distinct().Count() != use.TargetSeats.Count ||
            use.TargetSeats.Any(seat => !IsValidPlayerSeat(seat)) ||
            action.ProviderSeat != origin.InitialActorSeat || origin.TurnNumber != _turnNumber ||
            !IsValidPlayerSeat(origin.InitialActorSeat) || !IsValidPlayerSeat(origin.InitialTargetSeat)) return false;
        var index = _resolutionStack.FindIndex(item => item.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame frame ||
            frame.Id != origin.ParentProgramFrameId || frame.InstructionIndex != origin.InstructionIndex ||
            frame.OwnerSeat != origin.OwnerSeat || frame.SkillId != origin.SkillId ||
            frame.TriggerId != origin.BindingId || frame.SkillInstanceId != origin.SkillInstanceId ||
            frame.GameplayHash != origin.GameplayHash || frame.WindowContext?.ParentFrameId != origin.LifecycleFrameId ||
            !IsExactOwnVirtualOrdinaryTrickProgram(frame)) return false;
        var effect = ValidateVirtualOrdinaryTrickInstruction(frame);
        if (effect.OutputKind != origin.OutputKind || effect.Condition.Kind != origin.ConditionKind ||
            effect.Condition.SourceBind != origin.PindianBind ||
            ResolveProgramParticipant(frame, effect.ActorReference!) != origin.InitialActorSeat ||
            ResolveProgramParticipant(frame, effect.TargetReference!) != origin.InitialTargetSeat ||
            (origin.ConditionKind is SkillProgramConditionKind.Always or SkillProgramConditionKind.ChoiceIs ? origin.FrozenSourceWon is not null :
                frame.PindianResultBindings.Single(item => item.Name == origin.PindianBind).SourceWon != origin.FrozenSourceWon)) return false;
        // Provider and issuance remain frozen. The actual source may only move
        // along the existing typed, accepted actor-replacement producer chain.
        return MatchesDeclaredActualDamageUse(use, action.ActorSeat, origin.InitialActorSeat);
    }

    private bool IsProgramVirtualOrdinaryTrickUse(long frameId) =>
        LifecycleCardUse(frameId) is { } use && ValidProgramVirtualOrdinaryTrickUse(use);

    private Card ReadVirtualOrdinaryTrickUseAppearance(long ownerId, CardAppearanceReference appearance)
    {
        if (LifecycleCardUse(ownerId) is not { VirtualOrdinaryTrickOrigin: not null } use)
            return ReadMouLiuChengUseAppearance(ownerId, appearance);
        if (!ValidProgramVirtualOrdinaryTrickUse(use) || appearance.Id != 0 ||
            appearance.Kind != use.CardKind || appearance.Suit != Suit.None || appearance.Rank != 0)
            throw new InvalidOperationException("A virtual ordinary trick appearance lost its exact issued use.");
        return new Card(0, use.CardKind, Suit.None, 0);
    }

    private bool IsProgramVirtualAreaAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (IsProgramVirtualSingleAttackConsistent(attack, processing)) return true;
        if (LifecycleCardUse(attack.ResolutionId) is not { } use || !ValidProgramVirtualOrdinaryTrickUse(use) ||
            !UseVirtualOrdinaryTrickProgramOperationDescriptor.IsGroupOutput(use.CardKind) ||
            ActiveGroupCard is not { Effect: GroupCardEffect.ResponseAttack, CurrentAttack: { } current } group ||
            group.ResolutionId != use.Id || !SameAttackOwner(current, attack) || group.SourceSeat != use.SourceSeat ||
            group.DamageSourceSeat != attack.SourceSeat || group.TargetIndex < 0 || group.TargetIndex >= group.TargetSeats.Count ||
            !group.TargetSeats.SequenceEqual(use.TargetSeats) && !IsZixiExtraEffectTargetSubset(use, group.TargetSeats) ||
            group.RequiredCardKind != (use.CardKind == CardKind.BarbarianAssault ? CardKind.Slash : CardKind.Dodge))
            return false;
        var originalTarget = group.TargetSeats[group.TargetIndex];
        var targetMatches = !attack.DamageRedirected ? attack.TargetSeat == originalTarget :
            CompleteProgramEventHistory().OfType<ProgramDamageTransferredEvent>().Count(e =>
                e.ResolutionId == use.Id && e.SourceSeat == group.DamageSourceSeat &&
                e.OwnerSeat == originalTarget && e.TargetSeat == attack.TargetSeat &&
                e.DamageAmount > 0 && e.Nature == GetDamageNature(attack)) == 1;
        return targetMatches && processing.Count == 0 && group.PhysicalCards.Count == 0 && attack.Card is null &&
            attack.PhysicalCards.Count == 0 && attack.EffectiveCardKind == use.CardKind &&
            attack.ProgramSkillFrameId is null && attack.ProgramSkillCardUseFrameId is null &&
            attack.ProgramJudgmentFrameId is null && !attack.IsDelayedJudgmentDamage && !attack.IsSourceLess;
    }

    private bool IsProgramVirtualSingleAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { } use || !ValidProgramVirtualOrdinaryTrickUse(use) ||
            use.CardKind is not (CardKind.Duel or CardKind.FireAttack or CardKind.UnexpectedAssault) ||
            attack.PhysicalCards.Count != 0 || processing.Count != 0 || attack.EffectiveCardKind != use.CardKind ||
            attack.CardUserSeat != use.SourceSeat || attack.IsSourceLess || attack.IsDelayedJudgmentDamage ||
            attack.ProgramSkillFrameId is not null || attack.ProgramJudgmentFrameId is not null ||
            ActiveCardAttack is not { } current || !SameAttackOwner(current, attack) ||
            attack.Card is { } card && (card.Id != 0 || card.Kind != use.CardKind || card.Suit != Suit.None || card.Rank != 0)) return false;
        if (use.CardKind == CardKind.Duel)
            return ActiveDuel is { } duel && duel.ResolutionId == use.Id && SameAttackOwner(duel.Attack, attack) &&
                use.TargetIndex >= 0 && use.TargetIndex < use.TargetSeats.Count &&
                (attack.SourceSeat == use.SourceSeat && VirtualTrickDamageTargetMatches(use, attack, use.TargetSeats[use.TargetIndex]) ||
                 attack.SourceSeat == use.TargetSeats[use.TargetIndex] && VirtualTrickDamageTargetMatches(use, attack, use.SourceSeat));
        if (attack.SourceSeat != use.SourceSeat) return false;
        if (use.CardKind == CardKind.FireAttack)
            return ProgramEventHistory<FireAttackResolvedEvent>().LastOrDefault(e => e.ResolutionId == use.Id) is
                { CausedDamage: true, MatchingDiscardCardId: > 0 } paid && paid.SourceSeat == use.SourceSeat &&
                use.TargetSeats.Contains(paid.TargetSeat) && VirtualTrickDamageTargetMatches(use, attack, paid.TargetSeat);
        return use.UnexpectedAssaultReveal is { CanCauseDamage: true } reveal && reveal.CardUseFrameId == use.Id &&
            reveal.ActionId == use.Action!.ActionId && reveal.TargetIndex == use.TargetIndex &&
            ProgramEventHistory<UnexpectedAssaultRevealedEvent>().Count(e => e.Receipt == reveal) == 1 &&
            VirtualTrickDamageTargetMatches(use, attack, reveal.TargetSeat);
    }
    private bool VirtualTrickDamageTargetMatches(CardUseFrame use, CardAttackHandle attack, int original) =>
        attack.TargetSeat == original && !attack.DamageRedirected ||
        attack.DamageRedirected && ProgramEventHistory<ProgramDamageTransferredEvent>().Any(e => e.ResolutionId == use.Id &&
            e.SourceSeat == attack.SourceSeat && e.OwnerSeat == original && e.TargetSeat == attack.TargetSeat && e.DamageAmount > 0 && e.Nature == GetDamageNature(attack)) ||
        attack.IsChainPropagation && ProgramEventHistory<ChainedDamagePropagatedEvent>().Any(e => e.ResolutionId == use.Id &&
            e.SourceSeat == attack.SourceSeat && e.TargetSeat == attack.TargetSeat && e.Nature == GetDamageNature(attack));

    private bool FinishProgramVirtualOrdinaryAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (LifecycleCardUse(attack.ResolutionId) is not { } use || !ValidProgramVirtualOrdinaryTrickUse(use) ||
            use.CardKind is not (CardKind.Duel or CardKind.FireAttack or CardKind.UnexpectedAssault) ||
            attack.EffectiveCardKind != use.CardKind || attack.PhysicalCards.Count != 0 ||
            ActiveCardAttack is not { } current || !SameAttackOwner(current, attack)) return false;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed);
        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted,
            use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }

    private bool MatchesVirtualOrdinaryTrickUseAction(long frameId, CardActionContext action, int cardId) =>
        cardId == 0 && IsProgramVirtualOrdinaryTrickUse(frameId) &&
        LifecycleCardUse(frameId)?.Action?.ActionId == action.ActionId && action.Type == CardActionType.Use &&
        action.EffectiveKind == LifecycleCardUse(frameId)!.CardKind && action.PhysicalCards.Count == 0 &&
        action.ConversionChain.Count == 0;

    [NativeUseReturn(210)]
    private void ReturnProgramVirtualOrdinaryTrick(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { } origin) return;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != origin.ParentProgramFrameId ||
            frame.InstructionIndex != origin.InstructionIndex || frame.OwnerSeat != origin.OwnerSeat ||
            frame.SkillId != origin.SkillId || frame.TriggerId != origin.BindingId ||
            frame.SkillInstanceId != origin.SkillInstanceId || frame.GameplayHash != origin.GameplayHash ||
            use.Step != ResolutionFrameStep.Completed || frame.WindowContext?.ParentFrameId != origin.LifecycleFrameId ||
            ValidateVirtualOrdinaryTrickInstruction(frame).OutputKind != origin.OutputKind)
            throw new InvalidOperationException("A completed virtual ordinary trick lost its typed program return.");
        if (use.CardKind is CardKind.Duel or CardKind.FireAttack or CardKind.UnexpectedAssault && use.CardAttack is { Active: true })
        {
            if (ProgramEventHistory<ProgramVirtualOrdinaryAttackReturnedEvent>().Any(e => e.UseId == use.Id))
                throw new InvalidOperationException("A virtual ordinary attack cannot return twice.");
            AdvanceEventRulesAndQueueFact(new ProgramVirtualOrdinaryAttackReturnedEvent(use.Id, origin));
            return;
        }
        AdvanceRuntimeProgram(frame.Id);
    }

    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 44001)]
    private bool ResumeProgramVirtualOrdinaryAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || GetPausedPrivateOfferEffect(frame)?.Op != SkillProgramEffectOp.UseVirtualOrdinaryTrick ||
            ProgramEventHistory<ProgramVirtualOrdinaryAttackReturnedEvent>().LastOrDefault() is not { Origin: { } origin } returned ||
            origin.ParentProgramFrameId != frame.Id || origin.InstructionIndex != frame.InstructionIndex ||
            _resolutionStack.Any(f => f.Id == returned.UseId) || frame.SkillId != origin.SkillId || frame.SkillInstanceId != origin.SkillInstanceId ||
            ValidateVirtualOrdinaryTrickInstruction(frame).OutputKind != origin.OutputKind) return false;
        AdvanceRuntimeProgram(frame.Id); return true;
    }

    private void AssertProgramVirtualOrdinaryTricks()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(use => use.VirtualOrdinaryTrickOrigin is not null))
            if (!ValidProgramVirtualOrdinaryTrickUse(use))
                throw new InvalidOperationException("A virtual ordinary trick lost its issued action, frozen branch or typed owner.");
        foreach (var frame in _resolutionStack.OfType<ProgramSkillFrame>())
        {
            if (_resolutionStack.LastOrDefault()?.Id != frame.Id || frame.InstructionIndex <= 0) continue;
            var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
                .GetPausedInstruction(frame.InstructionIndex).Effect;
            if (effect.Op != SkillProgramEffectOp.UseVirtualOrdinaryTrick) continue;
            ValidateVirtualOrdinaryTrickInstruction(frame);
            var actor = ResolveProgramParticipant(frame, effect.ActorReference!);
            var options = VirtualOrdinaryTrickOptions(frame, effect);
            if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
                decision.PlayerSeat != actor || decision.SkillPrompt?.SkillId != frame.SkillId || options.Count < 2 ||
                !decision.Choices.Select(choice => choice.Id).SequenceEqual(options.Select(option => option.Id)) ||
                decision.Choices.Where((choice, index) => !choice.Targets.SequenceEqual(options[index].TargetSeats)).Any() ||
                decision.Choices.Any(choice => choice.Cards.Count != 0 ||
                    choice.Parameters.GetValueOrDefault("program-action") != "use-virtual-ordinary-trick" ||
                    choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(CultureInfo.InvariantCulture)))
                throw new InvalidOperationException("A virtual ordinary trick lost its exact legal choice prompt.");
        }
    }
}
