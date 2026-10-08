using System.IO;
using System.Windows;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
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
            model.SelectEquipmentPlayChoiceCommand.Execute(chooseCard);
            var chooseSource = model.EquipmentPlayChoices.Single(c => c.Cards.SequenceEqual([id]) &&
                c.Parameters.GetValueOrDefault("conversion-skill-id") == native.ConversionSource!.SkillId &&
                c.Parameters.GetValueOrDefault("conversion-binding-id") == native.ConversionSource.BindingId &&
                c.Parameters.GetValueOrDefault("conversion-instance-id") == native.ConversionSource.SkillInstanceId);
            model.SelectEquipmentPlayChoiceCommand.Execute(chooseSource);
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == native.TargetSeat));
            Program.Assert(model.CanPlaySelectedAsSlash, "The existing conversion source and target controls enable exactly this actual Judgment Slash.");
            Program.Render(root, 1120, 740, Path.Combine(output, "last-zone-judgment-ready.png"));
            model.PlaySelectedAsSlashCommand.Execute(null);
            var paid = Program.Engine(model);
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
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }
}
