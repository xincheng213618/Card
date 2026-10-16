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

internal static class CongjianUiChecks
{
    public static void RecipientThenPhysicalCard(string output)
    {
        foreach (var equipment in new[] { false, true })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(),
                new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
                new StandardClassicGeneralPackage(), new Fixture(equipment));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 8,
                ModeId = Fixture.Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 3
            }, registry);
            Accept(game, new StartGameCommand());
            Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
            Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
            if (equipment)
            {
                var weapon = game.CreateSnapshot(0).Players[0].Hand.First(card => card.Kind == CardKind.Crossbow);
                Accept(game, new PlayCardCommand(0, weapon.Id, [], game.Revision, game.PendingDecision!.PromptId));
                Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
            }
            var groupCard = game.CreateSnapshot(0).Players[0].Hand.First(card => card.Kind == CardKind.FiveGrains);
            var use = game.GetHumanLegalActions().First(action => action.CardId == groupCard.Id);
            Accept(game, new PlayCardCommand(0, use.CardId!.Value, use.TargetSeats,
                game.Revision, game.PendingDecision!.PromptId, use.PlayedCardKind) { ConversionSource = use.ConversionSource });
            Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:congjian");
            var offer = game.PendingDecision!;
            Accept(game, new AnswerPromptCommand(0, offer.PromptId,
                offer.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate").Id, game.Revision));
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(false, 17, true, store, useExpandedContent: true, contentRegistry: registry)
                { IsMotionEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            Program.Assert(!model.HasSaveError && model.IsSkillTargetSelectionPending && model.CenterSkillChoices.Count == 0 &&
                !model.CanConfirmSelected && model.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat)
                    .Order().SequenceEqual(Enumerable.Range(1, 7)),
                "Restored eight-seat Congjian must first highlight exactly the seven other final targets without repeated card buttons.");
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                var before = game.Revision;
                Program.Render(root, 1120, 780, null);
                Click(Seat(1));
                var retiredRecipientChoice = model.CenterSkillChoices[0];
                Click(Seat(2));
                model.SelectSkillChoiceCommand.Execute(retiredRecipientChoice);
                Program.Assert(game.Revision == before && !model.CanConfirmSelected &&
                    model.CenterSkillChoices.All(choice => choice.Targets.SequenceEqual([2])),
                    "Switching recipients must filter exact card choices and reject a delayed click for the old recipient.");
                model.ClearSelectionCommand.Execute(null);
                Program.Assert(!model.HasSelection && model.CenterSkillChoices.Count == 0 && game.Revision == before,
                    "Esc must clear the local recipient without paying or answering Congjian.");
                Click(Seat(2));
                var current = game.CreateSnapshot(0).Players[0];
                var expectedCards = current.Hand.Concat(current.Equipment).Select(card => card.Id).Order().ToArray();
                Program.Assert(model.CenterSkillChoices.SelectMany(choice => choice.Cards).Order().SequenceEqual(expectedCards) &&
                    model.CenterSkillChoices.SelectMany(choice => choice.Cards).Distinct().Count() == expectedCards.Length &&
                    model.ActionHint.Contains(model.Seats.Single(seat => seat.Seat == 2).GeneralName, StringComparison.Ordinal),
                    "Each physical hand/equipment card must appear once with its selected recipient named in the hint.");
                var selected = model.CenterSkillChoices.First(choice =>
                    choice.Parameters.GetValueOrDefault("source-zone") == (equipment ? "Equipment" : "Hand"));
                model.SelectSkillChoiceCommand.Execute(selected with { });
                Program.Assert(game.Revision == before, "Copied unpublished card parameters must remain inert.");
                foreach (var width in new[] { 1120, 1920 })
                {
                    Program.Render(root, width, width == 1120 ? 780 : 1080,
                        Path.Combine(output, $"congjian-{equipment}-{width}.png"));
                    var panel = (ItemsControl)window.FindName("ScrollingSkillChoices");
                    Program.Assert(Program.Find<Button>(panel).Count(button => button.Command == model.SelectSkillChoiceCommand) == expectedCards.Length,
                        "The rendered choice panel must contain one face per physical card at both supported table sizes.");
                }
                var stale = selected;
                model.LoadManualGameCommand.Execute(null);
                game = Program.Engine(model);
                model.SelectSkillChoiceCommand.Execute(stale);
                Program.Assert(game.Revision == before && model.CenterSkillChoices.Count == 0 && !model.HasSelection,
                    "Reload must retire both the old card choice and its local recipient draft.");
                Click(Seat(2));
                selected = model.CenterSkillChoices.Single(choice => choice.Id == stale.Id);
                Program.Render(root, 1120, 780, null);
                var cards = (ItemsControl)window.FindName("ScrollingSkillChoices");
                var button = Program.Find<Button>(cards).Single(candidate => candidate.Command == model.SelectSkillChoiceCommand &&
                    ReferenceEquals(candidate.CommandParameter, selected));
                var handBefore = game.CreateSnapshot(0).Players[0].HandCount;
                Click(button);
                var command = (AnswerPromptCommand)game.AcceptedCommands.Last();
                Program.Assert(command.Choice == selected.Id && command.ExpectedRevision == before && game.Revision == before + 1,
                    "The visible physical face must submit exactly its published card/recipient pair once.");
                var reward = equipment ? 2 : 1;
                for (var step = 0; step < 20 && game.CardMovements.Count(move => move.Reason.Value == "skill-program.final-target-gift.draw") < reward; step++)
                    Accept(game, new AdvanceOneStepCommand(game.Revision));
                var gift = game.Events.Select(item => item.Payload).OfType<ProgramFinalTargetGiftCommittedEvent>().Single();
                Program.Assert(gift.RecipientSeat == 2 && gift.CardId == selected.Cards.Single() && gift.Reward == reward &&
                    game.CardMovements.Count(move => move.Reason.Value == "skill-program.final-target-gift.give") == 1 &&
                    game.CardMovements.Count(move => move.Reason.Value == "skill-program.final-target-gift.draw") == reward &&
                    game.CreateSnapshot(0).Players[0].HandCount == handBefore - (equipment ? 0 : 1) + reward &&
                    game.CreateSnapshot(2).Players[2].Hand.Any(card => card.Id == gift.CardId),
                    "The chosen recipient must gain the exact physical card once; ordinary cards draw one and equipped cards draw two.");
                var replayed = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                Program.Assert(Enumerable.Range(0, 8).All(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer)) ==
                    SnapshotJson.Serialize(replayed.CreateSnapshot(viewer))) && game.CardMovements.SequenceEqual(replayed.CardMovements),
                    "The real UI submission must cold-replay all eight private views and physical movements.");
                var after = game.Revision;
                model.SelectSkillChoiceCommand.Execute(selected);
                Program.Assert(game.Revision == after, "A spent card button must not pay or draw again.");

                Button Seat(int seat) => Program.Find<Button>(root).Single(candidate => candidate.Command == model.SelectTargetCommand &&
                    candidate.DataContext is SeatViewModel view && view.Seat == seat);
            }
            finally { window.Content = null; window.DataContext = null; window.Close(); }
        }
    }

    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Command!.CanExecute(button.CommandParameter), "Expected an enabled bound control.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected accepted fixture command.");
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> condition)
    {
        for (var step = 0; step < 40; step++)
        {
            if (game.PendingDecision is { } prompt && condition(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Congjian fixture did not reach its real prompt.");
    }

    private sealed class Fixture(bool equipment) : IGameContentPackage
    {
        internal const string Mode = "identity:congjian-ui", Owner = "fixture:congjian-ui-owner";
        public PackageManifest Manifest { get; } = new("fixture:congjian-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(Owner, "张绣", "zhang_xiu", "classic:congjian", "qun", 4));
            var peers = Enumerable.Range(1, 7).Select(seat => $"fixture:congjian-ui-{seat}").ToArray();
            foreach (var id in peers) builder.AddGeneral(new(id, id, "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:congjian-ui-deck", "固定牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 100).Select(index => new ContentDeckPhysicalCard(
                    index % 2 == 0 ? "standard:five_grains" : equipment ? "standard:crossbow" : "standard:peach",
                    Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "从谏界面测试", 8, 8,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 7 },
                "fixture:congjian-ui-deck", GeneralCandidateCount: 8, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
