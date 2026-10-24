using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5ZhangxiuChecks
{
    private const string Driver = "fixture:s5-zhangxiu-driver", Watch = "fixture:s5-zhangxiu-watch", Deck = "fixture:s5-zhangxiu-deck";
    private const string Xiongluan = StandardShanheS5ZhangxiuPackage.XiongluanId;
    private const string Activation = "abolish-random-slot-and-designate";

    public static void XiongluanRealAreaCostsRecoveryChildrenAndAlreadyAbolishedJudgmentCold()
    {
        var s = Scene.Start(false); s.Play();
        var lion = s.Card(CardKind.SilverLion); var lightning = s.Card(CardKind.Lightning);
        s.Native(lion, []); s.Play(); s.Native(lightning, [0]); s.Play(); s.Skill("armor-only"); s.Play();
        Need(s.V().Equipment.Any(c => c.Id == lion) && s.V().Judgment.Any(c => c.Id == lightning) && s.V().Hp == 7,
            "The actual armor and delayed card were natively used before the area cost; only the Armor slot remains available.");
        s.Xiongluan(); s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
        var paid = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShanheS5Xiongluan is not null);
        Need(paid.ShanheS5Xiongluan is { Paid: true, ChildrenCompleted: false, Slot: EquipmentSlot.Armor,
                CandidateCount: 1, JudgmentWasAbolished: false } invoice && invoice.RandomBefore != invoice.RandomAfter &&
            s.V().IsJudgmentAreaAbolished && s.V().Hp == 8 &&
            s.Zone(lion) == CardLocation.DiscardPile && s.Zone(lightning) == CardLocation.DiscardPile &&
            s.E<DirectedTurnCardPolicyGrantedEvent>().Length == 0,
            "The real random slot and judgment area are paid once; native Silver Lion recovery owns its actual child before benefits are issued.");
        s.Cold(); s.DrainToPlay();
        Need(s.E<ShanheS5XiongluanCostPaidEvent>() is [{ Slot: EquipmentSlot.Armor, CandidateCount: 1 }] &&
            s.G.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == lightning && m.From == CardLocation.Judgment(0) && m.To == CardLocation.DiscardPile) == 1 &&
            s.E<DirectedTurnCardPolicyGrantedEvent>().Single().Policy is { ActorSeat: 0, TargetSeat: 2, Effects:
                DirectedTurnCardPolicyEffect.IgnoreDistance | DirectedTurnCardPolicyEffect.BypassSlashLimit } &&
            s.E<HandCardColorRestrictionGrantedEvent>().Where(e => e.Restriction.ParentFrameId == paid.Id &&
                e.Restriction.AffectedSeat == 2).Select(e => e.Restriction.IsRed).Order().SequenceEqual([false, true]),
            "Only after both real area-movement children settle do the existing targeted distance/quota and Hand prohibitions become active.");
        foreach (var slash in s.V().Hand.Where(c => c.Kind == CardKind.Slash).Select(c => c.Id).ToArray())
        { s.Native(slash, [2]); s.Until(() => s.P?.SkillPrompt?.SkillId == Watch); s.Continue(); s.Play(); }
        Need(s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) == 2 &&
            s.V(2).Hand.Count(c => c.Kind == CardKind.Dodge) == 2,
            "With every equipment slot abolished, two genuinely distant native Slashes still use the paid policy, and the target cannot spend either real Hand Dodge.");
        s.Cold();

        var repeat = Scene.Start(false, already: true); repeat.Play(); repeat.Skill("already-abolished"); repeat.Play();
        for (var turn = 0; turn < 2; turn++)
        {
            if (turn > 0) repeat.NextOwnPlay();
            Need(repeat.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Xiongluan),
                "A previously abolished judgment area does not disable S5 Xiongluan while a real equipment slot remains.");
            repeat.Xiongluan(); repeat.DrainToPlay();
            Need(repeat.E<ShanheS5XiongluanCostPaidEvent>().Length == turn + 1 &&
                repeat.E<ShanheS5XiongluanCostPaidEvent>().Last() is { JudgmentWasAbolished: true, CandidateCount: var count } && count == 2 - turn,
                "Each distinct actual Play pays exactly one randomly chosen remaining slot; this is a phase limit, not a game-once skill.");
            repeat.Cold();
        }
        repeat.NextOwnPlay();
        Need(!repeat.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Xiongluan),
            "On a fresh actual Play, no remaining equipment slot means no payable activation.");
    }

    public static void LeizhenNativeHandMaterialsThirdSlashNonstackAndUnequippedBenefitCold()
    {
        var s = Scene.Start(true); s.Play();
        var blade = s.Card(CardKind.Leizhen); var spear = s.Card(CardKind.ZhangbaSerpentSpear);
        Need(!s.V().Skills!.Any(k => k.ContentId == StandardShanheS5ZhangxiuPackage.LeizhenSkillId),
            "The fixed Leizhen starts as an extra real Hand entity and grants no skill before its native equipment Use.");
        s.Skill("two-weapons"); s.Play(); s.Native(blade, []); s.Play(); s.Native(spear, []); s.Play();
        Need(s.V().Equipment.Any(c => c.Id == blade) && s.V().Equipment.Any(c => c.Id == spear) &&
            s.E<ShanheLeizhenHandUseCountedEvent>() is [{ MaterialCount: 1 }],
            "The natively installed Leizhen counts the later spear's Hand payment, not its own earlier installation. " +
            JsonSerializer.Serialize(new { Counted = s.E<ShanheLeizhenHandUseCountedEvent>(), Uses = s.E<CardUsedEvent>(), s.G.CardMovements }));
        var pair = s.V().Hand.Where(c => c.Kind == CardKind.Peach).Take(2).Select(c => c.Id).ToArray();
        s.Accept(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, pair, [2], s.G.Revision, s.P!.PromptId));
        s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
        var third = s.G.ResolutionStack.OfType<CardUseFrame>().Single();
        Need(third.ShanheLeizhen is null && s.IncomingPrediction(2).DamageAmount == 1 &&
            s.E<ShanheLeizhenHandUseCountedEvent>().Last() is { MaterialCount: 2, RemainderBefore: 1, RemainderAfter: 0 } &&
            s.E<ShanheLeizhenChargeEarnedEvent>().Length == 1,
            "One genuine two-Hand-material conversion reaches three materials, but that same third Slash cannot consume the benefit it just earned.");
        s.Cold(); s.Continue(); s.Play();
        foreach (var kind in new[] { CardKind.SilverLion, CardKind.OffensiveHorse, CardKind.DefensiveHorse })
        { s.Native(s.Card(kind), []); s.Play(); }
        Need(s.E<ShanheLeizhenHandUseCountedEvent>().Sum(e => e.MaterialCount) == 6 &&
            s.E<ShanheLeizhenChargeEarnedEvent>().Length == 1 && s.E<ShanheLeizhenChargeSpentEvent>().Length == 0,
            "Three more native Hand equipment Uses complete another cycle while charged, without stacking a second benefit.");
        s.Skill("drop-equipment"); s.DrainToPlay();
        Need(s.V().Equipment.Count == 0 && !s.V().Skills!.Any(k => k.ContentId == StandardShanheS5ZhangxiuPackage.LeizhenSkillId) &&
            s.E<ShanheLeizhenHandUseCountedEvent>().Sum(e => e.MaterialCount) == 6 &&
            s.E<ShanheLeizhenChargeEarnedEvent>().Length == 1,
            "Real equipment discard removes the source without counting as a Hand Use or erasing its already-paid next-Slash benefit.");
        var slash = s.Card(CardKind.Slash);
        var candidate = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash &&
            a.CardId == slash && a.TargetSeats.SequenceEqual([3]));
        Need(s.G.PredictPublicActionDamage(0, candidate, 3).DamageAmount == 2 &&
            s.E<ShanheLeizhenChargeSpentEvent>().Length == 0,
            "The real proposed native Slash publicly predicts the saved benefit after unequipping, without consuming it before declaration.");
        s.Native(slash, [3]); s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
        var next = s.G.ResolutionStack.OfType<CardUseFrame>().Single();
        Need(next.ShanheLeizhen is { BaseApplied: true, BaseBefore: 1, BaseAfter: 2 } &&
            s.IncomingPrediction(3).DamageAmount == 2 && s.E<ShanheLeizhenChargeSpentEvent>().Length == 1 &&
            s.E<ShanheLeizhenHandUseCountedEvent>().Sum(e => e.MaterialCount) == 6,
            "The subsequent genuine Slash consumes the saved benefit once after unequipping; its public incoming prediction and initial native base both equal two.");
        s.Cold(); s.Continue(); s.Play();
        Need(s.E<DamageAppliedEvent>().Where(e => e.SourceSeat == 0).Select(e => e.Amount).SequenceEqual([1, 2]) &&
            s.V(2).Hp == 19 && s.V(3).Hp == 18 &&
            s.E<ShanheLeizhenBaseAppliedEvent>().Length == 1,
            "Cold continuation pays the third Slash's original one and the later Slash's earned two, without double spending or self-boosting.");
        s.Cold();
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Peer(int i) => "fixture:s5-zhangxiu-peer-" + i;
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Card(CardKind kind) => V().Hand.First(c => c.Kind == kind).Id;
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Accept(GameCommand c)
        {
            var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());
            if (!r.Accepted) throw new InvalidOperationException((r.Error?.ToString() ?? "Rejected S5 Zhang Xiu native command.") + " " +
                JsonSerializer.Serialize(new { Command = CommandJson.Serialize([c]), P, Legal = G.GetHumanLegalActions(), Recent = G.Events.TakeLast(5).Select(e => e.Payload) }));
        }
        public void Native(int card, int[] targets) => Accept(new PlayCardCommand(0, card, targets, G.Revision, P!.PromptId));
        public void Skill(string id) => Accept(new UseProgramSkillCommand(0, Driver, id, [], [], G.Revision, P!.PromptId));
        public void Xiongluan() => Accept(new UseProgramSkillCommand(0, StandardShanheS5ZhangxiuPackage.XiongluanId, Activation, [], [2], G.Revision, P!.PromptId));
        public void Continue() { var p = P!; var c = p.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c.Id, G.Revision)); }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void DrainToPlay()
        { Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }, true); }
        public void NextOwnPlay() { Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId)); DrainToPlay(); }
        public void Until(Func<bool> ready, bool drainWatch = false)
        {
            for (var i = 0; i < 240; i++)
            {
                if (ready()) return;
                Need(G.State.Winner == Winner.None, "The small native Zhang Xiu fixture ended before its expected boundary.");
                if (P is { PlayerSeat: 0 })
                {
                    if (drainWatch && P.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                    throw new InvalidOperationException("Unexpected S5 Zhang Xiu boundary: " + JsonSerializer.Serialize(new { P, Frames = G.ResolutionStack.Select(f => f.GetType().Name), Recent = G.Events.TakeLast(4).Select(e => e.Payload.GetType().Name) }));
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing native S5 Zhang Xiu boundary: " + JsonSerializer.Serialize(P));
        }
        public PublicDamagePrediction IncomingPrediction(int viewer)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            var attack = typeof(GameEngine).GetProperty("ActiveCardAttack", f)!.GetValue(G);
            return (PublicDamagePrediction)typeof(GameEngine).GetMethod("PredictPublicIncomingDamage", f)!.Invoke(G, [attack, viewer, null, null, null])!;
        }
        public void Cold()
        {
            string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(g.CreateSnapshot(i))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Zones = g.CreateCardZoneDiagnostics() });
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State(G) == State(restored), "Cold replay preserves the exact paid area/random/movement parent and earned or spent Leizhen native-use benefit."); G = restored;
        }
        public static Scene Start(bool weapon, bool already = false)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
                new StandardClassicGeneralPackage(), new StandardShanheS5ZhangxiuPackage(), new Fixture(weapon));
            ShanheStartingCard[] cards = weapon
                ? [new(CardKind.Leizhen, Suit.Spade, 1), new(CardKind.ZhangbaSerpentSpear, Suit.Spade, 12), new(CardKind.SilverLion, Suit.Club, 1),
                   new(CardKind.OffensiveHorse, Suit.Heart, 5), new(CardKind.DefensiveHorse, Suit.Spade, 5), new(CardKind.Slash, Suit.Heart, 7)]
                : already ? [] : [new(CardKind.SilverLion, Suit.Club, 1), new(CardKind.Lightning, Suit.Spade, 1), new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Heart, 7)];
            var setup = new ShanheBattleSetup(59, 1, "native-zhangxiu", StandardShanheS5ZhangxiuPackage.ZhangxiuId, weapon || already ? 8 : 7,
                4, 0, 0, 0, [Driver, Watch], cards, [], [new(Peer(2)) { StartingHand = weapon || already ? [] :
                    [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Heart, 3)] }, new(Peer(3)) { StartingHand = [] }],
                [new(Peer(1)) { StartingHand = [] }], new string('d', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, HumanRole = null, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture(bool weapon) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-s5-zhangxiu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var c = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"modifiers":{{(weapon ? "[{\"id\":\"slash-quota\",\"query\":\"slashLimit\",\"operation\":\"add\",\"value\":10,\"priority\":0}]" : "[]")}},"activations":[
              {"id":"armor-only","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["weapon","offensiveHorse","defensiveHorse","treasure"],"amount":0}]},
              {"id":"already-abolished","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["offensiveHorse","defensiveHorse","treasure"],"amount":0},{"op":"abolishOwnerAreas","target":"owner","zones":["judgment"]}]},
              {"id":"two-weapons","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["weapon"],"amount":2}]},
              {"id":"drop-equipment","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["equipment"]}]}]},
             {"id":"{{Watch}}","revision":1,"triggers":[
              {"id":"native-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"use","options":[{"id":"continue"}]}]},
              {"id":"native-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"heal","options":[{"id":"continue"}]}]},
              {"id":"area-discard","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementReasons":["equipment.slot-abolished","shanhe.s5.xiongluan.abolish-judgment"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            { [Driver] = new { name = "真实原生区域驱动", description = "已有native机制" }, [Watch] = new { name = "原生实付孩子", description = "真实付款与用牌", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, id, "真实新增行为检查") { Program = p, ProgramPresentation = c.Presentations[id] });
            for (var i = 1; i < 4; i++) b.AddGeneral(new(Peer(i), "实际原生目标", "supporter", "standard:none", "wei", 20));
            b.AddDeck(new(Deck, "小真实牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 3)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "张绣原生专版", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [StandardShanheS5ZhangxiuPackage.ZhangxiuId, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
