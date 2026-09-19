using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class QilinBowUiChecks
{
    public static void ExactMountChoice(string output)
    {
        var boundary = QilinBowScenario.FindHumanTrigger();
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
        var discardChoices = viewModel.SkillChoices.Where(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qilin-bow-discard").ToArray();
        var skip = viewModel.SkillChoices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qilin-bow-skip");
        var hpBefore = engine.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat].Hp;

        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsSkillSelectionPending &&
                       engine.PendingDecision is
                       {
                           Kind: DecisionKind.QilinBow,
                           IsPrivate: true,
                           PlayerSeat: var playerSeat
                       } &&
                       playerSeat == boundary.SourceSeat &&
                       discardChoices.SelectMany(choice => choice.Cards)
                           .SequenceEqual(boundary.MountCardIds) &&
                       discardChoices.All(choice =>
                           choice.Cards.Count == 1 &&
                           choice.Targets.SequenceEqual([boundary.TargetSeat])) &&
                       skip is not null &&
                       skip.Cards.Count == 0 &&
                       skip.Targets.Count == 0,
            $"The WPF must restore Qilin Bow as exact public mount choices plus one skip action. " +
            $"saveError={viewModel.SaveStatus}; pending={engine.PendingDecision?.Kind}; choices={viewModel.SkillChoices.Count}.");

        Program.Render(root, 1120, 740,
            Path.Combine(output, "119-qilin-bow-choice.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text =>
                           text.Contains("麒麟弓", StringComparison.Ordinal)) &&
                       visibleText.Any(text =>
                           text.Contains("坐骑", StringComparison.Ordinal)) &&
                       visibleText.Any(text =>
                           text.Contains("继续", StringComparison.Ordinal)),
            "The Qilin Bow surface must explain its public mount choice and continued damage.");

        var use = discardChoices[0];
        var mountId = use.Cards.Single();
        viewModel.SelectSkillChoiceCommand.Execute(use);
        var submitted = engine.AcceptedCommands.OfType<AnswerPromptCommand>().LastOrDefault();
        var resolved = engine.Events.Select(item => item.Payload)
            .OfType<QilinBowResolvedEvent>()
            .LastOrDefault();
        var targetAfter = engine.CreateSnapshot(boundary.SourceSeat, revealAll: true)
            .Players[boundary.TargetSeat];
        Program.Assert(submitted?.Choice == use.Id &&
                       resolved is { Used: true } &&
                       resolved.DiscardedMountCardId == mountId &&
                       targetAfter.Equipment.All(card => card.Id != mountId) &&
                       targetAfter.Hp < hpBefore &&
                       !viewModel.IsSkillSelectionPending,
            "The WPF Qilin Bow choice must discard the selected mount and continue the Slash damage.");

        window.Content = null;
        window.Close();
    }
}
