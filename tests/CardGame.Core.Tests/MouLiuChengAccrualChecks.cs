using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouLiuChengAccrualChecks
{
    private const string Lueying = "mou-mobile:lueying", Yingwu = "mou-mobile:yingwu", Driver = "fixture:mlca-driver", Watch = "fixture:mlca-watch",
        Conditional = "fixture:mlca-conditional", Quiet = "fixture:mlca-quiet", Redirect = "fixture:mlca-redirect", Cancel = "fixture:mlca-cancel",
        Prevent = "fixture:mlca-prevent", Add = "fixture:mlca-add", Insert = "fixture:mlca-insert", Owner = "fixture:mlca-owner", Ally = "fixture:mlca-ally",
        EnemyA = "fixture:mlca-enemy-a", EnemyB = "fixture:mlca-enemy-b", Deck = "fixture:mlca-deck", BoatMode = "fixture:mlca-boat-mode";
    private static ShanheStartingCard C(CardKind kind, Suit suit = Suit.Spade, int rank = 7) => new(kind, suit, rank);

    public static void ActualTargetNotificationsIndependentQuotasAndCardFamiliesCold()
    {
        var multi = Scene.Start(new("native-fangtian-three-targets") { Hand = [C(CardKind.Slash)], Equipment = [C(CardKind.FangtianHalberd)], HoldBeforeAccrual = true });
        var slash = multi.V().Hand.Single().Id; multi.Native(CardKind.Slash, [1, 2, 3]); multi.Reach(multi.BeforeAccrual);
        var frozen = multi.UseFor(slash); multi.AssertClaims(frozen);
        multi.Need(multi.Earned(frozen.Id).Length == 0 && frozen.MouLiuChengAccrualClaims is [{ Targets.Count: 3 }],
            "The owning Use and native candidate window have a frozen three-target qualification before the accrual candidate executes.");
        multi.PublicBudgets(0, 0); multi.Private(); multi.RejectPrompt(); multi.Cold(); multi.PublicBudgets(0, 0);
        multi.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "accrue"); multi.Reach(multi.Held);
        var use = multi.UseFor(slash); var earned = multi.Earned(use.Id);
        multi.Need(earned.Length == 2 && earned.All(e => e.Source.SkillId == Lueying) && earned.Select(e => e.TargetSeat).SequenceEqual([1, 2]) &&
            earned.Select(e => e.PhaseUsed).SequenceEqual([1, 2]) && multi.E<MouLiuChengTargetAnnouncedEvent>().Where(e => e.UseId == use.Id).Select(e => e.TargetSeat).SequenceEqual([1, 2, 3]) &&
            Enumerable.Range(0, 4).All(viewer => multi.Chui(viewer) == 2) && multi.E<DamageAppliedEvent>().Length == 0,
            "One actual last-Hand Fangtian Slash notifies each real target, earns one per other target up to its own exact-Play cap two, and publicly exposes the same two permanent Chui before any damage.");
        multi.PublicBudgets(2, 0); multi.AssertClaims(use); multi.Private(); multi.RejectPrompt(); multi.Cold(); multi.PublicBudgets(2, 0); multi.Continue(); multi.Play();
        multi.Need(multi.PaidOnce(slash) && multi.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) == 1 && multi.Earned(use.Id).Length == 2,
            "Native multi-target damage and completion cannot repeat an already notified target or repay the actual Slash material."); multi.Cold();

        var caps = Scene.Start(new("independent-skill-caps") { Hand = [C(CardKind.Slash), C(CardKind.Slash), C(CardKind.Slash), C(CardKind.DrawTwo), C(CardKind.DrawTwo), C(CardKind.IronChain)] });
        caps.PublicBudgets(0, 0);
        foreach (var example in new[] { (CardKind.Slash, 1, 1, 0), (CardKind.DrawTwo, 0, 1, 1), (CardKind.Slash, 1, 2, 1),
                     (CardKind.Slash, 1, 2, 1), (CardKind.IronChain, 0, 2, 2), (CardKind.DrawTwo, 0, 2, 2) })
        {
            caps.Native(example.Item1, [example.Item2]); caps.Reach(caps.Held);
            caps.Need(caps.AllEarned().Count(e => e.Source.SkillId == Lueying) == example.Item3 && caps.AllEarned().Count(e => e.Source.SkillId == Yingwu) == example.Item4 &&
                caps.AllEarned().GroupBy(e => (e.Source.SkillId, e.Phase)).All(g => g.Count() <= 2),
                "Lueying and Yingwu have separate two-occurrence budgets in the same actual Play; a real implicit self DrawTwo and explicit self IronChain count for Yingwu only.");
            caps.PublicBudgets(example.Item3, example.Item4); caps.Cold(); caps.PublicBudgets(example.Item3, example.Item4); caps.Continue(); caps.Play();
        }
        caps.Need(caps.AllEarned().Length == 4 && caps.AllEarned().Select(e => e.Phase).Distinct().Count() == 1 &&
            caps.AllEarned().Where(e => e.Source.SkillId == Yingwu).All(e => e.TargetSeat == 0),
            "The two named skills share a Chui resource while retaining distinct exact-phase quotas and true self-target eligibility."); caps.Cold();

        foreach (var acquireLate in new[] { false, true })
        {
            var late = Scene.Start(new(acquireLate ? "late-source-no-retroactive-target" : "late-added-real-target") { Hand = [C(CardKind.IronChain)], LateAdd = true, YingwuOnly = acquireLate });
            late.Native(CardKind.IronChain, [0]); late.Reach(() => late.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
            late.Private(); late.Cold(); late.Answer(c => c.Targets.SequenceEqual([1])); late.Reach(late.Held);
            var iron = late.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.IronChain); var additions = late.Earned(iron.Id);
            late.Need(iron.TargetSeats.SequenceEqual([0, 1]) && additions.Select(e => e.TargetSeat).SequenceEqual(acquireLate ? [1] : [0, 1]) &&
                additions.All(e => e.Source.SkillId == Yingwu) && late.Has(Lueying) && late.Chui() == (acquireLate ? 1 : 2) &&
                late.E<MouLiuChengTargetAnnouncedEvent>().Where(e => e.UseId == iron.Id).Select(e => e.TargetSeat).SequenceEqual([0, 1]),
                "A genuinely appended target receives its own notification while the completed prefix is preserved; acquiring Lueying later cannot retroactively award the old self target.");
            late.AssertClaims(iron); late.Cold(); late.Continue(); late.Play(); late.Cold();
        }
        var dependency = Scene.Start(new("yingwu-without-lueying") { Hand = [C(CardKind.DrawTwo)], YingwuOnly = true }); dependency.Native(CardKind.DrawTwo, [0]); dependency.Reach(dependency.Held);
        dependency.Need(dependency.Has(Yingwu) && !dependency.Has(Lueying) && dependency.Chui() == 0 && dependency.AllEarned().Length == 0,
            "Yingwu accrual checks the actor's genuinely live Lueying source when that target is notified."); dependency.Cold();

        var redirected = Scene.Start(new("native-redirect-before-notification") { Hand = [C(CardKind.Slash)], RedirectPeer = true });
        var redirectCost = redirected.V(1).Hand.Single().Id; redirected.Native(CardKind.Slash, [1]); redirected.Reach(redirected.Held);
        var redirectedUse = redirected.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Slash);
        redirected.Need(redirectedUse.TargetSeats.SequenceEqual([2]) && redirected.Earned(redirectedUse.Id) is [{ TargetSeat: 2 }] && redirected.Chui() == 1 &&
            redirected.G.CardMovements.Count(m => m.CardId == redirectCost && m.From == CardLocation.Hand(1)) == 1 &&
            redirected.G.CardMovements.Count(m => m.CardId == redirectCost && m.To == CardLocation.DiscardPile) == 1 &&
            !redirected.E<MouLiuChengTargetAnnouncedEvent>().Any(e => e.UseId == redirectedUse.Id && e.TargetSeat == 1),
            "The real native redirect and owned-card payment finish before after-designation: only actual new target two accrues, never the earlier selected target one."); redirected.Cold(); redirected.Continue(); redirected.Play();

        var cancelled = Scene.Start(new("targets-cancel-after-earned") { CancelTargets = true,
            Hand = [C(CardKind.Slash), C(CardKind.Dodge), C(CardKind.Dodge), C(CardKind.Dodge), C(CardKind.Dodge), C(CardKind.Dodge)] });
        cancelled.Use("adjust-targets"); cancelled.Play(); cancelled.Native(CardKind.Slash, [1, 2]); cancelled.Play();
        var cancellation = cancelled.E<OverflowUseTargetsCanceledEvent>().Single();
        cancelled.Need(cancellation.Receipt.BeforeTargetSeats.SequenceEqual([1, 2]) && cancellation.Receipt.ResultTargetSeats.Count == 0 &&
            cancelled.Earned(cancellation.Receipt.CardUseFrameId).Length == 2 && cancelled.E<DamageAppliedEvent>().Length == 0 &&
            cancelled.E<MouLiuChengCompletionStartedEvent>().Single().MarkBefore == 2 && cancelled.E<MouLiuChengCompletionStartedEvent>().Single().MarkAfter == 0 && cancelled.Chui() == 0,
            "Native later cancellation of all card targets cannot retract already issued designation gains; the real completion subsequently debits those two marks rather than erasing their history."); cancelled.Cold();

        var borrowed = Scene.Start(new("borrowed-sword-primary-only") { Hand = [C(CardKind.BorrowedSword)] }); borrowed.Use("equip-peer", [1]); borrowed.Play();
        borrowed.Native(CardKind.BorrowedSword, [1, 2]); borrowed.Reach(borrowed.Held); var pair = borrowed.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.BorrowedSword);
        borrowed.Need(pair.TargetSeats.SequenceEqual([1, 2]) && borrowed.Earned(pair.Id) is [{ TargetSeat: 1, Source.SkillId: Yingwu }] && borrowed.Chui() == 1 &&
            !borrowed.E<MouLiuChengTargetAnnouncedEvent>().Any(e => e.UseId == pair.Id && e.TargetSeat == 2),
            "Borrowed Sword is a non-damage ordinary trick but only its weapon owner is the primary designated character; the forced Slash victim is not a second Yingwu occurrence."); borrowed.Cold(); borrowed.Continue(); borrowed.Play();

        foreach (var kind in new[] { CardKind.Duel, CardKind.FireAttack, CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.UnexpectedAssault, CardKind.WumiaoFlood, CardKind.AgainstCurrent })
        {
            var damage = Scene.Start(new("direct-damage-family-" + kind) { Hand = [C(kind), C(CardKind.Dodge)], PreventDamage = true, Boat = kind == CardKind.AgainstCurrent });
            var material = damage.V().Hand.First(c => c.Kind == kind).Id; damage.Native(kind); damage.Play();
            var declared = damage.E<CardUseDeclaredEvent>().Single(e => e.CardId == material);
            damage.Need(declared.CardKind == kind && damage.AllEarned().Length == 0 && damage.Chui() == 0 && damage.E<DamageAppliedEvent>().Length == 0 &&
                damage.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == declared.ResolutionId) == 1 && damage.PaidOnce(material),
                "Every real direct-damage ordinary family, including the native Dragon Boat Against Current activity card, stays excluded even when the accepted Use causes zero actual damage."); damage.Cold();
        }
    }

    public static void ExactPlayPhasesSourceLifetimeAndPermanentChuiCold()
    {
        var early = Scene.Start(new("earlier-source-loss-priority") { Hand = [C(CardKind.Slash)], ConditionalSources = true, LoseAfterDesignation = true, LossPriority = 1000 });
        early.Native(CardKind.Slash, [1]); early.Reach(early.Held);
        early.Need(early.V().Hp == 2 && !early.Has(Lueying) && !early.Has(Yingwu) && early.Chui() == 0 && early.AllEarned().Length == 0,
            "The ordinary initial native priority is retained: an earlier real source loss prevents the unpaid accrual rather than moving every skill to an artificial final queue tail."); early.Cold(); early.Continue(); early.Play();

        var s = Scene.Start(new("permanent-exact-phase-source-regrant") { ConditionalSources = true, LoseAfterDesignation = true, InsertPlay = true, ExtraTurn = true, IncomingBorrowed = true,
            Equipment = [C(CardKind.Crossbow)], Hand = [C(CardKind.Slash), C(CardKind.Slash), C(CardKind.Slash), C(CardKind.Slash), C(CardKind.Slash), C(CardKind.DrawTwo), C(CardKind.IronChain)], LongHand = true });
        var originalTurn = s.G.State.TurnNumber; s.PublicBudgets(0, 0);
        foreach (var (kind, target, count) in new[] { (CardKind.Slash, 1, 1), (CardKind.DrawTwo, 0, 2), (CardKind.Slash, 1, 3), (CardKind.IronChain, 0, 4) })
        {
            if (s.V().Hp < 3) { s.Use("recover-one"); s.Play(); }
            s.PublicBudgets(s.AllEarned().Count(e => e.Source.SkillId == Lueying), s.AllEarned().Count(e => e.Source.SkillId == Yingwu));
            s.Native(kind, [target]); s.Reach(s.Held);
            s.Need(s.Chui() == count && !s.Has(Lueying) && !s.Has(Yingwu) && s.V().Hp == 2 && s.AllEarned().Length == count,
                "Each true target earns before a later native HP-loss child removes both conditional sources; those real gains persist while completion has no live unpaid source.");
            s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); s.Play();
        }
        var old = s.AllEarned(); var phase = old[0].Phase;
        s.Need(old.Length == 4 && old.All(e => e.Phase == phase) && old.Count(e => e.Source.SkillId == Lueying) == 2 && old.Count(e => e.Source.SkillId == Yingwu) == 2 &&
            s.E<MouLiuChengCompletionStartedEvent>().Length == 0 && Enumerable.Range(0, 4).All(viewer => s.Chui(viewer) == 4),
            "Four actual shared permanent marks survive two independent exact-Play caps and four real source losses/regrants, without injecting marks or bypassing an accepted completion debit."); s.Cold();
        s.End(); s.Play();
        s.Need(s.G.State.TurnNumber == originalTurn && s.Chui() == 4 && !s.Has(Lueying), "Returning from the real inserted Play into the normal Play of the same actual turn does not clear permanent Chui.");
        s.Use("recover-one"); s.Play(); s.PublicBudgets(0, 0); s.Native(CardKind.Slash, [1]); s.Reach(s.Held);
        var normal = s.AllEarned().Last();
        s.Need(normal.Phase != phase && normal.PhaseUsed == 1 && normal.Source.SkillId == Lueying && s.Chui() == 5 && s.AllEarned().Length == 5,
            "The normal Play has a distinct actual phase identity and a fresh per-skill allowance while the old inserted-phase history remains spent."); s.Cold(); s.Continue(); s.Play();
        s.End(); s.Play(); s.Need(s.G.State.TurnNumber > originalTurn && s.E<TurnStartedEvent>().Last().ActorSeat == 0 && s.Chui() == 5,
            "A genuine extra own actual turn preserves the shared balance and creates another real Play rather than resetting marks.");
        s.Use("recover-one"); s.Play(); s.Native(CardKind.Slash, [1]); s.Reach(s.Held); var extra = s.AllEarned().Last();
        s.Need(extra.Phase != normal.Phase && extra.PhaseUsed == 1 && s.Chui() == 6 && s.AllEarned().GroupBy(e => (e.Source.SkillId, e.Phase)).All(g => g.Count() <= 2),
            "A new extra-turn Play earns independently at one, so permanent Chui can exceed an invented cap while each historical phase retains its own maximum two."); s.Cold(); s.Continue(); s.Play();
        s.Use("recover-one"); s.Play(); s.PublicBudgets(1, 0); s.Cold(); s.PublicBudgets(1, 0); var forcedMaterial = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id; s.End();
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash") == true);
        var mother = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.BorrowedSword);
        s.Private(); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([forcedMaterial]) && c.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash"); s.Reach(s.Held);
        var foreign = s.AllEarned().Last();
        s.Need(s.G.State.CurrentSeat == 2 && foreign.Source.SkillId == Lueying && foreign.Phase.ActorSeat == 2 && foreign.Phase.ActualTurnOwnerSeat == 2 && foreign.PhaseUsed == 1 &&
            foreign.Phase != extra.Phase && s.Chui() == 7 && !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == mother.Id),
            "The weapon owner's genuine forced Slash Use can accrue in another actor's actual Play; its allowance belongs to that exact foreign phase, not the owner's previous or future Play.");
        s.Cold(); s.Continue(); s.Reach(() => s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == mother.Id));
        s.Need(s.PaidOnce(forcedMaterial) && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == mother.Id) == 1 && s.Chui() == 7 &&
            s.AllEarned().GroupBy(e => (e.Source.SkillId, e.Phase)).All(g => g.Count() <= 2),
            "The foreign native parent/child return and material payment each finish once without losing permanent marks or changing an earlier phase's consumed allowance."); s.Cold();
    }

    private sealed record Spec(string Name)
    {
        public ShanheStartingCard[] Hand { get; init; } = [C(CardKind.Slash)];
        public ShanheStartingCard[] Equipment { get; init; } = [];
        public bool LateAdd { get; init; }
        public bool YingwuOnly { get; init; }
        public bool RedirectPeer { get; init; }
        public bool CancelTargets { get; init; }
        public bool PreventDamage { get; init; }
        public bool ConditionalSources { get; init; }
        public bool LoseAfterDesignation { get; init; }
        public int LossPriority { get; init; } = -1000;
        public bool InsertPlay { get; init; }
        public bool ExtraTurn { get; init; }
        public bool IncomingBorrowed { get; init; }
        public bool LongHand { get; init; }
        public bool Boat { get; init; }
        public bool HoldBeforeAccrual { get; init; }
    }

    private sealed class Fixture(Spec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-liu-cheng-accrual", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            void Real(string bundle, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing complete real bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var catalog = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in ids) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            }
            Real("mou-liu-cheng-mobile", Lueying, Yingwu); if (spec.Boat) Real("dragonboat-activity", "dragonboat:aiye", "dragonboat:niepan");
            foreach (var kind in spec.Hand.Concat(spec.Equipment).Select(c => c.Kind).Concat(new[] { CardKind.Dodge, CardKind.Slash, CardKind.StoneAxe, CardKind.Indulgence, CardKind.BorrowedSword }).Distinct())
            { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:mlca-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int targets = 0) => new() { ["id"] = id, ["minCards"] = 0, ["maxCards"] = 0, ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            const string pause = "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held\",\"options\":[{\"id\":\"continue\"}]}";
            var driver = new JsonObject { ["id"] = Driver, ["revision"] = 1, ["modifiers"] = N("[{\"id\":\"range\",\"query\":\"attackRange\",\"operation\":\"add\",\"value\":3,\"priority\":0},{\"id\":\"quota\",\"query\":\"slashLimit\",\"operation\":\"add\",\"value\":8,\"priority\":0}" + (spec.LongHand ? ",{\"id\":\"keep-native-materials\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":20,\"priority\":0}" : "") + "]"),
                ["activations"] = new JsonArray(A("recover-one", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"), A("equip-peer", "{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"real-weapon\"}", 1),
                    A("adjust-targets", "{\"op\":\"grantNextCardTargetAdjustment\",\"target\":\"owner\"}")) };
            var watches = new JsonArray(N("{\"id\":\"before-real-effects\",\"window\":\"cardUseBeforeTargetEffects\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\",\"drawTwo\",\"ironChain\",\"borrowedSword\"],\"optional\":false,\"effects\":[" + pause + "]}"));
            if (spec.HoldBeforeAccrual) watches.Add(N("{\"id\":\"frozen-before-accrual\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"priority\":1000,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"frozen\",\"options\":[{\"id\":\"accrue\"}]}]}"));
            if (spec.LoseAfterDesignation) watches.Add(N("{\"id\":\"real-late-source-loss\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\",\"drawTwo\",\"ironChain\"],\"priority\":" + spec.LossPriority + ",\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}"));
            if (spec.ExtraTurn) watches.Add(N("{\"id\":\"one-extra-native-turn\",\"window\":\"turnEnding\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"pendExtraTurn\",\"target\":\"owner\"}]}"));
            var defs = new JsonArray(driver, new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches },
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            if (spec.PreventDamage) defs.Add(N("{\"id\":\"" + Prevent + "\",\"revision\":1,\"triggers\":[{\"id\":\"prevent-real-damage\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"optional\":false,\"effects\":[{\"op\":\"preventCurrentDamage\",\"target\":\"owner\"}]}]}"));
            if (spec.ConditionalSources) defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-lueying\",\"skillId\":\"" + Lueying + "\",\"minimumHp\":3},{\"id\":\"healthy-yingwu\",\"skillId\":\"" + Yingwu + "\",\"minimumHp\":3}]}"));
            if (spec.LateAdd) defs.Add(N("{\"id\":\"" + Add + "\",\"revision\":1,\"triggers\":[{\"id\":\"late-real-target\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"ironChain\"],\"priority\":-1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + (spec.YingwuOnly ? "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + Lueying + "\"]}," : "") + "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"addCurrentCardUseTarget\",\"target\":\"selectedTarget\"}]}]}"));
            if (spec.RedirectPeer) defs.Add(N("{\"id\":\"" + Redirect + "\",\"revision\":1,\"triggers\":[{\"id\":\"real-native-redirect\",\"window\":\"slashTargetRedirecting\",\"ownerRelation\":\"target\",\"cardKinds\":[\"slash\"],\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"slashRedirectable\"},{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"hand\",\"equipment\"],\"count\":1,\"destination\":\"discardPile\",\"awaitMovementTriggers\":true},{\"op\":\"redirectCurrentAttack\",\"target\":\"selectedTarget\"}]}]}"));
            if (spec.CancelTargets) defs.Add(N("{\"id\":\"" + Cancel + "\",\"revision\":1,\"triggers\":[{\"id\":\"actual-target-cancel\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"singleActionInstance\":true,\"priority\":-1000,\"optional\":false,\"effects\":[{\"op\":\"cancelOtherCurrentUseTargetsIfOverHandLimit\",\"target\":\"owner\"}]}]}"));
            if (spec.InsertPlay) defs.Add(N("{\"id\":\"" + Insert + "\",\"revision\":1,\"triggers\":[{\"id\":\"real-inserted-play\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"insertPhase\",\"target\":\"owner\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "谋刘赪真实指定目标与阶段夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray())
                    .Select(o => o!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => id) });
            var local = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in local.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = local.Presentations[id] });
            var own = new List<string> { Driver, Watch }; if (spec.ConditionalSources) own.Add(Conditional); else { own.Add(Yingwu); if (!spec.YingwuOnly) own.Add(Lueying); }
            if (spec.LateAdd) own.Add(Add); if (spec.CancelTargets) own.Add(Cancel); if (spec.InsertPlay) own.Add(Insert); if (spec.PreventDamage) own.Add(Prevent);
            if (spec.Boat)
            {
                var pools = new Dictionary<string, IReadOnlyList<string>>();
                foreach (var faction in new[] { "wei", "shu", "wu", "qun" })
                {
                    var ids = Enumerable.Range(0, 12).Select(i => "fixture:mlca-boat-" + faction + "-" + i).ToArray(); pools["team:" + faction] = ids;
                    foreach (var id in ids) b.AddGeneral(new(id, id, "", "standard:none", faction, 4, faction == "wei" ? own : [Quiet, Prevent]));
                }
                foreach (var id in new[] { "su-fei", "huang-quan", "tang-zi", "liu-qi" }) b.AddGeneral(new("dragonboat:" + id, id, "", "standard:none", "wei", 4, [Quiet, Prevent]));
                pools["team:wei"] = pools["team:wei"].Concat(["dragonboat:huang-quan", "dragonboat:tang-zi"]).ToArray(); pools["team:shu"] = pools["team:shu"].Concat(["dragonboat:huang-quan", "dragonboat:liu-qi"]).ToArray();
                pools["team:wu"] = pools["team:wu"].Concat(["dragonboat:su-fei", "dragonboat:tang-zi"]).ToArray(); pools["team:qun"] = pools["team:qun"].Concat(["dragonboat:su-fei", "dragonboat:liu-qi"]).ToArray();
                b.AddDeck(new(Deck, "固定六十四张活动伤害与中性实体", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("fixture:mlca-card-" + (i < 32 ? CardKind.AgainstCurrent : CardKind.Dodge), Suit.Spade, 7)).ToArray() });
                b.AddMode(new(BoatMode, BoatMode, 8, 8, new Dictionary<string, int>(), Deck, 6, pools.Values.SelectMany(p => p).Distinct().ToArray(), ContentModeKind.Team, pools.Keys.ToDictionary(k => k, _ => 2)) { DragonBoatActivity = new(pools, PlayerTier: 1) });
                return;
            }
            b.AddGeneral(new(Owner, "真实谋刘赪", "", "standard:none", "qun", 3, own, GeneralGender.Female));
            b.AddGeneral(new(Ally, "真实近邻目标", "", "standard:none", "wei", 12, new[] { Quiet }.Concat(spec.RedirectPeer ? [Redirect] : Array.Empty<string>()).Concat(spec.PreventDamage ? [Prevent] : Array.Empty<string>()).ToArray()));
            b.AddGeneral(new(EnemyA, "真实其他使用者", "", "standard:none", "wei", 12, (spec.IncomingBorrowed ? Array.Empty<string>() : new[] { Quiet }).Concat(spec.PreventDamage ? [Prevent] : Array.Empty<string>()).ToArray()));
            b.AddGeneral(new(EnemyB, "真实远端目标", "", "standard:none", "wei", 12, spec.PreventDamage ? [Quiet, Prevent] : [Quiet]));
            b.AddDeck(new(Deck, "固定四十八张真实实体", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("fixture:mlca-card-" + (i < 4 ? CardKind.StoneAxe : CardKind.Dodge), Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋刘赪指定目标小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(Spec spec, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public int Seats => G.State.Players.Count;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public MouLiuChengChuiEarnedEvent[] AllEarned() => E<MouLiuChengChuiEarnedEvent>().Where(e => e.Source.OwnerSeat == 0).ToArray();
        public MouLiuChengChuiEarnedEvent[] Earned(long use) => AllEarned().Where(e => e.UseId == use).ToArray();
        public bool Has(string id) => V().Skills?.Any(s => s.ContentId == id) == true;
        public int Chui(int viewer = 0) => G.CreateSnapshot(viewer).Players[0].Markers?.Where(m => m.Kind == PlayerMarkerKind.MouLiuChengChui).Sum(m => m.Count) ?? 0;
        public void PublicBudgets(int lueying, int yingwu)
        {
            foreach (var viewer in Enumerable.Range(0, Seats))
                foreach (var (skill, used) in new[] { (Lueying, lueying), (Yingwu, yingwu) })
                {
                    var states = G.CreateSnapshot(viewer).Players[0].SkillRuntimeStates?.Single(s => s.SkillId == skill).PublicRuleStates ?? [];
                    Need(states.Count(s => s.StateId == "after-designation" && s.Kind == ProgramPublicRuleStateKind.ActivationLimit &&
                        s.Scope == SkillUsageScope.Phase && s.Value == 2 && s.Used == used) == 1,
                        "Every viewer sees the actual live named-skill allowance two and used " + used + " for " + skill + "; frozen qualification alone cannot spend it.");
                    Immutable(states);
                }
        }
        public bool Held() => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public bool BeforeAccrual() => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "accrue");
        public CardUseFrame UseFor(int material) => G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == material);
        public bool PaidOnce(int id) => G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 && G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1;
        public void AssertClaims(CardUseFrame use)
        {
            Need(use.MouLiuChengAccrualClaims is not null && use.MouLiuChengAnnouncedTargets is not null, "Actual notification claims live only on the native owning Use."); Immutable(use.MouLiuChengAccrualClaims!); Immutable(use.MouLiuChengAnnouncedTargets!);
            foreach (var claim in use.MouLiuChengAccrualClaims!) { Immutable(claim.Targets); Need(claim.UseId == use.Id && claim.ActionId == use.Action!.ActionId && claim.ActorSeat == 0, "Frozen claims match the exact real native action and actor."); }
        }
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(CardKind kind, int[]? targets = null)
        {
            var actions = G.GetHumanLegalActions().Where(a => a.CardId is { } id && V().Hand.Any(c => c.Id == id && c.Kind == kind) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast &&
                (targets is null || a.TargetSeats.SequenceEqual(targets) || kind == CardKind.DrawTwo && targets.SequenceEqual([0]) && a.TargetSeats.Count == 0)).ToArray();
            var action = actions.FirstOrDefault(a => a.TargetSeats.SequenceEqual([1])) ?? actions.FirstOrDefault(); Need(action is not null, "A true native card and ordered target plan must be published: " + kind);
            Accept(new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "Choose an actual published human option."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 220; step++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "The small native scene ended before boundary " + line);
                var p = P;
                if (p is null) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (Held()) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-liu-cheng-use" && c.Parameters.GetValueOrDefault("branch") == "decline")) Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die" or "decline")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die" or "decline");
                else if (p.Choices.Count > 0) Answer(_ => true);
                else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Private()
        {
            Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, Seats - 1).All(i => G.CreateSnapshot(i).PendingDecision is null) && Enumerable.Range(0, Seats).All(i => G.CreateSnapshot(i).Players.Where(p => p.Seat != i).All(p => p.Hand.Count == 0)),
                "Only the real chooser sees pending options and each other player's Hand remains opaque."); Immutable(P!.Choices); foreach (var c in P.Choices) { Immutable(c.Cards); Immutable(c.Targets); }
        }
        private void Immutable<T>(IReadOnlyList<T> list) { if (list is IList mutable && mutable.Count > 0) { var rejected = false; try { mutable[0] = mutable[0]; } catch (NotSupportedException) { rejected = true; } Need(rejected, "Committed target and choice lists reject observer mutation."); } }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, Seats).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves actual target notifications, source instances, exact phase history, permanent marks, all private views and paid native parents."); }
        public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && before == State(), "Wrong-seat, stale and forged input cannot change actual targets, marks, material movements or accepted commands."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-accrual-choice"), G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision - 1)); }
        public void Accept(GameCommand command) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P, Players = Enumerable.Range(0, Seats).Select(i => new { i, V(i).Hp, V(i).HandCount, V(i).Markers, V(i).Skills }),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Earned = AllEarned(), Notifications = E<MouLiuChengTargetAnnouncedEvent>(), Payments = E<MouLiuChengCompletionStartedEvent>(),
            Movements = G.CardMovements.TakeLast(8), Finished = E<CardUseFinishedEvent>().TakeLast(5), Damage = E<DamageAppliedEvent>().TakeLast(4) });
        public static Scene Start(Spec spec)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec)); GameEngine game;
            if (spec.Boat)
                game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 8, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:wei", ModeId = BoatMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
            else
            {
                var setup = new ShanheBattleSetup(83, 1, "mlca-" + spec.Name, Owner, 3, 0, 0, 0, 0, [], spec.Hand, spec.Equipment,
                    [new(EnemyA) { StartingHand = spec.IncomingBorrowed ? [C(CardKind.BorrowedSword)] : [C(CardKind.Indulgence)] }, new(EnemyB) { StartingHand = [C(CardKind.Indulgence)] }], [new(Ally) { StartingHand = [C(CardKind.Indulgence)] }], new string('d', 64));
                game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                    UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
            }
            var scene = new Scene(spec, game, registry); scene.Accept(new StartGameCommand());
            if (spec.Boat) { scene.Reach(() => scene.P?.Kind == DecisionKind.SelectGeneral); var id = scene.P!.Choices.First(c => c.ContentIds.Single().StartsWith("fixture:mlca-boat-wei-", StringComparison.Ordinal)).ContentIds.Single(); scene.Accept(new SelectGeneralCommand(0, id, scene.G.Revision, scene.P.PromptId)); }
            scene.Play(); return scene;
        }
    }
}
