using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuZhiChecks
{
    private const string GeneralId = "classic:zhu-zhi";
    private const string SkillId = "classic:anguo";
    private const string SyntheticSkillId = "fixture:coverage-discard";
    private const string SyntheticGeneralId = "fixture:coverage-owner";
    private const string RulesResource = "CardGame.Content.Standard.SkillPrograms.classic-zhu-zhi.rules.json";
    private const string PresentationResource = "CardGame.Content.Standard.SkillPrograms.classic-zhu-zhi.presentation.json";

    public static void DefinitionAndResourceContracts()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var general = registry.Generals[GeneralId];
        var skill = registry.Skills[SkillId];
        Require(general is { Name: "朱治", FactionId: "wu", BaseHp: 4, PortraitKey: "zhu_zhi" } &&
                general.SkillIds.SequenceEqual([SkillId]) &&
                registry.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId) &&
                registry.Modes["identity:classic-8"].GeneralPoolIds!.Contains(GeneralId) &&
                skill.Program is { RuntimeVersion: "skill-program-v58", MinimumRulesVersion: 168 } &&
                skill.Program.Activations.Single() is { UsesPerPhase: 1, MinTargets: 0, MaxTargets: 0 } &&
                (int)SkillProgramConditionKind.AttackRangeCoverageDecreased == 22,
            "Zhu Zhi must be a formal Wu/Fame V general with phase-limited schema-55 Anguo.");
        var rules = Resource(RulesResource);
        var presentation = Resource(PresentationResource);
        Reject(rules.Replace("\"sourceBind\": \"range-change\"", "\"sourceBind\": \"future\"", StringComparison.Ordinal),
            presentation, "unknown attack-range coverage");
        Reject(rules.Replace("\"allowSameOwnerHandReturn\": true", "\"allowSameOwnerHandReturn\": false", StringComparison.Ordinal)
                .Replace("\"zones\": [\"equipment\"]", "\"zones\": [\"hand\"]", StringComparison.Ordinal),
            presentation, "coverageResultBind requires equipment");
        Reject(rules.Replace("\"zones\": [\"equipment\"]", "\"zones\": [\"hand\"]", StringComparison.Ordinal)
                .Replace("\"coverageResultBind\": \"range-change\",", "", StringComparison.Ordinal),
            presentation, "public non-hand");
        Reject(rules.Replace("\"coverageResultBind\": \"range-change\",",
                    "\"coverageResultBind\": \"range-change\", \"skipIfNoCards\": true,", StringComparison.Ordinal),
            presentation, "unconditional non-skipping");
        Reject(rules.Replace("\"coverageResultBind\": \"range-change\",",
                    "\"coverageResultBind\": \"range-change\", \"condition\": { \"kind\": \"wounded\" },",
                    StringComparison.Ordinal),
            presentation, "unconditional non-skipping");
    }

    public static void AnguoReturnsWeaponAndReplays()
    {
        var (game, registry) = Create("classic:qilin-bow");
        Require(game.GetHumanLegalActions().All(action => action.ProgramSkillId != SkillId),
            "Anguo must be absent before any other living player has equipment.");
        ReachNextPlayAfterAiEquips(game);
        var targets = game.CreateCardZoneDiagnostics()
            .Where(card => card.Location.Zone == CardZoneKind.Equipment && card.Location.OwnerSeat is > 0)
            .Select(card => card.Location.OwnerSeat!.Value).Distinct().ToArray();
        Require(targets.Length > 0, "AI fixture failed to equip a public weapon.");
        var selectedSeat = targets.First(seat => game.GetAttackRange(seat) > 1);
        var coverageBefore = CountCoverage(game, selectedSeat);
        Require(coverageBefore > 2, "Fixture weapon must cover the opposite living seat.");
        var action = game.GetHumanLegalActions().Single(item => item.ProgramSkillId == SkillId);
        Accept(game.Submit(new UseProgramSkillCommand(0, SkillId, "return-equipment", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        var targetPrompt = RequirePrompt(game);
        Require(targetPrompt.Choices.Any(choice => choice.Targets.SequenceEqual([selectedSeat])) &&
                targetPrompt.Choices.All(choice => targets.Contains(choice.Targets.Single())),
            "Anguo target choices must contain only equipped other living players.");
        var atTarget = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AnswerTarget(game, selectedSeat);
        AnswerTarget(atTarget, selectedSeat);
        Require(State(game) == State(atTarget), "Target-choice checkpoint diverged.");
        var equipment = RequirePrompt(game);
        Require(equipment.IsPrivate && equipment.Choices.Count > 0 &&
                equipment.Choices.All(choice => choice.Parameters.GetValueOrDefault("source-zone") == "Equipment"),
            "Anguo must select an actual public equipment card through its private command prompt.");
        var atEquipment = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var chosen = equipment.Choices.First();
        Answer(game, chosen);
        Answer(atTarget, RequirePrompt(atTarget).Choices.First());
        Answer(atEquipment, RequirePrompt(atEquipment).Choices.First());
        var moveReason = $"skill-program.{SkillId}.SelectAndMoveOwnedCard";
        Require(game.CardMovements.Count(move => move.Reason.Value == moveReason &&
                    move.From == CardLocation.Equipment(selectedSeat) && move.To == CardLocation.Processing) == 1 &&
                game.CardMovements.Count(move => move.Reason.Value == moveReason &&
                    move.From == CardLocation.Processing && move.To == CardLocation.Hand(selectedSeat)) == 1 &&
                CountCoverage(game, selectedSeat) < coverageBefore &&
                game.CardMovements.Count(move => move.Reason.Value == $"skill-program.{SkillId}.Draw") == 1 &&
                State(game) == State(atTarget) && State(game) == State(atEquipment) &&
                Events(game).SequenceEqual(Events(atEquipment)),
            "Removing a coverage-changing public weapon must return it to its owner, then draw exactly one with replay.");
        Require(game.GetHumanLegalActions().All(item => item.ProgramSkillId != SkillId),
            "Anguo must not be usable twice in one Play phase.");
    }

    public static void AnguoAwaitsXiaojiBeforeDrawing()
    {
        var (game, registry) = Create("classic:qilin-bow", xiaojiTargets: true);
        ReachNextPlayAfterAiEquips(game);
        var selectedSeat = game.CreateCardZoneDiagnostics()
            .Where(card => card.Location.Zone == CardZoneKind.Equipment && card.Location.OwnerSeat is > 0)
            .Select(card => card.Location.OwnerSeat!.Value).First();
        var zones = (CardZoneStore)typeof(GameEngine)
            .GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var nextThreeIds = zones.CardsAt(CardLocation.DrawPile).TakeLast(3)
            .Reverse().Select(card => card.Id).ToArray();
        Accept(game.Submit(new UseProgramSkillCommand(0, SkillId, "return-equipment", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        AnswerTarget(game, selectedSeat);
        Answer(game, RequirePrompt(game).Choices.First());
        var internalPrompt = (PendingDecision?)typeof(GameEngine)
            .GetField("_pendingDecision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game);
        Require(internalPrompt is { Kind: DecisionKind.ProgramTrigger } &&
                game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>()
                    .Any(window => window.Batch.AwaitingProgramFrameId is not null) &&
                game.ResolutionStack.OfType<ProgramSkillFrame>()
                    .Any(frame => frame.PendingMovementContinuation is not null) &&
                game.CardMovements.All(move => move.Reason.Value != $"skill-program.{SkillId}.Draw"),
            "Anguo must remain paused inside the scoped Xiaoji movement window before its own draw.");
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        for (var i = 0; i < 30 && game.ResolutionStack.OfType<ProgramSkillFrame>()
                 .Any(frame => frame.SkillId == SkillId); i++)
        {
            Advance(game);
            Advance(replay);
        }
        var xiaojiDraw = game.CardMovements.Select((move, index) => (move, index))
            .Where(item => item.move.Reason.Value == "skill-program.classic:xiaoji.Draw")
            .Select(item => item.index).ToArray();
        var anguoDraw = game.CardMovements.Select((move, index) => (move, index))
            .Where(item => item.move.Reason.Value == $"skill-program.{SkillId}.Draw")
            .Select(item => item.index).ToArray();
        var actualDrawIds = game.CardMovements
            .Where(move => move.Reason.Value == "skill-program.classic:xiaoji.Draw" ||
                move.Reason.Value == $"skill-program.{SkillId}.Draw")
            .Select(move => move.CardId).ToArray();
        Require(xiaojiDraw.Length >= 2 && anguoDraw.Length == 1 &&
                xiaojiDraw[^1] < anguoDraw[0] && State(game) == State(replay) &&
                Events(game).SequenceEqual(Events(replay)) &&
                actualDrawIds.SequenceEqual(nextThreeIds),
            "Xiaoji must draw two through its nested response before Anguo tests coverage and draws one, with replay.");
    }

    public static void AnguoMeasuresAfterNestedRangeResponse()
    {
        var (game, registry) = Create("classic:qilin-bow", rangeRescueTargets: true);
        ReachNextPlayAfterAiEquips(game);
        var selectedSeat = game.CreateCardZoneDiagnostics()
            .Where(card => card.Location.Zone == CardZoneKind.Equipment && card.Location.OwnerSeat is > 0)
            .Select(card => card.Location.OwnerSeat!.Value).First();
        var before = CountCoverage(game, selectedSeat);
        Require(before > 2, "The fixture weapon must initially extend coverage.");
        Accept(game.Submit(new UseProgramSkillCommand(0, SkillId, "return-equipment", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        AnswerTarget(game, selectedSeat);
        Answer(game, RequirePrompt(game).Choices.First());
        Require(game.Events.Any(item => item.Payload is TurnRuleModifierGrantedEvent granted &&
                    granted.Modifier.Source.SkillId == "fixture:range-rescue") &&
                CountCoverage(game, selectedSeat) == before &&
                game.CardMovements.All(move => move.Reason.Value != $"skill-program.{SkillId}.Draw"),
            "A nested equipment-loss response must restore coverage before Anguo decides whether to draw.");
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            "The nested public range response must replay without an extra Anguo draw.");
    }

    public static void AnguoCountsLivingCoverageRatherThanPrintedRange()
    {
        foreach (var (cardId, expectedDecline) in new[]
                 {
                     ("standard:crossbow", false),
                     ("standard:defensive_horse", false),
                     ("standard:offensive_horse", true),
                     ("classic:silver-lion", false)
                 })
        {
            var (game, _) = Create(cardId);
            ReachNextPlayAfterAiEquips(game);
            var equipped = game.CreateCardZoneDiagnostics().First(card =>
                card.Location.Zone == CardZoneKind.Equipment && card.Location.OwnerSeat is > 0);
            var seat = equipped.Location.OwnerSeat!.Value;
            var before = CountCoverage(game, seat);
            Accept(game.Submit(new UseProgramSkillCommand(0, SkillId, "return-equipment", [], [],
                game.Revision, game.PendingDecision!.PromptId)));
            AnswerTarget(game, seat);
            Answer(game, RequirePrompt(game).Choices.First());
            var after = CountCoverage(game, seat);
            var drew = game.CardMovements.Count(move => move.Reason.Value == $"skill-program.{SkillId}.Draw");
            Require((after < before) == expectedDecline && drew == (expectedDecline ? 1 : 0),
                $"Anguo must compare living-seat coverage for {cardId}, before={before}, after={after}, drew={drew}.");
        }
    }

    public static void AnguoDoesNotDrawWhenRangeFallsButCoverageStays()
    {
        var (game, _) = Create("classic:qilin-bow", rangePlusTwoTargets: true);
        ReachNextPlayAfterAiEquips(game);
        var seat = game.CreateCardZoneDiagnostics().First(card =>
            card.Location.Zone == CardZoneKind.Equipment && card.Location.OwnerSeat is > 0)
            .Location.OwnerSeat!.Value;
        var beforeRange = game.GetAttackRange(seat);
        var beforeCoverage = CountCoverage(game, seat);
        Accept(game.Submit(new UseProgramSkillCommand(0, SkillId, "return-equipment", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        AnswerTarget(game, seat);
        Answer(game, RequirePrompt(game).Choices.First());
        Require(game.GetAttackRange(seat) < beforeRange &&
                CountCoverage(game, seat) == beforeCoverage &&
                game.CardMovements.All(move => move.Reason.Value != $"skill-program.{SkillId}.Draw"),
            "Printed attack range can fall without reducing any living target coverage; Anguo must not draw.");
    }

    public static void GenericCoverageBindingSupportsDiscardAndRecover()
    {
        var (game, _) = Create("classic:qilin-bow", syntheticDiscardOwner: true);
        ReachNextPlayAfterAiEquips(game);
        var seat = game.CreateCardZoneDiagnostics().First(card =>
            card.Location.Zone == CardZoneKind.Equipment && card.Location.OwnerSeat is > 0)
            .Location.OwnerSeat!.Value;
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine)
            .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        players[0].Hp = 3; // Explicit fixture damage; main Anguo replay tests use commands only.
        Accept(game.Submit(new UseProgramSkillCommand(0, SyntheticSkillId, "return-equipment", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        AnswerTarget(game, seat);
        Answer(game, RequirePrompt(game).Choices.First());
        Require(game.CardMovements.Any(move => move.Reason.Value ==
                    $"skill-program.{SyntheticSkillId}.SelectAndMoveOwnedCard" &&
                    move.From == CardLocation.Equipment(seat) && move.To == CardLocation.DiscardPile) &&
                players[0].Hp == 4,
            "A different skill ID must bind public coverage after equipment-to-discard and conditionally recover.");
    }

    public static void AnguoCancelsWhenNestedMovementKillsSubject()
    {
        var (game, _) = Create("classic:qilin-bow", lethalLossTargets: true);
        ReachNextPlayAfterAiEquips(game);
        var seat = game.CreateCardZoneDiagnostics().First(card =>
            card.Location.Zone == CardZoneKind.Equipment && card.Location.OwnerSeat is > 0)
            .Location.OwnerSeat!.Value;
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine)
            .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        players[seat].Hp = 1; // Explicit lethal fixture; no Replay claim for injected setup.
        Accept(game.Submit(new UseProgramSkillCommand(0, SkillId, "return-equipment", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        AnswerTarget(game, seat);
        Answer(game, RequirePrompt(game).Choices.First());
        for (var i = 0; i < 100 && game.ResolutionStack.OfType<ProgramSkillFrame>()
                 .Any(frame => frame.SkillId == SkillId); i++) Advance(game);
        Require(!players[seat].IsAlive &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().All(frame => frame.SkillId != SkillId) &&
                game.CardMovements.All(move => move.Reason.Value != $"skill-program.{SkillId}.Draw"),
            "A lethal nested movement response must cancel the waiting coverage result and avoid an Anguo draw.");
    }

    public static void AnguoPreservesImmediateEquipmentRemovalHooks()
    {
        var (lion, _) = Create("classic:silver-lion");
        const int lionSeat = 1;
        EquipFixtureCard(lion, lionSeat, CardKind.SilverLion);
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine)
            .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lion)!;
        players[lionSeat].Hp = 3; // Wound is an explicit fixture injection; command-path tests cover Replay.
        Accept(lion.Submit(new UseProgramSkillCommand(0, SkillId, "return-equipment", [], [],
            lion.Revision, lion.PendingDecision!.PromptId)));
        AnswerTarget(lion, lionSeat);
        Answer(lion, RequirePrompt(lion).Choices.First());
        Require(players[lionSeat].Hp == 4 &&
                lion.Events.Any(item => item.Payload is SilverLionRemovedRecoveryEvent) &&
                lion.CardMovements.All(move => move.Reason.Value != $"skill-program.{SkillId}.Draw"),
            "Returning Silver Lion must run its immediate recovery and still avoid a false Anguo draw.");

        var (ox, _) = Create("classic:wooden-ox");
        const int oxSeat = 1;
        EquipFixtureCard(ox, oxSeat, CardKind.WoodenOx);
        var grainId = ox.CreateSnapshot(0, revealAll: true).Players[oxSeat].Hand.First().Id;
        var zones = typeof(GameEngine).GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ox)!;
        zones.GetType().GetMethod("Move")!.Invoke(zones,
            [grainId, CardLocation.Hand(oxSeat), CardLocation.WoodenOxGrain(oxSeat)]);
        Accept(ox.Submit(new UseProgramSkillCommand(0, SkillId, "return-equipment", [], [],
            ox.Revision, ox.PendingDecision!.PromptId)));
        AnswerTarget(ox, oxSeat);
        Answer(ox, RequirePrompt(ox).Choices.First());
        Require(ox.CardMovements.Any(move => move.CardId == grainId &&
                    move.From == CardLocation.WoodenOxGrain(oxSeat) &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.WoodenOxGrainDiscard) &&
                ox.CardMovements.All(move => move.Reason.Value != $"skill-program.{SkillId}.Draw"),
            "Returning Wooden Ox through Processing must discard stored grain through the synchronous hook.");
    }

    private static int CountCoverage(GameEngine game, int seat) =>
        Enumerable.Range(0, 4).Count(target => target != seat &&
            game.State.Players[target].IsAlive && game.GetCombatDistance(seat, target) <= game.GetAttackRange(seat));

    private static void EquipFixtureCard(GameEngine game, int seat, CardKind kind)
    {
        var card = game.CreateCardZoneDiagnostics().First(item =>
            item.CardKind == kind && item.Location == CardLocation.DrawPile);
        var zones = (CardZoneStore)typeof(GameEngine)
            .GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        _ = zones.Move(card.CardId, card.Location, CardLocation.Equipment(seat));
    }

    private static (GameEngine Game, ContentRegistry Registry) Create(string equipmentCardId,
        bool xiaojiTargets = false, bool rangeRescueTargets = false,
        bool rangePlusTwoTargets = false, bool syntheticDiscardOwner = false,
        bool lethalLossTargets = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new Scenario(equipmentCardId, xiaojiTargets, rangeRescueTargets,
                rangePlusTwoTargets, syntheticDiscardOwner, lethalLossTargets));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = Scenario.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0,
            syntheticDiscardOwner ? SyntheticGeneralId : GeneralId,
            game.Revision, game.PendingDecision!.PromptId)));
        ReachPlay(game);
        return (game, registry);
    }

    private static void ReachNextPlayAfterAiEquips(GameEngine game)
    {
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var i = 0; i < 600 &&
             !(game.PendingDecision?.Kind == DecisionKind.PlayCard && game.State.TurnNumber > 1); i++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard && game.State.TurnNumber > 1,
            $"Fixture did not return to Zhu Zhi Play: turn={game.State.TurnNumber}, pending={game.PendingDecision?.Kind}.");
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var i = 0; i < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fixture did not reach Play.");
    }
    private static void AnswerTarget(GameEngine game, int seat) =>
        Answer(game, RequirePrompt(game).Choices.Single(choice => choice.Targets.SequenceEqual([seat])));
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(
        new AnswerPromptCommand(game.PendingDecision!.PlayerSeat, game.PendingDecision.PromptId,
            choice.Id, game.Revision)));
    private static PendingDecision RequirePrompt(GameEngine game) => game.PendingDecision is
        { Kind: DecisionKind.ProgramTrigger } prompt ? prompt :
        throw new InvalidOperationException($"Expected program prompt, got {game.PendingDecision?.Kind}.");
    private static void Advance(GameEngine game) => Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
    private static IReadOnlyList<string> Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static string Resource(string name)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static void Reject(string rules, string presentation, string fragment)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException error) when (error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidOperationException($"Expected definition rejection containing '{fragment}'.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Rejected command.");
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(string equipmentCardId, bool xiaojiTargets,
        bool rangeRescueTargets, bool rangePlusTwoTargets, bool syntheticDiscardOwner,
        bool lethalLossTargets) : IGameContentPackage
    {
        public const string ModeId = "identity:classic-zhu-zhi-check-4";
        public PackageManifest Manifest { get; } = new("zhu-zhi-scenario", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            if (syntheticDiscardOwner)
            {
                var rules = Resource(RulesResource)
                    .Replace(SkillId, SyntheticSkillId, StringComparison.Ordinal)
                    .Replace("\"destination\": \"selectedTargetHand\",", "\"destination\": \"discardPile\",", StringComparison.Ordinal)
                    .Replace("\"targetRef\": { \"kind\": \"selectedTarget\" },", "", StringComparison.Ordinal)
                    .Replace("\"allowSameOwnerHandReturn\": true,", "", StringComparison.Ordinal)
                    .Replace("\"op\": \"draw\"", "\"op\": \"recover\"", StringComparison.Ordinal);
                var catalog = SkillProgramCatalog.Load(rules,
                    Resource(PresentationResource).Replace(SkillId, SyntheticSkillId, StringComparison.Ordinal));
                builder.AddSkill(new ContentSkillDefinition(SyntheticSkillId, "范围回复", "公共绑定测试")
                { Program = catalog.Programs[SyntheticSkillId],
                    ProgramPresentation = catalog.Presentations[SyntheticSkillId] });
                builder.AddGeneral(new ContentGeneralDefinition(SyntheticGeneralId, "公共范围测试",
                    "coverage_owner", SyntheticSkillId, "wu", BaseHp: 4));
            }
            if (rangeRescueTargets)
            {
                const string rules = """
                {"schemaVersion":58,"skills":[{"id":"fixture:range-rescue","revision":1,
                 "minimumRulesVersion":168,"triggers":[{"id":"restore-public-range",
                 "window":"cardsMoved","subject":"owner","sourceZones":["equipment"],
                 "movementOccurrence":"perBatch","optional":false,"priority":0,
                 "effects":[{"op":"grantTurnRuleModifier","target":"owner",
                 "ruleQuery":"attackRange","ruleOperation":"unlimited"}]}]}]}
                """;
                const string presentation = """
                {"schemaVersion":3,"skills":{"fixture:range-rescue":{"name":"范围响应","description":"装备离开后，攻击范围改为无限。"}}}
                """;
                var catalog = SkillProgramCatalog.Load(rules, presentation);
                builder.AddSkill(new ContentSkillDefinition("fixture:range-rescue", "范围响应", "测试公共失装响应")
                { Program = catalog.Programs["fixture:range-rescue"],
                    ProgramPresentation = catalog.Presentations["fixture:range-rescue"] });
            }
            if (rangePlusTwoTargets)
            {
                const string rules = """
                {"schemaVersion":58,"skills":[{"id":"fixture:range-plus-two","revision":1,
                 "minimumRulesVersion":168,"modifiers":[{"id":"public-plus-two",
                 "query":"attackRange","operation":"add","value":2,"priority":0}]}]}
                """;
                const string presentation = """
                {"schemaVersion":3,"skills":{"fixture:range-plus-two":{"name":"范围加二","description":"攻击范围加二。"}}}
                """;
                var catalog = SkillProgramCatalog.Load(rules, presentation);
                builder.AddSkill(new ContentSkillDefinition("fixture:range-plus-two", "范围加二", "测试公开范围")
                { Program = catalog.Programs["fixture:range-plus-two"],
                    ProgramPresentation = catalog.Presentations["fixture:range-plus-two"] });
            }
            if (lethalLossTargets)
            {
                const string rules = """
                {"schemaVersion":58,"skills":[{"id":"fixture:range-lethal","revision":1,
                 "minimumRulesVersion":168,"triggers":[{"id":"lose-life-on-equipment-loss",
                 "window":"cardsMoved","subject":"owner","sourceZones":["equipment"],
                 "movementOccurrence":"perBatch","optional":false,"priority":0,
                 "effects":[{"op":"loseHp","target":"owner","amount":1}]}]}]}
                """;
                const string presentation = """
                {"schemaVersion":3,"skills":{"fixture:range-lethal":{"name":"致命失装","description":"装备离开后失去一点体力。"}}}
                """;
                var catalog = SkillProgramCatalog.Load(rules, presentation);
                builder.AddSkill(new ContentSkillDefinition("fixture:range-lethal", "致命失装", "测试嵌套死亡")
                { Program = catalog.Programs["fixture:range-lethal"],
                    ProgramPresentation = catalog.Presentations["fixture:range-lethal"] });
            }
            var targets = new[] { "fixture:zhu-zhi-1", "fixture:zhu-zhi-2", "fixture:zhu-zhi-3" };
            foreach (var id in targets)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试目标", "supporter",
                    lethalLossTargets ? "fixture:range-lethal" :
                    rangePlusTwoTargets ? "fixture:range-plus-two" :
                    rangeRescueTargets ? "fixture:range-rescue" :
                    xiaojiTargets ? "classic:xiaoji" : "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:zhu-zhi-deck", "朱治测试牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 120)
                    .Select(index => new ContentDeckPhysicalCard(
                        (xiaojiTargets || rangeRescueTargets || lethalLossTargets) && index >= 12
                            ? "standard:dodge" : equipmentCardId,
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "朱治场景", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:zhu-zhi-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [syntheticDiscardOwner ? SyntheticGeneralId : GeneralId, .. targets]));
        }
    }
}
