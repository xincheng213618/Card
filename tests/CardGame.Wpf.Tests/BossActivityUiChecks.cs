using System.IO;
using System.Windows;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class BossActivityUiChecks
{
    public static void RealSetupAndCrossModeRestore(string output)
    {
        var store = new MemorySaveStore();
        using var vm = new MainViewModel(false, 17, showSetup: true, saveStore: store, useExpandedContent: true)
        { IsMotionEnabled = false };
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            vm.OpenLobbyCategoryCommand.Execute("activity");
            vm.SelectedTableMode = vm.VisibleTableModes.Single(mode => mode.ModeId == "activity:ghost-2018");
            Program.Assert(vm.IsActivityModeSelection && vm.IsTeamModeSelection && !vm.IsIdentityModeSelection &&
                vm.SelectedTableMode.PlayerCount == 4 && vm.StartingTeams is { Count: 1 } &&
                vm.SelectedStartingTeam.TeamId == "team:allies" && vm.SelectedStartingTeam.Name == "联合军",
                "A boss activity must offer the real four-seat game and only the player coalition.");
            Program.Assert(vm.TeamModeSetupText.Contains("两名人机") && vm.TeamModeSetupText.Contains("四关") &&
                vm.TeamModeSetupText.Contains("五阶") && vm.TeamModeSetupText.Contains("涅槃") &&
                vm.CurrentGuideBody.Contains("联合军"),
                "The setup and guide must explain the real AI teammates, stages and selected tier rules.");
            Program.Render(root, 1440, 880, Path.Combine(output, "153-ghost-setup.png"));
            vm.StartNewGameFromLobbyCommand.Execute(null);
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            for (var step = 0; step < 24 && !vm.IsGeneralSelectionPending; step++) vm.StepAiCommand.Execute(null);
            Program.Assert(vm.IsGeneralSelectionPending && vm.WindowTitle.Contains("驱鬼逐邪") &&
                vm.GeneralSelectionSubtitle.Contains("两名队友") && vm.GeneralSelectionContextTitle.Contains("联合军") &&
                vm.IdentityObjective.Contains("四关"),
                "The real boss activity must reach its coalition general prompt with the correct objective.");
            Program.Render(root, 1440, 880, Path.Combine(output, "154-ghost-selection.png"));
            vm.ConfirmGeneralChoiceCommand.Execute(null);
            if (vm.RunToHumanCommand.CanExecute(null)) vm.RunToHumanCommand.Execute(null);
            var snapshot = Program.Engine(vm).CreateSnapshot(0);
            Program.Assert(snapshot.Players.Count == 4 && snapshot.Players.Count(player => player.IsHuman) == 1 &&
                snapshot.Players.Count(player => player.TeamId == "team:allies") == 3 &&
                snapshot.Players.Single(player => player.TeamId == "team:boss") is { IsHuman: false } &&
                snapshot.Players.All(player => player.GeneralId.Length > 0) &&
                vm.Seats.Count(seat => seat.RoleLabel == "联合军") == 3 && vm.Seats.Count(seat => seat.RoleLabel == "鬼军") == 1 &&
                vm.TableModeText.Contains("第 1/4 关") && vm.AliveText.Contains("联合军 3"),
                "The four actual seats must show the coalition, AI boss and current stage without Jiange labels.");
            Program.Render(root, 1440, 880, Path.Combine(output, "155-ghost-table.png"));
            vm.SaveGameCommand.Execute(null);
            var saved = SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0));

            // The selected setup mode can differ from the saved match. Restoring
            // validates the saved team's mode, rather than the current selector.
            vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "team:standard-2v2");
            Program.Assert(vm.SelectedStartingTeam.TeamId == "team:blue", "Changing setup modes must retire an incompatible coalition choice.");
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && Program.Engine(vm).ModeId == "activity:ghost-2018" &&
                vm.SelectedStartingTeam.TeamId == "team:allies" && vm.WindowTitle.Contains("驱鬼逐邪") &&
                SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) == saved,
                "A boss checkpoint must restore from the 2v2 selector with its actual package, coalition and stage.");
        }
        finally
        {
            window.Content = null;
            window.DataContext = null;
            window.Close();
        }
    }
}
