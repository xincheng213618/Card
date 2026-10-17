using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ShanheRewardUiChecks
{
    private const string Player = "fixture:shanhe-ui-player";
    private const string Enemy = "fixture:shanhe-ui-enemy";
    private const string Force = "fixture:shanhe-ui-victory";
    private const string Guard = "fixture:shanhe-ui-guard";
    private const string Learned = "fixture:shanhe-ui-learned";
    private static readonly Lazy<ContentRegistry> FixtureRegistry = new(() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardTeamModePackage(), new RewardBattlePackage()));

    public static void RealVictoryRewardCardsConfirmSkipSkillAndReplacement(string output)
    {
        using var vm = new MainViewModel(false, 270018, true, new MemorySaveStore(),
            contentRegistry: FixtureRegistry.Value, shanheSaveStore: new MemoryShanheCampaignSaveStore(),
            shanheCodexStore: new MemoryShanheCodexStore());
        var panel = new ShanheCampaignPanel { DataContext = vm };
        try
        {
            var session = WinRewardSession();
            Attach(vm, session);
            Layout(panel);
            var overlay = (FrameworkElement)panel.FindName("ShanheRewardOverlay");
            var rows = (ItemsControl)panel.FindName("ShanheRewardCardRows");
            var confirm = (Button)panel.FindName("ShanheConfirmRewardButton");
            var ordinaryRevision = Program.Engine(vm).Revision;
            Program.Assert(vm.IsShanheRewardOpen && overlay.Visibility == Visibility.Visible &&
                vm.ShanheRewardCards.Count == 4 && vm.ShanheRewardCards.Count(card => card.IsSkip) == 1 &&
                rows.Items.Count == 4 && !confirm.IsEnabled && !vm.ConfirmShanheRewardCommand.CanExecute(null),
                "A real completed victory must show three reward cards and a separate decline card, with confirmation initially disabled.");
            var gold = session.Snapshot.Gold;
            var beforeSelection = Snapshot(session);
            var revision = session.Revision;
            var skill = vm.ShanheRewardCards.Single(card => card.Id == "learned");
            var staleSkip = vm.ShanheRewardCards.Single(card => card.IsSkip);
            var staleSkipButton = Bound(panel, vm.SelectShanheRewardCardCommand, staleSkip);
            Click(Bound(panel, vm.SelectShanheRewardCardCommand, skill), panel);
            Program.Assert(skill.IsSelected && ReferenceEquals(vm.SelectedShanheRewardCard, skill) &&
                confirm.IsEnabled && Snapshot(session) == beforeSelection,
                "Clicking a skill card must only preview its selection; the real campaign journal and resources stay unchanged.");
            Click(staleSkipButton, panel);
            Program.Assert(staleSkip.IsSelected && !skill.IsSelected && vm.ShanheRewardCards.Count(card => card.IsSelected) == 1 &&
                staleSkip.BonusText.Contains("100", StringComparison.Ordinal) && Snapshot(session) == beforeSelection &&
                panel.HandleEscape() && vm.IsShanheRewardOpen && session.Revision == revision,
                "The decline card must show its 100-coin compensation, select exclusively, and retain the unresolved reward when Escape is pressed.");
            Program.Render(panel, 1440, 880, Path.Combine(output, "shanhe-reward-choice.png"));
            Click(confirm, panel);
            Program.Assert(!vm.IsShanheRewardOpen && overlay.Visibility == Visibility.Collapsed &&
                session.Revision == revision + 1 && session.Snapshot.Gold == gold + 100 &&
                session.Snapshot.ExtraSkillIds.Count == 0 && session.Snapshot.TacticIds.Count == 0 &&
                session.AcceptedCommands.OfType<SkipShanheRewardCommand>().Count() == 1 &&
                Program.Engine(vm).Revision == ordinaryRevision,
                "Only confirmation may grant exactly 100 extra coins, without learning a skill or touching the hidden ordinary game.");
            var afterSkip = Snapshot(session);
            RawClick(confirm);
            RawClick(staleSkipButton);
            vm.SelectShanheRewardCardCommand.Execute(staleSkip);
            vm.ConfirmShanheRewardCommand.Execute(null);
            Layout(panel);
            Program.Assert(Snapshot(session) == afterSkip && !vm.SelectShanheRewardCardCommand.CanExecute(staleSkip),
                "Repeated confirmation and retired card buttons cannot grant the decline payment again.");

            session = WinRewardSession();
            Attach(vm, session);
            Layout(panel);
            Program.Assert(session.Revision == revision && !vm.SelectShanheRewardCardCommand.CanExecute(staleSkip),
                "A previous run's same-id, same-revision card cannot select or claim the new run's reward.");
            var freshBefore = Snapshot(session);
            RawClick(staleSkipButton);
            vm.SelectShanheRewardCardCommand.Execute(staleSkip);
            Program.Assert(Snapshot(session) == freshBefore && vm.SelectedShanheRewardCard is null && !confirm.IsEnabled,
                "A stale bound card leaves the new offer unselected and uncommitted.");
            skill = vm.ShanheRewardCards.Single(card => card.Id == "learned");
            gold = session.Snapshot.Gold;
            Click(Bound(panel, vm.SelectShanheRewardCardCommand, skill), panel);
            Program.Assert(session.Snapshot.ExtraSkillIds.Count == 0 && session.Snapshot.Gold == gold,
                "A new skill is not granted by the selection click alone.");
            Click(confirm, panel);
            Program.Assert(session.Snapshot.ExtraSkillIds.SequenceEqual([Learned]) && session.Snapshot.Gold == gold &&
                !session.AcceptedCommands.OfType<SkipShanheRewardCommand>().Any() && !vm.IsShanheRewardOpen,
                "Confirming a skill must grant only that skill, without also receiving decline compensation.");

            session = WinRewardSession(fillSkillSlot: true);
            Attach(vm, session);
            Layout(panel);
            gold = session.Snapshot.Gold;
            revision = session.Revision;
            skill = vm.ShanheRewardCards.Single(card => card.Id == "learned");
            Click(Bound(panel, vm.SelectShanheRewardCardCommand, skill), panel);
            var beforeReplacement = Snapshot(session);
            Program.Assert(vm.ShanheRewardNeedsReplacement && confirm.IsEnabled && !vm.IsShanheSkillReplacementOpen &&
                vm.SelectedShanheRewardReplacements.Single().ReplaceSkillId == Guard,
                "A full-slot reward must enable its confirmation button so that the separate replacement dialog can be requested.");
            Click(confirm, panel);
            var replacementOverlay = (FrameworkElement)panel.FindName("ShanheSkillReplacementOverlay");
            var replacementConfirm = (Button)panel.FindName("ShanheConfirmSkillReplacementButton");
            var replacementCancel = (Button)panel.FindName("ShanheCancelSkillReplacementButton");
            var locked = vm.ShanheSkillReplacementOptions.Single(option => option.SkillId == Force);
            var replaceable = vm.ShanheSkillReplacementOptions.Single(option => option.SkillId == Guard);
            var lockedButton = Bound(panel, vm.SelectShanheSkillReplacementCommand, locked);
            var staleReplacementButton = Bound(panel, vm.SelectShanheSkillReplacementCommand, replaceable);
            Program.Assert(vm.IsShanheSkillReplacementOpen && replacementOverlay.Visibility == Visibility.Visible &&
                vm.ShanheSkillReplacementOptions.Count == 2 && locked.IsLocked && !locked.IsEnabled &&
                !lockedButton.IsEnabled && !vm.SelectShanheSkillReplacementCommand.CanExecute(locked) &&
                replaceable.IsEnabled && !replaceable.IsLocked && !replacementConfirm.IsEnabled &&
                !overlay.IsEnabled && !confirm.IsEnabled && Snapshot(session) == beforeReplacement,
                "The real replacement modal must lock printed skills, permit the acquired skill, disable the reward underlay, and leave the journal and resources unchanged.");
            RawClick(lockedButton);
            vm.SelectShanheSkillReplacementCommand.Execute(locked);
            RawClick(confirm);
            vm.ConfirmShanheSkillReplacementCommand.Execute(null);
            Layout(panel);
            Program.Assert(!replacementConfirm.IsEnabled && vm.ShanheSkillReplacementOptions.All(option => !option.IsSelected) &&
                Snapshot(session) == beforeReplacement,
                "Locked printed skills and covered reward confirmation cannot select a replacement or prematurely commit the reward.");
            Click(staleReplacementButton, panel);
            Program.Assert(replaceable.IsSelected && replacementConfirm.IsEnabled && Snapshot(session) == beforeReplacement,
                "Selecting an acquired skill in the actual modal must only preview the replacement until the modal confirmation.");
            Program.Render(panel, 1440, 880, Path.Combine(output, "shanhe-skill-replacement.png"));
            Click(replacementCancel, panel);
            Program.Assert(!vm.IsShanheSkillReplacementOpen && vm.IsShanheRewardOpen && confirm.IsEnabled &&
                Snapshot(session) == beforeReplacement && !vm.SelectShanheSkillReplacementCommand.CanExecute(replaceable),
                "Canceling the replacement must return to the selected reward without learning, removing a skill, or changing coins.");
            Click(confirm, panel);
            RawClick(staleReplacementButton);
            vm.SelectShanheSkillReplacementCommand.Execute(replaceable);
            vm.ConfirmShanheSkillReplacementCommand.Execute(null);
            Layout(panel);
            Program.Assert(vm.IsShanheSkillReplacementOpen && !replacementConfirm.IsEnabled &&
                vm.ShanheSkillReplacementOptions.All(option => !option.IsSelected) && Snapshot(session) == beforeReplacement &&
                panel.HandleEscape() && !vm.IsShanheSkillReplacementOpen && vm.IsShanheRewardOpen && Snapshot(session) == beforeReplacement,
                "A retired option from a previous modal cannot select the reopened dialog; Escape must cancel it and preserve the pending reward.");
            Click(confirm, panel);
            var freshReplacement = vm.ShanheSkillReplacementOptions.Single(option => option.SkillId == Guard);
            Click(Bound(panel, vm.SelectShanheSkillReplacementCommand, freshReplacement), panel);
            Click(replacementConfirm, panel);
            Program.Assert(session.Snapshot.ExtraSkillIds.SequenceEqual([Learned]) && session.Snapshot.ExtraSkillSlots == 1 &&
                session.Snapshot.Gold == gold && session.Revision == revision + 1 && !vm.IsShanheRewardOpen &&
                !vm.IsShanheSkillReplacementOpen && Program.Engine(vm).Revision == ordinaryRevision,
                "Only the modal confirmation may exchange exactly one acquired skill without changing printed skills, slot count, coins, or the ordinary table.");
            var afterReplacement = Snapshot(session);
            RawClick(replacementConfirm);
            vm.SelectShanheSkillReplacementCommand.Execute(freshReplacement);
            vm.ConfirmShanheSkillReplacementCommand.Execute(null);
            Layout(panel);
            Program.Assert(Snapshot(session) == afterReplacement, "Retired modal controls cannot repeat a confirmed skill replacement.");
        }
        finally { panel.DataContext = null; }
    }

    public static void ShopSkillsUseReplacementModalAndChargeOnlyOnConfirmation(string output)
    {
        using var vm = new MainViewModel(false, 270018, true, new MemorySaveStore(),
            contentRegistry: FixtureRegistry.Value, shanheSaveStore: new MemoryShanheCampaignSaveStore(),
            shanheCodexStore: new MemoryShanheCodexStore());
        var panel = new ShanheCampaignPanel { DataContext = vm };
        try
        {
            var session = CreateSkillShopSession(fillSkillSlot: true);
            Attach(vm, session);
            vm.SelectedShanheMarketCategory = "技能";
            Layout(panel);
            var ordinaryRevision = Program.Engine(vm).Revision;
            var item = vm.ShanheShopItems.Single(product => product.Id == "learn");
            var buy = Bound(panel, vm.RequestShanheShopPurchaseCommand, item);
            var beforePurchase = Snapshot(session);
            var gold = session.Snapshot.Gold;
            var revision = session.Revision;
            Program.Assert(vm.IsShanheShopActive && vm.ShanheShopItems.Count == 1 && vm.ShanheMarketGoods.Single() == item &&
                Program.Find<Button>(panel).Count(button => ReferenceEquals(button.CommandParameter, item)) == 1 &&
                item.CanBuy && item.Price == 25 && buy.IsEnabled,
                "A full-slot skill product must appear as one bound purchasable card rather than one card per replacement target.");
            Click(buy, panel);
            var replacementConfirm = (Button)panel.FindName("ShanheConfirmSkillReplacementButton");
            var replacementCancel = (Button)panel.FindName("ShanheCancelSkillReplacementButton");
            var locked = vm.ShanheSkillReplacementOptions.Single(option => option.SkillId == Force);
            var oldOption = vm.ShanheSkillReplacementOptions.Single(option => option.SkillId == Guard);
            var oldOptionButton = Bound(panel, vm.SelectShanheSkillReplacementCommand, oldOption);
            Program.Assert(vm.IsShanheSkillReplacementOpen && !vm.IsShanheRewardOpen && !buy.IsEnabled &&
                locked.IsLocked && !vm.SelectShanheSkillReplacementCommand.CanExecute(locked) &&
                !replacementConfirm.IsEnabled && Snapshot(session) == beforePurchase,
                "Requesting a full-slot shop skill must open the shared modal and preserve its price and resources until confirmation.");
            Click(oldOptionButton, panel);
            Click(replacementCancel, panel);
            Program.Assert(!vm.IsShanheSkillReplacementOpen && buy.IsEnabled && Snapshot(session) == beforePurchase,
                "Canceling a selected shop replacement must spend no coins, consume no stock, and remove no skill.");
            Click(buy, panel);
            RawClick(oldOptionButton);
            vm.SelectShanheSkillReplacementCommand.Execute(oldOption);
            vm.ConfirmShanheSkillReplacementCommand.Execute(null);
            Layout(panel);
            Program.Assert(vm.IsShanheSkillReplacementOpen && !replacementConfirm.IsEnabled &&
                vm.ShanheSkillReplacementOptions.All(option => !option.IsSelected) && Snapshot(session) == beforePurchase &&
                panel.HandleEscape() && !vm.IsShanheSkillReplacementOpen && Snapshot(session) == beforePurchase,
                "A canceled modal's option must not select a new shop dialog, and Escape must return without charging the original price.");

            var previousItem = item;
            session = CreateSkillShopSession(fillSkillSlot: true);
            Attach(vm, session);
            Layout(panel);
            Program.Assert(session.Revision == revision && !vm.RequestShanheShopPurchaseCommand.CanExecute(previousItem),
                "A previous expedition's same-id, same-revision product must not act on the new shop.");
            beforePurchase = Snapshot(session);
            RawClick(buy);
            vm.RequestShanheShopPurchaseCommand.Execute(previousItem);
            vm.SelectShanheSkillReplacementCommand.Execute(oldOption);
            vm.ConfirmShanheSkillReplacementCommand.Execute(null);
            Program.Assert(!vm.IsShanheSkillReplacementOpen && Snapshot(session) == beforePurchase,
                "Retired shop and replacement objects cannot spend money or open a modal in a replacement expedition.");
            item = vm.ShanheShopItems.Single(product => product.Id == "learn");
            buy = Bound(panel, vm.RequestShanheShopPurchaseCommand, item);
            Click(buy, panel);
            var option = vm.ShanheSkillReplacementOptions.Single(entry => entry.SkillId == Guard);
            Click(Bound(panel, vm.SelectShanheSkillReplacementCommand, option), panel);
            Program.Assert(Snapshot(session) == beforePurchase && replacementConfirm.IsEnabled,
                "The chosen shop replacement remains uncommitted until the modal's own confirmation.");
            Program.Render(panel, 1440, 880, Path.Combine(output, "shanhe-shop-skill-replacement.png"));
            Click(replacementConfirm, panel);
            Program.Assert(!vm.IsShanheSkillReplacementOpen && session.Revision == revision + 1 &&
                session.Snapshot.Gold == gold - 25 && session.Snapshot.ExtraSkillIds.SequenceEqual([Learned]) &&
                session.Snapshot.ExtraSkillSlots == 1 && session.Snapshot.Phase == ShanheCampaignPhase.NodeChoice &&
                vm.ShanheShopItems.Single(product => product.Id == "learn") is { State: ShanheShopItemState.Purchased, CanBuy: false } &&
                Program.Engine(vm).Revision == ordinaryRevision,
                "Confirming the shared shop modal must debit the unchanged price once, replace one acquired skill, and mark only that product purchased.");
            var afterPurchase = Snapshot(session);
            RawClick(buy);
            RawClick(replacementConfirm);
            vm.SelectShanheSkillReplacementCommand.Execute(option);
            vm.RequestShanheShopPurchaseCommand.Execute(item);
            vm.ConfirmShanheSkillReplacementCommand.Execute(null);
            Layout(panel);
            Program.Assert(Snapshot(session) == afterPurchase,
                "Repeated shop or modal confirmations must not charge twice or repeat a completed replacement.");

            session = CreateSkillShopSession(fillSkillSlot: false);
            Attach(vm, session);
            Layout(panel);
            item = vm.ShanheShopItems.Single(product => product.Id == "learn");
            gold = session.Snapshot.Gold;
            revision = session.Revision;
            Click(Bound(panel, vm.RequestShanheShopPurchaseCommand, item), panel);
            Program.Assert(!vm.IsShanheSkillReplacementOpen && session.Snapshot.Gold == gold - item.Price &&
                session.Snapshot.ExtraSkillIds.SequenceEqual([Learned]) && session.Revision == revision + 1,
                "With an unused skill slot, the same purchase command must learn the skill and charge its price directly without a replacement dialog.");
        }
        finally { panel.DataContext = null; }
    }

    private static ShanheCampaignSession CreateSkillShopSession(bool fillSkillSlot)
    {
        var definition = new ShanheCampaignDefinition("fixture:shanhe-ui-skill-shop", "渡口集市", [Player], ["prepare"],
        [
            new("prepare", "营前整备", ShanheNodeKind.Event, ["shop"],
                Choices: [new("ready", "整备出发", RewardId: fillSkillSlot ? "guard" : null)]),
            new("shop", "渡口集市", ShanheNodeKind.Shop, ["battle"],
                Choices: [new("learn", "传习·振奋", GoldCost: 25, RewardId: "learned", LeavesNode: false)]),
            new("battle", "渡口守军", ShanheNodeKind.Battle, [], [new("gate", "守军", [new(Enemy)])])
        ],
        [
            new("guard", "既有守御", SkillId: Guard) { Description = "已有额外技能，占用一个槽。" },
            new("learned", "传习·振奋", SkillId: Learned) { Description = "习得振奋，占用一个槽。" }
        ], StartingGold: 80, ExtraSkillSlots: 1);
        var session = ShanheCampaignSession.Create(definition, new(71, Player), _ => FixtureRegistry.Value);
        Accept(session, new MoveShanheNodeCommand("prepare", session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("ready", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand("shop", session.Revision));
        return session;
    }

    public static void BossBundlesRequireEachGroupThenAdvanceThreeChapters(string output)
    {
        using var vm = new MainViewModel(false, 270018, true, new MemorySaveStore(),
            contentRegistry: FixtureRegistry.Value, shanheSaveStore: new MemoryShanheCampaignSaveStore(),
            shanheCodexStore: new MemoryShanheCodexStore());
        var panel = new ShanheCampaignPanel { DataContext = vm };
        try
        {
            var session = CreateThreeBossSession();
            var ordinaryRevision = Program.Engine(vm).Revision;
            for (var chapter = 1; chapter <= 3; chapter++)
            {
                var beforeGold = session.Snapshot.Gold;
                var beforeSlots = session.Snapshot.ExtraSkillSlots;
                Accept(session, new MoveShanheNodeCommand("boss" + chapter, session.Revision));
                WinPendingBattle(session);
                Attach(vm, session);
                Layout(panel);
                var summary = (FrameworkElement)panel.FindName("ShanheBossRewardOverlay");
                var cardOverlay = (FrameworkElement)panel.FindName("ShanheRewardOverlay");
                var finish = (Button)panel.FindName("ShanheConfirmBossRewardButton");
                var confirmCard = (Button)panel.FindName("ShanheConfirmRewardButton");
                var rewardBack = (Button)panel.FindName("ShanheRewardBackButton");
                Program.Assert(vm.IsShanheBossRewardOpen && !vm.IsShanheRewardOpen &&
                    summary.Visibility == Visibility.Visible && cardOverlay.Visibility == Visibility.Collapsed &&
                    ReferenceEquals(finish.Command, vm.ConfirmShanheBossRewardCommand) && !finish.IsEnabled &&
                    session.Snapshot.Gold == beforeGold + 500 && session.Snapshot.ExtraSkillSlots == beforeSlots + 1 &&
                    vm.ShanheBossFixedGoldText.Contains("500", StringComparison.Ordinal) &&
                    vm.ShanheBossSkillSlotsText.Contains("1", StringComparison.Ordinal) &&
                    session.Snapshot.AvailableNodes.Count == 0 && vm.ShanheBossRewardGroups.Count == 2 &&
                    vm.ShanheChapterTitle == "第" + chapter + "章 · 渡口",
                    "Each real chapter boss victory must show the bound completion overview, credit 500 coins and one slot once, and keep the next chapter closed.");
                var initial = Snapshot(session);
                RawClick(finish);
                vm.ConfirmShanheBossRewardCommand.Execute(null);
                Layout(panel);
                Program.Assert(Snapshot(session) == initial && !vm.ConfirmShanheBossRewardCommand.CanExecute(null),
                    "Final continuation must reject direct and button attempts while either boss reward group is unresolved.");
                if (chapter == 1) Program.Render(panel, 1440, 880, Path.Combine(output, "shanhe-boss-reward-overview.png"));
                var staleGroup = vm.ShanheBossRewardGroups.Single(group => group.Id == "tactics");
                var staleGroupButton = Bound(panel, vm.OpenShanheBossRewardGroupCommand, staleGroup);
                Click(staleGroupButton, panel);
                Program.Assert(vm.HasShanheBossRewardGroup && vm.IsShanheRewardOpen && !vm.IsShanheBossRewardOpen &&
                    cardOverlay.Visibility == Visibility.Visible && summary.Visibility == Visibility.Collapsed &&
                    ReferenceEquals(rewardBack.Command, vm.CloseShanheBossRewardGroupCommand) && rewardBack.IsEnabled &&
                    vm.ShanheRewardCards.Count == 3 && vm.ShanheRewardCards.All(card => !card.IsSkip) &&
                    Snapshot(session) == initial,
                    "Opening a boss reward group must show its three actual candidates and a return button without committing or offering normal decline coins.");
                Click(rewardBack, panel);
                Program.Assert(vm.IsShanheBossRewardOpen && Snapshot(session) == initial &&
                    vm.ShanheBossRewardGroups.All(group => !group.IsResolved),
                    "Returning from an unselected group must preserve both frozen boss groups without granting anything.");

                foreach (var groupId in new[] { "tactics", "cards" })
                {
                    var group = vm.ShanheBossRewardGroups.Single(item => item.Id == groupId);
                    Click(Bound(panel, vm.OpenShanheBossRewardGroupCommand, group), panel);
                    var card = vm.ShanheRewardCards.First(item => item.IsEnabled);
                    var beforeSelection = Snapshot(session);
                    var revision = session.Revision;
                    Click(Bound(panel, vm.SelectShanheRewardCardCommand, card), panel);
                    Program.Assert(card.IsSelected && confirmCard.IsEnabled && Snapshot(session) == beforeSelection,
                        "Selecting a boss candidate must only update the card highlight and confirmation binding.");
                    Click(confirmCard, panel);
                    var resolved = vm.ShanheBossRewardGroups.Single(item => item.Id == groupId);
                    var boundSummary = Bound(panel, vm.OpenShanheBossRewardGroupCommand, resolved);
                    Program.Assert(vm.IsShanheBossRewardOpen && !vm.IsShanheRewardOpen && resolved.IsResolved &&
                        resolved.SelectedName == card.Name && !boundSummary.IsEnabled &&
                        Program.Find<TextBlock>(boundSummary).Any(text => text.Text == card.Name) &&
                        session.Revision == revision + 1 && session.Snapshot.Phase == ShanheCampaignPhase.Reward &&
                        session.Snapshot.AvailableNodes.Count == 0 && session.Snapshot.Gold == beforeGold + 500 &&
                        session.Snapshot.ExtraSkillSlots == beforeSlots + 1,
                        "Each group confirmation must grant exactly that reward and show its selected name on the actual overview, without advancing the chapter or repeating fixed credits.");
                    var claimed = Snapshot(session);
                    RawClick(confirmCard);
                    RawClick(staleGroupButton);
                    vm.SelectShanheRewardCardCommand.Execute(card);
                    vm.OpenShanheBossRewardGroupCommand.Execute(staleGroup);
                    Layout(panel);
                    Program.Assert(Snapshot(session) == claimed && vm.IsShanheBossRewardOpen,
                        "Retired group and reward buttons cannot re-open or claim an already resolved group.");
                    Program.Assert(finish.IsEnabled == (groupId == "cards"),
                        "Final continuation becomes enabled only when both separate reward choices have been claimed.");
                }
                if (chapter == 1) Program.Render(panel, 1440, 880, Path.Combine(output, "shanhe-boss-reward-claimed.png"));
                var completedRevision = session.Revision;
                Click(finish, panel);
                Program.Assert(session.Revision == completedRevision + 1 && !vm.IsShanheBossRewardOpen &&
                    session.Snapshot.BossRewardSummary is null && session.Snapshot.CompletedNodeIds.Count == chapter &&
                    session.Snapshot.TacticIds.Count == chapter && session.Snapshot.StartingCards.Count == chapter &&
                    Program.Engine(vm).Revision == ordinaryRevision,
                    "The final overview confirmation must complete exactly one chapter after its tactic and card reward have been received, leaving the ordinary table untouched.");
                if (chapter < 3)
                    Program.Assert(session.Snapshot.Phase == ShanheCampaignPhase.Map &&
                        session.Snapshot.AvailableNodes.Single().Id == "boss" + (chapter + 1) &&
                        vm.ShanheMapNodes.Select(node => node.Node.Id).SequenceEqual(["boss" + (chapter + 1)]) &&
                        vm.ShanheChapterTitle == "第" + (chapter + 1) + "章 · 渡口",
                        "Only the completed overview may reveal the next numbered chapter, even when chapter nodes have adjacent graph depths.");
                else Program.Assert(session.Snapshot.Phase == ShanheCampaignPhase.Won && vm.IsShanhePreparation &&
                    !vm.IsShanheRunInProgress && vm.CanPrepareShanheExpedition,
                    "The third boss's completed reward overview must finish the expedition and restore preparation.");
                var finished = Snapshot(session);
                RawClick(finish);
                vm.ConfirmShanheBossRewardCommand.Execute(null);
                Layout(panel);
                Program.Assert(Snapshot(session) == finished, "Repeated chapter continuation cannot credit or advance a second time.");
            }
        }
        finally { panel.DataContext = null; }
    }

    private static ShanheCampaignSession CreateThreeBossSession()
    {
        var rewards = new List<ShanheRewardDefinition>();
        var nodes = new List<ShanheNodeDefinition>();
        for (var chapter = 1; chapter <= 3; chapter++)
        {
            var tacticIds = new List<string>();
            var cardIds = new List<string>();
            for (var choice = 1; choice <= 3; choice++)
            {
                var ordinal = (chapter - 1) * 3 + choice;
                var tacticId = "tactic" + ordinal;
                var cardId = "card" + ordinal;
                tacticIds.Add(tacticId); cardIds.Add(cardId);
                rewards.Add(new(tacticId, "战法·军阵" + ordinal)
                    { TacticSkillId = "fixture:shanhe-ui-tactic" + ordinal, Description = "本次征程每战攻击范围加一。" });
                rewards.Add(new(cardId, "行军手札" + ordinal, StartingCards: [new(CardKind.Peach, Suit.Heart, choice)])
                    { Description = "后续每场开局获得一张桃。" });
            }
            nodes.Add(new("boss" + chapter, "渡口首领" + chapter, ShanheNodeKind.BossBattle,
                chapter == 3 ? [] : ["boss" + (chapter + 1)],
                [new("encounter" + chapter, "守军", [new(Enemy)], VictoryGold: 7)])
            {
                ChapterNumber = chapter, ChapterTitle = "第" + chapter + "章 · 渡口",
                BossRewards = new(500, 1, [new("tactics", "战法", tacticIds), new("cards", "卡牌", cardIds)]),
                StoryText = "守军已退，整理本章收获。"
            });
        }
        var definition = new ShanheCampaignDefinition("fixture:shanhe-ui-boss-bundles", "三章渡口", [Player], ["boss1"],
            nodes, rewards, StartingGold: 10, ExtraSkillSlots: 1);
        return ShanheCampaignSession.Create(definition, new(71, Player), _ => FixtureRegistry.Value);
    }

    private static ShanheCampaignSession WinRewardSession(bool fillSkillSlot = false)
    {
        var definition = new ShanheCampaignDefinition("fixture:shanhe-ui-rewards", "渡口归来", [Player], ["prepare"],
        [
            new("prepare", "营前整备", ShanheNodeKind.Event, ["battle"],
                Choices: [new("ready", "整备出发", RewardId: fillSkillSlot ? "guard" : null)]),
            new("battle", "渡口守军", ShanheNodeKind.Battle, ["home"],
                [new("gate", "守军", [new(Enemy)], VictoryGold: 7)], ["learned", "draw", "medicine"])
                { RewardChoiceCount = 3, RewardSkipGold = 100, StoryText = "守军已退，整理此战收获。" },
            new("home", "河畔营火", ShanheNodeKind.Camp, [], Choices: [new("finish", "继续前行")])
        ],
        [
            new("guard", "既有守御", SkillId: Guard) { Description = "已有的额外技能，占用一个技能槽。" },
            new("learned", "传习·振奋", SkillId: Learned) { Description = "获得振奋技能，攻击范围加一；占用一个额外技能槽。" },
            new("draw", "粮道", Bonuses: new(Draw: 1)) { Description = "此后每场战斗的摸牌阶段额外摸一张牌。" },
            new("medicine", "随身药囊", StartingCards: [new(CardKind.Peach, Suit.Heart, 3)])
                { Description = "此后每场战斗开局额外获得一张桃。" }
        ], StartingGold: 10, ExtraSkillSlots: 1);
        var session = ShanheCampaignSession.Create(definition, new(71, Player), _ => FixtureRegistry.Value);
        Accept(session, new MoveShanheNodeCommand("prepare", session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("ready", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand("battle", session.Revision));
        WinPendingBattle(session);
        Program.Assert(session.Snapshot.Phase == ShanheCampaignPhase.Reward && session.Snapshot.RewardOffers.Count == 3 &&
            session.Snapshot.Gold == 17,
            "Reward UI setup must come from an actual fully resolved native victory and its validated battle journal.");
        return session;
    }

    private static void WinPendingBattle(ShanheCampaignSession session)
    {
        var setup = session.PendingBattleSetup!;
        var battle = GameEngine.CreateStandard(new()
        {
            Seed = setup.BattleSeed, PlayerCount = 2, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
            ShanheBattle = setup, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false,
            AiPolicyVersion = AiDecisionPolicy.CurrentVersion, MaxTurns = 8
        }, FixtureRegistry.Value);
        Accept(battle, new StartGameCommand());
        for (var step = 0; step < 40 && battle.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            Accept(battle, new AdvanceOneStepCommand(battle.Revision));
        Program.Assert(battle.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The small native fixture must reach its real human play prompt without a seed search.");
        Accept(battle, new UseProgramSkillCommand(0, Force, "victory", [], [1], battle.Revision, battle.PendingDecision!.PromptId));
        for (var step = 0; step < 40 && battle.State.Status != EngineStatus.Completed; step++)
        {
            // Later chapters carry the chosen Peach into the next real battle. Decline the
            // published rescue of the defeated enemy instead of waiting at a human prompt.
            if (battle.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 } rescue)
                Accept(battle, new AnswerPromptCommand(rescue.PlayerSeat, rescue.PromptId,
                    rescue.Choices.Single(choice => choice.Parameters.GetValueOrDefault("response") == "let-die").Id,
                    battle.Revision));
            else Accept(battle, new AdvanceOneStepCommand(battle.Revision));
        }
        Program.Assert(battle.State.Status == EngineStatus.Completed,
            $"The native UI battle fixture at {setup.NodeId} must finish; actual status {battle.State.Status}.");
        var completion = session.CompleteBattle(battle, session.Revision);
        Program.Assert(completion.Accepted,
            $"Reward UI native victory at {setup.NodeId} failed validation: {completion.Error}");
    }

    private static void Attach(MainViewModel vm, ShanheCampaignSession session)
    {
        typeof(MainViewModel).GetField("_shanheSession", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, session);
        vm.OpenShanheCommand.Execute(null);
    }
    private static Button Bound(FrameworkElement root, ICommand command, object parameter) =>
        Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, parameter));
    private static string Snapshot(ShanheCampaignSession session) => JsonSerializer.Serialize(session.Snapshot);
    private static void Click(Button button, FrameworkElement root)
    {
        Program.Assert(button.IsEnabled && button.Command?.CanExecute(button.CommandParameter) == true,
            "The actual reward button must be enabled and correctly bound before its click.");
        RawClick(button);
        Layout(root);
    }
    private static void RawClick(Button button) =>
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1440, 880)); root.Arrange(new Rect(0, 0, 1440, 880)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
    private static void Accept(ShanheCampaignSession session, ShanheCampaignCommand command)
    {
        var result = session.Submit(command);
        Program.Assert(result.Accepted, result.Error ?? "Campaign fixture command rejected.");
    }
    private static void Accept(GameEngine battle, GameCommand command)
    {
        var result = battle.Submit(command);
        Program.Assert(result.Accepted, result.Error?.ToString() ?? "Native battle fixture command rejected.");
    }

    private sealed class RewardBattlePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-reward-ui", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var programs = new object[]
            {
                new { id = Force, revision = 1, activations = new[]
                {
                    new { id = "victory", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1,
                        targetKind = "otherLiving", usesPerTurn = (int?)null,
                        effects = new[] { new { op = "loseHp", target = "selectedTarget", amount = 20 } } }
                } },
                new { id = Guard, revision = 1, modifiers = new[] { new { id = "guard", query = "attackRange", operation = "add", value = 1, priority = 0 } } },
                new { id = Learned, revision = 1, modifiers = new[] { new { id = "learned", query = "attackRange", operation = "add", value = 1, priority = 0 } } }
            }.Concat(Enumerable.Range(1, 9).Select(ordinal => (object)new
            {
                id = "fixture:shanhe-ui-tactic" + ordinal, revision = 1,
                modifiers = new[] { new { id = "range", query = "attackRange", operation = "add", value = 1, priority = 0 } }
            })).ToArray();
            var catalog = SkillProgramCatalog.Load(
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = programs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Force, Guard, Learned }.Concat(Enumerable.Range(1, 9).Select(ordinal => "fixture:shanhe-ui-tactic" + ordinal))
                        .ToDictionary(id => id, id => new { name = id, description = "Small native reward interaction fixture" }) }));
            foreach (var entry in catalog.Programs)
                builder.AddSkill(new(entry.Key, entry.Key == Learned ? "振奋" : entry.Key == Guard ? "守御" : "破阵", "攻击范围加一。") { Program = entry.Value });
            builder.AddGeneral(new(Player, "义军先锋", "", Force, "shu"));
            builder.AddGeneral(new(Enemy, "渡口守军", "", Guard, "qun"));
            builder.AddDeck(new("fixture:shanhe-ui-dodge", "闪牌堆", 4, 2, [new("standard:dodge", 40)]));
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "山河奖励交互", 2, 2, new Dictionary<string, int>(),
                "fixture:shanhe-ui-dodge", GeneralPoolIds: [Player, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }
}
