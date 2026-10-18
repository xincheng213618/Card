using System.Collections;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class OlMouZhouYuChecks
{
    private const string Zf = "mou-ol:zuifeng", Gq = "mou-ol:guqu", ZfBinding = "unique-suit-view-and-wine";
    private const string Owner = "fixture:mzy-owner", Driver = "fixture:mzy-driver", Watch = "fixture:mzy-watch", Quiet = "fixture:mzy-quiet";
    private const string Conditional = "fixture:mzy-conditional", Insert = "fixture:mzy-insert", Mode = "identity:classic-mou-zhou-yu";
    private const string ZfDiscard = "skill-program.mou-zhou-yu.zuifeng.discard", GqDraw = "skill-program.mou-zhou-yu.guqu.draw", Zenrun = "ol:zenrun";

    public static void ZuifengUniqueHandSuitPrivateViewFireAndPhaseQuotaCold()
    {
        var s = MzyScenario.Start("quota"); s.Stock(20);
        var pair = s.V().Hand.GroupBy(c => c.Suit).Where(g => g.Count() > 1).Take(2).Select(g => g.First()).ToArray();
        s.Need(pair.Length == 2, "The fixed real inventory contains two distinct standard suits."); s.Keep(pair.Select(c => c.Id).ToArray(), 1);
        s.BeginZf(pair[0].Id, 0); var viewed = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MouZuifeng is not null);
        s.Need(viewed.MouZuifeng is { Stage: MouZuifengStage.HandViewChoice, Viewed: IList { IsReadOnly: true } } r &&
            r.Viewed.Select(v => v.CardId).SequenceEqual([pair[1].Id]) && s.E<MouZuifengCostPaidEvent>().Single().From == CardLocation.Hand(0),
            "A self target remains legal with a real remaining Hand after the unique-suit Hand cost is paid.");
        s.PrivateView(0, 0); s.RejectPrompt(); var retired = s.P!; s.Cold(); s.ZfAnswer("show", pair[1].Id); s.RejectOld(retired);
        s.Need(s.E<MouZuifengShownEvent>().Single() is { SameSuit: false } && !s.E<MouZuifengFireIssuedEvent>().Any() &&
            s.P is { PlayerSeat: 0 } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "decline"),
            "The privately viewed original self card is publicly shown once; different suits do not fabricate fire damage or compulsory wine.");
        s.ZfAnswer("decline"); s.Play();

        var cost = s.V().Hand.Single(); s.BeginZf(cost.Id, 1);
        var paid = s.E<MouZuifengCostPaidEvent>().Last(); var targetHand = s.V(1).Hand.ToArray(); s.PrivateView(0, 1); s.Cold();
        var matching = targetHand.First(c => c.Suit == cost.Suit); var hp = s.V(1).Hp; s.ZfAnswer("show", matching.Id);
        s.Reach(() => s.Hold("fire"));
        s.Need(s.E<MouZuifengShownEvent>().Last() is { SameSuit: true } && s.E<MouZuifengFireIssuedEvent>().Count(e => e.FrameId == paid.FrameId) == 1 &&
            s.V(1).Hp == hp - 1 && Enumerable.Range(0, 4).All(v => s.G.CreateSnapshot(v).PublicRevealedCards.Any(c => c.Id == matching.Id)) &&
            Enumerable.Range(1, 3).All(v => s.G.CreateSnapshot(v).PrivateRevealedCards is not { Count: > 0 }),
            "Only the one shown original becomes public and matching effective suits cause one real fire injury before its native child returns.");
        s.Private(); s.Cold(); s.Continue(); s.Play();
        s.Use("lose-one"); s.Play(); s.Need(!s.Has(0, Zf), "A real HP change removes the original conditional Zuifeng source.");
        s.Use("recover-one"); s.Play(); s.Need(s.Has(0, Zf), "Native recovery legitimately regrants that source.");
        s.Stock(1); cost = s.V().Hand.Single(); s.BeginZf(cost.Id, 1);
        var different = s.V(1).Hand.First(c => c.Suit != cost.Suit); s.ZfAnswer("show", different.Id); s.Play();
        var uses = s.E<MouZuifengCostPaidEvent>();
        s.Need(uses.Length == s.V().MaxHp && uses.Select(e => e.UseOrdinal).SequenceEqual(Enumerable.Range(1, s.V().MaxHp)) &&
            uses.All(e => e.MaxHpAtPayment == s.V().MaxHp) && uses.Select(e => e.Phase.Token).Distinct().Count() == 1,
            "Three actual paid activations consume the current MaxHP allowance across source loss and regrant; the source instance does not reset it.");
        s.Stock(1); s.Reject(new UseProgramSkillCommand(0, Zf, ZfBinding, [], [], s.G.Revision, s.P!.PromptId));
        var turn = s.G.State.TurnNumber; var oldPhase = uses[0].Phase; s.End(); s.Play();
        s.BeginZf(s.V().Hand.Single().Id, 1); s.ZfAnswer("show", s.V(1).Hand.First().Id); s.Play();
        s.Need(s.G.State.TurnNumber == turn && s.E<MouZuifengCostPaidEvent>().Last() is { UseOrdinal: 1 } extra && extra.Phase.Token != oldPhase.Token,
            "A genuine inserted Play in the same actual turn receives a fresh phase allowance."); s.Cold();

        s = MzyScenario.Start("zones"); s.Stock(4); var ox = s.V().Hand[0].Id; s.Native(s.Actions().First(a => a.CardId == ox && a.Kind == LegalActionKind.Equip)); s.Play();
        var grain = s.V().Hand[0].Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Need(!s.Actions().Any(a => a.ProgramSkillId == Zf), "Two same-suit Hand cards do not become unique merely because an equipped Ox and its grain exist.");
        s.Give([s.V().Hand[0].Id], 1); s.Play(); s.Accept(new UseProgramSkillCommand(0, Zf, ZfBinding, [], [], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.ZfChoice("cost"));
        s.Need(s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "cost").SelectMany(c => c.Cards).SequenceEqual([s.V().Hand.Single().Id]) &&
            s.P.Choices.All(c => !c.Cards.Contains(ox) && !c.Cards.Contains(grain)), "Only the actual unique Hand entity can pay: equipment and Wooden Ox grain are never cost candidates.");
        s.Private(); s.Cold(); s.ZfAnswer("cancel"); s.Play(); s.Need(!s.E<MouZuifengCostPaidEvent>().Any(), "Cancelling the unpublished cost consumes no activation or native movement.");
    }

    public static void ZuifengForeignAlcoholOriginalMaterialAndPaidLifetimeCold()
    {
        foreach (var scene in new[] { "wine", "wine-loss", "wine-death", "wine-decline" })
        {
            var s = MzyScenario.Start(scene); s.Stock(12); s.Give([s.V().Hand[0].Id], 1); s.Play(); s.End();
            s.Reach(() => s.ZfChoice("show") && s.P!.PlayerSeat == 1);
            var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MouZuifeng is not null);
            s.Need(parent.OwnerSeat == 1 && parent.MouZuifeng!.TargetSeat == 0 && parent.MouZuifeng.Viewed.Select(v => v.CardId).SequenceEqual(s.V().Hand.Select(c => c.Id)),
                "The real AI's only legal unique Hand cost opens its true private view of the human's entire Hand."); s.PrivateView(1, 0); s.Cold(); s.Advance();
            s.Reach(() => s.ZfChoice("decline") && s.P!.PlayerSeat == 0);
            var shown = s.E<MouZuifengShownEvent>().Single(); var frame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == shown.FrameId);
            s.Need(s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "alcohol" && c.Cards.SequenceEqual([shown.CardId])) &&
                frame.MouZuifeng is { Stage: MouZuifengStage.AlcoholChoice, Paid: true } && s.E<GuquQueMarkedEvent>().Any(e => e.CardId == shown.CardId && e.Source.OwnerSeat == 1),
                "After the real fire child, the human may use that exact shown Hand original as wine on the other actor, despite being outside the human's Play.");
            s.Private(); s.RejectPrompt(); s.Cold();
            if (scene == "wine-decline")
            {
                s.ZfAnswer("decline"); s.Reach(() => s.E<MouZuifengCompletedEvent>().Any(e => e.FrameId == frame.Id));
                s.Need(!s.E<MouZuifengAlcoholIssuedEvent>().Any() && !s.E<GuquStartedEvent>().Any() && s.V().Hand.Any(c => c.Id == shown.CardId),
                    "Declining the actual optional request preserves the original Hand entity and creates neither Use nor Guqu draw."); s.Cold(); continue;
            }
            s.ZfAnswer("alcohol"); s.Reach(() => s.Hold("wine-use"));
            var issued = s.E<MouZuifengAlcoholIssuedEvent>().Single(); var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issued.UseId);
            s.Need(issued is { ActorSeat: 0, TargetSeat: 1, PrintedKind: CardKind.Dodge } && issued.CardId == shown.CardId && use.CardId == shown.CardId &&
                use.MouZuifengAlcohol is { ActorSeat: 0, TargetSeat: 1 } && use.Action is { EffectiveKind: CardKind.Alcohol } && use.TargetSeats.SequenceEqual([1]),
                "The accepted native Use retains the printed Dodge original, real human actor, foreign Zhou Yu target and exact Alcohol output; it is not free wine or a rescue surrogate.");
            s.Private(); s.Cold(); s.Continue();
            if (scene is "wine-loss" or "wine-death")
            {
                s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
                s.Answer(c => c.Targets.SequenceEqual([1])); s.Reach(() => s.Hold("wine-changed"));
                s.Need(scene == "wine-death" ? !s.V(1).IsAlive : !s.Has(1, Gq), "An actual native HP child changes the original source after this Use was accepted."); s.Private(); s.Cold(); s.Continue();
            }
            s.Reach(() => s.E<MouZuifengCompletedEvent>().Any(e => e.FrameId == frame.Id));
            s.Need(s.E<MouZuifengAlcoholPaidEvent>().Single() is { ActorSeat: 0 } pay && pay.CardId == shown.CardId && pay.UseId == issued.UseId &&
                pay.From == CardLocation.Hand(0) && pay.To == CardLocation.Processing &&
                s.G.CardMovements.Count(m => m.CardId == shown.CardId && m.Sequence == pay.Sequence && m.From == pay.From && m.To == pay.To) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardKind == CardKind.Alcohol && e.CardId == shown.CardId) == 1 &&
                s.E<MouZuifengAlcoholReturnedEvent>().Count(e => e.UseId == issued.UseId) == 1 &&
                s.E<MouZuifengCompletedEvent>().Count(e => e.FrameId == frame.Id && e.AlcoholUseId == issued.UseId) == 1 && !s.V().HasAlcoholEffect &&
                (scene == "wine-death" || s.V(1).HasAlcoholEffect),
                "One original native payment and one finish return through the exact parent, with the benefit on Zhou Yu and no duplicate cost after source loss or death."); s.Cold();
        }

        foreach (var scene in new[] { "cost-loss", "cost-death" })
        {
            var s = MzyScenario.Start(scene); s.Stock(2); s.Give([s.V().Hand[0].Id], 1); s.Play();
            s.Accept(new UseProgramSkillCommand(0, Zf, ZfBinding, [], [], s.G.Revision, s.P!.PromptId)); s.Reach(() => s.ZfChoice("cost"));
            s.ZfAnswer("cost", s.V().Hand.Single().Id); s.ZfAnswer("target", target: 1); s.Reach(() => s.Hold("zf-cost"));
            var paid = s.E<MouZuifengCostPaidEvent>().Single(); s.Need(!s.E<MouZuifengHandViewedEvent>().Any(), "The real logical discard's native children precede any private inspection."); s.Private(); s.Cold(); s.Continue();
            if (scene == "cost-loss")
            {
                s.Reach(() => s.ZfChoice("show")); s.Need(!s.Has(0, Zf), "The original paid discard child genuinely removes Zuifeng."); s.PrivateView(0, 1); s.Cold(); s.ZfAnswer("show", s.V(1).Hand.Single().Id);
            }
            s.Reach(() => s.E<MouZuifengCompletedEvent>().Any(e => e.FrameId == paid.FrameId));
            s.Need(s.E<MouZuifengCompletedEvent>().Count(e => e.FrameId == paid.FrameId && e.Paid) == 1 && s.E<MouZuifengCostPaidEvent>().Length == 1 &&
                (scene != "cost-death" || !s.V().IsAlive && !s.E<MouZuifengHandViewedEvent>().Any() && !s.E<MouZuifengAlcoholIssuedEvent>().Any()),
                "Paid source loss completes its issued flow, while dead owners open no fresh private view or wine request; native children and payment finish once."); s.Cold();
        }
    }

    public static void GuquEveryShownEntityTrueUseWhitelistAndActualTurnCold()
    {
        var s = MzyScenario.Start("whitelist"); s.Stock(40);
        var marked = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id; var ordinaryDodge = s.V().Hand.First(c => c.Kind == CardKind.Dodge).Id;
        s.ShowGive(marked, 1); s.Give([ordinaryDodge], 1); s.Play();
        var mark = s.E<GuquQueMarkedEvent>().Single(e => e.CardId == marked);
        s.Need(mark.HandOwnerSeat == 0 && mark.Source.OwnerSeat == 0 && !s.E<MouZuifengShownEvent>().Any() && s.V(1).Hand.Single(c => c.Id == marked).SkillMarkLabel == "阙",
            "A real generic public Hand reveal, independent of Zuifeng, signs the original identity; its mark follows a later genuine gift."); s.Cold();
        s.Reject(new UseProgramSkillCommand(0, Driver, "foreign-duel", [], [1], s.G.Revision, s.P!.PromptId));
        s.Need(!s.E<CardUseDeclaredEvent>().Any(e => e.SourceSeat == 1), "The real selected-actor zero-material Duel request cannot admit a forbidden true Use while that actor holds Que.");
        var hp = s.V(1).Hp; s.Use("incoming", targets: [1]); s.Play();
        s.Need(s.V(1).Hp == hp - 1 && !s.E<GuquUseClaimedEvent>().Any(e => e.Claim.ActorSeat == 1),
            "An other holder with a Slash Que cannot use the unmarked Dodge or convert the Que Slash into Dodge, even though both ordinary suppliers would answer this real attack.");
        s.Take(1, marked); s.Play(); s.Need(s.V().Hand.Any(c => c.Id == marked) && s.V(1).Hand.All(c => c.SkillMarkLabel != "阙"), "Actual movement out of that Hand immediately releases its whitelist.");
        hp = s.V(1).Hp; s.Use("incoming", targets: [1]); s.Play();
        s.Need(s.V(1).Hp == hp && !s.V(1).Hand.Any(c => c.Id == ordinaryDodge) && !s.E<GuquStartedEvent>().Any(),
            "Without a current Que in Hand, the same native ordinary Dodge is legal and gives no reward for its unmarked material.");
        s.Give([marked], 1); s.Play(); s.Need(s.V(1).Hand.Single().SkillMarkLabel == "阙" && s.E<GuquQueMarkedEvent>().Count(e => e.CardId == marked) == 1,
            "The unchanged original Que regains its restriction when it reenters another Hand, without inventing a second reveal.");
        var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.G.State.TurnNumber != turn && s.V(1).Hand.Single(c => c.Id == marked).SkillMarkLabel != "阙",
            "The original actual-turn lease expires at its real boundary rather than becoming a permanent physical marker."); s.Cold();

        foreach (var scene in new[] { "true-response", "true-response-death" })
        {
            s = MzyScenario.Start(scene); s.Stock(40);
            var dodge = s.V().Hand.First(c => c.Kind == CardKind.Dodge).Id; var slash = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id;
            s.ShowGive(dodge, 1); s.Give([slash], 1); s.Play();
            s.Use("peer-lose-two", targets: [1]); s.Play();
            var duel = s.Actions().First(a => a.Kind == LegalActionKind.Duel && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1])); s.Native(duel); s.Play();
            s.Need(!s.V(1).Hand.Any(c => c.Id == slash) && s.V(1).Hand.Any(c => c.Id == dodge) && !s.E<GuquStartedEvent>().Any() &&
                s.E<CardUseDeclaredEvent>().All(e => e.CardId != slash), "A real Duel's pure Played unmarked Slash remains legal and cannot masquerade as a Guqu true Use.");
            s.Use("incoming", targets: [1]); s.Reach(() => s.Hold("guqu-draw"));
            var reward = s.E<GuquStartedEvent>().Single(); var f = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == reward.FrameId);
            s.Need(reward.Claim is { ActorSeat: 1, ProviderSeat: 1, EffectiveKind: CardKind.Dodge, QueMaterialCount: 1 } && f.Guqu is { Stage: GuquStage.DrawChildren, DrawOutcome.ActualDrawIds: IList { IsReadOnly: true } } &&
                s.E<GuquUseMaterialClaimedEvent>().Count(e => e.ActionId == reward.Claim.ActionId && e.CardId == dodge && e.From == CardLocation.Hand(1)) == 1 && !s.E<GuquCompletedEvent>().Any(),
                "The genuine accepted Dodge consumes the marked original and owns one frozen native Draw2 while its actual gain child is paused."); s.Private(); s.RejectPrompt(); s.Cold(); s.Continue();
            s.Reach(() => s.E<GuquCompletedEvent>().Any(e => e.FrameId == f.Id)); if (s.V().IsAlive) s.Play();
            s.Need((scene == "true-response-death" ? !s.V().IsAlive : !s.Has(0, Gq)) && s.E<GuquDrawReturnedEvent>().Single(e => e.FrameId == f.Id) is { RequestedCount: 2, ActualCount: 2, ReplacementCount: 0 } &&
                s.E<GuquCompletedEvent>().Count(e => e.FrameId == f.Id && e.ActionId == reward.Claim.ActionId) == 1,
                "A genuine HP child loses Guqu or kills its owner after payment without cancelling or repeating its accepted native Draw2."); s.Cold();
        }

        foreach (var scene in new[] { "equipment-lion", "equipment-ox" })
        {
            s = MzyScenario.Start(scene); s.Stock(4); var old = s.V().Hand[0].Id;
            s.Native(s.Actions().First(a => a.CardId == old && a.Kind == LegalActionKind.Equip)); s.Play();
            int? grain = null;
            if (scene == "equipment-lion") { s.Use("lose-one"); s.Play(); }
            else { grain = s.V().Hand[0].Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Value], [], s.G.Revision, s.P!.PromptId)); s.Play(); }
            var next = s.V().Hand[0].Id; s.Show(next); s.Play(); var recoveryBefore = s.E<RecoveryAppliedEvent>().Length;
            s.Native(s.Actions().First(a => a.CardId == next && a.Kind == LegalActionKind.Equip)); s.Reach(() => s.Hold(scene == "equipment-lion" ? "lion" : "spill"));
            s.Need(s.V().Equipment.Any(c => c.Id == next) && s.G.CardMovements.Count(m => m.CardId == old && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1,
                "A marked original natural equipment Use installs its own printed entity and removes the old real equipment before its native loss child."); s.Private(); s.Cold(); s.Continue(); s.Play();
            var claim = s.E<GuquStartedEvent>().Single();
            s.Need(claim.Claim.ActorSeat == 0 && claim.Claim.QueMaterialCount == 1 && s.E<GuquCompletedEvent>().Count(e => e.FrameId == claim.FrameId) == 1 &&
                s.E<GuquDrawReturnedEvent>().Single() is { RequestedCount: 2, ActualCount: 2 } &&
                (grain is { } g ? s.G.CardMovements.Count(m => m.CardId == g && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1 : s.E<RecoveryAppliedEvent>().Length == recoveryBefore + 1) &&
                s.E<CardUseFinishedEvent>().Count(e => e.CardId == next) == 1,
                "The whole accepted equipment Use rewards once and drains actual Silver Lion recovery or Wooden Ox grain spill without repaying, cloning or treating spill as Que use."); s.Cold();
        }
        MzyGrainResponse();
        MzyBaguaUseAndPlayed();
        MzyActorReplacementClaim();
    }

    public static void GuquConversionDeclarationAndNativeDrawPaidRightsCold()
    {
        var s = MzyScenario.Start("declaration"); s.Stock(40); s.Give(s.V().Hand.Take(2).Select(c => c.Id).ToArray(), 1); s.Play();
        var material = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id; s.Show(material); s.Play();
        var action = s.Actions().First(a => a.CardId == material && a.Kind == LegalActionKind.Slash && a.ConversionSource is { SkillId: "classic:guhuo", BindingId: "declare-slash" } && a.TargetSeats.SequenceEqual([3]));
        s.RejectSource(action); s.Native(action); s.Reach(() => s.Hold("declaration-paid"));
        var declaration = s.G.ResolutionStack.OfType<CardDeclarationFrame>().Single(); var permit = declaration.Payment.GuquPermit; var claims = declaration.Payment.GuquDeclarationClaims;
        s.Need(permit is not null && permit.DeclarationId == declaration.Id && permit.ActorSeat == 0 && permit.CardId == material && permit.From == CardLocation.Hand(0) &&
            permit.Conversion == action.ConversionSource && permit.OutputKind == CardKind.Slash && claims is IList { IsReadOnly: true } && claims.Count == 1 &&
            claims[0].Source.OwnerSeat == 0 && claims[0].QueMaterialCount == 1 && !declaration.IsRevealed &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason.Value == "conversion.declaration.pay") == 1,
            "The source itself may convert its Que: the real Guhuo declaration freezes its exact admission and one marked-material reward before any future native Use exists.");
        s.Private(); s.RejectPrompt(); var old = s.P!; s.Cold(); s.Continue(); s.RejectOld(old); s.Reach(() => s.Hold("declaration-changed"));
        var kept = s.G.ResolutionStack.OfType<CardDeclarationFrame>().Single();
        s.Need(!s.Has(0, Gq) && kept.Id == declaration.Id && kept.Payment.GuquPermit == permit && kept.Payment.GuquDeclarationClaims!.SequenceEqual(claims!) &&
            !s.E<GuquStartedEvent>().Any(), "The actual declaration payment child loses the original source but cannot rewrite its immutable paid permission or reward claims."); s.Private(); s.Cold(); s.Continue();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Zenrun && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "target") == true); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([1]));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick"); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick"); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take");
        s.Reach(() => s.Hold("replacement")); var reward = s.E<GuquStartedEvent>().Single();
        s.Need(reward.Claim.Source == claims![0].Source && reward.Claim.GameplayHash == claims[0].GameplayHash && reward.Claim.QueMaterialCount == 1 &&
            !s.E<GuquCompletedEvent>().Any() && !s.E<GuquDrawReturnedEvent>().Any(), "The already accepted declaration transfers its original claim into the completed true Use, retaining a real replacement's obtain and compensation children."); s.Private(); s.Cold(); s.Continue(); s.Play();
        var finished = s.E<CardUseFinishedEvent>().Single(e => e.CardId == material && e.CardKind == CardKind.Slash);
        s.Need(s.E<GuquCompletedEvent>().Count(e => e.FrameId == reward.FrameId) == 1 && s.E<GuquDrawReturnedEvent>().Single() is { RequestedCount: 2, ActualCount: 0, ReplacementCount: 2 } &&
            s.E<GuquUseClaimedEvent>().Count(e => e.Claim.ActionId == reward.Claim.ActionId && e.Claim.Source == claims[0].Source) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            finished.ResolutionId == reward.Claim.ParentFrameId && !s.G.CardMovements.Any(m => m.Reason.Value == GqDraw),
            "One declared original becomes one native Use and one Guqu reward; actual draw and replacement-gained originals stay separate, without a second payment or live-source recheck."); s.Cold();
        MzyNullPaymentSourceLoss();
    }

    private static void MzyGrainResponse()
    {
        foreach (var scene in new[] { "grain-que", "grain-ordinary" })
        {
            var s = MzyScenario.Start(scene); s.Use("equip-peer", targets: [1]); s.Play();
            s.Need(s.V(1).Equipment is [{ Kind: CardKind.WoodenOx }], "The foreign holder first equips the one real deck Ox through the native equipment Use.");
            s.Stock(40); var slash = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id; var dodge = s.V().Hand.First(c => c.Kind == CardKind.Dodge).Id;
            if (scene == "grain-que") { s.Use("show-give-two", [slash, dodge], [1]); s.Play(); }
            else { s.ShowGive(slash, 1); s.Give([dodge], 1); }
            s.Need(s.V(1).Hand is [{ Id: var held, SkillMarkLabel: "阙" }] && held == slash &&
                s.G.CardMovements.Count(m => m.CardId == dodge && m.From == CardLocation.Hand(1) && m.To == CardLocation.WoodenOxGrain(1)) == 1 &&
                s.E<GuquQueMarkedEvent>().Count(e => e.CardId == dodge) == (scene == "grain-que" ? 1 : 0),
                "A genuine owner program selects the original Dodge from Hand and moves it into its equipped Ox's real grain, preserving the other Que in Hand; this setup does not claim an ordinary Ox-button activation.");
            s.Cold(); var hp = s.V(1).Hp; s.Use("incoming", targets: [1]);
            if (scene == "grain-ordinary")
            {
                s.Play(); s.Need(s.V(1).Hp == hp - 1 && !s.E<GuquStartedEvent>().Any() &&
                    !s.G.CardMovements.Any(m => m.CardId == dodge && m.From == CardLocation.WoodenOxGrain(1) && m.To != CardLocation.WoodenOxGrain(1)),
                    "An unmarked grain Dodge cannot answer a true Use while its owner holds another Que; its native ownership alone grants no whitelist exception.");
            }
            else
            {
                s.Reach(() => s.Hold("grain-draw")); var reward = s.E<GuquStartedEvent>().Single();
                s.Need(reward.Claim is { ActorSeat: 1, ProviderSeat: 1, EffectiveKind: CardKind.Dodge, QueMaterialCount: 1 } && s.V(1).Hp == hp &&
                    s.E<GuquUseMaterialClaimedEvent>().Count(e => e.ActionId == reward.Claim.ActionId && e.CardId == dodge && e.From == CardLocation.WoodenOxGrain(1)) == 1 &&
                    s.G.CardMovements.Count(m => m.CardId == dodge && m.From == CardLocation.WoodenOxGrain(1) && m.To == CardLocation.Processing) == 1 && !s.E<GuquCompletedEvent>().Any(),
                    "The original Que grain is a legal native Dodge material despite the restriction being activated by a different Que still in Hand; the exact grain source owns one draw reward.");
                s.Private(); s.Cold(); s.Continue(); s.Play();
                s.Need(s.E<GuquCompletedEvent>().Count(e => e.FrameId == reward.FrameId) == 1 && s.E<GuquDrawReturnedEvent>().Single() is { RequestedCount: 2, ActualCount: 2, ReplacementCount: 0 } &&
                    s.G.CardMovements.Count(m => m.CardId == dodge && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
                    "Cold return completes the real grain Dodge and its Draw2 once, without a second material payment or fabricated Hand source.");
            }
            s.Cold();
        }
    }

    private static void MzyNullPaymentSourceLoss()
    {
        var s = MzyScenario.Start("null-policy"); s.Stock(40);
        var material = s.V().Hand.First(c => c.Kind == CardKind.Nullification).Id; s.Show(material);
        var duel = s.Actions().First(a => a.Kind == LegalActionKind.Duel && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1]));
        s.Native(duel); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
        s.Private(); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([material]) && !c.Parameters.ContainsKey("conversion-skill-id")); s.Reach(() => s.Hold("null-paid"));
        var window = s.G.ResolutionStack.OfType<NullificationWindowFrame>().Single(); var action = window.CounterspellPayment!.Action;
        var claims = window.GuquClaims; var admissions = window.GuquResponseAdmissions;
        s.Need(action is { ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.Nullification } &&
            claims is IList { IsReadOnly: true } && claims.Count == 1 && claims[0].ActionId == action.ActionId && claims[0].QueMaterialCount == 1 &&
            admissions is IList { IsReadOnly: true } && admissions.Single().ActionId == action.ActionId && !s.E<GuquStartedEvent>().Any() &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Nullification) == 1,
            "A natural Que Nullification signs its exact accepted response admission before the unrespondable policy drains its real payment movement child.");
        s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); s.Reach(() => s.Hold("null-changed"));
        var kept = s.G.ResolutionStack.OfType<NullificationWindowFrame>().Single(f => f.Id == window.Id);
        s.Need(s.V().Hp == 12 && !s.Has(0, Gq) && kept.CounterspellPayment?.Action.ActionId == action.ActionId &&
            kept.GuquClaims!.SequenceEqual(claims!) && kept.GuquResponseAdmissions!.SequenceEqual(admissions!) && !s.E<GuquStartedEvent>().Any(),
            "The queued native cost child genuinely loses Guqu before CardUseCommitted while preserving the original accepted response and immutable reward claim.");
        s.Private(); s.Cold(); s.Continue(); s.Play();
        var reward = s.E<GuquStartedEvent>().Single();
        s.Need(reward.Claim.ActionId == action.ActionId && reward.Claim.Source == claims![0].Source &&
            s.E<GuquCompletedEvent>().Count(e => e.FrameId == reward.FrameId && e.ActionId == action.ActionId) == 1 &&
            s.E<GuquDrawReturnedEvent>().Single() is { RequestedCount: 2, ActualCount: 2, ReplacementCount: 0 } &&
            s.E<NullificationRespondedEvent>().Count(e => e.ResponderSeat == 0 && e.NullificationCardId == material) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.CardId == duel.CardId && e.CardKind == CardKind.Duel) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 && !s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1),
            "After source loss, the same paid native Nullification completes once and grants Draw2 from its original accepted claim; the nullified Duel has no damage or duplicate material cost."); s.Cold();
    }

    private static void MzyBaguaUseAndPlayed()
    {
        var s = MzyScenario.Start("bagua"); s.Use("equip-peer", targets: [1]); s.Play(); s.Stock(40);
        var marked = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id; s.ShowGive(marked, 1); var hp = s.V(1).Hp;
        s.Need(s.V(1).Equipment is [{ Kind: CardKind.BaguaFormation }] && s.V(1).Hand.Single().SkillMarkLabel == "阙", "The native armor holder actually has one revealed original Que in Hand.");
        s.Use("incoming", targets: [1]); s.Play();
        s.Need(s.V(1).Hp == hp - 1 && !s.E<JudgmentRequestedEvent>().Any() && !s.E<GuquStartedEvent>().Any(),
            "A true Slash's Dodge Use cannot evade the all-Que material rule with a zero-material Bagua offer or judgment."); s.Cold();
        var arrows = s.Actions().First(a => a.Kind == LegalActionKind.ArrowBarrage && a.ConversionSource is null); s.Native(arrows); s.Play();
        var judgment = s.E<JudgmentRequestedEvent>().Single(e => e.TargetSeat == 1 && e.Reason == JudgmentReasons.BaguaDefense && e.SourceCard == CardKind.ArrowBarrage);
        s.Need(s.E<JudgmentResolvedEvent>().Count(e => e.ResolutionId == judgment.ResolutionId && e.TargetSeat == 1 && e.Succeeded) == 1 &&
            s.V(1).Hp == hp - 1 && s.V(1).Hand.Single(c => c.Id == marked).SkillMarkLabel == "阙" && !s.E<GuquStartedEvent>().Any() &&
            s.E<CardUseFinishedEvent>().Count(e => e.CardId == arrows.CardId && e.CardKind == CardKind.ArrowBarrage) == 1,
            "The same armor still performs its real successful judgment against an AOE's pure Played Dodge response, preserving the Que original and creating no true-Use Guqu reward."); s.Cold();
    }

    private static void MzyActorReplacementClaim()
    {
        var s = MzyScenario.Start("actor-lineage"); s.Stock(4);
        var material = s.V().Hand[0].Id; s.Show(material);
        s.Native(s.Actions().First(a => a.CardId == material && a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1])));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        var original = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == material);
        var action = original.Action!; var claim = original.GuquClaims!.Single(); var admission = original.GuquAdmission;
        s.Need(action is { ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.Slash } && claim.ParentFrameId == original.Id && claim.ActionId == action.ActionId &&
            claim is { ActorSeat: 0, ProviderSeat: 0, QueMaterialCount: 1 } && admission is { ActorSeat: 0, ProviderSeat: 0 } &&
            s.E<GuquUseMaterialClaimedEvent>().Count(e => e.ActionId == action.ActionId && e.CardId == material && e.From == CardLocation.Hand(0)) == 1,
            "The natural publicly revealed Slash signs one original provider's claim before the existing native actor-replacement producer selects its new user.");
        s.Private(); s.RejectPrompt(); var stale = s.P!; s.Cold(); s.Answer(c => c.Targets.SequenceEqual([3])); s.RejectOld(stale);
        s.Reach(() => s.Hold("actor-draw"));
        var changed = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == original.Id); var reward = s.E<GuquStartedEvent>().Single();
        s.Need(changed.SourceSeat == 3 && changed.Action is { ActorSeat: 3, ProviderSeat: 0 } current && current.ActionId == action.ActionId &&
            current.PhysicalCards.SequenceEqual(action.PhysicalCards) && changed.CardId == material && changed.TargetSeats.SequenceEqual([1]) &&
            changed.GuquAdmission == admission && changed.GuquClaims!.Single() == claim && reward.Claim == claim &&
            s.E<ProgramCardUseActorReplacedEvent>().Count(e => e.CardUseFrameId == original.Id && e.PreviousActorSeat == 0 && e.ActorSeat == 3 && e.ProviderSeat == 0) == 1 &&
            s.E<CardActionAcceptedEvent>().Count(e => e.Action.ActionId == action.ActionId) == 1 && !s.E<GuquCompletedEvent>().Any(),
            "The native same-Use replacement changes only its actor through one real lineage fact; original ActionID, provider, physical material, admission and Guqu claim remain exact while the source's Draw2 child is suspended.");
        s.Private(); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<GuquUseClaimedEvent>().Count(e => e.Claim.ActionId == action.ActionId && e.Claim.Source == claim.Source) == 1 &&
            s.E<GuquCompletedEvent>().Count(e => e.FrameId == reward.FrameId && e.ActionId == action.ActionId) == 1 &&
            s.E<GuquDrawReturnedEvent>().Single() is { RequestedCount: 2, ActualCount: 2, ReplacementCount: 0 } && s.V(3).HandCount == 0 &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == original.Id && e.CardId == material && e.SourceSeat == 0) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == original.Id && e.CardId == material && e.CardKind == CardKind.Slash) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "A changed native actor cannot erase, duplicate or receive the original source's Guqu reward: its same accepted Use pays the original entity, finishes and draws exactly once after cold return."); s.Cold();
    }

    private sealed class MzyScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Has(int seat, string skill) => V(seat).Skills?.Any(k => k.ContentId == skill) == true;
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public bool Hold(string id) => P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Watch && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public bool ZfChoice(string branch) => P?.SkillPrompt?.SkillId == Zf && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-zuifeng" && c.Parameters.GetValueOrDefault("branch") == branch);
        public void Stock(int count) { while (count > 0) { var n = Math.Min(20, count); Use(n == 20 ? "stock-twenty" : n == 12 ? "stock-twelve" : n == 4 ? "stock-four" : n == 2 ? "stock-two" : "stock-one"); Play(); count -= n; } }
        public void Give(int[] ids, int seat) { if (ids.Length == 0) return; Use("give", ids, [seat]); Play(); }
        public void Keep(int[] kept, int seat) => Give(V().Hand.Where(c => !kept.Contains(c.Id)).Select(c => c.Id).ToArray(), seat);
        public void Show(int id) { Use("show"); Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true); Answer(c => c.Cards.SequenceEqual([id])); Play(); }
        public void ShowGive(int id, int seat) { Use("show-give", [id], [seat]); Play(); }
        public void Take(int seat, int id)
        {
            var slot = V(seat).Hand.Select((c, index) => (c.Id, index)).Single(e => e.Id == id).index; Use("take", targets: [seat]);
            Reach(() => P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true); Need(P!.Choices.All(c => c.Cards.Count == 0), "Foreign Hand choices disclose stable slots rather than other private entity faces.");
            Answer(c => c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(CultureInfo.InvariantCulture));
        }
        public void BeginZf(int cost, int target)
        {
            Accept(new UseProgramSkillCommand(0, Zf, ZfBinding, [], [], G.Revision, P!.PromptId)); Reach(() => ZfChoice("cost")); ZfAnswer("cost", cost); ZfAnswer("target", target: target); Reach(() => ZfChoice("show"));
        }
        public void ZfAnswer(string branch, int? card = null, int? target = null) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-zuifeng" && c.Parameters.GetValueOrDefault("branch") == branch &&
            (card is null || c.Cards.SequenceEqual([card.Value])) && (target is null || c.Targets.SequenceEqual([target.Value])));
        public void Use(string binding, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, binding, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        private PlayCardCommand NativeCommand(LegalAction a) => new(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources };
        public void Native(LegalAction a) => Accept(NativeCommand(a));
        public void RejectSource(LegalAction a) => Reject(NativeCommand(a) with { ConversionSource = a.ConversionSource! with { SkillInstanceId = "forged-mou-zhou-yu-source" } });
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Advance() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "Only the actual human answers one exact published choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "The real scenario ended before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Advance(); else if (p.Kind == DecisionKind.PlayCard) End(); else if (p.SkillPrompt?.SkillId == Watch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.SkillPrompt?.SkillId == Insert && p.Choices.Count == 1 && p.Choices[0].Targets.SequenceEqual([0])) Answer(c => c.Id == p.Choices[0].Id);
                else if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die" or "faction-slash-decline" or "faction-dodge-decline");
                else throw new InvalidOperationException("Unexpected true native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded true native boundary " + line + " " + Diagnostic());
        }
        public void Private(int viewer = 0) => Need(P is { IsPrivate: true } p && p.PlayerSeat == viewer && Enumerable.Range(0, 4).Where(v => v != viewer).All(v => G.CreateSnapshot(v).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(p => p.Seat != v).All(p => p.Hand.Count == 0)), "Private choices and other Hands stay absent from every unauthorized prepared player view.");
        public void PrivateView(int viewer, int target)
        {
            Private(viewer); var ids = V(target).Hand.Select(c => c.Id).ToArray();
            Need(G.CreateSnapshot(viewer).PrivateRevealedCards is { } all && all.Select(c => c.Id).SequenceEqual(ids) &&
                Enumerable.Range(0, 4).Where(v => v != viewer).All(v => G.CreateSnapshot(v).PrivateRevealedCards is not { Count: > 0 }) &&
                !E<MouZuifengShownEvent>().Any(e => e.FrameId == G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MouZuifeng is { Stage: MouZuifengStage.HandViewChoice }).Id),
                "The skill owner privately sees all actual target Hand originals, while public views have not yet received the chosen show.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold replay preserves exact private views, selected originals, phase allowance, source claims, RNG and owning native children."); }
        public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && State() == before, "A forged, stale or unauthorized command cannot change the prepared state."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("unpublished-mou-zhou-yu"), G.Revision)); }
        public void RejectOld(PendingDecision p) => Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision));
        public void Accept(GameCommand command) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + " " + result.Error?.Code + "/" + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " cause=" + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner,
            Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount, V(i).IsAlive }),
            Prompt = P is not { } p ? null : new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, ChoiceCount = p.Choices.Count, Choices = p.Choices.Take(5) },
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, Zuifeng = (f as ProgramSkillFrame)?.MouZuifeng, Guqu = (f as ProgramSkillFrame)?.Guqu }),
            Paid = E<MouZuifengCostPaidEvent>().TakeLast(4), Shown = E<MouZuifengShownEvent>().TakeLast(3), Wine = E<MouZuifengAlcoholIssuedEvent>().TakeLast(2), Guqu = E<GuquStartedEvent>().TakeLast(3), Completed = E<GuquCompletedEvent>().TakeLast(3) });
        public static MzyScenario Start(string scene)
        {
            ContentRegistry r; try { r = ContentRegistry.Build(new StandardContentPackage(), new MzyFixture(scene)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene is "cost-death" or "true-response-death" ? Role.Renegade : Role.Lord, ModeId = Mode,
                UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new MzyScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, Owner, g.Revision, s.P!.PromptId)); s.Play(); return s;
        }
    }

    private sealed class MzyFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-mou-zhou-yu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Real(string bundle, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            }
            Real("mou-zhou-yu-ol", Zf, Gq); if (scene == "declaration") { Real("classic-yu-ji", "classic:guhuo", "classic:chanyuan"); Real("ol-sun-hong", Zenrun); }
            static JsonNode N(string json) => JsonNode.Parse(json)!;
            static JsonObject A(string id, string effects, int min = 0, int max = 0, int targets = 0) => new() { ["id"] = id, ["minCards"] = min, ["maxCards"] = max, ["minTargets"] = targets, ["maxTargets"] = targets,
                ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["sourceZones"] = N("[\"hand\"]"), ["effects"] = N("[" + effects + "]") };
            static string Pause(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held-" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            const string capture = "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"selected\"}";
            const string show = "{\"op\":\"revealBoundCards\",\"target\":\"owner\",\"sourceBind\":\"selected\"}";
            const string give = "{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"selected\",\"destination\":\"selectedTargetHand\"}";
            var acts = new JsonArray(A("stock-twenty", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"), A("stock-twelve", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":12}"),
                A("stock-four", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":4}"), A("stock-two", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":2}"), A("stock-one", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}"),
                A("give", capture + "," + give, 1, 20, 1), A("show", "{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"zones\":[\"hand\"],\"minimumCards\":1,\"maximumCards\":1,\"resultBind\":\"selected\"}," + show), A("show-give", capture + "," + show + "," + give, 1, 1, 1),
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("incoming", "{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}", targets: 1), A("lose-one", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("recover-one", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"),
                A("peer-lose-two", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":2}", targets: 1));
            var grainScene = scene.StartsWith("grain-", StringComparison.Ordinal);
            if (grainScene)
            {
                acts.Add(A("show-give-two", capture + "," + show + "," + give, 2, 2, 1));
            }
            if (grainScene || scene == "bagua") acts.Add(A("equip-peer", "{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"gear\"}", targets: 1));
            var foreignDuel = A("foreign-duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); foreignDuel["targetKind"] = "otherLiving"; foreignDuel["usesPerPhase"] = 2; acts.Add(foreignDuel);
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts, ["modifiers"] = N("[{\"id\":\"keep-real-inventory\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":80,\"priority\":0}]") },
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var wine = scene.StartsWith("wine", StringComparison.Ordinal); var own = new List<string> { Driver };
            if (!wine) { own.Add(scene is "quota" or "cost-loss" or "cost-death" ? Conditional : Zf); if (scene != "quota") own.Add(scene is "declaration" or "null-policy" || scene.StartsWith("true-response", StringComparison.Ordinal) ? Conditional : Gq); }
            if (scene == "declaration") { own.Add("classic:guhuo"); own.Add(Zenrun); }
            if (scene is "quota" or "cost-loss" or "cost-death" or "true-response" or "true-response-death" or "declaration" or "null-policy" or "wine-loss" or "wine-death")
                defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy\",\"skillId\":\"" + (scene is "quota" or "cost-loss" or "cost-death" ? Zf : Gq) + "\",\"minimumHp\":" + (scene == "quota" ? 3 : scene == "true-response-death" ? 11 : scene is "cost-death" or "true-response" or "wine-loss" or "wine-death" ? 12 : 13) + "}]}"));
            if (scene == "quota")
            {
                own.Add(Insert); defs.Add(N("{\"id\":\"" + Insert + "\",\"revision\":1,\"triggers\":[{\"id\":\"actual-extra-play\",\"window\":\"playEnding\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"insertPhase\",\"target\":\"selectedTarget\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]}"));
            }
            var watch = new JsonArray();
            if (scene == "quota") watch.Add(N("{\"id\":\"real-fire\",\"window\":\"afterDamageApplied\",\"subject\":\"damageSource\",\"damageOccurrence\":\"perDamage\",\"optional\":false,\"effects\":[" + Pause("fire") + "]}"));
            if (scene.StartsWith("cost-", StringComparison.Ordinal)) watch.Add(N("{\"id\":\"actual-zf-cost\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + ZfDiscard + "\"],\"optional\":false,\"effects\":[" + Pause("zf-cost") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "cost-death" ? 20 : 1) + "}]}"));
            if (wine && scene != "wine-decline") watch.Add(N("{\"id\":\"actual-foreign-wine\",\"window\":\"cardUseCommitted\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"alcohol\"],\"optional\":false,\"effects\":[" + Pause("wine-use") +
                (scene is "wine-loss" or "wine-death" ? ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"eventTarget\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":" + (scene == "wine-death" ? 20 : 1) + "}," + Pause("wine-changed") : "") + "]}"));
            if (scene.StartsWith("true-response", StringComparison.Ordinal)) watch.Add(N("{\"id\":\"actual-guqu-draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + GqDraw + "\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("guqu-draw") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "true-response-death" ? 20 : 1) + "}]}"));
            if (scene == "equipment-lion") watch.Add(N("{\"id\":\"real-lion\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Pause("lion") + "]}"));
            if (scene == "equipment-ox") watch.Add(N("{\"id\":\"real-ox-spill\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"optional\":false,\"effects\":[" + Pause("spill") + "]}"));
            if (scene == "grain-que") watch.Add(N("{\"id\":\"real-grain-response-draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + GqDraw + "\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("grain-draw") + "]}"));
            if (scene == "actor-lineage")
            {
                watch.Add(N("""
                    {"id":"actual-actor","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}
                    """));
                watch.Add(N("{\"id\":\"real-actor-lineage-draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + GqDraw + "\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("actor-draw") + "]}"));
            }
            if (scene == "null-policy")
            {
                const string policy = "fixture:mzy-native-null-policy"; own.Add(policy);
                defs.Add(N("{\"id\":\"" + policy + "\",\"revision\":1,\"cardPolicies\":[{\"id\":\"real-unrespondable-null\",\"kind\":\"unrespondableNullification\",\"cardKinds\":[\"nullification\"]}]}"));
                watch.Add(N("{\"id\":\"real-null-payment\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + CardMoveReasons.Nullification.Value + "\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("null-paid") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}," + Pause("null-changed") + "]}"));
            }
            if (scene == "declaration")
            {
                watch.Add(N("{\"id\":\"real-declaration-payment\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"conversion.declaration.pay\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("declaration-paid") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}," + Pause("declaration-changed") + "]}"));
                watch.Add(N("{\"id\":\"real-replacement-obtain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.sun-hong.zenrun.obtain\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("replacement") + "]}"));
            }
            if (watch.Count > 0) { own.Add(Watch); defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watch }); }
            const string fallback = "fixture:mzy-fallback";
            if (scene == "whitelist") defs.Add(N("{\"id\":\"" + fallback + "\",\"revision\":1,\"viewAs\":[{\"id\":\"actual-slash-as-dodge\",\"inputKinds\":[\"slash\"],\"inputSuits\":[],\"outputKind\":\"dodge\",\"forPlay\":false,\"forResponse\":true}]}"));
            const string grainStore = "fixture:mzy-real-grain-storage";
            if (grainScene) defs.Add(N("""
                {"id":"fixture:mzy-real-grain-storage","revision":1,"triggers":[{"id":"store-original-dodge","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,"condition":{"kind":"compare","left":{"kind":"currentHandCount"},"operator":"greaterThanOrEqual","right":{"kind":"integerConstant","value":2}},"effects":[{"op":"selectOwnedCards","target":"owner","zones":["hand"],"cardKinds":["dodge"],"minimumCards":1,"maximumCards":1,"resultBind":"real-grain"},{"op":"moveBoundCards","target":"owner","sourceBind":"real-grain","destination":"ownerPersistentZone","destinationZone":"woodenOxGrain"}]}]}
                """));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "谋周瑜真实实体与原生孩子夹具", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "实际谋周瑜行为夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:mzy-pick"; b.AddSkill(new(pick, "真人", "固定实际本人") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            b.AddGeneral(new(Owner, "真实谋周瑜观察者", "supporter", pick, "wu", scene == "quota" ? 2 : 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:mzy-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pp = "fixture:mzy-pick-" + i; b.AddSkill(new(pp, "其他角色", "固定公开选择") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                var skills = new List<string>(); if (wine && i == 1) { skills.Add(Zf); skills.Add(scene is "wine-loss" or "wine-death" ? Conditional : Gq); } else skills.Add(Quiet);
                if (scene == "whitelist" && i == 1) skills.Add(fallback); if (grainScene && i == 1) skills.Add(grainStore); b.AddGeneral(new(peers[i - 1], "实际其他角色", "supporter", pp, "shu", 12, skills));
            }
            var kinds = new[] { CardKind.Slash, CardKind.Dodge, CardKind.Duel, CardKind.Crossbow, CardKind.Alcohol, CardKind.IronChain, CardKind.WoodenOx, CardKind.SilverLion, CardKind.Nullification, CardKind.BaguaFormation, CardKind.ArrowBarrage };
            foreach (var k in kinds) { var c = CardCatalog.Get(k); b.AddCard(new("fixture:mzy-" + k, c.DisplayName, c.CategoryName, "原印牌面不因技能改动", k)); }
            var same = wine || scene == "actor-lineage" || scene.StartsWith("cost-", StringComparison.Ordinal) || scene.StartsWith("equipment-", StringComparison.Ordinal); var homogeneous = scene is "zones" or "equipment-ox" ? CardKind.WoodenOx : scene == "equipment-lion" ? CardKind.SilverLion : scene == "actor-lineage" ? CardKind.Slash : CardKind.Dodge;
            var cards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("fixture:mzy-" + (grainScene ? i == 0 ? CardKind.WoodenOx : i % 2 == 0 ? CardKind.Slash : CardKind.Dodge : scene == "bagua" ? i == 0 ? CardKind.BaguaFormation : i % 2 == 0 ? CardKind.Slash : CardKind.ArrowBarrage : scene == "null-policy" ? i < 24 ? CardKind.Nullification : CardKind.Duel : same || scene == "zones" ? homogeneous : kinds[i / 8]), scene == "bagua" ? Suit.Heart : same || scene == "null-policy" ? Suit.Spade : scene == "zones" ? Suit.Club : new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i % 13 + 1)).ToArray();
            b.AddDeck(new("fixture:mzy-deck", "固定四十八实体库存", 0, 0, []) { PhysicalCards = cards });
            var roles = scene is "cost-death" or "true-response-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "完整OL谋周瑜原生行为", 4, 4, roles, "fixture:mzy-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
