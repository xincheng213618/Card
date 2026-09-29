using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LingTongChecks
{
    private const string General = "classic:ling-tong";
    private const string Xuanfeng = "classic:xuanfeng";
    private const string PhaseMode = "identity:classic-ling-tong-phase-check-5";
    private const string EquipMode = "identity:classic-ling-tong-equip-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 4, FactionId: "wu" } general &&
                general.SkillIds.SequenceEqual([Xuanfeng]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Ling Tong must be in the current Wu roster with four HP.");

        var xuanfeng = current.Skills[Xuanfeng].Program!;
        var phase = xuanfeng.Triggers.Single(item => item.Id == "discard-phase-gust");
        Require(phase.Window == SkillProgramTriggerWindow.DiscardPhaseEnded &&
                phase.Subject == SkillProgramTriggerSubject.Owner &&
                phase.TurnOwnerScope == SkillProgramTurnOwnerScope.Own &&
                phase.Optional &&
                phase.Condition is
                {
                    Kind: SkillProgramTriggerConditionKind.Compare,
                    Comparison: SkillProgramComparisonOperator.GreaterThanOrEqual,
                    Left: { Kind: SkillProgramTriggerValueKind.TurnOwnerDiscardPhaseHandDiscardCount },
                    Right: { Kind: SkillProgramTriggerValueKind.IntegerConstant, Value: 2 }
                },
            "Xuanfeng must watch its own discard phase end after at least two hand-card discards.");
        Require(phase.Effects.Count == 2 && phase.Effects.All(effect =>
                effect.Op == SkillProgramEffectOp.ChooseOtherOwnedCardDiscard &&
                effect.Target == SkillProgramEffectTarget.Owner &&
                effect.ChooserRef is { Kind: ProgramParticipantRef.Owner } &&
                effect.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
                effect.Condition.Kind == SkillProgramConditionKind.Always),
            "Each discard-phase step must let the owner discard one card from another character.");

        var equipment = xuanfeng.Triggers.Single(item => item.Id == "equipment-loss-gust");
        Require(equipment.Window == SkillProgramTriggerWindow.CardsMoved &&
                equipment.Subject == SkillProgramTriggerSubject.Owner &&
                equipment.SourceZones.SequenceEqual([CardZoneKind.Equipment]) &&
                equipment.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch &&
                equipment.Optional &&
                equipment.ExcludedMovementReasons.Count == 0 &&
                !equipment.IgnoreOwnSkillMovements &&
                equipment.Condition.Kind == SkillProgramTriggerConditionKind.Always,
            "Xuanfeng must observe every loss from its own equipment zone, whatever the cause.");
        Require(equipment.Effects.Select(effect => effect.Op).SequenceEqual(
                [SkillProgramEffectOp.ChooseOtherOwnedCardDiscard, SkillProgramEffectOp.ChooseOtherOwnedCardDiscard]),
            "The equipment-loss branch must offer the same two sequential discards.");

        const string gustTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:gust","revision":1,
            "minimumRulesVersion": 192,
            "triggers":[
            {"id":"phase","window":"discardPhaseEnded","subject":"owner","turnOwnerScope":"own",
            "optional":true,"priority":0,
            "condition":{"kind":"compare","left":{"kind":"turnOwnerDiscardPhaseHandDiscardCount"},
            "operator":"greaterThanOrEqual","right":{"kind":"integerConstant","value":2}},
            "effects":[
            {"op":"chooseOtherOwnedCardDiscard","target":"owner","chooserRef":{"kind":"owner"},"zones":["hand","equipment"]},
            {"op":"chooseOtherOwnedCardDiscard","target":"owner","chooserRef":{"kind":"owner"},"zones":["hand","equipment"]}]},
            {"id":"loss","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],
            "movementOccurrence":"perBatch","optional":true,"priority":0,
            "effects":[
            {"op":"chooseOtherOwnedCardDiscard","target":"owner","chooserRef":{"kind":"owner"},"zones":["hand","equipment"]},
            {"op":"chooseOtherOwnedCardDiscard","target":"owner","chooserRef":{"kind":"owner"},"zones":["hand","equipment"]}]}]}]}
            """;
        const string gustPresentation = """
            {"schemaVersion":3,"skills":{"fixture:gust":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(gustTemplate, gustPresentation)
                .Programs["fixture:gust"].Triggers.Count == 2,
            "Both Xuanfeng branches must load through the shared program parser.");
        Reject(gustTemplate.Replace(
                "{\"id\":\"phase\",\"window\":\"discardPhaseEnded\",\"subject\":\"owner\",\"turnOwnerScope\":\"own\",",
                "{\"id\":\"phase\",\"window\":\"discardPhaseEnded\",\"subject\":\"owner\",\"turnOwnerScope\":\"own\",\"sourceZones\":[\"equipment\"],"),
            gustPresentation, "the discard-phase branch must not declare movement filters");
        Reject(gustTemplate.Replace(
                "{\"id\":\"loss\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"equipment\"],",
                "{\"id\":\"loss\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"equipment\"],\"turnOwnerScope\":\"own\","),
            gustPresentation, "the movement branch must not declare a turn-owner scope");
        Reject(gustTemplate.Replace(
                "{\"id\":\"loss\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"equipment\"],",
                "{\"id\":\"loss\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"equipment\"],\"suits\":[\"club\"],"),
            gustPresentation, "the movement branch must not declare a suit filter");
        Reject(gustTemplate.Replace(
                "\"left\":{\"kind\":\"turnOwnerDiscardPhaseHandDiscardCount\"}",
                "\"left\":{\"kind\":\"turnOwnerDiscardPhaseHandDiscardCount\",\"value\":2}"),
            gustPresentation, "only integer constants accept a literal value");
        Reject(gustTemplate.Replace(
                "\"op\":\"chooseOtherOwnedCardDiscard\",\"target\":\"owner\"",
                "\"op\":\"chooseOtherOwnedCardDiscard\",\"target\":\"selectedTarget\""),
            gustPresentation, "the discard step must act through its owner");
        Reject(gustTemplate.Replace(
                "\"zones\":[\"hand\",\"equipment\"]",
                "\"zones\":[\"hand\",\"discardPile\"]", StringComparison.Ordinal),
            gustPresentation, "the discard choices must read playable character zones");
        Reject(gustTemplate.Replace(
                ",\"chooserRef\":{\"kind\":\"owner\"}", "", StringComparison.Ordinal),
            gustPresentation, "the discard step must name its chooser");
        const string overlapTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:overlap","revision":1,
            "minimumRulesVersion": 192,
            "triggers":[{"id":"loss","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],
            "movementOccurrence":"perBatch","optional":true,
            "movementReasons":["card.effect.dismantlement"],
            "excludedMovementReasons":["card.effect.dismantlement"],
            "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;
        Reject(overlapTemplate, gustPresentation,
            "movement reasons must not include and exclude the same id");
    }

    public static void DiscardPhaseGustDiscardsFromOthersAndReplays()
    {
        var registry = Registry(PhaseMode, SlashDeck());
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed, PhaseMode);
            DriveUntil(game, () => false, stopAtSkills: [Xuanfeng]);
            if (!IsProgramPrompt(game, Xuanfeng)) continue;

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            AcceptTrigger(game);
            AcceptTrigger(replay);
            AnswerOtherCardDiscard(game);
            AnswerOtherCardDiscard(replay);
            AnswerOtherCardDiscard(game);
            AnswerOtherCardDiscard(replay);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The discard-phase Xuanfeng must replay identically from the paused prompt.");
            Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == Xuanfeng &&
                        item.Window == SkillProgramTriggerWindow.DiscardPhaseEnded && item.Activated),
                "The discard-phase Xuanfeng must resolve as an activated discardPhaseEnded binding.");
            var gusts = game.CardMovements.Where(item =>
                    item.Reason.Value.Contains("xuanfeng", StringComparison.Ordinal) &&
                    item.To == CardLocation.DiscardPile).ToArray();
            Require(gusts.Length == 2 &&
                    gusts.All(item => item.From.OwnerSeat is { } owner && owner != 0),
                "Xuanfeng must discard exactly one card from each of two other characters.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a discard-phase Xuanfeng prompt.");
    }

    public static void DeclinedGustDiscardsNothingAndReplays()
    {
        var registry = Registry(PhaseMode, SlashDeck());
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed, PhaseMode);
            DriveUntil(game, () => false, stopAtSkills: [Xuanfeng]);
            if (!IsProgramPrompt(game, Xuanfeng)) continue;

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            AcceptTrigger(game);
            AcceptTrigger(replay);
            DeclineOtherCardDiscard(game);
            DeclineOtherCardDiscard(replay);
            DeclineOtherCardDiscard(game);
            DeclineOtherCardDiscard(replay);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The declined Xuanfeng must replay identically from the paused prompt.");
            Require(!game.CardMovements.Any(item =>
                    item.Reason.Value.Contains("xuanfeng", StringComparison.Ordinal)),
                "Declining both steps must not move any card by Xuanfeng.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a declinable Xuanfeng prompt.");
    }

    public static void EquipmentLossGustDiscardsFromOthersAndReplays()
    {
        var registry = Registry(EquipMode, CrossbowDeck());
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed, EquipMode);
            DriveToEquipmentLoss(game);
            if (!IsProgramPrompt(game, Xuanfeng)) continue;
            Require(game.CardMovements.Any(item => item.From == CardLocation.Equipment(0) &&
                    item.To == CardLocation.DiscardPile),
                "The replaced crossbow must leave Ling Tong's equipment zone for the discard pile.");

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            AcceptTrigger(game);
            AcceptTrigger(replay);
            AnswerOtherCardDiscard(game);
            AnswerOtherCardDiscard(replay);
            AnswerOtherCardDiscard(game);
            AnswerOtherCardDiscard(replay);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The equipment-loss Xuanfeng must replay identically from the paused prompt.");
            Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == Xuanfeng &&
                        item.Window == SkillProgramTriggerWindow.CardsMoved && item.Activated),
                "The equipment-loss Xuanfeng must resolve as an activated cardsMoved binding.");
            var gusts = game.CardMovements.Where(item =>
                    item.Reason.Value.Contains("xuanfeng", StringComparison.Ordinal) &&
                    item.To == CardLocation.DiscardPile).ToArray();
            Require(gusts.Length == 2 &&
                    gusts.All(item => item.From.OwnerSeat is { } owner && owner != 0),
                "The equipment-loss Xuanfeng must discard one card from each of two other characters.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced an equipment-loss Xuanfeng prompt.");
    }

    private static void AnswerOtherCardDiscard(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The other-card discard prompt vanished.");
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-discard") ??
            throw new InvalidOperationException("The discard prompt lost its card choices.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
    }

    private static void DeclineOtherCardDiscard(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The other-card discard prompt vanished.");
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-decline") ??
            throw new InvalidOperationException("The discard prompt lost its decline option.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
    }

    private static void AcceptTrigger(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The program trigger prompt vanished.");
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") != "skip") ??
            throw new InvalidOperationException("The program trigger lost its accept option.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
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
        int budget = 900)
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
                    Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                        choice.Id, game.Revision)));
                    continue;
            }
        }
    }

    private static void DriveToEquipmentLoss(GameEngine game)
    {
        for (var step = 0; step < 900 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (IsProgramPrompt(game, Xuanfeng)) return;
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.PlayCard)
            {
                var equip = game.GetHumanLegalActions().FirstOrDefault(
                    item => item.Kind == LegalActionKind.Equip && item.CardId is not null);
                if (equip is not null)
                {
                    Accept(game.Submit(new PlayCardCommand(0, equip.CardId!.Value,
                        equip.TargetSeats, game.Revision, prompt.PromptId,
                        equip.PlayedCardKind, equip.TargetCardId)));
                    continue;
                }
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                continue;
            }
            if (prompt.Kind == DecisionKind.DiscardCards)
            {
                Accept(game.Submit(new DiscardCardsCommand(0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision)));
                continue;
            }
            var fallback = prompt.Choices.FirstOrDefault(item =>
                item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                prompt.Choices.FirstOrDefault();
            if (fallback is null)
            {
                Advance(game);
                continue;
            }
            Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                fallback.Id, game.Revision)));
        }
    }

    private static void DriveUntilSettled(GameEngine game) =>
        DriveUntil(game, () => game.ResolutionStack.Count == 0);

    private static ContentRegistry Registry(string mode, ContentDeckRecipe deck) =>
        ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new LingTongScenario(mode, deck));

    private static ContentDeckRecipe SlashDeck() =>
        DeckCore("standard:slash");

    private static ContentDeckRecipe CrossbowDeck() =>
        DeckCore("standard:crossbow");

    private static ContentDeckRecipe DeckCore(string kind) =>
        new("fixture:ling-tong-deck", "凌统测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard(kind, Suit.Spade, index % 13 + 1)).ToArray()
        };

    private static GameEngine Start(ContentRegistry registry, int seed, string mode)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 40
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Ling Tong fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Ling Tong selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Ling Tong fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Ling Tong command failed.");
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
            $"Expected invalid Ling Tong composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class LingTongScenario(string modeId, ContentDeckRecipe deck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("ling-tong-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 155, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1-4 are skill-less AI banks so the
            // only live skill is Xuanfeng on the human seat.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:ling-tong-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:ling-tong-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:ling-tong-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:ling-tong-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "凌统测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:ling-tong-bank-a",
                    "fixture:ling-tong-bank-b",
                    "fixture:ling-tong-bank-c",
                    "fixture:ling-tong-bank-d"]));
        }
    }
}
