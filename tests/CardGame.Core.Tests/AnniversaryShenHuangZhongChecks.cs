using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class AnniversaryShenHuangZhongChecks
{
    private const string Hero = "anniversary:shen-huang-zhong", Ally = "fixture:anniversary-hz-ally", Enemy = "fixture:anniversary-hz-enemy",
        Driver = "fixture:anniversary-hz-driver", Quiet = "fixture:anniversary-hz-quiet", NoDraw = "fixture:anniversary-hz-no-draw",
        Keep = "fixture:anniversary-hz-keep", DiscardHold = "fixture:anniversary-hz-discard", DrawHold = "fixture:anniversary-hz-draw", HpHold = "fixture:anniversary-hz-hp", Deck = "fixture:anniversary-hz-deck";
    private const string Lieqiong = "anniversary-god:lieqiong", Zhanjue = "anniversary-god:zhanjue";
    public static void LieqiongPhysicalDiscardAmplificationAndTianchongCold()
    {
        var s = Scene.Start(); s.Play(); var hand = s.V(2).Hand.ToArray();
        s.StartHit(2); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "Lifeng");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == DiscardHold);
        var f = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(p => p.AnniversaryHuangZhong?.Body == AnniversaryHzBody.Lifeng);
        var r = f.AnniversaryHuangZhong!; var ids = r.Cards.ToArray();
        s.Need(ids.Length == 3 && s.V(2).HandCount == 2 && ids.All(id => hand.Any(c => c.Id == id)) &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.ParentFrameId == f.Id && e.ExecutorSeat == 2 && e.ActualCount == 3),
            "Lifeng randomly selects the ceiling of half the actual hand and pays one real victim-owned logical discard before its child.");
        s.Cold(); s.Step(); s.Play();
        s.Need(ids.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.anniversary-huang-zhong.lifeng") == 1), "Cold return never repeats the random discard.");
        s.Hit(2, "Diji"); var issue = s.E<AnniversaryHzBodyIssuedEvent>().Last(e => e.Body == AnniversaryHzBody.Diji);
        var hp = s.V(2).Hp; s.StartHit(2);
        s.Need(s.V(2).Hp == hp - 2 && s.E<AnniversaryHzStatusConsumedEvent>().Count(e => e.Id == issue.Id && e.Reason == "damage") == 1 &&
            s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "Tianchong"), "Diji adds one once to the next actual damage, and another hit this actual turn enables Tianchong.");
        var max = s.V(0).MaxHp; var paidHp = s.V(2).Hp;
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "Tianchong");
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        var t = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(p => p.AnniversaryHuangZhong?.Body == AnniversaryHzBody.Tianchong);
        s.Need(t.AnniversaryHuangZhong is { HpPaid: true, DyingId: > 0 } receipt && receipt.Amount == paidHp &&
            s.E<ProgramSkillHpLostEvent>().Count(e => e.FrameId == t.Id && e.TargetSeat == 2 && e.Amount == paidHp) == 1,
            "Tianchong really loses all remaining HP and owns the exact native dying child.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die"); s.Play();
        s.Need(!s.V(2).IsAlive && s.V(0).MaxHp == max + 1 && s.E<ProgramSkillHpLostEvent>().Count(e => e.FrameId == t.Id) == 1,
            "Only death from that exact paid dying result grants one maximum HP, without repeating HP loss after cold restore.");
    }
    public static void ZhanjueNativeDrawPhaseSlashRecoveryAndWoundControls()
    {
        var s = Scene.Start();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Zhanjue); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "slash");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == DrawHold && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.AnniversaryHuangZhong?.Stage == AnniversaryHzStage.Drawing));
        var draw = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single();
        s.Need(draw.Context.RequestedCount == 2, "Zhanjue draws the actual current HP through the native draw boundary.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        s.Use("incoming", [1]); s.Body("Zhongshu", owner: 1); s.Play();
        var first = s.V(0).Hand.First(c => c.Kind == CardKind.Crossbow).Id; s.Equip(first); s.Play();
        s.Need(s.V(0).Equipment.Count == 0 && s.G.CardMovements.Any(m => m.CardId == first && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.anniversary-huang-zhong.zhongshu.cleanup"),
            "A real incoming wound makes the next native equipment Use ineffective after paying its material; it never installs the weapon.");
        s.Equip(s.V(0).Hand.Single(c => c.Kind == CardKind.Crossbow).Id); s.Play();
        s.Need(s.G.GetCombatDistance(0, 2) > 1 && s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "Non-Slash uses preserve Zhanjue's next-Slash distance privilege.");
        var hp = s.V(2).Hp; var responses = s.E<CardActionAcceptedEvent>().Count(e => e.Action.ActorSeat == 2 && e.Action.EffectiveKind == CardKind.Dodge);
        s.Slash(2); s.Body("Lifeng"); s.Play();
        s.Need(s.V(2).Hp == hp - 1 && s.E<CardActionAcceptedEvent>().Count(e => e.Action.ActorSeat == 2 && e.Action.EffectiveKind == CardKind.Dodge) == responses &&
            !s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "The one issued Slash is unrespondable even with real Dodges, and the consumed distance privilege does not apply to a second Slash.");
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == Zhanjue); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        var before = s.V(0).Hp; s.Answer(c => c.Parameters.GetValueOrDefault("option") == "recover"); s.Play();
        s.Need(s.E<AnniversaryHzZhanjueIssuedEvent>().Last().DrawCount == s.V(0).MaxHp - before, "The second mode draws actual lost HP in this new Play phase.");
        s.Use("damage", [2]); s.Reach(() => s.P?.SkillPrompt?.SkillId == HpHold); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Body("Zhongshu"); s.Play();
        s.Need(s.V(0).Hp == before + 1 && s.E<AnniversaryHzZhanjueRecoveredEvent>().Length == 1, "The first real damage reserves and pays one same-amount recovery across its HP child.");
        hp = s.V(2).Hp; s.Use("forced-slash", [2]); s.Body("Lifeng"); s.Play();
        s.Need(s.V(2).Hp == hp - 1 && s.E<CardActionAcceptedEvent>().Any(e => e.Action.ActorSeat == 2 && e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Dodge) &&
            s.E<AnniversaryHzStatusConsumedEvent>().Any(e => e.Reason == "use" && s.E<AnniversaryHzBodyIssuedEvent>().Any(b => b.Id == e.Id && b.Target == 2 && b.Body == AnniversaryHzBody.Zhongshu)),
            "Zhongshu consumes the victim's real Dodge use and makes it ineffective after the Dodge's actual payment.");
        s.Hit(3, "Qihai"); hp = s.V(3).Hp; s.Use("forced-slash", [3]); s.Body("Diji"); s.Play();
        s.Need(s.V(3).Hp == hp - 1 && s.V(3).Hand.Any(c => c.Kind == CardKind.Dodge && c.Suit == Suit.Heart) &&
            !s.E<CardActionAcceptedEvent>().Any(e => e.Action.ActorSeat == 3 && e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Dodge) && s.V(0).Hp == before + 1,
            "Qihai prevents the only Heart Dodge from being offered or paid, while later damage never repeats the already-used recovery.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-shen-huang-zhong", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"forced-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","useCardActionWindows":true,"targetRestriction":"distanceUnlimitedAgainstTarget"}]}]},
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Keep}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}]},
              {"id":"{{DiscardHold}}","revision":1,"triggers":[{"id":"discard","isSkillInvocation":false,"window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.anniversary-huang-zhong.lifeng"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"discard","options":[{"id":"continue"}]}]}]},
              {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{HpHold}}","revision":1,"triggers":[{"id":"hp","isSkillInvocation":false,"window":"afterHpRecovered","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}]}
            """;
            var labels = new[] { Driver, NoDraw, Quiet, Keep, DiscardHold, DrawHold, HpHold }.ToDictionary(id => id, id => new { name = id, description = "十周年神黄忠真实引擎小局", optionLabels = id == DiscardHold ? new Dictionary<string, string> { ["continue"] = "继续原裂穹" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Ally, "持裂穹的同行", "", Quiet, "wu", 12)); b.AddGeneral(new(Enemy, "真实受击角色", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定实体牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "神黄忠小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Ally, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Use(string id, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Step() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void StartHit(int target) { Use("damage", [target]); Reach(() => P?.SkillPrompt?.SkillId == Lieqiong); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "Lifeng") == true); }
        public void Body(string body, int owner = 0)
        { Reach(() => P?.SkillPrompt?.SkillId == Lieqiong && P.PlayerSeat == owner); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Answer(c => c.Parameters.GetValueOrDefault("option") == body); }
        public void Hit(int target, string body) { StartHit(target); Answer(c => c.Parameters.GetValueOrDefault("option") == body); Play(); }
        public void Equip(int id) { var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && a.CardId == id); Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind)); }
        public void Slash(int target) { var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target])); Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 240; i++)
            {
                if (done()) return;
                if (P?.SkillPrompt?.SkillId is DrawHold or HpHold or Zhanjue) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End();
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else if (P is null || P.PlayerSeat != 0) Step();
                else throw new InvalidOperationException("Unexpected anniversary Huang Zhong boundary " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Bounded anniversary Huang Zhong occurrence not reached " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))), "Cold restore preserves actual chosen wounds, payments and typed children.");
        }
        public void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected command."); }
        public void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Facts = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(37, 1, "anniversary-hz", Hero, 2, 0, 0, 0, 0, [Driver, NoDraw, Keep, DrawHold, HpHold],
                [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.Slash, Suit.Club, 7), new(CardKind.Peach, Suit.Heart, 3)], [],
                [new(Enemy, ExtraSkillIds: [NoDraw, DiscardHold]) { StartingHand = Enumerable.Range(0, 5).Select(_ => new ShanheStartingCard(CardKind.Dodge, Suit.Heart, 7)).ToArray() }, new(Enemy, ExtraSkillIds: [NoDraw]) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 7)] }],
                [new(Ally, ExtraSkillIds: [NoDraw, Lieqiong]) { StartingHand = [] }], new string('e', 64)) { DefaultGodFactionId = "wu" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 28 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
