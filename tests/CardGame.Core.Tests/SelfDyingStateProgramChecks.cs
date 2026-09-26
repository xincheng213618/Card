using CardGame.Content.Standard;
using CardGame.Core;

internal static class SelfDyingStateProgramChecks
{
    private const string SkillId = "classic:niepan";

    public static void DefinitionsAndVersionBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var skill = current.Skills[SkillId];
        var trigger = skill.Program?.Triggers.Single();

        Require(GameCheckpoint.CurrentRulesVersion >= 127 &&
                StandardClassicGeneralPackage.CurrentVersion >= new Version(1, 108, 0) &&
                skill is
                {
                    LegacyKind: null,
                    Tags: SkillTag.Limited,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    Program.UsesCompositionKernel: true,
                    Program.MinimumRulesVersion: 128
                } &&
                trigger is
                {
                    Id: "activation",
                    Window: SkillProgramTriggerWindow.SelfDyingResponse,
                    Subject: SkillProgramTriggerSubject.Owner,
                    Optional: true,
                    UsageScope: SkillUsageScope.Game,
                    UsageLimit: 1,
                    Effects:
                    [
                        {
                            Op: SkillProgramTriggerEffectOp.DiscardOwnedZoneCards,
                            Target: SkillProgramTriggerEffectTarget.Owner,
                            Zones: [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment]
                        },
                        {
                            Op: SkillProgramTriggerEffectOp.SetChainedState,
                            Target: SkillProgramTriggerEffectTarget.Owner,
                            Chained: false
                        },
                        {
                            Op: SkillProgramTriggerEffectOp.RecoverTo,
                            Target: SkillProgramTriggerEffectTarget.Owner,
                            NumberExpression: SkillProgramNumberExpression.IntegerConstant,
                            MinimumValue: 3,
                            ClampToMaxHp: true
                        },
                        {
                            Op: SkillProgramTriggerEffectOp.Draw,
                            Target: SkillProgramTriggerEffectTarget.Owner,
                            Amount: 3
                        }
                    ]
                },
            "Current Niepan must use its bounded self-dying program.");

        Reject(
            Rules.Replace("\"schemaVersion\":22", "\"schemaVersion\":21", StringComparison.Ordinal)
                .Replace("\"minimumRulesVersion\":127", "\"minimumRulesVersion\":126", StringComparison.Ordinal),
            "requires schema 22");
        Reject(
            Rules.Replace("\"judgment\"", "\"drawPile\"", StringComparison.Ordinal),
            "distinct hand, equipment and/or judgment zones");
        Reject(
            Rules.Replace("\"chained\":false", "\"chained\":null", StringComparison.Ordinal),
            "must be a boolean");
    }

    public static void ClearsOwnedStateAndReplays()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new FixturePackage());
        var game = FindFixture(registry);
        Play(game, LegalActionKind.Equip, CardKind.Crossbow, []);
        Play(game, LegalActionKind.Lightning, CardKind.Lightning, [0]);
        Play(game, LegalActionKind.IronChain, CardKind.IronChain, [0]);
        Require(game.CreateSnapshot(0, revealAll: true).Players[0] is
                { IsChained: true, HandCount: 1, Equipment.Count: 1, Judgment.Count: 1 },
            "The fixture must enter Niepan with one card in each owned card zone and a chained state.");

        var activation = game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.UseProgramSkill &&
            action.ProgramSkillId == FixturePackage.DyingSkillId) ??
            throw new InvalidOperationException("The fixture did not expose its lose-HP program action.");
        var lost = game.Submit(new UseProgramSkillCommand(
            0,
            activation.ProgramSkillId!,
            activation.ProgramActivationId!,
            [],
            [],
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(lost.Accepted, lost.Error?.Message ?? "The fixture could not enter dying.");

        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Program Niepan did not publish a dying response.");
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            item.Parameters.GetValueOrDefault("skill-id") == SkillId &&
            item.Parameters.GetValueOrDefault("binding-id") == "activation") ??
            throw new InvalidOperationException(
                "The dying prompt did not expose program Niepan. " +
                string.Join(" | ", prompt.Choices.Select(item =>
                    $"{item.Id}:{string.Join(',', item.Parameters.Select(pair => $"{pair.Key}={pair.Value}"))}")));
        var paused = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restoredAtPrompt = GameReplay.Restore(paused, registry);
        Require(restoredAtPrompt.PendingDecision?.Choices.Any(item => item.Id == choice.Id) == true,
            "A paused schema 22 Niepan prompt must restore its exact activation choice.");

        Resolve(game, choice.Id);
        Resolve(restoredAtPrompt, choice.Id);
        VerifyResolved(game);
        VerifyResolved(restoredAtPrompt);
        Require(SnapshotJson.Serialize(restoredAtPrompt.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "The restored Niepan prompt must produce the same public and private state.");

        var replayed = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A completed schema 22 Niepan command prefix must replay exactly.");
    }

    private static void Resolve(GameEngine game, ChoiceId choiceId)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Niepan activation prompt disappeared.");
        var accepted = game.Submit(new AnswerPromptCommand(
            0, prompt.PromptId, choiceId, game.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Program Niepan was rejected.");
    }

    private static void VerifyResolved(GameEngine game)
    {
        var player = game.CreateSnapshot(0, revealAll: true).Players[0];
        var payloads = game.Events.Select(item => item.Payload).ToArray();
        var discarded = payloads.OfType<ProgramOwnedZoneCardsDiscardedEvent>()
            .FirstOrDefault(item => item.SkillId == SkillId) ??
            throw new InvalidOperationException("Program Niepan did not publish its owned-zone discard event.");
        var chained = payloads.OfType<ProgramChainedStateSetEvent>()
            .FirstOrDefault(item => item.SkillId == SkillId) ??
            throw new InvalidOperationException("Program Niepan did not publish its chained-state event.");
        var resolved = payloads.OfType<ProgramBindingResolvedEvent>()
            .FirstOrDefault(item => item.SkillId == SkillId && item.BindingId == "activation") ??
            throw new InvalidOperationException("Program Niepan did not publish its completed binding event.");
        var discardMoves = payloads.OfType<CardMovedEvent>().Count(item =>
            item.Reason == new CardMoveReason("skill-program.classic:niepan.DiscardOwnedZoneCards"));
        var drawMoves = payloads.OfType<CardMovedEvent>().Count(item =>
            item.Reason == new CardMoveReason("skill-program.classic:niepan.Draw"));

        Require(player is { Hp: 3, HandCount: 3, IsChained: false } &&
                discarded is
                {
                    BindingId: "activation",
                    OwnerSeat: 0,
                    Zones: [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment],
                    CardCount: 3
                } &&
                chained is { BindingId: "activation", OwnerSeat: 0, IsChained: false } &&
                resolved is { Activated: true, Completed: true } &&
                discardMoves == 3 &&
                drawMoves == 3 &&
                !payloads.OfType<NiepanResolvedEvent>().Any(),
            "Current Niepan must clear owned zones and chaining, recover to three, draw three, and avoid the historical branch.");
    }

    private static GameEngine FindFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = FixturePackage.ModeId,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 40
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            ReachHumanPlay(game);
            var kinds = game.CreateSnapshot(0, revealAll: true).Players[0].Hand
                .Select(card => card.Kind)
                .ToHashSet();
            if (kinds.Contains(CardKind.Crossbow) &&
                kinds.Contains(CardKind.Lightning) &&
                kinds.Contains(CardKind.IronChain))
                return game;
        }
        throw new InvalidOperationException("No bounded schema 22 fixture dealt all three setup cards.");
    }

    private static void Play(
        GameEngine game,
        LegalActionKind actionKind,
        CardKind cardKind,
        IReadOnlyList<int> targetSeats)
    {
        var action = game.GetHumanLegalActions().FirstOrDefault(item =>
            item.Kind == actionKind && item.CardId is not null && item.TargetSeats.SequenceEqual(targetSeats)) ??
            throw new InvalidOperationException($"The fixture did not expose {actionKind} for [{string.Join(',', targetSeats)}].");
        var played = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            cardKind));
        Require(played.Accepted, played.Error?.Message ?? $"The fixture could not play {cardKind}.");
        ReachHumanPlay(game);
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) return;
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The fixture failed to advance to human play.");
        }
        throw new InvalidOperationException("The fixture did not reach human play within its bounded schedule.");
    }

    private static void Reject(string rules, string expected)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, Presentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException($"Expected invalid schema 22 fixture containing '{expected}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixturePackage : IGameContentPackage
    {
        public const string ModeId = "identity:schema-22-niepan-5";
        public const string DyingSkillId = "fixture:schema-22-lose-hp";
        private const string DeckId = "fixture:schema-22-niepan-deck";
        private static readonly string[] GeneralIds = Enumerable.Range(0, 5)
            .Select(index => $"fixture:schema-22-niepan-{index}")
            .ToArray();

        public PackageManifest Manifest { get; } = new(
            "fixture-schema-22-niepan",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 108, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(DyingRules, DyingPresentation);
            builder.AddSkill(new ContentSkillDefinition(DyingSkillId, "失去体力", "令自己失去五点体力。")
            {
                Program = catalog.Programs[DyingSkillId],
                ExecutionForms = SkillExecutionForm.Trigger
            });
            foreach (var id in GeneralIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "涅槃程序测试",
                    "pang_tong",
                    SkillId,
                    "shu",
                    BaseHp: 4,
                    AdditionalSkillIds: [DyingSkillId]));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "涅槃程序牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 0,
                [
                    new ContentDeckCardCount("standard:iron_chain", 14),
                    new ContentDeckCardCount("standard:lightning", 13),
                    new ContentDeckCardCount("standard:crossbow", 13)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "涅槃程序",
                5,
                5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 1,
                GeneralPoolIds: GeneralIds));
        }

        private const string DyingRules = """
            {"schemaVersion":1,"skills":[{"id":"fixture:schema-22-lose-hp","revision":1,
            "modifiers":[],"viewAs":[],"activations":[{"id":"invoke","minCards":0,"maxCards":0,
            "minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
            "effects":[{"op":"loseHp","target":"owner","amount":5}]}]}]}
            """;

        private const string DyingPresentation = """
            {"schemaVersion":1,"skills":{"fixture:schema-22-lose-hp":{"name":"失去体力",
            "description":"令自己失去五点体力。"}}}
            """;
    }

    private const string Rules = """
        {"schemaVersion":22,"skills":[{"id":"classic:niepan","revision":1,"minimumRulesVersion":127,
        "modifiers":[],"viewAs":[],"activations":[],"triggers":[{"id":"activation",
        "window":"selfDyingResponse","subject":"owner","optional":true,"priority":0,
        "usageScope":"game","usageLimit":1,"effects":[
        {"op":"discardOwnedZoneCards","target":"owner","zones":["hand","equipment","judgment"]},
        {"op":"setChainedState","target":"owner","chained":false},
        {"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true},
        {"op":"draw","target":"owner","amount":3}]}],"contributions":[],"cardIdentities":[]}]}
        """;

    private const string Presentation = """
        {"schemaVersion":1,"skills":{"classic:niepan":{"name":"涅槃","description":"测试"}}}
        """;
}
