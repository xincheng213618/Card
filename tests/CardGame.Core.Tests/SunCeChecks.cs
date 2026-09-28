using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SunCeChecks
{
    private const string General = "classic:sun-ce";
    private const string Jiang = "classic:jiang";
    private const string Hunzi = "classic:hunzi";
    private const string Yingzi = "classic:yingzi";
    private const string Yinghun = "classic:yinghun";
    private const string Mode = "identity:classic-sun-ce-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 4, FactionId: "wu" } general &&
                general.SkillIds.SequenceEqual([Jiang, Hunzi]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Sun Ce must be in the current Wu roster with four HP.");

        var jiang = current.Skills[Jiang].Program!;
        var useDuel = jiang.Triggers.Single(item => item.Id == "draw-on-using-duel");
        Require(useDuel.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects &&
                useDuel.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
                useDuel.Optional &&
                useDuel.CardKinds.SequenceEqual([CardKind.Duel]) &&
                useDuel.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
                useDuel.Effects.Select(item => item.Op).SequenceEqual([SkillProgramEffectOp.Draw]),
            "Jiang must offer an optional draw when Sun Ce uses a duel.");
        var useRedSlash = jiang.Triggers.Single(item => item.Id == "draw-on-using-red-slash");
        Require(useRedSlash.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects &&
                useRedSlash.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
                useRedSlash.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash]) &&
                useRedSlash.Condition.Kind == SkillProgramTriggerConditionKind.CardActionCardIsRed,
            "Jiang must gate the used-slash draw on the played card being red.");
        var targetedByDuel = jiang.Triggers.Single(item => item.Id == "draw-on-targeted-by-duel");
        Require(targetedByDuel.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects &&
                targetedByDuel.OwnerRelation == SkillProgramCardActionOwnerRelation.Target &&
                targetedByDuel.CardKinds.SequenceEqual([CardKind.Duel]),
            "Jiang must also fire when Sun Ce becomes a duel's target.");
        var targetedByRedSlash = jiang.Triggers.Single(item => item.Id == "draw-on-targeted-by-red-slash");
        Require(targetedByRedSlash.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects &&
                targetedByRedSlash.OwnerRelation == SkillProgramCardActionOwnerRelation.Target &&
                targetedByRedSlash.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash]) &&
                targetedByRedSlash.Condition.Kind == SkillProgramTriggerConditionKind.CardActionCardIsRed,
            "Jiang must gate the targeted-slash draw on the played card being red.");
        Require(jiang.Triggers.All(item =>
                item.Effects.Single().Target == SkillProgramEffectTarget.Owner &&
                item.Effects.Single().Amount == 1),
            "Every Jiang draw must move exactly one card to the owner.");

        var hunzi = current.Skills[Hunzi].Program!;
        var awakening = hunzi.Triggers.Single();
        Require(awakening.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
                !awakening.Optional &&
                awakening.UsageScope == SkillUsageScope.Game &&
                awakening.UsageLimit == 1 &&
                awakening.Condition is
                {
                    Kind: SkillProgramTriggerConditionKind.Compare,
                    Comparison: SkillProgramComparisonOperator.Equal,
                    Left.Kind: SkillProgramTriggerValueKind.CurrentHp,
                    Right.Kind: SkillProgramTriggerValueKind.IntegerConstant,
                    Right.Value: 1
                },
            "Hunzi must awaken once per game at the owner's turn start with exactly one HP.");
        Require(awakening.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.ChangeMaximumHp,
            SkillProgramEffectOp.GrantSkills]) &&
                awakening.Effects[0].Amount == -1 &&
                awakening.Effects[1].SkillIds.SequenceEqual([Yingzi, Yinghun]),
            "Hunzi must lose one maximum HP and grant Yingzi and Yinghun.");

        const string redTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:red","revision":1,
            "minimumRulesVersion": 181,
            "triggers":[{"id":"t","window":"cardUseTargetsFinalized","ownerRelation":"actor","optional":true,
            "cardKinds":["slash"],"condition":{"kind":"cardActionCardIsRed"},
            "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;
        const string redPresentation = """
            {"schemaVersion":3,"skills":{"fixture:red":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(redTemplate, redPresentation)
                .Programs["fixture:red"].Triggers.Single().Effects.Count == 1,
            "A cardActionCardIsRed condition must be definable on card-use triggers.");
        Reject(redTemplate.Replace("\"window\":\"cardUseTargetsFinalized\"", "\"window\":\"afterDamageApplied\""),
            redPresentation, "cardActionCardIsRed outside a card-action trigger");
    }

    public static void JiangDrawsWhenUsingDuelAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            var duel = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.Duel);
            if (duel is null) continue;
            Play(game, duel);
            DriveUntil(game, () => false, stopAtSkills: [Jiang]);
            if (game.State.Status == EngineStatus.Completed ||
                !IsProgramPrompt(game, Jiang))
                continue;
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The duel-use Jiang prompt must pause at an identical checkpoint.");
            Activate(game, Jiang);
            Activate(replay, Jiang);
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            DriveUntil(replay, () => replay.ResolutionStack.Count == 0);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Jiang duel draw must replay identically from the paused checkpoint.");
            Require(game.CardMovements.Count(item => item.To == CardLocation.Hand(0) &&
                        item.Reason.Value.Contains(Jiang, StringComparison.Ordinal)) == 1,
                "Using a duel must draw exactly one card via Jiang.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Jiang duel-use draw.");
    }

    public static void JiangDrawsWhenTargetedButNotOnBlackSlash()
    {
        TargetedDraw();
        BlackSlashUseDoesNotDraw();
    }

    private static void TargetedDraw()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            EndPlay(game);
            DriveUntil(game, () => false, stopAtSkills: [Jiang]);
            if (game.State.Status == EngineStatus.Completed ||
                !IsProgramPrompt(game, Jiang))
                continue;
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Activate(game, Jiang);
            Activate(replay, Jiang);
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            DriveUntil(replay, () => replay.ResolutionStack.Count == 0);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The targeted Jiang draw must replay identically from the paused checkpoint.");
            Require(game.CardMovements.Count(item => item.To == CardLocation.Hand(0) &&
                        item.Reason.Value.Contains(Jiang, StringComparison.Ordinal)) == 1,
                "Being targeted by a duel or red slash must draw exactly one card via Jiang.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a targeted Jiang draw.");
    }

    private static void BlackSlashUseDoesNotDraw()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            var blackSlash = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash &&
                hand.Any(card => card.Id == action.CardId &&
                    card.Suit is Suit.Spade or Suit.Club));
            if (blackSlash is null) continue;
            var beforeEvents = game.Events.Count;
            Play(game, blackSlash);
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            if (game.State.Status == EngineStatus.Completed) continue;
            Require(game.Events.Skip(beforeEvents).Select(item => item.Payload)
                    .OfType<ProgramBindingStartedEvent>()
                    .All(item => item.SkillId != Jiang),
                "Using a black slash must not bind Jiang.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a black-slash use.");
    }

    public static void HunziAwakensGrantsSkillsAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            EndPlay(game);
            DriveUntil(game, () => IsAwakened(game));
            if (!IsAwakened(game)) continue;
            var awakened = game.Events.Select(item => item.Payload)
                .OfType<SkillAwakenedEvent>()
                .Single(item => item.SkillId == Hunzi);
            Require(awakened.PlayerSeat == 0 && awakened.MaximumHp == 4 &&
                    awakened.AcquiredSkillIds.SequenceEqual([Yingzi, Yinghun]),
                "Hunzi must reduce the lord's five maximum HP to four and grant Yingzi and Yinghun.");
            var skills = game.CreateSnapshot(0, true).Players[0].Skills!
                .Select(item => item.ContentId).ToArray();
            Require(skills.Contains(Yingzi) && skills.Contains(Yinghun),
                "The awakened Sun Ce must own the granted Yingzi and Yinghun skills.");
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Hunzi awakening must replay identically from the paused checkpoint.");
            // The granted Yinghun must prompt at the wounded owner's next turn start.
            DriveUntil(game, () => false, stopAtSkills: [Yinghun]);
            if (game.State.Status == EngineStatus.Completed ||
                !IsProgramPrompt(game, Yinghun))
                continue;
            // The granted Yingzi must offer its extra draw in the following draw phase.
            DriveUntil(game, () => false, stopAtSkills: [Yingzi]);
            if (game.State.Status == EngineStatus.Completed ||
                !IsProgramPrompt(game, Yingzi))
                continue;
            Activate(game, Yingzi);
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            Require(game.CardMovements.Count(item => item.To == CardLocation.Hand(0) &&
                        item.Reason.Value.Contains(Yingzi, StringComparison.Ordinal)) == 1,
                "The granted Yingzi must draw exactly one extra card in the draw phase.");
            completed++;
        }
        Require(completed == 1, "No seeded setup awakened Hunzi with the granted skills live.");
    }

    private static bool IsAwakened(GameEngine game) =>
        game.CreateSnapshot(0, true).Players[0].MaxHp == 4;

    private static bool IsProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0
        } prompt && prompt.SkillPrompt?.SkillId == skillId;

    private static void DriveUntil(
        GameEngine game,
        Func<bool> done,
        string[]? stopAtSkills = null,
        int budget = 500)
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
        Require(result.Accepted, result.Error?.Message ?? "Sun Ce skill answer failed.");
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
        Require(game.Submit(new StartGameCommand()).Accepted, "Sun Ce fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Sun Ce selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Sun Ce fixture did not reach Play.");
    }

    private static void EndPlay(GameEngine game)
    {
        var prompt = game.PendingDecision;
        Require(prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "Sun Ce fixture lost the play decision before ending the phase.");
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt!.PromptId)));
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Sun Ce fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Sun Ce card action failed.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Sun Ce command failed.");
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
            $"Expected invalid Sun Ce composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("sun-ce-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 151, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1 and 4 are high-HP duel banks,
            // seats 2 and 3 are additional opponents so AI aggression and duels
            // can drop Sun Ce to one HP for the Hunzi awakening.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:sun-ce-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:sun-ce-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:sun-ce-opponent-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:sun-ce-opponent-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 180).Select(index => (index % 4) switch
            {
                1 => "standard:duel",
                2 => "standard:dodge",
                _ => "standard:slash"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:sun-ce-deck", "孙策测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "孙策测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:sun-ce-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:sun-ce-bank-a",
                    "fixture:sun-ce-bank-b",
                    "fixture:sun-ce-opponent-c",
                    "fixture:sun-ce-opponent-d"]));
        }
    }
}
