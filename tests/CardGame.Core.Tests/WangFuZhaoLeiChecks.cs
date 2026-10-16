using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WangFuZhaoLeiChecks
{
    private const string Skill = "mobile:xunyi", Driver = "fixture:xunyi-driver", Quiet = "fixture:xunyi-quiet", Watch = "fixture:xunyi-watch";
    private const string Owner = "fixture:xunyi-owner", Mode = "identity:classic-xunyi-fixture";

    public static void ActualDamagePointsSourceLessSelfAndMutualExclusionCold()
    {
        var (game, registry) = Start();
        Need(E<XunyiRelationChangedEvent>(game).Single().Relation.HolderSeat == 1 && Mark(game, 1) == 1,
            "The actual mandatory game-start other-character selection establishes one attributed Yi relation.");
        Use(game, "outgoing-two", [2]); Play(game);
        Need(E<XunyiPointPaidEvent>(game) is [{ RecipientSeat: 1, Draw: true, Count: 1 }, { RecipientSeat: 1, Draw: true, Count: 1 }],
            "One actual two-point outgoing damage gives the original partner two separate real single-card draws.");
        Use(game, "incoming-two", [2]); Reach(game, () => Discard(game));
        var prompt = P(game)!;
        Need(prompt.PlayerSeat == 1 && prompt.IsPrivate && prompt.Choices.All(c => c.Cards.Count == 1) &&
            Enumerable.Range(0, 4).Where(s => s != 1).All(s => game.CreateSnapshot(s).PendingDecision is null),
            "The actual opposite participant privately selects their own hand or equipment card; other viewers receive no private choices.");
        var unchanged = State(game);
        var rejected = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, game.Revision));
        Need(!rejected.Accepted && State(game) == unchanged, "The Xunyi owner cannot answer the partner's private discard prompt.");
        game = Cold(game, registry); Answer(game, _ => true); Reach(game, () => Discard(game));
        game = Cold(game, registry); Accept(game, new AdvanceOneStepCommand(game.Revision)); Play(game);
        Need(E<XunyiPointPaidEvent>(game).Skip(2).ToArray() is [{ RecipientSeat: 1, Draw: false, Count: 1 }, { RecipientSeat: 1, Draw: false, Count: 1 }],
            "The native AI's next real choice pays the second point once after the exact original damage cursor returns.");
        Use(game, "source-less"); Play(game);
        Need(E<XunyiDamageQualifiedEvent>(game).Last().Receipt is { ActualSourceSeat: null, ActualAmount: 2, DiscardRecipientSeat: 1, DrawRecipientSeat: null } &&
            E<XunyiPointPaidEvent>(game).Skip(4).Count(e => !e.Draw && e.Count == 1) == 2,
            "Actual source-less received damage satisfies source-not-partner and discards twice without creating a dealing branch.");
        var before = E<XunyiPointPaidEvent>(game).Length;
        Use(game, "self-two"); Play(game);
        Need(E<XunyiPointPaidEvent>(game).Skip(before).Select(e => (e.RecipientSeat, e.Draw, e.Count)).SequenceEqual(new[] { (1, false, 1), (1, true, 1), (1, false, 1), (1, true, 1) }),
            "Self-damage satisfies each independent written branch once per point, without enumerating the same participant twice.");
        before = E<XunyiDamageIssuedEvent>(game).Length;
        Use(game, "outgoing-two", [1]); Play(game); Use(game, "incoming-two", [1]); Play(game); Use(game, "lose-hp"); Play(game);
        Need(E<XunyiDamageIssuedEvent>(game).Length == before,
            "Damage between the two linked participants and separate HP loss cannot trigger Xunyi.");
        _ = Cold(game, registry);
        var (prevented, preventedRegistry) = Start("prevent");
        var hp = V(prevented, 0).Hp; Use(prevented, "self-two"); Play(prevented);
        Need(V(prevented, 0).Hp == hp && E<XunyiDamageQualifiedEvent>(prevented).Length == 0 && E<XunyiPointPaidEvent>(prevented).Length == 0,
            "A native prevented damage attempt produces no actual damage receipt or Xunyi consequence.");
        _ = Cold(prevented, preventedRegistry);
    }

    private static PlayerSnapshot V(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat];
    private static int Mark(GameEngine game, int seat) => (V(game, seat).Markers ?? []).SingleOrDefault(m => m.Kind == PlayerMarkerKind.XunyiYi)?.Count ?? 0;
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine game) where T : IGameEvent => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Discard(GameEngine game) => P(game)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "xunyi-discard") == true;
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Xunyi command."); }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    { var prompt = P(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(choose).Id, game.Revision)); }
    private static void Use(GameEngine game, string binding, IReadOnlyList<int>? targets = null) =>
        Accept(game, new UseProgramSkillCommand(0, Driver, binding, [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void Play(GameEngine game) => Reach(game, () => P(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine game, Func<bool> done)
    {
        for (var step = 0; step < 220; step++)
        {
            if (done()) return;
            Need(game.State.Winner == Winner.None, "Xunyi fixture ended before its actual boundary.");
            var prompt = P(game);
            if (Discard(game) && prompt!.PlayerSeat == 0) Answer(game, _ => true);
            else if (prompt?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (prompt is { Kind: DecisionKind.RescueDying } && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass")) Answer(game, c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Missing real Xunyi boundary: " + JsonSerializer.Serialize(new { Pending = P(game), Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(12).Select(e => e.Payload.ToString()) }));
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), game.CardMovements
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(State(game) == State(restored), "Cold real-command replay preserves every viewer, source relation, original damage, paid point, private prompt and typed child.");
        return restored;
    }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, () => P(game) is { Kind: DecisionKind.SelectGeneral });
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        Reach(game, () => P(game)?.SkillPrompt?.SkillId == Skill && P(game)!.Choices.Any(c => c.Targets.SequenceEqual([1])));
        Answer(game, c => c.Targets.SequenceEqual([1])); Play(game); return (game, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wang-fu-zhao-lei", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "mobile:wang-fu-zhao-lei");
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"outgoing-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                {"id":"incoming-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
                {"id":"self-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]},
                {"id":"partner-self","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":2}]},
                {"id":"lose-hp","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实殉义边界驱动", description = "真实damage命令" }, [Quiet] = new { name = "实际其他角色", description = "固定安静角色" } };
            if (scenario == "prevent")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-prevention","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"effects":[{"op":"preventCurrentDamage","target":"owner"}]}]}
                """));
                names[Watch] = new { name = "实际防止伤害", description = "真实防伤" };
            }
            if (scenario == "lion")
            {
                rules["skills"]![0]!["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]}"""));
                rules["skills"]![0]!["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"lower-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]}"""));
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-lion-recovery-damage-child","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"recovery","options":[{"id":"continue"}]},{"op":"damage","target":"owner","amount":1}]}]}
                """));
                names[Watch] = new { name = "实际狮子回复后伤害子窗", description = "真实回复子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
                builder.AddCard(new("fixture:xunyi-lion", "白银狮子", "装备牌", "实际原生白银狮子", CardKind.SilverLion));
            }
            if (scenario == "loss")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-gain-source-loss","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.xunyi.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"before","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"after","options":[{"id":"after-loss"}]}]}]}
                """));
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"fixture:xunyi-conditional","revision":1,"hpSkillGrants":[{"id":"actual-xunyi","skillId":"{{Skill}}","minimumHp":13}]}"""));
                names["fixture:xunyi-conditional"] = new { name = "实际体力技能来源", description = "实际主公13体力时授予殉义" };
                names[Watch] = new { name = "实际摸牌后失源子窗", description = "真实技能失去", optionLabels = new Dictionary<string, string> { ["continue"] = "继续", ["after-loss"] = "继续失源后的原伤害" } };
            }
            if (scenario == "death-child")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-discard-holder-death","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.xunyi.discard"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"before","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}
                """));
                names[Watch] = new { name = "实际弃牌后义角色死亡子窗", description = "真实失去体力死亡", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id], SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => id == Quiet ? 10000d : -10000d) });
            builder.AddGeneral(new(Owner, "真实王甫赵累", "mobile-wang-fu-zhao-lei", Driver, "shu", 12,
                scenario == "loss" ? ["fixture:xunyi-conditional", Watch] : scenario == "prevent" ? [Skill, Watch] : [Skill], GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:xunyi-peer-{i}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "实际其他角色", "supporter", Quiet, "wei", 12, scenario is "death-child" or "lion" ? [Watch] : []));
            builder.AddDeck(new("fixture:xunyi-deck", "固定实体牌堆", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(scenario == "lion" && i % 3 == 0 ? "fixture:xunyi-lion" : "standard:dodge", Suit.Spade, 7)).ToArray() });
            builder.AddMode(new(Mode, "真实殉义边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:xunyi-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
