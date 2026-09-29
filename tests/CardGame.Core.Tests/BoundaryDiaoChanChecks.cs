using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryDiaoChanChecks
{
    private const string GeneralId = "boundary:diao-chan";
    private const string XiaojiOwnerId = "fixture:diao-xiaoji";
    private const string BiyueId = "boundary:biyue";

    public static void LijianEquipmentLossTriggerPrecedesDuel()
    {
        var registry = Registry(allCrossbows: true, ownerXiaoji: true);
        var game = StartAtPlay(registry, seed: 1, generalId: XiaojiOwnerId);
        var weapon = game.CreateSnapshot(0, true).Players[0].Hand.First(card =>
            card.Kind == CardKind.Crossbow);
        Play(game, game.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.Equip && action.CardId == weapon.Id));
        if (game.PendingDecision?.Kind != DecisionKind.PlayCard) Advance(game);
        var action = game.GetHumanLegalActions().Single(item =>
            item.Kind == LegalActionKind.UseProgramSkill &&
            item.ProgramSkillId == "boundary:lijian");
        var pair = action.SelectableTargetSeats.Take(2).ToArray();
        var handBefore = game.CreateSnapshot(0, true).Players[0].HandCount;
        UseLijian(game, weapon.Id, pair[0], pair[1]);
        var xiaoji = game.PendingDecision ??
            throw new InvalidOperationException("Lijian did not wait for the equipment-loss trigger.");
        Require(xiaoji is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0,
                SkillPrompt.SkillId: "classic:xiaoji" } &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame =>
                    frame.SkillId == "boundary:lijian") &&
                game.ResolutionStack.All(frame => frame is not ResponseWindowFrame) &&
                game.Events.Select(item => item.Payload).OfType<DamageRequestedEvent>()
                    .All(item => item.SourceCard is not null),
            "Lijian must suspend its virtual Duel until Xiaoji resolves the lost weapon.");
        var paused = GameReplay.Restore(GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        foreach (var branch in new[] { game, paused })
        {
            var prompt = branch.PendingDecision!;
            var activate = prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate");
            var answered = branch.Submit(new AnswerPromptCommand(0, prompt.PromptId,
                activate.Id, branch.Revision));
            Require(answered.Accepted, answered.Error?.Message ?? "Xiaoji activation was rejected.");
            var xiaojiResolved = branch.Events.Single(item => item.Payload is ProgramBindingResolvedEvent
                { SkillId: "classic:xiaoji", Completed: true });
            var lijianResolved = branch.Events.LastOrDefault(item => item.Payload is ProgramSkillResolvedEvent
                { SkillId: "boundary:lijian", Completed: true });
            var duelDamage = branch.Events.FirstOrDefault(item => item.Payload is DamageRequestedEvent);
            Require(branch.CreateSnapshot(0, true).Players[0].HandCount == handBefore + 2 &&
                    (branch.ResolutionStack.LastOrDefault() is ResponseWindowFrame
                        { IncomingCard: CardKind.Duel, RequiredCardKind: CardKind.Slash } ||
                     lijianResolved is not null) &&
                    (duelDamage is null || xiaojiResolved.Sequence < duelDamage.Sequence) &&
                    (lijianResolved is null || xiaojiResolved.Sequence < lijianResolved.Sequence),
                $"Xiaoji must draw two cards before the suspended Lijian Duel opens its response window " +
                $"(hand={branch.CreateSnapshot(0, true).Players[0].HandCount}, expected={handBefore + 2}, " +
                $"pending={branch.PendingDecision?.Kind}, top={branch.ResolutionStack.LastOrDefault()?.Kind}, " +
                $"frames={string.Join(',', branch.ResolutionStack.Select(frame => frame.Kind))}, " +
                $"xiaojiSeq={xiaojiResolved.Sequence}, duelDamageSeq={duelDamage?.Sequence}, " +
                $"lijianSeq={lijianResolved?.Sequence}).");
        }
        Require(State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
            "The equipment-loss trigger and resumed virtual Duel must replay exactly.");
    }

    private static void UseLijian(GameEngine game, int cost, int source, int responder)
    {
        var result = game.Submit(new UseProgramSkillCommand(0, "boundary:lijian",
            "discard-and-start-duel", [cost],
            [source, responder], game.Revision, game.PendingDecision!.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Boundary Lijian was rejected.");
    }

    private static GameEngine StartAtPlay(ContentRegistry registry, int seed, string generalId = GeneralId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, ModeId = Scenario.ModeId,
            HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Boundary Diao Chan fixture start failed.");
        var selection = game.PendingDecision!;
        Require(selection.ValidContentIds.Contains(generalId), "Boundary Diao Chan fixture was not offered.");
        Require(game.Submit(new SelectGeneralCommand(0, generalId, game.Revision,
            selection.PromptId)).Accepted, "Boundary Diao Chan selection failed.");
        for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Boundary Diao Chan Play was not reached.");
        return game;
    }

    private static ContentRegistry Registry(bool allCrossbows, bool ownerXiaoji = false) => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new Scenario(allCrossbows, ownerXiaoji));

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Boundary Diao Chan card play failed.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Boundary Diao Chan did not advance.");
    }

    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(bool allCrossbows, bool ownerXiaoji) : IGameContentPackage
    {
        public const string ModeId = "identity:classic-boundary-diao-chan-check-5";
        public PackageManifest Manifest { get; } = new("boundary-diao-chan-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("fixture:boundary-diao-chan-deck", "界貂蝉牌堆", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(allCrossbows ? "standard:crossbow" :
                        (index % 4) switch
                        {
                            0 => "standard:crossbow",
                            1 => "standard:nullification",
                            _ => "standard:slash"
                        }, (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:diao-male-1", "男性目标一", "supporter",
                "standard:none", "wei", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:diao-male-2", "男性目标二", "supporter",
                "standard:none", "shu", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:diao-male-3", "男性目标三", "supporter",
                "standard:none", "wu", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:diao-female", "女性目标", "supporter",
                "standard:none", "qun", BaseHp: 4, Gender: GeneralGender.Female));
            if (ownerXiaoji)
                builder.AddGeneral(new ContentGeneralDefinition(XiaojiOwnerId, "离间枭姬组合", "supporter",
                    "boundary:lijian", "qun", BaseHp: 3, Gender: GeneralGender.Female,
                    AdditionalSkillIds: ["classic:xiaoji"]));
            builder.AddMode(new ContentModeDefinition(ModeId, "界貂蝉测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:boundary-diao-chan-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [ownerXiaoji ? XiaojiOwnerId : GeneralId, "fixture:diao-male-1", "fixture:diao-male-2",
                    "fixture:diao-male-3", "fixture:diao-female"]));
        }
    }
}
