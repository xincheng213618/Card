using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class IceSwordUiChecks
{
    public static void SequentialOpaqueChoices(string output)
    {
        var boundary = IceSwordScenario.FindHumanTrigger();
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
        var firstDiscards = viewModel.SkillChoices.Where(IsDiscard).ToArray();
        var retainDamage = viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "ice-sword-damage");
        var target = engine.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat);

        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsSkillSelectionPending &&
                       engine.PendingDecision is
                       {
                           Kind: DecisionKind.IceSword,
                           IsPrivate: true,
                           PlayerSeat: 0
                       } &&
                       firstDiscards.Length == target.Hand.Count + target.Equipment.Count &&
                       firstDiscards.Count(choice => choice.Cards.Count == 0) == target.Hand.Count &&
                       firstDiscards.Count(choice => choice.Cards.Count == 1) == target.Equipment.Count &&
                       retainDamage.Cards.Count == 0 &&
                       retainDamage.Targets.Count == 0,
            "The WPF must restore Ice Sword as opaque hand slots, public equipment and one damage alternative.");

        Program.Render(root, 1120, 740,
            Path.Combine(output, "117-ice-sword-first.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text =>
                           text.Contains("寒冰剑", StringComparison.Ordinal)) &&
                       visibleText.Any(text =>
                           text.Contains("防止", StringComparison.Ordinal)) &&
                       visibleText.Any(text =>
                           text.Contains("暗手牌", StringComparison.Ordinal)),
            "The first Ice Sword choice must explain damage prevention and opaque target hand slots.");

        var first = firstDiscards.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("target-zone") == "equipment") ??
                    firstDiscards[0];
        viewModel.SelectSkillChoiceCommand.Execute(first);
        var firstSubmitted = engine.AcceptedCommands.OfType<AnswerPromptCommand>().LastOrDefault();
        var secondDiscards = viewModel.SkillChoices.Where(IsDiscard).ToArray();
        Program.Assert(firstSubmitted?.Choice == first.Id &&
                       engine.PendingDecision is
                       {
                           Kind: DecisionKind.IceSword,
                           IsPrivate: true,
                           PlayerSeat: 0
                       } &&
                       viewModel.SkillChoices.All(choice =>
                           choice.Parameters.GetValueOrDefault("action") != "ice-sword-damage") &&
                       secondDiscards.Length == firstDiscards.Length - 1,
            "The first WPF discard must commit and refresh to a mandatory reduced second choice.");

        Program.Render(root, 1120, 740,
            Path.Combine(output, "118-ice-sword-second.png"));
        visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text =>
                           text.Contains("继续弃置", StringComparison.Ordinal)) &&
                       visibleText.Any(text =>
                           text.Contains("第二张", StringComparison.Ordinal)),
            "The second Ice Sword surface must make the mandatory sequential discard explicit.");

        var second = secondDiscards[0];
        viewModel.SelectSkillChoiceCommand.Execute(second);
        var resolved = engine.Events.Select(item => item.Payload)
            .OfType<IceSwordResolvedEvent>()
            .LastOrDefault();
        Program.Assert(resolved is { Used: true } &&
                       resolved.DiscardedCardIds.Count == 2 &&
                       !viewModel.IsSkillSelectionPending,
            "The second WPF choice must finish one typed two-card Ice Sword prevention.");

        window.Content = null;
        window.Close();
    }

    private static bool IsDiscard(PromptChoice choice) =>
        choice.Parameters.GetValueOrDefault("action") == "ice-sword-discard";
}
