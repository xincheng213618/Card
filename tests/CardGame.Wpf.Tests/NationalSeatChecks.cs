using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.ViewModels;

internal static class NationalSeatChecks
{
    private static void Require(bool value, string message) => Program.Assert(value, message);
    private static string State(MainViewModel vm) => GameCheckpointJson.Serialize(Program.Engine(vm).CreateCheckpoint());

    private static MainViewModel Ready()
    {
        var vm = new MainViewModel(false, 721019, true, new MemorySaveStore(), useExpandedContent: true) { IsMotionEnabled = false };
        vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "national:lite-4");
        vm.StartNewGameCommand.Execute(null);
        for (var i = 0; i < 100 && !vm.CanEndTurn; i++) PersistenceChecks.Step(vm);
        Require(vm.CanEndTurn, "National portrait fixture failed to reach play.");
        return vm;
    }

    public static void PrivacyAndReveal()
    {
        using var vm = Ready();
        var before = State(vm);
        var own = Program.Engine(vm).CreateSnapshot(0, true).Players[0];
        var primary = GeneralSlotViewModel.FromPlayer(own, false);
        var secondary = GeneralSlotViewModel.FromPlayer(own, true);
        Require(primary.HasPortrait && secondary.HasPortrait && !primary.IsSkillEnabled && !secondary.IsSkillEnabled, "Own dark slots lost portraits or enabled skills.");
        var other = own with { IsHuman = false };
        foreach (var second in new[] { false, true })
        {
            var hidden = GeneralSlotViewModel.FromPlayer(other, second);
            Require(!hidden.IsKnown && !hidden.HasPortrait && !hidden.IsSkillEnabled && hidden.GeneralId.Length == 0 && hidden.Name == "暗将" &&
                !hidden.DetailText.Contains(own.GeneralName) && !hidden.DetailText.Contains(own.SecondaryGeneralName!), "Trusted input leaked an unrevealed general through its portrait or tooltip.");
        }
        other = other with { IsSecondaryGeneralPublic = true };
        Require(!GeneralSlotViewModel.FromPlayer(other, false).HasPortrait && GeneralSlotViewModel.FromPlayer(other, true) is { HasPortrait: true, IsSkillEnabled: true },
            "Revealing the secondary slot also exposed the primary or left the revealed skill disabled.");
        vm.IsDeveloperView = true;
        Require(vm.Seats.Where(seat => !seat.IsHuman).All(seat => seat.RelationshipLabel == "未明" && !seat.IsTeammate && !seat.PrimaryGeneral!.HasPortrait && !seat.SecondaryGeneral!.HasPortrait &&
            seat.Kingdom == "未明势力" && seat.SkillName == "未知 / 未知"),
            "Developer visibility assigned undisclosed allies or revealed portrait slots.");
        Require(State(vm) == before, "Portrait projection or developer toggling changed the game.");
    }


}
