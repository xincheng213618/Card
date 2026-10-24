using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Core;

internal static class BoundaryYiJiMobileUiChecks
{
    public static void JijiePrivateBottomAndRecipientButton(string output)
    {
        var scene = BoundaryYiJiScenario.CreateScene();
        using var model = ShiFifthBatchUiChecks.Model(scene.Game, scene.Registry);
        var game = Program.Engine(model);
        var chip = model.HumanSkillCards.Single(skill => skill.ContentId == "boundary:jijie-mobile");
        Program.Assert(chip.IsAvailable && model.ActivateHumanSkillCommand.CanExecute(chip),
            "The actual Yi Ji skill rail exposes the zero-card Jijie activation.");
        model.ActivateHumanSkillCommand.Execute(chip);
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision?.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
        ShiFifthBatchUiChecks.Refresh(model);

        var viewed = game.CreateSnapshot(0).PrivateRevealedCards!.Single();
        var face = model.PrivatelyViewedCards.Single();
        Program.Assert(model.HasPrivatelyViewedCards && model.PrivateRevealTitle.Contains("机捷", StringComparison.Ordinal) &&
            face.Id == viewed.Id && face.Kind == viewed.Kind && face.Name == viewed.DisplayName && face.IsPrivateReveal,
            "The actual view model presents the privately viewed bottom entity while choosing its recipient.");
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        host.Capture(output, "boundary-yi-ji-mobile-private-bottom-and-target", 1120);
        var faces = (ItemsControl)host.Window.FindName("PrivatelyViewedCards");
        var visibleFace = Program.Find<FrameworkElement>(faces).Any(element =>
            ReferenceEquals(element.DataContext, face) && element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0);
        Program.Assert(faces.IsVisible && faces.ActualHeight > 0 && visibleFace,
            "The native WPF private-card surface visibly renders that exact bottom-card object.");

        var recipient = model.SkillChoices.Single(choice => choice.Targets.SequenceEqual([1]));
        var seat = model.Seats.Single(seat => seat.Seat == 1);
        var button = Program.Find<Button>(host.Root).Single(button => button.IsVisible &&
            ReferenceEquals(button.Command, model.SelectTargetCommand) && ReferenceEquals(button.CommandParameter, seat));
        Program.Assert(button.IsLoaded && button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0 &&
            seat.IsLegalTarget && button.Command!.CanExecute(seat),
            "The real rendered recipient portrait retains the current seat object and executable command.");
        var commands = game.AcceptedCommands.Count;
        var before = game.CreateSnapshot(0).Players[1].HandCount;
        Click(button);
        Program.Assert(seat.IsSelectedTarget && model.CanConfirmSelected && game.AcceptedCommands.Count == commands &&
            model.PrivatelyViewedCards.Single().Id == viewed.Id,
            "Clicking the recipient portrait keeps the actual bottom face visible and prepares an unpaid target draft.");
        host.Root.UpdateLayout();
        var confirm = (Button)host.Window.FindName("PlayCardButton");
        Program.Assert(ReferenceEquals(confirm.Command, model.ConfirmSelectedCommand) && confirm.Command!.CanExecute(null),
            "The actual primary button confirms the prepared recipient through the shared command.");
        Click(confirm);
        Program.Assert(game.AcceptedCommands.Count == commands + 1 &&
            game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.Choice == recipient.Id &&
            game.CardMovements.Count(movement => movement.CardId == viewed.Id &&
                movement.From == CardLocation.DrawPile && movement.To == CardLocation.Hand(1)) == 1 &&
            game.CreateSnapshot(0).Players[1].HandCount == before + 1 && !model.HasPrivatelyViewedCards,
            "Clicking the actual target button submits one choice, gives the viewed bottom entity once and retires its private face.");
    }

    private static void Click(Button button)
    {
        Program.Assert(button.IsLoaded && button.IsVisible && button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0,
            "The actual Yi Ji button must be rendered and enabled before clicking.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
}
