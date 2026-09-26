using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryZhangLiaoChecks
{
    private const string GeneralId = "boundary:zhang-liao";
    private const string SkillId = "boundary:tuxi";

    public static void DefinitionAndSchemaBoundary()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var general = registry.Generals[GeneralId];
        var trigger = registry.Skills[SkillId].Program!.Triggers.Single();
        Require(general.Name == "界张辽" && general.FactionId == "wei" && general.BaseHp == 4 &&
                general.SkillIds.SequenceEqual([SkillId]) &&
                registry.Skills[SkillId].LegacyKind is null &&
                registry.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId) &&
                registry.Modes["identity:classic-8"].GeneralPoolIds!.Contains(GeneralId) &&
                !registry.Modes["identity:classic-boundary-5"].GeneralPoolIds!.Contains(GeneralId) &&
                trigger is { Window: SkillProgramTriggerWindow.DrawPhaseStarting,
                    DrawPhaseMode: SkillProgramDrawPhaseMode.Additive, Optional: true,
                    Effects: [
                        { Op: SkillProgramEffectOp.SelectTargets,
                          TargetKind: SkillProgramTargetKind.OtherLivingHandAtLeastOwner,
                          NumberExpression: SkillProgramNumberExpression.PlannedNormalDrawCount },
                        { Op: SkillProgramEffectOp.AdjustNormalDraw,
                          NumberExpression: SkillProgramNumberExpression.SelectedTargetCount },
                        { Op: SkillProgramEffectOp.TakeRandomHandCardFromSelectedTargets }
                    ] },
            "The 2018 boundary Zhang Liao must use its own additive draw-plan skill in the current identity pool.");
        Require(registry.Skills["classic:tuxi"].Program!.Triggers.Single().DrawPhaseMode ==
                    SkillProgramDrawPhaseMode.Replacement,
            "The new draw-plan syntax must not change classic Tuxi behavior.");

        var publicAi = ProgramCompositionAi.Estimate(
            trigger.Effects,
            new PlayerSkillContext(0, 4, 4, 4, TurnPhase.Draw, IsOwnTurn: true),
            publicContext: new ProgramAiPublicContext(1, NormalDrawCount: 3,
                EligibleTargetCount: 4));
        Require(publicAi.Hint.OwnerDraw == 0 && !publicAi.IsSelfLethal,
            "Public AI estimation must account for three selected steals and three fewer draws without private hand contents.");

        const string genericPresentation = """
            {"schemaVersion":3,"skills":{"fixture:draw-plan":{"name":"通用摸牌计划","description":"测试"}}}
            """;
        const string genericPlan = """
            {"schemaVersion":59,"skills":[{"id":"fixture:draw-plan","revision":1,
            "minimumRulesVersion":169,"triggers":[{"id":"plan","window":"drawPhaseStarting",
            "subject":"owner","optional":true,"drawPhaseMode":"additive","effects":[
            {"op":"selectTargets","target":"owner","targetKind":"anyLiving",
             "minimumTargets":1,"maximumTargets":8,"numberExpression":"plannedNormalDrawCount",
             "targetAiOrder":"stable"},
            {"op":"adjustNormalDraw","target":"owner","numberExpression":"selectedTargetCount"}]}]}]}
            """;
        Require(SkillProgramCatalog.Load(genericPlan, genericPresentation).Programs
                .ContainsKey("fixture:draw-plan"),
            "Draw-plan and selected-target expressions must compose without a target-hand transfer.");
        var noTargetSet = JsonNode.Parse(genericPlan)!;
        noTargetSet["skills"]![0]!["triggers"]![0]!["effects"]!.AsArray().RemoveAt(0);
        Reject(noTargetSet.ToJsonString(), genericPresentation, "target set");
        Reject(genericPlan.Replace("\"drawPhaseStarting\"", "\"turnEnding\"", StringComparison.Ordinal),
            genericPresentation, "drawPhaseMode");
        var wrongWindow = JsonNode.Parse(genericPlan)!;
        var triggerNode = wrongWindow["skills"]![0]!["triggers"]![0]!.AsObject();
        triggerNode["window"] = "turnEnding";
        triggerNode.Remove("drawPhaseMode");
        Reject(wrongWindow.ToJsonString(), genericPresentation, "DrawPlan");

        // The target-count expression is reusable with every legal target limit.
        // The public estimator must price the same selected count for both the
        // normal-draw debit and the following one-card-per-target transfer.
        foreach (var (limit, handCount, normalDrawCount, eligibleCount) in new[]
                 {
                     ((string?)null, 4, 2, 2),
                     ("currentHandCount", 1, 2, 2),
                     ("plannedNormalDrawCount", 4, 0, 2)
                 })
        {
            var composition = JsonNode.Parse(genericPlan)!;
            var steps = composition["skills"]![0]!["triggers"]![0]!["effects"]!.AsArray();
            var selection = steps[0]!.AsObject();
            selection["targetKind"] = "otherLivingWithHand";
            selection["maximumTargets"] = 2;
            if (limit is null) selection.Remove("numberExpression");
            else selection["numberExpression"] = limit;
            steps.Add(JsonNode.Parse("""
                {"op":"takeRandomHandCardFromSelectedTargets","target":"owner","amount":1}
                """));
            var program = SkillProgramCatalog.Load(composition.ToJsonString(), genericPresentation)
                .Programs["fixture:draw-plan"];
            var estimate = ProgramCompositionAi.Estimate(
                program.Triggers.Single().Effects,
                new PlayerSkillContext(0, 4, 4, handCount, TurnPhase.Draw, IsOwnTurn: true),
                publicContext: new ProgramAiPublicContext(1, NormalDrawCount: normalDrawCount,
                    EligibleTargetCount: eligibleCount));
            Require(estimate.Score == 0 && estimate.Hint.OwnerDraw == 0,
                $"Public AI must use one selected count for {limit ?? "fixed"} target debit and transfer.");
            if (limit is null)
            {
                var unknownCandidates = ProgramCompositionAi.Estimate(
                    program.Triggers.Single().Effects,
                    new PlayerSkillContext(0, 4, 4, handCount, TurnPhase.Draw, IsOwnTurn: true),
                    publicContext: new ProgramAiPublicContext(1, NormalDrawCount: normalDrawCount));
                Require(unknownCandidates.Score == 0,
                    "An omitted public candidate count must fall back to one legal target, not zero.");
            }
        }
    }

    public static void DrawPlanSelectionAndReplay()
    {
        CheckPlan(0, 2);
        CheckPlan(+1, 3);
        CheckPlan(-1, 1);
        CheckPlan(-2, 0);
    }

    public static void HandThresholdAndPriorReplacement()
    {
        var boostedRegistry = Registry(0, preDraw: true);
        var boosted = Start(boostedRegistry);
        for (var step = 0; step < 30 && boosted.State.Phase != TurnPhase.Play; step++)
            Advance(boosted);
        var boostedView = boosted.CreateSnapshot(0, true);
        Require(boosted.State.Phase == TurnPhase.Play &&
                boostedView.Players[0].HandCount == 7 &&
                boostedView.Players.Skip(1).All(player => player.HandCount == 4) &&
                boosted.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                    .All(item => item.SkillId != SkillId),
            "A five-card owner must not select four-card targets after an earlier extra draw.");

        var replacementRegistry = Registry(0, replacement: true);
        var replacement = Start(replacementRegistry);
        for (var step = 0; step < 30 &&
             replacement.PendingDecision?.SkillPrompt?.SkillId != "classic:tuxi"; step++)
            Advance(replacement);
        Require(replacement.PendingDecision?.SkillPrompt?.SkillId == "classic:tuxi",
            "The prior replacement must be offered before the additive plan.");
        Answer(replacement, "activate");
        Answer(replacement, replacement.PendingDecision!.Choices.First(choice => choice.Targets.Count == 1));
        Require(replacement.State.Phase == TurnPhase.Play &&
                replacement.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                    .All(item => item.SkillId != SkillId),
            "A completed replacement must suppress later draw-plan selection without a missing-plan error.");
    }

    public static void SupplyShortageSkipsTheDrawWindow()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new SupplyScenario());
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, PlayerCount = 5, ModeId = SupplyScenario.ModeId,
                HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            var selection = game.PendingDecision!;
            if (!game.Submit(new SelectGeneralCommand(0, SupplyScenario.HumanId,
                game.Revision, selection.PromptId)).Accepted) continue;
            for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                AdvanceOne(game);
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard) continue;
            var owner = game.CreateSnapshot(0, true).Players.Single(player =>
                player.GeneralId == GeneralId).Seat;
            var action = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.SupplyShortage && item.TargetSeat == owner);
            if (action is null) continue;
            var played = game.Submit(new PlayCardCommand(0, action.CardId!.Value,
                action.TargetSeats, game.Revision, game.PendingDecision.PromptId));
            Require(played.Accepted, played.Error?.Message ?? "Supply Shortage could not target Zhang Liao.");
            for (var step = 0; step < 60 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                AdvanceOne(game);
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard) continue;
            var ended = game.Submit(new EndPlayPhaseCommand(0, game.Revision,
                game.PendingDecision.PromptId));
            Require(ended.Accepted, ended.Error?.Message ?? "Supply Shortage fixture could not end Play.");
            for (var step = 0; step < 600 && !game.Events.Select(item => item.Payload)
                     .OfType<DelayedCardResolvedEvent>()
                     .Any(item => item.CardKind == CardKind.SupplyShortage && item.TargetSeat == owner); step++)
                AdvanceOne(game);
            var resolution = game.Events.Select(item => item.Payload)
                .OfType<DelayedCardResolvedEvent>().FirstOrDefault(item =>
                    item.CardKind == CardKind.SupplyShortage && item.TargetSeat == owner);
            if (resolution?.SkippedDrawPhase != true) continue;
            Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                    .All(item => item.SkillId != SkillId) &&
                    State(GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry)) == State(game),
                "Supply Shortage must skip the entire draw phase, including boundary Tuxi.");
            return;
        }
        throw new InvalidOperationException("No deterministic Supply Shortage skip fixture reached boundary Zhang Liao.");
    }

    private static void CheckPlan(int adjustment, int expectedPlan)
    {
        var registry = Registry(adjustment);
        var game = Start(registry);
        for (var step = 0; step < 30 &&
             game.PendingDecision?.SkillPrompt?.SkillId != SkillId &&
             game.State.Phase != TurnPhase.Play; step++)
            Advance(game);
        var initial = game.CreateSnapshot(0, true);
        Require(initial.Players[0].HandCount == 4 &&
                initial.Players.Skip(1).All(player => player.HandCount == 4),
            $"The public hand-count boundary fixture must start with equal four-card hands " +
            $"({string.Join(',', initial.Players.Select(player => $"{player.Seat}:{player.HandCount}"))}).");
        var activation = game.PendingDecision;
        if (expectedPlan == 0)
        {
            Require(activation?.SkillPrompt?.SkillId != SkillId &&
                    game.CreateSnapshot(0, true).Players[0].HandCount == 4,
                "A zero normal-draw plan must not offer a positive-count Tuxi choice.");
            return;
        }
        Require(activation is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, IsPrivate: true } &&
                activation.SkillPrompt?.SkillId == SkillId &&
                game.CreateSnapshot(1).PendingDecision is null,
            "Tuxi must publish a private optional draw-phase activation.");
        var skip = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Answer(game, "activate");
        var prompt = game.PendingDecision!;
        Require(prompt.IsPrivate && prompt.SkillPrompt?.SkillId == SkillId &&
                prompt.ValidTargetSeats.Count == 4 &&
                prompt.Choices.Max(choice => choice.Targets.Count) == expectedPlan &&
                prompt.Choices.All(choice => choice.Targets.Count is >= 1 and <= 4 &&
                    choice.Targets.Distinct().Count() == choice.Targets.Count &&
                    choice.Parameters.GetValueOrDefault("maximum-targets") == expectedPlan.ToString()) &&
                prompt.Prompt.Contains($"1 至 {expectedPlan}", StringComparison.Ordinal) ==
                    (expectedPlan > 1),
            "The target prompt must freeze the current adjusted plan and public hand eligibility.");
        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "Target selection must restore with its frozen plan and private choices.");
        var forged = game.Submit(new AnswerPromptCommand(0, prompt.PromptId,
            new ChoiceId("program-targets.forged"), game.Revision));
        Require(!forged.Accepted && State(paused) == State(game),
            "A forged target set must reject atomically.");
        var chosen = prompt.Choices.First(choice => choice.Targets.Count == expectedPlan);
        Answer(game, chosen);
        Answer(paused, paused.PendingDecision!.Choices.Single(choice => choice.Id == chosen.Id));
        Require(game.State.Phase == TurnPhase.Play &&
                game.CreateSnapshot(0, true).Players[0].HandCount == 4 + expectedPlan &&
                chosen.Targets.All(seat => game.CreateSnapshot(0, true).Players[seat].HandCount == 3) &&
                game.Events.Select(item => item.Payload).OfType<ProgramNormalDrawAdjustedEvent>()
                    .Any(item => item.SkillId == SkillId && item.Adjustment == -expectedPlan) &&
                game.Events.Select(item => item.Payload).OfType<ProgramRandomHandCardsTakenEvent>()
                    .Any(item => item.SkillId == SkillId && item.CardCount == expectedPlan &&
                        item.TargetSeats.SequenceEqual(chosen.Targets)) &&
                State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "Tuxi must reduce normal draw before taking one hidden card from each selected target and replay.");

        Answer(skip, "skip");
        Require(skip.State.Phase == TurnPhase.Play &&
                skip.CreateSnapshot(0, true).Players[0].HandCount == 4 + expectedPlan &&
                !skip.Events.Select(item => item.Payload).OfType<ProgramRandomHandCardsTakenEvent>()
                    .Any(item => item.SkillId == SkillId),
            "Declining Tuxi must retain the adjusted normal draw plan without transferring cards.");
    }

    private static ContentRegistry Registry(int adjustment, bool preDraw = false, bool replacement = false) => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new Scenario(adjustment, preDraw, replacement));

    private static GameEngine Start(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 5, ModeId = Scenario.ModeId,
            HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fixture did not start.");
        var prompt = game.PendingDecision!;
        Require(game.Submit(new SelectGeneralCommand(0, Scenario.OwnerId,
            game.Revision, prompt.PromptId)).Accepted, "Fixture owner could not be selected.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Fixture could not advance.");
    }

    private static void AdvanceOne(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Supply Shortage fixture did not advance.");
    }

    private static void Answer(GameEngine game, string action) =>
        Answer(game, game.PendingDecision!.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action));

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var result = game.Submit(new AnswerPromptCommand(0, game.PendingDecision!.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Fixture prompt answer failed.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(string rules, string presentation, string message)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected a program validation failure containing '{message}'.");
    }

    private const string Presentation = """
        {"schemaVersion":3,"skills":{"boundary:tuxi":{"name":"突袭","description":"测试"}}}
        """;
    private sealed class Scenario(int adjustment, bool preDraw, bool replacement) : IGameContentPackage
    {
        public const string ModeId = "identity:boundary-zhang-liao-check-5";
        public const string OwnerId = "fixture:boundary-zhang-liao-owner";
        public PackageManifest Manifest { get; } = new("boundary-zhang-liao-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var extra = new List<string>();
            if (adjustment != 0)
            {
                extra.Add("fixture:draw-adjustment");
                var catalog = SkillProgramCatalog.Load($$"""
                    {"schemaVersion":59,"skills":[{"id":"fixture:draw-adjustment","revision":1,
                    "minimumRulesVersion":169,"triggers":[{"id":"adjust","window":"drawPhaseStarting",
                    "subject":"owner","optional":false,"priority":200,"effects":[{"op":"adjustNormalDraw",
                    "target":"owner","amount":{{adjustment}}}]}]}]}
                    """, """
                    {"schemaVersion":3,"skills":{"fixture:draw-adjustment":{"name":"摸牌调整","description":"测试"}}}
                    """);
                builder.AddSkill(new ContentSkillDefinition("fixture:draw-adjustment", "摸牌调整", "测试")
                { Program = catalog.Programs["fixture:draw-adjustment"] });
            }
            if (preDraw)
            {
                extra.Add("fixture:pre-draw");
                var catalog = SkillProgramCatalog.Load("""
                    {"schemaVersion":59,"skills":[{"id":"fixture:pre-draw","revision":1,
                    "minimumRulesVersion":169,"triggers":[{"id":"early","window":"drawPhaseStarting",
                    "subject":"owner","optional":false,"priority":200,"effects":[
                    {"op":"draw","target":"owner","amount":1}]}]}]}
                    """, """
                    {"schemaVersion":3,"skills":{"fixture:pre-draw":{"name":"先摸牌","description":"测试"}}}
                    """);
                builder.AddSkill(new ContentSkillDefinition("fixture:pre-draw", "先摸牌", "测试")
                { Program = catalog.Programs["fixture:pre-draw"] });
            }
            if (replacement) extra.Add("classic:tuxi");
            builder.AddGeneral(new ContentGeneralDefinition(OwnerId, "突袭测试", "supporter",
                SkillId, "wei", BaseHp: 4, AdditionalSkillIds: extra));
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:boundary-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "目标", "supporter",
                    "standard:none", "wei", BaseHp: 4));
            builder.AddMode(new ContentModeDefinition(ModeId, "界张辽测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "classic:standard-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerId, .. targets]));
        }
    }

    private sealed class SupplyScenario : IGameContentPackage
    {
        public const string ModeId = "identity:boundary-zhang-liao-supply-5";
        public const string HumanId = "fixture:supply-human";
        public PackageManifest Manifest { get; } = new("boundary-zhang-liao-supply", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var targets = Enumerable.Range(1, 3).Select(index => $"fixture:supply-target-{index}").ToArray();
            foreach (var target in new[] { HumanId }.Concat(targets))
                builder.AddGeneral(new ContentGeneralDefinition(target, "兵粮测试", "supporter",
                    "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:boundary-supply-deck", "兵粮测试牌堆", 4, 2,
                [new ContentDeckCardCount("standard:supply_shortage", 30),
                 new ContentDeckCardCount("standard:dodge", 90)]));
            builder.AddMode(new ContentModeDefinition(ModeId, "界张辽兵粮测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:boundary-supply-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [GeneralId, HumanId, .. targets]));
        }
    }
}
