using System.Windows;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class IdentityDualGeneralUiChecks
{
    public static void SelectionAndTable(string output)
    {
        using var vm = new MainViewModel(autoAdvance: false, seed: 31, showSetup: true,
            saveStore: new MemorySaveStore(), useExpandedContent: true) { IsMotionEnabled = false };
        var mode = vm.TableModes.Single(mode => mode.ModeId == "identity:classic-dual-8");
        vm.SelectedTableMode = mode;
        vm.SelectedStartingRole = vm.StartingRoles.Single(role => role.Role == Role.Lord);
        vm.StartNewGameCommand.Execute(null);
        vm.ContinueFromIdentityRevealCommand.Execute(null);
        ReachSelection();
        Program.Assert(vm.GeneralSelectionContextDetail.Contains("选择主将") && vm.GeneralSelectionSubtitle.Contains("全员双将"),
            "The new identity entry must explain the primary and deputy selection without national-war reveal rules.");
        vm.ToggleGeneralRosterCommand.Execute(null);
        var primary = vm.CustomGeneralChoices.Single(choice => choice.GeneralId == "classic:zhang-fei");
        vm.CustomGeneralSearch = "张飞";
        vm.SelectGeneralChoiceCommand.Execute(primary);
        ReachSelection();
        Program.Assert(vm.GeneralSelectionContextDetail.Contains("选择副将") && vm.CustomGeneralSearch.Length == 0,
            "The second prompt must identify the deputy and retire the previous slot's search.");
        vm.ToggleGeneralRosterCommand.Execute(null);
        var deputy = vm.CustomGeneralChoices.Single(choice => choice.GeneralId == "classic:da-qiao");
        Program.Assert(!vm.CustomGeneralChoices.Any(choice => choice.GeneralId == primary.GeneralId) &&
            deputy.HealthText.Contains("4") && deputy.HealthDescription.Contains("主公 +1"),
            "Deputy previews must show the floored combination health plus the Lord bonus, across factions.");
        vm.PreviewGeneralChoiceCommand.Execute(deputy);
        using var host = new NativeResponseStatusChecks.NativeHost(vm);
        host.Capture(output, "identity-dual-deputy-selection", 1120);
        var revision = Program.Engine(vm).Revision;
        vm.SelectGeneralChoiceCommand.Execute(primary);
        Program.Assert(Program.Engine(vm).Revision == revision, "A retired primary choice must not answer the deputy prompt.");
        vm.SaveGameCommand.Execute(null);
        vm.LoadManualGameCommand.Execute(null);
        vm.SelectGeneralChoiceCommand.Execute(vm.CustomGeneralChoices.Single(choice => choice.GeneralId == deputy.GeneralId));
        for (var step = 0; step < 40 && !Program.Engine(vm).Events.Any(e => e.Payload is SetupCompletedEvent); step++)
            vm.StepAiCommand.Execute(null);
        Program.Assert(Program.Engine(vm).Events.Any(e => e.Payload is SetupCompletedEvent), "Every AI must finish both selections.");
        Program.Assert(vm.Seats.Count == 8 && vm.Seats.All(seat => !seat.IsNationalSeat && seat.HasSecondaryGeneral &&
            seat.PrimaryGeneral is { IsRevealed: true } && seat.SecondaryGeneral is { IsRevealed: true }),
            "All identity seats must render both public portraits while retaining identity badges.");
        Program.Assert(vm.HumanPlayer!.MaxHp == 4 && vm.WindowTitle.Contains("双将") && vm.TableModeText.Contains("双将") &&
            vm.HumanSkillCards.Any(skill => skill.SourceText.StartsWith("主将")) &&
            vm.HumanSkillCards.Any(skill => skill.SourceText.StartsWith("副将")) &&
            vm.HumanSkillCards.All(skill => !skill.IsDisabled), "The table must publish both enabled skill sources and the combination health.");
        host.Capture(output, "identity-dual-table", 1120);
        host.Capture(output, "identity-dual-table", 1920);
        var seats = Program.Find<FrameworkElement>(host.Root).Where(element => element.Name == "DualPortraits" && element.IsVisible).ToArray();
        Program.Assert(seats.Length > 0, "The actual seat template must display the dual portrait surface.");

        void ReachSelection()
        {
            for (var step = 0; step < 24 && !vm.IsGeneralSelectionPending; step++) vm.StepAiCommand.Execute(null);
            Program.Assert(vm.IsGeneralSelectionPending, "The UI must reach the next human general prompt.");
        }
    }
}
