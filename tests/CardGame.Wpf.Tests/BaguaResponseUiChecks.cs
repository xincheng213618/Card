using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class BaguaResponseUiChecks
{
    public static void ArmorChoicePrecedesJudgmentAndHandDodge(string output)
    {
        foreach (var activate in new[] { false, true })
        {
            var (game, registry) = BaguaResponseScenario.Create();
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
                saveStore: store, useExpandedContent: false, contentRegistry: registry) { IsMotionEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            var engine = Program.Engine(model);
            var hp = engine.CreateSnapshot(0).Players[0].Hp;
            var window = new MainWindow(model);
            try
            {
                Program.Assert(!model.HasSaveError && model.Hand.All(card => !card.IsPlayable) &&
                    model.CanConfirmSelected && model.PlayButtonText == "发动八卦" && model.ActionDockCancelText == "不发动" &&
                    model.HandResponseExtraChoices.Count == 0 && model.CurrentDecisionContext!.Description.Contains("是否发动【八卦阵】"),
                    "Bagua must first show its explicit activate/decline controls and hold hand Dodge selection.");
                Program.Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, $"bagua-before-{activate}.png"));
                var confirm = (Button)window.FindName("PlayCardButton");
                var cancel = (Button)window.FindName("CancelActionButton");
                Program.Assert(confirm.Visibility == Visibility.Visible && confirm.IsEnabled &&
                    cancel.Visibility == Visibility.Visible && cancel.IsEnabled,
                    "Both real Bagua decision buttons must be visible and enabled before any card selection.");
                if (activate) model.ConfirmSelectedCommand.Execute(null);
                else model.CancelActionCommand.Execute(null);
                if (activate)
                {
                    Program.Assert(engine.Events.Select(item => item.Payload).OfType<JudgmentRequestedEvent>()
                        .Any(item => item.Reason == JudgmentReasons.BaguaDefense), "Activation must issue the actual judgment before hand response.");
                    for (var step = 0; step < 16 && !model.Hand.Any(card => card.IsPlayable); step++) model.StepAiCommand.Execute(null);
                }
                Program.Assert(engine.PendingDecision is { Kind: DecisionKind.RespondDodge } &&
                    engine.CreateSnapshot(0).Players[0].Hp == hp && model.Hand.Any(card => card.IsPlayable) &&
                    model.PlayButtonText == "打出闪" && !model.CanConfirmSelected && model.ActionDockCancelText == "放 弃",
                    "Declining or failing Bagua must enable hand Dodge with a fresh ordinary response and no automatic damage.");
                Program.Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, $"bagua-after-{activate}.png"));
                var card = model.Hand.First(item => item.IsPlayable);
                model.SelectCardCommand.Execute(card);
                Program.Assert(model.CanConfirmSelected, "The continued hand Dodge must require explicit selection.");
                model.ConfirmSelectedCommand.Execute(null);
                Program.Assert(engine.CreateSnapshot(0).Players[0].Hp == hp &&
                    engine.CreateCardZoneDiagnostics().Single(item => item.CardId == card.Id).Location == CardLocation.DiscardPile,
                    "Confirming the continued hand Dodge must pay its actual card and prevent damage.");
            }
            finally { window.Close(); }
        }
    }
}
