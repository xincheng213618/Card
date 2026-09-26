using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class QuhuUiChecks
{
    public static void WinningDamageTarget(string output)
    {
        var boundary = QuhuScenario.Find(sourceWins: true);
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
        var prompt = engine.PendingDecision;

        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsSkillSelectionPending &&
                       prompt is
                       {
                           Kind: DecisionKind.ProgramTrigger,
                           PlayerSeat: var playerSeat,
                           IsPrivate: true,
                           SkillPrompt.SkillId: "classic:quhu"
                       } &&
                       playerSeat == boundary.SourceSeat &&
                       viewModel.SkillChoices.Count == prompt.ValidTargetSeats.Count &&
                       viewModel.SkillChoices.All(choice =>
                           choice.Targets.Count == 1 &&
                           choice.Targets[0] != boundary.OpponentSeat),
            $"WPF must restore the mandatory winning Quhu damage-target choice. " +
            $"saveError={viewModel.SaveStatus}; pending={prompt?.Kind}; choices={viewModel.SkillChoices.Count}.");

        Program.Render(root, 1120, 740, Path.Combine(output, "141-quhu-damage-target.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text => text.Contains("驱虎", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("攻击范围", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("伤害", StringComparison.Ordinal)),
            "The Quhu surface must explain its mandatory in-range attributed damage choice.");

        var choice = viewModel.SkillChoices[0];
        var victim = choice.Targets.Single();
        var hpBefore = engine.CreateSnapshot(boundary.SourceSeat, revealAll: true).Players[victim].Hp;
        viewModel.SelectSkillChoiceCommand.Execute(choice);
        var after = engine.CreateSnapshot(boundary.SourceSeat, revealAll: true);
        Program.Assert(after.Players[victim].Hp == hpBefore - 1 &&
                       engine.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Any(item =>
                           item.SourceSeat == boundary.OpponentSeat && item.TargetSeat == victim),
            "The WPF Quhu target choice must attribute one normal damage to the Pindian opponent.");

        window.Content = null;
        window.Close();
    }
}
