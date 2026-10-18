using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class OlMouHuangYueyingChecks
{
    private const string Lixian = "mou-ol:lixian", Bingcai = "mou-ol:bingcai", Fengzi = "ol:fengzi";
    private const string Driver = "fixture:omhy-driver", Watch = "fixture:omhy-watch", Conditional = "fixture:omhy-conditional";
    private const string Owner = "fixture:omhy-owner", Mode = "identity:classic-omhy";
    private const string WineRescue = "fixture:omhy-self-wine";

    public static void BingcaiGlobalFirstBasicTrueUseAndRecastClassificationCold()
    {
        var s = MhyScenario.Start("first");
        s.Use("slash", [1]); var first = s.BeginBingcai(); var cost = s.V().Hand.First().Id;
        s.PayBingcai(cost); s.Play();
        var paid = s.E<MouBingcaiRecastPaidEvent>().Single(); var invoice = s.E<NativeRecastInvocationRecordedEvent>().Single(e => e.InvocationId == paid.RecastInvocationId);
        s.Need(first.BasicKind == CardKind.Slash && paid.MatchesDamage && !paid.IsTrick && paid.CardId == cost &&
            invoice is { InitiatorSeat: 0, ExecutorSeat: 0, PrimaryCount: 1, ActualRecastCount: 1, ActualDrawCount: 1 } && invoice.ParentFrameId == first.FrameId &&
            s.E<MouBingcaiExtraEffectIssuedEvent>().Single().UseId == first.UseId && s.E<MouBingcaiExtraEffectRoundCompletedEvent>().Count(e => e.UseId == first.UseId) == 1,
            "The first real global Basic Use owns one complete physical recast, one Draw1 and one extra resolution of the original Slash.");
        s.Use("slash", [1]); s.Play();
        s.Need(s.E<MouBingcaiStartedEvent>().Length == 1 && s.G.CardMovements.Count(m => m.CardId == cost && m.Reason == CardMoveReasons.RecastDiscard) == 1,
            "A later Basic in the same actual turn neither reopens Bingcai nor repays the first material."); s.Cold();

        var late = MhyScenario.Start("late"); late.Use("slash", [1]); late.Play(); late.Use("grant-bingcai"); late.Play(); late.Use("slash", [1]); late.Play();
        late.Need(late.E<MouBingcaiStartedEvent>().Length == 0 && late.V().Skills!.Any(k => k.ContentId == Bingcai),
            "The actual first Basic before the source was granted still consumes this table-wide turn opportunity.");
        late.Use("extra"); late.Play(); var oldTurn = late.G.State.TurnNumber; late.End(); late.PlayAfter(oldTurn);
        late.Use("slash", [1]); late.BeginBingcai(); late.PayBingcai(late.V().Hand.First().Id); late.Play();
        late.Need(late.E<MouBingcaiStartedEvent>().Length == 1 && late.E<TurnStartedEvent>().Any(e => e.TurnNumber > oldTurn && e.ActorSeat == 0),
            "A genuinely pending extra turn has its own first Basic opportunity; an inserted skill does not fake a turn reset."); late.Cold();

        var duel = MhyScenario.Start("played"); duel.Use("duel", [1]); duel.Reach(() => duel.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        var response = duel.Respond(); duel.Play();
        duel.Need(duel.E<CardRespondedEvent>().Any(e => e.CardId == response && e.ResponderSeat == 0) && duel.E<MouBingcaiStartedEvent>().Length == 0,
            "A genuine Duel Played response does not invent a Basic Use or consume the first-Use opportunity.");
        duel.Use("slash", [1]); duel.BeginBingcai(); duel.PayBingcai(duel.V().Hand.First().Id); duel.Play(); duel.Cold();

        var dodge = MhyScenario.Start("dodge"); dodge.End(); dodge.Reach(() => dodge.Optional(Bingcai)); dodge.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        dodge.Reach(() => dodge.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); var id = dodge.Respond(); dodge.Play();
        dodge.Need(dodge.E<CardRespondedEvent>().Any(e => e.CardId == id && e.ResponderSeat == 0) && dodge.E<MouBingcaiStartedEvent>().Length == 0,
            "The real Dodge responds to an earlier first Slash; it cannot become another global first Basic by its response direction."); dodge.Cold();
        foreach (var scene in new[] { "damage-trick", "delayed-trick" })
        {
            var category = MhyScenario.Start(scene); var hp = category.V(1).Hp; var material = category.V().Hand.First().Id;
            var expected = scene == "damage-trick" ? CardKind.UnexpectedAssault : CardKind.Lightning;
            category.Use("slash", [1]); var start = category.BeginBingcai(); category.PayBingcai(material, CardKind.IronChain); category.Play();
            var payment = category.E<MouBingcaiRecastPaidEvent>().Single();
            category.Need(payment.CardId == material && payment.PrintedKind == expected && payment.EffectiveKind == expected && payment.IsTrick &&
                payment.MatchesDamage == (scene == "damage-trick") &&
                category.E<NativeRecastInvocationRecordedEvent>().Single(e => e.InvocationId == payment.RecastInvocationId) is
                    { PrimaryCount: 1, ActualRecastCount: 1, ActualDrawCount: 1 } &&
                category.E<MouLixianNameAddedEvent>().Single().OutputKind == CardKind.IronChain &&
                category.G.CardMovements.Count(m => m.CardId == material && m.Reason == CardMoveReasons.RecastDiscard) == 1,
                "A real Instant/Delayed trick recast signs its actual entity, one whole recast and a player-selected newly added name.");
            category.Need(category.V(1).Hp == hp - (scene == "damage-trick" ? 2 : 1) &&
                category.E<MouBingcaiExtraEffectIssuedEvent>().Count(e => e.UseId == start.UseId) == (scene == "damage-trick" ? 1 : 0) &&
                category.E<MouBingcaiExtraEffectRoundCompletedEvent>().Count(e => e.UseId == start.UseId) == (scene == "damage-trick" ? 1 : 0) &&
                category.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == start.UseId) == 1,
                "The current explicit damage-card whitelist includes Unexpected Assault and excludes Lightning; the latter still unlocks a trick name without adding damage.");
            category.Cold();
        }
    }

    public static void BingcaiBasicEffectsAndIndependentExtraRoundsCold()
    {
        foreach (var scene in new[] { "peach", "alcohol", "additive" })
        {
            var s = MhyScenario.Start(scene); var before = s.V().Hp;
            if (scene == "peach") { s.Use("wound-two"); s.Play(); before = s.V().Hp; }
            var output = scene == "peach" ? CardKind.Peach : scene == "alcohol" ? CardKind.Alcohol : CardKind.Slash;
            var action = s.Actions().First(a => a.CardId is not null && (a.PlayedCardKind ?? s.V().Hand.Single(c => c.Id == a.CardId).Kind) == output && a.Kind != LegalActionKind.Recast);
            var original = action.CardId!.Value; var target = action.TargetSeats.FirstOrDefault(1); var targetHp = s.V(target).Hp;
            s.Native(action); var first = s.BeginBingcai(); var recast = s.V().Hand.First(c => c.Id != original).Id; s.PayBingcai(recast);
            if (scene == "additive")
            {
                s.Reach(() => s.Optional(Fengzi)); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                s.Reach(() => s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cost"));
                var other = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("branch") == "cost").Cards.Single();
                s.Need(other != original && other != recast, "Fengzi pays a distinct actual Hand entity rather than reusing either native material."); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([other]));
            }
            s.Play();
            s.Need(first.BasicKind == output && s.E<MouBingcaiExtraEffectIssuedEvent>().Count(e => e.UseId == first.UseId) == 1 &&
                s.E<MouBingcaiExtraEffectRoundCompletedEvent>().Count(e => e.UseId == first.UseId) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == first.UseId) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == original && m.To == CardLocation.Processing) == 1,
                "The extra round belongs to the original true Basic Use and keeps its original entity payment and whole completion once.");
            if (scene == "peach") s.Need(s.V().Hp == before + 2 && s.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 2,
                "A first Peach truly performs two separate native recovery effects rather than manufacturing another Peach Use.");
            else if (scene == "alcohol") s.Need(s.V().HasAlcoholEffect && s.V().AlcoholDamageBonus == 2,
                "A first Alcohol accumulates the actual two-point native Wine benefit without a second physical use.");
            else s.Need(s.V(target).Hp == targetHp - 3 && s.E<ExtraCardEffectIssuedEvent>().Count(e => e.CardUseFrameId == first.UseId) == 1 &&
                s.E<ExtraCardEffectStartedEvent>().Count(e => e.CardUseFrameId == first.UseId) == 1 &&
                s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == target && e.Amount == 1) == 3,
                "Bingcai and Fengzi independently add one round to the same real Slash: three actual damage effects, two separate invoices, one Use.");
            s.Cold();
        }
        var wine = MhyScenario.Start("legacy-wine"); wine.Use("dying");
        wine.Reach(() => wine.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying } p &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "program-trigger" &&
                c.Parameters.GetValueOrDefault("skill-id") == WineRescue && c.Parameters.GetValueOrDefault("binding-id") == "self-wine"));
        wine.Need(wine.V().Hp == 0 && wine.G.ResolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == 0) &&
            wine.E<CardUseDeclaredEvent>().Length == 0, "Real HP loss opens the owner's self-dying window before this table's first Basic Use.");
        wine.Private(); wine.Cold(); wine.Answer(c => c.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            c.Parameters.GetValueOrDefault("skill-id") == WineRescue && c.Parameters.GetValueOrDefault("binding-id") == "self-wine" &&
            c.Parameters.GetValueOrDefault("target-seat") == "0" && c.Cards.Count == 0);
        var firstWine = wine.BeginBingcai(); var legacy = wine.E<MouBingcaiLegacyAlcoholIssuedEvent>().Single();
        var nativeWine = wine.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == firstWine.UseId);
        wine.Need(firstWine.BasicKind == CardKind.Alcohol && legacy.UseId == firstWine.UseId && legacy.ActionId == firstWine.ActionId &&
            nativeWine is { CardId: 0, PhysicalCardIds.Count: 0, MouBingcaiLegacyAlcohol: not null } &&
            nativeWine.Action is { Type: CardActionType.Use, EffectiveKind: CardKind.Alcohol, EffectiveSuit: Suit.None, EffectiveRank: 0, PhysicalCards.Count: 0 } &&
            wine.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == legacy.ProgramParentId && f.SkillId == WineRescue && f.WindowContext?.Window == SkillProgramTriggerWindow.SelfDyingResponse),
            "The genuine legacy zero-material Alcohol keeps the original self-rescue program, Dying frame and normalized first-Basic action.");
        wine.PayBingcai(wine.V().Hand.First().Id); wine.Reach(() => wine.Hold("wine-recovery"));
        wine.Need(wine.V().Hp == 1 && wine.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            !wine.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == firstWine.UseId),
            "The original native self-rescue Recovery1 waits for its real HP child before the extra round or whole Use can finish.");
        wine.Private(); wine.Cold(); wine.Continue(); wine.Reach(() => wine.Hold("wine-recovery"));
        wine.Need(wine.V().Hp == 2 && wine.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 2 &&
            wine.E<MouBingcaiExtraEffectRoundStartedEvent>().Count(e => e.UseId == firstWine.UseId) == 1 &&
            !wine.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == firstWine.UseId),
            "The independent Bingcai round performs a second actual Recovery1 and waits for its own native HP child.");
        wine.Private(); wine.Cold(); wine.Continue(); wine.Play();
        wine.Need(wine.E<MouBingcaiLegacyAlcoholIssuedEvent>().Length == 1 && wine.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == firstWine.UseId) == 1 &&
            wine.E<MouBingcaiRecastPaidEvent>().Length == 1 && wine.E<MouBingcaiDrawReturnedEvent>() is [{ ActualDrawCount: 1, ReplacementGainedCount: 0 }] &&
            wine.E<MouBingcaiExtraEffectRoundCompletedEvent>().Count(e => e.UseId == firstWine.UseId) == 1 &&
            !wine.G.ResolutionStack.OfType<DyingFrame>().Any() && wine.V().Hp == 2,
            "Cold return completes one recast/Draw1, one real self-rescue Use and one extra recovery round without replaying the legacy producer."); wine.Cold();
    }

    public static void LixianOriginalMaterialNamesAndSeparatePhaseUnlocksCold()
    {
        foreach (var scene in new[] { "lixian-hand", "lixian-equipment", "lixian-ox" })
        {
            var s = MhyScenario.Start(scene); int material;
            if (scene != "lixian-hand")
            {
                var equipment = s.V().Hand.First().Id; s.Native(s.Actions().First(a => a.CardId == equipment && a.Kind == LegalActionKind.Equip)); s.Play();
                if (scene == "lixian-ox") { material = s.V().Hand.First().Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [material], [], s.G.Revision, s.P!.PromptId)); s.Play(); }
                else { material = equipment; s.Use("wound-one"); s.Play(); }
            }
            else material = s.V().Hand.First().Id;
            s.Use("hurt-self"); var used = s.ResolveLixian(CardKind.DrawTwo, material); s.Play();
            var expectedFrom = scene == "lixian-hand" ? CardLocation.Hand(0) : scene == "lixian-ox" ? CardLocation.WoodenOxGrain(0) : CardLocation.Equipment(0);
            s.Need(used.CardId == material && used.From == expectedFrom && used.EffectiveSuit == Suit.Club && used.EffectiveRank == 7 &&
                s.G.CardMovements.Count(m => m.CardId == material && m.From == expectedFrom && m.To == CardLocation.Processing) == 1 &&
                s.E<MouLixianNameUsedEvent>().Single().OutputKind == CardKind.DrawTwo && s.E<MouLixianUseReturnedEvent>().Count(e => e.UseId == used.UseId) == 1,
                "Lixian uses one original Hand/equipment/Ox entity with its real suit, rank, native Processing cost and one typed return, never a zero-material substitute."); s.Cold();
        }
        var physical = MhyScenario.Start("lixian-physical"); physical.End(); var physicalFirst = physical.BeginBingcai();
        var physicalAttack = physical.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == physicalFirst.UseId);
        physical.Need(physicalAttack is { CardKind: CardKind.Slash, CardId: > 0, PhysicalCardIds.Count: 1 } &&
            physicalAttack.SourceSeat != 0 && physicalAttack.TargetSeats.Contains(0) &&
            physicalAttack.PhysicalCardIds![0] == physicalAttack.CardId &&
            physical.G.CreateCardZoneDiagnostics().Single(c => c.CardId == physicalAttack.CardId).Location == CardLocation.Processing,
            "An actual foreign AI Hand Slash, with its one paid physical entity still Processing, is this turn's first Basic and attacks the real owner.");
        var physicalRecast = physical.V().Hand.FirstOrDefault(c => c.Kind == CardKind.IronChain);
        physical.Need(physicalRecast is not null, "The fixed seed's actual owner Hand must contain the real IronChain recast material; no entity is injected or seed searched.");
        physical.PayBingcai(physicalRecast!.Id, CardKind.Snatch);
        var physicalInner = physical.ResolveLixian(CardKind.Snatch, target: physicalAttack.SourceSeat, outerPhysicalAttack: physicalAttack); physical.Play();
        physical.Need(physicalInner.CardId != physicalAttack.CardId &&
            physical.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == physicalAttack.Id && e.CardId == physicalAttack.CardId && e.CardKind == CardKind.Slash) == 1 &&
            physical.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == physicalInner.UseId && e.CardId == physicalInner.CardId) == 1 &&
            new[] { physicalAttack.CardId, physicalInner.CardId }.All(id =>
                physical.G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Processing) == 1 &&
                physical.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1) &&
            !physical.E<MouBingcaiExtraEffectIssuedEvent>().Any(e => e.UseId == physicalAttack.Id),
            "The mismatching IronChain recast adds Snatch without duplicating the Slash: original attack and nested native trick each finish and retire only their own one material once."); physical.Cold();

        foreach (var scene in new[] { "unlock-added", "unlock-used" })
        {
            var s = MhyScenario.Start(scene); s.End();
            var added = new[] { CardKind.Snatch, CardKind.IronChain, CardKind.Dismantlement };
            var use = new[] { CardKind.DrawTwo, CardKind.Snatch, CardKind.IronChain };
            for (var i = 0; i < 3; i++)
            {
                s.Reach(() => s.Optional(Bingcai));
                if (scene == "unlock-used" && i == 2) s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else { s.BeginBingcai(); s.PayBingcai(s.V().Hand.First().Id, added[i]); }
                s.Reach(() => s.Optional(Lixian));
                if (scene == "unlock-added") s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else s.ResolveLixian(use[i], target: use[i] == CardKind.DrawTwo ? null : 1);
            }
            var names = s.E<MouLixianNameAddedEvent>().Select(e => e.OutputKind).Distinct().ToArray();
            var used = s.E<MouLixianNameUsedEvent>().Select(e => e.OutputKind).Distinct().ToArray();
            var markers = s.V().Markers ?? [];
            s.Need(scene == "unlock-added" ? names.Length == 3 && used.Length == 0 : names.Length == 2 && used.Length == 3,
                "Player-selected additional names and actual accepted Lixian output names are separate permanent histories.");
            s.Need(markers is IList { IsReadOnly: true } && markers.Any(m => m.Kind == PlayerMarkerKind.MouLixianAvailableNames) &&
                (scene == "unlock-added" ? markers.Single(m => m.Kind == PlayerMarkerKind.MouLixianAvailableNames).Name.Contains("准备") :
                    markers.Single(m => m.Kind == PlayerMarkerKind.MouLixianUsedNames).Name.Contains("结束")),
                "Actual player snapshots expose the correct permanent name progress and only the corresponding unlocked phase."); s.Cold();
            if (scene == "unlock-added")
            {
                s.Reach(() => s.Optional(Lixian) && s.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(f => f.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow));
                s.ResolveLixian(CardKind.DrawTwo); s.Play();
                s.Need(s.E<MouLixianStartedEvent>().Any(e => e.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow), "All three added names enable a real next Preparation opportunity.");
            }
            else
            {
                s.Play(); s.End(); s.Reach(() => s.Optional(Lixian) && s.G.State.CurrentSeat == 0);
                s.ResolveLixian(CardKind.DrawTwo); s.Reach(() => s.E<MouLixianCompletedEvent>().Length >= 4);
                s.Need(s.E<MouLixianStartedEvent>().Any(e => e.Window == SkillProgramTriggerWindow.TurnEnding) &&
                    !s.E<MouLixianStartedEvent>().Any(e => e.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow),
                    "Three actually used names enable Ending without granting Preparation from only two added names.");
            }
            s.Cold();
        }
    }

    public static void BingcaiPaidForeignUseEquipmentLossAndDeathCold()
    {
        foreach (var scene in new[] { "lion", "ox", "loss", "death" })
        {
            var s = MhyScenario.Start(scene); int cost; int? grain = null;
            if (scene is "lion" or "ox")
            {
                cost = s.V().Hand.First().Id; s.Native(s.Actions().First(a => a.CardId == cost && a.Kind == LegalActionKind.Equip)); s.Play();
                if (scene == "lion") { s.Use("wound-one"); s.Play(); }
                else { grain = s.V().Hand.First().Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Value], [], s.G.Revision, s.P!.PromptId)); s.Play(); }
            }
            else cost = s.V().Hand.First().Id;
            s.End(); var start = s.BeginBingcai(); var native = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == start.UseId);
            s.Need(native.SourceSeat != 0 && native.Action?.ActorSeat == native.SourceSeat && start.Source.OwnerSeat == 0,
                "The recast belongs to this observer while the accepted first Basic is another character's original native Use.");
            if (grain is { } g) s.Need(!s.P!.Choices.Any(c => c.Cards.Contains(g)), "Bingcai excludes real Wooden Ox grain despite Lixian accepting that source.");
            s.Answer(c => s.Branch(c, "mou-bingcai", "material") && c.Cards.SequenceEqual([cost]));
            s.Reach(() => s.Hold(scene == "lion" ? "lion" : scene == "ox" ? "spill" : "paid"));
            var frame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == start.FrameId); var receipt = frame.MouBingcai!;
            s.Need(receipt.Stage == MouBingcaiStage.CostChildren && receipt.Paid && receipt.MaterialId == cost && frame.NativeRecastInvocation?.Materials.Count == 1 &&
                receipt.ActualDrawIds is IList { IsReadOnly: true } && receipt.ReplacementGainedIds is IList { IsReadOnly: true } &&
                !s.E<NativeRecastInvocationRecordedEvent>().Any(e => e.InvocationId == receipt.RecastInvocationId) &&
                !s.E<MouBingcaiDrawReturnedEvent>().Any(e => e.FrameId == frame.Id) && s.G.ResolutionStack.Any(f => f.Id == native.Id),
                "The genuine cost/recovery/spill child retains one original recast invoice before Draw1 and before the foreign mother Use can continue.");
            s.Private(); s.Cold(); s.Continue();
            s.Reach(() => s.E<MouBingcaiCompletedEvent>().Any(e => e.FrameId == start.FrameId));
            s.Reach(() => s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == start.UseId));
            var paid = s.E<MouBingcaiRecastPaidEvent>().Single(e => e.FrameId == start.FrameId);
            s.Need(s.G.CardMovements.Count(m => m.CardId == cost && m.Reason == CardMoveReasons.RecastDiscard) == 1 &&
                s.E<NativeRecastInvocationRecordedEvent>().Count(e => e.InvocationId == paid.RecastInvocationId && e.PrimaryCount == 1 && e.ActualRecastCount == 1) == 1 &&
                s.E<MouBingcaiCompletedEvent>().Count(e => e.FrameId == start.FrameId && e.Recast) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == start.UseId) == 1,
                "Cold return drains all real children and completes both recast and foreign Use once under the original paid rights.");
            if (scene is "loss" or "death") s.Need(s.E<MouBingcaiExtraEffectIssuedEvent>().Count(e => e.UseId == start.UseId) == 1 &&
                (scene == "death" ? !s.V().IsAlive : s.V().IsAlive && !s.V().Skills!.Any(k => k.ContentId == Bingcai)),
                "An actually lost source or dead skill owner does not revoke the paid matching extra round on somebody else's accepted Use.");
            if (grain is { } spill) s.Need(s.G.CardMovements.Count(m => m.CardId == spill && m.From == CardLocation.WoodenOxGrain(0) && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1,
                "The separate real Ox spill is drained once but never treated as another recast primary.");
            s.Cold();
        }
    }

    private sealed class MhyScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public bool Branch(PromptChoice c, string family, string branch) => c.Parameters.GetValueOrDefault("program-action") == family && c.Parameters.GetValueOrDefault("branch") == branch;
        public bool HasBranch(string family, string branch) => P is { PlayerSeat: 0 } p && p.Choices.Any(c => Branch(c, family, branch));
        public bool Optional(string skill) => P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void PlayAfter(int turn) => Reach(() => G.State.TurnNumber > turn && P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public MouBingcaiStartedEvent BeginBingcai()
        {
            Reach(() => Optional(Bingcai) || HasBranch("mou-bingcai", "material"));
            if (Optional(Bingcai)) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(() => HasBranch("mou-bingcai", "material")); Private(); RejectPrompt(); Cold();
            return E<MouBingcaiStartedEvent>().Last();
        }
        public void PayBingcai(int card, CardKind? added = null)
        {
            var frame = E<MouBingcaiStartedEvent>().Last().FrameId;
            Answer(c => Branch(c, "mou-bingcai", "material") && c.Cards.SequenceEqual([card]));
            Reach(() => HasBranch("mou-bingcai", "add-name") || E<MouBingcaiCompletedEvent>().Any(e => e.FrameId == frame));
            if (HasBranch("mou-bingcai", "add-name"))
            {
                var choices = P!.Choices.Where(c => Branch(c, "mou-bingcai", "add-name")).ToArray();
                Need(choices.Select(c => c.Parameters["output-kind"]).Distinct().Count() == choices.Length && choices.All(c => c.Cards.Count == 0),
                    "Only still-locked names are published as real private choices; material IDs do not select a name.");
                var stale = P!; Private(); Cold(); Answer(c => Branch(c, "mou-bingcai", "add-name") && (added is null || c.Parameters["output-kind"] == added.ToString()));
                RejectOld(stale);
            }
            Reach(() => E<MouBingcaiCompletedEvent>().Any(e => e.FrameId == frame));
        }
        public MouLixianUseIssuedEvent ResolveLixian(CardKind kind, int? material = null, int? target = null, CardUseFrame? outerPhysicalAttack = null)
        {
            Reach(() => Optional(Lixian) || HasBranch("mou-lixian", "name"));
            if (Optional(Lixian)) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(() => HasBranch("mou-lixian", "name")); Private(); RejectPrompt(); Cold();
            var frame = E<MouLixianStartedEvent>().Last().FrameId;
            Answer(c => Branch(c, "mou-lixian", "name") && c.Parameters.GetValueOrDefault("output-kind") == kind.ToString());
            Reach(() => HasBranch("mou-lixian", "material")); var old = P!;
            var selected = material ?? P!.Choices.First(c => Branch(c, "mou-lixian", "material")).Cards.Single();
            Private(); Cold(); Answer(c => Branch(c, "mou-lixian", "material") && c.Cards.SequenceEqual([selected])); RejectOld(old);
            Reach(() => HasBranch("mou-lixian", "target"));
            Need(P!.Choices.Where(c => Branch(c, "mou-lixian", "target")).All(c => c.Cards.SequenceEqual([selected])),
                "Every actual native target option retains exactly the one selected physical material.");
            Private(); Cold(); Answer(c => Branch(c, "mou-lixian", "target") && (target is null || c.Targets.SequenceEqual([target.Value])));
            Reach(() => Hold("native")); var issue = E<MouLixianUseIssuedEvent>().Single(e => e.FrameId == frame);
            var parent = G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == frame); var use = G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issue.UseId);
            Need(parent.MouLixian is { Stage: MouLixianStage.NativeUse, Paid: true, Targets: IList { IsReadOnly: true } } r &&
                r.MaterialId == selected && use.MouLixianOrigin is { } origin && origin.ProgramFrameId == frame && origin.Source == r.Source &&
                origin.GameplayHash == r.GameplayHash && origin.UseId == issue.UseId && origin.ActionId == issue.ActionId && origin.CardId == selected &&
                use.Action is { Type: CardActionType.Use } action && action.ActionId == issue.ActionId &&
                action.PhysicalCards is [{ CardId: var paid }] && paid == selected && use.PhysicalCardIds!.SequenceEqual([selected]) &&
                G.CreateCardZoneDiagnostics().Single(c => c.CardId == selected).Location == CardLocation.Processing &&
                !E<MouLixianUseReturnedEvent>().Any(e => e.FrameId == frame),
                "The actual committed native trick pauses with its real single Processing entity and exact owning Lixian return still outstanding.");
            if (outerPhysicalAttack is { } outer) Need(G.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == outer.Id &&
                f.SourceSeat == outer.SourceSeat && f.CardKind == CardKind.Slash && f.CardId == outer.CardId &&
                f.PhysicalCardIds is { Count: 1 } ids && ids[0] == outer.CardId) &&
                G.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.Processing).Select(c => c.CardId).Order()
                    .SequenceEqual(new[] { outer.CardId, selected }.Order()) &&
                !E<CardUseFinishedEvent>().Any(e => e.ResolutionId == outer.Id || e.ResolutionId == issue.UseId),
                "The live physical Slash ancestor and the nested paid Snatch retain exactly their distinct two Processing entities across the native committed child.");
            Private(); Cold(); Continue();
            if (scene == "unlock-used" && kind == CardKind.IronChain)
            {
                Reach(() => Hold("chain"));
                var change = E<CharacterStateChangedEvent>().Single(e => e.Change.ParentFrameId == issue.UseId &&
                    e.Change.Window == SkillProgramTriggerWindow.CharacterEnteredChain).Change;
                Need(G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.Id == change.Id &&
                    w.Window == SkillProgramTriggerWindow.CharacterEnteredChain && w.CharacterStateContinuation == CharacterStateContinuation.CardUse &&
                    w.ResumeProgramFrameId == issue.UseId && w.ResumeCardId == selected && w.ResumeCardKind == CardKind.IronChain) &&
                    G.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == issue.UseId && f.MouLixianOrigin?.ProgramFrameId == frame) &&
                    G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == frame && f.MouLixian is { Paid: true, Stage: MouLixianStage.NativeUse }) &&
                    V(change.TargetSeat).IsChained && change.TargetSeat == target &&
                    G.CardMovements.Count(m => m.CardId == selected && m.To == CardLocation.Processing) == 1 &&
                    G.CardMovements.Count(m => m.CardId == selected && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
                    !E<CardUseFinishedEvent>().Any(e => e.ResolutionId == issue.UseId) &&
                    !E<MouLixianUseReturnedEvent>().Any(e => e.FrameId == frame),
                    "Actual IronChain retires its real material once, then its native entered-chain child retains the original Use and Lixian parent before whole completion.");
                Private(); Cold(); Continue();
            }
            Reach(() => E<MouLixianCompletedEvent>().Any(e => e.FrameId == frame && e.Used));
            Need(E<MouLixianUseReturnedEvent>().Count(e => e.FrameId == frame && e.ActionId == issue.ActionId) == 1 &&
                E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issue.UseId) == 1, "The whole native Use returns to the same paid parent once.");
            if (scene == "unlock-used" && kind == CardKind.IronChain) Need(
                E<CharacterStateChangedEvent>().Count(e => e.Change.ParentFrameId == issue.UseId && e.Change.Window == SkillProgramTriggerWindow.CharacterEnteredChain) == 1 &&
                G.CardMovements.Count(m => m.CardId == selected && m.To == CardLocation.Processing) == 1 &&
                G.CardMovements.Count(m => m.CardId == selected && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
                "Returning from the entered-chain child neither repeats the real material payment nor issues another state-change opportunity.");
            if (kind == CardKind.Snatch) Need(E<TargetCardTakenEvent>().Count(e => e.ResolutionId == issue.UseId &&
                e.SourceSeat == 0 && e.TargetSeat == target && e.FromZone == CardZoneKind.Hand && e.PublicCardId is null && e.PublicCardKind is null) == 1,
                "The completed native Snatch takes one actual hidden Hand entity while its public effect fact retains Hand privacy.");
            return issue;
        }
        public int Respond()
        {
            var p = P!; var c = p.Choices.First(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") is not ("pass" or "take-damage" or "let-die"));
            var id = c.Cards.Single(); Private(); Cold(); Answer(choice => choice.Id == c.Id); return id;
        }
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "The actual human answers one exact published native choice.");
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Native fixture ended before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.SkillPrompt?.SkillId == Watch && p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) Continue();
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.Kind == DecisionKind.SelectTargetCard)
                {
                    Need(p.Choices.Count > 0 && p.Choices.All(c => c.Cards.Count == 0 &&
                        c.Parameters.GetValueOrDefault("action") == "target-card-slot" && c.Parameters.ContainsKey("slot-index")),
                        "The native hidden target selection exposes only published opaque slots, never foreign physical identities.");
                    var slot = p.Choices[0].Id; Private(); Cold(); Answer(c => c.Id == slot);
                }
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RespondSlash && p.Choices.Any(c => c.Cards.Count == 1)) Respond();
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Private() => Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(v => G.CreateSnapshot(v).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(p => p.Seat != v).All(p => p.Hand.Count == 0)),
            "Only the native chooser sees their private exact material/name/target options; every viewer preserves foreign Hand privacy.");
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold replay preserves all actual views, frozen invoices, original native parents, permanent names and accepted commands."); }
        public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && State() == before, "A forged, stale or foreign-seat input cannot mutate native state or payment."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices.First().Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("unpublished-mhy-choice"), G.Revision)); }
        public void RejectOld(PendingDecision retired) => Reject(new AnswerPromptCommand(0, retired.PromptId, retired.Choices.First().Id, G.Revision));
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, "Rejected " + command.GetType().Name + " " + result.Error?.Code + "/" + result.Error?.Message);
        }
        public void Need(bool value, string why) { if (!value) throw new InvalidOperationException(why + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner,
            Prompt = P is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(4) } : null,
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, Bing = (f as ProgramSkillFrame)?.MouBingcai, Lixian = (f as ProgramSkillFrame)?.MouLixian }),
            Recast = E<MouBingcaiRecastPaidEvent>().TakeLast(2), Added = E<MouLixianNameAddedEvent>().TakeLast(3), Used = E<MouLixianNameUsedEvent>().TakeLast(3) });
        public static MhyScenario Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new MhyFixture(scene));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "death" ? Role.Renegade : Role.Lord,
                ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new MhyScenario(scene, g, registry); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, Owner, g.Revision, s.P!.PromptId)); s.Play(); return s;
        }
    }

    private sealed class MhyFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-mou-huang-yueying", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            static IReadOnlyList<JsonNode> Production(IContentRegistryBuilder builder, string bundle, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var rules = Read("rules"); var c = SkillProgramCatalog.Load(rules, Read("presentation"));
                foreach (var id in ids) builder.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
                return JsonNode.Parse(rules)!["skills"]!.AsArray().Where(n => ids.Contains(n!["id"]!.GetValue<string>())).Select(n => n!.DeepClone()).ToArray();
            }
            var official = Production(b, "mou-huang-yueying-ol", Lixian, Bingcai);
            if (scene == "additive") official = official.Concat(Production(b, "ol-wang-rong", Fengzi)).ToArray();
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject Activation(string id, string effects, int targets = 0) => new()
            { ["id"] = id, ["minCards"] = 0, ["maxCards"] = 0, ["minTargets"] = targets, ["maxTargets"] = targets,
                ["targetKind"] = targets == 0 ? "anyLiving" : "otherLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            static string Pause(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"hold-" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var acts = new JsonArray(Activation("slash", "{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}", 1),
                Activation("wound-one", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"),
                Activation("wound-two", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}"),
                Activation("hurt-self", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}"),
                Activation("dying", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":9}"),
                Activation("grant-bingcai", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + Bingcai + "\"]}"),
                Activation("extra", "{\"op\":\"pendExtraTurn\",\"target\":\"owner\"}"));
            var duel = Activation("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", 1); duel["usesPerPhase"] = 2; acts.Add(duel);
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts },
                N("{\"id\":\"fixture:omhy-quiet\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            // The real source nodes share this catalog with the observer that references them.
            // Their registered definitions and presentation still come from the original embedded bundle above.
            foreach (var source in official) defs.Add(source);
            var watches = new JsonArray();
            if (scene.StartsWith("lixian-", StringComparison.Ordinal) || scene.StartsWith("unlock-", StringComparison.Ordinal))
                watches.Add(N("{\"id\":\"native-material-use\",\"window\":\"cardUseCommitted\",\"ownerRelation\":\"actor\",\"cardCategories\":[\"instantTrick\"],\"optional\":false,\"effects\":[" + Pause("native") + "]}"));
            if (scene == "unlock-used")
                watches.Add(N("{\"id\":\"actual-entered-chain\",\"window\":\"characterEnteredChain\",\"subject\":\"any\",\"optional\":false,\"effects\":[" + Pause("chain") + "]}"));
            if (scene == "legacy-wine")
            {
                defs.Add(N("{\"id\":\"" + WineRescue + "\",\"revision\":1,\"triggers\":[{\"id\":\"self-wine\",\"window\":\"selfDyingResponse\",\"subject\":\"owner\",\"optional\":true,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"useVirtualDyingAlcohol\",\"target\":\"owner\"}]}]}"));
                watches.Add(N("{\"id\":\"actual-wine-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Pause("wine-recovery") + "]}"));
            }
            if (scene == "lion") watches.Add(N("{\"id\":\"actual-lion\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Pause("lion") + "]}"));
            if (scene == "ox") watches.Add(N("{\"id\":\"actual-grain\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"optional\":false,\"effects\":[" + Pause("spill") + "]}"));
            if (scene is "loss" or "death") watches.Add(N("{\"id\":\"actual-paid-cost\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"card.recast.discard\"],\"optional\":false,\"effects\":[" + Pause("paid") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "death" ? 20 : 1) + "}]}"));
            if (watches.Count > 0) defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches });
            if (scene == "loss") defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy\",\"skillId\":\"" + Bingcai + "\",\"minimumHp\":9}]}"));
            var foreign = scene is "dodge" or "lion" or "ox" or "loss" or "death" or "unlock-added" or "unlock-used";
            if (foreign) defs.Add(N("{\"id\":\"fixture:omhy-foreign\",\"revision\":1,\"modifiers\":[{\"id\":\"real-range\",\"query\":\"attackRange\",\"operation\":\"add\",\"value\":4,\"priority\":0}],\"triggers\":[{\"id\":\"actual-preparation-slash\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实原实体和原生结算边界",
                optionLabels = (n["triggers"]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs.Where(pair => pair.Key is not (Lixian or Bingcai or Fengzi)))
                b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = p, ProgramPresentation = catalog.Presentations[id], Tags = SkillTag.Locked });
            const string pick = "fixture:omhy-owner-pick"; b.AddSkill(new(pick, "固定本人", "真实公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -1000000d) });
            var skills = new List<string> { Driver, Lixian }; if (watches.Count > 0) skills.Add(Watch);
            if (scene != "late") skills.Add(scene == "loss" ? Conditional : Bingcai); if (scene == "additive") skills.Add(Fengzi);
            if (scene == "legacy-wine") skills.Add(WineRescue);
            b.AddGeneral(new(Owner, "谋黄月英实际本人", "supporter", pick, "shu", 8, skills));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:omhy-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var id = "fixture:omhy-pick-" + i; b.AddSkill(new(id, "固定其他角色", "真实公开AI选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                var peerSkills = new List<string>(); if (scene != "lixian-physical" || i != 1) peerSkills.Add("fixture:omhy-quiet");
                if (foreign && (i == 1 || scene.StartsWith("unlock-", StringComparison.Ordinal))) peerSkills.Add("fixture:omhy-foreign");
                b.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", id, "wei", 12, peerSkills));
            }
            var kind = scene switch { "peach" => CardKind.Peach, "alcohol" => CardKind.Alcohol, "dodge" or "legacy-wine" => CardKind.Dodge,
                "damage-trick" => CardKind.UnexpectedAssault, "delayed-trick" => CardKind.Lightning,
                "lion" or "lixian-equipment" => CardKind.SilverLion, "ox" or "lixian-ox" => CardKind.WoodenOx,
                "lixian-hand" or "unlock-added" or "unlock-used" => CardKind.IronChain, _ => CardKind.Slash };
            const string physical = "fixture:omhy-entity"; var card = CardCatalog.Get(kind); b.AddCard(new(physical, card.DisplayName, card.CategoryName, "实际小牌堆原实体", kind));
            const string mixedIronChain = "fixture:omhy-physical-iron-chain";
            if (scene == "lixian-physical") { var ironChain = CardCatalog.Get(CardKind.IronChain); b.AddCard(new(mixedIronChain, ironChain.DisplayName, ironChain.CategoryName, "实际混合实体", CardKind.IronChain)); }
            b.AddDeck(new("fixture:omhy-deck", "固定原实体小牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(i => new ContentDeckPhysicalCard(scene == "lixian-physical" && i % 2 == 0 ? mixedIronChain : physical, Suit.Club, 7)).ToArray() });
            var roles = scene == "death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } :
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "谋黄月英真实原生小局", 4, 4, roles, "fixture:omhy-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
