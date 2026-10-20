using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouHuaXiongMobileChecks
{
    private const string Yaowu = "mou-mobile:yaowu", Yangwei = "mou-mobile:yangwei", Driver = "fixture:mhxm-driver",
        Watch = "fixture:mhxm-watch", Conditional = "fixture:mhxm-conditional", Quiet = "fixture:mhxm-quiet", Skip = "fixture:mhxm-skip",
        Owner = "fixture:mhxm-owner", Ally = "fixture:mhxm-ally", EnemyA = "fixture:mhxm-enemy-a", EnemyB = "fixture:mhxm-enemy-b", Deck = "fixture:mhxm-deck";
    private const string YaowuDraw = "skill-program.mou-mobile:yaowu.Draw", YangweiDraw = "skill-program.mou-mobile:yangwei.Draw";
    private static ShanheStartingCard C(CardKind kind, Suit suit = Suit.Spade, int rank = 7) => new(kind, suit, rank);

    public static void YaowuNativeSlashColorsBeforeArmorAndHpCold()
    {
        foreach (var example in new[]
        {
            (Name: "red-slash-recover", Kind: CardKind.Slash, Suit: Suit.Heart, Red: true, Recover: true, Neutral: false, Rewrite: false, Alcohol: false),
            (Name: "black-fire-slash", Kind: CardKind.FireSlash, Suit: Suit.Spade, Red: false, Recover: false, Neutral: false, Rewrite: false, Alcohol: false),
            (Name: "red-thunder-slash-draw", Kind: CardKind.ThunderSlash, Suit: Suit.Diamond, Red: true, Recover: false, Neutral: false, Rewrite: false, Alcohol: false),
            (Name: "neutral-native-slash", Kind: CardKind.Slash, Suit: Suit.Spade, Red: false, Recover: false, Neutral: true, Rewrite: false, Alcohol: false),
            (Name: "effective-hongyan-red", Kind: CardKind.Slash, Suit: Suit.Spade, Red: true, Recover: true, Neutral: false, Rewrite: true, Alcohol: false),
            (Name: "wine-slash-one-benefit", Kind: CardKind.Slash, Suit: Suit.Heart, Red: true, Recover: true, Neutral: false, Rewrite: false, Alcohol: true)
        })
        {
            var s = Scene.Start(new(example.Name) { HeroSeat = 1, Hand = [C(example.Kind, example.Suit), C(CardKind.Alcohol)], Hongyan = example.Rewrite,
                LeanRegistry = example.Name == "red-slash-recover", NeutralSlash = example.Neutral });
            if (example.Recover) { s.Use("loss-one"); s.Play(); }
            if (!example.Neutral) { s.Use("armor-one", [1]); s.Play(); }
            if (example.Alcohol) { s.Native(CardKind.Alcohol, []); s.Play(); }
            var sourceHand = s.V().HandCount; var victimHand = s.V(1).HandCount; var hp = s.V().Hp;
            var id = s.V().Hand.First(c => c.Kind == example.Kind).Id;
            if (example.Neutral)
            {
                s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
                s.Private(); s.Answer(c => c.Targets.SequenceEqual([1]));
            }
            else s.Native(example.Kind, [1], id);
            s.Reach(() => s.Benefit() || s.Hold("before-payment"));
            var started = s.E<MouHuaXiongYaowuStartedEvent>().Single();
            var nativeUse = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == started.AttackId);
            s.Need(started is { OwnerSeat: 1, SourceSeat: 0 } && started.EffectiveKind == example.Kind && started.IsRed == example.Red &&
                s.V(1) is { Hp: 4, Armor: 1 } && s.E<DamageAppliedEvent>().Length == 0,
                "Yaowu classifies the actual Slash-family effective color before the original damage pays Armor or HP.");
            if (example.Red)
            {
                s.Need(s.Benefit() && s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "recover") == example.Recover,
                    "Only the real red Slash source privately chooses; actual recovery is offered only while wounded.");
                s.Private(); s.RejectPrompt(); s.Cold(); s.ChooseBenefit(example.Recover ? "recover" : "draw"); s.Reach(() => s.Hold("before-payment"));
            }
            var benefit = s.E<MouHuaXiongYaowuBenefitChosenEvent>().Single(); var recipient = example.Red ? 0 : 1;
            s.Need(benefit.RecipientSeat == recipient && benefit.Benefit == (example.Recover ? "source-recover" : example.Red ? "source-draw" : "owner-draw") && s.V().Hp == hp + (example.Recover ? 1 : 0) &&
                s.V().HandCount == sourceHand - (example.Neutral ? 0 : 1) + (example.Red && !example.Recover ? 1 : 0) && s.V(1).HandCount == victimHand + (example.Red ? 0 : 1) &&
                s.V(1) is { Hp: 4, Armor: 1 } && s.E<DamageAppliedEvent>().Length == 0 && s.ResolvedYaowu() == 1 && s.CompletedYaowuBinding(started.WindowId) == 1 &&
                s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(recipient) && m.Reason.Value == YaowuDraw) == (example.Recover ? 0 : 1),
                "The selected real recovery or one real Draw fully returns before Armor payment; an alcohol-enhanced damage still grants only one benefit.");
            s.Private(); s.Cold(); s.Continue(); s.Play();
            var damage = s.E<DamageAppliedEvent>().Single();
            s.Need(damage is { SourceSeat: 0, TargetSeat: 1, ArmorPayment.ArmorAbsorbed: 1 } &&
                damage.ArmorPayment.HpLossAmount == (example.Alcohol ? 1 : 0) && s.V(1).Hp == (example.Alcohol ? 3 : 4) && s.V(1).Armor == 0 &&
                s.E<MouHuaXiongYaowuStartedEvent>().Length == 1 && s.ResolvedYaowu() == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == started.AttackId) == 1 &&
                (example.Neutral ? nativeUse.Action is { PhysicalCards.Count: 0, EffectiveSuit: Suit.None } : s.PaidOnce(id)),
                "The same native Slash finishes once with its real material payment and original one/two damage; Armor is paid after Yaowu and remains separate from equipment."); s.Cold();
        }
        var excluded = Scene.Start(new("non-slash-and-loss") { HeroSeat = 1, Hand = [C(CardKind.Duel)] });
        excluded.Native(CardKind.Duel, [1]); excluded.Reach(() => excluded.Hold("before-payment"));
        excluded.Need(excluded.E<MouHuaXiongYaowuStartedEvent>().Length == 0 && excluded.V(1).Hp == 4, "A genuine Duel damage reaches its original damage window without becoming Slash damage.");
        excluded.Continue(); excluded.Play(); excluded.Use("loss-target-one", [1]); excluded.Play();
        excluded.Need(excluded.V(1).Hp == 2 && excluded.E<DamageAppliedEvent>().Length == 1 && excluded.E<MouHuaXiongYaowuStartedEvent>().Length == 0,
            "Direct HP Loss bypasses Yaowu as well; neither a real Duel nor Loss fabricates a Slash benefit."); excluded.Cold();
    }

    public static void YaowuPaidBenefitChildrenSourceLossAndNativeReturnCold()
    {
        foreach (var child in new[] { "recover", "lose-victim-source", "dying-source" })
        {
            var s = Scene.Start(new("benefit-child-" + child) { HeroSeat = 1, Hand = [C(CardKind.Slash, Suit.Heart), C(CardKind.Peach)],
                BenefitChild = child, ConditionalYaowu = child == "lose-victim-source" });
            s.Use(child == "dying-source" ? "loss-five" : "loss-one"); s.Play();
            var slash = s.V().Hand.Single(c => c.Kind == CardKind.Slash).Id; var peach = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id;
            s.Native(CardKind.Slash, [1], slash); s.Reach(s.Benefit); s.ChooseBenefit(child == "recover" ? "recover" : "draw");
            s.Reach(() => s.Hold("benefit-child"));
            var original = s.E<MouHuaXiongYaowuStartedEvent>().Single(); var chosen = s.E<MouHuaXiongYaowuBenefitChosenEvent>().Single();
            s.Need(s.V(1).Hp == 4 && s.E<DamageAppliedEvent>().Length == 0 && chosen.RecipientSeat == 0 &&
                s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == original.FrameId && f.MouHuaXiongYaowu is not null) &&
                (child == "recover" ? s.E<RecoveryAppliedEvent>().Single() is { TargetSeat: 0, Amount: 1 } : s.Draws(YaowuDraw).Length == 1),
                "A genuine recovery/Draw child retains the exact unpaid original damage and its already issued one-benefit receipt."); s.Private(); s.RejectPrompt(); s.Cold(); s.Continue();
            if (child == "lose-victim-source")
            {
                s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true); s.Answer(c => c.Targets.SequenceEqual([1]));
            }
            if (child == "dying-source")
            {
                s.Reach(() => s.P is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 });
                s.Need(s.V().Hp == 0 && s.E<PlayerDyingEvent>().Single().VictimSeat == 0 && s.E<DamageAppliedEvent>().Length == 0,
                    "The real reward child can make the still-original Slash source dying before the victim has paid damage.");
                s.Private(); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([peach]) && c.Parameters.GetValueOrDefault("response") == "peach");
            }
            s.Reach(() => s.Hold("before-payment"));
            s.Need(s.ResolvedYaowu() == 1 && s.CompletedYaowuBinding(original.WindowId) == 1 && s.E<DamageAppliedEvent>().Length == 0 &&
                (child != "lose-victim-source" || !s.Has(1, Yaowu) && s.V(1).Hp == 3) &&
                (child != "dying-source" || s.V().Hp == 1 && s.PaidOnce(peach) && s.E<DyingResolvedEvent>().Single().Survived),
                "Losing the victim's original skill instance or rescuing the source cannot duplicate the already committed recovery/Draw or consume the suspended damage.");
            s.Cold(); s.Continue(); s.Play();
            s.Need(s.PaidOnce(slash) && s.E<DamageAppliedEvent>().Single().TargetSeat == 1 && s.V(1).Hp == (child == "lose-victim-source" ? 2 : 3) &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == original.AttackId) == 1 && s.E<MouHuaXiongYaowuBenefitChosenEvent>().Length == 1 &&
                !s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == original.FrameId),
                "The exact original paid Slash resumes after all real benefit, source-loss and rescue children and finishes once."); s.Cold();
        }
    }

    public static void YangweiActualDrawPaidSourceLossSlashPrivilegesCold()
    {
        var s = Scene.Start(new("paid-wei-source-loss") { YangweiChildLoss = true, BlockEmptyTarget = true,
            Hand = [C(CardKind.Slash), C(CardKind.FireSlash, Suit.Heart), C(CardKind.ThunderSlash), C(CardKind.Peach)] });
        s.Use("equip-peer", [2]); s.Play();
        s.Need(s.V(2).Equipment.Any(c => c.Kind == CardKind.RenwangShield) && s.G.GetCombatDistance(0, 2) > s.G.GetAttackRange(0) &&
            !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Contains(2)), "The real remote target wears Renwang Shield and lies outside the unchanged printed attack range before Wei.");
        var range = s.G.GetAttackRange(0); var before = s.V().HandCount; s.Yangwei(); s.Reach(() => s.Hold("yangwei-child"));
        var reserved = s.E<MouHuaXiongYangweiReservedEvent>().Single();
        s.Need(s.Draws(YangweiDraw).Length == 2 && s.V().HandCount == before + 2 && s.V().Hp == 4 &&
            s.E<MouHuaXiongWeiGrantedEvent>().Length == 0 && s.Wei(0) == 0 &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == reserved.FrameId).MouHuaXiongYangwei is { WeiResolved: false },
            "Yangwei reserves once then actually draws two; its gain child sees the paid receipt before any Wei mark or Slash privilege is granted.");
        s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); s.Play();
        s.Need(s.V().Hp == 3 && !s.Has(0, Yangwei) && s.E<MouHuaXiongWeiGrantedEvent>().Single() is { ExpiredBeforeGrant: false } &&
            Enumerable.Range(0, 4).All(viewer => s.Wei(viewer) == 1) && s.G.GetAttackRange(0) == range && !s.CanYangwei() &&
            s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])) &&
            !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Contains(3)),
            "After the real Draw child removes the skill source, the accepted skill still grants one public Wei: distance is ignored, attack range is unchanged, and empty-hand target prohibitions remain.");
        var first = s.V().Hand.Single(c => c.Kind == CardKind.Slash).Id; s.Native(CardKind.Slash, [2], first); s.Play();
        s.Need(s.V(2).Hp == 11 && s.V(2).Equipment.Any(c => c.Kind == CardKind.RenwangShield), "The real black Slash ignores Renwang Shield while the paid Wei remains effective without its original Yangwei source.");
        s.Use("armor-one", [2]); s.Play(); var second = s.V().Hand.Single(c => c.Kind == CardKind.FireSlash).Id; s.Native(CardKind.FireSlash, [2], second); s.Play();
        s.Need(s.V(2) is { Hp: 11, Armor: 0 } && s.E<DamageAppliedEvent>().Last().ArmorPayment is { ArmorAbsorbed: 1, HpLossAmount: 0 } &&
            s.PaidOnce(first) && s.PaidOnce(second) && !s.Actions().Any(a => a.Kind == LegalActionKind.Slash) &&
            s.E<ProgramSkillResolvedEvent>().Count(e => e.FrameId == reserved.FrameId && e.SkillId == Yangwei && e.Completed) == 1 && s.Draws(YangweiDraw).Length == 2,
            "Wei allows exactly one extra ordinary Slash and ignores equipment rather than the independent Armor resource; its accepted Draw cost and activation settle once."); s.Cold();
        s.End(); s.Reach(() => s.G.State.Phase != TurnPhase.Play || s.G.State.CurrentSeat != 0);
        s.Need(Enumerable.Range(0, 4).All(viewer => s.Wei(viewer) == 0), "Ending the actual Play phase removes the public Wei mark for every viewer."); s.Cold();
    }

    public static void YangweiPhaseExpiryNextOwnEndingExtraTurnSkipAndRegrantCold()
    {
        var regular = Scene.Start(new("ordinary-own-ending") { EndingWitness = true, ConditionalYangwei = true }); regular.Yangwei(); regular.Play();
        var issued = regular.G.State.TurnNumber; regular.End(); regular.Reach(() => regular.Hold("ending"));
        regular.Need(regular.Wei(0) == 0 && regular.E<MouHuaXiongCooldownRecoveredEvent>().Length == 0,
            "The issuing turn's own Ending clears phase Wei but cannot recover Yangwei's next-own-turn cooldown."); regular.Cold(); regular.Continue(); regular.Play();
        regular.Need(regular.G.State.TurnNumber > issued && regular.E<TurnEndedEvent>().Any(e => e.ActorSeat != 0 && e.TurnNumber > issued) &&
            !regular.CanYangwei() && regular.E<MouHuaXiongCooldownRecoveredEvent>().Length == 0,
            "Other actual turns and their Ending phases do not recover Yangwei; it remains disabled in the next actual own Play.");
        regular.Use("loss-one"); regular.Play(); regular.Need(!regular.Has(0, Yangwei), "A real HP change removes the conditional Yangwei source.");
        regular.Use("recover-one"); regular.Play();
        regular.Need(regular.Has(0, Yangwei) && !regular.CanYangwei() && regular.E<MouHuaXiongCooldownRecoveredEvent>().Length == 0,
            "Reacquiring a new real source instance during the next fresh Play cannot reset an unpaid owner-Ending cooldown.");
        var nextOwn = regular.G.State.TurnNumber; regular.End(); regular.Reach(() => regular.Hold("ending"));
        regular.Need(regular.E<MouHuaXiongCooldownRecoveredEvent>().Single() is { OwnerSeat: 0 } recovered && recovered.IssuedActualTurn == issued && recovered.EndingActualTurn == nextOwn,
            "The exact next actual own Ending recovers cooldown once, rather than waiting for a round or another Play."); regular.Cold(); regular.Continue(); regular.Play();
        regular.Need(regular.CanYangwei(), "Yangwei is available again after its real next-own-Ending recovery."); regular.Cold();

        var extra = Scene.Start(new("extra-own-skip-regrant") { YangweiChildLoss = true, EndingWitness = true, ChainExtraTurns = true, SkipWhileWounded = true });
        extra.Yangwei(); extra.Reach(() => extra.Hold("yangwei-child")); extra.Continue(); extra.Play();
        var initial = extra.G.State.TurnNumber; extra.End(); extra.Reach(() => extra.Hold("ending"));
        extra.Need(!extra.Has(0, Yangwei) && extra.E<MouHuaXiongCooldownRecoveredEvent>().Length == 0 && extra.Wei(0) == 0,
            "The paid grant expires at phase end, while removing and later reacquiring its source cannot reset the stored cooldown.");
        extra.Cold(); extra.Continue(); extra.Reach(() => extra.Hold("ending"));
        var skipped = extra.G.State.TurnNumber;
        extra.Need(skipped > initial && extra.G.State.CurrentSeat == 0 && extra.V().Hp == 3 &&
            extra.E<TurnStartedEvent>().Where(e => e.TurnNumber > initial).All(e => e.ActorSeat == 0) &&
            extra.E<ProgramBindingResolvedEvent>().Any(e => e.SkillId == Skip && e.Completed) &&
            extra.E<MouHuaXiongCooldownRecoveredEvent>().Single().EndingActualTurn == skipped,
            "A real extra own turn counts as the next actual turn, and its real Ending recovers Yangwei even though that turn skipped Play."); extra.Cold();
        extra.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "recover"); extra.Play();
        extra.Need(extra.V().Hp == 4 && extra.Has(0, Yangwei) && extra.CanYangwei() && extra.Wei(0) == 0 &&
            extra.E<MouHuaXiongYangweiReservedEvent>().Length == 1 && extra.E<MouHuaXiongCooldownRecoveredEvent>().Length == 1 && extra.Draws(YangweiDraw).Length == 2,
            "The source is really reacquired after the proper Ending and the following extra Play can activate anew without repeating the earlier Draw or retaining old Wei."); extra.Cold();
        RejectReservedTransplant();
    }

    private static void RejectReservedTransplant()
    {
        string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.mou-hua-xiong-mobile." + suffix + ".json") ?? throw new InvalidOperationException("Missing real complete mobile Hua Xiong bundle."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = JsonNode.Parse(Read("rules"))!; var presentation = JsonNode.Parse(Read("presentation"))!;
        var foreign = "fixture:foreign-yangwei"; rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Yangwei)!["id"] = foreign;
        var labels = presentation["skills"]!.AsObject(); var label = labels[Yangwei]!.DeepClone(); labels.Remove(Yangwei); labels.Add(foreign, label);
        var rejected = false; try { SkillProgramCatalog.Load(rules.ToJsonString(), presentation.ToJsonString()); } catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("The reserved Yangwei phase/payment operation must reject a foreign skill transplant during load.");
    }

    private sealed record Spec(string Name)
    {
        public int HeroSeat { get; init; }
        public ShanheStartingCard[] Hand { get; init; } = [C(CardKind.Slash), C(CardKind.FireSlash, Suit.Heart), C(CardKind.ThunderSlash)];
        public bool Hongyan { get; init; }
        public bool LeanRegistry { get; init; }
        public bool NeutralSlash { get; init; }
        public bool ConditionalYaowu { get; init; }
        public string? BenefitChild { get; init; }
        public bool YangweiChildLoss { get; init; }
        public bool ConditionalYangwei { get; init; }
        public bool BlockEmptyTarget { get; init; }
        public bool EndingWitness { get; init; }
        public bool ChainExtraTurns { get; init; }
        public bool SkipWhileWounded { get; init; }
    }

    private sealed class Fixture(Spec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-hua-xiong-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            void Real(string bundle, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing complete native bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var catalog = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
                foreach (var id in ids) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            }
            if (spec.LeanRegistry) { b.AddSkill(new("standard:none", "无", "固定小局无技能")); Real("mou-hua-xiong-mobile", Yaowu); }
            else Real("mou-hua-xiong-mobile", Yaowu, Yangwei);
            if (spec.Hongyan || spec.BlockEmptyTarget) Real("passive-card-rules", "classic:hongyan", "classic:kongcheng");
            foreach (var kind in spec.Hand.Select(c => c.Kind).Concat(new[] { CardKind.Dodge, CardKind.RenwangShield }).Distinct())
            { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:mhxm-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            static JsonNode N(string json) => JsonNode.Parse(json)!;
            static JsonObject A(string id, string effects, int targets = 0) => new() { ["id"] = id, ["minCards"] = 0, ["maxCards"] = 0, ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            static string Pause(string option) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"witness\",\"options\":[{\"id\":\"" + option + "\"}]}";
            var acts = new JsonArray(A("loss-one", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("loss-five", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":5}"),
                A("recover-one", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"),
                A("loss-target-one", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}", 1), A("armor-one", "{\"op\":\"grantArmor\",\"target\":\"selectedTarget\",\"amount\":1}", 1),
                A("equip-peer", "{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"native-equipment\"}", 1));
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts },
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            if (spec.NeutralSlash) defs[0]!["triggers"] = N("[{\"id\":\"native-neutral-slash\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":true,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"grantArmor\",\"target\":\"selectedTarget\",\"amount\":1},{\"op\":\"useVirtualCard\",\"target\":\"selectedTarget\",\"outputKind\":\"slash\",\"targetRestriction\":\"normalSlashTarget\",\"useCardActionWindows\":true}]}]");
            var watches = new JsonArray();
            if (spec.HeroSeat == 1) watches.Add(N("{\"id\":\"after-native-benefit\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageSource\",\"priority\":-1000,\"optional\":false,\"effects\":[" + Pause("before-payment") + "]}"));
            if (spec.BenefitChild is { } child)
            {
                var recovery = child == "recover";
                var tail = child == "lose-victim-source" ? ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}" : child == "dying-source" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : "";
                watches.Add(N("{\"id\":\"real-benefit-child\",\"window\":\"" + (recovery ? "afterHpRecovered" : "cardsGained") + "\",\"subject\":\"owner\"," +
                    (recovery ? "" : "\"destinationZones\":[\"hand\"],\"movementReasons\":[\"" + YaowuDraw + "\"],\"movementOccurrence\":\"perBatch\",") +
                    "\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("benefit-child") + tail + "]}"));
            }
            if (spec.YangweiChildLoss) watches.Add(N("{\"id\":\"real-yangwei-draw-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"" + YangweiDraw + "\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("yangwei-child") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}"));
            if (spec.EndingWitness) watches.Add(N("{\"id\":\"actual-own-ending\",\"window\":\"turnEnding\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":2,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"ending-choice\",\"options\":[{\"id\":\"continue\"},{\"id\":\"recover\"}]},{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1,\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"ending-choice\",\"optionId\":\"recover\"}}" + (spec.ChainExtraTurns ? ",{\"op\":\"pendExtraTurn\",\"target\":\"owner\"}" : "") + "]}"));
            if (watches.Count > 0) defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches });
            var grants = new JsonArray();
            if (spec.ConditionalYaowu) grants.Add(N("{\"id\":\"healthy-yaowu\",\"skillId\":\"" + Yaowu + "\",\"minimumHp\":4}"));
            if (spec.YangweiChildLoss || spec.ConditionalYangwei) grants.Add(N("{\"id\":\"healthy-yangwei\",\"skillId\":\"" + Yangwei + "\",\"minimumHp\":4}"));
            if (spec.SkipWhileWounded)
            {
                defs.Add(N("{\"id\":\"" + Skip + "\",\"revision\":1,\"triggers\":[{\"id\":\"real-skipped-next-play\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
                grants.Add(N("{\"id\":\"wounded-skip\",\"skillId\":\"" + Skip + "\",\"maximumHp\":3}"));
            }
            if (grants.Count > 0) defs.Add(new JsonObject { ["id"] = Conditional, ["revision"] = 1, ["hpSkillGrants"] = grants });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "谋华雄原生小局行为夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(key => n[key]?.AsArray() ?? new JsonArray()).SelectMany(binding => binding!["effects"]!.AsArray())
                    .SelectMany(effect => effect!["options"]?.AsArray() ?? new JsonArray()).Select(option => option!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string> { Driver }; if (watches.Count > 0) own.Add(Watch); if (spec.Hongyan) own.Add("classic:hongyan");
            if (spec.HeroSeat == 0) { own.Add(Yaowu); if (spec.YangweiChildLoss || spec.ConditionalYangwei) own.Add(Conditional); else own.Add(Yangwei); }
            b.AddGeneral(new(Owner, "谋华雄小局本人", "", "standard:none", "qun", spec.HeroSeat == 0 ? 4 : 6, own));
            var ally = new List<string> { Quiet }; if (spec.HeroSeat == 1) { if (!spec.LeanRegistry) ally.Add(Yangwei); if (spec.ConditionalYaowu) ally.Add(Conditional); else ally.Add(Yaowu); }
            b.AddGeneral(new(Ally, "真实其他角色", "", "standard:none", "qun", spec.HeroSeat == 1 ? 4 : 12, ally));
            b.AddGeneral(new(EnemyA, "真实远距防具目标", "", "standard:none", "qun", 12, [Quiet]));
            b.AddGeneral(new(EnemyB, "真实禁止目标", "", "standard:none", "qun", 12, spec.BlockEmptyTarget ? new[] { Quiet, "classic:kongcheng" } : new[] { Quiet }));
            b.AddDeck(new(Deck, "固定四十八张实体", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("fixture:mhxm-card-" + (i < 3 ? CardKind.RenwangShield : CardKind.Dodge), Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动谋华雄原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB],
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
        public bool Has(int owner, string skill) => V(owner).Skills?.Any(s => s.ContentId == skill) == true;
        public bool CanYangwei() => Actions().Any(a => a.ProgramSkillId == MouHuaXiongMobileChecks.Yangwei && a.ProgramActivationId == "draw-and-wei");
        public int Wei(int viewer) => G.CreateSnapshot(viewer).Players[0].Markers?.Where(m => m.Name == "威").Sum(m => m.Count) ?? 0;
        public CardMovementRecord[] Draws(string reason) => G.CardMovements.Where(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == reason).ToArray();
        public int CompletedYaowuBinding(long frameId) => E<ProgramBindingResolvedEvent>().Count(e => e.FrameId == frameId && e.SkillId == Yaowu && e.BindingId == "slash-damage-benefit" &&
            e.OwnerSeat == spec.HeroSeat && e.Window == SkillProgramTriggerWindow.BeforeDamageApplied && e.Activated && e.Completed);
        public int ResolvedYaowu() => E<MouHuaXiongYaowuStartedEvent>().Sum(e => CompletedYaowuBinding(e.FrameId));
        public bool Benefit() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-hua-xiong-yaowu") == true;
        public bool Hold(string name) => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == (name == "ending" ? "continue" : name));
        public bool PaidOnce(int id) => G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 && G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1;
        public void Yangwei() { Need(CanYangwei(), "The genuine published Yangwei activation must be legal before payment."); Accept(new UseProgramSkillCommand(0, MouHuaXiongMobileChecks.Yangwei, "draw-and-wei", [], [], G.Revision, P!.PromptId)); }
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(CardKind kind, int[] targets, int? id = null)
        {
            var action = Actions().FirstOrDefault(a => a.CardId is { } cardId && (id is null || id == cardId) && V().Hand.Any(c => c.Id == cardId && c.Kind == kind) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast && a.TargetSeats.SequenceEqual(targets));
            Need(action is not null, "The real published native card and ordered target plan must exist: " + kind);
            Accept(new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        }
        public void ChooseBenefit(string name) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-hua-xiong-yaowu" && c.Parameters.GetValueOrDefault("option-id") == name);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") is "continue" or "before-payment" or "benefit-child" or "yangwei-child");
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && choice is not null, "Choose an actually published private owner choice."); Accept(new AnswerPromptCommand(0, p.PromptId, choice!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "The bounded fixture ended before boundary " + line);
                var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (Benefit()) ChooseBenefit("draw");
                else if (p.SkillPrompt?.SkillId == Watch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected bounded native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Private()
        {
            Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(viewer => G.CreateSnapshot(viewer).PendingDecision is null) &&
                Enumerable.Range(0, 4).All(viewer => G.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0)), "Only the actual chooser sees private options, with all other Hands opaque.");
            foreach (var choice in P!.Choices) { Immutable(choice.Cards); Immutable(choice.Targets); } Immutable(P.Choices);
        }
        private void Immutable<T>(IReadOnlyList<T> list) { if (list is IList mutable && mutable.Count > 0) { var before = JsonSerializer.Serialize(list); var rejected = false; try { mutable[0] = mutable[0]; } catch (NotSupportedException) { rejected = true; } Need(rejected && before == JsonSerializer.Serialize(list), "Committed private nested lists reject observer mutation."); } }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(viewer => SnapshotJson.Serialize(G.CreateSnapshot(viewer))), Frames = G.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves all four private views, actual owning frames, pending benefits, phase grants, cooldown, payments and accepted commands."); }
        public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && before == State(), "Forged, stale and foreign input cannot mutate real payment, marks, damage or cooldown."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-mou-hua-xiong"), G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision - 1)); }
        public void Accept(GameCommand command) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).MaxHp, V(i).Armor, V(i).HandCount, V(i).Skills, V(i).Markers }),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Movements = G.CardMovements.TakeLast(8),
            Rules = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouHuaXiong", StringComparison.Ordinal)).TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            Bindings = E<ProgramBindingResolvedEvent>().Where(e => e.SkillId == Yaowu).TakeLast(4), Damage = E<DamageAppliedEvent>().TakeLast(3), Finished = E<CardUseFinishedEvent>().TakeLast(3) });
        public static Scene Start(Spec spec)
        {
            ContentRegistry registry; try { registry = spec.LeanRegistry ? ContentRegistry.Build(new Fixture(spec)) : ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec)); } catch (Exception e) { throw new InvalidOperationException(spec.Name + " fixture: " + e.Message, e); }
            var hp = spec.HeroSeat == 0 ? 4 : 6;
            var setup = new ShanheBattleSetup(83, 1, "mhxm-" + spec.Name, Owner, hp, 0, 0, 0, 0, [], spec.Hand, [],
                [new(EnemyA) { StartingHand = [] }, new(EnemyB) { StartingHand = [] }], [new(Ally) { StartingHand = [] }], new string('a', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var scene = new Scene(spec, game, registry); scene.Accept(new StartGameCommand());
            if (spec.NeutralSlash) scene.Reach(() => scene.P?.SkillPrompt?.SkillId == Driver && scene.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            else scene.Play();
            return scene;
        }
    }
}
