using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class QiaoZhouChecks
{
    private const string Zhiming = "ol:zhiming", Xingbu = "ol:xingbu", Driver = "fixture:qz-driver";
    private const string Quiet = "fixture:qz-quiet", Watch = "fixture:qz-watch", Gain = "fixture:qz-gain";
    private const string DiscardBoundary = "fixture:qz-discard-boundary";
    private const string Conditional = "fixture:qz-healthy-forecast";
    private const string Owner = "fixture:qz-owner", Mode = "identity:classic-qiao-zhou-fixture";
    private const string RevealReason = "skill-program.ol:xingbu.forecast.reveal", CleanupReason = "skill-program.ol:xingbu.forecast.discard";
    private const string PreparationReason = "skill-program.ol:xingbu.preparation-discard";

    public static void ZhimingActualPreparationDiscardEndAndPublicForecastCold()
    {
        var (g, registry) = Start("zhiming", 3, false);
        Reach(g, () => OwnedSelection(P(g), Zhiming));
        var first = P(g)!; var hand = V(g, 0).Hand.First().Id;
        Need(V(g, 0).HandCount == 5 && first.IsPrivate && g.CreateSnapshot(1).PendingDecision is null &&
            first.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"),
            "Preparation really draws one before an optional private zero-or-one HE top-deck selection.");
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([hand])); Play(g);
        Need(g.CardMovements.Count(m => m.CardId == hand && m.From == CardLocation.Hand(0) && m.To == CardLocation.DrawPile) == 1 &&
            g.CardMovements.Any(m => m.CardId == hand && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw),
            "The selected actual preparation hand entity becomes the top card and is drawn in the following normal Draw.");
        var equip = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(g, new PlayCardCommand(0, equip.CardId!.Value, [], g.Revision, P(g)!.PromptId)); Play(g);
        var before = V(g, 0).HandCount; var turn = g.State.TurnNumber;
        End(g); Reach(g, () => OwnedSelection(P(g), Zhiming));
        Need(V(g, 0).HandCount == before + 1 && E<PhaseChangedEvent>(g).Any(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Discard) &&
            P(g)!.Choices.Any(c => c.Cards.SequenceEqual([equip.CardId.Value])),
            "Only the actual completed Discard boundary draws the second Zhiming card and permits the equipped entity as the optional top card.");
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([equip.CardId.Value])); Reach(g, () => Optional(P(g), Xingbu));
        Activate(g); Reach(g, () => Forecast(P(g)));
        var receipt = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.QiaoZhouForecast is not null).QiaoZhouForecast!;
        var ids = receipt.Cards.Select(c => c.CardId).ToArray();
        Need(receipt is { Stage: QiaoZhouForecastStage.Recipient, RedCount: 3, Cards.Count: 3 } && ids[0] == equip.CardId.Value &&
            ids.All(id => Location(g, id) == CardLocation.Processing) &&
            E<ProgramCardsRevealedEvent>(g).Single(e => e.SkillId == Xingbu).Cards.Select(c => c.Id).SequenceEqual(ids) &&
            Enumerable.Range(0, 4).All(s => g.CreateSnapshot(s).Players[0].Equipment.All(c => c.Id != equip.CardId.Value)),
            "The real ending program publicly reveals three exact physical top cards, including the equipment just returned by Zhiming, before its optional recipient choice.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option") == "finish");
        Reach(g, () => g.State.TurnNumber > turn);
        Need(ids.All(id => Location(g, id) == CardLocation.DiscardPile) &&
            ids.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason.Value == RevealReason) == 1 &&
                g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value == CleanupReason) == 1) &&
            !E<QiaoZhouForecastGrantedEvent>(g).Any(),
            "Declining a recipient still discards each actual revealed entity exactly once and invents no forecast mark.");
        _ = Cold(g, registry);
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static CardLocation Location(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    private static bool OwnedSelection(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Forecast(PendingDecision? p) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "qiao-zhou-forecast") == true;
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 250; step++)
        {
            if (done()) return;
            var p = P(g);
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Qiao Zhou boundary: " + JsonSerializer.Serialize(p));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Qiao Zhou boundary missing: " + JsonSerializer.Serialize(new { Pending = P(g), Frames = g.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Facts = g.Events.TakeLast(5).Select(e => e.Payload.ToString()) }));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Qiao Zhou command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "Cold commands preserve every private view, public reveal, pending forecast, exact paid hand card, owning frame and typed movement child."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scenario, int red, bool play = true)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario, red));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 18 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); if (play) Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario, int red) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-qiao-zhou", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:qiao-zhou");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],"triggers":{{(scenario == "recipient" && red is 1 or 2 ? "[{\"id\":\"actual-stock\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":8}]}]" : "[]")}},"activations":[
                {"id":"top-three","minCards":3,"maxCards":3,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"top"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"drawPileTop","awaitMovementTriggers":true}]},
                {"id":"top-five","minCards":5,"maxCards":5,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"top"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"drawPileTop","awaitMovementTriggers":true}]},
                {"id":"flip","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"owner"}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"unrelated-limit","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":1}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"actual-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["rule.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw","options":[{"id":"continue"}]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-hand-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{PreparationReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{DiscardBoundary}}","revision":1,"triggers":[{"id":"actual-start","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"start","options":[{"id":"continue"}]}]},{"id":"actual-end","window":"discardPhaseEnded","turnOwnerScope":"own","allowOwnDiscardPhaseEnded":true,"subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"end","options":[{"id":"continue"}]}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-forecast","skillId":"{{Xingbu}}","minimumHp":8}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, Gain, Watch, DiscardBoundary, Conditional };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "谯周真实窗口夹具", optionLabels = id is Gain or Watch or DiscardBoundary ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "实际谯周夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:qz-owner-pick", "固定本人", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:qz-peer-pick", "固定其他", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            var own = new List<string> { Driver, Gain, Watch, DiscardBoundary }; if (scenario == "zhiming") { own.Add(Zhiming); own.Add(Xingbu); } if (scenario == "donor") own.Add(Xingbu);
            b.AddGeneral(new(Owner, "谯周本人", "ol-qiao-zhou", "fixture:qz-owner-pick", "shu", 4, own));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:qz-peer-{i}").ToArray();
            foreach (var id in peers) b.AddGeneral(new(id, "受卜与赋卜者", "supporter", "fixture:qz-peer-pick", "shu", 8, scenario == "recipient" ? [Quiet, Conditional] : [Quiet]));
            b.AddDeck(new("fixture:qz-deck", "固定真实颜色实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(scenario == "zhiming" ? "standard:crossbow" : "standard:slash", red == 3 ? Suit.Heart : red == 0 ? Suit.Spade : i % 2 == 0 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "谯周真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:qz-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
