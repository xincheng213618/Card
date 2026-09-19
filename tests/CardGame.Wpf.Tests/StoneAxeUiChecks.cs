using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class StoneAxeUiChecks
{
    public static void ExactCostResponse(string output)
    {
        var boundary = StoneAxeScenario.FindHumanTrigger();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1,
            DateTimeOffset.UtcNow,
            false,
            boundary.Game.CreateCheckpoint()));
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: boundary.Game.Seed,
            showSetup: true,
            saveStore: store,
            useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        viewModel.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var engine = Program.Engine(viewModel);
        var prompt = engine.PendingDecision ??
            throw new InvalidOperationException("The restored Stone Axe WPF fixture lost its prompt.");
        var candidateIds = engine.CreateSnapshot(0, revealAll: true).Players[0]
            .Hand.Concat(engine.CreateSnapshot(0, revealAll: true).Players[0].Equipment)
            .Select(card => card.Id)
            .ToArray();
        var useChoices = viewModel.SkillChoices.Where(choice =>
            choice.Parameters.GetValueOrDefault("action") == "stone-axe-use").ToArray();

        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsSkillSelectionPending &&
                       prompt.Kind == DecisionKind.StoneAxe &&
                       prompt.IsPrivate &&
                       useChoices.Length == candidateIds.Length * (candidateIds.Length - 1) / 2 &&
                       useChoices.All(choice =>
                           choice.Cards.Count == 2 &&
                           choice.Cards.Distinct().Count() == 2 &&
                           choice.Cards.All(candidateIds.Contains)) &&
                       viewModel.SkillChoices.Count(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "stone-axe-skip") == 1,
            "The WPF must restore the private Stone Axe prompt as exact two-card choices plus skip.");

        Program.Render(root, 1120, 740,
            Path.Combine(output, "112-stone-axe-response.png"));
        var visibleText = Program.Find<TextBlock>(root)
            .Select(text => text.Text)
            .ToArray();
        Program.Assert(visibleText.Any(text =>
                           text.Contains("贯石斧", StringComparison.Ordinal)) &&
                       visibleText.Any(text =>
                           text.Contains("手牌", StringComparison.Ordinal)) &&
                       visibleText.Any(text =>
                           text.Contains("装备区", StringComparison.Ordinal)),
            "The central Stone Axe response did not identify the effect and both eligible card zones.");

        var useStoneAxe = useChoices.First(choice =>
            choice.Cards.Contains(boundary.StoneAxeCardId));
        viewModel.SelectSkillChoiceCommand.Execute(useStoneAxe);
        var resolved = engine.Events.Select(item => item.Payload)
            .OfType<StoneAxeResolvedEvent>()
            .LastOrDefault();
        Program.Assert(resolved is { Used: true } &&
                       resolved.DiscardedCardIds.SequenceEqual(useStoneAxe.Cards) &&
                       useStoneAxe.Cards.All(cardId => engine.CardMovements.Any(movement =>
                           movement.CardId == cardId &&
                           movement.To == CardLocation.DiscardPile &&
                           movement.Reason == CardMoveReasons.StoneAxeDiscard)) &&
                       engine.CardMovements.Any(movement =>
                           movement.CardId == boundary.StoneAxeCardId &&
                           movement.From == CardLocation.Equipment(0) &&
                           movement.To == CardLocation.Processing &&
                           movement.Reason == CardMoveReasons.StoneAxeDiscard),
            "The WPF Stone Axe choice did not commit its exact hand/equipment pair.");

        window.Content = null;
        window.Close();
    }
}
