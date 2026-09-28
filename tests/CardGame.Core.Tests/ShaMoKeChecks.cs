using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShaMoKeChecks
{
    private const string General = "classic:sha-mo-ke";
    private const string Jili = "classic:jili";
    private const string Mode = "identity:classic-sha-mo-ke-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 4, FactionId: "shu" } general &&
                general.SkillIds.SequenceEqual([Jili]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "Sha Mo Ke must be in the current Shu roster with four HP and Jili.");

        var jili = current.Skills[Jili].Program!;
        var use = jili.Triggers.Single(item => item.Window == SkillProgramTriggerWindow.CardUseCommitted);
        var response = jili.Triggers.Single(item => item.Window == SkillProgramTriggerWindow.CardResponseAccepted);
        foreach (var trigger in new[] { use, response })
        {
            Require(trigger.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
                    trigger.Optional &&
                    trigger.Condition.Kind == SkillProgramTriggerConditionKind.Compare &&
                    trigger.Condition.Comparison == SkillProgramComparisonOperator.Equal &&
                    trigger.Condition.Left!.Kind == SkillProgramTriggerValueKind.CardsUsedOrRespondedThisTurn &&
                    trigger.Condition.Right!.Kind == SkillProgramTriggerValueKind.CurrentAttackRange &&
                    trigger.Effects.Select(item => item.Op).SequenceEqual([SkillProgramEffectOp.Draw]) &&
                    trigger.Effects[0].Target == SkillProgramEffectTarget.Owner &&
                    trigger.Effects[0].NumberExpression == SkillProgramNumberExpression.CurrentAttackRange,
                "Jili must offer an optional draw sized by the current attack range on the range-th use and response.");
        }

        const string windowTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:jili-window","revision":1,
            "minimumRulesVersion": 183,
            "triggers":[{"id":"t","window":"WINDOW","ownerRelation":"actor","optional":true,
            "condition":{"kind":"compare","left":{"kind":"cardsUsedOrRespondedThisTurn"},"operator":"equal","right":{"kind":"currentAttackRange"}},
            "effects":[{"op":"draw","target":"owner","numberExpression":"currentAttackRange"}]}]}]}
            """;
        const string windowPresentation = """
            {"schemaVersion":3,"skills":{"fixture:jili-window":{"name":"测试","description":"测试"}}}
            """;
        Load(windowTemplate.Replace("WINDOW", "cardUseCommitted"), windowPresentation);
        Reject(windowTemplate.Replace("WINDOW", "turnEnding"), windowPresentation,
            "attack-range comparison requires a card-action or Slash response boundary");

        const string mixedDraw = """
            {"schemaVersion":62,"skills":[{"id":"fixture:jili-draw","revision":1,
            "minimumRulesVersion": 183,
            "triggers":[{"id":"t","window":"cardUseCommitted","ownerRelation":"actor","optional":true,
            "condition":{"kind":"compare","left":{"kind":"cardsUsedOrRespondedThisTurn"},"operator":"equal","right":{"kind":"currentAttackRange"}},
            "effects":[{"op":"draw","target":"owner","amount":1,"numberExpression":"currentAttackRange"}]}]}]}
            """;
        Reject(mixedDraw, windowPresentation.Replace("jili-window", "jili-draw"),
            "draw accepts a constant or a supported public-state expression");
    }

    public static void FirstUseDrawsOnceSecondUseDoesNotAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            var first = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.CardId is { } && action.TargetSeat is { });
            if (first is null) continue;
            Play(game, first);
            DrivePlayResolution(game);

            var draws = game.CardMovements.Where(item =>
                item.To == CardLocation.Hand(0) &&
                item.Reason.Value.Contains(Jili, StringComparison.Ordinal)).ToArray();
            if (draws.Length != 1) continue;
            Require(draws[0].From == CardLocation.DrawPile,
                "The range-one first use must draw exactly one card from the draw pile.");

            var second = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind is not (LegalActionKind.EndPlay or LegalActionKind.Peach) &&
                action.CardId is { });
            if (second is null) continue;
            Play(game, second);
            DrivePlayResolution(game);
            Require(JiliDraws(game) == 1,
                "The second use in the same turn must not draw again.");

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Jili use draws must replay identically.");
            completed++;
        }
        Require(completed == 1, "No seeded setup exercised two Jili uses in one turn.");
    }

    public static void FirstResponseDuringForeignTurnDrawsAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (!DriveToFirstJiliPrompt(game)) continue;
            Activate(game, Jili);
            DrivePlayResolution(game);
            Require(JiliDraws(game) == 1,
                "The range-one first response in a foreign turn must draw exactly one card.");
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Jili response draw must replay identically.");
            completed++;
        }
        Require(completed == 1, "No seeded setup let Sha Mo Ke respond during a foreign turn.");
    }

    private static int JiliDraws(GameEngine game) => game.CardMovements.Count(item =>
        item.To == CardLocation.Hand(0) &&
        item.Reason.Value.Contains(Jili, StringComparison.Ordinal));

    private static void DrivePlayResolution(GameEngine game)
    {
        for (var step = 0; step < 600 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null || prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId == Jili)
            {
                Activate(game, Jili);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                    return;
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

    private static bool DriveToFirstJiliPrompt(GameEngine game)
    {
        for (var step = 0; step < 3_000 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId == Jili && prompt.PlayerSeat == 0)
                return true;
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
                case DecisionKind.RespondDodge:
                case DecisionKind.RespondSlash:
                {
                    var answer = prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "dodge" ||
                        choice.Parameters.GetValueOrDefault("response") == "slash");
                    if (answer is null) return false;
                    Answer(game, answer);
                    continue;
                }
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    continue;
                default:
                    var choice = prompt.Choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        prompt.Choices.FirstOrDefault(item =>
                            item.Parameters.GetValueOrDefault("response") == "take-damage") ??
                        prompt.Choices.FirstOrDefault(item =>
                            item.Parameters.GetValueOrDefault("response") == "pass") ??
                        prompt.Choices.First();
                    Answer(game, choice);
                    continue;
            }
        }
        return false;
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
        Require(result.Accepted, result.Error?.Message ?? "Sha Mo Ke skill answer failed.");
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
        Require(game.Submit(new StartGameCommand()).Accepted, "Sha Mo Ke fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Sha Mo Ke selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Sha Mo Ke fixture did not reach Play.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Sha Mo Ke fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Sha Mo Ke card action failed.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Sha Mo Ke command failed.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Load(string rules, string presentation) =>
        _ = SkillProgramCatalog.Load(rules, presentation);

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
            $"Expected invalid Sha Mo Ke composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("sha-mo-ke-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 153, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; the four opponents are durable banks so
            // rebel AI turns keep slashing the owner and open response windows.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:sha-mo-ke-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:sha-mo-ke-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:sha-mo-ke-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:sha-mo-ke-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 240).Select(index => (index % 6) switch
            {
                0 => "standard:slash",
                1 => "standard:dodge",
                2 => "standard:dismantlement",
                3 => "standard:snatch",
                4 => "standard:dodge",
                _ => "standard:slash"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:sha-mo-ke-deck", "沙摩柯测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "沙摩柯测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:sha-mo-ke-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:sha-mo-ke-bank-a",
                    "fixture:sha-mo-ke-bank-b",
                    "fixture:sha-mo-ke-bank-c",
                    "fixture:sha-mo-ke-bank-d"]));
        }
    }
}
