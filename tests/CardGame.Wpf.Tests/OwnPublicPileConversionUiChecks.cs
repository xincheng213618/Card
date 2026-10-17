using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class OwnPublicPileConversionUiChecks
{
    public static void RealBingPlayRailAndPublishedResponseSource()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        PlayRail(registry);
        ResponseChoice(registry);
    }

    private static void PlayRail(ContentRegistry registry)
    {
        var game = Create(registry);
        using var model = Restore(game, registry);
        game = Program.Engine(model);
        var pile = Bing(game.CreateSnapshot(0).Players[0]);
        var actions = game.GetHumanLegalActions();
        var action = actions.First(a => a.Kind == LegalActionKind.Slash && a.CardId is { } id &&
            pile.Any(c => c.Id == id) && a.TargetSeats.Count == 1 && a.ConversionSource?.SkillId == "ol:jibing");
        Program.Assert(pile.Count == 2 && pile.All(c => c.Kind == CardKind.Slash) &&
            actions.Where(a => a.CardId is { } id && pile.Any(c => c.Id == id))
                .All(a => a.ConversionSource?.SkillId == "ol:jibing") &&
            model.Hand.All(c => pile.All(p => p.Id != c.Id)),
            "Real Jibing must keep both printed Slashes in its own public pile, with no source-free native or Hand option.");
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            var revision = game.Revision;
            var commands = game.AcceptedCommands.Count;
            model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(s => s.ContentId == "ol:jibing"));
            Layout(root);
            var choice = model.EquipmentPlayChoices.Single(c => c.Cards.SequenceEqual([action.CardId!.Value]));
            Program.Assert(model.EquipmentPlayChoices.All(c => c.Cards.Count == 1 && pile.Any(p => p.Id == c.Cards[0])) &&
                !model.CanConfirmSelected && game.Revision == revision,
                "The skill-first regional rail must expose exactly the owner's published Bing materials without committing.");
            Click(root, model.SelectEquipmentPlayChoiceCommand, choice);
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == action.TargetSeats[0]));
            Layout(root);
            Program.Assert(model.CanConfirmSelected && game.Revision == revision && game.AcceptedCommands.Count == commands,
                "Selecting a public-pile material and real legal target must remain reversible until confirmation.");
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.AcceptedCommands.Count == commands + 1 &&
                game.AcceptedCommands.Last() is PlayCardCommand played && played.CardId == action.CardId &&
                played.PlayedCardKind == CardKind.Slash && played.ConversionSource == action.ConversionSource &&
                played.TargetSeats!.SequenceEqual(action.TargetSeats) &&
                game.CardMovements.Count(m => m.CardId == action.CardId && m.From.Zone == CardZoneKind.PublicPersistentPile &&
                    m.From.OwnerSeat == 0 && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1,
                "The WPF confirm must submit the exact Bing source/entity/target and pay that real public-pile card once.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void ResponseChoice(ContentRegistry registry)
    {
        var game = Create(registry);
        Accept(game, new UseProgramSkillCommand(0, Fixture.Driver, "incoming-duel", [], [1],
            game.Revision, game.PendingDecision!.PromptId));
        Reach(game, DecisionKind.RespondSlash);
        using var model = Restore(game, registry);
        game = Program.Engine(model);
        var pile = Bing(game.CreateSnapshot(0).Players[0]);
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        var choice = model.HandResponseExtraChoices.First(c => c.Cards.Count == 1 &&
            pile.Any(p => p.Id == c.Cards[0]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == "ol:jibing");
        Program.Assert(model.IsHandResponsePending && prompt.IncomingCard == CardKind.Duel &&
            prompt.Choices.Where(c => c.Cards.Count == 1 && pile.Any(p => p.Id == c.Cards[0]))
                .All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == "ol:jibing") &&
            model.Hand.All(c => pile.All(p => p.Id != c.Id)),
            "The actual Duel response must publish source-qualified Bing alternatives without pretending they are native Hand Slashes.");
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            var commands = game.AcceptedCommands.Count;
            var id = choice.Cards.Single();
            Layout(root);
            Click(root, model.SelectHandResponseChoiceCommand, choice);
            Program.Assert(game.AcceptedCommands.Count == commands + 1 &&
                game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.PromptId == prompt.PromptId && answer.Choice == choice.Id &&
                game.CardMovements.Count(m => m.CardId == id && m.From.Zone == CardZoneKind.PublicPersistentPile &&
                    m.From.OwnerSeat == 0 && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Respond) == 1,
                "The real response control must answer its current published source and pay the original Bing entity once.");
            var accepted = game.AcceptedCommands.Count;
            model.SelectHandResponseChoiceCommand.Execute(choice);
            Program.Assert(game.AcceptedCommands.Count == accepted,
                "A retired public-pile response choice must not pay the entity again.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static IReadOnlyList<CardSnapshot> Bing(PlayerSnapshot owner) =>
        owner.PublicPersistentPiles is { Count: > 0 } piles
            ? piles.Single(p => p.SourceSkillId == "ol:jibing").Cards
            : owner.PublicPersistentPileSkillId == "ol:jibing" ? owner.PublicPersistentPileCards ?? [] : [];

    private static GameEngine Create(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, DecisionKind.PlayCard);
        return game;
    }

    private static void Reach(GameEngine game, DecisionKind kind)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } prompt)
            {
                if (prompt.Kind == kind) return;
                if (prompt.Kind == DecisionKind.ProgramTrigger && prompt.SkillPrompt?.SkillId == "ol:jibing")
                {
                    var activate = prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                    Accept(game, new AnswerPromptCommand(0, prompt.PromptId, activate.Id, game.Revision));
                    continue;
                }
                throw new InvalidOperationException($"Unexpected native Bing UI fixture prompt: {prompt.Kind}/{prompt.SkillPrompt?.SkillId}.");
            }
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException($"The actual Bing UI fixture did not reach {kind}.");
    }

    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }

    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native Bing UI fixture command rejected."); }
    private static void Click(FrameworkElement root, ICommand command, object parameter)
    {
        var button = Program.Find<Button>(root).Single(b => ReferenceEquals(b.Command, command) && ReferenceEquals(b.CommandParameter, parameter));
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible, "The real public-pile control must be enabled and visible.");
        button.Command!.Execute(button.CommandParameter);
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 780)); root.Arrange(new Rect(0, 0, 1120, 780)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Mode = "identity:own-bing-ui", Owner = "fixture:own-bing-ui-owner", Driver = "fixture:own-bing-ui-driver";
        public PackageManifest Manifest { get; } = new("fixture-own-bing-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:ma-yuan-yi");
            var catalog = SkillProgramCatalog.Load("""
                {"schemaVersion":62,"skills":[{"id":"fixture:own-bing-ui-driver","revision":1,"activations":[
                {"id":"incoming-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving",
                "usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]}]}
                """, """
                {"schemaVersion":3,"skills":{"fixture:own-bing-ui-driver":{"name":"原生决斗","description":"令其他角色对自己使用决斗"}}}
                """);
            builder.AddSkill(new(Driver, "原生决斗", "原生决斗") { Program = catalog.Programs[Driver] });
            builder.AddGeneral(new(Owner, "集兵交互", "supporter", "ol:jibing", "qun", 6, [Driver, "ol:wangjing"]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:own-bing-ui-peer-{i}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "原生目标", "supporter", "standard:none", "wei", 6));
            builder.AddDeck(new("fixture:own-bing-ui-deck", "真实杀牌库", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, i % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "集兵交互", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:own-bing-ui-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
