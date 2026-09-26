using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class BorrowedSwordUiChecks
{
    public static void OrderedTargetsAndOwnerResponse(string output)
    {
        VerifyOrderedTargetSelection(output);
        VerifyOwnerResponse(output);
    }

    private static void VerifyOrderedTargetSelection(string output)
    {
        var fixture = BorrowedSwordScenario.FindHumanSourcePlay();
        var action = fixture.GetHumanLegalActions().First(candidate =>
            candidate.Kind == LegalActionKind.BorrowedSword);
        var cardId = action.CardId ??
            throw new InvalidOperationException("Borrowed Sword source fixture omitted its physical card.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1,
            DateTimeOffset.UtcNow,
            false,
            fixture.CreateCheckpoint()));
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: fixture.Seed,
            showSetup: true,
            saveStore: store,
            useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        viewModel.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;

        var borrowedSword = viewModel.Hand.Single(card => card.Id == cardId);
        Program.Assert(borrowedSword.IsPlayable && borrowedSword.Name == "借刀杀人",
            "The classic Borrowed Sword card was not enabled in the WPF hand.");
        viewModel.SelectCardCommand.Execute(borrowedSword);
        Program.Assert(viewModel.HasTargetCombinationChoices &&
                       viewModel.TargetCombinationChoices.All(choice => choice.Targets.Count == 2) &&
                       viewModel.TargetCombinationChoices.Any(choice =>
                           choice.Targets.SequenceEqual(action.TargetSeats)),
            "Borrowed Sword must render ordered weapon-owner/Slash-target combinations instead of an ambiguous seat toggle.");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "110-borrowed-sword-ordered-targets.png"));
        Program.Assert(Program.Find<TextBlock>(root).Any(text =>
                text.Text.Contains("使用【杀】，否则获得其武器", StringComparison.Ordinal)),
            "The ordered target choice did not explain both Borrowed Sword outcomes.");

        var choice = viewModel.TargetCombinationChoices.First(candidate =>
            candidate.Targets.SequenceEqual(action.TargetSeats));
        viewModel.SelectTargetCombinationChoiceCommand.Execute(choice);
        var engine = Program.Engine(viewModel);
        var declaredUse = engine.Events.Select(item => item.Payload)
            .OfType<CardUseDeclaredEvent>()
            .LastOrDefault(declared =>
                declared.CardId == cardId && declared.CardKind == CardKind.BorrowedSword);
        Program.Assert(declaredUse is not null &&
                       engine.Events.Any(item =>
                           item.Payload is TargetsConfirmedEvent targets &&
                           targets.ResolutionId == declaredUse.ResolutionId &&
                           targets.TargetSeats.SequenceEqual(action.TargetSeats)) &&
                       !viewModel.HasSelection,
            "The WPF ordered target button did not commit the exact Borrowed Sword pair.");

        window.Content = null;
        window.Close();
    }

    private static void VerifyOwnerResponse(string output)
    {
        var fixture = BorrowedSwordScenario.FindHumanOwnerResponse();
        var prompt = fixture.PendingDecision ??
            throw new InvalidOperationException("Borrowed Sword owner fixture lost its prompt.");
        var weapon = fixture.CreateSnapshot(0, revealAll: true).Players[0].Equipment.Single(card =>
            EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon);
        var giveChoice = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "borrowed-sword-give-weapon");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1,
            DateTimeOffset.UtcNow,
            false,
            fixture.CreateCheckpoint()));
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: fixture.Seed,
            showSetup: true,
            saveStore: store,
            useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        viewModel.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;

        Program.Assert(!viewModel.HasSaveError &&
                       viewModel.IsResponseSelectionPending &&
                       viewModel.ResponseChoices.Any(choice =>
                           choice.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash") &&
                       viewModel.ResponseChoices.Any(choice => choice.Id == giveChoice.Id),
            "The WPF did not restore both forced-Slash and weapon-transfer choices.");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "111-borrowed-sword-owner-response.png"));
        Program.Assert(Program.Find<TextBlock>(root).Any(text =>
                text.Text.Contains("交给", StringComparison.Ordinal) &&
                text.Text.Contains(weapon.DisplayName, StringComparison.Ordinal)),
            "The Borrowed Sword response panel did not name the exact weapon to transfer.");

        viewModel.SelectResponseChoiceCommand.Execute(
            viewModel.ResponseChoices.Single(choice => choice.Id == giveChoice.Id));
        var engine = Program.Engine(viewModel);
        Program.Assert(engine.Events.Any(item =>
                           item.Payload is BorrowedSwordResolvedEvent resolved &&
                           !resolved.UsedSlash &&
                           resolved.TransferredWeaponCardId == weapon.Id) &&
                       engine.CardMovements.Any(movement =>
                           movement.CardId == weapon.Id &&
                           movement.From == CardLocation.Equipment(0) &&
                           movement.Reason == CardMoveReasons.BorrowedSwordGive),
            "The central WPF response did not commit the exact weapon-transfer branch.");

        window.Content = null;
        window.Close();
    }
}
