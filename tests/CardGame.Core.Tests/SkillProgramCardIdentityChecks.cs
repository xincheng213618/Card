using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillProgramCardIdentityChecks
{
    private const string ProgramId = "card-identity-test:wushen";
    private const string IdentityId = "heart-hand-as-slash";
    private const string OwnerGeneralId = "card-identity-test:owner";
    private const string ModeId = "identity:card-identity-test-4";

    internal static void Definitions()
    {
        var program = SkillProgramCatalog.Load(Rules, Presentation).Programs[ProgramId];
        var identity = program.CardIdentities.Single();
        var distance = program.Modifiers.Single();
        Require(program is { RuntimeVersion: "skill-program-v58", MinimumRulesVersion: 168 } &&
                identity is
                {
                    Id: IdentityId,
                    OutputKind: CardKind.Slash
                } &&
                identity.Zones.SequenceEqual([CardZoneKind.Hand]) &&
                identity.InputSuits.SequenceEqual([Suit.Heart]) &&
                distance is
                {
                    Query: SkillRuleQuery.SlashDistanceLimit,
                    Operation: SkillRuleOperation.Unlimited,
                    SourceCardIdentityId: IdentityId
                },
            "Schema 10 must keep mandatory hand identity and action-scoped Slash distance as separate bindings.");

        AssertReject(Rules.Replace("\"schemaVersion\":58", "\"schemaVersion\":57", StringComparison.Ordinal),
            "expected 58");
        AssertReject(Rules.Replace("\"sourceCardIdentityId\":\"heart-hand-as-slash\"",
                "\"sourceCardIdentityId\":\"missing\"", StringComparison.Ordinal),
            "unknown card identity");
        AssertReject(Rules.Replace("\"zones\":[\"hand\"]", "\"zones\":[\"equipment\"]", StringComparison.Ordinal),
            "owner hand zone");
    }

    internal static void MandatoryIdentityDistanceAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 19,
            PlayerCount = 4,
            ModeId = ModeId,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted,
            "The card-identity fixture could not start.");
        var setup = game.PendingDecision ?? throw new InvalidOperationException("General selection was not exposed.");
        Require(game.Submit(new SelectGeneralCommand(
                0, OwnerGeneralId, game.Revision, setup.PromptId)).Accepted,
            "The card-identity owner could not be selected.");
        AdvanceToHumanPlay(game);

        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Play prompt was not exposed.");
        var slashChoices = prompt.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("action") == "slash").ToArray();
        var far = slashChoices.FirstOrDefault(choice => choice.Targets.SequenceEqual([2])) ??
            throw new InvalidOperationException("A mandatory heart Slash did not ignore distance to seat 2.");
        var cardId = far.Cards.Single();
        Require(far.Parameters.GetValueOrDefault("conversion-skill-id") == ProgramId &&
                far.Parameters.GetValueOrDefault("conversion-binding-id") == IdentityId &&
                !prompt.Choices.Any(choice =>
                    choice.Cards.Contains(cardId) &&
                    choice.Parameters.GetValueOrDefault("action") == "equip"),
            "A heart equipment card must expose only its mandatory Slash identity, not its physical Equip action.");

        var restoredAtPrompt = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Play(game, far);
        Play(restoredAtPrompt, restoredAtPrompt.PendingDecision!.Choices.Single(choice => choice.Id == far.Id));
        AdvanceUntilResolutionSettles(game);
        AdvanceUntilResolutionSettles(restoredAtPrompt);

        var accepted = game.Events.Select(item => item.Payload)
            .OfType<CardActionAcceptedEvent>()
            .Single(item => item.Action.PhysicalCards.Any(card => card.CardId == cardId));
        Require(accepted.Action.EffectiveKind == CardKind.Slash &&
                accepted.Action.PhysicalCards.Single().CardKind == CardKind.OffensiveHorse &&
                accepted.Action.ConversionChain.Single() is
                {
                    SkillId: ProgramId,
                    BindingId: IdentityId,
                    OwnerSeat: 0
                },
            "The accepted action must freeze physical identity, effective Slash and the exact state binding occurrence.");
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } next &&
                !next.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "slash"),
            "The mandatory identity may ignore distance but must not bypass the normal once-per-play-phase Slash limit.");
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(restoredAtPrompt.CreateSnapshot(0, revealAll: true)) &&
                game.Events.Select(item => item.Payload.GetType().Name)
                    .SequenceEqual(restoredAtPrompt.Events.Select(item => item.Payload.GetType().Name)),
            "A paused mandatory card-identity action did not replay exactly.");

    }

    private static void AdvanceToHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "Could not advance to the human play phase.");
        }
        throw new InvalidOperationException("The card-identity fixture did not reach human play.");
    }

    private static void AdvanceUntilResolutionSettles(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.ResolutionStack.Count == 0 &&
                game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "The card-identity action could not finish.");
        }
        throw new InvalidOperationException("The card-identity action did not settle within the bounded steps.");
    }

    private static void Play(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Expected prompt was lost.");
        var playedKind = Enum.Parse<CardKind>(choice.Parameters["played-card-kind"], ignoreCase: false);
        var conversion = new CardConversionSource(
            choice.Parameters["conversion-skill-id"],
            choice.Parameters["conversion-binding-id"],
            int.Parse(choice.Parameters["conversion-owner-seat"], System.Globalization.CultureInfo.InvariantCulture),
            choice.Parameters["conversion-instance-id"]);
        var result = game.Submit(new PlayCardCommand(
            prompt.PlayerSeat,
            choice.Cards.Single(),
            choice.Targets,
            game.Revision,
            prompt.PromptId,
            playedKind)
        {
            ConversionSource = conversion
        });
        Require(result.Accepted, result.Error?.Message ?? "The card-identity choice was rejected.");
    }

    private static void AssertReject(string rules, string expected)
    {
        try { _ = SkillProgramCatalog.Load(rules, Presentation); }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidOperationException($"Expected rejection containing '{expected}'.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixturePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(
            "card-identity-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            var program = catalog.Programs[ProgramId];
            var text = catalog.Presentations[ProgramId];
            builder.AddSkill(new ContentSkillDefinition(ProgramId, text.Name, text.Description)
            {
                Program = program
            });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "牌身份测试", "guan_yu", ProgramId, "shu", BaseHp: 8));
            var others = Enumerable.Range(1, 3).Select(index => $"card-identity-test:other-{index}").ToArray();
            foreach (var id in others)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "牌身份陪测", "cao_cao", "standard:none", "wei", BaseHp: 8));
            builder.AddDeck(new ContentDeckRecipe(
                "card-identity-test:deck",
                "全红桃赤兔牌堆",
                4,
                2,
                [])
            {
                PhysicalCards = Enumerable.Range(0, 80)
                    .Select(_ => new ContentDeckPhysicalCard("standard:offensive_horse", Suit.Heart, 5))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "持续牌身份场景",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "card-identity-test:deck",
                GeneralCandidateCount: 4,
                GeneralPoolIds: [OwnerGeneralId, .. others]));
        }
    }

    private const string Rules = """
        {"schemaVersion":58,"skills":[
          {"id":"card-identity-test:wushen","revision":1,
           "modifiers":[
             {"id":"slash-distance","priority":0,"query":"slashDistanceLimit","operation":"unlimited","value":0,
              "sourceCardIdentityId":"heart-hand-as-slash","condition":{"kind":"always"}}
           ],
           "cardIdentities":[
             {"id":"heart-hand-as-slash","zones":["hand"],"inputKinds":[],"inputSuits":["heart"],
              "outputKind":"slash","condition":{"kind":"always"}}
           ]}
        ]}
        """;

    private const string Presentation = """
        {"schemaVersion":3,"skills":{
          "card-identity-test:wushen":{"name":"武神测试","description":"红桃手牌持续视为杀，且无距离限制。"}
        }}
        """;
}
