using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Persistence;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IShanheCodexStore _shanheCodexStore = null!;
    private ShanheCodexProfile _shanheCodexProfile = new();
    private bool _shanheCodexReadFailed;
    private string? _selectedCodexId;
    private int _selectedCodexSlot;
    private string _shanheCodexMessage = "选中节点查看效果；选中左侧槽位安排初始战法。";
    public ObservableCollection<ShanheCodexNode> ShanheCodexNodes { get; } = [];
    public ObservableCollection<ShanheCodexLink> ShanheCodexLinks { get; } = [];
    public IReadOnlyList<ShanheCodexColumn> ShanheCodexColumns { get; } = Enumerable.Range(1, 80).Select(i => new ShanheCodexColumn(i)).ToArray();
    public ObservableCollection<ShanheCodexSlot> ShanheCodexSlots { get; } = [];
    public ShanheCodexNode? SelectedCodexNode => ShanheCodexNodes.FirstOrDefault(node => node.Id == _selectedCodexId);
    public int ShanheBookBalance => _shanheCodexProfile.Books;
    public string ShanheCodexLoadoutCount => $"初始战法  {_shanheCodexProfile.EquippedTacticIds.Count(id => id is not null)} / 6";
    public string ShanheCodexMessage { get => _shanheCodexMessage; private set => SetProperty(ref _shanheCodexMessage, value); }
    public string ShanheCodexEquipLabel => $"装备至第 {_selectedCodexSlot + 1} 位";
    private bool _isShanheCodexPickerOpen;
    public bool IsShanheCodexPickerOpen { get => _isShanheCodexPickerOpen; private set => SetProperty(ref _isShanheCodexPickerOpen, value); }
    public string ShanheCodexSlotHint => $"第 {_selectedCodexSlot + 1} 位 · 可装备{SlotRarityName(_selectedCodexSlot)}及以下战法";
    public IEnumerable<ShanheCodexNode> ShanheEquippableTactics => ShanheCodexNodes.Where(node => node.IsAvailable && node.IsUnlocked && node.IsTactic && FitsSelectedSlot(node));
    public bool HasShanheEquippableTactics => ShanheEquippableTactics.Any();
    public ICommand CloseShanheCodexPickerCommand { get; private set; } = null!;
    public ICommand SelectCodexNodeCommand { get; private set; } = null!;
    public ICommand SelectCodexSlotCommand { get; private set; } = null!;
    public ICommand UnlockCodexNodeCommand { get; private set; } = null!;
    public ICommand EquipCodexTacticCommand { get; private set; } = null!;
    public ICommand RemoveCodexTacticCommand { get; private set; } = null!;

    private void InitializeShanheCodex(IShanheCodexStore? store, bool memoryOnly)
    {
        _shanheCodexStore = store ?? (memoryOnly ? new MemoryShanheCodexStore() : new FileShanheCodexStore());
        try
        {
            if (_shanheCodexStore.Exists) _shanheCodexProfile = _shanheCodexStore.Read();
            _shanheCodexProfile = ShanheCodexLoadouts.EnsurePlans(_shanheCodexProfile);
        }
        catch (Exception error) when (IsSaveException(error))
        {
            _shanheCodexProfile = ShanheCodexLoadouts.EnsurePlans(new());
            _shanheCodexReadFailed = true; ShanheCodexMessage = $"山河册读取失败，原文件已保留：{ReadableSaveError(error)}";
        }
        SelectCodexNodeCommand = new RelayCommand<ShanheCodexNode>(node =>
        {
            if (node is null || !ShanheCodexNodes.Contains(node)) return;
            _selectedCodexId = node.Id; RefreshShanheCodex();
        });
        SelectCodexSlotCommand = new RelayCommand<ShanheCodexSlot>(slot =>
        {
            if (slot is null || !ShanheCodexSlots.Contains(slot)) return;
            _selectedCodexSlot = slot.Index;
            if (slot.RewardId is not null) _selectedCodexId = ShanheCodexNodes.FirstOrDefault(node => node.RuntimeRewardId == slot.RewardId)?.Id;
            IsShanheCodexLoadoutManagerOpen = false;
            RefreshShanheCodex();
            IsShanheCodexPickerOpen = CanPrepareShanheExpedition;
            if (IsShanheCodexPickerOpen && !ShanheEquippableTactics.Any(node => node.Id == _selectedCodexId) && ShanheEquippableTactics.FirstOrDefault() is { } first)
            { _selectedCodexId = first.Id; RefreshShanheCodex(); }
        });
        CloseShanheCodexPickerCommand = new RelayCommand(() => IsShanheCodexPickerOpen = false);
        UnlockCodexNodeCommand = new RelayCommand(UnlockSelectedCodexNode,
            () => CanPrepareShanheExpedition && !_shanheCodexReadFailed && SelectedCodexNode is { IsUnlocked: false, IsAvailable: true } node && ShanheBookBalance >= node.PathCost);
        EquipCodexTacticCommand = new RelayCommand(EquipSelectedCodexTactic,
            () => CanPrepareShanheExpedition && !_shanheCodexReadFailed && SelectedCodexNode is { IsTactic: true, IsUnlocked: true, IsEquipped: false, IsAvailable: true } node && FitsSelectedSlot(node));
        RemoveCodexTacticCommand = new RelayCommand(() =>
        {
            if (!RemoveCodexTacticCommand.CanExecute(null)) return;
            var slots = _shanheCodexProfile.EquippedTacticIds.ToArray(); slots[_selectedCodexSlot] = null;
            SaveCodexProfile(ShanheCodexLoadouts.UpdateActiveSlots(_shanheCodexProfile, slots), "已卸下战法，下次出征生效。");
        }, () => CanPrepareShanheExpedition && !_shanheCodexReadFailed && _shanheCodexProfile.EquippedTacticIds[_selectedCodexSlot] is not null);
        InitializeShanheCodexLoadouts();
    }

    private void UnlockSelectedCodexNode()
    {
        if (!UnlockCodexNodeCommand.CanExecute(null) || SelectedCodexNode is not { } node) return;
        var path = ShanheCodexTree.RequiredPath(node.TreeId, _shanheCodexProfile.UnlockedRewardIds);
        var researched = _shanheCodexProfile.UnlockedRewardIds.Concat(path.Select(entry => ShanheCodexTree.Key(entry.Id)))
            .Distinct(StringComparer.Ordinal).ToArray();
        var definition = StandardShanheCampaignCatalog.CreateCodexDefinition(SelectedShanheDifficulty.Value, SelectedShanheScenario.Id);
        var runtime = ShanheCodexTree.ResolveUnlockedRewardIds(researched, definition.Rewards.Select(reward => reward.Id));
        SaveCodexProfile(_shanheCodexProfile with
        {
            Books = checked(_shanheCodexProfile.Books - path.Sum(entry => entry.Cost)),
            UnlockedRewardIds = researched.Concat(runtime).Distinct(StringComparer.Ordinal).ToArray()
        }, $"已研习{node.Name}及前置路线，消耗{node.PathCost}册。" + (node.IsTactic ? "可装备为初始战法。" : "下次征程可在商店或奖励中遇见。"));
    }
    private void EquipSelectedCodexTactic()
    {
        if (!EquipCodexTacticCommand.CanExecute(null) || SelectedCodexNode is not { } node) return;
        var slots = _shanheCodexProfile.EquippedTacticIds.ToArray(); slots[_selectedCodexSlot] = node.RuntimeRewardId;
        var definition = StandardShanheCampaignCatalog.CreateCodexDefinition(SelectedShanheDifficulty.Value, SelectedShanheScenario.Id);
        var runtime = ShanheCodexTree.ResolveUnlockedRewardIds(_shanheCodexProfile.UnlockedRewardIds, definition.Rewards.Select(reward => reward.Id));
        if (SaveCodexProfile(ShanheCodexLoadouts.UpdateActiveSlots(_shanheCodexProfile with
        {
            UnlockedRewardIds = _shanheCodexProfile.UnlockedRewardIds.Concat(runtime).Distinct(StringComparer.Ordinal).ToArray()
        }, slots), $"{node.Name}已装入第{_selectedCodexSlot + 1}位，下次出征生效。")) IsShanheCodexPickerOpen = false;
    }
    private bool SaveCodexProfile(ShanheCodexProfile next, string message)
    {
        if (_shanheCodexReadFailed) return false;
        try
        {
            _shanheCodexStore.Write(next); _shanheCodexProfile = next;
            ShanheCodexMessage = message; RefreshShanheCodex(); return true;
        }
        catch (Exception error) when (IsSaveException(error))
        { ShanheCodexMessage = $"山河册未保存，本次操作未生效：{ReadableSaveError(error)}"; return false; }
    }
    private ShanheCampaignOptions CreateCodexCampaignOptions(int seed, string generalId, ShanheCampaignDefinition definition)
    {
        var known = definition.Rewards.Select(reward => reward.Id).ToHashSet(StringComparer.Ordinal);
        return new(seed, generalId)
        {
            ExpeditionId = Guid.NewGuid().ToString("N"),
            UseCodexRewardPool = true,
            UnlockedRewardIds = ShanheCodexTree.ResolveUnlockedRewardIds(
                _shanheCodexProfile.UnlockedRewardIds.Concat(StandardShanheCampaignCatalog.GetDefaultCodexUnlockedRewardIds(definition)).ToArray(), known),
            EquippedTacticIds = _shanheCodexProfile.EquippedTacticIds.OfType<string>().Where(known.Contains).ToArray()
        };
    }

    private void RefreshShanheCodex()
    {
        if (_selectedShanheDifficulty is null || _shanheCodexStore is null) return;
        if (!CanPrepareShanheExpedition)
        {
            IsShanheCodexPickerOpen = false;
            IsShanheCodexLoadoutManagerOpen = false;
        }
        // This is the next expedition's loadout; a resumed expedition keeps its frozen options.
        var definition = StandardShanheCampaignCatalog.CreateCodexDefinition(SelectedShanheDifficulty.Value, SelectedShanheScenario.Id);
        var rewardIds = definition.Rewards.Select(reward => reward.Id).ToHashSet(StringComparer.Ordinal);
        ShanheCodexNodes.Clear();
        ShanheCodexLinks.Clear();
        foreach (var entry in ShanheCodexTree.Entries)
        {
            var key = ShanheCodexTree.Key(entry.Id);
            var runtimeId = ShanheCodexTree.RuntimeRewardId(entry.Id);
            var path = ShanheCodexTree.RequiredPath(entry.Id, _shanheCodexProfile.UnlockedRewardIds);
            var node = new ShanheCodexNode(key, entry.Name, entry.Description, entry.RewardType,
                entry.Lane - 1, entry.Layer - 1, entry.Rarity switch { 2 => "稀有", 3 => "史诗", 4 => "传说", _ => "普通" },
                entry.Cost, _shanheCodexProfile.UnlockedRewardIds.Contains(key),
                runtimeId is not null && _shanheCodexProfile.EquippedTacticIds.Contains(runtimeId), key == _selectedCodexId,
                entry.RewardType == "装备" ? ShanheArt.Get(entry.RewardId) ?? (ShanheCodexTree.EquipmentKind(entry.Id) is { } kind ? CardArt.Get(kind) : null) : null)
            {
                TacticImage = entry.RewardType is "战法" or "随机战法" ? ShanheArt.Get(entry.RewardId) : null,
                TreeId = entry.Id, RuntimeRewardId = runtimeId, IsAvailable = runtimeId is not null && rewardIds.Contains(runtimeId),
                PathCost = path.Sum(item => item.Cost),
                PathDescription = path.Count == 0 ? "此路线已研习" : "前置路线：" + string.Join(" → ", path.Select(item => item.Name))
            };
            ShanheCodexNodes.Add(node);
        }
        foreach (var entry in ShanheCodexTree.Entries)
        {
            var to = ShanheCodexNodes.Single(node => node.TreeId == entry.Id);
            foreach (var parentId in entry.Parents.Concat(entry.Conditions).Distinct())
            {
                var from = ShanheCodexNodes.Single(node => node.TreeId == parentId);
                var x1 = from.X + 49; var y1 = from.Y + 31; var x2 = to.X + 49; var y2 = to.Y + 31;
                var mid = x1 + (x2 - x1) * .5;
                var data = FormattableString.Invariant($"M{x1},{y1} H{mid} V{y2} H{x2}");
                ShanheCodexLinks.Add(new(data, from.IsUnlocked && to.IsUnlocked ? "#A37C37" : "#7B6B4E"));
            }
        }
        if (SelectedCodexNode is null && ShanheCodexNodes.FirstOrDefault() is { } first)
        { _selectedCodexId = first.Id; var index = ShanheCodexNodes.IndexOf(first); ShanheCodexNodes[index] = first with { IsSelected = true }; }
        ShanheCodexSlots.Clear();
        for (var i = 0; i < 6; i++)
        {
            var id = _shanheCodexProfile.EquippedTacticIds[i];
            var node = ShanheCodexNodes.FirstOrDefault(node => node.RuntimeRewardId == id && id is not null);
            ShanheCodexSlots.Add(new(i, id, node?.Name ?? (id is null ? "未装备" : "其他篇章"), node?.Accent ?? (i switch { 2 or 3 => "#7B609B", 4 => "#BC702F", 5 => "#A33C2D", _ => "#9D8154" }), i == _selectedCodexSlot) { TacticImage = node?.TacticImage });
        }
        foreach (var property in new[] { nameof(SelectedCodexNode), nameof(ShanheBookBalance), nameof(ShanheCodexLoadoutCount), nameof(ShanheCodexEquipLabel) }) RaisePropertyChanged(property);
        foreach (var command in new[] { UnlockCodexNodeCommand, EquipCodexTacticCommand, RemoveCodexTacticCommand }.OfType<RelayCommand>()) command.NotifyCanExecuteChanged();
        foreach (var property in new[] { nameof(ShanheCodexSlotHint), nameof(ShanheEquippableTactics), nameof(HasShanheEquippableTactics) }) RaisePropertyChanged(property);
        RefreshShanheCodexLoadouts(rewardIds);
    }
    private static int SlotRarity(int index) => index switch { 0 or 1 => 1, 2 or 3 => 2, 4 => 3, _ => 4 };
    private static string SlotRarityName(int index) => SlotRarity(index) switch { 1 => "普通", 2 => "稀有", 3 => "史诗", _ => "传说" };
    private bool FitsSelectedSlot(ShanheCodexNode node) => (node.Rarity switch { "稀有" => 2, "史诗" => 3, "传说" => 4, _ => 1 }) <= SlotRarity(_selectedCodexSlot);

}

public sealed record ShanheCodexNode(string Id, string Name, string Description, string Kind, int Lane, int Column,
    string Rarity, int? UnlockCost, bool IsUnlocked, bool IsEquipped, bool IsSelected, ImageSource? CardImage)
{
    public ImageSource? TacticImage { get; init; }
    public int TreeId { get; init; }
    public string? RuntimeRewardId { get; init; }
    public bool IsAvailable { get; init; }
    public int PathCost { get; init; }
    public string PathDescription { get; init; } = "";
    public double X => Column * 124 + 12;
    public double Y => Lane * 71 + 20;
    public bool IsTactic => Kind is "战法" or "随机战法";
    public bool IsSkill => Kind == "技能";
    public bool IsEquipment => Kind == "装备";
    public bool HasCardImage => CardImage is not null;
    public string Glyph => Lane switch { 0 => "刃", 1 => "御", 2 => "焰", 3 => "反", _ => "生" };
    public string Accent => Rarity switch { "稀有" => "#7B609B", "史诗" => "#BC702F", "传说" => "#A33C2D", _ => "#8A7049" };
    public string StateLabel => IsEquipped ? "已装备" : IsUnlocked ? "已解锁" : UnlockCost is { } cost ? $"{cost} 册" : "未解锁";
    public string ActionHint => !IsAvailable ? "图鉴预览 · 当前篇章尚不可获得" : IsTactic ? "解锁后可装备为初始战法" : "解锁后加入征程商店与奖励池";
    public string CostLabel => IsUnlocked ? "已研习" : $"本项 {UnlockCost} 册 · 路线共 {PathCost} 册";
    public string AccessibleName => $"{Name}，{Rarity}{Kind}，{StateLabel}，{Description}，{ActionHint}";
    public double NodeOpacity => IsUnlocked ? 1 : .72;
}
public sealed record ShanheCodexSlot(int Index, string? RewardId, string Name, string Accent, bool IsSelected)
{
    public ImageSource? TacticImage { get; init; }
    public double X => Index % 2 == 0 ? 4 : 102;
    public double Y => Index * 56 + 4;
    public string SlotLabel => $"战法位 {Index + 1}";
    public string Glyph => RewardId is null ? "＋" : Name[..1];
}

public sealed record ShanheCodexLink(string Data, string Color);
public sealed record ShanheCodexColumn(int Number)
{
    public double X => (Number - 1) * 124 + 51;
    public string Label => Number < 11 ? new[] { "", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" }[Number] : Number.ToString();
}
