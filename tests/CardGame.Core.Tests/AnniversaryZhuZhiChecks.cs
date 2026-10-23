using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryZhuZhiChecks
{
    private const string Hero = "anniversary:zhu-zhi", Anguo = "anniversary:anguo", Peer = "fixture:zhu-zhi-peer", NoDraw = "fixture:zhu-zhi-no-draw",
        Quiet = "fixture:zhu-zhi-quiet", Prime = "fixture:zhu-zhi-start-equipment", Hold = "fixture:zhu-zhi-equipment-hold", Deck = "fixture:zhu-zhi-deck";
    public static void RemainingOwnerBenefitsAndNativeEquipmentResumeCold()
    {
        var s = Scene.Start(); s.Play();
        s.Need(s.V(0).Hp == 2 && s.V(0).HandCount == 1 && s.V(1).Hp == 5 && s.V(1).HandCount == 0 && s.V(1).Equipment.Count == 1 && s.V(2).Hp == 2,
            "The real fixture has an original minimum-Hand target, a wounded tied-minimum owner, and only the target initially equipped.");
        s.Anguo(1); s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold);
        var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Anguo);
        var bound = root.CardSetBindings.Single(b => b.Name == "owner-equipment-card"); var gear = bound.CardIds.Single();
        var issued = s.E<CardUseDeclaredEvent>().Single(e => e.SourceSeat == 0 && e.CardId == gear);
        s.Need(issued.CardKind == CardKind.Crossbow && root.PendingMovementContinuation is { SubjectSeat: 0, BeforeCount: 0, CoverageResultBind: null } && bound.SourceLocations.SequenceEqual([CardLocation.DrawPile]) &&
            s.V(0).Hp == 3 && s.V(0).HandCount == 1 && s.V(1).HandCount == 1 && s.V(0).Equipment.Any(c => c.Id == gear) &&
            Branches(root.Id).SequenceEqual(["target-hand:perform", "target-hp:skip", "target-equipment:skip", "owner-hand:skip", "owner-hp:perform", "owner-equipment:perform"]),
            "Anguo gives only the target's minimum-Hand draw, then really recovers the tied-minimum owner and uses an actual deck equipment entity for that owner, keeping the original selected target untouched.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        s.Need(!s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == root.Id) && s.V(0).Hp == 3 && s.V(0).HandCount == 1 && s.V(1).HandCount == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.ResolutionId && e.CardId == gear && e.CardKind == CardKind.Crossbow) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == gear && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.EquipmentUse) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == gear && m.From == CardLocation.Processing && m.To == CardLocation.Equipment(0)) == 1 &&
            s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 1 && !s.CanAnguo(),
            "Cold returns the paid owner-equipment movement child once, preserves the native one-card Use and one recovery, and leaves the actual Play activation spent.");

        s.End(); s.Play(); s.Anguo(2); s.Play();
        var second = s.E<ProgramSkillStartedEvent>().Last(e => e.SkillId == Anguo).FrameId;
        s.Need(second != root.Id && Branches(second).SequenceEqual(["target-hand:skip", "target-hp:perform", "target-equipment:perform", "owner-hand:perform", "owner-hp:skip", "owner-equipment:skip"]) &&
            s.V(0).HandCount == 2 && s.V(0).Hp == 3 && s.V(2).Hp == 3 && s.V(2).HandCount == 2 && s.V(2).Equipment.Count == 1 && s.V(0).Equipment.Count == 1 &&
            s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) == 1 && !s.CanAnguo(),
            "At the next actual Play, the other target really recovers and uses equipment, so the owner receives only the remaining live minimum-Hand draw; already performed target branches never repeat on the owner.");
        string[] Branches(long id) => s.E<AnniversaryZhuZhiBranchEvent>().Where(e => e.FrameId == id).Select(e => e.ResultBind + ":" + (e.Performed ? "perform" : "skip")).ToArray();
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-zhu-zhi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero); var c = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Peer, "固定最小值角色", "", NoDraw, "wei", 6));
            b.AddDeck(new(Deck, "真实固定装备牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 40).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "朱治三分支原生结清", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        private static string Presentation => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
        { [NoDraw] = new { name = "不摸普通牌", description = "固定短局" }, [Quiet] = new { name = "跳过其他人出牌", description = "保留实际回合边界" }, [Prime] = new { name = "开局真实装备", description = "固定原生装备" },
          [Hold] = new { name = "装备入区暂停", description = "已支付原生使用的移动子窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
        private static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"quiet-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{Prime}}","revision":1,"triggers":[{"id":"original-equipment","window":"gameStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"useRandomDeckEquipment","target":"owner","targetRef":{"kind":"owner"},"resultBind":"original-equipment"}]}]},
          {"id":"{{Hold}}","revision":1,"triggers":[
            {"id":"equipment-entered","window":"cardsGained","subject":"owner","destinationZones":["equipment"],"movementOccurrence":"perBatch","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"equipment","options":[{"id":"continue"}]}]}
          ]}
        ]}
        """;
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool CanAnguo() => G.GetHumanLegalActions().Any(a => a.ProgramSkillId == AnniversaryZhuZhiChecks.Anguo);
        public void Anguo(int target) => Accept(new UseProgramSkillCommand(0, AnniversaryZhuZhiChecks.Anguo, "three-minimum-benefits", [], [target], G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "The actual human answers the original equipment movement child."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready) { for (var i = 0; i < 130; i++) { if (ready()) return; if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Anguo boundary. " + Dump()); Accept(new AdvanceOneStepCommand(G.Revision)); } Need(false, "Anguo missed its bounded native continuation."); }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(); var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))), "Cold retains the original selected target, branch choices, real deck entity and paid owner native-use movement receipt.");
        }
        public void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Anguo command."); }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount, V(i).Equipment }),
            Branches = E<AnniversaryZhuZhiBranchEvent>(), Uses = E<CardUseDeclaredEvent>(), Finished = E<CardUseFinishedEvent>(), Recovered = E<RecoveryAppliedEvent>(), Frames = G.ResolutionStack });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(79, 1, "zhu-zhi-native-branches", Hero, 2, 0, 0, 0, 0, [NoDraw, Hold], [new(CardKind.Dodge, Suit.Heart, 2)], [],
                [new(Peer, Hp: 2, ExtraSkillIds: [Quiet]) { StartingHand = [new(CardKind.Slash, Suit.Club, 3), new(CardKind.Slash, Suit.Club, 4)] },
                 new(Peer, ExtraSkillIds: [Quiet]) { StartingHand = [new(CardKind.Slash, Suit.Spade, 5), new(CardKind.Slash, Suit.Spade, 6)] }],
                [new(Peer, Hp: 5, ExtraSkillIds: [Quiet, Prime]) { StartingHand = [] }], new string('a', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
