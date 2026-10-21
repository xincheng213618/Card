using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WushuangCombatChecks
{
    private const string Driver = "fixture:wushuang-combat-driver", Quiet = "fixture:wushuang-combat-quiet", Human = "fixture:wushuang-combat-human";
    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentRegistry.Build(new Fixture(StandardWushuangContentRegistry.Create())));

    public static void NoArmorFixedDamageAndFiniteReduction()
    {
        Require(!WushuangTactics.ById[2193].IsAvailableInShop && !WushuangTactics.ById[2177].IsAvailableInShop,
            "Armor bypass and double-armor damage cannot occupy a single-duel shop slot.");
        foreach (var item in new (int Seed, int Tactic, int Armor, int Lost, int Amount)[] { (36, 2210, 0, 2, 2) })
        {
            var g = Start(item.Seed, item.Tactic); var hp = Player(g, 1).Hp;
            Use(g, "hit-three", 1); Play(g);
            var damage = E<DamageAppliedEvent>(g).Last(e => e.SourceSeat == 0 && e.TargetSeat == 1);
            Require(damage.Amount == item.Amount && Player(g, 1).Armor == item.Armor && Player(g, 1).Hp == hp - item.Lost,
                "The fixed two-point damage pays real HP in an armor-free single duel.");
            Cold(g);
        }
        {
            var g = Start(53, 2196); var before = E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 0);
            Use(g, "hit-one", 0); Play(g); Use(g, "hit-one", 0); Play(g); Cold(g);
            Require(E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 0) == before,
                "The first two incoming one-point segments are reduced to zero without inventing a damage application.");
            Use(g, "hit-one", 0); Play(g);
            Require(E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 0) == before + 1 && Player(g, 0).Armor == 0 &&
                E<WushuangIncomingReductionIssuedEvent>(g).Count(e => e.TargetSeat == 0) == 3,
                "Zero damage consumes the finite per-round reduction allowance; a third hit is not prevented indefinitely.");
            Cold(g);
        }
    }

    public static void DrawTwoAndAdditionalDismantlementKeepNativePaymentsCold()
    {
        {
            var g = Start(1, 2030); Use(g, "obtain-draw"); Play(g);
            var hand = Player(g, 0).HandCount; var use = Card(g, CardKind.DrawTwo); Play(g);
            Require(Player(g, 0).HandCount == hand + 2 && E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == use) == 1,
                "A genuine DrawTwo pays one physical card and draws exactly three, with one accepted card use.");
            Cold(g);
        }
        {
            var g = Start(280, 2239); Use(g, "obtain-dismantlement"); Play(g);
            var hand = Player(g, 1).HandCount; var use = Card(g, CardKind.Dismantlement);
            Until(g, () => P(g)?.Kind == DecisionKind.SelectTargetCard); Cold(g);
            Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == "0");
            Until(g, () => g.ResolutionStack.OfType<TargetCardSelectionFrame>().Any(f => f.ExtraForeignSelection?.AllowFinish == true));
            Require(E<TargetCardDiscardedEvent>(g).Count(e => e.ResolutionId == use) == 1,
                "The additional selection is owned by the original already-paid Dismantlement.");
            Cold(g); Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == "0"); Play(g);
            Require(Player(g, 1).HandCount == hand - 2 && E<TargetCardDiscardedEvent>(g).Count(e => e.ResolutionId == use) == 2 &&
                E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == use) == 1,
                "The optional second discard moves another real target card without paying or declaring Dismantlement again.");
            Cold(g);
        }
    }

    public static void RecastAndActualDrawThresholdResumeTheirPaidInstruction()
    {
        {
            var g = Start(42, 2040); Use(g, "obtain-slash"); Play(g); Card(g, CardKind.Slash);
            Until(g, () => P(g)?.SkillPrompt?.SkillId == "wushuang:tactic-2040");
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Until(g, () => g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.OwnedCardSelection is not null));
            Cold(g);
            for (var i = 0; i < 2; i++) { Answer(g, c => c.Cards.Count == 1); Until(g, () => P(g)?.PlayerSeat == 0); }
            if (g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.OwnedCardSelection is not null))
                Answer(g, c => c.Cards.Count == 0);
            Play(g);
            var recasts = E<CardRecastEvent>(g).Where(e => e.ActorSeat == 0).ToArray();
            Require(recasts.Length == 2 && recasts.Select(e => e.CardId).Distinct().Count() == 2 &&
                g.CardMovements.Count(m => m.From == CardLocation.Hand(0) && m.Reason == CardMoveReasons.RecastDiscard) == 2 &&
                g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.RecastDraw) == 2,
                "Two exact recast materials each leave once, and the single paid recast draws exactly two cards.");
            Cold(g);
        }
        {
            var g = Start(11, 2104); var before = E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1);
            Use(g, "obtain-draw"); Play(g);
            Require(E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1) == before,
                "Obtaining a chosen pile entity does not count as drawing it.");
            Use(g, "draw-nine"); Play(g);
            Require(E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1) == before + 1,
                "Nine actual draw movements issue one skill damage, even when a gain child suspends the producer.");
            Cold(g);
        }
    }

    public static void LightningJudgmentsUseNativeReplacementAndSourcelessDamage()
    {
        var g = Start(3, 2019);
        Require(E<JudgmentResolvedEvent>(g).Any(e => e.Reason == "闪电" && e.Suit == Suit.Spade && e.Rank == 2) &&
            E<DamageAppliedEvent>(g).Any(e => e.SourceLess && e.Amount == 3 && e.Nature == DamageNature.Thunder) && !g.IsFaulted,
            "The round's actual Judgment phase resolves a replaceable Lightning judgment and a genuine source-less Thunder damage segment. " + Debug(g));
        Cold(g);
    }

    public static void DyingExitDamageWaitsForItsNativePaidChildCold()
    {
        foreach (var (seed, tactic, amount) in new[] { (56, 2174, 1), (54, 2175, 2) })
        {
            var g = Start(seed, tactic);
            Use(g, "hit-twenty", 0); Play(g); Use(g, "hit-nine", 0); Play(g);
            Require(Player(g, 0).Hp == 1 && Player(g, 0).Armor == 0, "The actual damage payments prepare a one-HP owner with no remaining Armor.");
            var damageBefore = E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1);
            var finalAmount = amount + (Player(g, 1).WushuangTacticIds!.Contains(2333) ? 1 : 0) +
                (Player(g, 1).WushuangTacticIds!.Contains(2334) ? 2 : 0);
            Use(g, "hit-one", 0);
            Until(g, () => P(g)?.SkillPrompt?.SkillId == Driver &&
                g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.WushuangCombat?.TacticId == tactic) &&
                g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(f => f.Window == SkillProgramTriggerWindow.DyingExited));
            Require(E<DyingResolvedEvent>(g).Count(e => e.VictimSeat == 0 && e.Survived) == 1 &&
                E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == finalAmount) == damageBefore + 1 &&
                g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.WushuangCombat is { } r && r.TacticId == tactic && r.Amount == amount),
                "A genuine rescued damage victim deals the exact bravery amount before the original damage resumes. " + Debug(g));
            Cold(g); Play(g);
            Require(Player(g, 0).Hp == 1 && E<DyingResolvedEvent>(g).Count(e => e.VictimSeat == 0 && e.Survived) == 1 &&
                E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1) == damageBefore + 1,
                "Returning the paused child neither rescues twice nor repeats the already-applied bravery damage.");
            Cold(g);
        }
    }

    private static GameEngine Start(int seed, int tactic)
    {
        var g = GameEngine.CreateStandard(StandardWushuangContentRegistry.CreateOptions(seed) with
        { AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false }, Registry.Value);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Human, g.Revision, P(g)!.PromptId));
        Until(g, () => V(g, 0).Wushuang is { CanShop: true, Phase: WushuangShopPhase.OpeningTactic });
        var offer = V(g, 0).Wushuang!.Offers.SingleOrDefault(o => o.TacticId == tactic);
        Require(offer is not null, $"The verified seed {seed} must still expose official tactic {tactic}.");
        Accept(g, new BuyWushuangOfferCommand(0, offer!.Id, null, g.Revision)); Play(g); return g;
    }
    private static GameSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat);
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).PendingDecision;
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PlayerSnapshot Player(GameEngine g, int seat) => V(g, 0).Players.Single(p => p.Seat == seat);
    private static void Use(GameEngine g, string binding, params int[] targets) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], targets, g.Revision, P(g)!.PromptId));
    private static long Card(GameEngine g, CardKind kind)
    {
        var action = g.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && Player(g, 0).Hand.Any(c => c.Id == id && c.Kind == kind) &&
            (kind == CardKind.DrawTwo || a.TargetSeats.Contains(1)));
        Require(action is not null, "Expected actual legal action " + kind + " " + Debug(g));
        var before = E<CardUseDeclaredEvent>(g).Select(e => e.ResolutionId).ToHashSet();
        Accept(g, new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId));
        return E<CardUseDeclaredEvent>(g).Last(e => !before.Contains(e.ResolutionId)).ResolutionId;
    }
    private static void Play(GameEngine g) => Until(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Until(GameEngine g, Func<bool> ready)
    {
        for (var step = 0; step < 240; step++)
        {
            if (ready()) return;
            if (P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, play.PromptId));
            else if (P(g) is { PlayerSeat: 0, Choices.Count: > 0 } prompt)
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" || c.Cards.Count == 0);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Combat fixture did not reach its actual continuation boundary: " + SnapshotJson.Serialize(V(g, 0)));
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; var c = p.Choices.FirstOrDefault(predicate) ?? p.Choices[0]; Accept(g, new AnswerPromptCommand(0, p.PromptId, c.Id, g.Revision)); }
    private static void Cold(GameEngine g)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), Registry.Value);
        Require(Enumerable.Range(0, 2).All(seat => SnapshotJson.Serialize(V(g, seat)) == SnapshotJson.Serialize(V(restored, seat))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements),
            "Cold replay preserves both private player views and each exact movement at this paid continuation.");
    }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(c); Require(r.Accepted, r.Error?.Message ?? "Combat command rejected."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Debug(GameEngine g) => JsonSerializer.Serialize(new
    {
        V(g, 0).TurnNumber,
        players = V(g, 0).Players.Select(p => new { p.Seat, p.Hp, p.Armor, p.WushuangTacticIds }),
        prompt = P(g)?.Kind,
        events = g.Events.TakeLast(8).Select(e => new { type = e.Payload.GetType().Name, payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) })
    });

    private sealed class Fixture(ContentRegistry source) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:wushuang-combat", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var x in source.Cards.Values) b.AddCard(x);
            foreach (var x in source.Skills.Values) b.AddSkill(x);
            foreach (var x in source.Generals.Values) b.AddGeneral(x);
            foreach (var x in source.Decks.Values) b.AddDeck(x);
            foreach (var x in source.Modes.Values.Where(x => x.Id != WushuangMode.ModeId)) b.AddMode(x);
            var activations = new List<object>();
            foreach (var (id, amount) in new[] { ("hit-one", 1), ("hit-three", 3), ("hit-four", 4), ("hit-nine", 9), ("hit-twenty", 20) })
                activations.Add(new { id, minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "anyLiving", usesPerTurn = (int?)null,
                    effects = new[] { new { op = "damage", target = "selectedTarget", amount } } });
            activations.Add(new { id = "draw-nine", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "draw", target = "owner", amount = 9 } } });
            foreach (var (id, kind) in new[] { ("obtain-draw", "drawTwo"), ("obtain-dismantlement", "dismantlement"), ("obtain-slash", "slash") })
                activations.Add(new { id, minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                    effects = new[] { new { op = "obtainWushuangDeckCards", target = "owner", amount = 1, outputKind = kind } } });
            object[] armor = [new { id = "initial-armor", window = "gameStarting", subject = "owner", optional = false, effects = new[] { new { op = "grantArmor", target = "owner", amount = 4 } } }];
            object[] ownerTriggers = [armor[0],
                new { id = "rescue-one", window = "selfDyingResponse", subject = "owner", optional = false, effects = new[] { new { op = "recover", target = "owner", amount = 1 } } },
                new { id = "native-damage-child", window = "afterDamageApplied", subject = "damageSource", damageOccurrence = "perDamage", optional = true, effects = new[] { new { op = "draw", target = "owner", amount = 1 } } }];
            var rules = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new object[] {
                new { id = Driver, revision = 1, activations, triggers = ownerTriggers }, new { id = Quiet, revision = 1, triggers = armor } } });
            var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                [Driver] = new { name = "战法代表机制", description = "原生伤害、摸牌与指定实体取得。" }, [Quiet] = new { name = "安静对手", description = "初始护甲。" } } });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Combat fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Human, "战法测试主将", "", Driver, "qun", BaseHp: 30));
            b.AddGeneral(new("fixture:wushuang-combat-peer", "战法测试对手", "", Quiet, "qun", BaseHp: 30));
            b.AddDeck(new("fixture:wushuang-combat-deck", "固定牌实体", 4, 0, []) { PhysicalCards =
                Enumerable.Range(0, 4).SelectMany(_ => new[] { "standard:draw_two", "standard:dismantlement", "standard:slash" })
                    .Select(id => new ContentDeckPhysicalCard(id, Suit.Spade, 2))
                    .Concat(Enumerable.Range(0, 503).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 2))).ToArray() });
            b.AddMode(new(WushuangMode.ModeId, "战法代表机制验证", 2, 2, new Dictionary<string, int>(), "fixture:wushuang-combat-deck", 2,
                [Human, "fixture:wushuang-combat-peer"], ContentModeKind.Team, new Dictionary<string, int> { [WushuangMode.PlayerTeamId] = 1, [WushuangMode.AiTeamId] = 1 }));
        }
    }
}
