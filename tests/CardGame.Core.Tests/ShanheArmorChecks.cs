using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheArmorChecks
{
    private const string Duijia = "shanhe:duijia-2026", Fanci = "shanhe:fanci";
    private const string Kuanggu = "classic:kuanggu", Jueqing = "classic:jueqing", Xiaoming = "shanhe:xiaoming";
    private const string Driver = "fixture:shanhe-armor-driver", Watch = "fixture:shanhe-armor-damage-child";
    private const string Health = "fixture:shanhe-armor-health", Owner = "fixture:shanhe-armor-owner";
    private const string Ally = "fixture:shanhe-armor-ally", EnemyA = "fixture:shanhe-armor-enemy-a";
    private const string EnemyB = "fixture:shanhe-armor-enemy-b", Deck = "fixture:shanhe-armor-deck";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture-armor-production", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [],
            [new("standard:cao-cao")], [], new string('a', 64))));

    public static void RepeatedTeamArmorStartsOnlyAlliesSurvivesSkillLossAndRestartsCold()
    {
        var s = Scene.Start("opening");
        var grants = s.Events<ArmorGrantedEvent>().Where(e => e.Source.SkillId == Duijia).ToArray();
        Need(s.V(0).Armor == 0 && s.V(1).Armor == 6 && s.V(2).Armor == 0 && s.V(3).Armor == 0 &&
            s.V(0).Hp == 8 && s.V(1).Hp == 8 && s.V(1).MaxHp == 12,
            "Two real acquired copies grant six Armor to the actual teammate, excluding their owner and both enemies; no HP or maximum-HP is granted.");
        Need(grants.Length == 2 && grants.All(e => e.PlayerSeat == 1 && e.Amount == 3 && e.Source.OwnerSeat == 0) &&
            grants.Select(e => (e.ArmorBefore, e.ArmorAfter)).SequenceEqual([(0, 3), (3, 6)]) &&
            grants.Select(e => e.Source.SkillInstanceId).Distinct(StringComparer.Ordinal).Count() == 2 &&
            s.Events<RecoveryAppliedEvent>().Length == 0 && s.Events<ProgramRecoveryAppliedEvent>().Length == 0,
            "Each formal GameStarting tactic instance issues its own genuine three-point grant; Armor is neither capped at five nor disguised as recovery.");
        Need(!JsonSerializer.Serialize(s.V()).Contains("\"Armor\"", StringComparison.Ordinal) &&
            Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players[1].Armor == 6),
            "A zero Armor value preserves the old snapshot JSON shape while acquired Armor is public to every viewer.");
        s.Cold();
        s.Use("lose-team-armor");
        Need(s.V().Skills?.Any(skill => skill.ContentId == Duijia) != true && s.V(1).Armor == 6,
            "Actual skill loss removes both tactic grants without revoking Armor already paid to a teammate.");
        s.Use("hit-one", [1]);
        Need(s.V(1).Armor == 5 && s.V(1).Hp == 8 && s.Events<DamageAppliedEvent>().Single() is
                { TargetSeat: 1, Amount: 1, ArmorPayment: { ArmorBefore: 6, ArmorAbsorbed: 1, ArmorAfter: 5, HpLossAmount: 0 } },
            "The retained Armor is genuinely spendable after its source skill is lost.");
        s.Cold();
        var next = Scene.Start("opening");
        Need(next.V(1).Armor == 6 && next.Events<ArmorGrantedEvent>().Where(e => e.Source.SkillId == Duijia)
                .Select(e => (e.ArmorBefore, e.ArmorAfter)).SequenceEqual([(0, 3), (3, 6)]),
            "A new real encounter initializes Armor from zero and executes the two opening grants, rather than adding them to the previous encounter's remainder.");
        next.Cold();
    }

    public static void FullAndPartialArmorKeepDamageCounterAndRealHealthDeltaCold()
    {
        var s = Scene.Start("counter");
        s.Use("armor-self");
        s.Begin("self-two"); s.AtDamageChild();
        var applied = s.Events<DamageAppliedEvent>().Single();
        var damage = s.G.ResolutionStack.OfType<DamageFrame>().Single();
        Need(applied is { SourceSeat: 0, TargetSeat: 0, Amount: 2, RemainingHp: 8,
                ArmorPayment: { DamageAmount: 2, ArmorBefore: 3, ArmorAbsorbed: 2, ArmorAfter: 1,
                    HpBefore: 8, HpLossAmount: 0, HpAfter: 8, ActualHpLost: 0 } } &&
            damage.ArmorPayment == applied.ArmorPayment && damage.Id == applied.ArmorPayment!.DamageFrameId &&
            damage.ParentFrameId == applied.ArmorPayment!.AttackFrameId && s.V().Armor == 1 && s.V().Hp == 8 &&
            s.CounterUses == 0 && s.V().HandCount == 0,
            "A fully absorbed real damage segment retains its exact owning DamageFrame/Applied receipt and full two-point damage, with no HP change or premature counter payment.");
        s.Cold(); s.ContinueDamage(); s.Play();
        Need(s.CounterUses == 1 && s.V(2).Hp == 11 && s.V(3).Hp == 11 && s.V(1).Hp == 12 &&
            s.Events<AfterDamageEvent>().Any(e => e.TargetSeat == 0 && e.Amount == 2) && s.V().HandCount == 0 &&
            s.Events<ProgramBindingResolvedEvent>().All(e => e.SkillId != Health),
            "Full absorption still executes the real after-damage Fanci once against both enemies; the zero HP delta triggers no health observer.");
        Need(s.Events<DamageAppliedEvent>().Where(e => e.TargetSeat is 2 or 3)
                .All(e => e.ArmorPayment is null && !JsonSerializer.Serialize(e).Contains("\"ArmorPayment\"", StringComparison.Ordinal)),
            "Ordinary zero-Armor counter damage retains the old nullable receipt JSON shape.");
        s.Begin("self-three"); s.AtDamageChild();
        Need(s.Events<DamageAppliedEvent>().Last() is { TargetSeat: 0, Amount: 3,
                ArmorPayment: { ArmorBefore: 1, ArmorAbsorbed: 1, ArmorAfter: 0, HpBefore: 8,
                    HpLossAmount: 2, UnclampedHpAfter: 6, HpAfter: 6, ActualHpLost: 2 } } &&
            s.G.ResolutionStack.OfType<DamageFrame>().Single().ArmorPayment == s.Events<DamageAppliedEvent>().Last().ArmorPayment &&
            s.V().Hp == 6 && s.V().Armor == 0,
            "Partial absorption separately freezes three damage, one Armor payment and exactly two actual lost HP.");
        s.Cold(); s.ContinueDamage(); s.Play();
        var diagnostic = s.DamageDiagnostic();
        Need(s.CounterUses == 1,
            "Partial Armor payment must not replenish Fanci's already paid same-turn quota. " + diagnostic);
        Need(s.V().HandCount == 2 &&
            s.Events<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Health && e.Activated && e.Completed) == 2,
            "The genuine per-point health observer must draw twice for exactly two actual HP lost. " + diagnostic);
        Need(s.Events<AfterDamageEvent>().Where(e => e.TargetSeat == 0).Select(e => e.Amount).SequenceEqual([2, 3]),
            "Both native after-damage facts must retain their full damage amounts. " + diagnostic);
        Need(s.Events<RecoveryAppliedEvent>().Length == 0 && s.Events<ProgramRecoveryAppliedEvent>().Length == 0,
            "Armor payment must never fabricate recovery. " + diagnostic);
        s.Cold();

        var k = Scene.Start("kuanggu");
        k.Use("armor-three", [1]); k.Use("hit-two", [1]);
        Need(k.V(1).Armor == 1 && k.V(1).Hp == 12 && k.V().Hp == 10 &&
            k.Events<DamageAppliedEvent>().Single() is { TargetSeat: 1, Amount: 2, ArmorPayment: { HpLossAmount: 0 } } &&
            k.Events<ProgramRecoveryAppliedEvent>().Count(e => e.Receipt.SourceSeat == 0 &&
                e.Receipt.TargetSeat == 0 && e.Receipt.HpAfter - e.Receipt.HpBefore == 1) == 2,
            "Native classic Kuanggu recovers once per actual damage point at distance one even when the recipient's Armor absorbs both points and its HP stays unchanged.");
        k.Cold();
    }

    public static void IronChainCarriesFullDamageWhileHpLossAndMaximumReplacementBypassArmorCold()
    {
        var s = Scene.Start("chain");
        s.Use("armor-three", [1]); s.Use("armor-one", [2]);
        var chain = s.PlayCard(CardKind.IronChain, [1, 2]);
        Need(s.V(1).IsChained && s.V(2).IsChained, "One genuine Iron Chain physically links the two selected recipients.");
        s.Cold();
        s.PlayCard(CardKind.Alcohol);
        Need(s.V().HasAlcoholEffect, "The actual paid Alcohol supplies the elemental Slash's native two-point damage.");
        var slash = s.PlayCard(CardKind.FireSlash, [1]);
        var hits = s.Events<DamageAppliedEvent>();
        Need(hits is [{ SourceSeat: 0, TargetSeat: 1, Amount: 2, Nature: DamageNature.Fire,
                    ArmorPayment: { ArmorBefore: 3, ArmorAbsorbed: 2, ArmorAfter: 1, HpLossAmount: 0 } },
                { SourceSeat: 0, TargetSeat: 2, Amount: 2, Nature: DamageNature.Fire,
                    ArmorPayment: { ArmorBefore: 1, ArmorAbsorbed: 1, ArmorAfter: 0, HpLossAmount: 1 } }] &&
            s.V(1).Hp == 12 && s.V(2).Hp == 11 && !s.V(1).IsChained && !s.V(2).IsChained &&
            !s.V().HasAlcoholEffect && hits[0].ArmorPayment!.DamageFrameId != hits[1].ArmorPayment!.DamageFrameId &&
            hits[0].ArmorPayment!.AttackFrameId == hits[1].ArmorPayment!.AttackFrameId &&
            s.Events<AfterDamageEvent>().Count(e => e.Amount == 2 && e.Nature == DamageNature.Fire) == 2,
            "A fully absorbed first elemental hit still conducts its full two damage; each recipient owns a distinct Armor payment instead of propagating zero HP loss or reusing the first receipt.");
        Need(new[] { chain, slash }.All(id => s.G.CardMovements.Count(m => m.CardId == id &&
                m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1),
            "The real chained damage spends its original physical Iron Chain and elemental Slash once each.");
        s.Cold();

        var j = Scene.Start("jueqing");
        j.Use("armor-three", [1]); j.Use("lose-one", [1]); j.PlayCard(CardKind.Slash, [1]);
        Need(j.V(1).Armor == 3 && j.V(1).Hp == 10 && j.Events<DamageAppliedEvent>().Length == 0 &&
            j.Events<AfterDamageEvent>().Length == 0 &&
            j.Events<ProgramSkillHpLostEvent>().Single() is { SkillId: Driver, TargetSeat: 1, Amount: 1 } &&
            j.Events<DamageReplacedWithHpLossEvent>().Single() is { PolicyOwnerSeat: 0, TargetSeat: 1, Amount: 1 },
            "A genuine loseHp instruction and native Jueqing's real physical Slash replacement both bypass Armor without creating fake damage or consuming Armor.");
        j.Cold();

        var x = Scene.Start("maximum");
        x.Use("armor-three", [1]); x.PlayCard(CardKind.Slash, [1]);
        Need(x.V(1).Armor == 3 && x.V(1).MaxHp == 11 && x.V(1).Hp == 11 &&
            x.Events<DamageAppliedEvent>().Length == 0 &&
            x.Events<CardDamageReplacedWithMaximumHpLossEvent>().Single().Receipt is { TargetSeat: 1, Amount: 1 },
            "The actual first-Slash Xiaoming replacement changes maximum HP before damage payment and cannot spend the recipient's Armor.");
        x.Cold(); x.PlayCard(CardKind.Slash, [1]);
        Need(x.V(1).Armor == 2 && x.V(1).Hp == 11 &&
            x.Events<CardDamageReplacedWithMaximumHpLossEvent>().Length == 1 &&
            x.Events<DamageAppliedEvent>().Single() is { Amount: 1, ArmorPayment: { ArmorBefore: 3,
                ArmorAbsorbed: 1, ArmorAfter: 2, HpLossAmount: 0 } },
            "After the real round replacement quota is spent, the next physical Slash returns to ordinary Armor-paid damage exactly once across cold recovery.");
        x.Cold();
    }

    private static void Need(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        private PendingDecision? P => G.PendingDecision;
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int CounterUses => Events<SkillUsageConsumedEvent>().Count(e => e.SkillId == Fanci && e.SkillOwnerSeat == 0);
        public string DamageDiagnostic() => JsonSerializer.Serialize(new
        {
            CounterUses, V().Hp, V().Armor, V().HandCount,
            Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId },
            Bindings = Events<ProgramBindingResolvedEvent>().Where(e => e.SkillId is Health or Fanci)
                .Select(e => new { e.SkillId, e.OwnerSeat, e.Window, e.Activated, e.Completed }),
            After = Events<AfterDamageEvent>().Select(e => new { e.TargetSeat, e.Amount }),
            Recovery = Events<RecoveryAppliedEvent>(),
            ProgramRecovery = Events<ProgramRecoveryAppliedEvent>().Select(e => e.Receipt),
            Draws = G.CardMovements.Where(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0))
                .Select(m => new { m.CardId, m.Sequence }),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind })
        });
        private void Accept(GameCommand command)
        { var r = G.Submit(command); Need(r.Accepted, r.Error?.Message ?? "The Armor fixture rejected a real command."); }
        public void Begin(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver,
            id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Use(string id, int[]? targets = null) { Begin(id, targets); Play(); }
        public void AtDamageChild() => Reach(() => P?.SkillPrompt?.SkillId == Watch &&
            P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        public void ContinueDamage()
        { var p = P!; Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue").Id, G.Revision)); }
        public int PlayCard(CardKind kind, int[]? targets = null)
        {
            var action = G.GetHumanLegalActions().First(a => a.CardId is { } id &&
                V().Hand.Any(card => card.Id == id && card.Kind == kind) && a.ConversionSource is null &&
                a.Kind != LegalActionKind.Recast && (targets is null || a.TargetSeats.SequenceEqual(targets)));
            var cardId = action.CardId!.Value;
            Accept(new PlayCardCommand(0, cardId, action.TargetSeats, G.Revision, P!.PromptId,
                action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
            Play(); return cardId;
        }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        private void Reach(Func<bool> done)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The bounded Armor fixture unexpectedly finished a match.");
                if (P?.PlayerSeat == 0)
                {
                    if (P.SkillPrompt?.SkillId == Watch) ContinueDamage();
                    else throw new InvalidOperationException("Unexpected Armor prompt: " + JsonSerializer.Serialize(P));
                }
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded Armor boundary: " + JsonSerializer.Serialize(P));
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves exact Armor grants, native damage receipts, nested children, all four views and original physical card payments.");
            var zones = G.CreateCardZoneDiagnostics();
            Need(zones.Select(card => card.CardId).Distinct().Count() == zones.Count, "Every real card still occupies exactly one zone.");
        }
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var extra = scene switch { "kuanggu" => new[] { Kuanggu }, "jueqing" => new[] { Jueqing }, _ => Array.Empty<string>() };
            ShanheStartingCard[] cards = scene switch
            {
                "chain" => [new(CardKind.IronChain, Suit.Club, 6), new(CardKind.Alcohol, Suit.Spade, 9), new(CardKind.FireSlash, Suit.Heart, 4)],
                "jueqing" => [new(CardKind.Slash, Suit.Spade, 6)],
                "maximum" => [new(CardKind.Slash, Suit.Spade, 6), new(CardKind.Slash, Suit.Club, 8)], _ => []
            };
            var setup = new ShanheBattleSetup(83, 1, "fixture-armor-" + scene, Owner, 8, 0, 0, 0, 3,
                extra, cards, [], [new(EnemyA), new(EnemyB)], [new(Ally, Hp: scene == "opening" ? 8 : 12)], new string('d', 64))
            { TacticSkillIds = scene switch { "opening" => [Duijia, Duijia], "counter" => [Fanci], "maximum" => [Xiaoming], _ => [] } };
            var game = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
                MaxTurns = 4, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var result = new Scene(game, registry); result.Accept(new StartGameCommand()); result.Play(); return result;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-armor", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { Duijia, Fanci, Kuanggu, Jueqing, Xiaoming }) builder.AddSkill(Production.Value.GetSkill(id));
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"armor-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantArmor","target":"owner","amount":3}]},
                    {"id":"armor-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"grantArmor","target":"selectedTarget","amount":1}]},
                    {"id":"armor-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"grantArmor","target":"selectedTarget","amount":3}]},
                    {"id":"hit-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                    {"id":"hit-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                    {"id":"self-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]},
                    {"id":"self-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":3}]},
                    {"id":"lose-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                    {"id":"lose-team-armor","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Duijia}}"],"sourceBind":"standard:none"}]}]},
                  {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-damage-child","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"priority":100,
                    "effects":[{"op":"chooseOption","target":"owner","resultBind":"continue-child","options":[{"id":"continue"}]}]}]},
                  {"id":"{{Health}}","revision":1,"triggers":[{"id":"actual-hp-point","window":"afterHealthChanged","subject":"owner","hpChangeOccurrence":"perPoint","optional":false,
                    "condition":{"kind":"healthChangeKindIs","hpChangeKind":"damage"},"effects":[{"op":"draw","target":"owner","amount":1}]}]}
                ]}
                """;
            var presentation = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "真实护甲操作", description = "只通过原生指令赋予护甲、伤害、失去体力及失去技能" },
                    [Watch] = new { name = "已付伤害子流程", description = "暂停真实伤害尾部，不制造事件", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Health] = new { name = "实际体力变化观察", description = "每真实损失一点伤害体力摸一张，不按护甲支付伪造体力变化" }
                }
            });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Owner, "护甲使用者", "", Driver, "shu", 12, scene == "counter" ? [Watch, Health] : []));
            foreach (var id in new[] { Ally, EnemyA, EnemyB }) builder.AddGeneral(new(id, "真实参战角色", "", "standard:none", "qun", 12));
            builder.AddDeck(new(Deck, "零自然摸牌真实牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, i % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "护甲支付真实战斗", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
