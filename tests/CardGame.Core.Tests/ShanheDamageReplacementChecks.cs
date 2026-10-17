using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheDamageReplacementChecks
{
    private const string Driver = "fixture:shanhe-replacement-driver", Quiet = "fixture:shanhe-replacement-quiet";
    private const string Layer = "fixture:shanhe-card-layer", Absolute = "fixture:shanhe-absolute";
    private const string Owner = "fixture:shanhe-replacement-owner", Enemy = "fixture:shanhe-replacement-enemy";
    private const string DeathNotice = "fixture:shanhe-replacement-death-notice", Deck = "fixture:shanhe-replacement-deck";
    private const string RecoveryPause = "fixture:shanhe-recovery-counter-pause";

    public static void AbsoluteHpLossKeepsAlcoholLuoyiAndExplicitCardLayerCold()
    {
        var s = Scenario.Start(absolute: true);
        s.Use("chain", targets: [1]); s.Use("chain", targets: [2]);
        s.Play(CardKind.Alcohol);
        var before = s.V(1).Hp;
        s.Play(CardKind.FireSlash, 1);
        s.Need(s.V(1).Hp == before - 5 && s.V(1).MaxHp == 24 && s.V(1).IsChained && s.V(2).IsChained,
            "Absolute HP loss includes the real Alcohol, native Luoyi and explicit card layer (1+1+1+2), excludes the ordinary +3 damage layer, and issues no elemental propagation.");
        s.Need(s.Events<DamageAppliedEvent>().Length == 0 && s.Events<ChainedDamagePropagatedEvent>().Length == 0 &&
            s.Events<DamageReplacedWithHpLossEvent>().Single().Amount == 5,
            "The converted hit remains genuine HP loss and never creates ordinary after-damage facts.");
        s.Need(s.Events<CardDamageReplacedWithMaximumHpLossEvent>().Length == 0 &&
            !s.Events<SkillUsageConsumedEvent>().Any(e => e.Scope == SkillUsageScope.Round &&
                e.SkillId is "shanhe:xiaoming" or "shanhe:mouming"),
            "The local engine preserves its existing absolute-HP-loss priority when both replacement policies are held; this is an explicit local ordering, not a claim about undocumented original-game precedence.");
        s.Cold();
    }

    public static void RoundQuotaSeparatesSlashAndTrickAndIgnoresArmorCold()
    {
        var s = Scenario.Start();
        var lion = s.V().Hand.Single(c => c.Kind == CardKind.SilverLion).Id;
        s.Use("equip", [lion], [1]);
        s.Use("chain", targets: [1]); s.Use("chain", targets: [2]);
        s.Play(CardKind.Alcohol);
        s.Play(CardKind.FireSlash, 1);
        var first = s.Events<CardDamageReplacedWithMaximumHpLossEvent>().Single();
        s.Need(first.Receipt is { Amount: 5, MaximumHpBefore: 24, MaximumHpAfter: 19, HpAfter: 19 } &&
            s.V(1).Equipment.Single().Kind == CardKind.SilverLion && s.V(1).IsChained && s.V(2).IsChained &&
            s.Events<DamageAppliedEvent>().Length == 0 && s.Events<SilverLionDamageCappedEvent>().Length == 0,
            "The first real Slash replaces only its card layer, bypasses Silver Lion and leaves the unissued chain intact.");
        s.Cold();
        s.Play(CardKind.Slash, 1);
        s.Need(s.Events<CardDamageReplacedWithMaximumHpLossEvent>().Length == 1 &&
            s.Events<DamageAppliedEvent>().Single() is { TargetSeat: 1, Amount: 1 },
            "A later Slash in the same round is ordinary damage and receives the real armor cap.");
        s.Play(CardKind.Duel, 2);
        s.Need(s.Events<CardDamageReplacedWithMaximumHpLossEvent>().Last().Receipt is { CardKind: CardKind.Duel, Amount: 4 } &&
            s.Events<SkillUsageConsumedEvent>().Count(e => e.Scope == SkillUsageScope.Round && e.SkillId is "shanhe:xiaoming" or "shanhe:mouming") == 2,
            "The first trick's independent allowance includes native Luoyi and the explicit card layer and is consumed once.");
        s.Cold();
        var round = s.Events<RoundStartedEvent>().Last().RoundNumber;
        s.EndTurn();
        s.Need(s.Events<RoundStartedEvent>().Last().RoundNumber > round, "The fixture must cross a real normal-round boundary.");
        s.Use("strip", targets: [1]);
        s.Play(CardKind.Slash, 1);
        s.Need(s.Events<CardDamageReplacedWithMaximumHpLossEvent>().Length == 3 &&
            s.Events<CardDamageReplacedWithMaximumHpLossEvent>().Last().Receipt is { CardKind: CardKind.Slash, Amount: 4 },
            "A real new round resets the allowance without reissuing any earlier payment or replacement.");
        s.Cold();
    }

    public static void ZeroMaximumDiesWithoutDyingAndNativeCostsStayPaidCold()
    {
        var s = Scenario.Start(zeroMaximum: true);
        var duel = s.V().Hand.First(c => c.Kind == CardKind.Duel).Id;
        s.BeginPlay(duel, 1);
        s.ReachPlay();
        s.Need(!s.V(1).IsAlive && s.V(1).Hp == 0 && s.V(1).MaxHp == 0 && s.V(2).IsAlive &&
            s.Events<PlayerDiedEvent>().Single() is { VictimSeat: 1, KillerSeat: null } &&
            s.Events<PlayerDyingEvent>().Length == 0 && s.Events<DamageAppliedEvent>().Length == 0,
            "A zero maximum causes direct native death without a damage or rescue window; other enemies retain their own lives.");
        s.Need(s.G.CardMovements.Count(m => m.CardId == duel && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == duel && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            s.Events<CardDamageReplacedWithMaximumHpLossEvent>().Single().Receipt.Amount == 4,
            "The genuine Duel pays and finishes once even while a death-trigger child suspends its typed return.");
        s.Cold();
    }

    public static void PaidDyingPeachRecoveryStartsBloodBladeAndReturnsToOriginalDyingCold()
    {
        var s = Scenario.Start(rescue: true);
        var peach = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id;
        var enemyHp = s.G.CreateSnapshot(0).Players.Skip(1).Sum(p => p.Hp);
        s.Accept(new UseProgramSkillCommand(0, Driver, "hurt-twelve", [], [], s.G.Revision, s.G.PendingDecision!.PromptId));
        s.Reach(() => s.G.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.V().Hp == 0 && s.Events<PlayerDyingEvent>().Single().VictimSeat == 0,
            "The owner must enter a genuine dying window through actual twelve-point program damage.");
        s.Cold();
        var prompt = s.G.PendingDecision!;
        var choice = prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("response") == "peach" && c.Cards.Contains(peach));
        s.Accept(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, s.G.Revision));
        s.ReachPlay();
        s.Need(s.ColdRecoveredDyingAttack,
            "The fixture must cold-restore while blood blade owns a real attack above its paid native Peach and original dying token.");
        s.Need(s.V().IsAlive && s.V().Hp == 1 && s.G.CreateSnapshot(0).Players.Skip(1).Sum(p => p.Hp) == enemyHp - 1 &&
            s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == "shanhe:xueren") == 1 &&
            s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == "shanhe:shixue-1") == 1 &&
            s.Events<DyingResolvedEvent>().Single() is { VictimSeat: 0, Survived: true },
            "A real paid rescue must trigger exactly one blood-blade counter and recovery draw, then return to the original dying token.");
        s.Need(s.Events<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat != 0 && e.Amount == 1) == 1 &&
            s.Events<NativeCardRecoveryAppliedEvent>().Single().Receipt is { HpBefore: 0, HpAfter: 1, CardKind: CardKind.Peach } &&
            s.G.CardMovements.Count(m => m.CardId == peach && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == peach && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "The nested counter uses a native recovery receipt and never replays the Peach's physical cost.");
        s.Cold();
    }

    public static void NativeShenquPeachInsideDamageStartsBloodBladeAndReturnsCold()
    {
        var s = Scenario.Start(shenqu: true);
        var peach = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id;
        var enemyHp = s.G.CreateSnapshot(0).Players.Skip(1).Sum(p => p.Hp);
        s.Use("hurt-one");
        s.Need(s.ColdNativePeachAttack && s.V().Hp == 12 &&
            s.G.CreateSnapshot(0).Players.Skip(1).Sum(p => p.Hp) == enemyHp - 1 &&
            s.Events<HulaoPeachIssuedEvent>().Length == 1 && s.Events<HulaoPeachReturnedEvent>().Length == 1 &&
            s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == "shanhe:xueren") == 1 &&
            s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == "shanhe:shixue-1") == 1,
            "The production Shenqu program pays a native Peach inside its damage window, triggers blood blade/recovery draw once, and returns through the original issued use after an in-flight cold restore.");
        s.Need(s.Events<NativeCardRecoveryAppliedEvent>().Single().Receipt is { HpBefore: 11, HpAfter: 12 } &&
            s.G.CardMovements.Count(m => m.CardId == peach && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == peach && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            s.Events<PlayerDyingEvent>().Length == 0,
            "An ordinary paid Peach child has its own recovery receipt and never borrows a dying-rescue token or duplicates its cost.");
        s.Cold();
    }

    public static void RealLonghunTwoHeartDyingRecoveryPaysWholeGroupAndDrawsOnceCold()
    {
        var s = Scenario.Start(longhun: true);
        var hearts = s.V().Hand.Where(c => c.Kind == CardKind.Dodge && c.Suit == Suit.Heart).Select(c => c.Id).Order().ToArray();
        s.Need(hearts.Length == 2, "The conversion uses exactly two declared physical heart cards, without searching seeds.");
        var enemyHp = s.G.CreateSnapshot(0).Players.Skip(1).Sum(p => p.Hp);
        s.Accept(new UseProgramSkillCommand(0, Driver, "hurt-twelve", [], [], s.G.Revision, s.G.PendingDecision!.PromptId));
        s.Reach(() => s.G.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 });
        s.Cold();
        var prompt = s.G.PendingDecision!;
        var choice = prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("response") == "extended-view-as" &&
            c.Parameters.GetValueOrDefault("conversion-binding-id") == "2-heart" && c.Cards.Order().SequenceEqual(hearts));
        s.Accept(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, s.G.Revision));
        s.ReachPlay();
        s.Need(s.ColdRecoveredDyingAttack && s.V().Hp == 2 && s.G.CreateSnapshot(0).Players.Skip(1).Sum(p => p.Hp) == enemyHp - 1 &&
            s.Events<NativeCardRecoveryAppliedEvent>().Single().Receipt is { RequestedAmount: 2, HpBefore: 0, HpAfter: 2 } &&
            s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == "shanhe:xueren") == 1 &&
            s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == "shanhe:shixue-1") == 1,
            "The real OL Longhun two-heart conversion recovers two in one actual event, yielding one counter and one recovery draw through a cold-restored original dying return.");
        foreach (var id in hearts)
            s.Need(s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
                "Every material in the actual conversion group is paid and discarded exactly once.");
        s.Cold();
    }

    public static void RealHuituoRedJudgmentRecoveryFeedsBloodBladeThroughGenericReceiptCold()
    {
        var s = Scenario.Start(huituo: true);
        var enemyHp = s.G.CreateSnapshot(0).Players.Skip(1).Sum(p => p.Hp);
        s.Use("hurt-one");
        s.Need(s.ColdProgramBenefitAttack && s.V().Hp == 12 && s.G.CreateSnapshot(0).Players.Skip(1).Sum(p => p.Hp) == enemyHp - 1 &&
            s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == "classic:huituo") == 1 &&
            s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == "shanhe:xueren") == 1 &&
            s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == "shanhe:shixue-1") == 1,
            "The production Huituo selects the wounded owner and a real red judgment; its native recovery producer shares the same frame-owned HP invoice as primitive Recover.");
        var judgment = s.Events<JudgmentResolvedEvent>().Single(e => e.Reason == "huituo");
        s.Need(judgment.Suit == Suit.Heart && s.G.CardMovements.Count(m => m.CardId == judgment.CardId &&
            m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "The judgment entity remains bound across the recovery counter's in-flight cold restore and is discarded once on return.");
        s.Cold();
    }

    public static void SelectedTargetsKeepIndependentFrozenRecoveryInvoicesCold()
    {
        var s = Scenario.Start(multipleRecovery: true);
        s.Use("hurt-target", targets: [1]); s.Use("hurt-target", targets: [2]);
        s.Use("recover-pair", targets: [1, 2]);
        var receipts = s.Events<ProgramRecoveryAppliedEvent>().Where(e => e.Receipt.SourceSeat == 0 && e.Receipt.TargetSeat is 1 or 2).ToArray();
        s.Need(s.ColdMultipleRecoveryInvoices && receipts.Length == 2 && receipts.Select(e => e.ProgramFrameId).Distinct().Count() == 1 &&
            receipts.Select(e => e.Receipt.HpChangeId).Distinct().Count() == 2 && s.V(1).Hp == 24 && s.V(2).Hp == 24 && s.V().Hp == 10 &&
            s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == "shanhe:xueren") == 2,
            "One Recover(selectedTargets) instruction keeps both frozen actual invoices until their individual HP windows return; each healed opponent counters once after cold replay.");
        s.Cold();
    }

    private sealed class Scenario(GameEngine game, ContentRegistry registry)
    {
        private bool _traceRecoveredDyingAttack;
        private bool _traceNativePeachAttack;
        private bool _traceProgramBenefitAttack;
        private bool _traceMultipleRecoveryInvoices;
        public bool ColdRecoveredDyingAttack { get; private set; }
        public bool ColdNativePeachAttack { get; private set; }
        public bool ColdProgramBenefitAttack { get; private set; }
        public bool ColdMultipleRecoveryInvoices { get; private set; }
        public GameEngine G { get; private set; } = game;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(0).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        public void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.ToString() ?? "Replacement command rejected."); }
        public void Use(string id, int[]? cards = null, int[]? targets = null)
        {
            Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], G.Revision, G.PendingDecision!.PromptId));
            ReachPlay();
        }
        public void Play(CardKind kind, int? target = null) => BeginAndSettle(V().Hand.First(c => c.Kind == kind).Id, target);
        private void BeginAndSettle(int id, int? target) { BeginPlay(id, target); ReachPlay(); }
        public void BeginPlay(int id, int? target)
        {
            var action = G.GetHumanLegalActions().First(a => a.CardId == id && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Accept(new PlayCardCommand(0, id, action.TargetSeats, G.Revision, G.PendingDecision!.PromptId,
                action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
        }
        public void EndTurn()
        { Accept(new EndPlayPhaseCommand(0, G.Revision, G.PendingDecision!.PromptId)); ReachPlay(); }
        public void ReachPlay() => Reach(() => G.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 240; step++)
            {
                if (done()) return;
                if (_traceRecoveredDyingAttack && !ColdRecoveredDyingAttack &&
                    G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "shanhe:xueren" &&
                        f.AttackAttempt is not null && f.AttackReturn?.RecoveryOrigin?.SuspendedDyingFrameId is not null) &&
                    G.ResolutionStack.OfType<CardUseFrame>().Any(f => f.RecoveryApplication is not null))
                {
                    Cold();
                    ColdRecoveredDyingAttack = true;
                }
                if (_traceNativePeachAttack && !ColdNativePeachAttack &&
                    G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "shanhe:xueren" &&
                        f.AttackAttempt is not null && f.AttackReturn?.RecoveryOrigin is not null) &&
                    G.ResolutionStack.OfType<CardUseFrame>().Any(f => f.HulaoPeachOrigin is not null && f.RecoveryApplication is not null))
                {
                    Cold();
                    ColdNativePeachAttack = true;
                }
                if (_traceProgramBenefitAttack && !ColdProgramBenefitAttack &&
                    G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "classic:huituo" && f.RecoveryApplications?.Count > 0) &&
                    G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "shanhe:xueren" && f.AttackAttempt is not null))
                { Cold(); ColdProgramBenefitAttack = true; }
                if (_traceMultipleRecoveryInvoices && !ColdMultipleRecoveryInvoices &&
                    G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Driver && f.RecoveryApplications?.Count == 2))
                { Cold(); ColdMultipleRecoveryInvoices = true; }
                if (G.ResolutionStack.Any(f => f is DeathFrame)) Cold();
                if (G.PendingDecision is { PlayerSeat: 0, Choices.Count: > 0 } choice)
                    Accept(new AnswerPromptCommand(0, choice.PromptId, choice.Choices[0].Id, G.Revision));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("No bounded replacement play boundary.");
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves replacement receipts, native death frames, quotas and physical costs.");
        }
        public static Scenario Start(bool absolute = false, bool zeroMaximum = false, bool rescue = false, bool shenqu = false,
            bool longhun = false, bool huituo = false, bool multipleRecovery = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(absolute, rescue || shenqu || longhun || huituo, huituo));
            var skills = new List<string> { "classic:luoyi" };
            if (rescue || shenqu || longhun || huituo) skills.AddRange(["shanhe:xueren", "shanhe:shixue-1"]);
            if (shenqu) skills.Add("shanhe:shengui-shenqu");
            if (longhun) skills.Add("ol:longhun");
            if (huituo) skills.Add("classic:huituo");
            var starting = new List<ShanheStartingCard> { new(CardKind.Peach), new(CardKind.Alcohol), new(CardKind.FireSlash),
                new(CardKind.Slash), new(CardKind.Slash), new(CardKind.Duel), new(CardKind.Duel), new(CardKind.SilverLion) };
            if (longhun) starting.AddRange([new(CardKind.Dodge, Suit.Heart, 3), new(CardKind.Dodge, Suit.Heart, 4)]);
            var setup = new ShanheBattleSetup(71, 1, "fixture:replacement", Owner, 12, 0, 0, 0, 0,
                skills,
                starting, [],
                Enumerable.Range(1, 3).Select(_ => new ShanheCombatantDefinition(Enemy, MaxHpBonus: zeroMaximum ? 0 : 20,
                    ExtraSkillIds: multipleRecovery ? ["shanhe:xueren", "shanhe:shixue-1", RecoveryPause] : [])).ToArray(),
                [], new string('a', 64));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, ModeId = ShanheBattleSetup.BattleModeId, PlayerCount = 4,
                HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
            }, registry);
            var scenario = new Scenario(game, registry);
            scenario._traceRecoveredDyingAttack = rescue || longhun;
            scenario._traceNativePeachAttack = shenqu;
            scenario._traceProgramBenefitAttack = huituo;
            scenario._traceMultipleRecoveryInvoices = multipleRecovery;
            scenario.Accept(new StartGameCommand()); scenario.ReachPlay(); return scenario;
        }
    }

    private sealed class Fixture(bool absolute, bool rescue, bool redJudgment) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-replacements", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            string Read(string file)
            {
                using var stream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + file)!;
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var production = SkillProgramCatalog.Load(Read("shanhe-replacements.rules.json"), Read("shanhe-replacements.presentation.json"));
            foreach (var (id, program) in production.Programs)
                builder.AddSkill(new(id, id, id) { Program = program, Tags = SkillTag.Locked });
            var reactions = SkillProgramCatalog.Load(Read("shanhe-reactions.rules.json"), Read("shanhe-reactions.presentation.json"));
            foreach (var id in new[] { "shanhe:xueren", "shanhe:shixue-1" })
                builder.AddSkill(new(id, id, id) { Program = reactions.Programs[id], Tags = SkillTag.Locked });
            var boss = SkillProgramCatalog.Load(Read("shanhe-boss.rules.json"), Read("shanhe-boss.presentation.json"));
            builder.AddSkill(new("shanhe:shengui-shenqu", "神躯", boss.Presentations["shanhe:shengui-shenqu"].Description)
                { Program = boss.Programs["shanhe:shengui-shenqu"], ProgramPresentation = boss.Presentations["shanhe:shengui-shenqu"] });
            var rules = $$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"{{{Driver}}}","revision":1,"modifiers":[
                    {"id":"slash","query":"slashLimit","operation":"add","value":8,"priority":0},
                    {"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}],"activations":[
                    {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                    {"id":"equip","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
                    {"id":"strip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"selectedTarget","amount":20,"zones":["hand"]}]},
                    {"id":"hurt-twelve","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":12,"nature":"normal"}]},
                    {"id":"hurt-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"nature":"normal"}]},
                    {"id":"hurt-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"normal"}]},
                    {"id":"recover-pair","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTargets","amount":1}]}]},
                  {"id":"{{{Layer}}}","revision":1,"damageModifiers":[
                    {"id":"card","cardKinds":["slash","fireSlash","thunderSlash","duel"],"amount":2,"condition":"always","damageLayer":"cardDamage"},
                    {"id":"additional","cardKinds":["slash","fireSlash","thunderSlash","duel"],"amount":3,"condition":"always"}]},
                  {"id":"{{{Absolute}}}","revision":1,"cardPolicies":[{"id":"absolute","kind":"damageBecomesHpLoss","cardKinds":[]}]},
                  {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"{{{RecoveryPause}}}","revision":1,"triggers":[{"id":"pause-counter","window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","optional":true,"condition":{"kind":"damageTargetIsOther"},"effects":[{"op":"draw","target":"owner","amount":1}]}]},
                  {"id":"{{{DeathNotice}}}","revision":1,"triggers":[{"id":"notice","window":"ownerDied","subject":"owner","optional":true,"effects":[{"op":"draw","target":"owner","amount":1,"condition":{"kind":"wounded"}}]}]}
                ]}
                """;
            var fixture = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Layer, Absolute, Quiet, DeathNotice, RecoveryPause }.ToDictionary(id => id, _ => new { name = "替换机制夹具", description = "真实牌、原生裸衣与死亡返回" })
            }));
            foreach (var (id, program) in fixture.Programs) builder.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = fixture.Presentations[id] });
            var own = new List<string> { Layer, "shanhe:xiaoming", "shanhe:mouming" };
            if (absolute) own.Add(Absolute);
            if (rescue) own.Add(RecoveryPause);
            builder.AddGeneral(new(Owner, "替换来源", "", Driver, "shu", 12, own));
            builder.AddGeneral(new(Enemy, "固定守军", "", Quiet, "qun", 4, [DeathNotice]));
            builder.AddDeck(new(Deck, "纯闪", 0, 2, [])
                { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:dodge", redJudgment ? Suit.Heart : Suit.Spade, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "替换机制", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
