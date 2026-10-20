using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouXuHuangShipoChecks
{
    private static ShanheStartingCard C(CardKind kind) => MouXuHuangCheckScene.C(kind);
    private static MouXuHuangCheckSpec All(string name) => new(name)
    {
        Shipo = true, SetupRange = true, TargetHp = 1, PeerHp = [1, 1, 1],
        Hand = [C(CardKind.SupplyShortage), C(CardKind.SupplyShortage), C(CardKind.Dodge)],
        Peers = [[C(CardKind.Dodge)], [C(CardKind.Dodge)], [C(CardKind.Dodge)]]
    };
    private static void PrepareAll(MouXuHuangCheckScene s)
    {
        s.Native(CardKind.SupplyShortage, [1]); s.Play(); s.Native(CardKind.SupplyShortage, [2]); s.Play();
        s.BeginMouyi(3); var id = s.DuanFrame().Id; s.Strategy("siege"); s.FinishMouyi(id);
        s.Need(Enumerable.Range(1, 3).All(i => s.V(i).Judgment.Any(c => c.Kind == CardKind.SupplyShortage)) &&
            s.V(3).Judgment.Any(c => s.G.CreateCardZoneDiagnostics().Single(d => d.CardId == c.Id).CardKind == CardKind.Slash),
            "All three actual targets contain native SupplyShortage; the last one is a genuinely converted printed Slash, not a manually relabeled Judgment fixture.");
    }
    private static long AcceptAll(MouXuHuangCheckScene s)
    {
        s.Need(s.E<MouXuHuangShipoStartedEvent>().Length == 0, "The complete roster cannot start in normal Play.");
        s.ActivateShipo(); var root = s.ShipoFrame(); var start = s.E<MouXuHuangShipoStartedEvent>().Single();
        s.Need(start.FrameId == root.Id && start.Phase.Kind == ActualDiscardRecoveryPhaseKind.Ending && start.Phase.ActorSeat == 0 && start.Source.OwnerSeat == 0 &&
            s.P!.Choices.Count(c => c.Parameters.GetValueOrDefault("branch") == "mode-all") == 1 &&
            s.P.Choices.Single(c => c.Parameters.GetValueOrDefault("branch") == "mode-all").Targets.SequenceEqual([1, 2, 3]),
            "An actual own Ending offers exactly the complete effective-SupplyShortage other roster in native action order; no arbitrary subset or self is published.");
        s.Private(); s.RejectPrompt(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "mode-all");
        var frozen = s.E<MouXuHuangShipoTargetFrozenEvent>().Where(e => e.FrameId == root.Id).OrderBy(e => e.Index).Select(e => e.TargetSeat).ToArray();
        s.Need(frozen.SequenceEqual([1, 2, 3]) && s.ShipoFrame().MouXuHuangShipo is { Accepted: true, Mode: "mode-all" } r && r.Targets.SequenceEqual(frozen),
            "Accepted mode and the complete immutable original roster belong to the one owning Ending frame.");
        s.Frozen(s.ShipoFrame().MouXuHuangShipo!.Targets); s.Cold(); return root.Id;
    }
    private static void Finish(MouXuHuangCheckScene s, long root) => s.Reach(() => s.E<MouXuHuangShipoCompletedEvent>().Any(e => e.FrameId == root));
    private static void CheckRecipients(MouXuHuangCheckScene s, long root, int[] seats)
    {
        var done = s.E<MouXuHuangShipoRecipientCompletedEvent>().Where(e => e.FrameId == root).ToArray();
        s.Need(done.Select(e => e.TargetSeat).SequenceEqual(seats) && done.Select(e => e.Cursor).Distinct().Count() == seats.Length &&
            s.E<MouXuHuangShipoCompletedEvent>().Count(e => e.FrameId == root) == 1 && !s.G.ResolutionStack.Any(f => f.Id == root),
            "Each original ordered recipient returns once and the accepted Ending parent completes once without reissuing earlier payment or damage.");
        foreach (var paid in s.E<MouXuHuangShipoGiftPaidEvent>().Where(e => e.FrameId == root))
            s.Need(s.G.CardMovements.Count(m => m.CardId == paid.CardId && m.From == CardLocation.Hand(paid.GiverSeat) && m.To == paid.To &&
                m.Sequence == paid.Sequence && m.Reason.Value == MouXuHuangCheckScene.GiftReason) == 1,
                "Every gift fact signs the actual original Hand entity, destination and single native movement rather than an inferred count.");
    }

    public static void ActualEndingFrozenCompleteRosterAndRecipientHandOrDamageCold()
    {
        foreach (var give in new[] { false, true })
        {
            var human = MouXuHuangCheckScene.Start(new("real-other-owner-human-payer-" + give)
            { Shipo = true, SourceSeat = 1, OwnerHp = 3, PeerHp = [12, 12, 12], Hand = [C(CardKind.Dodge)] });
            var card = human.V().Hand.Single().Id; human.End(); human.Reach(() => human.ShipoBranch("give") && human.P is { PlayerSeat: 0 });
            var start = human.E<MouXuHuangShipoStartedEvent>().Single();
            human.Need(start.Source.OwnerSeat == 1 && start.Phase.ActorSeat == 1 && start.Phase.Kind == ActualDiscardRecoveryPhaseKind.Ending && human.G.State.CurrentSeat == 1 &&
                human.ShipoFrame().MouXuHuangShipo!.Targets.SequenceEqual([0]),
                "A real AI source reaches its own Ending and independently chooses the only strictly lower-HP human; another player's earlier Ending could not trigger it.");
            human.Private(); human.RejectPrompt(); human.Cold();
            human.Need(human.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "give").All(c => c.Cards.SequenceEqual([card]) &&
                c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)), "Only an actual own Hand entity can pay the recipient's independent gift choice.");
            var hp = human.V().Hp;
            human.Answer(c => c.Parameters.GetValueOrDefault("branch") == (give ? "give" : "damage")); Finish(human, start.FrameId);
            human.Need(give ? human.E<MouXuHuangShipoGiftPaidEvent>().Count(e => e.FrameId == start.FrameId && e.GiverSeat == 0 && e.CardId == card) == 1 && human.V().Hp == hp :
                human.E<MouXuHuangShipoDamageIssuedEvent>().Count(e => e.FrameId == start.FrameId && e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
                human.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == 1 && human.V().Hp == hp - 1,
                "The actual human freely gives the real card or receives real sourced Damage1; neither branch is replaced with source draw or direct HP subtraction.");
            CheckRecipients(human, start.FrameId, [0]); human.Cold();
        }

        foreach (var prevented in new[] { false, true })
        {
            var one = MouXuHuangCheckScene.Start(new("strict-single-empty-hand-" + prevented)
            { Shipo = true, OwnerHp = 4, PeerHp = [4, 3, 6], Peers = [[], [], []], TargetArmor = prevented ? 0 : 1,
                TargetEquipment = CardKind.SilverLion, PreventTarget = prevented });
            one.ActivateShipo(); var root = one.ShipoFrame().Id;
            one.Need(one.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "mode-single").Select(c => c.Targets.Single()).SequenceEqual([2]) &&
                !one.ShipoBranch("mode-all"), "Strict lower actual HP excludes equal-HP ally, higher-HP enemy and self; the mode cannot silently expand beyond the one selected character.");
            one.Private(); one.Cold(); one.Answer(c => c.Parameters.GetValueOrDefault("branch") == "mode-single" && c.Targets.SequenceEqual([2]));
            one.Reach(() => one.ShipoBranch("damage")); one.Need(one.P is { PlayerSeat: 2 } && one.P.Choices.All(c => c.Parameters.GetValueOrDefault("branch") == "damage") &&
                one.V(2).HandCount == 0 && one.V(2).Equipment.Count == 1, "Equipment-only is genuinely unable to give a Hand card but must still face its real damage obligation.");
            one.Private(); one.RejectPrompt(); one.Cold(); Finish(one, root);
            one.Need(one.E<MouXuHuangShipoGiftPaidEvent>().Length == 0 && one.E<MouXuHuangShipoDamageIssuedEvent>() is [{ TargetSeat: 2, Amount: 1 }] && one.V(2).Hp == 3 &&
                (prevented ? one.E<DamageAppliedEvent>().Length == 0 : one.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 2 && e.ArmorPayment is { ArmorBefore: 1, ArmorAfter: 0, ArmorAbsorbed: 1, HpLossAmount: 0 })),
                "Real native prevention or complete Armor absorption retains HP, proving the issued Damage1 follows the native damage pipeline rather than LoseHp.");
            CheckRecipients(one, root, [2]); one.Cold();
        }

        var changing = MouXuHuangCheckScene.Start(All("frozen-roster-slot-and-hp-change") with { GiftChild = "remove-later" });
        PrepareAll(changing); var changingRoot = AcceptAll(changing); changing.Reach(() => changing.Hold("gift-child"));
        changing.Need(changing.E<MouXuHuangShipoGiftPaidEvent>() is [{ GiverSeat: 1 }] && changing.ShipoFrame().MouXuHuangShipo!.Targets.SequenceEqual([1, 2, 3]),
            "The first genuine Hand gift is already paid while the original full roster remains frozen on the source frame.");
        changing.Private(); changing.RejectPrompt(); changing.Cold(); changing.Continue();
        changing.Reach(() => changing.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
        changing.Answer(c => c.Targets.SequenceEqual([2])); changing.Reach(() => changing.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Judgment)) == true);
        changing.Private(); changing.Cold(); changing.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Judgment));
        changing.Reach(() => changing.ShipoBranch("select-card"));
        changing.Need(changing.V(2).Hp == 11 && changing.V(2).Hp > changing.V().Hp && changing.V(2).Judgment.Count == 0 &&
            changing.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 2 && e.Amount == 11 && e.RemainingHp == 12) == 1 &&
            changing.E<MouXuHuangShipoDamageIssuedEvent>().Count(e => e.FrameId == changingRoot && e.Cursor == 1 && e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) == 1 &&
            changing.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) == 1 &&
            !changing.E<MouXuHuangShipoGiftPaidEvent>().Any(e => e.FrameId == changingRoot && e.GiverSeat == 2) &&
            changing.E<MouXuHuangShipoTargetFrozenEvent>().Where(e => e.FrameId == changingRoot).Select(e => e.TargetSeat).SequenceEqual([1, 2, 3]),
            "A real child removes the later shortage and heals it to 12 above owner HP, then the same living original recipient independently chooses real Damage1 to 11; no current-eligibility re-filter replaces the accepted full roster.");
        changing.Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline-transfer"); Finish(changing, changingRoot); CheckRecipients(changing, changingRoot, [1, 2, 3]); changing.Cold();

        var dead = MouXuHuangCheckScene.Start(All("later-recipient-real-death") with { GiftChild = "kill-later" });
        PrepareAll(dead); var deadRoot = AcceptAll(dead); dead.Reach(() => dead.Hold("gift-child")); dead.Cold(); dead.Continue();
        dead.Reach(() => dead.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true); dead.Answer(c => c.Targets.SequenceEqual([2])); Finish(dead, deadRoot);
        dead.Need(!dead.V(2).IsAlive && dead.E<PlayerDiedEvent>().Any(e => e.VictimSeat == 2) &&
            dead.E<MouXuHuangShipoRecipientCompletedEvent>().Single(e => e.FrameId == deadRoot && e.TargetSeat == 2).SkippedDead &&
            !dead.E<MouXuHuangShipoGiftPaidEvent>().Any(e => e.FrameId == deadRoot && e.GiverSeat == 2) &&
            !dead.E<MouXuHuangShipoDamageIssuedEvent>().Any(e => e.FrameId == deadRoot && e.TargetSeat == 2),
            "Only actual later death skips an original frozen recipient; the paid earlier gift and still-living final obligation continue once."); CheckRecipients(dead, deadRoot, [1, 2, 3]); dead.Cold();
    }

    public static void ExactObtainedSubsetSingleRecipientAndAcceptedSourceLifetimeCold()
    {
        var subset = MouXuHuangCheckScene.Start(All("actual-three-gifts-two-subset")); var initial = subset.V().Hand.Single(c => c.Kind == CardKind.Dodge).Id;
        PrepareAll(subset); var root = AcceptAll(subset); subset.Reach(() => subset.ShipoBranch("select-card"));
        var gifts = subset.E<MouXuHuangShipoGiftPaidEvent>().Where(e => e.FrameId == root).OrderBy(e => e.Cursor).Select(e => e.CardId).ToArray();
        subset.Need(gifts.Length == 3 && gifts.Distinct().Count() == 3 && subset.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "select-card").Select(c => c.Cards.Single()).Order().SequenceEqual(gifts.Order()) &&
            !subset.P.ValidCardIds.Contains(initial), "Only the three actual received IDs are eligible; an indistinguishable original own Hand entity is excluded by provenance.");
        subset.Private(); subset.RejectPrompt(); subset.Cold(); var before = subset.G.CardMovements.Count;
        foreach (var id in gifts.Take(2)) { subset.Answer(c => c.Parameters.GetValueOrDefault("branch") == "select-card" && c.Cards.SequenceEqual([id])); subset.Private(); subset.RejectPrompt(); subset.Cold(); }
        subset.Need(subset.G.CardMovements.Count == before && subset.ShipoFrame().MouXuHuangShipo!.SelectedCardIds.SequenceEqual(gifts.Take(2)),
            "Incremental private subset choices persist on the original frame and do not transfer entities before final recipient commitment.");
        subset.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish-cards"); subset.Private(); subset.RejectPrompt(); subset.Cold();
        subset.Need(subset.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "recipient").All(c => c.Targets.Count == 1 && c.Targets[0] != 0),
            "The entire chosen subset has exactly one living other recipient, including an earlier original giver.");
        subset.Answer(c => c.Parameters.GetValueOrDefault("branch") == "recipient" && c.Targets.SequenceEqual([1])); Finish(subset, root);
        var transfer = subset.E<MouXuHuangShipoTransferPaidEvent>().Single(e => e.FrameId == root);
        var moved = subset.G.CardMovements.Where(m => m.Reason.Value == MouXuHuangCheckScene.TransferReason).ToArray();
        subset.Need(transfer is { RecipientSeat: 1, Count: 2 } && moved.Length == 2 && moved.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1) &&
            m.Sequence > transfer.Before && m.Sequence <= transfer.After) && moved.Select(m => m.CardId).Order().SequenceEqual(gifts.Take(2).Order()) &&
            subset.V().Hand.Any(c => c.Id == gifts[2]) && subset.V().Hand.Any(c => c.Id == initial) &&
            subset.E<MouXuHuangShipoCompletedEvent>().Single(e => e.FrameId == root) is { ActualObtainedCount: 3, TransferredCount: 2 },
            "One native atomic transfer gives exactly the selected two original received entities to one earlier giver, leaving both unselected received and initial cards in Hand.");
        CheckRecipients(subset, root, [1, 2, 3]); subset.Cold();

        var filtered = MouXuHuangCheckScene.Start(All("obtained-moved-away-unrelated-draw") with { GiftChild = "filter-obtained" });
        var original = filtered.V().Hand.Single(c => c.Kind == CardKind.Dodge).Id; PrepareAll(filtered); var filterRoot = AcceptAll(filtered);
        filtered.Reach(() => filtered.Hold("gift-child")); var first = filtered.E<MouXuHuangShipoGiftPaidEvent>().Single().CardId;
        filtered.Private(); filtered.RejectPrompt(); filtered.Cold(); filtered.Continue();
        filtered.Reach(() => filtered.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
        filtered.Answer(c => c.Targets.SequenceEqual([1])); filtered.Reach(() => filtered.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
        filtered.Private(); filtered.Cold(); filtered.Answer(c => c.Cards.SequenceEqual([first])); filtered.Reach(() => filtered.ShipoBranch("select-card"));
        var eligible = filtered.E<MouXuHuangShipoGiftPaidEvent>().Where(e => e.FrameId == filterRoot && filtered.V().Hand.Any(c => c.Id == e.CardId)).Select(e => e.CardId).Distinct().Order().ToArray();
        var unrelated = filtered.G.CardMovements.Where(m => m.Reason.Value == "skill-program." + MouXuHuangCheckScene.Watch + ".Draw" && m.To == CardLocation.Hand(0)).Select(m => m.CardId).ToArray();
        var offered = filtered.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "select-card").Select(c => c.Cards.Single()).Order().ToArray();
        filtered.Need(eligible.Length == 2 && offered.SequenceEqual(eligible) && !offered.Contains(first) && !offered.Contains(original) && unrelated.Length == 1 && !offered.Contains(unrelated[0]),
            "A true movement child removes the first received card and really draws an unrelated replacement; neither that lost provenance nor original/unrelated own cards enter the remaining exact obtained set.");
        filtered.Private(); filtered.RejectPrompt(); filtered.Cold(); filtered.Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline-transfer"); Finish(filtered, filterRoot);
        filtered.Need(filtered.E<MouXuHuangShipoTransferPaidEvent>().Length == 0, "Optional redistribution can decline without returning or moving all received cards."); CheckRecipients(filtered, filterRoot, [1, 2, 3]); filtered.Cold();

        foreach (var death in new[] { false, true })
        {
            var lifetime = MouXuHuangCheckScene.Start(All(death ? "accepted-owner-real-death" : "accepted-source-real-loss") with
            { ConditionalSources = !death, OwnerHp = death ? 1 : 4, GiftChild = death ? "source-death" : "source-loss" });
            PrepareAll(lifetime); var lifeRoot = AcceptAll(lifetime); lifetime.Reach(() => lifetime.Hold("gift-child")); var paid = lifetime.E<MouXuHuangShipoGiftPaidEvent>().Single();
            lifetime.Private(); lifetime.RejectPrompt(); lifetime.Cold(); lifetime.Continue();
            if (death)
            {
                lifetime.Reach(() => lifetime.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-targets") == true);
                lifetime.Private(); lifetime.Cold(); lifetime.Answer(c => c.Targets.SequenceEqual([2, 3]));
            }
            Finish(lifetime, lifeRoot);
            lifetime.Need(lifetime.E<MouXuHuangShipoGiftPaidEvent>().Count(e => e.CardId == paid.CardId && e.Sequence == paid.Sequence) == 1 &&
                (death ? !lifetime.V().IsAlive && lifetime.G.State.Winner == Winner.None && lifetime.E<MouXuHuangShipoDamageIssuedEvent>().Where(e => e.FrameId == lifeRoot).Select(e => e.TargetSeat).SequenceEqual([2, 3]) &&
                    lifetime.E<MouXuHuangShipoDamageIssuedEvent>().Where(e => e.FrameId == lifeRoot).All(e => e.SourceSeat == 0 && e.Amount == 1) && lifetime.E<MouXuHuangShipoTransferPaidEvent>().Length == 0 :
                    lifetime.V().Hp == 3 && !lifetime.Has(MouXuHuangCheckScene.Shipo) && lifetime.E<MouXuHuangShipoGiftPaidEvent>().Count(e => e.FrameId == lifeRoot) == 3),
                "An already accepted full Shipo survives actual source loss or nonterminal owner death: paid gift never repeats, later living obligations finish, and a dead source receives no gift or redistribution.");
            CheckRecipients(lifetime, lifeRoot, [1, 2, 3]); lifetime.Cold();
        }
    }
}
