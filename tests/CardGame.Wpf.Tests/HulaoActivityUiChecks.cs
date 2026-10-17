using System.IO;
using System.Windows;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class HulaoActivityUiChecks
{
    public static void SetupFormCounterAndCrossModeRestore(string output)
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
            vm.SelectedTableMode = vm.VisibleTableModes.Single(mode => mode.ModeId == "activity:hulao-2022");
            Program.Assert(vm.IsActivityModeSelection && vm.IsTeamModeSelection &&
                vm.SelectedTableMode.PlayerCount == 4 && vm.StartingTeams.Count == 1 &&
                vm.SelectedStartingTeam.TeamId == "team:allies" && vm.SelectedStartingTeam.Name == "联合军",
                "Hulao setup must offer the four-seat coalition and two AI teammates.");
            Program.Assert(vm.TeamModeSetupText.Contains("两名人机") && vm.TeamModeSetupText.Contains("五阶") &&
                vm.TeamModeSetupText.Contains("第二次洗牌") && vm.TeamModeSetupText.Contains("武器") &&
                vm.CurrentGuideTitle.Contains("虎牢关") && vm.CurrentGuideBody.Contains("神吕布") &&
                !vm.SelectedTableMode.Name.Contains("首批") && !vm.TeamModeSetupText.Contains("首批") &&
                !vm.TeamModeSetupText.Contains("四关") && !vm.CurrentGuideBody.Contains("鬼将"),
                "Hulao setup and guide must explain its two forms and shuffle deadline without Ghost rules.");
            Program.Render(root, 1440, 880, Path.Combine(output, "156-hulao-setup.png"));

            vm.StartNewGameFromLobbyCommand.Execute(null);
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            for (var step = 0; step < 24 && !vm.IsGeneralSelectionPending; step++) vm.StepAiCommand.Execute(null);
            Program.Assert(vm.IsGeneralSelectionPending && vm.WindowTitle.Contains("虎牢关") &&
                vm.GeneralSelectionContextTitle.Contains("联合军") && vm.GeneralSelectionSubtitle.Contains("两名队友") &&
                vm.IdentityObjective.Contains("第二形态") && !vm.GeneralSelectionContextDetail.Contains("四关") &&
                vm.TableArenaTitle == "虎 牢 关",
                "Hulao general selection must retain its coalition, actual objective and arena title.");
            Program.Render(root, 1440, 880, Path.Combine(output, "157-hulao-selection.png"));
            vm.ConfirmGeneralChoiceCommand.Execute(null);
            if (vm.RunToHumanCommand.CanExecute(null)) vm.RunToHumanCommand.Execute(null);
            var snapshot = Program.Engine(vm).CreateSnapshot(0);
            Program.Assert(snapshot.Players.Count == 4 && snapshot.Players.Count(player => player.IsHuman) == 1 &&
                snapshot.Players.Count(player => player.TeamId == "team:allies") == 3 &&
                vm.Seats.Count(seat => seat.RoleLabel == "神吕布") == 1 &&
                vm.TableModeText.Contains("第 1/2 阶段") && vm.TableModeText.Contains("洗牌 0/2") &&
                vm.AliveText.Contains("联合军 3") && vm.AliveText.Contains("神吕布 1") &&
                !vm.TableModeText.Contains("关 ·") && !vm.AliveText.Contains("鬼军"),
                "The live table must show Hulao's form and shuffle counters and the actual boss label.");
            var boss = vm.Seats.Single(seat => seat.RoleLabel == "神吕布");
            var sharedPortrait = GeneralArt.GetSkin("classic:shen-lu-bu");
            Program.Assert(boss.HasPortrait && boss.Portrait.Image is not null && sharedPortrait is not null &&
                new[] { "hulao:lvbu-myth", "hulao:lvbu-rage", "hulao:lvbu-demon" }.All(id =>
                    GeneralArt.NormalizeKey(id) == "shen-lu-bu" &&
                    GeneralArt.GetSkin(id)?.LocalPath == sharedPortrait.LocalPath &&
                    new GeneralPortraitViewModel(id).Image is not null),
                "The actual Hulao boss and all three form aliases must load the existing attributed Shen Lu Bu portrait.");
            Program.Render(root, 1440, 880, Path.Combine(output, "158-hulao-table.png"));
            vm.SaveGameCommand.Execute(null);
            var saved = SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0));
            vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "team:standard-2v2");
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && Program.Engine(vm).ModeId == "activity:hulao-2022" &&
                vm.SelectedStartingTeam.TeamId == "team:allies" && vm.WindowTitle.Contains("虎牢关") &&
                vm.TableModeText.Contains("第 1/2 阶段") && vm.TableModeText.Contains("洗牌 0/2") &&
                SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) == saved,
                "Restoring Hulao from another selector must resolve its package, coalition and form counters.");
        }
        finally
        {
            window.Content = null;
            window.DataContext = null;
            window.Close();
        }
    }
}
