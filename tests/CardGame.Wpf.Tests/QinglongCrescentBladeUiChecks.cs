using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class QinglongCrescentBladeUiChecks
{
    public static void ExactFollowupChoice(string output)
    {
        var boundary = QinglongCrescentBladeScenario.FindHumanTrigger();
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
        var followups = viewModel.SkillChoices.Where(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qinglong-slash").ToArray();
        var skip = viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qinglong-skip");

        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsSkillSelectionPending &&
                       engine.PendingDecision is
                       {
                           Kind: DecisionKind.QinglongCrescentBlade,
                           IsPrivate: true,
                           PlayerSeat: 0
                       } prompt &&
                       followups.Length > 0 &&
                       followups.All(choice =>
                           choice.Cards.Count == 1 &&
                           choice.Targets.SequenceEqual([boundary.TargetSeat])) &&
                       skip.Cards.Count == 0 &&
                       prompt.ValidTargetSeats.SequenceEqual([boundary.TargetSeat]),
            "The WPF must restore Qinglong as private exact same-target Slash choices plus skip.");

        Program.Render(root, 1120, 740,
            Path.Combine(output, "116-qinglong-followup.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text =>
                           text.Contains("青龙偃月刀", StringComparison.Ordinal)) &&
                       visibleText.Any(text =>
                           text.Contains("同一目标", StringComparison.Ordinal)) &&
                       visibleText.Any(text =>
                           text.Contains("再使用一张", StringComparison.Ordinal)),
            "The central Qinglong choice must explain the same-target follow-up Slash.");

        var use = followups[0];
        viewModel.SelectSkillChoiceCommand.Execute(use);
        var submitted = engine.AcceptedCommands.OfType<AnswerPromptCommand>().LastOrDefault();
        var resolved = engine.Events.Select(item => item.Payload)
            .OfType<QinglongCrescentBladeResolvedEvent>()
            .LastOrDefault();
        var newSlash = engine.Events.Select(item => item.Payload)
            .OfType<CardUsedEvent>()
            .LastOrDefault(item => item.CardId == use.Cards.Single());
        Program.Assert(submitted?.Choice == use.Id &&
                       resolved is { Used: true } &&
                       resolved.SlashCardIds.SequenceEqual(use.Cards) &&
                       resolved.TargetSeat == boundary.TargetSeat &&
                       newSlash?.TargetSeat == boundary.TargetSeat,
            "The WPF Qinglong action must submit the typed choice and open the exact new Slash.");

        window.Content = null;
        window.Close();
    }
}
