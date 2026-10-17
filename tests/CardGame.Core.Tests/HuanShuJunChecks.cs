using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class HuanShuJunChecks
{
    private const string Huanpei = "ol:huanpei", Wenyi = "ol:wenyi", Miaoxin = "ol:miaoxin";
    private const string Owner = "fixture:hsj-owner", Driver = "fixture:hsj-driver", Quiet = "fixture:hsj-quiet", Watch = "fixture:hsj-watch";
    private const string Conditional = "fixture:hsj-conditional", Regrant = "fixture:hsj-regrant", Incoming = "fixture:hsj-incoming";
    private const string Supply = "fixture:hsj-peach-supplier";
    private const string Mode = "identity:classic-huan-shu-jun";
    private const string AdjustDraw = "skill-program.huan-shu-jun.huanpei.draw", AdjustDiscard = "skill-program.huan-shu-jun.huanpei.discard";
    private const string MiaoxinDraw = "skill-program.huan-shu-jun.miaoxin.draw", Gift = "skill-program.huan-shu-jun.wenyi.give";
    private const string Take = "skill-program.sun-hong.zenrun.obtain", Compensation = "skill-program.sun-hong.zenrun.compensation";

    public static void HuanpeiRoundAdjustmentTrueBasicUsesAndNativeResponsesCold()
    {
        foreach (var (scene, kind, retain) in new[] { ("adjust-draw", CardKind.FireSlash, 1), ("adjust-discard", CardKind.ThunderSlash, -1), ("equal", CardKind.Slash, 4), ("peach", CardKind.Peach, -1), ("alcohol", CardKind.Alcohol, 4) })
        {
            var s = Scenario.Start(scene);
            if (retain >= 0) s.Trim(retain);
            if (kind == CardKind.Peach) { s.Use("lose-one"); s.Play(); }
            var beforeHand = s.V().HandCount; var hp = s.V().Hp; var sequence = s.Sequence;
            var id = s.BeginHuanpei(); s.Private(0); s.Reject(); s.Cold(); s.PayAdjustment();
            s.Reach(() => s.Branch("basic-use"));
            var paid = s.E<HuanpeiAdjustmentAcceptedEvent>().Single(e => e.FrameId == id);
            var returned = s.E<HuanpeiAdjustmentCompletedEvent>().Single(e => e.FrameId == id);
            s.Need(paid.HandCount == beforeHand && paid.Hp == hp && paid.RequestedCount == Math.Abs(beforeHand - hp) && s.V().HandCount == hp &&
                returned.ActualDrawCount == Math.Max(0, hp - beforeHand) && returned.ReplacementCount == 0 &&
                s.E<HuanpeiCostEntityEvent>().Count(e => e.FrameId == id) == Math.Max(0, beforeHand - hp),
                "The accepted adjustment pays the actual Hand-to-current-HP difference, including a genuine zero adjustment.");
            s.Need(!s.Branch("cancel"), "After payment, an available genuine basic output must be chosen rather than canceled.");
            s.Need(!s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("kind") == nameof(CardKind.Dodge)) &&
                (kind == CardKind.Peach || !s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("kind") == nameof(CardKind.Peach))),
                "The post-payment menu keeps native play legality: no proactive Dodge or full-health Peach.");
            IReadOnlyList<int> basicTargets = (kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)
                ? new[] { 1 } : Array.Empty<int>();
            s.Cold(); s.Basic(kind, basicTargets);
            s.Reach(() => s.Hold("completed")); var issued = s.E<HuanpeiIssuedEvent>().Single(e => e.PaymentFrameId == id);
            var action = s.Action(issued.ActionId);
            s.AssertZero(action, kind); s.Need(issued.Intent == HuanpeiIntent.Play && !s.E<HuanpeiReturnedEvent>().Any(e => e.PaymentFrameId == id),
                "The real native completed-use observer still owns the paid zero-entity use before its typed return.");
            s.Cold(); s.Continue(); s.Play();
            s.Need(s.E<HuanpeiReturnedEvent>().Count(e => e.PaymentFrameId == id && e.ActionId == action.ActionId) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId) == 1 &&
                !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Huanpei) &&
                !s.G.CardMovements.Any(m => m.Sequence > sequence && m.CardId == 0),
                "One native use returns once, spends the owner-wide round allowance and never moves an invented physical card.");
            if (kind == CardKind.Peach) s.Need(s.V().Hp == hp + 1 && s.E<RecoveryAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 0), "The genuine zero Peach performs native recovery.");
            if (kind == CardKind.Alcohol)
            {
                s.Need(s.V().HasAlcoholEffect, "The actual zero Alcohol grants its ordinary native effect.");
                s.End(); s.Reach(() => s.E<AlcoholExpiredEvent>().Any(e => e.PlayerSeat == 0)); s.Need(!s.V().HasAlcoholEffect, "The native Alcohol expires at the actual turn boundary.");
            }
            if (scene == "equal")
            {
                var source = s.E<HuanShuJunStartedEvent>().Single(e => e.FrameId == id).Source;
                s.Use("replace-source"); s.Play(); s.Use("grant", skill: Regrant); s.Play();
                s.Need(s.Has(Huanpei) && !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Huanpei), "A genuinely lost and newly granted source cannot refresh this owner's already-paid current-round quota.");
                var round = paid.Round; s.End(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && s.E<RoundStartedEvent>().Last().RoundNumber > round);
                var next = s.BeginHuanpei(); s.PayAdjustment(); s.Reach(() => s.Branch("basic-use")); s.Basic(CardKind.Alcohol, []); s.Play();
                s.Need(s.E<HuanShuJunStartedEvent>().Single(e => e.FrameId == next).Source.SkillInstanceId != source.SkillInstanceId &&
                    s.E<HuanpeiAdjustmentAcceptedEvent>().Single(e => e.FrameId == next).Round > round && s.E<HuanpeiReturnedEvent>().Count(e => e.PaymentFrameId == next) == 1,
                    "The next genuine round admits the new source exactly once without rewriting the old round's receipt.");
            }
            s.Cold();
        }
        {
            var s = Scenario.Start("dodge"); s.End(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
            var native = s.P!; var incoming = s.G.ResolutionStack.OfType<CardUseFrame>().Last(); var damage = s.E<DamageAppliedEvent>().Length;
            s.Need(s.G.State.CurrentSeat != 0 && s.OpenHuanpei(CardKind.Dodge), "A real foreign native Slash offers Huanpei only for its true Dodge Use.");
            s.Cold(); s.Answer(c => c.Parameters.ContainsKey("huanpei-open") && c.Parameters.GetValueOrDefault("output-kind") == nameof(CardKind.Dodge));
            var id = s.Own().Id; s.PayAdjustment(); s.Reach(() => s.Hold("completed")); var issued = s.E<HuanpeiIssuedEvent>().Single(e => e.PaymentFrameId == id);
            var action = s.Action(issued.ActionId); s.AssertZero(action, CardKind.Dodge);
            s.Need(issued.Intent == HuanpeiIntent.OwnSlashDodge && action.Type == CardActionType.Response && action.ResponderSeat == 0 && action.ParentActionId == incoming.Action!.ActionId &&
                s.E<DamageAppliedEvent>().Length == damage, "The actual Dodge binds to the unchanged incoming Slash and prevents damage before either native parent returns.");
            s.Private(0); s.Cold(); s.Continue(); s.Reach(() => s.E<HuanpeiReturnedEvent>().Any(e => e.PaymentFrameId == id)); s.RejectOld(native);
            s.Need(s.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 0 && e.EffectiveCardKind == CardKind.Dodge) && s.E<HuanpeiAdjustmentAcceptedEvent>().Count(e => e.FrameId == id) == 1, "A completed true Dodge and cold return do not repay its adjustment."); s.Cold();
        }
        foreach (var kind in new[] { CardKind.Peach, CardKind.Alcohol })
        {
            var s = Scenario.Start("dying-" + kind); s.Use("self-dying"); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
            var dying = s.G.ResolutionStack.OfType<DyingFrame>().Single(); s.Need(s.OpenHuanpei(kind), "The genuine self-rescue publishes the required true-use basic kind.");
            s.Cold(); s.Answer(c => c.Parameters.ContainsKey("huanpei-open") && c.Parameters.GetValueOrDefault("output-kind") == kind.ToString());
            var id = s.Own().Id; s.PayAdjustment(); s.Reach(() => s.Hold("completed")); var issued = s.E<HuanpeiIssuedEvent>().Single(e => e.PaymentFrameId == id);
            s.AssertZero(s.Action(issued.ActionId), kind); s.Need(issued.Intent == HuanpeiIntent.Dying && s.V().Hp > 0 && !s.E<DyingResolvedEvent>().Any(e => e.ResolutionId == dying.Id), "The zero self-rescue applies native recovery inside the original unfinished dying tree.");
            s.Cold(); s.Continue(); s.Play(); s.Need(s.E<DyingResolvedEvent>().Count(e => e.ResolutionId == dying.Id && e.Survived) == 1 && s.E<HuanpeiReturnedEvent>().Count(e => e.PaymentFrameId == id) == 1 && !s.V().HasAlcoholEffect,
                "Both actual self-Peach and self-Alcohol return once; dying Alcohol does not fabricate a play-phase wine bonus."); s.Cold();
        }
        foreach (var scene in new[] { "played-duel", "played-arrows" })
        {
            var s = Scenario.Start(scene);
            if (scene == "played-duel") s.Use("duel", targets: [1]); else s.End();
            s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash or DecisionKind.RespondDodge });
            s.Need(!s.P!.Choices.Any(c => c.Parameters.ContainsKey("huanpei-open")), "Duel and AOE pure-played requests cannot borrow Huanpei's true-Use permission.");
            var material = s.P.Choices.First(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("conversion-skill-id") == Driver).Cards.Single();
            s.Cold(); s.Answer(c => c.Cards.SequenceEqual([material]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == Driver);
            s.Reach(() => scene == "played-duel" ? s.E<DuelResponseEvent>().Any(e => e.ResponderSeat == 0 && e.SlashCardId == material && e.UsedSlash) : s.E<GroupResponseEvent>().Any(e => e.ResponderSeat == 0 && e.ResponseCardId == material && e.UsedResponse));
            s.Need(s.E<HuanpeiAdjustmentAcceptedEvent>().Length == 0 && s.E<HuanpeiIssuedEvent>().Length == 0, "The real physical played response neither consumes round quota nor invents a Huanpei Use."); s.Cold();
        }
    }

    public static void WenyiPeachAndGiftedEquipmentUseRestoreChildrenCold()
    {
        foreach (var scene in new[] { "wenyi-loss", "wenyi-damage", "wenyi-native-slash" })
        {
            var s = Scenario.Start(scene); s.States(1); var before = s.V(1).Hp;
            int? slashId = null;
            if (scene == "wenyi-native-slash")
            {
                s.Use("stock"); s.Play();
                s.Need(s.V().Hand.Any(c => c.Kind == CardKind.Slash) && s.V().Hand.Any(c => c.Kind == CardKind.Peach),
                    "The fixed 26-entity deck gives the actual user both a natural Slash and a separate natural Peach.");
                slashId = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id;
                s.PlayCard(slashId.Value, [1]);
            }
            else if (scene == "wenyi-damage") s.Use("peer-damage-two", targets: [1]);
            else s.ToOne(1);
            s.OfferWenyi();
            var health = s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
            s.Need(health.Change.TargetSeat == 1 && health.Change.HpBefore == before && before != 1 && health.Change.HpAfter == 1 &&
                health.Change.Kind == (scene is "wenyi-damage" or "wenyi-native-slash" ? HpChangeKind.Damage : HpChangeKind.Loss), "Wenyi observes the real non-one-to-one HP delta, including foreign damage rather than only its own HP events.");
            s.Cold(); s.Activate(); s.Reach(() => s.Branch("wenyi-peach")); s.Private(0); s.Reject();
            var id = s.Own().Id; var card = s.V().Hand.First(c => c.Kind == CardKind.Peach);
            s.BranchAnswer("wenyi-peach"); s.Reach(() => s.P!.Choices.Any(c => c.Cards.SequenceEqual([card.Id]))); s.Answer(c => c.Cards.SequenceEqual([card.Id]));
            s.Reach(() => s.Hold("recovery")); var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.WenyiUse?.ProgramFrameId == id);
            s.Need(use.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.Peach } action && action.PhysicalCards is [var cost] && cost.CardId == card.Id && cost.From == CardLocation.Hand(0) && action.EffectiveDesignatedTargetSeats.SequenceEqual([1]) &&
                s.V(1).Hp == 2 && s.V(1).IsFaceDown && s.V(1).IsChained && !s.E<WenyiNativeReturnedEvent>().Any(e => e.FrameId == id) && !s.E<WenyiRestorationIssuedEvent>().Any(e => e.FrameId == id),
                "The genuine physical Peach recovers the frozen non-dying target; native recovery descendants finish before return and restoration.");
            long? originalUseId = null;
            if (slashId is { } physicalSlash)
            {
                var originalUse = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == physicalSlash);
                originalUseId = originalUse.Id;
                var damage = s.G.ResolutionStack.OfType<DamageFrame>().Single(d => d.ParentFrameId == originalUse.Id);
                s.Need(originalUse.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0,
                        EffectiveKind: CardKind.Slash, PhysicalCards: [var slashCost] } && slashCost.CardId == physicalSlash &&
                    slashCost.From == CardLocation.Hand(0) && damage.WenyiHealth is { } parent && parent.HealthChangeId == health.Id &&
                    s.G.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.Processing).Select(c => c.CardId).SequenceEqual([physicalSlash]) &&
                    s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == card.Id).Location == CardLocation.DiscardPile &&
                    s.G.CardMovements.Count(m => m.CardId == card.Id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
                    s.G.CardMovements.Count(m => m.CardId == card.Id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1 &&
                    !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == originalUse.Id || e.ResolutionId == use.Id) &&
                    !s.E<WenyiDamageHealthReturnedEvent>().Any(e => e.Receipt.DamageFrameId == damage.Id),
                    "The original real Slash stays in Processing; the separate Peach has paid and finished its physical movement once, while both whole Uses and the exact HP-one recovery child remain unfinished.");
            }
            s.Private(1); s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision)); s.Reach(() => s.Hold("face"));
            s.Need(!s.V(1).IsFaceDown && s.V(1).IsChained && s.E<WenyiNativeReturnedEvent>().Count(e => e.FrameId == id) == 1, "Face-up restoration follows the whole native Peach, while the original chain remains until its distinct next child.");
            s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision)); s.Play();
            s.Need(!s.V(1).IsChained && s.E<WenyiPaidEvent>().Count(e => e.FrameId == id && e.Branch == "peach") == 1 && s.E<WenyiRestorationIssuedEvent>().Where(e => e.FrameId == id).Select(e => e.FaceState).SequenceEqual([true, false]) && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) == 1,
                "Physical payment, complete native use, face-up and unchain restoration each occur once.");
            if (originalUseId is { } originalId)
                s.Need(s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == originalId) == 1 &&
                    s.E<WenyiDamageHealthReturnedEvent>().Count(e => e.Receipt.AttackFrameId == originalId) == 1 &&
                    s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1 &&
                    s.G.CardMovements.Count(m => m.CardId == slashId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1,
                    "The same real Slash resumes after the cold Peach and restoration children, applying and returning its original one-point damage only once.");
            s.Use("peer-one-less", targets: [1]); s.Play(); s.Need(s.E<WenyiPaidEvent>().Length == 1, "Another genuine HP change to one cannot reuse the spent game allowance without Miaoxin."); s.Cold();
        }
        foreach (var scene in new[] { "wenyi-lion", "wenyi-ox" })
        {
            var s = Scenario.Start(scene); var equipment = s.V().Hand[0]; s.PlayCard(equipment.Id, []); s.Play();
            int? oldEquipment = null; int? grain = null;
            if (scene == "wenyi-lion")
            {
                oldEquipment = s.V().Hand[0].Id; s.Use("place", [oldEquipment.Value], [1]); s.Play(); s.Use("lose-two"); s.Play();
            }
            else { grain = s.V().Hand[0].Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Value], [], s.G.Revision, s.P!.PromptId)); s.Play(); }
            s.States(1); s.ToOne(1); s.OfferWenyi(); s.Activate(); s.Reach(() => s.Branch("wenyi-equipment"));
            var id = s.Own().Id; var hp = s.V().Hp; s.BranchAnswer("wenyi-equipment", equipment.Id); s.Reach(() => s.Hold(scene == "wenyi-lion" ? "recovery" : "spill"));
            s.Need(s.Own(id).HuanShuJun is { Paid: true, Stage: HuanShuJunStage.EquipmentGiftChildren } r && r.Materials is [var original] && original.CardId == equipment.Id && original.From == CardLocation.Equipment(0) &&
                s.G.CardMovements.Count(m => m.CardId == equipment.Id && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Hand(1) && m.Reason.Value == Gift) == 1 && !s.E<WenyiNativeIssuedEvent>().Any(e => e.FrameId == id),
                "The exact equipped original first enters the recipient's Hand; source Lion/Ox children must return before the recipient can really use it.");
            s.Cold(); s.ContinueAny();
            if (scene == "wenyi-lion")
            {
                s.Reach(() => s.Hold("recovery")); var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.WenyiUse?.ProgramFrameId == id);
                s.Need(s.V().Hp == hp + 1 && s.V(1).Hp == 2 && use.Action is { ActorSeat: 1, ProviderSeat: 1, Type: CardActionType.Use } a && a.PhysicalCards is [var paid] && paid.CardId == equipment.Id && paid.From == CardLocation.Hand(1) &&
                    s.G.CardMovements.Any(m => m.CardId == oldEquipment && m.From == CardLocation.Equipment(1) && m.To == CardLocation.DiscardPile) && !s.E<WenyiNativeReturnedEvent>().Any(e => e.FrameId == id),
                    "The recipient's genuine equipment Use replaces its old Lion and waits for its separate native recovery; donor and new actor remain distinct.");
                s.Cold(); s.ContinueAny();
            }
            s.Play(); var issued = s.E<WenyiNativeIssuedEvent>().Single(e => e.FrameId == id);
            s.Need(issued is { ActorSeat: 1, ProviderSeat: 1, TargetSeat: 1 } && issued.Kind == equipment.Kind && s.V(1).Equipment.Any(c => c.Id == equipment.Id) && !s.V(1).IsFaceDown && !s.V(1).IsChained &&
                s.E<WenyiNativeReturnedEvent>().Count(e => e.FrameId == id && e.UseId == issued.UseId) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == equipment.Id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.EquipmentUse) == 1,
                "Gifted equipment is a real original-entity native Use, followed by one complete restoration and no duplicated payment.");
            if (grain is { } grainId) s.Need(s.G.CardMovements.Count(m => m.CardId == grainId && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "equipment.wooden-ox.grain-discard") == 1 &&
                s.E<WenyiEquipmentGivenEvent>().All(e => e.CardId != grainId) && !s.V(1).Hand.Any(c => c.Id == grainId), "The donor's true Ox spill is not gifted or silently added to the receiver's physical use cost."); s.Cold();
        }
        {
            var s = Scenario.Start("wenyi-blocked"); var original = s.V().Hand[0]; s.States(1); s.ToOne(1); s.OfferWenyi(); s.Activate();
            s.Reach(() => s.Branch("wenyi-equipment")); var id = s.Own().Id;
            s.Need(s.E<PlayerAreasAbolishedEvent>().Any(e => e.Seat == 1 && e.Equipment) && s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("huan-shu-jun") == "wenyi-equipment" && c.Cards.SequenceEqual([original.Id])),
                "A genuinely abolished equipment area does not prohibit the original equipment gift itself.");
            s.Private(0); s.Cold(); s.BranchAnswer("wenyi-equipment", original.Id); s.Reach(() => s.Hold("face"));
            s.Need(s.V(1).Hand.Any(c => c.Id == original.Id) && s.V(1).Equipment.Count == 0 && !s.E<WenyiNativeIssuedEvent>().Any(e => e.FrameId == id) && !s.V(1).IsFaceDown && s.V(1).IsChained,
                "The paid but unusable original remains in the recipient's real Hand, while restoration follows without a fabricated equipment Use.");
            s.Private(1); s.Cold(); s.ContinueAny(); s.Play();
            s.Need(!s.V(1).IsChained && s.E<WenyiPaidEvent>().Count(e => e.FrameId == id) == 1 && s.E<HuanShuJunCompletedEvent>().Count(e => e.FrameId == id && e.Paid) == 1 &&
                !s.G.CardMovements.Any(m => m.CardId == original.Id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing),
                "The actual no-use result returns once, preserves the given entity and completes the living recipient's restoration."); s.Cold();
        }
        WenyiSuppliedPeachCompatibilityCold();
        {
            var s = Scenario.Start("wenyi-red-hand-blocked");
            var originals = s.V().Hand.Select(c => c.Id).ToArray();
            s.Need(originals.Length > 0 && s.V().Hand.All(c => c.Kind == CardKind.Peach && c.Suit == Suit.Heart),
                "The restriction counterexample starts with real natural red Peaches in the aid owner's Hand.");
            var before = s.Sequence; s.End(); s.OfferWenyi();
            var restriction = s.E<HandCardColorRestrictionGrantedEvent>().Single().Restriction;
            var target = s.G.State.CurrentSeat;
            var health = s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
            s.Need(target != 0 && restriction is { AffectedSeat: 0, IsRed: true } && restriction.TurnNumber == s.G.State.TurnNumber &&
                restriction.TurnSeat == target && restriction.Source.OwnerSeat == target &&
                health.Change.TargetSeat == target && health.Change.HpBefore == 12 && health.Change.HpAfter == 1 &&
                s.G.CardMovements.Count(m => m.Sequence > before && m.CardKind == CardKind.Peach && m.From == CardLocation.Hand(target) && m.To == CardLocation.DiscardPile) == 1,
                "A real foreign turn-start program discards its own red entity, grants the native hand-color restriction to the different Wenyi owner, then actually loses HP to one.");
            s.Activate(); s.Reach(() => s.Branch("wenyi-peach")); var id = s.Own().Id;
            s.BranchAnswer("wenyi-peach");
            s.Reach(() => s.Own(id).HuanShuJun!.Stage == HuanShuJunStage.WenyiPeachRequest);
            s.Need(!s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "wenyi-peach-supply") &&
                !s.P.Choices.Any(c => c.Cards.Any(originals.Contains)) &&
                originals.All(card => s.V().Hand.Any(c => c.Id == card && c.Kind == CardKind.Peach && c.Suit == Suit.Heart)) &&
                !s.E<WenyiPaidEvent>().Any(e => e.FrameId == id) && !s.E<WenyiNativeIssuedEvent>().Any(e => e.FrameId == id),
                "Natural red Hand Peaches remain real owned entities but are absent from the exact other-target Wenyi use menu; no payment or native Use bypasses the actual color prohibition.");
            s.Private(0); s.Reject(); s.Cold(); s.BranchAnswer("cancel");
            s.Reach(() => s.E<HuanShuJunCompletedEvent>().Any(e => e.FrameId == id));
            s.Need(s.E<HuanShuJunCompletedEvent>().Count(e => e.FrameId == id && !e.Paid) == 1 &&
                !s.E<WenyiPaidEvent>().Any(e => e.FrameId == id) && !s.E<WenyiNativeIssuedEvent>().Any(e => e.FrameId == id) &&
                !s.G.CardMovements.Any(m => m.Sequence > before && originals.Contains(m.CardId) && m.From == CardLocation.Hand(0)),
                "Cold cancellation of this unavailable supply preserves Wenyi's allowance and every restricted original without moving or replacing a material.");
            s.Cold();
        }
        {
            var s = Scenario.Start("wenyi-red-equipment-blocked");
            var restrictionCost = s.V().Hand[0]; var before = s.Sequence;
            s.Use("restrict-peer", targets: [1]);
            s.Reach(() => s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
            s.Answer(c => c.Cards.SequenceEqual([restrictionCost.Id])); s.Play();
            var restriction = s.E<HandCardColorRestrictionGrantedEvent>().Single().Restriction;
            s.Need(restriction is { AffectedSeat: 1, IsRed: true, Source.OwnerSeat: 0 } &&
                restriction.TurnNumber == s.G.State.TurnNumber && restriction.TurnSeat == 0 &&
                s.G.CardMovements.Count(m => m.Sequence > before && m.CardId == restrictionCost.Id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1,
                "A real original red equipment discard pays the native color-restriction producer and binds its frozen color to the different recipient's Hand.");
            var original = s.V().Hand.First(c => c.Kind == CardKind.Crossbow && c.Suit == Suit.Heart);
            s.States(1); s.ToOne(1); s.OfferWenyi(); s.Activate(); s.Reach(() => s.Branch("wenyi-equipment")); var id = s.Own().Id;
            s.Need(s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("huan-shu-jun") == "wenyi-equipment" && c.Cards.SequenceEqual([original.Id])) &&
                !s.E<PlayerAreasAbolishedEvent>().Any(e => e.Seat == 1),
                "A native red Hand restriction does not prevent the original gift to an otherwise available equipment area.");
            s.Private(0); s.Cold(); s.BranchAnswer("wenyi-equipment", original.Id); s.Reach(() => s.Hold("face"));
            s.Need(s.V(1).Hand.Any(c => c.Id == original.Id && c.Kind == CardKind.Crossbow && c.Suit == Suit.Heart) && s.V(1).Equipment.Count == 0 &&
                s.G.CardMovements.Count(m => m.CardId == original.Id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1) && m.Reason.Value == Gift) == 1 &&
                !s.G.CardMovements.Any(m => m.CardId == original.Id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) &&
                !s.E<WenyiNativeIssuedEvent>().Any(e => e.FrameId == id) && !s.V(1).IsFaceDown && s.V(1).IsChained,
                "The genuinely given red Crossbow stays in the restricted recipient's Hand: no native Use or Processing payment occurs, while face-up restoration still starts.");
            s.Private(1); s.Cold(); s.ContinueAny(); s.Play();
            s.Need(!s.V(1).IsFaceDown && !s.V(1).IsChained && s.V(1).Hand.Any(c => c.Id == original.Id) &&
                s.E<WenyiPaidEvent>().Count(e => e.FrameId == id && e.Branch == "equipment") == 1 &&
                s.E<WenyiEquipmentGivenEvent>().Count(e => e.FrameId == id && e.CardId == original.Id && e.RecipientSeat == 1) == 1 &&
                s.E<WenyiRestorationIssuedEvent>().Where(e => e.FrameId == id).Select(e => e.FaceState).SequenceEqual([true, false]) &&
                s.E<HuanShuJunCompletedEvent>().Count(e => e.FrameId == id && e.Paid) == 1 &&
                !s.E<WenyiNativeIssuedEvent>().Any(e => e.FrameId == id),
                "Cold returning from the forbidden equipment Use preserves the paid gift once and completes both living-recipient restoration children.");
            s.Cold();
        }
    }

    private static void WenyiSuppliedPeachCompatibilityCold()
    {
        foreach (var adapter in new[] { "huanpei", "single-response", "multi", "excluded-self", "funded", "slot", "deck", "weijie" })
        {
            var s = Scenario.Start("wenyi-supply-" + adapter);
            var foreign = adapter is "deck" or "weijie";
            var target = foreign || adapter == "excluded-self" ? 0 : 1;
            int? gearId = null;
            if (adapter == "huanpei") s.Trim(1);
            if (adapter == "slot") { gearId = s.V().Hand[0].Id; s.PlayCard(gearId.Value, []); s.Play(); }
            if (foreign)
            {
                s.End(); s.OfferWenyi();
                s.Need(s.G.State.CurrentSeat != 0 && s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single().Change is { TargetSeat: 0, HpBefore: 13, HpAfter: 1 },
                    "The native top-deck and Weijie suppliers are requested in a genuine foreign actual turn, after its real turn-start HP loss.");
            }
            else { s.ToOne(target); s.OfferWenyi(); }
            s.Activate(); s.Reach(() => s.Branch("wenyi-peach")); var parentId = s.Own().Id;
            s.BranchAnswer("wenyi-peach");
            s.Reach(() => s.Own(parentId).HuanShuJun!.Stage == HuanShuJunStage.WenyiPeachRequest);
            s.Private(0); s.Reject();
            long? supplierId = null; int[] materialIds = []; CardLocation? materialFrom = null;
            if (adapter == "excluded-self")
            {
                s.Need(!s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == Supply) &&
                    !s.E<WenyiPaidEvent>().Any(e => e.FrameId == parentId),
                    "ExcludeOwnerEffects rejects a Peach on the actual self target even though the same response-only sources can aid another character.");
                s.Cold(); s.BranchAnswer("cancel"); s.Play();
                s.Need(!s.E<WenyiNativeIssuedEvent>().Any(e => e.FrameId == parentId) &&
                    s.E<HuanShuJunCompletedEvent>().Count(e => e.FrameId == parentId && !e.Paid) == 1,
                    "An unavailable self-directed conversion neither pays Wenyi nor invents a Peach Use.");
                s.Cold(); continue;
            }
            if (adapter == "huanpei")
            {
                var hp = s.V().Hp;
                s.Need(s.OpenHuanpei(CardKind.Peach), "Wenyi's real other-target Peach need offers Huanpei even while its owner is at full HP.");
                s.Answer(c => c.Parameters.ContainsKey("huanpei-open") && c.Parameters.GetValueOrDefault("output-kind") == nameof(CardKind.Peach));
                supplierId = s.Own().Id; s.PayAdjustment(); s.Reach(() => s.Hold("supply-paid"));
                s.Need(s.Own(parentId).HuanShuJun is { Paid: true, Stage: HuanShuJunStage.NativeIssuing } &&
                    s.E<HuanpeiAdjustmentAcceptedEvent>().Single(e => e.FrameId == supplierId) is { HandCount: 1 } paid && paid.Hp == hp &&
                    s.E<HuanpeiAdjustmentCompletedEvent>().Single(e => e.FrameId == supplierId).ActualDrawCount == hp - 1 &&
                    !s.E<HuanpeiIssuedEvent>().Any(e => e.PaymentFrameId == supplierId) &&
                    !s.E<WenyiNativeIssuedEvent>().Any(e => e.FrameId == parentId),
                    "Real Huanpei replenishment pays the owner round quota and the exact Wenyi request before its native gain child, without issuing an early Peach.");
                s.Cold(); s.ContinueAny();
            }
            else if (adapter is "single-response" or "multi")
            {
                var route = adapter == "multi" ? "multi" : "single";
                var choice = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("wenyi-supply") == route && c.Parameters.GetValueOrDefault("conversion-skill-id") == Supply);
                materialIds = choice.Cards.ToArray(); materialFrom = CardLocation.Hand(0);
                s.Need(materialIds.Length == (adapter == "multi" ? 2 : 1) && choice.Targets.SequenceEqual([target]) &&
                    materialIds.All(id => s.V().Hand.Any(c => c.Id == id && c.Kind == CardKind.Crossbow)),
                    "The response-only Peach conversion publishes its complete original material set and the actual other target, rather than substituting the owner.");
                s.Cold(); s.Answer(c => c.Id == choice.Id);
            }
            else if (adapter == "funded")
            {
                s.Need(s.V().HandCount == s.V().Hp && s.V().Hand.Select(c => c.Suit).Distinct().Count() == 1,
                    "The actual draw-funded owner has a nonempty, genuinely same-color Hand equal to current HP before payment.");
                s.Answer(c => c.Parameters.ContainsKey("draw-funded-basic") && c.Parameters.GetValueOrDefault("output-kind") == nameof(CardKind.Peach));
                s.Reach(() => s.Hold("supply-paid"));
                var draw = s.G.ResolutionStack.OfType<DrawFundedDistinctBasicFrame>().Single(); supplierId = draw.Id;
                s.Need(draw.Payment is { Intent: DrawFundedDistinctBasicIntent.WenyiPeach, TargetSeat: 1, ActualDrawCount: 1 } && draw.Payment.ParentFrameId == parentId &&
                    s.E<DrawFundedDistinctBasicPaidEvent>().Count(e => e.PaymentFrameId == supplierId && e.ActualDrawCount == 1) == 1 &&
                    !s.E<WenyiNativeIssuedEvent>().Any(e => e.FrameId == parentId),
                    "The funded adapter owns its exact Wenyi parent and real one-card native draw, whose gain child precedes zero Peach issue.");
                s.Cold(); s.ContinueAny();
            }
            else if (adapter == "slot")
            {
                s.Answer(c => c.Parameters.GetValueOrDefault("wenyi-supply") == "tiered-zero" && c.Parameters.GetValueOrDefault("conversion-skill-id") == Supply);
                s.Reach(() => s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "equipment-slot-named-use"));
                s.Need(!s.E<EquipmentSlotNamedUsePaidEvent>().Any(), "The exact accepted slot supplier owns a real unpaid slot choice before any abolition.");
                s.Private(0); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("equipment-slot") == nameof(EquipmentSlot.Weapon));
                s.Reach(() => s.Hold("supply-paid"));
                var payment = s.E<EquipmentSlotNamedUsePaidEvent>().Single().Payment; supplierId = payment.FrameId;
                s.Need(payment is { Intent: EquipmentSlotNamedUseIntent.WenyiPeach, Yin: true, EffectiveKind: CardKind.Peach, Slot: EquipmentSlot.Weapon, PreviousCapacity: 1 } &&
                    payment.OriginalParentId == parentId && s.G.CardMovements.Count(m => m.CardId == gearId && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "equipment.slot-abolished") == 1 &&
                    !s.E<EquipmentSlotNamedUseIssuedEvent>().Any(e => e.PaymentFrameId == supplierId),
                    "The configured Yin-basic shared fixture pays a real equipped weapon slot and waits for its actual child; it does not claim OL Youlong's initial face.");
                s.Cold(); s.ContinueAny();
            }
            else if (adapter == "deck")
            {
                s.Answer(c => c.Parameters.GetValueOrDefault("deck-basic-source") == "activate");
                var view = s.G.ResolutionStack.OfType<RequestedDeckBasicFrame>().Single(); supplierId = view.Id;
                s.Need(view.Receipt is { Intent: RequestedDeckBasicIntent.WenyiPeach, ActorSeat: 0 } && view.Receipt.OwnerFrameId == parentId &&
                    s.G.CreateSnapshot(0).PrivateRevealedCards!.Select(c => c.Id).SequenceEqual(view.CardIds) &&
                    Enumerable.Range(1, 3).All(seat => s.G.CreateSnapshot(seat).PrivateRevealedCards is null && s.G.CreateSnapshot(seat).PendingDecision is null),
                    "The actual foreign-turn top-deck request privately preserves the ordered viewed entities and the exact pending Wenyi parent.");
                var choice = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("deck-basic-source") == "card" && c.Targets.SequenceEqual([target]));
                materialIds = choice.Cards.ToArray(); materialFrom = CardLocation.DrawPile;
                s.Cold(); s.Answer(c => c.Id == choice.Id);
            }
            else
            {
                s.Need(s.P!.Choices.Any(c => c.Parameters.ContainsKey("weijie-request")), "The actual foreign-turn Wenyi Peach need publishes the native Weijie supply adapter.");
                s.Answer(c => c.Parameters.ContainsKey("weijie-request"));
                var draft = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Weijie is not null); supplierId = draft.Id;
                s.Need(draft.Weijie!.Need is { Intent: WeijieIntent.WenyiPeach, ActorSeat: 0, ProviderSeat: 0 } need && need.OwnerFrameId == parentId && need.RequestFrameId == parentId && need.Targets.SequenceEqual([target]),
                    "Weijie captures Wenyi's true-Use Peach need without borrowing a dying, pure-played or faction-provider permission.");
                s.Answer(c => c.Parameters.GetValueOrDefault("weijie-choice") == "declare" && c.Parameters.GetValueOrDefault("kind") == nameof(CardKind.Peach));
                s.Answer(c => c.Parameters.GetValueOrDefault("weijie-choice") == "victim");
                s.Need(s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("weijie-choice") == "card").All(c => c.Cards.Count == 0),
                    "The foreign Hand cost remains an opaque canonical slot rather than exposing its hidden printed name.");
                s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("weijie-choice") == "card"); s.Reach(() => s.Hold("supply-paid"));
                var paid = s.E<WeijieCostPaidEvent>().Single(e => e.FrameId == supplierId);
                s.Need(paid.PrintedKind == CardKind.Peach && paid.From == CardLocation.Hand(paid.VictimSeat) && paid.VictimSeat != 0 &&
                    s.G.CardMovements.Count(m => m.Sequence == paid.Sequence && m.CardId == paid.CardId && m.From == paid.From && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.guo-tu.weijie.discard") == 1 &&
                    !s.E<WeijieAttemptResolvedEvent>().Any(e => e.FrameId == supplierId) && !s.E<WenyiPaidEvent>().Any(e => e.FrameId == parentId),
                    "One real foreign Hand entity is discarded and its native child is awaited before matching or paying the still-pending Wenyi aid.");
                s.Cold(); s.ContinueAny();
            }

            s.Reach(() => s.Hold("recovery"));
            var native = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.WenyiUse?.ProgramFrameId == parentId);
            var actual = native.Action!;
            s.Need(actual is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.Peach } &&
                actual.EffectiveDesignatedTargetSeats.SequenceEqual([target]) && s.V(target).Hp == 2 &&
                actual.PhysicalCards.Select(c => c.CardId).SequenceEqual(materialIds) &&
                actual.PhysicalCards.All(c => c.From == materialFrom) && !s.E<WenyiNativeReturnedEvent>().Any(e => e.FrameId == parentId) &&
                s.E<WenyiPaidEvent>().Count(e => e.FrameId == parentId && e.Branch == "peach") == 1,
                "Every adapter issues one actual Peach Use with the complete original materials and real beneficiary, then waits for the original recovery child before returning.");
            if (adapter == "huanpei")
                s.Need(native.HuanpeiUse is { } huanpeiUse && huanpeiUse.Payment.ProgramFrameId == supplierId && huanpeiUse.Payment.Request.Intent == HuanpeiIntent.WenyiPeach &&
                    s.E<HuanpeiIssuedEvent>().Count(e => e.PaymentFrameId == supplierId && e.UseId == native.Id) == 1 && !s.E<HuanpeiReturnedEvent>().Any(e => e.PaymentFrameId == supplierId),
                    "The same real zero Peach simultaneously carries its paid Huanpei and Wenyi origins, each awaiting the genuine native return.");
            if (adapter is "funded" or "slot")
                s.Need(actual.ConversionChain is [var paidSource] && paidSource.SkillId == Supply && paidSource.OwnerSeat == 0 && actual.PhysicalCards.Count == 0,
                    "The exact paid funded or slot source issues a zero-entity Peach without substituting an unprovided physical card.");
            if (adapter is "single-response" or "multi")
                s.Need(actual.ConversionChain is [var source] && source.SkillId == Supply &&
                    actual.PhysicalCards.All(c => c.CardKind == CardKind.Crossbow) &&
                    materialIds.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1),
                    "Response-only single and multiple physical sources remain true Use costs, preserving each original printed equipment entity and its once-paid movement.");
            if (adapter == "deck")
                s.Need(actual.ConversionChain.Count == 0 && actual.PhysicalCards is [{ CardKind: CardKind.Peach }] &&
                    s.E<RequestedDeckBasicPaidEvent>().Count(e => e.OwnerFrameId == parentId && e.Intent == RequestedDeckBasicIntent.WenyiPeach && e.CardId == materialIds.Single() && e.CardKind == CardKind.Peach) == 1 &&
                    s.G.CardMovements.Count(m => m.CardId == materialIds.Single() && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing) == 1,
                    "The top-deck adapter pays exactly its selected original DrawPile Peach rather than drawing or replacing it with another entity.");
            if (adapter == "weijie")
                s.Need(native.WeijieOrigin is { Need.Intent: WeijieIntent.WenyiPeach } &&
                    s.E<WeijieAttemptResolvedEvent>().Count(e => e.FrameId == supplierId && e.Matched && e.NativeLegal) == 1 &&
                    s.E<WeijieUseIssuedEvent>().Count(e => e.AttemptId == supplierId && e.UseId == native.Id && e.OutputKind == CardKind.Peach) == 1 &&
                    !s.E<WeijieUseReturnedEvent>().Any(e => e.AttemptId == supplierId),
                    "Successful Weijie really issues the zero Peach under its exact native paid origin, before either supplier or Wenyi can return.");
            s.Private(target); s.Cold(); s.ContinueAny();
            s.Reach(() => s.E<HuanShuJunCompletedEvent>().Any(e => e.FrameId == parentId));
            s.Need(s.E<WenyiNativeReturnedEvent>().Count(e => e.FrameId == parentId && e.UseId == native.Id && e.ActionId == actual.ActionId) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == native.Id) == 1 &&
                s.E<HuanShuJunCompletedEvent>().Count(e => e.FrameId == parentId && e.Paid) == 1,
                "Cold replay returns the same complete native Peach and its original paid Wenyi parent exactly once.");
            if (adapter == "huanpei")
                s.Need(s.E<HuanpeiAdjustmentAcceptedEvent>().Count(e => e.FrameId == supplierId) == 1 && s.E<HuanpeiReturnedEvent>().Count(e => e.PaymentFrameId == supplierId && e.UseId == native.Id) == 1 &&
                    s.E<WenyiPaidEvent>().Count(e => e.FrameId == parentId) == 1 && !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Huanpei),
                    "The nested recovery does not repay or replenish either Huanpei's actual round quota or Wenyi's game allowance.");
            if (adapter == "funded")
                s.Need(s.E<DrawFundedDistinctBasicIssuedEvent>().Count(e => e.PaymentFrameId == supplierId && e.OwnerFrameId == native.Id && e.Intent == DrawFundedDistinctBasicIntent.WenyiPeach) == 1 &&
                    s.E<DrawFundedDistinctBasicReturnedEvent>().Count(e => e.PaymentFrameId == supplierId && e.OwnerFrameId == native.Id) == 1,
                    "The separate funded payment has one exact native issue and one typed return.");
            if (adapter == "slot")
                s.Need(s.E<EquipmentSlotNamedUseConsumedEvent>().Count(e => e.PaymentFrameId == supplierId) == 1 &&
                    s.E<EquipmentSlotNamedUseIssuedEvent>().Count(e => e.PaymentFrameId == supplierId && e.NativeOwnerId == native.Id) == 1,
                    "The same real abolished-slot invoice is consumed once for the exact native Peach, rather than paying again after its child.");
            if (adapter == "weijie")
                s.Need(s.E<WeijieUseReturnedEvent>().Count(e => e.AttemptId == supplierId && e.UseId == native.Id && e.ActionId == actual.ActionId) == 1 &&
                    s.E<WeijieCostPaidEvent>().Count(e => e.FrameId == supplierId) == 1,
                    "The exact foreign discard receipt survives the cold native return and is never charged twice.");
            s.Cold();
        }
    }

    public static void MiaoxinExactDamageReplenishesGameAllowanceAndNativeDrawCold()
    {
        foreach (var scene in new[] { "miaoxin", "miaoxin-replacement" })
        {
            var s = Scenario.Start(scene); var before = s.V().HandCount; s.Use("self-damage-two"); s.OfferWenyi();
            var health = s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single(); s.Need(health.Change is { Kind: HpChangeKind.Damage, HpBefore: 3, HpAfter: 1 } && s.E<MiaoxinBenefitIssuedEvent>().Length == 0,
                "The real HP-one window precedes after-damage benefits for the same native two-point injury.");
            s.Activate(); s.Reach(() => s.Branch("wenyi-peach")); s.BranchAnswer("wenyi-peach");
            s.Answer(c => c.Cards.Count == 1 && s.V().Hand.Any(h => h.Id == c.Cards[0] && h.Kind == CardKind.Peach));
            s.Reach(() => scene == "miaoxin-replacement" ? s.Optional("ol:zenrun") : s.Hold("miaoxin"));
            var first = s.E<MiaoxinBenefitIssuedEvent>().Single(); s.Need(first is { Occurrence: 0, DrawCount: 1, RefreshWenyi: true } && s.E<WenyiPaidEvent>().Length == 1 && s.E<MiaoxinWenyiRefreshedEvent>().Length == 0,
                "The first actual damage point freezes the exhausted-allowance branch only after the real Wenyi Peach spent its charge.");
            if (scene == "miaoxin-replacement")
            {
                var attempt = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(); s.Need(attempt.Context.ParentFrameId == first.FrameId && attempt.Context.RequestedCount == 1, "Miaoxin owns the actual native Draw1 attempt, not a generic substitute draw.");
                s.Cold(); s.Activate(); s.Reach(() => s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Zenrun?.Stage == ZenrunStage.Target));
                s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([1]));
                s.Need(s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0), "The real replacement exposes opaque foreign Hand slots without their private faces.");
                s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
                var replacementCard = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Zenrun is not null).Zenrun!.Selected.Single().CardId;
                s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); s.Reach(() => s.Hold("obtained"));
                s.Need(s.E<MiaoxinWenyiRefreshedEvent>().Length == 0 && s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Id == attempt.Id).Replacement is { } replacement && replacement.GainedIds.SequenceEqual([replacementCard]) &&
                    !s.E<HuanShuJunDrawReturnedEvent>().Any(e => e.FrameId == first.FrameId), "The exact replacement entity and its native gain child precede the original draw result and allowance refresh.");
                s.Private(0); s.Cold(); s.ContinueAny(); s.Reach(() => s.Hold("compensated"));
                s.Need(s.P!.PlayerSeat == 1 && s.E<MiaoxinWenyiRefreshedEvent>().Length == 0 && s.E<ZenrunCompensationReturnedEvent>().Single().ActualDrawCount == 1,
                    "The actual victim's separate native compensation child must finish before the original Miaoxin request can return.");
                s.Private(1); s.Cold(); s.ContinueAny(); s.Reach(() => s.Hold("miaoxin"));
            }
            s.Private(0); s.Cold(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            var benefits = s.E<MiaoxinBenefitIssuedEvent>(); var draws = s.E<HuanShuJunDrawReturnedEvent>().Where(e => benefits.Any(b => b.FrameId == e.FrameId)).ToArray();
            s.Need(benefits.Length == 2 && benefits[0].DamageWindowId == benefits[1].DamageWindowId && benefits.Select(e => e.Occurrence).SequenceEqual([0, 1]) && benefits.Select(e => e.DrawCount).SequenceEqual([1, 2]) &&
                benefits.Select(e => e.RefreshWenyi).SequenceEqual([true, false]) && s.E<MiaoxinWenyiRefreshedEvent>().Count(e => e.FrameId == first.FrameId) == 1 && draws.Select(e => (e.ActualCount, e.ReplacementCount)).SequenceEqual(scene == "miaoxin" ? new[] { (1, 0), (2, 0) } : new[] { (0, 1), (2, 0) }) &&
                s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 2) == 1 && s.V().HandCount == before - 1 + 3,
                "One real Damage2 yields two ordered point benefits; the first refreshes once, and replacement entities never become fictional DrawPile materials.");
            s.ToOne(1); s.OfferWenyi(); s.Activate(); s.Reach(() => s.Branch("wenyi-peach")); s.BranchAnswer("wenyi-peach"); s.Answer(c => c.Cards.Count == 1 && s.V().Hand.Any(h => h.Id == c.Cards[0] && h.Kind == CardKind.Peach)); s.Play();
            s.Need(s.E<WenyiPaidEvent>().Length == 2, "Miaoxin restores a genuine later game-allowance use instead of leaving an irreversible once-per-game flag.");
            s.Use("peer-one-less", targets: [1]); s.Play(); s.Need(s.E<WenyiPaidEvent>().Length == 2 && !s.Optional(Wenyi), "The restored allowance is spent once and cannot be multiplied by the second damage point."); s.Cold();
        }
    }

    public static void PaidSourceLossDeathPrivacyAndRejectedInputCold()
    {
        foreach (var scene in new[] { "adjust-loss", "adjust-death" })
        {
            var s = Scenario.Start(scene); var id = s.BeginHuanpei(); s.Private(0); s.Reject();
            if (scene == "adjust-loss")
            {
                s.BranchAnswer("cancel"); s.Play();
                s.Need(s.E<HuanpeiAdjustmentAcceptedEvent>().Length == 0 && s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Huanpei), "An actual unpaid cancel spends neither adjustment nor round allowance.");
                id = s.BeginHuanpei();
            }
            s.PayAdjustment(); s.Reach(() => s.Hold("paid"));
            var source = s.Own(id).HuanShuJun!.Source;
            s.Need(s.Own(id).HuanShuJun!.Paid && s.E<HuanpeiAdjustmentAcceptedEvent>().Count(e => e.FrameId == id) == 1 && s.E<HuanpeiIssuedEvent>().Length == 0, "Real adjustment payment and native gain children precede any zero-use issue.");
            s.Cold(); s.ContinueAny();
            if (scene == "adjust-loss")
            {
                s.Reach(() => s.Branch("basic-use")); s.Need(s.V().IsAlive && !s.Has(Huanpei) && s.Own(id).HuanShuJun!.Source == source, "A paid adjustment retains the exact removed conditional source rather than substituting a new grant.");
                s.Basic(CardKind.Alcohol, []); s.Play(); s.Need(s.E<HuanpeiIssuedEvent>().Count(e => e.PaymentFrameId == id && e.Kind == CardKind.Alcohol) == 1 && s.E<HuanpeiReturnedEvent>().Count(e => e.PaymentFrameId == id) == 1, "Accepted adjustment completes its promised living user's native use after source loss.");
            }
            else
            {
                s.Reach(() => !s.V().IsAlive && !s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == id));
                s.Need(s.E<PlayerDiedEvent>().Count(e => e.VictimSeat == 0) == 1 && s.E<HuanpeiAdjustmentAcceptedEvent>().Count(e => e.FrameId == id) == 1 && !s.E<HuanpeiIssuedEvent>().Any(e => e.PaymentFrameId == id) && s.E<HuanShuJunCompletedEvent>().Count(e => e.FrameId == id && e.Paid) == 1,
                    "A truly dead user drains paid children once and issues no new basic Use or dead-player menu.");
            }
            s.Cold();
        }
        foreach (var scene in new[] { "wenyi-paid-loss", "wenyi-paid-death", "wenyi-recipient-death" })
        {
            var s = Scenario.Start(scene); s.States(1); s.ToOne(1); s.OfferWenyi(); s.Activate(); s.Reach(() => s.Branch("wenyi-equipment"));
            var id = s.Own().Id; var original = s.V().Hand[0]; var old = s.P!; s.Reject(); s.Cold(); s.BranchAnswer("wenyi-equipment", original.Id); s.Reach(() => s.Hold("paid"));
            s.Need(s.E<WenyiPaidEvent>().Count(e => e.FrameId == id && e.Branch == "equipment") == 1 && s.E<WenyiEquipmentGivenEvent>().Count(e => e.FrameId == id && e.CardId == original.Id) == 1 && s.G.CreateSnapshot(0).Players[1].Hand.Count == 0 && !s.E<WenyiNativeIssuedEvent>().Any(e => e.FrameId == id),
                "The real given original and its exact private child have one paid invoice before the recipient's actual use.");
            s.RejectOld(old); s.Cold(); s.ContinueAny();
            if (scene == "wenyi-recipient-death") { s.Reach(() => s.P is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Targets.SequenceEqual([1]))); s.Answer(c => c.Targets.SequenceEqual([1])); }
            s.Reach(() => s.E<HuanShuJunCompletedEvent>().Any(e => e.FrameId == id));
            if (scene == "wenyi-recipient-death")
                s.Need(!s.V(1).IsAlive && !s.E<WenyiNativeIssuedEvent>().Any(e => e.FrameId == id) && !s.E<WenyiRestorationIssuedEvent>().Any(e => e.FrameId == id), "A genuinely dead recipient receives no new equipment Use or restoration; its paid original is not replaced.");
            else
            {
                var issued = s.E<WenyiNativeIssuedEvent>().Single(e => e.FrameId == id);
                s.Need((scene == "wenyi-paid-loss" ? s.V().IsAlive && !s.Has(Wenyi) : !s.V().IsAlive) && s.V(1).Equipment.Any(c => c.Id == original.Id) && !s.V(1).IsFaceDown && !s.V(1).IsChained &&
                    issued is { ActorSeat: 1, ProviderSeat: 1 } && s.E<WenyiNativeReturnedEvent>().Count(e => e.FrameId == id && e.UseId == issued.UseId) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId) == 1,
                    "The exact paid gift survives source loss or real issuer death: the living recipient uses the same entity and restores once.");
            }
            s.Need(s.E<WenyiPaidEvent>().Count(e => e.FrameId == id) == 1 && s.E<HuanShuJunCompletedEvent>().Count(e => e.FrameId == id && e.Paid) == 1 && s.G.CardMovements.Count(m => m.CardId == original.Id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1) && m.Reason.Value == Gift) == 1,
                "Cold source/death continuation preserves once-only original payment and whole completion."); s.Cold();
        }
    }

    private sealed class Scenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public int Sequence => G.CardMovements.LastOrDefault()?.Sequence ?? 0;
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Has(string skill) => V().Skills?.Any(s => s.ContentId == skill) == true;
        public ProgramSkillFrame Own(long? id = null) => G.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.HuanShuJun is not null && (id is null || f.Id == id));
        public bool Branch(string branch) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("huan-shu-jun") == branch) == true;
        public bool Optional(string skill) => P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool OpenHuanpei(CardKind kind) => P?.Choices.Any(c => c.Parameters.ContainsKey("huanpei-open") && c.Parameters.GetValueOrDefault("output-kind") == kind.ToString()) == true;
        public bool Hold(string option) => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void OfferWenyi() => Reach(() => Optional(Wenyi));
        public void BranchAnswer(string branch, int? card = null) => Answer(c => c.Parameters.GetValueOrDefault("huan-shu-jun") == branch && (card is null || c.Cards.SequenceEqual([card.Value])));
        public void Basic(CardKind kind, IReadOnlyList<int> targets) => Answer(c => c.Parameters.GetValueOrDefault("huan-shu-jun") == "basic-use" && c.Parameters.GetValueOrDefault("kind") == kind.ToString() && c.Targets.SequenceEqual(targets));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void ContinueAny() { if (P!.PlayerSeat == 0) Continue(); else Accept(new AdvanceOneStepCommand(G.Revision)); }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Use(string id, int[]? cards = null, int[]? targets = null, string skill = Driver) => Accept(new UseProgramSkillCommand(0, skill, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void ToOne(int seat) { Need(V(seat).Hp > 1, "The real target must lose a positive actual HP difference to reach one."); Use("peer-lose-" + (V(seat).Hp - 1), targets: [seat]); }
        public void States(int seat) { Use("face", targets: [seat]); Play(); Use("chain", targets: [seat]); Play(); }
        public void Trim(int count) { var ids = V().Hand.Skip(count).Select(c => c.Id).ToArray(); if (ids.Length > 0) { Use("trim", ids); Play(); } }
        public long BeginHuanpei() { Use("adjust-and-use-basic", skill: Huanpei); Reach(() => Branch("cost-confirm") || Branch("cost-toggle")); return Own().Id; }
        public void PayAdjustment()
        {
            Reach(() => Branch("cost-toggle") || Branch("cost-confirm")); var old = P!;
            while (!Branch("cost-confirm")) { var r = Own().HuanShuJun!; var choice = P!.Choices.First(c => c.Parameters.GetValueOrDefault("huan-shu-jun") == "cost-toggle" && !r.Selected.Contains(c.Cards.Single())); Answer(c => c.Id == choice.Id); }
            if (P!.PromptId != old.PromptId) RejectOld(old); Frozen(Own().HuanShuJun!.Selected); BranchAnswer("cost-confirm");
        }
        public void PlayCard(int card, int[] targets)
        {
            var a = G.GetHumanLegalActions().First(a => a.CardId == card && a.TargetSeats.SequenceEqual(targets) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast);
            Accept(new PlayCardCommand(0, card, targets, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public CardActionContext Action(long id) => G.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last(f => f.Action.ActionId == id).Action;
        public void AssertZero(CardActionContext action, CardKind kind) => Need(action.ActorSeat == 0 && action.ProviderSeat == 0 && action.EffectiveKind == kind && action.PhysicalCards.Count == 0 && action.EffectiveSuit == Suit.None && action.EffectiveRank == 0 && action.EffectiveIsRed is null && action.ConversionChain is [var source] && source.SkillId == Huanpei && source.OwnerSeat == 0 &&
            E<HuanpeiIssuedEvent>().Where(e => e.ActionId == action.ActionId).Any(issued => E<HuanShuJunStartedEvent>().Any(started => started.FrameId == issued.PaymentFrameId && started.Source == source)),
            "The actual zero-entity basic preserves actor, native kind, the exact paid conversion source and native colorless None/zero appearance.");
        public void Private(int seat) => Need(P is { IsPrivate: true } p && p.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => G.CreateSnapshot(i).PendingDecision is null), "Only the real chooser receives the current private choice and its original cards.");
        public void Reject()
        {
            var p = P!; var state = State(); Need(!G.Submit(new AnswerPromptCommand((p.PlayerSeat + 1) % 4, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == state && !G.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished-huan-shu-jun"), G.Revision)).Accepted && State() == state, "Foreign and unpublished input cannot mutate source, frozen selections, payments or quota.");
        }
        public void RejectOld(PendingDecision old) { var state = State(); Need(!G.Submit(new AnswerPromptCommand(old.PlayerSeat, old.PromptId, old.Choices[0].Id, G.Revision)).Accepted && State() == state, "A genuinely stale native or consecutive cost prompt cannot answer the current decision."); }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P; var c = p?.Choices.FirstOrDefault(predicate); Need(p is { PlayerSeat: 0 } && c is not null, "The human must answer one actually published canonical choice."); Accept(new AnswerPromptCommand(0, p!.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 210; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "The bounded native fixture ended before boundary at " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == Watch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Need(p.SkillPrompt?.SkillId != Wenyi, "An unexpected Wenyi opportunity must not be silently consumed."); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); }
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) Answer(c => c.Cards.Count == 0 && !c.Parameters.ContainsKey("huanpei-open") && c.Parameters.GetValueOrDefault("response") is not ("faction-slash-request" or "faction-defense-request"));
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else throw new InvalidOperationException("Unexpected real native boundary at " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary at " + line + " " + Diagnostic());
        }
        public void Accept(GameCommand command)
        {
            try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(result)); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " cause=" + e.Message + " " + Diagnostic(), e); }
        }
        public void Need(bool yes, string text) { if (!yes) throw new InvalidOperationException(text + " " + Diagnostic()); }
        public void Cold() { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == state, "Cold replay preserves exact sources, private menus, original entities, native child cursors and once-only payments."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).MaxHp, V(i).IsAlive, V(i).HandCount }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.TakeLast(12).Select(e => e.Payload), Moves = G.CardMovements.TakeLast(8) });
        private void Frozen<T>(IReadOnlyList<T> list) { var refused = false; try { ((IList)list).Add(default(T)); } catch (NotSupportedException) { refused = true; } Need(refused, "The owning receipt exposes detached read-only selections, even when empty."); }
        public static Scenario Start(string scene)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var dead = scene is "adjust-death" or "wenyi-paid-death";
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = dead ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 9, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, Owner, g.Revision, s.P!.PromptId)); s.Play(); return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-huan-shu-jun", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            Production(b, "ol-huan-shu-jun", Huanpei, Wenyi, Miaoxin);
            if (scene == "miaoxin-replacement") Production(b, "ol-sun-hong", "ol:zenrun");
            if (scene == "wenyi-supply-deck") Production(b, "ordinary-zhuge-ke", "ol:aocai");
            if (scene == "wenyi-supply-weijie") Production(b, "ol-guo-tu", "ol:weijie");
            static JsonNode N(string json) => JsonNode.Parse(json)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0, string more = "") => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + more + "\"effects\":[" + effects + "]}");
            var peerHp = scene == "wenyi-native-slash" ? 2 : scene == "wenyi-damage" ? 3 : 12;
            var ownerHp = scene == "wenyi-supply-funded" ? 5 : scene.StartsWith("miaoxin", StringComparison.Ordinal) ? 2 : scene is "adjust-draw" or "adjust-discard" or "equal" or "peach" or "alcohol" or "dodge" or "dying-Peach" or "dying-Alcohol" ? 3 : 12;
            var acts = new JsonArray(A("lose-one", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("lose-two", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}"),
                A("self-damage-two", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":2}"), A("self-dying", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":4}"),
                A("stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"),
                A("peer-to-one", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":" + (peerHp - 1) + "}", targets: 1), A("peer-one-less", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}", targets: 1),
                A("peer-damage-two", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":2}", targets: 1), A("face", "{\"op\":\"setFaceState\",\"target\":\"selectedTarget\",\"faceDown\":true}", targets: 1),
                A("chain", "{\"op\":\"setChainedState\",\"target\":\"selectedTarget\",\"chained\":true}", targets: 1),
                A("place", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"equipment\"},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"equipment\"}", 1, 1, "\"sourceZones\":[\"hand\"],\"cardCategories\":[\"equipment\"],"),
                A("replace-source", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"" + Huanpei + "\"],\"sourceBind\":\"" + Regrant + "\"}"));
            for (var amount = 1; amount <= 13; amount++) acts.Add(A("peer-lose-" + amount, "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":" + amount + "}", targets: 1));
            var trim = A("trim", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"trim\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"trim\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}", more: "\"sourceZones\":[\"hand\"],"); trim["minCards"] = 1; trim["maxCards"] = 60; acts.Add(trim);
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2; acts.Add(duel);
            if (scene == "wenyi-red-equipment-blocked")
            {
                var restrict = A("restrict-peer", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"discardPile\",\"resultBind\":\"restricted-red\",\"freezeMovedCardSuit\":true},{\"op\":\"grantTurnHandColorRestriction\",\"target\":\"selectedTarget\",\"sourceBind\":\"restricted-red\",\"useFrozenSuit\":true}", targets: 1);
                restrict["targetKind"] = "otherLiving"; acts.Add(restrict);
            }
            var driver = new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts, ["modifiers"] = N("[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":60,\"priority\":0}]") };
            if (scene == "dodge") driver["modifiers"]!.AsArray().Add(N("{\"id\":\"actual-victim\",\"query\":\"initialHandSize\",\"operation\":\"add\",\"value\":4,\"priority\":0}"));
            if (scene is "played-duel" or "played-arrows") driver["viewAs"] = N("[{\"id\":\"played\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"" + (scene == "played-duel" ? "slash" : "dodge") + "\",\"forPlay\":false,\"forResponse\":true,\"allowSameKind\":true}]");
            var defs = new JsonArray(driver, N($$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""),
                new JsonObject { ["id"] = Regrant, ["revision"] = 1, ["activations"] = new JsonArray(A("grant", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + Huanpei + "\"]}")) });
            if (scene is "wenyi-supply-single-response" or "wenyi-supply-multi" or "wenyi-supply-excluded-self")
                defs.Add(N($$"""
                    {"id":"{{Supply}}","revision":1,"viewAs":[
                      {"id":"one-response-peach","inputCount":1,"sourceZones":["hand"],"inputKinds":["crossbow"],"inputSuits":[],"outputKind":"peach","forPlay":false,"forResponse":true,"excludeOwnerEffects":true},
                      {"id":"two-response-peach","inputCount":2,"sourceZones":["hand"],"inputKinds":["crossbow"],"inputSuits":[],"outputKind":"peach","forPlay":false,"forResponse":true,"extendedUse":true,"excludeOwnerEffects":true}
                    ]}
                    """));
            if (scene == "wenyi-supply-funded")
                defs.Add(N($$$"""
                    {"id":"{{{Supply}}}","revision":1,"viewAs":[{"id":"funded-peach","inputCount":0,"sourceZones":[],"inputKinds":[],"inputSuits":[],"outputKind":"peach","forPlay":true,"forResponse":false,"useOnly":true,"drawFundedDistinctBasic":{"methodLedgerId":"fixture:hsj-funded-peach"}}]}
                    """));
            if (scene == "wenyi-supply-slot")
                defs.Add(N($$$"""
                    {"id":"{{{Supply}}}","revision":1,
                      "states":[{"id":"yin","initialValue":true,"visibility":"public","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}],
                      "activations":[{"id":"equipment-slot-payment","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"payEquipmentSlotForNamedUse","target":"owner"}]}],
                      "viewAs":[{"id":"slot-peach","inputCount":0,"sourceZones":[],"inputKinds":[],"inputSuits":[],"outputKind":"peach","forPlay":true,"forResponse":false,"useOnly":true,"allowSameKind":true,"conversionStateId":"equipment-slot-named-tier","minimumTier":2,"maximumTier":2,"tieredRoundConversion":{"stateId":"equipment-slot-named-tier","usageId":"yin"}}]
                    }
                    """));
            if (scene is "wenyi-supply-deck" or "wenyi-supply-weijie")
                defs.Add(N($$"""
                    {"id":"{{Incoming}}","revision":1,"triggers":[{"id":"foreign-health-one","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingHighestHand"},{"op":"loseHp","target":"selectedTarget","amount":12}]}]}
                    """));
            if (scene == "wenyi-red-hand-blocked")
                defs.Add(N($$"""
                    {"id":"{{Incoming}}","revision":1,"triggers":[{"id":"foreign-red-restriction","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[
                      {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"discardPile","resultBind":"restricted-red","freezeMovedCardSuit":true},
                      {"op":"selectTarget","target":"owner","targetKind":"otherLivingHighestHand"},
                      {"op":"grantTurnHandColorRestriction","target":"selectedTarget","sourceBind":"restricted-red","useFrozenSuit":true},
                      {"op":"loseHp","target":"owner","amount":11}
                    ]}]}
                    """));
            if (scene == "wenyi-blocked") defs.Add(N("""{"id":"fixture:hsj-blocked-equipment","revision":1,"triggers":[{"id":"actual-abolished-area","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"abolishOwnerAreas","target":"owner","zones":["equipment"]}]}]}"""));
            if (scene == "dodge") defs.Add(N($$"""{"id":"{{Incoming}}","revision":1,"triggers":[{"id":"real-incoming","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingHighestHand"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]}"""));
            if (scene is "adjust-loss" or "wenyi-paid-loss") defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-source\",\"skillId\":\"" + (scene == "adjust-loss" ? Huanpei : Wenyi) + "\",\"minimumHp\":13}]}"));
            static string Choose(string option) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"seen\",\"options\":[{\"id\":\"" + option + "\"}]}";
            var watch = new JsonArray();
            if (!scene.StartsWith("wenyi", StringComparison.Ordinal) && !scene.StartsWith("miaoxin", StringComparison.Ordinal) && !scene.StartsWith("played", StringComparison.Ordinal) && scene is not ("adjust-loss" or "adjust-death"))
                watch.Add(N("{\"id\":\"real-completed\",\"window\":\"cardUseCompleted\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\",\"dodge\",\"peach\",\"alcohol\"],\"includeResponseUses\":true,\"optional\":false,\"effects\":[" + Choose("completed") + "]}"));
            if (scene is "wenyi-loss" or "wenyi-damage" or "wenyi-native-slash" or "wenyi-lion" || scene.StartsWith("wenyi-supply-", StringComparison.Ordinal)) watch.Add(N("{\"id\":\"real-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Choose("recovery") + "]}"));
            if (scene is "wenyi-loss" or "wenyi-damage" or "wenyi-native-slash" or "wenyi-blocked" or "wenyi-red-equipment-blocked") watch.Add(N("{\"id\":\"real-face\",\"window\":\"characterTurnedFaceUp\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Choose("face") + "]}"));
            if (scene == "wenyi-ox") watch.Add(N("{\"id\":\"real-spill\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"optional\":false,\"effects\":[" + Choose("spill") + "]}"));
            var paidScene = scene is "adjust-loss" or "adjust-death" or "wenyi-paid-loss" or "wenyi-paid-death" or "wenyi-recipient-death";
            if (paidScene)
            {
                var adjust = scene.StartsWith("adjust", StringComparison.Ordinal); var effect = scene == "wenyi-recipient-death" ? "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}" : "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene.EndsWith("loss", StringComparison.Ordinal) ? 1 : 20) + "}";
                watch.Add(N("{\"id\":\"real-paid\",\"window\":\"" + (adjust ? "cardsGained" : "cardsMoved") + "\",\"subject\":\"owner\",\"movementOccurrence\":\"perBatch\"," + (adjust ? "\"destinationZones\":[\"hand\"]," : "\"sourceZones\":[\"hand\"],") + "\"movementReasons\":[\"" + (adjust ? AdjustDraw : Gift) + "\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Choose("paid") + "," + effect + "]}"));
            }
            if (scene.StartsWith("miaoxin", StringComparison.Ordinal))
                foreach (var (option, reason) in new[] { ("miaoxin", MiaoxinDraw), ("obtained", Take), ("compensated", Compensation) })
                    watch.Add(N("{\"id\":\"real-" + option + "\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + reason + "\"],\"optional\":false,\"effects\":[" + Choose(option) + "]}"));
            if (scene is "wenyi-supply-huanpei" or "wenyi-supply-funded")
                watch.Add(N("{\"id\":\"real-supply-paid\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + (scene == "wenyi-supply-huanpei" ? AdjustDraw : "skill-program.draw-funded-distinct-basic.draw") + "\"],\"optional\":false,\"effects\":[" + Choose("supply-paid") + "]}"));
            if (scene is "wenyi-supply-slot" or "wenyi-supply-weijie")
                watch.Add(N("{\"id\":\"real-supply-paid\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"" + (scene == "wenyi-supply-slot" ? "equipment" : "hand") + "\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + (scene == "wenyi-supply-slot" ? "equipment.slot-abolished" : "skill-program.guo-tu.weijie.discard") + "\"],\"optional\":false,\"effects\":[" + Choose("supply-paid") + "]}"));
            if (watch.Count > 0) defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watch });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "Small genuine native command fixture", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(a => a!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var c = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in c.Programs) b.AddSkill(new(id, id, "Real native fixture") { Program = program, ProgramPresentation = c.Presentations[id] });
            const string Pick = "fixture:hsj-pick";
            b.AddSkill(new(Pick, "Human", "Public fixture selection") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var skills = new List<string> { Driver };
            if (scene.StartsWith("wenyi", StringComparison.Ordinal)) skills.Add(scene == "wenyi-paid-loss" ? Conditional : Wenyi);
            else if (scene.StartsWith("miaoxin", StringComparison.Ordinal)) { skills.Add(Wenyi); skills.Add(Miaoxin); if (scene == "miaoxin-replacement") skills.Add("ol:zenrun"); }
            else skills.Add(scene == "adjust-loss" ? Conditional : Huanpei);
            if (scene == "wenyi-supply-huanpei") skills.Add(Huanpei);
            if (scene is "wenyi-supply-single-response" or "wenyi-supply-multi" or "wenyi-supply-excluded-self" or "wenyi-supply-funded" or "wenyi-supply-slot") skills.Add(Supply);
            if (scene == "wenyi-supply-deck") skills.Add("ol:aocai");
            if (scene == "wenyi-supply-weijie") skills.Add("ol:weijie");
            if (watch.Count > 0) skills.Add(Watch);
            b.AddGeneral(new(Owner, "环淑君真实技能夹具", "ol-huan-shu-jun", Pick, "wei", ownerHp, skills, GeneralGender.Female));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:hsj-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pick = "fixture:hsj-pick-" + i; b.AddSkill(new(pick, "Peer", "Public fixture selection") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                var peerSkills = new List<string>(); if (scene != "played-arrows") peerSkills.Add(Quiet); if (watch.Count > 0) peerSkills.Add(Watch); if (scene is "dodge" or "wenyi-supply-deck" or "wenyi-supply-weijie" or "wenyi-red-hand-blocked") peerSkills.Add(Incoming); if (scene == "wenyi-blocked") peerSkills.Add("fixture:hsj-blocked-equipment");
                b.AddGeneral(new(peers[i - 1], "真实原生其他角色", "supporter", pick, "qun", peerHp, peerSkills, GeneralGender.Male));
            }
            var kind = scene == "wenyi-lion" ? CardKind.SilverLion : scene == "wenyi-ox" ? CardKind.WoodenOx : scene is "wenyi-loss" or "wenyi-damage" or "wenyi-supply-deck" or "wenyi-supply-weijie" or "wenyi-red-hand-blocked" || scene.StartsWith("miaoxin", StringComparison.Ordinal) ? CardKind.Peach : scene == "played-arrows" ? CardKind.ArrowBarrage : CardKind.Crossbow;
            const string CardId = "fixture:hsj-card"; b.AddCard(new(CardId, "真实" + kind, CardCatalog.Get(kind).CategoryName, "Original printed entity", kind));
            if (scene == "wenyi-native-slash")
            {
                const string SlashCard = "fixture:hsj-natural-slash", PeachCard = "fixture:hsj-natural-peach";
                b.AddCard(new(SlashCard, "真实杀", CardCatalog.Get(CardKind.Slash).CategoryName, "Natural physical Slash", CardKind.Slash));
                b.AddCard(new(PeachCard, "真实桃", CardCatalog.Get(CardKind.Peach).CategoryName, "Natural physical Peach", CardKind.Peach));
                b.AddDeck(new("fixture:hsj-deck", "26张真实杀桃", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 26).Select(i => new ContentDeckPhysicalCard(i < 13 ? SlashCard : PeachCard, new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i % 13 + 1)).ToArray() });
            }
            else b.AddDeck(new("fixture:hsj-deck", "固定小真实牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(CardId, scene is "wenyi-red-hand-blocked" or "wenyi-red-equipment-blocked" ? Suit.Heart : scene == "wenyi-supply-funded" ? Suit.Spade : new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i % 13 + 1)).ToArray() });
            var roles = scene is "adjust-death" or "wenyi-paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "环淑君真实原生边界", 4, 4, roles, "fixture:hsj-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
        private static void Production(IContentRegistryBuilder b, string bundle, params string[] ids)
        {
            string Read(string part) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + part + ".json") ?? throw new InvalidOperationException("Missing real embedded skill " + bundle + "." + part); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var id in ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
        }
    }
}
