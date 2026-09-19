using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class CixiongDoubleSwordsUiChecks
{
    public static void ActivationChoice(string output)
    {
        var boundary = CixiongDoubleSwordsScenario.FindHumanTrigger();
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
        var activationChoices = viewModel.SkillChoices.Where(choice =>
            choice.Parameters.GetValueOrDefault("action") is "cixiong-use" or "cixiong-skip").ToArray();

        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsSkillSelectionPending &&
                       engine.PendingDecision is
                       {
                           Kind: DecisionKind.CixiongDoubleSwords,
                           IsPrivate: true,
                           PlayerSeat: 0
                       } &&
                       activationChoices.Length == 2 &&
                       activationChoices.All(choice =>
                           choice.Cards.Count == 0 && choice.Targets.Count == 0),
            "The WPF must restore Cixiong Double Swords as one private activate-or-skip choice.");

        Program.Render(root, 1120, 740,
            Path.Combine(output, "115-cixiong-activation.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text =>
                           text.Contains("雌雄双股剑", StringComparison.Ordinal)) &&
                       visibleText.Any(text =>
                           text.Contains("弃一张手牌", StringComparison.Ordinal)) &&
                       visibleText.Any(text =>
                           text.Contains("摸一张牌", StringComparison.Ordinal)),
            "The central Cixiong choice must explain both target resource alternatives.");

        var use = activationChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "cixiong-use");
        viewModel.SelectSkillChoiceCommand.Execute(use);
        var submitted = engine.AcceptedCommands.OfType<AnswerPromptCommand>().LastOrDefault();
        var targetPrompt = engine.CreateSnapshot(boundary.TargetSeat).PendingDecision;
        var targetHandIds = engine.CreateSnapshot(boundary.TargetSeat, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat).Hand
            .Select(card => card.Id)
            .Order()
            .ToArray();
        Program.Assert(submitted?.Choice == use.Id &&
                       targetPrompt is
                       {
                           Kind: DecisionKind.CixiongDoubleSwords,
                           IsPrivate: true
                       } &&
                       targetPrompt.PlayerSeat == boundary.TargetSeat &&
                       targetPrompt.ValidCardIds.Order().SequenceEqual(targetHandIds) &&
                       targetPrompt.Choices.Count(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "cixiong-discard") == targetHandIds.Length &&
                       targetPrompt.Choices.Count(choice =>
                           choice.Parameters.GetValueOrDefault("action") == "cixiong-draw") == 1,
            "The WPF activation must commit the typed choice and open the exact private target alternatives.");

        var advanced = engine.Submit(new AdvanceOneStepCommand(engine.Revision));
        var resolved = engine.Events.Select(item => item.Payload)
            .OfType<CixiongDoubleSwordsResolvedEvent>()
            .LastOrDefault();
        Program.Assert(advanced.Accepted && resolved is { Activated: true },
            advanced.Error?.Message ??
            "The WPF Cixiong activation did not continue through the target's staged choice.");

        window.Content = null;
        window.Close();
    }
}
