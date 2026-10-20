using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5DongMinChecks
{
    private const string Boss = StandardShanheS5BossPackage.DongMinBaseId, Cap = StandardShanheS5BossPackage.DungongId;
    private const string Tactic = "shanhe:jingyu-moulue-2-2026", Jueqing = "classic:jueqing", Xiaoming = "shanhe:xiaoming";
    private const string Owner = "fixture:dungong-owner", Peer = "fixture:dungong-peer", Driver = "fixture:dungong-driver";
    private const string Watch = "fixture:dungong-damage-child", Suppress = "fixture:dungong-hp-suppression", Deck = "fixture:dungong-deck";
    private const string Late = "fixture:dungong-after-origin-grant", HiddenMode = "identity:classic-dungong-hidden", Gaoling = "ol:gaoling";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture-dungong-production", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], [new(Boss)], [], new string('c', 64))));

    public static void NativeFixedCapPrecedesArmorAndPublicPredictionCold()
    {
        VerifyActualEncounterIsolation();
        VerifyLoaderBoundaries();
        foreach (var armor in new[] { 2, 4 })
        {
            var s = Scene.Start("native"); s.Use("armor-" + armor, [3]); s.PlayCard(CardKind.Alcohol);
            var action = s.CardAction(CardKind.Slash, [3]); var before = s.State();
            var prediction = s.G.PredictPublicActionDamage(0, action, 3);
            Need((prediction is { DamageAmount: 3, ArmorAbsorbed: 3, HpLossAmount: 0 } && armor == 4 ||
                prediction is { DamageAmount: 3, ArmorAbsorbed: 2, HpLossAmount: 1 } && armor == 2) &&
                Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players[3].IncomingDamageCap == 3),
                "The actual visible fixed cap applies to the full public five-damage Slash estimate before Armor, rather than capping predicted HP loss.");
            Need(prediction.HpAfter == (armor == 2 ? 4 : 5) && prediction.ArmorAfter == (armor == 2 ? 0 : 1) &&
                !prediction.IsLethal && s.State() == before,
                "A pure real-action estimate neither invents a lethal five-HP hit nor advances rules, spends Alcohol, moves a card or consumes a grant.");
            var paid = s.BeginCard(action); s.AtDamageChild();
            var hit = s.Events<DamageAppliedEvent>().Single(); var frame = s.G.ResolutionStack.OfType<DamageFrame>().Single();
            Need(s.Events<ProgramCardDamageModifiedEvent>().Where(e => e.Source.SkillId == Tactic)
                    .Select(e => (e.BaseAmount, e.ModifiedAmount)).SequenceEqual([(2, 3), (3, 4), (4, 5)]) &&
                s.Events<ProgramDamageCappedEvent>() is [{ SourceSeat: 0, TargetSeat: 3, BaseAmount: 5, CappedAmount: 3, Source.SkillId: Cap }] &&
                hit is { Amount: 3, SourceSeat: 0, TargetSeat: 3, SourceLess: false, ArmorPayment: not null } &&
                hit.ArmorPayment.DamageAmount == 3 && hit.ArmorPayment.ArmorBefore == armor &&
                hit.ArmorPayment.ArmorAbsorbed == Math.Min(3, armor) && hit.ArmorPayment.HpBefore == 5 &&
                hit.ArmorPayment.HpLossAmount == (armor == 2 ? 1 : 0) && hit.ArmorPayment.HpAfter == s.V(3).Hp &&
                hit.ArmorPayment.ActualHpLost == (armor == 2 ? 1 : 0) &&
                frame.ArmorPayment == hit.ArmorPayment && frame.Id == hit.ArmorPayment.DamageFrameId &&
                frame.ParentFrameId == hit.ArmorPayment.AttackFrameId && !s.V().HasAlcoholEffect,
                "The real Alcohol/physical Slash and three independent native bonuses issue five damage, one fixed cap to three, then one exact Armor/HP receipt on the owning DamageFrame.");
            s.Cold(); s.Continue(); s.Play();
            Need(s.Events<AfterDamageEvent>() is [{ TargetSeat: 3, Amount: 3 }] && s.Events<ProgramDamageCappedEvent>().Length == 1 &&
                s.Events<DamageAppliedEvent>().Length == 1 && s.Events<CardUseDebitRecordedEvent>().Count(e => e.Debit.ActorSeat == 0) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
                "Full absorption still creates genuine three-point AfterDamage; cold child recovery neither recaps nor repays the original physical Slash or native quota."); s.Cold();
        }

        var low = Scene.Start("plain"); low.Begin("hit-two", [3]); low.AtDamageChild(); low.Cold(); low.Continue(); low.Play();
        Need(low.Events<ProgramDamageCappedEvent>().Length == 0 && low.Events<DamageAppliedEvent>() is [{ Amount: 2, TargetSeat: 3 }] &&
            low.V(3).Hp == 3, "A true two-point program hit remains two; a maximum is never a set-to-three bonus.");
        VerifySilverLionAndReplacementOrder();
        VerifyProgramAiUsesTheVisibleRecipientCap();
        VerifyConcealedGeneralDoesNotPublishADisabledCap();
    }

    public static void ElementalChainAndLiveCapSourcesRecheckWithoutInflatingLowerDamageCold()
    {
        foreach (var originCap in new[] { true, false })
        {
            var s = Scene.Start(originCap ? "chain-origin" : "chain-middle");
            s.Use(originCap ? "armor-4" : "armor-2", [1]); s.Use(originCap ? "armor-2" : "armor-4", [2]);
            var chainA = s.PlayCard(CardKind.IronChain, [1, 2]); var chainB = s.PlayCard(CardKind.IronChain, [3]);
            Need(new[] { 1, 2, 3 }.All(seat => s.V(seat).IsChained), "Two original Iron Chain entities genuinely link the three selected recipients.");
            s.PlayCard(CardKind.Alcohol); var slash = s.BeginCard(s.CardAction(CardKind.ThunderSlash, [1]));
            var expected = originCap ? new[] { 3, 3, 3 } : [5, 3, 5];
            for (var recipient = 1; recipient <= 3; recipient++)
            {
                s.AtDamageChild(); var hit = s.Events<DamageAppliedEvent>().Last();
                Need(hit.TargetSeat == recipient && hit.Amount == expected[recipient - 1] && hit.Nature == DamageNature.Thunder &&
                    s.Events<DamageAppliedEvent>().Length == recipient &&
                    (recipient == 1 ? hit.ArmorPayment?.HpLossAmount == (originCap ? 0 : 3) :
                        recipient == 2 ? hit.ArmorPayment?.HpLossAmount == (originCap ? 1 : 0) : hit.ArmorPayment is null),
                    "Each genuine linked recipient pays its own finalized cap/Armor amount: " + s.Diagnostic());
                s.Cold(); s.Continue();
            }
            s.Play();
            Need(s.Events<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual(new[] { (1, expected[0]), (2, expected[1]), (3, expected[2]) }) &&
                s.Events<AfterDamageEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual(new[] { (1, expected[0]), (2, expected[1]), (3, expected[2]) }) &&
                s.Events<ChainedDamagePropagatedEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual(new[] { (2, expected[0]), (3, expected[0]) }) &&
                s.Events<ProgramDamageCappedEvent>() is [var capped] && capped.Source.SkillId == Cap &&
                capped.TargetSeat == (originCap ? 1 : 2) && capped.BaseAmount == 5 && capped.CappedAmount == 3 &&
                s.Events<RecipientScopedDamageBaseEvent>() is [{ FixedTargetDamageCap: true, Amount: var issuedBase }] && issuedBase == expected[0] &&
                s.Events<ProgramCardDamageModifiedEvent>().Count(e => e.Source.SkillId == Tactic) == 3 &&
                s.V(1).Armor == (originCap ? 1 : 0) && s.V(2).Armor == (originCap ? 0 : 1) &&
                s.V(1).Hp == (originCap ? 5 : 9) && s.V(2).Hp == (originCap ? 11 : 5) && s.V(3).Hp == (originCap ? 9 : 7),
                "The first actual finalized amount is the fixed propagation base: capped origin gives 3/3/3; only a capped middle gives 5/3/5 without rewriting the tail or propagating HP loss.");
            Need(new[] { chainA, chainB, slash }.All(card => s.G.CardMovements.Count(m => m.CardId == card &&
                m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1),
                "The real chained hit retains one physical payment for each original Iron Chain and elemental Slash."); s.Cold();
        }

        var noSource = Scene.Start("source-less"); noSource.Use("armor-2", [0]);
        var beforeSourceLess = noSource.State(); var sourceLessHint = noSource.G.GetHumanLegalActions().Single(a =>
            a.ProgramSkillId == Driver && a.ProgramActivationId == "source-less").ProgramAiHint!;
        Need(sourceLessHint is { OwnerDamage: 3, OwnerArmorLoss: 2, OwnerHpLoss: 1, OwnerDirectHpLoss: 0, OwnerWouldEnterDying: false } &&
            noSource.State() == beforeSourceLess,
            "The real ReceiveOwnerDamage5 hint preserves source-less Damage semantics: cap3 then Armor2 loses one HP, while the owner's own Jueqing cannot turn a nonexistent source into direct loss.");
        noSource.Begin("source-less"); noSource.AtDamageChild();
        Need(noSource.Events<ProgramDamageCappedEvent>() is [{ SourceSeat: null, TargetSeat: 0, BaseAmount: 5, CappedAmount: 3, Source.SkillId: Cap }] &&
            noSource.Events<DamageAppliedEvent>() is [{ TargetSeat: 0, Amount: 3, SourceLess: true,
                ArmorPayment: { ArmorBefore: 2, ArmorAbsorbed: 2, ArmorAfter: 0, HpLossAmount: 1 } }] && noSource.V().Hp == 11 &&
            noSource.Events<DamageReplacedWithHpLossEvent>().Length == 0,
            "A genuine source-less native owner-damage instruction is capped and pays Armor while its public cap fact retains a null source.");
        noSource.Cold(); noSource.Continue(); noSource.Play();
        Need(noSource.Events<AfterDamageEvent>() is [{ Amount: 3, TargetSeat: 0 }] && noSource.Events<ProgramDamageCappedEvent>().Length == 1,
            "Cold source-less completion preserves the same three-point real damage without inventing an actor or duplicate cap."); noSource.Cold();

        var live = Scene.Start("live"); live.Use("armor-5", [3]); Need(live.V(3).IncomingDamageCap == 3, "The actually enabled original target cap is published before suppression.");
        live.Use("hurt-other", [3]); Need(live.V(3) is { Hp: 4, IncomingDamageCap: null } &&
            !JsonSerializer.Serialize(live.V(3)).Contains("\"IncomingDamageCap\"", StringComparison.Ordinal),
            "A real HP payment enters the existing exact-HP suppression condition; the now-absent derived cap preserves the old null-omitted snapshot shape.");
        live.Begin("hit-five", [3]); live.AtDamageChild();
        Need(live.V(3) is { Hp: 4, Armor: 0, IncomingDamageCap: null } && live.Events<ProgramDamageCappedEvent>().Length == 0 &&
            live.Events<DamageAppliedEvent>() is [{ Amount: 5, ArmorPayment: { ArmorBefore: 5, ArmorAbsorbed: 5, HpLossAmount: 0 } }],
            "A registered but genuinely suppressed cap cannot constrain a later native damage attempt; displaying the printed skill is not proof of an active binding.");
        live.Cold(); live.Continue(); live.Play(); live.Use("recover-one", [3]); Need(live.V(3) is { Hp: 5, IncomingDamageCap: 3 },
            "Native recovery leaves the suppressing HP boundary and restores the public cap without replacing the general or injecting a grant.");
        live.Begin("hit-five", [3]); live.AtDamageChild();
        Need(live.V(3).Hp == 2 && live.Events<ProgramDamageCappedEvent>() is [{ BaseAmount: 5, CappedAmount: 3 }] &&
            live.Events<DamageAppliedEvent>().Select(e => e.Amount).SequenceEqual([5, 3]),
            "The restored original cap is re-evaluated and applied exactly once to the next attempt.");
        live.Cold(); live.Continue(); live.Play(); live.Cold();
        var loss = Scene.Start("loss"); loss.Use("self-five"); loss.Use("lose-cap");
        Need(loss.V().Hp == 9 && loss.Events<ProgramOwnerSkillsReplacedEvent>() is [{ OwnerSeat: 0, LostSkillIds: [Cap] }] && loss.V().IncomingDamageCap is null,
            "The existing legal terminal replacement operation actually removes the original cap grant.");
        loss.Cold(); loss.Begin("self-five"); loss.AtDamageChild();
        Need(loss.V().Hp == 4 && loss.Events<DamageAppliedEvent>().Select(e => e.Amount).SequenceEqual([3, 5]) &&
            loss.Events<ProgramDamageCappedEvent>().Length == 1, "Actual skill loss affects future attempts without retroactively recapping or repaying the previous hit.");
        loss.Cold(); loss.Continue(); loss.Play(); loss.Cold();
        VerifyCapAcquiredAfterTheFirstActualChainPayment();
    }

    private static void VerifySilverLionAndReplacementOrder()
    {
        var lion = Scene.Start("lion"); lion.EquipSilverLion(3); lion.Use("armor-2", [3]); lion.PlayCard(CardKind.Alcohol);
        var action = lion.CardAction(CardKind.Slash, [3]); var before = lion.State(); var predicted = lion.G.PredictPublicActionDamage(0, action, 3);
        Need(predicted is { DamageAmount: 1, ArmorAbsorbed: 1, HpLossAmount: 0 } && lion.State() == before,
            "Visible Silver Lion and a larger skill cap combine by their minimum, with the same pure public prediction.");
        lion.BeginCard(action); lion.AtDamageChild();
        Need(lion.Events<DamageAppliedEvent>() is [{ Amount: 1, ArmorPayment: { ArmorBefore: 2, ArmorAbsorbed: 1, ArmorAfter: 1, HpLossAmount: 0 } }] &&
            lion.Events<SilverLionDamageCappedEvent>() is [{ BaseAmount: 5, ModifiedAmount: 1 }] && lion.Events<ProgramDamageCappedEvent>().Length == 0,
            "Silver Lion's real cap to one is never followed by a fictitious Dungong 1-to-3 fact or payment."); lion.Cold(); lion.Continue(); lion.Play(); lion.Cold();
        foreach (var scene in new[] { "jueqing", "maximum" })
        {
            var s = Scene.Start(scene); s.Use("armor-4", [3]); s.PlayCard(CardKind.Alcohol); s.PlayCard(CardKind.Slash, [3]);
            Need(s.Events<ProgramDamageCappedEvent>().Length == 0 && s.Events<DamageAppliedEvent>().Length == 0 &&
                s.Events<AfterDamageEvent>().Length == 0 && s.V(3) is { Hp: 3, Armor: 4 } &&
                (scene == "jueqing" ? s.Events<DamageReplacedWithHpLossEvent>() is [{ TargetSeat: 3, Amount: 2 }] && s.V(3).MaxHp == 5 :
                    s.Events<CardDamageReplacedWithMaximumHpLossEvent>() is [{ Receipt.TargetSeat: 3, Receipt.Amount: 2 }] && s.V(3).MaxHp == 3),
                "Native Jueqing or the real first-round maximum-HP replacement consumes its original two-point card layer before later additional bonuses, Dungong and Armor; it creates no real damage facts."); s.Cold();
        }
    }

    private static void VerifyProgramAiUsesTheVisibleRecipientCap()
    {
        var s = Scene.Start("ai"); var before = s.State(); var view = s.G.CreateSnapshot(0);
        var action = s.G.GetHumanLegalActions().Single(a => a.ProgramSkillId == Driver && a.ProgramActivationId == "hit-five");
        var hint = action.ProgramAiHint!; var boss = view.Players[3];
        var capped = hint.PredictTarget(boss.Hp, boss.MaxHp, boss.Armor, boss.IncomingDamageCap);
        var uncapped = hint.PredictTarget(4, 12, 0);
        var brain = new SimpleAiBrain(0, 83, AiDecisionPolicy.CurrentVersion);
        var end = s.G.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.EndPlay);
        var (chosen, _) = brain.ChoosePlay(view, [action, end], 1); var targets = brain.ChooseActiveSkillTargets(view, chosen, []);
        Need(boss.IncomingDamageCap == 3 && capped is { DamageAmount: 3, HpLossAmount: 3, HpAfter: 2, IsLethal: false } &&
            uncapped is { DamageAmount: 5, HpLossAmount: 5, HpAfter: -1, IsLethal: true } && chosen == action && targets.SequenceEqual([2]) &&
            brain.ScoreProgramTarget(view, 2, hint) > brain.ScoreProgramTarget(view, 3, hint) && s.State() == before,
            "The same real Damage5 action values the visible capped five-HP boss as nonlethal and selects the genuinely vulnerable four-HP enemy through public native program AI, without advancing rules.");
        s.Use(chosen.ProgramActivationId!, targets.ToArray());
        Need(!s.V(2).IsAlive && s.V(3) is { Hp: 5, IncomingDamageCap: 3 } &&
            s.Events<DamageAppliedEvent>() is [{ TargetSeat: 2, Amount: 5, RemainingHp: -1 }] && s.Events<ProgramDamageCappedEvent>().Length == 0,
            "Executing that real AI-selected command harms only the uncapped enemy; it does not consume or issue an unrelated registered boss cap."); s.Cold();
    }

    private static void VerifyConcealedGeneralDoesNotPublishADisabledCap()
    {
        var s = Scene.Start("hidden");
        foreach (var seat in new[] { 1, 2, 3 })
        {
            Need(s.G.CreateSnapshot(seat).Players[seat] is { IdentityConcealed: true, IncomingDamageCap: null } own &&
                own.Skills?.Any(skill => skill.ContentId == Cap) == true && Enumerable.Range(0, 4).All(viewer =>
                    s.G.CreateSnapshot(viewer).Players[seat].IncomingDamageCap is null),
                "Real identity concealment disables even the owner's known original cap, rather than exposing or enabling it through a derived scalar.");
            var foreign = s.G.CreateSnapshot(0).Players[seat];
            Need(foreign.GeneralId == "concealment:ol" && (foreign.Skills?.Count ?? 0) == 0 &&
                !JsonSerializer.Serialize(foreign).Contains("\"IncomingDamageCap\"", StringComparison.Ordinal),
                "A foreign view cannot infer the concealed original cap from either skill metadata or the new null-omitted derived field.");
        }
        s.Cold(); s.Use("hurt-other", [1]);
        Need(s.Events<IdentityGeneralAppearedEvent>() is [{ Receipt.OwnerSeat: 1, Receipt.Entry: IdentityAppearanceEntry.ProgramHpLoss }] &&
            s.Events<IdentityHpDeductionPreventedEvent>() is [{ OwnerSeat: 1, RequestedDeduction: 1 }] &&
            Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players[1] is
                { IdentityConcealed: false, IsGeneralPublic: true, Hp: 5, IncomingDamageCap: 3 }),
            "The native first-loss appearance reveals its actual template, prevents that one deduction, and publishes the now-enabled cap to every real viewer."); s.Cold();
    }

    private static void VerifyCapAcquiredAfterTheFirstActualChainPayment()
    {
        var s = Scene.Start("chain-late"); s.Use("armor-2", [1]); s.Use("armor-4", [0]);
        s.PlayCard(CardKind.IronChain, [0, 1]); s.PlayCard(CardKind.IronChain, [2]); s.PlayCard(CardKind.Alcohol);
        Need(new[] { 0, 1, 2 }.All(seat => s.V(seat).IsChained) && !s.V(3).IsChained,
            "Real Iron Chain payments prepare origin1, the owner0 as middle recipient and tail2; seat3 is genuinely outside this chain.");
        s.BeginCard(s.CardAction(CardKind.ThunderSlash, [1]));
        s.AtSkill(Late);
        var paidFirst = s.G.ResolutionStack.OfType<DamageFrame>().Single();
        Need(s.Events<DamageAppliedEvent>() is [{ TargetSeat: 1, Amount: 5 }] && s.Events<RecipientScopedDamageBaseEvent>().Length == 0 &&
            new[] { 1, 0, 2 }.All(seat => s.V(seat).IncomingDamageCap is null),
            "The first real five-damage segment has already paid without an active cap or an issued cap scope; registration alone cannot manufacture its baseline.");
        s.Cold(); s.Continue(); s.AtSkill(Late);
        Need(s.V(0).IncomingDamageCap == 3 && s.Events<DamageAppliedEvent>().Length == 1 && s.Events<ProgramDamageCappedEvent>().Length == 0,
            "A genuine owner GrantSkills acquires the actual cap only during the first segment's native AfterDamage child, before this owner is paid as the middle chained recipient.");
        s.Cold(); s.Continue();
        foreach (var recipient in new[] { 1, 0, 2 })
        {
            s.AtDamageChild(); Need(s.Events<DamageAppliedEvent>().Last().TargetSeat == recipient,
                "The late-grant child returns to the original native chain recipient before the next actual payment.");
            if (recipient == 1) Need(s.Events<RecipientScopedDamageBaseEvent>().Length == 0,
                "Even after the actual grant, the first paid damage frame's unfinished child has not prematurely issued a late base.");
            if (recipient == 0) Need(s.Events<RecipientScopedDamageBaseEvent>() is
                [{ FixedTargetDamageCap: true, Amount: 5, LateFixedTargetDamageCap: { } proof }] &&
                proof.DamageFrameId == paidFirst.Id && proof.AttackFrameId == paidFirst.ParentFrameId &&
                proof.PaidTargetSeat == 1 && proof.NextTargetSeat == 0 && proof.Amount == 5 && proof.CapSource.OwnerSeat == 0 &&
                proof.ChainTargetIndex == 0 && proof.NextTargetIndex == 0 && proof.ProgramUseInstructionIndex is null &&
                !string.IsNullOrEmpty(proof.ChainTargetsHash) && proof.RequestedEventIndex < proof.AppliedEventIndex && proof.AppliedEventIndex < proof.AfterDamageEventIndex,
                "Only completion of the original paid DamageFrame issues one exact late-cap proof, retaining its native parent, full five-damage base, chain cursor and ordered requested/applied/after facts.");
            s.Cold(); s.Continue();
        }
        s.Play();
        Need(s.Events<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(1, 5), (0, 3), (2, 5)]) &&
            s.Events<ProgramDamageCappedEvent>() is [{ TargetSeat: 0, BaseAmount: 5, CappedAmount: 3 }] &&
            s.Events<RecipientScopedDamageBaseEvent>() is [{ FirstTargetSeat: 1, Amount: 5, FixedTargetDamageCap: true }] &&
            s.Events<ChainedDamagePropagatedEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(0, 5), (2, 5)]) &&
            s.V(1) is { Hp: 9, Armor: 0 } && s.V(0) is { Hp: 12, Armor: 1 } && s.V(2).Hp == 7,
            "A cap acquired after the first paid segment still gives real 5/3/5, issuing exactly one proven original baseline without rolling the middle cap into the tail."); s.Cold();
    }

    private static void VerifyLoaderBoundaries()
    {
        string Resource(string suffix)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith('.' + suffix, StringComparison.Ordinal)))!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var rules = Resource("shanhe-current-dungong.rules.json"); var presentation = Resource("shanhe-current-dungong.presentation.json");
        void Reject(Action<JsonObject> mutate)
        {
            var copy = JsonNode.Parse(rules)!; var modifier = copy["skills"]![0]!["damageModifiers"]![0]!.AsObject(); mutate(modifier);
            var rejected = false; try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The shared Always fixed cap only accepts a bounded 1..20 incoming-participant cap with empty card kinds and the existing damage layer contract.");
        }
        Reject(m => m["sourceScope"] = "ownerUsed"); Reject(m => m["sourceScope"] = "allLivingSkillOwners");
        Reject(m => m["cardKinds"] = new JsonArray("slash")); Reject(m => m["amount"] = 0); Reject(m => m["amount"] = 21);
        Reject(m => m["damageLayer"] = "cardDamage"); Reject(m => m.Remove("capsToAmount"));
        Reject(m => { m["condition"] = "damageSourceAttackRangeBelowThree"; m["amount"] = 3; });
    }

    private static void VerifyActualEncounterIsolation()
    {
        var modern = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        var encounter = modern.Nodes.Single(node => node.Id == "luoyang").Encounters!.Single(e => e.Enemies.Any(npc => npc.GeneralId == Boss));
        var general = Production.Value.Generals[Boss];
        Need(encounter.Enemies is [{ GeneralId: Boss, MaxHpBonus: 0 }] && general.BaseHp == 5 && general.SkillIds.SequenceEqual([Cap]) &&
            StandardShanheCampaignCatalog.CreateDefinition(1).Nodes.SelectMany(n => n.Encounters ?? []).SelectMany(e => e.Enemies).All(npc => npc.GeneralId != Boss),
            "The declared local 2026 candidate actually uses the separate five-HP single-Dungong base form; the 2024 campaign does not silently acquire it.");
        var ordinary = StandardShanheCampaignCatalog.CreateBattleRegistry(new(83, 1, "fixture-dungong-unrelated", "classic:guan-yu",
            4, 0, 0, 0, 0, [], [], [], [new("standard:cao-cao")], [], new string('b', 64)));
        Need(!ordinary.Generals.ContainsKey(Boss) && !ordinary.Skills.ContainsKey(Cap), "An unrelated recipe does not register the dedicated current boss package.");
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "The actual fixed-cap command was rejected."); }
        public void Begin(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Use(string id, int[]? targets = null) { Begin(id, targets); Play(); }
        public void EquipSilverLion(int target)
        {
            Accept(new UseProgramSkillCommand(0, Driver, "equip", [V().Hand.Single(c => c.Kind == CardKind.SilverLion).Id], [target], G.Revision, P!.PromptId)); Play();
        }
        public LegalAction CardAction(CardKind kind, int[]? targets = null) => G.GetHumanLegalActions().First(a => a.CardId is { } id &&
            V().Hand.Any(c => c.Id == id && c.Kind == kind) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast &&
            (targets is null || a.TargetSeats.SequenceEqual(targets)));
        public int BeginCard(LegalAction action)
        {
            var card = action.CardId!.Value; Accept(new PlayCardCommand(0, card, action.TargetSeats, G.Revision, P!.PromptId,
                action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource }); return card;
        }
        public int PlayCard(CardKind kind, int[]? targets = null) { var card = BeginCard(CardAction(kind, targets)); Play(); return card; }
        public void Continue() => Accept(new AnswerPromptCommand(0, P!.PromptId,
            P.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue").Id, G.Revision));
        public void Answer(Func<PromptChoice, bool> predicate) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(predicate).Id, G.Revision));
        public void AtSkill(string id) => Reach(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == id &&
            P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        public void AtDamageChild() => Reach(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Watch);
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                Need(G.State.Winner == Winner.None && P?.PlayerSeat != 0, "Unexpected fixed-cap continuation: " + Diagnostic());
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded fixed-cap continuation: " + Diagnostic());
        }
        public string Diagnostic() => JsonSerializer.Serialize(new { Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId, P.Prompt },
            Damage = Events<DamageAppliedEvent>().Select(e => new { e.SourceSeat, e.TargetSeat, e.Amount, e.ArmorPayment }),
            Caps = Events<ProgramDamageCappedEvent>(), Chains = Events<ChainedDamagePropagatedEvent>(),
            Seats = Enumerable.Range(0, 4).Select(seat => new { Seat = seat, V(seat).Hp, V(seat).Armor, V(seat).IsAlive }),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind }) });
        public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint()) });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves the original capped attempt, exact Armor payment, propagation base, active sources and physical entities without replaying a completed cost.");
            var zones = G.CreateCardZoneDiagnostics(); Need(zones.Select(c => c.CardId).Distinct().Count() == zones.Count, "Every real entity still owns exactly one card zone.");
        }
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            if (scene == "hidden")
            {
                var concealed = GameEngine.CreateStandard(new() { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                    ModeId = HiddenMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
                    MaxTurns = 3, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
                var hidden = new Scene(concealed, registry); hidden.Accept(new StartGameCommand());
                hidden.Reach(() => hidden.P is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
                hidden.Accept(new SelectGeneralCommand(0, Owner, hidden.G.Revision, hidden.P!.PromptId)); hidden.Play(); return hidden;
            }
            var chain = scene.StartsWith("chain-", StringComparison.Ordinal); var high = scene is "native" or "lion" or "jueqing" or "maximum" || chain;
            var own = new List<string> { Watch }; if (scene is "source-less" or "loss") own.Add(Cap);
            if (scene is "jueqing" or "source-less") own.Add(Jueqing); if (scene == "chain-late") own.Add(Late);
            ShanheStartingCard[] cards = chain ? [new(CardKind.IronChain, Suit.Club, 6), new(CardKind.IronChain, Suit.Spade, 8),
                new(CardKind.Alcohol, Suit.Spade, 9), new(CardKind.ThunderSlash, Suit.Spade, 7)] : high ?
                scene == "lion" ? [new(CardKind.SilverLion, Suit.Club, 1), new(CardKind.Alcohol, Suit.Spade, 9), new(CardKind.Slash, Suit.Spade, 7)] :
                    [new(CardKind.Alcohol, Suit.Spade, 9), new(CardKind.Slash, Suit.Spade, 7)] : [];
            var tactics = high ? new List<string> { Tactic, Tactic, Tactic } : []; if (scene == "maximum") tactics.Add(Xiaoming);
            var setup = new ShanheBattleSetup(83, 1, "fixture-dungong-" + scene, Owner, 12, 0, 0, 0, 0, own, cards, [],
                [new(scene == "chain-middle" ? Boss : Peer, Hp: scene == "ai" ? 4 : null) { StartingHand = [] },
                 new(chain ? Peer : Boss, ExtraSkillIds: scene == "live" ? [Suppress] : []) { StartingHand = [] }],
                [new(scene == "chain-origin" ? Boss : Peer) { StartingHand = [] }], new string('d', 64)) { TacticSkillIds = tactics };
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var result = new Scene(game, registry); result.Accept(new StartGameCommand()); result.Play(); return result;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-dungong", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in new[] { Cap, Tactic, Jueqing, Xiaoming, Gaoling }) b.AddSkill(Production.Value.GetSkill(id));
            b.AddGeneral(Production.Value.Generals[Boss]);
            object Activation(string id, int targets, params object[] effects) => new { id, minCards = 0, maxCards = 0,
                minTargets = targets, maxTargets = targets, targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            var definitions = new object[] {
                new { id = Driver, revision = 1, activations = new[] {
                    Activation("armor-2", 1, new { op = "grantArmor", target = "selectedTarget", amount = 2 }),
                    Activation("armor-4", 1, new { op = "grantArmor", target = "selectedTarget", amount = 4 }),
                    Activation("armor-5", 1, new { op = "grantArmor", target = "selectedTarget", amount = 5 }),
                    Activation("hit-two", 1, new { op = "damage", target = "selectedTarget", amount = 2 }),
                    Activation("hit-five", 1, new { op = "damage", target = "selectedTarget", amount = 5 }),
                    Activation("self-five", 0, new { op = "damage", target = "owner", amount = 5 }),
                    Activation("source-less", 0, new { op = "receiveOwnerDamage", target = "owner", amount = 5 }),
                    Activation("hurt-other", 1, new { op = "loseHp", target = "selectedTarget", amount = 1 }),
                    Activation("recover-one", 1, new { op = "recover", target = "selectedTarget", amount = 1 }),
                    Activation("lose-cap", 0, new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { Cap }, sourceBind = "standard:none" }) } },
                new { id = Watch, revision = 1, triggers = new[] { new { id = "actual-paid-damage", window = "afterDamageApplied", subject = "any",
                    damageOccurrence = "perDamage", optional = false, effects = new[] { new { op = "chooseOption", target = "owner", resultBind = "paid-child",
                        options = new[] { new { id = "continue" } } } } } } },
                new { id = Late, revision = 1, triggers = new[] { new { id = "real-post-origin-acquisition", window = "afterDamageApplied", subject = "any",
                    damageOccurrence = "perDamage", optional = false, priority = 200, usageScope = "game", usageLimit = 1,
                    effects = new object[] { new { op = "chooseOption", target = "owner", resultBind = "before-grant", options = new[] { new { id = "continue" } } },
                        new { op = "grantSkills", target = "owner", skillIds = new[] { Cap } },
                        new { op = "chooseOption", target = "owner", resultBind = "after-grant", options = new[] { new { id = "continue" } } } } } } }
            };
            // The placement action owns and pays a real selected equipment card.
            var root = JsonSerializer.SerializeToNode(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions })!;
            root["skills"]![0]!["activations"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { id = "equip", minCards = 1, maxCards = 1,
                sourceZones = new[] { "hand" }, cardCategories = new[] { "equipment" }, minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null,
                effects = new object[] { new { op = "captureSelectedCards", target = "owner", resultBind = "gear" },
                    new { op = "placeSelectedEquipment", target = "selectedTarget", sourceBind = "gear" } } }));
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实盾弓测试操作", description = "仅调用现有实体牌、伤害、护甲及失血回复指令" },
                [Watch] = new { name = "真实伤害付款子窗口", description = "在实际付款后暂停，不构造伤害事实",
                    optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Late] = new { name = "原段付款后真实获得", description = "通过原生Owner授予，使中段受害者自身获得盾弓",
                    optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var catalog = SkillProgramCatalog.Load(root.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Suppress, "真实HP4失效", "使用既有等于指定体力时的共享技能抑制规则") { SuppressionRule = new(4) });
            b.AddGeneral(new(Owner, "盾弓真实测试使用者", "", Driver, "shu", 12)); b.AddGeneral(new(Peer, "没有盾弓的实际同行", "", "standard:none", "qun", 12));
            var hiddenPeers = Enumerable.Range(1, 3).Select(seat => "fixture:dungong-concealed-" + seat).ToArray();
            foreach (var id in hiddenPeers) b.AddGeneral(new(id, "真正隐匿的原盾弓", "", Cap, "jin", 5, [Gaoling]));
            var lion = CardCatalog.Get(CardKind.SilverLion); b.AddCard(new("fixture:dungong-silver-lion", lion.DisplayName, lion.CategoryName, lion.Description, CardKind.SilverLion));
            b.AddDeck(new(Deck, "不自然摸牌的真实牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "盾弓实际付款与传导", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Peer, Boss], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
            b.AddMode(new(HiddenMode, "真实隐匿派生上限", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, Deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. hiddenPeers]));
        }
    }
}
