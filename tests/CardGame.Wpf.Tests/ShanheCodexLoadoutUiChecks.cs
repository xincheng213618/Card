using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ShanheCodexLoadoutUiChecks
{
    public static void PlanControlsPersistIndependentCopiesAndFrozenExpedition(string output)
    {
        var account = new TrackingStore(LegacyProfile());
        var campaign = new MemoryShanheCampaignSaveStore();
        string firstId, copyId, frozenRun;
        string[] frozenTactics;
        using (var vm = Create(account, campaign))
        {
            var panel = Book(vm);
            firstId = vm.ShanheCodexLoadoutPlans.Single().Id;
            Program.Assert(account.Writes == 0 && account.Read().LoadoutPlans is null &&
                vm.ShanheCodexSlots.Select(slot => slot.RewardId).SequenceEqual(LegacyProfile().EquippedTacticIds),
                "Reading a legacy six-slot account exposes its first plan in memory without an implicit migration write.");
            Click(panel, "CodexManageLoadoutsButton");
            Program.Assert(vm.IsShanheCodexLoadoutManagerOpen &&
                ((Border)panel.FindName("CodexLoadoutManagerOverlay")).Visibility == Visibility.Visible &&
                !((Canvas)panel.FindName("CodexBookContent")).IsEnabled,
                "The actual manage button opens a modal plan manager and disables the covered book controls.");
            Rename(panel, "  日常推进  ");
            Program.Assert(vm.ShanheCodexSelectedLoadoutName == "日常推进" && account.Read().LoadoutPlans!.Single().Name == "日常推进",
                "The bound name editor saves the normalized name of the real active plan.");
            Click(panel, "CodexCopyLoadoutButton");
            copyId = vm.ShanheCodexLoadoutPlans.Single(plan => plan.IsSelected).Id;
            Program.Assert(copyId != firstId && vm.ShanheCodexLoadoutPlans.Count == 2 &&
                account.Read().LoadoutPlans!.All(plan => plan.EquippedTacticIds.SequenceEqual(LegacyProfile().EquippedTacticIds)),
                "Copy activates a distinct saved plan containing the current six slots.");
            Rename(panel, "进攻预备");
            Click(panel, "CodexLoadoutManagerCloseButton");
            Equip(panel, vm, 1, 20);
            Program.Assert(vm.ShanheCodexSlots[1].RewardId == ShanheCodexTree.RuntimeRewardId(20) &&
                account.Read().LoadoutPlans!.Single(plan => plan.Id == firstId).EquippedTacticIds[1] is null,
                "Equipping through the real picker changes the copy without mutating the original plan's slot array.");

            Click(panel, "CodexManageLoadoutsButton");
            Select(panel, vm, firstId);
            Program.Assert(vm.ShanheCodexSlots[1].RewardId is null, "Switching to the original restores its independent six slots.");
            Select(panel, vm, copyId);
            Program.Assert(vm.ShanheCodexSlots[1].RewardId == ShanheCodexTree.RuntimeRewardId(20), "Switching back restores the copy's saved edit.");
            Click(panel, "CodexNewLoadoutButton");
            var emptyId = vm.ShanheCodexLoadoutPlans.Single(plan => plan.IsSelected).Id;
            Program.Assert(vm.ShanheCodexLoadoutPlans.Count == 3 && vm.ShanheCodexSlots.All(slot => slot.RewardId is null),
                "New creates and activates an empty six-slot plan rather than cloning the previously selected one.");
            Click(panel, "CodexDeleteLoadoutButton");
            Program.Assert(vm.ShanheCodexLoadoutPlans.Count == 2 && vm.ShanheCodexLoadoutPlans.All(plan => plan.Id != emptyId) &&
                vm.ShanheCodexLoadoutPlans.Single(plan => plan.IsSelected).Id == firstId,
                "Deleting the active empty plan selects the first remaining plan and preserves its saved equipment.");
            Click(panel, "CodexLoadoutManagerCloseButton");
            ClickSlot(panel, vm, 0);
            ClickCommand(panel, vm.CloseShanheCodexPickerCommand);
            ClickCommand(panel, vm.RemoveCodexTacticCommand);
            Program.Assert(vm.ShanheCodexSlots.All(slot => slot.RewardId is null) &&
                account.Read().LoadoutPlans!.Single(plan => plan.Id == copyId).EquippedTacticIds.OfType<string>().Count() == 2,
                "The actual remove button updates only the active plan and leaves its earlier copy intact.");
            Click(panel, "CodexManageLoadoutsButton");
            Select(panel, vm, copyId);
            Program.Render(panel, 1400, 714, Path.Combine(output, "shanhe-loadout-manager.png"));
            Click(panel, "CodexLoadoutManagerCloseButton");

            var saved = account.Read();
            frozenTactics = saved.EquippedTacticIds.OfType<string>().ToArray();
            var accountBeforeRun = Json(saved);
            var writes = account.Writes;
            vm.StartShanheCommand.Execute(null);
            frozenRun = ShanheCampaignJson.Serialize(campaign.Read().Campaign);
            Program.Assert(vm.IsShanheRunInProgress && campaign.Read().Campaign.Options.EquippedTacticIds.SequenceEqual(frozenTactics),
                "A real expedition freezes precisely the selected plan's equipped tactics.");
            AssertFrozenEditors(vm, vm.ShanheCodexLoadoutPlans.Single(plan => plan.Id == firstId));
            Program.Assert(account.Writes == writes && Json(account.Read()) == accountBeforeRun &&
                ShanheCampaignJson.Serialize(campaign.Read().Campaign) == frozenRun,
                "Disabled and directly invoked plan edits cannot rewrite the account, its books, or the active expedition.");
            panel.DataContext = null;
        }

        using (var preparation = Create(account, new MemoryShanheCampaignSaveStore()))
        {
            var panel = Book(preparation);
            Program.Assert(preparation.ShanheCodexSelectedLoadoutName == "进攻预备" &&
                preparation.ShanheCodexSlots.Select(slot => slot.RewardId).SequenceEqual(account.Read().EquippedTacticIds),
                "A fresh VM restores the selected renamed plan and all six saved slot positions.");
            Click(panel, "CodexManageLoadoutsButton");
            Select(panel, preparation, firstId);
            Click(panel, "CodexLoadoutManagerCloseButton");
            Program.Assert(preparation.ShanheCodexSlots.All(slot => slot.RewardId is null) &&
                ShanheCampaignJson.Serialize(campaign.Read().Campaign) == frozenRun,
                "Preparing a different account plan later leaves an already saved expedition untouched.");
            panel.DataContext = null;
        }

        using (var cold = Create(account, campaign))
        {
            var panel = Book(cold);
            Program.Assert(!((Button)panel.FindName("CodexManageLoadoutsButton")).IsEnabled &&
                !cold.OpenShanheCodexLoadoutManagerCommand.CanExecute(null) &&
                cold.ShanheCodexSlots.All(slot => slot.RewardId is null) &&
                cold.ShanheCodexLoadoutPlans.Single(plan => plan.IsSelected).Id == firstId,
                "An unexamined saved expedition locks preparation while displaying the later account plan without changing the saved run.");
            var accountBeforeContinue = Json(account.Read());
            var writes = account.Writes;
            cold.ContinueShanheCommand.Execute(null);
            var restored = (ShanheCampaignSession)typeof(MainViewModel).GetField("_shanheSession", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cold)!;
            Program.Assert(cold.IsShanheRunInProgress && restored.CreateCheckpoint().Options.EquippedTacticIds.SequenceEqual(frozenTactics) &&
                ShanheCampaignJson.Serialize(restored.CreateCheckpoint()) == frozenRun &&
                cold.ShanheTacticCards.Select(card => card.Id).SequenceEqual(frozenTactics) &&
                cold.ShanheCodexSlots.All(slot => slot.RewardId is null) &&
                Json(account.Read()) == accountBeforeContinue && account.Writes == writes,
                "Cold continuation restores the expedition's original tactic recipe instead of substituting the account's newly selected empty plan.");
            panel.DataContext = null;
        }
    }

    public static void FailedReadsAndWritesKeepTheCurrentPlanAndBooks()
    {
        var legacy = LegacyProfile();
        var account = new TrackingStore(legacy with
        {
            FormatVersion = FileShanheCodexStore.CurrentFormatVersion,
            LoadoutPlans = [new("first", "原方案", legacy.EquippedTacticIds), new("spare", "备用", new string?[6])],
            SelectedLoadoutPlanId = "first"
        }) { RejectWrites = true };
        var before = Json(account.Read());
        using (var vm = Create(account, new MemoryShanheCampaignSaveStore()))
        {
            var panel = Book(vm);
            Click(panel, "CodexManageLoadoutsButton");
            Select(panel, vm, "spare");
            AssertUnchanged();
            Rename(panel, "失败的改名"); AssertUnchanged();
            foreach (var button in new[] { "CodexNewLoadoutButton", "CodexCopyLoadoutButton", "CodexDeleteLoadoutButton" })
            { Click(panel, button); AssertUnchanged(); }
            Click(panel, "CodexLoadoutManagerCloseButton");
            Equip(panel, vm, 1, 20);
            AssertUnchanged();
            Program.Assert(vm.IsShanheCodexPickerOpen && vm.SelectedCodexNode is { IsEquipped: false } && account.RejectedWrites >= 6,
                "A failed equip keeps the actual picker open and publishes no equipped state after the rejected profile write.");
            panel.DataContext = null;

            void AssertUnchanged() => Program.Assert(Json(account.Read()) == before && account.Writes == 0 &&
                vm.ShanheCodexLoadoutPlans.Single(plan => plan.IsSelected).Id == "first" && vm.ShanheCodexSelectedLoadoutName == "原方案" &&
                vm.ShanheCodexSlots.Select(slot => slot.RewardId).SequenceEqual(legacy.EquippedTacticIds) && vm.ShanheBookBalance == legacy.Books &&
                vm.ShanheCodexMessage.Contains("未保存", StringComparison.Ordinal),
                "Rejected switching, naming, creation, copying, deletion and equip writes preserve the active plan, its six slots and every book.");
        }

        var unreadable = new ReadFailureStore();
        using var failed = Create(unreadable, new MemoryShanheCampaignSaveStore());
        var parameter = failed.ShanheCodexLoadoutPlans.FirstOrDefault();
        failed.ShanheCodexLoadoutName = "不能覆盖旧文件";
        foreach (var command in EditingCommands(failed))
        {
            Program.Assert(!command.CanExecute(parameter), "An unreadable account must disable every plan mutation.");
            command.Execute(parameter);
        }
        Program.Assert(unreadable.Writes == 0 && failed.ShanheCodexMessage.Contains("读取失败", StringComparison.Ordinal),
            "A failed account read keeps the source untouched even if disabled plan commands are invoked directly.");
    }

    private static ShanheCodexProfile LegacyProfile() => new()
    {
        Books = 5000,
        UnlockedRewardIds = new[] { 16, 20 }.SelectMany(id => ShanheCodexTree.RequiredPath(id, []).Select(node => ShanheCodexTree.Key(node.Id)))
            .Concat(new[] { ShanheCodexTree.RuntimeRewardId(16)!, ShanheCodexTree.RuntimeRewardId(20)! }).Distinct(StringComparer.Ordinal).ToArray(),
        EquippedTacticIds = [ShanheCodexTree.RuntimeRewardId(16), null, null, null, null, null]
    };
    private static MainViewModel Create(IShanheCodexStore store, IShanheCampaignSaveStore campaign)
    {
        var vm = new MainViewModel(false, 270017, true, new MemorySaveStore(), useExpandedContent: true,
            shanheSaveStore: campaign, shanheCodexStore: store);
        vm.SelectedShanheScenario = vm.ShanheScenarios.Single(item => item.Id == StandardShanheCampaignCatalog.Returning2026EditionId);
        return vm;
    }
    private static ShanheCodexPanel Book(MainViewModel vm) { var panel = new ShanheCodexPanel { DataContext = vm }; Layout(panel); return panel; }
    private static void Rename(ShanheCodexPanel panel, string name)
    {
        var editor = (TextBox)panel.FindName("CodexLoadoutNameBox");
        editor.SetCurrentValue(TextBox.TextProperty, name); editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Layout(panel); Click(panel, "CodexRenameLoadoutButton");
    }
    private static void Select(ShanheCodexPanel panel, MainViewModel vm, string id) => Click(panel,
        Program.Find<Button>(panel).Single(button => ReferenceEquals(button.Command, vm.SelectShanheCodexLoadoutCommand) &&
            button.CommandParameter is ShanheCodexLoadoutOption plan && plan.Id == id));
    private static void ClickSlot(ShanheCodexPanel panel, MainViewModel vm, int slot) => Click(panel,
        Program.Find<Button>(panel).Single(button => ReferenceEquals(button.Command, vm.SelectCodexSlotCommand) && button.CommandParameter is ShanheCodexSlot { Index: var index } && index == slot));
    private static void Equip(ShanheCodexPanel panel, MainViewModel vm, int slot, int treeId)
    {
        ClickSlot(panel, vm, slot);
        var picker = (Border)panel.FindName("CodexPickerOverlay");
        Click(panel, Program.Find<Button>(picker).Single(button => button.CommandParameter is ShanheCodexNode node && node.TreeId == treeId));
        Click(panel, "PickerEquipButton");
    }
    private static void AssertFrozenEditors(MainViewModel vm, ShanheCodexLoadoutOption other)
    {
        vm.ShanheCodexLoadoutName = "征途中不可保存";
        foreach (var command in EditingCommands(vm))
        {
            Program.Assert(!command.CanExecute(other), "An active expedition must disable every plan mutation.");
            command.Execute(other);
        }
    }
    private static ICommand[] EditingCommands(MainViewModel vm) => [vm.SelectShanheCodexLoadoutCommand, vm.NewShanheCodexLoadoutCommand,
        vm.CopyShanheCodexLoadoutCommand, vm.RenameShanheCodexLoadoutCommand, vm.DeleteShanheCodexLoadoutCommand];
    private static void ClickCommand(ShanheCodexPanel panel, ICommand command) => Click(panel, Program.Find<Button>(panel).Single(button => ReferenceEquals(button.Command, command)));
    private static void Click(ShanheCodexPanel panel, string name) => Click(panel, (Button)panel.FindName(name));
    private static void Click(ShanheCodexPanel panel, Button button)
    {
        Program.Assert(button.IsEnabled && button.Command?.CanExecute(button.CommandParameter) == true, "The actual loadout control must be enabled: " + button.Name);
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null); Layout(panel);
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1050, 535)); root.Arrange(new Rect(0, 0, 1050, 535)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
    private static string Json(ShanheCodexProfile profile) => JsonSerializer.Serialize(profile);
    private sealed class TrackingStore : IShanheCodexStore
    {
        private readonly MemoryShanheCodexStore _inner = new();
        public TrackingStore(ShanheCodexProfile initial) => _inner.Write(initial);
        public bool Exists => true;
        public int Writes { get; private set; }
        public int RejectedWrites { get; private set; }
        public bool RejectWrites { get; set; }
        public ShanheCodexProfile Read() => _inner.Read();
        public void Write(ShanheCodexProfile profile)
        {
            if (RejectWrites)
            { new MemoryShanheCodexStore().Write(profile); RejectedWrites++; throw new IOException("fixture: validated loadout write failed"); }
            _inner.Write(profile); Writes++;
        }
    }
    private sealed class ReadFailureStore : IShanheCodexStore
    {
        public bool Exists => true;
        public int Writes { get; private set; }
        public ShanheCodexProfile Read() => throw new InvalidDataException("fixture: unreadable original account");
        public void Write(ShanheCodexProfile profile) => Writes++;
    }
}
