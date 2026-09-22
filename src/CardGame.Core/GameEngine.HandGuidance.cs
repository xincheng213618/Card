namespace CardGame.Core;

public enum HandGuidanceReason
{
    Playable, ConversionOnly, ResponseOnly, HealthFull, SlashLimitReached,
    AlcoholAlreadyActive, NoLegalTarget, WaitingForTurn, ResolveCurrentPrompt,
    SelectDiscard, GameFinished
}

/// <summary>Read-only guidance for one card owned by the local human, without opponent secrets.</summary>
public sealed record HandCardGuidance(int CardId, bool CanPlay, HandGuidanceReason Reason, string Message);

public sealed partial class GameEngine
{
    /// <summary>
    /// Explains the current human hand. Legal actions remain authoritative;
    /// this query does not advance a frame, consume randomness or publish events.
    /// </summary>
    public IReadOnlyList<HandCardGuidance> GetHumanHandGuidance()
    {
        if (_options.HumanSeat < 0) return [];
        var actor = _players[_options.HumanSeat];
        var actions = GetHumanLegalActions().Where(action => action.CardId is not null).ToLookup(action => action.CardId!.Value);
        var ownPrompt = _pendingDecision?.PlayerSeat == actor.Seat ? _pendingDecision : null;
        var skill = PassiveRules(actor);
        var context = CreateSkillContext(actor);
        return GetHand(actor).Select(card =>
        {
            HandCardGuidance Hint(HandGuidanceReason reason, string message, bool playable = false) => new(card.Id, playable, reason, message);
            if (_status == EngineStatus.Completed) return Hint(HandGuidanceReason.GameFinished, "本局已经结束，可在战报中回顾对局。");
            if (ownPrompt?.Kind == DecisionKind.DiscardCards)
                return Hint(HandGuidanceReason.SelectDiscard, "点击选为弃牌；选够指定数量后，再确认弃置。");
            if (ownPrompt is not null && ownPrompt.Kind != DecisionKind.PlayCard)
                return Hint(HandGuidanceReason.ResolveCurrentPrompt, "请在中央完成当前响应；候选按钮会列出可用的牌、技能或放弃选项。");
            if (ownPrompt?.Kind != DecisionKind.PlayCard)
                return Hint(HandGuidanceReason.WaitingForTurn, "现在不是你的出牌阶段；需要响应时，中央会显示可选操作。");

            var cardActions = actions[card.Id].ToArray();
            var identity = GetProgramCardIdentityMatches(actor, card).FirstOrDefault();
            if (cardActions.Length > 0)
            {
                if (cardActions.Any(action => action.Kind == LegalActionKind.Recast))
                    return Hint(HandGuidanceReason.Playable, "可选择一到两名存活角色切换连环，或不选目标，点击重铸换一张牌。", true);
                if (!IsSlashCard(card.Kind) && cardActions.All(action => action.PlayedCardKind == CardKind.Slash))
                    return Hint(HandGuidanceReason.ConversionOnly, $"当前可通过【{skill.Name}】当作杀使用；选择目标后确认转化。", true);
                return Hint(HandGuidanceReason.Playable,
                    cardActions.All(action => action.TargetSeats.All(seat => seat == actor.Seat))
                        ? "当前可以使用；点击选中，再确认出牌。"
                        : "当前可以使用；选中后，合法目标或目标组合会亮起。", true);
            }

            if (identity is null && card.Kind == CardKind.Peach && actor.Hp >= actor.MaxHp)
                return Hint(HandGuidanceReason.HealthFull, "你的体力已满，当前不能用桃回复；保留后可在濒死时救援。");
            if ((IsSlashCard(card.Kind) || identity is not null && IsSlashCard(identity.Identity.OutputKind)) &&
                _slashCountThisTurn >= GetSlashUseLimit(actor) &&
                !HasSlashAllowanceForAnyTarget(actor,
                    GetSlashUseVariants(actor, identity?.Identity.OutputKind ?? card.Kind)
                        .Select(variant => variant.EffectiveKind)))
                return Hint(HandGuidanceReason.SlashLimitReached, "本回合可使用杀的次数已用完；可以保留它用于响应或下个回合。");
            if (identity is null && card.Kind == CardKind.Alcohol && actor.HasAlcoholEffect)
                return Hint(HandGuidanceReason.AlcoholAlreadyActive, "你已有酒的效果，等待下一张杀消耗加伤；当前不能叠加饮酒。");
            if (card.Kind is CardKind.Dodge or CardKind.Nullification)
                return Hint(HandGuidanceReason.ResponseOnly, card.Kind == CardKind.Dodge
                    ? "闪用于受到杀或万箭齐发时响应；当前没有可用的主动转化方式。"
                    : "无懈可击用于锦囊响应窗口，不能在出牌阶段主动打出。");
            return Hint(HandGuidanceReason.NoLegalTarget, "当前没有符合规则的目标；可在图鉴查看这张牌的作用，或使用其他牌。");
        }).ToArray();
    }
}
