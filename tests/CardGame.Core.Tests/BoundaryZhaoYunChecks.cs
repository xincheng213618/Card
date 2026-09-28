using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryZhaoYunChecks
{
    private const string General = "boundary:zhao-yun";
    private const string Longdan = "boundary:longdan";
    private const string Yajiao = "boundary:yajiao";
    private const string BasicMode = "identity:boundary-zhao-yun-basic-check-5";
    private const string EquipMode = "identity:boundary-zhao-yun-equip-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 4, FactionId: "shu" } general &&
                general.SkillIds.SequenceEqual([Longdan, Yajiao]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "Jie Zhao Yun must be in the current Shu roster with four HP.");

        var longdan = current.Skills[Longdan].Program!;
        Require(longdan.ViewAs.Select(item => item.Id).SequenceEqual(
                ["dodge-to-slash", "slash-to-dodge", "alcohol-to-peach", "peach-to-alcohol"]),
            "Jie Longdan must offer all four conversion directions.");
        var dodgeToSlash = longdan.ViewAs.First(item => item.Id == "dodge-to-slash");
        Require(dodgeToSlash.OutputKind == CardKind.Slash &&
                dodgeToSlash.InputKinds.SequenceEqual([CardKind.Dodge]) &&
                dodgeToSlash.ForPlay && dodgeToSlash.ForResponse,
            "Jie Longdan must convert a dodge into a slash for play and response.");
        var slashToDodge = longdan.ViewAs.First(item => item.Id == "slash-to-dodge");
        Require(slashToDodge.OutputKind == CardKind.Dodge &&
                slashToDodge.InputKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
                !slashToDodge.ForPlay && slashToDodge.ForResponse,
            "Jie Longdan must convert a slash into a dodge for responses only.");
        var alcoholToPeach = longdan.ViewAs.First(item => item.Id == "alcohol-to-peach");
        Require(alcoholToPeach.OutputKind == CardKind.Peach &&
                alcoholToPeach.InputKinds.SequenceEqual([CardKind.Alcohol]) &&
                !alcoholToPeach.ForPlay && alcoholToPeach.ForResponse,
            "Jie Longdan must convert alcohol into a peach for dying responses only.");
        var peachToAlcohol = longdan.ViewAs.First(item => item.Id == "peach-to-alcohol");
        Require(peachToAlcohol.OutputKind == CardKind.Alcohol &&
                peachToAlcohol.InputKinds.SequenceEqual([CardKind.Peach]) &&
                peachToAlcohol.ForPlay && peachToAlcohol.ForResponse,
            "Jie Longdan must convert a peach into alcohol for play and response.");

        var yajiao = current.Skills[Yajiao].Program!;
        Require(yajiao.Triggers.Select(item => item.Window).SequenceEqual(
                [SkillProgramTriggerWindow.CardUseCommitted, SkillProgramTriggerWindow.CardResponseAccepted]) &&
                yajiao.Triggers.All(item => item.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
                    item.Optional),
            "Yajiao must trigger on the owner using or playing a card.");
        var useTrigger = yajiao.Triggers[0];
        Require(useTrigger.Condition.Kind == SkillProgramTriggerConditionKind.All &&
                useTrigger.Condition.Children.Any(child =>
                    child.Kind == SkillProgramTriggerConditionKind.CardActionFromOwnerHand) &&
                useTrigger.Condition.Children.Any(child =>
                    child.Kind == SkillProgramTriggerConditionKind.Not &&
                    child.Children.Single().Kind == SkillProgramTriggerConditionKind.OwnerIsTurnPlayer),
            "Yajiao must gate its trigger on a hand card used outside the owner's turn.");
        Require(useTrigger.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.RevealTopCards,
                SkillProgramEffectOp.GiveBoundCard,
                SkillProgramEffectOp.SelectTarget,
                SkillProgramEffectOp.SelectAndMoveOwnedCard]),
            "Yajiao must reveal the deck top, then branch into the gift or the area discard.");
        var gift = useTrigger.Effects[1];
        Require(gift.TargetKind == SkillProgramTargetKind.AnyLiving &&
                gift.Condition.Kind == SkillProgramConditionKind.BoundCardCategoryMatchesCardAction,
            "The matching branch must offer the revealed card to any living player.");
        var select = useTrigger.Effects[2];
        Require(select.TargetKind == SkillProgramTargetKind.OtherLivingWhoseAttackRangeIncludesOwner &&
                select.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment]) &&
                select.SkipIfNoTarget &&
                select.Condition.Kind == SkillProgramConditionKind.Not,
            "The mismatched branch must pick a player whose attack range covers the owner.");
        var discard = useTrigger.Effects[3];
        Require(discard.CardOwnerRef?.Kind == ProgramParticipantRef.SelectedTarget &&
                discard.Destination == SkillProgramCardDestination.DiscardPile &&
                discard.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment]),
            "The mismatched branch must discard one card from the chosen player's area.");

        const string handTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:hand","revision":1,
            "minimumRulesVersion": 184,
            "triggers":[{"id":"t","window":"cardUseCommitted","ownerRelation":"actor","optional":true,
            "condition":{"kind":"cardActionFromOwnerHand"},
            "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;
        const string handPresentation = """
            {"schemaVersion":3,"skills":{"fixture:hand":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(handTemplate, handPresentation)
                .Programs["fixture:hand"].Triggers.Single().Effects.Count == 1,
            "A cardActionFromOwnerHand condition must be definable on card-action triggers.");
        Reject(handTemplate.Replace("\"window\":\"cardUseCommitted\"", "\"window\":\"afterDamageApplied\""),
            handPresentation, "cardActionFromOwnerHand outside a card-action trigger");

        const string giftTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:gift","revision":1,
            "minimumRulesVersion": 184,
            "triggers":[{"id":"t","window":"cardUseCommitted","ownerRelation":"actor","optional":true,
            "effects":[{"op":"revealTopCards","target":"owner","amount":1,"resultBind":"r","visibility":"public"},
            {"op":"giveBoundCard","target":"owner","sourceBind":"r","targetKind":"anyLiving",
            "condition":{"kind":"choiceIs","sourceBind":"r","optionId":"x"}}]}]}]}
            """;
        Reject(giftTemplate, handPresentation, "giveBoundCard with a non-branch condition");
    }

    public static void LongdanConvertsSlashToDodgeAndFiresYajiao()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, BasicMode, seed);
            if (!AwaitIncomingSlash(game)) continue;
            var prompt = game.PendingDecision!;
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            var converted = prompt.Choices.Where(choice => choice.Cards.Count == 1 &&
                hand.Single(card => card.Id == choice.Cards[0]).Kind is CardKind.Slash).ToList();
            if (converted.Count == 0) continue;
            Answer(game, converted[0]);
            DriveUntil(game, () => false, stopAtSkills: [Yajiao]);
            if (game.State.Status == EngineStatus.Completed || !IsProgramPrompt(game, Yajiao)) continue;
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Longdan-response Yajiao prompt must pause at an identical checkpoint.");
            Activate(game, Yajiao);
            Activate(replay, Yajiao);
            DriveUntil(game, () => game.ResolutionStack.Count == 0, resolveSkillIds: [Yajiao]);
            DriveUntil(replay, () => replay.ResolutionStack.Count == 0, resolveSkillIds: [Yajiao]);
            var gave = game.CardMovements.Count(item =>
                item.To.Zone == CardZoneKind.Hand &&
                item.Reason.Value.Contains("GiveBoundCard", StringComparison.Ordinal));
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Yajiao gift branch must replay identically from the paused checkpoint.");
            if (gave == 0) continue;
            Require(gave == 1, "A matching revealed card must be given to exactly one player via Yajiao.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Longdan slash-to-dodge response.");
    }

    public static void YajiaoDoesNotFireOnOwnTurnUse()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, BasicMode, seed);
            ReachPlay(game);
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash &&
                hand.Any(card => card.Id == action.CardId && card.Kind == CardKind.Slash));
            if (slash is null) continue;
            var beforeEvents = game.Events.Count;
            Play(game, slash);
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            if (game.State.Status == EngineStatus.Completed) continue;
            Require(game.Events.Skip(beforeEvents).Select(item => item.Payload)
                    .OfType<ProgramBindingStartedEvent>()
                    .All(item => item.SkillId != Yajiao),
                "Using a hand card during the owner's own turn must not bind Yajiao.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced an own-turn hand-card use.");
    }

    public static void YajiaoMismatchDiscardsFromRangedPlayerAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, EquipMode, seed);
            if (!AwaitIncomingSlash(game)) continue;
            var prompt = game.PendingDecision!;
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            var realDodge = prompt.Choices.Where(choice => choice.Cards.Count == 1 &&
                hand.Single(card => card.Id == choice.Cards[0]).Kind == CardKind.Dodge).ToList();
            if (realDodge.Count == 0) continue;
            Answer(game, realDodge[0]);
            DriveUntil(game, () => false, stopAtSkills: [Yajiao]);
            if (game.State.Status == EngineStatus.Completed || !IsProgramPrompt(game, Yajiao)) continue;
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Activate(game, Yajiao);
            Activate(replay, Yajiao);
            DriveUntil(game, () => game.ResolutionStack.Count == 0, resolveSkillIds: [Yajiao]);
            DriveUntil(replay, () => replay.ResolutionStack.Count == 0, resolveSkillIds: [Yajiao]);
            if (game.State.Status == EngineStatus.Completed) continue;
            var discardMovement = game.CardMovements.SingleOrDefault(item =>
                item.Reason.Value.Contains("SelectAndMoveOwnedCard", StringComparison.Ordinal) &&
                item.Reason.Value.Contains(Yajiao, StringComparison.Ordinal));
            if (discardMovement is null) continue;
            Require(game.CardMovements.Count(item =>
                        item.Reason.Value.Contains("GiveBoundCard", StringComparison.Ordinal)) == 0,
                "The mismatched branch must not gift the revealed card.");
            Require(discardMovement.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
                    discardMovement.To == CardLocation.DiscardPile,
                "The mismatched branch must discard a card from the chosen player's area.");
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Yajiao mismatched branch must replay identically from the paused checkpoint.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a mismatched-category Yajiao discard.");
    }

    private static bool AwaitIncomingSlash(GameEngine game)
    {
        for (var step = 0; step < 2500 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } prompt &&
                prompt.Kind == DecisionKind.RespondDodge &&
                prompt.IncomingCard == CardKind.Slash)
                return true;
            Step(game);
        }
        return false;
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
        Require(result.Accepted, result.Error?.Message ?? "Zhao Yun skill answer failed.");
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
        Require(game.Submit(new StartGameCommand()).Accepted, "Zhao Yun fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Zhao Yun selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Zhao Yun fixture did not reach Play.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Zhao Yun fixture did not advance.");
    }

    private static void Step(GameEngine game)
    {
        GameCommand command = game.PendingDecision is { PlayerSeat: 0 } prompt
            ? prompt.Kind == DecisionKind.PlayCard ? new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)
                : new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Last().Id, game.Revision)
            : new AdvanceOneStepCommand(game.Revision);
        var result = game.Submit(command);
        Require(result.Accepted, $"Zhao Yun fixture could not continue: {result.Error?.Message}");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Zhao Yun card action failed.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Zhao Yun command failed.");
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
            $"Expected invalid Zhao Yun composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("boundary-zhao-yun-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 154, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jzy-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jzy-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jzy-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jzy-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            var basicCards = Enumerable.Range(0, 200).Select(index => (index % 3) switch
            {
                1 => "standard:dodge",
                _ => "standard:slash"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:jzy-basic-deck", "界赵云基础牌堆", 5, 2, [])
            { PhysicalCards = basicCards });
            var equipCards = Enumerable.Range(0, 200).Select(index => (index % 6) switch
            {
                1 => "standard:dodge",
                3 => "standard:crossbow",
                _ => "standard:slash"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:jzy-equip-deck", "界赵云装备牌堆", 5, 2, [])
            { PhysicalCards = equipCards });
            var pool = new[]
            {
                General, "fixture:jzy-bank-a", "fixture:jzy-bank-b", "fixture:jzy-bank-c", "fixture:jzy-bank-d"
            };
            builder.AddMode(new ContentModeDefinition(BasicMode, "界赵云基础测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:jzy-basic-deck", GeneralCandidateCount: 5, GeneralPoolIds: pool));
            builder.AddMode(new ContentModeDefinition(EquipMode, "界赵云装备测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:jzy-equip-deck", GeneralCandidateCount: 5, GeneralPoolIds: pool));
        }
    }
}
