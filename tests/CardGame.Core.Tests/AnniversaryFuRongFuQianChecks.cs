using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryFuRongFuQianChecks
{
    private const string Hero = "anniversary:fu-rong-fu-qian", Xuewei = "anniversary:xuewei", Yuguan = "anniversary:yuguan";
    private const string Driver = "fixture:ann-fu-pair-driver", Watch = "fixture:ann-fu-pair-watch", Quiet = "fixture:ann-fu-pair-quiet", Deck = "fixture:ann-fu-pair-deck";
    public static void WholeDamageProtectionAndPostMaximumLostHpRefillCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry); Accept(g, new StartGameCommand()); Play(g);
        Need(V(g).Hp == 4 && V(g).MaxHp == 6, "The formal double general starts at genuine four HP out of six maximum in the ordinary Team setup.");
        var firstTurn = g.State.TurnNumber; var hand = V(g).HandCount;
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Offer(g, Xuewei); Activate(g);
        Menu(g, AnniversaryFuRongFuQianStage.ArmChoice); Answer(g, c => c.Targets.SequenceEqual([0]));
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var paid = Root(g)!; var grant = E<AnniversaryXueweiArmedEvent>(g).Single().Grant;
        Need(g.State.TurnNumber != firstTurn && grant.TargetSeat == 0 && grant.OwnerHp == 4 && grant.TargetHp == 4 &&
            paid.AnniversaryFuRongFuQian is { Operation: SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei,
                Stage: AnniversaryFuRongFuQianStage.DrawChildren, HpPaid: true, ActualLoss: 1, HpBefore: 4, HpAfter: 3,
                PreventedAmount: 2, Targets: [0], Draws.Count: 1 } && V(g).Hp == 3 && V(g).HandCount == hand + 1 &&
            E<ProgramDamagePreventedEvent>(g).Count(e => e.SkillId == Xuewei && e.TargetSeat == 0 && e.Amount == 2) == 1 &&
            E<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == paid.Id && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            E<DamageAppliedEvent>(g).All(e => e.TargetSeat != 0) &&
            g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.anniversary:xuewei.draw") == 1,
            "One real two-point damage is wholly prevented; Xuewei really loses only one HP and draws only once when its two participant references name the same character." + Detail(g));
        var paidId = paid.Id;
        g = Cold(g, registry); Continue(g); Offer(g, Yuguan); Activate(g); Menu(g, AnniversaryFuRongFuQianStage.TargetsChoice);
        var max = Root(g)!;
        Need(V(g).Hp == 3 && V(g).MaxHp == 5 && max.AnniversaryFuRongFuQian is { MaximumBefore: 6, MaximumAfter: 5,
            HpBeforeMaximum: 3, HpAfterMaximum: 3, LostHp: 2, TargetCount: 2 } &&
            E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 1 && e.TurnNumber == g.State.TurnNumber) &&
            E<AnniversaryXueweiHpPaidEvent>(g).Count(e => e.FrameId == paidId) == 1 &&
            E<AnniversaryFuRongFuQianDrawReturnedEvent>(g).Count(e => e.FrameId == paidId && e.Requested == 1 && e.ActualCount == 1) == 1,
            "The cold return never pays again, and Yuguan begins after the actual foreign turn ends, pays maximum six to five, then freezes post-payment lost HP as two rather than the earlier three." + Detail(g));
        var yuguanId = max.Id;
        Answer(g, c => c.Targets.SequenceEqual([0])); Answer(g, c => c.Targets.SequenceEqual([1]));
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "done"); Play(g);
        Need(V(g).Hp == 3 && V(g).MaxHp == 5 && V(g).HandCount == 5 && V(g, 1).HandCount == V(g, 1).MaxHp &&
            E<AnniversaryFuRongFuQianTargetChosenEvent>(g).Where(e => e.FrameId == yuguanId).OrderBy(e => e.Index).Select(e => e.TargetSeat).SequenceEqual([0, 1]) &&
            E<AnniversaryFuRongFuQianDrawReturnedEvent>(g).Where(e => e.FrameId == yuguanId).Select(e => e.RecipientSeat).SequenceEqual([0, 1]) &&
            E<AnniversaryYuguanMaximumPaidEvent>(g).Count(e => e.FrameId == yuguanId) == 1 &&
            E<AnniversaryXueweiExpiredEvent>(g).Count(e => e.GrantFrameId == grant.FrameId && e.ActualTurn == g.State.TurnNumber) == 1,
            "Exactly the two chosen real recipients receive their own native maximum-minus-hand draw requests; the original protection expires at the source's next actual turn start." + Detail(g));
        var applied = E<DamageAppliedEvent>(g).Length;
        Accept(g, new UseProgramSkillCommand(0, Driver, "two-damage", [], [], g.Revision, P(g)!.PromptId)); Play(g);
        Need(V(g).Hp == 1 && E<DamageAppliedEvent>(g).Skip(applied).ToArray() is [{ SourceSeat: 0, TargetSeat: 0, Amount: 2 }] &&
            E<AnniversaryXueweiHpPaidEvent>(g).Length == 1 && E<ProgramDamagePreventedEvent>(g).Count(e => e.SkillId == Xuewei) == 1,
            "The genuine next-turn damage resumes its native two-point HP payment and cannot borrow the expired protection." + Detail(g));
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)),
            "The short accepted command journal replays the exact protection grant, once-paid HP/native draw child, post-maximum X, selected recipients and actual-turn expiry.");
    }
    private static GameOptions Options()
    {
        var setup = new ShanheBattleSetup(7, 1, "anniversary-fu-pair", Hero, 4, 0, 0, 0, 0, [Driver, Watch],
            [new(CardKind.Dodge, Suit.Heart, 2)], [],
            [new(Peer(2)) { StartingHand = [] }, new(Peer(3)) { StartingHand = [] }], [new(Peer(1)) { StartingHand = [] }], new string('a', 64));
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8 };
    }
    private static string Peer(int seat) => "fixture:ann-fu-pair-peer-" + seat;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryFuRongFuQian is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool b, string message) { if (!b) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Fu Rong Fu Qian command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose) { var p = P(g)!; var c = p.Choices.FirstOrDefault(choose); Need(c is not null, "Missing Fu Rong Fu Qian choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => Key(c) == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Offer(GameEngine g, string skill) => Reach(g, p => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => Key(c) == "activate"));
    private static void Menu(GameEngine g, AnniversaryFuRongFuQianStage stage) => Reach(g, p => p.Choices.Any(c => Key(c) == "anniversary-fu-rong-fu-qian") && Root(g)?.AnniversaryFuRongFuQian?.Stage == stage);
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var i = 0; i < 180; i++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.SkillPrompt?.SkillId == Watch) { Continue(g); continue; }
                if (p.SkillPrompt?.SkillId is Xuewei or Yuguan && p.Choices.Any(c => Key(c) == "skip")) { Answer(g, c => Key(c) == "skip"); continue; }
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Fu Rong Fu Qian boundary: " + JsonSerializer.Serialize(p) + Detail(g));
            }
            Need(g.State.Winner == Winner.None, "The short Fu Rong Fu Qian scene must retain its true native boundary."); Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fu Rong Fu Qian missed its bounded native boundary." + Detail(g));
    }
    private static string Detail(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).MaxHp, V(g, s).HandCount }),
        Root = Root(g)?.AnniversaryFuRongFuQian, Prevented = E<ProgramDamagePreventedEvent>(g), HpPaid = E<AnniversaryXueweiHpPaidEvent>(g), MaximumPaid = E<AnniversaryYuguanMaximumPaidEvent>(g) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "The already-paid whole-damage protection and native draw child cold-restore identically."); return cold; }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-fu-pair", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[{"id":"two-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]}],"triggers":[{"id":"actual-foreign-turn-damage","window":"turnStartBeforeNormalFlow","subject":"owner","turnOwnerScope":"otherLiving","usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"damage","target":"owner","amount":2}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"already-paid-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:xuewei.draw"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            { [Driver] = new { name = "真实两点伤害", description = "既有原生两点伤害；一次真实其他回合入口。" },
              [Watch] = new { name = "已支付血卫摸牌", description = "在真实手牌获得子窗暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
              [Quiet] = new { name = "安静其他角色", description = "跳过普通出牌。" } } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            for (var s = 1; s < 4; s++) b.AddGeneral(new(Peer(s), "同桌角色", "", Quiet, "wei", 4));
            b.AddDeck(new(Deck, "固定小闪牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 2)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "血卫与御关原生主流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
