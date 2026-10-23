using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryLiuYongChecks
{
    private const string Hero = "anniversary:liu-yong", Zhuning = "anniversary:zhuning", Fengxiang = "anniversary:fengxiang", Peer = "fixture:anniversary-liuyong-peer",
        NoDraw = "fixture:anniversary-liuyong-no-draw", Quiet = "fixture:anniversary-liuyong-quiet", Driver = "fixture:anniversary-liuyong-driver",
        DrawHold = "fixture:anniversary-liuyong-draw", Deck = "fixture:anniversary-liuyong-deck";
    public static void XiLeaderNativeUsesAndPhaseQuotaResumeCold()
    {
        var s = Scene.Start(); s.Play();
        var dodges = s.V(0).Hand!.Where(c => c.Kind == CardKind.Dodge).Select(c => c.Id).ToArray();
        s.Zhuning(3); s.Select(dodges[0]); s.Give(); s.Reach(() => s.P?.SkillPrompt?.SkillId == DrawHold);
        var gift = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.AnniversaryLiuYong?.Mode == "gift");
        var transition = s.Root!;
        s.Need(gift.AnniversaryLiuYong is { Stage: AnniversaryLiuYongStage.GiftChildren, GiftPaid: true, Recipient: 3 } &&
            transition.AnniversaryLiuYong is { Mode: "transition", Stage: AnniversaryLiuYongStage.Drawing } &&
            s.E<AnniversaryLiuYongLeaderChangedEvent>() is [{ Previous: null, Current: 3 }] &&
            s.E<AnniversaryLiuYongGiftPaidEvent>() is [{ Count: 1, Recipient: 3 }] &&
            s.G.CardMovements.Count(m => m.CardId == dodges[0] && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(3) && m.Reason.Value == "skill.anniversary.zhuning.gift") == 1,
            "One real private gift makes its receiver the unique Xi leader before the original program opens a real Fengxiang draw child.");
        s.Cold(); s.Skip(); s.UseMenu(); s.Native(CardKind.Slash, 3); s.Play();
        var first = s.E<AnniversaryLiuYongNativeReturnedEvent>().Single();
        s.Need(!first.CausedDamage && s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == first.UseId && e.CardKind == CardKind.Slash) &&
            s.E<AnniversaryLiuYongLeaderChangedEvent>().Select(e => (e.Previous, e.Current)).SequenceEqual([(null, 3), (3, null)]) &&
            s.G.CardMovements.Count(m => m.CardId == dodges[0] && m.From == CardLocation.Hand(3) && m.To == CardLocation.Processing) == 1 &&
            s.E<AnniversaryLiuYongLimitExpandedEvent>().Length == 1 && s.CanZhuning(),
            "The actual enemy uses the gifted Dodge: Xi is consumed on hand departure, its sole-leader disappearance draws once, and a truly undamaged native Slash raises this actual phase's quota to two.");
        s.Ping(); s.Play();
        var none = s.E<AnniversaryLiuYongStartedEvent>().Last(e => e.Mode == "damage");
        s.Need(s.V(0).Hp == 2 && s.E<AnniversaryLiuYongDrawReturnedEvent>().Count(e => e.FrameId == none.FrameId && e.Count == 1) == 1 &&
            s.E<RecoveryAppliedEvent>().Length == 0, "An incoming real damage with no unique Xi leader draws for Liu Yong instead of inventing a recovery target.");
        s.Zhuning(1); s.Select(dodges[1]); s.Give(); s.UseMenu(); s.Native(CardKind.Duel, 2); s.Play();
        var second = s.E<AnniversaryLiuYongNativeReturnedEvent>().Last();
        s.Need(second.CausedDamage && second.UseId != first.UseId && s.E<DamageAppliedEvent>().Count(e => e.SourceCardUseResolutionId == second.UseId && e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == second.UseId && e.CardKind == CardKind.Duel) == 1 &&
            s.E<AnniversaryLiuYongLimitExpandedEvent>().Length == 1 && !s.CanZhuning(),
            "The second paid gift uses a complete native Duel; actual card damage prevents any additional quota and the phase never exceeds two activations.");
        s.Ping(); s.Play();
        var healing = s.E<AnniversaryLiuYongStartedEvent>().Last(e => e.Mode == "damage");
        s.Need(s.V(0).Hp == 1 && s.V(1).Hp == 19 && s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1 &&
            !s.E<AnniversaryLiuYongDrawReturnedEvent>().Any(e => e.FrameId == healing.FrameId) &&
            s.E<AnniversaryLiuYongLeaderChangedEvent>().Last() is { Previous: null, Current: 1 },
            "With a unique Xi holder, the same incoming-damage rule performs that participant's true native recovery and does not also draw.");

        var expanded = s.E<AnniversaryLiuYongLimitExpandedEvent>().Single();
        s.End(); s.Reach(() => s.G.State.TurnNumber > expanded.Turn && s.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        s.Need(s.CanZhuning(), "A new real Play-phase instance resets the activation budget without borrowing the previous phase's expanded quota.");
        var changes = s.E<AnniversaryLiuYongLeaderChangedEvent>().Length; var paid = s.E<AnniversaryLiuYongGiftPaidEvent>().Length;
        s.Zhuning(2); s.Give(); s.UseMenu();
        s.Need(s.E<AnniversaryLiuYongGiftPaidEvent>().Length == paid + 1 && s.E<AnniversaryLiuYongGiftPaidEvent>().Last().Count == 0 &&
            s.E<AnniversaryLiuYongLeaderChangedEvent>().Length == changes,
            "The literal zero-card gift is allowed but creates no fake hand movement or Xi-leader change.");
        s.Native(CardKind.FireAttack, 1); s.Play();
        var third = s.E<AnniversaryLiuYongNativeReturnedEvent>().Last();
        s.Need(!third.CausedDamage && s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == third.UseId && e.CardKind == CardKind.FireAttack) &&
            s.E<AnniversaryLiuYongLimitExpandedEvent>().Length == 2 && s.CanZhuning(),
            "The real FireAttack reveal/payment pipeline completes without matching-color material; its own whole-use result expands only the new phase.");
        s.Zhuning(2); s.Give(); s.UseMenu(); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "finish"); s.Play();
        s.Need(!s.CanZhuning() && s.E<AnniversaryLiuYongNativeIssuedEvent>().Length == 3 && s.E<AnniversaryLiuYongLimitExpandedEvent>().Length == 2 &&
            s.E<AnniversaryLiuYongGiftPaidEvent>().Count(e => e.Count == 0) == 2 &&
            s.E<AnniversaryLiuYongDrawReturnedEvent>().Count(e => e.FrameId == transition.Id) == 1,
            "Declining the optional card never fabricates a use or a third activation; cold never repeats the first gift or its native draw.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-liuyong", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero); var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Peer, "有真实响应的角色", "", Quiet, "wei", 20));
            builder.AddDeck(new(Deck, "固定不同色实体", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "周年刘永实际牌流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Presentation => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new[] { NoDraw, Quiet, Driver, DrawHold }.ToDictionary(id => id, id => new { name = id, description = "真实隙手牌与原生伤害、摸牌" }) });
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
          {"id":"{{Driver}}","revision":1,"activations":[{"id":"ping","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]}]}
        ]}
        """;
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryLiuYong is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool CanZhuning() => G.GetHumanLegalActions().Any(a => a.ProgramSkillId == AnniversaryLiuYongChecks.Zhuning && a.ProgramActivationId == "gift-and-use");
        public void Zhuning(int target) => Accept(new UseProgramSkillCommand(0, AnniversaryLiuYongChecks.Zhuning, "gift-and-use", [], [target], G.Revision, P!.PromptId));
        public void Ping() => Accept(new UseProgramSkillCommand(0, Driver, "ping", [], [], G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void UseMenu() => Reach(() => Root?.AnniversaryLiuYong?.Stage == AnniversaryLiuYongStage.UseChoice && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "finish") == true);
        public void Select(int id) => Answer(c => c.Cards.SequenceEqual([id]));
        public void Give() => Answer(c => c.Parameters.GetValueOrDefault("option") == "give");
        public void Native(CardKind kind, int target)
        {
            var r = Root!.AnniversaryLiuYong!;
            var choices = P!.Choices.Where(c => c.Parameters.GetValueOrDefault("option")?.StartsWith("use-", StringComparison.Ordinal) == true).ToArray();
            var choice = choices.FirstOrDefault(c => c.Label.Contains("【" + CardCatalog.Get(kind).DisplayName + "】", StringComparison.Ordinal) && c.Targets.SequenceEqual([target]));
            Need(r.Stage == AnniversaryLiuYongStage.UseChoice && choice is not null, "Missing the exact published native damage-card plan."); Answer(c => c.Id == choice!.Id);
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Missing the exact Liu Yong prompt choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 160; step++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == DrawHold) Skip();
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Liu Yong boundary " + Dump());
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded real Liu Yong flow did not reach its original native continuation.");
        }
        public void Cold()
        {
            var snapshots = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(); var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(snapshots.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))), "Cold retains the private Xi gift, native draw child, true phase quota and exact typed return.");
        }
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Liu Yong rejected a real command."); }
        public void Need(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Receipt = Root?.AnniversaryLiuYong, Starts = E<AnniversaryLiuYongStartedEvent>(), Leaders = E<AnniversaryLiuYongLeaderChangedEvent>(), Paid = E<AnniversaryLiuYongGiftPaidEvent>(), Native = E<AnniversaryLiuYongNativeReturnedEvent>(), Expanded = E<AnniversaryLiuYongLimitExpandedEvent>() });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(43, 1, "anniversary-liuyong", Hero, 3, 0, 0, 0, 0, [NoDraw, Driver, DrawHold],
                [new(CardKind.Slash, Suit.Club, 7), new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Heart, 3)], [],
                [new(Peer, Hp: 20, ExtraSkillIds: [NoDraw]) { StartingHand = [] }, new(Peer, Hp: 20, ExtraSkillIds: [NoDraw]) { StartingHand = [] }],
                [new(Peer, Hp: 18, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], new string('d', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
}
