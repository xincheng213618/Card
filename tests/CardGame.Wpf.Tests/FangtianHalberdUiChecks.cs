using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class FangtianHalberdUiChecks
{
    public static void ExactTargetCombination(string output)
    {
        var boundary = FangtianHalberdScenario.FindHumanLastHandSlash();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1,
            DateTimeOffset.UtcNow,
            false,
            boundary.BeforeSlash));
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: boundary.Game.Seed,
            showSetup: true,
            saveStore: store,
            useExpandedContent: true,
            contentRegistry: boundary.Registry)
        {
            IsMotionEnabled = false
        };
        viewModel.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var slash = viewModel.Hand.Single(card => card.Id == boundary.Slash.Id);

        Program.Assert(!viewModel.HasSaveError && slash.IsPlayable,
            "The WPF must restore the exact last-hand Slash with Fangtian equipped.");
        viewModel.SelectCardCommand.Execute(slash);
        var tripleChoices = viewModel.TargetCombinationChoices
            .Where(choice => choice.Targets.Count == 3)
            .ToArray();
        Program.Assert(viewModel.HasTargetCombinationChoices &&
                       viewModel.TargetCombinationChoices.Count == 10 &&
                       tripleChoices.Length == 4 &&
                       viewModel.TargetCombinationChoices.All(choice =>
                           choice.Cards.SequenceEqual([boundary.Slash.Id]) &&
                           choice.Targets.Count is 2 or 3 &&
                           choice.Targets.Distinct().Count() == choice.Targets.Count) &&
                       viewModel.CurrentGuideTitle.Contains("方天画戟", StringComparison.Ordinal) &&
                       viewModel.CurrentGuideBody.Contains("最后一张手牌", StringComparison.Ordinal),
            "Fangtian must render exact two/three-target buttons and explain the last-hand condition.");

        Program.Render(root, 1120, 740,
            Path.Combine(output, "120-fangtian-halberd-targets.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text => text.Contains("方天画戟", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("使用【杀】", StringComparison.Ordinal)),
            "The Fangtian target surface must name the weapon and the shared Slash use.");

        var choice = tripleChoices[0];
        var engine = Program.Engine(viewModel);
        var hpBefore = engine.CreateSnapshot(0, revealAll: true).Players
            .Where(player => choice.Targets.Contains(player.Seat))
            .ToDictionary(player => player.Seat, player => player.Hp);
        viewModel.SelectTargetCombinationChoiceCommand.Execute(choice);
        var submitted = engine.AcceptedCommands.OfType<PlayCardCommand>().LastOrDefault();
        var used = engine.Events.Select(item => item.Payload)
            .OfType<FangtianHalberdUsedEvent>()
            .LastOrDefault();
        var hpAfter = engine.CreateSnapshot(0, revealAll: true).Players
            .Where(player => choice.Targets.Contains(player.Seat))
            .ToDictionary(player => player.Seat, player => player.Hp);
        Program.Assert(submitted?.TargetSeats.SequenceEqual(choice.Targets) == true &&
                       used?.TargetSeats.SequenceEqual(choice.Targets) == true &&
                       choice.Targets.All(seat => hpAfter[seat] == hpBefore[seat] - 1) &&
                       !viewModel.HasSelection,
            "The Fangtian WPF button must submit and resolve the exact selected target combination.");

        window.Content = null;
        window.Close();
    }
}
