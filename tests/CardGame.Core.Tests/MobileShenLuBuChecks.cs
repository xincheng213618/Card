using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MobileShenLuBuChecks
{
    private const string Hero = "mobile:shen-lu-bu", Wuqian = "mobile-god:wuqian", Wumou = "mobile-god:wumou",
        Peer = "fixture:mobile-lb-peer", Quiet = "fixture:mobile-lb-quiet", NoDraw = "fixture:mobile-lb-no-draw",
        Keep = "fixture:mobile-lb-keep", GainHold = "fixture:mobile-lb-gain", Deck = "fixture:mobile-lb-deck";
    public static void PrintedTrickTargetsAndWholeUseWuqianAcrossTurns()
    {
        var s = Scene.Start(); s.Play();
        var hand = s.V(0).Hand.ToArray();
        var arrows = hand.Single(c => c.Kind == CardKind.ArrowBarrage).Id;
        var draw = hand.Single(c => c.Kind == CardKind.DrawTwo).Id;
        var chain = hand.Single(c => c.Kind == CardKind.IronChain).Id;
        var counter = hand.Single(c => c.Kind == CardKind.Nullification).Id;
        var actions = s.G.GetHumanLegalActions();
        s.Need(s.G.GetCombatDistance(0, 2) == 2 && s.G.GetAttackRange(0) == 1 &&
            actions.Any(a => a.CardId == arrows && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1, 2, 3])) &&
            actions.Where(a => a.CardId == draw).All(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([0])) &&
            actions.Any(a => a.CardId == draw) && actions.Any(a => a.CardId == chain && a.TargetSeats.SequenceEqual([0, 2])) &&
            actions.Any(a => a.CardId == chain && a.TargetSeats.SequenceEqual([2])) && actions.All(a => a.CardId != counter),
            "Wumou preserves the actual printed AOE, self-use and linked-target templates, including a target outside Slash range; Nullification acquires no invented play target.");
        s.Issue(2); s.Play();
        var issue1 = s.E<MobileLuBuWuqianIssuedEvent>().Single();
        s.Need(s.Rage == 0 && s.V(0).Skills!.Count(k => k.Id == "classic:wushuang") == 1,
            "The real two-Rage cost grants the canonical Wushuang binding once.");
        var hp = Enumerable.Range(1, 3).Select(i => s.V(i).Hp).ToArray();
        s.Use(arrows, [1, 2, 3]);
        s.Reach(() => s.P is { PlayerSeat: 2, Kind: DecisionKind.RespondDodge });
        var whole = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.MobileLuBuUse is not null);
        s.Need(whole.TargetSeats.SequenceEqual([1, 2, 3]) && whole.CardKind == CardKind.Slash &&
            s.E<CardUseDeclaredEvent>().Single(e => e.ResolutionId == whole.Id).PublicCard?.Kind == CardKind.ArrowBarrage,
            "The accepted multi-target action is one real effective Slash Use with its original publicly paid ordinary-trick material.");
        s.Step();
        s.Reach(() => s.P is { PlayerSeat: 2, Kind: DecisionKind.RespondDodge } &&
            s.E<CardActionAcceptedEvent>().Count(e => e.Action.ActorSeat == 2 && e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Dodge) == 1);
        s.Cold(); s.Step(); s.Play();
        s.Need(s.V(1).Hp == hp[0] - 1 && s.V(2).Hp == hp[1] && s.V(3).Hp == hp[2] - 1 && s.Rage == 2 &&
            !s.E<MobileLuBuWuqianEndedEvent>().Any() && s.V(2).HandCount == 2 &&
            s.G.CardMovements.Count(m => m.CardId == arrows && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
            "The distant target pays two real Dodges while the other two targets take damage; a partially dodged whole Use preserves Wuqian and cold return does not repay its material.");
        s.Issue(3); s.Play(); var issues = s.E<MobileLuBuWuqianIssuedEvent>();
        s.Need(issues.Length == 2 && s.Rage == 0 && s.V(0).Skills!.Count(k => k.Id == "classic:wushuang") == 1 &&
            s.G.GetHumanLegalActions().Any(a => a.CardId == draw),
            "Wuqian can really be invoked again in the same Play phase; distinct affected targets increase Slash quota without duplicating Wushuang.");
        s.End(); s.Play();
        s.Need(s.G.State.TurnNumber == 5 && !s.E<MobileLuBuWuqianEndedEvent>().Any() &&
            s.V(0).Skills!.Any(k => k.Id == "classic:wushuang") && !s.G.CardMovements.Any(m => m.Reason.Value == "skill.mobile-god.wuqian.damage-card"),
            "Issued effects survive the actual round boundary; a current ordinary trick is an effective damage card and prevents the ending-stage fallback gain.");
        var ownerHp = s.V(0).Hp;
        s.Use(draw, [0]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); s.AnswerDodge();
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); s.AnswerDodge(); s.Play();
        var ended = s.E<MobileLuBuWuqianEndedEvent>();
        s.Need(s.V(0).Hp == ownerHp && ended.Length == 2 && ended.Select(e => e.FrameId).ToHashSet().SetEquals(issues.Select(i => i.FrameId)) &&
            ended.Select(e => e.UseFrameId).Distinct().Count() == 1 && !s.V(0).Skills!.Any(k => k.Id == "classic:wushuang") &&
            s.E<MobileLuBuWushuangRevokedEvent>().Count(e => e.GrantId == issue1.GrantId) == 1 &&
            s.E<MobileLuBuWushuangRevokedEvent>().Length == 2,
            "Only completion of the next whole damage-card Use with zero damage retires both original issues and their own grants, after both self-targeted native Dodges.");
    }
    public static void EndingDamageCardGainKeepsOriginalPaidFrameCold()
    {
        var s = Scene.Start(gainOnly: true); s.Play(); var before = s.V(0).HandCount; s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == GainHold);
        var frame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MobileLuBu is not null);
        var receipt = frame.MobileLuBu!; var id = receipt.Cards.Single();
        s.Need(receipt.Op == SkillProgramEffectOp.GainMobileShenLuBuDamageCard && frame.WindowContext?.Window == SkillProgramTriggerWindow.TurnEnding &&
            s.V(0).HandCount == before + 1 && s.V(0).Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash) &&
            s.G.CardMovements.Count(m => m.CardId == id && m.Sequence > receipt.Before && m.Sequence <= receipt.After && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(0) && m.Reason.Value == "skill.mobile-god.wuqian.damage-card") == 1,
            "The ending fallback selects an actual damage card and commits one native obtain before pausing on its original paid program frame.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        s.Need(s.G.State.TurnNumber == 5 && s.V(0).HandCount == before + 1 && s.V(0).Hand.Any(c => c.Id == id) &&
            s.G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill.mobile-god.wuqian.damage-card") == 1 &&
            !s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == frame.Id),
            "Cold return consumes the exact obtain child once and resumes the real ending stage without drawing or selecting again.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mobile-shen-lu-bu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero, "classic:lu-bu");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Keep}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}]},
              {"id":"{{GainHold}}","revision":1,"triggers":[{"id":"gain","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-god.wuqian.damage-card"],"optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}
            ]}
            """;
            var labels = new[] { NoDraw, Quiet, Keep, GainHold }.ToDictionary(id => id, id => new { name = id, description = "移动版神吕布真实原生用牌小局" });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "保留原生响应的角色", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定伤害牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动版神吕布小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Rage => V(0).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.Rage)?.Count ?? 0;
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Step() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Issue(int target) => Accept(new UseProgramSkillCommand(0, Wuqian, "rage-attack", [], [target], G.Revision, P!.PromptId));
        public void Use(int id, IReadOnlyList<int> targets)
        {
            var a = G.GetHumanLegalActions().First(a => a.CardId == id && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual(targets));
            Need(a.ConversionSource?.SkillId == Wumou, "The real action must retain the forced identity source.");
            Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
                { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void AnswerDodge() => Answer(c => c.Parameters.GetValueOrDefault("response") == "dodge");
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected mobile Lu Bu boundary " + JsonSerializer.Serialize(P));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded actual mobile Lu Bu occurrence was not reached " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))), "Cold restore preserves the exact original Use or paid gain child.");
        }
        public void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected mobile Lu Bu command."); }
        public void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Facts = G.Events.TakeLast(5).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool gainOnly = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            IReadOnlyList<ShanheStartingCard> cards = gainOnly ? [new(CardKind.Dodge, Suit.Heart, 7)] :
                [new(CardKind.ArrowBarrage, Suit.Heart, 1), new(CardKind.DrawTwo, Suit.Heart, 7), new(CardKind.IronChain, Suit.Club, 10),
                 new(CardKind.Nullification, Suit.Club, 12), new(CardKind.Dodge, Suit.Heart, 7), new(CardKind.Dodge, Suit.Heart, 7)];
            var setup = new ShanheBattleSetup(37, 1, "mobile-lb", Hero, 5, 0, 0, 0, 0, gainOnly ? [NoDraw, Keep, GainHold] : [NoDraw, Keep], cards, [],
                [new(Peer, ExtraSkillIds: [NoDraw, Keep]) { StartingHand = Enumerable.Range(0, 4).Select(_ => new ShanheStartingCard(CardKind.Dodge, Suit.Heart, 7)).ToArray() },
                 new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }],
                [new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], new string('a', 64)) { DefaultGodFactionId = "qun" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var scene = new Scene(g, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
}
