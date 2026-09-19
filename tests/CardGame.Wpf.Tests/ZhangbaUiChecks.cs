using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ZhangbaUiChecks
{
    public static void ActiveDraft(string output)
    {
        var boundary = ZhangbaScenario.FindHumanActiveUse();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1,
            DateTimeOffset.UtcNow,
            false,
            boundary.BeforeUse));
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: boundary.Game.Seed,
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
        var engine = Program.Engine(viewModel);
        var action = viewModel.HumanActiveSkillActions.Single(candidate =>
            candidate.Kind == LegalActionKind.UseEquipmentEffect &&
            candidate.EquipmentKind == CardKind.ZhangbaSerpentSpear);

        viewModel.SelectActiveSkillCommand.Execute(action);
        foreach (var cardId in boundary.CostCardIds)
        {
            viewModel.SelectCardCommand.Execute(viewModel.Hand.Single(card => card.Id == cardId));
        }
        viewModel.SelectTargetCommand.Execute(viewModel.Seats.Single(seat => seat.Seat == boundary.TargetSeat));

        Program.Assert(viewModel.IsActiveSkillSelectionPending &&
                       viewModel.CanConfirmActiveSkill &&
                       viewModel.ActiveSkillButtonText.Contains("丈八蛇矛", StringComparison.Ordinal) &&
                       viewModel.Hand.Count(card => card.IsSelected) == 2 &&
                       viewModel.Seats.Single(seat => seat.Seat == boundary.TargetSeat).IsSelectedTarget,
            "The WPF Zhangba draft must select exactly two hand cards and one legal Slash target.");

        Program.Render(root, 1120, 740,
            Path.Combine(output, "113-zhangba-active-slash.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text => text.Contains("丈八蛇矛", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("牌 2/2", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("目标 1/1", StringComparison.Ordinal)),
            "The central Zhangba draft did not expose its exact card and target requirements.");

        viewModel.ConfirmSelectedCommand.Execute(null);
        var command = engine.AcceptedCommands.OfType<UseEquipmentEffectCommand>().LastOrDefault();
        var converted = engine.Events.Select(item => item.Payload)
            .OfType<ZhangbaSerpentSpearConvertedEvent>()
            .LastOrDefault();
        Program.Assert(command is not null &&
                       command.EquipmentKind == CardKind.ZhangbaSerpentSpear &&
                       command.CardIds.SequenceEqual(boundary.CostCardIds.Order()) &&
                       command.TargetSeats.SequenceEqual([boundary.TargetSeat]) &&
                       converted is { IsUse: true } &&
                       converted.PhysicalCardIds.SequenceEqual(command.CardIds),
            "The WPF Zhangba draft did not commit the typed equipment-effect command and both costs.");

        window.Content = null;
        window.Close();

        ResponsePrompt(output);
    }

    private static void ResponsePrompt(string output)
    {
        var boundary = ZhangbaScenario.FindHumanResponse();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(
            1,
            DateTimeOffset.UtcNow,
            false,
            boundary.Game.CreateCheckpoint()));
        using var viewModel = new MainViewModel(
            autoAdvance: false,
            seed: boundary.Game.Seed,
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
        var engine = Program.Engine(viewModel);
        var choice = viewModel.ResponseChoices.FirstOrDefault(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "zhangba-slash" &&
            candidate.Cards.Count == 2) ??
            throw new InvalidOperationException("The restored WPF response has no Zhangba pair choice.");

        Program.Assert(viewModel.IsResponseSelectionPending &&
                       choice.Cards.All(cardId => viewModel.Hand.Any(card => card.Id == cardId)) &&
                       choice.Description.Contains("丈八蛇矛", StringComparison.Ordinal),
            "The WPF must render Zhangba as one exact two-card Slash response choice.");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "114-zhangba-response.png"));
        var visibleText = Program.Find<TextBlock>(root).Select(text => text.Text).ToArray();
        Program.Assert(visibleText.Any(text => text.Contains("丈八蛇矛", StringComparison.Ordinal)) &&
                       visibleText.Any(text => text.Contains("两张手牌", StringComparison.Ordinal)),
            "The central Zhangba response did not explain its two-hand-card conversion.");

        viewModel.SelectResponseChoiceCommand.Execute(choice);
        var converted = engine.Events.Select(item => item.Payload)
            .OfType<ZhangbaSerpentSpearConvertedEvent>()
            .LastOrDefault();
        Program.Assert(converted is { IsUse: false } &&
                       converted.PhysicalCardIds.SequenceEqual(choice.Cards) &&
                       choice.Cards.All(cardId => engine.CardMovements.Any(move =>
                           move.CardId == cardId && move.Reason == CardMoveReasons.ResponseFinished)),
            "The WPF Zhangba response did not commit both exact hand cards.");

        window.Content = null;
        window.Close();
    }
}
