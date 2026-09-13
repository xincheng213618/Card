using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TargetCardChecks
{
    public static void Controls(string output)
    {
        var (viewModel, action) = FindFixture();
        using (viewModel)
        {
            var engine = Program.Engine(viewModel);
            var targetSeat = action.TargetSeat ??
                throw new InvalidOperationException("Target-card fixture lost its target seat.");
            var targetBefore = engine.CreateSnapshot(targetSeat, revealAll: true)
                .Players.Single(player => player.Seat == targetSeat);

            viewModel.SelectCardCommand.Execute(viewModel.Hand.Single(card => card.Id == action.CardId));
            viewModel.SelectTargetCommand.Execute(viewModel.Seats.Single(seat => seat.Seat == targetSeat));
            Program.Assert(viewModel.CanConfirmSelected, "Target-card action did not reach the confirmation boundary.");
            viewModel.PlaySelectedCardCommand.Execute(null);
            Program.AdvanceToDecision(viewModel);

            Program.Assert(viewModel.IsTargetCardSelectionPending, "WPF did not expose the private target-card prompt.");
            var prompt = engine.PendingDecision ??
                throw new InvalidOperationException("Target-card fixture has no pending prompt.");
            Program.Assert(prompt.Kind == DecisionKind.SelectTargetCard, "Wrong WPF target-card prompt kind.");
            Program.Assert(prompt.IsPrivate && prompt.Choices.Count == targetBefore.HandCount,
                "Target-card prompt did not preserve its private count-only contract.");
            Program.Assert(viewModel.TargetCardChoices.Count == prompt.Choices.Count,
                "WPF target-card collection does not mirror the published choices.");
            Program.Assert(viewModel.TargetCardChoices.All(choice =>
                    choice.Cards.Count == 0 &&
                    choice.Targets.SequenceEqual([targetSeat]) &&
                    choice.Parameters.GetValueOrDefault("action") == "target-card-slot" &&
                    choice.Parameters.ContainsKey("slot-index") &&
                    !choice.Parameters.ContainsKey("card-id") &&
                    choice.Description.Contains("牌位", StringComparison.Ordinal)),
                "WPF exposed a target-card identity instead of an opaque slot.");

            var observerSeat = Enumerable.Range(0, engine.PlayerCount)
                .First(seat => seat != engine.State.HumanSeat);
            Program.Assert(engine.CreateSnapshot(observerSeat).PendingDecision is null,
                "An ordinary viewer received the source player's private target-card prompt.");

            var window = new MainWindow(viewModel);
            window.ApplyTemplate();
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, "target-card-selection.png"));
            var buttons = Program.Find<Button>(root)
                .Where(button => button.Command == viewModel.SelectTargetCardChoiceCommand &&
                                 button.Visibility == Visibility.Visible &&
                                 button.IsEnabled &&
                                 button.ActualWidth > 0 &&
                                 button.ActualHeight > 0)
                .ToArray();
            Program.Assert(buttons.Length == prompt.Choices.Count,
                "Every opaque target-card slot must be reachable as a visible button.");

            var revision = engine.Revision;
            viewModel.SelectTargetCardChoiceCommand.Execute(viewModel.TargetCardChoices.Last());
            Program.Assert(engine.Revision > revision && !viewModel.IsTargetCardSelectionPending,
                "Selecting an opaque target-card slot did not commit through the command boundary.");
            Program.Assert(engine.State.ProcessingCardCount == 0 && engine.ResolutionStack.Count == 0,
                "Target-card selection left an in-flight resolution behind.");
            Program.Assert(engine.Events.Select(item => item.Payload).Any(payload =>
                    payload is TargetCardDiscardedEvent or TargetCardTakenEvent),
                "Target-card selection did not publish its typed result.");
            Program.Assert(!viewModel.PromptText.Contains("未执行", StringComparison.Ordinal), viewModel.PromptText);
            Program.Render(root, 1120, 740, Path.Combine(output, "target-card-finished.png"));
            window.Content = null;
            window.Close();
        }
    }

    private static (MainViewModel ViewModel, LegalAction Action) FindFixture()
    {
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var viewModel = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: new MemorySaveStore())
            {
                IsMotionEnabled = false
            };
            viewModel.SelectGeneralChoiceCommand.Execute(viewModel.GeneralChoices[0]);
            Program.AdvanceToDecision(viewModel);
            var engine = Program.Engine(viewModel);
            var full = engine.CreateSnapshot(engine.State.HumanSeat, revealAll: true);
            var action = engine.GetHumanLegalActions().FirstOrDefault(candidate =>
                candidate.Kind is LegalActionKind.Dismantlement or LegalActionKind.Snatch &&
                candidate.CardId is not null &&
                candidate.TargetSeat is not null &&
                candidate.TargetCardId is null &&
                full.Players.Single(player => player.Seat == candidate.TargetSeat).HandCount > 0 &&
                full.Players.All(player => player.Hand.All(card => card.Kind != CardKind.Nullification)));
            if (action is not null)
            {
                return (viewModel, action);
            }

            viewModel.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic WPF target-card fixture.");
    }
}
