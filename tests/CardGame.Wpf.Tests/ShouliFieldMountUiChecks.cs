using System.IO;
using System.Windows;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ShouliFieldMountUiChecks
{
    public static void ForeignMountUsesPublishedConversion(string output)
    {
        var scenario = ShenMaChaoChecks.CreateForeignMountUiScenario();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, scenario.Game.CreateCheckpoint()));
        using var model = new MainViewModel(false, scenario.Game.Seed, true, store,
            contentRegistry: scenario.Registry, historyStore: new MemoryMatchHistoryStore(),
            preferencesStore: new MemoryPlayerPreferencesStore()) { IsMotionEnabled = false, IsSoundEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var game = Program.Engine(model);
        var foreign = game.CreateSnapshot(0).Players[1].Equipment.Single();
        var action = game.GetHumanLegalActions().First(item => item.CardId == foreign.Id && item.TargetSeat == 1 &&
            item.ConversionSource?.SkillId == "anniversary:shouli");
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            var revision = game.Revision;
            var commands = game.AcceptedCommands.Count;
            model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == "anniversary:shouli"));
            var material = model.EquipmentPlayChoices.Single(choice => choice.Cards.SequenceEqual([foreign.Id]));
            model.SelectEquipmentPlayChoiceCommand.Execute(material);
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == action.TargetSeat));
            Program.Render(root, 1120, 740, Path.Combine(output, "shouli-foreign-mount-selection.png"));
            Program.Assert(model.CanConfirmSelected && game.Revision == revision && game.AcceptedCommands.Count == commands &&
                model.Hand.All(card => card.Id != foreign.Id) && model.HumanEquipmentSlots.All(slot => slot.Card?.Id != foreign.Id),
                "Selecting a published field mount must remain a reversible draft while its actual owner keeps the equipment.");
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.AcceptedCommands.Count == commands + 1 && game.AcceptedCommands.Last() is PlayCardCommand played &&
                played.CardId == foreign.Id && played.ConversionSource == action.ConversionSource &&
                played.TargetSeats!.SequenceEqual(action.TargetSeats) &&
                game.CardMovements.Count(move => move.CardId == foreign.Id && move.From == CardLocation.Equipment(1) &&
                    move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Use) == 1,
                "One confirmation must submit the exact published source and pay the other role's real field entity once.");
        }
        finally { window.Content = null; window.Close(); }
    }
}
