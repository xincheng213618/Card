using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenTaishiCiChecks
{
    private const string Hero = "happy:shen-taishi-ci", Driver = "fixture:happy-tsc-driver", Quiet = "fixture:happy-tsc-quiet",
        NoDraw = "fixture:happy-tsc-no-draw", DrawHold = "fixture:happy-tsc-draw", GainHold = "fixture:happy-tsc-gain",
        Peer = "fixture:happy-tsc-peer", Caster = "fixture:happy-tsc-caster", Deck = "fixture:happy-tsc-deck";

    public static void FirstDyingEqualHpJudgmentAndForeignEndingRoundAwakeningResumeCold()
    {
        var s = Scene.Start(); s.Play();
        var peach = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id;
        var paidCard = s.V().Hand.Single(c => c.Kind == CardKind.Dodge && c.Suit == Suit.Club).Id;
        var physicalSlash = s.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
        var equipment = s.V().Equipment.Single().Id;
        s.Need(s.V().Hp == 4 && s.Wei().SequenceEqual([0, 1, 1, 1]), "The real four-HP general initializes all three other living characters.");
        s.Use("collapse"); s.Play();
        s.Need(s.V().Hp == 3 && s.Wei().SequenceEqual([0, 1, 1, 1]) && s.V().Equipment.Any(c => c.Id == equipment) &&
            s.E<HappyTaishiCiDulieRecoveredEvent>() is [{ WeiCount: 3 }] && s.E<ShenTaishiCiPoweiMissionEvent>().Length == 0,
            "Its first genuine dying entry recovers to three field Wei while retaining the Wei and real equipment; it never executes the mobile mission failure.");
        s.Use("heal"); s.Play(); s.Use("collapse");
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.V().Hp == 0 && s.E<PlayerDyingEvent>().Count(e => e.VictimSeat == 0) == 2 && s.E<HappyTaishiCiDulieRecoveredEvent>().Length == 1 && s.Wei().Sum() == 3,
            "A second actual dying entry with Wei still present does not repeat the first-entry recovery.");
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "peach" && c.Cards.SequenceEqual([peach])); s.Play();
        s.Use("heal"); s.Play(); s.Use("tap", [2]); s.Play();
        s.Need(s.V().Hp == 4 && s.Wei().SequenceEqual([0, 1, 0, 1]) && s.E<HappyTaishiCiWeiRemovedEvent>() is [{ Count: 1, Round: 1 }],
            "A real first-round damage removes only that victim's Wei and records one removal, after the signed-HP rescue returns.");
        s.End(); s.Reach(() => s.Chongwei(1));
        s.Need(s.Wei().SequenceEqual([0, 1, 0, 1]) && s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "take" && c.Cards.Count == 0),
            "Another character's start preserves the current ring and offers a genuine opaque eligible hand slot, rather than rotating all Wei.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); s.Play();
        var cancelled = s.E<ShenTaishiCiDulieCancelledEvent>().Single();
        s.Need(s.V().Hp == 4 && s.V(3).Hp == 4 && cancelled.RemainingTargetCount == 0 &&
            s.E<ProgramJudgmentOutcomeRecordedEvent>().Any(e => e.ProgramFrameId == cancelled.FrameId && e.Outcome is { Suit: Suit.Heart, Reason: "skill.happy.dulie" }) &&
            s.E<CardUseDeclaredEvent>().Any(e => e.ResolutionId == cancelled.UseId && e.SourceSeat == 3) &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == cancelled.UseId) == 1 && !s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 0) &&
            s.Wei().SequenceEqual([0, 1, 1, 0]),
            "An equal-HP other's actual Slash is really judged and cancelled; only the next own-turn prelude simultaneously migrates the two remaining Wei and skips self.");
        s.Use("tap", [1]); s.Play(); s.End(); s.Reach(() => s.Chongwei(2));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "damage" && c.Cards.SequenceEqual([paidCard]));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == DrawHold && s.Pending(SkillProgramEffectOp.AwakenHappyTaishiCiPowei) is not null);
        var awake = s.Pending(SkillProgramEffectOp.AwakenHappyTaishiCiPowei)!; var awakeId = awake.Id;
        var draw = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == awake.Id);
        var handBeforeDraw = s.V().HandCount;
        var paid = s.E<ShenTaishiCiPoweiPaidEvent>().Single(e => e.Branch == "damage");
        s.Need(s.G.State.CurrentSeat == 2 && s.Wei().All(n => n == 0) && awake.HappyTaishiCi is { Count: 2, Round: 2, Stage: HappyTaishiCiStage.Drawing } &&
            draw.Context.RequestedCount == 2 && s.E<HappyTaishiCiAwakenedEvent>() is [{ DrawCount: 2, Round: 2 }] &&
            paid.CardId == paidCard && paid.From == CardLocation.Hand(0) && paid.To == CardLocation.DiscardPile &&
            s.G.CardMovements.Count(m => m.Sequence == paid.Sequence && m.CardId == paidCard && m.Reason.Value == "skill-program.shen-taishi-ci.powei.discard") == 1 &&
            s.E<ShenTaishiCiPoweiRangeEvent>().Any(e => e.AttackerSeat == 2 && e.OwnerSeat == 0 && e.ActualTurn == s.G.State.TurnNumber),
            "The real marked-turn payment clears the last Wei, then that foreign turn's ending immediately awakens: only this round's two removals count, and relocation deltas and last round's removal do not.");
        s.Cold(); s.Skip(); s.Reach(() => s.E<HappyTaishiCiDrawReturnedEvent>().Any(e => e.FrameId == awakeId));
        s.Need(s.V().HandCount == handBeforeDraw + 2 && s.V().Skills!.Any(k => k.ContentId == "happy:shenzhu") &&
            s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == draw.Id && e.ParentFrameId == awakeId && e.RequestedCount == 2 && !e.Replaced) == 1,
            "The original awakening's real grant and two-card native draw survive a pending draw child cold return without duplicate payment or grant.");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == GainHold && s.Pending(SkillProgramEffectOp.ObtainHappyTaishiCiStartSlash) is not null);
        var start = s.Pending(SkillProgramEffectOp.ObtainHappyTaishiCiStartSlash)!; var startReceipt = start.HappyTaishiCi!;
        s.Need(startReceipt.CardId is { } gained && s.V().Hand.Any(c => c.Id == gained && c.Kind == CardKind.Slash) &&
            s.G.CardMovements.Count(m => m.CardId == gained && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill.happy.shenzhu.start-slash") == 1,
            "The next actual own-turn prelude obtains its real first printed Slash and pauses on its original gained-card child.");
        s.Cold(); s.Skip(); s.Play();
        var action = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId == physicalSlash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([3]));
        s.Accept(new PlayCardCommand(0, physicalSlash, action.TargetSeats, s.G.Revision, s.P!.PromptId, action.PlayedCardKind));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "happy:shenzhu");
        var beforeChoice = s.V().HandCount;
        s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "draw-one"); s.Play();
        s.Need(s.V().HandCount == beforeChoice + 1 && s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([3])) &&
            s.E<HappyTaishiCiAwakenedEvent>().Length == 1 && s.E<HappyTaishiCiDulieRecoveredEvent>().Length == 1 &&
            s.G.CardMovements.Count(m => m.CardId == startReceipt.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill.happy.shenzhu.start-slash") == 1,
            "The acquired skill actually resolves the original physical Slash's one-card/extra-Slash choice; cold never repeats the start Slash, first dying recovery or awakening.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-taishi-ci", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"collapse","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":4}]},
                  {"id":"heal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":3}]},
                  {"id":"tap","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damageParticipants","target":"selectedTarget","amount":1}]}]},
                 {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                 {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
                 {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"native-draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":false}]}]},
                 {"id":"{{GainHold}}","revision":1,"triggers":[{"id":"start-slash-gain","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.happy.shenzhu.start-slash"],"optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":false}]}]}
                ]}
                """;
            var labels = new[] { Driver, Quiet, NoDraw, DrawHold, GainHold }.ToDictionary(id => id, id => new { name = id, description = "真实濒死、轮次与原生付款返回小局" });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实安静同伴", "", Quiet, "wu", 4, [NoDraw]));
            b.AddGeneral(new(Caster, "等体力真实杀来源", "", NoDraw, "wei", 4));
            b.AddDeck(new(Deck, "固定红桃杀牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐太史慈原生小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer, Caster], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int[] Wei() => Enumerable.Range(0, 4).Select(i => V(i).Markers?.FirstOrDefault(m => m.Kind == PlayerMarkerKind.ShenTaishiCiWei)?.Count ?? 0).ToArray();
        public ProgramSkillFrame? Pending(SkillProgramEffectOp op) => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.HappyTaishiCi?.Op == op);
        public bool Chongwei(int seat) => P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == "happy:chongwei" && p.TargetSeat == seat && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cancel");
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var prompt = P!; var choice = prompt.Choices.FirstOrDefault(predicate);
            Need(choice is not null, "Missing exact current Happy Taishi Ci choice.");
            Accept(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice!.Id, G.Revision));
        }
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 280; step++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "Unexpected terminal Happy Taishi Ci fixture.");
                if (P is not { PlayerSeat: 0 } prompt) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (prompt.SkillPrompt?.SkillId is DrawHold or GainHold) Skip();
                else if (prompt.SkillPrompt?.SkillId == "happy:chongwei") Answer(c => c.Parameters.GetValueOrDefault("branch") == "cancel");
                else if (prompt.Kind == DecisionKind.DiscardCards) Accept(new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, G.Revision));
                else throw new InvalidOperationException("Unexpected Happy Taishi Ci boundary " + JsonSerializer.Serialize(prompt));
            }
            throw new InvalidOperationException("Happy Taishi Ci missed its fixed short native flow " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) &&
                frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))), "Cold preserves the exact original source, round count, native pending draw/gain and all player views.");
        }
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Happy Taishi Ci command.");
        }
        public void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P,
                Wei = Wei(), Hp = Enumerable.Range(0, 4).Select(i => V(i).Hp), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
                Facts = G.Events.TakeLast(22).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) }));
        }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(31, 1, "happy-tsc-native", Hero, 4, 0, 0, 0, 0, [Driver, NoDraw, DrawHold, GainHold],
                [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Dodge, Suit.Diamond, 3), new(CardKind.Peach, Suit.Heart, 4)],
                [new(CardKind.QinggangSword, Suit.Club, 1)],
                [new(Peer) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 8)] }, new(Caster) { StartingHand = [new(CardKind.Slash, Suit.Spade, 9)] }],
                [new(Peer) { StartingHand = [new(CardKind.Dodge, Suit.Club, 8)] }], new string('a', 64)) { DefaultGodFactionId = "wu" };
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
}
