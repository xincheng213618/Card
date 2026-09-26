namespace CardGame.Core;

internal sealed record ActiveActionInvocation(
    long FrameId,
    int ActorSeat,
    ActiveActionRule Rule,
    ActiveActionSelection Selection,
    ActiveSkillEffect Effect,
    IReadOnlyList<int> CardIds,
    IReadOnlyList<int> TargetSeats);

internal sealed record ActiveGlobalCardUse(
    long ResolutionId, Card Representative, Suit Suit, IReadOnlyList<int> Targets);

/// <summary>Game primitives needed by the independent active-effect composer.</summary>
internal interface IActiveActionEffectHost
{
    ActiveGlobalCardUse UseSelectedCardsAsGlobal(int actorSeat, IReadOnlyList<int> cardIds,
        CardKind outputKind);
    void OpenGlobalCardWindow(ActiveGlobalCardUse use, int actorSeat, CardKind outputKind,
        LegalActionKind actionKind, CardKind responseKind);
    void PublishEvent(IGameEvent gameEvent);
    int LoseHp(ActiveActionInvocation action);
    void BeginDying(ActiveActionInvocation action);
    IReadOnlyList<int> Draw(ActiveActionInvocation action, CardMoveReason reason);
    int Discard(ActiveActionInvocation action, CardMoveReason reason, bool handOnly);
    int Recover(ActiveActionInvocation action, int targetSeat, bool childFrame, int? amount = null);
    int Give(ActiveActionInvocation action, int targetSeat, CardMoveReason reason);
    int ConsumeGiftLedger(ActiveActionInvocation action, int cardCount);
    void BeginVirtualDuel(ActiveActionInvocation action, int sourceSeat, int targetSeat);
    IReadOnlyList<int> PrepareFactionSlashRequest(ActiveActionInvocation action, int targetSeat);
    void AdvanceFactionSlashRequest();
    void PublishSuitChoice(ActiveActionInvocation action, int targetSeat,
        IReadOnlyList<PromptChoice> choices, string prompt);
    Card RevealRandomHandGift(ActiveActionInvocation action, int targetSeat);
    bool BeginSkillDamage(ActiveActionInvocation action, int targetSeat, Card? card);
    void MarkUsed(ActiveActionInvocation action);
    void Complete(ActiveActionInvocation action);
    string Name(int seat);
    void Log(ActiveActionInvocation action, string message, int? targetSeat = null);
    void LogCategory(string category, ActiveActionInvocation action, string message,
        int? targetSeat = null);
}

/// <summary>
/// Composes ordinary active costs and outcomes. The host owns movement, event,
/// dying, recovery-frame and replay state; no general identity is dispatched here.
/// </summary>
internal static class ActiveActionExecutor
{
    public static bool IsSupported(ActiveSkillEffectKind kind) => kind is
        ActiveSkillEffectKind.LoseHpAndDraw or
        ActiveSkillEffectKind.DiscardAndDraw or
        ActiveSkillEffectKind.GiveCardsAndRecover or
        ActiveSkillEffectKind.DiscardAndRecover or
        ActiveSkillEffectKind.DiscardAndRecoverTargets or
        ActiveSkillEffectKind.RevealGiftAndDamage or
        ActiveSkillEffectKind.RequestSlash or
        ActiveSkillEffectKind.PayHpOrDiscardWeaponAndDamage or
        ActiveSkillEffectKind.DiscardAndStartDuel or
        ActiveSkillEffectKind.DiscardAndRecoverSelfAndTarget or
        ActiveSkillEffectKind.StartArrowBarrage;

    public static bool TryExecuteImmediate(ActiveActionRule rule, ActiveSkillEffect effect, int actorSeat,
        IReadOnlyList<int> cardIds, IActiveActionEffectHost host)
    {
        if (effect.Kind != ActiveSkillEffectKind.StartArrowBarrage)
            return false;
        var use = host.UseSelectedCardsAsGlobal(actorSeat, cardIds, CardKind.ArrowBarrage);
        host.PublishEvent(new LuanjiConvertedEvent(use.ResolutionId, actorSeat,
            Array.AsReadOnly(cardIds.ToArray()), use.Suit));
        host.LogCategory("SkillTriggered", new ActiveActionInvocation(use.ResolutionId, actorSeat,
                rule, ActiveActionCatalog.Selection(rule.Kind), effect, cardIds, []),
            $"{host.Name(actorSeat)} 发动【{rule.Name}】，将两张{SuitName(use.Suit)}手牌当【万箭齐发】使用。");
        host.OpenGlobalCardWindow(use, actorSeat, CardKind.ArrowBarrage,
            LegalActionKind.ArrowBarrage, CardKind.Dodge);
        return true;
    }

    public static bool TryExecute(ActiveActionInvocation action, IActiveActionEffectHost host)
    {
        var effect = action.Effect;
        switch (effect.Kind)
        {
            case ActiveSkillEffectKind.RequestSlash:
            {
                var targetSeat = action.TargetSeats.Single();
                var candidates = host.PrepareFactionSlashRequest(action, targetSeat);
                host.PublishEvent(new JijiangRequestedEvent(action.FrameId, action.ActorSeat,
                    candidates, IsActiveUse: true, targetSeat));
                host.LogCategory("SkillTriggered", action,
                    $"{host.Name(action.ActorSeat)} 发动主公技【{action.Rule.Name}】，请求其他蜀势力角色为其对 {host.Name(targetSeat)} 提供【杀】。",
                    targetSeat);
                host.AdvanceFactionSlashRequest();
                return true;
            }
            case ActiveSkillEffectKind.RevealGiftAndDamage:
            {
                host.MarkUsed(action);
                var targetSeat = action.TargetSeats.Single();
                var choiceAction = action.Rule.SuitChoiceAction ??
                    throw new InvalidOperationException("A suit-choice effect has no response action.");
                var choicePrefix = action.Rule.SuitChoiceIdPrefix ??
                    throw new InvalidOperationException("A suit-choice effect has no choice identity.");
                var choices = Enum.GetValues<Suit>()
                    .Select(suit => new PromptChoice(
                        new ChoiceId($"{choicePrefix}{suit.ToString().ToLowerInvariant()}"),
                        $"选择{SuitName(suit)}", [], [],
                        new Dictionary<string, string>
                        {
                            ["action"] = choiceAction,
                            ["suit"] = suit.ToString()
                        })).ToArray();
                host.PublishSuitChoice(action, targetSeat, choices,
                    $"{host.Name(action.ActorSeat)} 对你发动【{action.Rule.Name}】：先选择一种花色，再获得并展示其一张随机手牌。");
                host.Log(action,
                    $"{host.Name(action.ActorSeat)} 对 {host.Name(targetSeat)} 发动【{action.Rule.Name}】，等待其选择花色。",
                    targetSeat);
                return true;
            }
            case ActiveSkillEffectKind.PayHpOrDiscardWeaponAndDamage:
            {
                host.MarkUsed(action);
                if (action.CardIds.Count == 1)
                    host.Discard(action, RequireCostReason(action), handOnly: false);
                else if (host.LoseHp(action) == 0)
                {
                    host.BeginDying(action);
                    return true;
                }
                if (!host.BeginSkillDamage(action, action.TargetSeats.Single(), card: null))
                    CompleteDamage(action, dealtDamage: false, host);
                return true;
            }
            case ActiveSkillEffectKind.DiscardAndStartDuel:
            {
                host.MarkUsed(action);
                host.Discard(action, RequireCostReason(action), handOnly: false);
                var sourceSeat = action.TargetSeats[0];
                var targetSeat = action.TargetSeats[1];
                host.BeginVirtualDuel(action, sourceSeat, targetSeat);
                host.Log(action,
                    $"{host.Name(action.ActorSeat)} 发动【{action.Rule.Name}】，弃置一张牌并视为 {host.Name(sourceSeat)} 对 {host.Name(targetSeat)} 使用【决斗】。",
                    targetSeat);
                return true;
            }
            case ActiveSkillEffectKind.LoseHpAndDraw:
            {
                var hp = host.LoseHp(action);
                if (hp == 0)
                {
                    host.BeginDying(action);
                    return true;
                }

                var drawn = host.Draw(action, RequireDrawReason(action));
                host.Complete(action);
                host.Log(action, $"{host.Name(action.ActorSeat)} 发动【{action.Rule.Name}】，失去 {effect.HpCost} 点体力并摸了 {drawn.Count} 张牌。");
                return true;
            }
            case ActiveSkillEffectKind.DiscardAndDraw:
            {
                var discarded = host.Discard(action, RequireCostReason(action), handOnly: false);
                var drawn = host.Draw(action, RequireDrawReason(action));
                if (action.Selection.OncePerTurn) host.MarkUsed(action);
                host.Complete(action);
                host.Log(action, $"{host.Name(action.ActorSeat)} 发动【{action.Rule.Name}】，弃置 {discarded} 张牌并摸了 {drawn.Count} 张牌。");
                return true;
            }
            case ActiveSkillEffectKind.DiscardAndRecoverSelfAndTarget:
            {
                host.Discard(action, RequireCostReason(action), handOnly: true);
                var targetSeat = action.TargetSeats.Single();
                host.Recover(action, action.ActorSeat, childFrame: true);
                host.Recover(action, targetSeat, childFrame: true);
                host.MarkUsed(action);
                host.Complete(action);
                host.Log(action, $"{host.Name(action.ActorSeat)} 发动【{action.Rule.Name}】，弃置两张手牌，令自己与 {host.Name(targetSeat)} 各回复 1 点体力。", targetSeat);
                return true;
            }
            case ActiveSkillEffectKind.DiscardAndRecover:
            {
                var discarded = host.Discard(action, RequireCostReason(action), handOnly: true);
                var targetSeat = action.TargetSeats.Single();
                var recovered = host.Recover(action, targetSeat, childFrame: false);
                host.MarkUsed(action);
                host.Complete(action);
                host.Log(action, $"{host.Name(action.ActorSeat)} 发动【{action.Rule.Name}】，弃置 {discarded} 张手牌并令 {host.Name(targetSeat)} 回复 {recovered} 点体力。", targetSeat);
                return true;
            }
            case ActiveSkillEffectKind.DiscardAndRecoverTargets:
            {
                var discarded = host.Discard(action, RequireCostReason(action), handOnly: true);
                var recoveredTargets = 0;
                foreach (var targetSeat in action.TargetSeats)
                    if (host.Recover(action, targetSeat, childFrame: true) > 0)
                        recoveredTargets++;
                host.MarkUsed(action);
                host.Complete(action);
                host.Log(action, $"{host.Name(action.ActorSeat)} 发动【{action.Rule.Name}】，弃置 {discarded} 张手牌并令 {recoveredTargets} 名受伤角色各回复 {effect.RecoveryAmount} 点体力。");
                return true;
            }
            case ActiveSkillEffectKind.GiveCardsAndRecover:
            {
                var targetSeat = action.TargetSeats.Single();
                var given = host.Give(action, targetSeat, action.Rule.TransferReason ??
                    throw new InvalidOperationException("A gift effect has no movement reason."));
                var previous = host.ConsumeGiftLedger(action, given);
                var recoveringSeat = action.Selection.GiftRecovery ==
                    ActiveGiftRecoveryRule.OwnerAfterCumulativeTwoCards
                    ? action.ActorSeat
                    : targetSeat;
                var recovered = action.Selection.GiftRecovery ==
                                ActiveGiftRecoveryRule.OwnerAfterCumulativeTwoCards
                    ? previous < 2 && previous + given >= 2
                        ? host.Recover(action, recoveringSeat, childFrame: true, amount: 1)
                        : 0
                    : host.Recover(action, recoveringSeat, childFrame: false);
                host.Complete(action);
                host.Log(action, recovered > 0
                    ? $"{host.Name(action.ActorSeat)} 发动【{action.Rule.Name}】，向 {host.Name(targetSeat)} 交给 {given} 张牌，{host.Name(recoveringSeat)} 回复 {recovered} 点体力。"
                    : $"{host.Name(action.ActorSeat)} 发动【{action.Rule.Name}】，向 {host.Name(targetSeat)} 交给 {given} 张牌。");
                return true;
            }
            default:
                return false;
        }
    }

    public static void ResolveSuitChoice(ActiveActionInvocation action, Suit chosenSuit,
        IActiveActionEffectHost host)
    {
        if (action.Effect.Kind != ActiveSkillEffectKind.RevealGiftAndDamage)
            throw new InvalidOperationException("The active action does not await a suit choice.");
        var targetSeat = action.TargetSeats.Single();
        var card = host.RevealRandomHandGift(action, targetSeat);
        var causesDamage = card.Suit != chosenSuit;
        host.PublishEvent(new FanjianCardRevealedEvent(action.FrameId, action.ActorSeat,
            targetSeat, chosenSuit, card.Id, card.Kind, card.Suit, causesDamage));
        host.LogCategory("FanjianRevealed", action,
            $"{host.Name(targetSeat)} 为【{action.Rule.Name}】选择{SuitName(chosenSuit)}，获得并展示了{SuitName(card.Suit)}【{card.DisplayName}】" +
            (causesDamage ? "，花色不同。" : "，花色相同。"), targetSeat);
        if (!causesDamage || !host.BeginSkillDamage(action, targetSeat, card))
            host.Complete(action);
    }

    public static void CompleteDamage(ActiveActionInvocation action, bool dealtDamage,
        IActiveActionEffectHost host)
    {
        if (action.Effect.Kind is not (ActiveSkillEffectKind.RevealGiftAndDamage or
            ActiveSkillEffectKind.PayHpOrDiscardWeaponAndDamage))
            throw new InvalidOperationException("The active action does not await damage completion.");
        host.Complete(action);
        if (action.Effect.Kind == ActiveSkillEffectKind.PayHpOrDiscardWeaponAndDamage)
        {
            var targetSeat = action.TargetSeats.Single();
            host.Log(action, dealtDamage
                ? $"{host.Name(action.ActorSeat)} 发动【{action.Rule.Name}】，对 {host.Name(targetSeat)} 造成 1 点伤害。"
                : $"{host.Name(action.ActorSeat)} 发动【{action.Rule.Name}】后未能对 {host.Name(targetSeat)} 造成伤害。",
                targetSeat);
        }
    }

    public static void CompleteFactionRequest(ActiveActionInvocation action,
        IActiveActionEffectHost host)
    {
        if (action.Effect.Kind != ActiveSkillEffectKind.RequestSlash)
            throw new InvalidOperationException("The active action is not a faction card request.");
        host.Complete(action);
    }

    private static CardMoveReason RequireCostReason(ActiveActionInvocation action) =>
        action.Rule.CostReason ?? throw new InvalidOperationException("An active discard effect has no movement reason.");

    private static CardMoveReason RequireDrawReason(ActiveActionInvocation action) =>
        action.Rule.DrawReason ?? throw new InvalidOperationException("An active draw effect has no movement reason.");

    private static string SuitName(Suit suit) => suit switch
    {
        Suit.Spade => "黑桃",
        Suit.Heart => "红桃",
        Suit.Club => "梅花",
        Suit.Diamond => "方块",
        _ => suit.ToString()
    };
}
