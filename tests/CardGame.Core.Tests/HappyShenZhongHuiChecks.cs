using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class HappyShenZhongHuiChecks
{
    private const string Hero = "happy:shen-zhong-hui", Linjie = "happy:linjie", Duzhang = "happy:duzhang", Jianghuo = "happy:jianghuo", Lishi = "happy:lishi";
    private const string Driver = "fixture:happy-zh-driver", Watch = "fixture:happy-zh-watch", Quiet = "fixture:happy-zh-quiet", PeerUse = "fixture:happy-zh-native-use", Deck = "fixture:happy-zh-deck";

    public static void TwiceActualTurnDrawColdAndAllCardLishiNativeUse()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry); Accept(g, new StartGameCommand()); Play(g);
        var initialHand = V(g).HandCount; var actualTurn = g.State.TurnNumber;
        Need(V(g).Hp == 4 && V(g).MaxHp == 4 && Mark(g) == 0, "The formal Happy general starts at four HP with no manufactured Lin.");
        Iron(g, 1); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch && Root(g)?.ShenZhongHui?.Operation == SkillProgramEffectOp.ResolveDuzhang);
        var first = Root(g)!; var firstId = first.Id;
        Need(first.SkillId == Duzhang && first.ShenZhongHui is { Stage: ShenZhongHuiStage.DrawChildren, Count: 1 } &&
            V(g).HandCount == initialHand && Mark(g) == 0 &&
            E<ShenZhongHuiDrawReturnedEvent>(g).Count(e => e.FrameId == firstId && e.Actual == 1) == 1,
            "The first real sole black use has already paid its one-card native gain and pauses before granting Lin in the same original Duzhang frame.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(Mark(g) == 1 && V(g).HandCount == initialHand && Limit(g) == V(g).Hp + 1,
            "Cold return grants one genuine hand-limit Lin and does not redraw the already-paid physical entity.");
        Iron(g, 2); Play(g);
        Need(g.State.TurnNumber == actualTurn && Mark(g) == 2 && V(g).HandCount == initialHand && Limit(g) == V(g).Hp + 2 &&
            E<ShenZhongHuiStartedEvent>(g).Count(e => e.Source.SkillId == Duzhang) == 2 &&
            E<SkillUsageConsumedEvent>(g).Where(e => e.SkillId == Duzhang && e.Scope == SkillUsageScope.Turn).Select(e => e.Count).SequenceEqual([1, 1]),
            "A second independent sole black native use in this same actual turn really draws and grants Lin, consuming the Happy second use rather than the anniversary once-only quota.");
        Iron(g, 3); Play(g);
        Need(Mark(g) == 2 && V(g).HandCount == initialHand - 1 && E<ShenZhongHuiStartedEvent>(g).Count(e => e.Source.SkillId == Duzhang) == 2,
            "The third sole black use resolves its original card but cannot obtain a third Duzhang draw or marker.");

        Use(g, "self", []); Play(g);
        foreach (var target in Enumerable.Range(1, 3)) { Use(g, "poke", [target]); Play(g); }
        Need(E<DamageAppliedEvent>(g).Where(e => e.Amount > 0).Select(e => e.TargetSeat).Distinct().Order().SequenceEqual([0, 1, 2, 3]),
            "Every living character really receives damage before the unmodified native awakening boundary.");
        End(g); Play(g);
        Need(V(g).MaxHp == 5 && Mark(g) == 2 && V(g).Skills!.Any(s => s.ContentId == Lishi) &&
            V(g).Skills!.All(s => s.ContentId is { } id && id != Linjie && !id.StartsWith("anniversary:", StringComparison.Ordinal)) &&
            E<SkillAwakenedEvent>(g).Count(e => e.SkillId == Jianghuo) == 1 &&
            E<ShenZhongHuiStartedEvent>(g).Single(e => e.Operation == SkillProgramEffectOp.AwakenJianghuo).Source?.SkillId == Jianghuo,
            "The real next own turn gathers Lin, draws, increases maximum HP and replaces only the Happy Linjie with Happy Lishi.");
        End(g); Reach(g, () => Root(g)?.ShenZhongHui is { Operation: SkillProgramEffectOp.ChooseLishiPunishments, Stage: ShenZhongHuiStage.LishiChoice });
        var issueId = Root(g)!.Id;
        Answer(g, c => c.Parameters.GetValueOrDefault("value") == "9");
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == PeerUse && P(g)!.PlayerSeat == 1 &&
            P(g)!.Choices.Any(c => Key(c) == "activate") && E<LishiPhaseAppliedEvent>(g).Any(e => e.IssueFrameId == issueId && e.TargetSeat == 1 && e.Option == 4));
        Need(Mark(g) == 0 && E<LishiPunishmentIssuedEvent>(g).Count(e => e.FrameId == issueId && e.Source.SkillId == Lishi && e.Option is 1 or 4) == 6 &&
            V(g, 1).Skills!.Any(s => s.ContentId == "happy:lishi-curse"),
            "Two real Lin pay once for two selected future obligations per other role, and the phase helper is acquired from this Happy edition.");
        var health = V(g).Hp; var usesBefore = E<CardUseDeclaredEvent>(g).Count(e => e.SourceSeat == 1 && e.CardId == 0);
        Answer(g, c => Key(c) == "activate");
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == PeerUse && P(g)!.Choices.Any(c => Key(c) == "select-target"));
        Answer(g, c => c.Targets.SequenceEqual([0]));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == PeerUse && P(g)!.Choices.Any(c => Option(c) == "done"));
        var use = E<LishiHandTypeUsedEvent>(g).Single(e => e.ActorSeat == 1);
        Need(use.Category == "basic" && use.ActionId > 0 && V(g).Hp == health - 1 &&
            E<CardUseDeclaredEvent>(g).Count(e => e.SourceSeat == 1 && e.CardId == 0) == usesBefore + 1 &&
            E<LishiPhaseAppliedEvent>(g).Any(e => e.IssueFrameId == issueId && e.Option == 4 && e.TargetSeat == 1 && e.Phase == use.Phase),
            "A real legacy material-free Slash records its accepted Basic use under the issued Play obligation; the following explicit-native material-free Slash is blocked before another Use or damage, despite having no hand material.");
        Answer(g, c => Option(c) == "done"); Play(g);
        var beforeSelf = E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && !e.SourceLess && e.Nature == DamageNature.Thunder);
        var selfHp = V(g).Hp; End(g);
        Reach(g, () => E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && !e.SourceLess && e.Nature == DamageNature.Thunder) == beforeSelf + 1);
        Need(Mark(g) == 0 && V(g).Hp == selfHp - 1,
            "The next genuine zero-Lin Ending inflicts one native thunder damage from Zhong Hui himself, preserving the Happy printed source instead of the anniversary source-less branch.");
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)),
            "The full journal replays the shared actual-turn two-use quota, paid draw cold return, Happy awakening IDs, real future-phase issue, zero-material category limit and sourced self damage.");
    }

    private static GameOptions Options()
    {
        var setup = new ShanheBattleSetup(7, 1, "happy-zhong-hui", Hero, 4, 0, 0, 0, 0, [Driver, Watch],
            [new(CardKind.IronChain, Suit.Spade, 1), new(CardKind.IronChain, Suit.Club, 2), new(CardKind.IronChain, Suit.Spade, 3)], [],
            [new(Peer(2)) { StartingHand = [] }, new(Peer(3)) { StartingHand = [] }], [new(Peer(1)) { StartingHand = [] }], new string('f', 64)) { DefaultGodFactionId = "wei" };
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 };
    }
    private static string Peer(int seat) => "fixture:happy-zh-peer-" + seat;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ShenZhongHui is not null);
    private static int Mark(GameEngine g) => V(g).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.LinCold)?.Count ?? 0;
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option-id");
    private static void Need(bool valid, string why) { if (!valid) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Happy Zhong Hui native command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing Happy Zhong Hui choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Use(GameEngine g, string binding, int[] targets) => Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], targets, g.Revision, P(g)!.PromptId));
    private static void Iron(GameEngine g, int target)
    {
        var a = g.GetHumanLegalActions().First(a => a.CardId is { } id && V(g).Hand.Any(c => c.Id == id && c.Kind == CardKind.IronChain) && a.TargetSeats.SequenceEqual([target]));
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
    }
    private static void Continue(GameEngine g) => Answer(g, c => Option(c) == "continue");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<bool> ready)
    {
        for (var i = 0; i < 320; i++)
        {
            if (ready()) return;
            if (P(g) is { } p)
            {
                if (p.SkillPrompt?.SkillId == Watch) { Continue(g); continue; }
                if (p.SkillPrompt?.SkillId is Linjie or PeerUse && p.Choices.Any(c => Key(c) == "skip")) { Answer(g, c => Key(c) == "skip"); continue; }
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondDodge) { Answer(g, c => c.Cards.Count == 0); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Happy Zhong Hui boundary: " + JsonSerializer.Serialize(new { Prompt = p, Lin = Mark(g), Stage = Root(g)?.ShenZhongHui, ActualTurn = g.State.TurnNumber }));
            }
            Need(g.State.Winner == Winner.None, "The short native Happy Zhong Hui fixture must retain its next boundary.");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Happy Zhong Hui missed the bounded native boundary: " + JsonSerializer.Serialize(new { Prompt = P(g), Lin = Mark(g), Stage = Root(g)?.ShenZhongHui }));
    }
    private static int Limit(GameEngine g)
    {
        var players = (System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(g)!;
        return (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(g, [players[0]])!;
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(restored), "Cold restore preserves original sole-black Use, paid real draw and deferred Lin grant before the native parent returns."); return restored; }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-zhong-hui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var json = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.happy:duzhang.draw","skill-program.happy:jianghuo.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{PeerUse}}","revision":1,"triggers":[{"id":"two-material-free-uses","window":"playPhaseStarting","subject":"owner","optional":true,"effects":[
                {"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
                {"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":false},
                {"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true},
                {"op":"chooseOption","target":"owner","resultBind":"done","options":[{"id":"done"}]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "原生伤害驱动", description = "实际伤害满足原觉醒条件。" }, [Quiet] = new { name = "安静角色", description = "跳过其他角色出牌。" }, [Watch] = new { name = "已付摸牌子窗", description = "暂停真实获得，保留原始技能帧。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }, [PeerUse] = new { name = "真实零实体用牌", description = "依次尝试两个原生零实体杀。", optionLabels = new Dictionary<string, string> { ["done"] = "完成" } } };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer(1), "真实原生用牌者", "supporter", PeerUse, "wei", 8));
            for (var s = 2; s < 4; s++) b.AddGeneral(new(Peer(s), "真实同行", "supporter", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 2)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐钟会实际额度与立世", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
