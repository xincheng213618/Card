using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class LastZoneConversionUiChecks
{
    public static void RestoredJudgmentMaterialUsesExistingConversionControls(string output)
    {
        var (game, registry) = LastZoneConversionScenario.Create("judgment");
        var id = LastZoneConversionScenario.PlaceJudgment(game);
        var native = game.GetHumanLegalActions().First(a => a.CardId == id && a.Kind == LegalActionKind.Slash &&
            a.ConversionSource?.BindingId == "last-judgment-slash");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true,
            contentRegistry: registry) { IsMotionEnabled = false, IsSoundEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError && !ReferenceEquals(game, Program.Engine(model)),
            "The existing manual save loader must restore the real Judgment material and current configured source.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            var chooseCard = model.EquipmentPlayChoices.Single(c => c.Cards.SequenceEqual([id]) &&
                c.Parameters.GetValueOrDefault("action") == "select-judgment-play-card");
            Program.Assert(chooseCard.Description.Contains("判定牌") && !model.Hand.Any(c => c.Id == id),
                "The real Judgment entity appears in the shared conversion controls with its correct public region, outside the Hand surface.");
            Program.Render(root, 1120, 740, Path.Combine(output, "last-zone-judgment-before-reload.png"));
            var retiredButton = Program.Find<Button>(root).Single(button =>
                ReferenceEquals(button.Command, model.SelectEquipmentPlayChoiceCommand) && ReferenceEquals(button.CommandParameter, chooseCard));
            var retiredCommand = retiredButton.Command!;
            var retiredParameter = retiredButton.CommandParameter;
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError && !model.HasSelection, "Reload must clear the Judgment conversion draft.");
            var restored = Program.Engine(model);
            var state = SnapshotJson.Serialize(restored.CreateSnapshot(0));
            var revision = restored.Revision;
            var commands = restored.AcceptedCommands.Count;
            var movements = restored.CardMovements.Count;
            var text = model.SelectedCardText;
            retiredCommand.Execute(retiredParameter);
            Program.Assert(!model.HasSelection && model.SelectedCardText == text && restored.Revision == revision &&
                restored.AcceptedCommands.Count == commands && restored.CardMovements.Count == movements &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0)) == state,
                "A retired Judgment button recreated the loaded regional-card draft.");
            var currentCard = model.EquipmentPlayChoices.Single(c => c.Id == chooseCard.Id);
            Program.Assert(!ReferenceEquals(currentCard, chooseCard), "Reload must publish a new Judgment choice with the same ID.");
            model.SelectEquipmentPlayChoiceCommand.Execute(currentCard with { });
            Program.Assert(!model.HasSelection && model.SelectedCardText == text && restored.Revision == revision &&
                restored.AcceptedCommands.Count == commands && restored.CardMovements.Count == movements &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0)) == state,
                "An equal copied regional choice must not select the Judgment material.");
            chooseCard = currentCard;
            model.SelectEquipmentPlayChoiceCommand.Execute(chooseCard);
            var selectedText = model.SelectedCardText;
            model.SelectEquipmentPlayChoiceCommand.Execute(chooseCard);
            Program.Assert(model.HasSelection && model.SelectedCardText == selectedText &&
                model.EquipmentPlayChoices.Single(c => c.Id == chooseCard.Id).Description.StartsWith("✓", StringComparison.Ordinal),
                "A regional choice retired by selection must not cancel the current regional material.");
            model.SelectEquipmentPlayChoiceCommand.Execute(model.EquipmentPlayChoices.Single(c => c.Id == chooseCard.Id));
            Program.Assert(!model.HasSelection && model.EquipmentPlayChoices.All(c =>
                c.Parameters.GetValueOrDefault("action") != "select-card-conversion"),
                "The current regional button must still toggle off and withdraw its source choices.");
            model.SelectEquipmentPlayChoiceCommand.Execute(model.EquipmentPlayChoices.Single(c => c.Id == chooseCard.Id));
            var chooseSource = model.EquipmentPlayChoices.Single(c => c.Cards.SequenceEqual([id]) &&
                c.Parameters.GetValueOrDefault("conversion-skill-id") == native.ConversionSource!.SkillId &&
                c.Parameters.GetValueOrDefault("conversion-binding-id") == native.ConversionSource.BindingId &&
                c.Parameters.GetValueOrDefault("conversion-instance-id") == native.ConversionSource.SkillInstanceId);
            model.SelectEquipmentPlayChoiceCommand.Execute(chooseSource);
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == native.TargetSeat));
            Program.Assert(model.CanPlaySelectedAsSlash, "The existing conversion source and target controls enable exactly this actual Judgment Slash.");
            var cancelledCard = model.EquipmentPlayChoices.Single(c => c.Id == chooseCard.Id);
            var cancelledSource = model.EquipmentPlayChoices.Single(c => c.Id == chooseSource.Id);
            model.CancelActionCommand.Execute(null);
            Program.Assert(!model.HasSelection && model.EquipmentPlayChoices.All(c => !c.Description.StartsWith("✓", StringComparison.Ordinal) &&
                c.Parameters.GetValueOrDefault("action") != "select-card-conversion"),
                "Cancelling a regional conversion must rebuild the card labels and withdraw its source choices.");
            model.SelectEquipmentPlayChoiceCommand.Execute(cancelledCard);
            Program.Assert(!model.HasSelection, "The cancelled regional parameter must not rebuild a draft.");
            model.SelectEquipmentPlayChoiceCommand.Execute(cancelledSource);
            Program.Assert(!model.HasSelection && restored.Revision == revision && restored.AcceptedCommands.Count == commands &&
                restored.CardMovements.Count == movements && SnapshotJson.Serialize(restored.CreateSnapshot(0)) == state,
                "The cancelled regional source must not rebuild a draft or pay its material.");
            model.SelectEquipmentPlayChoiceCommand.Execute(model.EquipmentPlayChoices.Single(c => c.Id == chooseCard.Id));
            model.SelectEquipmentPlayChoiceCommand.Execute(model.EquipmentPlayChoices.Single(c => c.Id == chooseSource.Id));
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == native.TargetSeat));
            Program.Assert(model.CanPlaySelectedAsSlash, "Current regional choices must remain playable after cancelling and reselecting.");
            VerifyJudgmentAdvice();
            Program.Render(root, 1120, 740, Path.Combine(output, "last-zone-judgment-ready.png"));
            model.PlaySelectedAsSlashCommand.Execute(null);
            var paid = Program.Engine(model);
            var publicUse = paid.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Last(e => e.CardId == id);
            var publicPlay = model.RecentPlays.Single(play => play.PublicCardId == id);
            Program.Assert(publicUse is { CardKind: CardKind.Slash, PublicCard.Kind: CardKind.Lightning } &&
                publicPlay.Kind == CardKind.Slash && publicPlay.PublicCard == publicUse.PublicCard &&
                publicPlay.Rank == publicUse.PublicCard.RankText && !string.IsNullOrEmpty(publicPlay.SuitGlyph),
                "The real Judgment-to-Slash public face mixed its effective illustration with a fabricated printed suit or rank.");
            Program.Render(root, 1120, 740, Path.Combine(output, "card-identity-converted-judgment.png"));
            Program.Assert(paid.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Count(e =>
                e.Action.Type == CardActionType.Use && e.Action.EffectiveKind == CardKind.Slash &&
                e.Action.PhysicalCards is [var card] && card.CardId == id && card.CardKind == CardKind.Lightning &&
                card.From == CardLocation.Judgment(0) && e.Action.ConversionChain.SequenceEqual([native.ConversionSource!])) == 1 &&
                paid.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Judgment(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1,
                "The visible central controls submit one real native Slash with the exact printed delayed entity and Judgment payment claim.");
            for (var step = 0; step < 200; step++)
            {
                var prompt = Program.Engine(model).CreateSnapshot(0).PendingDecision;
                if (prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) break;
                var child = model.SkillChoices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                if (child is not null) model.SelectSkillChoiceCommand.Execute(child);
                else if (model.CanStepAi) model.StepAiCommand.Execute(null);
                else throw new InvalidOperationException("The actual central Judgment Slash lost its native completion controls.");
            }
            paid = Program.Engine(model);
            Program.Assert(!model.EquipmentPlayChoices.Any(c => c.Cards.Contains(id)) && !model.Hand.Any(c => c.Id == id),
                "The paid Judgment material retires from the central selector and never becomes a fabricated Hand card.");
            Program.Assert(paid.Events.Count(e => e.Payload is CardUseFinishedEvent f && f.CardId == id && f.CardKind == CardKind.Slash) == 1 &&
                paid.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1,
                "The UI's actual Slash and exact completion child finish once after the regional material was paid.");
            Program.Render(root, 1120, 740, Path.Combine(output, "last-zone-judgment-paid.png"));

            void VerifyJudgmentAdvice()
            {
                var before = Views();
                var selected = model.SelectedCardText;
                var target = model.Seats.Single(s => s.IsSelectedTarget).Seat;
                var actions = restored.GetHumanLegalActions();
                model.OpenContextGuideCommand.Execute(null);
                Program.Render(root, 1120, 740, Path.Combine(output, "last-zone-judgment-advice-before.png"));
                var adviceButton = Program.Find<Button>(root).Single(button => button.Name == "PlayAdviceButton");
                Program.Assert(adviceButton.Visibility == Visibility.Visible && adviceButton.IsEnabled &&
                    adviceButton.Command!.CanExecute(adviceButton.CommandParameter),
                    "The real Judgment advice fixture must activate an available bound button.");
                Console.WriteLine($"Judgment advice: material={id}, printed={restored.CreateSnapshot(0).Players[0].Judgment.Single().Kind}, actions={actions.Count}.");
                typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(adviceButton, null);
                Program.Assert(model.HasPlayAdvice && model.CurrentPlayAdvice?.Revision == revision,
                    "Advice must score the complete current action list, including the real Judgment conversion.");
                var material = restored.CreateSnapshot(0).Players[0].Judgment.Single();
                var advice = PlayAdvisor.Recommend(restored.CreateSnapshot(0), [native], []);
                var suit = material.Suit switch { Suit.Spade => "♠", Suit.Heart => "♥", Suit.Club => "♣", _ => "♦" };
                Program.Assert(advice?.Action == native.Description && advice.Selection.Contains($"【{material.DisplayName}】{suit}{material.RankText}") &&
                    advice.Selection.Contains("当作【杀】") && !model.Hand.Any(c => c.Id == id),
                    "A selected Judgment recommendation must describe its printed Lightning face and effective Slash without fabricating Hand material.");
                Program.Render(root, 1120, 740, Path.Combine(output, "last-zone-judgment-advice.png"));
                model.ToggleHelpCommand.Execute(null);
                Program.Assert(!model.IsHelpOpen && model.CanPlaySelectedAsSlash && model.SelectedCardText == selected &&
                    model.Seats.Single(s => s.IsSelectedTarget).Seat == target && ReferenceEquals(restored, Program.Engine(model)) &&
                    restored.Revision == revision && restored.AcceptedCommands.Count == commands && restored.CardMovements.Count == movements &&
                    Views().SequenceEqual(before) && System.Text.Json.JsonSerializer.Serialize(restored.GetHumanLegalActions()) == System.Text.Json.JsonSerializer.Serialize(actions),
                    "Opening and closing actual advice must preserve every player view, all legal actions and the payable Judgment draft.");

                string[] Views() => Enumerable.Range(0, restored.CreateSnapshot(0).Players.Count)
                    .Select(seat => SnapshotJson.Serialize(restored.CreateSnapshot(seat))).ToArray();
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }
}
