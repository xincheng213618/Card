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

            store.Write(GameSaveSlot.Manual, new GameSaveFile(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, oldCheckpoint));
            vm.LoadManualGameCommand.Execute(null);
            Require(!vm.HasSaveError && Program.Engine(vm).RulesVersion == 5, "Rules 5 save no longer loads.");
            vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == cardId));
            vm.SelectTargetCommand.Execute(vm.HumanPlayer!);
            Require(!vm.HumanPlayer!.IsSelectedTarget && !vm.ShowRecastAction && !vm.CanRecastSelected,
                "Old save shows new rule actions.");
            vm.SelectedGuideCategory = "全部";
            vm.GuideSearchText = "铁索连环";
            Require(vm.FilteredGuideCards.Single().Description.Contains("其他存活角色") && !vm.FilteredGuideCards.Single().Description.Contains("重铸"),
                "Old save displays current-rules card text.");
            vm.GuideSearchText = "乐不思蜀";
            Require(vm.FilteredGuideCards.Single().Description.Contains("红色") && !vm.FilteredGuideCards.Single().Description.Contains("红桃"),
                "Old save displays rules 11 delayed-card text.");
            vm.StartNewGameCommand.Execute(null);
            Require(Program.Engine(vm).RulesVersion == GameCheckpoint.CurrentRulesVersion && vm.FilteredGuideCards.Single().Description.Contains("红桃") &&
                vm.FilteredGuideCards.Single().Description.Contains("不为"), "New match retained the old delayed-card description.");
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
