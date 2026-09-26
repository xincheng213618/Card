using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class MengjinUiChecks
{
    public static void OpaqueTargetCardChoice(string output)
    {
        var boundary = MengjinScenario.FindHumanTrigger();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1, DateTimeOffset.UtcNow, false, boundary.Game.CreateCheckpoint()));
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
        var targetBefore = engine.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        var handChoices = viewModel.SkillChoices.Where(choice =>
            choice.Parameters.GetValueOrDefault("target-zone") == "hand").ToArray();
        var equipmentChoices = viewModel.SkillChoices.Where(choice =>
            choice.Parameters.GetValueOrDefault("target-zone") == "equipment").ToArray();

        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsSkillSelectionPending &&
                       engine.PendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } &&
                       handChoices.Length == targetBefore.Hand.Count &&
                       handChoices.All(choice => choice.Cards.Count == 0) &&
                       equipmentChoices.SelectMany(choice => choice.Cards)
                           .SequenceEqual(targetBefore.Equipment.Select(card => card.Id)),
            $"WPF must restore Mengjin with opaque hand slots and exact public equipment. " +
            $"saveError={viewModel.SaveStatus}; pending={engine.PendingDecision?.Kind}; choices={viewModel.SkillChoices.Count}.");

        Program.Render(root, 1120, 740, Path.Combine(output, "140-mengjin-choice.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text => text.Contains("猛进", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("暗手牌位", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("判定区", StringComparison.Ordinal)),
            "The Mengjin surface must explain hidden hand slots and that judgment cards are excluded.");

        var use = handChoices.First();
        var discardedId = targetBefore.Hand[int.Parse(
            use.Parameters["slot-index"], System.Globalization.CultureInfo.InvariantCulture)].Id;
        viewModel.SelectSkillChoiceCommand.Execute(use);
        var resolved = engine.Events.Select(item => item.Payload)
            .OfType<MengjinResolvedEvent>().LastOrDefault();
        var targetAfter = engine.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        Program.Assert(resolved is { Used: true } &&
                       resolved.DiscardedCardId == discardedId &&
                       targetAfter.Hand.All(card => card.Id != discardedId) &&
                       targetAfter.Hp == targetBefore.Hp &&
                       !viewModel.IsSkillSelectionPending,
            "The WPF Mengjin choice must discard the selected opaque hand slot and finish the canceled Slash.");

        window.Content = null;
        window.Close();
    }
}
