using System.IO;
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

internal static class ShanheCodexUiChecks
{
    public static void UnlockEquipPersistAndMarket(string output)
    {
        var store = new MemoryShanheCodexStore();
        store.Write(new() { Books = 5000 }); // Earned currency fixture; never alters the player's profile.
        var campaign = new MemoryShanheCampaignSaveStore();
        using var vm = Create(store, campaign);
        vm.SelectedShanheScenario = vm.ShanheScenarios.Single(item => item.Id == StandardShanheCampaignCatalog.Returning2026EditionId);
        var book = new ShanheCodexPanel { DataContext = vm };
        Layout(book);
        Program.Assert(vm.ShanheCodexSlots.Count == 6 && vm.ShanheCodexSlots.All(slot => slot.RewardId is null) &&
            vm.ShanheCodexNodes.Count == 150 && Node(16).PathCost == 24 && Node(57).PathCost == 272,
            "Six initial slots and the actual prerequisite costs must be shown, rather than generated lanes or flat unlock prices.");
        Program.Render(book, 1400, 714, Path.Combine(output, "shanhe-book-empty.png"));
        vm.SelectCodexNodeCommand.Execute(Node(2));
        vm.UnlockCodexNodeCommand.Execute(null);
        Layout(book);
        var slotButton = Program.Find<Button>(book).Single(button => button.CommandParameter is ShanheCodexSlot { Index: 0 });
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(slotButton, null);
        Layout(book);
        Program.Assert(vm.IsShanheCodexPickerOpen && vm.HasShanheEquippableTactics &&
            vm.ShanheEquippableTactics.All(node => node.Rarity == "普通") && vm.SelectedCodexNode is { TreeId: 2, TacticImage: not null },
            "Clicking an initial slot must open the picker with only unlocked tactics within that slot's rarity cap.");
        Program.Render(book, 1400, 714, Path.Combine(output, "shanhe-tactic-picker.png"));
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke((Button)book.FindName("PickerEquipButton"), null);
        Layout(book);
        Program.Assert(!vm.IsShanheCodexPickerOpen && vm.ShanheCodexSlots[0].RewardId == StandardShanheCampaignCatalog.OpeningInheritanceRewardId,
            "The actual picker confirmation must equip inheritance, persist it and close the overlay.");
        var selectedIds = new[] { 16, 20, 33, 37, 44, 71 };
        for (var index = 0; index < selectedIds.Length; index++)
        {
            vm.SelectCodexSlotCommand.Execute(vm.ShanheCodexSlots[index]);
            vm.SelectCodexNodeCommand.Execute(Node(selectedIds[index]));
            var cost = vm.SelectedCodexNode!.PathCost;
            var balance = vm.ShanheBookBalance;
            Program.Assert(vm.UnlockCodexNodeCommand.CanExecute(null), "A supported tactic with sufficient books must be unlockable.");
            vm.UnlockCodexNodeCommand.Execute(null);
            Program.Assert(vm.ShanheBookBalance == balance - cost && vm.SelectedCodexNode!.IsUnlocked &&
                !vm.UnlockCodexNodeCommand.CanExecute(null), "Unlock must deduct the remaining route exactly once.");
            vm.UnlockCodexNodeCommand.Execute(null);
            vm.EquipCodexTacticCommand.Execute(null);
            Program.Assert(vm.ShanheBookBalance == balance - cost && vm.ShanheCodexSlots[index].RewardId == vm.SelectedCodexNode!.RuntimeRewardId,
                "A disabled repeated unlock cannot spend currency, and each selected slot receives the actual tactic.");
        }
        foreach (var id in new[] { 3, 57 })
        {
            vm.SelectCodexNodeCommand.Execute(Node(id));
            if (vm.UnlockCodexNodeCommand.CanExecute(null)) vm.UnlockCodexNodeCommand.Execute(null);
            Program.Assert(vm.SelectedCodexNode!.IsUnlocked && !vm.EquipCodexTacticCommand.CanExecute(null),
                "Skill and equipment research must never be equippable in initial tactic slots.");
        }
        var saved = store.Read();
        Program.Assert(saved.EquippedTacticIds.OfType<string>().Count() == 6 && saved.UnlockedRewardIds.Contains("codex-57"),
            "Account research and all six slots must persist independently of a run.");
        using (var cold = Create(store, new MemoryShanheCampaignSaveStore()))
            Program.Assert(cold.ShanheBookBalance == saved.Books && cold.ShanheCodexSlots.Select(slot => slot.RewardId).SequenceEqual(saved.EquippedTacticIds),
                "A fresh VM must restore the independent book and loadout profile.");
        vm.SelectCodexNodeCommand.Execute(Node(16));
        Layout(book);
        ((ScrollViewer)book.FindName("CodexTreeScroll")).ScrollToHorizontalOffset(Node(16).X - 160);
        Program.Render(book, 1400, 714, Path.Combine(output, "shanhe-book-equipped.png"));
        vm.StartShanheCommand.Execute(null);
        var checkpoint = campaign.Read().Campaign;
        Program.Assert(checkpoint.Options.UseCodexRewardPool && checkpoint.Options.EquippedTacticIds!.Count == 6 &&
            checkpoint.Options.UnlockedRewardIds!.Contains("codex-57") && !vm.EquipCodexTacticCommand.CanExecute(null),
            "Starting must freeze the equipped loadout and research pool; active expeditions cannot edit that loadout.");

        // Actual random-stock merchant, with fixture funds and all supported research available.
        var original = StandardShanheCampaignCatalog.CreateCodexDefinition(1, vm.SelectedShanheScenario.Id);
        var merchant = original.Nodes.First(node => node.Kind == ShanheNodeKind.Shop);
        var definition = original with { StartingNodeIds = original.StartingNodeIds.Append(merchant.Id).Distinct().ToArray(), StartingGold = 3000, RandomStartingNode = false };
        var session = ShanheCampaignSession.Create(definition, new(270017, definition.PlayerGeneralIds[0])
        { UseCodexRewardPool = true, UnlockedRewardIds = definition.Rewards.Select(reward => reward.Id).ToArray() }, StandardShanheCampaignCatalog.CreateBattleRegistry);
        Program.Assert(session.Submit(new MoveShanheNodeCommand(merchant.Id, session.Revision)).Accepted, "Merchant fixture must enter through the real command path.");
        Attach(vm, session);
        var shop = new ShanheShopPanel { DataContext = vm };
        Layout(shop);
        var categories = (ListBox)shop.FindName("MarketCategories");
        foreach (var category in new[] { "战法", "装备", "卡牌", "技能" })
        {
            categories.SelectedItem = category;
            Layout(shop);
            Program.Assert(vm.ShanheMarketGoods.Any() && vm.ShanheMarketGoods.All(item => item.MarketCategory == category),
                "The visible category selector must filter actual stock.");
            if (category is "装备" or "卡牌") Program.Assert(vm.ShanheMarketGoods.All(item => item.HasCardImage), "Card goods must show their real card art.");
            Program.Render(shop, 1400, 714, Path.Combine(output, "shanhe-market-" + category + ".png"));
        }
        categories.SelectedItem = "卡牌";
        Layout(shop);
        var goods = vm.ShanheMarketGoods.First(item => item.CanBuy);
        var sameCategory = vm.ShanheMarketGoods.First(item => item.Id != goods.Id);
        var gold = session.Snapshot.Gold;
        var button = Program.Find<Button>(shop).Single(item => ReferenceEquals(item.CommandParameter, goods));
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
        Layout(shop);
        Program.Assert(session.Snapshot.Gold == gold - goods.Price && vm.ShanheMarketGoods.Single(item => item.Id == sameCategory.Id).Price == sameCategory.Price + 100,
            "The real card purchase button must charge the shown price and update the other visible card prices.");
        gold = session.Snapshot.Gold;
        vm.RefreshShanheMarketCommand.Execute(null);
        Program.Assert(session.Snapshot.Gold == gold - 100 && !vm.RefreshShanheMarketCommand.CanExecute(null),
            "Refresh must use the real command and update its availability.");

        ShanheCodexNode Node(int id) => vm.ShanheCodexNodes.Single(node => node.TreeId == id);
    }

    public static void FailedProfileWritePreservesProgress()
    {
        using var vm = Create(new FailingStore(), new MemoryShanheCampaignSaveStore());
        vm.SelectCodexNodeCommand.Execute(vm.ShanheCodexNodes.Single(node => node.TreeId == 16));
        vm.UnlockCodexNodeCommand.Execute(null);
        Program.Assert(vm.ShanheBookBalance == 5000 && !vm.SelectedCodexNode!.IsUnlocked && vm.ShanheCodexMessage.Contains("未保存"),
            "A failed profile write must neither consume books nor publish an unlock.");
    }
    private sealed class FailingStore : IShanheCodexStore
    {
        public bool Exists => true;
        public ShanheCodexProfile Read() => new() { Books = 5000 };
        public void Write(ShanheCodexProfile profile) => throw new IOException("fixture");
    }
    private static MainViewModel Create(IShanheCodexStore store, IShanheCampaignSaveStore campaign) =>
        new(false, 270017, true, new MemorySaveStore(), useExpandedContent: true, shanheSaveStore: campaign, shanheCodexStore: store);
    private static void Attach(MainViewModel vm, ShanheCampaignSession session)
    {
        typeof(MainViewModel).GetField("_shanheSession", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, session);
        typeof(MainViewModel).GetMethod("RefreshShanhePresentation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1050, 535)); root.Arrange(new Rect(0, 0, 1050, 535)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
}
