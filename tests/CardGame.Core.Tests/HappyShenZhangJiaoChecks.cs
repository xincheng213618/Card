using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class HappyShenZhangJiaoChecks
{
    private const string Hero = "happy:shen-zhang-jiao", Yizhao = "happy:yizhao", Sanshou = "happy:sanshou", Sijun = "happy:sijun", Tianjie = "happy:tianjie";
    private const string Driver = "fixture:happy-zj-driver", NoDraw = "fixture:happy-zj-no-draw", Quiet = "fixture:happy-zj-quiet", Hold = "fixture:happy-zj-hold", Deck = "fixture:happy-zj-deck";
    private const string ObtainReason = "skill.anniversary-zhang-jiao.obtain";

    public static void UseOnlyHuangDeckOnlyThirtySixAndFourSkillCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = Start(registry); Play(g);
        Need(V(g).Hp == 3 && V(g).MaxHp == 3 && Mark(g) == 0, "Happy Zhang Jiao really starts at three HP and has no inherited anniversary HP increase.");
        var slash = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId is { } id && V(g).Hand.Single(c => c.Id == id).Rank == 13 && a.TargetSeats.SequenceEqual([3]));
        Accept(g, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, g.Revision, P(g)!.PromptId, slash.PlayedCardKind)); Play(g);
        Need(E<AnniversaryYizhaoAccumulatedEvent>(g) is [{ Source.SkillId: Yizhao, Rank: 13, Before: 0, After: 13, ObtainedCount: 1 }] && Mark(g) == 13,
            "A real rank-thirteen Slash use accumulates Huang and obtains the one real rank-one deck card through the reused source-bound producer.");
        var discarded = V(g).Hand.ToArray(); Use(g, "discard"); Play(g);
        Need(discarded.Where(c => c.Rank == 12).Sum(c => c.Rank) == 36 && discarded.All(c => Zone(g, c.Id) == CardLocation.DiscardPile) &&
            Mark(g) == 13 && E<AnniversaryYizhaoAccumulatedEvent>(g).Length == 1,
            "Real original owned discards include a full rank-sum-36 subset but do not accumulate any Happy Yizhao Huang.");
        var drawIds = g.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.DrawPile).Select(c => c.CardId).ToArray();
        Need(drawIds.Length == 3, "The fixed native deck has exactly three rank-twelve entities left before the next preparation.");
        End(g); Reach(g, () => Offer(P(g), Sijun)); Activate(g);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Hold && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Sijun));
        var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Sijun);
        var paid = frame.AnniversaryZhangJiao!; var selected = paid.Cards.ToArray();
        Need(paid is { Stage: AnniversaryZhangJiaoStage.Obtained, Rank: 36, MarkerBefore: 13, HappySijunPayment: not null } &&
            Mark(g) == 0 && selected.Order().SequenceEqual(drawIds.Order()) && selected.All(id => discarded.All(c => c.Id != id)) &&
            paid.HappySijunPayment.Movements.Count == 3 && paid.HappySijunPayment.Movements.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) &&
            selected.Sum(id => V(g).Hand.Single(c => c.Id == id).Rank) == 36 &&
            E<AnniversarySijunObtainedEvent>(g) is [{ Source.SkillId: Sijun, RemovedHuang: 13, RankSum: 36 }] && E<AnniversaryZhangJiaoShuffledEvent>(g).Length == 0,
            "Happy Sijun really pays all Huang and obtains only the original draw-pile sum-36 entities, excluding equally eligible discard entities, before the gain child and later shuffle.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(E<AnniversarySijunObtainedEvent>(g).Length == 1 && E<AnniversaryZhangJiaoShuffledEvent>(g) is [{ Source.SkillId: Sijun }] &&
            selected.All(id => g.CardMovements.Count(m => m.CardId == id && m.Sequence > paid.HappySijunPayment!.Before && m.Sequence <= paid.HappySijunPayment.After &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == ObtainReason) == 1) &&
            discarded.All(c => !g.CardMovements.Any(m => m.CardId == c.Id && m.Reason.Value == ObtainReason && m.Sequence > paid.Before && m.Sequence <= paid.After)),
            "Cold return never repeats acquisition or marker payment, preserves the original draw-pile invoice through its separate shuffle stage, and really shuffles the unselected discard cards afterward.");
        var hp2 = V(g, 2).Hp; var hp3 = V(g, 3).Hp;
        End(g); Reach(g, () => Offer(P(g), Tianjie)); Activate(g);
        Reach(g, () => P(g)?.Choices.Any(c => Key(c) == "anniversary-tianjie") == true);
        Answer(g, c => c.Targets.SequenceEqual([3])); Answer(g, c => c.Targets.SequenceEqual([2]));
        Answer(g, c => c.Parameters.GetValueOrDefault("option") == "finish");
        Reach(g, () => E<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == Tianjie && e.Activated && e.Completed));
        var thunder = E<DamageAppliedEvent>(g).Where(e => e.SourceSeat == 0 && e.Nature == DamageNature.Thunder).ToArray();
        Need(thunder.Select(e => e.TargetSeat).SequenceEqual([2, 3]) && thunder.Select(e => e.Amount).SequenceEqual([2, 1]) && V(g, 2).Hp == hp2 - 2 && V(g, 3).Hp == hp3 - 1,
            "The same actual turn's real shuffle enables Happy Tianjie: actual seat order, two genuine Dodges versus the minimum one, and native damage returns all remain intact.");
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)), "Accepted commands replay Happy's use-only accumulation, draw-only payment, cold child, shuffle and native ending thunder.");

        var defense = Start(registry); Play(defense); var hp = V(defense).Hp;
        Use(defense, "damage-self"); Reach(defense, () => Offer(P(defense), Sanshou)); Activate(defense); Play(defense);
        var shown = E<ProgramCardsRevealedEvent>(defense).Single(e => e.SkillId == Sanshou).Cards;
        Need(V(defense).Hp == hp && E<AnniversarySanshouResolvedEvent>(defense) is [{ Source.SkillId: Sanshou, UsedCategoryMask: 0, Prevented: true }] &&
            shown.Count == 3 && shown.All(c => defense.CardMovements.Any(m => m.CardId == c.Id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile)) &&
            Mark(defense) == 0 && defense.ResolutionStack.All(f => f is not ProgramSkillFrame { AnniversaryZhangJiao: not null }),
            "Happy's fourth distinct skill Sanshou really snapshots this turn's unused Basic category, prevents actual damage, and physically cleans its three revealed cards without creating discard Huang.");
    }

    private static GameOptions Options()
    {
        var setup = new ShanheBattleSetup(7, 1, "happy-zj", Hero, 3, 0, 0, 0, 0, [Driver, NoDraw, Hold],
            [new(CardKind.Slash, Suit.Spade, 13), new(CardKind.Slash, Suit.Spade, 12), new(CardKind.Slash, Suit.Heart, 12), new(CardKind.Slash, Suit.Diamond, 12)], [],
            [new(Peer(2), ExtraSkillIds: [NoDraw]) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 7), new(CardKind.Dodge, Suit.Diamond, 8)] },
             new(Peer(3), ExtraSkillIds: [NoDraw]) { StartingHand = [] }],
            [new(Peer(1), ExtraSkillIds: [NoDraw]) { StartingHand = [] }], new string('d', 64)) { DefaultGodFactionId = "qun" };
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 };
    }
    private static GameEngine Start(ContentRegistry r) { var g = GameEngine.CreateStandard(Options(), r); Accept(g, new StartGameCommand()); return g; }
    private static string Peer(int seat) => "fixture:happy-zj-peer-" + seat;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static bool Offer(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => Key(c) == "activate");
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    private static int Mark(GameEngine g) => V(g).Markers?.FirstOrDefault(m => m.Kind == PlayerMarkerKind.Huang)?.Count ?? 0;
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected real Happy Zhang Jiao command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing native Happy Zhang Jiao choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => Key(c) == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> ready)
    {
        for (var n = 0; n < 200; n++)
        {
            if (ready()) return;
            if (P(g) is { } p)
            {
                if (p.SkillPrompt?.SkillId == Hold) { Continue(g); continue; }
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Happy Zhang Jiao boundary: " + JsonSerializer.Serialize(new { Prompt = p, Huang = Mark(g), Gained = E<AnniversarySijunObtainedEvent>(g), Shuffled = E<AnniversaryZhangJiaoShuffledEvent>(g) }));
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Happy Zhang Jiao missed the bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Cold restore preserves original-frame random entities, draw-pile payment, marker cost, native child and player views."); return restored; }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-zj", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var json = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"discard","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},
                {"id":"damage-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]}]},
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Hold}}","revision":1,"triggers":[{"id":"gain","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{ObtainReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实驱动", description = "真实弃牌与伤害。" }, [NoDraw] = new { name = "原生少摸", description = "避免消耗小牌堆。" }, [Quiet] = new { name = "安静角色", description = "跳过他人的出牌。" }, [Hold] = new { name = "真实获得子窗", description = "暂停已付获得。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Native Happy Zhang Jiao fixture") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            for (var s = 1; s < 4; s++) b.AddGeneral(new(Peer(s), "真实同行", "supporter", Quiet, "wei", 9));
            b.AddDeck(new(Deck, "四张固定实体牌堆", 0, 2, []) { PhysicalCards = [new("standard:slash", Suit.Club, 1), new("standard:slash", Suit.Spade, 12), new("standard:slash", Suit.Heart, 12), new("standard:slash", Suit.Diamond, 12)] });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐张角真实四技能", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
