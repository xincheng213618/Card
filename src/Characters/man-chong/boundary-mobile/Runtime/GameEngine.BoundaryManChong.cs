namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string BoundaryManChongAction = "boundary-man-chong-junxing";
    private const string BoundaryManChongDiscard = "skill-program.boundary-junxing-mobile.target-discard";

    [ProgramCapability]
    private sealed class BoundaryManChongHost(GameEngine engine) : IBoundaryManChongHost
    {
        public SkillProgramStepOutcome Choose(ProgramSkillFrame frame, SkillProgramEffect effect) => engine.BeginBoundaryManChongJunxing(frame, effect);
    }

    private SkillProgramStepOutcome BeginBoundaryManChongJunxing(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.BoundaryManChongJunxing is not null || frame.SelectedTargetSeats is not [var target] ||
            target == frame.OwnerSeat || !IsValidPlayerSeat(target) || !_players[target].IsAlive)
            throw new InvalidOperationException("Junxing requires its original distinct living recipient.");
        var count = GetProgramCardSet(frame, effect.SourceBind!).CardIds.Count;
        if (count < 1) throw new InvalidOperationException("Junxing requires the original nonempty hand payment.");
        ReplaceRuntimeTop(frame = frame with { BoundaryManChongJunxing = new(frame.InstructionIndex, target, count, effect.SourceBind!, effect.ResultBind!) });
        PublishBoundaryManChongJunxing(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private Card[] BoundaryManChongDiscardable(int target) => GetHand(_players[target]).Concat(GetEquipment(target))
        .Where(card => !IsSelfHandCategoryDiscardForbidden(target, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard)).ToArray();

    private void PublishBoundaryManChongJunxing(ProgramSkillFrame frame)
    {
        var draft = frame.BoundaryManChongJunxing!;
        var cards = BoundaryManChongDiscardable(draft.TargetSeat);
        var choices = new List<PromptChoice>();
        PromptChoice Choice(string branch, string label, int? card = null) => new(
            new ChoiceId($"boundary-man-chong.{frame.Id}.{branch}.{card?.ToString() ?? "none"}"), label,
            card is { } id ? [id] : [], [], new Dictionary<string, string> { ["program-action"] = BoundaryManChongAction, ["branch"] = branch });
        choices.AddRange(cards.Where(card => draft.SelectedIds.Contains(card.Id)).Select(card => Choice("unpick", "取消选择【" + card.DisplayName + "】", card.Id)));
        if (cards.Length >= draft.Count)
        {
            if (draft.SelectedIds.Count < draft.Count)
                choices.AddRange(cards.Where(card => !draft.SelectedIds.Contains(card.Id)).Select(card => Choice("card", "选择弃置【" + card.DisplayName + "】", card.Id)));
            else choices.Add(Choice("discard", $"弃置所选{draft.Count}张牌并失去1点体力"));
        }
        choices.Add(Choice("turn", $"翻面，然后摸{draft.Count}张牌"));
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        var prompt = new PendingDecision(DecisionKind.ProgramTrigger, draft.TargetSeat,
            $"【峻刑】弃置{draft.Count}张手牌或装备并失去1点体力，或翻面摸{draft.Count}张牌。",
            choices.SelectMany(choice => choice.Cards).ToArray(), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = draft.TargetSeat, Choices = Array.AsReadOnly(choices.ToArray()), SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        var remaining = draft.Count - draft.SelectedIds.Count;
        _pendingDecision = NativeOwnedSelection(prompt, frame, "junxing-target", "branch", ["card"], ["discard"], draft.SelectedIds, remaining, remaining);
        _status = _players[draft.TargetSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer(BoundaryManChongAction)]
    private void AnswerBoundaryManChongJunxing(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundaryManChongJunxing: { Paid: false } draft } frame ||
            _pendingDecision?.PlayerSeat != draft.TargetSeat || !_pendingDecision.Choices.Any(current => AssistedChoicesEqual([current], [choice])))
            throw new InvalidOperationException("Junxing lost its original recipient's current private choice.");
        AssertBoundaryManChongJunxing(frame);
        var branch = choice.Parameters.GetValueOrDefault("branch");
        if (branch is "card" or "unpick")
        {
            if (choice.Cards is not [var id] || !BoundaryManChongDiscardable(draft.TargetSeat).Any(card => card.Id == id))
                throw new InvalidOperationException("Junxing can stage only the recipient's actual discardable HE entity.");
            var selected = branch == "card" ? draft.SelectedIds.Append(id).ToArray() : draft.SelectedIds.Where(card => card != id).ToArray();
            if (selected.Length > draft.Count || selected.Distinct().Count() != selected.Length) throw new InvalidOperationException("Junxing cannot repeat or overpay a staged entity.");
            ClearPendingDecision();
            ReplaceRuntimeTop(frame = frame with { BoundaryManChongJunxing = draft with { SelectedIds = selected } });
            PublishBoundaryManChongJunxing(frame);
            return;
        }
        if (branch is not ("discard" or "turn") || choice.Cards.Count != 0 ||
            branch == "discard" && (draft.SelectedIds.Count != draft.Count ||
                draft.SelectedIds.Any(id => !BoundaryManChongDiscardable(draft.TargetSeat).Any(card => card.Id == id))))
            throw new InvalidOperationException("Junxing requires its complete current selected payment or turn-over option.");
        ClearPendingDecision();
        var before = _movementSequence;
        ReplaceRuntimeTop(frame = frame with
        {
            ChoiceBindings = Array.AsReadOnly(frame.ChoiceBindings.Append(new ProgramChoiceResultBinding(draft.ResultBind,
                branch == "discard" ? "discarded" : "turned", draft.TargetSeat)).ToArray()),
            BoundaryManChongJunxing = branch == "discard" ? draft with { Paid = true, SequenceBefore = before, SequenceAfter = before } : null,
            PendingMovementContinuation = branch == "discard" ? new(frame.OwnerSeat, 0, null) : null
        });
        if (branch == "discard")
        {
            MoveProgramLogicalDiscard(frame, draft.TargetSeat, draft.SelectedIds, new(BoundaryManChongDiscard), (_, records) =>
            {
                var active = GetActiveProgramFrame(frame.Id);
                ReplaceRuntimeTop(active with { BoundaryManChongJunxing = active.BoundaryManChongJunxing! with { SequenceAfter = records.Max(record => record.Sequence) } });
            });
        }
        var after = _movementSequence;
        AdvanceEventRulesAndQueueFact(new BoundaryManChongChoiceCommittedEvent(frame.Id, draft.InstructionIndex,
            draft.ResultBind, draft.TargetSeat, draft.Count, branch == "discard", before, after));
        if (branch == "turn") AdvanceRuntimeProgram(frame.Id);
        else if (!TryDrainFireTargetMovement(GetActiveProgramFrame(frame.Id))) ReturnRuntimeProgramMovement(frame.Id);
    }

    [ProgramContinuation(ProgramContinuationKind.Movement, 53000)]
    private bool ReturnBoundaryManChongJunxing(ProgramSkillFrame frame)
    {
        if (frame.BoundaryManChongJunxing is not { Paid: true }) return false;
        if (frame.PendingMovementContinuation is not { } pending || !BoundaryManChongPaidMovement(frame,
                ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect, pending))
            throw new InvalidOperationException("Junxing cannot consume another instruction's paid movement return.");
        if (TryDrainFireTargetMovement(frame)) return true;
        ReplaceRuntimeTop(frame = frame with { BoundaryManChongJunxing = null, PendingMovementContinuation = null });
        if (!_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            CancelProgramBindingAndCleanup(frame, "峻刑付款响应后技能来源失效。");
        else AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 53000)]
    private bool BoundaryManChongPaidMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        frame.BoundaryManChongJunxing is { Paid: true } draft && effect?.Op == SkillProgramEffectOp.ResolveBoundaryManChongJunxing &&
        draft.InstructionIndex == frame.InstructionIndex && effect.SourceBind == draft.SourceBind && effect.ResultBind == draft.ResultBind &&
        pending == frame.PendingMovementContinuation && pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        draft.SelectedIds.Count == draft.Count && draft.SequenceAfter > draft.SequenceBefore &&
        draft.SelectedIds.All(id => _cardMovements.Count(move => move.CardId == id && move.Sequence > draft.SequenceBefore && move.Sequence <= draft.SequenceAfter &&
            move.From.OwnerSeat == draft.TargetSeat && move.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
            move.To is { Zone: CardZoneKind.DiscardPile or CardZoneKind.OutsideGame } && move.Reason.Value == BoundaryManChongDiscard) == 1);

    private bool IsBoundaryManChongChoiceResult(ProgramSkillFrame frame, ProgramChoiceResultBinding binding)
    {
        if (frame.SkillId != "boundary:junxing-mobile") return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        var producer = plan.Instructions.Take(frame.InstructionIndex).SingleOrDefault(effect =>
            effect.Op == SkillProgramEffectOp.ResolveBoundaryManChongJunxing && effect.ResultBind == binding.Name);
        return producer is not null && frame.SelectedTargetSeats is [var target] && binding.ChooserSeat == target &&
            binding.OptionId is "discarded" or "turned" && ProgramEventHistory<BoundaryManChongChoiceCommittedEvent>().Count(record =>
                record.FrameId == frame.Id && record.ResultBind == binding.Name && record.TargetSeat == target &&
                record.Count == GetProgramCardSet(frame, producer.SourceBind!).CardIds.Count &&
                record.Discarded == (binding.OptionId == "discarded")) == 1;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 53000)]
    private void AssertBoundaryManChongJunxing(ProgramSkillFrame frame)
    {
        if (frame.BoundaryManChongJunxing is not { } draft) return;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ResolveBoundaryManChongJunxing || draft.InstructionIndex != frame.InstructionIndex ||
            effect.SourceBind != draft.SourceBind || effect.ResultBind != draft.ResultBind || frame.SelectedTargetSeats is not [var target] ||
            target != draft.TargetSeat || target == frame.OwnerSeat || draft.Count < 1 || draft.Count != GetProgramCardSet(frame, draft.SourceBind).CardIds.Count ||
            draft.SelectedIds.Count > draft.Count || draft.SelectedIds.Distinct().Count() != draft.SelectedIds.Count)
            throw new InvalidOperationException("Junxing lost its original cost count, recipient or owning instruction.");
        if (!draft.Paid && (draft.SelectedIds.Any(id => !BoundaryManChongDiscardable(target).Any(card => card.Id == id)) ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) || _pendingDecision is not { IsPrivate: true } prompt || prompt.PlayerSeat != target))
            throw new InvalidOperationException("Junxing lost its unspent target selection or private chooser.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveBoundaryManChongJunxing)]
    private PromptChoice BoundaryManChongAi(PendingDecision prompt, ProgramSkillFrame frame) =>
        _players[frame.BoundaryManChongJunxing!.TargetSeat].IsFaceDown
            ? prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("branch") == "turn")
            : prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "discard") ??
              prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "card") ??
              prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("branch") == "turn");
}
