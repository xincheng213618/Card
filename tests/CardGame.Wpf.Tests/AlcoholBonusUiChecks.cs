using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.ViewModels;

internal static class AlcoholBonusUiChecks
{
    public static void PublicStackedBonusFallbackAndClearing(string output)
    {
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore(), useExpandedContent: true)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsNewGameSetupOpen = false };
        var engine = Program.Engine(model);
        var original = engine.CreateSnapshot(0);
        var checkpoint = JsonSerializer.Serialize(engine.CreateCheckpoint());
        GameSnapshot Project(bool hasEffect, int? bonus) => original with
        {
            PendingDecision = null,
            Players = original.Players.Select(p => p.Seat is 0 or 1
                ? p with { HasAlcoholEffect = hasEffect, AlcoholDamageBonus = bonus } : p).ToArray()
        };
        void Refresh(GameSnapshot snapshot) => typeof(MainViewModel)
            .GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [snapshot]);
        Refresh(Project(true, 2));
        Program.Assert(model.HumanSummary.Contains("下一张杀伤害+2") &&
            model.Seats.Where(s => s.Seat is 0 or 1).All(s => s.HandText.Contains("酒效+2") &&
                s.DeferredPileText.Contains("酒效+2") && s.DeferredPileTooltip.Contains("伤害+2")),
            "Both the local summary and public seats show the actual stacked bonus.");
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            Program.Render(root, 1120, 780, Path.Combine(output, "alcohol-stacked-bonus.png"));
            Program.Assert(Program.Find<TextBlock>(root).Any(t => t.Text.Contains("酒效+2") &&
                t.Visibility == Visibility.Visible && t.ActualWidth > 0 && t.ActualHeight > 0),
                "The actual WPF seat binding visibly renders the public bonus.");
            Refresh(Project(true, null));
            Program.Assert(model.HumanSummary.Contains("下一张杀伤害+1") &&
                model.Seats.Where(s => s.Seat is 0 or 1).All(s => s.HandText.Contains("酒效+1") &&
                    s.DeferredPileText.Contains("酒效+1")),
                "Existing Boolean-only snapshots keep their normal one-point bonus.");
            Refresh(Project(false, null));
            Program.Assert(!model.HumanSummary.Contains("酒效") && model.Seats.All(s =>
                !s.HandText.Contains("酒效") && !s.DeferredPileText.Contains("酒效") && !s.DeferredPileTooltip.Contains("伤害+")) &&
                JsonSerializer.Serialize(engine.CreateCheckpoint()) == checkpoint,
                "Consumption or turn-end removal clears stale labels without advancing the match.");
        }
        finally
        {
            Refresh(original);
            window.Content = null;
            window.DataContext = null;
            window.Close();
        }
    }
}
