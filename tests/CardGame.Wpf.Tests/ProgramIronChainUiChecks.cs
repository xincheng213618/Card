using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ProgramIronChainUiChecks
{
    public static void TargetCountGuideAndSubmission()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        foreach (var (generalId, maximum) in new[] { ("boundary:pang-tong", 3), ("fixture:chain-owner", 2) })
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = Fixture.ModeId, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false
            }, registry);
            Accept(game, new StartGameCommand());
            Accept(game, new SelectGeneralCommand(0, generalId, game.Revision, game.PendingDecision!.PromptId));
            for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            Program.Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard, "The fixed Iron Chain UI fixture reaches Play.");
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true,
                contentRegistry: registry) { IsMotionEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            var card = model.Hand.First(item => item.Kind == CardKind.IronChain && item.IsPlayable);
            model.SelectCardCommand.Execute(card);
            model.OpenContextGuideCommand.Execute(null);
            Program.Assert(model.CurrentGuideBody.Contains($"1–{maximum}") &&
                           model.CurrentGuideSteps.Any(step => step.Text.Contains($"1–{maximum}")),
                "The guide must describe the actual current Iron Chain target count.");
            model.IsHelpOpen = false;
            var selected = model.Seats.Take(maximum).Select(seat => seat.Seat).ToArray();
            foreach (var seat in selected) model.SelectTargetCommand.Execute(model.Seats.Single(item => item.Seat == seat));
            Program.Assert(model.CanConfirmSelected && model.Seats.Count(seat => seat.IsSelectedTarget) == maximum,
                "All current Iron Chain targets remain selectable together.");
            model.ConfirmSelectedCommand.Execute(null);
            for (var step = 0; step < 40 && Program.Engine(model).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                model.StepAiCommand.Execute(null);
            Program.Assert(selected.All(seat => Program.Engine(model).CreateSnapshot(0).Players[seat].IsChained) &&
                           Program.Engine(model).CardMovements.Count(move => move.CardId == card.Id &&
                               move.Reason == CardMoveReasons.IronChainUse) == 1,
                "The UI submits the exact target set and pays the physical card once.");
        }
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected an accepted Iron Chain UI command.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-chain-ui";
        public PackageManifest Manifest { get; } = new("fixture-chain-ui", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { "owner", "one", "two" })
                builder.AddGeneral(new($"fixture:chain-{id}", id, "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:chain-ui-deck", "铁索目标界面", 4, 0,
                [new ContentDeckCardCount("standard:iron_chain", 32)]));
            builder.AddMode(new(ModeId, "铁索目标界面", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:chain-ui-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["boundary:pang-tong", "fixture:chain-owner", "fixture:chain-one", "fixture:chain-two"]));
        }
    }
}
