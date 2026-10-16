using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;
using static SharedPhaseSkillScenario;

internal static class SharedPhaseSkillUiChecks
{
    public static void BolanOwnerChoiceAndRecipientRail(string output)
    {
        var registry = Registry(real: true);
        var original = Start(registry);
        Play(original);
        using (var model = Load(original, registry))
        {
            var game = Program.Engine(model);
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                var source = model.HumanSkillCards.Last(skill => skill.ContentId == "ol:bolan");
                Program.Assert(source.IsAvailable && source.SkillOwnerSeat is not null &&
                    model.HumanSkillCards.Count(skill => skill.ContentId == "ol:bolan") == 3,
                    "Every other living Bolan owner must provide an enabled recipient-rail entry.");
                Program.Render(root, 1120, 740, Path.Combine(output, "bolan-foreign-entry.png"));
                var rail = (ItemsControl)window.FindName("HumanSkillCards");
                var button = Program.Find<Button>(rail).Single(button => ReferenceEquals(button.CommandParameter, source));
                button.Command!.Execute(button.CommandParameter);
                Program.Assert(game.AcceptedCommands.Last() is UseProgramSkillCommand
                    { SkillId: "ol:bolan", SkillOwnerSeat: { } owner } && owner == source.SkillOwnerSeat,
                    "The actual Bolan rail control must submit its exact foreign source owner.");
                Play(game);
                Refresh(model, game.CreateSnapshot(0));
                var grant = game.Events.Select(item => item.Payload).OfType<ProgramPlayPhaseSkillsGrantedEvent>().Last();
                var acquired = model.HumanSkillCards.Single(skill => skill.ContentId == grant.Grants.Single().SkillId);
                Program.Assert(!acquired.IsDisabled && acquired.SourceText.Contains("获得技能", StringComparison.Ordinal) &&
                    !model.HumanSkillCards.Single(skill => skill.ContentId == "ol:bolan" && skill.SkillOwnerSeat == source.SkillOwnerSeat).IsAvailable,
                    "The chosen Bolan skill must join the recipient rail while that source's used entry stays grey.");
                Program.Render(root, 1120, 740, Path.Combine(output, "bolan-acquired-skill.png"));
                model.EndTurnCommand.Execute(null);
                Reach(game, _ => game.State.Phase != TurnPhase.Play);
                Refresh(model, game.CreateSnapshot(0));
                Program.Assert(model.HumanSkillCards.All(skill => skill.ContentId != acquired.ContentId),
                    "Ending the originating phase must remove the acquired skill from the actual rail.");
                var revision = game.Revision;
                model.ActivateHumanSkillCommand.Execute(acquired);
                Program.Assert(game.Revision == revision, "A retired acquired Bolan button must not act after expiry.");
            }
            finally { window.Content = null; window.Close(); }
        }

        original = Start(registry, ownerHuman: true);
        Reach(original, prompt => prompt?.SkillPrompt?.SkillId == "ol:bolan");
        Answer(original, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        using (var model = Load(original, registry))
        {
            var game = Program.Engine(model);
            var window = new MainWindow(model);
            try
            {
                Program.Assert(model.SkillChoices.Count == 3 && model.SkillChoices.All(choice => choice.Parameters.ContainsKey("skill-id")),
                    "The owner must see the frozen three actual skill options after loading its paused choice.");
                var retired = model.SkillChoices[1];
                var chosen = retired.Parameters["skill-id"];
                var root = (FrameworkElement)window.Content;
                Program.Render(root, 1440, 860, Path.Combine(output, "bolan-owner-three-choices.png"));
                var choices = (ItemsControl)window.FindName(model.HasPinnedPublicModuleChoices ? "PinnedPublicModuleChoices" : "ScrollingSkillChoices");
                var button = Program.Find<Button>(choices).Single(button => ReferenceEquals(button.Command, model.SelectSkillChoiceCommand) &&
                    ReferenceEquals(button.CommandParameter, retired) && button.ActualWidth > 0 && button.ActualHeight > 0);
                button.Command!.Execute(button.CommandParameter);
                Play(game);
                Refresh(model, game.CreateSnapshot(0));
                Program.Assert(model.HumanSkillCards.Any(skill => skill.ContentId == chosen) && model.SkillChoices.Count == 0,
                    "Choosing a real skill option must close the prompt and show that exact skill in the owner rail.");
                var revision = game.Revision;
                model.SelectSkillChoiceCommand.Execute(retired);
                Program.Assert(game.Revision == revision, "A retired skill choice must not grant a second skill.");
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static MainViewModel Load(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, 31, true, store, contentRegistry: registry)
        { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }
    private static void Refresh(MainViewModel model, GameSnapshot snapshot) => typeof(MainViewModel)
        .GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [snapshot]);
}
