using System.Collections.ObjectModel;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<ShanheShopItem> ShanheShopItems { get; } = [];
    public ObservableCollection<ShanheBuildCard> ShanheCodexCards { get; } = [];
    private bool _shanheCodexShowCollection;
    public bool ShanheCodexShowCollection
    {
        get => _shanheCodexShowCollection;
        set { if (SetProperty(ref _shanheCodexShowCollection, value)) RefreshShanheModules(); }
    }
    public bool IsShanheShopActive => _shanheSession?.Snapshot is { Phase: ShanheCampaignPhase.NodeChoice } state &&
        _shanheSession.Definition.Nodes.Any(node => node.Id == state.CurrentNodeId && node.Kind == ShanheNodeKind.Shop);
    public IEnumerable<ShanheCampaignChoice> ShanheShopActions => IsShanheShopActive ? ShanheChoices.Where(choice => _shanheSession!.Snapshot.NodeChoices.Any(option => option.Id == choice.Id && option.RewardId is null)) : [];
    public int ShanheShopItemCount => ShanheShopItems.Count;
    public int ShanheCodexCount => ShanheCodexCards.Count;
    public string ShanheShopNotice => IsShanheShopActive ? "行商已至 · 货品与价格以当前商店为准，购买后立即存档。" :
        "前往山河地图的「行商」节点交易。下方为本篇商旅货品图鉴，仅供预览，不代表当前库存。";
    public string ShanheBuildSummary => _shanheSession is { } session
        ? $"{ShanheRankStatusText}   |   基础整备 · 体力上限 +{session.Snapshot.Bonuses.MaxHp} · 起手 +{session.Snapshot.Bonuses.InitialHand} · 摸牌 +{session.Snapshot.Bonuses.Draw} · 出杀 +{session.Snapshot.Bonuses.Slash}   |   额外技能 {session.Snapshot.ExtraSkillIds.Count}/{session.Snapshot.ExtraSkillSlots} 槽"
        : "尚未出征 · 可先浏览本篇战法，出征后在此查看基础整备与技能槽。";
    public string ShanheCodexNotice => ShanheCodexShowCollection ? "本次征程已获得的战法、额外技能与随身牌装备。" : "本篇全战法图鉴 · 查看效果，规划下一段征程。";

    private void RefreshShanheModules()
    {
        var definition = _shanheSession?.Definition ?? StandardShanheCampaignCatalog.CreateCodexDefinition(SelectedShanheDifficulty.Value, SelectedShanheScenario.Id);
        var state = _shanheSession?.Snapshot;
        ShanheShopItems.Clear();
        if (IsShanheShopActive)
        {
            foreach (var product in state!.ShopItems.Where(product => product.Reward is not null))
            {
                var reward = product.Reward;
                var choices = ShanheChoices.Where(choice => choice.Id == product.ChoiceId &&
                    (choice.ReplaceSkillId is null || product.ReplaceableSkillIds.Contains(choice.ReplaceSkillId))).ToArray();
                var status = product.State switch
                {
                    ShanheShopItemState.Purchased => "已购",
                    ShanheShopItemState.AlreadyOwned => "已拥有",
                    ShanheShopItemState.InsufficientResources => "铜币或体力不足",
                    ShanheShopItemState.NoSkillSlot => "没有可用技能槽",
                    ShanheShopItemState.RewardLimitReached => "已达获得上限",
                    _ => "购买"
                };
                if (choices.Length == 0 || !product.CanPurchase)
                    AddProduct(null, status);
                else AddProduct(choices[0], choices[0].ReplaceSkillId is null ? status : "购买 · 选择替换技能");
                void AddProduct(ShanheCampaignChoice? choice, string label) => ShanheShopItems.Add(DecorateShanheProduct(new(
                    product.ChoiceId, reward?.Name ?? product.Name, product.Description, reward is null ? "" : RewardRule(reward),
                    product.GoldCost, label, choice, product.HpCost, reward is null ? "行商货品" : RewardCategory(reward), product.State), reward));
            }
        }
        else
        {
            var entries = definition.Nodes.Where(node => node.Kind == ShanheNodeKind.Shop)
                .SelectMany(node => node.Choices ?? []).Where(choice => choice.RewardId is not null).DistinctBy(choice => choice.RewardId);
            foreach (var entry in entries)
            {
                var reward = definition.Rewards.Single(item => item.Id == entry.RewardId);
                ShanheShopItems.Add(DecorateShanheProduct(new(entry.Id, reward.Name, reward.Description, RewardRule(reward), entry.GoldCost,
                    "货品图鉴 · 预览", null, entry.HpCost, RewardCategory(reward)), reward));
            }
        }
        RefreshShanheCodex();
        RefreshShanheMarketPresentation();
        ShanheCodexCards.Clear();
        if (ShanheCodexShowCollection)
        {
            foreach (var card in ShanheTacticCards.Concat(ShanheExtraSkillCards)) ShanheCodexCards.Add(card);
            if (state is not null)
            {
                foreach (var card in state.StartingCards) AddLoadout(card, "随身牌 · 每战起手获得");
                foreach (var card in state.StartingEquipment) AddLoadout(card, ShanheStartingResourcesExpireOnUse
                    ? "随身装备 · 每战起手获得" : "开场装备 · 每战装备");
            }
        }
        else
            foreach (var reward in definition.Rewards.Where(reward => IsShanheTactic(reward)))
                ShanheCodexCards.Add(new(reward.Id, reward.Name, reward.Description, RewardRule(reward),
                    reward.TacticSkillId is null ? "基础整备 · 成长效果见上方" :
                    state?.TacticIds.Contains(reward.Id) == true ? "战法 · 本次征程已获得" : "战法图鉴 · 尚未获得"));
        foreach (var property in new[] { nameof(IsShanheShopActive), nameof(ShanheShopActions), nameof(ShanheShopItemCount), nameof(ShanheCodexCount), nameof(ShanheShopNotice), nameof(ShanheCodexNotice), nameof(ShanheBuildSummary) }) RaisePropertyChanged(property);
    }
    private static ShanheShopItem DecorateShanheProduct(ShanheShopItem item, ShanheRewardDefinition? reward)
    {
        if (reward is null) return item;
        var card = reward.StartingEquipment?.FirstOrDefault() ?? reward.StartingCards?.FirstOrDefault();
        var entry = ShanheCodexTree.Entries.FirstOrDefault(node => ShanheCodexTree.RuntimeRewardId(node.Id) == reward.Id);
        var rarity = StandardShanheCampaignCatalog.ShopMetadataByRewardId.TryGetValue(reward.Id, out var metadata) ? metadata.Rarity : entry?.Rarity;
        return item with
        {
            TacticImage = IsShanheTactic(reward) ? ShanheArt.Get(metadata?.SourceRewardId ?? entry?.RewardId ?? 0) : null,
            CardImage = card is null ? null : CardArt.Get(card.Kind),
            RankArtwork = card is null ? null : CardArt.GetRank(card.Rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => card.Rank.ToString() }, GetSuitGlyph(card.Suit)),
            SuitArtwork = card is null ? null : CardArt.GetSuit(GetSuitGlyph(card.Suit)),
            Accent = rarity switch { 2 => "#81619A", 3 => "#BF853C", 4 => "#A94531", _ => "#987653" }
        };
    }
    private static bool IsShanheTactic(ShanheRewardDefinition reward) => reward.OpeningSkillRewardIds is not null || reward.TacticSkillId is not null || reward.Bonuses is { } bonuses && (bonuses.MaxHp != 0 || bonuses.InitialHand != 0 || bonuses.Draw != 0 || bonuses.Slash != 0);
    private static string RewardCategory(ShanheRewardDefinition reward) => IsShanheTactic(reward) ? "战法" : reward.SkillId is not null ? "额外技能" : reward.StartingEquipment?.Count > 0 ? "随身装备" : reward.StartingCards?.Count > 0 ? "随身牌" : "补给";
    private string RewardRule(ShanheRewardDefinition reward) => reward.TacticSkillId is { } id && CurrentPresentationRegistry.Skills.TryGetValue(id, out var skill) ? skill.Description :
        reward.SkillId is { } extra && CurrentPresentationRegistry.Skills.TryGetValue(extra, out var learned) ? learned.Description : string.Empty;
    private void AddLoadout(ShanheStartingCard card, string source) => ShanheCodexCards.Add(new(card.Kind.ToString(), CardCatalog.Get(card.Kind).DisplayName,
        $"{GetSuitGlyph(card.Suit)}{(card.Rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => card.Rank.ToString() })}", ShanheStartingResourceNotice, source));
}
public sealed record ShanheShopItem(string Id, string Name, string Description, string RuleDescription, int Price, string Status, ShanheCampaignChoice? Choice, int HpCost = 0, string Category = "货品", ShanheShopItemState? State = null)
{
    public System.Windows.Media.ImageSource? TacticImage { get; init; }
    public string PriceText => $"{Price} 铜币" + (HpCost == 0 ? "" : $" · {HpCost} 体力");
    public bool HasRuleDescription => !string.IsNullOrWhiteSpace(RuleDescription) && RuleDescription != Description;
    public System.Windows.Media.ImageSource? CardImage { get; init; }
    public System.Windows.Media.ImageSource? RankArtwork { get; init; }
    public System.Windows.Media.ImageSource? SuitArtwork { get; init; }
    public bool HasCardImage => CardImage is not null;
    public string Accent { get; init; } = "#987653";
    public string MarketCategory => Category switch { "额外技能" => "技能", "随身装备" => "装备", "随身牌" => "卡牌", _ => Category };
    public string DisplayName => Name.Replace("战法·", "").Replace("传习·", "");
    public string Glyph => DisplayName[..Math.Min(2, DisplayName.Length)];
    public bool CanBuy => Choice?.IsEnabled == true;
}




