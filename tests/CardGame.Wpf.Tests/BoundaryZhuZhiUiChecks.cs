using System.Reflection;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Core;

internal static class BoundaryZhuZhiUiChecks
{
    public static void InitialAnguoTargetMarkerAndProgress(string output)
    {
        const string skill = "boundary:anguo-mobile";
        var scene = BoundaryZhuZhiChecks.CreateScene(); var game = scene.Game;
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { SkillPrompt.SkillId: skill } prompt &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("branch") == "mark"));
        using var model = ShiFifthBatchUiChecks.Model(game, scene.Registry); game = Program.Engine(model);
        var before = model.HumanSkillCards.Single(chip => chip.ContentId == skill).ProgressText;
        Program.Assert(model.IsSkillTargetSelectionPending && !string.IsNullOrWhiteSpace(before),
            "The actual initial Anguo choice exposes board target selection and the owner's current skill progress.");
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        Program.Render(host.Root, 1120, 740, null);
        var target = model.Seats.Single(seat => seat.Seat == 1);
        var choice = model.SkillChoices.Single(choice => choice.Targets.SequenceEqual([1]) &&
            choice.Parameters.GetValueOrDefault("branch") == "mark");
        var commands = game.AcceptedCommands.Count;
        var seatButton = Program.Find<Button>(host.Root).Single(button => button.IsVisible &&
            ReferenceEquals(button.Command, model.SelectTargetCommand) && ReferenceEquals(button.CommandParameter, target));
        Program.Assert(target.IsLegalTarget && seatButton.IsEnabled && seatButton.Command!.CanExecute(target),
            "The actual Anguo recipient portrait carries the current seat object and a legal target command.");
        if (!target.IsSelectedTarget) Click(seatButton);
        Program.Assert(target.IsSelectedTarget && model.CanConfirmSelected && game.AcceptedCommands.Count == commands,
            "The recipient is selected as an unpaid draft, including when a unique target was selected automatically.");
        host.Root.UpdateLayout();
        var confirm = (Button)host.Window.FindName("PlayCardButton");
        Program.Assert(ReferenceEquals(confirm.Command, model.ConfirmSelectedCommand) && confirm.Command!.CanExecute(null),
            "The visible primary action confirms the actual Anguo recipient draft.");
        Click(confirm);
        Program.Assert(game.AcceptedCommands.Count == commands + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
            answer.Choice == choice.Id,
            "The real Anguo confirmation submits the exact published initial-mark choice once.");
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        ShiFifthBatchUiChecks.Refresh(model);
        var holder = model.Seats.Single(seat => seat.Seat == 1);
        var badge = holder.VisibleStatusBadges.Single(status => status.Label == "安国");
        var progress = model.HumanSkillCards.Single(chip => chip.ContentId == skill);
        Program.Assert(badge.Count == 1 && holder.ShowPortraitStatuses && !string.IsNullOrWhiteSpace(progress.ProgressText) &&
            progress.ProgressText != before,
            "The marked seat exposes its real Anguo badge and Zhu Zhi's displayed progress updates after the initial mark.");
        host.Capture(output, "boundary-zhu-zhi-mobile-initial-anguo-mark", 1120);
        Program.Assert(Program.Find<TextBlock>(host.Root).Any(text => text.IsVisible && text.ActualWidth > 0 &&
                text.ActualHeight > 0 && ReferenceEquals(text.DataContext, badge) && text.Text == "安国") &&
            Program.Find<TextBlock>(host.Root).Any(text => text.IsVisible && text.ActualWidth > 0 && text.ActualHeight > 0 &&
                ReferenceEquals(text.DataContext, progress) && text.Text == progress.ProgressText),
            "The loaded native WPF tree visibly renders the exact marked-seat badge and updated skill-progress objects.");
    }

    private static void Click(Button button)
    {
        Program.Assert(button.IsLoaded && button.IsVisible && button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0,
            "The actual Anguo button must be rendered and enabled before clicking.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
}
