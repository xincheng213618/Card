using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class NationalHealthChecks
{
    private static void Require(bool value, string message) => Program.Assert(value, message);
    private static string Checkpoint(MainViewModel vm) => GameCheckpointJson.Serialize(Program.Engine(vm).CreateCheckpoint());

    public static void ControlsAndOldPackage(string output)
    {
        var store = new FileGameSaveStore(Path.Combine(output, "dual-hp-saves", Guid.NewGuid().ToString("N")));
        MainViewModel? found = null;
        for (var seed = 721000; seed < 721200; seed++)
        {
            var candidate = new MainViewModel(false, seed, true, store, useExpandedContent: true) { IsMotionEnabled = false };
            candidate.SelectedTableMode = candidate.TableModes.Single(mode => mode.ModeId == "national:lite-4");
            candidate.StartNewGameCommand.Execute(null);
            Program.AdvanceToDecision(candidate);
            if (candidate.GeneralChoices.Any(general => general.GeneralId == "national:wei-cao-cao")) { found = candidate; break; }
            candidate.Dispose();
        }
        using var vm = found ?? throw new InvalidOperationException("No bounded WPF fixture offered Cao Cao.");
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "64-national-primary-hp.png"));
        Require(Program.Find<TextBlock>(root).Count(text => text.Name == "GeneralHealthPreview" && text.Visibility == Visibility.Visible && text.ActualHeight > 0 && text.Text.StartsWith("基础体力")) == vm.GeneralChoices.Count,
            "Primary general controls did not show their base health.");
        var cao = vm.GeneralChoices.Single(general => general.GeneralId == "national:wei-cao-cao");
        var choiceButton = Program.Find<Button>(root).Single(button => ReferenceEquals(button.CommandParameter, cao));
        choiceButton.Command.Execute(cao);
        var primaryRevision = Program.Engine(vm).Revision;
        Require(vm.SelectedGeneralChoice == cao,
            "Primary candidate click should preview without submitting.");
        vm.ConfirmGeneralChoiceCommand.Execute(null);
        Require(Program.Engine(vm).Revision > primaryRevision, "Primary candidate confirmation did not submit.");
        Program.AdvanceToDecision(vm);
        Require(vm.IsGeneralSelectionPending && vm.GeneralChoices.Any(general => general.HealthText == "组合体力 3") && vm.GeneralChoices.Any(general => general.HealthText == "组合体力 4"),
            $"Secondary candidates did not distinguish 3+4 and 4+4 combinations: {string.Join(", ", vm.GeneralChoices.Select(general => $"{general.Name}={general.HealthText}"))}.");
        Require(vm.GeneralSelectionSubtitle.Contains("曹操") && vm.GeneralSelectionFooter.Contains("暗置登场"), "Dual-general context or hidden-entry instructions are missing.");
        var selecting = Checkpoint(vm);
        Program.Render(root, 1120, 740, Path.Combine(output, "65-national-secondary-hp.png"));
        vm.SaveGameCommand.Execute(null);
        Require(!vm.HasSaveError, vm.SaveStatus);
        vm.StartTutorialCommand.Execute(null);
        Require(vm.GeneralChoices.All(choice => !choice.HasHealthPreview), "National health badges leaked into identity practice.");
        vm.ExitTutorialCommand.Execute(null);
        Require(Checkpoint(vm) == selecting && vm.GeneralChoices.Any(general => general.HealthText == "组合体力 3"), "Tutorial lost the second-general selection preview.");
        vm.LoadManualGameCommand.Execute(null);
        Require(!vm.HasSaveError && Checkpoint(vm) == selecting, "New content package did not restore a partial selection.");
        Program.Render(root, 1120, 740, Path.Combine(output, "65-national-secondary-hp.png"));
        var guo = vm.GeneralChoices.Single(general => general.GeneralId == "national:wei-guo-jia");
        Require(guo.HealthDescription.Contains("4+3") && guo.HealthDescription.Contains("向下取整"), "Pair preview does not explain rounding.");
        choiceButton = Program.Find<Button>(root).Single(button => ReferenceEquals(button.CommandParameter, guo));
        choiceButton.Command.Execute(guo);
        vm.ConfirmGeneralChoiceCommand.Execute(null);
        Program.AdvanceToDecision(vm);
        Require(vm.HumanPlayer is { Hp: 3, MaxHp: 3, HealthValue: "3/3" }, "Confirmed pair did not enter the table at 3/3.");
        Program.Render(root, 1120, 740, Path.Combine(output, "66-national-three-hp.png"));
        vm.SaveGameCommand.Execute(null);
        var threeHp = Checkpoint(vm);
        vm.LoadManualGameCommand.Execute(null);
        Require(!vm.HasSaveError && Checkpoint(vm) == threeHp && vm.HumanPlayer!.MaxHp == 3, "Three-health save changed its rule or maximum.");
        NationalExperienceChecks.Complete(vm);
        MatchSummaryChecks.VerifyCompleted(vm);

        var oldStore = new FileGameSaveStore(Path.Combine(output, "old-national", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.GetDirectoryName(oldStore.GetPath(GameSaveSlot.Manual))!);
        using (var resource = typeof(NationalHealthChecks).Assembly.GetManifestResourceStream("CardGame.Wpf.Tests.Fixtures.national-v7.json")!)
        using (var file = File.Create(oldStore.GetPath(GameSaveSlot.Manual))) resource.CopyTo(file);
        var oldFile = oldStore.Read(GameSaveSlot.Manual);
        using var legacy = new MainViewModel(false, 33, true, oldStore, useExpandedContent: true);
        legacy.LoadManualGameCommand.Execute(null);
        Require(!legacy.HasSaveError && Program.Engine(legacy).RulesVersion == 7 && Checkpoint(legacy) == GameCheckpointJson.Serialize(oldFile.Checkpoint),
            "Frozen shipped 1.0.0 save no longer restores with its exact original journal and fingerprint.");
        Require(legacy.Seats.All(seat => seat.MaxHp == 4) && legacy.NationalHealthRuleText.Contains("固定为 4"), "Legacy game silently adopted the new pair formula.");
        legacy.NewGameCommand.Execute(null);
        Require(legacy.NationalHealthRuleText.Contains("平均"), "New-game setup retained the loaded save's obsolete health explanation.");
        legacy.CancelNewGameSetupCommand.Execute(null);
        Require(legacy.NationalHealthRuleText.Contains("固定为 4"), "Canceling setup lost the old match's rule explanation.");
        NationalExperienceChecks.Complete(legacy);
        MatchSummaryChecks.VerifyCompleted(legacy);
        window.Content = null;
        window.Close();
    }
}
