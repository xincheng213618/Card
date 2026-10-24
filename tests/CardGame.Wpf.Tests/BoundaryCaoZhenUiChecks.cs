using System.Reflection;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Core;
using CardGame.Wpf.ViewModels;

internal static class BoundaryCaoZhenUiChecks
{
    public static void SecretForecastActualTargetsAndOwnerOnlyProgress(string output)
    {
        var scene = BoundaryCaoZhenScenario.CreateScene(singleHuman: true);
        var game = scene.Game;
        BoundaryCaoZhenScenario.Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        BoundaryCaoZhenScenario.Reach(game, prompt => prompt?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("branch") == "holder") == true);
        using var model = ShiFifthBatchUiChecks.Model(game, scene.Registry);
        game = Program.Engine(model);
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        SelectAndConfirm(1, "holder");
        SelectAndConfirm(2, "target");
        BoundaryCaoZhenScenario.Play(game, 0);
        ShiFifthBatchUiChecks.Refresh(model);
        var prediction = game.CreateSnapshot(0).BoundarySidiPredictions;
        var skill = model.HumanSkillCards.Single(card => card.ContentId == BoundaryCaoZhenScenario.Skill);
        var expected = game.CreateSnapshot(0).Players.Single(player => player.Seat == 1).Name + " → " +
            game.CreateSnapshot(0).Players.Single(player => player.Seat == 2).Name;
        Program.Assert(prediction is [{ HolderSeat: 1, TargetSeat: 2 }] &&
                Enumerable.Range(1, 3).All(viewer => game.CreateSnapshot(viewer).BoundarySidiPredictions is null) &&
                skill.ProgressText == "预测 1" && skill.StateText.Contains(expected, StringComparison.Ordinal),
            "The real owner's rail remembers the exact secret forecast while every other viewer has no forecast projection.");
        host.Capture(output, "boundary-cao-zhen-mobile-private-forecast", 1120);
        Program.Assert(Program.Find<TextBlock>(host.Root).Any(text => text.IsVisible && text.ActualWidth > 0 && text.ActualHeight > 0 &&
                ReferenceEquals(text.DataContext, skill) && text.Text == skill.ProgressText),
            "The loaded native rail visibly renders the actual owner's forecast count.");

        void SelectAndConfirm(int seat, string branch)
        {
            Program.Render(host.Root, 1120, 740, null);
            var target = model.Seats.Single(player => player.Seat == seat);
            var choice = model.SkillChoices.Single(item => item.Parameters.GetValueOrDefault("branch") == branch && item.Targets.SequenceEqual([seat]));
            var accepted = game.AcceptedCommands.Count;
            var button = Program.Find<Button>(host.Root).Single(item => item.IsVisible &&
                ReferenceEquals(item.Command, model.SelectTargetCommand) && ReferenceEquals(item.CommandParameter, target));
            Program.Assert(model.IsSkillTargetSelectionPending && target.IsLegalTarget && button.IsEnabled,
                "The actual forecast step exposes its legal holder or predicted-target portrait.");
            if (!target.IsSelectedTarget) Click(button);
            Program.Assert(model.CanConfirmSelected && game.AcceptedCommands.Count == accepted,
                "Selecting a secret target remains an editable draft until the real confirmation.");
            var confirm = (Button)host.Window.FindName("PlayCardButton");
            Program.Assert(ReferenceEquals(confirm.Command, model.ConfirmSelectedCommand) && confirm.Command!.CanExecute(null),
                "The visible primary action confirms the exact current forecast target draft.");
            Click(confirm);
            Program.Assert(game.AcceptedCommands.Count == accepted + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.Choice == choice.Id,
                "Each native confirmation submits its published secret choice exactly once.");
        }
    }

    private static void Click(Button button)
    {
        Program.Assert(button.IsLoaded && button.IsVisible && button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0,
            "The actual forecast button must be rendered and enabled before clicking.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
}
