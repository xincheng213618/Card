using System.IO;
using System.Windows;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static partial class ShenwuActivityUiChecks
{
    public static void ExpandedVersionsTiersAndRestore(string output)
    {
        var store = new MemorySaveStore();
        using var vm = new MainViewModel(false, 17, showSetup: true, saveStore: store, useExpandedContent: true) { IsMotionEnabled = false };
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            vm.OpenLobbyCategoryCommand.Execute("activity");
            Program.Assert(vm.VisibleTableModes.Count(mode => mode.ModeId.StartsWith("activity:shenwu-2020-tier-")) == 5 &&
                vm.VisibleTableModes.Any(mode => mode.ModeId == "activity:shenwu-2017") &&
                vm.VisibleTableModes.All(mode => !mode.ModeId.StartsWith("activity:shenwu-2024")),
                "The lobby exposes the real five-tier 2020 expansion and preserves 2017 without guessed 2024 presets.");
            vm.SelectedTableMode = vm.VisibleTableModes.Single(mode => mode.ModeId == "activity:shenwu-2020-tier-5");
            vm.OpenModeConfigurationCommand.Execute(vm.VisibleModeGroups.Single(group => group.Variants.Any(mode => mode.ModeId == "activity:shenwu-2020-tier-5")));
            Program.Assert(vm.TeamModeSetupText.Contains("英招") && vm.TeamModeSetupText.Contains("困兽") &&
                vm.TeamModeSetupText.Contains("敌方5阶") && vm.CurrentGuideBody.Contains("重生") &&
                new[] { "xiangliu", "zhuyan", "bifang", "yingzhao" }.All(id => GeneralArt.GetPortrait($"shenwu2020:{id}") is ImageBrush { ImageSource: not null }),
                "New setup explains eight beasts, chosen enemy tier and distinct awakening/rebirth, with real four-beast artwork.");
            Program.Render(root, 1440, 880, Path.Combine(output, "shenwu-2020-tier-five-setup.png"));
            vm.StartNewGameFromLobbyCommand.Execute(null);
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            for (var step = 0; step < 24 && !vm.IsGeneralSelectionPending; step++) vm.StepAiCommand.Execute(null);
            var choice = vm.GeneralChoices.First(general => general.GeneralId == "classic:shen-guan-yu" ||
                general.GeneralId == "classic:shen-lu-meng" || general.GeneralId == "classic:shen-cao-cao" || general.GeneralId == "classic:shen-lu-bu" ||
                general.GeneralId == "ol:shen-zhao-yun");
            vm.PreviewGeneralChoiceCommand.Execute(choice);
            vm.ConfirmGeneralChoiceCommand.Execute(null);
            if (vm.RunToHumanCommand.CanExecute(null)) vm.RunToHumanCommand.Execute(null);
            var game = Program.Engine(vm);
            Program.Assert(game.ModeId == "activity:shenwu-2020-tier-5" && vm.Seats.Count == 6 &&
                vm.TableModeText.Contains("敌方5阶") && game.CreateSnapshot(0).Players.Where(player => player.TeamId == "team:beasts")
                    .All(player => player.Skills!.Any(skill => skill.ContentId == "shenwu:kunshou")),
                "Chosen tier is used by the actual six-seat table and every enemy has the fifth-tier awakening.");
            Program.Render(root, 1440, 880, Path.Combine(output, "shenwu-2020-tier-five-table.png"));
            vm.SaveGameCommand.Execute(null);
            var snapshot = SnapshotJson.Serialize(game.CreateSnapshot(0));
            vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "activity:shenwu-2017");
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && Program.Engine(vm).ModeId == "activity:shenwu-2020-tier-5" &&
                vm.SelectedTableMode.ModeId == "activity:shenwu-2020-tier-5" && SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) == snapshot,
                "Cross-version restore retains the saved tier, native grants and exact battle.");
        }
        finally
        {
            window.Content = null;
            window.DataContext = null;
            window.Close();
        }
    }
}
