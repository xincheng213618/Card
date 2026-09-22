namespace CardGame.Core;

public sealed partial class GameEngine
{
    private enum LierenStage { Offer, OpponentPindian, Gain }

    private sealed class LierenResolution(AttackResolution attack)
    {
        public AttackResolution Attack { get; } = attack;
        public LierenStage Stage { get; set; }
        public int? OwnerCardId { get; set; }
        public int? TargetCardId { get; set; }
    }

    private bool TryBeginLierenChoice(AttackResolution attack)
    {
        if (!UsesFormalZhuRong || attack.LierenAttempted || !attack.DamageWasApplied ||
            attack.IsChainPropagation || attack.SourceSkill is not null ||
            attack.EffectiveCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
            return false;
        attack.MarkLierenAttempted();
        var owner = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        if (!owner.IsAlive || !target.IsAlive || !HasRuntimeSkill(owner, SkillKind.Lieren) ||
            GetHand(owner).Count == 0 || GetHand(target).Count == 0)
            return false;
        _pendingLieren = new LierenResolution(attack) { Stage = LierenStage.Offer };
        PublishLierenOffer(_pendingLieren);
        return true;
    }

    private void PublishLierenOffer(LierenResolution pending)
    {
        var owner = _players[pending.Attack.SourceSeat];
        var target = _players[pending.Attack.TargetSeat];
        var choices = GetHand(owner).Select(card => new PromptChoice(
                new ChoiceId($"lieren.use.{card.Id}"), $"以【{card.DisplayName}】（{card.Rank}）发动【烈刃】。",
                [card.Id], [target.Seat], new Dictionary<string, string> { ["action"] = "lieren-use" }))
            .Append(new PromptChoice(new ChoiceId("lieren.skip"), "不发动【烈刃】。", [], [],
                new Dictionary<string, string> { ["action"] = "lieren-skip" })).ToArray();
        _pendingDecision = new PendingDecision(DecisionKind.Lieren, owner.Seat,
            $"你对 {target.Name} 使用【杀】造成了伤害，是否发动【烈刃】？",
            GetHand(owner).Select(card => card.Id).ToArray(), [target.Seat])
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private CommandResult SubmitLierenPromptAnswer(PromptChoice selected)
    {
        if (_pendingLieren is null || _pendingDecision is not { Kind: DecisionKind.Lieren })
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的烈刃窗口。");
        return Accept(() =>
        {
            ResolveLierenChoice(selected);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveLierenChoice(PromptChoice selected)
    {
        var pending = _pendingLieren ?? throw new InvalidOperationException("There is no Lieren resolution.");
        var action = selected.Parameters.GetValueOrDefault("action");
        if (pending.Stage == LierenStage.Offer)
        {
            if (action == "lieren-skip" && selected.Cards.Count == 0)
            {
                FinishLieren(pending, used: false, won: false, gainedCardId: null);
                return;
            }
            if (action != "lieren-use" || selected.Cards.Count != 1)
                throw new InvalidOperationException("The Lieren offer choice is malformed.");
            var owner = _players[pending.Attack.SourceSeat];
            pending.OwnerCardId = GetHand(owner).Single(card => card.Id == selected.Cards[0]).Id;
            pending.Stage = LierenStage.OpponentPindian;
            PublishLierenOpponentChoice(pending);
            return;
        }
        if (pending.Stage == LierenStage.OpponentPindian)
        {
            if (action != "lieren-pindian" || selected.Cards.Count != 1)
                throw new InvalidOperationException("The Lieren Pindian choice is malformed.");
            ResolveLierenPindian(pending, selected.Cards[0]);
            return;
        }
        if (pending.Stage != LierenStage.Gain || action != "lieren-gain")
            throw new InvalidOperationException("The Lieren gain choice is malformed.");
        ResolveLierenGain(pending, selected);
    }

    private void PublishLierenOpponentChoice(LierenResolution pending)
    {
        var owner = _players[pending.Attack.SourceSeat];
        var target = _players[pending.Attack.TargetSeat];
        var hand = GetHand(target);
        ClearPendingDecision();
        _pendingDecision = new PendingDecision(DecisionKind.Lieren, target.Seat,
            $"{owner.Name} 对你发动【烈刃】，请选择一张手牌拼点。", hand.Select(c => c.Id).ToArray(), [], owner.Seat)
        {
            PromptId = target.IsHuman ? CreatePromptId() : default, IsPrivate = true,
            Choices = hand.Select(card => new PromptChoice(new ChoiceId($"lieren.pindian.{card.Id}"),
                $"以【{card.DisplayName}】（{card.Rank}）拼点", [card.Id], [],
                new Dictionary<string, string> { ["action"] = "lieren-pindian" })).ToArray()
        };
        _status = target.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveLierenPindian(LierenResolution pending, int targetCardId)
    {
        var owner = _players[pending.Attack.SourceSeat];
        var target = _players[pending.Attack.TargetSeat];
        var ownerCard = GetHand(owner).Single(card => card.Id == pending.OwnerCardId);
        var targetCard = GetHand(target).Single(card => card.Id == targetCardId);
        pending.TargetCardId = targetCard.Id;
        ClearPendingDecision();
        MoveCard(ownerCard, CardLocation.Hand(owner.Seat), CardLocation.Processing, CardMoveReasons.PindianReveal);
        MoveCard(targetCard, CardLocation.Hand(target.Seat), CardLocation.Processing, CardMoveReasons.PindianReveal);
        var won = ownerCard.Rank > targetCard.Rank;
        QueueGameEvent(new PindianResolvedEvent(pending.Attack.ResolutionId, SkillKind.Lieren, owner.Seat,
            target.Seat, ownerCard.Id, targetCard.Id, ownerCard.Rank, targetCard.Rank, won));
        MoveCard(ownerCard, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.PindianFinish);
        MoveCard(targetCard, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.PindianFinish);
        AddLog("Pindian", $"{owner.Name} 以 {ownerCard.Rank} 点与 {target.Name} 的 {targetCard.Rank} 点拼点，烈刃{(won ? "获胜" : "未赢")}。", owner.Seat, target.Seat);
        if (!won || GetHand(target).Count + GetEquipment(target).Count == 0)
        {
            FinishLieren(pending, used: true, won, gainedCardId: null);
            return;
        }
        pending.Stage = LierenStage.Gain;
        PublishLierenGainChoice(pending);
    }

    private void PublishLierenGainChoice(LierenResolution pending)
    {
        var owner = _players[pending.Attack.SourceSeat];
        var target = _players[pending.Attack.TargetSeat];
        var choices = new List<PromptChoice>();
        for (var slot = 0; slot < GetHand(target).Count; slot++)
            choices.Add(new PromptChoice(new ChoiceId($"lieren.gain.hand.{slot}"), $"获得 {target.Name} 的第 {slot + 1} 张暗置手牌。", [], [target.Seat],
                new Dictionary<string, string> { ["action"] = "lieren-gain", ["zone"] = "hand", ["slot"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        foreach (var card in GetEquipment(target))
            choices.Add(new PromptChoice(new ChoiceId($"lieren.gain.equipment.{card.Id}"), $"获得 {target.Name} 的【{card.DisplayName}】。", [card.Id], [target.Seat],
                new Dictionary<string, string> { ["action"] = "lieren-gain", ["zone"] = "equipment" }));
        _pendingDecision = new PendingDecision(DecisionKind.Lieren, owner.Seat,
            $"【烈刃】拼点获胜，获得 {target.Name} 的一张牌。", [], [target.Seat])
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveLierenGain(LierenResolution pending, PromptChoice selected)
    {
        var owner = _players[pending.Attack.SourceSeat];
        var target = _players[pending.Attack.TargetSeat];
        Card card;
        CardLocation from;
        if (selected.Parameters.GetValueOrDefault("zone") == "hand" &&
            int.TryParse(selected.Parameters.GetValueOrDefault("slot"), out var slot))
        {
            card = GetHand(target)[slot];
            from = CardLocation.Hand(target.Seat);
        }
        else
        {
            card = GetEquipment(target).Single(item => selected.Cards.SequenceEqual([item.Id]));
            from = CardLocation.Equipment(target.Seat);
        }
        ClearPendingDecision();
        MoveCard(card, from, CardLocation.Hand(owner.Seat), CardMoveReasons.LierenGain);
        FinishLieren(pending, used: true, won: true, card.Id);
    }

    private void FinishLieren(LierenResolution pending, bool used, bool won, int? gainedCardId)
    {
        var attack = pending.Attack;
        ClearPendingDecision();
        QueueGameEvent(new LierenResolvedEvent(attack.ResolutionId, attack.SourceSeat, attack.TargetSeat,
            pending.OwnerCardId, pending.TargetCardId, used, won, gainedCardId));
        _pendingLieren = null;
        CompleteAttack(attack);
    }

    private bool IsAiLierenPending() => _pendingLieren is not null &&
        _pendingDecision is { Kind: DecisionKind.Lieren, PlayerSeat: var seat } && !_players[seat].IsHuman;

    private void ResolvePendingAiLieren()
    {
        var pending = _pendingLieren ?? throw new InvalidOperationException("There is no AI Lieren choice.");
        var decision = _pendingDecision ?? throw new InvalidOperationException("Lieren requires a decision.");
        PromptChoice selected;
        if (pending.Stage == LierenStage.Offer)
            selected = decision.Choices.Where(c => c.Parameters.GetValueOrDefault("action") == "lieren-use")
                .OrderByDescending(c => GetHand(_players[decision.PlayerSeat]).Single(card => card.Id == c.Cards[0]).Rank).First();
        else if (pending.Stage == LierenStage.OpponentPindian)
            selected = decision.Choices.OrderByDescending(c => GetHand(_players[decision.PlayerSeat]).Single(card => card.Id == c.Cards[0]).Rank).First();
        else
            selected = decision.Choices.First();
        ResolveLierenChoice(selected);
        PublishState();
    }
}
