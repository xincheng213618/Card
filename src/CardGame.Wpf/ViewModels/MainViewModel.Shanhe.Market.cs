using System.Windows.Input;
using CardGame.Core.Campaigns;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public IReadOnlyList<string> ShanheMarketCategories { get; } = new[] { "战法", "装备", "卡牌", "技能", "补给" };
    private string _selectedShanheMarketCategory = "战法";
    public string SelectedShanheMarketCategory
    {
        get => _selectedShanheMarketCategory;
        set
        {
            if (!ShanheMarketCategories.Contains(value) || !SetProperty(ref _selectedShanheMarketCategory, value)) return;
            RaisePropertyChanged(nameof(ShanheMarketGoods)); RaisePropertyChanged(nameof(ShanheMarketCategoryNotice));
            RaisePropertyChanged(nameof(ShanheMarketHasGoods));
            RaisePropertyChanged(nameof(ShanheMarketSurchargeNotice));
        }
    }
    public IEnumerable<ShanheShopItem> ShanheMarketGoods => ShanheShopItems.Where(item => item.MarketCategory == SelectedShanheMarketCategory);
    public bool ShanheMarketHasGoods => ShanheMarketGoods.Any();
    public string ShanheMarketCategoryNotice => SelectedShanheMarketCategory switch
    {
        "技能" => "习得技能占用额外技能槽；槽位已满时选择替换。",
        "装备" => "装备在下场战斗开始时装备，同槽位的新装备会替换旧装备。",
        "卡牌" => "随身牌在今后每场战斗开始时获得。",
        "补给" => "补充军资与体力，继续征程。",
        _ => "购买战法加入本次征程，不占六个初始战法位。"
    };
    public string ShanheMarketSurchargeNotice => IsShanheShopActive && _shanheSession!.CreateCheckpoint().Options.UseCodexRewardPool &&
        SelectedShanheMarketCategory is "战法" or "装备" or "卡牌"
        ? $"每购买一次{SelectedShanheMarketCategory}，本店同类商品额外消耗铜币 +100" : "货品及价格以当前商店为准";
    public string ShanheMarketRefreshText => $"刷新 · {_shanheSession?.Snapshot.ShopRefreshCost ?? 100} 铜币";
    public string ShanheMarketRefreshRemaining => IsShanheShopActive ? $"剩余刷新次数：{_shanheSession!.Snapshot.ShopRefreshRemaining}" : "到达集市后可刷新货品";
    private RelayCommand? _refreshShanheMarketCommand;
    public ICommand RefreshShanheMarketCommand => _refreshShanheMarketCommand ??= new RelayCommand(() =>
    {
        if (_shanheSession is not { } session || !RefreshShanheMarketCommand.CanExecute(null)) return;
        ApplyShanheCommand(new RefreshShanheShopCommand(session.Revision));
    }, () => IsShanheShopActive && _shanheSession!.Snapshot.ShopRefreshRemaining > 0 &&
        _shanheSession.Snapshot.Gold >= _shanheSession.Snapshot.ShopRefreshCost && !IsTutorialActive);
    private void RefreshShanheMarketPresentation()
    {
        foreach (var property in new[] { nameof(ShanheMarketGoods), nameof(ShanheMarketHasGoods), nameof(ShanheMarketCategoryNotice),
            nameof(ShanheMarketSurchargeNotice), nameof(ShanheMarketRefreshText), nameof(ShanheMarketRefreshRemaining) }) RaisePropertyChanged(property);
        _refreshShanheMarketCommand?.NotifyCanExecuteChanged();
    }
}
