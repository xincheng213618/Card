using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static partial class ShenwuActivityUiChecks
{
    public static void SharedGodDraftSixSeatsAndRestore(string output)
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true,
            includeBossActivities: true, includeHulaoActivities: true, includeShenwuActivities: true);
        var activity = registry.Modes["activity:shenwu-2017"].ShenwuActivity!;
        var store = new MemorySaveStore();
        using var vm = new MainViewModel(false, 17, showSetup: true, saveStore: store,
            useExpandedContent: true) { IsMotionEnabled = false };
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            vm.OpenLobbyCategoryCommand.Execute("activity");
            vm.SelectedTableMode = vm.VisibleTableModes.Single(mode => mode.ModeId == "activity:shenwu-2017");
            Program.Assert(vm.IsActivityModeSelection && vm.IsTeamModeSelection && vm.SelectedTableMode.PlayerCount == 6 &&
                vm.StartingTeams.Count == 1 && vm.SelectedStartingTeam.TeamId == activity.PlayerTeamId &&
                vm.TeamModeSetupText.Contains("共享") && vm.TeamModeSetupText.Contains("五阶") &&
                vm.TeamModeSetupText.Contains("烛阴") && vm.CurrentGuideTitle.Contains("神武再世") &&
                !vm.TeamModeSetupText.Contains("四关") && !vm.CurrentGuideBody.Contains("器械"),
                "Shenwu setup must explain the six-seat encounter, shared gods and local tier five.");
            Program.Render(root, 1440, 880, Path.Combine(output, "159-shenwu-setup.png"));

            vm.StartNewGameFromLobbyCommand.Execute(null);
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            for (var step = 0; step < 24 && !vm.IsGeneralSelectionPending; step++) vm.StepAiCommand.Execute(null);
            var shared = vm.GeneralChoices.Where(choice => activity.GodPoolIds.Contains(choice.GeneralId)).ToArray();
            Program.Assert(vm.IsGeneralSelectionPending && shared.Length == 3 &&
                vm.GeneralChoices.Count(choice => !activity.GodPoolIds.Contains(choice.GeneralId)) == 5 &&
                vm.GeneralSelectionFooter.Contains("三人共享") && vm.GeneralSelectionContextDetail.Contains("五名普通") &&
                vm.GeneralSelectionSubtitle.Contains("两名队友") && vm.IdentityObjective.Contains("烛阴可以不消灭") &&
                shared.All(choice => choice.Kingdom == "神" && choice.HealthText.Contains("五阶")),
                "The actual draft must show five ordinary candidates and three shared, still-god candidates.");
            Program.Render(root, 1440, 880, Path.Combine(output, "160-shenwu-selection.png"));
            // Avoid a seven-stars setup exchange; all choices remain real engine offers.
            var chosen = shared.First(choice => choice.GeneralId != "ol:shen-zhuge-liang");
            vm.PreviewGeneralChoiceCommand.Execute(chosen);
            vm.ConfirmGeneralChoiceCommand.Execute(null);
            if (vm.RunToHumanCommand.CanExecute(null)) vm.RunToHumanCommand.Execute(null);
            var game = Program.Engine(vm);
            var snapshot = game.CreateSnapshot(0);
            Program.Assert(snapshot.Players.Count == 6 && snapshot.Players.Count(player => player.IsHuman) == 1 &&
                snapshot.Players.Count(player => player.TeamId == activity.PlayerTeamId && !player.IsHuman) == 2 &&
                snapshot.Players.Single(player => player.IsHuman).FactionId == "god" &&
                snapshot.Players.Single(player => player.IsHuman).GeneralId == chosen.GeneralId &&
                snapshot.Players.Single(player => player.IsHuman).Equipment.Count > 0 &&
                snapshot.Players.Count(player => player.GeneralId == chosen.GeneralId) == 1 &&
                vm.Seats.Count(seat => seat.RoleLabel == "首领") == 1 &&
                vm.Seats.Count(seat => seat.RoleLabel == "烛阴") == 2 &&
                vm.Seats.Where(seat => seat.TeamId == activity.EnemyTeamId).All(seat => seat.HasPortrait && seat.Portrait.Image is not null) &&
                vm.Seats.Count(seat => seat.IsTeammate) == 2 &&
                vm.TopSeats.Count == 3 && vm.LeftSeats.Count == 1 && vm.RightSeats.Count == 1 &&
                vm.TableModeText.Contains("首领") && vm.TableArenaTitle == "神 武 再 世" &&
                vm.AliveText.Contains("联合军 3") && vm.AliveText.Contains("首领 1") && vm.AliveText.Contains("烛阴 2"),
                "The live six-seat table must retain god faction, dedicated equipment and distinct leader/minion labels.");
            Program.Render(root, 1440, 880, Path.Combine(output, "161-shenwu-table.png"));
            vm.SaveGameCommand.Execute(null);
            var saved = SnapshotJson.Serialize(game.CreateSnapshot(0));
            vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "team:standard-2v2");
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && Program.Engine(vm).ModeId == "activity:shenwu-2017" &&
                vm.SelectedStartingTeam.TeamId == activity.PlayerTeamId && vm.WindowTitle.Contains("神武再世") &&
                vm.TableArenaTitle == "神 武 再 世" && vm.Seats.Count(seat => seat.RoleLabel == "首领") == 1 &&
                SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) == saved,
                "Restoring Shenwu from another selector must retain the encounter, god draft and actual six seats.");

            var configuredWithoutShenwu = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true,
                includeBossActivities: true, includeHulaoActivities: true);
            using var reopened = new MainViewModel(false, 17, showSetup: true, saveStore: store,
                useExpandedContent: true, contentRegistry: configuredWithoutShenwu) { IsMotionEnabled = false };
            Program.Assert(!configuredWithoutShenwu.Modes.ContainsKey("activity:shenwu-2017") &&
                reopened.TableModes.All(mode => mode.ModeId != "activity:shenwu-2017"),
                "The second desktop must genuinely start without Shenwu in its configured registry or lobby.");
            reopened.LoadManualGameCommand.Execute(null);
            Program.Assert(!reopened.HasSaveError && Program.Engine(reopened).ModeId == "activity:shenwu-2017" &&
                reopened.SelectedTableMode.ModeId == "activity:shenwu-2017" && reopened.IsTeamModeSelection &&
                reopened.StartingTeams.Count == 1 && reopened.SelectedStartingTeam.TeamId == activity.PlayerTeamId &&
                reopened.Seats.Count == 6 && reopened.Seats.Count(seat => seat.RoleLabel == "首领") == 1 &&
                reopened.Seats.Count(seat => seat.RoleLabel == "烛阴") == 2 &&
                reopened.TopSeats.Count == 3 && reopened.LeftSeats.Count == 1 && reopened.RightSeats.Count == 1 &&
                reopened.WindowTitle.Contains("神武再世") && reopened.TableArenaTitle == "神 武 再 世" &&
                SnapshotJson.Serialize(Program.Engine(reopened).CreateSnapshot(0)) == saved,
                "Cross-configuration restore must use the checkpoint registry for mode metadata, coalition and the unchanged six-seat table.");
            reopened.StartNewGameFromLobbyCommand.Execute(null);
            reopened.ContinueFromIdentityRevealCommand.Execute(null);
            for (var step = 0; step < 24 && !reopened.IsGeneralSelectionPending; step++) reopened.StepAiCommand.Execute(null);
            var restarted = Program.Engine(reopened);
            var restartedView = restarted.CreateSnapshot(0);
            Program.Assert(!reopened.HasSaveError && restarted.ModeId == "activity:shenwu-2017" &&
                reopened.IsGeneralSelectionPending && restarted.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } &&
                restartedView.Players.Count == 6 && restartedView.Players.Single(player => player.IsHuman).TeamId == activity.PlayerTeamId &&
                restartedView.Players.Count(player => player.TeamId == activity.PlayerTeamId) == 3 &&
                restartedView.Players.Count(player => player.TeamId == activity.EnemyTeamId) == 3 &&
                reopened.SelectedStartingTeam.TeamId == activity.PlayerTeamId && reopened.TableArenaTitle == "神 武 再 世" &&
                reopened.GeneralChoices.Count(choice => activity.GodPoolIds.Contains(choice.GeneralId)) == 3 &&
                reopened.GeneralSelectionFooter.Contains("三人共享"),
                "Starting again after cross-configuration restore must reach a real Shenwu draft through the restored matching registry, retaining six seats and coalition setup.");
        }
        finally
        {
            window.Content = null;
            window.DataContext = null;
            window.Close();
        }
    }
}
