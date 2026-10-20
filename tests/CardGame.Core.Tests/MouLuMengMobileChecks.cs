using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouLuMengMobileChecks
{
    private const string Keji = "mou-mobile:keji", Dujiang = "mou-mobile:dujiang", Duojing = "mou-mobile:duojing",
        Driver = "fixture:mlmm-driver", Watch = "fixture:mlmm-watch", Conditional = "fixture:mlmm-conditional", Quiet = "fixture:mlmm-quiet",
        Conversion = "fixture:mlmm-conversion", Owner = "fixture:mlmm-owner", Ally = "fixture:mlmm-ally", EnemyA = "fixture:mlmm-enemy-a",
        EnemyB = "fixture:mlmm-enemy-b", Deck = "fixture:mlmm-deck";
    private static ShanheStartingCard C(CardKind kind, Suit suit = Suit.Spade, int rank = 7) => new(kind, suit, rank);

    public static void KejiRealCostsIndependentBranchesArmorHandLimitAndRescueCold()
    {
        var s = Scene.Start(new("keji-costs") { Equipment = [C(CardKind.StoneAxe)], Hand = [C(CardKind.Peach), C(CardKind.Slash)] });
        s.Need(s.V() is { Hp: 4, Armor: 0, HandLimit: 4 } && s.CanKeji("discard-hand") && s.CanKeji("lose-hp"),
            "Unawakened Keji begins with two independent real branch allowances and adds the current Armor to HP-based Hand limit.");
        var discarded = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id; var equipped = s.V().Equipment.Single().Id;
        s.Reject(new UseProgramSkillCommand(0, Keji, "discard-hand", [equipped], [], s.G.Revision, s.P!.PromptId));
        s.Reject(new UseProgramSkillCommand(0, Keji, "discard-hand", [discarded], [1], s.G.Revision, s.P!.PromptId));
        s.Cold(); s.UseKeji("discard-hand", discarded); s.Play();
        s.Need(s.DiscardedOnce(discarded) && s.V() is { Hp: 4, Armor: 1, HandLimit: 5 } && !s.CanKeji("discard-hand") && s.CanKeji("lose-hp") &&
            s.E<MouLuMengKejiCostReturnedEvent>() is [{ Branch: "discard-hand", Paid: true }],
            "Discarding an actual printed Peach is a Hand-card cost rather than a prohibited Peach Use; exactly one real payment grants Armor one.");
        s.UseKeji("lose-hp"); s.Play();
        s.Need(s.V() is { Hp: 3, Armor: 3, HandLimit: 6 } && !s.CanKeji("discard-hand") && !s.CanKeji("lose-hp") &&
            s.E<DamageAppliedEvent>().Length == 0 && s.E<MouLuMengKejiCostReturnedEvent>().Count(e => e.Paid) == 2 &&
            s.E<ArmorGrantedEvent>().Where(e => e.Source.SkillId == Keji).Select(e => e.Amount).SequenceEqual([1, 2]),
            "The second independent branch really loses HP before granting Armor two, without converting HP Loss into damage or paying either branch twice.");
        s.Use("duel", [1]); s.AnswerDuelDecline(); s.Play();
        s.Need(s.V() is { Hp: 3, Armor: 2, HandLimit: 5 } && s.E<DamageAppliedEvent>().Single().ArmorPayment is { ArmorAbsorbed: 1, HpLossAmount: 0 },
            "A native Duel consumes Armor independently of HP and immediately reduces Keji's live Hand-limit bonus."); s.Cold();

        var supplied = Scene.Start(new("duojing-is-not-awakening") { DirectDuojing = true });
        supplied.UseKeji("discard-hand", supplied.V().Hand[0].Id); supplied.Play(); supplied.UseKeji("lose-hp"); supplied.Play();
        supplied.Need(supplied.Has(Duojing) && supplied.E<MouLuMengDujiangAwakenedEvent>().Length == 0 && supplied.E<MouLuMengKejiCostReturnedEvent>().Count(e => e.Paid) == 2,
            "Merely receiving Duojing does not fabricate Dujiang awakening or merge the two unawakened Keji allowances."); supplied.Cold();

        var child = Scene.Start(new("paid-discard-source-loss") { ConditionalKeji = true, DiscardChild = true });
        var material = child.V().Hand[0].Id; child.UseKeji("discard-hand", material); child.Reach(() => child.Hold("discard-child"));
        child.Need(child.V().Armor == 0 && child.DiscardedOnce(material) && child.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MouLuMengKeji is not null),
            "The true discarded material and owning Keji receipt survive its actual movement child before Armor is granted.");
        child.Private(); child.RejectPrompt(); child.Cold(); child.Continue(); child.Play();
        child.Need(child.V() is { Hp: 3, Armor: 1, HandLimit: 3 } && !child.Has(Keji) && child.DiscardedOnce(material) &&
            child.E<MouLuMengKejiCostReturnedEvent>().Single().Paid && child.CompletedActivation(Keji, "discard-hand") == 1,
            "Losing the original Keji source in the already paid discard child removes its live Hand-limit bonus but cannot repay or erase the accepted Armor grant."); child.Cold();

        var rescue = Scene.Start(new("keji-paid-self-rescue") { Hand = [C(CardKind.Peach)] }); rescue.Use("loss-three"); rescue.Play();
        var peach = rescue.V().Hand.Single(c => c.Kind == CardKind.Peach).Id; rescue.UseKeji("lose-hp");
        rescue.Reach(() => rescue.P is { Kind: DecisionKind.RescueDying });
        rescue.Need(rescue.V() is { Hp: 0, Armor: 0 } && rescue.E<MouLuMengKejiCostReturnedEvent>().Length == 0,
            "A lethal real Keji HP cost enters native Dying and cannot grant its Armor before the rescue children return.");
        rescue.Private(); rescue.RejectPrompt(); rescue.Cold(); rescue.Answer(c => c.Cards.SequenceEqual([peach]) && c.Parameters.GetValueOrDefault("response") == "peach"); rescue.Play();
        rescue.Need(rescue.V() is { Hp: 1, Armor: 2 } && rescue.PaidOnce(peach) && rescue.E<DyingResolvedEvent>().Single() is { VictimSeat: 0, Survived: true } &&
            rescue.E<MouLuMengKejiCostReturnedEvent>().Single() is { Branch: "lose-hp", Paid: true } && rescue.CompletedActivation(Keji, "lose-hp") == 1,
            "Self-Dying permits the real Peach; the once-paid Keji loss resumes only after rescue and grants Armor two exactly once."); rescue.Cold();
    }

    public static void KejiEffectivePeachRestrictionsConversionAndNativeDyingCold()
    {
        var s = Scene.Start(new("effective-peach") { Conversions = true, Hand = [C(CardKind.Peach), C(CardKind.Dodge)] });
        s.Use("loss-one"); s.Play(); var peach = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id; var dodge = s.V().Hand.Single(c => c.Kind == CardKind.Dodge).Id;
        s.Need(!s.Actions().Any(a => a.PlayedCardKind == CardKind.Peach || a.Kind == LegalActionKind.Peach) &&
            s.Actions().Any(a => a.CardId == peach && a.ConversionSource?.SkillId == Conversion && a.PlayedCardKind == CardKind.Slash),
            "Live Keji prohibits effective Peach while merely wounded, including a real conversion; an actual printed Peach converted to Slash remains legal.");
        s.Native(CardKind.Slash, [1], peach, Conversion); s.ChooseAllDuojing(false); s.Play();
        s.Need(s.PaidOnce(peach) && s.E<CardUseDeclaredEvent>().Single(e => e.CardId == peach).CardKind == CardKind.Slash && s.V(1).Hp == 11,
            "The central rule uses the actual effective card kind rather than forbidding the printed Peach entity."); s.Cold();
        s.Use("loss-target-twelve", [1]); s.Play();
        var dying = s.E<PlayerDyingEvent>().Single(e => e.VictimSeat == 1);
        s.Need(s.V(1).Hp <= 0 && s.V().Hand.Any(c => c.Id == dodge) && !s.G.CardMovements.Any(m => m.CardId == dodge && m.From == CardLocation.Hand(0)) &&
            s.E<DyingResponseEvent>().Any(e => e.ResolutionId == dying.ResolutionId && e.ResponderSeat == 0 && !e.UsedPeach) &&
            s.E<DyingResolvedEvent>().Single(e => e.ResolutionId == dying.ResolutionId).Survived == false,
            "A native other-player rescue cannot use Keji owner's converted Peach while that actor has no own unresolved Dying; the real entity remains unspent."); s.Cold();

        var own = Scene.Start(new("converted-self-peach") { Conversions = true, Hand = [C(CardKind.Dodge)] }); own.Use("loss-three"); own.Play();
        var converted = own.V().Hand.Single(c => c.Kind == CardKind.Dodge).Id; own.UseKeji("lose-hp"); own.Reach(() => own.P is { Kind: DecisionKind.RescueDying });
        own.Need(own.P!.Choices.Any(c => c.Cards.SequenceEqual([converted]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == Conversion && c.Parameters.GetValueOrDefault("response") == "peach"),
            "The same effective-Peach conversion is actually published during the actor's exact own unresolved Dying."); own.Private(); own.Cold();
        own.Answer(c => c.Cards.SequenceEqual([converted]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == Conversion && c.Parameters.GetValueOrDefault("response") == "peach"); own.Play();
        own.Need(own.PaidOnce(converted) && own.V() is { Hp: 1, Armor: 2 } && own.E<DyingResponseEvent>().Single(e => e.UsedPeach).UsedPeachPhysicalCardKind == CardKind.Dodge &&
            own.E<DyingResolvedEvent>().Single().Survived && own.CompletedActivation(Keji, "lose-hp") == 1,
            "Native converted self-rescue pays its true Dodge exactly once and returns the original lethal Keji cost and Armor grant."); own.Cold();

        foreach (var sourceHasKeji in new[] { false, true })
        {
            var tunan = Scene.Start(new(sourceHasKeji ? "tunan-source-keji-user-ordinary" : "tunan-source-ordinary-user-keji")
                { Tunan = true, KejiOnPeerOnly = !sourceHasKeji, Hand = [C(CardKind.Dodge)] });
            tunan.Use("loss-target-one", [1]); tunan.Play(); tunan.Need(tunan.V(1).Hp == 11, "The real Tunan counterpart is wounded by a native HP-loss instruction before the next real Play-start offer.");
            tunan.End(); tunan.Reach(() => tunan.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "ol:tunan" && c.Parameters.GetValueOrDefault("program-action") == "activate") == true);
            tunan.Cold(); tunan.Answer(c => c.Parameters.GetValueOrDefault("skill-id") == "ol:tunan" && c.Parameters.GetValueOrDefault("program-action") == "activate");
            tunan.Reach(() => tunan.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
            tunan.Answer(c => c.Targets.SequenceEqual([1]));
            tunan.Reach(() => tunan.G.CreateSnapshot(1).PendingDecision is { PlayerSeat: 1 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "tunan-branch"));
            var counterpartPrompt = tunan.G.CreateSnapshot(1).PendingDecision!;
            var producer = tunan.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:tunan");
            var revealed = tunan.E<ProgramCardsRevealedEvent>().Single(e => e.FrameId == producer.Id).Cards.Single();
            tunan.Need(producer.OwnerSeat == 0 && producer.SelectedTargetSeats.SequenceEqual([1]) && revealed.Kind == CardKind.Peach &&
                tunan.Has(Keji) == sourceHasKeji && (tunan.V(1).Skills?.Any(skill => skill.ContentId == Keji) == true) == !sourceHasKeji &&
                counterpartPrompt.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "use") == sourceHasKeji && counterpartPrompt.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "slash") &&
                Enumerable.Range(0, 4).Where(viewer => viewer != 1).All(viewer => tunan.G.CreateSnapshot(viewer).PendingDecision is null) && tunan.E<RecoveryAppliedEvent>().Length == 0,
                "Tunan effective-Peach gate follows actual user seat one, not source seat zero: Keji user cannot choose printed Peach, while Keji source cannot forbid the other wounded user's Peach; the real Peach-as-Slash option survives.");
            if (!sourceHasKeji) tunan.Reject(new AnswerPromptCommand(0, counterpartPrompt.PromptId, new($"tunan-branch.frame-{producer.Id}.use"), tunan.G.Revision));
            tunan.Cold(); tunan.Reach(() => tunan.E<CardUseDeclaredEvent>().Any(e => e.CardId == revealed.Id));
            var used = tunan.E<CardUseDeclaredEvent>().Single(e => e.CardId == revealed.Id);
            tunan.Reach(() => tunan.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == used.ResolutionId) && !tunan.G.ResolutionStack.Any(f => f.Id == producer.Id)); tunan.Play();
            tunan.Need(used.SourceSeat == 1 && used.CardKind == (sourceHasKeji ? CardKind.Peach : CardKind.Slash) &&
                tunan.V(1).Hp == (sourceHasKeji ? 12 : 11) && tunan.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 1 && e.Amount == 1) == (sourceHasKeji ? 1 : 0) &&
                tunan.G.CardMovements.Count(m => m.CardId == revealed.Id && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing) == 1 &&
                tunan.G.CardMovements.Count(m => m.CardId == revealed.Id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
                tunan.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == used.ResolutionId && e.CardId == (sourceHasKeji ? revealed.Id : 0) && e.CardKind == used.CardKind) == 1 &&
                tunan.E<ProgramBindingResolvedEvent>().Count(e => e.FrameId == producer.Id && e.SkillId == "ol:tunan" && e.BindingId == "tunan-launch" && e.Activated && e.Completed) == 1,
                "The actual counterpart alone uses the one revealed real entity: permitted Peach recovers once, forbidden Peach instead becomes a true Slash without recovery, and the exact original Tunan returns once."); tunan.Cold();
        }
    }

    public static void DujiangRealPreparationOnceGrantSharedKejiQuotaAndRegrantCold()
    {
        RejectForeignReservedOperation();
        static void PublicBudget(Scene scene, int used)
        {
            var states = scene.G.CreateSnapshot(0).Players[0].SkillRuntimeStates?.Single(s => s.SkillId == Keji).PublicRuleStates ?? [];
            scene.Need(new[] { "discard-hand", "lose-hp" }.All(branch => states.Count(s => s.StateId == branch &&
                s.Kind == ProgramPublicRuleStateKind.ActivationLimit && s.Scope == SkillUsageScope.Phase && s.Value == 1 && s.Used == used) == 1),
                "Both public Keji branch limits reflect the actual awakened shared phase budget, with limit one and used " + used + ".");
        }
        var delayed = Scene.Start(new("actual-preparation-threshold") { InitialArmor = 2 });
        delayed.Need(!delayed.Has(Duojing) && delayed.E<MouLuMengDujiangAwakenedEvent>().Length == 0, "Armor below three cannot awaken in actual Preparation.");
        delayed.Use("armor-one", [0]); delayed.Play(); var issuingTurn = delayed.G.State.TurnNumber;
        delayed.Need(delayed.V().Armor == 3 && !delayed.Has(Duojing), "Gaining the threshold during Play does not invent a Preparation window.");
        delayed.End(); delayed.Play(); var awake = delayed.E<MouLuMengDujiangAwakenedEvent>().Single();
        delayed.Need(awake.OwnerSeat == 0 && awake.Armor == 3 && delayed.G.State.TurnNumber > issuingTurn && delayed.Has(Duojing) &&
            delayed.V() is { MaxHp: 4, Hp: 4, Armor: 3 } && delayed.E<RecoveryAppliedEvent>().Length == 0 &&
            delayed.E<SkillUsageConsumedEvent>().Count(e => e.SkillId == Dujiang && e.Scope == SkillUsageScope.Game) == 1 &&
            !delayed.G.CardMovements.Any(m => m.Reason.Value.StartsWith("skill-program." + Dujiang + ".", StringComparison.Ordinal)),
            "Only the real next own Preparation forces one permanent Duojing grant, with no MaxHP, HP, Armor or draw reward/cost."); delayed.Cold();
        delayed.UseKeji("discard-hand", delayed.V().Hand[0].Id); delayed.Play();
        delayed.Need(!delayed.CanKeji("lose-hp") && !delayed.CanKeji("discard-hand"), "After actual awakening, paying either Keji branch exhausts their shared one-use Play allowance.");
        PublicBudget(delayed, 1); delayed.Cold();
        delayed.End(); delayed.Play(); delayed.Need(delayed.E<MouLuMengDujiangAwakenedEvent>().Length == 1, "Later real Preparations cannot awaken or grant the skill twice.");
        PublicBudget(delayed, 0); delayed.Cold();

        var regrant = Scene.Start(new("awakened-shared-source-regrant") { InitialArmor = 3, ConditionalKeji = true });
        regrant.Need(regrant.Has(Duojing) && regrant.E<MouLuMengDujiangAwakenedEvent>().Length == 1 && regrant.CanKeji("lose-hp"), "The first actual Preparation can awaken at exactly three Armor.");
        regrant.UseKeji("lose-hp"); regrant.Play();
        regrant.Need(!regrant.Has(Keji) && regrant.V() is { Hp: 3, Armor: 5, HandLimit: 3 }, "A real paid HP branch can remove the granting Keji source and still complete its Armor payment.");
        regrant.Use("recover-one"); regrant.Play();
        regrant.Need(regrant.Has(Keji) && regrant.V() is { Hp: 4, Armor: 5, HandLimit: 9 } && !regrant.CanKeji("lose-hp") && !regrant.CanKeji("discard-hand") &&
            regrant.E<MouLuMengDujiangAwakenedEvent>().Length == 1 && regrant.E<MouLuMengKejiReservedEvent>().Length == 1,
            "Reacquiring a new Keji source cannot clear actual awakening or refresh the already paid shared phase budget."); regrant.Cold();
        regrant.End(); regrant.Play(); regrant.Need(regrant.CanKeji("discard-hand") && regrant.CanKeji("lose-hp") && regrant.E<MouLuMengDujiangAwakenedEvent>().Length == 1,
            "A fresh real Play restores one choice from the shared budget without repeating awakening."); regrant.Cold();
    }

    public static void DuojingFinalTargetsArmorOpaqueGainExactPhaseAndNativeChildrenCold()
    {
        var multi = Scene.Start(new("final-targets-whole-use") { InitialArmor = 4, Equipment = [C(CardKind.FangtianHalberd)], Hand = [C(CardKind.Slash), C(CardKind.Indulgence), C(CardKind.Dodge)] });
        foreach (var target in new[] { 1, 2, 3 }) { multi.Use("equip-peer", [target]); multi.Play(); }
        multi.Use("armor-one", [2]); multi.Play(); var slash = multi.V().Hand.First(c => c.Kind == CardKind.Slash).Id;
        multi.Use("discard-two", cards: multi.V().Hand.Where(c => c.Id != slash).Take(2).Select(c => c.Id).ToArray()); multi.Play();
        multi.Need(multi.V().HandCount == 1 && Enumerable.Range(1, 3).All(i => multi.V(i).Equipment.Any(c => c.Kind == CardKind.RenwangShield)),
            "A genuine last-Hand Slash and equipped Fangtian publish three actual final targets, each retaining a real Renwang Shield.");
        multi.Native(CardKind.Slash, [1, 2, 3], slash); multi.Reach(multi.DuojingChoice);
        var started = multi.E<MouLuMengDuojingStartedEvent>().Single();
        multi.Need(started.TargetCount == 3 && multi.E<CardUseFinishedEvent>().All(e => e.ResolutionId != started.CardUseId), "The exact native mother Slash remains suspended while final-target choices are paid.");
        multi.PayDuojing(true); multi.PayDuojing(true); multi.PayDuojing(false); multi.Play();
        var paid = multi.E<MouLuMengDuojingArmorPaidEvent>(); var taken = multi.E<MouLuMengDuojingTakenEvent>();
        multi.Need(paid.Select(e => e.TargetSeat).SequenceEqual([1, 2]) && paid.Select(e => (e.ArmorBefore, e.ArmorAfter)).SequenceEqual([(4, 3), (3, 2)]) &&
            taken.Length == 2 && taken.All(e => e.CardId is { } id && multi.ObtainedOnce(e.TargetSeat, id)) && multi.E<MouLuMengDuojingPhaseCreditEvent>().Length == 2 &&
            multi.E<MouLuMengDuojingSkippedEvent>().Single().TargetSeat == 3 && multi.V(1).Hp == 11 && multi.V(2) is { Hp: 12, Armor: 0 } && multi.V(3).Hp == 11 &&
            multi.E<DamageAppliedEvent>().Single(e => e.TargetSeat == 2).ArmorPayment is { ArmorAbsorbed: 1, HpLossAmount: 0 } &&
            multi.PaidOnce(slash) && multi.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == started.CardUseId) == 1 && multi.E<MouLuMengDuojingCompletedEvent>().Length == 1,
            "Two distinct actual Armor payments and hidden Hand gains grant two credits; one payment bypasses equipment Armor for the whole native Use, including a skipped target, while resource Armor still absorbs damage."); multi.Cold();
        multi.Use("draw-nine"); multi.Play();
        for (var i = 0; i < 2; i++) { multi.Native(CardKind.Slash, [1]); multi.ChooseAllDuojing(false); multi.Play(); }
        multi.Need(!multi.Actions().Any(a => a.Kind == LegalActionKind.Slash) && multi.V().Hand.Any(c => c.Kind == CardKind.Slash) && multi.E<MouLuMengDuojingArmorPaidEvent>().Length == 2,
            "The original phase gains exactly two extra ordinary Slash uses, without unlimited quota or refunds after skipped later offers."); multi.Cold();

        var empty = Scene.Start(new("empty-hand-distance-prohibition") { InitialArmor = 1, DirectDuojing = true, EmptyPeers = true, BlockEmptyTarget = true });
        empty.Need(empty.G.GetCombatDistance(0, 2) > empty.G.GetAttackRange(0) && !empty.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Contains(2)) &&
            !empty.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Contains(3)), "Duojing adds neither distance exemption nor a bypass for a real empty-Hand target prohibition.");
        empty.Native(CardKind.Slash, [1]); empty.Reach(empty.DuojingChoice); empty.PayDuojing(true); empty.Play();
        empty.Need(empty.V().Armor == 0 && empty.E<MouLuMengDuojingTakenEvent>().Single().CardId is null && empty.E<MouLuMengDuojingPhaseCreditEvent>().Length == 1 &&
            empty.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1])), "An empty target Hand still permits paying Armor one and gaining one real phase credit without fabricating an obtained entity."); empty.Cold();

        foreach (var lethal in new[] { false, true })
        {
            var child = Scene.Start(new(lethal ? "gain-child-rescue-source-loss" : "gain-child-source-loss") { InitialArmor = 4, ConditionalDuojing = true,
                GainChildLoss = lethal ? 4 : 1, PeerHand = [C(CardKind.Peach)] });
            var original = child.V().Hand.First(c => c.Kind == CardKind.Slash).Id; child.Native(CardKind.Slash, [1]); child.Reach(child.DuojingChoice); child.PayDuojing(true, stopAtGain: true);
            var native = child.E<MouLuMengDuojingStartedEvent>().Single(); var obtained = child.E<MouLuMengDuojingTakenEvent>().Single().CardId!.Value;
            child.Need(child.V().Armor == 3 && child.E<MouLuMengDuojingPhaseCreditEvent>().Length == 0 && child.ObtainedOnce(1, obtained) &&
                child.E<CardUseFinishedEvent>().All(e => e.ResolutionId != native.CardUseId), "The actual hidden-card gain child retains one paid Armor invoice and the exact original native Slash before phase credit returns.");
            child.Private(); child.RejectPrompt(); child.Cold(); child.Continue();
            if (lethal)
            {
                child.Reach(() => child.P is { Kind: DecisionKind.RescueDying }); child.Private(); child.Cold();
                child.Answer(c => c.Cards.SequenceEqual([obtained]) && c.Parameters.GetValueOrDefault("response") == "peach");
            }
            child.Play();
            child.Need(!child.Has(Duojing) && child.V().Hp == (lethal ? 1 : 3) && child.V().Armor == 3 && child.E<MouLuMengDuojingArmorPaidEvent>().Length == 1 &&
                child.E<MouLuMengDuojingPhaseCreditEvent>().Length == 1 && child.E<MouLuMengDuojingCompletedEvent>().Length == 1 && child.PaidOnce(original) &&
                child.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == native.CardUseId) == 1 && child.V(1).Hp == 11 &&
                (!lethal || child.PaidOnce(obtained) && child.E<DyingResolvedEvent>().Single().Survived),
                "Source loss or a real self-rescue in the obtained-card child returns the exact already paid mother Slash once, without repeating Armor or hidden obtain."); child.Cold();
        }

        var dead = Scene.Start(new("paid-gain-child-death") { InitialArmor = 4, ConditionalDuojing = true, GainChildLoss = 4, PeerHand = [C(CardKind.Dodge)] });
        var dyingSlash = dead.V().Hand.First(c => c.Kind == CardKind.Slash).Id; dead.Native(CardKind.Slash, [1]); dead.Reach(dead.DuojingChoice); dead.PayDuojing(true, stopAtGain: true);
        var deadUse = dead.E<MouLuMengDuojingStartedEvent>().Single(); var deadCard = dead.E<MouLuMengDuojingTakenEvent>().Single().CardId!.Value;
        dead.Cold(); dead.Continue(); dead.Reach(() => dead.E<PlayerDiedEvent>().Any(e => e.VictimSeat == 0) && dead.E<MouLuMengDuojingCompletedEvent>().Length == 1, allowWinner: true);
        dead.Need(!dead.V().IsAlive && dead.V().Armor == 3 && dead.ObtainedOnce(1, deadCard) && dead.E<MouLuMengDuojingArmorPaidEvent>().Length == 1 &&
            dead.E<MouLuMengDuojingTakenEvent>().Length == 1 && dead.E<MouLuMengDuojingPhaseCreditEvent>().Length == 1 &&
            !dead.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == deadUse.FrameId) &&
            dead.G.CardMovements.Count(m => m.CardId == dyingSlash && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            dead.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == deadUse.CardUseId) <= 1,
            "Real death inside the paid gain child drains and retires its owning receipt with exactly one Armor, gain and earned credit; native death/attack termination never pays the original Slash twice."); dead.Cold();

        var pure = Scene.Start(new("pure-played-excluded") { InitialArmor = 2, DirectDuojing = true }); pure.Use("duel", [1]); pure.Reach(() => pure.P is { Kind: DecisionKind.RespondSlash });
        var responseId = pure.V().Hand.First(c => c.Kind == CardKind.Slash).Id; pure.Private(); pure.Cold(); pure.Answer(c => c.Cards.SequenceEqual([responseId]) && c.Parameters.GetValueOrDefault("response") == "slash"); pure.Play();
        pure.Need(pure.PaidOnce(responseId) && pure.V().Armor == 2 && pure.E<MouLuMengDuojingStartedEvent>().Length == 0,
            "A real native Duel Slash is pure Played, so it cannot offer Duojing, spend Armor or grant Slash quota."); pure.Cold();

        var foreign = Scene.Start(new("foreign-true-use-credit") { InitialArmor = 2, DirectDuojing = true, IncomingBorrowed = true, Equipment = [C(CardKind.StoneAxe)] });
        var forcedId = foreign.V().Hand.First(c => c.Kind == CardKind.Slash).Id; foreign.End();
        foreign.Reach(() => foreign.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash") == true);
        var parent = foreign.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.BorrowedSword);
        foreign.Private(); foreign.Cold(); foreign.Answer(c => c.Cards.SequenceEqual([forcedId]) && c.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash");
        foreign.Reach(foreign.DuojingChoice); foreign.PayDuojing(true); foreign.Reach(() => foreign.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == parent.Id));
        var foreignUse = foreign.E<MouLuMengDuojingStartedEvent>().Single();
        foreign.Need(foreign.PaidOnce(forcedId) && foreign.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == foreignUse.CardUseId) == 1 &&
            foreign.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == parent.Id) == 1 && foreign.E<MouLuMengDuojingPhaseCreditEvent>().Length == 1,
            "The exact native Borrowed Sword forced true Slash Use is eligible outside its actor's Play and returns its original parent and actual material once."); foreign.Play();
        foreign.Use("draw-nine"); foreign.Play(); foreign.Native(CardKind.Slash, [1]); foreign.ChooseAllDuojing(false); foreign.Play();
        foreign.Need(!foreign.Actions().Any(a => a.Kind == LegalActionKind.Slash) && foreign.V().Hand.Any(c => c.Kind == CardKind.Slash),
            "A credit paid in another actor's real phase cannot carry into the next own Play; only that new phase's ordinary Slash remains."); foreign.Cold();
    }

    private static void RejectForeignReservedOperation()
    {
        string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.mou-lu-meng-mobile." + suffix + ".json") ?? throw new InvalidOperationException("Missing complete actual mobile Lu Meng bundle."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = JsonNode.Parse(Read("rules"))!; var presentation = JsonNode.Parse(Read("presentation"))!; const string foreign = "fixture:foreign-keji";
        rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Keji)!["id"] = foreign;
        var labels = presentation["skills"]!.AsObject(); var own = labels[Keji]!.DeepClone(); labels.Remove(Keji); labels[foreign] = own;
        var rejected = false; try { SkillProgramCatalog.Load(rules.ToJsonString(), presentation.ToJsonString()); } catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("The reserved Keji payment and shared-awakening operation must reject a foreign skill transplant.");
    }

    private sealed record Spec(string Name)
    {
        public int InitialArmor { get; init; }
        public ShanheStartingCard[] Hand { get; init; } = [C(CardKind.Slash), C(CardKind.Slash), C(CardKind.Dodge)];
        public ShanheStartingCard[] Equipment { get; init; } = [];
        public ShanheStartingCard[] PeerHand { get; init; } = [C(CardKind.Indulgence)];
        public bool DirectDuojing { get; init; }
        public bool ConditionalKeji { get; init; }
        public bool ConditionalDuojing { get; init; }
        public bool DiscardChild { get; init; }
        public int GainChildLoss { get; init; }
        public bool Conversions { get; init; }
        public bool EmptyPeers { get; init; }
        public bool BlockEmptyTarget { get; init; }
        public bool IncomingBorrowed { get; init; }
        public bool Tunan { get; init; }
        public bool KejiOnPeerOnly { get; init; }
    }

    private sealed class Fixture(Spec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-lu-meng-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            void Real(string bundle, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing complete real bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var production = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
                foreach (var id in ids) b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description) { Program = production.Programs[id], ProgramPresentation = production.Presentations[id] });
            }
            Real("mou-lu-meng-mobile", Keji, Dujiang, Duojing); if (spec.BlockEmptyTarget) Real("passive-card-rules", "classic:kongcheng");
            if (spec.Tunan) Real("ol-lv-kai", "ol:tunan");
            foreach (var kind in spec.Hand.Concat(spec.Equipment).Concat(spec.PeerHand).Select(c => c.Kind).Concat(new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.BorrowedSword, CardKind.RenwangShield }).Distinct())
            { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:mlmm-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int targets = 0, int cards = 0) => new() { ["id"] = id, ["minCards"] = cards, ["maxCards"] = cards, ["minTargets"] = targets, ["maxTargets"] = targets,
                ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            static string Pause(string option) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held\",\"options\":[{\"id\":\"" + option + "\"}]}";
            var acts = new JsonArray(A("loss-one", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("loss-three", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":3}"),
                A("recover-one", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"), A("loss-target-twelve", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":12}", 1),
                A("loss-target-one", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}", 1),
                A("armor-one", "{\"op\":\"grantArmor\",\"target\":\"selectedTarget\",\"amount\":1}", 1), A("draw-nine", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":9}"),
                A("equip-peer", "{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"real-shield\"}", 1),
                A("discard-two", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":2}", 0, 2));
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", 1); duel["usesPerPhase"] = 2; duel["targetKind"] = "otherLiving"; acts.Add(duel);
            var driver = new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts };
            if (spec.InitialArmor > 0) driver["triggers"] = N("[{\"id\":\"initial-real-armor\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"grantArmor\",\"target\":\"owner\",\"amount\":" + spec.InitialArmor + "}]}]");
            var defs = new JsonArray(driver, N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            if (spec.Conversions) defs.Add(N("{\"id\":\"" + Conversion + "\",\"revision\":1,\"viewAs\":[{\"id\":\"effective-peach\",\"inputKinds\":[\"dodge\"],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"peach\",\"forPlay\":true,\"forResponse\":true,\"extendedUse\":true},{\"id\":\"printed-peach-slash\",\"inputKinds\":[\"peach\"],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false}]}"));
            var grants = new JsonArray(); if (spec.ConditionalKeji) grants.Add(N("{\"id\":\"healthy-keji\",\"skillId\":\"" + Keji + "\",\"minimumHp\":4}"));
            if (spec.ConditionalDuojing) grants.Add(N("{\"id\":\"healthy-duojing\",\"skillId\":\"" + Duojing + "\",\"minimumHp\":4}"));
            if (grants.Count > 0) defs.Add(new JsonObject { ["id"] = Conditional, ["revision"] = 1, ["hpSkillGrants"] = grants });
            var watches = new JsonArray();
            if (spec.DiscardChild) watches.Add(N("{\"id\":\"real-keji-discard-child\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementReasons\":[\"skill-program." + Keji + ".DiscardSelected\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("discard-child") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}"));
            if (spec.GainChildLoss > 0) watches.Add(N("{\"id\":\"real-duojing-gain-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"skill-program.mou-mobile:duojing.obtain\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("gain-child") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + spec.GainChildLoss + "}]}"));
            if (watches.Count > 0) defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "移动谋吕蒙真实小局夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(binding => binding!["effects"]!.AsArray())
                    .SelectMany(effect => effect!["options"]?.AsArray() ?? new JsonArray()).Select(option => option!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string> { Driver }; if (!spec.ConditionalKeji && !spec.KejiOnPeerOnly) own.Add(Keji);
            if (!spec.DirectDuojing && !spec.ConditionalDuojing) own.Add(Dujiang); if (spec.DirectDuojing) own.Add(Duojing);
            if (grants.Count > 0) own.Add(Conditional); if (spec.Conversions) own.Add(Conversion); if (watches.Count > 0) own.Add(Watch);
            if (spec.Tunan) own.Add("ol:tunan");
            b.AddGeneral(new(Owner, "移动谋吕蒙本人", "", "standard:none", "wu", 4, own));
            b.AddGeneral(new(Ally, "真实盟友目标", "", "standard:none", "qun", 12, spec.KejiOnPeerOnly ? [Quiet, Keji] : [Quiet]));
            b.AddGeneral(new(EnemyA, "真实远距目标", "", "standard:none", "qun", 12, spec.IncomingBorrowed ? Array.Empty<string>() : [Quiet]));
            b.AddGeneral(new(EnemyB, "真实禁止目标", "", "standard:none", "qun", 12, spec.BlockEmptyTarget ? [Quiet, "classic:kongcheng"] : [Quiet]));
            b.AddDeck(new(Deck, "固定四十八张实体", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("fixture:mlmm-card-" + (spec.Tunan ? CardKind.Peach : i < 6 ? CardKind.RenwangShield : CardKind.Slash), Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动谋吕蒙原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(Spec spec, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public bool Has(string skill) => V().Skills?.Any(s => s.ContentId == skill) == true;
        public bool CanKeji(string branch) => Actions().Any(a => a.ProgramSkillId == Keji && a.ProgramActivationId == branch);
        public int CompletedActivation(string skill, string branch) => E<ProgramSkillResolvedEvent>().Count(e => e.SkillId == skill && e.ActivationId == branch && e.Completed);
        public bool Hold(string name) => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == name);
        public bool DuojingChoice() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lu-meng-duojing") == true;
        public bool Taking() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lu-meng-duojing-take") == true;
        public bool PaidOnce(int id) => G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1;
        public bool DiscardedOnce(int id)
        {
            var move = G.CardMovements.Where(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program." + Keji + ".DiscardSelected").ToArray();
            return move is [var paid] && E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.InitiatorSeat == 0 && e.ExecutorSeat == 0 && e.SubjectSeat == 0 &&
                e.ActualCount == 1 && e.HandCount == 1 && e.Reason == paid.Reason && e.SequenceBefore < paid.Sequence && e.SequenceAfter >= paid.Sequence) == 1;
        }
        public bool ObtainedOnce(int victim, int id) => G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(victim)) == 1 &&
            G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Hand(0)) == 1;
        public void UseKeji(string branch, int? card = null) { Need(CanKeji(branch), "A genuine published Keji branch must exist before payment."); Accept(new UseProgramSkillCommand(0, Keji, branch, card is { } id ? [id] : [], [], G.Revision, P!.PromptId)); }
        public void Use(string id, int[]? targets = null, int[]? cards = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(CardKind kind, int[] targets, int? id = null, string? conversion = null)
        {
            var action = Actions().FirstOrDefault(a => a.CardId is { } cid && (id is null || id == cid) && a.Kind != LegalActionKind.Recast && a.TargetSeats.SequenceEqual(targets) &&
                (conversion is null ? a.ConversionSource is null && V().Hand.Any(c => c.Id == cid && c.Kind == kind) : a.ConversionSource?.SkillId == conversion && a.PlayedCardKind == kind));
            Need(action is not null, "The native effective card and exact target plan must be genuinely published: " + kind);
            Accept(new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        }
        public void PayDuojing(bool pay, bool stopAtGain = false)
        {
            Reach(DuojingChoice); Private(); RejectPrompt(); Cold(); var before = E<MouLuMengDuojingArmorPaidEvent>().Length;
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lu-meng-duojing" && c.Parameters.GetValueOrDefault("option-id") == (pay ? "pay" : "skip"));
            if (!pay) return;
            Reach(() => Taking() || Hold("gain-child") || DuojingChoice() || P is { Kind: DecisionKind.PlayCard } || E<MouLuMengDuojingCompletedEvent>().Length > 0);
            if (Taking())
            {
                var p = P!; var victim = E<MouLuMengDuojingArmorPaidEvent>()[before].TargetSeat;
                Need(p.Choices.All(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("source-zone") == "Hand" && c.Parameters.GetValueOrDefault("card-owner-seat") == victim.ToString(System.Globalization.CultureInfo.InvariantCulture) && c.Parameters.ContainsKey("slot-index")),
                    "Paid Duojing publishes only native foreign-Hand opaque slots, never another player's hidden physical card ID or Equipment/Judgment.");
                Private(); RejectPrompt(); Cold(); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lu-meng-duojing-take" && c.Parameters.GetValueOrDefault("slot-index") == "0");
            }
            if (stopAtGain) Reach(() => Hold("gain-child"));
        }
        public void ChooseAllDuojing(bool pay) { Reach(() => DuojingChoice() || P is { Kind: DecisionKind.PlayCard }); while (DuojingChoice()) { PayDuojing(pay); Reach(() => DuojingChoice() || P is { Kind: DecisionKind.PlayCard }); } }
        public void AnswerDuelDecline() { Reach(() => P is { Kind: DecisionKind.RespondSlash }); Answer(c => c.Parameters.GetValueOrDefault("response") == "take-damage"); }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") is "discard-child" or "gain-child");
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && choice is not null, "Choose a genuinely published owner choice."); Accept(new AnswerPromptCommand(0, p.PromptId, choice!.Id, G.Revision)); }
        public void Reach(Func<bool> done, bool allowWinner = false, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return; Need(allowWinner || G.State.Winner == Winner.None, "The small fixture ended before boundary " + line);
                var p = P; if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (DuojingChoice()) Answer(c => c.Parameters.GetValueOrDefault("option-id") == "skip");
                else if (p.SkillPrompt?.SkillId == Watch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "ol:tunan" && c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected real boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded real boundary " + line + " " + Diagnostic());
        }
        public void Private()
        {
            Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(viewer => G.CreateSnapshot(viewer).PendingDecision is null) &&
                Enumerable.Range(0, 4).All(viewer => G.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0)), "Only the real chooser sees its private choice; all other Hands remain opaque.");
            Immutable(P!.Choices); foreach (var choice in P.Choices) { Immutable(choice.Cards); Immutable(choice.Targets); }
        }
        private void Immutable<T>(IReadOnlyList<T> list) { if (list is IList mutable && mutable.Count > 0) { var before = JsonSerializer.Serialize(list); var rejected = false; try { mutable[0] = mutable[0]; } catch (NotSupportedException) { rejected = true; } Need(rejected && before == JsonSerializer.Serialize(list), "Committed nested lists reject observer mutation."); } }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(viewer => SnapshotJson.Serialize(G.CreateSnapshot(viewer))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves all private views, native parent cursors, costs, awakening history, original phase credits and command invoices."); }
        public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && before == State(), "Stale, foreign and forged commands cannot change real payment, HP, Armor, phase usage or native owning frames."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-mobile-lu-meng"), G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision - 1)); }
        public void Accept(GameCommand command) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).MaxHp, V(i).Armor, V(i).HandLimit, V(i).HandCount, V(i).Skills }),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Movements = G.CardMovements.TakeLast(10),
            Rules = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouLuMeng", StringComparison.Ordinal)).TakeLast(12).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            Damage = E<DamageAppliedEvent>().TakeLast(4), Dying = E<DyingResolvedEvent>().TakeLast(3), Finished = E<CardUseFinishedEvent>().TakeLast(4) });
        public static Scene Start(Spec spec)
        {
            ContentRegistry registry; try { registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec)); } catch (Exception e) { throw new InvalidOperationException(spec.Name + " fixture: " + e.Message, e); }
            var setup = new ShanheBattleSetup(83, 1, "mlmm-" + spec.Name, Owner, 4, 0, 0, 0, 0, [], spec.Hand, spec.Equipment,
                [new(EnemyA) { StartingHand = spec.IncomingBorrowed ? [C(CardKind.BorrowedSword)] : spec.EmptyPeers ? [] : spec.PeerHand }, new(EnemyB) { StartingHand = spec.EmptyPeers ? [] : spec.PeerHand }],
                [new(Ally) { StartingHand = spec.EmptyPeers ? [] : spec.PeerHand }], new string('b', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var scene = new Scene(spec, game, registry); scene.Accept(new StartGameCommand()); scene.Play(); return scene;
        }
    }
}
