using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ActiveEffectUiChecks
{
    public static void LiveEffectsRestoreAndExpire(string output)
    {
        var (game, registry) = ActiveEffectScenario.Luoyi();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, 17, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            var luoyi = model.HumanPlayer!.StatusBadges.Single(status => status.Label == "裸衣");
            Program.Assert(luoyi.Tooltip.Contains("伤害+1") && luoyi.Tooltip.Contains("下个回合开始") && !luoyi.HasCount,
                "Cold loading must display real Luoyi as a duration effect, without claiming only one remaining attack.");
            var snapshot = game.CreateSnapshot(0);
            Refresh(snapshot with { Players = snapshot.Players.Select(player => player.Seat == 1 ? player with
            {
                ActiveEffects = [new("fixture:one-use", "巧说", "下一张基本牌可调整目标。", 1, ActivePlayerEffectExpiry.ConsumedOrTurnEnd, 1),
                    new("fixture:one-use", "巧说", "额外目标无距离限制。", 1, ActivePlayerEffectExpiry.ConsumedOrTurnEnd, 1),
                    new("fixture:duration", "时效", "持续到来源角色的下回合。", 1, ActivePlayerEffectExpiry.NextSourceTurnStart,
                        SourceSeatDisplayNumber: 4)]
            } : player).ToArray() });
            var oneUse = model.Seats.Single(seat => seat.Seat == 1).StatusBadges.Single(status => status.Label == "巧说");
            Program.Assert(oneUse is { Count: 1, HasCount: true } && oneUse.Tooltip.Contains("消耗后移除") && oneUse.Tooltip.Contains("额外目标"),
                "Effects from one skill must share one readable badge, retain their details and show one actual use instead of adding independent descriptions as uses.");
            Program.Assert(model.Seats.Single(seat => seat.Seat == 1).StatusBadges.Single(status => status.Label == "时效").Tooltip.Contains("第4席下个回合"),
                "Duration tooltips must use the table's displayed seat number rather than the internal zero-based seat.");
            foreach (var width in new[] { 1120, 1440 })
            {
                Program.Render(root, width, 860, Path.Combine(output, $"active-effects-{width}.png"));
                var local = (ItemsControl)window.FindName("HumanStatusBadges");
                Program.Assert(local.Visibility == Visibility.Visible && local.Items.Count == 1 &&
                    Program.Find<Grid>(local).Any(grid => grid.DataContext is SeatStatusViewModel { Label: "裸衣" }),
                    "Actual local XAML must render the restored live duration effect in the hand-dock status area.");
            }
            Refresh(snapshot);
            Program.Assert(model.Seats.Single(seat => seat.Seat == 1).StatusBadges.All(status => status.Label != "巧说"),
                "A consumed or cleared one-use fact must retire the old badge immediately.");
            var turn = game.State.TurnNumber;
            ActiveEffectScenario.Accept(game, new EndPlayPhaseCommand(0, game.Revision, ActiveEffectScenario.Prompt(game)!.PromptId));
            ActiveEffectScenario.Reach(game, () => game.State.TurnNumber > turn && game.State.CurrentSeat == 0 &&
                game.CreateSnapshot(0).Players[0].ActiveEffects?.Any(item => item.SkillId == "boundary:luoyi") != true);
            Refresh(game.CreateSnapshot(0));
            Program.Render(root, 1120, 860, null);
            Program.Assert(model.HumanPlayer!.StatusBadges.All(status => status.Label != "裸衣") &&
                ((ItemsControl)window.FindName("HumanStatusBadges")).Visibility == Visibility.Collapsed,
                "The next real owner turn must remove the previously loaded Luoyi badge before any new activation.");
        }
        finally { window.Content = null; window.Close(); }

        void Refresh(GameSnapshot snapshot) => typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [snapshot]);
    }
}
