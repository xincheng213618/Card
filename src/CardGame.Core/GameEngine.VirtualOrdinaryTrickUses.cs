using System.Globalization;

namespace CardGame.Core;

public sealed record ProgramVirtualOrdinaryTrickOrigin(long ParentProgramFrameId, int InstructionIndex,
    long LifecycleFrameId, int OwnerSeat, int InitialActorSeat, int InitialTargetSeat,
    string SkillId, string BindingId, string SkillInstanceId, string GameplayHash, int TurnNumber,
    CardKind OutputKind, SkillProgramConditionKind ConditionKind, string? PindianBind, bool? FrozenSourceWon,
    long CardActionId = 0);

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IVirtualOrdinaryTrickProgramHost
    {
        public SkillProgramStepOutcome UseVirtualOrdinaryTrick(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BeginProgramVirtualOrdinaryTrick(frame, effect);
    }

    // A phase program must still be the exact candidate paused in its own actual
    // PlayEnding window. Merely having a similarly named context is insufficient.
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

    private SkillProgramEffect ValidateVirtualOrdinaryTrickInstruction(ProgramSkillFrame frame)
    {
        if (!IsExactOwnPlayEndingProgram(frame) || frame.InstructionIndex <= 0)
            throw new InvalidOperationException("A virtual ordinary trick lost its exact owning PlayEnding program.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.UseVirtualOrdinaryTrick ||
            effect.Target != SkillProgramEffectTarget.Owner ||
            effect.ActorReference?.Kind is not (ProgramParticipantRef.Owner or ProgramParticipantRef.SelectedTarget) ||
            effect.TargetReference?.Kind is not (ProgramParticipantRef.Owner or ProgramParticipantRef.SelectedTarget) ||
            effect.OutputKind is not (CardKind.DrawTwo or CardKind.Dismantlement) ||
            (effect.OutputKind == CardKind.DrawTwo) != (effect.ActorReference.Kind == effect.TargetReference.Kind) ||
            effect.Condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.PindianWon or SkillProgramConditionKind.PindianNotWon) ||
            (effect.ActorReference.Kind == ProgramParticipantRef.SelectedTarget ||
             effect.TargetReference.Kind == ProgramParticipantRef.SelectedTarget) && frame.SelectedTargetSeats.Count != 1)
            throw new InvalidOperationException("A virtual ordinary trick lost its exact references or output instruction.");
        if (effect.Condition.Kind != SkillProgramConditionKind.Always)
        {
            var result = frame.PindianResultBindings.SingleOrDefault(item => item.Name == effect.Condition.SourceBind);
            if (result is null || result.SourceSeat != frame.OwnerSeat ||
                frame.SelectedTargetSeats is not [var opponent] || result.OpponentSeat != opponent ||
                result.SourceWon != (effect.Condition.Kind == SkillProgramConditionKind.PindianWon))
                throw new InvalidOperationException("A virtual ordinary trick lost its frozen named Pindian branch.");
        }
        return effect;
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
            .Where(option => effect.OutputKind == CardKind.DrawTwo
                ? actorSeat == targetSeat && option.TargetSeats.Count == 0
                : option.TargetSeats.SequenceEqual([targetSeat])).ToArray();
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
        var frozen = effect.Condition.Kind == SkillProgramConditionKind.Always ? (bool?)null :
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
            !action.TargetSeats.SequenceEqual(use.TargetSeats) || use.TargetSeats.Count == 0 && !HasOverflowTargetCancellation(use) ||
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
            !IsExactOwnPlayEndingProgram(frame)) return false;
        var effect = ValidateVirtualOrdinaryTrickInstruction(frame);
        if (effect.OutputKind != origin.OutputKind || effect.Condition.Kind != origin.ConditionKind ||
            effect.Condition.SourceBind != origin.PindianBind ||
            ResolveProgramParticipant(frame, effect.ActorReference!) != origin.InitialActorSeat ||
            ResolveProgramParticipant(frame, effect.TargetReference!) != origin.InitialTargetSeat ||
            (origin.ConditionKind == SkillProgramConditionKind.Always ? origin.FrozenSourceWon is not null :
                frame.PindianResultBindings.Single(item => item.Name == origin.PindianBind).SourceWon != origin.FrozenSourceWon)) return false;
        // Provider and issuance remain frozen. The actual source may only move
        // along the existing typed, accepted actor-replacement producer chain.
        return MatchesDeclaredActualDamageUse(use, action.ActorSeat, origin.InitialActorSeat);
    }

    private bool IsProgramVirtualOrdinaryTrickUse(long frameId) =>
        LifecycleCardUse(frameId) is { } use && ValidProgramVirtualOrdinaryTrickUse(use);

    private bool MatchesVirtualOrdinaryTrickUseAction(long frameId, CardActionContext action, int cardId) =>
        cardId == 0 && IsProgramVirtualOrdinaryTrickUse(frameId) &&
        LifecycleCardUse(frameId)?.Action?.ActionId == action.ActionId && action.Type == CardActionType.Use &&
        action.EffectiveKind == LifecycleCardUse(frameId)!.CardKind && action.PhysicalCards.Count == 0 &&
        action.ConversionChain.Count == 0;

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
        AdvanceRuntimeProgram(frame.Id);
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
