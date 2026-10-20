namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 逊贤: the settled used/played card is offered to a character with more
    // hand cards or more health; the once-per-turn limit is the trigger's.
    private SkillProgramStepOutcome XunxianProgramGiftUsedCard(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.DiscardPileReceived,
                MovementBatch: { } batch, MovementIndex: { } index } ||
            index < 0 || index >= batch.Movements.Count ||
            _winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            _cardZones.GetLocation(batch.Movements[index].CardId) != CardLocation.DiscardPile)
            return SkillProgramStepOutcome.Continue;
        var recipients = _players.Where(player => player.IsAlive && player.Seat != active.OwnerSeat &&
            (GetHand(player).Count > GetHand(owner).Count || player.Hp > owner.Hp))
            .Select(player => player.Seat).ToArray();
        if (recipients.Length == 0)
        {
            AddLog("SkillEffect", $"{owner.Name} 逊贤：没有手牌数或体力值大于你的角色，牌留在弃牌堆。", active.OwnerSeat);
            AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.Continue;
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = recipients.Select(seat => new PromptChoice(
            new ChoiceId($"xunxian-gift.frame-{frame.Id}.seat-{seat}"),
            $"交给 {_players[seat].Name}。",
            [], [seat],
            new Dictionary<string, string>
            {
                ["program-action"] = "xunxian-gift",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["recipient"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择接受牌的角色。",
            [], recipients, active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择角色", "将置入弃牌堆的牌交给一名手牌数或体力值大于你的角色。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("xunxian-gift")]
    private void ResolveXunxianGiftChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Xunxian gift choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.XunxianGiftUsedCard } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Xunxian gift choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("recipient"),
                System.Globalization.CultureInfo.InvariantCulture, out var recipient) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.DiscardPileReceived,
                MovementBatch: { } batch, MovementIndex: { } index } ||
            index < 0 || index >= batch.Movements.Count)
            throw new InvalidOperationException("The Xunxian gift choice lost its chooser or settled card.");
        ClearPendingDecision();
        var movement = batch.Movements[index];
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive || !IsValidPlayerSeat(recipient) ||
            recipient == active.OwnerSeat || !_players[recipient].IsAlive ||
            _cardZones.GetLocation(movement.CardId) != CardLocation.DiscardPile ||
            !(GetHand(_players[recipient]).Count > GetHand(owner).Count || _players[recipient].Hp > owner.Hp))
        {
            CancelProgramBindingAndCleanup(active, "逊贤的目标或弃牌堆中的牌已失效，剩余结算取消。");
            return;
        }
        var card = _cardZones.CardsAt(CardLocation.DiscardPile).Single(item => item.Id == movement.CardId);
        MoveCard(card, CardLocation.DiscardPile, CardLocation.Hand(recipient),
            new($"skill-program.{active.SkillId}.xunxian-gift"));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，将 {card.DisplayName} 交给 {_players[recipient].Name}。",
            active.OwnerSeat, recipient);
        AdvanceEventRulesAndQueueFact(new ProgramXunxianGiftEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, recipient, card.Id, movement.Sequence));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }
}
