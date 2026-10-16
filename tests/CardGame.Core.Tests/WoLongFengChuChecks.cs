using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WoLongFengChuChecks
{
    private const string Youlong = "ol:youlong", Luanfeng = "ol:luanfeng", Driver = "fixture:wlfc-driver";
    private const string Watch = "fixture:wlfc-watch", Gain = "fixture:wlfc-gain", Hp = "fixture:wlfc-hp", Conditional = "fixture:wlfc-conditional";
    private const string Quiet = "fixture:wlfc-quiet", Attack = "fixture:wlfc-attack", Owner = "fixture:wlfc-owner", Mode = "identity:classic-wolong-fixture";

    public static void YoulongPaidEquipmentMovementSurvivesColdSourceLossAndRoundFaces()
    {
        foreach (var loss in new[] { false, true })
        {
            var (g, registry) = Start(loss ? "loss" : "normal");
            var gear = EquipmentAction(g, CardKind.Crossbow);
            SubmitPlay(g, gear); Play(g); var hand = V(g, 0).HandCount;
            Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g)));
            Need(P(g) is { IsPrivate: true, PlayerSeat: 0 } && g.CreateSnapshot(1).PendingDecision is null && !E<EquipmentSlotNamedUsePaidEvent>(g).Any(),
                "The accepted actual zero use owns a private unpaid slot choice; no card name or cost is consumed before selection.");
            g = Cold(g, registry); Slot(g, EquipmentSlot.Weapon); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var payment = E<EquipmentSlotNamedUsePaidEvent>(g).Single().Payment;
            Need(payment is { Yin: false, EffectiveKind: CardKind.DrawTwo, Slot: EquipmentSlot.Weapon, PreviousCapacity: 1 } &&
                V(g, 0).Equipment.Count == 0 && !E<EquipmentSlotNamedUseIssuedEvent>(g).Any() &&
                g.CardMovements.Count(m => m.CardId == gear.CardId && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "equipment.slot-abolished") == 1 &&
                V(g, 0).Skills!.Any(s => s.Id == Youlong) != loss,
                "The actual equipment entity and Yang round allowance are paid once before their real child, including HP-driven source loss.");
            g = Cold(g, registry); AuditPaidDraft(g, registry); Continue(g); Play(g);
            Need(V(g, 0).HandCount == hand + 2 && E<EquipmentSlotNamedUsePaidEvent>(g).Count() == 1 &&
                E<EquipmentSlotNamedUseConsumedEvent>(g).Count() == 1 && E<EquipmentSlotNamedUseIssuedEvent>(g).Count() == 1 &&
                E<TieredRoundConversionUseIssuedEvent>(g).Any(e => e.Receipt is { Source.SkillId: Youlong, EffectiveKind: CardKind.DrawTwo, MaterialCount: 0 }) &&
                E<CardUseFinishedEvent>(g).Any(e => e is { CardId: 0, CardKind: CardKind.DrawTwo } && E<EquipmentSlotNamedUseIssuedEvent>(g).Any(i => i.NativeOwnerId == e.ResolutionId)),
                "Cold returning from the paid cost issues one real native DrawTwo, even after the source disappeared, without a second equipment or quota payment. " +
                JsonSerializer.Serialize(new { loss, hand, current = V(g, 0).HandCount, paid = E<EquipmentSlotNamedUsePaidEvent>(g).Count(), consumed = E<EquipmentSlotNamedUseConsumedEvent>(g).Count(), issued = E<EquipmentSlotNamedUseIssuedEvent>(g).Count(), actions = E<CardActionAcceptedEvent>(g).Select(e => new { e.Action.ActorSeat, e.Action.Type, e.Action.EffectiveKind, physical = e.Action.PhysicalCards.Count, conversions = e.Action.ConversionChain.Select(c => c.SkillId) }) }));
            _ = Cold(g, registry); if (loss) continue;
            Named(g, CardKind.Slash, 1); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor); Play(g);
            Need(E<EquipmentSlotNamedUsePaidEvent>(g).Select(e => e.Payment.Yin).SequenceEqual([false, true]) && !NamedActions(g).Any(),
                "Yang and Yin each pay their own one-use allowance during the actual round.");
            var round = E<RoundStartedEvent>(g).Count(); Use(g, "extra"); Play(g); End(g); Play(g);
            Need(E<RoundStartedEvent>(g).Count() == round && !NamedActions(g).Any(), "An additional actual turn does not refresh either round allowance.");
            NextRound(g, round + 1);
            Need(!NamedActions(g).Any(a => a.PlayedCardKind == CardKind.DrawTwo) && NamedActions(g).Any(a => a.PlayedCardKind == CardKind.Duel),
                "The next real round refreshes face quota while preserving each already used card name.");
            Named(g, CardKind.Duel, 1); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.DefensiveHorse); Play(g);
            Need(!NamedActions(g).Any(a => a.PlayedCardKind == CardKind.Slash) && NamedActions(g).Any(a => a.PlayedCardKind == CardKind.FireSlash) &&
                NamedActions(g).Any(a => a.PlayedCardKind == CardKind.ThunderSlash), "Ordinary, fire and thunder Slash retain distinct actually used kinds.");
            _ = Cold(g, registry);
        }
    }

    public static void NativeUseOnlyResponsesAndPostAbolitionSlashLegality()
    {
        var (g, registry) = Start("response"); Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor); Play(g);
        var hp = V(g, 0).Hp; Use(g, "duel", [1]); Play(g);
        Need(V(g, 0).Hp == hp - 1 && E<DuelResponseEvent>(g).Any(e => e.ResponderSeat == 0 && !e.UsedSlash) &&
            E<EquipmentSlotNamedUsePaidEvent>(g).Count() == 1 && !E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Response),
            "Youlong cannot become a plain Slash response in a real Duel; failing it does not consume Yin or pay a slot.");
        Use(g, "hurt", [1]); Reach(g, () => P(g) is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 } p && p.Choices.Any(c => Zero(c, CardKind.Dodge)));
        var hpAfterDuel = V(g, 0).Hp; g = Cold(g, registry); Answer(g, c => Zero(c, CardKind.Dodge)); Reach(g, () => Slots(P(g)));
        g = Cold(g, registry); Slot(g, EquipmentSlot.Treasure); Play(g);
        Need(V(g, 0).Hp == hpAfterDuel && E<TieredRoundConversionUseIssuedEvent>(g).Any(e => e.Receipt.Source.SkillId == Youlong && e.Receipt.EffectiveKind == CardKind.Dodge) &&
            E<EquipmentSlotNamedUsePaidEvent>(g).Last().Payment is { Yin: true, Intent: EquipmentSlotNamedUseIntent.Dodge },
            "The genuine incoming Slash permits a zero-entity Dodge use and returns to its original attack after one paid Yin slot.");
        _ = Cold(g, registry);

        (g, registry) = Start("normal"); Use(g, "duel", [1]);
        Reach(g, () => P(g) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 } p && p.Choices.Any(c => Zero(c, CardKind.Nullification)));
        g = Cold(g, registry); Answer(g, c => Zero(c, CardKind.Nullification)); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.DefensiveHorse); Play(g);
        Need(E<EquipmentSlotNamedUsePaidEvent>(g).Single().Payment.Intent == EquipmentSlotNamedUseIntent.Nullification &&
            E<NullificationResolvedEvent>(g).Single().ChainDepth == 1 && !E<DuelResponseEvent>(g).Any(),
            "Initial Yang can pay one real native Nullification use and cancel its original Duel without inventing a material entity.");
        _ = Cold(g, registry);

        foreach (var multi in new[] { false, true })
        {
            (g, registry) = Start("range");
            if (multi) { Use(g, "double-weapon"); Play(g); }
            for (var i = 0; i < (multi ? 2 : 1); i++)
            { SubmitPlay(g, EquipmentAction(g, CardKind.QinggangSword)); Play(g); }
            Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor); Play(g);
            Need(g.GetAttackRange(0) >= 2 && g.GetCombatDistance(0, 2) == 2 && V(g, 0).Equipment.Count == (multi ? 2 : 1),
                "Only actual installed native weapons make the opposite seat reachable, including expanded same-category capacity.");
            Named(g, CardKind.Slash, 2); Reach(g, () => Slots(P(g)));
            Need(P(g)!.Choices.All(c => c.Parameters.GetValueOrDefault("equipment-slot") != nameof(EquipmentSlot.Weapon)),
                "Payment excludes removing the whole weapon category, rather than pretending another soon-to-be-removed weapon remains usable.");
            g = Cold(g, registry); Slot(g, EquipmentSlot.Treasure); Play(g);
            Need(V(g, 0).Equipment.Count == (multi ? 2 : 1) && E<EquipmentSlotNamedUsePaidEvent>(g).Count() == 2,
                "A payable alternative preserves native range and issues the real Slash after cold restoration.");
            _ = Cold(g, registry);
        }
        (g, registry) = Start("quota"); Use(g, "double-weapon"); Play(g);
        for (var i = 0; i < 2; i++) { SubmitPlay(g, EquipmentAction(g, CardKind.Crossbow)); Play(g); }
        SubmitPlay(g, g.GetHumanLegalActions().First(a => a.CardId > 0 && a.ConversionSource?.SkillId == Driver && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]))); Play(g);
        Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor); Play(g);
        Named(g, CardKind.Slash, 1); Reach(g, () => Slots(P(g)));
        Need(P(g)!.Choices.All(c => c.Parameters.GetValueOrDefault("equipment-slot") != nameof(EquipmentSlot.Weapon)),
            "Removing both actual Crossbows cannot borrow either removed weapon's unlimited Slash allowance after a real earlier debit.");
        g = Cold(g, registry); Slot(g, EquipmentSlot.Treasure); Play(g); _ = Cold(g, registry);

        (g, registry) = Start("normal"); SubmitPlay(g, EquipmentAction(g, CardKind.Crossbow)); Play(g);
        Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor); Play(g); Use(g, "die-five");
        Reach(g, () => P(g) is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 } p && p.Choices.Any(c => Zero(c, CardKind.Peach)));
        var dying = g.ResolutionStack.OfType<DyingFrame>().Last(); g = Cold(g, registry); Answer(g, c => Zero(c, CardKind.Peach)); Reach(g, () => Slots(P(g)));
        g = Cold(g, registry); Slot(g, EquipmentSlot.Weapon); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        Need(V(g, 0).Hp == 0 && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.EquipmentSlotNamedUse?.Intent == EquipmentSlotNamedUseIntent.DyingPeach) &&
            E<EquipmentSlotNamedUsePaidEvent>(g).Last().Payment.Intent == EquipmentSlotNamedUseIntent.DyingPeach,
            "A real zero Peach rescue suspends the exact original Dying parent while its actual installed weapon is paid and its loss child remains open.");
        g = Cold(g, registry); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Hp);
        Need(g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.TieredRoundConversionUse?.DyingFrameId == dying.Id && u.DyingResponse is { UsedAlcohol: false }) && V(g, 0).Hp == 1,
            "After cold cost return the genuine native Peach owns its exact Dying response receipt and real HP recovery child.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(E<DyingResolvedEvent>(g).Single(e => e.ResolutionId == dying.Id).Survived && E<EquipmentSlotNamedUsePaidEvent>(g).Count() == 2 &&
            E<EquipmentSlotNamedUseConsumedEvent>(g).Count() == 2 && E<EquipmentSlotNamedUseIssuedEvent>(g).Count() == 2,
            "Cold recovery resolves the original Dying once and never repeats the already paid rescue slot.");
        _ = Cold(g, registry);

        (g, registry) = Start("qinglong"); Use(g, "equip-deck", [0]); Play(g);
        Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor); Play(g);
        Need(V(g, 1).Hand.Any(c => c.Kind == CardKind.Dodge), "The fixed actual target holds a native Dodge for Qinglong.");
        SubmitPlay(g, g.GetHumanLegalActions().First(a => a.CardId > 0 && a.ConversionSource?.SkillId == Driver && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1])));
        Reach(g, () => P(g) is { Kind: DecisionKind.QinglongCrescentBlade, PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "tiered-round-zero-forced-slash"));
        var first = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardAttack is not null);
        Need(first.CardAttack!.SuccessfulDodgeResponses == 1, "An actual first Slash debit and genuine native Dodge open the original Qinglong continuation.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("action") == "tiered-round-zero-forced-slash" && c.Parameters.GetValueOrDefault("output-kind") == nameof(CardKind.FireSlash));
        Reach(g, () => Slots(P(g))); Need(P(g)!.Choices.All(c => c.Parameters.GetValueOrDefault("equipment-slot") != nameof(EquipmentSlot.Weapon)), "A forced Qinglong continuation preserves its original native weapon owner.");
        g = Cold(g, registry); Slot(g, EquipmentSlot.Treasure); Play(g);
        Need(E<EquipmentSlotNamedUsePaidEvent>(g).Last().Payment.Intent == EquipmentSlotNamedUseIntent.Qinglong &&
            E<QinglongCrescentBladeResolvedEvent>(g).Any(e => e.ResolutionId == first.Id && e.Used && e.SlashCardIds.Count == 0 && e.EffectiveSlashKind == CardKind.FireSlash) &&
            E<EquipmentSlotNamedUseIssuedEvent>(g).Count() == 2 && V(g, 0).Equipment.Single().Kind == CardKind.QinglongCrescentBlade,
            "The paid zero FireSlash uses Qinglong's native unlimited follow-up after the first debit and both attacks return once with the real weapon intact.");
        _ = Cold(g, registry);

        (g, registry) = Start("qinglong"); Use(g, "equip-deck", [0]); Play(g);
        Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor); Play(g); Use(g, "only-weapon"); Play(g);
        SubmitPlay(g, g.GetHumanLegalActions().First(a => a.CardId > 0 && a.ConversionSource?.SkillId == Driver && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1])));
        Reach(g, () => P(g) is { Kind: DecisionKind.QinglongCrescentBlade, PlayerSeat: 0 });
        Need(P(g)!.Choices.All(c => c.Parameters.GetValueOrDefault("action") != "tiered-round-zero-forced-slash") && E<EquipmentSlotNamedUsePaidEvent>(g).Count() == 1,
            "When only the weapon category remains, a native Qinglong request never publishes an impossible Youlong cost choice.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("action") == "qinglong-skip"); Play(g); _ = Cold(g, registry);

        (g, registry) = Start("snatch"); SubmitPlay(g, EquipmentAction(g, CardKind.OffensiveHorse)); Play(g); Use(g, "only-offhorse"); Play(g);
        Need(g.GetCombatDistance(0, 2) == 1 && !NamedActions(g).Any(a => a.PlayedCardKind == CardKind.Snatch && a.TargetSeats.SequenceEqual([2])) &&
            NamedActions(g).Any(a => a.PlayedCardKind == CardKind.Snatch && a.TargetSeats.SequenceEqual([1])),
            "A sole remaining offensive-horse slot cannot fund a Snatch that becomes distance two after its real payment, while an adjacent target stays payable.");
        Named(g, CardKind.Snatch, 1); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.OffensiveHorse); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        g = Cold(g, registry); Continue(g); Play(g);
        Need(E<EquipmentSlotNamedUseIssuedEvent>(g).Count() == 1 && V(g, 1).HandCount == 3 && g.GetCombatDistance(0, 2) == 2,
            "The payable adjacent Snatch returns through the actual equipment loss child and uses only the surviving native target invoice.");
        _ = Cold(g, registry);

        (g, registry) = Start("loss"); SubmitPlay(g, EquipmentAction(g, CardKind.Crossbow)); Play(g);
        Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor); Play(g); Use(g, "kill-six", [1]);
        Reach(g, () => P(g) is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 } p && p.Choices.Any(c => Zero(c, CardKind.Peach)));
        var otherDying = g.ResolutionStack.OfType<DyingFrame>().Last(); Answer(g, c => Zero(c, CardKind.Peach)); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Weapon);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        Need(V(g, 0).Skills!.All(s => s.Id != Youlong) && V(g, 0).Hp == 4 && V(g, 1).Hp == 0,
            "The paid real equipment child removes the responder's exact Youlong source while the original other victim still truly needs a Peach.");
        g = Cold(g, registry); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Hp);
        Need(V(g, 1).Hp == 1 && g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.TieredRoundConversionUse?.DyingFrameId == otherDying.Id),
            "The exact retired paid source supplements native qualification after skill loss and still issues a genuine needed Peach.");
        g = Cold(g, registry); Continue(g); Play(g); Need(E<DyingResolvedEvent>(g).Single(e => e.ResolutionId == otherDying.Id).Survived && E<EquipmentSlotNamedUseIssuedEvent>(g).Count() == 2,
            "Source loss never repays or cancels a still-legal already-paid native rescue."); _ = Cold(g, registry);

        (g, registry) = Start("silver"); SubmitPlay(g, EquipmentAction(g, CardKind.SilverLion)); Play(g);
        Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Treasure); Play(g); Use(g, "die-five");
        Reach(g, () => P(g) is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 } p && p.Choices.Any(c => Zero(c, CardKind.Peach)));
        var lionDying = g.ResolutionStack.OfType<DyingFrame>().Last(); Answer(g, c => Zero(c, CardKind.Peach)); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Hp); Need(V(g, 0).Hp == 1 && E<EquipmentSlotNamedUseIssuedEvent>(g).Count() == 1,
            "Discarding the genuine paid Silver Lion heals the dying wearer before its selected virtual Peach has issued.");
        g = Cold(g, registry); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); g = Cold(g, registry); Continue(g); Play(g);
        Need(E<DyingResolvedEvent>(g).Single(e => e.ResolutionId == lionDying.Id).Survived && E<EquipmentSlotNamedUsePaidEvent>(g).Count() == 2 &&
            E<EquipmentSlotNamedUseCancelledEvent>(g).Count() == 1 && E<EquipmentSlotNamedUseConsumedEvent>(g).Count() == 1 && E<EquipmentSlotNamedUseIssuedEvent>(g).Count() == 1 &&
            E<TieredRoundConversionUseIssuedEvent>(g).All(e => e.Receipt.EffectiveKind != CardKind.Peach),
            "When the native paid cost already saves its victim, the original Dying completes normally, the actual cost remains spent, and no no-longer-needed Peach or used name is fabricated.");
        NextRound(g, 2); Named(g, CardKind.Duel, 1); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.DefensiveHorse); Play(g);
        Need(NamedActions(g).Any(a => a.PlayedCardKind == CardKind.Peach), "A cancelled unissued Peach remains an unused output name on a later legal Yin face."); _ = Cold(g, registry);
    }

    public static void LuanfengWholeAreaAndOtherRescueKeepFaceAndRoundQuota()
    {
        var (g, registry) = Start("normal"); Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor); Play(g);
        Use(g, "whole"); Play(g); Use(g, "empty-hand"); Play(g); Use(g, "die"); Reach(g, () => Optional(P(g), Luanfeng));
        g = Cold(g, registry); Activate(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Hp);
        Need(V(g, 0).Hp == 3 && V(g, 0).IsEquipmentAreaAbolished && !E<LuanfengSlotsRestoredEvent>(g).Any(),
            "Native recovery reaches three and pauses its actual HP child before any slot restoration or hand draw.");
        g = Cold(g, registry); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain);
        Need(!V(g, 0).IsEquipmentAreaAbolished && Enum.GetValues<EquipmentSlot>().All(s => Capacity(g, 0, s) == 1) &&
            V(g, 0).HandCount == 1 && E<LuanfengSlotsRestoredEvent>(g).Single() is { RestoredSlots: 5, RequestedDraw: 1 } && !E<EquipmentSlotNamedNamesResetEvent>(g).Any(),
            "Restoring a real whole-area abolition counts five actual slots and draws only to one, preserving the still-running self-reset continuation.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(E<EquipmentSlotNamedNamesResetEvent>(g).Count() == 1 && NamedActions(g).Any(a => a.PlayedCardKind == CardKind.Slash) &&
            !NamedActions(g).Any(a => a.PlayedCardKind == CardKind.DrawTwo), "Self rescue clears used names once while retaining Yin and the already spent Yang quota.");
        Named(g, CardKind.Slash, 1); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Treasure); Play(g);
        Need(!NamedActions(g).Any(), "Restoration does not refresh either face's actual round allowance.");
        NextRound(g, 2); Need(NamedActions(g).Any(a => a.PlayedCardKind == CardKind.DrawTwo), "The cleared self name becomes available only with the next real round's Yang quota.");
        _ = Cold(g, registry);

        (g, registry) = Start("normal"); Named(g, CardKind.DrawTwo); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Armor); Play(g);
        Use(g, "two-slots", [1]); Play(g); var hand = V(g, 1).HandCount;
        Use(g, "kill", [1]); Reach(g, () => Optional(P(g), Luanfeng)); Activate(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Hp);
        Need(P(g)!.PlayerSeat == 1 && g.CreateSnapshot(0).PendingDecision is null, "The other dying recipient owns its actual private recovery child.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(V(g, 1).Hp == 3 && Capacity(g, 1, EquipmentSlot.Armor) == 1 && Capacity(g, 1, EquipmentSlot.OffensiveHorse) == 1 &&
            V(g, 1).HandCount == hand && E<LuanfengSlotsRestoredEvent>(g).Single() is { VictimSeat: 1, RestoredSlots: 2, RequestedDraw: 0 } &&
            !E<EquipmentSlotNamedNamesResetEvent>(g).Any(), "Saving another qualifying victim restores only its two abolished slots and never clears the source's used names.");
        Named(g, CardKind.Slash, 1); Reach(g, () => Slots(P(g))); Slot(g, EquipmentSlot.Treasure); Play(g); NextRound(g, 2);
        Need(!NamedActions(g).Any(a => a.PlayedCardKind == CardKind.DrawTwo) && NamedActions(g).Any(a => a.PlayedCardKind == CardKind.Duel),
            "Other-recipient rescue preserves the source's lifetime used names across the next real round.");
        _ = Cold(g, registry);
    }

    private static int Capacity(GameEngine g, int seat, EquipmentSlot slot) => V(g, seat).EquipmentSlotCapacities?.GetValueOrDefault(slot, 1) ?? 1;
    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static IEnumerable<LegalAction> NamedActions(GameEngine g) => g.GetHumanLegalActions().Where(a => a.ConversionSource?.SkillId == Youlong);
    private static LegalAction EquipmentAction(GameEngine g, CardKind kind) => g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && a.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Kind == kind));
    private static bool Slots(PendingDecision? p) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "equipment-slot-named-use") == true;
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Zero(PromptChoice c, CardKind k) => c.Parameters.GetValueOrDefault("response") == "tiered-round-zero-use" && c.Parameters.GetValueOrDefault("output-kind") == k.ToString();
    private static void Named(GameEngine g, CardKind k, int? target = null) => SubmitPlay(g, NamedActions(g).First(a => a.PlayedCardKind == k && (target is null || a.TargetSeats.SequenceEqual([target.Value]))));
    private static void SubmitPlay(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Slot(GameEngine g, EquipmentSlot s) => Answer(g, c => c.Parameters.GetValueOrDefault("equipment-slot") == s.ToString());
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void NextRound(GameEngine g, int round) { End(g); Reach(g, () => E<RoundStartedEvent>(g).Count() >= round && P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }); }
    private static void Reach(GameEngine g, Func<bool> predicate)
    {
        for (var i = 0; i < 230; i++)
        {
            if (predicate()) return; var p = P(g);
            if (p?.SkillPrompt?.SkillId is Watch or Gain or Hp) Continue(g);
            else if (p?.Kind == DecisionKind.DiscardCards) Accept(g, new DiscardCardsCommand(p.PlayerSeat, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "take-damage")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.QinglongCrescentBlade }) Answer(g, c => c.Parameters.GetValueOrDefault("action") == "qinglong-skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.SelectTargetCard }) Answer(g, _ => true);
            else if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) End(g);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        var text = JsonSerializer.Serialize(P(g)); throw new InvalidOperationException("WoLongFengChu boundary missing: " + text[..Math.Min(500, text.Length)] + "; frames=" + JsonSerializer.Serialize(g.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step })) + "; facts=" + JsonSerializer.Serialize(g.Events.TakeLast(5).Select(e => e.Payload.ToString())));
    }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual WoLongFengChu command."); }
    private static void Need(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        static string State(GameEngine e) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(e.CreateSnapshot(s))).ToArray(), Frames = e.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = e.Events.Select(x => JsonSerializer.Serialize(x.Payload, x.Payload.GetType())).ToArray(), e.CardMovements, Commands = CommandJson.Serialize(e.AcceptedCommands), Zones = e.CreateCardZoneDiagnostics() });
        Need(State(g) == State(cold), "Cold replay preserves all private views, exact paid/native receipts, pending recovery children, events, physical movements and accepted commands."); return cold;
    }
    private static void AuditPaidDraft(GameEngine game, ContentRegistry registry)
    {
        var original = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.EquipmentSlotNamedUse?.Payment is not null);
        var d = original.EquipmentSlotNamedUse!;
        var issued = E<EquipmentSlotNamedUseStartedEvent>(game).Single(e => e.FrameId == original.Id);
        Need(!JsonSerializer.Serialize(issued).Contains("OriginalDecisionSignature", StringComparison.Ordinal) &&
            !issued.ToString().Contains(d.OriginalDecisionSignature, StringComparison.Ordinal) &&
            issued.GetType().GetProperties().All(p => p.Name != "OriginalDecisionSignature"),
            "The private native prompt integrity digest stays out of public JSON, text and reflection, rather than exposing a hand-membership oracle.");
        foreach (var bad in new[] { d with { Source = d.Source with { OwnerSeat = 1 } }, d with { OriginalDecisionSignature = "changed-private-invoice" },
            d with { Payment = d.Payment! with { PhaseInstance = d.Payment.PhaseInstance + 1 } } })
        {
            var invalid = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var store = (FrameStore)typeof(GameEngine).GetField("_resolutionStack", flags)!.GetValue(invalid)!;
            var owner = store.OfType<ProgramSkillFrame>().Single(f => f.Id == original.Id); store.Replace(owner with { EquipmentSlotNamedUse = bad });
            var rejected = false;
            try { typeof(GameEngine).GetMethod("AssertCoreInvariants", flags)!.Invoke(invalid, null); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException e && e.Message.StartsWith("Youlong", StringComparison.Ordinal)) { rejected = true; }
            Need(rejected, "The restored owning payment frame rejects altered source, private prompt invoice and paid phase scalars against immutable committed receipts.");
        }
    }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 18 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wo-long-feng-chu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:wo-long-feng-chu");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,{{(scenario == "range" ? "" : "\"modifiers\":[{\"id\":\"range\",\"query\":\"slashDistanceLimit\",\"operation\":\"unlimited\",\"value\":0,\"priority\":0}],")}}
                {{(scenario is "quota" or "qinglong" ? "\"viewAs\":[{\"id\":\"real-slash\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false}]," : "")}}
                "triggers":[{"id":"stock","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"draw","target":"owner","amount":12}]}],
                "activations":[
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":8}]},
                {"id":"die-five","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":5}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"kill-six","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":6}]},
                {"id":"empty-hand","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},
                {"id":"whole","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"abolishOwnerAreas","target":"owner","zones":["equipment"]}]},
                {"id":"two-slots","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"selectedTarget","equipmentSlots":["armor","offensiveHorse"],"amount":0}]},
                {"id":"double-weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["weapon"],"amount":2}]},
                {"id":"only-weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["armor","offensiveHorse","defensiveHorse","treasure"],"amount":0}]},
                {"id":"only-offhorse","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["weapon","armor","defensiveHorse","treasure"],"amount":0}]},
                {"id":"equip-deck","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"weapon"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"cost-child","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["equipment.slot-abolished"],"optional":false,"effects":[{{(scenario == "loss" ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}," : "")}}{"op":"chooseOption","target":"owner","resultBind":"cost","options":[{"id":"continue"}]}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.luanfeng.draw-to"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
              {"id":"{{Hp}}","revision":1,"triggers":[{"id":"recovery-child","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-youlong","skillId":"{{Youlong}}","minimumHp":5}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Attack}}","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],"triggers":[{"id":"real-counter-slash","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]}
            ]}
            """;
            var ids = new[] { Driver, Watch, Gain, Hp, Conditional, Quiet, Attack };
            var labels = ids.ToDictionary(id => id, id => new { name = id, description = "卧龙凤雏实际命令与原生结算夹具", optionLabels = id is Watch or Gain or Hp ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "实际规则夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:wlfc-owner-pick", "固定主人", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:wlfc-peer-pick", "固定其他", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            b.AddGeneral(new(Owner, "卧龙凤雏机制", "ol-wo-long-feng-chu", "fixture:wlfc-owner-pick", "shu", 4, [scenario == "loss" ? Conditional : Youlong, Luanfeng, Driver, Watch, Gain, Hp]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:wlfc-peer-{i}").ToArray();
            foreach (var id in peers) b.AddGeneral(new(id, "其他", "supporter", "fixture:wlfc-peer-pick", "wei", 6,
                scenario == "response" ? [Quiet, Gain, Hp, Attack] : [Quiet, Gain, Hp]));
            b.AddCard(new("fixture:wlfc-qinglong", "青龙偃月刀", "装备牌", "原生追杀武器", LegacyKind: CardKind.QinglongCrescentBlade));
            b.AddCard(new("fixture:wlfc-lion", "白银狮子", "装备牌", "原生移除回复", LegacyKind: CardKind.SilverLion));
            b.AddDeck(new("fixture:wlfc-deck", "固定无响应装备实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 180).Select(i => new ContentDeckPhysicalCard(scenario == "qinglong" ? i < 160 ? "standard:dodge" : "fixture:wlfc-qinglong" : scenario == "range" ? "standard:qinggang_sword" : scenario == "snatch" ? "standard:offensive_horse" : scenario == "silver" ? "fixture:wlfc-lion" : "standard:crossbow", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "卧龙凤雏真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:wlfc-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
