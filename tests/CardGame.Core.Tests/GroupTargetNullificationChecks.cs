using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GroupTargetNullificationChecks
{
    public static void AttacksOnlyCancelCurrentRecipient()
    {
        Verify(CardKind.BarbarianAssault);
        Verify(CardKind.ArrowBarrage);
    }

    public static void HarvestKeepsSharedRevealAndSkipsOnlyCanceledPickers() => Verify(CardKind.FiveGrains);
    public static void RecoveryOnlyCancelsCurrentRecipient() => Verify(CardKind.PeachGarden);

    private static void Verify(CardKind kind)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(kind));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = "fixture:group-counterspell-mode", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:group-counterspell-owner", game.Revision, game.PendingDecision!.PromptId));
        ReachPlay(game);
        if (kind == CardKind.PeachGarden)
        {
            for (var seat = 0; seat < 4; seat++)
            {
                Accept(game, new UseProgramSkillCommand(0, "fixture:group-counterspell", "hurt", [], [seat], game.Revision, game.PendingDecision!.PromptId));
                ReachPlay(game);
            }
        }
        var before = game.CreateSnapshot(0, revealAll: true);
        var action = game.GetHumanLegalActions().First(a => a.CardId is not null && a.ConversionSource is null &&
            before.Players[0].Hand.Single(c => c.Id == a.CardId).Kind == kind);
        var start = game.Events.Count;
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId));
        var replay = Restore(game, registry);
        var targets = new List<int>();
        var depths = new List<int>();
        long lastWindow = -1;
        var sawColdChain = false;
        var originalReveal = game.CreateSnapshot(0).PublicRevealedCards.Select(c => c.Id).ToArray();
        if (kind == CardKind.FiveGrains) Require(originalReveal.Length == 4, "Harvest must reveal all four cards before any recipient's counterspell.");
        for (var step = 0; step < 160 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
        {
            var window = game.ResolutionStack.OfType<NullificationWindowFrame>().LastOrDefault();
            if (window is not null)
            {
                Require(window.TargetSeats.Count == 1 && window.GroupTargetReturn is not null,
                    "Every group counterspell must own exactly the current target.");
                var target = window.TargetSeats[0];
                if (kind == CardKind.FiveGrains)
                    Require(Enumerable.Range(0, 4).All(viewer => game.CreateSnapshot(viewer).HarvestPickerSeat == target &&
                        game.CreateSnapshot(viewer).PublicRevealedCards.Count > 0),
                        "The current Harvest recipient and remaining public pool must stay visible to all viewers during counterspells.");
                var prompt = game.CreateSnapshot(0).PendingDecision;
                if (lastWindow != window.Id)
                {
                    Require(window.ChainDepth == 0 && !window.EffectNullified, "The next recipient must start a fresh counterspell chain.");
                    targets.Add(target);
                    lastWindow = window.Id;
                }
                if (window.ChainDepth > 0 && !sawColdChain)
                {
                    Equivalent(game, Restore(game, registry));
                    sawColdChain = true;
                }
                if (prompt is { Kind: DecisionKind.Nullification, PlayerSeat: 0 })
                {
                    Require(prompt.TargetSeat == target && prompt.Prompt.Contains(game.CreateSnapshot(0).Players[target].Name),
                        "The published counterspell prompt must identify the current recipient.");
                    var desiredDepth = target is 1 or 3 ? 1 : target == 2 ? 2 : 0;
                    var choice = prompt.Choices.First(c => c.Parameters.GetValueOrDefault("response") ==
                        (window.ChainDepth < desiredDepth ? "nullification" : "pass"));
                    depths.Add(window.ChainDepth);
                    SubmitPair(game, replay, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
                    continue;
                }
            }
            if (game.PendingDecision is { Kind: DecisionKind.SelectHarvestCard, PlayerSeat: 0 } pick)
                SubmitPair(game, replay, new AnswerPromptCommand(0, pick.PromptId, pick.Choices[0].Id, game.Revision));
            else SubmitPair(game, replay, new AdvanceOneStepCommand(game.Revision));
        }
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard && sawColdChain && depths.Contains(2),
            "Group effects must finish after independent nullification and counter-nullification chains.");
        Require(targets.SequenceEqual(kind is CardKind.FiveGrains or CardKind.PeachGarden ? new[] { 0, 1, 2, 3 } : new[] { 1, 2, 3 }),
            "All recipients must receive their own ordered counterspell window.");
        var after = game.CreateSnapshot(0, revealAll: true);
        Require(after.HarvestPickerSeat is null, "A completed group effect must not leave a public Harvest recipient.");
        if (kind is CardKind.BarbarianAssault or CardKind.ArrowBarrage)
            Require(after.Players[1].Hp == before.Players[1].Hp && after.Players[3].Hp == before.Players[3].Hp &&
                after.Players[2].Hp == before.Players[2].Hp - 1,
                "Canceled recipients must be protected while the counter-nullified recipient takes the attack.");
        else if (kind == CardKind.PeachGarden)
            Require(Enumerable.Range(0, 4).All(seat => after.Players[seat].Hp == before.Players[seat].Hp + (seat is 0 or 2 ? 1 : 0)),
                "Recovery counterspells must skip only their individual recipient.");
        else
        {
            var picks = game.Events.Skip(start).Select(e => e.Payload).OfType<HarvestCardSelectedEvent>().ToArray();
            Require(picks.Select(e => e.PlayerSeat).SequenceEqual([0, 2]) && picks.All(e => originalReveal.Contains(e.CardId)),
                "Only uncanceled pickers may take cards from the original shared reveal.");
            Require(game.CardMovements.Count(m => originalReveal.Contains(m.CardId) && m.Reason == CardMoveReasons.HarvestDiscard) == 2,
                "Cards left by canceled pickers must be discarded at the end of the original draft.");
        }
        var facts = game.Events.Skip(start).Select(e => e.Payload).ToArray();
        var resolved = facts.OfType<NullificationResolvedEvent>().ToArray();
        Require(resolved.Select(e => e.TargetSeat!.Value).SequenceEqual(targets) &&
            resolved.Select(e => e.WindowFrameId).Distinct().Count() == targets.Count && resolved.All(e => e.WindowFrameId > 0),
            "Public counterspell facts must retain their individual target and response window identities.");
        Require(facts.OfType<GroupCardUsedEvent>().Count() == 1 && facts.OfType<CardUseFinishedEvent>().Count(e => e.CardKind == kind) == 1,
            "Individual counterspells must not repay, redeclare or prematurely finish the group card.");
        Require(facts.OfType<NullificationResolvedEvent>().Select(e => e.ChainDepth).SequenceEqual(
            kind is CardKind.FiveGrains or CardKind.PeachGarden ? new[] { 0, 1, 2, 1 } : new[] { 1, 2, 1 }),
            "Counterspell parity must be scoped to each recipient.");
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fixture failed to reach Play.");
    }
    private static GameEngine Restore(GameEngine game, ContentRegistry registry) =>
        GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
    private static void SubmitPair(GameEngine game, GameEngine replay, GameCommand command)
    {
        Accept(game, command);
        Accept(replay, command);
        Equivalent(game, replay);
    }
    private static void Equivalent(GameEngine game, GameEngine replay) => Require(
        Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(replay.CreateSnapshot(seat))) &&
        JsonSerializer.Serialize(game.Events) == JsonSerializer.Serialize(replay.Events) && game.CardMovements.SequenceEqual(replay.CardMovements),
        "Group counterspell cold replay must preserve every player view, event and card movement.");
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "Group counterspell command rejected.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(CardKind kind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-group-counterspell", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:group-counterspell","revision":1,"viewAs":[{"id":"counterspell","inputKinds":[],"inputSuits":["spade"],"outputKind":"nullification","forPlay":false,"forResponse":true}],"activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]}]}""";
            var presentation = """{"schemaVersion":3,"skills":{"fixture:group-counterspell":{"name":"群体无懈回归","description":"黑桃牌当无懈可击使用"}}}""";
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            builder.AddSkill(new ContentSkillDefinition("fixture:group-counterspell", "群体无懈回归", "黑桃牌当无懈可击使用")
            { Program = catalog.Programs["fixture:group-counterspell"], ProgramPresentation = catalog.Presentations["fixture:group-counterspell"] });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:group-counterspell-owner", "源", "supporter", "fixture:group-counterspell", "wu", BaseHp: 8));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new ContentGeneralDefinition($"fixture:group-counterspell-{seat}", $"目标{seat}", "supporter", "standard:none", "wei", BaseHp: 8));
            var definition = kind switch { CardKind.BarbarianAssault => "standard:barbarian_assault", CardKind.ArrowBarrage => "standard:arrow_barrage",
                CardKind.FiveGrains => "standard:five_grains", CardKind.PeachGarden => "standard:peach_garden", _ => throw new InvalidOperationException() };
            builder.AddDeck(new ContentDeckRecipe("fixture:group-counterspell-deck", "群体无懈回归", 12, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard(definition, Suit.Spade, 7)).ToArray() });
            builder.AddMode(new ContentModeDefinition("fixture:group-counterspell-mode", "群体无懈回归", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:group-counterspell-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:group-counterspell-owner", "fixture:group-counterspell-1", "fixture:group-counterspell-2", "fixture:group-counterspell-3"]));
        }
    }
}
