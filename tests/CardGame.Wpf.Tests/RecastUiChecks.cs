using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class RecastUiChecks
{
    public static void ConvertedLianhuanSelectionAndReplay(string output)
    {
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var saveStore = new FileGameSaveStore(Path.Combine(output, "lianhuan-saves"));
            using var vm = new MainViewModel(autoAdvance: false, seed: seed, showSetup: false,
                saveStore: saveStore, useExpandedContent: true) { IsMotionEnabled = false };
            var general = vm.GeneralChoices.SingleOrDefault(choice => choice.GeneralId == "classic:pang-tong");
            if (general is null) continue;
            vm.SelectGeneralChoiceCommand.Execute(general);
            Program.AdvanceToDecision(vm);
            var engine = Program.Engine(vm);
            if (engine.PendingDecision?.Kind != DecisionKind.PlayCard) continue;
            var recast = engine.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Recast && action.ConversionSource is
                    { SkillId: "classic:lianhuan", BindingId: "club-hand-as-iron-chain" });
            if (recast is null) continue;

            var cardId = recast.CardId!.Value;
            vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == cardId));
            Require(!vm.CanRecastSelected,
                "A converted recast must wait for an explicit conversion-source selection.");
            var conversionChoice = vm.EquipmentPlayChoices.Single(choice =>
                choice.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:lianhuan" &&
                choice.Parameters.GetValueOrDefault("conversion-binding-id") == "club-hand-as-iron-chain" &&
                choice.Cards.SequenceEqual([cardId]));
            vm.SelectEquipmentPlayChoiceCommand.Execute(conversionChoice);
            Require(vm.CanRecastSelected,
                "Selecting the published Lianhuan source must enable converted recast.");
            var before = engine.AcceptedCommands.Count;
            vm.RecastSelectedCommand.Execute(null);
            Require(engine.AcceptedCommands.Count == before + 1 &&
                    engine.AcceptedCommands.Last() is RecastCardCommand command &&
                    command.CardId == cardId && command.ConversionSource == recast.ConversionSource &&
                    engine.Events.Any(entry => entry.Payload is CardRecastEvent evt &&
                        evt.CardId == cardId && evt.CardKind == CardKind.IronChain),
                "The WPF recast must submit the exact selected Program source and resolve as Iron Chain.");

            vm.SaveGameCommand.Execute(null);
            var saved = SnapshotJson.Serialize(engine.CreateSnapshot(0, revealAll: true));
            vm.LoadManualGameCommand.Execute(null);
            Require(!vm.HasSaveError &&
                    SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0, revealAll: true)) == saved &&
                    Program.Engine(vm).AcceptedCommands.Last() is RecastCardCommand restored &&
                    restored.ConversionSource == recast.ConversionSource,
                "Converted Lianhuan recast must save and replay its exact source.");
            return;
        }

        throw new InvalidOperationException("No bounded Pang Tong WPF fixture exposed a converted recast.");
    }

    public static void ControlsAndOldSaves(string output)
    {
        var store = new FileGameSaveStore(Path.Combine(output, "recast-saves"));
        using var vm = new MainViewModel(false, 721019, showSetup: false, saveStore: store) { IsMotionEnabled = false };
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var engine = Program.Engine(vm);
        var oldCheckpoint = engine.CreateCheckpoint() with { RulesVersion = 5 };
        var cardId = engine.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Recast).CardId!.Value;
        var handCount = vm.Hand.Count;
        var commandsBefore = engine.AcceptedCommands.Count;
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            var button = (Button)window.FindName("RecastButton");
            vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == cardId));
            Program.Render(root, 1120, 740, Path.Combine(output, "47-recast-ready.png"));
            Require(button.IsEnabled && button.Visibility == Visibility.Visible && !vm.CanConfirmSelected, "Recast should have a distinct enabled control with zero targets.");
            Require(!Shortcut(window, Key.Enter) && engine.AcceptedCommands.Count == commandsBefore, "Enter silently recast an unconfirmed card.");
            vm.SelectTargetCommand.Execute(vm.HumanPlayer!);
            Require(vm.CanConfirmSelected && !vm.CanRecastSelected, "A chain target did not disable recasting.");
            vm.RecastSelectedCommand.Execute(null);
            Require(engine.AcceptedCommands.Count == commandsBefore, "Recast discarded a card with chain targets selected.");
            vm.SelectTargetCommand.Execute(vm.HumanPlayer!);
            Shortcut(window, Key.F1);
            Require(vm.CurrentGuideSteps.Any(step => step.Text.Contains("重铸")) && !Shortcut(window, Key.Enter), "Recast guidance is missing or modal isolation failed.");
            Shortcut(window, Key.Escape);
            var eventStart = engine.Events.Count;
            button.Command.Execute(null);
            Require(engine.AcceptedCommands.Count == commandsBefore + 1 && engine.AcceptedCommands.Last() is RecastCardCommand command && command.CardId == cardId,
                "Recast button did not submit one exact independent command.");
            Require(vm.Hand.Count == handCount && vm.Hand.All(card => card.Id != cardId) && !vm.HasSelection && !vm.ShowRecastAction,
                "Recast did not exchange one card and clear its draft.");
            Require(!engine.Events.Skip(eventStart).Any(entry => entry.Payload is CardUseDeclaredEvent) &&
                vm.RecentPlays.First().Name == "铁索连环" && vm.RecentPlays.First().ActorName.Contains("重铸") &&
                vm.BattleCues.Any(cue => cue.Label.Contains("重铸")),
                "Recast feedback was absent or recorded as a normal card use.");
            Program.AdvanceToDecision(vm);
            Program.Render(root, 1120, 740, Path.Combine(output, "48-recast-result.png"));
            vm.SaveGameCommand.Execute(null);
            var savedState = SnapshotJson.Serialize(engine.State);
            vm.LoadManualGameCommand.Execute(null);
            Require(!vm.HasSaveError && Program.Engine(vm).RulesVersion == GameCheckpoint.CurrentRulesVersion && SnapshotJson.Serialize(Program.Engine(vm).State) == savedState,
                "JSON save did not restore the recast command and its replacement card.");

            var preservedState = SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0, revealAll: true));
            store.Write(GameSaveSlot.Manual, new GameSaveFile(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, oldCheckpoint));
            vm.LoadManualGameCommand.Execute(null);
            Require(vm.HasSaveError &&
                    Program.Engine(vm).RulesVersion == GameCheckpoint.CurrentRulesVersion &&
                    SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0, revealAll: true)) == preservedState,
                "The exact-rules loader accepted rules v5 or changed the active game after rejecting it.");
            vm.StartNewGameCommand.Execute(null);
            Require(Program.Engine(vm).RulesVersion == GameCheckpoint.CurrentRulesVersion,
                "New match did not use the current rules version.");
            vm.GuideSearchText = "酒";
            Require(vm.FilteredGuideCards.Single(card => card.Kind == CardKind.Alcohol).Timing.Contains("仅可自救"),
                "New match did not display the current self-rescue Alcohol timing text.");
            vm.GuideSearchText = "诸葛连弩";
            Require(vm.FilteredGuideCards.Single().Description.Contains("攻击范围 1") &&
                !vm.FilteredGuideCards.Single().Description.Contains("+1"),
                "New match retained the legacy Crossbow range text.");
            vm.GuideSearchText = "青釭剑";
            Require(vm.FilteredGuideCards.Single().Description.Contains("攻击范围 2"),
                "New match did not display the current Qinggang range text.");
            vm.GuideSearchText = "八卦阵";
            Require(vm.FilteredGuideCards.Single().Description.Contains("需要使用或打出闪"),
                "New match did not display the formal Bagua response timing.");
            vm.GuideSearchText = "仁王盾";
            Require(vm.FilteredGuideCards.Single().Description.Contains("黑色杀对你无效"),
                "New match did not display the formal Renwang effect text.");
            vm.GuideSearchText = "乐不思蜀";
            Require(vm.FilteredGuideCards.Single().Description.Contains("红桃") && vm.FilteredGuideCards.Single().Description.Contains("不为"),
                "New match retained the old delayed-card description.");
            vm.GuideSearchText = "铁索连环";
            Require(vm.FilteredGuideCards.Single().Description.Contains("重铸") && vm.FilteredGuideCards.Single().Description.Contains("自己"),
                "New match retained the old Iron Chain rule description.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static bool Shortcut(MainWindow window, Key key) => (bool)typeof(MainWindow)
        .GetMethod("HandleShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [key, ModifierKeys.None])!;
    private static void Require(bool condition, string message) => Program.Assert(condition, message);
}
