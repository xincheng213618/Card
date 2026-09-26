using CardGame.Content.Standard;
using CardGame.Core;
using System.Reflection;

internal static class ProgramLifecycleChecks
{
    private const int HumanSeat = 0;

    public static void TypedTriggerComparisonsAndWindowBoundaries()
    {
        var comparison = """
            {"kind":"compare","left":{"kind":"cardsUsedThisTurn"},
            "operator":"greaterThanOrEqual","right":{"kind":"currentHp"}}
            """;
        var program = SkillProgramCatalog.Load(
            Rules13(comparison, "playEnding"), Presentation).Programs[FixturePackage.SkillId];
        var condition = program.Triggers.Single().Condition;
        Require(condition.Evaluate(new SkillProgramTriggerFacts(4, 4, true)) &&
                !condition.Evaluate(new SkillProgramTriggerFacts(3, 4, true)),
            "The typed comparison must compare frozen turn-use and HP facts without a skill-specific condition.");

        var reverseWithConstant = """
            {"kind":"compare","left":{"kind":"integerConstant","value":3},
            "operator":"lessThan","right":{"kind":"currentHp"}}
            """;
        var reverse = SkillProgramCatalog.Load(
            Rules13(reverseWithConstant, "turnStartBeforeNormalFlow"), Presentation)
            .Programs[FixturePackage.SkillId].Triggers.Single().Condition;
        Require(reverse.Evaluate(new SkillProgramTriggerFacts(0, 4, false)) &&
                !reverse.Evaluate(new SkillProgramTriggerFacts(0, 3, false)),
            "The same Compare node must support a constant left operand and reverse ordering.");

        var turnEnding = SkillProgramCatalog.Load(
            Rules13(comparison, "turnEnding"), Presentation).Programs[FixturePackage.SkillId];
        Require(turnEnding.Triggers.Single().Condition.Evaluate(
                new SkillProgramTriggerFacts(4, 4, true)),
            "TurnEnding must accept the same serializable frozen trigger facts as PlayEnding.");

        var exactFaceStateRules = Rules13("{\"kind\":\"always\"}", "turnEnding").Replace(
            "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}",
            "{\"op\":\"setFaceState\",\"target\":\"owner\",\"faceDown\":true}",
            StringComparison.Ordinal);
        var exactFaceState = SkillProgramCatalog.Load(exactFaceStateRules, Presentation)
            .Programs[FixturePackage.SkillId].Triggers.Single().Effects.Single();
        Require(exactFaceState is { Op: SkillProgramEffectOp.SetFaceState, FaceDown: true },
            "The current program must preserve the exact public face-state value.");
        var playEndingFaceState = SkillProgramCatalog.Load(
            exactFaceStateRules.Replace("turnEnding", "playEnding", StringComparison.Ordinal),
            Presentation).Programs[FixturePackage.SkillId].Triggers.Single().Effects.Single();
        Require(playEndingFaceState is { Op: SkillProgramEffectOp.SetFaceState, FaceDown: true },
            "The shared face-state operation must also accept a PlayEnding window.");

        var afterDamage = SkillProgramCatalog.Load(
            Rules13(comparison, "afterDamageApplied").Replace("\"priority\":0,",
                "\"damageOccurrence\":\"perDamagePoint\",\"priority\":0,", StringComparison.Ordinal),
            Presentation).Programs[FixturePackage.SkillId].Triggers.Single().Condition;
        Require(afterDamage.Evaluate(new SkillProgramTriggerFacts(4, 4, true)),
            "AfterDamageApplied must accept the same frozen public comparison facts.");

        foreach (var unsupported in new[] { "selfDyingResponse" })
        {
            try
            {
                _ = SkillProgramCatalog.Load(Rules13(comparison, unsupported), Presentation);
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("trigger conditions require", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            throw new InvalidOperationException(
                $"The {unsupported} window must reject trigger conditions until it freezes matching facts.");
        }
    }

    public static void RejectsUnsupportedEventUsageScope()
    {
        static string WithUsageScope(string rules, string scope) => rules.Replace(
            "\"priority\":0,",
            $"\"priority\":0,\"usageScope\":\"{scope}\",\"usageLimit\":1,",
            StringComparison.Ordinal);

        var rules = Rules("{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}");
        foreach (var supported in new[] { "game", "round", "turn", "phase" })
            _ = SkillProgramCatalog.Load(WithUsageScope(rules, supported), Presentation);

        try
        {
            _ = SkillProgramCatalog.Load(WithUsageScope(rules, "event"), Presentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("event usage scope is not supported", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException(
            "Lifecycle programs must reject event-scoped usage until the runtime carries event identity.");
    }

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

    public static void SuccessfulProgramsCleanupUnconsumedTemporaryCards()
    {
        var incomplete = Rules("""
            {"op":"revealTopCards","target":"owner","amount":1,"resultBind":"held","visibility":"public"}
            """);
        try { _ = Registry(incomplete); }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("not fully consumed", StringComparison.OrdinalIgnoreCase))
        {
            goto RejectedUnconsumedBinding;
        }
        throw new InvalidOperationException("A revealed card without an explicit disposition must be rejected.");

        RejectedUnconsumedBinding:
        var registry = Registry(Rules("""
            {"op":"revealTopCards","target":"owner","amount":1,"resultBind":"held","visibility":"public"},
            {"op":"moveBoundCards","target":"owner","sourceBind":"held","destination":"discardPile"}
            """));
        var game = CreateAndSelect(registry);
        ReachHumanPlay(game);

        var reveal = game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>().Single();
        var cardId = reveal.Cards.Single().Id;
        Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Single(item => item.SkillId == FixturePackage.SkillId) is { Completed: true } &&
                game.CardMovements.Any(move => move.CardId == cardId &&
                    move.To == CardLocation.DiscardPile) &&
                game.CreateSnapshot(HumanSeat).PublicRevealedCards.Count == 0,
            "A valid program must explicitly dispose its revealed card before returning to the turn.");

        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "Explicit temporary-card disposition must replay exactly.");
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

    public static void AiOptionalProgramsUseTheProgramRouterAndResumeSubsetChoices()
    {
        var registry = Registry(Rules("""
            {"op":"revealTopCards","target":"owner","amount":1,"resultBind":"held","visibility":"public"},
            {"op":"selectCardSubset","target":"owner","sourceBind":"held","resultBind":"selected","minimumCards":0,"maximumCards":1,"maximumRankSum":13,"aiOrder":"mostCardsThenRankSum"},
            {"op":"moveBoundCards","target":"owner","sourceBind":"selected","destination":"ownerHand"},
            {"op":"moveBoundCards","target":"owner","sourceBind":"held","exceptBind":"selected","destination":"discardPile"}
            """, optional: true));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 11,
            PlayerCount = 4,
            ModeId = FixturePackage.ModeId,
            HumanSeat = -1,
            HumanRole = null,
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The AI lifecycle fixture failed to start.");
        for (var step = 0; step < 32 &&
             !game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                 .Any(item => item.SkillId == FixturePackage.SkillId); step++)
        {
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The AI lifecycle fixture could not advance.");
        }
        Require(game.Events.Select(item => item.Payload).OfType<ProgramCardSubsetSelectedEvent>()
                    .Any(item => item.SkillId == FixturePackage.SkillId && item.CardIds.Count == 1) &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == FixturePackage.SkillId && item.Completed),
            "An AI optional ProgramTrigger must activate through its own router and resume after subset choice.");
    }

    public static void SameSkillInstanceAcrossSourcesProducesOneCandidate()
    {
        var registry = Registry(Rules(
            "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}"));
        var game = CreateAndSelect(registry);
        ReachHumanPlay(game);
        var players = (IReadOnlyList<CharacterState>)(typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The lifecycle players are unavailable."));
        var owner = players[HumanSeat];
        var template = owner.SkillGrants.Grants.Single(grant =>
            grant.SkillId == FixturePackage.SkillId &&
            grant.SourceId == CharacterState.PrimarySkillSource);
        owner.SkillGrants.Grant(new SkillGrant(
            "equipment:test",
            template.SkillId,
            template.SkillInstanceId,
            "equipment:test"));

        var collect = typeof(GameEngine).GetMethod(
            "CollectProgramTriggerCandidates",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The lifecycle candidate collector is unavailable.");
        ProgramTriggerCandidate[] Candidates() =>
            ((IReadOnlyList<ProgramTriggerCandidate>)collect.Invoke(game,
                [owner, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, 0])!).ToArray();

        Require(Candidates().Length == 1,
            "Two grants of the same skill instance must not subscribe the same binding twice.");
        owner.SkillGrants.RemoveGrant(template.GrantId);
        Require(Candidates().Length == 1,
            "Removing one source must keep the shared skill instance active through its remaining source.");
        owner.SkillGrants.SetEnabled("equipment:test", false);
        Require(Candidates().Length == 0,
            "Disabling the last source must remove the shared skill instance candidate.");
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
        {"schemaVersion":60,"skills":[{"id":"{{FixturePackage.SkillId}}","revision":1,
        "minimumRulesVersion":170,"modifiers":[],"viewAs":[],"activations":[],
        "triggers":[{"id":"binding","window":"{{window}}","subject":"owner","optional":{{optional.ToString().ToLowerInvariant()}},
        {{occurrence}}"priority":0,"effects":[{{effects}}]}],"contributions":[],"cardIdentities":[]}]}
        """;
    }

    private static string Rules13(string condition, string window) => $$"""
        {"schemaVersion":60,"skills":[{"id":"{{FixturePackage.SkillId}}","revision":1,
        "minimumRulesVersion":170,"modifiers":[],"viewAs":[],"activations":[],
        "triggers":[{"id":"binding","window":"{{window}}","subject":"owner","optional":true,
        "condition":{{condition}},"priority":0,"effects":[{"op":"draw","target":"owner","amount":1}]}],
        "contributions":[],"cardIdentities":[]}]}
        """;

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
