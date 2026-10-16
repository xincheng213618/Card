using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class RecordedSuitSlashChecks
{
    private const string Skill = "mou:liegong";
    private static readonly Suit[] Suits = [Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond];

    public static void RecordingRevealDamageAndReplay()
    {
        var (game, registry) = Start();
        foreach (var suit in Suits)
        {
            var card = game.State.Players[0].Hand.First(c => c.Kind == CardKind.DrawTwo && c.Suit == suit);
            Play(game, card.Id, []);
            Settle(game);
        }
        Require(States(game).All(s => s.Value), "Actual uses record all four public suits without duplicate records.");
        var initialHand = game.State.Players[0].Hand.Count;
        var hp = game.State.Players[1].Hp;
        Slash(game);
        Reach(game, p => p.SkillPrompt?.SkillId == Skill);
        Cold(game, registry);
        Answer(game, "activate");
        var receipt = game.Events.Select(e => e.Payload).OfType<ProgramRecordedSuitSlashIssuedEvent>().Single().Receipt;
        Require(receipt.SuitMask == 15 && receipt.DamageBonus == 3, "Four suits reveal three matching cards and freeze a three-damage bonus.");
        Cold(game, registry);
        var eventCount = game.Events.Count;
        Settle(game);
        Require(game.State.Players[1].Hp == hp - 4, "A real paid Slash deals four damage when all Dodge suits are forbidden.");
        Require(!game.Events.Skip(eventCount).Select(e => e.Payload).OfType<CardRespondedEvent>().Any(), "Forbidden physical Dodge cards are never paid.");
        Require(States(game).All(s => !s.Value) && game.State.Players[0].Hand.Count == initialHand - 1,
            "Only Slash is paid; revealed cards are discarded and records clear at exact completion.");
        var revealed = game.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>().Where(e => e.SkillId == Skill).Single().Cards.Select(c => c.Id).ToArray();
        var movements = game.Events.Select(e => e.Payload).OfType<CardMovedEvent>().Where(e => revealed.Contains(e.CardId)).ToArray();
        Require(revealed.Length == 3 && revealed.All(id => movements.Any(e => e.CardId == id && e.From == CardLocation.Processing && e.To == CardLocation.DiscardPile)) &&
            movements.All(e => e.To != CardLocation.Hand(0)), "All three revealed physical cards are discarded without entering the owner hand.");
        Require(game.State.ProcessingCardCount == 0, "No revealed or used card remains in processing.");
        Cold(game, registry);
    }

    private static IReadOnlyList<ProgramBooleanStateSnapshot> States(GameEngine game) =>
        game.CreateSnapshot(0).Players[0].SkillRuntimeStates!.Single(s => s.SkillId == Skill).BooleanStates!;
    private static PendingDecision? Prompt(GameEngine game) => game.CreateSnapshot(0).PendingDecision;
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine game, GameCommand command) => BoundaryLiDianChecks.Accept(game, command);
    private static void Answer(GameEngine game, string action)
    {
        var p = Prompt(game)!;
        Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == action).Id, game.Revision));
    }
    private static void Play(GameEngine game, int cardId, int[] targets, LegalAction? action = null) =>
        Accept(game, new PlayCardCommand(0, cardId, targets, game.Revision, Prompt(game)!.PromptId, action?.PlayedCardKind)
        { ConversionSource = action?.ConversionSource, AdditionalConversionSources = action?.AdditionalConversionSources });
    private static void Slash(GameEngine game)
    {
        var action = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeat == 1);
        Play(game, action.CardId!.Value, [1], action);
    }
    private static void Settle(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> goal)
    {
        for (var i = 0; i < 120; i++)
        {
            if (Prompt(game) is { } p && goal(p)) return;
            if (Prompt(game) is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } optional &&
                optional.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(game, "skip");
            else if (Prompt(game) is { PlayerSeat: 0 } selection &&
                selection.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"))
                Accept(game, new AnswerPromptCommand(0, selection.PromptId,
                    selection.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-target").Id, game.Revision));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Finite recorded-suit fixture did not reach its expected prompt: " + Prompt(game)?.Prompt);
    }
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        for (var seat = -1; seat < 4; seat++)
            Require(JsonSerializer.Serialize(game.CreateSnapshot(seat)) == JsonSerializer.Serialize(restored.CreateSnapshot(seat)), "Cold command replay matches all five safe views.");
    }
    private static (GameEngine, ContentRegistry) Start(bool multiple = false, bool weapon = false, bool targetRecords = false, bool group = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(multiple, weapon, targetRecords, group));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:classic-recorded-suit", UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:recorded-owner", game.Revision, Prompt(game)!.PromptId)); Settle(game);
        return (game, registry);
    }
    private static SkillProgram LoadFixture(string id, string members) => SkillProgramCatalog.Load(
        "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + id + "\",\"revision\":1," + members + "}]}",
        "{\"schemaVersion\":3,\"skills\":{\"" + id + "\":{\"name\":\"准备\",\"description\":\"准备\"}}}").Programs[id];
    private sealed class Fixture(bool multiple, bool weapon, bool targetRecords, bool group) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-recorded-suit", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "mou:huang-zhong");
            if (multiple || weapon)
            {
                var extra = multiple ? "\"triggers\":[{\"id\":\"extra\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"optional\":false,\"priority\":200,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"addCurrentCardUseTarget\",\"target\":\"selectedTarget\"}]}]"
                    : "\"activations\":[{\"id\":\"equip\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"equipment\"}]}]";
                b.AddSkill(new("fixture:recorded-driver", "准备", "准备") { Program = LoadFixture("fixture:recorded-driver", extra) });
            }
            if (weapon) b.AddCard(new("fixture:weapon", "丈八蛇矛", "装备牌", "攻击范围3", CardKind.ZhangbaSerpentSpear));
            // The formal skill runs through actual paid uses in a fixed first-turn fixture.
            b.AddGeneral(new("fixture:recorded-owner", "谋黄忠", "mou_huang_zhong", Skill, "shu", 8,
                multiple || weapon ? ["fixture:recorded-driver"] : []));
            if (group) b.AddCard(new("fixture:arrows", "万箭齐发", "锦囊牌", "每名其他角色打出闪。", CardKind.ArrowBarrage));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:recorded-{i}", "固定目标", "supporter", targetRecords && i == 1 ? Skill : "standard:none", "wei", 8));
            b.AddDeck(new("fixture:recorded-deck", "固定实体", 24, 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i =>
                new ContentDeckPhysicalCard(group && i % 16 < 4 ? "fixture:arrows" : i % 16 < 8 ? "standard:draw_two" : i % 16 < 10 ? "standard:fire_slash" : i % 16 < 12 ? "standard:thunder_slash" : "standard:dodge", Suits[i % 4], 7))
                .Concat(weapon ? Enumerable.Range(0, 4).Select(_ => new ContentDeckPhysicalCard("fixture:weapon", Suit.Club, 12)) : []).ToArray() });
            b.AddMode(new("identity:classic-recorded-suit", "固定", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:recorded-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:recorded-owner", "fixture:recorded-1", "fixture:recorded-2", "fixture:recorded-3"]));
        }
    }
}
