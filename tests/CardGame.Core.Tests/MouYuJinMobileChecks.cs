using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouYuJinMobileChecks
{
    private const string Xiayuan = "mou-mobile:xiayuan", Driver = "fixture:myjm-driver", Watch = "fixture:myjm-watch",
        Conditional = "fixture:myjm-conditional", Quiet = "fixture:myjm-quiet", Owner = "fixture:myjm-owner", AllyA = "fixture:myjm-ally-a",
        AllyB = "fixture:myjm-ally-b", Enemy = "fixture:myjm-enemy", Deck = "fixture:myjm-deck";
    private const string CostReason = "skill-program.mou-mobile:xiayuan.MoveBoundCards";
    private static ShanheStartingCard C(CardKind kind, Suit suit = Suit.Spade, int rank = 7) => new(kind, suit, rank);

    public static void XiayuanExactDamageInvoiceArmorOnlyAndUnboundedHistoricalRestoreCold()
    {
        var native = Scene.Start(new("native-slash-armor-only") { Equipment = [C(CardKind.StoneAxe)] });
        native.Use("armor-one", [2]); native.Play(); var slash = native.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
        native.NativeSlash(2, slash); native.Reach(native.Offer);
        var applied = native.E<DamageAppliedEvent>().Single(); var payment = applied.ArmorPayment!;
        native.Need(applied is { Amount: 1, TargetSeat: 2, ArmorPayment.ArmorBefore: 1, ArmorPayment.ArmorAbsorbed: 1, ArmorPayment.ArmorAfter: 0, ArmorPayment.HpLossAmount: 0 } &&
            native.V(2).Hp == 12 && native.E<MouYuJinXiayuanIssuedEvent>().Length == 0 && !native.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == payment.AttackFrameId),
            "One real Slash damage exhausting resource Armor qualifies before any Xiayuan acceptance even when actual HP loss is zero; its native mother remains unfinished.");
        native.Private(); native.RejectPrompt(); native.Cold(); native.Decline(); native.Play();
        native.Need(native.PaidUseOnce(slash) && native.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == payment.AttackFrameId) == 1 &&
            native.RoundUses(0) == 0 && native.E<MouYuJinXiayuanIssuedEvent>().Length == 0 && native.V(2).Armor == 0,
            "Declining the actual optional offer neither restores Armor nor consumes the native Round allowance; the original paid Slash returns once.");
        native.Use("armor-one", [2]); native.Play(); native.Use("damage-one", [2]); native.Reach(native.Offer); native.Activate();
        var issued = native.E<MouYuJinXiayuanIssuedEvent>().Single(); var cost = native.SelectCosts(); native.Play();
        native.AssertRestored(issued, cost, 1, 0, 1);
        native.Need(native.RoundUses(0) == 1 && native.V(2).Hp == 12 && issued.OriginalPayment != payment && native.MotherReturned(issued),
            "A later genuine damage in the same Round remains eligible after a decline; only its distinct original invoice restores the exact one Armor after actual Hand2."); native.Cold();

        var partial = Scene.Start(new("qualification-negative-boundaries")); partial.Use("armor-three", [2]); partial.Play(); partial.Use("damage-two", [2]); partial.Play();
        partial.Need(partial.V(2) is { Hp: 12, Armor: 1 } && partial.E<DamageAppliedEvent>().Single().ArmorPayment is { ArmorBefore: 3, ArmorAbsorbed: 2, ArmorAfter: 1 } &&
            partial.E<MouYuJinXiayuanIssuedEvent>().Length == 0 && partial.RoundUses(0) == 0,
            "Partial Armor absorption cannot qualify as losing all Armor or consume an optional Round allowance.");
        partial.Use("loss-target-one", [2]); partial.Play(); partial.Use("damage-one", [3]); partial.Play();
        partial.Use("armor-one", [0]); partial.Play(); partial.Use("damage-one", [0]); partial.Play();
        partial.Need(partial.V(2) is { Hp: 11, Armor: 1 } && partial.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 3 && e.ArmorPayment is null) &&
            partial.E<DamageAppliedEvent>().Single(e => e.TargetSeat == 0).ArmorPayment is { ArmorBefore: 1, ArmorAfter: 0 } && partial.E<MouYuJinXiayuanIssuedEvent>().Length == 0,
            "Direct HP Loss leaves Armor untouched, damage starting without Armor has no positive Armor invoice, and the skill owner's own exhausted Armor is excluded.");
        partial.Use("damage-one", [2]); partial.Reach(partial.Offer); partial.Activate(); var exact = partial.E<MouYuJinXiayuanIssuedEvent>().Single(); var paid = partial.SelectCosts(); partial.Play();
        partial.AssertRestored(exact, paid, 1, 0, 1);
        partial.Need(exact.OriginalPayment is { ArmorBefore: 1, ArmorAbsorbed: 1, HpLossAmount: 0 } && partial.V(2).Hp == 11 && partial.MotherReturned(exact),
            "Only the later exhausting segment restores its remaining one Armor, never the earlier partial loss or a present-time deficit."); partial.Cold();

        var large = Scene.Start(new("historical-armor-twenty-three")); large.Use("armor-twenty", [2]); large.Play(); large.Use("armor-three", [2]); large.Play();
        large.Use("damage-twenty-three", [2]); large.Reach(large.Offer); large.Activate(); var overTwenty = large.E<MouYuJinXiayuanIssuedEvent>().Single(); var two = large.SelectCosts(); large.Play();
        large.AssertRestored(overTwenty, two, 23, 0, 23);
        large.Need(overTwenty.OriginalPayment is { DamageAmount: 23, ArmorBefore: 23, ArmorAbsorbed: 23, ArmorAfter: 0, HpLossAmount: 0 } && large.V(2).Hp == 12 && large.MotherReturned(overTwenty),
            "Two ordinary native grants20+3 and one actual damage23 yield one full historical restoration23, without clipping to the fixed literal20 descriptor or inventing repeated payments."); large.Cold();

        var shortHand = Scene.Start(new("two-hand-not-two-he") { Hand = [C(CardKind.Dodge)], Equipment = [C(CardKind.StoneAxe)] });
        shortHand.Use("armor-one", [2]); shortHand.Play(); shortHand.Use("damage-one", [2]); shortHand.Play();
        shortHand.Need(shortHand.V() is { HandCount: 1 } && shortHand.V().Equipment.Count == 1 && shortHand.E<MouYuJinXiayuanIssuedEvent>().Length == 0 && shortHand.RoundUses(0) == 0,
            "One actual Hand plus one equipped entity is not the required two Hand materials and cannot accept or consume Xiayuan."); shortHand.Cold();
    }

    public static void XiayuanFriendlyOwnersSharedInvoiceRoundExtraTurnAndRegrantCold()
    {
        var s = Scene.Start(new("friendly-two-owners-round") { MultipleOwners = true, ConditionalMinimumHp = 4, ExtraOwnTurn = true });
        s.Use("armor-three", [2]); s.Play(); s.Use("damage-three", [2]); s.Reach(s.Offer); s.Activate();
        var first = s.E<MouYuJinXiayuanIssuedEvent>().Single(); var costs = s.SelectCosts(); s.Play();
        var pair = s.E<MouYuJinXiayuanIssuedEvent>();
        s.Need(pair.Length == 2 && pair.Select(e => e.Source.OwnerSeat).SequenceEqual([0, 1]) && pair.All(e => e.OriginalPayment == first.OriginalPayment && e.OriginalDamageWindowId == first.OriginalDamageWindowId) &&
            pair.All(e => e.RoundNumber == first.RoundNumber) && s.V(2) is { Hp: 12, Armor: 6 } && s.RoundUses(0) == 1 && s.RoundUses(1) == 1,
            "Two genuine friendly Xiayuan owners may independently accept the same original exhausted-Armor invoice; the second remains eligible after the first restores current Armor.");
        s.AssertRestored(first, costs, 3, 0, 3); var second = pair[1]; var secondCost = s.CostIds(second.FrameId); s.AssertRestored(second, secondCost, 3, 3, 6);
        s.Need(s.MotherReturned(first) && s.MotherReturned(second) && s.E<DamageAppliedEvent>().Length == 1 && s.V(1).HandCount == 2,
            "The real AI ally chooses and pays its own two actual Hand entities, adding three Armor rather than replacing the first owner's grant or replaying the damage."); s.Cold();
        s.Use("loss-one"); s.Play(); s.Need(!s.Has(0, Xiayuan), "An actual HP change removes the conditional original Xiayuan source."); s.Use("recover-one"); s.Play();
        s.Need(s.Has(0, Xiayuan) && s.RoundUses(0) == 1, "A genuine new source instance cannot refresh the named skill's consumed Round budget.");
        s.Use("damage-six", [2]); s.Play(); s.Need(s.V(2).Armor == 0 && s.E<MouYuJinXiayuanIssuedEvent>().Length == 2, "Both owners remain spent for another exhausting damage in the same actual Round.");
        var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.G.State.TurnNumber > turn && s.E<TurnStartedEvent>().Last().ActorSeat == 0 && s.E<RoundStartedEvent>().Last().RoundNumber == first.RoundNumber &&
            s.RoundUses(0) == 1 && s.RoundUses(1) == 1,
            "A genuine extra own actual turn is still the same Round and cannot refresh either owner's once-per-Round acceptance."); s.Cold();
        s.Use("armor-three", [2]); s.Play(); s.Use("damage-three", [2]); s.Play(); s.Need(s.E<MouYuJinXiayuanIssuedEvent>().Length == 2, "Even after fresh actual-turn state, same-Round Armor exhaustion cannot offer a third accepted rescue.");
        s.End(); s.Play(); s.Need(s.E<RoundStartedEvent>().Last().RoundNumber > first.RoundNumber && s.RoundUses(0) == 0 && s.RoundUses(1) == 0,
            "Only a real native RoundStarted clears both named-skill allowances; an owner turn or new source alone does not.");
        s.Use("armor-three", [2]); s.Play(); s.Use("damage-three", [2]); s.Reach(s.Offer); s.Activate(); s.SelectCosts(); s.Play();
        var refreshed = s.E<MouYuJinXiayuanIssuedEvent>().Skip(2).ToArray();
        s.Need(refreshed.Length == 2 && refreshed.All(e => e.RoundNumber > first.RoundNumber) && s.V(2).Armor == 6 && s.RoundUses(0) == 1 && s.RoundUses(1) == 1 &&
            refreshed.All(e => s.E<MouYuJinXiayuanCompletedEvent>().Count(c => c.FrameId == e.FrameId && c.RestoredAmount == 3) == 1),
            "The next genuine Round permits each owner one new actual Hand2 payment and one historical three-Armor grant."); s.Cold();
    }

    public static void XiayuanPaidDiscardSourceLossSelfRescueDeathAndOriginalDamageReturnCold()
    {
        foreach (var rescueSource in new[] { false, true })
        {
            var s = Scene.Start(new(rescueSource ? "paid-source-dying-rescue" : "paid-source-loss") { ConditionalMinimumHp = rescueSource ? 1 : 4,
                CostChildLoss = 1, Hand = [C(CardKind.Dodge), C(CardKind.Dodge), C(CardKind.Peach)] });
            if (rescueSource) { s.Use("loss-three"); s.Play(); }
            s.Use("armor-three", [2]); s.Play(); s.Use("damage-three", [2]); s.Reach(s.Offer); s.Activate(); var receipt = s.E<MouYuJinXiayuanIssuedEvent>().Single();
            var cost = s.SelectCosts(); s.Reach(() => s.Hold(0));
            s.Need(s.V(2).Armor == 0 && s.DiscardedOnce(receipt, cost) && s.E<ArmorGrantedEvent>().All(e => e.ProgramFrameId != receipt.FrameId) &&
                s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == receipt.FrameId).MouYuJinXiayuan is { CostReturned: false, Completed: false },
                "The exact native two-Hand discard and original damage invoice are retained in the real movement child before any restoration.");
            s.Private(); s.RejectPrompt(); s.Cold(); s.Continue();
            if (rescueSource)
            {
                s.Reach(() => s.P is { Kind: DecisionKind.RescueDying }); var peach = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id;
                s.Need(s.V().Hp == 0 && s.V(2).Armor == 0 && s.E<MouYuJinXiayuanCompletedEvent>().Length == 0,
                    "The already paid movement child can enter a real source Dying before the original third-party restoration is issued."); s.Private(); s.Cold();
                s.Answer(c => c.Cards.SequenceEqual([peach]) && c.Parameters.GetValueOrDefault("response") == "peach"); s.Play();
                s.Need(s.PaidUseOnce(peach) && s.E<DyingResolvedEvent>().Single() is { VictimSeat: 0, Survived: true } && s.V().Hp == 1,
                    "Actual self-rescue spends the retained physical Peach once and returns its exact native Dying child.");
            }
            else s.Play();
            s.AssertRestored(receipt, cost, 3, 0, 3);
            s.Need((rescueSource ? s.Has(0, Xiayuan) : !s.Has(0, Xiayuan)) && s.RoundUses(0) == 1 && s.MotherReturned(receipt) &&
                s.E<DamageAppliedEvent>().Length == 1 && !s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == receipt.FrameId),
                "Paid source loss or loss/regrant during real self-rescue cannot repeat the Hand cost or replace the historical amount; the original damage mother returns once."); s.Cold();
        }

        var death = Scene.Start(new("nonterminal-paid-ai-source-death") { AiSourceOnly = true, CostChildOwner = 1, CostChildLoss = 4 });
        death.Use("armor-three", [2]); death.Play(); death.Use("damage-three", [2]); death.Reach(() => death.Hold(1));
        var ai = death.E<MouYuJinXiayuanIssuedEvent>().Single(); var actualCost = death.CostIds(ai.FrameId);
        death.Need(ai.Source.OwnerSeat == 1 && death.V(1).Hp == 4 && death.V(2).Armor == 0 && death.DiscardedOnce(ai, actualCost) &&
            death.G.CreateSnapshot(1).PendingDecision is { IsPrivate: true, PlayerSeat: 1 } && death.G.CreateSnapshot(0).PendingDecision is null,
            "The genuine friendly AI source accepts and pays its own actual Hand2; its native private movement child is visible only to that source, without commands forced on the AI.");
        death.Cold(); death.Play(); death.AssertRestored(ai, actualCost, 3, 0, 3);
        death.Need(!death.V(1).IsAlive && death.V().IsAlive && death.V(2).IsAlive && death.G.State.Winner == Winner.None &&
            death.E<PlayerDiedEvent>().Count(e => e.VictimSeat == 1) == 1 && death.MotherReturned(ai) && death.RoundUses(1) == 1,
            "A real nonterminal source death after actual Hand2 payment still restores the surviving recipient once; no terminal cutoff or new material disguises this paid-tail guarantee."); death.Cold();

        var victim = Scene.Start(new("original-target-dying-rescue") { Hand = [C(CardKind.Peach), C(CardKind.Dodge), C(CardKind.Dodge)] });
        victim.Use("loss-target-eleven", [2]); victim.Play(); victim.Use("armor-one", [2]); victim.Play(); victim.Use("damage-two", [2]); victim.Reach(() => victim.P is { Kind: DecisionKind.RescueDying });
        var rescue = victim.V().Hand.Single(c => c.Kind == CardKind.Peach).Id;
        victim.Need(victim.V(2) is { Hp: 0, Armor: 0 } && victim.E<MouYuJinXiayuanIssuedEvent>().Length == 0, "The original lethal damage's own native Dying resolves before its surviving-recipient after-damage opportunity."); victim.Private(); victim.Cold();
        victim.Answer(c => c.Cards.SequenceEqual([rescue]) && c.Parameters.GetValueOrDefault("response") == "peach"); victim.Reach(victim.Offer); victim.Activate();
        var saved = victim.E<MouYuJinXiayuanIssuedEvent>().Single(); var savedCost = victim.SelectCosts(); victim.Play(); victim.AssertRestored(saved, savedCost, 1, 0, 1);
        victim.Need(victim.PaidUseOnce(rescue) && saved.OriginalPayment is { ArmorBefore: 1, ArmorAbsorbed: 1, HpLossAmount: 1, HpAfter: 0 } && victim.V(2) is { Hp: 1, Armor: 1 } &&
            victim.E<DyingResolvedEvent>().Single() is { VictimSeat: 2, Survived: true } && victim.MotherReturned(saved),
            "After actual target rescue, Xiayuan still uses the original paid Armor invoice rather than the target's changed HP or current Armor; rescue, Hand2, grant and mother return each occur once."); victim.Cold();

        var unavailable = Scene.Start(new("original-target-actually-dies")); unavailable.Use("loss-target-eleven", [2]); unavailable.Play();
        unavailable.Use("armor-one", [2]); unavailable.Play(); unavailable.Use("damage-two", [2]); unavailable.Play();
        unavailable.Need(!unavailable.V(2).IsAlive && unavailable.V(2).Armor == 0 && unavailable.E<DyingResolvedEvent>().Single() is { VictimSeat: 2, Survived: false } &&
            unavailable.E<MouYuJinXiayuanIssuedEvent>().Length == 0 && unavailable.RoundUses(0) == 0 &&
            !unavailable.E<ArmorGrantedEvent>().Any(e => e.Source.SkillId == Xiayuan),
            "A recipient who truly dies from the original Armor-exhausting damage receives no invented restoration, replacement cost or consumed Xiayuan use."); unavailable.Cold();
    }

    private sealed record Spec(string Name)
    {
        public ShanheStartingCard[] Hand { get; init; } = [C(CardKind.Slash), C(CardKind.Dodge), C(CardKind.Dodge), C(CardKind.Dodge), C(CardKind.Dodge), C(CardKind.Dodge)];
        public ShanheStartingCard[] Equipment { get; init; } = [];
        public bool MultipleOwners { get; init; }
        public bool AiSourceOnly { get; init; }
        public int? ConditionalMinimumHp { get; init; }
        public bool ExtraOwnTurn { get; init; }
        public int CostChildOwner { get; init; }
        public int CostChildLoss { get; init; }
    }

    private sealed class Fixture(Spec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-yu-jin-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.mou-yu-jin-mobile." + suffix + ".json") ?? throw new InvalidOperationException("Missing complete real mobile Yu Jin bundle."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var real = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            b.AddSkill(new(Xiayuan, real.Presentations[Xiayuan].Name, real.Presentations[Xiayuan].Description) { Program = real.Programs[Xiayuan], ProgramPresentation = real.Presentations[Xiayuan] });
            foreach (var kind in spec.Hand.Concat(spec.Equipment).Select(c => c.Kind).Concat(new[] { CardKind.Dodge, CardKind.Indulgence }).Distinct())
            { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:myjm-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int targets = 0) => new() { ["id"] = id, ["minCards"] = 0, ["maxCards"] = 0, ["minTargets"] = targets, ["maxTargets"] = targets,
                ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            static string Pause() => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held\",\"options\":[{\"id\":\"continue\"}]}";
            var acts = new JsonArray(A("loss-one", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("loss-three", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":3}"),
                A("recover-one", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"), A("loss-target-one", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}", 1),
                A("loss-target-eleven", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":11}", 1));
            foreach (var (id, amount) in new[] { ("one", 1), ("two", 2), ("three", 3), ("six", 6), ("twenty-three", 23) })
                acts.Add(A("damage-" + id, "{\"op\":\"damageParticipants\",\"target\":\"selectedTarget\",\"amount\":" + amount + "}", 1));
            foreach (var (id, amount) in new[] { ("one", 1), ("three", 3), ("twenty", 20) }) acts.Add(A("armor-" + id, "{\"op\":\"grantArmor\",\"target\":\"selectedTarget\",\"amount\":" + amount + "}", 1));
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts },
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            if (spec.ConditionalMinimumHp is { } min) defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-xiayuan\",\"skillId\":\"" + Xiayuan + "\",\"minimumHp\":" + min + "}]}"));
            var watches = new JsonArray();
            if (spec.CostChildLoss > 0) watches.Add(N("{\"id\":\"actual-paid-hand-child\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementReasons\":[\"" + CostReason + "\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause() + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + spec.CostChildLoss + "}]}"));
            if (spec.ExtraOwnTurn) watches.Add(N("{\"id\":\"one-native-extra-own-turn\",\"window\":\"turnEnding\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause() + ",{\"op\":\"pendExtraTurn\",\"target\":\"owner\"}]}"));
            if (watches.Count > 0) defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "移动谋于禁原生狭援小局夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(key => n[key]?.AsArray() ?? new JsonArray()).SelectMany(binding => binding!["effects"]!.AsArray())
                    .SelectMany(effect => effect!["options"]?.AsArray() ?? new JsonArray()).Select(option => option!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => id) });
            var fixture = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in fixture.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = fixture.Presentations[id] });
            var own = new List<string> { Driver }; if (!spec.AiSourceOnly) own.Add(spec.ConditionalMinimumHp is null ? Xiayuan : Conditional);
            if ((spec.CostChildLoss > 0 && spec.CostChildOwner == 0) || spec.ExtraOwnTurn) own.Add(Watch);
            var ally = new List<string> { Quiet }; if (spec.MultipleOwners || spec.AiSourceOnly) ally.Add(Xiayuan); if (spec.CostChildLoss > 0 && spec.CostChildOwner == 1) ally.Add(Watch);
            b.AddGeneral(new(Owner, "狭援真实人类来源", "", "standard:none", "wei", 4, own));
            b.AddGeneral(new(AllyA, "狭援真实友军来源", "", "standard:none", "wei", 4, ally));
            b.AddGeneral(new(AllyB, "双方真正友军目标", "", "standard:none", "wei", 12, [Quiet]));
            b.AddGeneral(new(Enemy, "原生其他目标", "", "standard:none", "qun", 12, [Quiet]));
            b.AddDeck(new(Deck, "固定四十八张真实闪", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("fixture:myjm-card-" + CardKind.Dodge, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋于禁狭援原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, AllyA, AllyB, Enemy],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 3, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }

    private sealed class Scene(Spec spec, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Has(int seat, string skill) => V(seat).Skills?.Any(s => s.ContentId == skill) == true;
        public int RoundUses(int owner)
        {
            var facts = G.Events.Select(e => e.Payload).ToArray(); var start = Array.FindLastIndex(facts, e => e is RoundStartedEvent);
            return facts.Skip(start + 1).OfType<SkillUsageConsumedEvent>().Count(e => e.SkillOwnerSeat == owner && e.SkillId == Xiayuan &&
                e.Scope == SkillUsageScope.Round && e.UsageId == "restore-after-armor-loss");
        }
        public bool Offer() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == Xiayuan) == true;
        public bool CostChoice() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" && c.Parameters.GetValueOrDefault("result-bind") == "xiayuan-cost") == true;
        public bool Hold(int seat) => G.CreateSnapshot(seat).PendingDecision is { } p && p.SkillPrompt?.SkillId == Watch && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public bool PaidUseOnce(int id) => G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1;
        public int[] CostIds(long frame) => G.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == frame)?.CardSetBindings.SingleOrDefault(b => b.Name == "xiayuan-cost")?.CardIds.ToArray() ??
            G.CardMovements.Where(m => m.Reason.Value == CostReason && E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.ParentFrameId == frame && m.Sequence > e.SequenceBefore && m.Sequence <= e.SequenceAfter && m.From == CardLocation.Hand(e.ExecutorSeat))).Select(m => m.CardId).Distinct().ToArray();
        public bool DiscardedOnce(MouYuJinXiayuanIssuedEvent issued, IReadOnlyList<int> cards)
        {
            var facts = E<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.ParentFrameId == issued.FrameId && e.ExecutorSeat == issued.Source.OwnerSeat && e.InitiatorSeat == issued.Source.OwnerSeat &&
                e.SubjectSeat == issued.Source.OwnerSeat && e.ActualCount == 2 && e.HandCount == 2 && e.Reason.Value == CostReason && e.SequenceBefore >= issued.MovementBefore).ToArray();
            return cards.Count == 2 && cards.Distinct().Count() == 2 && facts is [var fact] && cards.All(id =>
                G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(issued.Source.OwnerSeat) && m.Sequence > fact.SequenceBefore && m.Sequence <= fact.SequenceAfter) == 1 &&
                G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile && m.Sequence > fact.SequenceBefore && m.Sequence <= fact.SequenceAfter) == 1);
        }
        public void AssertRestored(MouYuJinXiayuanIssuedEvent issued, IReadOnlyList<int> cards, int amount, int before, int after)
        {
            var cost = E<MouYuJinXiayuanCostReturnedEvent>().Where(e => e.FrameId == issued.FrameId).ToArray(); var grant = E<ArmorGrantedEvent>().Where(e => e.ProgramFrameId == issued.FrameId).ToArray();
            Need(DiscardedOnce(issued, cards) && cost is [{ ActualHandCount: 2 }] && E<NativeLogicalDiscardInvocationRecordedEvent>().Single(e => e.ParentFrameId == issued.FrameId).InvocationId == cost[0].InvocationId &&
                grant is [{ InstructionIndex: 3 }] && grant[0].PlayerSeat == issued.OriginalPayment.TargetSeat && grant[0].Source.SkillId == Xiayuan && grant[0].Source.OwnerSeat == issued.Source.OwnerSeat &&
                grant[0].Amount == amount && grant[0].ArmorBefore == before && grant[0].ArmorAfter == after &&
                E<MouYuJinXiayuanCompletedEvent>().Count(e => e.FrameId == issued.FrameId && e.TargetSeat == issued.OriginalPayment.TargetSeat && e.RestoredAmount == amount) == 1 &&
                E<DamageAppliedEvent>().Count(e => e.ArmorPayment == issued.OriginalPayment) == 1,
                "One actual logical Hand2 cost and its returned invoice precede exactly one historical Armor grant and completion for the exact original damage payment.");
        }
        public bool MotherReturned(MouYuJinXiayuanIssuedEvent issued) => E<ProgramSkillResolvedEvent>().Count(e => e.FrameId == issued.OriginalPayment.AttackFrameId && e.SkillId == Driver && e.Completed) == 1 &&
            !G.ResolutionStack.Any(f => f.Id == issued.FrameId || f.Id == issued.OriginalPayment.DamageFrameId || f.Id == issued.OriginalPayment.AttackFrameId);
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void NativeSlash(int target, int id)
        {
            var action = G.GetHumanLegalActions().SingleOrDefault(a => a.Kind == LegalActionKind.Slash && a.CardId == id && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]));
            Need(action is not null, "A real native Slash target plan must be published before physical payment.");
            Accept(new PlayCardCommand(0, id, [target], G.Revision, P!.PromptId, action!.PlayedCardKind));
        }
        public void Activate() { Private(); RejectPrompt(); Cold(); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == Xiayuan); Reach(CostChoice); }
        public void Decline() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public int[] SelectCosts()
        {
            Reach(CostChoice); var cards = V().Hand.Where(c => c.Kind == CardKind.Dodge).Take(2).Select(c => c.Id).ToArray(); if (cards.Length < 2) cards = V().Hand.Take(2).Select(c => c.Id).ToArray();
            Need(cards.Length == 2 && P!.Choices.Where(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards").All(c => c.Cards.Count == 1 && V().Hand.Any(card => card.Id == c.Cards[0])) &&
                V().Equipment.All(c => !P.ValidCardIds.Contains(c.Id)), "The native private exact-two selection contains only real self Hand entities, never Equipment or a fabricated shorter cost.");
            Private(); RejectPrompt(); Cold(); foreach (var card in cards) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" && c.Cards.SequenceEqual([card])); return cards;
        }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null && p.PlayerSeat == 0, "Choose only a published real human choice."); Accept(new AnswerPromptCommand(0, p.PromptId, choice!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 220; step++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "The small native fixture ended before boundary " + line);
                var p = P; if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == Watch) Continue();
                else if (Offer()) Decline();
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Private()
        {
            Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(i => G.CreateSnapshot(i).PendingDecision is null) &&
                Enumerable.Range(0, 4).All(i => G.CreateSnapshot(i).Players.Where(p => p.Seat != i).All(p => p.Hand.Count == 0)), "Only the actual chooser sees its private prompt; every foreign Hand remains opaque.");
            Immutable(P!.Choices); foreach (var c in P.Choices) { Immutable(c.Cards); Immutable(c.Targets); }
        }
        private void Immutable<T>(IReadOnlyList<T> list) { if (list is IList mutable && mutable.Count > 0) { var rejected = false; try { mutable[0] = mutable[0]; } catch (NotSupportedException) { rejected = true; } Need(rejected, "Committed nested selection lists reject observer mutation."); } }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves the original damage invoice, exact paid Hand2, all private views, Round usage and native child cursors."); }
        public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && before == State(), "Forged, wrong-seat and stale commands cannot change selected entities, damage, Armor, usage or owning frames."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-xiayuan-cost"), G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision - 1)); }
        public void Accept(GameCommand command) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).Armor, V(i).HandCount, V(i).Skills }), OtherPrompt = G.CreateSnapshot(1).PendingDecision,
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Movements = G.CardMovements.TakeLast(10),
            Rules = E<MouYuJinXiayuanIssuedEvent>(), Returned = E<MouYuJinXiayuanCostReturnedEvent>(), Completed = E<MouYuJinXiayuanCompletedEvent>(), Grants = E<ArmorGrantedEvent>().TakeLast(6),
            Damage = E<DamageAppliedEvent>().TakeLast(4), Dying = E<DyingResolvedEvent>(), Deaths = E<PlayerDiedEvent>(), Rounds = E<RoundStartedEvent>(), Usage = E<SkillUsageConsumedEvent>().Where(e => e.SkillId == Xiayuan) });
        public static Scene Start(Spec spec)
        {
            ContentRegistry registry; try { registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec)); } catch (Exception e) { throw new InvalidOperationException(spec.Name + " fixture: " + e.Message, e); }
            var setup = new ShanheBattleSetup(83, 1, "myjm-" + spec.Name, Owner, 4, 0, 0, 0, 0, [], spec.Hand, spec.Equipment,
                [new(Enemy) { StartingHand = [] }], [new(AllyA) { StartingHand = [C(CardKind.Dodge), C(CardKind.Dodge), C(CardKind.Dodge), C(CardKind.Dodge)] }, new(AllyB) { StartingHand = [C(CardKind.Indulgence)] }], new string('c', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 18, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var scene = new Scene(spec, game, registry); scene.Accept(new StartGameCommand()); scene.Play(); return scene;
        }
    }
}
