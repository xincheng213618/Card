using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaoAngChecks
{
    private const string General = "classic:cao-ang";
    private const string Kangkai = "classic:kangkai";
    private const string Mode = "identity:classic-cao-ang-check-5";

    public static void DefinitionAndObserverDistanceSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 4, FactionId: "wei" } general &&
                general.SkillIds.SequenceEqual([Kangkai]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2017 Cao Ang must be in the current Wei roster with four HP.");

        var program = current.Skills[Kangkai].Program!;
        var self = program.Triggers.Single(item => item.Id == "self-targeted-by-slash");
        var observer = program.Triggers.Single(item => item.Id == "nearby-slash-target");
        Require(self.OwnerRelation == SkillProgramCardActionOwnerRelation.Target &&
                self.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
                self.Effects.Single().Op == SkillProgramEffectOp.Draw,
            "The self-targeted branch must draw one card on Slash kinds only.");
        Require(observer.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer &&
                observer.Optional &&
                observer.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.Draw,
                    SkillProgramEffectOp.SelectAndMoveOwnedCard, SkillProgramEffectOp.ChooseOption,
                    SkillProgramEffectOp.UseBoundCardByTarget]),
            "The observer branch must chain draw, public gift, choice and bound-card use.");
        var gift = observer.Effects.Single(item => item.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard);
        Require(gift.Destination == SkillProgramCardDestination.SelectedTargetHand &&
                gift.RevealBeforeMove && gift.AwaitMovementTriggers &&
                gift.ResultBind == "gift",
            "The gift must move publicly into the selected target's hand with a movement continuation.");

        const string generic = """
            {"schemaVersion":62,"skills":[{"id":"fixture:generic","revision":1,
            "minimumRulesVersion": 176,
            "triggers":[{"id":"watch","window":"cardUseTargetsFinalized","ownerRelation":"observer",
            "cardKinds":["slash"],"optional":true,
            "condition":{"kind":"compare","left":{"kind":"ownerEventTargetDistance"},
            "operator":"lessThanOrEqual","right":{"kind":"integerConstant","value":1}},
            "effects":[{"op":"selectTarget","target":"owner","targetKind":"eventTarget"},
            {"op":"draw","target":"owner","amount":1},
            {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},
            "cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,
            "destination":"selectedTargetHand","targetRef":{"kind":"selectedTarget"},
            "resultBind":"gift","revealBeforeMove":true,"awaitMovementTriggers":true},
            {"op":"chooseOption","target":"selectedTarget","resultBind":"use-gift",
            "options":[{"id":"use-equipment","condition":{"kind":"always"}},
            {"id":"keep","condition":{"kind":"always"}}]},
            {"op":"useBoundCardByTarget","target":"selectedTarget","sourceBind":"gift",
            "condition":{"kind":"choiceIs","sourceBind":"use-gift","optionId":"use-equipment"}}]}]}]}
            """;
        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:generic":{"name":"通用","description":"测试",
            "optionLabels":{"use-equipment":"使用","keep":"保留"}}}}
            """;
        Require(SkillProgramCatalog.Load(generic, presentation).Programs["fixture:generic"]
                .Triggers.Single().Effects.Count == 5,
            "A distance-gated observer gift chain must be independently definable.");
        Reject(generic.Replace("\"window\":\"cardUseTargetsFinalized\"", "\"window\":\"drawPhaseStarting\""),
            presentation, "ownerEventTargetDistance outside card use");
        Reject(generic.Replace("\"target\":\"selectedTarget\",\"sourceBind\":\"gift\"",
                "\"target\":\"owner\",\"sourceBind\":\"gift\""),
            presentation, "useBoundCardByTarget requires selectedTarget");
        Reject(generic.Replace("\"sourceBind\":\"gift\",", string.Empty, StringComparison.Ordinal),
            presentation);
    }

    public static void NearbyTargetGiftRevealsAndRecipientMayEquip()
    {
        var registry = Registry();
        var baguaId = 0;
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            var snapshot = game.CreateSnapshot(0, true);
            var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.TargetSeat == 1 &&
                snapshot.Players[0].Hand.Any(card => card.Id == action.CardId));
            var bagua = snapshot.Players[0].Hand.FirstOrDefault(card => card.Kind == CardKind.BaguaFormation);
            if (slash is null || bagua is null) continue;
            baguaId = bagua.Id;
            Play(game, slash);
            var checkpoint = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(checkpoint, registry);
            Activate(game, Kangkai);
            Activate(replay, Kangkai);
            GiveCard(game, baguaId);
            GiveCard(replay, baguaId);
            for (var step = 0; step < 12 && !game.Events.Select(item => item.Payload)
                     .OfType<ProgramOptionChosenEvent>().Any(item => item.SkillId == Kangkai); step++)
            {
                Advance(game);
                Advance(replay);
            }
            Require(game.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
                    .Any(item => item.SkillId == Kangkai && item.OptionId == "use-equipment"),
                "The recipient AI must choose the free equipment use.");
            Finish(game);
            Finish(replay);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The gift and equipment use must replay identically from the checkpoint.");
            var movements = game.CardMovements.Where(item => item.CardId == baguaId).ToArray();
            Require(movements.Any(item => item.To == CardLocation.Hand(1)) &&
                    movements.Any(item => item.To == CardLocation.Equipment(1)),
                "The gifted armor must move to the recipient's hand and then into their equipment.");
            Require(game.Events.Select(item => item.Payload).Any(item => item is EquipmentChangedEvent
                    { PlayerSeat: 1 } equipment && equipment.CardId == baguaId),
                "The recipient must actually equip the gifted armor.");
            Require(game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
                    .Any(item => item.SkillId == Kangkai && item.Bind == "gift" &&
                         item.Cards.Single().Id == baguaId),
                "The gift must be revealed publicly.");
            Require(game.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
                    .Single(item => item.SkillId == Kangkai).OptionId == "use-equipment",
                "The recorded choice must be the equipment use.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Slash with a gifted armor within distance 1.");
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
            "The Kangkai prompt must be a program trigger.");
        var activate = prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate") ??
            throw new InvalidOperationException("The Kangkai prompt lost its activate choice.");
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            activate.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Kangkai activation failed.");
    }

    private static void GiveCard(GameEngine game, int cardId)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "No pending decision for the gift selection.");
        if (prompt.Choices.Any(item => item.Parameters.GetValueOrDefault("program-action") == "select-target"))
        {
            var targetChoice = prompt.Choices.Single(item =>
                item.Parameters.GetValueOrDefault("program-action") == "select-target" &&
                item.Targets.SequenceEqual([1]));
            Answer(game, targetChoice);
            prompt = game.PendingDecision ?? throw new InvalidOperationException(
                "The target selection did not advance to the gift selection.");
        }
        var choice = prompt.Choices.FirstOrDefault(item => item.Cards.SequenceEqual([cardId])) ??
            throw new InvalidOperationException(
                $"The gift selection lost card {cardId}: kind={prompt.Kind}, seat={prompt.PlayerSeat}, choices=" +
                JsonSerializer.Serialize(prompt.Choices.Select(item => new { item.Cards,
                    action = item.Parameters.GetValueOrDefault("program-action"),
                    zone = item.Parameters.GetValueOrDefault("source-zone"),
                    slot = item.Parameters.GetValueOrDefault("slot-index"),
                    option = item.Parameters.GetValueOrDefault("option-id") }).ToArray()) + ".");
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The gift selection failed.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Cao Ang skill answer failed.");
    }

    private static GameEngine Start(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Cao Ang fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Cao Ang selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 60 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Cao Ang fixture did not reach Play.");
    }

    private static void Finish(GameEngine game)
    {
        for (var step = 0; step < 120 && game.ResolutionStack.Count > 0; step++)
        {
            if (game.PendingDecision is { } prompt && prompt.Kind == DecisionKind.ProgramTrigger)
            {
                var choice = prompt.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("program-action") == "activate") ?? prompt.Choices.First();
                var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                    choice.Id, game.Revision));
                Require(result.Accepted, result.Error?.Message ?? "Could not answer a program trigger.");
            }
            else if (game.PendingDecision is { PlayerSeat: 0 } response)
            {
                var choice = response.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("response") is "take-damage" or "let-die") ??
                    response.Choices.First();
                Answer(game, choice);
            }
            else Advance(game);
        }
        Require(game.ResolutionStack.Count == 0,
            $"A Slash resolution remained active: pending={game.PendingDecision?.Kind}/{game.PendingDecision?.PlayerSeat}, stack={string.Join(',', game.ResolutionStack.Select(item => item.GetType().Name + '/' + item.Step))}.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Cao Ang fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Cao Ang card action failed.");
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
            $"Expected invalid Kangkai composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("cao-ang-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 146, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:cao-ang-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "测试目标", "supporter",
                    "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 180).Select(index => (index % 4) switch
            {
                0 => "standard:slash",
                1 => "standard:bagua",
                2 => "standard:dodge",
                _ => "standard:qinggang_sword"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:cao-ang-deck", "曹昂测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "曹昂测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:cao-ang-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General, .. targets]));
        }
    }
}
