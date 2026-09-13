using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TeamExperienceChecks
{
    public static void ControlsAndRestore(string output)
    {
        var store = new FileGameSaveStore(Path.Combine(output, "team-saves", Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(false, 721019, true, store) { IsMotionEnabled = false };
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var original = State(vm);
        var identityObjective = vm.IdentityObjective;
        vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "team:standard-2v2");
        Program.Render(root, 1120, 740, Path.Combine(output, "31-team-setup.png"));
        var teamChoices = (ListBox)window.FindName("TeamChoices");
        Require(teamChoices.Visibility == Visibility.Visible && teamChoices.Items.Count == 2, "Team picker is missing.");
        teamChoices.SelectedItem = vm.StartingTeams.Single(team => team.TeamId == "team:red");
        Require(vm.SelectedStartingTeam.TeamId == "team:red" && vm.TeamModeSetupText.Contains("赤队"), "Red team control did not update setup.");
        vm.OpenContextGuideCommand.Execute(null);
        Require(vm.GuideIdentity.Contains("赤队") && !vm.GuideIdentity.Contains("主公"), "Team setup still describes a hidden identity.");
        Require(vm.IsTeamGuide && vm.CurrentGuideTitle.Contains("队伍"), "Team setup guide did not switch modes.");
        vm.ToggleHelpCommand.Execute(null);
        vm.CancelNewGameSetupCommand.Execute(null);
        Require(State(vm) == original && vm.IdentityObjective == identityObjective && !vm.IsTeamGuide,
            "Previewing team setup changed the suspended identity match.");

        vm.NewGameCommand.Execute(null);
        Program.Render(root, 1120, 740, Path.Combine(output, "31-team-setup.png"));
        ((Button)window.FindName("StartNewGameButton")).Command.Execute(null);
        Require(vm.HumanPlayer?.TeamId == "team:red" && vm.HumanPlayer.RoleLabel == "赤队", "Core did not apply the selected team.");
        Require(vm.GeneralSelectionSubtitle.Contains("1 位队友") && vm.GeneralSelectionSubtitle.Contains("2 位对手"), "Selection counts a teammate as an enemy.");
        Require(vm.IdentityObjective.Contains("与赤队队友") && vm.IdentityObjective.Contains("击败青队"), "Objective does not follow the player's team.");
        Require(vm.Seats.Count(seat => seat.IsTeammate) == 1 && vm.Seats.Count(seat => seat.SeatLabel.Contains("对手")) == 2, "Public enemy/friend markers are wrong.");
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        for (var i = 0; i < 300 && !vm.CanEndTurn && !vm.HasGameOver; i++) PersistenceChecks.Step(vm);
        Require(vm.CanEndTurn, "Red team never reached its play phase.");
        Require(vm.AliveText == "青队 2 · 赤队 2 存活", "Living team counts are wrong.");
        Program.Render(root, 1120, 740, Path.Combine(output, "32-team-table.png"));
        var teamState = State(vm);
        var teamObjective = vm.IdentityObjective;
        vm.NewGameCommand.Execute(null);
        vm.SelectedTableMode = vm.TableModes.Single(mode => mode.PlayerCount == 5);
        vm.OpenContextGuideCommand.Execute(null);
        Require(vm.IsIdentityGuide && !vm.GuideIdentity.Contains("赤队"), "Identity setup retained the old team's guide.");
        vm.ToggleHelpCommand.Execute(null);
        vm.CancelNewGameSetupCommand.Execute(null);
        Require(State(vm) == teamState && vm.IdentityObjective == teamObjective && vm.IsTeamGuide,
            "Canceling identity setup changed the active team's presentation.");

        vm.OpenContextGuideCommand.Execute(null);
        vm.SelectedGuideSection = vm.GuideSections.Single(section => section.Key == "basics");
        Program.Render(root, 1120, 740, Path.Combine(output, "33-team-guide.png"));
        Require(Program.Find<StackPanel>(root).Single(panel => panel.Name == "TeamBasics").Visibility == Visibility.Visible &&
            Program.Find<StackPanel>(root).Single(panel => panel.Name == "IdentityBasics").Visibility == Visibility.Collapsed,
            "Actual guide controls show identity rules in a team game.");
        vm.ToggleHelpCommand.Execute(null);
        vm.StartTutorialCommand.Execute(null);
        Require(vm.IsTutorialActive && vm.IsIdentityGuide && !vm.IdentityObjective.Contains("队友"), "Practice inherited the formal match's team rules.");
        vm.ExitTutorialCommand.Execute(null);
        Require(State(vm) == teamState && vm.IdentityObjective == teamObjective && vm.IsTeamGuide, "Practice did not restore red-team presentation.");
        vm.SaveGameCommand.Execute(null);
        Require(!vm.HasSaveError, vm.SaveStatus);
        using var resumed = new MainViewModel(false, 3, true, store) { IsMotionEnabled = false };
        resumed.LoadManualGameCommand.Execute(null);
        Require(!resumed.HasSaveError && State(resumed) == teamState, "Team file failed to restore exactly.");
        Require(resumed.SelectedStartingTeam.TeamId == "team:red" && resumed.IdentityObjective == teamObjective,
            "Load silently changed the chosen team back to blue.");

        for (var step = 0; step < 18000 && !resumed.HasGameOver; step++) PersistenceChecks.Step(resumed);
        Require(resumed.HasGameOver && Program.Engine(resumed).State.Winner is Winner.TeamA or Winner.TeamB or Winner.Draw,
            "Red team did not finish a real match through UI commands.");
        var expected = Program.Engine(resumed).State.Winner switch { Winner.TeamB => "胜 利", Winner.TeamA => "败 北", _ => "平 局" };
        Require(resumed.GameOutcomeTitle == expected, "Team result is not from the red player's perspective.");
        var expectedReport = MatchSummaryChecks.VerifyCompleted(resumed);
        resumed.SaveGameCommand.Execute(null);
        Require(!resumed.HasSaveError, resumed.SaveStatus);
        var completed = State(resumed);
        vm.LoadManualGameCommand.Execute(null);
        Require(!vm.HasSaveError && State(vm) == completed && vm.GameOutcomeTitle == expected, "Completed team save lost the result.");
        Require(expectedReport == System.Text.Json.JsonSerializer.Serialize(vm.CompletedMatch), "File restore changed team result statistics.");
        Program.Render(root, 1120, 740, Path.Combine(output, "34-team-result.png"));
        MatchSummaryChecks.VerifyControls(window);
        vm.NewGameCommand.Execute(null);
        Require(vm.SelectedStartingTeam.TeamId == "team:red", "Rematch setup lost the original team.");
        Program.Render(root, 1120, 740, Path.Combine(output, "35-team-rematch.png"));
        teamChoices.SelectedItem = vm.StartingTeams.Single(team => team.TeamId == "team:blue");
        vm.StartNewGameCommand.Execute(null);
        Require(vm.HumanPlayer?.TeamId == "team:blue" && vm.IdentityObjective.Contains("击败赤队"), "Switching to blue for a rematch failed.");
        Require(vm.CompletedMatch is null, "New match retained the old report.");
        Require(vm.Seats.Count(seat => seat.IsTeammate) == 1, "Blue rematch retained red enemy markers.");
        window.Content = null;
        window.Close();
        using var expanded = new MainViewModel(false, 721019, true, new MemorySaveStore(), useExpandedContent: true);
        var expandedWindow = new MainWindow(expanded);
        Program.Render((FrameworkElement)expandedWindow.Content, 1120, 740, Path.Combine(output, "36-expanded-setup.png"));
        expandedWindow.Content = null;
        expandedWindow.Close();
    }

    private static string State(MainViewModel vm) => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0, true));
    private static void Require(bool condition, string message) => Program.Assert(condition, message);
}
