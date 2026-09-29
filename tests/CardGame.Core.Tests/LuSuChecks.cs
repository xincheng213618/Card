using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LuSuChecks
{
    private const string General = "classic:lu-su";
    private const string Haoshi = "classic:haoshi";
    private const string HaoshiGive = "classic:haoshi-give";
    private const string Dimeng = "classic:dimeng";
    private const string Mode = "identity:classic-lu-su-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "wu" } general &&
                general.SkillIds.SequenceEqual([Haoshi, Dimeng]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Lu Su must be in the current Wu roster with three HP.");

        var haoshi = current.Skills[Haoshi].Program!;
        var draw = haoshi.Triggers.Single();
        Require(draw.Window == SkillProgramTriggerWindow.DrawPhaseStarting &&
                draw.Subject == SkillProgramTriggerSubject.Owner &&
                draw.Optional &&
                draw.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.Draw,
                SkillProgramEffectOp.GrantTurnSkills]) &&
                draw.Effects.Single(item => item.Op == SkillProgramEffectOp.Draw).Amount == 2 &&
                draw.Effects.Single(item =>
                    item.Op == SkillProgramEffectOp.GrantTurnSkills).SkillIds.SequenceEqual([HaoshiGive]),
            "Haoshi must offer two extra draw cards and grant the turn-scoped give skill.");

        var give = current.Skills[HaoshiGive].Program!.Triggers.Single();
        Require(give.Window == SkillProgramTriggerWindow.AfterNormalDraw &&
                give.Subject == SkillProgramTriggerSubject.Owner &&
                !give.Optional &&
                ConditionHas(give.Condition, SkillProgramTriggerConditionKind.Compare),
            "The granted Haoshi give must fire mandatorily after the owner's normal draw when hand exceeds five.");
        Require(give.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.SelectTarget,
            SkillProgramEffectOp.SelectOwnedCards,
            SkillProgramEffectOp.MoveBoundCards]),
            "The give must target the least-hand player, select half the hand and move it.");
        var giveSelection = give.Effects.Single(item => item.Op == SkillProgramEffectOp.SelectOwnedCards);
        Require(giveSelection.NumberExpression == SkillProgramNumberExpression.HandHalfFloor &&
                giveSelection.Zones.SequenceEqual([CardZoneKind.Hand]),
            "The give must select floor(hand/2) hand cards.");
        var giveMove = give.Effects.Single(item => item.Op == SkillProgramEffectOp.MoveBoundCards);
        Require(giveMove.Destination == SkillProgramCardDestination.SelectedTargetHand &&
                give.Effects.Single(item => item.Op == SkillProgramEffectOp.SelectTarget).TargetKind ==
                    SkillProgramTargetKind.OtherLivingLeastHandCount,
            "The give must move the bound cards to the selected least-hand player.");

        var dimeng = current.Skills[Dimeng].Program!;
        var activation = dimeng.Activations.Single();
        Require(activation.UsesPerTurn == 1 && activation.MinCards == 0 &&
                activation.TargetKind == SkillProgramTargetKind.OtherLivingPair &&
                activation.MinTargets == 2 && activation.MaxTargets == 2,
            "Dimeng must be a once-per-turn active skill choosing two other players.");
        Require(activation.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.SelectOwnedCards,
            SkillProgramEffectOp.MoveBoundCards,
            SkillProgramEffectOp.ExchangeSelectedTargetHands]),
            "Dimeng must discard the hand difference then exchange the pair's hands.");
        var cost = activation.Effects.First(item => item.Op == SkillProgramEffectOp.SelectOwnedCards);
        Require(cost.NumberExpression == SkillProgramNumberExpression.SelectedPairHandDifference &&
                cost.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]),
            "The Dimeng cost must equal the pair's hand-count difference from own hand and equipment.");

        const string giveTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:give","revision":1,
            "minimumRulesVersion": 184,
            "triggers":[{"id":"t","window":"afterNormalDraw","subject":"owner",
            "optional":false,
            "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingLeastHandCount"},
            {"op":"selectOwnedCards","target":"owner","zones":["hand"],
            "numberExpression":"handHalfFloor","resultBind":"gift"},
            {"op":"moveBoundCards","target":"owner","sourceBind":"gift",
            "destination":"selectedTargetHand"}]}]}]}
            """;
        const string givePresentation = """
            {"schemaVersion":3,"skills":{"fixture:give":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(giveTemplate, givePresentation)
                .Programs["fixture:give"].Triggers.Single().Effects.Count == 3,
            "The give chain must load with handHalfFloor and a selected-target hand move.");
        Reject(giveTemplate.Replace("\"zones\":[\"hand\"]", "\"zones\":[\"hand\",\"equipment\"]"),
            givePresentation, "handHalfFloor with non-hand zones");

        const string pairTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:pact","revision":1,
            "minimumRulesVersion": 184,
            "activations":[{"id":"pact","minCards":0,"maxCards":0,"sourceZones":["hand"],
            "minTargets":2,"maxTargets":2,"targetKind":"otherLivingPair",
            "usesPerTurn":1,"condition":{"kind":"always"},
            "effects":[{"op":"selectOwnedCards","target":"owner","zones":["hand","equipment"],
            "numberExpression":"selectedPairHandDifference","resultBind":"cost"},
            {"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"discardPile"},
            {"op":"exchangeSelectedTargetHands","target":"owner"}]}]}]}
            """;
        const string pairPresentation = """
            {"schemaVersion":3,"skills":{"fixture:pact":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(pairTemplate, pairPresentation)
                .Programs["fixture:pact"].Activations.Single().Effects.Count == 3,
            "The Dimeng chain must load with a hand-ordered pair and the exchange.");
        Reject(pairTemplate.Replace("\"minTargets\":2,\"maxTargets\":2", "\"minTargets\":1,\"maxTargets\":1"),
            pairPresentation, "hand-ordered pairs require exactly two targets");
        Reject(pairTemplate.Replace("\"op\":\"exchangeSelectedTargetHands\",\"target\":\"owner\"",
                "\"op\":\"exchangeSelectedTargetHands\",\"target\":\"selectedTarget\""),
            pairPresentation, "the exchange reads the ordered pair via the owner placeholder");
    }

    public static void HaoshiGivesHalfHandAfterExtraDrawAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (DriveUntilHaoshiPrompt(game) is null) continue;
            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            Activate(game, Haoshi);
            Activate(replay, Haoshi);
            CompleteGive(game);
            CompleteGive(replay);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Haoshi give must replay identically from the paused checkpoint.");
            var snapshot = game.CreateSnapshot(0, true);
            var given = game.CardMovements.Where(item =>
                item.Reason.Value.Contains(HaoshiGive, StringComparison.Ordinal) &&
                item.To.Zone == CardZoneKind.Hand && item.From.OwnerSeat == 0).ToArray();
            var others = snapshot.Players.Where(item => item.Seat != 0).ToArray();
            Require(given.Length > 0 &&
                    given.Length == given.Select(item => item.CardId).Count() &&
                    given.Length == (snapshot.Players[0].Hand.Count + given.Length) / 2,
                "The give must move floor(half) of the post-draw hand.");
            var giveTarget = given.Select(item => item.To.OwnerSeat).Distinct().Single();
            var recipientPreGive = snapshot.Players.Single(item => item.Seat == giveTarget).Hand.Count - given.Length;
            Require(others.Where(item => item.Seat != giveTarget)
                    .All(item => item.Hand.Count >= recipientPreGive),
                "The give recipient must have had the fewest hand cards among the others.");
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved the Haoshi give.");
    }

    private static bool ConditionHas(SkillProgramTriggerCondition condition,
        SkillProgramTriggerConditionKind kind) =>
        condition.Kind == kind || (condition.Children?.Any(child => ConditionHas(child, kind)) ?? false);

    private static PendingDecision? DriveUntilHaoshiPrompt(GameEngine game)
    {
        DriveUntil(game, () => false, stopAtSkills: [Haoshi]);
        return game.State.Status == EngineStatus.Completed ? null :
            IsProgramPrompt(game, Haoshi) ? game.PendingDecision : null;
    }

    private static void CompleteGive(GameEngine game)
    {
        DriveUntil(game, () => game.ResolutionStack.Count == 0);
    }

    private static bool IsProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0
        } prompt && prompt.SkillPrompt?.SkillId == skillId;

    private static void DriveUntil(
        GameEngine game,
        Func<bool> done,
        string[]? stopAtSkills = null,
        DecisionKind[]? stopAtDecisions = null,
        int budget = 800)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId is { } skillId &&
                stopAtSkills is not null && stopAtSkills.Contains(skillId))
            {
                return;
            }
            if (stopAtDecisions is not null && stopAtDecisions.Contains(prompt.Kind) &&
                prompt.PlayerSeat == 0)
            {
                return;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                    Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                    continue;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    continue;
                default:
                    var choice = prompt.Choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        prompt.Choices.First();
                    Answer(game, choice);
                    continue;
            }
        }
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario());

    private static void Activate(GameEngine game, string skillId)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            $"No pending decision to activate {skillId}.");
        Require(prompt.Kind == DecisionKind.ProgramTrigger,
            $"The {skillId} prompt must be a program trigger.");
        var activate = prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate") ??
            throw new InvalidOperationException($"The {skillId} prompt lost its activate choice.");
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            activate.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? $"{skillId} activation failed.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Lu Su skill answer failed.");
    }

    private static GameEngine Start(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = Mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Lu Su fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Lu Su selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Lu Su fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Lu Su command failed.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Reject(string rules, string presentation, string because = "")
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(
            $"Expected invalid Lu Su composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("lu-su-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 154, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1-4 are skill-less AI banks so the
            // only live skills are Haoshi and Dimeng on the human seat.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:lu-su-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:lu-su-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:lu-su-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:lu-su-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 180).Select(index => (index % 4) switch
            {
                1 => "standard:dodge",
                2 => "standard:peach",
                _ => "standard:slash"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:lu-su-deck", "鲁肃测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "鲁肃测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:lu-su-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:lu-su-bank-a",
                    "fixture:lu-su-bank-b",
                    "fixture:lu-su-bank-c",
                    "fixture:lu-su-bank-d"]));
        }
    }
}
