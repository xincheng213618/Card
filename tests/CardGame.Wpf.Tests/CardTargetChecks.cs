using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.ViewModels;

internal static class CardTargetChecks
{
    public static void DirectSelection(string output)
    {
        CheckChain(2, output);
        CheckChain(1, output);
        CheckConversion();
    }

    private static MainViewModel Create(int seed)
    {
        var vm = new MainViewModel(false, seed, showSetup: false, saveStore: new MemorySaveStore()) { IsMotionEnabled = false };
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        return vm;
    }

    private static void CheckChain(int count, string output)
    {
        using var vm = Create(721019);
        var engine = Program.Engine(vm);
        var action = engine.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.IronChain &&
            action.TargetSeats.Count == count);
        var cardId = action.CardId!.Value;
        var before = SnapshotJson.Serialize(engine.State);
        var commandsBefore = engine.CreateCheckpoint().Commands.Count;
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var confirm = (Button)window.FindName("PlayCardButton");
        try
        {
            vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == cardId));
            Require(vm.IsMultiTargetCardSelected && !vm.HasTargetCombinationChoices && !vm.CanConfirmSelected,
                "Iron Chain should start with an empty direct target draft, without the combination list.");
            Require(!Shortcut(window, Key.Enter) && SnapshotJson.Serialize(engine.State) == before, "Empty target confirmation used a card.");
            vm.SelectTargetCommand.Execute(vm.HumanPlayer!);
            Require(Selected(vm).SequenceEqual([engine.State.HumanSeat]), "Current Iron Chain rules did not allow the player as a target.");
            vm.SelectTargetCommand.Execute(vm.HumanPlayer!);
            foreach (var target in action.TargetSeats.Reverse())
                vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == target));
            Require(vm.CanConfirmSelected && Selected(vm).SequenceEqual(action.TargetSeats.Order()), "Seat clicks lost the chosen targets.");
            if (count == 2)
            {
                var extra = vm.Seats.First(seat => !action.TargetSeats.Contains(seat.Seat));
                Require(!extra.IsLegalTarget, "A third target still glows after selecting two.");
                vm.SelectTargetCommand.Execute(extra);
                Require(Selected(vm).Length == 2 && SnapshotJson.Serialize(engine.State) == before, "Third target changed the draft or engine.");
            }
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == action.TargetSeats[0]));
            Require(Selected(vm).Length == count - 1 && vm.CanConfirmSelected == (count == 2), "Target deselection has the wrong confirmation state.");
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == action.TargetSeats[0]));
            Shortcut(window, Key.F1);
            Require(vm.CurrentGuideTitle == "确认铁索连环的目标" && vm.CurrentGuideSteps.Any(step => step.Text.Contains("进入连环")),
                "Guide does not describe the selected targets and their actual chain changes.");
            Require(!Shortcut(window, Key.Enter) && SnapshotJson.Serialize(engine.State) == before, "Guide committed the hidden draft.");
            if (count == 2) Program.Render(root, 1120, 740, Path.Combine(output, "45-iron-chain-guide.png"));
            Shortcut(window, Key.Escape);
            vm.NewGameCommand.Execute(null);
            Shortcut(window, Key.Escape);
            Require(Selected(vm).Length == count, "Canceling setup lost the draft.");
            vm.StartTutorialCommand.Execute(null);
            Require(vm.IsTutorialActive && !vm.IsMultiTargetCardSelected, "Chain draft leaked into the tutorial.");
            vm.ExitTutorialCommand.Execute(null);
            Require(ReferenceEquals(engine, Program.Engine(vm)) && SnapshotJson.Serialize(engine.State) == before && Selected(vm).Length == count,
                "Tutorial did not restore the full target draft.");
            vm.SortHandCommand.Execute(null);
            Require(Selected(vm).Length == count && vm.CanConfirmSelected, "Hand sorting lost the target draft.");
            Shortcut(window, Key.Escape);
            Require(!vm.HasSelection && Selected(vm).Length == 0 && !vm.IsMultiTargetCardSelected, "Esc left stale card targets.");
            vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == cardId));
            foreach (var target in action.TargetSeats.Reverse()) vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == target));
            Program.Render(root, 1120, 740, Path.Combine(output, $"44-iron-chain-{count}-targets.png"));
            Require(confirm.IsEnabled && confirm.Visibility == Visibility.Visible, "The main confirmation button is inaccessible.");
            Require(Shortcut(window, Key.Enter), "Enter did not use Iron Chain.");
            var commands = engine.CreateCheckpoint().Commands;
            Require(commands.Count == commandsBefore + 1 && commands.Last() is PlayCardCommand played &&
                played.CardId == cardId && played.TargetSeats.SequenceEqual(action.TargetSeats), "Confirmation did not submit one canonical multi-target command.");
            Require(vm.Hand.All(card => card.Id != cardId) && Selected(vm).Length == 0 && !vm.HasSelection, "Committed targets or hand card remained selected.");
            for (var step = 0; step < 400 && !vm.CanEndTurn && !vm.HasGameOver; step++) PersistenceChecks.Step(vm);
            var resolved = engine.Events.Select(entry => entry.Payload).OfType<IronChainResolvedEvent>().LastOrDefault();
            Require(resolved is not null && resolved.TargetSeats.SequenceEqual(action.TargetSeats) &&
                vm.Seats.Where(seat => action.TargetSeats.Contains(seat.Seat)).All(seat => seat.IsChained),
                "The chosen targets did not reach the actual chained state.");
            if (count == 2) Program.Render(root, 1120, 740, Path.Combine(output, "46-iron-chain-resolved.png"));
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void CheckConversion()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            using var vm = new MainViewModel(false, seed, showSetup: false, saveStore: new MemorySaveStore());
            var general = vm.GeneralChoices.FirstOrDefault(choice => choice.SkillName == "武圣");
            if (general is null) continue;
            vm.SelectGeneralChoiceCommand.Execute(general);
            Program.AdvanceToDecision(vm);
            var engine = Program.Engine(vm);
            var chainIds = engine.GetHumanLegalActions().Where(action => action.Kind == LegalActionKind.IronChain).Select(action => action.CardId).ToHashSet();
            var conversion = engine.GetHumanLegalActions().FirstOrDefault(action => chainIds.Contains(action.CardId) && action.PlayedCardKind == CardKind.Slash);
            if (conversion is null) continue;
            var cardId = conversion.CardId!.Value;
            vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == cardId));
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == conversion.TargetSeats.Single()));
            Require(vm.CanPlaySelected && vm.HasAlternateSlash, "One legal target should offer both Iron Chain and Wusheng.");
            var second = vm.Seats.First(seat => seat.IsLegalTarget && !seat.IsSelectedTarget);
            vm.SelectTargetCommand.Execute(second);
            Require(vm.CanPlaySelected && !vm.CanPlaySelectedAsSlash, "Two targets must never enable converted Slash.");
            vm.PlaySelectedAsSlashCommand.Execute(null);
            Require(vm.Hand.Any(card => card.Id == cardId), "Invalid multi-target conversion used a card.");
            vm.SelectTargetCommand.Execute(second);
            vm.PlaySelectedAsSlashCommand.Execute(null);
            Require(engine.CreateCheckpoint().Commands.Last() is PlayCardCommand played && played.CardId == cardId &&
                played.PlayedCardKind == CardKind.Slash && played.TargetSeats.SequenceEqual(conversion.TargetSeats),
                "Explicit conversion did not keep the chosen physical card and single target.");
            Console.WriteLine($"  Direct Iron Chain: one/two targets resolved; Wusheng conversion seed {seed}.");
            return;
        }
        throw new InvalidOperationException("No real red Iron Chain/Wusheng opening found.");
    }

    private static int[] Selected(MainViewModel vm) => vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).Order().ToArray();
    private static bool Shortcut(MainWindow window, Key key) => (bool)typeof(MainWindow)
        .GetMethod("HandleShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [key, ModifierKeys.None])!;
    private static void Require(bool condition, string message) => Program.Assert(condition, message);
}
