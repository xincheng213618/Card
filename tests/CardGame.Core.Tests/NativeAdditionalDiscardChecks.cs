using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class NativeAdditionalDiscardChecks
{
    internal const string Hero = "mou:cao-shuang", Skill = "mou:jianzhuan", Reason = "skill.mou:jianzhuan.discard";
    private const string Watch = "fixture:additional-discard-watch", Quiet = "fixture:additional-discard-quiet",
        Peer = "fixture:additional-discard-peer", Deck = "fixture:additional-discard-deck";

    public static void JianzhuanFixedDiscardProjectsWholeSetAndKeepsRecastSeparateCold()
    {
        var s = PrepareSelfDiscard();
        var equipment = s.V(0).Equipment.First(c => c.Kind == CardKind.Crossbow).Id;
        var hand = s.V(0).Hand.First(c => c.Kind == CardKind.DrawTwo).Id;
        var moves = s.G.CardMovements.Count;
        var frame = s.Frame!.Id;
        s.Need(s.P is { RequiredCardCount: 2, MinimumCardCount: 2, SkillPrompt.SkillId: Skill } &&
            s.P.Choices.All(c => c.Parameters.GetValueOrDefault("owned-selection-role") == "pick"),
            "The actual X2 self-discard publishes one complete two-card owned draft despite its singleton native choices.");
        var stage = s.P!.Choices[0].Parameters["owned-selection-stage"];
        s.Pick(c => c.Cards.SequenceEqual([equipment]));
        s.Need(s.P is { RequiredCardCount: 1, MinimumCardCount: 1 } && s.Frame!.Id == frame &&
            s.Frame.MouCaoShuangAnniversary!.Cards.Select(c => c.Id).SequenceEqual([equipment]) &&
            s.P.Choices.All(c => c.Parameters["owned-selection-stage"] == stage && c.Parameters["owned-selection-selected"] == equipment.ToString()) &&
            s.G.CardMovements.Count == moves,
            "A native partial pick retains its original frame and selected equipment, reduces remaining bounds, and moves nothing before the whole set is collected.");
        s.Cold(); s.Pick(c => c.Cards.SequenceEqual([hand]));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        s.Need(s.Frame is { Id: var id, MouCaoShuangAnniversary.Stage: MouCaoShuangAnniversaryStage.MoveChildren } && id == frame &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Single(e => e.ParentFrameId == frame).ActualCount == 2,
            "Both actual HE cards are paid together on the original frame and open a true shared discard-observer child.");
        s.Cold(); s.Play();
        s.Need(s.G.CardMovements.Skip(moves).Where(m => m.Reason.Value == Reason).Select(m => m.CardId).Order().SequenceEqual(new[] { equipment, hand }.Order()) &&
            s.E<MouCaoShuangAnniversaryItemCompletedEvent>().Last() is { Option: 4, X: 2, Actual: 2 },
            "Paid observer recovery completes X2 once and never replays either selected material.");

        s.UseItem(1); s.Pick(c => c.Targets.SequenceEqual([0]));
        s.Need(s.P is { RequiredCardCount: 3, MinimumCardCount: 3, SkillPrompt.SkillId: Skill } && s.Frame!.MouCaoShuangAnniversary!.Payer == 0,
            "Option one uses the selected payer's real HE draft with its independently frozen X3 requirement.");
        moves = s.G.CardMovements.Count;
        var three = s.P!.Choices.Take(3).Select(c => c.Cards.Single()).ToArray();
        s.Pick(c => c.Cards.SequenceEqual([three[0]])); s.Cold();
        s.Pick(c => c.Cards.SequenceEqual([three[1]]));
        s.Need(s.P!.RequiredCardCount == 1 && s.G.CardMovements.Count == moves,
            "Option one's third material remains editable and unpaid while the original first two are retained cold.");
        s.Pick(c => c.Cards.SequenceEqual([three[2]])); s.Play();
        s.Need(s.G.CardMovements.Skip(moves).Where(m => m.Reason.Value == Reason).Select(m => m.CardId).Order().SequenceEqual(three.Order()),
            "Option one discards the complete X3 set through its original per-payer native collector.");

        var observed = s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Watch);
        s.UseItem(3);
        s.Need(s.P is { RequiredCardCount: 0 } && s.Frame!.MouCaoShuangAnniversary is { Option: 3, Required: 4 } &&
            s.P.Choices.All(c => !c.Parameters.ContainsKey("owned-selection-role")),
            "Option three is a real recast and never receives discard-only presentation roles or bounds.");
        while (s.Frame?.MouCaoShuangAnniversary is { Stage: MouCaoShuangAnniversaryStage.Cards, Option: 3 }) s.Pick(c => c.Cards.Count == 1);
        s.Play();
        s.Need(s.E<CardRecastEvent>().Length == 4 && s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Watch) == observed && observed == 5,
            "Exactly four native recasts draw normally and never masquerade as the five actual discarded cards observed from options one and four.");
        s.Replay();
    }

    internal static Scene PrepareSelfDiscard()
    {
        var s = Scene.Start(); s.Play(); s.UseItem(2); s.Play(); s.UseItem(4); return s;
    }

    internal static Scene PrepareShimouReset()
    {
        var s = Scene.Start("mou:xun-yu", hp: 3, handCount: 4);
        s.Reach(() => s.ShimouFrame?.MouXunYuAnniversary?.Stage == MouXyStage.Initial);
        s.Pick(c => c.Parameters.GetValueOrDefault("branch") == "yin"); s.Play();
        s.Accept(new UseProgramSkillCommand(0, "mou:shimou", "adjust-and-command", [], [], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.ShimouFrame?.MouXunYuAnniversary?.Stage == MouXyStage.Target);
        s.Pick(c => c.Targets.SequenceEqual([0]));
        s.Need(s.ShimouFrame?.MouXunYuAnniversary is { Stage: MouXyStage.Discard, Actor: 0, Requested: 3, Selected.Count: 0 } &&
            s.P is { RequiredCardCount: 3, MinimumCardCount: 3 },
            "The real initial Yin self adjustment must require exactly three of the six current Hand cards.");
        var before = s.G.CardMovements.Count;
        s.Pick(c => c.Parameters.GetValueOrDefault("branch") == "add");
        s.Need(s.ShimouFrame!.MouXunYuAnniversary!.Selected.Count == 1 &&
            s.P is { RequiredCardCount: 2, MinimumCardCount: 2 } && s.G.CardMovements.Count == before,
            "One genuine native Shimou pick creates the unpaid partial selection needed by the reset recovery regression.");
        return s;
    }

    internal sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; set; } = game;
        public ContentRegistry Registry => registry;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public ProgramSkillFrame? Frame => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MouCaoShuangAnniversary is not null);
        public ProgramSkillFrame? ShimouFrame => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MouXunYuAnniversary is not null);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void UseItem(int option)
        {
            var card = V(0).Hand.First(c => c.Kind == CardKind.DrawTwo);
            var action = G.GetHumanLegalActions().First(a => a.CardId == card.Id && a.ConversionSource is null && a.Kind == LegalActionKind.DrawTwo);
            Accept(new PlayCardCommand(0, card.Id, action.TargetSeats, G.Revision, P!.PromptId));
            Reach(() => Frame?.MouCaoShuangAnniversary?.Stage == MouCaoShuangAnniversaryStage.Option);
            Pick(c => c.Parameters.GetValueOrDefault("item") == option.ToString());
        }
        public void Pick(Func<PromptChoice, bool> choose)
        { var p = P!; var c = p.Choices.First(choose); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c.Id, G.Revision)); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 150; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { Pick(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); continue; }
                    throw new InvalidOperationException("Unexpected actual Jianzhuan prompt. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded native Jianzhuan scene stays live."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missed the bounded native Jianzhuan boundary. " + Dump());
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "The original partial selection or paid-child frame cold-restores without adding a payment."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "All original native item and material commands replay the same real discards and separate recasts."); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + Dump()); }
        internal void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Native additional discard command rejected."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { P, Receipt = Frame?.MouCaoShuangAnniversary, Shimou = ShimouFrame?.MouXunYuAnniversary,
            Players = G.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.HandCount, p.Hp }) });
        internal static Scene Start(string generalId = Hero, int hp = 4, int handCount = 12)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(generalId));
            var setup = new ShanheBattleSetup(83, 1, "native-additional-discard", generalId, hp, 0, 0, 0, 0, [Watch],
                Enumerable.Range(1, handCount).Select(n => new ShanheStartingCard(CardKind.DrawTwo, Suit.Club, n)).ToArray(),
                [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.BaguaFormation, Suit.Spade, 2)],
                [new(Peer, Hp: 12) { StartingHand = [] }, new(Peer, Hp: 12) { StartingHand = [] }, new(Peer, Hp: 12) { StartingHand = [] }], [], new string('e', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 3 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(string generalId = Hero) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-native-additional-discard", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, generalId);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-discard-child","window":"discardPileReceived","subject":"owner","movementDiscardOnly":true,"discardOwnerScope":"own","movementReasons":["{{Reason}}"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Watch] = new { name = "实际弃牌子窗", description = "只有真正弃牌才进入此子窗口。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Quiet] = new { name = "安静同行", description = "保留正常回合而跳过出牌阶段。" } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静实际同行", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定真实无中牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:draw_two", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "额外原生弃牌选择", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [generalId, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
