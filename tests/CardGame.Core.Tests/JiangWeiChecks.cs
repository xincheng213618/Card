using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class JiangWeiChecks
{
    private const string General = "classic:jiang-wei";
    private const string Tiaoxin = "classic:tiaoxin";
    private const string Zhiji = "classic:zhiji";
    private const string Guanxing = "classic:guanxing";
    private const string TiaoxinMode = "identity:classic-jiang-wei-check-5";
    private const string ZhijiMode = "identity:classic-jiang-wei-zhiji-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 4, FactionId: "shu" } general &&
                general.SkillIds.SequenceEqual([Tiaoxin, Zhiji]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Jiang Wei must be in the current Shu roster with four HP.");

        var tiaoxin = current.Skills[Tiaoxin].Program!;
        var activation = tiaoxin.Activations.Single();
        Require(activation.UsesPerTurn == 1 && activation.MinCards == 0 && activation.MaxCards == 0 &&
                activation.TargetKind == SkillProgramTargetKind.OtherLivingWhoseAttackRangeIncludesOwner &&
                activation.MinTargets == 1 && activation.MaxTargets == 1 &&
                activation.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.RequestSlashByTarget,
                    SkillProgramEffectOp.SelectAndMoveOwnedCard]),
            "Tiaoxin must be a once-per-turn active skill choosing a player whose range covers the owner.");
        var request = activation.Effects[0];
        Require(request.Target == SkillProgramEffectTarget.SelectedTarget &&
                request.ResultBind == "tiaoxin-answer" &&
                request.Condition.Kind == SkillProgramConditionKind.Always,
            "Tiaoxin must first demand a Slash from the selected target.");
        var discard = activation.Effects[1];
        Require(discard.CardOwnerRef!.Kind == ProgramParticipantRef.SelectedTarget &&
                discard.ChooserRef!.Kind == ProgramParticipantRef.Owner &&
                discard.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
                discard.Destination == SkillProgramCardDestination.DiscardPile &&
                discard.SkipIfNoCards &&
                discard.Condition is
                {
                    Kind: SkillProgramConditionKind.ChoiceIs,
                    SourceBind: "tiaoxin-answer",
                    OptionId: "declined"
                },
            "Tiaoxin must discard one selected-target card only on the declined branch.");

        var zhiji = current.Skills[Zhiji].Program!;
        var awakening = zhiji.Triggers.Single();
        Require(awakening.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
                awakening.Subject == SkillProgramTriggerSubject.Owner &&
                !awakening.Optional &&
                awakening.UsageScope == SkillUsageScope.Game &&
                awakening.UsageLimit == 1 &&
                awakening.Condition is
                {
                    Kind: SkillProgramTriggerConditionKind.Compare,
                    Comparison: SkillProgramComparisonOperator.Equal,
                    Left.Kind: SkillProgramTriggerValueKind.CurrentHandCount,
                    Right.Kind: SkillProgramTriggerValueKind.IntegerConstant,
                    Right.Value: 0
                },
            "Zhiji must awaken once per game at the owner's turn start with no hand cards.");
        Require(awakening.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.ChooseOption,
            SkillProgramEffectOp.Recover,
            SkillProgramEffectOp.Draw,
            SkillProgramEffectOp.ChangeMaximumHp,
            SkillProgramEffectOp.GrantSkills]),
            "Zhiji must offer one of two options, then lose one maximum HP and grant Guanxing.");
        var choice = awakening.Effects[0];
        Require(choice.Target == SkillProgramEffectTarget.Owner &&
                choice.ResultBind == "zhiji-choice" &&
                choice.Options.Select(item => item.Id).SequenceEqual(["recover", "draw-two"]),
            "Zhiji must let the owner choose between recovery and drawing two cards.");
        Require(awakening.Effects[1] is { Amount: 1 } recover &&
                recover.Condition is { Kind: SkillProgramConditionKind.ChoiceIs, OptionId: "recover" } &&
                awakening.Effects[2] is { Amount: 2 } draw &&
                draw.Condition is { Kind: SkillProgramConditionKind.ChoiceIs, OptionId: "draw-two" },
            "Each Zhiji option must gate exactly its own branch.");
        Require(awakening.Effects[3].Amount == -1 &&
                awakening.Effects[4].SkillIds.SequenceEqual([Guanxing]),
            "Zhiji must reduce one maximum HP and grant the existing classic:guanxing.");

        const string requestTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:taunt","revision":1,
            "minimumRulesVersion": 185,
            "activations":[{"id":"taunt","minCards":0,"maxCards":0,"sourceZones":["hand"],
            "minTargets":1,"maxTargets":1,"targetKind":"otherLivingWhoseAttackRangeIncludesOwner",
            "usesPerTurn":1,"condition":{"kind":"always"},
            "effects":[
            {"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"answer"},
            {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},
            "cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand","equipment"],"count":1,
            "destination":"discardPile","skipIfNoCards":true,
            "condition":{"kind":"choiceIs","sourceBind":"answer","optionId":"declined"}}]}]}]}
            """;
        const string requestPresentation = """
            {"schemaVersion":3,"skills":{"fixture:taunt":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(requestTemplate, requestPresentation)
                .Programs["fixture:taunt"].Activations.Single().Effects.Count == 2,
            "The Tiaoxin chain must load with a slash request and a conditioned discard.");
        Reject(requestTemplate.Replace("\"target\":\"selectedTarget\",\"resultBind\":\"answer\"",
                "\"target\":\"owner\",\"resultBind\":\"answer\""),
            requestPresentation, "the slash request requires a selected target");
        Reject(requestTemplate.Replace(",\"resultBind\":\"answer\"", ""),
            requestPresentation, "the slash request requires a result binding");
        Reject(requestTemplate.Replace("\"optionId\":\"declined\"", "\"optionId\":\"declined-x\""),
            requestPresentation, "the discard branch must reference a declared option");
        Reject(requestTemplate.Replace(
                "\"destination\":\"discardPile\",\"skipIfNoCards\":true",
                "\"destination\":\"discardPile\",\"skipIfNoCards\":true,\"resultBind\":\"kept\""),
            requestPresentation, "a conditional card movement cannot produce a result binding");
        Reject(requestTemplate.Replace(
                "\"op\":\"requestSlashByTarget\",\"target\":\"selectedTarget\",\"resultBind\":\"answer\"",
                "{\"op\":\"requestSlashByTarget\",\"target\":\"selectedTarget\",\"resultBind\":\"answer\",\"condition\":{\"kind\":\"always\"}}"),
            requestPresentation, "the slash request itself must stay unconditional");
    }

    public static void TiaoxinForcesSlashAgainstOwnerAndReplays()
    {
        var registry = Registry(TiaoxinMode, Deck(A: "standard:slash", B: "standard:dodge", C: "standard:peach"));
        var completed = 0;
        for (var seed = 1; seed <= 80 && completed < 1; seed++)
        {
            var game = Start(registry, seed, TiaoxinMode);
            if (!DriveToFirstPlay(game)) continue;
            var neighbor = FirstNeighborWithSlash(game, requireSlash: true);
            if (neighbor is null) continue;
            var slash = game.CreateSnapshot(0, true).Players[neighbor.Value]
                .Hand.First(card => IsSlash(card.Kind));

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            if (!ActivateTiaoxin(game, neighbor.Value) || !ActivateTiaoxin(replay, neighbor.Value)) continue;
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The forced Tiaoxin Slash must replay identically from the paused checkpoint.");
            var used = game.Events.Select(item => item.Payload).OfType<CardUsedEvent>()
                .SingleOrDefault(item => item.SourceSeat == neighbor.Value && item.TargetSeat == 0 &&
                    IsSlash(item.CardKind));
            Require(used is not null && used.CardId == slash.Id,
                "The targeted player must use the exact hand Slash against Jiang Wei.");
            Require(game.CardMovements.Any(movement =>
                    movement.CardId == slash.Id &&
                    movement.From == CardLocation.Hand(neighbor.Value) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Use),
                "The forced Slash must be paid as a real card use.");
            Require(!game.CardMovements.Any(movement =>
                    movement.From.OwnerSeat == neighbor.Value &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason.Value.Contains(Tiaoxin, StringComparison.Ordinal)),
                "Using the Slash must spare the target from the Tiaoxin discard.");
            Require(game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>()
                    .Any(item => item.SkillId == Tiaoxin),
                "Tiaoxin must resolve as a program skill.");
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved the forced Tiaoxin Slash branch.");
    }

    private static bool DriveToFirstPlay(GameEngine game)
    {
        DriveUntil(game, () => false, stopAtDecisions: [DecisionKind.PlayCard]);
        return game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    }

    private static int? FirstNeighborWithSlash(GameEngine game, bool requireSlash)
    {
        var players = game.CreateSnapshot(0, true).Players;
        foreach (var seat in new[] { 1, 4 })
        {
            var hasSlash = players[seat].Hand.Any(card => IsSlash(card.Kind));
            if (hasSlash == requireSlash) return seat;
        }
        return null;
    }

    private static bool ActivateTiaoxin(GameEngine game, int targetSeat)
    {
        var prompt = game.PendingDecision;
        if (prompt is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return false;
        var result = game.Submit(new UseProgramSkillCommand(0, Tiaoxin, "taunt",
            [], [targetSeat], game.Revision, prompt.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Tiaoxin activation failed.");
        return true;
    }

    private static bool IsSlash(CardKind kind) =>
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;

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

    private static void DriveUntilSettled(GameEngine game) =>
        DriveUntil(game, () => game.ResolutionStack.Count == 0);

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Jiang Wei answer failed.");
    }

    private static ContentRegistry Registry(string mode, ContentDeckRecipe deck) => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new JiangWeiScenario(mode, deck));

    private static ContentDeckRecipe Deck(string A, string B, string C) =>
        DeckCore(index => (index % 4) switch
        {
            1 => B,
            2 => C,
            _ => A
        });

    private static ContentDeckRecipe DeckCore(Func<int, string> kindFor) =>
        new("fixture:jiang-wei-deck", "姜维测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard(kindFor(index), (Suit)(index % 4), index % 13 + 1)).ToArray()
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
        Require(game.Submit(new StartGameCommand()).Accepted, "Jiang Wei fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Jiang Wei selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Jiang Wei fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Jiang Wei command failed.");
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
            $"Expected invalid Jiang Wei composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class JiangWeiScenario(string modeId, ContentDeckRecipe deck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("jiang-wei-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 155, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1-4 are skill-less AI banks so the
            // only live skills are Tiaoxin and Zhiji on the human seat.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jiang-wei-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jiang-wei-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jiang-wei-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jiang-wei-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "姜维测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:jiang-wei-bank-a",
                    "fixture:jiang-wei-bank-b",
                    "fixture:jiang-wei-bank-c",
                    "fixture:jiang-wei-bank-d"]));
        }
    }

    private enum ZhijiChoice { Recover, DrawTwo }
}
