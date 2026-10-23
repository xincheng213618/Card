using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class ShenCaoPiChecks
{
    private const string Hero = "anniversary:shen-cao-pi", Driver = "fixture:shen-cp-driver", Pulse = "fixture:shen-cp-pulse",
        Probe = "fixture:shen-cp-probe", Quiet = "fixture:shen-cp-quiet", NoDraw = "fixture:shen-cp-no-draw", Watch = "fixture:shen-cp-watch",
        Ally = "fixture:shen-cp-ally", Enemy = "fixture:shen-cp-enemy", Cao = "fixture:shen-cp-cao", Deck = "fixture:shen-cp-deck", PlayHold = "fixture:shen-cp-play-hold";
    public static void YinzhiNativeHandResponseAndActualTurnRestriction()
    {
        var s = Scene.Start(); s.Play(); var before = s.V(0).HandCount; var dodge = s.V(2).Hand.Single(c => c.Kind == CardKind.Dodge).Id;
        s.Use("anniversary:yinzhi", "edict", [2]); s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch); s.Cold(); s.Skip(); s.Play();
        s.Need(s.V(0).HandCount == before + 1 && s.Limit(2) == s.V(2).Hp && s.E<YinzhiIssuedEvent>() is [{ Count: 1, TargetSeat: 2 }],
            "Yinzhi's real first draw returns before its nonlocked-skill suppression is issued.");
        var slash = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([2]));
        before = s.V(0).HandCount;
        s.Accept(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, s.G.Revision, s.P!.PromptId, slash.PlayedCardKind)); s.Play();
        s.Need(s.V(0).HandCount == before && s.E<YinzhiHandRewardRequestedEvent>().Length == 1 &&
            s.G.CardMovements.Any(m => m.CardId == dodge && m.From == CardLocation.Hand(2) && m.To == CardLocation.Processing),
            "The defender's genuine physical Dodge is one completed hand response and pays one native draw, with no second response-use reward.");
        var old = s.V(2).Hand.Select(c => c.Id).ToArray(); s.End();
        s.Reach(() => s.P is { PlayerSeat: 2 } && s.P.SkillPrompt?.SkillId == PlayHold);
        var actions = s.Actions(2); var newCards = s.G.CardMovements.Where(m => m.To == CardLocation.Hand(2) && m.From == CardLocation.DrawPile && m.TurnNumber == s.G.State.TurnNumber).Select(m => m.CardId).ToHashSet();
        s.Need(s.Limit(2) == 0 && old.Length > 0 && newCards.Count == 2 && !actions.Any(a => a.CardId is { } id && old.Contains(id)) &&
            actions.Any(a => a.CardId is { } id && newCards.Contains(id) && a.Kind == LegalActionKind.Slash),
            "The target's real own turn restores its printed modifier but permits only entities actually gained in that turn.");
        s.Cold();
        s.Answer(_ => true);
        s.Reach(() => s.E<YinzhiTurnRestrictionReleasedEvent>().Length == 1);
        s.Need(s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 2 && e.TargetSeat == 0) && s.Actions(2).Any(a => a.CardId is { } id && old.Contains(id)),
            "The actual target-to-Cao-Pi damage releases only this issued own-turn card restriction.");
        s.Replay();
    }
    private static void CheckNativeCixiongCompletion()
    {
        var native = Scene.Start(nativeEquipment: true); native.Play();
        var weapon = native.V(0).Hand.Single(c => c.Kind == CardKind.CixiongDoubleSwords).Id;
        native.Accept(new UseProgramSkillCommand(0, Driver, "arm-target", [weapon], [1], native.G.Revision, native.P!.PromptId)); native.Play();
        native.Use(Driver, "damage", [1]); native.Play(); native.End();
        native.Reach(() => native.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "keming-echo") == true);
        var echo = native.G.ResolutionStack.OfType<KemingSkillEchoFrame>().Single(); var hand = native.V(0).HandCount;
        native.Need(echo.Return.Kind == KemingEchoReturnKind.CixiongCompletion && echo.Capture.Names is [{ IsDraw: true, Name: CardKind.Slash }] &&
            native.E<CixiongDoubleSwordsResolvedEvent>().Length == 0, "The actual paid Cixiong draw suspends its original native completion in a typed echo return.");
        native.Cold(); native.Answer(c => c.Parameters.GetValueOrDefault("accept") == "yes");
        native.Reach(() => native.E<CixiongDoubleSwordsResolvedEvent>().Length == 1);
        native.Need(native.V(0).HandCount == hand + 1 && native.E<CixiongDoubleSwordsResolvedEvent>() is [{ Activated: true, SourceDrawCount: 1 }] &&
            native.E<KemingEchoDrawPaidEvent>() is [{ Actual: 1 }], "The original Cixiong completion returns once after the real same-name echo without repeating its source draw.");
        native.Replay();
    }
    public static void KemingBorrowsNativeDamageAndEchoesSameNamesCold()
    {
        var s = Scene.Start(borrowScenario: true); s.Play(); var duel = s.V(0).Hand.Single(c => c.Kind == CardKind.Duel).Id;
        s.Accept(new PlayCardCommand(0, duel, [2], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        s.Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary:keming"); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "keming-borrow") == true);
        s.Cold(); s.Answer(_ => true);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "standard:jianxiong"); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); s.Play();
        var receipt = s.E<KemingBorrowIssuedEvent>().Single();
        s.Need(receipt.Receipt.Selected.SkillId == "standard:jianxiong" && s.V(0).Hand.Any(c => c.Id == duel) && !s.V(0).Skills!.Any(k => k.ContentId == "standard:jianxiong") &&
            s.G.CardMovements.Any(m => m.CardId == duel && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0)),
            "The original native damage window borrows Jianxiong once and gains the real Duel entity, without permanently granting its skill.");
        s.Use(Driver, "damage", [1]); s.Play(); s.Need(s.E<KemingEchoArmedEvent>() is [{ TargetSeat: 1 }], "A real own-to-other damage arms the next qualifying skill invocation."); s.End();
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "keming-echo") == true);
        var f = s.G.ResolutionStack.OfType<KemingSkillEchoFrame>().Single(); var before = s.V(0).HandCount;
        s.Need(f.Capture.SkillKey == Pulse && f.Capture.Names.Count == 5 && f.Capture.Names.All(n => n.IsDraw && n.Name == CardKind.Slash) &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(p => p.Id == f.ParentFrameId && p.SkillId == Pulse) && s.G.CreateSnapshot(1).PendingDecision is null,
            "The completed real six-card skill owns a frozen private five-name receipt and a recipient-only echo choice on its original parent.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("accept") == "yes");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch); s.Cold(); s.Skip();
        s.Reach(() => s.E<KemingEchoFinishedEvent>().Length == 1);
        s.Need(s.V(0).HandCount == before + 5 && s.E<KemingEchoDrawPaidEvent>() is [{ Requested: 5, Actual: 5 }] && s.E<KemingEchoReservedEvent>().Length == 1 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.anniversary:keming.same-name-draw") == 5,
            "Keming draws five matching real deck entities through one native draw return, after cold restoration before choice and payment.");
        s.Replay();
        CheckNativeCixiongCompletion();
    }
    private sealed class Fixture(bool nativeEquipment = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shen-cp", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            b.AddCard(new("fixture:shen-cp-cixiong", "雌雄双股剑", "装备牌", "攻击范围2，使用杀指定异性角色为目标后，其弃一张手牌或令你摸一张牌。", CardKind.CixiongDoubleSwords));
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0},{"id":"range","query":"attackRange","operation":"add","value":10,"priority":0}],"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"arm-target","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"cost"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"cost"}]}]},
              {"id":"{{Pulse}}","revision":1,"triggers":[{"id":"draw-six","window":"playPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":6}]}]},
              {"id":"{{Probe}}","revision":1,"modifiers":[{"id":"empty","query":"handLimit","operation":"set","value":0,"priority":0},{"id":"range","query":"attackRange","operation":"add","value":10,"priority":0},{"id":"unlimited","query":"slashLimit","operation":"unlimited","value":0,"priority":0}]},
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{PlayHold}}","revision":1,"triggers":[{"id":"hold","isSkillInvocation":false,"window":"playPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hold","options":[{"id":"continue"}]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"watch","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}]}
            """;
            var labels = new[] { Driver, Pulse, Probe, Quiet, NoDraw, Watch, PlayHold }.ToDictionary(id => id, id => new { name = id, description = "神曹丕真实原生小局", optionLabels = id == PlayHold ? new Dictionary<string, string> { ["continue"] = "继续原生出牌阶段" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id], Tags = id is NoDraw or Quiet or PlayHold ? SkillTag.Locked : SkillTag.None });
            b.AddGeneral(new(Ally, "实际技能来源", "", nativeEquipment ? Probe : Pulse, "wei", 12, Gender: GeneralGender.Male));
            b.AddGeneral(new(Cao, "原生奸雄来源", "", "standard:jianxiong", "wei", 12, [Probe, PlayHold], nativeEquipment ? GeneralGender.Female : GeneralGender.Male) { CharacterId = "character:cao-cao", RulesetId = "sanguosha-anniversary" });
            b.AddGeneral(new(Enemy, "安静同桌", "", Quiet, "wei", 12, Gender: GeneralGender.Female));
            b.AddDeck(new(Deck, "固定同名实体牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "神曹丕真实小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Ally, Cao, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        private System.Collections.IList Players => (System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(G)!;
        public int Limit(int seat) => (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G, [Players[seat]])!;
        public IReadOnlyList<LegalAction> Actions(int seat) => (IReadOnlyList<LegalAction>)typeof(GameEngine).GetMethod("BuildLegalActions", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G, [Players[seat], true, null])!;
        public void Use(string skill, string activation, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, skill, activation, [], targets, G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 240; i++)
            {
                if (done()) return;
                if (P?.SkillPrompt?.SkillId == Watch) Skip();
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End();
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Skip();
                else if (P is null || P.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else throw new InvalidOperationException("Unexpected Shen Cao Pi boundary " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Bounded Shen Cao Pi occurrence not reached " + JsonSerializer.Serialize(P));
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray(), Frames = G.ResolutionStack, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Moves = G.CardMovements });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold restoration preserves the original source skill, borrowed candidate and private draw names."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "Accepted-command replay preserves original damage, reserved invocation and matching entity payments."); }
        public void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected command."); }
        public void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.CurrentSeat, G.State.TurnNumber, P, Facts = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool nativeEquipment = false, bool borrowScenario = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(nativeEquipment));
            var setup = new ShanheBattleSetup(37, 1, "anniversary-cp", Hero, 4, 0, 0, 0, 0, [Driver, NoDraw, Watch],
                nativeEquipment ? [new(CardKind.CixiongDoubleSwords, Suit.Spade, 2)] : [new(CardKind.Slash, Suit.Heart, 8), new(CardKind.Duel, Suit.Spade, 1)], [],
                [new(Cao, Hp: borrowScenario ? 1 : null) { StartingHand = nativeEquipment ? [] : [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Slash, Suit.Spade, 3)] }, new(Enemy) { StartingHand = [] }],
                [new(Ally, ExtraSkillIds: [NoDraw]) { StartingHand = nativeEquipment ? [new(CardKind.Slash, Suit.Spade, 7)] : [] }], new string('e', 64)) { DefaultGodFactionId = "wei" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
