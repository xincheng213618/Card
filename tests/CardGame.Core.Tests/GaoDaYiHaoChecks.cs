using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GaoDaYiHaoChecks
{
    public static void CoreFighterRevivesOncePerGame()
    {
        var registry = Registry();
        var revived = false;
        for (var seed = 1; seed <= 600 && !revived; seed++)
        {
            var game = Start(registry, Mode, seed);
            for (var step = 0; step < 3000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is null)
                {
                    Advance(game);
                    continue;
                }
                var rescue = prompt.PlayerSeat == 0
                    ? prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
                        choice.Parameters.GetValueOrDefault("skill-id") == CoreFighter)
                    : null;
                if (rescue is null)
                {
                    if (prompt.PlayerSeat != 0)
                    {
                        Advance(game);
                        continue;
                    }
                    StepDeclining(game);
                    continue;
                }
                var checkpoint = RoundTrip(game.CreateCheckpoint());
                var answer = game.Submit(new AnswerPromptCommand(
                    0, prompt.PromptId, rescue.Id, game.Revision));
                Require(answer.Accepted, answer.Error?.Message ?? "Core Fighter activation failed.");
                SettleResolution(game);
                if (game.State.Status == EngineStatus.Completed) break;
                var after = game.CreateSnapshot(0, true).Players[0];
                Require(after.IsAlive && after.Hp == 2 && after.Hand.Count == 2,
                    "Core Fighter must discard all cards, recover to two HP and draw two.");
                var replay = GameReplay.Restore(checkpoint, registry);
                var replayAnswer = replay.Submit(new AnswerPromptCommand(
                    0, replay.PendingDecision!.PromptId, rescue.Id, replay.Revision));
                Require(replayAnswer.Accepted, "Core Fighter replay activation failed.");
                SettleResolution(replay);
                Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                    "The Core Fighter rescue must replay identically from the paused checkpoint.");
                revived = true;
                break;
            }
        }
        Require(revived, "No seeded setup exercised Core Fighter's dying rescue.");
    }


    private const string General = "classic:gao-da-yi-hao";
    private const string BeamRifle = "classic:beam-rifle";
    private const string IField = "classic:i-field";
    private const string MobileArmor = "classic:mobile-armor";
    private const string CoreFighter = "classic:core-fighter";
    private const string Mode = "identity:gao-da-yi-hao-check-5";

    public static void BeamRifleDiscardsOneAndDamagesOncePerTurn()
    {
        var registry = Registry();
        var game = Start(registry, Mode, 1);
        ReachPlay(game);
        var action = game.GetHumanLegalActions().FirstOrDefault(item =>
            item.Kind == LegalActionKind.UseProgramSkill &&
            item.ProgramSkillId == BeamRifle &&
            item.ProgramActivationId == "beam-shot");
        if (action is null)
            throw new InvalidOperationException(
                "Beam Rifle must offer its activation during Gundam One's play phase.");
        var before = game.CreateSnapshot(0, true);
        Require(before.Players[1].IsAlive && action.SelectableCardIds.Count > 0 &&
                action.SelectableCardIds.All(cardId =>
                    before.Players[0].Hand.Any(card => card.Id == cardId)),
            "Beam Rifle must select its payment from hand cards.");
        var cardId = action.SelectableCardIds[0];
        var result = game.Submit(new UseProgramSkillCommand(
            0, BeamRifle, "beam-shot", [cardId], [1], game.Revision,
            game.PendingDecision!.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Beam Rifle activation failed.");
        var after = game.CreateSnapshot(0, true);
        Require(after.Players[0].Hand.All(card => card.Id != cardId) &&
                after.Players[1].Hp == before.Players[1].Hp - 1 &&
                game.Events.Select(item => item.Payload).OfType<DamageRequestedEvent>()
                    .Any(item => item.SourceSeat == 0 && item.TargetSeat == 1 && item.Amount == 1),
            "Beam Rifle must discard the payment card and deal one damage to the target.");
        Require(game.GetHumanLegalActions().All(item =>
                item.ProgramSkillId != BeamRifle),
            "Beam Rifle must stay exhausted for the rest of the turn.");
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(Snapshot(replay) == Snapshot(game),
            "The Beam Rifle activation must replay identically from a checkpoint.");
    }

    private static void StepDeclining(GameEngine game)
    {
        var prompt = game.PendingDecision;
        GameCommand command = prompt is { PlayerSeat: 0 }
            ? prompt.Kind == DecisionKind.PlayCard
                ? new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)
                : new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Last().Id, game.Revision)
            : new AdvanceOneStepCommand(game.Revision);
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "Gundam One fixture could not continue.");
    }

    private static void SettleResolution(GameEngine game)
    {
        for (var settle = 0; settle < 200 && game.ResolutionStack.Count > 0 &&
                game.State.Status != EngineStatus.Completed; settle++)
        {
            var pending = game.PendingDecision;
            if (pending is null || pending.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            Answer(game, pending.Choices.Last());
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
        Require(result.Accepted, result.Error?.Message ?? "Gundam One skill answer failed.");
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
        Require(game.Submit(new StartGameCommand()).Accepted, "Gundam One fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Gundam One selection failed.");
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
            "Gundam One fixture did not reach Play.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Gundam One fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Gundam One card action failed.");
    }

    private static string Snapshot(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string State(GameEngine game) => Snapshot(game);

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("gao-da-yi-hao-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 156, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition("fixture:gdyh-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:gdyh-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:gdyh-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:gdyh-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            var deckCards = Enumerable.Range(0, 240).Select(index => (index % 10) switch
            {
                <= 5 => "standard:slash",
                6 => "standard:dodge",
                7 => "standard:draw_two",
                8 => "standard:barbarian_assault",
                _ => "standard:duel"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:gdyh-deck", "高达一号测试牌堆", 5, 2, [])
            { PhysicalCards = deckCards });
            var pool = new[]
            {
                General, "fixture:gdyh-bank-a", "fixture:gdyh-bank-b", "fixture:gdyh-bank-c", "fixture:gdyh-bank-d"
            };
            builder.AddMode(new ContentModeDefinition(Mode, "高达一号测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:gdyh-deck", GeneralCandidateCount: 5, GeneralPoolIds: pool));
        }
    }
}
