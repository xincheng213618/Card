using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ShanheModulesUiChecks
{
    public static void ShopPurchaseAndCodexNavigation(string output)
    {
        using var vm = new MainViewModel(false, 270017, true, new MemorySaveStore(), useExpandedContent: true,
            shanheSaveStore: new MemoryShanheCampaignSaveStore());
        var codex = new ShanheCodexPanel { DataContext = vm };
        var host = new ShanheCampaignPanel { DataContext = vm };
        Layout(host);
        Layout(codex);
        Program.Assert(vm.ShanheCodexCount > 0 && !vm.IsShanheShopActive && vm.ShanheShopItems.All(item => !item.CanBuy),
            "Before a run the catalog must be explorable and merchant previews must never permit purchase.");
        Program.Assert(vm.ShanheCodexCards.All(card => card.Id is not "supplies" and not "medicine") &&
            vm.ShanheShopItems.Any(item => item.Category == "随身牌") && vm.ShanheShopItems.Any(item => item.Category == "随身装备"),
            "One-off money and healing supplies must not be presented as persistent tactics.");
        var toggle = (CheckBox)codex.FindName("CollectionToggle");
        toggle.IsChecked = true;
        Layout(codex);
        Program.Assert(vm.ShanheCodexShowCollection && vm.ShanheCodexCount == 0,
            "The actual collection toggle must display the honest empty collection.");
        toggle.IsChecked = false;
        Layout(codex);
        Program.Assert(!vm.ShanheCodexShowCollection && vm.ShanheCodexCount > 0,
            "Switching back must restore the real tactic catalog.");

        var original = StandardShanheCampaignCatalog.CreateDefinition(1, vm.SelectedShanheScenario.Id);
        var shop = original.Nodes.First(node => node.Kind == ShanheNodeKind.Shop && node.Choices!.Any(choice => choice.GoldCost > 0 && choice.RewardId is not null));
        var tacticChoice = shop.Choices!.First(choice => choice.RewardId is { } rewardId && original.Rewards.Single(reward => reward.Id == rewardId).TacticSkillId is not null);
        var ownedAlias = tacticChoice with { Id = "module-owned-alias", Name = "同战法其他货品" };
        shop = shop with { Choices = shop.Choices!.Append(ownedAlias).ToArray() };
        var definition = original with { Nodes = original.Nodes.Select(node => node.Id == shop.Id ? shop : node).ToArray(), StartingNodeIds = original.StartingNodeIds.Append(shop.Id).Distinct().ToArray(), StartingGold = 0, RandomStartingNode = false };
        var session = ShanheCampaignSession.Create(definition, new(270017, definition.PlayerGeneralIds[0]), StandardShanheCampaignCatalog.CreateBattleRegistry);
        Program.Assert(session.Submit(new MoveShanheNodeCommand(shop.Id, session.Revision)).Accepted, "The real fixture must enter its shop.");
        Attach(vm, session);
        Layout(host);
        var tabs = (TabControl)host.FindName("ShanheJourneyTabs");
        Program.Assert(tabs.SelectedIndex == 2,
            "Entering a real merchant must open the dedicated merchant page in the campaign host.");
        var panel = new ShanheShopPanel { DataContext = vm };
        Layout(panel);
        Program.Assert(vm.IsShanheShopActive && vm.ShanheShopItems.Any(item => item.Price > 0 && !item.CanBuy),
            "A merchant with zero gold must display actual prices and disable unaffordable goods.");
        definition = definition with { StartingGold = 999 };
        session = ShanheCampaignSession.Create(definition, new(270017, definition.PlayerGeneralIds[0]), StandardShanheCampaignCatalog.CreateBattleRegistry);
        session.Submit(new MoveShanheNodeCommand(shop.Id, session.Revision));
        Attach(vm, session);
        Layout(panel);
        var item = vm.ShanheShopItems.Single(item => item.Id == tacticChoice.Id);
        var oldChoice = item.Choice!;
        var oldGold = session.Snapshot.Gold;
        var button = Program.Find<Button>(panel).Single(button => ReferenceEquals(button.CommandParameter, item));
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
        Layout(panel);
        Program.Assert(session.Snapshot.Gold == oldGold - item.Price && !vm.ChooseShanheOptionCommand.CanExecute(oldChoice),
            "Clicking the actual purchase button must debit exactly its price and invalidate the old option.");
        Program.Assert(vm.ShanheShopItems.Single(product => product.Id == item.Id) is { State: ShanheShopItemState.Purchased, Status: "已购", CanBuy: false } &&
            vm.ShanheShopItems.Single(product => product.Id == ownedAlias.Id) is { State: ShanheShopItemState.AlreadyOwned, Status: "已拥有", CanBuy: false },
            "Purchased stock must remain visible and differ from a separate product whose tactic is already owned.");
        var revision = session.Revision;
        vm.ChooseShanheOptionCommand.Execute(oldChoice);
        Program.Assert(session.Revision == revision, "Reusing a stale purchase must not repeat the transaction.");
        Program.Render(panel, 1000, 530, System.IO.Path.Combine(output, "shanhe-shop-module.png"));
        Program.Render(codex, 1000, 530, System.IO.Path.Combine(output, "shanhe-codex-module.png"));
        Program.Render(host, 1200, 750, System.IO.Path.Combine(output, "shanhe-shop-page.png"));
        tabs.SelectedIndex = 1;
        Program.Render(host, 1200, 750, System.IO.Path.Combine(output, "shanhe-codex-page.png"));
        Program.Assert(host.HandleEscape() && tabs.SelectedIndex == 0,
            "Escape must return from the codex to the route without changing the campaign.");
        tabs.SelectedIndex = 2;
        Layout(host);
        var leave = vm.ShanheShopActions.Single(action => action.Id == "$leave");
        var leaveButton = Program.Find<Button>(host).Single(button => ReferenceEquals(button.CommandParameter, leave));
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(leaveButton, null);
        Layout(host);
        Program.Assert(!vm.IsShanheShopActive && tabs.SelectedIndex == 0,
            "The real leave-shop button must return to the route and end the shop's transaction phase.");
        vm.SelectedShanheMarketCategory = "装备";
        Program.Assert(vm.ShanheMarketCategoryNotice.Contains("加入手牌", StringComparison.Ordinal) &&
            vm.ShanheCarryoverNotice.Contains("被技能提前取得则保留", StringComparison.Ordinal),
            "A new expedition must explain that starting equipment enters the hand and claimed cards remain.");
        var refreshedCarryover = false;
        vm.PropertyChanged += (_, change) => refreshedCarryover |= change.PropertyName == nameof(MainViewModel.ShanheCarryoverNotice);
        var legacy = ShanheCampaignSession.Create(definition with { StartingResourcesExpireOnUse = false },
            new(270017, definition.PlayerGeneralIds[0]), StandardShanheCampaignCatalog.CreateBattleRegistry);
        Attach(vm, legacy);
        Program.Assert(refreshedCarryover && vm.ShanheMarketCategoryNotice.Contains("开始时装备", StringComparison.Ordinal) &&
            vm.ShanheCarryoverNotice.Contains("直接装备", StringComparison.Ordinal) &&
            !vm.ShanheCarryoverNotice.Contains("移出本局", StringComparison.Ordinal),
            "Loading a legacy expedition must notify the bound carryover notice and retain its direct-equipment contract.");
        host.DataContext = null;
    }
    private static void Attach(MainViewModel vm, ShanheCampaignSession session)
    {
        typeof(MainViewModel).GetField("_shanheSession", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, session);
        typeof(MainViewModel).GetMethod("RefreshShanhePresentation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1000, 530)); root.Arrange(new Rect(0, 0, 1000, 530)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
}



