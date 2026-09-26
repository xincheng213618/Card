using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillProgramCauseDeathChecks
{
    private const string CauseProgramId = "cause-death-test:owner";
    private const string ObserverProgramId = "cause-death-test:observer";
    private const string CauseTriggerId = "ganglie-cause-death";
    private const string ObserverTriggerId = "ganglie-observer-draw";
    private const string OwnerGeneralId = "cause-death-test:owner-general";
    private const string ObserverGeneralId = "cause-death-test:observer-general";
    private const string WuhunSkillId = "cause-death-test:wuhun";

    internal static void Definitions()
    {
        var catalog = SkillProgramCatalog.Load(Rules, Presentation);
        var cause = catalog.Programs[CauseProgramId];
        var effect = cause.Triggers.Single().Effects.Single();
        Require(cause is { RuntimeVersion: "skill-program-v9", MinimumRulesVersion: 93 } &&
                effect is
                {
                    Op: SkillProgramTriggerEffectOp.CauseDeath,
                    Target: SkillProgramTriggerEffectTarget.JudgmentSubject,
                    Amount: 0
                },
            "Schema 9 must freeze causeDeath as a non-damage judgment lifecycle effect.");

        AssertReject(Rules.Replace("\"schemaVersion\":9", "\"schemaVersion\":8", StringComparison.Ordinal),
            "causeDeath");
        AssertReject(Rules.Replace("\"target\":\"judgmentSubject\"",
                "\"target\":\"owner\"", StringComparison.Ordinal),
            "selectedTarget or judgmentSubject");
        AssertReject(Rules.Replace("\"target\":\"judgmentSubject\"",
                "\"target\":\"judgmentSubject\",\"amount\":1", StringComparison.Ordinal),
            "only target and condition");
    }

    internal static void NestedDeathSkillAndReplay()
    {
        var registry = CreateRegistry(
            "cause-death-program-nested",
            playerCount: 5,
            new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2,
                [nameof(Role.Renegade)] = 1
            });
        var (game, sourceSeat, observerSeat) = FindGanglieBoundary(registry, requireNonLordSource: true);
        ActivateGanglie(game);
        AdvanceToWuhunPrompt(game);
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException(
                $"Program causeDeath did not pause at nested Wuhun target selection; " +
                $"winner={game.State.Winner}, status={game.State.Status}, " +
                $"stack={string.Join(',', game.ResolutionStack.Select(frame => frame.Kind))}.");
        Require(prompt is
        {
            Kind: DecisionKind.ProgramTrigger,
            PlayerSeat: 0,
            IsPrivate: true
        } && prompt.ValidTargetSeats.Contains(sourceSeat) &&
                game.ResolutionStack.OfType<DeathFrame>().Any(frame => frame.VictimSeat == 0) &&
                game.ResolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Single() is
                {
                    OwnerSeat: 0
                } deathWindow,
            $"Program causeDeath must let its dead target open a nested, private death-skill choice " +
            $"(kind={prompt.Kind}, player={prompt.PlayerSeat}, private={prompt.IsPrivate}, " +
            $"targets=[{string.Join(',', prompt.ValidTargetSeats)}], expected=[{sourceSeat}], " +
            $"death=[{string.Join(',', game.ResolutionStack.OfType<DeathFrame>().Select(frame => frame.VictimSeat))}], " +
            $"death-windows=[{string.Join(',', game.ResolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Select(frame => frame.OwnerSeat))}]).");

        var observerHandBefore = game.CreateSnapshot(0, revealAll: true).Players[observerSeat].HandCount;
        var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var selectedChoiceId = prompt.Choices.Single(choice => choice.Targets.Contains(sourceSeat)).Id;
        Answer(game, prompt.Choices.Single(choice => choice.Id == selectedChoiceId));
        Answer(restored, restored.PendingDecision!.Choices.Single(choice => choice.Id == selectedChoiceId));
        FinishResolution(game);
        FinishResolution(restored);

        var events = game.Events.Select(item => item.Payload).ToArray();
        var causeIndex = Array.FindIndex(events, item => item is ProgramCauseDeathDeclaredEvent cause &&
            cause.SkillId == CauseProgramId && cause.TriggerId == CauseTriggerId &&
            cause.SourceSeat == 0 && cause.TargetSeat == 0);
        var ownerDeathIndex = Array.FindIndex(events, causeIndex + 1, item =>
            item is PlayerDiedEvent death && death.VictimSeat == 0 && death.KillerSeat is null);
        var nestedStartIndex = Array.FindIndex(events, ownerDeathIndex + 1, item =>
            item is ProgramBindingStartedEvent started && started.OwnerSeat == 0 &&
            started.SkillId == WuhunSkillId &&
            started.Window == SkillProgramTriggerWindow.OwnerDied);
        var sourceDeathIndex = Array.FindIndex(events, nestedStartIndex + 1, item =>
            item is PlayerDiedEvent death && death.VictimSeat == sourceSeat && death.KillerSeat is null);
        Require(causeIndex >= 0 && ownerDeathIndex > causeIndex && nestedStartIndex > ownerDeathIndex &&
                sourceDeathIndex > nestedStartIndex &&
                !events.Skip(causeIndex).Take(ownerDeathIndex - causeIndex)
                    .Any(item => item is DamageAppliedEvent or PlayerDyingEvent) &&
                !game.CreateSnapshot(0, revealAll: true).Players[0].IsAlive &&
                !game.CreateSnapshot(0, revealAll: true).Players[sourceSeat].IsAlive &&
                game.CreateSnapshot(0, revealAll: true).Players[observerSeat].HandCount == observerHandBefore + 1 &&
                events.OfType<CardMovedEvent>().Any(item =>
                    item.Reason.Value == $"skill-program.{ObserverProgramId}.judgment.draw") &&
                game.State.Winner == Winner.None && game.ResolutionStack.Count == 0,
            "causeDeath must bypass damage/dying, resume after nested Wuhun, and continue later program candidates.");

        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                restored.Events.Select(item => item.Payload.GetType().Name)
                    .SequenceEqual(game.Events.Select(item => item.Payload.GetType().Name)),
            "A paused nested causeDeath checkpoint did not replay exactly.");
    }

    internal static void TerminalShortCircuit()
    {
        var registry = CreateRegistry(
            "cause-death-program-terminal",
            playerCount: 4,
            new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 2,
                [nameof(Role.Rebel)] = 1
            });
        var (game, _, observerSeat) = FindGanglieBoundary(registry, requireNonLordSource: false);
        var observerHandBefore = game.CreateSnapshot(0, revealAll: true).Players[observerSeat].HandCount;
        ActivateGanglie(game);
        FinishResolution(game);

        var events = game.Events.Select(item => item.Payload).ToArray();
        var cause = events.OfType<ProgramCauseDeathDeclaredEvent>().Single(item =>
            item.SkillId == CauseProgramId && item.TargetSeat == 0);
        Require(cause.SourceSeat == 0 &&
                events.OfType<PlayerDiedEvent>().Any(item => item.VictimSeat == 0 && item.KillerSeat is null) &&
                !events.OfType<PlayerDyingEvent>().Any(item => item.VictimSeat == 0) &&
                !events.OfType<ProgramBindingStartedEvent>().Any(item =>
                    item.OwnerSeat == 0 && item.SkillId == WuhunSkillId &&
                    item.Window == SkillProgramTriggerWindow.OwnerDied) &&
                !events.OfType<ProgramJudgmentTriggerResolvedEvent>().Any(item =>
                    item.SkillId == ObserverProgramId && item.TriggerId == ObserverTriggerId) &&
                game.CreateSnapshot(0, revealAll: true).Players[observerSeat].HandCount == observerHandBefore &&
                game.State is { Winner: Winner.LordAndLoyalists, Status: EngineStatus.Completed } &&
                game.ResolutionStack.Count == 0,
            $"A terminal causeDeath must skip death skills and later trigger candidates while unwinding cleanup. " +
            $"source={cause.SourceSeat}; died={events.OfType<PlayerDiedEvent>().Any(item => item.VictimSeat == 0 && item.KillerSeat is null)}; " +
            $"dying={events.OfType<PlayerDyingEvent>().Any(item => item.VictimSeat == 0)}; " +
            $"wuhun={events.OfType<ProgramBindingStartedEvent>().Any(item => item.OwnerSeat == 0 && item.SkillId == WuhunSkillId && item.Window == SkillProgramTriggerWindow.OwnerDied)}; " +
            $"observer={events.OfType<ProgramJudgmentTriggerResolvedEvent>().Any(item => item.SkillId == ObserverProgramId && item.TriggerId == ObserverTriggerId)}; " +
            $"hand={game.CreateSnapshot(0, revealAll: true).Players[observerSeat].HandCount}/{observerHandBefore}; " +
            $"state={game.State.Winner}/{game.State.Status}; stack={game.ResolutionStack.Count}.");

        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.Events.Select(item => item.Payload.GetType().Name)
                    .SequenceEqual(game.Events.Select(item => item.Payload.GetType().Name)),
            "Terminal causeDeath did not replay exactly.");

    }

    private static (GameEngine Game, int SourceSeat, int ObserverSeat) FindGanglieBoundary(
        ContentRegistry registry,
        bool requireNonLordSource)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = registry.Modes.Values.Single(mode =>
                    mode.Id.StartsWith("identity:classic-cause-death", StringComparison.Ordinal)).MaxPlayers,
                ModeId = registry.Modes.Keys.Single(id =>
                    id.StartsWith("identity:classic-cause-death", StringComparison.Ordinal)),
                HumanSeat = 0,
                HumanRole = Role.Rebel,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 40
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !game.Submit(new SelectGeneralCommand(
                    0, OwnerGeneralId, game.Revision, setup.PromptId)).Accepted)
                continue;

            for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.Ganglie, PlayerSeat: 0 } &&
                    game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                        .LastOrDefault(item => item.TargetSeat == 0 && item.SourceSeat != 0) is { } damage)
                {
                    var full = game.CreateSnapshot(0, revealAll: true);
                    var observer = full.Players.SingleOrDefault(player =>
                        player.GeneralId == ObserverGeneralId && player.IsAlive);
                    if (observer is not null &&
                        (!requireNonLordSource ||
                         full.Players[damage.SourceSeat].Role != Role.Lord &&
                         GameRules.EvaluateWinner(full.Players.Select(player => new PlayerLifeState(
                             player.Role ?? throw new InvalidOperationException("A revealed fixture role was missing."),
                             player.IsAlive && player.Seat != 0))) == Winner.None))
                        return (game, damage.SourceSeat, observer.Seat);
                }

                GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pending
                    ? pending.Kind switch
                    {
                        DecisionKind.PlayCard =>
                            new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
                        DecisionKind.RespondDodge =>
                            AnswerCommand(game, pending, "response", "take-damage"),
                        DecisionKind.RescueDying =>
                            AnswerCommand(game, pending, "response", "let-die"),
                        _ => new AnswerPromptCommand(
                            0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                    }
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted) break;
            }
        }
        throw new InvalidOperationException("No bounded Ganglie causeDeath fixture was found.");
    }

    private static void ActivateGanglie(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Ganglie prompt was lost.");
        Answer(game, prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "ganglie"));
    }

    private static void AdvanceToWuhunPrompt(GameEngine game)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } prompt &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-target")) return;
            if (game.PendingDecision is not null)
                throw new InvalidOperationException(
                    $"Unexpected prompt {game.PendingDecision.Kind} before nested Wuhun.");
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "Could not advance to nested Wuhun.");
        }
        throw new InvalidOperationException("Nested Wuhun prompt did not appear within the bounded steps.");
    }

    private static void FinishResolution(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            if (game.ResolutionStack.Count == 0 || game.State.Status == EngineStatus.Completed) return;
            GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pending
                ? new AnswerPromptCommand(
                    0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                : new AdvanceOneStepCommand(game.Revision);
            var result = game.Submit(command);
            Require(result.Accepted, result.Error?.Message ?? "causeDeath resolution could not finish.");
        }
        throw new InvalidOperationException("causeDeath did not settle within the bounded steps.");
    }

    private static AnswerPromptCommand AnswerCommand(
        GameEngine game,
        PendingDecision pending,
        string key,
        string value) =>
        new(0, pending.PromptId,
            pending.Choices.Single(choice => choice.Parameters.GetValueOrDefault(key) == value).Id,
            game.Revision);

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Expected prompt was lost.");
        var result = game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Prompt answer was rejected.");
    }

    private static ContentRegistry CreateRegistry(
        string packageId,
        int playerCount,
        IReadOnlyDictionary<string, int> roleCounts) =>
        ContentRegistry.Build(new StandardContentPackage(),
            new FixturePackage(packageId, playerCount, roleCounts));

    private sealed class FixturePackage(
        string packageId,
        int playerCount,
        IReadOnlyDictionary<string, int> roleCounts) : IGameContentPackage
    {
        private string ModeId => $"identity:classic-cause-death-{playerCount}-{packageId}";

        public PackageManifest Manifest { get; } = new(
            packageId, new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, program) in catalog.Programs)
            {
                var text = catalog.Presentations[id];
                builder.AddSkill(new ContentSkillDefinition(id, text.Name, text.Description)
                { Program = program });
            }
            var wuhun = SkillProgramCatalog.Load(WuhunRules, WuhunPresentation).Programs[WuhunSkillId];
            builder.AddSkill(new ContentSkillDefinition(
                WuhunSkillId,
                "武魂",
                "受到每点伤害后，伤害来源获得梦魇；死亡时令最多者判定。")
            {
                Program = wuhun,
                Tags = SkillTag.Locked,
                ExecutionForms = SkillExecutionForm.State
            });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId,
                "直接死亡测试",
                "shen_guan_yu",
                CauseProgramId,
                "god",
                BaseHp: 8,
                AdditionalSkillIds: ["standard:ganglie", WuhunSkillId]));
            builder.AddGeneral(new ContentGeneralDefinition(
                ObserverGeneralId,
                "后续候选测试",
                "cao_cao",
                ObserverProgramId,
                "wei",
                BaseHp: 8));
            var otherIds = Enumerable.Range(1, playerCount - 2)
                .Select(index => $"cause-death-test:other-{playerCount}-{index}")
                .ToArray();
            foreach (var id in otherIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "直接死亡陪测", "guan_yu", "standard:none", "wei", BaseHp: 8));
            builder.AddDeck(new ContentDeckRecipe(
                $"cause-death-test:deck-{playerCount}",
                "直接死亡全梅花牌堆",
                4,
                2,
                [])
            {
                PhysicalCards = Enumerable.Range(0, 96)
                    .Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 5))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "可配置直接死亡场景",
                playerCount,
                playerCount,
                roleCounts,
                DeckId: $"cause-death-test:deck-{playerCount}",
                GeneralCandidateCount: playerCount,
                GeneralPoolIds: [OwnerGeneralId, ObserverGeneralId, .. otherIds]));
        }
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

    private const string Rules = """
        {"schemaVersion":9,"skills":[
          {"id":"cause-death-test:owner","revision":1,"triggers":[
            {"id":"ganglie-cause-death","window":"judgmentFinalized","subject":"owner",
             "suits":["spade","heart","club","diamond"],"minimumRank":1,"maximumRank":13,
             "excludedReasons":[],"judgmentReasons":["skill.ganglie"],"optional":false,
             "effects":[{"op":"causeDeath","target":"judgmentSubject"}]}
          ]},
          {"id":"cause-death-test:observer","revision":1,"triggers":[
            {"id":"ganglie-observer-draw","window":"judgmentFinalized","subject":"any",
             "suits":["spade","heart","club","diamond"],"minimumRank":1,"maximumRank":13,
             "excludedReasons":[],"judgmentReasons":["skill.ganglie"],"optional":false,
             "effects":[{"op":"draw","target":"owner","amount":1}]}
          ]}
        ]}
        """;

    private const string Presentation = """
        {"schemaVersion":1,"skills":{
          "cause-death-test:owner":{"name":"直接死亡","description":"刚烈判定后令判定角色直接死亡。"},
          "cause-death-test:observer":{"name":"后续候选","description":"刚烈判定后摸一张牌。"}
        }}
        """;

    private const string WuhunRules = """
        {"schemaVersion":38,"skills":[{"id":"cause-death-test:wuhun","revision":2,"minimumRulesVersion":143,
        "modifiers":[],"viewAs":[],"activations":[],"triggers":[
          {"id":"damage-nightmare","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamagePoint","optional":false,"priority":0,
           "effects":[{"op":"changeAttributedMarker","target":"owner","targetRef":{"kind":"eventSource"},"marker":"nightmare","amount":1}]},
          {"id":"death-judgment","window":"ownerDied","subject":"owner","optional":false,"priority":0,
           "effects":[{"op":"selectTarget","target":"owner","targetKind":"maximumAttributedMarker","marker":"nightmare"},
                      {"op":"startJudgment","target":"selectedTarget","judgmentReason":"skill.wuhun.death","resultBind":"judgment","visibility":"public"},
                      {"op":"causeDeathUnlessBoundCardKind","target":"selectedTarget","sourceBind":"judgment","excludedCardKinds":["peach","peachGarden"]}]}
        ],"contributions":[],"cardIdentities":[],"states":[]}]}
        """;

    private const string WuhunPresentation = """
        {"schemaVersion":1,"skills":{"cause-death-test:wuhun":{"name":"武魂","description":"归属梦魇与死亡判定测试。"}}}
        """;
}
