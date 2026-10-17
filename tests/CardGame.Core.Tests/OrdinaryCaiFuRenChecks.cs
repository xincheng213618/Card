using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class OrdinaryCaiFuRenChecks
{
    private const string Owner = "fixture:ordinary-cfr-owner", Mode = "identity:classic-ordinary-cfr";
    private const string Driver = "fixture:ordinary-cfr-driver", Quiet = "fixture:ordinary-cfr-quiet";
    private const string Peer = "fixture:ordinary-cfr-peer", Watch = "fixture:ordinary-cfr-watch";
    private const string Conditional = "fixture:ordinary-cfr-conditional";
    private const string Donation = "ol:xianzhou", Listen = "ol:qieting";
    private const string Given = "program.all-equipment-donation.give";
    private const string Moved = "skill-program.cai-fu-ren.qieting.move", Draw = "skill-program.cai-fu-ren.qieting.draw";

    public static void QietingActualTurnUseTargetsAndSingleChoiceCold()
    {
        foreach (var scene in new[] { "quiet", "self-use", "legacy", "actor", "dead-turn-owner" })
        {
            var s = Scenario.Start(scene);
            if (scene is "legacy" or "actor") { s.Use("stock"); s.Play(); }
            var ownTurn = s.G.State.TurnNumber;
            s.Need(!s.Optional(Listen), "Own-turn Play is not a Qieting ended-turn offer.");
            s.End();
            s.Reach(() => s.Optional(Listen) || s.E<TurnStartedEvent>().Any(e => e.ActorSeat == 2 && e.TurnNumber > ownTurn));
            var ended = s.E<TurnEndedEvent>().Last(e => e.ActorSeat == 1);
            var eligible = scene != "legacy";
            s.Need(s.Optional(Listen) == eligible, "Only the actual turn actor's final true Uses on other designated people block Qieting.");
            if (scene is "legacy" or "actor")
            {
                var declared = s.E<CardUseDeclaredEvent>().Single(e => e.SourceSeat == 1 && e.CardId == 0 && e.CardKind == CardKind.Slash);
                var actual = s.E<OrdinaryQietingActualUseEvent>().Last(e => e.NativeUseId == declared.ResolutionId);
                s.Need(actual.ActualTurn == ended.TurnNumber && actual.TurnOwnerSeat == 1 && actual.UsedOnAnother &&
                    (scene == "legacy" ? actual.ActorSeat == 1 : actual.ActorSeat != 1 &&
                        s.E<ProgramCardUseActorReplacedEvent>().Any(e => e.CardUseFrameId == declared.ResolutionId && e.PreviousActorSeat == 1 && e.ActorSeat == actual.ActorSeat)),
                    "A genuine legacy zero Slash and a real actor replacement retain the original actual-turn identity and latest actual user.");
            }
            if (!eligible) { s.Cold(); continue; }
            s.Activate();
            var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Listen);
            s.Need(root.OrdinaryQieting is { Paid: false } r && r.Qualification.ActualTurn == ended.TurnNumber &&
                r.Qualification.TurnOwnerSeat == 1 && !r.Qualification.UsedOnAnother &&
                s.P!.Choices is [{ Parameters: var parameters }] && parameters.GetValueOrDefault("option") == "draw",
                "The single private option freezes the just-ended actual turn, rather than the next actor's turn.");
            if (scene == "dead-turn-owner") s.Need(!s.V(1).IsAlive, "The actual turn owner really died before the ended-turn draw offer.");
            s.Private(0); s.Reject(); s.Cold();
            var old = s.P!; var hand = s.V().HandCount;
            s.Answer(c => s.Option(c) == "draw");
            s.Reach(() => !s.G.ResolutionStack.Any(f => f.Id == root.Id));
            s.Need(s.E<OrdinaryQietingOptionIssuedEvent>().Count(e => e.FrameId == root.Id && e.Option == "draw") == 1 &&
                s.E<OrdinaryQietingDrawReturnedEvent>().Single(e => e.FrameId == root.Id).ActualCount == 1 &&
                s.E<OrdinaryQietingCompletedEvent>().Count(e => e.FrameId == root.Id && e.Paid) == 1 && s.V().HandCount == hand + 1 &&
                !s.E<OrdinaryQietingEquipmentMovedEvent>().Any(e => e.FrameId == root.Id),
                "One ordinary Qieting binding draws exactly one native entity and cannot execute a second option.");
            s.RejectOld(old);
            if (scene == "quiet")
            {
                s.Reach(() => s.Optional(Listen)); s.Activate();
                var next = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Listen);
                s.Need(next.OrdinaryQieting!.Qualification.ActualTurn > ended.TurnNumber && next.OrdinaryQieting.Qualification.TurnOwnerSeat == 2,
                    "A separate genuine foreign actual turn receives its own single opportunity.");
            }
            s.Cold();
        }
        foreach (var scene in new[] { "own-dodge", "own-null", "own-played", "own-forced", "own-insert", "pijing-remove", "pijing-add" })
        {
            var s = Scenario.Start(scene); s.Use("stock"); s.Play();
            var turn = s.G.State.TurnNumber;
            if (scene == "own-dodge")
            {
                s.Use("poke", targets: [1]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
                var parent = s.G.ResolutionStack.OfType<CardUseFrame>().Last();
                var card = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
                s.Cold(); s.Answer(c => c.Cards.SequenceEqual([card])); s.Play();
                s.Need(parent.SourceSeat == 1 && s.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 0 && e.CardId == card && e.EffectiveCardKind == CardKind.Dodge),
                    "The actual turn actor really pays a native Slash-defense Dodge, rather than a pure played group response.");
            }
            else if (scene == "own-forced")
            {
                s.Use("poke", targets: [1]);
                s.Reach(() => s.P is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "request-slash" && c.Cards.Count == 1));
                var cost = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
                s.Cold(); s.Answer(c => c.Cards.SequenceEqual([cost])); s.Play();
                var declared = s.E<CardUseDeclaredEvent>().Single(e => e.CardId == cost && e.SourceSeat == 0);
                s.Need(s.E<OrdinaryQietingActualUseEvent>().Last(e => e.NativeUseId == declared.ResolutionId) is
                    { ActorSeat: 0, TurnOwnerSeat: 0, UsedOnAnother: true } &&
                    s.G.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                    s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == declared.ResolutionId) == 1,
                    "A foreign issuer's genuine request still makes the current turn actor use its own physical Slash on another person.");
            }
            else if (scene == "own-insert")
            {
                var insertion = s.E<ProgramPhaseScheduledEvent>().Single(e => e.SkillId == "fixture:ordinary-cfr-insert" && e.Started);
                s.Need(insertion.OwnerSeat == 0 && insertion.Phase == TurnPhase.Play &&
                    !s.E<ProgramPhaseScheduledEvent>().Any(e => e.FrameId == insertion.FrameId && !e.Started),
                    "The first actual Play prompt belongs to the true pre-preparation inserted phase.");
                s.Cold();
                s.PlayHand(s.V().Hand.First().Id, [1]); s.Play();
                s.End(); s.Play();
                s.Need(s.G.State.TurnNumber == turn && s.G.State.CurrentSeat == 0 &&
                    !s.E<TurnEndedEvent>().Any(e => e.TurnNumber == turn) &&
                    s.E<ProgramPhaseScheduledEvent>().Count(e => e.FrameId == insertion.FrameId && !e.Started && e.OwnerSeat == 0 && e.Phase == TurnPhase.Play) == 1 &&
                    s.E<ProgramBindingResolvedEvent>().Count(e => e.SkillId == insertion.SkillId && e.BindingId == insertion.BindingId && e.Completed) == 1,
                    "Ending the genuine inserted Play resumes normal Play inside the same actual turn; its earlier foreign-target Use is retained.");
            }
            else if (scene is "own-null" or "own-played")
            {
                s.Use("duel", targets: [1]);
                s.Reach(() => s.P is { PlayerSeat: 0 } p && p.Kind == (scene == "own-null" ? DecisionKind.Nullification : DecisionKind.RespondSlash));
                var card = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
                s.Cold(); s.Answer(c => c.Cards.SequenceEqual([card])); s.Play();
                s.Need(s.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 0 && e.CardId == card &&
                    e.EffectiveCardKind == (scene == "own-null" ? CardKind.Nullification : CardKind.Slash)),
                    "A real native Nullification Use or Duel played Slash pays its actual Hand material once.");
            }
            else
            {
                s.PlayHand(s.V().Hand.First().Id, [1]); s.Reach(() => s.Optional("ol:pijing")); s.Activate();
                s.Answer(c => c.Parameters.GetValueOrDefault("branch") == (scene == "pijing-remove" ? "remove" : "add") &&
                    c.Targets.SequenceEqual(scene == "pijing-remove" ? [1] : [3]));
                s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish"); s.Play();
                var applied = s.E<PijingTargetsAppliedEvent>().Single();
                var final = s.E<PijingTargetSlotEvent>().Where(e => e.FrameId == applied.FrameId && e.Final).OrderBy(e => e.Index).Select(e => e.Seat).ToArray();
                s.Need(final.SequenceEqual(scene == "pijing-remove" ? [] : new[] { 1, 3 }),
                    "The actual Pijing producer really removes or appends designated targets before Qieting evaluates the whole Use.");
            }
            s.End();
            s.Reach(() => s.Optional(Listen) || s.E<TurnStartedEvent>().Any(e => e.TurnNumber > turn));
            s.Need(s.Optional(Listen) == (scene is not ("pijing-add" or "own-forced" or "own-insert")),
                "Dodge/Nullification without other designated targets, pure Played, and a truly emptied final target list do not count as using on another.");
            if (s.Optional(Listen)) s.Private(1);
            s.Cold();
        }
        {
            var s = Scenario.Start("own-faction"); s.Use("stock"); s.Play();
            var provider = Enumerable.Range(1, 3).Single(i => s.V(i).GeneralId == "fixture:ordinary-cfr-peer-1");
            s.Need(s.V(provider).Role == Role.Loyalist && s.PublicGeneralFaction(provider) == "shu",
                "The real Shu Loyalist provider is derived from its selected public general, not from a guessed seat.");
            var material = s.V().Hand.First().Id;
            s.Use("give", [material], [provider]); s.Play();
            var request = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.UseProgramSkill && a.ProgramSkillId == "classic:jijiang");
            var target = new[] { 1, 3 }.First(i => i != provider);
            var turn = s.G.State.TurnNumber;
            s.Accept(new UseProgramSkillCommand(0, "classic:jijiang", request.ProgramActivationId!, [], [target], s.G.Revision, s.P!.PromptId));
            s.Reach(() => s.Hold("lord"));
            var use = s.G.ResolutionStack.OfType<CardUseFrame>().Last(f => f.SourceSeat == 0 && f.CardKind == CardKind.Slash);
            s.Need(use.Action is { Type: CardActionType.Use, ActorSeat: 0, RequesterSeat: 0 } a && a.ProviderSeat == provider &&
                a.PhysicalCards is [var cost] && cost.CardId == material && cost.From == CardLocation.Hand(provider) &&
                a.EffectiveDesignatedTargetSeats.SequenceEqual([target]) && s.V(provider).HandCount == 0 &&
                s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(provider) && m.To == CardLocation.Processing) == 1,
                "Actual allied supply plays one provider material, while the current Lord owns the separate true Slash Use and foreign designated target.");
            var id = use.Id; s.Private(0); s.Cold(); s.Continue(); s.Play();
            s.Need(s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == id) == 1 &&
                s.E<OrdinaryQietingActualUseEvent>().Last(e => e.NativeUseId == id) is { ActorSeat: 0, TurnOwnerSeat: 0, UsedOnAnother: true },
                "Whole native faction return retains the principal's true actual user; the supplier does not replace the Lord in the Qieting ledger.");
            s.End(); s.Reach(() => s.Optional(Listen) || s.E<TurnStartedEvent>().Any(e => e.TurnNumber > turn));
            s.Need(!s.Optional(Listen) && !s.E<OrdinaryQietingStartedEvent>().Any(e => e.Qualification.ActualTurn == turn),
                "The current Lord really used a supplied card on another person, so ordinary Qieting cannot trigger after that actual turn.");
            s.Cold();
        }
    }

    public static void QietingEmptySlotEquipmentMovementAndNativeChildrenCold()
    {
        foreach (var blocked in new[] { false, true })
        {
            var s = Scenario.Start(blocked ? "same-slot" : "equipment-child"); s.Use("stock-all"); s.Play();
            var cards = s.V().Hand.Where(c => c.Kind == CardKind.SilverLion).Take(2).Select(c => c.Id).ToArray();
            s.Use("place", [cards[0]], [1]); s.Play();
            if (blocked) { s.PlayHand(cards[1], []); s.Play(); }
            s.Use("hurt-peer", targets: [1]); s.Play();
            var hp = s.V(1).Hp;
            s.End(); s.Reach(() => s.Optional(Listen)); s.Activate();
            var f = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(p => p.SkillId == Listen);
            s.Need(s.P!.Choices.Any(c => s.Option(c) == "draw") &&
                s.P.Choices.Any(c => c.Cards.SequenceEqual([cards[0]]) && s.Option(c) == "equipment:" + cards[0]) == !blocked,
                "Only a genuinely empty usable equipment slot admits this original foreign equipment; ordinary Qieting cannot replace an occupied slot.");
            s.Private(0); s.Reject(); s.Cold();
            if (blocked)
            {
                s.Answer(c => s.Option(c) == "draw"); s.Reach(() => !s.G.ResolutionStack.Any(p => p.Id == f.Id));
                s.Need(s.V().Equipment.Any(c => c.Id == cards[1]) && s.V(1).Equipment.Any(c => c.Id == cards[0]) &&
                    !s.E<OrdinaryQietingEquipmentMovedEvent>().Any(), "Choosing the sole legal Draw option does not replace either real Silver Lion.");
            }
            else
            {
                s.Answer(c => c.Cards.SequenceEqual([cards[0]])); s.Reach(() => s.Hold("recovery"));
                var paid = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == f.Id).OrdinaryQieting;
                s.Need(paid is { Paid: true, Stage: OrdinaryQietingStage.MovementChildren } && s.V(1).Hp == hp + 1 &&
                    !s.E<OrdinaryQietingCompletedEvent>().Any(e => e.FrameId == f.Id),
                    "The exact foreign Silver Lion removal owns its real recovery child before the single equipment option completes.");
                s.Private(1); s.Cold(); s.Reach(() => !s.G.ResolutionStack.Any(p => p.Id == f.Id));
                s.Need(s.V().Equipment.Any(c => c.Id == cards[0]) && !s.V(1).Equipment.Any(c => c.Id == cards[0]) &&
                    s.E<OrdinaryQietingEquipmentMovedEvent>().Count(e => e.FrameId == f.Id && e.CardId == cards[0]) == 1 &&
                    s.E<OrdinaryQietingCompletedEvent>().Count(e => e.FrameId == f.Id && e.Paid) == 1 &&
                    !s.E<OrdinaryQietingDrawReturnedEvent>().Any(e => e.FrameId == f.Id),
                    "The original entity enters the previously empty slot once; native recovery and cold return cannot turn one choice into both options.");
            }
            s.Cold();
        }
    }

    public static void XianzhouWholeEquipmentGiftRecipientChoicesAndQuotaCold()
    {
        {
            var s = Scenario.Start("whole"); s.Use("stock-all"); s.Play();
            foreach (var kind in new[] { CardKind.Crossbow, CardKind.SilverLion, CardKind.WoodenOx })
            { s.PlayHand(s.V().Hand.First(c => c.Kind == kind).Id, []); s.Play(); }
            var grain = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id;
            s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], s.G.Revision, s.P!.PromptId)); s.Play();
            s.Use("wound"); s.Play();
            var equipment = s.V().Equipment.Select(c => c.Id).ToArray();
            s.Donate(1); s.Reach(() => s.Hold("recovery"));
            var paid = s.E<EquipmentDonationPaidEvent>().Single();
            var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == paid.ProgramFrameId);
            s.Need(paid.PaidCount == 3 && paid.ActualDeliveredCount == 3 && root.EquipmentDonation!.PaidCardIds.Order().SequenceEqual(equipment.Order()) &&
                equipment.All(id => s.V(1).Hand.Any(c => c.Id == id)) && s.V().Equipment.Count == 0 &&
                s.G.CardMovements.Count(m => m.CardId == grain && m.From == CardLocation.WoodenOxGrain(0) &&
                    m.To == CardLocation.DiscardPile && m.Reason.Value == "equipment.wooden-ox.grain-discard") == 1 &&
                !s.V(1).Hand.Any(c => c.Id == grain),
                "The whole original Equipment set is already delivered as X=3; the old Ox's separate grain is discarded, never transferred as a fourth material.");
            s.Need(s.E<RecoveryAppliedEvent>().Any(e => e.TargetSeat == 0 && e.Amount == 1) &&
                !s.E<EquipmentDonationBenefitIssuedEvent>().Any(), "Silver Lion's real paid recovery child precedes the recipient's benefit decision.");
            s.Cold(); s.Continue(); s.Reach(() => s.DonationPrompt());
            s.Need(s.P!.PlayerSeat == 1 && s.P.Choices.Any(c => s.Option(c) == "recover") && s.P.Choices.Any(c => s.Option(c) == "damage"),
                "The actual recipient, not the issuer, owns the private recover-or-damage choice.");
            s.Private(1); s.Reject(); s.Cold(); s.Reach(() => !s.G.ResolutionStack.Any(f => f.Id == root.Id));
            var damage = s.E<EquipmentDonationBenefitIssuedEvent>().Where(e => e.ProgramFrameId == root.Id && e.Benefit == "damage").ToArray();
            s.Need(damage.Length > 0 && damage.Length <= 3 && damage.Select(e => e.TargetSeat).Distinct().Count() == damage.Length &&
                damage.All(e => s.E<DamageAppliedEvent>().Any(d => d.SourceSeat == 1 && d.TargetSeat == e.TargetSeat && d.Amount == 1)) &&
                s.E<EquipmentDonationPaidEvent>().Count(e => e.ProgramFrameId == root.Id) == 1 &&
                equipment.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing && m.Reason.Value == Given) == 1) &&
                !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Donation),
                "The real recipient deals one sourced damage per distinct in-range choice, returns once, and cannot reopen the paid limited skill.");
            s.Cold();
        }
        foreach (var scene in new[] { "recipient-recover", "recipient-zero" })
        {
            var s = Scenario.Start(scene);
            var donor = Enumerable.Range(1, 3).Single(seat => s.V(seat).GeneralId == "fixture:ordinary-cfr-donor");
            s.Need(s.V(donor).Role == Role.Loyalist, "A real allied AI donor, derived from its chosen public general, will choose the human Lord recipient.");
            s.Use("gear", targets: [donor]); s.Play();
            var card = s.V(donor).Equipment.Single().Id; var full = s.V(donor).Hp;
            s.End(); s.Reach(() => s.DonationPrompt() && s.P!.PlayerSeat == 0);
            var paid = s.E<EquipmentDonationPaidEvent>().Single(); var frame = paid.ProgramFrameId;
            s.Need(paid.Source.OwnerSeat == donor && paid.RecipientSeat == 0 && paid.ActualDeliveredCount == 1 && s.V().Hand.Any(c => c.Id == card),
                "A real AI Play activation delivers its sole actual equipment before the human recipient chooses.");
            s.Private(0); s.Reject(); s.Cold(); var old = s.P!;
            if (scene == "recipient-recover") s.Answer(c => s.Option(c) == "recover");
            else
            {
                s.Answer(c => s.Option(c) == "damage");
                s.Need(s.P!.Choices.Any(c => s.Option(c) == "confirm" && c.Targets.Count == 0),
                    "The real recipient may confirm zero damage targets instead of being forced to fill X.");
                s.RejectOld(old); s.Cold(); s.Answer(c => s.Option(c) == "confirm");
            }
            s.Reach(() => !s.G.ResolutionStack.Any(f => f.Id == frame));
            s.Need(s.E<EquipmentDonationPaidEvent>().Count(e => e.ProgramFrameId == frame) == 1 &&
                s.E<SkillUsageConsumedEvent>().Count(e => e.SkillOwnerSeat == donor && e.SkillId == Donation && e.Scope == SkillUsageScope.Game) == 1 &&
                (scene == "recipient-recover" ? s.V(donor).Hp == full && s.E<EquipmentDonationBenefitIssuedEvent>().Count(e => e.ProgramFrameId == frame && e.Benefit == "recover") == 1 &&
                    !s.E<RecoveryAppliedEvent>().Any(e => e.TargetSeat == donor && e.Amount > 0) : !s.E<EquipmentDonationBenefitIssuedEvent>().Any(e => e.ProgramFrameId == frame)),
                "Full-health recovery is a legal zero actual recovery, while zero-target damage issues none; both still consume exactly one whole donation.");
            s.Cold();
        }
    }

    public static void XianzhouPaidSourceLossDeathsPrivacyAndReplayCold()
    {
        foreach (var scene in new[] { "source-loss", "owner-death", "recipient-death" })
        {
            var s = Scenario.Start(scene); s.Use("stock"); s.Play();
            s.PlayHand(s.V().Hand.First().Id, []); s.Play();
            var original = s.V().Equipment.Single().Id;
            s.Donate(1); s.Reach(() => s.Hold("cost"));
            var paid = s.E<EquipmentDonationPaidEvent>().Single();
            s.Need(paid.PaidCount == 1 && paid.ActualDeliveredCount == 1 && s.V(1).Hand.Any(c => c.Id == original) &&
                !s.E<EquipmentDonationBenefitIssuedEvent>().Any(),
                "The exact paid equipment and delivered X exist before the true loss/death observer; no benefit is issued early.");
            s.Private(s.P!.PlayerSeat); s.Reject(); s.Cold();
            if (s.P!.PlayerSeat == 0) s.Continue();
            else s.Accept(new AdvanceOneStepCommand(s.G.Revision));
            if (scene != "recipient-death")
            {
                s.Reach(() => s.DonationPrompt() || scene == "owner-death" && !s.G.ResolutionStack.Any(f => f.Id == paid.ProgramFrameId));
                s.Need((s.DonationPrompt() && s.P!.PlayerSeat == 1 || scene == "owner-death" &&
                    s.E<EquipmentDonationBenefitIssuedEvent>().Any(e => e.ProgramFrameId == paid.ProgramFrameId && e.RecipientSeat == 1 && e.Benefit == "damage")) &&
                    (scene == "source-loss" ? !s.V().Skills!.Any(k => k.ContentId == Donation) : !s.V().IsAlive),
                    "Already-paid source loss or actual issuer death does not let the issuer steal or cancel the living recipient's choice.");
                s.Cold();
            }
            s.Reach(() => !s.G.ResolutionStack.Any(f => f.Id == paid.ProgramFrameId));
            s.Need(s.E<EquipmentDonationPaidEvent>().Count(e => e.ProgramFrameId == paid.ProgramFrameId) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == original && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing && m.Reason.Value == Given) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == original && m.From == CardLocation.Processing && m.To == CardLocation.Hand(1) && m.Reason.Value == Given) == 1 &&
                s.E<ProgramSkillResolvedEvent>().Count(e => e.FrameId == paid.ProgramFrameId && e.SkillId == Donation && e.Completed) == 1,
                "Frozen payment, limited consumption, physical ownership and the native return survive cold loss/death without repaying a cost.");
            if (scene == "recipient-death")
                s.Need(!s.V(1).IsAlive && !s.E<EquipmentDonationBenefitIssuedEvent>().Any() && s.P?.PlayerSeat != 1,
                    "Actual recipient death drains the paid subtree but cannot publish a dead recipient's fresh benefit menu.");
            else
                s.Need(s.E<EquipmentDonationBenefitIssuedEvent>().Any(e => e.ProgramFrameId == paid.ProgramFrameId && e.RecipientSeat == 1 && e.Benefit == "damage") &&
                    s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 1),
                    "The living recipient retains its already-paid damage branch and is the actual native damage source.");
            s.Cold();
        }
    }

    private sealed class Scenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public string? PublicGeneralFaction(int seat) => registry.Generals[V(seat).GeneralId].FactionId;
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option");
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public bool DonationPrompt() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "equipment-donation") == true;
        public bool Optional(string id) => P?.SkillPrompt?.SkillId == id && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Donate(int recipient) => Accept(new UseProgramSkillCommand(0, Donation, "all-equipment-donation", [], [recipient], G.Revision, P!.PromptId));
        public void PlayHand(int id, int[] targets)
        {
            var action = G.GetHumanLegalActions().First(a => a.CardId == id && a.TargetSeats.SequenceEqual(targets) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast);
            Accept(new PlayCardCommand(0, id, targets, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void Answer(Func<PromptChoice, bool> match)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(match);
            Need(p.PlayerSeat == 0 && c is not null, "Only the real human can select a currently published choice.");
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
        }
        public void Private(int seat) => Need(P is { IsPrivate: true } p && p.PlayerSeat == seat &&
            Enumerable.Range(0, 4).Where(i => i != seat).All(i => G.CreateSnapshot(i).PendingDecision is null),
            "Each native decision and frozen target choice is visible only to its actual chooser.");
        public void Reject()
        {
            var p = P!; var state = State();
            Need(!G.Submit(new AnswerPromptCommand(p.PlayerSeat == 0 ? 1 : 0, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == state &&
                !G.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("ordinary-cfr-unpublished"), G.Revision)).Accepted && State() == state,
                "Foreign or unpublished input must reject without altering source identity, payments, private views or continuation cursors.");
        }
        public void RejectOld(PendingDecision old)
        {
            var state = State();
            Need(!G.Submit(new AnswerPromptCommand(0, old.PromptId, old.Choices[0].Id, G.Revision)).Accepted && state == State(),
                "A genuine previous private prompt cannot answer a later decision or replay a paid option.");
        }
        public void Cold()
        {
            var state = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(state == State(), "Cold JSON replay preserves all four private views, exact physical invoices, source instances and once-only native returns.");
        }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 170; i++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The small real-command fixture ended before its requested boundary.");
                var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == Watch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification)
                    Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not ("faction-slash-request" or "faction-defense-request"));
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else throw new InvalidOperationException("Unexpected native prompt " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + Diagnostic());
        }
        public void Accept(GameCommand command)
        {
            try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(result)); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " cause=" + e.Message + " " + Diagnostic(), e); }
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new
        {
            Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P,
            Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).GeneralId, V(i).Role, V(i).Hp, V(i).MaxHp, V(i).IsAlive, V(i).HandCount, V(i).Equipment }),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Uses = E<OrdinaryQietingActualUseEvent>(), Qieting = E<OrdinaryQietingStartedEvent>(), Donations = E<EquipmentDonationPaidEvent>(),
            Benefits = E<EquipmentDonationBenefitIssuedEvent>(), Damage = E<DamageAppliedEvent>(), Deaths = E<PlayerDiedEvent>(),
            Resolved = E<ProgramSkillResolvedEvent>(), Bindings = E<ProgramBindingResolvedEvent>()
        });
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands)
        });
        public static Scenario Start(string scene)
        {
            ContentRegistry r;
            try { r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 7, PlayerCount = 4, ModeId = Mode, HumanSeat = 0, HumanRole = scene == "owner-death" ? Role.Renegade : Role.Lord,
                UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, r);
            var s = new Scenario(scene, g, r); s.Accept(new StartGameCommand());
            s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, Owner, g.Revision, s.P!.PromptId)); s.Play(); return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-cai-fu-ren", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            Production(b, "ol-cai-fu-ren", Donation, Listen);
            var peerListen = scene.StartsWith("own-", StringComparison.Ordinal) || scene.StartsWith("pijing-", StringComparison.Ordinal);
            var aiDonor = scene is "recipient-recover" or "recipient-zero";
            var faction = scene == "own-faction";
            if (faction) CharacterFixtureContent.Register(b, "classic:liu-bei");
            if (scene.StartsWith("pijing-", StringComparison.Ordinal)) Production(b, "ol-liu-pan", "ol:pijing");
            static JsonNode N(string json) => JsonNode.Parse(json)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0, string? more = null) =>
                N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards +
                    ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," +
                    (more ?? "") + "\"effects\":[" + effects + "]}");
            var duel = A("duel", """{"op":"useSelectedActorDuel","target":"owner"}""", targets: 1).AsObject();
            duel["targetKind"] = "otherLiving";
            duel["usesPerPhase"] = 2;
            var defs = new JsonArray(new JsonObject
            {
                ["id"] = Driver, ["revision"] = 1,
                ["modifiers"] = N("""[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0},{"id":"slashes","query":"slashLimit","operation":"add","value":4,"priority":0}]"""),
                ["activations"] = new JsonArray(
                    A("stock", """{"op":"draw","target":"owner","amount":8}"""), A("stock-all", """{"op":"draw","target":"owner","amount":20}"""),
                    A("wound", """{"op":"loseHp","target":"owner","amount":2}"""), A("hurt-peer", """{"op":"loseHp","target":"selectedTarget","amount":2}""", targets: 1),
                    A("poke", """{"op":"damage","target":"selectedTarget","amount":1}""", targets: 1),
                    A("give", """{"op":"giveSelected","target":"selectedTarget"}""", 1, 1),
                    A("gear", """{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}""", targets: 1),
                    A("place", """{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}""", 1, 1,
                        "\"sourceZones\":[\"hand\"],\"cardCategories\":[\"equipment\"],"), duel)
            });
            defs.Add(N($$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""));
            if (scene is "legacy" or "actor" or "own-dodge" or "own-forced")
            {
                var afterDamage = scene is "own-dodge" or "own-forced";
                var window = afterDamage ? "afterDamageApplied" : "turnStartBeforeNormalFlow";
                var targetKind = afterDamage ? "eventSource" : "otherLivingHighestHand";
                var occurrence = afterDamage ? "\"damageOccurrence\":\"perDamage\"," : "";
                var use = scene == "own-forced" ? "{\"op\":\"requestSlashByTarget\",\"target\":\"selectedTarget\",\"resultBind\":\"requested\"}" :
                    scene == "own-dodge" ? "{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}" :
                    "{\"op\":\"useVirtualCard\",\"target\":\"selectedTarget\",\"outputKind\":\"slash\",\"targetRestriction\":\"distanceUnlimitedAgainstTarget\",\"useCardActionWindows\":" + (scene == "actor" ? "true" : "false") + "}";
                defs.Add(N($$"""{"id":"{{Peer}}","revision":1,"triggers":[{"id":"real-zero-slash","window":"{{window}}","subject":"owner",{{occurrence}}"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"{{targetKind}}"},{{use}}]}]}"""));
            }
            if (scene == "actor") defs.Add(N("""{"id":"fixture:ordinary-cfr-role","revision":1,"triggers":[{"id":"actual-replacement","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]}"""));
            if (scene == "self-use") defs.Add(N($$"""{"id":"{{Peer}}","revision":1,"triggers":[{"id":"real-self-use","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"owner"},"outputKind":"drawTwo"}]}]}"""));
            if (scene == "dead-turn-owner") defs.Add(N($$"""{"id":"{{Peer}}","revision":1,"triggers":[{"id":"real-ending-death","window":"turnEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]}"""));
            if (scene == "own-insert") defs.Add(N("""{"id":"fixture:ordinary-cfr-insert","revision":1,"triggers":[{"id":"real-preparation-extra-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}"""));
            if (scene == "source-loss") defs.Add(N($$"""{"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"live-donation","skillId":"{{Donation}}","minimumHp":13}]}"""));
            var watch = new JsonArray();
            if (scene is "whole" or "equipment-child") watch.Add(N("""{"id":"actual-lion-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"recovery","options":[{"id":"recovery"}]}]}"""));
            if (scene is "source-loss" or "owner-death") watch.Add(N($$"""{"id":"actual-paid-source","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{Given}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost","options":[{"id":"cost"}]},{"op":"loseHp","target":"owner","amount":{{(scene == "source-loss" ? 1 : 20)}}}]}"""));
            if (scene == "recipient-death") watch.Add(N($$"""{"id":"actual-paid-recipient","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Given}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost","options":[{"id":"cost"}]},{"op":"loseHp","target":"owner","amount":20}]}"""));
            if (faction) watch.Add(N("""{"id":"real-supplied-lord-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"lord","options":[{"id":"lord"}]}]}"""));
            if (watch.Count > 0) defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watch });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new
            {
                name = n!["id"]!.GetValue<string>(), description = "普通蔡夫人的实际回合与原生装备付款边界",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray())
                    .SelectMany(a => a!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray())
                    .Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id)
            });
            var programs = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in programs.Programs) b.AddSkill(new(id, id, "真实小夹具") { Program = p, ProgramPresentation = programs.Presentations[id] });
            b.AddSkill(new("fixture:ordinary-cfr-own-pick", "本人", "固定公开真人选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            var own = new List<string> { Driver };
            if (!peerListen && !aiDonor) { own.Add(Listen); own.Add(scene == "source-loss" ? Conditional : Donation); }
            if (scene.StartsWith("pijing-", StringComparison.Ordinal)) own.Add("ol:pijing");
            if (scene == "own-insert") own.Add("fixture:ordinary-cfr-insert");
            if (faction) own.Add("classic:jijiang");
            if (watch.Count > 0 && scene != "recipient-death") own.Add(Watch);
            b.AddGeneral(new(Owner, "普通蔡夫人原生规则拥有者", "supporter", "fixture:ordinary-cfr-own-pick", faction ? "shu" : "qun", 12, own, GeneralGender.Female));
            var peers = Enumerable.Range(1, 3).Select(i => aiDonor && i == 1 ? "fixture:ordinary-cfr-donor" : "fixture:ordinary-cfr-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pick = "fixture:ordinary-cfr-pick-" + i;
                b.AddSkill(new(pick, "真实其他角色", "只固定实际公开角色") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role,
                    role => (aiDonor || faction) && i == 1 ? role == Role.Loyalist ? 100000000d : -100000000d : 1000000d * (4 - i)) });
                var skills = new List<string> { Quiet };
                if (i == 1)
                {
                    if (peerListen) skills.Add(Listen);
                    if (aiDonor) { skills.Remove(Quiet); skills.Add(Donation); }
                    if (scene is "legacy" or "actor" or "own-dodge" or "own-forced" or "self-use" or "dead-turn-owner") skills.Add(Peer);
                    if (scene == "self-use") skills.Remove(Quiet);
                    if (scene == "actor") skills.Add("fixture:ordinary-cfr-role");
                }
                if (scene is "equipment-child" or "recipient-death") skills.Add(Watch);
                b.AddGeneral(new(peers[i - 1], "实际其他角色", "supporter", pick, faction && i == 1 ? "shu" : "wei", 12, skills));
            }
            var kind = scene switch { "own-dodge" => CardKind.Dodge, "own-null" => CardKind.Nullification,
                "source-loss" or "owner-death" or "recipient-death" or "recipient-recover" or "recipient-zero" => CardKind.Crossbow, _ => CardKind.Slash };
            var kinds = scene is "whole" or "equipment-child" or "same-slot" ? new[] { CardKind.Crossbow, CardKind.SilverLion, CardKind.WoodenOx, CardKind.Slash } : [kind];
            foreach (var k in kinds) b.AddCard(new("fixture:ordinary-cfr-card-" + k, "真实" + k, CardCatalog.Get(k).CategoryName, "原生印刷实体", k));
            var physical = kinds.Length == 1 ? Enumerable.Range(0, 40).Select(_ => new ContentDeckPhysicalCard("fixture:ordinary-cfr-card-" + kind, Suit.Club, 7)).ToArray() :
                kinds.SelectMany(k => Enumerable.Range(0, k == CardKind.Slash ? 8 : 4).Select(_ => new ContentDeckPhysicalCard("fixture:ordinary-cfr-card-" + k, Suit.Club, 7))).ToArray();
            b.AddDeck(new("fixture:ordinary-cfr-deck", "固定小真实实体牌堆", 0, 0, []) { PhysicalCards = physical });
            var roles = aiDonor || faction ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 } :
                scene == "owner-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } :
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "普通蔡夫人真实原生边界", 4, 4, roles, "fixture:ordinary-cfr-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
        private static void Production(IContentRegistryBuilder b, string bundle, params string[] ids)
        {
            string Read(string suffix)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json")
                    ?? throw new InvalidOperationException("Missing production skill resource " + bundle + "." + suffix);
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var id in ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
        }
    }
}
