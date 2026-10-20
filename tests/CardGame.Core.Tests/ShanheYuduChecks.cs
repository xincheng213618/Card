using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheYuduChecks
{
    private const string Boss = StandardShanheS5YuduPackage.YuduId, Cuidu = StandardShanheS5YuduPackage.CuiduId;
    private const string Poison = StandardShanheS5YuduPackage.ZhongduId, Niepan = StandardShanheS5YuduPackage.NiepanId;
    private const string Medicine = StandardShanheCampaignCatalog.CurrentYaoli6SkillId, Guicai = "classic:guicai";
    private const string Owner = "fixture:s5-yudu-owner", Ally = "fixture:s5-yudu-ally", Enemy = "fixture:s5-yudu-enemy", Deck = "fixture:s5-yudu-deck";
    private const string Driver = "fixture:s5-yudu-driver", Gain = "fixture:s5-yudu-gain", BeforeDraw = "fixture:s5-yudu-before", Mutation = "fixture:s5-yudu-mutation";
    private const string Health = "fixture:s5-yudu-health", Lost = "fixture:s5-yudu-lost", Gate = "fixture:s5-yudu-gate", Quiet = "fixture:s5-yudu-quiet";
    private const string DrawReason = "program.damage-target-skill.round-draw", NiepanDraw = "skill-program." + Niepan + ".Draw", JudgmentReason = "shanhe.s5.zhongdu";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new ShanheBattleSetup(83, 1, "fixture:s5-yudu-production", Boss, 3, 0, 0, 0, 0, [], [], [], [new("classic:cao-cao")], [], new string('c', 64)) { TacticSkillIds = [Medicine] }));

    public static void ActualEnemyDamagePoisonsOnceAndDrawsFrozenRoundThroughArmorChainCold()
    {
        VerifyLoaderBoundaries();
        var s = Scene.Start("damage"); s.Play(); s.Use("armor-two", [2]); s.Begin("two", [2]); s.Until(() => s.At(Gain));
        var first = s.RoundRoot.DamageTargetSkillRoundDraw!; s.CheckRoundPayment(first);
        Need(first is { TargetSeat: 2, DamageAmount: 2, RoundNumber: 1, DrawCount: 1, GrantedGrant: not null, ExistingGrant: null } &&
            s.E<DamageAppliedEvent>() is [{ TargetSeat: 2, Amount: 2, ArmorPayment: { ArmorBefore: 2, ArmorAbsorbed: 2, HpLossAmount: 0 } }] && s.V(2).Hp == 20 && s.Has(Poison, 2),
            "A real cardless two-point enemy damage fully paid by Armor still grants one Poison and draws once for the real round, rather than requiring HP loss or drawing per damage point.");
        s.Privacy(); s.Cold(); s.Continue(); s.Play(); s.CompletedRound(first.ProgramFrameId);
        s.Begin("one", [2]); s.Until(() => s.At(Gain)); var repeat = s.RoundRoot.DamageTargetSkillRoundDraw!; s.CheckRoundPayment(repeat);
        Need(repeat.GrantedGrant is null && repeat.ExistingGrant == first.GrantedGrant && s.V(2).Skills?.Count(k => k.ContentId == Poison) == 1 &&
            s.E<SkillsAcquiredEvent>().Count(e => e.PlayerSeat == 2 && e.SourceSkillId == Cuidu && e.SkillIds.Contains(Poison)) == 1,
            "An already-poisoned enemy retains its exact original single grant; another actual damage still independently pays the round draw.");
        s.Cold(); s.Continue(); s.Play();
        s.Card(CardKind.IronChain, [2, 3]); s.Play(); s.Card(CardKind.FireSlash, [2]);
        for (var target = 2; target <= 3; target++)
        {
            s.Until(() => s.At(Gain)); var paid = s.RoundRoot.DamageTargetSkillRoundDraw!; s.CheckRoundPayment(paid);
            Need(paid.TargetSeat == target && paid.DamageAmount == 1 && paid.Nature == DamageNature.Fire && paid.DrawCount == 1 &&
                (target == 2 ? paid.ExistingGrant == first.GrantedGrant : paid.GrantedGrant is not null),
                "Each genuine element-chain enemy segment owns its own exact after-damage grant/draw, including the indirect recipient; an existing grant is retained rather than duplicated.");
            s.Cold(); s.Continue();
        }
        s.Play();
        Need(s.E<ChainedDamagePropagatedEvent>() is [{ TargetSeat: 3, Amount: 1 }] && s.E<DamageTargetSkillRoundGrantedEvent>().Length == 4 &&
            s.E<DamageTargetSkillRoundDrawPaidEvent>().Length == 4 && s.E<SkillsAcquiredEvent>().Count(e => e.SourceSkillId == Cuidu) == 2,
            "The original cardless damage, repeated enemy damage and two real fire recipients issue four independent payments but only two acquired identities.");
        s.Use("one", [1]); s.Use("one", [0]); s.Use("source-less");
        Need(s.E<DamageTargetSkillRoundGrantedEvent>().Length == 4 && !s.Has(Poison, 1) && !s.Has(Poison) &&
            s.E<DamageAppliedEvent>().Last() is { SourceLess: true, TargetSeat: 0 },
            "Real ally damage, self damage and source-less damage cannot borrow the public-enemy source entitlement."); s.Cold();

        foreach (var name in new[] { "jueqing", "maximum" })
        {
            var replaced = Scene.Start(name); replaced.Play(); replaced.Use("armor-two", [2]); replaced.Card(CardKind.FireSlash, [2]); replaced.Play();
            Need(replaced.V(2).Armor == 2 && replaced.E<DamageAppliedEvent>().Length == 0 && replaced.E<DamageTargetSkillRoundGrantedEvent>().Length == 0 && !replaced.Has(Poison, 2) &&
                (name == "jueqing" ? replaced.E<DamageReplacedWithHpLossEvent>().Length == 1 : replaced.E<CardDamageReplacedWithMaximumHpLossEvent>().Length == 1),
                "Early real HP/maximum-HP replacement bypasses Armor and never enters the new actual-damage poison grant."); replaced.Cold();
        }
        var capped = Scene.Start("cap"); capped.Play();
        while (capped.Round < 6) { capped.EndPlay(); capped.Play(); }
        capped.Begin("one", [1]); capped.Until(() => capped.At(Gain)); var cap = capped.RoundRoot.DamageTargetSkillRoundDraw!;
        Need(cap is { RoundNumber: 6, DrawCount: 5 } && cap.DrawOutcome?.ActualDrawIds.Count == 5,
            "Six naturally progressed small two-seat rounds request the production cap five, rather than an unbounded current-round count.");
        capped.CheckRoundPayment(cap); capped.Cold(); capped.Continue(); capped.Play(); capped.CompletedRound(cap.ProgramFrameId);

        var lethal = Scene.Start("lethal"); lethal.Play(); lethal.Begin("two", [2]); lethal.Until(() => lethal.At(Gain));
        var deadTarget = lethal.RoundRoot.DamageTargetSkillRoundDraw!;
        Need(!lethal.V(2).IsAlive && lethal.V(3).IsAlive && lethal.G.State.Winner == Winner.None && deadTarget.TargetDeadAtGrant && deadTarget.GrantedGrant is null &&
            lethal.E<SkillsAcquiredEvent>().All(e => e.SourceSkillId != Cuidu) && deadTarget.DrawOutcome?.ActualDrawIds.Count == 1,
            "A genuinely dead damage target receives no invented new grant; the living source still receives its independent local round draw while the other enemy keeps the battle live.");
        lethal.CheckRoundPayment(deadTarget); lethal.Cold(); lethal.Continue(); lethal.Play();
    }

    public static void FinalPreparationJudgmentRemovesOrPaysPoisonBeforeNativeNiepanCold()
    {
        var cured = Scene.Start("diamond"); cured.Play(); cured.Use("armor-two", [0]); cured.Use("incoming", [2]);
        var infection = cured.E<DamageTargetSkillRoundGrantedEvent>().Single();
        Need(infection.Source.OwnerSeat == 2 && infection.GrantedGrant is not null && cured.Has(Poison) && cured.V().Hp == 1,
            "The human acquires Poison through another real public enemy's Armor-paid native damage, rather than a fabricated grant or printed replacement.");
        cured.EndPlay(); cured.AtGuicai(0); var diamond = cured.V().Hand.First(c => c.Suit == Suit.Diamond).Id;
        var judgment = cured.G.ResolutionStack.OfType<JudgmentFrame>().Single();
        Need(judgment.Reason == JudgmentReason && judgment.Suit == Suit.Spade, "The actual next preparation starts the real original black judgment before any legal replacement.");
        cured.Cold(); cured.Answer(c => c.Cards.SequenceEqual([diamond])); cured.Play();
        var outcome = cured.E<ProgramJudgmentOutcomeRecordedEvent>().Single(e => e.Outcome.Reason == JudgmentReason);
        var issued = cured.E<PrepPoisonOutcomeIssuedEvent>().Single();
        Need(outcome.Outcome is { Suit: Suit.Diamond, CardId: var finalId } && finalId == diamond && issued.Cured && issued.JudgmentFrameId == outcome.Outcome.JudgmentFrameId &&
            cured.E<PrepPoisonGrantRevokedEvent>() is [{ OwnerSeat: 0, Grant: var removed }] && removed == infection.GrantedGrant &&
            !cured.Has(Poison) && cured.V().Hp == 1 && cured.E<PrepPoisonHpLossPaidEvent>().Length == 0 &&
            cured.E<PrepPoisonOutcomeCompletedEvent>() is [{ Cured: true, ActualHpLoss: 0 }],
            "The native final Diamond, not the printed original black card, revokes the exact poison identity without paying HP or leaving its self-loss producer suspended.");
        cured.CheckCleanup(outcome.Outcome); cured.Cold(); cured.Use("armor-two", [0]); cured.Use("incoming", [2]);
        var reinfection = cured.E<DamageTargetSkillRoundGrantedEvent>().Last();
        Need(cured.Has(Poison) && reinfection.GrantedGrant is not null && reinfection.GrantedGrant.GrantId != infection.GrantedGrant!.GrantId &&
            cured.V().Skills?.Count(k => k.ContentId == Poison) == 1,
            "After the real Diamond removes the old identity, a later actual enemy damage may infect the same living recipient again with one new independent grant."); cured.Cold();

        var s = Scene.Start("niepan"); s.Play(); s.Card(CardKind.IronChain, [0, 1]); s.Play(); s.Use("armor-two", [0]); s.Use("incoming", [2]); s.Use("armor-two", [0]);
        Need(s.V() is { Hp: 1, Armor: 2, IsChained: true } && s.Has(Poison), "The poisoned human has one real HP, retained numeric Armor and a genuinely chained state before its next preparation.");
        s.EndPlay(); s.AtGuicai(0); s.Cold(); s.SkipGuicai(); s.Until(s.AtNiepan);
        var poison = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PrepPoisonOutcome is not null);
        var loss = s.E<PrepPoisonHpLossPaidEvent>().Single(); var round = s.Round;
        Need(poison.PrepPoisonOutcome is { Cured: false, HpLossApplied: true, HpBefore: 1, HpAfter: -1, ActualHpLoss: 2 } &&
            loss is { OwnerSeat: 0, ActualCount: 2, HpBefore: 1, HpAfter: -1 } && loss.RequestedCount == round && round == 2 && s.V().Armor == 2 &&
            s.E<DamageAppliedEvent>().Length == 1 && s.E<RecoveryAppliedEvent>().Length == 0,
            "The genuine non-Diamond preparation pays current round two as one HP-loss instruction, bypassing retained Armor and entering a real dying window without another damage occurrence.");
        var poisonFrame = poison.Id; s.CheckCleanup(poison.PrepPoisonOutcome!.Outcome); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Niepan);
        s.Until(() => s.At(Gain));
        var nirvana = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Niepan);
        var drawn = s.G.CardMovements.Where(m => m.Reason.Value == NiepanDraw && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)).ToArray();
        Need(s.V() is { Hp: -1, HandCount: 3, IsChained: false, IsFaceDown: false } && drawn.Length == 3 &&
            s.E<RecoveryAppliedEvent>().Length == 0 && s.E<RecoveryAmountBonusAppliedEvent>().Length == 0 &&
            s.E<SkillUsageConsumedEvent>().Count(e => e.SkillId == Niepan && e.Scope == SkillUsageScope.Game) == 1 &&
            s.E<ProgramOwnedZoneCardsDiscardedEvent>().Single(e => e.SkillId == Niepan) is { Zones: [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment], CardCount: 4 } &&
            nirvana.NativeDraw is { RequestedCount: 3, ReturnKind: NativeDrawAttemptReturnKind.ProgramInstruction },
            "The exact new 95 clears the real three Hand cards and one equipped horse, faces up and unchains, then pays native Draw three while still dying; recovery has not been pulled ahead of its genuine gained-card children.");
        var nirvanaFrame = nirvana.Id; s.Privacy(); s.Cold(); s.Continue(); s.Until(() => s.At(Health));
        var bonus = s.E<RecoveryAmountBonusAppliedEvent>().Single();
        Need(bonus.Receipt is { BaseRequestedAmount: 4, ModifiedRequestedAmount: 6, HpBefore: -1, HpAfter: 5 } &&
            bonus.Receipt.Sources.Single().Source.SkillId == Medicine && s.E<RecoveryAppliedEvent>() is [{ TargetSeat: 0, Amount: 6, RemainingHp: 5 }] &&
            s.E<ProgramRecoveryRequestedEvent>().Single(e => e.ProgramFrameId == nirvanaFrame).RequestedAmount == 4 && s.V().Armor == 2,
            "Only after Draw children return does RecoverTo-three repay the full negative-HP debt: request four receives one live VI bonus and recovers six from minus one to five without spending Armor.");
        s.Cold(); s.Continue(); s.Play();
        Need(s.E<ProgramBindingResolvedEvent>().Single(e => e.FrameId == nirvanaFrame) is { Activated: true, Completed: true } &&
            s.E<PrepPoisonOutcomeCompletedEvent>().Single(e => e.ProgramFrameId == poisonFrame) is { Cured: false, ActualHpLoss: 2 } &&
            s.E<PrepPoisonHpLossPaidEvent>().Length == 1 && s.E<RecoveryAppliedEvent>().Length == 1 && s.E<SkillUsageConsumedEvent>().Count(e => e.SkillId == Niepan) == 1 &&
            drawn.All(m => s.G.CardMovements.Count(actual => actual.CardId == m.CardId && actual.Reason.Value == NiepanDraw) == 1) && s.Has(Poison),
            "The real dying, paid recovery and preparation parents all return once without reapplying poison HP loss, drawing again or removing the still-uncured status."); s.Cold();

        var empty = Scene.Start("empty"); empty.Until(() => empty.At(Lost));
        var noCard = empty.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PrepPoisonOutcome is not null).PrepPoisonOutcome!;
        Need(noCard.Outcome is { CardId: null, Suit: null, Rank: null } && noCard is { RoundNumber: 1, Cured: false, RequestedHpLoss: 1, ActualHpLoss: 1 } &&
            empty.V().Hp == 1 && empty.Has(Poison) && empty.E<PrepPoisonGrantRevokedEvent>().Length == 0 &&
            empty.G.CreateCardZoneDiagnostics().Count == 4 && !empty.G.CardMovements.Any(m => m.From.Zone == CardZoneKind.Judgment),
            "A legally exhausted four-card deck produces a true no-result judgment: no Diamond cure or cleanup entity is invented, and the real round-one HP loss still occurs.");
        empty.Cold(); empty.Continue(); empty.Play(); Need(empty.E<PrepPoisonOutcomeCompletedEvent>().Length == 1, "The no-card preparation returns after its one paid HP child."); empty.Cold();
    }

    public static void PaidPoisonSourcesAndActualRecipesStayIsolatedCold()
    {
        foreach (var name in new[] { "grant-loss", "grant-death", "paid-loss", "paid-death" })
        {
            var s = Scene.Start(name); s.Play(); s.Begin("two", [2]);
            var before = name.StartsWith("grant-", StringComparison.Ordinal); s.Until(() => s.At(before ? BeforeDraw : Mutation));
            var frame = s.RoundRoot.Id; var receipt = s.RoundRoot.DamageTargetSkillRoundDraw!;
            Need(receipt.GrantedGrant is not null && s.Has(Poison, 2) && (before ? receipt is { Stage: DamageTargetSkillRoundDrawStage.DrawAttempt, DrawOutcome: null } : receipt.DrawOutcome?.ActualDrawIds.Count == 1),
                "The source-change boundary is a real granted status followed by either its unpaid native attempt or its already-paid gained-card child.");
            if (before)
            {
                var attempt = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Context.ParentFrameId == frame);
                Need(attempt.Context is { RecipientSeat: 0, RequestedCount: 1 } && attempt.Return.Kind == NativeDrawAttemptReturnKind.ProgramOperation &&
                    s.RoundRoot.NativeDraw?.Operation == SkillProgramEffectOp.GrantDamageTargetSkillAndDrawByRound && s.Draws().Length == 0,
                    "An unpaid source change rides the operation's exact native pre-draw parent rather than a new raw draw or unrelated phase request.");
            }
            else s.CheckRoundPayment(receipt);
            s.Cold(); s.Continue();
            if (name.EndsWith("death", StringComparison.Ordinal))
            {
                s.Until(() => !s.V().IsAlive && s.E<DamageTargetSkillRoundCompletedEvent>().Any(e => e.ProgramFrameId == frame) && s.G.ResolutionStack.All(f => f.Id != frame));
                Need(s.G.State.Winner == Winner.None && s.V(1).IsAlive && s.Has(Poison, 2) &&
                    s.E<PlayerDiedEvent>().Count(e => e.VictimSeat == 0) == 1 && s.Draws().Length == (before ? 0 : 1) &&
                    s.E<DamageTargetSkillRoundDrawPaidEvent>().Single(e => e.ProgramFrameId == frame).ActualCount == (before ? 0 : 1) &&
                    s.E<SkillsAcquiredEvent>().Count(e => e.SourceSkillId == Cuidu && e.PlayerSeat == 2) == 1,
                    "A true dead source stops unissued cards but drains an already-paid Draw and returns its exact producer while an ally preserves the live battle; the independent target status is retained. " + s.Diagnostic());
            }
            else
            {
                s.Until(() => s.At(before ? BeforeDraw : Mutation));
                Need(s.V().Hp == 19 && !s.Has(Cuidu) && s.Has(Poison, 2), "The real HP child withdraws the derived original source without revoking the acquired target's independent Poison.");
                s.Cold(); s.Continue(); s.Play(); s.CompletedRound(frame);
                Need(s.Draws().Length == 1 && s.E<DamageTargetSkillRoundDrawPaidEvent>().Length == 1, "A living source's paid grant continues exactly one round draw across skill loss, including loss before the separate physical draw pays.");
                s.Use("one", [3]); Need(s.E<DamageTargetSkillRoundGrantedEvent>().Length == 1 && !s.Has(Poison, 3), "Future damage after genuine source loss has no new poison or draw qualification.");
                if (name == "paid-loss")
                {
                    s.EndPlay(); s.AtGuicai(2); s.Cold(); s.SkipGuicai();
                    s.Until(() => s.E<PrepPoisonOutcomeCompletedEvent>().Any());
                    Need(s.E<PrepPoisonHpLossPaidEvent>() is [{ OwnerSeat: 2, RequestedCount: 1, ActualCount: 1 }] && s.V(2).Hp == 17 && s.Has(Poison, 2),
                        "The acquired Poison really resolves on its recipient's later preparation after the original inflictor skill has gone; source loss does not merely leave a decorative skill in the view.");
                }
            }
            s.Cold();
        }
        VerifyActualRecipes();
    }

    private static void VerifyActualRecipes()
    {
        var old = StandardShanheCampaignCatalog.CreateDefinition(3, StandardShanheCampaignCatalog.LocalEditionId);
        Need(old.Nodes.SelectMany(n => n.Encounters ?? []).All(e => e.Enemies.All(m => m.GeneralId != Boss)), "The new complete Yudu form is absent from 2024.");
        for (var tier = 1; tier <= 3; tier++)
        {
            var definition = StandardShanheCampaignCatalog.CreateDefinition(tier, StandardShanheCampaignCatalog.Returning2026EditionId);
            var encounter = definition.Nodes.Single(n => n.Id == "dagu").Encounters!.Single(e => e.Id == "dagu:yudu-s5");
            Need(encounter.Enemies is [{ GeneralId: Boss, Hp: null, MaxHpBonus: 0, InitialHandBonus: 0, DrawBonus: 0, SlashBonus: 0, StartingHand: null }] &&
                (encounter.Allies?.Count ?? 0) == 0 && encounter.VictoryGold == 80 && encounter.HealAfterVictory == 2, "All local tiers retain raw three/three HP and the complete base form, without higher-band equipment, extra skills or growth.");
            var setup = new ShanheBattleSetup(83, 1, "dagu", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], encounter.Enemies, [], ShanheCampaignContent.GetContentHash(definition));
            var r = StandardShanheCampaignCatalog.CreateBattleRegistry(setup); var g = StandardShanheCampaignCatalog.CreateBattle(setup);
            Need(g.Submit(new StartGameCommand()).Accepted, "The actual conditional production encounter starts through its own recipe.");
            var npc = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == Boss);
            Need(npc is { Hp: 3, MaxHp: 3, HandCount: 4, Armor: 0 } && npc.Equipment.Count == 0 &&
                npc.Skills?.Select(k => k.ContentId).SequenceEqual([Cuidu, Poison, Niepan]) == true && r.GetSkill(Niepan).Tags.HasFlag(SkillTag.Limited),
                "The actual boss initializes its original three HP, ordinary four-card hand and exact locked/locked/limited three-skill source form.");
            Need(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "Actual startup, recipe and source fingerprints survive real cold replay.");
            var plain = setup with { EnemyRoster = [new("classic:cao-cao")] }; var extra = plain with { ExtraSkillIds = [Cuidu] };
            Need(!StandardShanheCampaignCatalog.CreateBattleRegistry(plain).Skills.ContainsKey(Cuidu) && StandardShanheCampaignCatalog.CreateBattleRegistry(extra).Skills.ContainsKey(Cuidu) &&
                !StandardShanheCampaignCatalog.CreateBattleRegistry(plain).Generals.ContainsKey(Boss), "Warm actual-alias, extra-skill and bare recipes preserve conditional registry/cache boundaries in both directions.");
        }
    }

    private static void VerifyLoaderBoundaries()
    {
        string Read(string suffix) { var a = typeof(StandardContentPackage).Assembly; using var stream = a.GetManifestResourceStream(a.GetManifestResourceNames().Single(n => n.EndsWith('.' + suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = Read("shanhe-s5-yudu.rules.json"); var presentation = Read("shanhe-s5-yudu.presentation.json"); _ = SkillProgramCatalog.Load(rules, presentation);
        foreach (var (index, change) in new (int, Action<JsonObject>)[] {
            (0, t => t["optional"] = true), (0, t => t["damageOccurrence"] = "perDamagePoint"), (0, t => t["subject"] = "owner"),
            (0, t => t["effects"]![0]!["amount"] = 0), (0, t => t["effects"]![0]!["amount"] = 21),
            (1, t => t["optional"] = true), (1, t => t["effects"]![2]!["suits"] = new JsonArray("heart")),
            (1, t => t["effects"]![2]!["sourceBind"] = "poison-card"), (1, t => t["effects"]![1]!["destination"] = "ownerHand") })
        {
            var copy = JsonNode.Parse(rules)!; change(copy["skills"]![index]!["triggers"]![0]!.AsObject()); var rejected = false;
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The new generic operations retain their mandatory per-damage source/cap and exact own-preparation final-Diamond judgment/cleanup/terminal-outcome contract.");
        }
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, g.PlayerCount).Select(i => SnapshotJson.Serialize(g.CreateSnapshot(i))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
        g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(g.CreateCheckpoint()) });

    private sealed class Scene(GameEngine game, ContentRegistry registry, string name)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public int Round => E<RoundStartedEvent>().LastOrDefault()?.RoundNumber ?? 0;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public ProgramSkillFrame RoundRoot => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DamageTargetSkillRoundDraw is not null);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Has(string skill, int seat = 0) => V(seat).Skills?.Any(s => s.ContentId == skill) == true;
        public bool At(string skill) => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.ContainsKey("option-id"));
        public bool AtNiepan() => P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Niepan);
        public CardMovementRecord[] Draws() => G.CardMovements.Where(m => m.Reason.Value == DrawReason && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)).ToArray();
        public void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, (result.Error?.Message ?? "Real Yudu command rejected.") + " " + Diagnostic()); }
        public void Answer(Func<PromptChoice, bool> choose) { var c = P!.Choices.FirstOrDefault(choose); Need(c is not null, "The expected native choice must be published. " + Diagnostic()); Accept(new AnswerPromptCommand(0, P!.PromptId, c!.Id, G.Revision)); }
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Begin(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Use(string id, int[]? targets = null) { Begin(id, targets); Play(); }
        public void Card(CardKind kind, int[] targets)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && V().Hand.Any(c => c.Id == id && c.Kind == kind) && a.ConversionSource is null && a.TargetSeats.SequenceEqual(targets));
            Need(action is not null, "The original physical card must have a genuinely legal native action. " + Diagnostic());
            Accept(new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void AtGuicai(int target) => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.ProgramJudgmentReplacement } && G.ResolutionStack.OfType<JudgmentFrame>().Single().TargetSeat == target);
        public void SkipGuicai() => Answer(c => c.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip");
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Until(Func<bool> done)
        {
            for (var i = 0; i < 240; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) { Continue(); continue; }
                    if (p.Kind == DecisionKind.RescueDying) { Answer(c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass"); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.Kind == DecisionKind.DiscardCards) { Accept(new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, G.Revision)); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    throw new InvalidOperationException("Unexpected real Yudu boundary: " + Diagnostic());
                }
                Need(G.State.Winner == Winner.None, "The bounded Yudu fixture must stay live. " + Diagnostic()); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded Yudu native boundary was not reached: " + Diagnostic());
        }
        public void CheckRoundPayment(DamageTargetSkillRoundDrawReceipt r)
        {
            var frame = RoundRoot; var outcome = frame.DamageTargetSkillRoundDraw!.DrawOutcome!;
            var paid = E<DamageTargetSkillRoundDrawPaidEvent>().Single(e => e.ProgramFrameId == frame.Id);
            var window = G.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single(w => w.Id == r.DamageWindowId);
            var moves = G.CardMovements.Where(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter && m.Reason.Value == DrawReason).ToArray();
            Need(frame.Id == r.ProgramFrameId && r.Source is { SkillId: Cuidu, OwnerSeat: 0 } && r.GameplayHash == registry.GetSkill(Cuidu).Program!.GameplayHash &&
                window.ParentFrameId == r.DamageFrameId && window.TargetSeat == r.TargetSeat && window.SourceSeat == 0 &&
                r.DrawCount == Math.Min(5, r.RoundNumber) && r.GrantedSkillId == Poison && outcome.RecipientSeat == 0 && outcome.RequestedCount == r.DrawCount && outcome.ActualDrawIds.Count == r.DrawCount &&
                paid is { Replaced: false, Cancelled: false } && paid.ActualCount == r.DrawCount && paid.RequestedCount == r.DrawCount && paid.SequenceBefore == outcome.SequenceBefore && paid.SequenceAfter == outcome.SequenceAfter &&
                moves.Select(m => m.CardId).SequenceEqual(outcome.ActualDrawIds) && moves.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) &&
                outcome.ActualDrawIds is IList<int> { IsReadOnly: true } && moves.All(m => G.CardMovements.Count(actual => actual.Sequence == m.Sequence && actual == m) == 1),
                "Each paid source-round operation retains the exact native damage window/frame, immutable independent grant identity and original Draw-only outcome/interval once. " + Diagnostic());
        }
        public void CompletedRound(long frame) => Need(E<DamageTargetSkillRoundCompletedEvent>().Count(e => e.ProgramFrameId == frame) == 1 && G.ResolutionStack.All(f => f.Id != frame), "The exact granted-status/draw producer returns once without an orphan or duplicate payment. " + Diagnostic());
        public void CheckCleanup(ProgramJudgmentOutcomeBinding outcome)
        {
            Need(outcome.CardId is { } id && G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program." + Poison + ".MoveBoundCards") == 1 &&
                G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location == CardLocation.DiscardPile,
                "The exact final judgment entity is cleaned once before the terminal poison outcome; no retained hand identity or invented result is substituted.");
        }
        public void Privacy() => Need(Enumerable.Range(0, G.PlayerCount).All(i => G.CreateSnapshot(i).Players.Where(p => p.Seat != i).All(p => p.Hand.Count == 0)) &&
            Enumerable.Range(1, G.PlayerCount - 1).All(i => G.CreateSnapshot(i).PendingDecision is null), "Only the real human recipient sees its private cost/response; trusted grant receipts never expose foreign hands.");
        public void Cold() { var before = State(G); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State(G) == before, "Cold replay preserves native damage/grant/draw, final judgment/cleanup, paid poison HP loss and dying recovery child ownership without repeated costs."); }
        public string Diagnostic() => JsonSerializer.Serialize(new { name, Round, G.State.TurnNumber, G.State.Winner,
            Seats = G.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.Armor, p.HandCount, p.IsAlive, Skills = p.Skills?.Select(s => s.ContentId) }),
            Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId, Choices = P.Choices.Select(c => c.Id) },
            Grants = E<DamageTargetSkillRoundGrantedEvent>(), Draws = E<DamageTargetSkillRoundDrawPaidEvent>(), Poison = E<PrepPoisonHpLossPaidEvent>(), Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind }) });
        public static Scene Start(string name)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(name)); var cap = name == "cap"; var sick = name is "diamond" or "niepan";
            ShanheStartingCard[] cards = name == "damage" ? [new(CardKind.IronChain, Suit.Club, 3), new(CardKind.FireSlash, Suit.Heart, 5)] : name is "jueqing" or "maximum" ? [new(CardKind.FireSlash, Suit.Heart, 5)] :
                sick ? [new(CardKind.Dodge, Suit.Diamond, 10), new(CardKind.Dodge, Suit.Diamond, 11), new(CardKind.Dodge, Suit.Spade, 9)] : [];
            if (name == "niepan") cards = [.. cards, new(CardKind.IronChain, Suit.Club, 3)];
            var setup = new ShanheBattleSetup(83, 1, "fixture:s5-yudu-" + name, Owner, sick ? 1 : name == "empty" ? 2 : 20, 0, 0, 0, 0, [], cards,
                name == "niepan" ? [new(CardKind.DefensiveHorse, Suit.Spade, 5)] : [],
                cap ? [new(Enemy) { StartingHand = [] }] : [new(Enemy, Hp: name == "lethal" ? 1 : 20, ExtraSkillIds: sick ? [Cuidu] : []) { StartingHand = name == "empty" ? null : [] }, new(Enemy) { StartingHand = name == "empty" ? null : [] }],
                cap ? [] : [new(Ally) { StartingHand = name == "empty" ? null : [] }], new string('a', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = cap ? 2 : 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = cap ? 24 : 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scene(g, r, name); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(string name) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-s5-yudu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in new[] { Cuidu, Poison, Niepan, Guicai, Medicine, "classic:jueqing", "shanhe:xiaoming" }) b.AddSkill(Production.Value.GetSkill(id));
            var defs = new List<Dictionary<string, object>>(); var labels = new Dictionary<string, object>();
            object Pause(string id) => new { op = "chooseOption", target = "owner", resultBind = id, options = new[] { new { id } } };
            void Add(string id, Dictionary<string, object> value, params string[] options) { value["id"] = id; value["revision"] = 1; defs.Add(value); labels[id] = new { name = id, description = "真实伤害、判定、授毒及濒死原生小夹具", optionLabels = options.ToDictionary(s => s, s => s) }; }
            object Act(string id, int targets, params object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = targets, maxTargets = targets, targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            Add(Driver, new() { ["modifiers"] = new object[] { new { id = "legal-physical-range", query = "attackRange", operation = "add", value = 8, priority = 0 }, new { id = "retain-real-materials", query = "handLimit", operation = "add", value = 12, priority = 0 } },
                ["activations"] = new object[] { Act("one", 1, new { op = "damage", target = "selectedTarget", amount = 1 }), Act("two", 1, new { op = "damage", target = "selectedTarget", amount = 2 }),
                    Act("armor-two", 1, new { op = "grantArmor", target = "selectedTarget", amount = 2 }), Act("incoming", 1, new { op = "damage", target = "owner", sourceRef = new { kind = "selectedTarget" }, amount = 2 }),
                    Act("source-less", 0, new { op = "receiveOwnerDamage", target = "owner", amount = 1 }) } });
            Add(Gain, new() { ["triggers"] = new[] { new { id = "true-paid-draw", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementReasons = new[] { DrawReason, NiepanDraw }, movementOccurrence = "perBatch", optional = false, effects = new[] { Pause("continue") } } } }, "continue");
            var death = name.EndsWith("death", StringComparison.Ordinal); var beforeEffects = new List<object> { Pause("before") };
            beforeEffects.Add(new { op = "loseHp", target = "owner", amount = death ? 20 : 1 }); if (!death) beforeEffects.Add(Pause("lost"));
            Add(BeforeDraw, new() { ["triggers"] = new[] { new { id = "real-granted-native-attempt", window = "nativeDrawStarting", subject = "owner", optional = false, effects = beforeEffects.ToArray() } } }, death ? ["before"] : ["before", "lost"]);
            var mutations = new List<object> { Pause("paid") }; mutations.Add(new { op = "loseHp", target = "owner", amount = death ? 20 : 1 }); if (!death) mutations.Add(Pause("lost"));
            Add(Mutation, new() { ["triggers"] = new[] { new { id = "first-paid-source-loss", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementReasons = new[] { DrawReason }, movementOccurrence = "perBatch", priority = 200, usageScope = "game", usageLimit = 1, optional = false, effects = mutations.ToArray() } } }, death ? ["paid"] : ["paid", "lost"]);
            Add(Gate, new() { ["hpSkillGrants"] = new[] { new { id = "real-source-threshold", skillId = Cuidu, minimumHp = 20 } } });
            Add(Health, new() { ["triggers"] = new[] { new { id = "real-recovery-child", window = "afterHpRecovered", subject = "owner", optional = false, effects = new[] { Pause("recovered") } } } }, "recovered");
            Add(Lost, new() { ["triggers"] = new[] { new { id = "real-poison-hp-child", window = "afterHpLost", subject = "owner", optional = false, effects = new[] { Pause("lost") } } } }, "lost");
            Add(Quiet, new() { ["triggers"] = new[] { new { id = "quiet-real-peer", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var skills = new List<string>();
            if (name is "damage" or "cap" or "lethal") skills.Add(Cuidu);
            if (name is "diamond" or "niepan") skills.Add(Guicai);
            if (name == "niepan") skills.AddRange([Niepan, Medicine, Health]);
            if (name == "empty") skills.AddRange([Poison, Lost]);
            if (name is "jueqing" or "maximum") skills.Add(name == "jueqing" ? "classic:jueqing" : "shanhe:xiaoming");
            if (name.Contains("loss", StringComparison.Ordinal) || name.Contains("death", StringComparison.Ordinal)) skills.Add(Gate);
            if (name.StartsWith("grant-", StringComparison.Ordinal)) skills.Add(BeforeDraw);
            if (name.StartsWith("paid-", StringComparison.Ordinal)) skills.Add(Mutation);
            if (name == "paid-loss") skills.Add(Guicai);
            if (name is "damage" or "cap" or "lethal" or "niepan" || name.StartsWith("grant-", StringComparison.Ordinal)) skills.Add(Gain);
            b.AddGeneral(new(Owner, "真实淬毒或中毒拥有者", "", Driver, "qun", name is "diamond" or "niepan" or "empty" ? 6 : 20, skills));
            b.AddGeneral(new(Ally, "真实同队存活角色", "", Quiet, "qun", 20)); b.AddGeneral(new(Enemy, "真实敌方目标", "", Quiet, "wei", 20));
            var standard = ContentRegistry.Build(new StandardContentPackage()); foreach (var kind in new[] { CardKind.FireSlash, CardKind.IronChain, CardKind.DefensiveHorse })
                if (standard.Cards.Values.All(c => c.LegacyKind != kind)) { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:s5-yudu-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            b.AddDeck(new(Deck, "固定真实黑判定牌、零自然摸牌", name == "empty" ? 1 : 0, 0, []) { PhysicalCards = Enumerable.Range(0, name == "empty" ? 4 : 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实伤害授毒及准备濒死", name == "cap" ? 2 : 4, name == "cap" ? 2 : 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = name == "cap" ? 1 : 2, [ShanheBattleSetup.EnemyTeamId] = name == "cap" ? 1 : 2 }));
        }
    }
}
