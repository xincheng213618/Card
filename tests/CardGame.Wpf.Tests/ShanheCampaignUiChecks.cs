using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ShanheCampaignUiChecks
{
    public static void StoryMapChoicesAndIndependentColdRestore(string output)
    {
        var fixture = Path.Combine(Path.GetFullPath(output), "shanhe-save-" + Guid.NewGuid().ToString("N"));
        var campaign = new FileShanheCampaignSaveStore(fixture);
        var ordinary = new MemorySaveStore();
        try
        {
            using var vm = Create(ordinary, campaign);
            vm.SaveGameCommand.Execute(null);
            var ordinarySave = GameCheckpointJson.Serialize(ordinary.Read(GameSaveSlot.Manual).Checkpoint);
            var ordinarySaveStatus = vm.SaveStatus;
            var window = new MainWindow(vm);
            window.ApplyTemplate();
            var root = (FrameworkElement)window.Content;
            try
            {
                vm.OpenShanheCommand.Execute(null);
                Layout(root);
                var panel = Program.Find<ShanheCampaignPanel>(root).Single();
                Program.Assert(vm.IsShanheOpen && vm.ShanheEditionNotice.Contains("本地起步版") &&
                    vm.ShanheContextDescription.Contains("诸侯") && vm.ShanheGenerals.Count > 1 &&
                    vm.ShanheDifficulties.Count == 3,
                    "The distinct campaign entry must explain its local edition, story, character choice and three local difficulties.");
                Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-startup.png"));
                ClickNavigation((Button)panel.FindName("ShanhePrepareButton"), root);
                Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-general-selection.png"));
                Click((Button)panel.FindName("ShanheStartButton"), root);
                var start = vm.ShanheMapRows.SelectMany(row => row.Nodes).Single(node => node.IsAvailable);
                Program.Assert(campaign.Exists && start.KindLabel == "奇遇" && vm.ShanheContextDescription.Length > 0,
                    "A real new expedition must save its randomly selected story entry before any encounter starts.");
                var ordinaryGame = Program.Engine(vm);
                var ordinaryRevision = ordinaryGame.Revision;
                var focusSurface = (FrameworkElement)typeof(MainWindow)
                    .GetMethod("CurrentFocusSurface", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [vm])!;
                Program.Assert(focusSurface.Name == "ShanheOverlay" && FocusManager.GetIsFocusScope(panel) &&
                    KeyboardNavigation.GetTabNavigation(panel) == KeyboardNavigationMode.Cycle &&
                    !TableShortcutChecks.Input(window, Key.Enter) &&
                    TableShortcutChecks.Input(window, Key.Enter, ModifierKeys.Control) &&
                    ordinaryGame.Revision == ordinaryRevision && campaign.Read().Campaign.Commands.Count == 0,
                    "The map must own modal focus and its Enter routes must leave the hidden table and campaign journal untouched.");
                Program.Assert(TableShortcutChecks.Input(window, Key.Escape) && !vm.IsShanheOpen &&
                    ordinaryGame.Revision == ordinaryRevision,
                    "Escape must close the campaign surface without answering the hidden ordinary prompt.");
                vm.OpenShanheCommand.Execute(null);
                Layout(root);
                // Use the currently published node after the panel has refreshed its immutable UI choices.
                start = vm.ShanheMapRows.SelectMany(row => row.Nodes).Single(node => node.IsAvailable);
                Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-campaign-map.png"));
                Click(Bound(panel, vm.PreviewShanheNodeCommand, start), root);
                Program.Assert(vm.IsShanheOpen && vm.ShanheChoices.Count >= 2 &&
                    vm.ShanheContextDescription.Length > 15 && !vm.IsShanheBattleSession,
                    "The real opening story node must offer meaningful choices without replacing the ordinary battle.");
                var choice = vm.ShanheChoices.First(option => option.IsEnabled);
                Program.Render(root, 1440, 880, Path.Combine(output, "178-shanhe-story-choice.png"));
                Click(Bound(panel, vm.ChooseShanheOptionCommand, choice), root);
                Program.Assert(vm.ShanheChoices.Count == 0 && vm.ShanheRouteSummary.Contains(start.Name) &&
                    campaign.Read().Campaign.Commands.Count == 2 && vm.SaveStatus == ordinarySaveStatus && !vm.HasSaveError,
                    "One actual story choice must close the event, retain the visited route and save exactly two campaign actions.");
                var expectedVital = vm.ShanheVitalText;
                var expectedGold = vm.ShanheGoldText;
                var expectedRoute = vm.ShanheRouteSummary;
                var originalBytes = File.ReadAllText(campaign.PathName);
                using var cold = Create(ordinary, campaign);
                cold.ContinueShanheCommand.Execute(null);
                Program.Assert(cold.IsShanheOpen && cold.ShanheVitalText == expectedVital && cold.ShanheGoldText == expectedGold &&
                    cold.ShanheRouteSummary == expectedRoute && File.ReadAllText(campaign.PathName) == originalBytes &&
                    GameCheckpointJson.Serialize(ordinary.Read(GameSaveSlot.Manual).Checkpoint) == ordinarySave,
                    "A cold campaign restore must preserve route, resources and its source file while leaving the ordinary manual save unchanged.");
                var invalid = campaign.Read() with { Difficulty = 4 };
                var rejected = false;
                try { campaign.Write(invalid); } catch (InvalidDataException) { rejected = true; }
                Program.Assert(rejected && File.ReadAllText(campaign.PathName) == originalBytes &&
                    !Directory.EnumerateFiles(fixture, ".shanhe-*.tmp").Any(),
                    "An invalid save must be rejected before replacing the previous file or leaving temporary output.");
                var fresh = ShanheCampaignJsonText(campaign);
                Program.Assert(!vm.StartShanheCommand.CanExecute(null), "An active expedition must lock its chosen general and starting loadout until it ends.");
                vm.StartShanheCommand.Execute(null);
                Program.Assert(!vm.SelectShanheNodeCommand.CanExecute(start), "An earlier revision's map node must be retired.");
                vm.SelectShanheNodeCommand.Execute(start);
                vm.ChooseShanheOptionCommand.Execute(choice);
                Program.Assert(ShanheCampaignJsonText(campaign) == fresh,
                    "A disabled restart and retired map or story objects must leave the active expedition unchanged.");
            }
            finally { window.Content = null; window.DataContext = null; window.Close(); }
        }
        finally
        {
            // Exact files in this check's fresh fixture; never consume the historical cleanup queue.
            if (File.Exists(campaign.PathName)) File.Delete(campaign.PathName);
            if (File.Exists(campaign.PathName + ".previous")) File.Delete(campaign.PathName + ".previous");
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: false);
        }
    }

    public static void ActualEncounterSaveRoutingAndStaleMapChoices(string output)
    {
        var ordinary = new MemorySaveStore();
        var campaign = new SwitchingShanheSaveStore();
        using var vm = Create(ordinary, campaign);
        vm.SaveGameCommand.Execute(null);
        var ordinarySave = GameCheckpointJson.Serialize(ordinary.Read(GameSaveSlot.Manual).Checkpoint);
        vm.OpenShanheCommand.Execute(null);
        vm.StartShanheCommand.Execute(null);
        var opening = vm.ShanheMapRows.SelectMany(row => row.Nodes).Single(node => node.IsAvailable);
        vm.SelectShanheNodeCommand.Execute(opening);
        var definition = StandardShanheCampaignCatalog.CreateDefinition();
        var openingDefinition = definition.Nodes.Single(node => node.Id == opening.Id);
        var tacticChoiceIds = (openingDefinition.Choices ?? []).Where(choice => choice.RewardId is { } rewardId &&
            definition.Rewards.Single(reward => reward.Id == rewardId).TacticSkillId is not null)
            .Select(choice => choice.Id).ToHashSet(StringComparer.Ordinal);
        var startingTactic = vm.ShanheChoices.FirstOrDefault(option => option.IsEnabled && tacticChoiceIds.Contains(option.Id));
        Program.Assert(startingTactic is not null, "The opening must offer at least one real tactic so the first encounter can validate its native skill grant.");
        vm.ChooseShanheOptionCommand.Execute(startingTactic);
        var tacticCard = vm.ShanheTacticCards.Single();
        Program.Assert(tacticCard.Description.Length > 10 && tacticCard.SourceText.Contains("不占额外技能槽") &&
            vm.ShanheExtraSkillCards.Count == 0,
            "Acquiring a tactic must publish its full build description without occupying a learned-general skill slot.");
        var battleNode = vm.ShanheMapRows.SelectMany(row => row.Nodes).Single(node => node.IsAvailable);
        vm.SelectShanheNodeCommand.Execute(battleNode);
        var game = Program.Engine(vm);
        var tacticIds = game.CreateCheckpoint().Options.ShanheBattle!.TacticSkillIds;
        Program.Assert(tacticIds.Count == 1 && tacticIds.All(id => vm.HumanSkillCards.Any(skill =>
            skill.ContentId == id && skill.Description == game.ContentRegistry!.GetSkill(id).Description &&
            skill.Tooltip.Contains(skill.Description))) && vm.ShanheExtraSkillCards.Count == 0,
            "The real encounter must expose its newly granted tactic as a fully described native skill using the active battle registry.");
        Program.Assert(vm.IsShanheBattleSession && vm.IsShanheBattleActive && !vm.IsShanheOpen &&
            game.CreateSnapshot(0).Players.Count == 2 && game.AcceptedCommands.OfType<StartGameCommand>().Count() == 1,
            "The real route must create one native two-seat encounter and submit exactly one normal StartGame command.");
        Program.Assert(vm.TableModeText.Contains(battleNode.Name) && vm.WindowTitle.Contains("山河图") &&
            !vm.TableModeText.Contains("2v2") && vm.IdentityObjective.Contains("独自迎敌") &&
            vm.IdentityRevealRoster.Contains("义军 1 席") && vm.AliveText.Contains("敌军 1") &&
            vm.Seats.Select(seat => seat.RoleLabel).ToHashSet().SetEquals(["义军", "敌军"]) &&
            vm.ShanheCarryoverNotice.Contains("技能槽") && vm.ShanheCarryoverNotice.Contains("剩余体力"),
            "The native table must describe its actual single-player encounter, public sides and persistent build rather than a standard 2v2.");
        vm.SaveGameCommand.Execute(null);
        Program.Assert(vm.FlushPendingSave() && campaign.Read().Battle is not null && !vm.HasSaveError &&
            vm.SaveStatus.Contains("山河图已保存") &&
            GameCheckpointJson.Serialize(ordinary.Read(GameSaveSlot.Manual).Checkpoint) == ordinarySave,
            "Manual and close-flush saves during a campaign encounter must use the independent campaign file.");
        var expected = SnapshotJson.Serialize(game.CreateSnapshot(0));
        var campaignJournal = ShanheCampaignJsonText(campaign);
        var previousBattle = GameCheckpointJson.Serialize(campaign.Read().Battle!);
        campaign.FailWrites = true;
        vm.SaveGameCommand.Execute(null);
        Program.Assert(vm.HasSaveError && vm.SaveStatus.Contains("独立征程未保存") &&
            (bool)typeof(MainViewModel).GetField("_autoSavePending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)! &&
            SnapshotJson.Serialize(game.CreateSnapshot(0)) == expected && ShanheCampaignJsonText(campaign) == campaignJournal &&
            GameCheckpointJson.Serialize(campaign.Read().Battle!) == previousBattle,
            "A failed campaign manual save must expose its unsaved state on the table, retain its pending retry and preserve the prior file and live battle.");
        campaign.FailWrites = false;
        vm.SaveGameCommand.Execute(null);
        Program.Assert(!vm.HasSaveError && vm.SaveStatus.Contains("山河图已保存") &&
            !(bool)typeof(MainViewModel).GetField("_autoSavePending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!,
            "A later successful manual campaign save must clear both the visible error and the pending retry.");
        var saved = campaign.Read();
        var savedBattle = saved.Battle!;
        var wrongBattle = savedBattle with { Options = savedBattle.Options with { Seed = savedBattle.Options.Seed ^ 1 } };
        foreach (var invalidBattle in new[] { wrongBattle, savedBattle with { Options = null! } })
        {
            var mismatchedStore = new MemoryShanheCampaignSaveStore();
            mismatchedStore.Write(saved with { Battle = invalidBattle });
            var source = JsonSerializer.Serialize(mismatchedStore.Read());
            using var invalid = Create(ordinary, mismatchedStore);
            var untouched = SnapshotJson.Serialize(Program.Engine(invalid).CreateSnapshot(0));
            invalid.ContinueShanheCommand.Execute(null);
            Program.Assert(!invalid.HasShanheSession && !invalid.IsShanheBattleSession && invalid.ShanheStatusText.Contains("失败") &&
                invalid.CanPrepareShanheExpedition && invalid.StartShanheCommand.CanExecute(null) &&
                SnapshotJson.Serialize(Program.Engine(invalid).CreateSnapshot(0)) == untouched &&
                JsonSerializer.Serialize(mismatchedStore.Read()) == source,
                "A campaign with the wrong encounter seed or missing battle options must fail safely, preserve its source and ordinary game, and allow a fresh expedition.");
        }
        using var cold = Create(ordinary, campaign);
        cold.ContinueShanheCommand.Execute(null);
        Program.Assert(cold.IsShanheOpen && cold.IsShanheBattleActive && cold.IsShanheBattleSession &&
            SnapshotJson.Serialize(Program.Engine(cold).CreateSnapshot(0)) == expected,
            "A fresh view model must restore the campaign and its current battle from their independent journals.");
        cold.ResumeShanheBattleCommand.Execute(null);
        Program.Assert(!cold.IsShanheOpen && !cold.SelectShanheNodeCommand.CanExecute(battleNode),
            "Returning to the restored battle must retire all earlier view model map objects.");
        cold.SelectShanheNodeCommand.Execute(battleNode);
        Program.Assert(ShanheCampaignJsonText(campaign) == campaignJournal,
            "An old encounter button must not restart or advance a restored battle.");
        var window = new MainWindow(cold);
        window.ApplyTemplate();
        try
        {
            var root = (FrameworkElement)window.Content;
            Program.Assert(TableShortcutChecks.Input(window, Key.F5), "The real table F5 route must accept a campaign manual save.");
            Program.Render(root, 1440, 880, Path.Combine(output, "179-shanhe-real-encounter.png"));
            AssertUniqueOpponentRings(window, cold, 2);
            Program.Assert(cold.TopSeats.Count == 1 && cold.LeftSeats.Count == 0 && cold.RightSeats.Count == 0,
                "A two-seat encounter must display its sole opponent once, centered above the human seat.");
            Program.Assert(Program.Find<TextBlock>(root).Any(text => text.Text.Contains("山河图已保存") &&
                BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path == nameof(MainViewModel.SessionSaveStatus)),
                "The actual battle dock must bind the independent campaign manual-save confirmation while the map is closed.");
            cold.OpenShanheCommand.Execute(null);
            Layout(root);
            var panel = Program.Find<ShanheCampaignPanel>(root).Single();
            ((TabControl)panel.FindName("ShanheJourneyTabs")).SelectedIndex = 1;
            cold.ShanheCodexShowCollection = true;
            Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-campaign-build.png"));
            Program.Assert(Program.Find<ShanheCodexPanel>(panel).Single().DataContext == cold &&
                cold.ShanheCodexCards.Any(card => card.Id == tacticCard.Id && card.Description == tacticCard.Description) &&
                cold.ShanheTacticCards.Single().Description == tacticCard.Description,
                "The actual build tab must render the restored tactic's complete description.");
            cold.CloseShanheCommand.Execute(null);
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
        cold.StartNewGameCommand.Execute(null);
        Program.Assert(!cold.IsShanheBattleSession, "An ordinary new match must detach the campaign table marker.");
        cold.OpenShanheCommand.Execute(null);
        cold.ResumeShanheBattleCommand.Execute(null);
        Program.Assert(cold.IsShanheBattleSession && SnapshotJson.Serialize(Program.Engine(cold).CreateSnapshot(0)) == expected,
            "Starting an ordinary match must leave the suspended campaign battle available for an exact return.");
        RenderThreeSeatRepresentative(output, savedBattle.Options.ShanheBattle!);
    }

    public static void EditionSelectionExclusiveBossAndColdRestore(string output)
    {
        var ordinary = new MemorySaveStore();
        var campaign = new MemoryShanheCampaignSaveStore();
        using var vm = Create(ordinary, campaign, seed: 1);
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            vm.OpenShanheCommand.Execute(null);
            Layout(root);
            var panel = Program.Find<ShanheCampaignPanel>(root).Single();
            ClickNavigation((Button)panel.FindName("ShanhePrepareButton"), root);
            var selector = (ComboBox)panel.FindName("ShanheScenarioSelector");
            selector.SelectedItem = vm.ShanheScenarios.Single(option => option.Id == StandardShanheCampaignCatalog.Returning2026EditionId);
            vm.SelectedShanheGeneral = vm.ShanheGenerals.Single(general => general.Id == "classic:shen-guan-yu");
            Layout(root);
            Program.Assert(vm.SelectedShanheScenario.Id == StandardShanheCampaignCatalog.Returning2026EditionId &&
                vm.ShanheEditionNotice.Contains("2026") && vm.ShanheContextDescription.Contains("神鬼无前") &&
                vm.ShanheEditionNotice.Contains("神武将出征默认按群") &&
                Program.Find<TextBlock>(root).Any(text => text.Text == vm.SelectedShanheGeneralMeta && text.Text.Contains("出征默认群")),
                "The actual edition selector must explain its verified exclusive form before starting a run.");
            Program.Assert(vm.ShanheCodexNodes.Single(node => node.TreeId == 35) is
                { IsAvailable: true, RuntimeRewardId: StandardShanheCampaignCatalog.CurrentFarStrikeRewardId },
                "The actual S5 Far Strike node is usable only where its current rule has an expedition reward.");
            selector.SelectedItem = vm.ShanheScenarios.Single(option => option.Id == StandardShanheCampaignCatalog.LocalEditionId);
            Layout(root);
            Program.Assert(!vm.SelectedShanheGeneralMeta.Contains("出征默认") && !vm.ShanheEditionNotice.Contains("神武将出征默认按群") &&
                !Program.Find<TextBlock>(root).Any(text => text.Text.Contains("出征默认群")),
                "Switching to the old edition updates the real selected-general binding and removes the new god-faction policy notice.");
            Program.Assert(!vm.ShanheCodexNodes.Single(node => node.TreeId == 35).IsAvailable,
                "The historical edition retains the source-tree preview without offering the current Far Strike variant.");
            selector.SelectedItem = vm.ShanheScenarios.Single(option => option.Id == StandardShanheCampaignCatalog.Returning2026EditionId);
            Layout(root);
            Program.Assert(vm.SelectedShanheGeneralMeta.Contains("出征默认群") &&
                Program.Find<TextBlock>(root).Any(text => text.Text == vm.SelectedShanheGeneralMeta),
                "Switching back republishes the actual selected god general's default faction before expedition creation.");
            Click((Button)panel.FindName("ShanheStartButton"), root);
            var opening = vm.ShanheMapRows.SelectMany(row => row.Nodes).Single(node => node.IsAvailable);
            vm.SelectShanheNodeCommand.Execute(opening);
            vm.ChooseShanheOptionCommand.Execute(vm.ShanheChoices.First(choice => choice.IsEnabled));
            var bossNode = vm.ShanheMapRows.SelectMany(row => row.Nodes).Single(node => node.IsAvailable);
            Program.Assert(bossNode.Id == "hulao", "The verified seed must enter the real first-chapter exclusive boss route.");
            vm.SelectShanheNodeCommand.Execute(bossNode);
            var game = Program.Engine(vm);
            var bossId = game.CreateCheckpoint().Options.ShanheBattle!.EnemyRoster[0].GeneralId;
            var boss = game.CreateSnapshot(0).Players.Single(player => player.GeneralId == bossId);
            var knownForm = bossId == StandardShanheBossPackage.ShenguiLvbuId
                ? boss.Skills!.Select(skill => skill.Id).ToHashSet().SetEquals(["shanhe:shengui-wushuang", "shanhe:shengui-shenqu"]) &&
                    game.CreateSnapshot(0).Players.Single(player => player.GeneralId == StandardShanheBossPackage.FenghuangId)
                        .Skills!.Select(skill => skill.Id).ToHashSet().SetEquals(["ol:fudao", "ol:xiuhao"])
                : bossId == StandardShanheS5BossPackage.ZhangliaoBaseId
                    ? boss.Skills!.Select(skill => skill.Id).ToHashSet().SetEquals([StandardShanheS5BossPackage.TuxiId, "boundary:pojun"])
                    : bossId == StandardShanheS5BossPackage.ZhangliaoLingdongId
                        ? boss.Skills!.Select(skill => skill.Id).ToHashSet().SetEquals([StandardShanheS5BossPackage.TuxiId, "boundary:pojun", StandardShanheS5BossPackage.LingdongId])
                        : bossId == StandardShanheS5BossPackage.ZhangliaoHuangkongId &&
                            boss.Skills!.Select(skill => skill.Id).ToHashSet().SetEquals([StandardShanheS5BossPackage.TuxiId, "boundary:pojun", StandardShanheS5BossPackage.LingdongId, StandardShanheS5BossPackage.HuangkongId]);
            Program.Assert(vm.IsShanheBattleSession && knownForm && GeneralArt.HasPortrait(bossId),
                "The selected edition starts the actually drawn dedicated form, with its correct skills and portrait, rather than substituting an ordinary same-name general.");
            Program.Assert(game.CreateCheckpoint().Options.ShanheBattle!.DefaultGodFactionId == "qun" &&
                game.CreateSnapshot(0).Players[0].FactionId == "qun",
                "The selected edition's visible god-faction notice reaches the actual battle recipe and public table faction.");
            vm.SaveGameCommand.Execute(null);
            Program.Assert(vm.FlushPendingSave(), "The real exclusive encounter must save successfully.");
            var original = ShanheCampaignJsonText(campaign);
            var expected = SnapshotJson.Serialize(game.CreateSnapshot(0));
            Program.Render(root, 1440, 880, Path.Combine(output, "180-shanhe-2026-exclusive-boss.png"));
            using var cold = Create(ordinary, campaign);
            Program.Assert(cold.SelectedShanheScenario.Id == StandardShanheCampaignCatalog.LocalEditionId,
                "A fresh UI starts with the old edition selected so restoration must obtain the edition from its checkpoint.");
            cold.ContinueShanheCommand.Execute(null);
            Program.Assert(cold.IsShanheBattleSession && cold.SelectedShanheScenario.Id == StandardShanheCampaignCatalog.Returning2026EditionId &&
                cold.SelectedShanheGeneralMeta.Contains("出征默认群") &&
                SnapshotJson.Serialize(Program.Engine(cold).CreateSnapshot(0)) == expected && ShanheCampaignJsonText(campaign) == original,
                "Cold restoration must recover the saved edition and exact exclusive boss state without rewriting the source save.");
            var mismatched = new MemoryShanheCampaignSaveStore();
            var saved = campaign.Read();
            mismatched.Write(saved with { Difficulty = saved.Difficulty == 1 ? 2 : 1 });
            using var rejected = Create(ordinary, mismatched);
            var untouched = SnapshotJson.Serialize(Program.Engine(rejected).CreateSnapshot(0));
            rejected.ContinueShanheCommand.Execute(null);
            Program.Assert(!rejected.IsShanheBattleSession && rejected.ShanheStatusText.Contains("失败") &&
                SnapshotJson.Serialize(Program.Engine(rejected).CreateSnapshot(0)) == untouched &&
                mismatched.Read().Campaign.CampaignId == saved.Campaign.CampaignId,
                "A mismatched edition/difficulty save must preserve its source and the live ordinary game.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
        VerifyFengyiPreviewAndFiveSeatTable(output);
    }

    public static void LauncherSelectionMapAndDetachedWindow(string output)
    {
        var store = new SwitchingShanheSaveStore();
        using var vm = Create(new MemorySaveStore(), store);
        var owner = new MainWindow(vm);
        try
        {
            owner.Show();
            vm.OpenShanheCommand.Execute(null);
            var root = (FrameworkElement)owner.Content;
            Layout(root);
            var panel = Program.Find<ShanheCampaignPanel>(root).Single();
            vm.SelectedShanheScenario = vm.ShanheScenarios.Single(s => s.Id == StandardShanheCampaignCatalog.Returning2026EditionId);
            Layout(root);
            CheckPreparationLayout(panel);
            Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-launcher-2026.png"));
            ClickNavigation((Button)panel.FindName("ShanhePrepareButton"), root);
            var setup = (Border)panel.FindName("ShanheSetupOverlay");
            var generals = (ListBox)panel.FindName("ShanheGeneralSelector");
            Click((Button)panel.FindName("ShanheNextGeneralPageButton"), root);
            var chosen = vm.ShanheGeneralPage.Last();
            generals.SelectedItem = chosen;
            Layout(root);
            Program.Assert(setup.Visibility == Visibility.Visible &&
                !((Grid)panel.FindName("CampaignContent")).IsEnabled &&
                KeyboardNavigation.GetControlTabNavigation(setup) == KeyboardNavigationMode.Cycle &&
                vm.SelectedShanheGeneral == chosen &&
                !string.IsNullOrWhiteSpace(vm.SelectedShanheGeneralDescription) &&
                !string.IsNullOrWhiteSpace(vm.SelectedShanheSkillText),
                "The visible portrait roster must update the actual selected general and its skill preview.");
            Program.Assert(TableShortcutChecks.Input(owner, Key.Escape) && vm.IsShanheOpen &&
                setup.Visibility == Visibility.Collapsed && !store.Exists,
                "Escape must close only preparation and must not begin or save an expedition.");
            ClickNavigation((Button)panel.FindName("ShanheRulesButton"), root);
            var rules = (Border)panel.FindName("ShanheRulesOverlay");
            var details = (Expander)panel.FindName("ShanheScenarioDetailsExpander");
            details.IsExpanded = true;
            Layout(root);
            Program.Assert(((TextBlock)panel.FindName("ShanheScenarioDetailsText")).Text ==
                StandardShanheCampaignCatalog.CreateCodexDefinition(vm.SelectedShanheDifficulty.Value,
                    StandardShanheCampaignCatalog.Returning2026EditionId).IntroText && !store.Exists,
                "The short 2026 launcher keeps the exact full scenario introduction reachable through rules without starting or saving a run.");
            var rulesScroll = (ScrollViewer)panel.FindName("ShanheRulesScroll");
            rulesScroll.ScrollToBottom();
            Layout(root);
            Program.Assert(rulesScroll.VerticalOffset > 0 && rulesScroll.ScrollableHeight > 0,
                "The expanded full introduction can actually scroll to its ending inside the rules surface.");
            Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-rules.png"));
            Program.Assert(rules.Visibility == Visibility.Visible && TableShortcutChecks.Input(owner, Key.Escape) &&
                rules.Visibility == Visibility.Collapsed && vm.IsShanheOpen,
                "Rules must be a dismissible inner surface without closing the expedition.");
            owner.OpenShanheWindow();
            owner.OpenShanheWindow();
            var detached = owner.OwnedWindows.OfType<ShanheCampaignWindow>().Single();
            Program.Assert(ReferenceEquals(detached.DataContext, vm) && panel.Visibility == Visibility.Collapsed &&
                !detached.CampaignPanel.ShowDetachButton,
                "Opening again must activate one owned campaign window with the shared model and one visible panel.");
            var detachedRoot = (FrameworkElement)detached.Content;
            detached.Width = 960; detached.Height = 680;
            detached.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            detached.UpdateLayout();
            CheckPreparationLayout(detached.CampaignPanel);
            Program.Render(detached, (int)detached.ActualWidth, (int)detached.ActualHeight,
                Path.Combine(output, "shanhe-launcher-compact.png"));
            ClickNavigation((Button)detached.CampaignPanel.FindName("ShanhePrepareButton"), detachedRoot);
            Click((Button)detached.CampaignPanel.FindName("ShanheStartButton"), detachedRoot);
            Program.Assert(store.Exists && vm.HasShanheSession && vm.ShanheMapNodes.Count > 1 &&
                vm.ShanheMapLinks.Count > 0 && vm.ShanheMapNodes.Any(node => node.IsAvailable),
                "Preparation in the detached window must create a real saved campaign and actionable linked map.");
            detached.Width = 960; detached.Height = 680;
            detached.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            detached.UpdateLayout();
            var compactBounds = detached.CampaignPanel.TransformToAncestor(detached)
                .TransformBounds(new Rect(detached.CampaignPanel.RenderSize));
            Program.Assert(compactBounds.Left >= 0 && compactBounds.Top >= 0 &&
                compactBounds.Right <= detached.ActualWidth && compactBounds.Bottom <= detached.ActualHeight,
                "The minimum-size window must keep the complete campaign surface inside its client bounds.");
            Program.Render(detached, (int)detached.ActualWidth, (int)detached.ActualHeight,
                Path.Combine(output, "shanhe-window-compact.png"));
            detached.Width = 1440; detached.Height = 920;
            detached.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            detached.UpdateLayout();
            Program.Render(detached, (int)detached.ActualWidth, (int)detached.ActualHeight,
                Path.Combine(output, "shanhe-window.png"));
            store.FailWrites = true;
            detached.Close();
            Program.Assert(detached.IsVisible && vm.IsShanheOpen && vm.ShanheStatusText.Contains("未保存"),
                "A failed independent save must keep the campaign window and its retry status visible.");
            vm.CloseShanheCommand.Execute(null);
            Program.Assert(vm.IsShanheOpen, "The embedded close command must also preserve a campaign whose save failed.");
            store.FailWrites = false;
            Program.Assert(TableShortcutChecks.Input(owner, Key.F5) && vm.ShanheStatusText.Contains("已保存"),
                "F5 on the campaign surface must retry the independent map save, including outside a battle.");
            detached.Close();
            Layout(root);
            Program.Assert(!vm.IsShanheOpen && panel.Visibility == Visibility.Visible && store.Exists &&
                owner.OwnedWindows.OfType<ShanheCampaignWindow>().Count() == 0,
                "Closing the detached window must preserve the save, restore the embedded host and leave no owned window.");
            vm.OpenShanheCommand.Execute(null);
            Program.Assert(vm.IsShanheOpen && vm.ContinueShanheCommand.CanExecute(null),
                "Closing a campaign window must keep its shared view model alive for the next entry.");
        }
        finally { store.FailWrites = false; owner.Close(); }

        static void CheckPreparationLayout(ShanheCampaignPanel target)
        {
            var copy = (ScrollViewer)target.FindName("ShanhePreparationCopy");
            var actions = (StackPanel)target.FindName("ShanhePreparationActions");
            var overview = (TextBlock)target.FindName("ShanheScenarioOverview");
            var copyBounds = copy.TransformToAncestor(target).TransformBounds(new Rect(copy.RenderSize));
            var actionsBounds = actions.TransformToAncestor(target).TransformBounds(new Rect(actions.RenderSize));
            Program.Assert(copy.ViewportHeight > 0 && overview.ActualHeight <= copy.ViewportHeight &&
                copyBounds.Bottom <= actionsBounds.Top && actions.IsVisible,
                "The entire overview stays visible above the real preparation actions, including the minimum detached window.");
        }
    }

    public static void FullRosterSearchPagesAndSelection(string output)
    {
        var store = new MemoryShanheCampaignSaveStore();
        using var vm = Create(new MemorySaveStore(), store);
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            vm.OpenShanheCommand.Execute(null);
            Layout(root);
            var panel = Program.Find<ShanheCampaignPanel>(root).Single();
            ClickNavigation((Button)panel.FindName("ShanhePrepareButton"), root);
            var roster = (ListBox)panel.FindName("ShanheGeneralSelector");
            var search = (TextBox)panel.FindName("ShanheGeneralSearchBox");
            Program.Assert(vm.ShanheGenerals.Count > 100 && roster.Items.Count is > 0 and <= 8 &&
                Program.Find<ListBoxItem>(roster).Count() <= 8 && vm.ShanheGeneralCountText.Contains(vm.ShanheGenerals.Count.ToString()),
                "The campaign must offer the complete ordinary roster while realizing only a bounded page of portraits.");
            var firstPage = vm.ShanheGeneralPage.Select(general => general.Id).ToArray();
            var selectedBeforePage = vm.SelectedShanheGeneral;
            Click((Button)panel.FindName("ShanheNextGeneralPageButton"), root);
            Program.Assert(!vm.ShanheGeneralPage.Select(general => general.Id).SequenceEqual(firstPage) &&
                ReferenceEquals(vm.SelectedShanheGeneral, selectedBeforePage) && roster.Items.Count <= 8,
                "Turning the roster page must expose more generals while preserving the chosen expedition general.");
            search.Text = "诸葛亮";
            Layout(root);
            var target = vm.ShanheGeneralPage.Single(general => general.Id == "classic:zhuge-liang");
            roster.SelectedItem = target;
            Layout(root);
            Program.Assert(ReferenceEquals(vm.SelectedShanheGeneral, target) &&
                vm.ShanheGeneralPage.All(general => general.Name.Contains("诸葛亮")) &&
                vm.SelectedShanheGeneralDescription.Contains("观星"),
                "Searching the actual roster must allow selecting a general outside the initial eight and display real skills.");
            Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-full-roster-search.png"));
            search.Text = "观星";
            Layout(root);
            Program.Assert(vm.ShanheGeneralPage.Contains(target), "Campaign search must include registered skill names.");
            search.Text = "OL";
            Layout(root);
            Program.Assert(vm.ShanheGeneralPage.Count > 0 && vm.ShanheGeneralPage.All(general =>
                general.SearchText.Contains("OL", StringComparison.OrdinalIgnoreCase)),
                "Campaign search must match edition metadata as well as names.");
            search.Text = "__missing_shanhe_general__";
            Layout(root);
            Program.Assert(vm.HasNoShanheGeneralResults && roster.Items.Count == 0 &&
                ReferenceEquals(vm.SelectedShanheGeneral, target),
                "An empty search must explain its empty state without silently clearing the selected general.");
            vm.ClearShanheGeneralSearchCommand.Execute(null);
            Layout(root);
            Program.Assert(!vm.HasNoShanheGeneralResults && vm.ShanheGenerals.Contains(target) &&
                ReferenceEquals(vm.SelectedShanheGeneral, target) && !store.Exists,
                "Clearing search must restore the full roster without starting a run or changing its chosen general.");
            Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-full-roster.png"));
            Click((Button)panel.FindName("ShanheStartButton"), root);
            Program.Assert(store.Exists && store.Read().Campaign.Options.PlayerGeneralId == target.Id,
                "Confirming a searched general must create the real campaign with that exact content ID.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
    }

    public static void PreparationLocksAndEncounterPreview(string output)
    {
        var store = new MemoryShanheCampaignSaveStore();
        using var vm = Create(new MemorySaveStore(), store);
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            vm.OpenShanheCommand.Execute(null);
            Layout(root);
            var panel = Program.Find<ShanheCampaignPanel>(root).Single();
            Program.Assert(vm.IsShanhePreparation && vm.CanPrepareShanheExpedition,
                "A fresh preparation surface must permit choosing a general and editing a loadout.");
            ClickNavigation((Button)panel.FindName("ShanhePrepareButton"), root);
            var rankSelector = (ComboBox)panel.FindName("ShanheRankSelector");
            rankSelector.SetCurrentValue(Selector.SelectedItemProperty, vm.ShanheRanks.Single(option => option.Value == 5));
            Layout(root);
            Program.Assert(vm.ShanheRanks.Count == 5 && vm.SelectedShanheRank.Value == 5 &&
                ((Border)panel.FindName("ShanheSetupOverlay")).Visibility == Visibility.Visible &&
                rankSelector.Visibility == Visibility.Visible && rankSelector.ActualHeight > 0 && rankSelector.IsEnabled &&
                vm.SelectedShanheRankDescription.Contains("获得5张") && vm.SelectedShanheRankDescription.Contains("整次征程"),
                $"The actual preparation selector must expose all five ranks and distinguish gaining five cards from refilling a hand. Rank={vm.SelectedShanheRank.Value}, enabled={rankSelector.IsEnabled}, height={rankSelector.ActualHeight}.");
            Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-rank-five-preparation.png"));
            vm.StartShanheCommand.Execute(null);
            Layout(root);
            var saved = ShanheCampaignJsonText(store);
            Program.Assert(vm.IsShanheRunInProgress && !vm.IsShanhePreparation &&
                !vm.CanPrepareShanheExpedition && !vm.StartShanheCommand.CanExecute(null) &&
                !vm.EquipCodexTacticCommand.CanExecute(null) && !vm.UnlockCodexNodeCommand.CanExecute(null) &&
                !vm.RemoveCodexTacticCommand.CanExecute(null),
                "Beginning an expedition must lock the general, new-run action and all codex edits.");
            vm.SelectedShanheRank = vm.ShanheRanks[0];
            Program.Assert(vm.SelectedShanheRank.Value == 5 && !rankSelector.IsEnabled &&
                store.Read().Campaign.Options.PlayerRank == 5 && ShanheCampaignJsonText(store) == saved &&
                vm.ShanheRankStatusText.Contains("重生可用"),
                "The chosen rank must be frozen into the saved expedition and neither a hidden selector nor a programmatic edit can downgrade it.");
            using (var cold = Create(new MemorySaveStore(), store))
            {
                cold.SelectedShanheRank = cold.ShanheRanks[1];
                cold.ContinueShanheCommand.Execute(null);
                Program.Assert(cold.SelectedShanheRank.Value == 5 && cold.ShanheRankStatusText.Contains("重生可用") &&
                    ShanheCampaignJsonText(store) == saved,
                    "Continuing restores the saved fifth rank instead of the current preparation preference and leaves its journal untouched.");
            }
            typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke((Button)panel.FindName("ShanhePrepareButton"), null);
            Layout(root);
            Program.Assert(((Border)panel.FindName("ShanheSetupOverlay")).Visibility == Visibility.Collapsed &&
                ShanheCampaignJsonText(store) == saved,
                "A routed click against the hidden preparation entry must not reopen setup during a run.");
            var opening = vm.ShanheMapRows.SelectMany(row => row.Nodes).Single(node => node.IsAvailable);
            vm.SelectShanheNodeCommand.Execute(opening);
            vm.ChooseShanheOptionCommand.Execute(vm.ShanheChoices.First(choice => choice.IsEnabled));
            Layout(root);
            var battleNode = vm.ShanheMapRows.SelectMany(row => row.Nodes).Single(node => node.IsAvailable);
            var game = Program.Engine(vm);
            var journal = ShanheCampaignJsonText(store);
            Program.Assert(vm.ShanheMapNodes.Count < vm.ShanheMapRows.Sum(row => row.Nodes.Count) &&
                vm.ShanheChapterTitle.StartsWith("第一章"),
                "An active map must show its genuine current chapter instead of every chapter at once.");
            Click(Bound(panel, vm.PreviewShanheNodeCommand, battleNode), root);
            Program.Assert(vm.IsShanheEncounterPreviewOpen && !vm.IsShanheBattleActive &&
                ReferenceEquals(Program.Engine(vm), game) && ShanheCampaignJsonText(store) == journal &&
                vm.ShanheEncounterEnemyName.Length > 0 && vm.ShanheEncounterRewardText.Contains("铜币"),
                "Previewing an encounter must show real opponents and rewards without committing a move, save or battle.");
            Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-encounter-preview.png"));
            Program.Assert(panel.HandleEscape() && !vm.IsShanheEncounterPreviewOpen &&
                ShanheCampaignJsonText(store) == journal, "Escape must dismiss only the uncommitted encounter preview.");
            battleNode = vm.ShanheMapRows.SelectMany(row => row.Nodes).Single(node => node.IsAvailable);
            Click(Bound(panel, vm.PreviewShanheNodeCommand, battleNode), root);
            Click(Program.Find<Button>(panel).Single(button => ReferenceEquals(button.Command, vm.ChallengeShanheEncounterCommand)), root);
            Program.Assert(!vm.IsShanheEncounterPreviewOpen && vm.IsShanheBattleActive && vm.IsShanheBattleSession &&
                store.Read().Battle?.Options.ShanheBattle is { PlayerRank: 5, RebirthAvailable: true } &&
                Program.Engine(vm).CreateSnapshot(0).Players[0].Skills!.Count(skill => skill.ContentId == ShanheGeneralRanks.RebirthSkillId) == 1 &&
                !ReferenceEquals(Program.Engine(vm), game),
                "Confirming the actual challenge button must enter and save the real native battle exactly once.");
            var committed = ShanheCampaignJsonText(store);
            vm.ChallengeShanheEncounterCommand.Execute(null);
            Program.Assert(ShanheCampaignJsonText(store) == committed,
                "A repeated challenge confirmation cannot replace or restart the committed encounter.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
    }

    public static void PublishedFixedNpcCardsAreReadOnlyInEncounterPreview(string output)
    {
        var published = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        var guangcheng = published.Nodes.Single(node => node.Id == "guangcheng");
        var fixedEnemies = new[] { StandardShanheS5LiJuePackage.LijueId, StandardShanheS5GuosiPackage.GuosiId }
            .Select(id => guangcheng.Encounters!.SelectMany(encounter => encounter.Enemies).Single(enemy => enemy.GeneralId == id)).ToArray();
        Program.Assert(fixedEnemies.SelectMany(enemy => enemy.StartingCards ?? []).Select(card => card.Kind)
                .SequenceEqual([CardKind.ScarletBloodSword, CardKind.ZhanhunDao]) &&
            fixedEnemies.All(enemy => enemy.StartingHand is null),
            "The actual published Guangcheng candidates retain random opening hands and expose only their two additional fixed weapons.");
        // Retain the entire published graph and both original departures.
        // Expose the route choice instead of randomly selecting one entrance;
        // the real southern preparation commands still unlock Guangcheng.
        var session = ShanheCampaignSession.Create(published with { RandomStartingNode = false },
            new(89, "classic:guan-yu"), StandardShanheCampaignCatalog.CreateBattleRegistry);
        var store = new PreviewTrackingShanheStore();
        using var model = Create(new MemorySaveStore(), store);
        model.SelectedShanheScenario = model.ShanheScenarios.Single(option => option.Id == StandardShanheCampaignCatalog.Returning2026EditionId);
        typeof(MainViewModel).GetField("_shanheSession", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, session);
        var window = new MainWindow(model); window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            model.OpenShanheCommand.Execute(null); Layout(root);
            var panel = Program.Find<ShanheCampaignPanel>(root).Single();
            var departure = model.ShanheMapRows.SelectMany(row => row.Nodes).Single(candidate => candidate.Id == "guangcheng-start");
            Click(Bound(panel, model.PreviewShanheNodeCommand, departure), root);
            Click(Bound(panel, model.ChooseShanheOptionCommand, model.ShanheChoices.Single(choice => choice.Id == "supplies")), root);
            var node = model.ShanheMapRows.SelectMany(row => row.Nodes).Single(candidate => candidate.Id == guangcheng.Id);
            Program.Assert(node.IsAvailable && session.Snapshot.Phase == ShanheCampaignPhase.Map &&
                session.Snapshot.CompletedNodeIds.Contains(departure.Id) && session.CreateCheckpoint().Commands.Count == 2 && store.Exists,
                "The actual southern departure and preparation choice must complete before its published Guangcheng encounter is available for preview.");
            var ordinary = Program.Engine(model);
            var ordinaryCheckpoint = GameCheckpointJson.Serialize(ordinary.CreateCheckpoint());
            var journal = ShanheCampaignJson.Serialize(session.CreateCheckpoint());
            var snapshot = JsonSerializer.Serialize(session.Snapshot);
            var writes = store.WriteCount;
            var revision = session.Revision;
            Click(Bound(panel, model.PreviewShanheNodeCommand, node), root);
            var scroll = (ScrollViewer)panel.FindName("ShanheEncounterEnemyDetailScroll");
            Program.Assert(model.IsShanheEncounterPreviewOpen && scroll.Content is TextBlock detail &&
                detail.Text == model.ShanheEncounterEnemyDetail && detail.Visibility == Visibility.Visible && detail.ActualHeight > 0 &&
                detail.Text.Contains("李傕", StringComparison.Ordinal) && detail.Text.Contains("郭汜", StringComparison.Ordinal) &&
                detail.Text.Contains("额外起手牌（随机起手之外）", StringComparison.Ordinal) &&
                new[] { CardKind.ScarletBloodSword, CardKind.ZhanhunDao }.All(kind =>
                    detail.Text.Contains("【" + CardCatalog.Get(kind).DisplayName + "】", StringComparison.Ordinal) &&
                    detail.Text.Contains(CardCatalog.Get(kind).Description, StringComparison.Ordinal)),
                "The real preview control shows Scarlet Blood Sword and Soul Cleaver with their complete CardCatalog rules as additional hand cards, alongside the published Li Jue and Guo Si.");
            Program.Assert(session.PendingBattleSetup is null && session.Revision == revision && session.Snapshot.EncounterOrdinal == 0 &&
                ShanheCampaignJson.Serialize(session.CreateCheckpoint()) == journal && JsonSerializer.Serialize(session.Snapshot) == snapshot &&
                ReferenceEquals(Program.Engine(model), ordinary) && GameCheckpointJson.Serialize(ordinary.CreateCheckpoint()) == ordinaryCheckpoint &&
                !model.IsShanheBattleSession && store.WriteCount == writes && store.Exists,
                "The public fixed-card preview does not roll an encounter, deal or reveal random opening cards, create a battle, change either journal or write a save.");
            scroll.ScrollToBottom(); Layout(root);
            Program.Assert(scroll.VerticalOffset > 0 && Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1,
                "The actual scroll surface reaches the complete extra-card rules at the end of the published candidate roster.");
            Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-public-fixed-card-preview.png"));
            Click((Button)panel.FindName("ShanheEncounterCloseButton"), root);
            Program.Assert(!model.IsShanheEncounterPreviewOpen && session.PendingBattleSetup is null &&
                ShanheCampaignJson.Serialize(session.CreateCheckpoint()) == journal && store.WriteCount == writes && store.Exists,
                "Closing the actual fixed-card preview preserves the unissued encounter and read-only save boundary.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
    }

    public static void InheritanceOffersBeforeFirstMapAndSurvivesColdRestore(string output)
    {
        var store = new MemoryShanheCampaignSaveStore();
        var codex = new MemoryShanheCodexStore();
        codex.Write(new() { UnlockedRewardIds = [StandardShanheCampaignCatalog.OpeningInheritanceRewardId],
            EquippedTacticIds = [StandardShanheCampaignCatalog.OpeningInheritanceRewardId, null, null, null, null, null] });
        using var vm = new MainViewModel(false, 270019, true, new MemorySaveStore(), useExpandedContent: true,
            shanheSaveStore: store, shanheCodexStore: codex) { IsMotionEnabled = false };
        var panel = new ShanheCampaignPanel { DataContext = vm };
        try
        {
            vm.OpenShanheCommand.Execute(null);
            vm.StartShanheCommand.Execute(null);
            Layout(panel);
            var before = ShanheCampaignJsonText(store);
            Program.Assert(vm.IsShanheRunInProgress && vm.IsShanheRewardOpen && !vm.CanPrepareShanheExpedition &&
                vm.ShanheRewardNotice.Contains("传承") && vm.ShanheRewardCards.Count == 3 &&
                vm.ShanheRewardCards.All(card => !card.IsSkip) && vm.ShanheMapNodes.All(node => !node.IsAvailable) &&
                store.Read().Campaign.Commands.Count == 0,
                "Equipping inheritance must present its three real rare skills before any map action and prevent skipping or restarting.");
            var offers = vm.ShanheRewardCards.Select(card => card.Id).ToArray();
            using (var cold = new MainViewModel(false, 270019, true, new MemorySaveStore(), useExpandedContent: true,
                shanheSaveStore: store, shanheCodexStore: codex))
            {
                cold.ContinueShanheCommand.Execute(null);
                Program.Assert(cold.IsShanheRewardOpen && cold.ShanheRewardCards.Select(card => card.Id).SequenceEqual(offers) &&
                    ShanheCampaignJsonText(store) == before,
                    "Cold restoration must reopen the same inheritance candidates without redrawing or writing the save.");
            }
            var selected = vm.ShanheRewardCards[0];
            Click(Bound(panel, vm.SelectShanheRewardCardCommand, selected), panel);
            Program.Assert(ShanheCampaignJsonText(store) == before && vm.ShanheExtraSkillCards.Count == 0,
                "Selecting an inheritance card must not grant it before confirmation.");
            Program.Render(panel, 1200, 750, Path.Combine(output, "shanhe-inheritance-choice.png"));
            Click((Button)panel.FindName("ShanheConfirmRewardButton"), panel);
            var saved = store.Read().Campaign;
            var expectedSkill = StandardShanheCampaignCatalog.CreateCodexDefinition(1,
                StandardShanheCampaignCatalog.LocalEditionId).Rewards.Single(reward => reward.Id == selected.Id).SkillId;
            Program.Assert(!vm.IsShanheRewardOpen && vm.ShanheExtraSkillCards.Single().Id == expectedSkill &&
                saved.Commands.Count == 1 && saved.Commands[0] is CardGame.Core.Campaigns.ChooseShanheOpeningSkillCommand &&
                vm.ShanheMapRows.SelectMany(row => row.Nodes).Count(node => node.IsAvailable) == 1,
                "Confirming inheritance must grant exactly the chosen skill then unlock the untouched first map node.");
            var committed = ShanheCampaignJsonText(store);
            vm.SelectShanheRewardCardCommand.Execute(selected);
            vm.ConfirmShanheRewardCommand.Execute(null);
            Program.Assert(ShanheCampaignJsonText(store) == committed, "A stale inheritance card cannot grant a second skill.");
        }
        finally { panel.DataContext = null; }
    }

    private static MainViewModel Create(IGameSaveStore ordinary, IShanheCampaignSaveStore campaign, int seed = 270017) =>
        new(false, seed, true, ordinary, useExpandedContent: true, shanheSaveStore: campaign)
        { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };

    private sealed class SwitchingShanheSaveStore : IShanheCampaignSaveStore
    {
        private readonly MemoryShanheCampaignSaveStore _inner = new();
        public bool FailWrites { get; set; }
        public bool Exists => _inner.Exists;
        public ShanheCampaignSaveFile Read() => _inner.Read();
        public void Write(ShanheCampaignSaveFile save)
        {
            if (FailWrites) throw new IOException("The campaign fixture disk is temporarily unavailable.");
            _inner.Write(save);
        }
    }

    private static void VerifyFengyiPreviewAndFiveSeatTable(string output)
    {
        var full = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        var luoyang = full.Nodes.Single(node => node.Id == "luoyang");
        var encounter = luoyang.Encounters!.Single(candidate => candidate.Id == "luoyang:fengyi-s5");
        // Preserve the published Luoyang node and every real candidate, while making
        // it the fixture's first route; no earlier battle or victory is fabricated.
        var definition = full with { StartingNodeIds = [luoyang.Id], Nodes = [luoyang] };
        var session = ShanheCampaignSession.Create(definition, new(83, "classic:guan-yu"), StandardShanheCampaignCatalog.CreateBattleRegistry);
        var store = new PreviewTrackingShanheStore();
        using (var model = Create(new MemorySaveStore(), store))
        {
            model.SelectedShanheScenario = model.ShanheScenarios.Single(option => option.Id == StandardShanheCampaignCatalog.Returning2026EditionId);
            typeof(MainViewModel).GetField("_shanheSession", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, session);
            var window = new MainWindow(model); window.ApplyTemplate();
            var root = (FrameworkElement)window.Content;
            try
            {
                model.OpenShanheCommand.Execute(null); Layout(root);
                var panel = Program.Find<ShanheCampaignPanel>(root).Single();
                var node = model.ShanheMapRows.SelectMany(row => row.Nodes).Single(candidate => candidate.Id == luoyang.Id);
                var ordinary = Program.Engine(model); var ordinaryCheckpoint = GameCheckpointJson.Serialize(ordinary.CreateCheckpoint());
                var journal = ShanheCampaignJson.Serialize(session.CreateCheckpoint()); var state = JsonSerializer.Serialize(session.Snapshot);
                var writes = store.WriteCount;
                Click(Bound(panel, model.PreviewShanheNodeCommand, node), root);
                var expected = "董卓·覆銮凶影  体力 10/15";
                Program.Assert(model.IsShanheEncounterPreviewOpen && model.ShanheEncounterEnemyDetail.Contains(expected, StringComparison.Ordinal) &&
                    !model.ShanheEncounterEnemyDetail.Contains("董卓·覆銮凶影  体力 15/15", StringComparison.Ordinal) &&
                    Program.Find<TextBlock>(panel).Any(text => text.Text.Contains(expected, StringComparison.Ordinal)),
                    "The real Luoyang preview binding uses General.InitialHp for the new partial-health boss instead of silently displaying full 15/15.");
                var portraitCaption = Program.Find<TextBlock>(panel).Single(text =>
                    BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path == nameof(MainViewModel.ShanheEncounterEnemyPortraitName));
                Program.Assert(portraitCaption.Text == model.ShanheEncounterEnemyPortraitName &&
                    portraitCaption.Text == "董卓·洛阳技能形态" &&
                    portraitCaption.ToolTip is string names && names == model.ShanheEncounterEnemyName && names.Contains("覆銮凶影") &&
                    portraitCaption.Parent is Border { Parent: Border portraitHost } captionHost &&
                    captionHost.ActualHeight > 0 && captionHost.ActualHeight < portraitHost.ActualHeight,
                    "The real portrait caption names its pictured form, fits inside the portrait and retains the complete candidate list in its tooltip and scrollable details.");
                Program.Assert(session.PendingBattleSetup is null && session.Revision == 0 && session.Snapshot.EncounterOrdinal == 0 &&
                    ShanheCampaignJson.Serialize(session.CreateCheckpoint()) == journal && JsonSerializer.Serialize(session.Snapshot) == state &&
                    ReferenceEquals(Program.Engine(model), ordinary) && GameCheckpointJson.Serialize(ordinary.CreateCheckpoint()) == ordinaryCheckpoint &&
                    store.WriteCount == writes && !store.Exists,
                    "Opening the actual preview neither rolls or commits an encounter, changes either command journal, replaces the ordinary engine nor attempts a save.");
                var scroll = (ScrollViewer)panel.FindName("ShanheEncounterEnemyDetailScroll");
                Program.Assert(scroll is { ScrollableHeight: > 0, Content: TextBlock detail } &&
                    detail.Text.Contains(expected, StringComparison.Ordinal) && detail.ActualHeight > scroll.ViewportHeight,
                    "The genuine multi-candidate Luoyang details remain in a scrollable native text surface instead of clipping the final 10/15 source form.");
                scroll.ScrollToBottom(); Layout(root);
                Program.Assert(scroll.VerticalOffset > 0 && Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1,
                    "The actual encounter detail control can reach the bottom of the complete source roster.");
                Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-fengyi-preview-scrolled.png"));
                Click((Button)panel.FindName("ShanheEncounterCloseButton"), root);
                Program.Assert(!model.IsShanheEncounterPreviewOpen && ShanheCampaignJson.Serialize(session.CreateCheckpoint()) == journal &&
                    store.WriteCount == writes && !store.Exists,
                    "Closing the read-only native preview preserves the same unissued encounter and save boundary.");
            }
            finally { window.Content = null; window.DataContext = null; window.Close(); }
        }

        var setup = new ShanheBattleSetup(83, 1, luoyang.Id, "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [],
            encounter.Enemies, encounter.Allies ?? [], ShanheCampaignContent.GetContentHash(full)) { DefaultGodFactionId = full.DefaultGodFactionId };
        var battle = StandardShanheCampaignCatalog.CreateBattle(setup);
        var accepted = battle.Submit(new StartGameCommand());
        Program.Assert(accepted.Accepted && battle.PlayerCount == 5, "The full Fengyi layout uses a genuine five-seat native encounter.");
        using var table = Create(new MemorySaveStore(), new MemoryShanheCampaignSaveStore());
        typeof(MainViewModel).GetField("_shanheTableGame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(table, battle);
        typeof(MainViewModel).GetField("_shanheTableSetup", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(table, setup);
        typeof(MainViewModel).GetMethod("ReplaceEngine", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(table, [battle]);
        table.IsNewGameSetupOpen = false;
        var tableWindow = new MainWindow(table); tableWindow.ApplyTemplate();
        try
        {
            var root = (FrameworkElement)tableWindow.Content;
            Program.Render(root, 1440, 880, Path.Combine(output, "shanhe-fengyi-five-seat.png"));
            AssertUniqueOpponentRings(tableWindow, table, 5);
            Program.Assert(table.RightSeats.Count == 1 && table.TopSeats.Count == 2 && table.LeftSeats.Count == 1 &&
                table.Seats.Where(seat => !seat.IsHuman).Select(seat => seat.GeneralId).ToHashSet().SetEquals(
                    ["classic:sun-jian", StandardShanheS5BossPackage.DongZhuoFengyiId, StandardShanheS5BossPackage.LvbuBajiId, StandardShanheS5BossPackage.DiaoChanBiyueId]) &&
                table.Seats.Count(seat => seat.IsTeammate) == 1 && table.Seats.Single(seat => seat.GeneralId == "classic:sun-jian").IsTeammate,
                "The actual four non-human seats occupy disjoint 1/2/1 rings, including the real Sun Jian ally and all three distinct source enemies.");
            foreach (var seat in table.Seats.Where(seat => !seat.IsHuman))
                Program.Assert(Program.Find<TextBlock>(root).Any(text => ReferenceEquals(text.DataContext, seat) && text.Text == seat.VerticalName &&
                    text.Visibility == Visibility.Visible && text.ActualWidth > 0 && text.ActualHeight > 0),
                    "Every actual opponent/ally model has its own laid-out name in the real seat template: " + seat.GeneralId);
            var dong = table.Seats.Single(seat => seat.GeneralId == StandardShanheS5BossPackage.DongZhuoFengyiId);
            Program.Assert(dong.HealthValue == "10/15" && !dong.IsTeammate && dong.HasPortrait &&
                GeneralArt.NormalizeKey(dong.Portrait.GeneralId) == GeneralArt.NormalizeKey(dong.GeneralId) &&
                dong.Portrait.Image is System.Windows.Media.Imaging.BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } &&
                GeneralArt.GetSkin(dong.GeneralId)!.LocalPath == GeneralArt.GetSkin("classic:dong-zhuo")!.LocalPath,
                "The new boss's real seat displays wounded 10/15 and the Dong Zhuo asset alias, with a decoded image rather than a transparent fallback.");
            foreach (var (generalId, portraitId) in new[]
                {
                    (StandardShanheS5BossPackage.LvbuBajiId, "classic:lu-bu"),
                    (StandardShanheS5BossPackage.DiaoChanBiyueId, "classic:diao-chan")
                })
            {
                var seat = table.Seats.Single(item => item.GeneralId == generalId);
                Program.Assert(seat.HasPortrait && GeneralArt.NormalizeKey(seat.Portrait.GeneralId) == GeneralArt.NormalizeKey(generalId) &&
                    seat.Portrait.Image is System.Windows.Media.Imaging.BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } &&
                    GeneralArt.GetSkin(generalId)!.LocalPath == GeneralArt.GetSkin(portraitId)!.LocalPath,
                    "Each companion's exact source form renders its own decoded character artwork through the existing shared-skin contract: " + generalId);
            }
        }
        finally { tableWindow.Content = null; tableWindow.DataContext = null; tableWindow.Close(); }
    }

    private sealed class PreviewTrackingShanheStore : IShanheCampaignSaveStore
    {
        private readonly MemoryShanheCampaignSaveStore _inner = new();
        public int WriteCount { get; private set; }
        public bool Exists => _inner.Exists;
        public ShanheCampaignSaveFile Read() => _inner.Read();
        public void Write(ShanheCampaignSaveFile save) { WriteCount++; _inner.Write(save); }
    }

    private static void RenderThreeSeatRepresentative(string output, CardGame.Core.Campaigns.ShanheBattleSetup previousSetup)
    {
        var definition = StandardShanheCampaignCatalog.CreateDefinition();
        var node = definition.Nodes.First(candidate => candidate.Encounters?.Any(encounter =>
            encounter.Enemies.Count == 1 && encounter.Allies?.Count == 1) == true);
        var encounter = node.Encounters!.First(candidate => candidate.Enemies.Count == 1 && candidate.Allies?.Count == 1);
        var setup = previousSetup with
        {
            NodeId = node.Id, EncounterOrdinal = previousSetup.EncounterOrdinal + 1,
            EnemyRoster = encounter.Enemies, AllyRoster = encounter.Allies!
        };
        var battle = StandardShanheCampaignCatalog.CreateBattle(setup);
        var started = battle.Submit(new StartGameCommand());
        Program.Assert(started.Accepted && battle.CreateSnapshot(0).Players.Count == 3,
            "The three-seat layout fixture must start a real catalog encounter with one AI ally and one enemy.");
        using var model = Create(new MemorySaveStore(), new MemoryShanheCampaignSaveStore());
        // Bind a real native encounter without fabricating victories through the preceding campaign chapters.
        typeof(MainViewModel).GetField("_shanheTableGame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, battle);
        typeof(MainViewModel).GetField("_shanheTableSetup", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, setup);
        typeof(MainViewModel).GetMethod("ReplaceEngine", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [battle]);
        model.IsNewGameSetupOpen = false;
        var window = new MainWindow(model);
        window.ApplyTemplate();
        try
        {
            Program.Render((FrameworkElement)window.Content, 1440, 880, Path.Combine(output, "shanhe-three-seat-encounter.png"));
            AssertUniqueOpponentRings(window, model, 3);
            Program.Assert(model.LeftSeats.Count == 1 && model.RightSeats.Count == 1 && model.TopSeats.Count == 0 &&
                model.Seats.Count(seat => seat.IsTeammate) == 1 && model.IdentityRevealRoster.Contains("义军 2 席"),
                "A three-seat native encounter must render the distinct AI ally and enemy once on opposite sides.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
    }

    private static void AssertUniqueOpponentRings(MainWindow window, MainViewModel model, int playerCount)
    {
        var rings = new[] { "TopSeatRing", "LeftSeatRing", "RightSeatRing" }
            .Select(name => (ItemsControl)window.FindName(name)).ToArray();
        var displayed = rings.SelectMany(ring => ring.Items.Cast<SeatViewModel>()).ToArray();
        var expected = model.Seats.Where(seat => !seat.IsHuman).Select(seat => seat.Seat).ToHashSet();
        Program.Assert(model.Seats.Count == playerCount && displayed.Length == playerCount - 1 &&
            displayed.Select(seat => seat.Seat).Distinct().Count() == displayed.Length &&
            displayed.Select(seat => seat.Seat).ToHashSet().SetEquals(expected),
            "The actual three XAML rings must partition all non-human seats without duplicating or dropping a target.");
        foreach (var ring in rings)
        foreach (var seat in ring.Items.Cast<SeatViewModel>())
            Program.Assert(ring.ItemContainerGenerator.ContainerFromItem(seat) is FrameworkElement container &&
                ReferenceEquals(container.DataContext, seat),
                "Each opponent must have one generated native seat container bound to its own unique seat model.");
    }

    private static string ShanheCampaignJsonText(IShanheCampaignSaveStore store) =>
        CardGame.Core.Campaigns.ShanheCampaignJson.Serialize(store.Read().Campaign);

    private static Button Bound(FrameworkElement root, System.Windows.Input.ICommand command, object parameter) =>
        Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, parameter));

    private static void Click(Button button, FrameworkElement root)
    {
        Program.Assert(button.IsEnabled && button.Command?.CanExecute(button.CommandParameter) == true,
            "The actual campaign button must be enabled before its routed click.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
        Layout(root);
    }

    private static void ClickNavigation(Button button, FrameworkElement root)
    {
        Program.Assert(button.IsEnabled, "A campaign navigation button must be enabled before its routed click.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
        Layout(root);
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1440, 880)); root.Arrange(new Rect(0, 0, 1440, 880)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
}
