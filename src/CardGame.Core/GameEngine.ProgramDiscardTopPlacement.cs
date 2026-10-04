namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IProgramDiscardTopPlacementHost
    {
        public SkillProgramStepOutcome PutDiscardedCardsOnDrawPileTop(ProgramSkillFrame frame) =>
            engine.PutProgramDiscardedCardsOnDrawPileTop(frame);
    }

    private void AssertProgramDiscardTopPlacement(ProgramSkillFrame frame)
    {
        if (frame.DiscardTopPlacement is not { } state) return;
        var paused = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (paused.Op is not (SkillProgramEffectOp.PutDiscardedCardsOnDrawPileTop or SkillProgramEffectOp.PutOwnOrPreviousFirstDiscardOnTop) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardsMoved, MovementBatch: { } batch } ||
            state.CandidateCardIds.Count == 0 || state.CandidateCardIds.Distinct().Count() != state.CandidateCardIds.Count ||
            state.SelectedCardIds.Distinct().Count() != state.SelectedCardIds.Count ||
            state.SelectedCardIds.Any(id => !state.CandidateCardIds.Contains(id)) ||
            state.CandidateCardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.DiscardPile ||
                !batch.Movements.Any(move => move.CardId == id && IsProgramDiscardTopMovement(frame, move))) ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision || decision.PlayerSeat != frame.OwnerSeat ||
            decision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") is not ("discard-top-select" or "discard-top-finish")))
            throw new InvalidOperationException("An active discard-top draft has invalid frozen cards or choices.");
    }

    internal static bool IsDiscardMovementReason(CardMoveReason reason)
    {
        var value = reason.Value;
        if (value is "card.effect.dismantlement" or "card.effect.dismantlement-judgment" or
            "mode.identity.lord-killed-loyalist" or "skill.gongqi.cost") return true;
        if (value.StartsWith("skill-program.", StringComparison.Ordinal))
        {
            // Only these exact paid producer suffixes are discards.
            if (value.EndsWith($".{nameof(SkillProgramEffectOp.DiscardTurnOverAndTakeHand)}.payment", StringComparison.Ordinal)) return true;
            if (value.EndsWith($".{nameof(SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach)}.discard", StringComparison.Ordinal) ||
                value.EndsWith($".{nameof(SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest)}.discard", StringComparison.Ordinal)) return true;
            var operation = value[(value.LastIndexOf('.') + 1)..];
            return operation is nameof(SkillProgramEffectOp.ResolveGameTargetHandHpChoice) or nameof(SkillProgramEffectOp.DiscardSelected) or nameof(SkillProgramEffectOp.MoveBoundCards) or
                nameof(SkillProgramEffectOp.DiscardOwnedZoneCards) or nameof(SkillProgramEffectOp.SelectAndMoveOwnedCard) or
                nameof(SkillProgramEffectOp.ChooseOwnCardDiscard) or nameof(SkillProgramEffectOp.ChooseOtherOwnedCardDiscard) or
                nameof(SkillProgramEffectOp.ChooseDifferentCategoryDiscard) or nameof(SkillProgramEffectOp.DiscardParticipantCards) or
                nameof(SkillProgramEffectOp.DiscardTargetEquipment) or nameof(SkillProgramEffectOp.TakeSelectedTargetCards) or
                nameof(SkillProgramEffectOp.ChooseCategoryAlternativeDiscard) or nameof(SkillProgramEffectOp.EscalatingDiscardOrDamage) or
                nameof(SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard) or nameof(SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected) or
                nameof(SkillProgramEffectOp.RequestAttackRangeAid) or nameof(SkillProgramEffectOp.PayEquipmentColorDiscard) or
                nameof(SkillProgramEffectOp.PayCompletedUseDiscardOrLoseHp) or
                nameof(SkillProgramEffectOp.DiscardOwnedCardToAdjustCurrentDamage) or nameof(SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit) or nameof(SkillProgramEffectOp.PlaceMatchedJudgmentCard) or
                nameof(SkillProgramEffectOp.ResolvePrepDiscardOrEnding) or nameof(SkillProgramEffectOp.RequireTargetDiscardOrEquipmentRecast);
        }
        // Recasting, use/response cleanup, death cleanup and replacement are not discards.
        if (value.StartsWith("card.recast.", StringComparison.Ordinal) || value.Contains("death", StringComparison.Ordinal) ||
            value.Contains("finished", StringComparison.Ordinal) || value is "card.harvest-discard" or
            "equipment.wooden-ox.grain-discard" or "skill.zaiqi.discard") return false;
        return value.EndsWith(".discard", StringComparison.Ordinal) ||
            value.EndsWith("-discard", StringComparison.Ordinal) || value.Contains(".discard-", StringComparison.Ordinal);
    }

    private SkillProgramStepOutcome PutProgramDiscardedCardsOnDrawPileTop(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.WindowContext is not { Window: SkillProgramTriggerWindow.CardsMoved, MovementBatch: { } batch } ||
            active.DiscardTopPlacement is not null || _pendingDecision is not null)
            throw new InvalidOperationException("Discard-top placement requires a clean frozen movement window.");
        var ids = batch.Movements.Where(move => IsProgramDiscardTopMovement(active, move) &&
                _cardZones.GetLocation(move.CardId) == CardLocation.DiscardPile)
            .Select(move => move.CardId).Distinct().ToArray();
        if (ids.Length == 0 || !_players[active.OwnerSeat].IsAlive) return SkillProgramStepOutcome.Continue;
        active = active with { DiscardTopPlacement = new(ids, []) };
        ReplaceRuntimeTop(active);
        PublishProgramDiscardTopPlacement(active);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishProgramDiscardTopPlacement(ProgramSkillFrame frame)
    {
        var state = frame.DiscardTopPlacement ?? throw new InvalidOperationException("The discard-top draft is missing.");
        var chosen = state.SelectedCardIds.ToHashSet();
        var choices = state.CandidateCardIds.Where(id => !chosen.Contains(id)).Select(id =>
        {
            var card = _cardZones.CardsAt(CardLocation.DiscardPile).Single(item => item.Id == id);
            return new PromptChoice(new ChoiceId($"program-discard-top.{frame.Id}.select.{id}"),
                $"选择【{card.DisplayName}】置于牌堆顶（按下一张先摸的顺序）。", [id], [],
                new Dictionary<string, string> { ["program-action"] = "discard-top-select" });
        }).ToList();
        choices.Add(new PromptChoice(new ChoiceId($"program-discard-top.{frame.Id}.finish"),
            "完成排序，其余牌留在弃牌堆。", [], [],
            new Dictionary<string, string> { ["program-action"] = "discard-top-finish" }));
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            "依次选择要置于牌堆顶的弃牌，可随时完成。", state.CandidateCardIds, [], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name, $"{skill.Name} · 弃牌排序", "所选第一张将最先摸到。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveProgramDiscardTopPlacementChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("Discard-top placement lost its frame.");
        var state = frame.DiscardTopPlacement ?? throw new InvalidOperationException("Discard-top placement lost its draft.");
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != frame.OwnerSeat || !decision.Choices.Any(item => item.Id == choice.Id))
            throw new InvalidOperationException("Discard-top placement choice is not current.");
        if (!_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            state.CandidateCardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.DiscardPile))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "弃牌已变化，牌堆顶放置取消。");
            return;
        }
        var action = choice.Parameters.GetValueOrDefault("program-action");
        if (action == "discard-top-select" && choice.Cards is [var id] &&
            state.CandidateCardIds.Contains(id) && !state.SelectedCardIds.Contains(id))
        {
            ClearPendingDecision();
            frame = frame with { DiscardTopPlacement = state with { SelectedCardIds = [..state.SelectedCardIds, id] } };
            ReplaceRuntimeTop(frame);
            PublishProgramDiscardTopPlacement(frame);
            return;
        }
        if (action != "discard-top-finish" || choice.Cards.Count != 0)
            throw new InvalidOperationException("Invalid discard-top placement action.");
        ClearPendingDecision();
        ReplaceRuntimeTop(frame with { DiscardTopPlacement = null });
        var cards = state.SelectedCardIds.Select(id => _cardZones.CardsAt(CardLocation.DiscardPile).Single(card => card.Id == id)).ToArray();
        if (cards.Length > 0)
        {
            MoveCards(cards, CardLocation.DiscardPile, CardLocation.DrawPile,
                new CardMoveReason($"skill-program.{frame.SkillId}.{SkillProgramEffectOp.PutDiscardedCardsOnDrawPileTop}"));
            _cardZones.PlaceDrawPileCardsAtTop(state.SelectedCardIds);
        }
        AdvanceRuntimeProgram(frame.Id);
    }
}
