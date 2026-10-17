using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class LeDaQiaoZixiChecks
{
    private const string ZdSkill = "le:zixi", ZdQin = "le:qiqin", ZdOwner = "fixture:ldz-owner", ZdMode = "identity:classic-le-da-qiao-zixi";
    private const string ZdDriver = "fixture:ldz-driver", ZdWatch = "fixture:ldz-watch", ZdQuiet = "fixture:ldz-quiet", ZdGrant = "fixture:ldz-grant", ZdInsert = "fixture:ldz-insert";
    private const string ZdPlace = "skill-program.le-da-qiao.zixi.place", ZdDraw = "skill-program.le-da-qiao.zixi.draw", ZdObtain = "skill-program.le-da-qiao.zixi.obtain";
    private const string ZdEffect = "unique-target-judgment-effects";

    public static void ZixiOriginalQinSubslotsPlacementJudgmentAndMovementCold()
    {
        foreach (var branch in new[] { "confirm", "cancel" })
        {
            var empty = ZdScenario.Start("empty"); empty.OpenPlacement(); var frame = empty.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ZixiPlacement is not null);
            empty.Private(); empty.RejectPrompt(); empty.Cold(); empty.Answer(c => c.Parameters.GetValueOrDefault("branch") == branch); empty.Play();
            empty.Need(empty.E<ZixiPlacementCompletedEvent>().Single(e => e.FrameId == frame.Id) is { Paid: false, EntityCount: 0 } &&
                !empty.E<ZixiPlacementCommittedEvent>().Any() && !empty.G.CardMovements.Any(m => m.Reason.Value == ZdPlace),
                "Both genuine zero-confirm and cancellation finish the optional boundary without payment, false ownership or a repeated offer."); empty.Cold();
        }
        var s = ZdScenario.Start("movement"); var initial = s.Marks(); s.OpenPlacement(); s.Select(initial[0], 0, "indulgence");
        var retainedView = s.G.CreateSnapshot(0); var retained = SnapshotJson.Serialize(retainedView); s.Cold(); s.Select(initial[1], 0, "supplyShortage");
        var draft = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ZixiPlacement is not null).ZixiPlacement!;
        s.Need(draft is { Paid: false, Entities: IList { IsReadOnly: true }, TargetSeat: 0 } && draft.Entities.Count == 2 &&
            s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("branch") != "select") && !s.G.CardMovements.Any(m => m.Reason.Value == ZdPlace),
            "A private two-entity draft freezes distinct actual cards and distinct subslots on one chosen character without moving cards during selection.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "undo" && c.Cards.SequenceEqual([initial[1]])); s.Select(initial[1], 0, "supplyShortage");
        s.Private(); s.RejectPrompt(); var old = s.P!; s.Cold(); s.Confirm(); s.RejectOld(old); s.Reach(() => s.Hold("placed"));
        var paid = s.E<ZixiPlacementPaidEvent>().Single(); var placed = s.E<ZixiQinPlacedEvent>(); var f = s.Frame(paid.FrameId);
        s.Need(f.ZixiPlacement is { Stage: ZixiPlacementStage.MovementChildren, Paid: true, Entities: IList { IsReadOnly: true } } &&
            placed.Length == 2 && placed.Select(e => e.CardId).ToHashSet().SetEquals(initial.Take(2)) &&
            placed.Select(e => e.SlotKind).ToHashSet().SetEquals(new[] { CardKind.Indulgence, CardKind.SupplyShortage }) &&
            placed.All(e => s.G.CardMovements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind && m.From == e.From && m.To == CardLocation.Judgment(0) && m.Reason.Value == ZdPlace) == 1) &&
            !s.E<CardUseDeclaredEvent>().Any() && s.V().Judgment.All(c => c.QinJudgmentSlot is not null) && SnapshotJson.Serialize(retainedView) == retained,
            "One atomic real placement preserves both original printed entities in Judgment and publishes scalar subslots without pretending to use delayed cards.");
        s.Private(); s.Cold(); s.Continue(); s.Play(); s.Gather();
        foreach (var id in initial.Take(2))
        {
            s.Use("transfer-j", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Cards.Contains(id)) == true); s.Private(); s.Cold(); s.Answer(c => c.Cards.Contains(id)); s.Play();
        }
        s.Need(s.V(1).Judgment.Count == 2 && s.V(1).Judgment.All(c => c.QinJudgmentSlot is not null) &&
            !s.Actions().Any(a => a.TargetSeats.Contains(1) && a.Kind is LegalActionKind.Indulgence or LegalActionKind.SupplyShortage) &&
            s.E<ZixiQinPlacedEvent>().Length == 2 && initial.Take(2).All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Judgment(0) && m.To == CardLocation.Judgment(1)) == 1),
            "Real Judgment-to-Judgment transfers retain their original subslots and block both corresponding natural delayed cards without fabricating another placement.");
        var cutoff = s.G.CardMovements.Max(m => m.Sequence); s.End(); s.Play();
        s.Need(s.E<TurnStartedEvent>().Any(e => e.ActorSeat == 1) && s.V(1).Judgment.Count == 2 &&
            !s.G.CardMovements.Any(m => m.Sequence > cutoff && initial.Take(2).Contains(m.CardId) && m.From == CardLocation.Judgment(1)),
            "The actual holder passes its native Judgment phase without judging or consuming either raw Qin entity."); s.Cold();
        s.Use("take-j", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Cards.Contains(initial[0])) == true); s.Private(); s.Cold(); s.Answer(c => c.Cards.Contains(initial[0])); s.Play();
        s.Need(s.V().Hand.Single(c => c.Id == initial[0]).QinJudgmentSlot is null && s.V().Hand.Single(c => c.Id == initial[0]).SkillMarkLabel?.Contains("琴", StringComparison.Ordinal) == true &&
            s.Actions().Any(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([1])),
            "An actual take back to Hand removes only the occupied subslot while retaining global Qin membership and reopens the corresponding delayed target.");
        var dismantle = s.Actions().First(a => a.Kind == LegalActionKind.Dismantlement && a.TargetSeats.SequenceEqual([1]) && a.TargetCardId == initial[1]);
        s.Cold(); s.Native(dismantle); s.Play();
        s.Need(s.G.CardMovements.Any(m => m.CardId == initial[1] && m.From == CardLocation.Judgment(1) && m.To == CardLocation.Processing) &&
            s.G.CardMovements.Count(m => m.CardId == initial[1] && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "A genuine Dismantlement can remove a non-delayed raw Qin from the same public Judgment area through its native two-stage removal.");
        s.End(); s.Play(); s.Need(s.V().Hand.Any(c => c.Id == initial[1] && c.QinJudgmentSlot is null) &&
            s.E<QiqinCollectedEntityEvent>().Any(e => e.CardId == initial[1]), "The shared Qiqin pool really reclaims the discarded original after its slot is removed."); s.Cold();
        ZdDelayedSubslotConsumers();
    }

    public static void ZixiSoleTargetBasicTrickRepeatAndNativeDrawCold()
    {
        foreach (var kind in new[] { CardKind.DrawTwo, CardKind.Peach, CardKind.Alcohol })
        {
            var s = ZdScenario.Start("repeat-" + kind); s.PlaceStart(1, 0); s.Gather();
            if (kind == CardKind.DrawTwo) s.Top(4); if (kind == CardKind.Peach) { s.Use("hurt-three", targets: [0]); s.Play(); }
            var beforeHp = s.V().Hp; var card = s.Actions().First(a => a.Kind == ZdAction(kind)); var movement = s.G.CardMovements.Max(m => m.Sequence);
            int? fengziCost = null; s.Native(card);
            if (kind == CardKind.Peach)
            {
                s.Reach(() => s.OptionalSkill("ol:fengzi")); s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                fengziCost = s.Hand().First(c => c.Kind == CardKind.Dodge).Id;
                s.Reach(() => s.P?.SkillPrompt?.SkillId == "ol:fengzi" && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "extra-effects-rank-guess" && c.Parameters.GetValueOrDefault("branch") == "cost" && c.Cards.SequenceEqual([fengziCost.Value])));
                s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "cost" && c.Cards.SequenceEqual([fengziCost.Value]));
            }
            s.Effect();
            if (kind == CardKind.Peach)
            {
                for (var recovery = 1; recovery <= 3; recovery++)
                {
                    s.Reach(() => s.Hold("repeat-recovery")); var live = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == card.CardId && u.CardKind == CardKind.Peach);
                    s.Need(s.V().Hp == beforeHp + recovery && s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == recovery &&
                        live.ExtraCardEffect is { Source.SkillId: "ol:fengzi" } && live.ZixiEffectRounds is { Grants.Count: 1 } &&
                        !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == live.Id) &&
                        (recovery < 3 ? !s.E<ZixiExtraEffectRoundStartedEvent>().Any(e => e.UseId == live.Id) :
                            live.ExtraCardEffect.WholeGroupRepeated && live.ZixiEffectRounds is { Active: true, ActiveTargetSeat: 0, Started: 1 }),
                        "The original recovery, true Fengzi whole-Use extra recovery and independent Zixi target-local recovery each pause in their own real health child without overwriting either native receipt or finishing the original Peach early.");
                    s.Private(); s.Cold(); s.Continue();
                }
            }
            s.Play(); var issued = s.E<ZixiExtraEffectIssuedEvent>().Single();
            if (kind == CardKind.Peach)
            {
                s.Need(s.E<ExtraCardEffectIssuedEvent>().Where(e => e.CardUseFrameId == issued.UseId).ToArray() is [var global] &&
                    global.Source.SkillId == "ol:fengzi" && global.ActionId == issued.ActionId && global.PaidCardId == fengziCost &&
                    s.E<ExtraEffectsAndRankGuessPaidEvent>().Count(e => e.ProgramFrameId == global.ProgramFrameId && e.Receipt.PaidCardId == fengziCost) == 1 &&
                    s.G.CardMovements.Count(m => m.CardId == fengziCost && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.extra-effect.cost") == 1 &&
                    s.E<ExtraCardEffectStartedEvent>().Count(e => e.CardUseFrameId == issued.UseId && e.ActionId == issued.ActionId && e.TargetSeat == 0 && e.EffectiveKind == CardKind.Peach) == 2 &&
                    s.E<ZixiExtraEffectRoundCompletedEvent>().Count(e => e.UseId == issued.UseId && e.TargetSeat == 0) == 1,
                    "One real Basic-category Fengzi discard and one Zixi grant add two distinct native rounds to the same Peach, without a second declaration, original-card payment or shared-state replacement.");
            }
            s.Need(issued.JudgmentCount == 1 && issued.TargetSeat == 0 && s.E<ZixiExtraEffectRoundStartedEvent>().Count(e => e.UseId == issued.UseId && e.FrameId == issued.FrameId && e.TargetSeat == 0) == 1 &&
                s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardId == card.CardId && e.CardKind == kind) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardId == card.CardId && e.CardKind == kind) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == card.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == card.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
                (kind == CardKind.DrawTwo ? s.G.CardMovements.Count(m => m.Sequence > movement && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 4 :
                    kind == CardKind.Peach ? s.V().Hp == beforeHp + 3 && s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 3 :
                    s.V().HasAlcoholEffect && s.E<AlcoholAppliedEvent>().Count(e => e.ResolutionId == issued.UseId && e.SourceSeat == 0 && e.DamageBonus == 1) == 2),
                "The complete dated version repeats ordinary DrawTwo and self Alcohol, while actual Fengzi plus Zixi resolve self Peach three times under one original Use and one original physical cost."); s.Cold();
        }
        var draw = ZdScenario.Start("draw-two"); draw.PlaceStart(2, 1); draw.Gather(); draw.Top(2);
        draw.Native(draw.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]))); draw.Effect(); draw.Reach(() => draw.Hold("draw"));
        var started = draw.E<ZixiEffectsStartedEvent>().Single();
        draw.Need(draw.Frame(started.FrameId).ZixiEffects is { JudgmentCount: 2 } &&
            draw.G.CardMovements.Count(m => m.Reason.Value == ZdDraw && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 2 &&
            !draw.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == started.UseId),
            "Two real Judgment entities start exactly one native Draw2 and retain the original Slash while its actual gain children are suspended.");
        draw.Private(); draw.RejectPrompt(); draw.Cold(); draw.Continue(); draw.Play();
        draw.Need(draw.E<ZixiEffectsDrawReturnedEvent>().Single(e => e.FrameId == started.FrameId) is { RequestedCount: 2, ActualCount: 2, ReplacementCount: 0 } &&
            draw.E<ZixiEffectsCompletedEvent>().Count(e => e.FrameId == started.FrameId && e.UseId == started.UseId && e.JudgmentCount == 2) == 1 &&
            !draw.E<ZixiExtraEffectIssuedEvent>().Any() && !draw.E<ZixiEffectsDamageIssuedEvent>().Any(), "The two-card branch returns once and never accumulates the one- or three-card outcomes."); draw.Cold();

        var replacement = ZdScenario.Start("replacement"); replacement.PlaceStart(2, 1); replacement.Gather(); replacement.Top(4);
        foreach (var id in replacement.Hand().Where(c => c.Kind is not (CardKind.Slash or CardKind.Nullification) && !replacement.Marks().Contains(c.Id)).Take(2).Select(c => c.Id).ToArray()) replacement.Give(id, 2);
        replacement.Use("grant-zenrun"); replacement.Play(); replacement.Native(replacement.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]))); replacement.Effect(); replacement.Reach(() => replacement.OptionalSkill("ol:zenrun"));
        replacement.Private(); replacement.Cold(); replacement.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        replacement.Reach(() => replacement.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([2])) == true);
        replacement.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([2]));
        for (var i = 0; i < 2; i++) replacement.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick");
        replacement.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); replacement.Reach(() => replacement.Hold("replacement")); replacement.Private(); replacement.Cold(); replacement.Continue(); replacement.Play();
        replacement.Need(replacement.E<ZixiEffectsDrawReturnedEvent>().Single() is { RequestedCount: 2, ActualCount: 0, ReplacementCount: 2 } &&
            replacement.G.CardMovements.Count(m => m.From == CardLocation.Hand(2) && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.sun-hong.zenrun.obtain") == 2 &&
            replacement.E<ZixiEffectsCompletedEvent>().Length == 1,
            "The same native Draw2's true Zenrun replacement keeps Actual0 separate from two original foreign entities and drains native compensation before returning."); replacement.Cold();
        ZdFloodTargetLocalRound();
    }

    public static void ZixiThreeJudgmentEquipmentObtainDamageAndPaidChildrenCold()
    {
        var s = ZdScenario.Start("three"); s.PlaceStart(2, 0); s.Gather();
        var lion = s.Hand().First(c => c.Kind == CardKind.SilverLion).Id; var ox = s.Hand().First(c => c.Kind == CardKind.WoodenOx).Id; s.Place(lion, 0); s.Place(ox, 0);
        var grain = s.Hand().First(c => c.Kind == CardKind.Crossbow).Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Use("hurt-three", targets: [0]); s.Play(); s.PlaceEnd(1, 0); var original = s.V().Judgment.Select(c => c.Id).Concat(s.V().Equipment.Select(c => c.Id)).ToHashSet(); var hp = s.V().Hp;
        var peach = s.Actions().First(a => a.Kind == LegalActionKind.Peach); s.Native(peach); s.Effect(); s.Reach(() => s.Hold("recovered"));
        var started = s.E<ZixiEffectsStartedEvent>().Single(); var receipt = s.Frame(started.FrameId).ZixiEffects!;
        s.Need(receipt is { JudgmentCount: 3, Stage: ZixiEffectsStage.ObtainChildren, Entities: IList { IsReadOnly: true }, ActualCount: 5 } && receipt.Entities.Select(e => e.CardId).ToHashSet().SetEquals(original) &&
            s.E<ZixiEffectsObtainPaidEvent>().Single(e => e.FrameId == started.FrameId) is { SelectedCount: 5, ActualCount: 5 } &&
            s.E<ZixiEffectsEntityObtainedEvent>().Where(e => e.FrameId == started.FrameId).Select(e => e.CardId).ToHashSet().SetEquals(original) &&
            s.E<SilverLionRemovedRecoveryEvent>().Count(e => e.Reason.Value == ZdObtain) == 1 && s.V().Hp == hp + 1 && !s.E<ZixiEffectsDamageIssuedEvent>().Any(),
            "The three-card branch genuinely obtains all three original Judgment and both Equipment entities, and the Lion child is fully native before any new three-damage issue.");
        s.Private(); s.Cold(); s.Continue(); s.Reach(() => s.Hold("obtained"));
        s.Need(s.G.CardMovements.Count(m => m.CardId == grain && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1 &&
            !s.E<ZixiEffectsEntityObtainedEvent>().Any(e => e.CardId == grain) && !s.E<ZixiEffectsDamageIssuedEvent>().Any(),
            "The actual Ox grain spill belongs to the paid child tree rather than the obtained-card count, and it cannot let damage overtake the original movement children.");
        s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<ZixiEffectsDamageIssuedEvent>().Single() is { SourceSeat: 0, TargetSeat: 0, Amount: 3 } &&
            s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 3) == 1 &&
            s.E<ZixiEffectsCompletedEvent>().Count(e => e.FrameId == started.FrameId && e.JudgmentCount == 3) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == started.UseId && e.CardId == peach.CardId) == 1 &&
            original.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Hand(0) && m.Reason.Value == ZdObtain) == 1) &&
            s.V().Judgment.Count == 0 && s.V().Equipment.Count == 0 && s.V().Hp == hp - 1,
            "After Lion and Ox children the original owner takes exactly three native damage and the still-owning Peach resolves once, without old-version Judgment discard or a duplicated original cost."); s.Cold();
        ZdPlacementEquippedQinChildren();
    }

    public static void ZixiFinalTargetsResponsesSourceRightsAndInputPrivacyCold()
    {
        var s = ZdScenario.Start("multi"); s.PlaceStart(1, 1); s.Gather();
        var chain = s.Actions().First(a => a.Kind == LegalActionKind.IronChain && a.TargetSeats.SequenceEqual([1, 2])); s.Native(chain); s.Play();
        s.Use("damage", targets: [1]); s.Play(); s.Need(!s.E<ZixiEffectsStartedEvent>().Any() && !s.E<ZixiExtraEffectIssuedEvent>().Any(), "Two final designated characters and a child skill's naked damage cannot become a fictitious sole-target card Use.");
        s.Use("duel", targets: [1]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }); s.Private(); s.Cold(); s.Answer(c => c.Cards.Count == 1); s.Play();
        s.Need(s.E<DuelResponseEvent>().Any(e => e.ResponderSeat == 0 && e.UsedSlash) && !s.E<ZixiEffectsStartedEvent>().Any(e => e.Source.OwnerSeat == 0), "The real Duel Slash is Played and does not inherit a Basic true-Use target trigger."); s.Cold();

        s = ZdScenario.Start("rescue"); s.PlaceStart(1, 0); s.Gather(); s.Use("dying"); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }); var dying = s.G.ResolutionStack.OfType<DyingFrame>().Single().Id;
        s.Private(); s.Cold(); var rescue = s.P!.Choices.First(c => c.Cards.Count == 1 && s.Hand().Any(h => h.Id == c.Cards[0] && h.Kind == CardKind.Peach)); s.Answer(c => c.Id == rescue.Id); s.Effect(); s.Play();
        var rescueGrant = s.E<ZixiExtraEffectIssuedEvent>().Single();
        s.Need(s.V().Hp == 2 && s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 2 &&
            s.E<DyingResponseEvent>().Count(e => e.ResolutionId == dying && e.ResponderSeat == 0 && e.UsedPeach) == 1 && s.E<DyingResolvedEvent>().Count(e => e.ResolutionId == dying && e.Survived) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == rescueGrant.UseId) == 1 && s.E<ZixiExtraEffectRoundStartedEvent>().Count(e => e.UseId == rescueGrant.UseId) == 1,
            "A real self-Peach rescue has one actual character target and completes two native recoveries before returning its single Dying response."); s.Cold();

        foreach (var scene in new[] { "paid-loss", "paid-death" })
        {
            s = ZdScenario.Start(scene); s.PlaceStart(2, 1); s.Gather(); s.Top(2); s.Native(s.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]))); s.Effect(); s.Reach(() => s.Hold("draw"));
            var started = s.E<ZixiEffectsStartedEvent>().Single(); s.Private(); s.RejectPrompt(); s.Cold(); var old = s.P!; s.Continue(); s.RejectOld(old);
            s.Reach(() => s.E<ZixiEffectsCompletedEvent>().Any(e => e.FrameId == started.FrameId)); s.PlayOrDead();
            s.Need(s.E<ZixiEffectsDrawReturnedEvent>().Single(e => e.FrameId == started.FrameId) is { RequestedCount: 2, ActualCount: 2 } &&
                s.E<ZixiEffectsCompletedEvent>().Count(e => e.FrameId == started.FrameId && e.UseId == started.UseId) == 1 &&
                s.E<ProgramBindingResolvedEvent>().Count(e => e.FrameId == started.FrameId && e.SkillId == ZdSkill && e.BindingId == ZdEffect && e.Activated && e.Completed) == 1 &&
                (scene == "paid-death" ? !s.V().IsAlive : s.V().Skills?.Any(k => k.ContentId == ZdSkill) != true) &&
                s.G.CardMovements.Count(m => m.Reason.Value == ZdDraw && m.To == CardLocation.Hand(0)) == 2,
                "Once accepted, native Draw2 and its exact finalized-Use child return once after genuine source loss or death, without reoffering, redrawing or substituting another source."); s.Cold();
        }

        s = ZdScenario.Start("counterspell"); s.PlaceStart(1, 0); s.Gather(); s.Top(4); var card = s.Actions().First(a => a.Kind == LegalActionKind.DrawTwo); var before = s.G.CardMovements.Max(m => m.Sequence);
        s.Native(card); s.Effect(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.Nullification }); s.Private(); s.Cold(); s.Answer(c => c.Cards.Count == 1); s.Play();
        var grant = s.E<ZixiExtraEffectIssuedEvent>().Single();
        s.Need(s.E<NullificationResolvedEvent>().Any(e => e.EffectCardId == card.CardId && e.EffectNullified) && s.E<ZixiExtraEffectRoundStartedEvent>().Count(e => e.UseId == grant.UseId) == 1 &&
            s.G.CardMovements.Count(m => m.Sequence > before && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 2 &&
            s.G.CardMovements.Count(m => m.CardId == card.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == grant.UseId) == 1,
            "A genuine first-round Nullification preserves the one-card payment while the independently repeated native trick effect resolves its remaining round."); s.Cold();

        s = ZdScenario.Start("borrowed"); s.PlaceStart(1, 1); s.Gather(); s.Place(s.Hand().First(c => c.Kind == CardKind.Crossbow).Id, 1);
        foreach (var id in s.Hand().Where(c => c.Kind == CardKind.Slash).Take(2).Select(c => c.Id).ToArray()) s.Give(id, 1);
        card = s.Actions().First(a => a.Kind == LegalActionKind.BorrowedSword && a.TargetSeats.SequenceEqual([1, 0])); s.Native(card); s.Effect();
        for (var i = 0; i < 2; i++) { s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); s.Private(); s.Cold(); s.Answer(c => c.Cards.Count == 1); }
        s.Play();
        s.Need(s.E<ZixiEffectsStartedEvent>().Single() is { TargetSeat: 1, JudgmentCount: 1, CardKind: CardKind.BorrowedSword } &&
            s.E<ZixiExtraEffectRoundStartedEvent>().Length == 1,
            "A real Borrowed Sword's designated character is its one weapon owner; the separate following Slash-request seat does not falsely turn it into two card targets."); s.Cold();

        s = ZdScenario.Start("redirect"); s.PlaceStart(1, 0); s.Gather(); var hp0 = s.V().Hp; var hp3 = s.V(3).Hp; s.Use("offer", targets: [1]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("offer-option") == "target-0") == true); s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("offer-option") == "target-0");
        s.Reach(() => s.OptionalSkill("classic:liuli")); s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "classic:liuli" && s.P.Choices.Any(c => c.Targets.SequenceEqual([3])));
        s.Private(); s.Cold(); s.Answer(c => c.Targets.SequenceEqual([3]));
        var liuliCost = s.Hand().First(c => c.Kind == CardKind.Crossbow).Id;
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "classic:liuli" && s.P.Choices.Any(c => c.Cards.SequenceEqual([liuliCost])));
        s.Private(); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([liuliCost])); s.Play();
        var redirectedUse = s.E<CardUseDeclaredEvent>().Single(e => e.SourceSeat == 1 && e.CardKind == CardKind.Slash);
        var liuli = s.E<ProgramBindingStartedEvent>().Single(e => e.SkillId == "classic:liuli" && e.BindingId == "redirect-slash" && e.OwnerSeat == 0 && e.Window == SkillProgramTriggerWindow.SlashTargetRedirecting);
        s.Need(s.E<ProgramBindingResolvedEvent>().Count(e => e.FrameId == liuli.FrameId && e.SkillId == liuli.SkillId && e.BindingId == liuli.BindingId &&
                e.SkillInstanceId == liuli.SkillInstanceId && e.OwnerSeat == 0 && e.Window == liuli.Window && e.Activated && e.Completed) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == liuliCost && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.classic:liuli.SelectAndMoveOwnedCard") == 1 &&
            !s.E<ZixiEffectsStartedEvent>().Any() && !s.E<ZixiExtraEffectIssuedEvent>().Any() &&
            !s.E<ZixiExtraEffectRoundStartedEvent>().Any() && !s.E<ZixiExtraEffectRoundCompletedEvent>().Any() &&
            s.V().Hp == hp0 && s.V(3).Hp == hp3 - 1 && s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 1 && e.TargetSeat == 3 && e.Amount == 1) == 1 &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == redirectedUse.ResolutionId && e.SourceSeat == 1) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == redirectedUse.ResolutionId && e.CardId == redirectedUse.CardId && e.CardKind == CardKind.Slash) == 1,
            "The genuine ordinary Liuli pays and completes once before final targets: target zero's Judgment card cannot qualify an effect after the actual sole target becomes Judgment-empty target three. That one original AI Slash damages target three only once."); s.Cold();
        ZdLateAddedTarget();
        ZdLateReplacedTarget();
    }

    private static void ZdFloodTargetLocalRound()
    {
        var s = ZdScenario.Start("flood"); s.PlaceStart(1, 1); s.Gather();
        var discards = s.Hand().Where(c => c.Kind is CardKind.Crossbow or CardKind.Dodge && !s.Marks().Contains(c.Id)).Take(2).Select(c => c.Id).ToArray();
        s.Need(discards.Length == 2, "The native first-target Flood discard has two real available Hand materials rather than fabricated or empty costs.");
        foreach (var id in discards) s.Give(id, 1);
        var hp = s.V(1).Hp; var flood = s.G.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == "wumiao:juewu" &&
            a.PlayedCardKind == CardKind.WumiaoFlood && a.TargetSeats.SequenceEqual([1]));
        var material = flood.CardId!.Value; var printed = s.Hand().Single(c => c.Id == material).Kind;
        s.Native(flood); s.Effect(); s.Reach(() => s.Hold("flood-hit"));
        var grant = s.E<ZixiExtraEffectIssuedEvent>().Single(); var first = s.E<WumiaoFloodEffectStartedEvent>().Single(e => e.UseId == grant.UseId);
        s.Need(first is { TargetSeat: 1, TargetIndex: 0, ExtraEffectOrdinal: 0 } && first.ActionId == grant.ActionId &&
            s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == grant.UseId).WumiaoFlood is { Stage: WumiaoFloodStage.AwaitingDamage, TargetSeat: 1, TargetIndex: 0, ExtraEffectOrdinal: 0 } &&
            s.V(1).Hp == hp - 1 && !s.E<WumiaoFloodDiscardPaidEvent>().Any(e => e.UseId == grant.UseId) &&
            !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == grant.UseId),
            "The first real thunder damage child pauses before the original ordinal-zero mandatory discard or the original Use can retire.");
        s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); s.Reach(() => s.Hold("flood-hit"));
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == grant.UseId);
        s.Need(s.E<WumiaoFloodEffectStartedEvent>().Where(e => e.UseId == grant.UseId).Select(e => (e.TargetSeat, e.TargetIndex, e.ExtraEffectOrdinal)).SequenceEqual(new[] { (1, 0, 0), (1, 0, 1) }) &&
            use.WumiaoFlood is { Stage: WumiaoFloodStage.AwaitingDamage, TargetSeat: 1, TargetIndex: 0, ExtraEffectOrdinal: 1 } &&
            use.ZixiEffectRounds is { Active: true, ActiveTargetIndex: 0, ActiveTargetSeat: 1, Started: 1 } &&
            s.E<WumiaoFloodDiscardPaidEvent>().Count(e => e.UseId == grant.UseId) == 1 && s.V(1).Hp == hp - 2 &&
            !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == grant.UseId),
            "The target-local extra round re-enters the same native Flood at the same original target ordinal, after the first complete discard subtree and without a second physical payment.");
        s.Private(); s.Cold(); var old = s.P!; s.Continue(); s.RejectOld(old); s.Play();
        var paid = s.E<WumiaoFloodDiscardPaidEvent>().Where(e => e.UseId == grant.UseId).ToArray();
        s.Need(paid.Length == 2 && paid.All(e => e.TargetSeat == 1 && e.From == CardLocation.Hand(1)) &&
            paid.Select(e => e.CardId).ToHashSet().SetEquals(discards) && paid.Select(e => e.NullificationId).Distinct().Count() == 2 &&
            paid.All(e => s.G.CardMovements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId &&
                m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == "card.effect.wumiao-flood.discard") == 1) &&
            s.E<WumiaoFloodDamageReturnedEvent>().Count(e => e.UseId == grant.UseId && e.TargetSeat == 1 && e.TargetIndex == 0) == 2 &&
            !s.E<WumiaoFloodDrawReturnedEvent>().Any(e => e.UseId == grant.UseId) &&
            s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1 && e.Nature == DamageNature.Thunder) == 2 &&
            s.E<ZixiExtraEffectIssuedEvent>().Count(e => e.UseId == grant.UseId) == 1 &&
            s.E<ZixiExtraEffectRoundStartedEvent>().Count(e => e.UseId == grant.UseId && e.TargetSeat == 1) == 1 &&
            s.E<ZixiExtraEffectRoundCompletedEvent>().Count(e => e.UseId == grant.UseId && e.TargetSeat == 1) == 1 &&
            s.E<JuewuUseIssuedEvent>().Where(e => e.UseId == grant.UseId).ToArray() is [var issued] &&
            issued.CardId == material && issued.PrintedKind == printed && issued.PrintedRank == 7 && issued.ForeignHandEntrySequence is not null &&
            issued.OutputKind == CardKind.WumiaoFlood && issued.ActionId == grant.ActionId &&
            s.E<JuewuMaterialPaidEvent>().Count(e => e.UseId == grant.UseId && e.CardId == material && e.From == CardLocation.Hand(0) && e.To == CardLocation.Processing) == 1 &&
            s.E<JuewuUseFinishedEvent>().Count(e => e.UseId == grant.UseId && e.OutputKind == CardKind.WumiaoFlood) == 1 &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == grant.UseId && e.CardId == material && e.CardKind == CardKind.WumiaoFlood) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == grant.UseId && e.CardId == material && e.CardKind == CardKind.WumiaoFlood) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.CardKind == printed && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.CardKind == printed && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "Two complete real Flood damage-and-discard rounds stay on ordinal zero under one original converted Use; they never substitute the ordinal-one draw branch, rename the raw entity, or charge the Juewu material twice."); s.Cold();
    }

    private static void ZdLateReplacedTarget()
    {
        var s = ZdScenario.Start("late-redirect"); s.PlaceStart(1, 2); s.Gather();
        var top = s.Hand().Where(c => c.Kind == CardKind.Slash && !s.Marks().Contains(c.Id)).Take(3).Select(c => c.Id).ToArray();
        s.Need(top.Length == 3, "The late replacement draw has three original real cards on top without consuming its separate Zhenwei or Liuli costs.");
        s.Use("top", top); s.Play();
        var hp0 = s.V().Hp; var hp2 = s.V(2).Hp; var hp3 = s.V(3).Hp;
        var zhenweiCost = s.Hand().First(c => c.Kind == CardKind.Dismantlement && !s.Marks().Contains(c.Id)).Id;
        var liuliCost = s.Hand().First(c => c.Kind == CardKind.Crossbow && !s.Marks().Contains(c.Id)).Id;
        s.Need(hp2 < hp0 && s.V(2).Judgment.Count == 1 && s.V().Judgment.Count == 0 && s.V(3).Judgment.Count == 0,
            "The original target alone has one actual Judgment card and strictly less HP than the real Zhenwei protector.");
        s.Use("offer", targets: [1]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("offer-option") == "target-2") == true);
        s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("offer-option") == "target-2");
        s.Reach(() => s.OptionalSkill("ol:zhenwei"));
        var issued = s.E<ZixiExtraEffectIssuedEvent>().Where(e => e.Source.OwnerSeat == 1).ToArray();
        s.Need(issued is [{ TargetSeat: 2, JudgmentCount: 1 }] &&
            s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == issued[0].UseId) is { ZixiEffectRounds: { Started: 0 } original } use &&
            use.SourceSeat == 1 && use.TargetSeats.SequenceEqual([2]) && original.Grants is [{ TargetSeat: 2, CurrentTargetSeat: 2 }] &&
            s.E<ZixiEffectsStartedEvent>().Count(e => e.UseId == issued[0].UseId && e.Source.OwnerSeat == 1) == 1,
            "The source's real optional one-Judgment grant is issued on target two before the lower-priority Zhenwei observer starts.");
        var grant = issued[0];
        s.Private(); s.RejectPrompt(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "ol:zhenwei" && s.P.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == "sole-target-protection" && c.Parameters.GetValueOrDefault("branch") == "cost" && c.Cards.SequenceEqual([zhenweiCost])));
        s.Private(); s.Cold(); var costPrompt = s.P!;
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "cost" && c.Cards.SequenceEqual([zhenweiCost]));
        s.RejectOld(costPrompt);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "ol:zhenwei" && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "substitute"));
        s.Need(s.E<SoleTargetProtectionPaidEvent>().Where(e => e.Receipt.CardUseFrameId == grant.UseId).ToArray() is [var paid] &&
            paid.Receipt.Source.OwnerSeat == 0 && paid.Receipt.ProtectedSeat == 2 && paid.Receipt.Cost?.CardId == zhenweiCost &&
            s.G.CardMovements.Count(m => m.CardId == zhenweiCost && m.From == CardLocation.Hand(0) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.sole-target-protection.cost") == 1,
            "Zhenwei pays exactly one original private Hand card before choosing its native draw-and-substitute branch.");
        s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "substitute");
        s.Reach(() => s.OptionalSkill("classic:liuli"));
        var replacement = s.E<SoleTargetReplacedEvent>().Where(e => e.CardUseFrameId == grant.UseId).ToArray();
        var redirected = s.G.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.SoleTargetRedirectReturn?.Request.CardUseFrameId == grant.UseId);
        var parent = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == grant.UseId);
        s.Need(replacement is [{ PreviousTargetSeat: 2, NewTargetSeat: 0 }] && replacement[0].ActionId == grant.ActionId &&
            replacement[0].BeforeTargets.SequenceEqual([2]) && replacement[0].AfterTargets.SequenceEqual([0]) &&
            redirected.SoleTargetRedirectReturn!.FinalizedCursor.CandidateIndex > 0 &&
            parent.TargetSeats.SequenceEqual([0]) && parent.ZixiEffectRounds is { Started: 0, Grants: [{ TargetSeat: 2, CurrentTargetSeat: 0 }] } &&
            s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                m.Reason.Value == "skill-program.sole-target-protection.draw") == 1 &&
            s.E<ZixiExtraEffectIssuedEvent>().Count(e => e.UseId == grant.UseId) == 1,
            "After a real draw, native Zhenwei replaces two with zero and reopens its saved finalized cursor; the existing grant follows that exact mutation without recounting zero's empty Judgment area.");
        s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "classic:liuli" && s.P.Choices.Any(c => c.Targets.SequenceEqual([3])));
        s.Private(); s.Cold(); s.Answer(c => c.Targets.SequenceEqual([3]));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "classic:liuli" && s.P.Choices.Any(c => c.Cards.SequenceEqual([liuliCost])));
        s.Private(); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([liuliCost])); s.Play();
        var after = s.E<SoleTargetAfterProtectionRedirectedEvent>().Where(e => e.CardUseFrameId == grant.UseId).ToArray();
        var liuli = s.E<ProgramBindingStartedEvent>().Where(e => e.SkillId == "classic:liuli" && e.OwnerSeat == 0 && e.Window == SkillProgramTriggerWindow.SlashTargetRedirecting).ToArray();
        s.Need(after is [{ PreviousTargetSeat: 0, NewTargetSeat: 3 }] && after[0].ActionId == grant.ActionId &&
            after[0].BeforeTargets.SequenceEqual([0]) && after[0].AfterTargets.SequenceEqual([3]) &&
            liuli is [var binding] && after[0].ProgramFrameId == binding.FrameId &&
            s.E<ProgramBindingResolvedEvent>().Count(e => e.FrameId == binding.FrameId && e.SkillInstanceId == binding.SkillInstanceId &&
                e.SkillId == binding.SkillId && e.BindingId == binding.BindingId && e.Window == binding.Window && e.Activated && e.Completed) == 1 &&
            s.E<ZixiExtraEffectRedirectedEvent>().Where(e => e.UseId == grant.UseId && e.FrameId == grant.FrameId).ToArray() is
                [{ PreviousSeat: 0, TargetSeat: 3 }] &&
            s.E<ZixiExtraEffectRoundStartedEvent>().Count(e => e.UseId == grant.UseId && e.TargetSeat == 3) == 1 &&
            s.E<ZixiExtraEffectRoundCompletedEvent>().Count(e => e.UseId == grant.UseId && e.TargetSeat == 3) == 1 &&
            s.E<ZixiExtraEffectIssuedEvent>().Count(e => e.UseId == grant.UseId) == 1 &&
            s.E<ZixiEffectsStartedEvent>().Count(e => e.UseId == grant.UseId) == 1 &&
            s.E<SoleTargetProtectionPaidEvent>().Count(e => e.Receipt.CardUseFrameId == grant.UseId) == 1 &&
            s.E<SoleTargetReplacedEvent>().Count(e => e.CardUseFrameId == grant.UseId) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == liuliCost && m.From == CardLocation.Hand(0) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.classic:liuli.SelectAndMoveOwnedCard") == 1 &&
            s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 1 && e.TargetSeat == 3 && e.Amount == 1) == 2 &&
            s.V().Hp == hp0 && s.V(2).Hp == hp2 && s.V(3).Hp == hp3 - 2 &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == grant.UseId && e.SourceSeat == 1 && e.CardKind == CardKind.Slash) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == grant.UseId && e.CardKind == CardKind.Slash) == 1,
            "One accepted target-two grant follows paid Zhenwei to zero and paid native Liuli to three: target three takes exactly two original-source Slash hits under one declared and finished Use; neither empty-Judgment replacement gets a second grant or repeated cost."); s.Cold();
    }

    private static void ZdLateAddedTarget()
    {
        var s = ZdScenario.Start("late-target"); s.PlaceStart(1, 1); s.Gather();
        var hp1 = s.V(1).Hp; var hp3 = s.V(3).Hp;
        var slash = s.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
        s.Native(slash); s.Effect(); s.Reach(() => s.P?.SkillPrompt?.SkillId == "fixture:ldz-late");
        var grant = s.E<ZixiExtraEffectIssuedEvent>().Single();
        s.Need(grant.TargetSeat == 1 && s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == grant.UseId).TargetSeats.SequenceEqual([1]),
            "The genuine one-target grant is already issued before the lower-priority native target-addition instruction chooses anybody else.");
        s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([3])); s.Play();
        s.Need(s.V(1).Hp == hp1 - 2 && s.V(3).Hp == hp3 - 1 &&
            s.E<ZixiExtraEffectRoundStartedEvent>().Where(e => e.UseId == grant.UseId).ToArray() is [var round] && round.TargetSeat == 1 &&
            s.E<ZixiExtraEffectRoundCompletedEvent>().Count(e => e.UseId == grant.UseId && e.TargetSeat == 1) == 1 &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == grant.UseId && e.CardId == slash.CardId) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == grant.UseId && e.CardId == slash.CardId) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == slash.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == slash.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "A target genuinely added after acceptance receives its ordinary Slash once; only the original accepted target receives the extra native round, under the same original Use and one cost."); s.Cold();
    }

    private static void ZdDelayedSubslotConsumers()
    {
        var s = ZdScenario.Start("delayed-subslots"); var qin = s.Marks()[0]; s.OpenPlacement(); s.Select(qin, 1, "lightning"); s.Confirm(); s.Play();
        s.Need(s.V(1).Judgment.Single(c => c.Id == qin) is { Kind: CardKind.Indulgence, QinJudgmentSlot: CardKind.Lightning } &&
            s.Actions().Any(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([1])),
            "An actual printed Indulgence used only as a Lightning-slot Qin cannot falsely occupy the separate Indulgence slot.");
        var real = s.Actions().First(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([2])); s.Native(real); s.Play();
        var activation = s.G.GetHumanLegalActions().Single(a => a.ProgramSkillId == "boundary:guose"); var diamond = activation.SelectableCardIds.First();
        s.Accept(new UseProgramSkillCommand(0, "boundary:guose", "diamond-delayed", [diamond], [], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "diamond-delayed") == true);
        s.Need(s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "use" && c.Targets.SequenceEqual([1])) &&
            s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Cards.SequenceEqual([real.CardId!.Value]) && c.Targets.SequenceEqual([2])) &&
            !s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Cards.Contains(qin)),
            "Real boundary Guose offers the genuine delayed Indulgence for discard while excluding the printed-Indulgence Qin; its unoccupied true Indulgence slot remains usable.");
        s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Cards.SequenceEqual([real.CardId!.Value])); s.Play();
        var second = s.Actions().First(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([2])); s.Native(second); s.Play();
        var top = s.Hand().First().Id; s.Use("top", [top]); s.Play(); s.End(); s.Reach(() => s.OptionalSkill("friend:yangming"));
        s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "friend:yangming" && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "friend-pang-tong"));
        s.Need(s.P!.Choices.Any(c => c.Cards.SequenceEqual([top]) && c.Targets.SequenceEqual([1])) &&
            !s.P.Choices.Any(c => c.Cards.SequenceEqual([top]) && c.Targets.SequenceEqual([2])) &&
            s.G.CardMovements.Count(m => m.CardId == top && m.From == CardLocation.DrawPile && m.To.Zone == CardZoneKind.PublicPersistentPile && m.Reason.Value == "friend.yangming.place") == 1,
            "The real Yangming public-pile original Indulgence uses occupied subslots instead of printed Qin kind: Lightning Qin permits target one, while the genuine existing Indulgence excludes target two.");
        var yangmingFrame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "friend:yangming" && f.FriendPangTong is { Stage: FriendPangTongStage.Use });
        var yangmingStart = s.E<ProgramBindingStartedEvent>().Single(e => e.FrameId == yangmingFrame.Id && e.SkillId == yangmingFrame.SkillId &&
            e.BindingId == yangmingFrame.TriggerId && e.SkillInstanceId == yangmingFrame.SkillInstanceId && e.OwnerSeat == 0 && e.Window == SkillProgramTriggerWindow.PlayEnding);
        s.Private(); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([top]) && c.Targets.SequenceEqual([1]));
        s.Reach(() => s.E<CardUseFinishedEvent>().Any(e => e.CardId == top && e.CardKind == CardKind.Indulgence) &&
            s.E<ProgramBindingResolvedEvent>().Any(e => e.FrameId == yangmingStart.FrameId && e.SkillId == yangmingStart.SkillId && e.BindingId == yangmingStart.BindingId &&
                e.SkillInstanceId == yangmingStart.SkillInstanceId && e.OwnerSeat == yangmingStart.OwnerSeat && e.Window == yangmingStart.Window && e.Activated && e.Completed));
        var yangmingUse = s.E<FriendYangmingUseIssuedEvent>().Single(e => e.OwnerSeat == 0 && e.Return.ProgramFrameId == yangmingFrame.Id && e.Return.CardId == top);
        s.Need(s.G.CardMovements.Count(m => m.CardId == top && m.From == CardLocation.Processing && m.To == CardLocation.Judgment(1)) == 1 &&
            s.V(1).Judgment.Any(c => c.Id == qin && c.QinJudgmentSlot == CardKind.Lightning) &&
            s.E<CardUseFinishedEvent>().Count(e => e.CardId == top && e.CardKind == CardKind.Indulgence) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == yangmingUse.CardUseFrameId && e.CardId == top && e.CardKind == CardKind.Indulgence) == 1 &&
            yangmingUse.Return.SkillInstanceId == yangmingFrame.SkillInstanceId && yangmingUse.Return.GameplayHash == yangmingFrame.GameplayHash &&
            s.E<ProgramBindingResolvedEvent>().Count(e => e.FrameId == yangmingStart.FrameId && e.SkillId == yangmingStart.SkillId && e.BindingId == yangmingStart.BindingId &&
                e.SkillInstanceId == yangmingStart.SkillInstanceId && e.OwnerSeat == yangmingStart.OwnerSeat && e.Window == yangmingStart.Window && e.Activated && e.Completed) == 1,
            "The accepted real public-pile delayed Use completes beside the retained Qin, preserving both raw identities and one original physical payment; the same real Yangming binding automatically completes once when this used suit leaves no further legal card."); s.Cold();
    }

    private static void ZdPlacementEquippedQinChildren()
    {
        foreach (var kind in new[] { CardKind.SilverLion, CardKind.WoodenOx })
        {
            var scene = kind == CardKind.SilverLion ? "placement-lion" : "placement-ox";
            var s = ZdScenario.Start(scene); var marked = s.Marks();
            s.OpenPlacement(); s.Confirm(); s.Play(); s.Gather();
            var qin = s.Hand().First(card => marked.Contains(card.Id) && card.Kind == kind).Id;
            s.Native(s.Actions().Single(action => action.Kind == LegalActionKind.Equip && action.CardId == qin)); s.Play();
            s.Need(s.V().Equipment.Any(card => card.Id == qin && card.Kind == kind) &&
                s.E<QiqinInitialEntityMarkedEvent>().Count(e => e.OriginalOwnerSeat == 0 && e.CardId == qin && e.PrintedKind == kind) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.CardId == qin && e.CardKind == kind) == 1,
                "The actual initial Qin entity is genuinely equipped through its original native Use before any Judgment placement.");
            int? grain = null;
            if (kind == CardKind.SilverLion) { s.Use("hurt-three", targets: [0]); s.Play(); }
            else
            {
                grain = s.Hand().First(card => !marked.Contains(card.Id)).Id;
                s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Value], [], s.G.Revision, s.P!.PromptId)); s.Play();
                s.Need(s.V().WoodenOxGrain?.Any(card => card.Id == grain.Value) == true &&
                    s.G.CardMovements.Count(move => move.CardId == grain.Value && move.From == CardLocation.Hand(0) &&
                        move.To == CardLocation.WoodenOxGrain(0) && move.Reason == CardMoveReasons.WoodenOxStore) == 1,
                    "The equipment cost owns a genuine native stored grain entity rather than a simulated spill or Qin primary card.");
            }
            var hp = s.V().Hp;
            s.End(); s.Reach(() => s.Optional("end-place-qin")); s.OpenPlacement();
            s.Select(qin, 1, kind == CardKind.SilverLion ? "indulgence" : "supplyShortage");
            var originalPrompt = s.P!; s.Private(); s.RejectPrompt(); s.Cold(); s.Confirm(); s.RejectOld(originalPrompt);
            var paid = s.E<ZixiPlacementPaidEvent>().Single();
            var observed = new HashSet<string>(StringComparer.Ordinal);
            for (var step = 0; step < 3; step++)
            {
                s.Reach(() => s.Hold("placed") || s.Hold("recovered") || s.Hold("placement-spill") ||
                    s.E<ZixiPlacementCompletedEvent>().Any(e => e.FrameId == paid.FrameId));
                if (s.E<ZixiPlacementCompletedEvent>().Any(e => e.FrameId == paid.FrameId)) break;
                var option = s.P!.Choices.Single().Parameters["option-id"];
                s.Need(observed.Add(option) && s.Frame(paid.FrameId).ZixiPlacement is
                    { Paid: true, Stage: ZixiPlacementStage.MovementChildren, TailSealed: true, Entities: IList { IsReadOnly: true }, Grains: IList { IsReadOnly: true } } receipt &&
                    receipt.Entities.Count == 1 && receipt.Entities[0].CardId == qin &&
                    receipt.Entities[0].From == CardLocation.Equipment(0) && receipt.Grains.Count == (grain is null ? 0 : 1) &&
                    s.V(1).Judgment.Any(card => card.Id == qin && card.Kind == kind && card.QinJudgmentSlot ==
                        (kind == CardKind.SilverLion ? CardKind.Indulgence : CardKind.SupplyShortage)) &&
                    s.G.CardMovements.Count(move => move.CardId == qin && move.From == CardLocation.Equipment(0) &&
                        move.To == CardLocation.Judgment(1) && move.Reason.Value == ZdPlace) == 1 &&
                    !s.E<ZixiPlacementCompletedEvent>().Any(e => e.FrameId == paid.FrameId),
                    "Each actual recovery/primary/spill observer keeps the one paid placement and its frozen native equipment tail active before completion.");
                if (option == "recovered") s.Need(s.V().Hp == hp + 1 &&
                    s.E<SilverLionRemovedRecoveryEvent>().Count(e => e.PlayerSeat == 0 && e.Reason.Value == ZdPlace && e.RecoveredAmount == 1) == 1,
                    "The real Silver Lion's native positive recovery is already applied at its genuine health child, before the placement can finish.");
                if (option == "placement-spill") s.Need(grain is { } id &&
                    s.Frame(paid.FrameId).ZixiPlacement!.Grains.Single().CardId == id &&
                    s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(moved => moved.Batch.ParentBatchId == paid.BatchId &&
                        moved.Batch.Movements.Any(move => move.CardId == id && move.From == CardLocation.WoodenOxGrain(0) &&
                            move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.WoodenOxGrainDiscard)) &&
                    !s.E<ZixiQinPlacedEvent>().Any(e => e.CardId == id),
                    "The true Ox grain child belongs to the original primary batch and is excluded from Qin placement entities and quantity.");
                s.Private(); s.RejectPrompt(); var old = s.P!; s.Cold(); s.Continue(); s.RejectOld(old);
            }
            s.Reach(() => s.E<ZixiPlacementCompletedEvent>().Any(e => e.FrameId == paid.FrameId));
            s.Need(observed.SetEquals(kind == CardKind.SilverLion ? new[] { "placed", "recovered" } : new[] { "placed", "placement-spill" }) &&
                s.E<ZixiPlacementPaidEvent>().Count(e => e.FrameId == paid.FrameId && e.EntityCount == 1) == 1 &&
                s.E<ZixiQinPlacedEvent>().Where(e => e.FrameId == paid.FrameId).ToArray() is [var entity] &&
                entity.CardId == qin && entity.From == CardLocation.Equipment(0) && entity.Source.SkillId == ZdSkill && entity.Source.BindingId == "end-place-qin" &&
                s.E<ZixiPlacementCompletedEvent>().Count(e => e.FrameId == paid.FrameId && e.Paid && e.EntityCount == 1) == 1 &&
                s.E<ZixiPlacementTailSealedEvent>().Count(e => e.FrameId == paid.FrameId && e.GrainCount == (grain is null ? 0 : 1)) == 1 &&
                s.G.CardMovements.Count(move => move.CardId == qin && move.From == CardLocation.Equipment(0) && move.To == CardLocation.Judgment(1) && move.Reason.Value == ZdPlace) == 1 &&
                s.E<CardUseDeclaredEvent>().Count(e => e.CardId == qin) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.CardId == qin) == 1 &&
                (grain is { } stored ? s.V().Hp == hp &&
                    s.G.CardMovements.Count(move => move.CardId == stored && move.From == CardLocation.WoodenOxGrain(0) &&
                        move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1 :
                    s.V().Hp == hp + 1 && s.E<SilverLionRemovedRecoveryEvent>().Count(e => e.Reason.Value == ZdPlace && e.RecoveredAmount == 1) == 1),
                "After cold restoration at every real child, the equipped original Qin is placed exactly once; Lion recovery or Ox spill drains once, and placement never declares another delayed-card Use."); s.Cold();
        }
    }

    private static LegalActionKind ZdAction(CardKind kind) => kind switch { CardKind.DrawTwo => LegalActionKind.DrawTwo, CardKind.Peach => LegalActionKind.Peach, CardKind.Alcohol => LegalActionKind.Alcohol, _ => throw new InvalidOperationException() };
    private sealed class ZdScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public IReadOnlyList<CardSnapshot> Hand() => V().Hand;
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int[] Marks() => E<QiqinInitialEntityMarkedEvent>().Where(e => e.OriginalOwnerSeat == 0).Select(e => e.CardId).ToArray();
        public ProgramSkillFrame Frame(long id) => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == id);
        public LegalAction[] Actions() => G.GetHumanLegalActions().Where(a => a.CardId is not null && a.ConversionSource is null && a.ProgramSkillId is null).ToArray();
        public bool Optional(string binding) => P?.SkillPrompt?.SkillId == ZdSkill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("binding-id") == binding);
        public bool OptionalSkill(string skill) => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == ZdWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, ZdDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        public void Give(int id, int seat) { Use("give", [id], [seat]); Play(); }
        public void Gather() { Use("stock"); Play(); for (var i = 1; i <= 3; i++) if (V(i).HandCount > 0) { Use("take-all", targets: [i]); Play(); } Need(Hand().Count + V().Equipment.Count + Enumerable.Range(0, 4).Sum(i => V(i).Judgment.Count) == 56, "The small complete real inventory is acquired by Draw20+20 and original foreign-Hand transfers, without finding seeds or editing the random generator."); }
        public void Top(int count) { var ids = Hand().Where(c => c.Kind is CardKind.Crossbow or CardKind.Dodge && !Marks().Contains(c.Id)).Take(count).Select(c => c.Id).ToArray(); Need(ids.Length == count, "The actual draw seed has the required original nonmaterial Hand entities."); Use("top", ids); Play(); }
        public void Place(int id, int seat) { Use("place", [id], [seat]); Play(); }
        public void OpenPlacement() { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "zixi-placement") == true); }
        public void Select(int id, int seat, string slot) => Answer(c => c.Parameters.GetValueOrDefault("branch") == "select" && c.Cards.SequenceEqual([id]) && c.Targets.SequenceEqual([seat]) && c.Parameters.GetValueOrDefault("slot") == slot);
        public void Confirm() => Answer(c => c.Parameters.GetValueOrDefault("branch") == "confirm");
        public void PlaceStart(int count, int seat) { OpenPlacement(); var ids = Marks().Where(id => Hand().Any(c => c.Id == id)).Take(count).ToArray(); for (var i = 0; i < count; i++) Select(ids[i], seat, i == 0 ? "indulgence" : "supplyShortage"); Confirm(); Play(); }
        public void PlaceEnd(int count, int seat) { End(); Reach(() => Optional("end-place-qin")); OpenPlacement(); var ids = Marks().Where(id => Hand().Any(c => c.Id == id)).Take(count).ToArray(); for (var i = 0; i < count; i++) Select(ids[i], seat, "lightning"); Confirm(); Play(); }
        public void Effect() { Reach(() => Optional(ZdEffect)); Private(); Cold(); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only the real human chooser answers one published native choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void PlayOrDead() => Reach(() => !V().IsAlive || P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 190; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal before boundary " + line);
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.SkillPrompt?.SkillId == ZdWatch) Continue();
                else if (p.SkillPrompt?.SkillId == ZdInsert && p.Choices is [var only] && only.Targets.SequenceEqual([0]) && only.Parameters.GetValueOrDefault("program-action") == "select-target") { Private(); Cold(); Answer(c => c.Id == only.Id); }
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.Nullification) Answer(c => c.Parameters.GetValueOrDefault("response") == "pass");
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else throw new InvalidOperationException("Unexpected real native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Private() => Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(i => G.CreateSnapshot(i).PendingDecision is null), "The actual owner alone receives private drafts and original-material choices.");
        public void Need(bool valid, string why) { if (!valid) throw new InvalidOperationException(why + " " + Diagnostic()); }
        public void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, "Rejected " + c.GetType().Name + " " + JsonSerializer.Serialize(r.Error)); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public string Diagnostic() => JsonSerializer.Serialize(new { scene, P, Hp = V().Hp, PeerHp = V(1).Hp, Frames = G.ResolutionStack, Placed = E<ZixiQinPlacedEvent>(), Started = E<ZixiEffectsStartedEvent>(), Draws = E<ZixiEffectsDrawReturnedEvent>(), Obtain = E<ZixiEffectsObtainPaidEvent>(), Damage = E<ZixiEffectsDamageIssuedEvent>(), Completed = E<ZixiEffectsCompletedEvent>(), Flood = E<WumiaoFloodEffectStartedEvent>(), FloodDiscards = E<WumiaoFloodDiscardPaidEvent>(), Juewu = E<JuewuUseIssuedEvent>(), ExtraRounds = E<ZixiExtraEffectRoundStartedEvent>(), ExtraRedirects = E<ZixiExtraEffectRedirectedEvent>(), SoleReplaced = E<SoleTargetReplacedEvent>(), ProtectedRedirects = E<SoleTargetAfterProtectionRedirectedEvent>(), ProtectionPaid = E<SoleTargetProtectionPaidEvent>(), Liuli = E<LiuliRedirectedEvent>(), FinishedUses = E<CardUseFinishedEvent>(), AppliedDamage = E<DamageAppliedEvent>() });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold replay retains original Qin identities, occupied subslots, private selections, accepted card cost and exact native child/extra-effect cursors."); }
        public void Reject(GameCommand c) { var before = State(); Need(!G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()).Accepted && before == State(), "A foreign, unpublished or retired input cannot move a Qin, repeat payment or issue another native effect round."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-zixi-choice"), G.Revision)); }
        public void RejectOld(PendingDecision p) => Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision));
        public static ZdScenario Start(string scene)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new ZdFixture(scene)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "paid-death" ? Role.Renegade : Role.Lord, ModeId = ZdMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new ZdScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 }); s.Accept(new SelectGeneralCommand(0, ZdOwner, g.Revision, s.P!.PromptId)); s.Reach(() => s.Optional("start-place-qin")); return s;
        }
    }

    private sealed class ZdFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-da-qiao-zixi", new(1, 0, 0), []);
        private static readonly CardKind[] Kinds = [CardKind.Crossbow, CardKind.SilverLion, CardKind.WoodenOx, CardKind.Slash, CardKind.Peach, CardKind.Alcohol, CardKind.DrawTwo, CardKind.Dismantlement, CardKind.Indulgence, CardKind.SupplyShortage, CardKind.Nullification, CardKind.IronChain, CardKind.BorrowedSword, CardKind.Dodge];
        public void Register(IContentRegistryBuilder b)
        {
            void Real(string bundle, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var rules = Read("rules");
                if (scene == "late-redirect" && bundle == "ol-wen-pin")
                {
                    var real = JsonNode.Parse(rules)!;
                    var protection = real["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "ol:zhenwei")!["triggers"]!.AsArray().Single();
                    protection!["priority"] = -1000;
                    rules = real.ToJsonString();
                }
                var c = SkillProgramCatalog.Load(rules, Read("presentation")); foreach (var id in ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            }
            Real("le-xiao-qiao", ZdQin); Real("le-da-qiao", ZdSkill);
            if (scene == "flood") Real("wumiao-guan-yu", "wumiao:juewu");
            if (scene == "repeat-Peach") Real("ol-wang-rong", "ol:fengzi"); if (scene == "replacement") Real("ol-sun-hong", "ol:zenrun"); if (scene is "redirect" or "late-redirect") Real("passive-response-rules", "classic:liuli");
            if (scene == "late-redirect") Real("ol-wen-pin", "ol:zhenwei");
            if (scene == "delayed-subslots") { Real("boundary-da-qiao", "boundary:guose"); Real("friend-trio", "friend:yangming"); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0, bool many = false) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + (many ? "null" : cards.ToString(CultureInfo.InvariantCulture)) + ",\"sourceZones\":[\"hand\"],\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[" + effects + "]}");
            static string Choice(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var acts = new JsonArray(A("stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20},{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"),
                A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1),
                A("take-all", "{\"op\":\"selectOwnedCards\",\"target\":\"selectedTarget\",\"numberExpression\":\"allOwnedZoneCards\",\"zones\":[\"hand\"],\"resultBind\":\"taken\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"taken\",\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("top", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"top\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"top\",\"destination\":\"drawPileTop\",\"awaitMovementTriggers\":true}", 1, many: true),
                A("place", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gear\"},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"gear\"}", 1, 1),
                A("transfer-j", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"judgment\"],\"count\":1,\"destination\":\"selectedTargetCorrespondingZone\",\"targetRef\":{\"kind\":\"selectedTarget\"},\"awaitMovementTriggers\":true}", targets: 1),
                A("take-j", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"judgment\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("hurt-three", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":3}", targets: 1),
                A("damage", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}", targets: 1),
                A("dying", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":13}"),
                A("offer", "{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}", targets: 1));
            if (scene == "replacement") acts.Add(A("grant-zenrun", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"ol:zenrun\"]}"));
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2; acts.Add(duel);
            acts.Single(n => n!["id"]!.GetValue<string>() == "place")!["cardCategories"] = N("[\"equipment\"]");
            var defs = new JsonArray(new JsonObject { ["id"] = ZdDriver, ["revision"] = 1, ["modifiers"] = N("[{\"id\":\"keep-inventory\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":64,\"priority\":0}]"), ["activations"] = acts },
                N("{\"id\":\"" + ZdQuiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var own = new List<string> { ZdDriver, ZdWatch, ZdQin, scene == "paid-loss" ? ZdGrant : ZdSkill };
            if (scene == "paid-loss") defs.Add(N("{\"id\":\"" + ZdGrant + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-zixi\",\"skillId\":\"le:zixi\",\"minimumHp\":13}]}"));
            if (scene is "redirect" or "late-redirect") own.Add("classic:liuli");
            if (scene == "late-redirect") own.Add("ol:zhenwei");
            if (scene == "delayed-subslots") own.AddRange(["boundary:guose", "friend:yangming"]);
            if (scene == "flood") own.Add("wumiao:juewu");
            if (scene == "repeat-Peach") own.Add("ol:fengzi");
            if (scene == "late-target") { own.Add("fixture:ldz-late"); defs.Add(N("{\"id\":\"fixture:ldz-late\",\"revision\":1,\"triggers\":[{\"id\":\"add-after-zixi\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"priority\":-1000,\"optional\":false,\"condition\":{\"kind\":\"compare\",\"left\":{\"kind\":\"cardUseDesignatedTargetCount\"},\"operator\":\"equal\",\"right\":{\"kind\":\"integerConstant\",\"value\":1}},\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"addCurrentCardUseTarget\",\"target\":\"selectedTarget\"}]}]}")); }
            if (scene == "three") { own.Add(ZdInsert); defs.Add(N("{\"id\":\"" + ZdInsert + "\",\"revision\":1,\"triggers\":[{\"id\":\"extra-play\",\"window\":\"playEnding\",\"subject\":\"owner\",\"priority\":-1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"insertPhase\",\"target\":\"selectedTarget\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]}")); }
            var watch = new JsonArray();
            if (scene == "flood") watch.Add(N("{\"id\":\"flood-real-damage-child\",\"window\":\"afterDamageApplied\",\"subject\":\"damageSource\",\"damageCardKinds\":[\"wumiaoFlood\"],\"damageOccurrence\":\"perDamage\",\"optional\":false,\"effects\":[" + Choice("flood-hit") + "]}"));
            foreach (var zone in new[] { "hand", "equipment" }) watch.Add(N("{\"id\":\"placement-" + zone + "\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"" + zone + "\"],\"movementOccurrence\":\"perOwnerBatch\",\"movementReasons\":[\"" + ZdPlace + "\"],\"optional\":false,\"effects\":[" + Choice("placed") + "]}"));
            watch.Add(N("{\"id\":\"zixi-draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + ZdDraw + "\"],\"optional\":false,\"effects\":[" + Choice("draw") + (scene is "paid-loss" or "paid-death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "paid-death" ? 20 : 1) + "}" : "") + "]}"));
            watch.Add(N("{\"id\":\"whole-obtain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + ZdObtain + "\"],\"optional\":false,\"effects\":[" + Choice("obtained") + "]}"));
            if (scene is "three" or "placement-lion") watch.Add(N("{\"id\":\"lion-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Choice("recovered") + "]}"));
            if (scene == "replacement") watch.Add(N("{\"id\":\"replacement-obtain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.sun-hong.zenrun.obtain\"],\"optional\":false,\"effects\":[" + Choice("replacement") + "]}"));
            if (scene == "placement-ox") watch.Add(N("{\"id\":\"placement-native-grain\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"optional\":false,\"effects\":[" + Choice("placement-spill") + "]}"));
            if (scene == "repeat-Peach") watch.Add(N("{\"id\":\"same-peach-three-recovery-children\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Choice("repeat-recovery") + "]}"));
            defs.Add(new JsonObject { ["id"] = ZdWatch, ["revision"] = 1, ["triggers"] = watch });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "Real dated full Zixi native fixture", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var c = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })); foreach (var (id, p) in c.Programs) b.AddSkill(new(id, id, "Actual Qin and whole native Use children") { Program = p, ProgramPresentation = c.Presentations[id] });
            const string pick = "fixture:ldz-pick"; b.AddSkill(new(pick, "Owner", "Fixed human") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) }); b.AddGeneral(new(ZdOwner, "Real current Le Da Qiao", "supporter", pick, "wu", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:ldz-peer-" + i).ToArray(); for (var i = 1; i <= 3; i++) { var key = "fixture:ldz-peer-pick-" + i; b.AddSkill(new(key, "Peer", "Fixed public peer") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); b.AddGeneral(new(peers[i - 1], "Real other character", "supporter", key, "shu", 12, (scene is "redirect" or "late-redirect") && i == 1 ? [ZdQuiet, ZdSkill] : [ZdQuiet])); }
            foreach (var kind in Kinds) { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:ldz-" + kind, card.DisplayName, card.CategoryName, "Original printed entity", kind)); }
            var suits = new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }; var cards = Kinds.SelectMany(kind => suits.Select(suit => new ContentDeckPhysicalCard("fixture:ldz-" + kind, suit, 7))).ToArray();
            if (scene == "delayed-subslots") cards = Enumerable.Range(0, 56).Select(_ => new ContentDeckPhysicalCard("fixture:ldz-Indulgence", Suit.Diamond, 7)).ToArray();
            if (scene is "placement-lion" or "placement-ox") cards = Enumerable.Range(0, 56).Select(_ => new ContentDeckPhysicalCard(scene == "placement-lion" ? "fixture:ldz-SilverLion" : "fixture:ldz-WoodenOx", Suit.Heart, 7)).ToArray();
            b.AddDeck(new("fixture:ldz-deck", "Fifty-six original native entities", 4, 0, []) { PhysicalCards = cards });
            var roles = scene == "paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(ZdMode, "Real dated full Le Da Qiao", 4, 4, roles, "fixture:ldz-deck", GeneralCandidateCount: 4, GeneralPoolIds: [ZdOwner, .. peers]));
        }
    }
}
