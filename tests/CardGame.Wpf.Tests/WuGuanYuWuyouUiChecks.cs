using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class WuGuanYuWuyouUiChecks
{
    public static void ForeignContributionUsesIssuerAndKeepsPrivateReturn(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var prepared = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Fixture.Mode, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 3 }, registry);
        Accept(prepared, new StartGameCommand());
        Accept(prepared, new SelectGeneralCommand(0, Fixture.Donor, prepared.Revision,
            prepared.CreateSnapshot(0).PendingDecision!.PromptId));
        for (var i = 0; i < 40 && prepared.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; i++)
            Accept(prepared, new AdvanceOneStepCommand(prepared.Revision));
        Program.Assert(prepared.CreateSnapshot(0).PendingDecision?.Kind == DecisionKind.PlayCard,
            "The foreign contribution fixture reaches the donor's actual Play phase.");
        var issuer = prepared.CreateSnapshot(0).Players.Single(player => player.Skills?.Any(skill => skill.ContentId == "wumiao:wuyou") == true);
        Program.Assert(issuer.Seat != 0, "The available Wuyou source belongs to a real other character.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow,
            false, prepared.CreateCheckpoint()));
        using var model = new MainViewModel(false, prepared.Seed, true, store, contentRegistry: registry,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var game = Program.Engine(model);
        var source = model.HumanSkillCards.Single(skill => skill.ContentId == "wumiao:wuyou" && skill.SkillOwnerSeat == issuer.Seat);
        Program.Assert(source.IsAvailable && source.SourceText.Contains((issuer.Seat + 1).ToString("00"), StringComparison.Ordinal),
            "The actual skill rail must expose the foreign issuer's published contribution.");
        var material = model.Hand.First();
        var commands = game.AcceptedCommands.Count;
        var revision = game.Revision;
        var moves = game.CardMovements.Count;
        var views = Views(game);
        model.ActivateHumanSkillCommand.Execute(source);
        model.SelectCardCommand.Execute(material);
        model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == issuer.Seat));
        Program.Assert(model.CanConfirmSelected && game.Revision == revision && game.AcceptedCommands.Count == commands &&
            game.CardMovements.Count == moves && Views(game).SequenceEqual(views),
            "Selecting a foreign skill, the donor's original card and issuer portrait is reversible before confirmation.");
        model.ClearSelectionCommand.Execute(null);
        Program.Assert(!model.HasSelection, "Cancellation clears the foreign contribution draft.");
        model.ActivateHumanSkillCommand.Execute(source);
        model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == material.Id));
        model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == issuer.Seat));
        var window = new MainWindow(model);
        try { Program.Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, "wumiao-wuyou-foreign-contribution.png")); }
        finally { window.Content = null; window.Close(); }
        model.ConfirmSelectedCommand.Execute(null);
        Program.Assert(game.AcceptedCommands.Count == commands + 1 && game.AcceptedCommands.Last() is UseProgramSkillCommand command &&
            command.ActorSeat == 0 && command.SkillOwnerSeat == issuer.Seat && command.SkillId == "wumiao:wuyou" &&
            command.ActivationId == "hand-contribution" && command.CardIds.SequenceEqual([material.Id]) &&
            command.TargetSeats.SequenceEqual([issuer.Seat]),
            "The actual UI submits the original donor material and distinct issuer, rather than claiming the donor owns Wuyou.");
        Program.Assert(game.CardMovements.Skip(moves).Count(move => move.CardId == material.Id &&
            move.From == CardLocation.Hand(0) && move.To == CardLocation.Hand(issuer.Seat)) == 1,
            "The confirmed contribution moves its original Hand entity once.");
        var chooser = game.CreateSnapshot(issuer.Seat).PendingDecision;
        var donor = game.CreateSnapshot(0).PendingDecision;
        Program.Assert(chooser is { IsPrivate: true } && chooser.PlayerSeat == issuer.Seat && chooser.Choices.Count >= 5 &&
            (donor is null || donor.Choices.Count == 0 && donor.ValidCardIds.Count == 0),
            "The five-name return and issuer Hand choices remain private to the actual skill owner.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Program.Assert(Views(game).SequenceEqual(Views(restored)), "The native private return also survives a cold checkpoint.");
    }

    private static string[] Views(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
    private sealed class Fixture : IGameContentPackage
    {
        internal const string Donor = "fixture:wuyou-ui-donor";
        internal const string Mode = "identity:classic-wuyou-ui";
        public PackageManifest Manifest { get; } = new("fixture-wuyou-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(Donor, "武佑贡献者", "supporter", "standard:none", "shu", 6));
            builder.AddGeneral(new("fixture:wuyou-ui-issuer", "武佑提供者", "wumiao-guan-yu", "wumiao:wuyou", "shu", 6));
            builder.AddGeneral(new("fixture:wuyou-ui-peer-1", "其他人一", "supporter", "standard:none", "wei", 6));
            builder.AddGeneral(new("fixture:wuyou-ui-peer-2", "其他人二", "supporter", "standard:none", "wei", 6));
            builder.AddDeck(new("fixture:wuyou-ui-deck", "武佑原牌", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 8)).ToArray() });
            builder.AddMode(new(Mode, "外部武佑入口", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:wuyou-ui-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Donor,
                    "fixture:wuyou-ui-issuer", "fixture:wuyou-ui-peer-1", "fixture:wuyou-ui-peer-2"]));
        }
    }
}
