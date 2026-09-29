using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenZhaoYunChecks
{
    private const string General = "classic:shen-zhao-yun";
    private const string Juejing = "classic:juejing";
    private const string Longhun = "classic:longhun";
    private const string Zhanjiang = "classic:zhanjiang";
    private const string BasicMode = "identity:shen-zhao-yun-basic-check-5";
    private const string EquipMode = "identity:shen-zhao-yun-equip-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 2, FactionId: "god" } general &&
                general.SkillIds.SequenceEqual([Juejing, Longhun, Zhanjiang]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "Shen Zhao Yun must be a two-HP god general in the current roster.");

        var juejing = current.Skills[Juejing].Program!;
        var modifier = juejing.Modifiers.Single();
        Require(modifier.Query == SkillRuleQuery.HandLimit &&
                modifier.Operation == SkillRuleOperation.Set &&
                modifier.Value == 4,
            "Juejing must pin the hand limit to four.");
        Require(juejing.Triggers.Select(item => item.Id).SequenceEqual(
                ["no-normal-draw", "refill-to-fixed-hand-count"]),
            "Juejing must skip the normal draw and refill the hand.");
        var noDraw = juejing.Triggers[0];
        Require(noDraw.Window == SkillProgramTriggerWindow.DrawPhaseStarting &&
                !noDraw.Optional &&
                noDraw.DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement &&
                noDraw.Effects.Single().Op == SkillProgramEffectOp.Draw &&
                noDraw.Effects.Single().Amount == 0,
            "Juejing must replace the normal draw with a zero-card draw.");
        var refill = juejing.Triggers[1];
        Require(refill.Window == SkillProgramTriggerWindow.CardsMoved &&
                refill.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                refill.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch &&
                !refill.Optional &&
                refill.Effects.Single().NumberExpression == SkillProgramNumberExpression.HandLimitMinusHandCount,
            "Juejing must refill the hand to four whenever hand cards leave.");

        var longhun = current.Skills[Longhun].Program!;
        Require(longhun.ViewAs.Select(item => item.Id).SequenceEqual(
                ["heart-to-peach", "diamond-to-fire-slash", "club-to-dodge", "spade-to-nullification"]),
            "Longhun must offer the four suit conversions.");
        var heartToPeach = longhun.ViewAs[0];
        Require(heartToPeach.InputSuits.SequenceEqual([Suit.Heart]) &&
                heartToPeach.OutputKind == CardKind.Peach &&
                !heartToPeach.ForPlay && heartToPeach.ForResponse &&
                heartToPeach.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]),
            "Longhun must turn heart cards into peaches for responses from hand or equipment.");
        var diamondToFireSlash = longhun.ViewAs[1];
        Require(diamondToFireSlash.InputSuits.SequenceEqual([Suit.Diamond]) &&
                diamondToFireSlash.OutputKind == CardKind.FireSlash &&
                diamondToFireSlash.ForPlay && !diamondToFireSlash.ForResponse,
            "Longhun must turn diamond cards into fire slashes for play only.");
        var clubToDodge = longhun.ViewAs[2];
        Require(clubToDodge.InputSuits.SequenceEqual([Suit.Club]) &&
                clubToDodge.OutputKind == CardKind.Dodge &&
                !clubToDodge.ForPlay && clubToDodge.ForResponse,
            "Longhun must turn club cards into dodges for responses.");
        var spadeToNullification = longhun.ViewAs[3];
        Require(spadeToNullification.InputSuits.SequenceEqual([Suit.Spade]) &&
                spadeToNullification.OutputKind == CardKind.Nullification &&
                !spadeToNullification.ForPlay && spadeToNullification.ForResponse,
            "Longhun must turn spade cards into nullifications for responses.");

        var zhanjiang = current.Skills[Zhanjiang].Program!;
        var takeSword = zhanjiang.Triggers.Single();
        Require(takeSword.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
                takeSword.Optional &&
                takeSword.Effects[0].Op == SkillProgramEffectOp.SelectTarget &&
                takeSword.Effects[0].TargetKind == SkillProgramTargetKind.OtherLivingWithQinggangSword &&
                takeSword.Effects[0].SkipIfNoTarget &&
                takeSword.Effects[1].Op == SkillProgramEffectOp.SelectAndMoveOwnedCard &&
                takeSword.Effects[1].CardOwnerRef?.Kind == ProgramParticipantRef.SelectedTarget &&
                takeSword.Effects[1].Destination == SkillProgramCardDestination.OwnerHand &&
                takeSword.Effects[1].CardKinds.SequenceEqual([CardKind.QinggangSword]),
            "Zhanjiang must offer to take a Qinggang Sword from another player's equipment.");

        const string handPresentation = """
            {"schemaVersion":3,"skills":{"fixture:x":{"name":"测试","description":"测试"}}}
            """;
        const string responseFireSlash = """
            {"schemaVersion":62,"skills":[{"id":"fixture:x","revision":1,
            "minimumRulesVersion": 184,
            "viewAs":[{"id":"v","inputKinds":[],"inputSuits":["diamond"],"outputKind":"fireSlash",
            "forPlay":false,"forResponse":true}]}]}
            """;
        Reject(responseFireSlash, handPresentation, "fireSlash conversions stay play-only");
        const string twoSuitFireSlash = """
            {"schemaVersion":62,"skills":[{"id":"fixture:x","revision":1,
            "minimumRulesVersion": 184,
            "viewAs":[{"id":"v","inputKinds":[],"inputSuits":["diamond","heart"],"outputKind":"fireSlash",
            "forPlay":true,"forResponse":false}]}]}
            """;
        Reject(twoSuitFireSlash, handPresentation, "fireSlash conversions accept exactly one suit");
        const string zeroDraw = """
            {"schemaVersion":62,"skills":[{"id":"fixture:x","revision":1,
            "minimumRulesVersion": 184,
            "triggers":[{"id":"t","window":"drawPhaseStarting","subject":"owner","optional":false,
            "drawPhaseMode":"replacement",
            "effects":[{"op":"draw","target":"owner","amount":0}]}]}]}
            """;
        Require(SkillProgramCatalog.Load(zeroDraw, handPresentation)
                .Programs["fixture:x"].Triggers.Single().Effects.Single().Amount == 0,
            "A zero-card replacement draw must be definable to skip the normal draw.");
    }

    public static void JuejingSkipsDrawRefillsAndCapsAtFour()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, BasicMode, seed);
            ReachPlay(game);
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            if (hand.Count != 5)
                throw new InvalidOperationException(
                    "Juejing must skip the normal draw so the opening play-phase hand stays at the dealt five.");
            var drawTwo = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.DrawTwo &&
                hand.Any(card => card.Id == action.CardId && card.Kind == CardKind.DrawTwo));
            if (drawTwo is null) continue;
            Play(game, drawTwo);
            DriveToOwnTurnDecision(game);
            hand = game.CreateSnapshot(0, true).Players[0].Hand;
            if (hand.Count != 6) continue;
            Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
            DriveToOwnTurnDecision(game);
            if (game.State.Status == EngineStatus.Completed) continue;
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) continue;
            Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == 4,
                "The automatic hand-limit discard must settle at Juejing's fixed four instead of the current HP.");
            completed++;
        }
        Require(completed == 1, "No seeded setup exercised Juejing's draw skip, refill and cap.");
    }

    private static void DriveToOwnTurnDecision(GameEngine game)
    {
        for (var step = 0; step < 600 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.PlayerSeat == 0 &&
                prompt.Kind is DecisionKind.PlayCard or DecisionKind.DiscardCards)
                return;
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.RespondDodge)
            {
                var handNow = game.CreateSnapshot(0, true).Players[0].Hand;
                var dodge = prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 1 &&
                    handNow.Any(card => card.Id == choice.Cards[0] && card.Kind == CardKind.Dodge));
                Answer(game, dodge ?? prompt.Choices.Last());
                continue;
            }
            Answer(game, prompt.Choices.Last());
        }
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
        string[]? resolveSkillIds = null,
        bool stopAtPlayPrompt = false,
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
                    if (stopAtPlayPrompt) return;
                    Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                    continue;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    continue;
                default:
                    var choices = prompt.Choices;
                    if (resolveSkillIds is not null &&
                        prompt.Kind == DecisionKind.ProgramTrigger &&
                        prompt.SkillPrompt?.SkillId is { } resolving &&
                        resolveSkillIds.Contains(resolving))
                    {
                        choices = prompt.Choices.Where(choice =>
                            choice.Parameters.GetValueOrDefault("program-action") != "skip").ToArray();
                    }
                    var choice = choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        choices.First();
                    Answer(game, choice);
                    continue;
            }
        }
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario());

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Shen Zhao Yun skill answer failed.");
    }

    private static GameEngine Start(ContentRegistry registry, string modeId, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = modeId,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Shen Zhao Yun fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Shen Zhao Yun selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 120 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.SelectFaction, PlayerSeat: 0 } faction)
            {
                var wei = faction.Choices.Single(item =>
                    item.Parameters.GetValueOrDefault("faction-id") == "wei");
                Answer(game, wei);
                continue;
            }
            Advance(game);
        }
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Shen Zhao Yun fixture did not reach Play.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Shen Zhao Yun fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId)
        {
            ConversionSource = action.ConversionSource,
            AdditionalConversionSources = action.AdditionalConversionSources
        });
        Require(result.Accepted, result.Error?.Message ?? "Shen Zhao Yun card action failed.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Shen Zhao Yun command failed.");
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
            $"Expected invalid Shen Zhao Yun composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shen-zhao-yun-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 156, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition("fixture:szy-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:szy-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:szy-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:szy-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            var basicCards = Enumerable.Range(0, 240).Select(index => (index % 7) switch
            {
                0 => "standard:draw_two",
                1 => "standard:dodge",
                _ => "standard:slash"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:szy-basic-deck", "神赵云基础牌堆", 5, 2, [])
            { PhysicalCards = basicCards });
            var equipCards = Enumerable.Range(0, 240).Select(index => (index % 5) switch
            {
                3 => "standard:qinggang_sword",
                1 => "standard:dodge",
                _ => "standard:slash"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:szy-equip-deck", "神赵云装备牌堆", 5, 2, [])
            { PhysicalCards = equipCards });
            var pool = new[]
            {
                General, "fixture:szy-bank-a", "fixture:szy-bank-b", "fixture:szy-bank-c", "fixture:szy-bank-d"
            };
            builder.AddMode(new ContentModeDefinition(BasicMode, "神赵云基础测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:szy-basic-deck", GeneralCandidateCount: 5, GeneralPoolIds: pool));
            builder.AddMode(new ContentModeDefinition(EquipMode, "神赵云装备测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:szy-equip-deck", GeneralCandidateCount: 5, GeneralPoolIds: pool));
        }
    }
}
