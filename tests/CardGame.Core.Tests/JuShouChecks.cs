using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class JuShouChecks
{
    private const string Driver = "fixture:ju-shou-driver";
    private const string General = "fixture:ju-shou-owner";
    private const string Mode = "fixture:ju-shou-mode";

    public static void ConsecutiveSuitRankAndReplay()
    {
        foreach (var boundary in new[] { false, true })
        {
            var (game, registry) = Create(boundary);
            Use(game, "draw");
            var cards = Hand(game).Where(card => card.Kind == CardKind.Crossbow).ToArray();
            var first = cards[0];
            PlayEquipment(game, first.Id);
            Drain(game);
            Require(Draws(game) == 0, "The first use has no previous card to match.");
            var sameSuit = Hand(game).First(card => card.Suit == first.Suit && card.Rank != first.Rank);
            PlayEquipment(game, sameSuit.Id);
            Require(game.PendingDecision?.SkillPrompt?.SkillId == Skill(boundary), "Same suit must offer Jianying before the equipment effect.");
            var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Drain(game);
            Drain(restored);
            EqualReplay(game, restored);
            Require(Draws(game) == 1, "Matching suit must draw once.");
            var sameRank = Hand(game).First(card => card.Rank == sameSuit.Rank && card.Suit != sameSuit.Suit);
            PlayEquipment(game, sameRank.Id);
            Drain(game);
            Require(Draws(game) == 2, "Matching rank must draw once.");
            var neither = Hand(game).First(card => card.Rank != sameRank.Rank && card.Suit != sameRank.Suit);
            PlayEquipment(game, neither.Id);
            Drain(game);
            Require(Draws(game) == 2, "A nonmatching adjacent use must not draw.");
            var both = Hand(game).First(card => card.Rank == neither.Rank && card.Suit == neither.Suit);
            PlayEquipment(game, both.Id);
            Drain(game, activate: false);
            Require(Draws(game) == 2, "Jianying is optional even when both suit and rank match.");
            NextTurn(game);
            var next = Hand(game).First(card => card.Suit == both.Suit);
            PlayEquipment(game, next.Id);
            Drain(game);
            Require(Draws(game) == 2, "The first use of a new play phase must forget the previous phase.");
            EqualReplay(game, GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry));
        }
    }

    public static void BoundaryBasicConversionsAndPhaseAllowance()
    {
        foreach (var kind in new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash,
                     CardKind.Peach, CardKind.Alcohol })
        {
            var (game, registry) = Create(boundary: true);
            Use(game, "draw");
            if (kind == CardKind.Peach) Use(game, "lose");
            var first = Hand(game)[0];
            PlayEquipment(game, first.Id);
            Drain(game);
            var input = Hand(game).First(card => card.Suit != first.Suit && card.Rank != first.Rank);
            var conversion = game.GetHumanLegalActions().First(action => action.CardId == input.Id &&
                action.PlayedCardKind == kind && action.ConversionSource?.SkillId == "boundary:jianying");
            Play(game, conversion);
            var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Drain(game);
            Drain(restored);
            EqualReplay(game, restored);
            var used = game.Events.Select(item => item.Payload).OfType<CardUseAppearanceCapturedEvent>()
                .Select(item => item.Action).First(action => action.ConversionChain.Any(source =>
                    source.SkillId == "boundary:jianying"));
            Require(used.EffectiveSuit == first.Suit && used.EffectiveRank == input.Rank &&
                    used.EffectiveKind == kind && used.PhysicalCards.Single().CardId == input.Id,
                "Boundary conversion must freeze inherited suit, physical rank, chosen basic kind and real cost.");
            Require(Draws(game) == 1 && game.GetHumanLegalActions().All(action =>
                    action.ConversionSource?.SkillId != "boundary:jianying"),
                "All basic choices share one phase allowance and the inherited suit draws once.");
            if (kind is CardKind.FireSlash or CardKind.ThunderSlash)
                Require(game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Any(item =>
                        item.Nature == (kind == CardKind.FireSlash ? DamageNature.Fire : DamageNature.Thunder)),
                    "The chosen elemental Slash must retain its damage nature.");
            NextTurn(game);
            Require(game.GetHumanLegalActions().Any(action => action.ConversionSource?.SkillId == "boundary:jianying"),
                "The next play phase must restore the shared conversion allowance.");
        }
    }

    public static void BoundaryEquipmentCostAndFirstUse()
    {
        var (game, registry) = Create(boundary: true);
        var input = Hand(game)[0];
        var action = game.GetHumanLegalActions().First(item => item.CardId == input.Id &&
            item.PlayedCardKind == CardKind.Alcohol && item.ConversionSource?.SkillId == "boundary:jianying");
        Play(game, action);
        Drain(game);
        var used = game.Events.Select(item => item.Payload).OfType<CardUseAppearanceCapturedEvent>().Last().Action;
        Require(used.EffectiveSuit == input.Suit && Draws(game) == 0,
            "A first-use conversion keeps its physical suit and does not draw without a predecessor.");
        NextTurn(game);
        var equip = Hand(game)[0];
        PlayEquipment(game, equip.Id);
        Drain(game);
        var fromEquipment = game.GetHumanLegalActions().First(item => item.CardId == equip.Id &&
            item.PlayedCardKind == CardKind.Slash && item.ConversionSource?.SkillId == "boundary:jianying");
        Play(game, fromEquipment);
        Drain(game);
        Require(game.CardMovements.Any(move => move.CardId == equip.Id && move.From == CardLocation.Equipment(0) &&
                    move.To == CardLocation.Processing), "The equipped card must be paid from its actual equipment zone.");
        EqualReplay(game, GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry));
        foreach (var physicalKind in new[] { "standard:alcohol", "standard:peach" })
        {
            var (sameKindGame, sameKindRegistry) = Create(boundary: true, physicalKind);
            if (physicalKind == "standard:peach") Use(sameKindGame, "lose");
            var sameKind = sameKindGame.GetHumanLegalActions().First(item =>
                item.ConversionSource?.SkillId == "boundary:jianying" &&
                item.PlayedCardKind == Hand(sameKindGame).Single(card => card.Id == item.CardId).Kind);
            Play(sameKindGame, sameKind);
            Drain(sameKindGame);
            Require(sameKindGame.GetHumanLegalActions().All(item => item.ConversionSource?.SkillId != "boundary:jianying"),
                "A same-kind conversion remains legal and consumes the common phase allowance.");
            EqualReplay(sameKindGame, GameReplay.Restore(RoundTrip(sameKindGame.CreateCheckpoint()), sameKindRegistry));
        }
    }

    public static void ShibeiCountsDamageInstancesAndResets()
    {
        foreach (var boundary in new[] { false, true })
        {
            var (game, registry) = Create(boundary);
            var full = Hp(game);
            Use(game, "lose");
            Require(Hp(game) == full - 1, "HP loss is not damage and must not trigger Shibei.");
            Use(game, "damage-two");
            Require(Hp(game) == full - 2, "Two-point first damage is one instance and heals exactly one HP.");
            Use(game, "heal");
            Use(game, "damage-one");
            Require(Hp(game) == full - 2, "The second damage loses one extra HP after damage.");
            Use(game, "heal");
            Use(game, "damage-one");
            Require(Hp(game) == full - 2, "Each later damage also loses one HP; it is not limited to the second.");
            EqualReplay(game, GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry));
            Use(game, "heal");
            NextTurn(game);
            Use(game, "damage-one");
            Require(Hp(game) == full, "A new turn restores Shibei's first-damage recovery.");
        }
    }

    public static void SharedDefinitionValidation()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var rules = registry.Skills["boundary:jianying"].Program!;
        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        using var rulesReader = new StreamReader(assembly.GetManifestResourceStream(
            "CardGame.Content.Standard.SkillPrograms.classic-ju-shou.rules.json")!);
        using var presentationReader = new StreamReader(assembly.GetManifestResourceStream(
            "CardGame.Content.Standard.SkillPrograms.classic-ju-shou.presentation.json")!);
        var json = rulesReader.ReadToEnd();
        var presentation = presentationReader.ReadToEnd();
        Reject(json.Replace("\"cardUseCommitted\"", "\"cardUseCompleted\""), presentation);
        Reject(json.Replace("\"afterDamageApplied\"", "\"turnEnding\""), presentation);
        Reject(json.Replace("\"usesPerPhase\": 1", "\"usesPerPhase\": 0"), presentation);
        var firstLimit = json.IndexOf("\"usesPerPhase\": 1", StringComparison.Ordinal);
        Reject(json.Remove(firstLimit, "\"usesPerPhase\": 1".Length)
            .Insert(firstLimit, "\"usesPerPhase\": 2"), presentation);
        Reject(json.Replace("\"forResponse\": false", "\"forResponse\": true"), presentation);
        Require(rules.ViewAs.All(rule => rule.UsesPerPhase == 1), "Fixture uses shared phase-limited conversion rules.");
    }

    private static void Reject(string rules, string presentation)
    {
        try { SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Invalid shared history or conversion context was accepted.");
    }

    private static string Skill(bool boundary) => boundary ? "boundary:jianying" : "classic:jianying";
    private static IReadOnlyList<CardSnapshot> Hand(GameEngine game) => game.CreateSnapshot(0, true).Players[0].Hand;
    private static int Hp(GameEngine game) => game.CreateSnapshot(0, true).Players[0].Hp;
    private static int Draws(GameEngine game) => game.CardMovements.Count(move =>
        move.To == CardLocation.Hand(0) && move.Reason.Value.Contains("jianying.Draw", StringComparison.Ordinal));

    private static void Use(GameEngine game, string id)
    {
        Accept(game.Submit(new UseProgramSkillCommand(0, Driver, id, [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
    }

    private static void PlayEquipment(GameEngine game, int id) => Play(game,
        game.GetHumanLegalActions().First(action => action.CardId == id && action.Kind == LegalActionKind.Equip));

    private static void Play(GameEngine game, LegalAction action) => Accept(game.Submit(
        new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision,
            game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources }));

    private static void Drain(GameEngine game, bool activate = true)
    {
        for (var i = 0; i < 250; i++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.ResolutionStack.Count == 0) return;
            Step(game, activate);
        }
        throw new InvalidOperationException("Ju Shou resolution did not return to play.");
    }

    private static void Step(GameEngine game, bool activate = true)
    {
        if (game.PendingDecision is not { } prompt)
        { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); return; }
        var choice = prompt.Choices.FirstOrDefault(item => item.Parameters.GetValueOrDefault("program-action") ==
            (activate ? "activate" : "skip")) ?? prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("response") is "take-damage" or "pass") ?? prompt.Choices.First();
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision)));
    }

    private static void NextTurn(GameEngine game)
    {
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
    }

    private static void EqualReplay(GameEngine game, GameEngine replay) => Require(
        SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, true)) &&
        game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).SequenceEqual(
            replay.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))),
        "Ju Shou checkpoint and events must replay exactly.");
    private static GameCheckpoint RoundTrip(GameCheckpoint value) => GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(value));
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool boundary, string physicalKind = "standard:crossbow")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(boundary, physicalKind));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, General, game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        return (game, registry);
    }

    private sealed class Fixture(bool boundary, string physicalKind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("ju-shou-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ju-shou-driver","revision":1,
                "minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
                 "effects":[{"op":"draw","target":"owner","amount":20},
                  {"op":"draw","target":"owner","amount":20},{"op":"draw","target":"owner","amount":20}]},
                {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                 "effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"heal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                 "effects":[{"op":"recover","target":"owner","amount":9}]},
                {"id":"damage-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                 "effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"damage-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                 "effects":[{"op":"damage","target":"owner","amount":2}]}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules,
                """{"schemaVersion":3,"skills":{"fixture:ju-shou-driver":{"name":"Fixture","description":"Fixture"}}}""");
            builder.AddSkill(new ContentSkillDefinition(Driver, "Fixture", "Fixture") { Program = catalog.Programs[Driver] });
            builder.AddGeneral(new ContentGeneralDefinition(General, "测试沮授", "ju_shou", Skill(boundary), "qun", BaseHp: 3,
                AdditionalSkillIds: ["classic:shibei", Driver]));
            var opponents = Enumerable.Range(1, 3).Select(index => $"fixture:ju-shou-bank-{index}").ToArray();
            foreach (var id in opponents)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试对手", "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddDeck(new ContentDeckRecipe("fixture:ju-shou-deck", "Fixture", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 208).Select(index => new ContentDeckPhysicalCard(
                physicalKind, (Suit)(index % 4), index / 4 % 13 + 1)).ToArray() });
            builder.AddMode(new ContentModeDefinition(Mode, "Fixture", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:ju-shou-deck", GeneralCandidateCount: 4, GeneralPoolIds: [General, .. opponents]));
        }
    }
}
