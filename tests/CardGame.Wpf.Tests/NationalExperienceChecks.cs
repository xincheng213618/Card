using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class NationalExperienceChecks
{
    private static void Require(bool condition, string message) => Program.Assert(condition, message);
    private static string State(MainViewModel vm) => GameCheckpointJson.Serialize(Program.Engine(vm).CreateCheckpoint());

    public static void ControlsAndRestore(string output)
    {
        var saves = new FileGameSaveStore(Path.Combine(output, "national-saves", Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(false, 721019, true, saves, useExpandedContent: true) { IsMotionEnabled = false };
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "national:lite-4");
        Require(vm.NationalSetupText.StartsWith("魏 2 人、蜀 2 人", StringComparison.Ordinal),
            "Lite national faction distribution did not use the stable Wei-then-Shu order.");
        Program.Render(root, 1120, 740, Path.Combine(output, "59-national-setup.png"));
        Require(vm.IsNationalModeSelection && !vm.IsIdentityModeSelection && !vm.IsTeamModeSelection, "National setup retained identity or team choices.");
        vm.OpenContextGuideCommand.Execute(null);
        vm.SelectedGuideSection = vm.GuideSections.Single(section => section.Key == "basics");
        Program.Render(root, 1120, 740, Path.Combine(output, "60-national-guide.png"));
        Require(Program.Find<StackPanel>(root).Single(panel => panel.Name == "NationalBasics").Visibility == Visibility.Visible && !vm.IsIdentityGuide && !vm.IsTeamGuide,
            "National guide controls do not match the selected mode.");
        vm.ToggleHelpCommand.Execute(null);
        ((Button)window.FindName("StartNewGameButton")).Command.Execute(null);
        var selected = new List<string>();
        for (var i = 0; i < 300 && !vm.CanEndTurn; i++)
        {
            if (vm.IsGeneralSelectionPending) selected.Add(vm.GeneralChoices[0].Name);
            PersistenceChecks.Step(vm);
        }
        Require(selected.Count == 2 && selected.Distinct().Count() == 2 && vm.CanEndTurn, "Human national setup did not select two distinct generals and reach play.");
        Require(vm.IsNationalSnapshot && vm.HumanPlayer!.HasSecondaryGeneral && vm.NationalRevealChoices.Count == 2, "Dual-general presentation or reveal actions are missing.");
        Require(vm.HumanSkillCards.Count == 2 && vm.HumanSkillCards.All(skill => skill.IsDisabled && skill.StateText.Contains("尚未启用")),
            "Hidden national generals exposed enabled skills in the human skill rail.");
        var own = Program.Engine(vm).CreateSnapshot(0).Players[0];
        Require(vm.HumanPlayer!.RoleLabel is "魏" or "蜀" && !own.IsGeneralPublic && !own.IsSecondaryGeneralPublic, "Own private faction or hidden slots are wrong.");
        Require(vm.Seats.Where(seat => !seat.IsHuman).All(seat => seat.RoleLabel == "未明势力"), "Unrevealed opponents exposed their faction.");
        AdviceChecks.VerifyLive(vm);
        Program.Render(root, 1120, 740, Path.Combine(output, "61-national-hidden.png"));
        var reveals = (ItemsControl)window.FindName("NationalRevealButtons");
        var primary = vm.NationalRevealChoices.Single(choice => choice.Slot == GeneralSelectionSlot.Primary);
        var button = Program.Find<Button>(reveals).Single(item => Equals(item.CommandParameter, primary));
        Require(button.IsEnabled && button.ActualHeight > 0, "Primary reveal is not an actionable control.");
        button.Command.Execute(button.CommandParameter);
        Program.AdvanceToDecision(vm);
        Require(vm.NationalRevealChoices.Count == 1 && Program.Engine(vm).CreateSnapshot(1).Players[0] is { IsGeneralPublic: true, IsSecondaryGeneralPublic: false },
            "Primary reveal leaked or revealed the secondary slot.");
        Require(vm.HumanSkillCards.Count(skill => !skill.IsDisabled) == 1 && vm.HumanSkillCards.Count(skill => skill.IsDisabled) == 1,
            "The human skill rail did not enable only the revealed national slot.");
        var half = State(vm);
        vm.RevealNationalGeneralCommand.Execute(primary);
        Require(State(vm) == half, "Stale reveal command mutated the game.");
        Program.Render(root, 1120, 740, Path.Combine(output, "62-national-primary.png"));
        vm.SaveGameCommand.Execute(null);
        Require(!vm.HasSaveError, vm.SaveStatus);
        vm.StartTutorialCommand.Execute(null);
        Require(vm.IsTutorialActive && !vm.IsNationalSnapshot && vm.IsIdentityGuide, "Tutorial inherited national rules.");
        vm.ExitTutorialCommand.Execute(null);
        Require(State(vm) == half && vm.IsNationalGuide && vm.NationalRevealChoices.Count == 1, "Tutorial failed to restore hidden secondary general.");
        using var resumed = new MainViewModel(false, 12, true, saves, useExpandedContent: true) { IsMotionEnabled = false };
        resumed.LoadManualGameCommand.Execute(null);
        Require(!resumed.HasSaveError && State(resumed) == half && resumed.IsNationalSnapshot && resumed.NationalRevealChoices.Count == 1,
            "Half-revealed national save did not restore exactly.");
        resumed.RevealNationalGeneralCommand.Execute(resumed.NationalRevealChoices.Single());
        Program.AdvanceToDecision(resumed);
        Require(resumed.NationalRevealChoices.Count == 0 && Program.Engine(resumed).CreateSnapshot(1).Players[0].IsSecondaryGeneralPublic, "Secondary reveal failed after file restore.");
        Complete(resumed);
        var report = MatchSummaryChecks.VerifyCompleted(resumed);
        Require(resumed.CompletedMatch!.Players.All(player => player.SecondaryGeneralName is not null && player.Camp is "魏" or "蜀"), "National result lost dual generals or factions.");
        resumed.SaveGameCommand.Execute(null);
        Require(!resumed.HasSaveError, resumed.SaveStatus);
        vm.LoadManualGameCommand.Execute(null);
        Require(!vm.HasSaveError && State(vm) == State(resumed) && report == System.Text.Json.JsonSerializer.Serialize(vm.CompletedMatch), "Finished national save changed the result.");
        Program.Render(root, 1120, 740, Path.Combine(output, "63-national-result.png"));
        MatchSummaryChecks.VerifyControls(window);
        window.Content = null;
        window.Close();
    }

    internal static void Complete(MainViewModel vm)
    {
        var sounds = new List<GameSound>();
        var plays = 0;
        vm.SoundsRequested += (_, args) => sounds.AddRange(args.Sounds);
        for (var i = 0; i < 30000 && !vm.HasGameOver; i++)
        {
            if (vm.NationalRevealChoices.FirstOrDefault() is { } reveal) vm.RevealNationalGeneralCommand.Execute(reveal);
            else if (vm.CanEndTurn)
            {
                AdviceChecks.VerifyLive(vm);
                var action = Program.Engine(vm).GetHumanLegalActions().FirstOrDefault(candidate => candidate.CardId.HasValue && candidate.TargetSeats.Count <= 1 && candidate.TargetCardId is null);
                if (action is null) vm.EndTurnCommand.Execute(null);
                else
                {
                    vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == action.CardId));
                    if (action.TargetSeat is { } seat && !vm.Seats.Single(player => player.Seat == seat).IsSelectedTarget)
                        vm.SelectTargetCommand.Execute(vm.Seats.Single(player => player.Seat == seat));
                    if (action.Kind == LegalActionKind.Recast && vm.CanRecastSelected) vm.RecastSelectedCommand.Execute(null);
                    else if (vm.CanConfirmSelected) { vm.ConfirmSelectedCommand.Execute(null); plays++; }
                    else vm.EndTurnCommand.Execute(null);
                }
            }
            else PersistenceChecks.Step(vm);
        }
        var stalled = Program.Engine(vm).State;
        Require(vm.HasGameOver, $"National match stalled through WPF commands at turn {stalled.TurnNumber}, seat {stalled.CurrentSeat}, status {stalled.Status}, winner {stalled.Winner}, faction {stalled.WinnerFactionId}.");
        var view = Program.Engine(vm).CreateSnapshot(0);
        var sound = view.Winner == Winner.Draw ? GameSound.Draw : view.WinnerFactionId == view.Players[0].FactionId ? GameSound.Victory : GameSound.Defeat;
        var title = sound switch { GameSound.Victory => "胜 利", GameSound.Defeat => "败 北", _ => "平 局" };
        Require(vm.GameOutcomeTitle == title && sounds.Contains(sound), "National outcome or sound does not follow the human faction.");
        var expectedFactionText = view.WinnerFactionId switch
        {
            "wei" => "魏势力获胜",
            "shu" => "蜀势力获胜",
            "ambitious" => "野心家势力获胜",
            _ => string.Empty
        };
        Require(view.Winner == Winner.Draw || vm.GameOverText == expectedFactionText, "National winning faction label was reversed.");
        Require(plays > 0, "National human never used a card.");
        Require(view.Players.All(player => player.IsGeneralPublic && player.IsSecondaryGeneralPublic), "Finished UI retained hidden generals.");
        Console.WriteLine($"  National WPF: {view.TurnNumber} turns, {plays} human plays, {view.Players[0].FactionId} player, {view.WinnerFactionId} winner, {title}.");
    }
}
