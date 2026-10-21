using System.Text.Json;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MobileShenHuaTuoChecks
{
    private const string Hero = "mobile:shen-hua-tuo", Ally = "fixture:mobile-ht-ally", Enemy = "fixture:mobile-ht-enemy",
        Driver = "fixture:mobile-ht-driver", Quiet = "fixture:mobile-ht-quiet", NoDraw = "fixture:mobile-ht-no-draw",
        DrawHold = "fixture:mobile-ht-draw-hold", HpHold = "fixture:mobile-ht-hp-hold", Keep = "fixture:mobile-ht-keep", Deck = "fixture:mobile-ht-deck";
    public static void WulingOrdersEntriesEquipmentAndNativeDrawCold()
    {
        var s = Scene.Start(); s.Play();
        var weapon = s.V(0).Hand.Single(c => c.Kind == CardKind.Crossbow).Id;
        s.Accept(new UseProgramSkillCommand(0, Driver, "arm", [weapon], [2], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Use("mobile-god:wuling", "teach-five-spirits");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "recipient:0") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "recipient:0");
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "animal:1");
        var pending = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MobileHuaTuo is not null);
        s.Need(pending.MobileHuaTuo!.Order.SequenceEqual([MobileWulingAnimal.Crane]) && s.G.CreateSnapshot(1).PendingDecision is null,
            "The order is a private accepted choice on its original program; another player cannot inspect future animals.");
        s.Cold();
        foreach (var a in new[] { MobileWulingAnimal.Ape, MobileWulingAnimal.Deer, MobileWulingAnimal.Tiger, MobileWulingAnimal.Bear })
            s.Answer(c => c.Parameters.GetValueOrDefault("option") == "animal:" + (int)a);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == DrawHold);
        var invocation = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single();
        s.Need(invocation.Context.RequestedCount == 3 && invocation.Context.RecipientSeat == 0 && s.Mark(0, PlayerMarkerKind.MobileWulingCrane) == 1,
            "Crane immediately grants its real mark and owns a native three-card draw request.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        var taught = s.E<MobileWulingTaughtEvent>().Single();
        s.Need(s.E<MobileHuaTuoPaidEvent>().Count(e => e.FrameId == taught.CourseId && e.Kind == "crane") == 1 && s.V(0).HandCount == 5,
            "Cold return pays the immediate three-card draw once.");
        s.Teach(1, [MobileWulingAnimal.Bear, MobileWulingAnimal.Tiger, MobileWulingAnimal.Deer, MobileWulingAnimal.Ape, MobileWulingAnimal.Crane]);
        var hp = s.V(1).Hp; s.Use(Driver, "damage", [1]); s.Play(); s.Use(Driver, "damage", [1]); s.Play();
        s.Need(s.Mark(1, PlayerMarkerKind.MobileWulingBear) == 1 && s.V(1).Hp == hp - 1 &&
            !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == "mobile-god:wuling"),
            "The first actual damage is reduced, the second is not, and the two accepted teachings spend the phase limit.");
        s.End(); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option")?.StartsWith("ape:", StringComparison.Ordinal) == true) == true && s.P.PlayerSeat == 0);
        var equipment = s.V(2).Equipment.Single().Id;
        s.Answer(c => c.Cards.SequenceEqual([equipment])); s.Play();
        s.Need(s.Mark(0, PlayerMarkerKind.MobileWulingApe) == 1 && s.V(0).Hand.Any(c => c.Id == equipment) && s.V(2).Equipment.Count == 0 &&
            s.G.CardMovements.Any(m => m.CardId == equipment && m.From == CardLocation.Equipment(2) && m.To == CardLocation.Hand(0)),
            "Actual preparation advances the private persistent course and Ape transfers the selected public equipment entity.");
        s.End(); s.Play();
        s.Need(s.Mark(0, PlayerMarkerKind.MobileWulingDeer) == 1 && s.V(0).Hp == 3 && s.V(0).Skills!.Any(k => k.ContentId == "mobile-god:wuling-deer"),
            "The next actual preparation grants Deer, performs recovery and enables the delayed-target prohibition.");
        s.End(); s.Play();
        s.Need(s.Mark(0, PlayerMarkerKind.MobileWulingTiger) == 1 && !s.V(0).Skills!.Any(k => k.ContentId == "mobile-god:wuling-deer"),
            "Changing to Tiger really revokes the expired Deer policy.");
        var before = s.V(1).Hp;
        var slash = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1]));
        s.Accept(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, s.G.Revision, s.P!.PromptId, slash.PlayedCardKind)); s.Play();
        s.Need(s.V(1).Hp == before - 2 && s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 2),
            "Tiger adds one to an actual physical card's sole designated target.");
    }
    public static void YouyiSharedRenPaysWholeAreaBeforeRecoveryCold()
    {
        var s = Scene.Start(ren: true); s.Play(); s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "mobile-god:youyi");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); s.Play();
        var first = s.G.CreateSnapshot(0).BenevolenceCards!.Select(c => c.Id).ToArray();
        s.Need(first.Length == 6 && Enumerable.Range(0, 4).All(v => s.G.CreateSnapshot(v).BenevolenceCards!.Select(c => c.Id).SequenceEqual(first)),
            "Only this actually executed discard phase's six real discarded entities enter the common face-up Ren area.");
        s.Use(Driver, "draw"); s.Play(); s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == "mobile-god:youyi");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); s.Play();
        var ren = s.G.CreateSnapshot(0).BenevolenceCards!.Select(c => c.Id).ToArray();
        s.Need(ren.Length == 6 && first.Take(3).All(id => s.G.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Benevolence &&
            m.To == CardLocation.DiscardPile && m.Reason.Value == "benevolence.overflow")),
            "Overflow moves the oldest real Ren entities into the discard pile, while the public area retains six.");
        var hand = s.V(0).HandCount;
        s.Use("mobile-god:youyi", "clear-ren-and-heal"); s.Reach(() => s.P?.SkillPrompt?.SkillId == HpHold);
        var owner = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MobileHuaTuo?.Op == SkillProgramEffectOp.RecoverMobileShenHuaTuoYouyi);
        s.Need(s.G.CreateSnapshot(0).BenevolenceCards is { Count: 0 } && owner.MobileHuaTuo!.Cards.SequenceEqual(ren) &&
            ren.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Benevolence && m.To == CardLocation.DiscardPile &&
                m.Reason.Value == "skill.mobile-shen-hua-tuo.youyi.pay") == 1),
            "The original owner freezes and pays the entire actual shared area before the first recovery observer.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        s.Need(s.V(0).Hp == 3 && s.V(1).Hp == 9 && s.V(2).Hp == 9 && s.V(3).Hp == 9 && s.V(0).HandCount == hand &&
            ren.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Benevolence && m.To == CardLocation.DiscardPile &&
                m.Reason.Value == "skill.mobile-shen-hua-tuo.youyi.pay") == 1),
            "A cold HP child return heals every frozen living participant once and never repeats the shared payment.");
    }
    private sealed class Fixture : IContentPackage
    {
        public string PackageId => "fixture-mobile-shen-hua-tuo"; public string Version => "1";
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"arm","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"weapon"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"weapon"}]},
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]}]},
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Keep}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}]},
              {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"hold","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{HpHold}}","revision":1,"triggers":[{"id":"hold","isSkillInvocation":false,"window":"afterHpRecovered","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}]}
            """;
            var labels = new[] { Driver, NoDraw, Quiet, Keep, DrawHold, HpHold }.ToDictionary(id => id, id => new { name = id, description = "移动神华佗真实引擎小夹具" });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Ally, "受教角色", "", Quiet, "wei", 12)); b.AddGeneral(new(Enemy, "其他角色", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 120).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动神华佗小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Ally, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Mark(int seat, PlayerMarkerKind kind) => V(seat).Markers?.FirstOrDefault(m => m.Kind == kind)?.Count ?? 0;
        public void Use(string skill, string id, IReadOnlyList<int>? targets = null) => Accept(new UseProgramSkillCommand(0, skill, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Teach(int seat, IReadOnlyList<MobileWulingAnimal> animals)
        {
            Use("mobile-god:wuling", "teach-five-spirits"); Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "recipient:" + seat) == true);
            Answer(c => c.Parameters.GetValueOrDefault("option") == "recipient:" + seat);
            foreach (var a in animals) Answer(c => c.Parameters.GetValueOrDefault("option") == "animal:" + (int)a); Play();
        }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 240; i++)
            {
                if (done()) return;
                if (P?.SkillPrompt?.SkillId is DrawHold or HpHold) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End();
                else if (P is null || P.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else throw new InvalidOperationException("Unexpected mobile Hua Tuo boundary " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Bounded mobile Hua Tuo occurrence not reached " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))), "Cold restore preserves the owning pending order, shared Ren entities and native cursor.");
        }
        public void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected command."); }
        public void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Facts = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool ren = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var extra = new[] { Driver, NoDraw, ren ? HpHold : Keep, DrawHold };
            var hand = Enumerable.Range(0, ren ? 8 : 2).Select(_ => new ShanheStartingCard(CardKind.Slash, Suit.Spade, 7))
                .Concat(ren ? [] : new[] { new ShanheStartingCard(CardKind.Crossbow, Suit.Club, 1) }).ToArray();
            var setup = new ShanheBattleSetup(37, 1, "mobile-ht", Hero, 2, 0, 0, 0, 0, extra, hand, [],
                [new(Enemy, Hp: 8, ExtraSkillIds: [NoDraw]) { StartingHand = [] }, new(Enemy, Hp: 8, ExtraSkillIds: [NoDraw]) { StartingHand = [] }],
                [new(Ally, Hp: 8, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], new string('c', 64)) { DefaultGodFactionId = "wu" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 28 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand());
            return s;
        }
    }
}
