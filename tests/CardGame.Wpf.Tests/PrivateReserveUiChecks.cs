using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PrivateReserveUiChecks
{
    public static void ExchangePeekAndNatureBadges(string output)
    {
        var registry = PrivateReserveScenario.Registry();
        var game = PrivateReserveScenario.Start(registry);
        var store = new MemorySaveStore();
        Save();
        using var model = new MainViewModel(false, 19, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsAutoAdvance = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            Render("qixing-exchange", 1440, 860);
            var overlay = (Border)window.FindName("PrivateReserveOverlay");
            var starsRow = (ItemsControl)window.FindName("ReserveStarsRow");
            var handRow = (ItemsControl)window.FindName("ReserveHandRow");
            var confirm = (Button)window.FindName("ConfirmReserveExchangeButton");
            Program.Assert(model.IsReserveDialogOpen && model.IsReserveExchange && starsRow.Items.Count == 7 && handRow.Items.Count == 4 &&
                !confirm.IsEnabled && starsRow.TranslatePoint(new Point(), root).Y < handRow.TranslatePoint(new Point(), root).Y,
                "The native initial exchange opens seven stars above the owner's four hand cards, with confirmation disabled until an equal exchange.");
            var revision = game.Revision;
            var inventory = SnapshotJson.Serialize(game.CreateSnapshot(0));
            var retired = model.ReserveDialogHand[0];
            var star = model.ReserveDialogStars[0];
            model.MoveReserveCardCommand.Execute(retired);
            model.MoveReserveCardCommand.Execute(retired with { });
            Program.Assert(model.ReserveDialogStars.Contains(retired) && !model.CanConfirmReserveExchange,
                "A hand face moves into the star row without paying or confirming an unequal draft.");
            model.MoveReserveCardCommand.Execute(star);
            Program.Assert(model.ReserveDialogHand.Contains(star) && model.CanConfirmReserveExchange && game.Revision == revision &&
                SnapshotJson.Serialize(game.CreateSnapshot(0)) == inventory, "An equal local preview keeps every physical card in its original zone until confirmation.");
            model.MoveReserveCardCommand.Execute(star);
            Program.Assert(!model.CanConfirmReserveExchange && model.ReserveDialogStars.Contains(star), "Moving a previewed star back reverses the draft.");
            model.MoveReserveCardCommand.Execute(star);
            Render("qixing-exchange-preview", 1120, 780);
            Program.Assert(confirm.IsEnabled && overlay.ActualWidth <= 1120 && starsRow.ActualHeight > 130,
                "The compact real overlay retains complete faces, two rows and an enabled native confirmation button.");
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            model.MoveReserveCardCommand.Execute(retired);
            Program.Assert(!model.MoveReserveCardCommand.CanExecute(retired) && !model.CanConfirmReserveExchange,
                "Loading the same private prompt retires all old draft parameters and clears their preview.");
            var noExchangeMoves = game.CardMovements.Count;
            model.CloseReserveDialogCommand.Execute(null);
            Program.Assert(!model.IsReserveDialogOpen && game.CreateSnapshot(0).Players[0].PrivateReserveCount == 7 &&
                game.CardMovements.Count == noExchangeMoves, "Cancelling the offered exchange closes it without moving stars or hand cards.");

            Reach(PrivateReserveScenario.IsQixingOffer);
            Program.Assert(model.IsReserveDialogOpen && model.IsReserveExchange, "The actual normal draw opens the same exchange interface.");
            var handIds = model.ReserveDialogHand.Take(2).Select(card => card.Card.Id).ToArray();
            var starIds = model.ReserveDialogStars.Take(2).Select(card => card.Card.Id).ToArray();
            foreach (var id in handIds.Concat(starIds))
                model.MoveReserveCardCommand.Execute(model.ReserveDialogHand.Concat(model.ReserveDialogStars).Single(card => card.Card.Id == id));
            model.ConfirmReserveExchangeCommand.Execute(null);
            var owner = game.CreateSnapshot(0).Players[0];
            Program.Assert(!model.IsReserveDialogOpen && owner.PrivateReserveCount == 7 &&
                handIds.All(id => owner.PrivateReserveCards!.Any(card => card.Id == id)) &&
                starIds.All(id => owner.Hand.Any(card => card.Id == id)), "Confirm submits exactly the previewed equal physical exchange through the existing command session.");
            var afterExchange = game.Revision;
            model.ConfirmReserveExchangeCommand.Execute(null);
            Program.Assert(game.Revision == afterExchange, "Repeated confirmation cannot exchange or pay a second time.");
            var badge = Program.Find<Button>(root).Single(button => button.Name == "PrivateReserveBadge" && button.DataContext is SeatViewModel { Seat: 0 });
            Program.Assert((string)badge.Content == "七星[7]" && badge.IsEnabled, "The owner's portrait displays its clickable seven-star pile badge.");
            badge.Command!.Execute(badge.CommandParameter);
            Render("qixing-pile-peek", 1440, 860);
            Program.Assert(model.IsReserveDialogOpen && !model.IsReserveExchange && model.ReserveDialogStars.Count == 7 &&
                model.ReserveDialogHand.Count == 0 && Program.Find<Button>(starsRow).All(button => !button.IsEnabled) && game.Revision == afterExchange,
                "Clicking the portrait pile opens only its owner's read-only star faces and does not advance rules.");
            typeof(MainWindow).GetMethod("HandleShortcut", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, [System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.None]);
            Program.Assert(!model.IsReserveDialogOpen && model.ReserveDialogStars.Count == 0, "The read-only peek accepts Enter to close and withdraws its private faces.");

            Reach(g => g.PendingDecision?.Choices.Any(c => c.Parameters.ContainsKey("order-group")) == true);
            Render("qixing-ending-order", 1120, 780);
            model.SelectSkillChoiceCommand.Execute(model.CenterSkillChoices.Single(c => c.Parameters.GetValueOrDefault("skill-id") == PrivateReserveScenario.Mist && c.Parameters["program-action"] == "activate"));
            PayAndTarget(1);
            model.SelectSkillChoiceCommand.Execute(model.CenterSkillChoices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            PayAndTarget(2);
            Render("qixing-wind-mist", 1440, 860);
            Program.Assert(model.Seats.Single(seat => seat.Seat == 1).HasMist && model.Seats.Single(seat => seat.Seat == 2).HasGale &&
                VisibleBadge("MistBadge", 1) && VisibleBadge("GaleBadge", 2) && model.HumanPlayer!.PrivateReserveCount == 5,
                "The real ending selections leave five stars and draw separate mist/wind badges beside the chosen portraits.");
            Reach(g => g.State.CurrentSeat == 0 && g.State.TurnNumber > 1 && g.State.Phase == TurnPhase.Play);
            Render("qixing-nature-expired", 1120, 780);
            Program.Assert(model.Seats.All(seat => !seat.HasGale && !seat.HasMist) && !VisibleBadge("MistBadge", 1) && !VisibleBadge("GaleBadge", 2),
                "The native next-turn expiry removes both portrait badges.");
            VerifyResumedExchange();

            bool VisibleBadge(string name, int seat) => Program.Find<Border>(root).Any(border => border.Name == name &&
                border.DataContext is SeatViewModel current && current.Seat == seat && border.Visibility == Visibility.Visible);
            void PayAndTarget(int target)
            {
                model.SelectSkillChoiceCommand.Execute(model.CenterSkillChoices.First(choice => choice.Cards.Count == 1));
                if (model.CenterSkillChoices.SingleOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") is { } finish)
                    model.SelectSkillChoiceCommand.Execute(finish);
                model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == target));
                model.ConfirmSelectedCommand.Execute(null);
            }
            void VerifyResumedExchange()
            {
                game = PrivateReserveScenario.Start(registry);
                PrivateReserveScenario.Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters["program-action"] == "activate"));
                var paid = game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1);
                PrivateReserveScenario.Answer(game, paid);
                Save();
                model.LoadManualGameCommand.Execute(null);
                game = Program.Engine(model);
                Program.Assert(model.IsReserveExchange && model.ReserveDialogStars.Any(card => card.Card.Id == paid.Cards[0] && card.IsCommitted),
                    "A replayed older one-card selection restores its committed draft without permitting the completed choice to be toggled off.");
                model.MoveReserveCardCommand.Execute(model.ReserveDialogStars.First(card => card.WasReserve));
                model.ConfirmReserveExchangeCommand.Execute(null);
                Program.Assert(!model.IsReserveDialogOpen && game.CreateSnapshot(0).Players[0].PrivateReserveCount == 7,
                    "A restored partial exchange finishes once through the same UI and keeps the private pile size.");
                game = PrivateReserveScenario.Start(registry);
                PrivateReserveScenario.Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters["program-action"] == "activate"));
                var fullHand = game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).ToArray();
                foreach (var id in fullHand)
                    PrivateReserveScenario.Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Cards.SequenceEqual([id])));
                PrivateReserveScenario.Answer(game, game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1));
                Save();
                model.LoadManualGameCommand.Execute(null);
                game = Program.Engine(model);
                Program.Assert(model.IsReserveExchange && !model.CanConfirmReserveExchange &&
                    model.ReserveDialogStars.Count(card => !card.WasReserve && card.IsCommitted) == fullHand.Length &&
                    model.ReserveDialogHand.Count(card => card.WasReserve && card.IsCommitted) == 1,
                    "A checkpoint in the star-selection stage preserves both previously committed sets and the exact remaining count.");
                foreach (var card in model.ReserveDialogStars.Where(card => card.WasReserve).Take(fullHand.Length - 1).ToArray())
                    model.MoveReserveCardCommand.Execute(card);
                model.ConfirmReserveExchangeCommand.Execute(null);
                Program.Assert(!model.IsReserveDialogOpen && game.CreateSnapshot(0).Players[0].PrivateReserveCount == 7 &&
                    fullHand.All(id => game.CreateSnapshot(0).Players[0].PrivateReserveCards!.Any(card => card.Id == id)),
                    "The resumed final stage moves the originally committed whole hand once and pays no selected star twice.");
            }
            void Reach(Func<GameEngine, bool> condition)
            {
                PrivateReserveScenario.Reach(game, condition);
                // The fixture drives Core directly; present its returned private prompt as the UI command adapter does.
                typeof(MainViewModel).GetMethod("Refresh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(model, [game.CreateSnapshot(0)]);
            }
        }
        finally { window.Content = null; window.Close(); }

        void Save() => store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        void Render(string name, int width, int height) => Program.Render(root, width, height, Path.Combine(output, name + ".png"));
    }
}
