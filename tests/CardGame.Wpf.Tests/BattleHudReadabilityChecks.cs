using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class BattleHudReadabilityChecks
{
    public static void NativeHudBindingAndBounds(string output)
    {
        using (var model = new MainViewModel(false, 721019, false, new MemorySaveStore(),
                   historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
               { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false })
        {
            StartLordFixture(model);
            model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
            AdvanceToDecision(model);
            var engine = Engine(model);
            var action = engine.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Slash &&
                item.CardId is > 0 && item.ConversionSource is null && model.Hand.Any(card => card.Id == item.CardId && card.Kind == CardKind.Slash));
            var accepted = engine.AcceptedCommands.Count;
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                foreach (var size in new[] { (1120, 740), (1920, 1080) })
                {
                    Render(root, size.Item1, size.Item2, Path.Combine(output, $"battle-hud-native-phase-{size.Item1}.png"));
                    AssertNativeBounds(window, model, expectSubtitle: true, expectFaces: false);
                }
                model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == action.CardId));
                model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == action.TargetSeat));
                model.ConfirmSelectedCommand.Execute(null);
                Assert(engine.AcceptedCommands.Count == accepted + 1,
                    "The HUD fixture must commit exactly one real Slash before measuring its public card row.");
                var views = SafeViews(engine);
                foreach (var size in new[] { (1120, 740), (1920, 1080) })
                {
                    Render(root, size.Item1, size.Item2, Path.Combine(output, $"battle-hud-native-public-face-{size.Item1}.png"));
                    AssertNativeBounds(window, model, expectSubtitle: true, expectFaces: true);
                    Assert(engine.AcceptedCommands.Count == accepted + 1 && SafeViews(engine).SequenceEqual(views),
                        "Measuring the actual phase HUD and public card row must not change the committed game boundary.");
                }
            }
            finally { window.Content = null; window.DataContext = null; window.Close(); }
        }

        // Reach the existing genuine optional Zhefu prompt through its physical
        // response. This covers a visible choice panel and a collapsed subtitle.
        var fixture = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, zhefu: true);
        using var response = fixture.Model;
        response.IsMotionEnabled = false; response.IsSoundEnabled = false; response.IsMusicEnabled = false;
        response.SelectCardCommand.Execute(response.Hand.First(card => card.IsPlayable));
        response.ConfirmSelectedCommand.Execute(null);
        for (var step = 0; step < 50 && Engine(response).PendingDecision?.SkillPrompt?.SkillId != "ol:zhefu"; step++)
        {
            Assert(response.CanStepAi, "The native response stopped before its actual optional Zhefu prompt.");
            response.StepAiCommand.Execute(null);
        }
        Assert(Engine(response).PendingDecision?.SkillPrompt?.SkillId == "ol:zhefu" && response.HasCenterChoices,
            "The existing response fixture must reach its real visible skill choices before testing the compact HUD.");
        var promptWindow = new MainWindow(response);
        var promptRoot = (FrameworkElement)promptWindow.Content;
        var promptEngine = Engine(response);
        var promptAccepted = promptEngine.AcceptedCommands.Count;
        var promptViews = SafeViews(promptEngine);
        try
        {
            foreach (var size in new[] { (1120, 740), (1920, 1080) })
            {
                Render(promptRoot, size.Item1, size.Item2, Path.Combine(output, $"battle-hud-native-prompt-{size.Item1}.png"));
                AssertNativeBounds(promptWindow, response, expectSubtitle: false, expectFaces: false);
                Assert(promptEngine.AcceptedCommands.Count == promptAccepted && SafeViews(promptEngine).SequenceEqual(promptViews),
                    "Compact prompt HUD measurement must preserve the real decision and every player-safe snapshot.");
            }
        }
        finally { promptWindow.Content = null; promptWindow.DataContext = null; promptWindow.Close(); }
        Console.WriteLine("Battle HUD: actual phase/prompt container binding, visible subtitle bounds, 1120/1920, public card row and existing decision protection preserved.");
    }

    private static void AssertNativeBounds(MainWindow window, MainViewModel model, bool expectSubtitle, bool expectFaces)
    {
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback");
        var hud = (FrameworkElement)window.FindName("BattlePhaseHud");
        var title = (TextBlock)window.FindName("DecisionTitleText");
        var subtitle = (TextBlock)window.FindName("CurrentPlayerText");
        var center = (FrameworkElement)window.FindName("FeedbackCenter");
        var decision = (FrameworkElement)window.FindName("DecisionPanel");
        var viewport = (FrameworkElement)window.FindName("TablePlaysViewport");
        Rect Bounds(FrameworkElement element) => element.TransformToVisual(layer).TransformBounds(new Rect(element.RenderSize));
        var area = Bounds(hud);
        var centerArea = Bounds(center);
        Assert(ReferenceEquals(layer.PhaseHud, hud) && ReferenceEquals(layer.CenterAnchor, center) &&
            !ReferenceEquals(layer.PhaseHud, layer.CenterAnchor) && !layer.IsHitTestVisible && !layer.Focusable,
            "Battle text must receive the actual compact phase HUD separately from the existing decision center, without intercepting input.");
        Assert(title.Text == model.TableDecisionTitle && title.Visibility == Visibility.Visible && area.Contains(Bounds(title)),
            "The protected HUD must contain the actual visible phase or response title.");
        Assert(model.IsTableIdle == expectSubtitle && subtitle.Visibility == (expectSubtitle ? Visibility.Visible : Visibility.Collapsed),
            "The current-player subtitle must continue following the actual table-idle contract.");
        var expectedHeight = title.ActualHeight + (expectSubtitle ? subtitle.Margin.Top + subtitle.ActualHeight : 0);
        Assert(Math.Abs(hud.ActualHeight - expectedHeight) < .1 && (!expectSubtitle || area.Contains(Bounds(subtitle))) &&
            area.Width > 0 && area.Height > 0 && area.Width * area.Height < centerArea.Width * centerArea.Height * .5,
            "HUD bounds must track only the rendered title and visible subtitle, rather than prohibiting the entire central battlefield.");
        Assert(layer.IsPromptVisible == model.HasCenterChoices &&
            decision.Visibility == (model.HasCenterChoices ? Visibility.Visible : Visibility.Collapsed),
            "Adding compact HUD protection must preserve the existing actual decision-panel and layer prompt visibility.");
        if (viewport.Visibility == Visibility.Visible)
            Assert(!area.IntersectsWith(Bounds(viewport)), "The compact phase HUD must remain separate from the public card viewport row.");
        if (expectFaces)
        {
            var faces = Find<Border>((FrameworkElement)window.Content).Where(face => BattleFeedbackLayer.GetPlayAnchor(face) >= 0).ToArray();
            Assert(faces.Length > 0 && faces.All(face => !area.IntersectsWith(Bounds(face))),
                "An actual settled public card face must stay outside the small phase HUD protection rectangle.");
        }
    }

    private static string[] SafeViews(GameEngine engine) => Enumerable.Range(0, engine.CreateSnapshot(0).Players.Count)
        .Select(seat => SnapshotJson.Serialize(engine.CreateSnapshot(seat))).ToArray();
}
