using CardGame.Content.Standard;
using CardGame.Core;
using System.Reflection;

internal static class ProgramLifecycleChecks
{
    private const int HumanSeat = 0;



    public static void RejectsCardBindingsAcrossDetachedPhaseBoundary()
    {
        var rules = Rules("""
            {"op":"revealTopCards","target":"owner","amount":1,"resultBind":"held","visibility":"public"},
            {"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"},
            {"op":"moveBoundCards","target":"owner","sourceBind":"held","destination":"discardPile"}
            """);
        try
        {
            _ = SkillProgramCatalog.Load(rules, Presentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("card bindings cannot cross", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException("A phase insertion with live card bindings must fail at content load time.");
    }


    public static void MissingConditionalBindingsCancelWithoutLeakingCards()
    {
        var rules = Rules("""
            {"op":"revealTopCards","target":"owner","amount":1,"resultBind":"held","visibility":"public","condition":{"kind":"wounded"}},
            {"op":"selectCardSubset","target":"owner","sourceBind":"held","resultBind":"selected","minimumCards":0,"maximumCards":1,"maximumRankSum":13,"aiOrder":"mostCardsThenRankSum"}
            """);
        try { _ = Registry(rules); }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("resource operations must be always", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException("A conditional binding producer must be rejected before it can leak cards.");
    }

    public static void CancelledDamageProgramsDoNotCleanupParentAttackCards()
    {
        var registry = Registry(Rules("""
            {"op":"revealTopCards","target":"owner","amount":1,"resultBind":"held","visibility":"public"},
            {"op":"moveBoundCards","target":"owner","sourceBind":"held","destination":"discardPile"}
            """, "afterDamageApplied"));
        var game = CreateAndSelect(registry);
        ReachHumanPlay(game);
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var slash = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 1);
        var sourceCardId = slash.CardId!.Value;
        var played = game.Submit(new PlayCardCommand(
            HumanSeat,
            sourceCardId,
            slash.TargetSeats,
            game.Revision,
            prompt.PromptId,
            slash.PlayedCardKind,
            slash.TargetCardId)
        {
            ConversionSource = slash.ConversionSource
        });
        Require(played.Accepted, played.Error?.Message ?? "The lifecycle fixture Slash was rejected.");
        AdvanceUntilProgramResolved(game);

        Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == FixturePackage.SkillId && item.Completed) &&
                game.CardMovements.Where(move => move.CardId == sourceCardId)
                    .All(move => !move.Reason.Value.Contains("skill-program", StringComparison.Ordinal)),
            "Program disposal must move only its own bound cards, never the parent attack card.");
    }



    private static void AdvanceUntilProgramResolved(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            if (game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                .Any(item => item.SkillId == FixturePackage.SkillId)) return;
            if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt)
            {
                var decline = prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0);
                if (decline is null) throw new InvalidOperationException($"Unexpected prompt {prompt.Kind}.");
                var answered = game.Submit(new AnswerPromptCommand(
                    HumanSeat, prompt.PromptId, decline.Id, game.Revision));
                Require(answered.Accepted, answered.Error?.Message ?? "The lifecycle fixture prompt was rejected.");
                continue;
            }
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The lifecycle fixture could not advance.");
        }
        throw new InvalidOperationException("The lifecycle fixture did not resolve its program in bounded steps.");
    }

    private static GameEngine CreateAndSelect(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7,
            PlayerCount = 4,
            ModeId = FixturePackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The lifecycle fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            FixturePackage.OwnerGeneralId,
            game.Revision,
            selection.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The lifecycle fixture could not select its general.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 1024; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Play.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The lifecycle fixture could not advance.");
        }
        throw new InvalidOperationException("The lifecycle fixture did not reach human Play in bounded steps.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static ContentRegistry Registry(string rules)
    {
        var program = SkillProgramCatalog.Load(rules, Presentation).Programs[FixturePackage.SkillId];
        return ContentRegistry.Build(new StandardContentPackage(), new FixturePackage(program));
    }

    private static string Rules(
        string effects,
        string window = "turnStartBeforeNormalFlow",
        bool optional = false)
    {
        var occurrence = window == "afterDamageApplied" ? "\"damageOccurrence\":\"perDamagePoint\"," : "";
        return $$"""
        {"schemaVersion":62,"skills":[{"id":"{{FixturePackage.SkillId}}","revision":1,
        "minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],
        "triggers":[{"id":"binding","window":"{{window}}","subject":"owner","optional":{{optional.ToString().ToLowerInvariant()}},
        {{occurrence}}"priority":0,"effects":[{{effects}}]}],"contributions":[],"cardIdentities":[]}]}
        """;
    }


    private const string Presentation =
        "{\"schemaVersion\":3,\"skills\":{\"fixture:lifecycle\":{\"name\":\"Lifecycle\",\"description\":\"Fixture\"}}}";

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixturePackage(SkillProgram program) : IGameContentPackage
    {
        public const string SkillId = "fixture:lifecycle";
        public const string OwnerGeneralId = "fixture:lifecycle-owner";
        public const string ModeId = "identity:lifecycle-program-test-4";
        private const string DeckId = "fixture:lifecycle-deck";

        public PackageManifest Manifest { get; } = new("lifecycle-program-test", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new ContentSkillDefinition(SkillId, "Lifecycle", "Fixture")
            {
                Program = program,
                ExecutionForms = SkillExecutionForm.Trigger
            });
            var generalIds = Enumerable.Range(0, 4).Select(index => index == 0
                ? OwnerGeneralId
                : $"fixture:lifecycle-owner-{index}").ToArray();
            foreach (var id in generalIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "Lifecycle Owner", "supporter", SkillId, "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "Lifecycle Deck",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 96)
                    .Select(index => new ContentDeckPhysicalCard(
                        "standard:slash", (Suit)(index % 4), index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "Lifecycle Program Test",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: generalIds));
        }
    }
}
