using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class EquipmentHalfDrawChecks
{
    private const string SkillId = "fixture:equipment-draw";
    private const string ObserverId = "fixture:equipment-observer";
    private const string DrawReason = "skill-program.fixture:equipment-draw.Draw";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void PublicEquipmentCountDrawsOnceAndResumes()
    {
        var rules = Rules();
        var catalog = SkillProgramCatalog.Load(rules, Presentation);
        var effect = catalog.Programs[SkillId].Triggers.Single().Effects.Single();
        foreach (var equipmentCount in new[] { 0, 1, 2, 3, 4, 5, 6, 9, 10 })
        {
            var (game, registry) = Create();
            var zones = (CardZoneStore)typeof(GameEngine).GetField("_cardZones", PrivateInstance)!.GetValue(game)!;
            var owner = ((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", PrivateInstance)!.GetValue(game)!)[0];
            // These cases deliberately inject real zone entities to exercise expanded slots;
            // checkpoint replay is asserted separately for the unmodified zero-equipment case.
            if (equipmentCount > 0)
            {
                owner.EquipmentSlotCapacities[EquipmentSlot.Weapon] = 5;
                owner.EquipmentSlotCapacities[EquipmentSlot.Armor] = 5;
            }
            foreach (var card in zones.CardsAt(CardLocation.DrawPile)
                         .Where(card => card.Kind == CardKind.Crossbow).Take(Math.Min(5, equipmentCount)).ToArray())
                zones.Move(card.Id, CardLocation.DrawPile, CardLocation.Equipment(0));
            foreach (var card in zones.CardsAt(CardLocation.DrawPile)
                         .Where(card => card.Kind == CardKind.BaguaFormation).Take(Math.Max(0, equipmentCount - 5)).ToArray())
                zones.Move(card.Id, CardLocation.DrawPile, CardLocation.Equipment(0));
            Require(zones.CardsAt(CardLocation.Equipment(0)).Count == equipmentCount, "Fixture equipment count changed.");

            var expected = equipmentCount switch { 0 => 1, 1 or 2 => 2, 3 or 4 => 3, 5 or 6 => 4, 9 or 10 => 6, _ => throw new InvalidOperationException() };
            var estimate = ProgramCompositionAi.Estimate([effect], new(0, 4, 4, 3, TurnPhase.Draw, IsOwnTurn: true),
                publicContext: new(1, EquipmentCardCount: equipmentCount));
            Require(estimate.Hint.OwnerDraw == expected && estimate.Score > 0d,
                "AI must price the actual public equipment count, including expanded slots.");

            GameEngine? replay = equipmentCount == 0 ? GameReplay.Restore(game.CreateCheckpoint(), registry) : null;
            Answer(game, SkillId, "activate");
            Reach(game, ObserverId);
            Require(DrawCount(game) == expected, "One equipment-count draw must move the exact computed number of real cards.");
            foreach (var card in zones.CardsAt(CardLocation.Equipment(0)).ToArray())
                zones.Move(card.Id, CardLocation.Equipment(0), CardLocation.DrawPile);
            Answer(game, ObserverId, "skip");
            ReachPlay(game);
            Require(DrawCount(game) == expected && game.Events.Select(item => item.Payload)
                    .OfType<ProgramBindingResolvedEvent>().Count(item => item.SkillId == SkillId && item.Activated && item.Completed) == 1,
                "An awaited gained-card child must resume the paid draw exactly once after equipment changes.");
            if (replay is not null)
            {
                Answer(replay, SkillId, "activate"); Reach(replay, ObserverId);
                var paused = GameReplay.Restore(replay.CreateCheckpoint(), registry);
                foreach (var branch in new[] { replay, paused }) { Answer(branch, ObserverId, "skip"); ReachPlay(branch); }
                Require(SnapshotJson.Serialize(replay.CreateSnapshot(0)) == SnapshotJson.Serialize(paused.CreateSnapshot(0)) &&
                        SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(replay.CreateSnapshot(0)),
                    "The unmodified fixture must replay both the optional offer and its paid draw child.");
            }
        }
        var (skipped, _) = Create(); Answer(skipped, SkillId, "skip"); ReachPlay(skipped);
        Require(DrawCount(skipped) == 0, "Declining the optional draw must preserve normal drawing without the skill gain.");
        Reject(rules.Replace("\"target\":\"owner\",\"numberExpression\":\"equipmentHalfCeilingPlusOne\"",
            "\"target\":\"selectedTarget\",\"numberExpression\":\"equipmentHalfCeilingPlusOne\""));
    }

    private static int DrawCount(GameEngine game) => game.CardMovements.Count(move => move.Reason.Value == DrawReason);
    private static void Answer(GameEngine game, string skill, string action)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Missing equipment-count prompt.");
        var choice = prompt.Choices.Single(item => item.Parameters.GetValueOrDefault("skill-id") == skill &&
            item.Parameters.GetValueOrDefault("program-action") == action);
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Equipment-count answer rejected.");
    }
    private static void Reach(GameEngine game, string skill)
    {
        for (var step = 0; step < 40; step++)
        {
            if (game.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("skill-id") == skill) == true) return;
            Require(game.PendingDecision is null || game.PendingDecision.PlayerSeat != 0,
                "An unexpected human prompt interrupted the equipment-count fixture.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Equipment-count fixture failed to advance.");
        }
        throw new InvalidOperationException("Equipment-count fixture exceeded its bounded advance budget.");
    }
    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 40; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            // DrawCards commits each acquired card as a movement batch. A multi-card
            // draw can therefore queue more than one observer child for this operation.
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("skill-id") == ObserverId) == true)
            {
                Answer(game, ObserverId, "skip");
                continue;
            }
            Require(game.PendingDecision is null || game.PendingDecision.PlayerSeat != 0,
                $"An unexpected human prompt interrupted the draw continuation: {game.PendingDecision?.Kind}.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Draw continuation failed to advance.");
        }
        throw new InvalidOperationException("Draw continuation exceeded its bounded advance budget.");
    }
    private static (GameEngine, ContentRegistry) Create()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "fixture:equipment-half-draw", UseInteractiveSetup = false,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Equipment-count fixture start failed.");
        Reach(game, SkillId); return (game, registry);
    }
    private static string Rules() => "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[" +
        "{\"id\":\"" + SkillId + "\",\"revision\":1,\"triggers\":[{\"id\":\"gain\",\"window\":\"drawPhaseStarting\",\"subject\":\"owner\",\"optional\":true,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"numberExpression\":\"equipmentHalfCeilingPlusOne\"}]}]}," +
        "{\"id\":\"" + ObserverId + "\",\"revision\":1,\"triggers\":[{\"id\":\"pause\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"optional\":true,\"movementOccurrence\":\"perBatch\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"" + DrawReason + "\"],\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]}]}";
    private const string Presentation = """
        {"schemaVersion":3,"skills":{"fixture:equipment-draw":{"name":"装备摸牌","description":"共享装备数量表达式。"},"fixture:equipment-observer":{"name":"摸牌观察","description":"等待已支付摸牌的子窗口。"}}}
        """;
    private static void Reject(string rules)
    {
        try { SkillProgramCatalog.Load(rules, Presentation); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("equipment half-count draw", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Equipment-count expressions must reject unrelated target contexts.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("equipment-half-draw-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, program) in SkillProgramCatalog.Load(Rules(), Presentation).Programs)
                builder.AddSkill(new(id, id, "Fixture") { Program = program });
            var generals = Enumerable.Range(0, 4).Select(index => $"fixture:equipment-half-{index}").ToArray();
            foreach (var id in generals) builder.AddGeneral(new(id, "Fixture", "supporter", SkillId,
                BaseHp: 4, AdditionalSkillIds: [ObserverId]));
            builder.AddDeck(new("fixture:equipment-half-deck", "Fixture", 3, 0,
                [new("standard:peach", 100), new("standard:crossbow", 20), new("standard:bagua", 20)]));
            builder.AddMode(new("fixture:equipment-half-draw", "Fixture", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:equipment-half-deck", GeneralCandidateCount: 1, GeneralPoolIds: generals));
        }
    }
}
