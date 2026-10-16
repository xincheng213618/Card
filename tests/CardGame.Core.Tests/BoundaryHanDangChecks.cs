using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryHanDangChecks
{
    private const string Gongqi = "boundary:gongqi-current";
    private const string Jiefan = "boundary:jiefan-current";
    private const string Driver = "fixture:hd-driver";
    private const string Cost = "fixture:hd-cost";
    private const string Recovery = "fixture:hd-recovery";
    private const string Mode = "identity:classic-boundary-han-dang-fixture";

    public static void FrozenSuitQuotaPreparedPrivacyAndExpiry()
    {
        var (game, registry) = Create();
        Use(game, Driver, "draw"); ReachPlay(game);
        var events = 0;
        game.EventCommitted += envelope => { if (envelope.Payload is TurnSlashSuitAllowanceGrantedEvent) events++; };
        game.StateChanged += view => { if (view.Players[0].TurnSlashSuitAllowances is { } policies) Frozen(policies); };
        var cost = game.CreateSnapshot(0).Players[0].Hand.First(c => c.Suit == Suit.Heart).Id;
        BeginGongqi(game); AssertPrivate(game); Replay(game, registry);
        Answer(game, c => c.Cards.SequenceEqual([cost])); ReachPlay(game);
        var policy = game.CreateSnapshot(0).Players[0].TurnSlashSuitAllowances!.Single();
        Require(events == 1 && policy.Suit == Suit.Heart && policy.Source.SkillId == Gongqi &&
            game.ObserverFailures.Count == 0 && game.CardMovements.Count(m => m.CardId == cost && m.Reason.Value == "skill-program.bound-discard-slash.cost") == 1,
            "One real discard issues one public frozen suit policy after its payment.");
        for (var viewer = 0; viewer < 4; viewer++)
        {
            var view = game.CreateSnapshot(viewer);
            Frozen(view.Players[0].TurnSlashSuitAllowances!);
            Require(view.Players[0].TurnSlashSuitAllowances is [var shown] && shown == policy && (viewer == 0 || view.Players[0].Hand.Count == 0),
                "Prepared public scalar policies reveal no hidden hand identities to other seats.");
        }
        Replay(game, registry);
        for (var i = 0; i < 2; i++)
        {
            var slash = game.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.Slash && c.Suit == Suit.Heart).Id;
            Require(game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == slash && a.TargetSeats.Contains(2)),
                "Matching real Slashes remain usable against a distant target.");
            Accept(game, new PlayCardCommand(0, slash, [2], game.Revision, Prompt(game)!.PromptId)); ReachPlay(game);
        }
        var unmatched = game.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.Slash && c.Suit == Suit.Spade).Id;
        Accept(game, new PlayCardCommand(0, unmatched, [2], game.Revision, Prompt(game)!.PromptId)); ReachPlay(game);
        Require(game.Events.Count(e => e.Payload is CardUseDebitRecordedEvent debit && debit.Debit.ActorSeat == 0) == 1,
            "Matching uses do not debit the ordinary Slash quota; the nonmatching use debits it once.");
        var second = game.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.Slash && c.Suit == Suit.Spade).Id;
        var before = State(game);
        Require(!game.Submit(new PlayCardCommand(0, second, [2], game.Revision, Prompt(game)!.PromptId)).Accepted && State(game) == before,
            "A second nonmatching physical Slash rejects atomically after the ordinary quota is spent.");
        Use(game, Driver, "suppress"); ReachPlay(game);
        Require(game.State.Players[0].Hp == 1 && game.State.Players[0].TurnSlashSuitAllowances is [var remaining] && remaining == policy &&
            game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId is { } id && game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == id && c.Suit == Suit.Heart)),
            "The issued actual-turn suit policy survives real suppression of its source instance.");
        Replay(game, registry);
        EndTurn(game);
        Require(game.State.Players[0].TurnSlashSuitAllowances is null && game.Events.Any(e => e.Payload is TurnCardUseEffectsExpiredEvent expired && expired.GrantSequences.Contains(policy.GrantSequence)),
            "The actual turn boundary expires the public suit policy exactly once.");
        Replay(game, registry);
    }

    private static (GameEngine, ContentRegistry) Create(bool equipment = false, bool aiOthers = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(equipment, aiOthers));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:hd-owner", game.Revision, Prompt(game)!.PromptId)); ReachPlay(game);
        return (game, registry);
    }
    private static void BeginGongqi(GameEngine game) { Use(game, Gongqi, "discard-for-turn-benefits"); Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards")); }
    private static void Use(GameEngine game, string skill, string activation, IReadOnlyList<int>? targets = null) => Accept(game,
        new UseProgramSkillCommand(0, skill, activation, [], targets ?? [], game.Revision, Prompt(game)!.PromptId));
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void ReachPlay(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate) { var p = Prompt(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void EndTurn(GameEngine game)
    {
        var count = game.Events.Count(e => e.Payload is TurnEndedEvent { ActorSeat: 0 });
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        for (var step = 0; step < 80 && game.Events.Count(e => e.Payload is TurnEndedEvent { ActorSeat: 0 }) == count; step++) Advance(game);
        Require(game.Events.Count(e => e.Payload is TurnEndedEvent { ActorSeat: 0 }) == count + 1, "The actual human turn ends once.");
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 150; step++) { var p = Prompt(game); if (p is not null && predicate(p)) return; Advance(game); }
        throw new InvalidOperationException("Fixed Han Dang fixture did not reach boundary: " + JsonSerializer.Serialize(Prompt(game)));
    }
    private static void Advance(GameEngine game)
    {
        var p = Prompt(game);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(game, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision));
        else if (p?.SkillPrompt?.SkillId is Cost or Recovery) Continue(game);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void AssertPrivate(GameEngine game) { for (var viewer = 1; viewer < 4; viewer++) Require(game.CreateSnapshot(viewer).PendingDecision is null && game.CreateSnapshot(viewer).Players[0].Hand.Count == 0, "Other seats cannot see the actual owned-card payment prompt."); }
    private static void Frozen<T>(IReadOnlyList<T> values)
    {
        Require(values is IList<T> { IsReadOnly: true }, "Prepared policy lists are read-only.");
        var rejected = false; try { ((IList<T>)values)[0] = values[0]; } catch (NotSupportedException) { rejected = true; }
        Require(rejected, "Prepared observers cannot replace policy list elements.");
    }
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Replay(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)), "The real command prefix restores all four views, paid receipts, exact movement and facts cold.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool equipment, bool aiOthers) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-han-dang", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardClassicGeneralPackage).Assembly;
            string Embedded(string suffix) { using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var actual = SkillProgramCatalog.Load(Embedded("boundary-han-dang.rules.json"), Embedded("boundary-han-dang.presentation.json"));
            foreach (var id in new[] { Gongqi, Jiefan }) builder.AddSkill(new(id, id == Gongqi ? "弓骑" : "解烦", "当前普通OL") { Program = actual.Programs[id], ProgramPresentation = actual.Presentations[id], Tags = id == Jiefan ? SkillTag.Limited : SkillTag.None });
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                  {"id":"suppress","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":4}]},
                  {"id":"other-limited","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"draw","target":"owner","amount":1}]},
                  {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}]},
                 {"id":"fixture:hd-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
                 {"id":"fixture:hd-rewrite","revision":1,"cardPolicies":[{"id":"wounded-spade","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"heart","condition":{"kind":"wounded"} } ] },
                 {"id":"{{Cost}}","revision":1,"triggers":[{"id":"cost","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.bound-discard-slash.cost"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Recovery}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实驱动", description = "实际摸牌、失去体力及额外回合" }, ["fixture:hd-quiet"] = new { name = "安静回合", description = "限定普通杀额度" },
                    ["fixture:hd-rewrite"] = new { name = "受伤改色", description = "体力回复使黑桃变红桃的条件失效" },
                    [Cost] = new { name = "付款观察", description = "真实移动子链", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Recovery] = new { name = "回血观察", description = "真实回复子链", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var id in new[] { Driver, "fixture:hd-quiet", "fixture:hd-rewrite", Cost, Recovery }) builder.AddSkill(new(id, id, "机制夹具") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:hd-suppression", "真实抑制", "HP1抑制其他技能") { SuppressionRule = new(1) });
            builder.AddSkill(new("fixture:hd-selection", "固定选将", "无运行技能") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:hd-owner", "界韩当机制", "supporter", Gongqi, "wu", 4,
                equipment ? [Jiefan, Driver, "fixture:hd-rewrite", Cost, Recovery] : [Jiefan, Driver, "fixture:hd-suppression"]) { InitialHp = equipment ? 3 : 4 });
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:hd-target-{i}", "固定目标", "supporter", "fixture:hd-selection", "shu", 8,
                aiOthers ? ["fixture:hd-quiet", Gongqi] : ["fixture:hd-quiet"]));
            if (equipment) builder.AddCard(new("fixture:hd-lion", "白银狮子", "装备牌", "真实防具回复", CardKind.SilverLion));
            builder.AddDeck(new("fixture:hd-deck", "固定实体", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 160)
                .Select(i => new ContentDeckPhysicalCard(equipment ? "fixture:hd-lion" : "standard:slash", equipment || i % 2 == 0 ? Suit.Spade : Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "当前界韩当", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:hd-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:hd-owner", "fixture:hd-target-1", "fixture:hd-target-2", "fixture:hd-target-3"]));
        }
    }
}
