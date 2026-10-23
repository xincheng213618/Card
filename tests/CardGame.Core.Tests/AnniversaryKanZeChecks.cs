using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryKanZeChecks
{
    private const string Hero = "anniversary:kan-ze", Kuanshi = "anniversary:kuanshi", Xiashu = "anniversary:xiashu";
    private const string Driver = "fixture:ann-kan-ze-driver", Burst = "fixture:ann-kan-ze-burst", Watch = "fixture:ann-kan-ze-watch", Quiet = "fixture:ann-kan-ze-quiet", Deck = "fixture:ann-kan-ze-deck";

    public static void ActualSecondDamagePointPerTurnAndNativeRecoveryCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry); Accept(g, new StartGameCommand()); Play(g);
        Accept(g, new UseProgramSkillCommand(0, Driver, "actual-two-damage", [], [], g.Revision, P(g)!.PromptId)); Play(g);
        var armedTurn = g.State.TurnNumber;
        Need(V(g).Hp == 11 && E<DamageAppliedEvent>(g).Where(e => e.TargetSeat == 0).Sum(e => e.Amount) == 2,
            "The real native current turn has already applied two damage points before Kuanshi is granted.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == Kuanshi && p.Choices.Any(c => Key(c) == "activate"));
        Answer(g, c => Key(c) == "activate"); Reach(g, p => p.Choices.Any(c => c.Targets.SequenceEqual([0]))); Answer(g, c => c.Targets.SequenceEqual([0]));
        Step(g, "after-one");
        var grant = E<AnniversaryKuanshiArmedEvent>(g).Single().Grant;
        Need(grant.TargetSeat == 0 && grant.ActualTurn == armedTurn && g.State.TurnNumber != armedTurn && V(g).Hp == 9 &&
            E<AnniversaryKuanshiDamageIssuedEvent>(g).Length == 0 && E<RecoveryAppliedEvent>(g).Length == 0,
            "The ending's later third damage point does not invent a new second point; the first damage of the next actual turn also does not heal.");
        Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Watch && p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == "native-recovery"));
        var firstTurn = g.State.TurnNumber;
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.AnniversaryKuanshi is not null);
        Need(root.AnniversaryKuanshi is { Damage.PreviousDamage: 1, Damage.CurrentDamage: 2 } receipt && receipt.Grant == grant &&
            V(g).Hp == 9 && E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.Change.ParentFrameId == root.Id && f.ResumeFrameId == root.Id &&
                f.Continuation == PostEventContinuation.Program && f.Change is { SourceSeat: 0, TargetSeat: 0, Kind: HpChangeKind.Recovery, Amount: 1, HpBefore: 8, HpAfter: 9 }),
            "The second actual point binds the exact issued grant and native damage frame, then pays one genuine Recover1 before its health child returns." +
            JsonSerializer.Serialize(new { V(g).Hp, Receipt = root.AnniversaryKuanshi, Grant = grant,
                Recovery = E<RecoveryAppliedEvent>(g), HealthChildren = g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Select(f => new { f.Id, f.Change, f.Continuation, f.ResumeFrameId }) }));
        g = Cold(g, registry); Continue(g); Step(g, "after-two"); Continue(g); Step(g, "after-three");
        Need(g.State.TurnNumber == firstTurn && V(g).Hp == 8 && E<AnniversaryKuanshiDamageIssuedEvent>(g).Count(e => e.ActualTurn == firstTurn) == 1 &&
            E<AnniversaryKuanshiRecoveryBoundEvent>(g).Count(e => e.FrameId == root.Id) == 1 &&
            E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 0) == 1,
            "Cold return neither repeats the paid recovery nor rewards the third point of the same actual turn.");
        Continue(g);
        for (var foreign = 0; foreign < 2; foreign++)
        {
            Step(g, "after-one"); var next = g.State.TurnNumber;
            Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Watch); Continue(g); Step(g, "after-two"); Continue(g); Step(g, "after-three");
            Need(next != firstTurn && E<AnniversaryKuanshiDamageIssuedEvent>(g).Count(e => e.ActualTurn == next && e.PreviousDamage == 1 && e.CurrentDamage == 2) == 1,
                "The same unexpired grant earns one new native recovery in each different actual foreign turn, rather than once for its entire lifetime.");
            Continue(g);
        }
        Play(g);
        Need(E<AnniversaryKuanshiExpiredEvent>(g).Count(e => e.GrantId == grant.FrameId && e.ActualTurn == g.State.TurnNumber) == 1 &&
            E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 0) == 3 && V(g).Hp == 4,
            "The source's next actual turn expires the grant, after exactly one recovery in each of the three intervening actual turns.");
        var healed = E<RecoveryAppliedEvent>(g).Length;
        Accept(g, new UseProgramSkillCommand(0, Driver, "actual-two-damage", [], [], g.Revision, P(g)!.PromptId)); Play(g);
        Need(V(g).Hp == 2 && E<RecoveryAppliedEvent>(g).Length == healed && E<AnniversaryKuanshiDamageIssuedEvent>(g).Length == 3,
            "A true two-point damage in the source's new actual turn cannot use the expired grant and is not prevented.");
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)),
            "The accepted command journal replays actual cumulative damage, the ending grant, each actual-turn quota, typed recovery cold return and exact next-turn expiry.");
    }

    private static GameOptions Options()
    {
        var setup = new ShanheBattleSetup(7, 1, "anniversary-kan-ze", Hero, 13, 10, 0, 0, 0, [Driver, Burst, Watch],
            [new(CardKind.Dodge, Suit.Heart, 2)], [],
            [new(Peer(2)) { StartingHand = [] }, new(Peer(3)) { StartingHand = [] }],
            [new(Peer(1)) { StartingHand = [] }], new string('e', 64));
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8 };
    }
    private static string Peer(int seat) => "fixture:ann-kan-ze-peer-" + seat;
    private static PlayerSnapshot V(GameEngine g) => g.CreateSnapshot(0).Players.Single(p => p.Seat == 0);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool b, string message) { if (!b) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native anniversary Kan Ze command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "Missing exact Kan Ze choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Step(GameEngine g, string stage) => Reach(g, p => p.SkillPrompt?.SkillId == Burst && p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == stage));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var i = 0; i < 240; i++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.SkillPrompt?.SkillId == Xiashu && p.Choices.Any(c => Key(c) == "skip")) { Answer(g, c => Key(c) == "skip"); continue; }
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected anniversary Kan Ze boundary: " + JsonSerializer.Serialize(new { Prompt = p, Turn = g.State.TurnNumber, V(g).Hp,
                    Grants = E<AnniversaryKuanshiArmedEvent>(g), Issued = E<AnniversaryKuanshiDamageIssuedEvent>(g), Recovery = E<RecoveryAppliedEvent>(g) }));
            }
            Need(g.State.Winner == Winner.None, "The small Kan Ze scene must retain its real native boundary."); Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Anniversary Kan Ze missed its bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(cold), "The original grant, damage candidate, already-paid native recovery and all views cold-restore identically."); return cold; }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-kan-ze", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[{"id":"actual-two-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]}],"triggers":[{"id":"late-ending-third-point","window":"turnEnding","subject":"owner","usageScope":"game","usageLimit":1,"priority":-100,"isSkillInvocation":false,"optional":false,"effects":[{"op":"damage","target":"owner","amount":1}]}]},
              {"id":"{{Burst}}","revision":1,"triggers":[{"id":"three-actual-points","window":"turnStartBeforeNormalFlow","subject":"owner","turnOwnerScope":"otherLiving","isSkillInvocation":false,"optional":false,"effects":[
                {"op":"damage","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"after-one","options":[{"id":"continue"}]},
                {"op":"damage","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"after-two","options":[{"id":"continue"}]},
                {"op":"damage","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"after-three","options":[{"id":"continue"}]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-native-health-child","window":"afterHpRecovered","subject":"owner","isSkillInvocation":false,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native-recovery","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "原生伤害", description = "真实伤害与结束阶段后续一点。" },
                [Burst] = new { name = "各实际回合三点", description = "在实际回合原生入口逐次伤害。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Watch] = new { name = "真实回复子窗", description = "暂停已支付原生回复的健康子窗。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Quiet] = new { name = "安静其他角色", description = "跳过普通出牌。" } };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer(seat), "实际回合角色", "supporter", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "小固定闪牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "周年阚泽真实伤害与回复", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
