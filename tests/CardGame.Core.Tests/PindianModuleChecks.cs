using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PindianModuleChecks
{
    private const string ProgramSkillId = "fixture:program-pindian";
    private const string ProgramRules =
        """{"schemaVersion":29,"skills":[{"id":"fixture:program-pindian","revision":1,"minimumRulesVersion":134,"modifiers":[],"viewAs":[],"activations":[{"id":"contest","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerTurn":1,"condition":{"kind":"always"},"effects":[{"op":"pindian","target":"selectedTarget","amount":1,"condition":{"kind":"always"}},{"op":"draw","target":"owner","amount":2,"condition":{"kind":"pindianNotWon"}}]}],"triggers":[],"contributions":[],"cardIdentities":[]}]}""";
    private const string ProgramPresentation =
        """{"schemaVersion":1,"skills":{"fixture:program-pindian":{"name":"程序拼点","description":"拼点未赢摸两张牌。"}}}""";

    public static void ActiveProgramPindianSuspendsConditionsAndReplays()
    {
        var program = SkillProgramCatalog.Load(ProgramRules, ProgramPresentation).Programs[ProgramSkillId];
        Require(program.RuntimeVersion == "skill-program-v29" &&
                program.Activations.Single().Effects.Select(effect => effect.Op).SequenceEqual([
                    SkillProgramEffectOp.Pindian,
                    SkillProgramEffectOp.Draw
                ]),
            "Schema 29 must publish Pindian as a reusable active-program primitive.");

        var registry = ProgramRegistry(program);
        var game = CreateProgram(registry);
        var play = Prompt(game);
        var legal = game.GetHumanLegalActions();
        var action = legal.SingleOrDefault(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill && candidate.ProgramSkillId == ProgramSkillId) ??
            throw new InvalidOperationException(
                $"Program Pindian action missing: {string.Join(" | ", legal.Select(item => $"{item.Kind}:{item.ProgramSkillId}:{item.ProgramActivationId}"))}");
        var sourceCard = game.CreateSnapshot(0).Players[0].Hand[0];
        var before = game.CreateSnapshot(0).Players[0].HandCount;
        Accept(game.Submit(new UseProgramSkillCommand(
            0, ProgramSkillId, action.ProgramActivationId!, [sourceCard.Id], [1],
            game.Revision, play.PromptId)));
        Require(game.ResolutionStack[0] is ProgramSkillFrame { PindianResultBindings.Count: 0 } &&
                game.ResolutionStack[^1] is PindianFrame,
            "The active program must commit its instruction cursor before suspending into Pindian.");

        Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Require(game.ResolutionStack[0] is ProgramSkillFrame { PindianResultBindings.Count: 0 } &&
                game.ResolutionStack[^1] is PindianFrame
                {
                    Result: { SourceSeat: 0, OpponentSeat: 1, SourceRank: 7, OpponentRank: 7 }
                } && Prompt(game).SkillPrompt!.SkillId == "fixture:claim-a",
            "The child must freeze its Pindian result while result modules are still pending.");
        var restored = VerifyReplay(game, registry);
        for (var step = 0; step < 12 && game.ResolutionStack.Any(frame => frame is PindianFrame); step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 }) Answer(game, 1);
            else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            if (restored.PendingDecision is { PlayerSeat: 0 }) Answer(restored, 1);
            else Accept(restored.Submit(new AdvanceOneStepCommand(restored.Revision)));
        }

        Require(game.ResolutionStack.Count == 0 &&
                game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Single() is
                    { SkillId: ProgramSkillId, Completed: true } &&
                game.CreateSnapshot(0).Players[0].HandCount == before + 1,
            "A tied Pindian must discard the selected source card, execute only the not-won branch and complete once.");
        Equivalent(game, restored);
    }

    public static void PhaseChildPreservesPrivacyCardsAndReplay()
    {
        var registry = Registry();
        var game = Create(registry);
        var before = game.CreateSnapshot(0).Players[0].HandCount;
        Answer(game, 0);
        var selection = Prompt(game);
        Require(selection.ValidTargetSeats.Count == 5 && selection.Choices.All(choice =>
            choice.Cards.Count == 1 && choice.Targets.Count == 1 &&
            game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == choice.Cards[0])),
            "Source selection must contain only its own cards and living nonempty opponents.");
        Require(game.CreateSnapshot(1).PendingDecision is null && game.CreateSnapshot(1).PublicRevealedCards.Count == 0,
            "Neither selected hand cards nor a private source prompt may leak before both commit.");
        var revision = game.Revision;
        Require(!game.Submit(new AnswerPromptCommand(1, selection.PromptId, selection.Choices[0].Id, revision)).Accepted &&
            !game.Submit(new AnswerPromptCommand(0, selection.PromptId, new("forged"), revision)).Accepted &&
            game.Revision == revision, "Invalid owner/choice must not consume a module operation.");
        VerifyReplay(game, registry);
        Answer(game, 0);
        Require(game.PendingDecision is null && game.CreateSnapshot(1).PendingDecision is { IsPrivate: true } &&
            game.CreateSnapshot(0).PublicRevealedCards.Count == 0,
            "The opponent privately chooses before either card becomes public.");
        VerifyReplay(game, registry);
        Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        var first = Prompt(game);
        var result = game.Events.Select(item => item.Payload).OfType<PindianResultDeterminedEvent>().Single().Result;
        Require(!result.WonBy(0) && !result.WonBy(1) && result.SourceRank == result.OpponentRank,
            "A tied contest must not report either participant as winning.");
        Require(game.CreateSnapshot(2).PublicRevealedCards.Select(card => card.Id).Order()
            .SequenceEqual(new[] { result.SourceCardId, result.OpponentCardId }.Order()),
            "Both committed cards must remain publicly visible at the result window.");
        Require(first.SkillPrompt!.SkillId == "fixture:claim-a" && game.CreateSnapshot(1).PendingDecision is null,
            "Result candidates must start in deterministic owner/binding order and keep their prompt private.");
        var restored = VerifyReplay(game, registry);
        Answer(game, 1); Answer(restored, 1); // Skip the first binding; second may still claim the same card.
        Require(Prompt(game).SkillPrompt!.SkillId == "fixture:claim-b", "Skipping must advance one candidate only.");
        Require(!game.Submit(new AnswerPromptCommand(0, first.PromptId, first.Choices[0].Id, game.Revision)).Accepted,
            "An old result prompt cannot claim the next candidate's card.");
        Answer(game, 0); Answer(restored, 0);
        Require(game.ResolutionStack.Count == 0 && game.State.Phase == TurnPhase.Play &&
            game.CreateSnapshot(0).Players[0].HandCount == before + 3,
            "One draw before the child and two after it must execute exactly once; source card returns to hand.");
        var zones = game.CreateCardZoneDiagnostics();
        Require(zones.Single(card => card.CardId == result.SourceCardId).Location == CardLocation.Hand(0) &&
            zones.Single(card => card.CardId == result.OpponentCardId).Location == CardLocation.DiscardPile &&
            zones.All(card => card.Location != CardLocation.Processing),
            "Claimed cards must not be discarded again; unclaimed cards must leave Processing exactly once.");
        Require(game.Events.Select(item => item.Payload).OfType<PindianCardClaimedEvent>().Count() == 1,
            "Later candidates must re-query availability instead of claiming an already moved physical card.");
        Equivalent(game, restored);
        VerifyReplay(game, registry);
        Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Resume must reach ordinary Play without re-offering the start activation.");
    }

    public static void DeclineAndEmptyOpponentsDoNotStartAChild()
    {
        var game = Create(Registry());
        var count = game.CreateSnapshot(0).Players[0].HandCount;
        Answer(game, 1);
        Require(game.ResolutionStack.Count == 0 && game.CreateSnapshot(0).Players[0].HandCount == count &&
            !game.Events.Any(item => item.Payload is PindianResultDeterminedEvent),
            "Skipping activation must not draw, select, or start a contest.");
        var emptyRegistry = Registry(emptyOpponents: true);
        var empty = Create(emptyRegistry, expectActivation: false);
        Require(empty.PendingDecision?.Kind == DecisionKind.PlayCard && empty.ResolutionStack.Count == 0,
            "A source with cards but no eligible opponent must go directly to ordinary Play.");
    }

    public static void AiUsesPrivateChoicesAndSkipsConsumedCandidates()
    {
        var registry = Registry();
        var game = Create(registry);
        Answer(game, 1);
        for (var step = 0; step < 160; step++)
        {
            var module = game.Events.Select(item => item.Payload).OfType<SkillModuleResolvedEvent>()
                .FirstOrDefault(item => item.OwnerSeat != 0 && item.SkillId == "fixture:phase-pindian");
            if (module is not null)
            {
                Require(module.Used, "The AI must activate through the same module choices.");
                var claims = game.Events.Select(item => item.Payload).OfType<SkillModuleResolvedEvent>()
                    .Where(item => item.OwnerSeat == module.OwnerSeat && item.SkillId.StartsWith("fixture:claim", StringComparison.Ordinal)).ToArray();
                Require(claims.Length == 2 && !claims[0].Used && claims[1].Used,
                    "AI result policy must choose published skip/use options and recheck later candidates.");
                VerifyReplay(game, registry);
                return;
            }
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard } play)
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId)));
            else if (game.PendingDecision is { } prompt) Answer(game, prompt.Choices.Count - 1);
            else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException("Fixed AI scenario did not complete a phase Pindian.");
    }

    public static void ResultModuleRevisionChangesFingerprint()
    {
        var first = Registry();
        var second = Registry(revision: 2);
        Require(first.ContentHash != second.ContentHash, "Result module revisions must participate in gameplay fingerprints.");
        var game = Create(first);
        try { GameReplay.Restore(game.CreateCheckpoint(), second); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("A save must reject a changed result module revision.");
    }

    public static void ExistingActiveSkillsResumeAfterResultModules()
    {
        foreach (var skill in new[] { SkillKind.Tianyi, SkillKind.Xianzhen, SkillKind.Quhu })
        {
            var registry = Registry(legacy: skill);
            var game = Create(registry, expectActivation: false);
            var play = Prompt(game);
            var card = game.CreateSnapshot(0).Players[0].Hand[0];
            if (skill == SkillKind.Xianzhen)
            {
                var action = game.GetHumanLegalActions().Single(item =>
                    item.Kind == LegalActionKind.UseProgramSkill &&
                    item.ProgramSkillId == "classic:xianzhen" &&
                    item.ProgramActivationId == "challenge");
                Require(action.SelectableTargetSeats.Contains(1),
                    "The composition Xianzhen activation must expose the shared Pindian opponent.");
                Accept(game.Submit(new UseProgramSkillCommand(
                    0, "classic:xianzhen", "challenge", [], [1], game.Revision, play.PromptId)));
                var sourcePrompt = Prompt(game);
                Accept(game.Submit(new AnswerPromptCommand(
                    0, sourcePrompt.PromptId,
                    sourcePrompt.Choices.Single(choice => choice.Cards.SequenceEqual([card.Id])).Id,
                    game.Revision)));
            }
            else
            {
                Accept(game.Submit(new UseSkillCommand(0, skill, [card.Id], [1], game.Revision, play.PromptId)));
            }
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            Require(Prompt(game).SkillPrompt!.SkillId == "fixture:claim-a" &&
                (skill == SkillKind.Xianzhen
                    ? game.ResolutionStack[0] is ProgramSkillFrame { SkillId: "classic:xianzhen" }
                    : game.ResolutionStack[0] is ActiveSkillFrame { Skill: var parent } && parent == skill),
                "An existing active skill must suspend at the shared result-module prompt.");
            var restored = VerifyReplay(game, registry);
            Answer(game, 0); Answer(restored, 0);
            // Quhu's tied contest continues into damage to its source; the other skills finish immediately.
            for (var step = 0; step < 24 && game.ResolutionStack.Count > 0; step++)
            {
                if (game.PendingDecision is { } prompt)
                { Answer(game, prompt.Choices.Count - 1); Answer(restored, prompt.Choices.Count - 1); }
                else
                { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); Accept(restored.Submit(new AdvanceOneStepCommand(restored.Revision))); }
            }
            Require((skill == SkillKind.Xianzhen
                        ? game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>()
                            .Count(item => item.SkillId == "classic:xianzhen" && item.Completed) == 1
                        : game.Events.Select(item => item.Payload).OfType<ActiveSkillResolvedEvent>()
                            .Count(item => item.Skill == skill) == 1) &&
                game.Events.Select(item => item.Payload).OfType<PindianCardClaimedEvent>().Count() == 1 && game.ResolutionStack.Count == 0,
                "The legacy parent's own result effect and completion must execute once after card acquisition.");
            Equivalent(game, restored);
        }
    }

    public static void BothParticipantsCanClaimWithoutExposingTheirPrompts()
    {
        var registry = Registry(claimOwnCards: true);
        var game = Create(registry);
        Answer(game, 0); Answer(game, 0);
        Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Answer(game, 1); Answer(game, 0);
        Require(game.PendingDecision is null && game.CreateSnapshot(1).PendingDecision is { IsPrivate: true } &&
            game.CreateSnapshot(2).PendingDecision is null && game.CreateSnapshot(2).PublicRevealedCards.Count == 1,
            "The next owner's result prompt must stay private while the remaining physical card is public.");
        var replay = VerifyReplay(game, registry);
        for (var step = 0; step < 2; step++)
        {
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            Accept(replay.Submit(new AdvanceOneStepCommand(replay.Revision)));
        }
        var claims = game.Events.Select(item => item.Payload).OfType<PindianCardClaimedEvent>().ToArray();
        Require(claims.Length == 2 && claims[0].OwnerSeat == 0 && claims[1].OwnerSeat == 1 &&
            game.CreateCardZoneDiagnostics().Where(card => claims.Any(claim => claim.CardId == card.CardId))
                .All(card => card.Location == CardLocation.Hand(claims.Single(claim => claim.CardId == card.CardId).OwnerSeat)) &&
            game.ResolutionStack.Count == 0, "Each participant must acquire exactly its own card before the parent resumes.");
        Equivalent(game, replay);
    }

    private static ContentRegistry Registry(bool emptyOpponents = false, int revision = 1, SkillKind? legacy = null,
        bool claimOwnCards = false) =>
        legacy is null
            ? ContentRegistry.Build(new StandardContentPackage(), new FixturePackage(emptyOpponents, revision, ClaimOwnCards: claimOwnCards))
            : ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
                new StandardRescueSkillExpansionPackage(),
                new StandardClassicGeneralPackage(),
                new FixturePackage(emptyOpponents, revision, legacy));

    private static ContentRegistry ProgramRegistry(SkillProgram program) =>
        ContentRegistry.Build(new StandardContentPackage(), new ProgramFixturePackage(program));

    private static GameEngine CreateProgram(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7001, PlayerCount = 6,
            ModeId = "identity:classic-pindian-fixture", HumanSeat = 0, HumanRole = Role.Lord,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false, MaxTurns = 20 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        var setup = game.PendingDecision!;
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:pindian-general-0", game.Revision, setup.PromptId)));
        for (var step = 0; step < 24; step++)
        {
            if (game.PendingDecision?.Kind == DecisionKind.PlayCard) return game;
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException("The fixed program Pindian play boundary was not reached.");
    }

    private static GameEngine Create(ContentRegistry registry, bool expectActivation = true)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7001, PlayerCount = 6,
            ModeId = "identity:classic-pindian-fixture", HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 20 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        var setup = game.PendingDecision!;
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:pindian-general-0", game.Revision, setup.PromptId)));
        for (var step = 0; step < 24; step++)
        {
            if (expectActivation ? game.PendingDecision?.SkillPrompt?.SkillId == "fixture:phase-pindian"
                : game.PendingDecision?.Kind == DecisionKind.PlayCard) return game;
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException("The fixed phase Pindian boundary was not reached.");
    }

    private static PendingDecision Prompt(GameEngine game) => game.PendingDecision ??
        throw new InvalidOperationException("Expected a private human prompt.");
    private static void Answer(GameEngine game, int index)
    {
        var prompt = Prompt(game);
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices[index].Id, game.Revision)));
    }
    private static GameEngine VerifyReplay(GameEngine game, ContentRegistry registry)
    {
        var serializedFrames = JsonSerializer.Serialize(game.ResolutionStack);
        var frames = JsonSerializer.Deserialize<ResolutionFrame[]>(serializedFrames)!;
        Require(JsonSerializer.Serialize(frames) == serializedFrames, "All child and parent frames must be serializable data.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Equivalent(game, restored);
        return restored;
    }
    private static void Equivalent(GameEngine game, GameEngine restored)
    {
        static string[] Events(GameEngine engine) => engine.Events.Select(item =>
            $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) &&
            Events(game).SequenceEqual(Events(restored)), "Replay must reproduce public events, private prompt and card zones exactly.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class StartModule : IPhaseSkillModule
    {
        public string SkillId => "fixture:phase-pindian";
        public int Revision => 1;
        public PhaseSkillWindow Window => PhaseSkillWindow.PlayStarting;
        public SkillActivationPlan CreatePlan(PhaseSkillContext context) => new(
            new(SkillId, "Fixture", "Start contest", "Use or skip"), "Start contest",
            new(new("use"), "Use", [], [], new Dictionary<string, string>()),
            new(new("skip"), "Skip", [], [], new Dictionary<string, string>()),
            [new DrawSkillCards(1, new("fixture.pre")), new BeginSkillPindian(), new DrawSkillCards(2, new("fixture.post"))]);
    }

    private sealed class ClaimModule(string id, int revision, bool prefer, bool ownCards) : IPindianResultModule
    {
        public string SkillId => id;
        public int Revision => revision;
        public PindianCardClaimPlan? CreatePlan(PindianResultContext context)
        {
            var cardId = ownCards ? context.Result.CardOf(context.OwnerSeat) : context.Result.SourceCardId;
            return context.AvailableCardIds.Contains(cardId)
                ? new(new(id, id, "Claim", "Claim contest card"), "Claim contest card", cardId, prefer) : null;
        }
    }

    private sealed class FixturePackage(bool emptyOpponents, int revision, SkillKind? legacy = null, bool ClaimOwnCards = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-pindian", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new("fixture:phase-pindian", "Fixture", "Start contest") { PhaseSkill = new StartModule() });
            builder.AddSkill(new("fixture:claim-a", "Claim A", "Optional acquisition")
                { PindianResultSkill = new ClaimModule("fixture:claim-a", revision, false, ClaimOwnCards) });
            builder.AddSkill(new("fixture:claim-b", "Claim B", "Optional acquisition")
                { PindianResultSkill = new ClaimModule("fixture:claim-b", revision, true, ClaimOwnCards) });
            var ids = Enumerable.Range(0, 6).Select(index => $"fixture:pindian-general-{index}").ToArray();
            var initiatingSkill = legacy is null ? "fixture:phase-pindian" : $"classic:{legacy.ToString()!.ToLowerInvariant()}";
            foreach (var id in ids) builder.AddGeneral(new(id, "Fixture", "supporter", initiatingSkill, "wei",
                BaseHp: legacy is not null && id == ids[0] ? 2 : 4,
                AdditionalSkillIds: ["fixture:claim-a", "fixture:claim-b"]));
            builder.AddDeck(new("fixture:pindian-deck", "Fixed tied contests", emptyOpponents ? 0 : 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, 7)).ToArray() });
            builder.AddMode(new("identity:classic-pindian-fixture", "Pindian fixture", 6, 6,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3, [nameof(Role.Renegade)] = 1 },
                "fixture:pindian-deck", GeneralCandidateCount: 6, GeneralPoolIds: ids));
        }
    }

    private sealed class ProgramFixturePackage(SkillProgram program) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-program-pindian", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new(ProgramSkillId, "程序拼点", "拼点未赢摸两张牌。") { Program = program });
            builder.AddSkill(new("fixture:claim-a", "Claim A", "Optional acquisition")
                { PindianResultSkill = new ClaimModule("fixture:claim-a", 1, false, false) });
            builder.AddSkill(new("fixture:claim-b", "Claim B", "Optional acquisition")
                { PindianResultSkill = new ClaimModule("fixture:claim-b", 1, false, false) });
            var ids = Enumerable.Range(0, 6).Select(index => $"fixture:pindian-general-{index}").ToArray();
            foreach (var id in ids)
                builder.AddGeneral(new(id, "Fixture", "supporter", ProgramSkillId, "wei",
                    AdditionalSkillIds: ["fixture:claim-a", "fixture:claim-b"]));
            builder.AddDeck(new("fixture:pindian-deck", "Fixed tied contests", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 96)
                    .Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, 7)).ToArray()
            });
            builder.AddMode(new("identity:classic-pindian-fixture", "Pindian fixture", 6, 6,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3, [nameof(Role.Renegade)] = 1 },
                "fixture:pindian-deck", GeneralCandidateCount: 6, GeneralPoolIds: ids));
        }
    }
}
