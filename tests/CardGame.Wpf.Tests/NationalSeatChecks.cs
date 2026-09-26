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

    public static void MultiSkillProjection()
    {
        using var vm = Ready();
        var own = Program.Engine(vm).CreateSnapshot(0, true).Players[0] with
        {
            Skills =
            [
                new GeneralSkillDefinition("咆哮", "出牌阶段使用杀没有次数限制。"),
                new GeneralSkillDefinition("武圣", "红色牌可当作杀使用。")
            ],
            SecondarySkills =
            [
                new GeneralSkillDefinition("青囊", "弃置手牌令受伤角色回复。")
                { ContentId = "classic:qingnang", ActionForms = SkillActionForm.Active },
                new GeneralSkillDefinition("急救", "回合外红色牌可当桃。")
            ],
            IsSecondaryGeneralPublic = true
        };
        var primary = GeneralSlotViewModel.FromPlayer(own, false);
        var secondary = GeneralSlotViewModel.FromPlayer(own, true);
        Require(primary.SkillName == "咆哮 / 武圣" &&
                primary.DetailText.Contains("咆哮：", StringComparison.Ordinal) &&
                primary.DetailText.Contains("武圣：", StringComparison.Ordinal) &&
                !primary.IsSkillEnabled,
            "The hidden primary slot did not retain its ordered private multi-skill presentation.");
        Require(secondary.SkillName == "青囊 / 急救" &&
                secondary.DetailText.Contains("青囊：", StringComparison.Ordinal) &&
                secondary.DetailText.Contains("急救：", StringComparison.Ordinal) &&
                secondary.IsSkillEnabled,
            "The revealed secondary slot did not present and enable both skills.");

        var empty = GeneralSlotViewModel.FromPlayer(own with { Skills = [] }, false);
        var unavailable = GeneralSlotViewModel.FromPlayer(own with { Skills = null }, false);
        Require(empty.SkillName == "无" && empty.SkillStateText == "此将没有技能" &&
                unavailable.SkillName == "未知技能" && unavailable.SkillStateText == "技能信息未公开",
            "An empty visible skill collection was confused with unavailable private skill data.");

        var hidden = GeneralSlotViewModel.FromPlayer(own with
        {
            IsHuman = false,
            IsSecondaryGeneralPublic = false
        }, true);
        Require(!hidden.IsKnown && !hidden.DetailText.Contains("青囊", StringComparison.Ordinal) &&
                !hidden.DetailText.Contains("急救", StringComparison.Ordinal),
            "A trusted snapshot leaked an unrevealed secondary multi-skill list through WPF.");
    }

    public static void ControlsAndRelations(string output)
    {
        using var vm = Ready();
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "67-dual-hidden.png"));
        Require(Program.Find<Grid>(root).Count(grid => grid.Name == "DualPortraits" && grid.Visibility == Visibility.Visible && grid.ActualWidth > 0) == 4,
            "National seats did not render two portrait slots.");
        Require(Program.Find<TextBlock>(root).Count(text => text.Name == "SlotRevealState" && text.Text.EndsWith("暗置")) == 8, "The table does not expose each slot's independent reveal state.");
        CheckRelations(vm);
        var checkpoint = State(vm);
        var action = Program.Engine(vm).GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash);
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == action.CardId));
        var target = vm.Seats.Single(seat => seat.Seat == action.TargetSeat);
        var targetButton = Program.Find<Button>(root).Single(button => ReferenceEquals(button.CommandParameter, target));
        targetButton.Command.Execute(targetButton.CommandParameter);
        Require(target.IsSelectedTarget && vm.CanConfirmSelected && State(vm) == checkpoint, "Dual-portrait seat buttons broke target selection or submitted prematurely.");
        vm.OpenContextGuideCommand.Execute(null);
        vm.ToggleHelpCommand.Execute(null);
        Require(vm.Seats.Single(seat => seat.Seat == target.Seat).IsSelectedTarget && State(vm) == checkpoint, "Returning from guidance lost the selected dual seat.");
        vm.ClearSelectionCommand.Execute(null);
        vm.RevealNationalGeneralCommand.Execute(vm.NationalRevealChoices.Single(choice => choice.Slot == GeneralSelectionSlot.Secondary));
        Program.AdvanceToDecision(vm);
        Require(vm.HumanPlayer is { PrimaryGeneral.IsRevealed: false, SecondaryGeneral.IsRevealed: true }, "Secondary-first reveal updated the wrong visual slot.");
        vm.SaveGameCommand.Execute(null);
        var half = State(vm);
        vm.LoadManualGameCommand.Execute(null);
        Require(!vm.HasSaveError && State(vm) == half && vm.HumanPlayer is { PrimaryGeneral.IsRevealed: false, SecondaryGeneral.IsRevealed: true }, "Saved half-reveal did not restore its portrait states.");
        Program.Render(root, 1120, 740, Path.Combine(output, "68-dual-secondary-revealed.png"));
        var sawAlly = false;
        var sawOpponent = false;
        for (var step = 0; step < 1000 && !vm.HasGameOver && !(sawAlly && sawOpponent); step++)
        {
            PersistenceChecks.Step(vm);
            CheckRelations(vm);
            sawAlly |= vm.Seats.Any(seat => seat.RelationshipLabel == "同伴");
            sawOpponent |= vm.Seats.Any(seat => seat.RelationshipLabel == "对手");
        }
        Require(sawAlly && sawOpponent, "Live reveal progression never exposed both teammate and opponent markers.");
        Program.Render(root, 1120, 740, Path.Combine(output, "69-dual-known-camps.png"));
        Program.Render(root, 1440, 860, Path.Combine(output, "70-dual-wide.png"));
        Program.Render(root, 1120, 740, Path.Combine(output, "69-dual-known-camps.png"));
        window.Content = null;
        window.Close();
    }

    private static void CheckRelations(MainViewModel vm)
    {
        var view = Program.Engine(vm).CreateSnapshot(0);
        var ownFaction = view.Players[0].FactionId;
        foreach (var player in view.Players)
        {
            var seat = vm.Seats.Single(seat => seat.Seat == player.Seat);
            var expected = player.IsHuman ? "自己" : !player.IsFactionRevealed ? "未明" : player.FactionId == ownFaction ? "同伴" : "对手";
            Require(seat.RelationshipLabel == expected && seat.IsTeammate == (expected == "同伴"), "Seat relationship does not follow publicly revealed faction information.");
            Require(seat.PrimaryGeneral!.IsRevealed == player.IsGeneralPublic && seat.SecondaryGeneral!.IsRevealed == player.IsSecondaryGeneralPublic,
                "Live portrait state drifted from the engine reveal flags.");
            if (!player.IsHuman)
                Require(seat.PrimaryGeneral.IsKnown == player.IsGeneralPublic && seat.SecondaryGeneral!.IsKnown == player.IsSecondaryGeneralPublic,
                    "An opponent's unknown slot gained a name before revelation.");
        }
    }
}
