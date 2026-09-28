using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DengAiChecks
{
    private const string General = "classic:deng-ai";
    private const string Tuntian = "classic:tuntian";
    private const string Zaoxian = "classic:zaoxian";
    private const string Jixi = "classic:jixi";
    private const string Mode = "identity:classic-deng-ai-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 4, FactionId: "wei" } general &&
                general.SkillIds.SequenceEqual([Tuntian, Zaoxian]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "Deng Ai must start with Tuntian and the dormant Zaoxian; Jixi arrives only via awakening.");

        var tuntian = current.Skills[Tuntian].Program!;
        var lossTriggers = tuntian.Triggers.Where(item => item.Window == SkillProgramTriggerWindow.CardsMoved).ToArray();
        Require(lossTriggers.Length == 3 &&
                lossTriggers.Select(item => item.SourceZones.Single()).Order().SequenceEqual(
                    new[] { CardZoneKind.Equipment, CardZoneKind.Hand, CardZoneKind.Judgment }.Order()) &&
                lossTriggers.All(item => item.Subject == SkillProgramTriggerSubject.Owner &&
                    item.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch &&
                    item.Optional &&
                    item.IgnoreOwnSkillMovements &&
                    item.Condition.Kind == SkillProgramTriggerConditionKind.Not &&
                    item.Condition.Children.Single().Kind == SkillProgramTriggerConditionKind.OwnerIsTurnPlayer),
            "Tuntian must offer one optional judgment per outside-turn loss batch from hand, equipment and judgment zones.");
        var handTrigger = lossTriggers.Single(item => item.SourceZones.Single() == CardZoneKind.Hand);
        Require(handTrigger.ExcludedMovementReasons.SequenceEqual(
                ["card.use", "card.respond", "card.respond.nullification"]),
            "Tuntian must exclude the owner's own use and response movements from hand losses.");
        var expectedEffects = new[]
        {
            SkillProgramEffectOp.StartJudgment, SkillProgramEffectOp.FilterBoundCards,
            SkillProgramEffectOp.MoveBoundCards, SkillProgramEffectOp.MoveBoundCards
        };
        Require(lossTriggers.All(item => item.Effects.Select(effect => effect.Op).SequenceEqual(expectedEffects)) &&
                lossTriggers.All(item => item.Effects[0].JudgmentReason == "skill.tuntian" &&
                    item.Effects[0].ResultBind == "tuntian-judgment" &&
                    item.Effects[0].Visibility == SkillProgramCardSetVisibility.Public) &&
                lossTriggers.All(item => item.Effects[1].Suits.SequenceEqual([Suit.Spade, Suit.Club, Suit.Diamond]) &&
                    item.Effects[1].SourceBind == "tuntian-judgment" &&
                    item.Effects[1].ResultBind == "tian-cards") &&
                lossTriggers.All(item => item.Effects[2].SourceBind == "tian-cards" &&
                    item.Effects[2].Destination == SkillProgramCardDestination.OwnerPersistentZone &&
                    item.Effects[2].DestinationZone == CardZoneKind.Authority) &&
                lossTriggers.All(item => item.Effects[3].SourceBind == "tuntian-judgment" &&
                    item.Effects[3].ExceptBind == "tian-cards" &&
                    item.Effects[3].Destination == SkillProgramCardDestination.DiscardPile),
            "Tuntian must move non-heart judgment cards onto the authority zone and discard the heart remainder.");

        var modifier = tuntian.Modifiers.Single();
        Require(modifier.Query == SkillRuleQuery.OutgoingDistance &&
                modifier.Operation == SkillRuleOperation.Add &&
                modifier.ValueExpression == SkillRuleValueExpression.NegatedOwnedZoneCount &&
                modifier.ValueZone == CardZoneKind.Authority,
            "Tuntian must reduce outgoing distance by the number of authority-zone fields.");

        var zaoxian = current.Skills[Zaoxian].Program!;
        var awakening = zaoxian.Triggers.Single();
        Require(awakening.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
                !awakening.Optional &&
                awakening.UsageScope == SkillUsageScope.Game &&
                awakening.UsageLimit == 1 &&
                awakening.Condition is
                {
                    Kind: SkillProgramTriggerConditionKind.Compare,
                    Comparison: SkillProgramComparisonOperator.GreaterThanOrEqual,
                    Left.Kind: SkillProgramTriggerValueKind.CurrentOwnedZoneCount,
                    Left.Zone: CardZoneKind.Authority,
                    Right.Kind: SkillProgramTriggerValueKind.IntegerConstant,
                    Right.Value: 3
                },
            "Zaoxian must awaken once per game at turn start with at least three fields.");
        Require(awakening.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.ChangeMaximumHp,
            SkillProgramEffectOp.GrantSkills]) &&
                awakening.Effects[0].Amount == -1 &&
                awakening.Effects[1].SkillIds.SequenceEqual([Jixi]),
            "Zaoxian must lose one maximum HP and grant Jixi.");

        var viewAs = current.Skills[Jixi].Program!.ViewAs.Single();
        Require(viewAs.SourceZones.SequenceEqual([CardZoneKind.Authority]) &&
                viewAs.InputSuits.SequenceEqual([Suit.Spade, Suit.Club, Suit.Diamond]) &&
                viewAs.OutputKind == CardKind.Snatch &&
                viewAs.ForPlay && !viewAs.ForResponse,
            "Jixi must convert one authority-zone field into a proactive Snatch only.");

        const string negatedTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:negated","revision":1,
            "minimumRulesVersion": 183,
            "modifiers":[{"id":"m","query":"QUERY","operation":"add","valueExpression":"negatedOwnedZoneCount","valueZone":"ZONE","priority":0}],
            "triggers":[]}]}
            """;
        const string negatedPresentation = """
            {"schemaVersion":3,"skills":{"fixture:negated":{"name":"测试","description":"测试"}}}
            """;
        Reject(negatedTemplate.Replace("QUERY", "outgoingDistance").Replace("ZONE", "hand"),
            negatedPresentation, "negatedOwnedZoneCount requires a persistent owner zone");
        Reject(negatedTemplate.Replace("QUERY", "handLimit").Replace("ZONE", "authority"),
            negatedPresentation, "negatedOwnedZoneCount is supported only by outgoingDistance");
        const string snatchTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:snatch","revision":1,
            "minimumRulesVersion": 183,
            "viewAs":[{"id":"v","sourceZones":["hand"],"inputSuits":["spade"],"outputKind":"snatch","forPlay":FORRESPONSE,"forResponse":FORRESPONSE}],
            "triggers":[]}]}
            """;
        const string snatchPresentation = """
            {"schemaVersion":3,"skills":{"fixture:snatch":{"name":"测试","description":"测试"}}}
            """;
        Reject(snatchTemplate.Replace("FORRESPONSE", "true"), snatchPresentation,
            "snatch viewAs must stay play-only");
    }

    public static void TuntianStoresFieldsReducesDistanceAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            EndPlay(game);
            DriveAndCultivate(game, targetFields: 1);
            var fields = game.CreateSnapshot(0, true).Players[0].AuthorityCards!;
            if (game.State.Status == EngineStatus.Completed || fields.Count < 1)
                continue;

            var distanceTarget = FindBaseDistanceTwoOpponent(game);
            if (distanceTarget < 0) continue;
            Require(game.GetCombatDistance(0, distanceTarget) == 1,
                "One field must reduce the owner's outgoing distance to a base-distance-two opponent.");

            var stored = game.CardMovements.Where(item => item.To == CardLocation.Authority(0) &&
                    item.Reason.Value.Contains(Tuntian, StringComparison.Ordinal)).ToArray();
            var judged = game.CardMovements.Where(item => item.From == CardLocation.Judgment(0) &&
                    item.To == CardLocation.Processing).ToArray();
            var discardedHearts = judged.Count(item =>
                game.CardMovements.Any(move => move.CardId == item.CardId &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason.Value.Contains(Tuntian, StringComparison.Ordinal)));
            Require(stored.Length == fields.Count &&
                    stored.All(item => item.Reason.Value.Contains(Tuntian, StringComparison.Ordinal)),
                "Every stored field must enter the authority zone via Tuntian.");
            Require(discardedHearts + stored.Length == judged.Length,
                "Every Tuntian judgment card must either become a field or be discarded.");

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The stored fields and distance must replay identically.");
            completed++;
        }
        Require(completed == 1, "No seeded setup stored a field via Tuntian.");
    }

    public static void HeartJudgmentStaysOutAndDistanceUnchanged()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            EndPlay(game);
            DriveAndCultivate(game, targetFields: 0, stopAtHeartJudgment: true);
            var heartDiscard = game.CardMovements.FirstOrDefault(item =>
                item.To == CardLocation.DiscardPile &&
                item.Reason.Value.Contains(Tuntian, StringComparison.Ordinal) &&
                game.CardMovements.Any(judged => judged.CardId == item.CardId &&
                    judged.From == CardLocation.Judgment(0) &&
                    judged.To == CardLocation.Processing));
            if (game.State.Status == EngineStatus.Completed || heartDiscard is null)
                continue;
            Require(!game.CreateSnapshot(0, true).Players[0].AuthorityCards!.Any(card => card.Id == heartDiscard.CardId),
                "A heart judgment must not become a field.");
            var opponent = AliveOpponents(game).FirstOrDefault();
            if (opponent < 0) continue;
            Require(game.GetCombatDistance(0, opponent) == BaseDistance(game, 0, opponent),
                "Without fields the outgoing distance must stay unchanged.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a heart Tuntian judgment.");
    }

    public static void ZaoxianAwakensGrantsJixiAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            EndPlay(game);
            DriveAndCultivate(game, targetFields: 3, stopAtAwakening: true);
            var awakened = game.Events.Select(item => item.Payload)
                .OfType<SkillAwakenedEvent>()
                .SingleOrDefault(item => item.SkillId == Zaoxian);
            if (game.State.Status == EngineStatus.Completed || awakened is null)
                continue;
            Require(awakened.PlayerSeat == 0 && awakened.MaximumHp == 4 &&
                    awakened.AcquiredSkillIds.SequenceEqual([Jixi]),
                $"Zaoxian must reduce the lord Deng Ai's five maximum HP to four; got seat={awakened.PlayerSeat} maxHp={awakened.MaximumHp} acquired=[{string.Join(",", awakened.AcquiredSkillIds)}].");
            var snapshot = game.CreateSnapshot(0, true).Players[0];
            Require(snapshot.AuthorityCards!.Count >= 3 &&
                    snapshot.Skills!.Select(item => item.ContentId).Contains(Jixi),
                "The awakened Deng Ai must keep his fields and own Jixi.");
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Zaoxian awakening must replay identically.");
            completed++;
        }
        Require(completed == 1, "No seeded setup awakened Zaoxian with three fields.");
    }

    public static void JixiConvertsFieldIntoSnatchAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            EndPlay(game);
            DriveAndCultivate(game, targetFields: 3, stopAtAwakening: true);
            if (game.Events.Select(item => item.Payload).OfType<SkillAwakenedEvent>()
                    .SingleOrDefault(item => item.SkillId == Zaoxian) is null ||
                game.State.Status == EngineStatus.Completed)
                continue;
            if (!TryReachOwnPlay(game)) continue;

            var fields = game.CreateSnapshot(0, true).Players[0].AuthorityCards!;
            if (fields.Count == 0) continue;
            var fieldCardId = fields[0].Id;
            var target = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Snatch &&
                action.CardId == fieldCardId &&
                action.ConversionSource is { } source && source.SkillId == Jixi &&
                action.PlayedCardKind == CardKind.Snatch);
            if (target is null) continue;

            var targetSeat = target.TargetSeat!.Value;
            var beforeTargetCards = CountTargetCards(game, targetSeat);
            Play(game, target);
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            Require(!game.CreateSnapshot(0, true).Players[0].AuthorityCards!.Any(card => card.Id == fieldCardId),
                "The converted field must leave the authority zone when Jixi plays it.");
            Require(game.CardMovements.Any(item => item.From == CardLocation.Authority(0) &&
                        item.CardId == fieldCardId),
                "The played field must be recorded as moving out of the authority zone.");
            Require(CountTargetCards(game, targetSeat) < beforeTargetCards,
                "The Jixi snatch must take one card from its target.");
            completed++;
        }
        Require(completed == 1, "No seeded setup played a field as a Jixi snatch.");
    }

    private static bool IsAwakened(GameEngine game) =>
        game.Events.Select(item => item.Payload).OfType<SkillAwakenedEvent>()
            .Any(item => item.SkillId == Zaoxian);

    private static void DriveAndCultivate(GameEngine game, int targetFields,
        bool stopAtHeartJudgment = false, bool stopAtAwakening = false)
    {
        bool Reached() => stopAtHeartJudgment
            ? game.CardMovements.Any(item => item.To == CardLocation.DiscardPile &&
                item.Reason.Value.Contains(Tuntian, StringComparison.Ordinal))
            : game.CreateSnapshot(0, true).Players[0].AuthorityCards!.Count >= targetFields &&
              (!stopAtAwakening || IsAwakened(game));
        for (var step = 0; step < 6_000 && game.State.Status != EngineStatus.Completed && !Reached(); step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId == Tuntian && prompt.PlayerSeat == 0)
            {
                Activate(game, Tuntian);
                continue;
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

    private static int[] AliveOpponents(GameEngine game) =>
        game.CreateSnapshot(0, true).Players
            .Where(player => player.IsAlive && player.Seat != 0)
            .Select(player => player.Seat).Order().ToArray();

    private static void DriveUntil(GameEngine game, Func<bool> done, int budget = 500)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null || prompt.PlayerSeat != 0)
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

    private static int BaseDistance(GameEngine game, int sourceSeat, int targetSeat)
    {
        var alive = game.CreateSnapshot(0, true).Players.Where(player => player.IsAlive)
            .Select(player => player.Seat).Order().ToArray();
        var sourceIndex = Array.IndexOf(alive, sourceSeat);
        var targetIndex = Array.IndexOf(alive, targetSeat);
        var ring = Math.Abs(sourceIndex - targetIndex);
        return Math.Min(ring, alive.Length - ring);
    }

    private static int FindBaseDistanceTwoOpponent(GameEngine game)
    {
        var alive = game.CreateSnapshot(0, true).Players.Where(player => player.IsAlive && player.Seat != 0)
            .Select(player => player.Seat).Order();
        return alive.FirstOrDefault(seat =>
            seat != 0 && BaseDistance(game, 0, seat) == 2 &&
            game.GetCombatDistance(0, seat) == 1, -1);
    }

    private static int CountTargetCards(GameEngine game, int seat)
    {
        var player = game.CreateSnapshot(0, true).Players.Single(item => item.Seat == seat);
        return player.Hand.Count + player.Equipment.Count;
    }

    private static bool TryReachOwnPlay(GameEngine game)
    {
        for (var step = 0; step < 400 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                return true;
            if (prompt is null || prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
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
        Require(result.Accepted, result.Error?.Message ?? "Deng Ai skill answer failed.");
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
            MaxTurns = 14
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Deng Ai fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Deng Ai selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Deng Ai fixture did not reach Play.");
    }

    private static void EndPlay(GameEngine game)
    {
        var prompt = game.PendingDecision;
        Require(prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "Deng Ai fixture lost the play decision before ending the phase.");
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt!.PromptId)));
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Deng Ai fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource });
        Require(result.Accepted, result.Error?.Message ?? "Deng Ai card action failed.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Deng Ai command failed.");
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
            $"Expected invalid Deng Ai composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("deng-ai-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 153, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; the four opponents are durable banks so the
            // AI turn keeps playing dismantles and snatches against Deng Ai's cards
            // while the owner stays alive long enough to awaken.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:deng-ai-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:deng-ai-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:deng-ai-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:deng-ai-bank-d", "测试对手四",
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
            builder.AddDeck(new ContentDeckRecipe("fixture:deng-ai-deck", "邓艾测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "邓艾测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:deng-ai-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:deng-ai-bank-a",
                    "fixture:deng-ai-bank-b",
                    "fixture:deng-ai-bank-c",
                    "fixture:deng-ai-bank-d"]));
        }
    }
}
