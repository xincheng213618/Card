namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IFinalTargetTurnCountHost
    {
        public SkillProgramStepOutcome ExecuteFinalTargetTurnCount(SkillProgramEffect effect, ProgramSkillFrame frame) =>
            engine.ExecuteFinalTargetTurnCount(effect, frame);
    }
    private int GetNamedTurnSkillCount(int seat, string skill) => CompleteProgramEventHistory()
        .OfType<NamedTurnSkillChoiceCommittedEvent>().Count(e => e.OwnerSeat == seat && e.SkillId == skill && e.TurnNumber == _turnNumber);
    private bool NamedTurnActionAlreadyCommitted(int seat, string skill, long actionId) => CompleteProgramEventHistory()
        .OfType<NamedTurnSkillChoiceCommittedEvent>().Any(e => e.OwnerSeat == seat && e.SkillId == skill && e.ActionId == actionId);
    private CardUseFrame NonFinalTargetUse(ProgramSkillFrame frame)
    {
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } use } ||
            use.ActorSeat != frame.OwnerSeat || _currentSeat != frame.OwnerSeat || EquipmentCatalog.IsEquipment(use.EffectiveKind) ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == use.ParentCardUseFrameId) is not { Action: { Type: CardActionType.Use } action } parent ||
            action.ActionId != use.CardActionId || action.ActorSeat != frame.OwnerSeat || action.EffectiveKind != use.EffectiveKind)
            throw new InvalidOperationException("Non-final-target payment lost its actual own-turn non-equipment use.");
        return parent;
    }
    private bool CanOfferNonFinalTargetPayment(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        context.CardUse is { ActorSeat: var actor, EffectiveKind: var kind } use && actor == candidate.OwnerSeat && actor == _currentSeat &&
        !EquipmentCatalog.IsEquipment(kind) && !NamedTurnActionAlreadyCommitted(actor, candidate.SkillId, use.CardActionId) &&
        GetProgramTargetSeats(actor, SkillProgramTargetKind.CurrentCardUseTargets, context) is { } targets &&
        _players.Any(p => p.IsAlive && !targets.Contains(p.Seat) && GetHand(p).Count + GetEquipment(p).Count > 0);
    private IReadOnlyList<PromptChoice> NonFinalTargetChoices(ProgramSkillFrame frame)
    {
        var targets = NonFinalTargetUse(frame).TargetSeats;
        return _players.Where(p => p.IsAlive && !targets.Contains(p.Seat)).SelectMany(p =>
            BuildOwnedCardPaymentChoices(frame.Id, frame.OwnerSeat, p.Seat, [CardZoneKind.Hand, CardZoneKind.Equipment])
            .Select(choice => choice with
            {
                Id = new($"outside-target.frame-{frame.Id}.owner-{p.Seat}.{choice.Id.Value}"),
                Description = p.Name + "：" + choice.Description, Targets = [p.Seat],
                Parameters = new Dictionary<string, string>(choice.Parameters) { ["program-action"] = "named-turn-flow" }
            })).ToArray();
    }
    private SkillProgramStepOutcome ExecuteFinalTargetTurnCount(SkillProgramEffect effect, ProgramSkillFrame frame)
    {
        if (frame.NamedTurnCountFlow is not null) throw new InvalidOperationException("Turn-count flow cannot restart a paid cursor.");
        if (effect.Op == SkillProgramEffectOp.DiscardNonFinalTargetCardThenDraw)
        {
            if (NamedTurnActionAlreadyCommitted(frame.OwnerSeat, frame.SkillId, NonFinalTargetUse(frame).Action!.ActionId)) return SkillProgramStepOutcome.Continue;
            var choices = NonFinalTargetChoices(frame); if (choices.Count == 0) return SkillProgramStepOutcome.Continue;
            PublishNamedTurnFlow(frame, choices, "弃置本次用牌全部最终目标之外一名角色的一张手牌或装备牌，然后令其摸一张牌。", true);
            return SkillProgramStepOutcome.AwaitChoice;
        }
        var cards = GetHand(_players[frame.OwnerSeat]).Select(c => c.Id).ToArray();
        var required = Math.Max(0, cards.Length - GetNamedTurnSkillCount(frame.OwnerSeat, effect.SkillIds.Single()));
        if (required == 0) return SkillProgramStepOutcome.Continue;
        frame = frame with { NamedTurnCountFlow = new("hand", frame.OwnerSeat, required, cards, []) }; ReplaceRuntimeTop(frame);
        PublishNamedTurnHandDiscard(frame); return SkillProgramStepOutcome.AwaitChoice;
    }
    private void PublishNamedTurnFlow(ProgramSkillFrame frame, IReadOnlyList<PromptChoice> choices, string message, bool privateChoice)
    {
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, message,
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = privateChoice, Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void PublishNamedTurnHandDiscard(ProgramSkillFrame frame)
    {
        var draft = frame.NamedTurnCountFlow!;
        var choices = draft.CardIds.Except(draft.SelectedIds).Select(id =>
        {
            var card = GetHand(_players[frame.OwnerSeat]).Single(c => c.Id == id);
            return new PromptChoice(new($"named-turn-hand.frame-{frame.Id}.card-{id}"), "弃置【" + PublicPileCardLabel(card) + "】", [id], [],
                new Dictionary<string, string> { ["program-action"] = "named-turn-flow", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }).ToArray();
        PublishNamedTurnFlow(frame, choices, $"请选择 {draft.Required - draft.SelectedIds.Count} 张手牌弃置。", true);
    }
    private void ResolveNamedTurnFlowChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Turn-count choice lost its frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            _pendingDecision is not { PlayerSeat: var chooser } || chooser != frame.OwnerSeat || !_pendingDecision.Choices.Any(c => c.Id == selected.Id))
            throw new InvalidOperationException("Turn-count choice lost its published cursor.");
        if (!_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        { ClearPendingDecision(); CancelProgramBindingAndCleanup(frame, "技能来源已失效。"); return; }
        if (effect.Op == SkillProgramEffectOp.DiscardNonFinalTargetCardThenDraw)
        {
            var use = NonFinalTargetUse(frame);
            if (NamedTurnActionAlreadyCommitted(frame.OwnerSeat, frame.SkillId, use.Action!.ActionId)) throw new InvalidOperationException("Turn action was already committed.");
            var valid = NonFinalTargetChoices(frame).SingleOrDefault(c => c.Id == selected.Id) ?? throw new InvalidOperationException("Non-target payment is no longer valid.");
            var seat = valid.Targets.Single(); var from = new CardLocation(Enum.Parse<CardZoneKind>(valid.Parameters["source-zone"]), seat);
            var card = _cardZones.CardsAt(from)[int.Parse(valid.Parameters["slot-index"], System.Globalization.CultureInfo.InvariantCulture)];
            ClearPendingDecision();
            frame = frame with { NamedTurnCountFlow = new("target-movement", seat, 1, [], [], use.Action.ActionId), PendingMovementContinuation = new(frame.OwnerSeat, 0, null) };
            ReplaceRuntimeTop(frame);
            AdvanceEventRulesAndQueueFact(new NamedTurnSkillChoiceCommittedEvent(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, frame.TriggerId!, _turnNumber,
                use.Action.ActionId, frame.WindowContext!.ParentFrameId, seat));
            MoveCard(card, from, CardLocation.DiscardPile, new("skill-program.non-final-target.discard"));
            if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
            return;
        }
        if (effect.Op != SkillProgramEffectOp.DiscardHandToNamedTurnCount || frame.NamedTurnCountFlow is not { Stage: "hand" } draft ||
            selected.Cards is not [var id] || selected.Targets.Count != 0 || !draft.CardIds.Except(draft.SelectedIds).Contains(id) || _cardZones.GetLocation(id) != CardLocation.Hand(frame.OwnerSeat))
            throw new InvalidOperationException("Named-count discard lost actual hand payment.");
        ClearPendingDecision(); draft = draft with { SelectedIds = draft.SelectedIds.Append(id).ToArray() };
        frame = frame with { NamedTurnCountFlow = draft }; ReplaceRuntimeTop(frame);
        if (draft.SelectedIds.Count < draft.Required) { PublishNamedTurnHandDiscard(frame); return; }
        if (draft.SelectedIds.Any(cardId => _cardZones.GetLocation(cardId) != CardLocation.Hand(frame.OwnerSeat))) throw new InvalidOperationException("Named-count hand cost moved before commit.");
        ReplaceRuntimeTop(frame with { NamedTurnCountFlow = draft with { Stage = "hand-movement" }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveCards(GetHand(_players[frame.OwnerSeat]).Where(c => draft.SelectedIds.Contains(c.Id)).ToArray(), CardLocation.Hand(frame.OwnerSeat), CardLocation.DiscardPile, new("skill-program.named-turn-count.discard"));
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }
    private bool ResumeNamedTurnFlow(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId); var draft = frame.NamedTurnCountFlow; if (draft is null) return false;
        if (draft.Stage == "target-movement")
        {
            if (!_players[draft.TargetSeat].IsAlive) { ReplaceRuntimeTop(frame with { NamedTurnCountFlow = null }); return false; }
            ReplaceRuntimeTop(frame with { NamedTurnCountFlow = draft with { Stage = "target-draw" } });
            DrawProgramCards(frame.Id, draft.TargetSeat, 1, null, null, SkillProgramCardSetVisibility.Private, new("skill-program.non-final-target.draw"));
            if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
            return true;
        }
        if (draft.Stage is "target-draw" or "hand-movement") { ReplaceRuntimeTop(frame with { NamedTurnCountFlow = null }); return false; }
        return false;
    }
    private void AssertNamedTurnFlow(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (frame.NamedTurnCountFlow is not { } draft) return;
        var payment = effect.Op == SkillProgramEffectOp.DiscardNonFinalTargetCardThenDraw;
        if ((!payment && effect.Op != SkillProgramEffectOp.DiscardHandToNamedTurnCount) ||
            draft.Stage is not ("target-movement" or "target-draw" or "hand" or "hand-movement") ||
            payment != (draft.Stage is "target-movement" or "target-draw") || !IsValidPlayerSeat(draft.TargetSeat) || draft.Required < 0 ||
            draft.CardIds.Distinct().Count() != draft.CardIds.Count || draft.SelectedIds.Distinct().Count() != draft.SelectedIds.Count ||
            draft.SelectedIds.Any(id => !draft.CardIds.Contains(id)) || draft.SelectedIds.Count > draft.Required)
            throw new InvalidOperationException("Named-turn flow lost its typed stage and payment cursor.");
        if (payment && (draft.ActionId != NonFinalTargetUse(frame).Action!.ActionId || !NamedTurnActionAlreadyCommitted(frame.OwnerSeat, frame.SkillId, draft.ActionId!.Value)))
            throw new InvalidOperationException("Named-turn flow lost its committed action.");
        if (draft.Stage == "hand" && (draft.CardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(frame.OwnerSeat)) ||
            ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && (_pendingDecision?.PlayerSeat != frame.OwnerSeat || _pendingDecision.IsPrivate != true)))
            throw new InvalidOperationException("Named-turn hand draft lost its private physical costs.");
    }
}
