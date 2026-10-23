using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MobileShenSimaYiChecks
{
    private const string Hero = "mobile:shen-sima-yi", Jilue = "mobile-god:jilue", Lianpo = "mobile-god:lianpo",
        Peer = "fixture:mobile-sima-peer", Caster = "fixture:mobile-sima-caster", Quiet = "fixture:mobile-sima-quiet", NoDraw = "fixture:mobile-sima-no-draw",
        Keep = "fixture:mobile-sima-keep", Hold = "fixture:mobile-sima-hold", SeedRen = "fixture:mobile-sima-seed",
        Tricker = "fixture:mobile-sima-tricker", Driver = "fixture:mobile-sima-driver", Deck = "fixture:mobile-sima-deck";
    public static void UnansweredNativeWindowsRoundCapAndFactionAwakeningCold()
    {
        var s = Scene.Start(); s.Play(); s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold && s.G.ResolutionStack.OfType<MobileSimaRenjieFrame>().Any());
        var f = s.G.ResolutionStack.OfType<MobileSimaRenjieFrame>().Single();
        var counter = s.G.ResolutionStack.OfType<NullificationWindowFrame>().Single(n => n.Id == f.ParentFrameId);
        s.Need(f.ReturnKind == MobileSimaRenjieReturnKind.Counterspell && counter.CandidateSeats[counter.CandidateIndex] == 0 &&
            counter.SourceSeat == 3 && !f.Applied && s.Ren == 0 && s.G.ResolutionStack.OfType<SkillInvocationFrame>().Any(i => i.ParentFrameId == f.Id),
            "The real no-card counterspell opportunity suspends before granting Ren, on its exact native candidate and unified invocation child.");
        s.Cold(); s.Step();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Jilue);
        s.Need(s.G.State.TurnNumber == 5 && s.Ren == 4 && s.V(0).MaxHp == 3 && s.V(0).Hp == 3 &&
            s.E<CardUseDeclaredEvent>().Count(e => e.SourceSeat == 3 && e.CardKind == CardKind.DrawTwo) == 4 &&
            s.E<CardUseDeclaredEvent>().Count(e => e.SourceSeat == 3 && e.CardKind == CardKind.ArrowBarrage) == 1 &&
            s.E<MobileSimaRenjieAppliedEvent>().Count(e => e.OwnerSeat == 0 && e.RoundNumber == 1 && e.Gained) == 4 &&
            s.E<MobileSimaRenjieStartedEvent>().Count(e => e.Source.OwnerSeat == 0 && e.RoundNumber == 1 && e.ReturnKind == MobileSimaRenjieReturnKind.Counterspell) == 3 &&
            s.E<MobileSimaRenjieStartedEvent>().Count(e => e.Source.OwnerSeat == 0 && e.RoundNumber == 1 && e.ReturnKind == MobileSimaRenjieReturnKind.FailedAttack) == 1 &&
            s.E<MobileSimaRenjieAppliedEvent>().Count(e => e.FrameId == f.Id) == 1 &&
            s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 0 && e.SourceSeat == 3 && e.Amount == 1) == 1 &&
            s.Has("classic:guicai") && s.Has("classic:jizhi") && !s.Has("classic:fangzhu") &&
            s.E<MobileSimaJilueInitializedEvent>().Single(e => e.OwnerSeat == 0).FactionId == "shu",
            "Three unanswered counterspell opportunities and the actual unanswered Arrow defense fill the four-Ren round quota before the last two tricks; cold never repeats the first grant, and Baiyin grants the Shu skill plus Guicai at reduced maximum HP.");
        s.ActivateJilue(); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "learn:classic:zhiheng"); s.Play();
        s.Need(s.Ren == 2 && s.Has("classic:zhiheng") && s.E<MobileSimaJilueChoiceEvent>().Single(e => e.Source.SkillId == Jilue).Paid == 2,
            "The first real Play-start learning pays two source-attributed Ren and permanently acquires the selected canonical skill.");
    }
    public static void EscalatingPermanentLearningKillChoiceAndPaidDrawCold()
    {
        var s = Scene.Start(killCase: true);
        foreach (var skill in new[] { "classic:zhiheng", "classic:fangzhu", "classic:wansha" })
        {
            s.Reach(() => s.P?.SkillPrompt?.SkillId == Jilue); s.ActivateJilue();
            s.Answer(c => c.Parameters.GetValueOrDefault("option") == "learn:" + skill); s.Play();
            if (skill != "classic:wansha") { s.UseDriver("next-turn", []); s.Play(); s.End(); }
        }
        var learns = s.E<MobileSimaJilueChoiceEvent>().Where(e => e.Source.SkillId == Jilue).ToArray();
        s.Need(learns.Select(e => e.Paid).SequenceEqual([2, 2, 3]) && s.Ren == 2 && s.G.State.TurnNumber == 3 &&
            new[] { "classic:guicai", "classic:jizhi", "classic:zhiheng", "classic:fangzhu", "classic:wansha" }.All(s.Has),
            "Three actual Play starts pay 2/2/3 and preserve all previously acquired skills across actual extra turns.");
        s.UseDriver("kill", [1]); s.Reach(() => s.P?.SkillPrompt?.SkillId == Lianpo);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "extra-turn"); s.Play();
        s.UseDriver("kill", [2]); s.Play();
        s.Need(!s.V(1).IsAlive && !s.V(2).IsAlive && s.V(3).IsAlive &&
            s.E<MobileSimaJilueChoiceEvent>().Count(e => e.Source.SkillId == Lianpo && e.Choice == "extra-turn" && e.ActualTurn == 3) == 1 &&
            s.E<ProgramExtraTurnPendedEvent>().Count(e => e.SkillId == Lianpo) == 1,
            "Two real kills in one actual turn reserve only one Lianpo extra turn; with all five skills already owned the second kill creates no false learning option.");
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == Jilue); s.ActivateJilue();
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "draw:2");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(p => p.MobileSimaYi?.Stage == MobileSimaYiStage.Drawing));
        var draw = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(p => p.MobileSimaYi?.Stage == MobileSimaYiStage.Drawing);
        s.Need(s.G.State.TurnNumber == 4 && s.Ren == 0 && draw.MobileSimaYi!.Choice == "draw:2" &&
            s.E<MobileSimaJilueChoiceEvent>().Count(e => e.FrameId == draw.Id && e.Paid == 2) == 1,
            "Lianpo really starts the next own turn, and Jilue pays its two Ren before opening the native draw child.");
        var hand = s.V(0).HandCount; s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        s.Need(s.Ren == 0 && s.V(0).HandCount == hand + 2 && s.Has("classic:zhiheng") && s.Has("classic:wansha") &&
            s.E<MobileSimaJilueDrawReturnedEvent>().Count(e => e.FrameId == draw.Id && e.Requested == 2 && e.Actual == 2) == 1 &&
            s.E<MobileSimaJilueChoiceEvent>().Count(e => e.FrameId == draw.Id) == 1,
            "Cold return draws exactly twice from the one paid producer, never repays Ren, and keeps the permanent acquired skills.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mobile-shen-sima-yi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero, "anniversary:shen-hua-tuo", "classic:sima-yi", "classic:cao-pi", "classic:huang-yueying", "classic:sun-quan", "classic:jia-xu");
            var spell = "{\"op\":\"useVirtualOrdinaryTrick\",\"target\":\"owner\",\"actorRef\":{\"kind\":\"owner\"},\"targetRef\":{\"kind\":\"owner\"},\"outputKind\":\"drawTwo\"}";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Keep}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}]},
              {"id":"{{Hold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{SeedRen}}","revision":1,"triggers":[{"id":"seed","isSkillInvocation":false,"window":"gameStarting","subject":"owner","optional":false,"priority":100,"effects":[{"op":"changeAttributedMarker","target":"owner","targetRef":{"kind":"owner"},"marker":"ren","amount":9}]}]},
              {"id":"{{Tricker}}","revision":1,"triggers":[
                {"id":"real-arrow","window":"playPhaseStarting","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{{spell.Replace("drawTwo", "arrowBarrage", StringComparison.Ordinal)}}]},
                {"id":"four-real-draws","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{{string.Join(',', Enumerable.Repeat(spell, 4))}}]}]},
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"next-turn","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]}]}
            ]}
            """;
            var labels = new[] { NoDraw, Quiet, Keep, Hold, SeedRen, Tricker, Driver }.ToDictionary(id => id, id => new { name = id, description = "移动版神司马懿真实原生边界小局" });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "保留真实回合的角色", "", Quiet, "wei", 12));
            b.AddGeneral(new(Caster, "实际进入出牌阶段的角色", "", NoDraw, "wei", 12));
            b.AddDeck(new(Deck, "固定实体牌", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动版神司马懿小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer, Caster], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Ren => V(0).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.Ren)?.Count ?? 0;
        public bool Has(string id) => V(0).Skills?.Any(k => k.Id == id) == true;
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Step() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void UseDriver(string id, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
        public void ActivateJilue() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId is Jilue or Hold) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End();
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected mobile Sima Yi boundary " + JsonSerializer.Serialize(P));
                else Step();
            }
            throw new InvalidOperationException("The bounded actual mobile Sima Yi occurrence was not reached " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))), "Cold restore preserves the exact native response or already-paid original learning frame.");
        }
        public void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected mobile Sima Yi command."); }
        public void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Facts = G.Events.TakeLast(5).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool killCase = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(37, 1, "mobile-sima", Hero, 4, 0, 0, 0, 0,
                killCase ? [NoDraw, Keep, Driver, SeedRen, Hold] : [NoDraw, Keep], [], [],
                [new(Peer, ExtraSkillIds: [NoDraw, Keep]) { StartingHand = [] },
                 new(killCase ? Peer : Caster, ExtraSkillIds: killCase ? [NoDraw, Keep] : [Keep, Tricker]) { StartingHand = [] }],
                [new(Peer, ExtraSkillIds: killCase ? [NoDraw] : [NoDraw, Hold, "anniversary-god:jingyu"]) { StartingHand = [] }],
                new string('c', 64)) { DefaultGodFactionId = "shu" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var scene = new Scene(g, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
}
