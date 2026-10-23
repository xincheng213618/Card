using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class MoCaoCaoUiChecks
{
    public static void BachaoTargetsAndDeclineRemainUsable(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var prepared = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.ModeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false
        }, registry);
        Accept(prepared, new StartGameCommand());
        Reach(prepared, prompt => prompt.Kind == DecisionKind.SelectGeneral);
        Accept(prepared, new SelectGeneralCommand(0, Fixture.Hero, prepared.Revision,
            prepared.CreateSnapshot(0).PendingDecision!.PromptId));
        Reach(prepared, prompt => prompt.SkillPrompt?.SkillId == "ol:mo-bachao");
        var activation = prepared.CreateSnapshot(0).PendingDecision!;
        Accept(prepared, new AnswerPromptCommand(0, activation.PromptId,
            activation.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
            prepared.Revision));
        Reach(prepared, prompt => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("branch") == "target"));
        var checkpoint = prepared.CreateCheckpoint();

        foreach (var target in new[] { 0, 1, -1 })
        {
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, checkpoint));
            using var model = new MainViewModel(false, prepared.Seed, true, store, contentRegistry: registry,
                historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore(),
                exceptionReportStore: new ExceptionReportChecks.IgnoredReports())
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            var game = Program.Engine(model);
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            Program.Assert(prompt.SkillPrompt?.SkillId == "ol:mo-bachao" && model.IsSkillSelectionPending &&
                model.IsSkillTargetSelectionPending && model.Seats.All(seat => seat.IsLegalTarget) &&
                !model.HasCenterChoices && model.CenterSkillChoices.Count == 0 && model.CanCancelAction &&
                model.ActionDockCancelText == "取 消",
                "Restoring the real paid Bachao target prompt must expose its legal portraits and skill controls.");
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                var revision = game.Revision;
                var damageCount = game.Events.Count(entry => entry.Payload is MoCaoCaoBachaoDamageEvent);
                var choice = model.SkillChoices.Single(candidate => target < 0
                    ? candidate.Parameters.GetValueOrDefault("branch") == "decline"
                    : candidate.Targets.SequenceEqual([target]));
                if (target >= 0)
                {
                    foreach (var width in new[] { 1120, 1600 })
                    {
                        Program.Render(root, width, 900, Path.Combine(output, $"bachao-target-{target}-{width}.png"));
                        var seat = model.Seats.Single(item => item.Seat == target);
                        Click(Program.Find<Button>(root).Single(button => button.Command == model.SelectTargetCommand &&
                            ReferenceEquals(button.CommandParameter, seat)));
                        Program.Assert(seat.IsSelectedTarget && model.CanConfirmSelected && game.Revision == revision,
                            "Clicking a legal Bachao portrait must prepare a reversible target without submitting it.");
                        model.ClearSelectionCommand.Execute(null);
                        Program.Assert(!seat.IsSelectedTarget && !model.CanConfirmSelected && game.Revision == revision,
                            "Clearing the target must preserve the paid native decision.");
                    }
                    model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == target));
                    root.UpdateLayout();
                    Click((Button)window.FindName("PlayCardButton"));
                }
                else
                {
                    Program.Render(root, 1120, 900, Path.Combine(output, "bachao-decline.png"));
                    model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 1));
                    Program.Assert(model.CanConfirmSelected, "Cancellation must decline even when a target is already drafted.");
                    Click((Button)window.FindName("CancelActionButton"));
                }
                Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                    answer.Prompt == prompt.PromptId && answer.Choice == choice.Id,
                    "Each visible Bachao action must submit its exact current published choice once. " + model.PromptText);
                Reach(game, pending => pending.Kind == DecisionKind.PlayCard);
                var damage = game.Events.Select(entry => entry.Payload).OfType<MoCaoCaoBachaoDamageEvent>().Skip(damageCount).ToArray();
                Program.Assert(target < 0 ? damage.Length == 0 : damage is [{ ActorSeat: 0 }] && damage[0].TargetSeat == target,
                    "The selected native branch must damage only the chosen seat, or finish without damage.");
            }
            finally { window.DataContext = null; window.Content = null; window.Close(); }
        }
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> reached)
    {
        for (var step = 0; step < 60; step++)
        {
            var prompt = game.CreateSnapshot(0).PendingDecision;
            if (prompt is not null && reached(prompt)) return;
            Program.Assert(prompt is null || prompt.PlayerSeat != 0, "Unexpected human decision in the fixed Bachao fixture.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed Bachao fixture did not reach its native decision.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native command rejected."); }

    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible && button.ActualWidth > 0 && button.ActualHeight > 0,
            "The actual bound Bachao button must be visible and enabled.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Hero = "ol:mo-cao-cao", ModeId = "identity:bachao-ui";
        public PackageManifest Manifest { get; } = new("fixture-bachao-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            // The shared derived Rumo program is registered by the magic Sun Quan manifest.
            CharacterFixtureContent.Register(builder, Hero, "ol:mo-sun-quan");
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:bachao-ui-peer-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "对手", "supporter", "standard:none", "shu", 6));
            builder.AddDeck(new("fixture:bachao-ui-deck", "固定闪牌堆", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, index % 13 + 1)).ToArray() });
            builder.AddMode(new(ModeId, "霸朝选择", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:bachao-ui-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Hero, .. peers]));
        }
    }
}
