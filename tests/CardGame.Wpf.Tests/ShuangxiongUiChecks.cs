using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ShuangxiongUiChecks
{
    public static void DrawChoice(string output)
    {
        var game = Find();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var viewModel = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true) { IsMotionEnabled = false };
        viewModel.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var prompt = Program.Engine(viewModel).PendingDecision;
        Program.Assert(!viewModel.HasSaveError && viewModel.IsSkillSelectionPending &&
                       prompt is { Kind: DecisionKind.Shuangxiong, IsPrivate: true } &&
                       viewModel.SkillChoices.Count == 2,
            "WPF must restore the private Shuangxiong draw replacement choice.");
        Program.Render(root, 1120, 740, Path.Combine(output, "142-shuangxiong-draw-choice.png"));
        var text = Program.Find<TextBlock>(root).Select(item => item.Text).ToArray();
        Program.Assert(text.Any(item => item.Contains("双雄", StringComparison.Ordinal)) &&
                       text.Any(item => item.Contains("判定", StringComparison.Ordinal)),
            "The Shuangxiong surface must explain its judgment-based draw replacement.");
        window.Content = null;
        window.Close();
    }

    private static GameEngine Find()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 46, 0));
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            var selection = game.PendingDecision;
            if (selection is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } ||
                !selection.ValidContentIds.Contains("classic:yan-liang-wen-chou")) continue;
            if (!game.Submit(new SelectGeneralCommand(0, "classic:yan-liang-wen-chou", game.Revision, selection.PromptId)).Accepted) continue;
            for (var step = 0; step < 32 && game.PendingDecision?.Kind != DecisionKind.Shuangxiong; step++)
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            if (game.PendingDecision?.Kind == DecisionKind.Shuangxiong) return game;
        }
        throw new InvalidOperationException("No bounded Shuangxiong WPF fixture was found.");
    }
}
