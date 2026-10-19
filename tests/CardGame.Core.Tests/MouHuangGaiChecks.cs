using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class MouHuangGaiChecks
{
    private const string Kurou = "mou-mobile:kurou", ZhaJiang = "mou-mobile:zhajiang";
    private const string Driver = "fixture:mhg-driver", Conv = "fixture:mhg-conversion", Watch = "fixture:mhg-watch";
    private const string Quiet = "fixture:mhg-quiet", Conditional = "fixture:mhg-conditional", Neutral = "fixture:mhg-neutral", PeerResponse = "fixture:mhg-peer-response";
    private const string AiWitness = "fixture:mhg-ai-witness";
    private const string Owner = "fixture:mhg-owner", Mode = "identity:classic-mou-huang-gai-mobile";

    public static void RealPlayStartGiftsPerPointLossArmorPaymentPrivacyCold()
    {
        foreach (var kind in new[] { CardKind.Slash, CardKind.Peach, CardKind.Alcohol })
        {
            var s = Scene.Start("gift-" + kind, kind, atGift: true); var id = s.V().Hand[0].Id;
            var old = s.P!; var recipientBefore = s.V(1).HandCount; var loss = kind is CardKind.Peach or CardKind.Alcohol ? 2 : 1;
            s.Need(s.V() is { Hp: 4, MaxHp: 4, Armor: 0 }, "The real four-HP owner begins before its first optional Play-start gift.");
            s.ActivateGift(); s.Private(); s.RejectPrompt(); s.Cold(); s.Gift(id, 1);
            s.Reach(() => s.Hold("gift-child"));
            s.Need(s.V().Hp == 4 && s.V(1).HandCount == recipientBefore + 1 &&
                s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) == 1 &&
                !s.G.CardMovements.Any(m => m.CardId == id && m.To == CardLocation.Processing),
                "The selected real Hand entity reaches the other Hand once before the pending HP cost; giving is not a card Use.");
            s.RejectOld(old); s.Private(); s.Cold(); s.Continue(); s.Reach(() => s.Hold("loss-child"));
            s.Need(s.V().Hp == 4 - loss && s.V().Armor == 0 &&
                s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.Change is { Kind: HpChangeKind.Loss, TargetSeat: 0 } c && c.Amount == loss),
                "A genuine Loss window owns the one/two-point HP debit before its per-point Armor grants.");
            s.Cold(); s.Play();
            s.Need(s.V().Hp == 4 - loss && s.V().Armor == loss * 2 &&
                s.E<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Kurou && e.BindingId == "loss-armor" && e.Activated && e.Completed) == loss &&
                s.E<MouHuangGaiKurouGiftPaidEvent>().Single().Gift is { CardId: var original, RequestedHpLoss: var requested } && original == id && requested == loss &&
                s.E<MouHuangGaiKurouHpLossPaidEvent>().Single().Loss is { ActualLost: var lost, RequestedHpLoss: var charged } && lost == loss && charged == loss &&
                s.E<MouHuangGaiKurouArmorGrantedEvent>().Select(e => (e.OccurrenceIndex, e.Requested, e.Actual)).SequenceEqual(Enumerable.Range(0, loss).Select(i => (i, 2, 2))) &&
                s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) == 1 && s.OwnUses().Length == 0,
                "Peach and Alcohol lose two HP and resolve two genuine Armor2 occurrences; an ordinary gift resolves exactly one, with no duplicate payment or use ordinal.");
            s.Cold();
        }
        var declined = Scene.Start("decline", atGift: true); var untouched = declined.V().Hand.Select(c => c.Id).ToArray(); declined.SkipGift(); declined.Play();
        declined.Need(declined.V() is { Hp: 4, Armor: 0 } && declined.V().Hand.Select(c => c.Id).SequenceEqual(untouched) &&
            !declined.E<ProgramBindingResolvedEvent>().Any(e => e.SkillId == Kurou && e.Activated), "Declining the genuine optional entry spends no entity, HP or Armor grant.");

        var equipment = Scene.Start("gift-equipment", CardKind.SilverLion); var gear = equipment.V().Hand[0].Id;
        equipment.Native(equipment.Actions().First(a => a.Kind == LegalActionKind.Equip && a.CardId == gear)); equipment.Play();
        var turn = equipment.G.State.TurnNumber; equipment.End(); equipment.Reach(() => equipment.G.State.TurnNumber > turn && equipment.GiftOffer());
        equipment.ActivateGift(); equipment.Need(equipment.P!.Choices.Any(c => c.Cards.SequenceEqual([gear]) && c.Targets.SequenceEqual([1]) &&
            c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment)), "The published gift accepts an actual equipped entity, not only Hand materials.");
        equipment.Private(); equipment.Cold(); equipment.Gift(gear, 1); equipment.Play();
        equipment.Need(equipment.V() is { Hp: 3, Armor: 2 } && !equipment.V().Equipment.Any(c => c.Id == gear) &&
            equipment.G.CardMovements.Count(m => m.CardId == gear && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Hand(1)) == 1,
            "A real equipped Silver Lion is given through native removal and reaches the recipient Hand once; its ordinary card kind costs one HP."); equipment.Cold();

        var damage = Scene.Start("armor-boundaries"); damage.Use("self-damage-one"); damage.Play();
        damage.Need(damage.V() is { Hp: 3, Armor: 0 } && damage.E<DamageAppliedEvent>().Single().Amount == 1 && damage.ArmorOccurrences() == 0,
            "Damage reducing actual HP is not the explicit Loss event required by KuRou.");
        damage.Use("generic-armor-three"); damage.Play(); damage.Use("self-damage-two"); damage.Play();
        damage.Need(damage.V() is { Hp: 3, Armor: 1 } && damage.E<DamageAppliedEvent>().Last().ArmorPayment is { ArmorAbsorbed: 2, HpLossAmount: 0 } && damage.ArmorOccurrences() == 0,
            "Fully absorbed native damage spends Armor without manufacturing a Loss or another Armor grant.");
        damage.Use("self-damage-two"); damage.Play();
        damage.Need(damage.V() is { Hp: 2, Armor: 0 } && damage.E<DamageAppliedEvent>().Last().ArmorPayment is { ArmorAbsorbed: 1, HpLossAmount: 1 } && damage.ArmorOccurrences() == 0,
            "Partially absorbed damage also keeps its real HP reduction separate from direct HP Loss."); damage.Use("loss-one"); damage.Play();
        damage.Need(damage.V() is { Hp: 1, Armor: 2 } && damage.ArmorOccurrences() == 1, "A real direct Loss bypasses Armor and earns one genuine Armor2 occurrence."); damage.Cold();

        var unbounded = Scene.Start("armor-unbounded"); unbounded.Use("loss-two"); unbounded.Play();
        unbounded.Need(unbounded.V() is { Hp: 2, Armor: 4 } && unbounded.ArmorOccurrences() == 2, "Two real Loss points establish four KuRou Armor.");
        unbounded.Use("loss-one"); unbounded.Play(); unbounded.Need(unbounded.V().Armor == 6 && unbounded.ArmorOccurrences() == 3, "The next real Loss grants both Armor points, advancing four to six.");
        unbounded.Use("recover-one"); unbounded.Play(); unbounded.Use("loss-one"); unbounded.Play();
        unbounded.Need(unbounded.V().Armor == 8 && unbounded.ArmorOccurrences() == 4, "Another real Loss advances six to eight without clipping its paid grant.");
        unbounded.Use("generic-armor-three"); unbounded.Play(); unbounded.Use("recover-one"); unbounded.Play(); unbounded.Use("loss-one"); unbounded.Play();
        var kurouArmor = unbounded.E<MouHuangGaiKurouArmorGrantedEvent>();
        unbounded.Need(unbounded.V().Armor == 13 && unbounded.ArmorOccurrences() == 5 &&
            unbounded.E<ArmorGrantedEvent>().Single(e => e.Source.SkillId == Driver && e.Amount == 3) is { Amount: 3, ArmorBefore: 8, ArmorAfter: 11 } &&
            kurouArmor.Select(e => (e.ArmorBefore, e.ArmorAfter, e.Requested, e.Actual)).SequenceEqual(new[] { (0, 2, 2, 2), (2, 4, 2, 2), (4, 6, 2, 2), (6, 8, 2, 2), (11, 13, 2, 2) }) &&
            kurouArmor.Select(e => (e.HpChangeId, e.OccurrenceIndex)).Distinct().Count() == 5 &&
            unbounded.E<ArmorGrantedEvent>().Where(e => e.Source.SkillId == Kurou)
                .Select(e => (e.ProgramFrameId, e.PlayerSeat, e.Amount, e.ArmorBefore, e.ArmorAfter))
                .SequenceEqual(kurouArmor.Select(e => (e.FrameId, 0, e.Actual, e.ArmorBefore, e.ArmorAfter))),
            "Each real Loss point requests and grants two Armor exactly once through matching KuRou and generic invoices; generic Armor3 remains independent and existing Armor eleven advances to thirteen."); unbounded.Cold();

        foreach (var scene in new[] { "ai-safe-mix", "ai-all-fatal" })
        {
            var ai = Scene.StartAi(scene); var ownerSeat = ai.G.CreateSnapshot(0).Players.Single(p => p.GeneralId == Owner).Seat;
            var owner = ai.V(ownerSeat); var expectedHp = scene == "ai-safe-mix" ? 2 : 1;
            var resolved = ai.E<ProgramBindingResolvedEvent>().Single(e => e.SkillId == Kurou && e.BindingId == "give-before-loss");
            var given = ai.E<MouHuangGaiKurouGiftPaidEvent>();
            var actualStock = owner.Hand.Select(c => c.Kind).Concat(given.Select(e => e.Gift.PrintedKind)).ToHashSet();
            ai.Need(owner.MaxHp == 4 && owner.Hp == expectedHp - given.Sum(e => e.Gift.RequestedHpLoss) &&
                (scene != "ai-safe-mix" || actualStock.Contains(CardKind.Alcohol) && actualStock.Contains(CardKind.Dodge)),
                "The first actual AI result preserves the intended low-HP setup and real unsafe Alcohol/safe Dodge stock, including any one entity actually given.");
            ai.Need(Enumerable.Range(0, 4).Where(v => v != ownerSeat).All(v => ai.G.CreateSnapshot(v).Players[ownerSeat].Hand.Count == 0),
                "The low-HP AI evaluates only its own published materials; other viewers do not gain its private Hand."); ai.Cold();
            ai.Need(ai.V(ownerSeat).IsAlive && ai.V(ownerSeat).Hp > 0 && !ai.E<PlayerDyingEvent>().Any(e => e.VictimSeat == ownerSeat) &&
                resolved.OwnerSeat == ownerSeat && ownerSeat != 1 && ai.P is { PlayerSeat: 1 } witness && witness.SkillPrompt?.SkillId == AiWitness &&
                ai.G.State.CurrentSeat == ownerSeat && ai.G.AcceptedCommands.All(c => c is StartGameCommand or AdvanceOneStepCommand or SelectGeneralCommand),
                "The real optional activation and canonical card choice run through built-in AI commands and never knowingly pay a lethal gift.");
            if (scene == "ai-all-fatal") ai.Need(given.Length == 0 && ai.V(ownerSeat).Hp == expectedHp &&
                !resolved.Activated,
                "At one HP with no removal recovery, all gifts are fatal and the genuine optional AI must skip.");
            else ai.Need((given.Length == 0 && !resolved.Activated && ai.V(ownerSeat).Hp == expectedHp) || (given is [var safe] && resolved is { Activated: true, Completed: true } &&
                safe.Gift.EffectiveKind == CardKind.Dodge && safe.Gift.RequestedHpLoss == 1 && ai.V(ownerSeat).Hp == 1),
                "Native health valuation may conservatively skip; if it gives, the shared candidate ranking must choose safe Dodge rather than lethal Alcohol."); ai.Cold();
        }
    }

    public static void ActualDrawTimingDynamicOwnTurnUsePrivilegesCold()
    {
        var draw = Scene.Start("draw-timing", CardKind.Dodge, atGift: true);
        draw.Need(draw.V() is { Hp: 2, MaxHp: 4, HandCount: 8, Armor: 4 }, "A real pre-turn Loss2 yields the opening four plus normal two plus current lost-HP two, before Play-start giving.");
        var before = draw.V().HandCount; var gift = draw.V().Hand[0].Id; draw.ActivateGift(); draw.Gift(gift, 1); draw.Play();
        draw.Need(draw.V() is { Hp: 1, Armor: 6 } && draw.V().HandCount == before - 1,
            "The later Play-start HP loss changes X for uses but cannot retroactively add cards to an already completed Draw phase.");
        draw.Use("recover-one"); draw.Play(); var retained = draw.V().HandCount; var previous = draw.G.State.TurnNumber;
        draw.End(); draw.Reach(() => draw.G.State.TurnNumber > previous && draw.GiftOffer());
        draw.Need(draw.V().Hp == 2 && draw.V().HandCount == retained + 4, "The next real Draw phase recomputes X after recovery instead of retaining the earlier three lost HP."); draw.SkipGift(); draw.Play(); draw.Cold();

        var s = Scene.Start("privilege", CardKind.Slash); var nulls = s.E<NullificationRequestedEvent>().Length;
        var first = s.UseNative(CardKind.DrawTwo, []);
        s.Need(s.OwnAdmissions().Single() is { UseOrdinal: 1, Applies: true } && s.E<NullificationRequestedEvent>().Length == nulls,
            "An actual ordinary trick consumes slot one and its qualifying Use opens no Nullification request.");
        var requests = s.E<ResponseRequestedEvent>().Length; var second = s.UseNative(CardKind.Slash, [2]);
        s.Need(s.OwnAdmissions().Select(e => (e.UseOrdinal, e.Applies)).SequenceEqual(new[] { (1, true), (2, true) }) && s.E<ResponseRequestedEvent>().Length == requests &&
            s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1),
            "The second qualifying real Slash reaches distance two and cannot be answered even by a recipient with a genuine Dodge conversion.");
        s.Need(!s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "The prospective third Use already exceeds current X=2 and regains native distance/count restrictions.");
        s.Use("recover-one"); s.Play(); s.Need(s.V().Hp == 3 && !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])),
            "Real recovery lowers current X for subsequent legal-action publication without resetting the prior true-Use ordinal.");
        s.Use("loss-two"); s.Play(); requests = s.E<ResponseRequestedEvent>().Length; var third = s.UseNative(CardKind.Slash, [2]);
        s.Need(s.V().Hp == 1 && s.OwnAdmissions().Select(e => (e.UseOrdinal, e.Applies)).SequenceEqual(new[] { (1, true), (2, true), (3, true) }) &&
            s.E<ResponseRequestedEvent>().Length == requests && new[] { first, second, third }.All(id => s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == id) == 1),
            "A later genuine Loss raises X to three, permitting the third far Slash beyond ordinary quota; all three whole uses finish once.");
        s.Use("lose-zhajiang"); s.Play(); s.Need(!s.Has(ZhaJiang) && s.V().Armor == 8 &&
            !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "Actual source loss removes future privileges while retaining already paid Armor.");
        s.Use("grant-zhajiang"); s.Play(); s.Need(s.Has(ZhaJiang) && s.OwnUses().Length == 3 &&
            !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "Regranting the skill cannot reset the actual-turn use history or lend slot three to a fourth use."); s.Cold();

        var equipment = Scene.Start("equipment-slot", CardKind.SilverLion); var material = equipment.V().Hand[0].Id;
        equipment.Native(equipment.Actions().First(a => a.Kind == LegalActionKind.Equip && a.CardId == material)); equipment.Play();
        equipment.Need(equipment.OwnAdmissions().Single() is { UseOrdinal: 1, Applies: true } && equipment.V().Equipment.Any(c => c.Id == material),
            "A genuine equipment Use also occupies the first slot; slots are not restricted to damage or ordinary tricks."); equipment.Cold();
    }

    public static void NativeResponsesPlayedAndForeignTurnBoundariesCold()
    {
        var s = Scene.Start("own-responses"); s.Use("duel", targets: [1]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
        var nullCard = s.Respond(CardKind.Nullification); s.Play();
        s.Need(s.OwnUses().Single().IsNullificationUse && s.OwnAdmissions().Single() is { UseOrdinal: 1, Applies: true } && s.PaidOnce(nullCard),
            "A native paid Nullification on the owner's actual turn is a true Use and consumes the first slot once.");
        s.IncomingSlash(); var dodge = s.Respond(CardKind.Dodge); s.Play();
        s.Need(s.OwnAdmissions().Select(e => (e.UseOrdinal, e.Applies)).SequenceEqual(new[] { (1, true), (2, true) }) && s.PaidOnce(dodge) &&
            s.E<CardRespondedEvent>().Any(e => e.CardId == dodge && e.ResponderSeat == 0 && e.EffectiveCardKind == CardKind.Dodge),
            "Defending a real Slash with Dodge is a paid true Use, distinct from group-trick Played responses."); s.Cold();

        var played = Scene.Start("played"); played.Use("duel", targets: [1]); played.Reach(() => played.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        var slash = played.Respond(CardKind.Slash); played.Play();
        played.Need(played.PaidOnce(slash) && played.E<CardRespondedEvent>().Any(e => e.CardId == slash && e.ResponderSeat == 0) && played.OwnUses().Length == 0 && played.OwnAdmissions().Length == 0,
            "A Duel's genuine pure Played Slash pays a real material but cannot consume a true-Use slot or gain ZhaJiang admission.");
        _ = played.UseNative(CardKind.Slash, [2]); played.Need(played.OwnAdmissions().Single() is { UseOrdinal: 1, Applies: true },
            "The first real own Use after pure Played remains slot one with the full native privilege."); played.Cold();

        foreach (var kind in new[] { CardKind.Slash, CardKind.Duel })
        {
            var foreign = Scene.Start("foreign-" + kind, kind); foreign.End();
            foreign.Reach(() => foreign.G.State.CurrentSeat == 1 && foreign.P is { PlayerSeat: 0 } p &&
                p.Kind == (kind == CardKind.Slash ? DecisionKind.RespondDodge : DecisionKind.Nullification));
            foreign.Private(); foreign.Cold(); var response = foreign.Respond(kind == CardKind.Slash ? CardKind.Dodge : CardKind.Nullification);
            foreign.Reach(() => foreign.PaidOnce(response)); var fact = foreign.E<CurrentTurnCardUseKindsRecordedEvent>().Single(e => e.ActorSeat == 0 && e.TurnOwnerSeat == 1);
            foreign.Need(fact.WholeActualTurnUseSequence is > 0 && foreign.PaidOnce(response) && foreign.E<MouZhaJiangResponseAdmittedEvent>().Single(e => e.ActorSeat == 0 && e.Admission.TurnOwner == 1).Admission is { Applies: false, UseOrdinal: 1 },
                "Another player's actual turn still records the genuine response Use under that turn owner; it does not become the owner's turn."); foreign.Cold();
        }
    }

    public static void PaidGiftSourceLossDyingRescueAndFrozenUseCold()
    {
        var s = Scene.Start("gift-source-loss", atGift: true); var gift = s.V().Hand[0].Id; s.ActivateGift(); s.Gift(gift, 1);
        s.Reach(() => s.Hold("gift-child")); s.Need(s.Has(Kurou) && s.V().Hp == 4, "The original gift is paid while its exact KuRou source still exists.");
        s.Private(); s.Cold(); s.Continue(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        var dying = s.G.ResolutionStack.OfType<DyingFrame>().Single().Id;
        s.Need(!s.Has(Kurou) && s.V().Hp == 0 && s.V(1).HandCount == 5 &&
            s.G.CardMovements.Count(m => m.CardId == gift && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) == 1,
            "A real movement child loses three HP and disables the source; the already-paid gift still pays its final Loss1 and enters native dying once.");
        s.Private(); s.RejectPrompt(); s.Cold(); var rescue = s.Respond(CardKind.Peach); s.Play();
        s.Need(s.V() is { Hp: 1, IsAlive: true } && !s.Has(Kurou) && s.PaidOnce(rescue) && s.OwnAdmissions().Single() is { UseOrdinal: 1, Applies: true } &&
            s.E<DyingResolvedEvent>().Count(e => e.ResolutionId == dying && e.Survived) == 1 &&
            s.E<MouHuangGaiKurouGiftPaidEvent>().Length == 1 && s.E<MouHuangGaiKurouLossRequestedEvent>().Single().RequestedHpLoss == 1 &&
            s.E<MouHuangGaiKurouHpLossPaidEvent>().Single().Loss is { HpBefore: 1, HpAfter: 0, ActualLost: 1 } && s.E<MouHuangGaiKurouCompletedEvent>().Length == 1 &&
            s.G.CardMovements.Count(m => m.CardId == gift && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) == 1,
            "A real material Peach rescue consumes one own-turn Use and returns the original paid gift once after source loss, without giving or losing HP again."); s.Cold();

        var frozen = Scene.Start("frozen-use"); var before = frozen.E<ResponseRequestedEvent>().Length;
        var action = frozen.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])); var cost = action.CardId!.Value;
        frozen.Native(action); frozen.Reach(() => frozen.Hold("use-child"));
        var use = frozen.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.SourceSeat == 0 && u.CardId == cost);
        frozen.Need(use.Action is { PhysicalCards.Count: 1 } accepted && accepted.PhysicalCards[0].CardId == cost &&
            accepted.PhysicalCards is IList { IsReadOnly: true } && accepted.TargetSeats is IList { IsReadOnly: true } && frozen.Has(ZhaJiang) &&
            use.MouZhaJiangUse?.Admission is { Applies: true, UseOrdinal: 1, Hp: 2, MaxHp: 4, Source: not null },
            "The admitted far Slash owns its frozen native action and readonly real one-entity payment before its HP/source-changing child.");
        var useId = use.Id; frozen.Private(); frozen.RejectPrompt(); frozen.Cold(); frozen.Continue(); frozen.Play();
        frozen.Need(frozen.V() is { Hp: 3, Armor: 4 } && !frozen.Has(ZhaJiang) && frozen.PaidOnce(cost) &&
            frozen.E<ResponseRequestedEvent>().Length == before && frozen.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) == 1 &&
            frozen.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId) == 1 &&
            !frozen.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])),
            "Recovery and natural loss of the exact source cannot revoke an admitted far Slash's no-response effect, replay its cost, or lend that receipt to a later use.");
        frozen.Use("self-damage-two"); frozen.Play(); frozen.Need(frozen.V() is { Hp: 3, Armor: 2 }, "Armor already earned before source loss remains genuine damage payment."); frozen.Cold();

        var negative = Scene.Start("negative-cost", CardKind.SilverLion); var lion = negative.V().Hand[0].Id;
        negative.Need(negative.V().Hp == 1 && !negative.Has(ZhaJiang), "The deliberately low-HP conditional source is absent before either native use.");
        negative.Native(negative.Actions().First(a => a.Kind == LegalActionKind.Equip && a.CardId == lion)); negative.Play();
        var attacksBefore = negative.E<ResponseRequestedEvent>().Length;
        negative.Native(negative.Actions().First(a => a.ConversionSource?.SkillId == Conv && a.ConversionSource.BindingId == "real-lion-slash" && a.CardId == lion && a.TargetSeats.SequenceEqual([1])));
        negative.Reach(() => negative.Hold("negative-cost-child"));
        var paid = negative.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.SourceSeat == 0 && u.CardId == lion && u.CardKind == CardKind.Slash);
        negative.Need(negative.V().Hp == 2 && negative.Has(ZhaJiang) && paid.MouZhaJiangUse?.Admission is { Applies: false, UseOrdinal: 2, Hp: 1, Source: null } &&
            negative.G.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1,
            "A real Silver Lion equipment cost heals and reacquires the source, but cannot upgrade the previously negative admission of native Use2.");
        var negativeUse = paid.Id; negative.Private(); negative.Cold(); negative.Continue(); negative.Play();
        negative.Need(negative.E<ResponseRequestedEvent>().Skip(attacksBefore).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.RequiredCardKind == CardKind.Dodge) == 1 &&
            negative.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == negativeUse) == 1 &&
            negative.G.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1 &&
            negative.E<MouZhaJiangUseAdmittedEvent>().Single(e => e.UseId == negativeUse).Admission.Applies == false,
            "The negative signed cost receipt keeps the original target's genuine Dodge request and finishes once despite now meeting both HP and source conditions."); negative.Cold();

        var reserved = Scene.Start("reserved-declaration");
        var declare = reserved.Actions().First(a => a.ConversionSource?.SkillId == "classic:guhuo" && a.ConversionSource.BindingId == "declare-slash" && a.TargetSeats.SequenceEqual([2]));
        var original = declare.CardId!.Value; reserved.Native(declare); reserved.Reach(() => reserved.Hold("declaration-cost"));
        var mother = reserved.G.ResolutionStack.OfType<CardDeclarationFrame>().Single(); var declarationId = mother.Id;
        reserved.Need(mother.Payment.MouZhaJiang is { UseOrdinal: 1, Applies: true } && reserved.OwnUses().Length == 0 &&
            reserved.G.CardMovements.Count(m => m.CardId == original && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason.Value == "conversion.declaration.pay") == 1,
            "The real Guhuo mother reserves positive slot one before its one physical declaration payment has produced a CardUse ledger entry."); reserved.Private(); reserved.Cold(); reserved.Continue();
        reserved.Reach(() => reserved.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
        reserved.Answer(c => c.Targets.SequenceEqual([0]));
        reserved.Reach(() => reserved.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true);
        var childMaterial = reserved.P!.Choices.First(c => c.Cards.Count == 1 && reserved.V().Hand.Any(h => h.Id == c.Cards[0] && h.Kind == CardKind.SilverLion)).Cards.Single();
        reserved.Answer(c => c.Cards.SequenceEqual([childMaterial]));
        reserved.Reach(() => reserved.OwnAdmissions().Any(q => q.UseOrdinal == 2));
        reserved.Need(reserved.OwnAdmissions().Single() is { UseOrdinal: 2, Applies: false } && reserved.OwnUses().Length == 1 &&
            reserved.G.ResolutionStack.OfType<CardDeclarationFrame>().Single(f => f.Id == declarationId).Payment.MouZhaJiang is { UseOrdinal: 1, Applies: true },
            "The genuine physical Silver Lion Use in the declaration payment child receives slot two, while the still-live mother's slot-one receipt remains frozen."); reserved.Cold();
        reserved.Reach(() => reserved.Hold("declaration-child-returned")); reserved.Cold(); reserved.Continue(); reserved.Play();
        var admitted = reserved.E<MouZhaJiangUseAdmittedEvent>().Where(e => e.Admission.ActorSeat == 0).ToArray();
        var parent = admitted.Single(e => e.DeclarationId == declarationId); var child = admitted.Single(e => e.Kind == CardKind.SilverLion);
        reserved.Need(parent.Admission is { UseOrdinal: 1, Applies: true } && child.Admission is { UseOrdinal: 2, Applies: false } &&
            reserved.OwnUses().Select(e => e.CardActionId).SequenceEqual(new long?[] { child.ActionId, parent.ActionId }) &&
            reserved.OwnUses().Select(e => e.WholeActualTurnUseSequence).SequenceEqual(new int?[] { 1, 2 }) &&
            reserved.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == child.UseId) == 1 && reserved.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == parent.UseId) == 1 &&
            reserved.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) == 1 && reserved.PaidOnce(original) &&
            reserved.G.CardMovements.Count(m => m.CardId == childMaterial && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            reserved.G.CardMovements.Count(m => m.CardId == childMaterial && m.From == CardLocation.Processing && m.To == CardLocation.Equipment(0)) == 1 &&
            reserved.E<MouZhaJiangDeclarationAdmittedEvent>().Count(e => e.DeclarationId == declarationId) == 1,
            "Actual completion keeps child-before-parent ledger order while the typed reservation prevents shared first-X privileges, pays the mother's real entity once and returns both genuine uses once."); reserved.Cold();
    }

    // Uniform fixed stock plus real view-as materials keeps every fixture small and deterministic.
    private sealed class Fixture(string scene, CardKind kind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-huang-gai-mobile", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.mou-huang-gai-mobile." + suffix + ".json") ?? throw new InvalidOperationException("Missing real mobile Mou Huang Gai skill bundle."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var production = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var id in new[] { Kurou, ZhaJiang }) b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description)
                { Program = production.Programs[id], ProgramPresentation = production.Presentations[id] });
            if (scene == "reserved-declaration")
            {
                string ReadGuhuo(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.classic-yu-ji." + suffix + ".json") ?? throw new InvalidOperationException("Missing real Guhuo declaration bundle."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var guhuo = SkillProgramCatalog.Load(ReadGuhuo("rules"), ReadGuhuo("presentation"));
                foreach (var (id, program) in guhuo.Programs) b.AddSkill(new(id, guhuo.Presentations[id].Name, guhuo.Presentations[id].Description) { Program = program, ProgramPresentation = guhuo.Presentations[id] });
            }
            b.AddSkill(new(Neutral, "已失去来源", "中性真实替代技能"));
            static JsonNode N(string json) => JsonNode.Parse(json)!;
            static JsonObject A(string id, string effects, int targets = 0) => new() { ["id"] = id, ["minCards"] = 0, ["maxCards"] = 0,
                ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            static string Pause(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held-" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var acts = new JsonArray(A("loss-one", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("loss-two", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}"),
                A("recover-one", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"), A("generic-armor-three", "{\"op\":\"grantArmor\",\"target\":\"owner\",\"amount\":3}"),
                A("self-damage-one", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}"), A("self-damage-two", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":2}"),
                A("incoming", "{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}", 1),
                A("lose-zhajiang", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"" + ZhaJiang + "\"],\"sourceBind\":\"" + Neutral + "\"}"),
                A("grant-zhajiang", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + ZhaJiang + "\"]}"));
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2; acts.Add(duel);
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts,
                ["modifiers"] = N("[{\"id\":\"small-real-inventory\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":40,\"priority\":0}]") },
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"),
                N("{\"id\":\"" + Conv + "\",\"revision\":1,\"viewAs\":[{\"id\":\"real-study\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"drawTwo\",\"forPlay\":true,\"forResponse\":false,\"singleCardTrickUse\":true},{\"id\":\"real-dodge\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"dodge\",\"forPlay\":false,\"forResponse\":true},{\"id\":\"real-null\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"nullification\",\"forPlay\":false,\"forResponse\":true},{\"id\":\"real-rescue\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"peach\",\"forPlay\":false,\"forResponse\":true,\"extendedUse\":true}]}"));
            var own = scene.StartsWith("ai-", StringComparison.Ordinal) ? new List<string> { Driver } : new List<string> { Driver, Conv }; var watch = new JsonArray();
            if (scene == "reserved-declaration") own.Add("classic:guhuo");
            defs.Add(N("{\"id\":\"" + PeerResponse + "\",\"revision\":1,\"viewAs\":[{\"id\":\"native-dodge\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"dodge\",\"forPlay\":false,\"forResponse\":true},{\"id\":\"native-counter\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"nullification\",\"forPlay\":false,\"forResponse\":true}]}"));
            if (scene == "gift-source-loss")
            { defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"living-kurou-source\",\"skillId\":\"" + Kurou + "\",\"minimumHp\":2}]}")); own.Add(Conditional); }
            else own.Add(Kurou);
            if (scene is "frozen-use" or "negative-cost")
            { defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"wounded-zhajiang-source\",\"skillId\":\"" + ZhaJiang + "\"," + (scene == "negative-cost" ? "\"minimumHp\":2" : "\"maximumHp\":2") + "}]}")); own.Add(Conditional); }
            else own.Add(ZhaJiang);
            if (scene is "draw-timing" or "privilege" or "equipment-slot" or "own-responses" or "played" or "frozen-use" or "negative-cost" or "reserved-declaration" || scene.StartsWith("foreign-", StringComparison.Ordinal) || scene.StartsWith("ai-", StringComparison.Ordinal))
                watch.Add(N("{\"id\":\"real-starting-loss\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"priority\":1000,\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene is "negative-cost" or "ai-all-fatal" ? 3 : scene == "reserved-declaration" ? 1 : 2) + "}]}"));
            if (scene.StartsWith("gift-", StringComparison.Ordinal))
                foreach (var zone in new[] { "hand", "equipment" })
                watch.Add(N("{\"id\":\"real-gift-movement-child-" + zone + "\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"" + zone + "\"],\"movementReasons\":[\"skill-program.mou-mobile.kurou.give\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"priority\":1000,\"effects\":[" + Pause("gift-child") +
                    (scene == "gift-source-loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":3}" : "") + "]}"));
            if (scene.StartsWith("gift-", StringComparison.Ordinal) && scene != "gift-source-loss")
                watch.Add(N("{\"id\":\"real-loss-point-child\",\"window\":\"afterHpLost\",\"subject\":\"owner\",\"hpChangeOccurrence\":\"perPoint\",\"optional\":false,\"priority\":1000,\"effects\":[" + Pause("loss-child") + "]}"));
            if (scene == "frozen-use") watch.Add(N("{\"id\":\"frozen-use-hp-source-change\",\"window\":\"cardUseCommitted\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"optional\":false,\"priority\":1000,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[" + Pause("use-child") + ",{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}]}"));
            if (scene == "negative-cost")
            {
                defs.Last(n => n!["id"]!.GetValue<string>() == Conv)!["viewAs"]!.AsArray().Add(N("{\"id\":\"real-lion-slash\",\"inputKinds\":[\"silverLion\"],\"inputSuits\":[],\"sourceZones\":[\"equipment\"],\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false}"));
                watch.Add(N("{\"id\":\"real-lion-cost-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Pause("negative-cost-child") + "]}"));
            }
            if (scene == "reserved-declaration") watch.Add(N("{\"id\":\"real-reserved-declaration-cost\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"conversion.declaration.pay\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("declaration-cost") + ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"zones\":[\"hand\"],\"cardKinds\":[\"silverLion\"],\"minimumCards\":1,\"maximumCards\":1,\"resultBind\":\"reserved-equipment\"},{\"op\":\"revealBoundCards\",\"target\":\"owner\",\"sourceBind\":\"reserved-equipment\"},{\"op\":\"useBoundCardByTarget\",\"target\":\"selectedTarget\",\"sourceBind\":\"reserved-equipment\"}," + Pause("declaration-child-returned") + "]}"));
            if (watch.Count > 0) { defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watch }); own.Add(Watch); }
            if (scene.StartsWith("ai-", StringComparison.Ordinal)) defs.Add(N("{\"id\":\"" + AiWitness + "\",\"revision\":1,\"triggers\":[{\"id\":\"after-ai-gift\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"priority\":-1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("ai-witness") + "]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "谋黄盖固定小局真实原生与已付子流程",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:mhg-own-pick"; b.AddSkill(new(pick, "固定本人", "真实选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => scene.StartsWith("ai-", StringComparison.Ordinal) && r == Role.Lord ? 100000000d : -100000000d) });
            // Classic identity adds Lord +1: this fixture therefore has exactly the production general's actual four HP.
            b.AddGeneral(new(Owner, "谋黄盖真实本人", "supporter", pick, "wu", 3, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:mhg-peer-" + i).ToArray();
            foreach (var i in Enumerable.Range(1, 3))
            { var peerPick = "fixture:mhg-peer-pick-" + i; b.AddSkill(new(peerPick, "固定他人", "真实选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                var peerSkills = i == 1 && scene.StartsWith("foreign-", StringComparison.Ordinal) ? new List<string> { PeerResponse } : new List<string> { Quiet, PeerResponse };
                if (i == 1 && scene.StartsWith("ai-", StringComparison.Ordinal)) peerSkills.Add(AiWitness);
                b.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", peerPick, "wei", 12, peerSkills)); }
            var card = CardCatalog.Get(kind); const string entity = "fixture:mhg-real-entity"; b.AddCard(new(entity, card.DisplayName, card.CategoryName, "真实固定原始牌", kind));
            if (scene == "reserved-declaration") { var gear = CardCatalog.Get(CardKind.SilverLion); b.AddCard(new("fixture:mhg-reserved-equipment", gear.DisplayName, gear.CategoryName, "真实成本子窗装备", CardKind.SilverLion)); }
            b.AddDeck(new("fixture:mhg-deck", "四十八张真实小牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard(
                scene == "ai-safe-mix" ? i % 2 == 0 ? "standard:dodge" : "standard:alcohol" : scene == "ai-all-fatal" ? "standard:dodge" : scene == "reserved-declaration" && i % 2 == 1 ? "fixture:mhg-reserved-equipment" : entity, Suit.Spade, i % 13 + 1)).ToArray() });
            var roles = scene.StartsWith("ai-", StringComparison.Ordinal)
                ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }
                : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "谋黄盖原生小局", 4, 4, roles,
                "fixture:mhg-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }

    private sealed class Scene(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Has(string id) => V().Skills?.Any(s => s.ContentId == id) == true;
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public CurrentTurnCardUseKindsRecordedEvent[] OwnUses() => E<CurrentTurnCardUseKindsRecordedEvent>().Where(e => e.ActorSeat == 0 && e.TurnNumber == G.State.TurnNumber).ToArray();
        public MouZhaJiangAdmission[] OwnAdmissions() => G.Events.Select(e => e.Payload switch
            { MouZhaJiangUseAdmittedEvent u => u.Admission, MouZhaJiangResponseAdmittedEvent r => r.Admission, _ => null })
            .OfType<MouZhaJiangAdmission>().Where(q => q.ActorSeat == 0 && q.ActualTurn == G.State.TurnNumber).ToArray();
        public int ArmorOccurrences() => E<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Kurou && e.BindingId == "loss-armor" && e.Activated && e.Completed);
        public bool PaidOnce(int id) => G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1;
        public bool GiftOffer() => P?.SkillPrompt?.SkillId == Kurou && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "mou-huang-gai-kurou");
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void ActivateGift() { if (P!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-huang-gai-kurou") == true); }
        public void Gift(int id, int target) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-huang-gai-kurou" && c.Cards.SequenceEqual([id]) && c.Targets.SequenceEqual([target]));
        public void SkipGift() => Answer(c => c.Parameters.GetValueOrDefault("program-action") is "skip" or "cancel");
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        public long UseNative(CardKind kind, int[] targets)
        {
            var a = Actions().Where(a => (a.PlayedCardKind ?? (a.CardId is { } id ? V().Hand.FirstOrDefault(c => c.Id == id)?.Kind : null)) == kind && a.TargetSeats.SequenceEqual(targets))
                .OrderBy(a => a.ConversionSource is null ? 0 : 1).FirstOrDefault(); Need(a is not null, "A real published native " + kind + " plan must exist.");
            var card = a!.CardId!.Value; Native(a); Play(); var declared = E<CardUseDeclaredEvent>().Last(e => e.SourceSeat == 0 && e.CardId == card && e.CardKind == kind);
            Need(PaidOnce(card) && E<CardUseFinishedEvent>().Count(e => e.ResolutionId == declared.ResolutionId) == 1, "The real selected Hand material pays and finishes its whole native Use once."); return declared.ResolutionId;
        }
        public void IncomingSlash()
        { Use("incoming", [1]); Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])) == true);
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])); Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); }
        public int Respond(CardKind kind)
        {
            var c = P!.Choices.FirstOrDefault(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") is not ("pass" or "take-damage" or "let-die") &&
                (c.Parameters.GetValueOrDefault("conversion-binding-id") == (kind switch { CardKind.Peach => "real-rescue", CardKind.Dodge => "real-dodge", CardKind.Nullification => "real-null", _ => "" }) ||
                 V().Hand.Any(h => h.Id == c.Cards[0] && h.Kind == kind))); Need(c is not null, "A real owned paid " + kind + " response must be published.");
            var id = c!.Cards.Single(); Private(); RejectPrompt(); Cold(); Answer(x => x.Id == c.Id); return id;
        }
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "The owner answers one actually published canonical choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "The bounded fixture ended before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == Watch) Continue();
                else if (GiftOffer()) SkipGift();
                else if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Private() => Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(v => G.CreateSnapshot(v).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(p => p.Seat != v).All(p => p.Hand.Count == 0)), "Private canonical choices belong only to the real chooser and every foreign Hand stays opaque.");
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold replay preserves all four prepared views, exact owning frames, actual Use ordinals, real HP/Armor receipts and accepted payments."); }
        public void Reject(GameCommand c) { var before = State(); Need(!G.Submit(c).Accepted && State() == before, "A stale, forged or foreign command cannot alter any view, native frame, payment or history."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-mou-huang-gai"), G.Revision)); }
        public void RejectOld(PendingDecision p) => Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision));
        public void Accept(GameCommand c) { try { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, "Rejected " + c.GetType().Name + ": " + r.Error?.Code + "/" + r.Error?.Message); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " cause=" + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner,
            Prompt = P is { } p ? new { p.Kind, p.PlayerSeat, p.PromptId, p.RequiredCardKind, p.SkillPrompt, Choices = p.Choices.Take(6) } : null,
            Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).MaxHp, V(i).Armor, V(i).IsAlive, V(i).HandCount }),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step, Native = f is CardUseFrame u ? new { u.CardId, u.SourceSeat, u.TargetSeats, u.PhysicalCardIds, u.Action } : null }),
            OwnUses = OwnUses(), Bindings = E<ProgramBindingResolvedEvent>().Where(e => e.SkillId == Kurou || e.SkillId == Watch).TakeLast(10) });
        public static Scene Start(string scene, CardKind kind = CardKind.Slash, bool atGift = false)
        {
            ContentRegistry registry; try { registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene, kind)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
                UseInteractiveSetup = true, ConfirmOpeningHand = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new Scene(scene, game, registry); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, Owner, game.Revision, s.P!.PromptId)); if (atGift) s.Reach(s.GiftOffer); else s.Play(); return s;
        }
        public static Scene StartAi(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene, CardKind.Dodge));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 1, HumanRole = Role.Renegade, ModeId = Mode,
                UseInteractiveSetup = true, ConfirmOpeningHand = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new Scene(scene, game, registry); s.Accept(new StartGameCommand());
            for (var step = 0; step < 40 && s.P is not { PlayerSeat: 1, Kind: DecisionKind.SelectGeneral }; step++) s.Accept(new AdvanceOneStepCommand(game.Revision));
            s.Need(s.P is { PlayerSeat: 1, Kind: DecisionKind.SelectGeneral }, "The real human witness must receive its own general selection while the Lord is chosen by AI.");
            s.Accept(new SelectGeneralCommand(1, "fixture:mhg-peer-1", game.Revision, s.P!.PromptId));
            for (var step = 0; step < 150; step++)
            {
                if (s.P is { PlayerSeat: 1 } held && held.SkillPrompt?.SkillId == AiWitness &&
                    held.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "ai-witness") &&
                    s.E<ProgramBindingResolvedEvent>().Any(e => e.SkillId == Kurou && e.BindingId == "give-before-loss")) return s;
                s.Accept(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("The genuine AI did not reach its bounded first KuRou decision.");
        }
    }
}
