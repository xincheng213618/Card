using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryYuanYinChecks
{
    private const string Hero = "anniversary:yuan-yin", Moshou = "anniversary:moshou", Yunjiu = "anniversary:yunjiu", Plain = "fixture:yuan-yin-plain",
        NoDraw = "fixture:yuan-yin-no-draw", Quiet = "fixture:yuan-yin-quiet", Driver = "fixture:yuan-yin-driver", DrawHold = "fixture:yuan-yin-native-draw", PaidHold = "fixture:yuan-yin-paid-movement";
    public static void ActualBlackSelfTargetsNativeDrawCycleDeclineAndCold()
    {
        var s = Scene.Start(); s.Play();
        var black = s.V().Hand!.Where(c => c.Kind == CardKind.DrawTwo && c.Suit == Suit.Club).Select(c => c.Id).ToArray();
        var red = s.V().Hand!.Single(c => c.Kind == CardKind.DrawTwo && c.Suit == Suit.Heart).Id;
        s.Native(black[0]); s.Offer(Moshou); s.Skip(); s.Play();
        s.Need(!s.E<AnniversaryYuanYinMoshouIssuedEvent>().Any(), "Declining the original black self-target offer never consumes a Moshou cycle step.");
        s.Native(red); s.Play();
        s.Need(!s.E<AnniversaryYuanYinMoshouIssuedEvent>().Any(), "A genuinely red native self-target card never offers or advances Moshou.");
        var expected = new[] { 3, 2, 1, 3 };
        for (var i = 0; i < expected.Length; i++)
        {
            s.Native(black[i + 1]); s.Offer(Moshou); s.Activate();
            if (i == 0)
            {
                s.HoldDraw(); var root = s.Root!.Id;
                s.Need(s.Root.AnniversaryYuanYin is { Mode: "moshou", Stage: "drawing", Requested: 3 } &&
                    s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f => f.Context.ParentFrameId == root && f.Context.RequestedCount == 3),
                    "The first accepted real black target freezes MaxHP3 and issues its exact native draw before paying the held child.");
                s.Cold(); s.Continue(); s.HoldPaid();
                s.Need(s.Root is { Id: var paidRoot, AnniversaryYuanYin: { Stage: "draw-children", Draw.ActualDrawIds.Count: 3 } } && paidRoot == root &&
                    s.E<AnniversaryYuanYinMoshouAdvancedEvent>().Count(e => e.FrameId == root && e.Requested == 3 && e.Actual == 3) == 1,
                    "The real native draw pays before its movement observer, with the typed result and next count on the original frame.");
                s.Cold(); s.Continue();
            }
            s.Play();
            s.Need(s.E<AnniversaryYuanYinMoshouAdvancedEvent>().Select(e => e.Requested).SequenceEqual(expected.Take(i + 1)) &&
                s.E<AnniversaryYuanYinMoshouIssuedEvent>().Length == i + 1,
                "Accepted actual self-target uses follow 3, 2, 1, reset-to-current-MaxHP3 exactly once after both native cold returns.");
        }
        s.Need(s.E<AnniversaryYuanYinMoshouAdvancedEvent>().Select(e => e.Next).SequenceEqual([2, 1, 0, 2]) &&
            s.E<CardUseFinishedEvent>().Count(e => black.Contains(e.CardId) || e.CardId == red) == 6,
            "All six original native card uses finish, and only the four accepted black-card targets advance the cycle.");
    }
    public static void NativeDeathOwnedGiftMaximumRecoveryAndPaidCold()
    {
        var s = Scene.Start(death: true); s.Play();
        var victimCards = s.V(1).Hand!.Select(c => c.Id).ToArray();
        var black = s.V().Hand!.Single(c => c.Kind == CardKind.DrawTwo).Id;
        s.Kill(1); s.Offer(Yunjiu); s.Activate(); s.Stage("recipient"); var root = s.Root!.Id;
        var receipt = s.Root.AnniversaryYuanYin!;
        s.Need(!s.V(1).IsAlive && victimCards.All(id => s.Zone(id) == CardLocation.Hand(1)) &&
            s.G.ResolutionStack.OfType<ProgramKillTriggerWindowFrame>().Single(f => f.Id == receipt.ParentId).BeforeDeathCleanup &&
            s.G.ResolutionStack.OfType<DeathFrame>().Single(f => f.Id == receipt.DeathId).CleanedUpCardIds.Count == 0 &&
            !s.E<PlayerDiedEvent>().Any(e => e.VictimSeat == 1),
            "Yunjiu runs on the original confirmed death before native cleanup or PlayerDied publication, while the actual dead owner still holds both original entities.");
        s.Answer(c => c.Targets.SequenceEqual([2])); s.Stage("card");
        s.Need(s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand" && c.Cards.Count == 0) &&
            !s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("source-zone") == "Judgment"),
            "The living Yuan Yin privately chooses the dead owner's actual owned card by an opaque Hand slot rather than exposing its identity or requesting a dead player's input.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("slot-index") == "0"); s.HoldPaid();
        var paid = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == root).AnniversaryYuanYin!;
        var given = paid.CardId!.Value;
        s.Need(paid is { Stage: "gift-children", Delivered: true, Gain.Movements.Count: 1 } && s.Zone(given) == CardLocation.Hand(2) &&
            s.E<AnniversaryYuanYinGiftPaidEvent>().Count(e => e.FrameId == root && e.Delivered) == 1 &&
            !s.E<AnniversaryYuanYinBenefitIssuedEvent>().Any(e => e.FrameId == root) && s.G.ResolutionStack.OfType<DeathFrame>().Single(f => f.Id == paid.DeathId).CleanedUpCardIds.Count == 0,
            "The exact dead-owned entity is genuinely delivered once before its paid native observer; maximum HP, recovery and death cleanup still wait on that original child.");
        s.Cold(); s.Reach(() => s.E<AnniversaryYuanYinCompletedEvent>().Any(e => e.FrameId == root)); s.Play();
        s.Need(s.V().MaxHp == 4 && s.V().Hp == 3 && s.E<AnniversaryYuanYinBenefitIssuedEvent>().Where(e => e.FrameId == root).Select(e => e.Benefit).SequenceEqual(["maximum-hp", "recovery"]) &&
            s.E<MaximumHpChangedEvent>().Count(e => e.PlayerSeat == 0 && e.Delta == 1 && e.SkillId == Yunjiu) == 1 &&
            s.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1,
            "The paid native gift returns before exactly one real MaxHP+1 and then one native Recover1, including after cold restoration.");
        s.Need(s.E<AnniversaryYuanYinDeathWindowIssuedEvent>().Length == 1 && s.E<AnniversaryYuanYinDeathWindowCompletedEvent>().Length == 1 &&
            s.E<PlayerDiedEvent>().Count(e => e.VictimSeat == 1) == 1 && s.Zone(given) == CardLocation.Hand(2) &&
            s.G.CardMovements.Count(m => m.CardId == given && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(2) && m.Reason.Value == "skill-program.anniversary:yunjiu.give") == 1 &&
            victimCards.Where(id => id != given).All(id => s.Zone(id) == CardLocation.DiscardPile) &&
            !s.G.CardMovements.Any(m => m.CardId == given && m.Reason == CardMoveReasons.DeathDiscard),
            "The issued pre-cleanup window returns once to the original death: only remaining owned cards are discarded, and the already transferred entity is never discarded or paid twice.");
        s.Native(black); s.Offer(Moshou); s.Activate(); s.Play();
        s.Need(s.E<AnniversaryYuanYinMoshouIssuedEvent>() is [{ Requested: 4 }], "A later first Moshou uses the real increased MaxHP4 produced by Yunjiu.");
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryYuanYin is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Offer(string skill) => Reach(() => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") != "activate");
        public void Stage(string stage) => Reach(() => Root?.AnniversaryYuanYin?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-yuan-yin") == true);
        public void HoldDraw() => Reach(() => P?.SkillPrompt?.SkillId == DrawHold && Root?.AnniversaryYuanYin?.Stage == "drawing");
        public void HoldPaid() => Reach(() => P?.SkillPrompt?.SkillId == PaidHold);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Kill(int seat) => Accept(new UseProgramSkillCommand(0, Driver, "native-death", [], [seat], G.Revision, P!.PromptId));
        public void Native(int id)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id); Need(a is not null, "The fixed native card remains a genuine legal use.");
            Accept(new PlayCardCommand(0, id, a!.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only the actual Human chooser can answer the canonical current choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId is DrawHold or PaidHold) { Continue(); continue; }
                    throw new InvalidOperationException("Unexpected original Yuan Yin prompt at " + line + Dump());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Bounded native Yuan Yin flow not reached at " + line + Dump());
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
                "Cold preserves all private choices and exact paid native draw/death/gift frame state without repeating an issued operation.");
        }
        public void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Yuan Yin command."); }
        public void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P, Root = Root?.AnniversaryYuanYin,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).MaxHp, V(i).IsAlive, V(i).HandCount }),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start(bool death = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            IReadOnlyList<ShanheStartingCard> cards = death ? [new(CardKind.DrawTwo, Suit.Club, 7)] :
                Enumerable.Range(0, 5).Select(i => new ShanheStartingCard(CardKind.DrawTwo, Suit.Club, i + 1)).Append(new(CardKind.DrawTwo, Suit.Heart, 7)).ToArray();
            ShanheStartingCard[] peer = [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Diamond, 3)];
            var setup = new ShanheBattleSetup(41, 1, "yuan-yin-native-boundaries", Hero, 2, 0, 0, 0, 0, [NoDraw, Driver, DrawHold, PaidHold], cards, [],
                [new(Plain, Hp: 8) { StartingHand = peer }, new(Plain, Hp: 8) { StartingHand = peer }], [new(Plain, Hp: death ? 1 : 8) { StartingHand = peer }], new string('c', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-yuan-yin", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"none","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"hold","query":"handLimit","operation":"add","value":40,"priority":0}],"activations":[{"id":"native-death","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
             {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"native-draw","window":"nativeDrawStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held-draw","options":[{"id":"continue"}]}]}]},
             {"id":"{{PaidHold}}","revision":1,"triggers":[{"id":"paid-movement","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:moshou.draw","skill-program.anniversary:yunjiu.give"],"priority":1000,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held-paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object>();
            foreach (var id in new[] { NoDraw, Quiet, Driver }) names[id] = new { name = "袁胤原生固定输入", description = "原生回合与伤害" };
            foreach (var id in new[] { DrawHold, PaidHold }) names[id] = new { name = "袁胤原生暂停", description = "实际请求或已付观察者", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Plain, "固定同桌", "supporter", NoDraw, "wei", 8, AdditionalSkillIds: [Quiet, PaidHold]));
            const string deck = "fixture:yuan-yin-deck";
            b.AddDeck(new(deck, "固定原生闪", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "袁胤原生目标与死亡", 4, 4, new Dictionary<string, int>(), deck, GeneralPoolIds: [Hero, Plain], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
