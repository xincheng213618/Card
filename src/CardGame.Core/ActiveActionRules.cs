namespace CardGame.Core;

public enum ActiveCardSelector
{
    Hand,
    HandAndEquipmentInClassic,
    WeaponFromHandOrEquipment,
    SameSuitPair
}

[Flags]
public enum ActiveTargetSelector
{
    None = 0,
    Alive = 1,
    Other = 2,
    Wounded = 4,
    Male = 8,
    HasHand = 16,
    MoreHpThanOwner = 32,
    WithinAttackRange = 64,
    FactionSlashRequestTarget = 128
}

[Flags]
public enum ActiveActionGate
{
    None = 0,
    ClassicIdentity = 1,
    GlobalArrowBarragePlayable = 2,
    TwoSameSuitHandCards = 4,
    FactionSlashRequestAvailable = 8
}

public enum ActiveGiftRecoveryRule
{
    Recipient,
    OwnerAfterCumulativeTwoCards
}

public enum ActiveTargetPriority
{
    LowestHp,
    MostHostile
}

public sealed record ActiveActionSelection(
    ActiveCardSelector Cards = ActiveCardSelector.Hand,
    ActiveTargetSelector Targets = ActiveTargetSelector.None,
    ActiveActionGate Gate = ActiveActionGate.None,
    bool OncePerTurn = false,
    bool RequireClassicIdentityForTarget = false,
    ActiveGiftRecoveryRule GiftRecovery = ActiveGiftRecoveryRule.Recipient,
    ActiveTargetPriority AiTargetPriority = ActiveTargetPriority.LowestHp,
    string? ProviderFactionId = null);

/// <summary>
/// Active action legality and effect shape are independent of passive skill objects.
/// The trusted host still owns selection validation, movement, prompts and continuation.
/// </summary>
public sealed record ActiveActionRule(
    SkillKind Kind,
    string Name,
    Func<ActiveSkillContext, bool> IsAvailable,
    Func<ActiveSkillContext, ActiveSkillEffect> DescribeEffect,
    CardMoveReason? CostReason = null,
    CardMoveReason? DrawReason = null,
    CardMoveReason? TransferReason = null,
    string? PhaseGiftLedgerId = null,
    string? PhaseGiftSkillId = null,
    DecisionKind? SuitPromptKind = null,
    string? SuitChoiceAction = null,
    string? SuitChoiceIdPrefix = null)
{
    public bool CanUse(ActiveSkillContext context) => IsAvailable(context);
    public ActiveSkillEffect GetEffect(ActiveSkillContext context) => DescribeEffect(context);
}

public static class ActiveActionCatalog
{
    private static readonly IReadOnlyDictionary<SkillKind, ActiveActionSelection> Selections =
        new Dictionary<SkillKind, ActiveActionSelection>
        {
            [SkillKind.Jijiang] = new(Targets: ActiveTargetSelector.Alive | ActiveTargetSelector.FactionSlashRequestTarget,
                Gate: ActiveActionGate.ClassicIdentity | ActiveActionGate.FactionSlashRequestAvailable,
                AiTargetPriority: ActiveTargetPriority.MostHostile, ProviderFactionId: "shu"),
            [SkillKind.Qiangxi] = new(ActiveCardSelector.WeaponFromHandOrEquipment,
                ActiveTargetSelector.Alive | ActiveTargetSelector.Other | ActiveTargetSelector.WithinAttackRange,
                ActiveActionGate.ClassicIdentity, OncePerTurn: true,
                AiTargetPriority: ActiveTargetPriority.MostHostile),
            [SkillKind.Lijian] = new(ActiveCardSelector.HandAndEquipmentInClassic,
                ActiveTargetSelector.Alive | ActiveTargetSelector.Male,
                RequireClassicIdentityForTarget: true),
            [SkillKind.Luanji] = new(ActiveCardSelector.SameSuitPair,
                Gate: ActiveActionGate.ClassicIdentity |
                    ActiveActionGate.GlobalArrowBarragePlayable |
                    ActiveActionGate.TwoSameSuitHandCards),
            [SkillKind.Jieyin] = new(Targets: ActiveTargetSelector.Alive | ActiveTargetSelector.Other |
                ActiveTargetSelector.Male | ActiveTargetSelector.Wounded,
                RequireClassicIdentityForTarget: true),
            [SkillKind.Kujin] = new(),
            [SkillKind.Zhiheng] = new(ActiveCardSelector.HandAndEquipmentInClassic,
                OncePerTurn: true),
            [SkillKind.Rende] = new(Targets: ActiveTargetSelector.Alive | ActiveTargetSelector.Other,
                GiftRecovery: ActiveGiftRecoveryRule.OwnerAfterCumulativeTwoCards),
            [SkillKind.Fanjian] = new(Targets: ActiveTargetSelector.Alive | ActiveTargetSelector.Other,
                Gate: ActiveActionGate.ClassicIdentity,
                AiTargetPriority: ActiveTargetPriority.MostHostile),
            [SkillKind.Qingnang] = new(Targets: ActiveTargetSelector.Alive | ActiveTargetSelector.Wounded),
            [SkillKind.Huichun] = new(Targets: ActiveTargetSelector.Alive | ActiveTargetSelector.Wounded)
        };

    public static ActiveActionSelection Selection(SkillKind kind) =>
        Selections.TryGetValue(kind, out var selection) ? selection : new();

    private static readonly IReadOnlyDictionary<SkillKind, ActiveActionRule> Rules =
        new Dictionary<SkillKind, ActiveActionRule>
        {
            [SkillKind.Jijiang] = new(SkillKind.Jijiang, "激将",
                context => context.Owner.Phase == TurnPhase.Play,
                context => new ActiveSkillEffect(
                    ActiveSkillEffectKind.RequestSlash,
                    MinTargetCount: 1,
                    MaxTargetCount: 1)),
            [SkillKind.Qiangxi] = new(SkillKind.Qiangxi, "强袭",
                context => context.Owner.Phase == TurnPhase.Play &&
                    context.Owner.Hp > 0 &&
                    context.Owner.UsedActiveSkillKinds?.Contains(SkillKind.Qiangxi) != true,
                context => new ActiveSkillEffect(
                    ActiveSkillEffectKind.PayHpOrDiscardWeaponAndDamage,
                    HpCost: context.SelectedCardCount == 0 ? 1 : 0,
                    MinCardCount: 0,
                    MaxCardCount: 1,
                    MinTargetCount: 1,
                    MaxTargetCount: 1), CostReason: CardMoveReasons.QiangxiDiscard),
            [SkillKind.Lijian] = new(SkillKind.Lijian, "离间",
                context => context.Owner.IsOwnTurn &&
                    context.Owner.Phase == TurnPhase.Play &&
                    context.Owner.HandCount + context.AdditionalSelectableCardCount > 0 &&
                    context.Owner.UsedActiveSkillKinds?.Contains(SkillKind.Lijian) != true,
                context => new ActiveSkillEffect(
                    ActiveSkillEffectKind.DiscardAndStartDuel,
                    MinCardCount: 1,
                    MaxCardCount: 1,
                    MinTargetCount: 2,
                    MaxTargetCount: 2), CostReason: CardMoveReasons.LijianDiscard),
            [SkillKind.Luanji] = new(SkillKind.Luanji, "乱击",
                context => context.Owner.IsOwnTurn && context.Owner.Phase == TurnPhase.Play && context.Owner.HandCount >= 2,
                context => new ActiveSkillEffect(ActiveSkillEffectKind.StartArrowBarrage, MinCardCount: 2, MaxCardCount: 2)),
            [SkillKind.Jieyin] = new(SkillKind.Jieyin, "结姻",
                context => context.Owner.IsOwnTurn &&
                    context.Owner.Phase == TurnPhase.Play &&
                    context.Owner.HandCount >= 2 &&
                    context.Owner.UsedActiveSkillKinds?.Contains(SkillKind.Jieyin) != true,
                context => new ActiveSkillEffect(
                    ActiveSkillEffectKind.DiscardAndRecoverSelfAndTarget,
                    MinCardCount: 2,
                    MaxCardCount: 2,
                    MinTargetCount: 1,
                    MaxTargetCount: 1,
                    RecoveryAmount: 1), CostReason: CardMoveReasons.JieyinDiscard),
            [SkillKind.Kujin] = new(SkillKind.Kujin, "苦肉",
                context => context.Owner.Phase == TurnPhase.Play && context.Owner.Hp > 0,
                context => new ActiveSkillEffect(ActiveSkillEffectKind.LoseHpAndDraw, HpCost: 1, DrawCount: 2),
                DrawReason: CardMoveReasons.KujinDraw),
            [SkillKind.Zhiheng] = new(SkillKind.Zhiheng, "制衡",
                context => context.Owner.Phase == TurnPhase.Play &&
                    context.Owner.HandCount + context.AdditionalSelectableCardCount > 0 &&
                    (!context.EnforceOncePerTurn ||
                     context.Owner.UsedActiveSkillKinds?.Contains(SkillKind.Zhiheng) != true),
                context => new ActiveSkillEffect(
                    ActiveSkillEffectKind.DiscardAndDraw,
                    MinCardCount: 1,
                    MaxCardCount: Math.Max(
                        0,
                        context.Owner.HandCount + context.AdditionalSelectableCardCount)),
                CostReason: CardMoveReasons.ZhihengDiscard, DrawReason: CardMoveReasons.ZhihengDraw),
            [SkillKind.Rende] = new(SkillKind.Rende, "仁德",
                context => context.Owner.Phase == TurnPhase.Play &&
                    context.Owner.HandCount > 0 &&
                    (!context.EnforceOncePerTurn ||
                     context.Owner.UsedActiveSkillKinds?.Contains(SkillKind.Rende) != true),
                context => new ActiveSkillEffect(
                    ActiveSkillEffectKind.GiveCardsAndRecover,
                    MinCardCount: 1,
                    MaxCardCount: Math.Max(0, context.Owner.HandCount),
                    MinTargetCount: 1,
                    MaxTargetCount: 1,
                    RecoveryAmount: context.SelectedCardCount >= 2 ? 1 : 0),
                TransferReason: CardMoveReasons.RendeGive,
                PhaseGiftLedgerId: "cards-given",
                PhaseGiftSkillId: "classic:rende"),
            [SkillKind.Fanjian] = new(SkillKind.Fanjian, "反间",
                context => context.Owner.Phase == TurnPhase.Play &&
                    context.Owner.HandCount > 0 &&
                    context.Owner.UsedActiveSkillKinds?.Contains(SkillKind.Fanjian) != true,
                context => new ActiveSkillEffect(
                    ActiveSkillEffectKind.RevealGiftAndDamage,
                    MinTargetCount: 1,
                    MaxTargetCount: 1), TransferReason: CardMoveReasons.FanjianGive,
                SuitPromptKind: DecisionKind.Fanjian,
                SuitChoiceAction: "fanjian-choose-suit", SuitChoiceIdPrefix: "fanjian-suit-"),
            [SkillKind.Qingnang] = new(SkillKind.Qingnang, "青囊",
                context => context.Owner.Phase == TurnPhase.Play &&
                    context.Owner.HandCount > 0 &&
                    context.Owner.UsedActiveSkillKinds?.Contains(SkillKind.Qingnang) != true,
                context => new ActiveSkillEffect(
                    ActiveSkillEffectKind.DiscardAndRecover,
                    MinCardCount: 1,
                    MaxCardCount: 1,
                    MinTargetCount: 1,
                    MaxTargetCount: 1,
                    RecoveryAmount: 1), CostReason: CardMoveReasons.QingnangDiscard),
            [SkillKind.Huichun] = new(SkillKind.Huichun, "回春",
                context => context.Owner.Phase == TurnPhase.Play &&
                    context.Owner.HandCount >= 2 &&
                    context.Owner.UsedActiveSkillKinds?.Contains(SkillKind.Huichun) != true,
                context => new ActiveSkillEffect(
                    ActiveSkillEffectKind.DiscardAndRecoverTargets,
                    MinCardCount: 2,
                    MaxCardCount: 2,
                    MinTargetCount: 2,
                    MaxTargetCount: 3,
                    RecoveryAmount: 1), CostReason: CardMoveReasons.HuichunDiscard)
        };

    public static ActiveActionRule? Find(SkillKind kind) =>
        Rules.TryGetValue(kind, out var rule) ? rule : null;
}
