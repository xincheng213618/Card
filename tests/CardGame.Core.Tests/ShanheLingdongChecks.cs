using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheLingdongChecks
{
    private const string Lingdong = "shanhe:s5-lingdong-14045", Guicai = "classic:guicai", Tiandu = "classic:tiandu";
    private const string Jueqing = "classic:jueqing", Xiaoming = "shanhe:xiaoming", Reason = "shanhe.s5.lingdong";
    private const string Hongyan = "classic:hongyan", LoseColor = "fixture:lingdong-lose-color";
    private const string Driver = "fixture:lingdong-driver", Watch = "fixture:lingdong-damage-child", ClaimWatch = "fixture:lingdong-claim-child";
    private const string Owner = "fixture:lingdong-owner", Ally = "fixture:lingdong-ally", EnemyA = "fixture:lingdong-enemy-a";
    private const string EnemyB = "fixture:lingdong-enemy-b", Deck = "fixture:lingdong-deck";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture-lingdong-production", StandardShanheS5BossPackage.ZhangliaoLingdongId,
            4, 0, 0, 0, 0, [], [], [], [new("standard:cao-cao")], [], new string('a', 64))));

    public static void FinalNativeGuicaiColorControlsPreventionArmorAndAfterDamageCold()
    {
        VerifyCatalogIsolationAndLoaderBoundaries();
        foreach (var redResult in new[] { true, false })
        {
            var s = Scene.Start(redResult ? "black-red" : "red-black");
            s.Use("armor-one", [0]);
            var replacement = s.V().Hand.Single().Id;
            s.Begin("incoming", [2]); s.AtGuicai(0);
            var judgment = s.G.ResolutionStack.OfType<JudgmentFrame>().Single();
            var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Lingdong);
            Need(judgment.ParentFrameId == parent.Id && parent.WindowContext?.Window == SkillProgramTriggerWindow.BeforeDamageApplied &&
                judgment.Reason == Reason && judgment.Suit == (redResult ? Suit.Spade : Suit.Heart),
                "The real incoming damage owns the Lingdong program, which directly owns its original judgment before native Guicai replaces it.");
            s.Cold(); s.Answer(c => c.Cards.SequenceEqual([replacement]));
            if (!redResult)
            {
                s.AtDamageChild();
                var hit = s.Events<DamageAppliedEvent>().Single();
                Need(hit is { SourceSeat: 2, TargetSeat: 0, Amount: 2, ArmorPayment:
                        { ArmorBefore: 1, ArmorAbsorbed: 1, ArmorAfter: 0, HpBefore: 12, HpLossAmount: 1, HpAfter: 11 } } &&
                    s.G.ResolutionStack.OfType<DamageFrame>().Single().ArmorPayment == hit.ArmorPayment,
                    "A real red-to-black replacement permits the full two damage, with one exact Armor payment and one HP loss on its owning damage frame.");
                s.Cold(); s.Continue();
            }
            s.Play();
            var final = s.Events<JudgmentResolvedEvent>().Single(e => e.Reason == Reason);
            var outcome = s.Events<ProgramJudgmentOutcomeRecordedEvent>().Single().Outcome;
            Need(final.CardId == replacement && final.Suit == (redResult ? Suit.Heart : Suit.Spade) &&
                outcome is { TargetSeat: 0, Reason: Reason, InstructionIndex: 1 } &&
                outcome.JudgmentFrameId == final.ResolutionId && outcome.CardId == replacement && outcome.Suit == final.Suit &&
                s.Events<ProgramJudgmentReplacementResolvedEvent>().Single(e => e.SkillId == Guicai) is { Activated: true, ReplacementCardId: var paid } && paid == replacement,
                "The scalar outcome freezes the actual final Guicai card and color, rather than the original revealed card.");
            Need(redResult
                    ? s.V() is { Hp: 12, Armor: 1 } && s.Events<DamageAppliedEvent>().Length == 0 &&
                        s.Events<AfterDamageEvent>().Length == 0 && s.Events<ProgramDamagePreventedEvent>() is [{ SkillId: Lingdong, TargetSeat: 0, Amount: 2 }]
                    : s.V() is { Hp: 11, Armor: 0 } && s.Events<ProgramDamagePreventedEvent>().Length == 0 &&
                        s.Events<AfterDamageEvent>() is [{ TargetSeat: 0, Amount: 2 }],
                "Final red prevents payment and all damage-after effects; final black pays Armor/HP and retains the full native AfterDamage amount.");
            Need(s.G.CreateCardZoneDiagnostics().Single(card => card.CardId == replacement).Location == CardLocation.DiscardPile &&
                s.G.CardMovements.Count(move => move.CardId == replacement && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1,
                "The actual replacement card is paid once and cleaned up once through the native judgment continuation.");
            s.Cold();
        }
        var empty = Scene.Start("empty"); empty.Use("armor-one", [0]);
        var initialMovements = empty.G.CardMovements.Count;
        Need(empty.G.CreateCardZoneDiagnostics() is { Count: 4 } initial && initial.All(card => card.Location.Zone == CardZoneKind.Hand),
            "The legal four-card deck is genuinely exhausted by initial dealing, with no cards in either pile.");
        empty.Begin("incoming", [2]); empty.AtDamageChild();
        Need(empty.Events<ProgramJudgmentOutcomeRecordedEvent>() is
                [{ Outcome: { CardId: null, Suit: null, Rank: null, InstructionIndex: 1, TargetSeat: 0 } }] &&
            empty.Events<JudgmentResolvedEvent>() is [{ Reason: Reason, CardId: null, Suit: null, Rank: null }] &&
            empty.Events<DamageAppliedEvent>() is [{ Amount: 2, ArmorPayment:
                { ArmorBefore: 1, ArmorAbsorbed: 1, ArmorAfter: 0, HpLossAmount: 1, HpAfter: 11 } }] &&
            empty.G.CreateCardZoneDiagnostics().Count == 4 && empty.G.CardMovements.Count == initialMovements,
            "A genuinely empty pile issues a nullable no-result outcome; red never matches, real Armor/HP payment continues and no synthetic judgment entity is created.");
        empty.Cold(); empty.Continue(); empty.Play();
        Need(empty.Events<ProgramDamagePreventedEvent>().Length == 0 && empty.Events<AfterDamageEvent>() is [{ Amount: 2 }],
            "Empty outcome cleanup completes the same native damage exactly once without inventing a prevention or card movement.");
        empty.Cold();
    }

    public static void ElementalChainJudgesEachRecipientAndMiddlePreventionKeepsFullOriginDamageCold()
    {
        var s = Scene.Start("chain"); PrepareChain(s);
        s.BeginCard(CardKind.ThunderSlash, [1]); s.AtGuicai(1); s.Cold(); s.SkipGuicai();
        s.AtGuicai(2);
        Need(s.Events<DamageAppliedEvent>() is [{ TargetSeat: 1, Amount: 2, Nature: DamageNature.Thunder,
                ArmorPayment: { ArmorBefore: 3, ArmorAbsorbed: 2, ArmorAfter: 1, HpLossAmount: 0 } }],
            "The first native elemental segment conducts its full two damage despite complete Armor absorption.");
        s.Cold();
        var red = s.V().Hand.Single(card => card.Kind == CardKind.Dodge && card.Suit == Suit.Heart).Id;
        s.Answer(c => c.Cards.SequenceEqual([red])); s.AtGuicai(3);
        Need(s.Events<ProgramDamagePreventedEvent>() is [{ SkillId: Lingdong, TargetSeat: 2, Amount: 2 }] &&
            s.V(2) is { Hp: 12, Armor: 3 } && s.Events<DamageAppliedEvent>().Length == 1,
            "The middle recipient's real final red judgment prevents only that recipient, without consuming Armor or rewriting the first segment's amount.");
        s.Cold(); s.SkipGuicai(); s.Play();
        Need(s.Events<JudgmentRequestedEvent>().Where(e => e.Reason == Reason).Select(e => e.TargetSeat).SequenceEqual([1, 2, 3]) &&
            s.Events<ProgramJudgmentOutcomeRecordedEvent>().Select(e => (e.Outcome.TargetSeat, e.Outcome.Suit))
                .SequenceEqual(new (int, Suit?)[] { (1, Suit.Spade), (2, Suit.Heart), (3, Suit.Spade) }) &&
            s.Events<ProgramJudgmentOutcomeRecordedEvent>().Select(e => e.Outcome.JudgmentFrameId).Distinct().Count() == 3,
            "Every actual chained recipient owns exactly one independently finalized judgment, including the prevented middle recipient.");
        Need(s.Events<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount, e.Nature))
                .SequenceEqual([(1, 2, DamageNature.Thunder), (3, 2, DamageNature.Thunder)]) &&
            s.Events<AfterDamageEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(1, 2), (3, 2)]) &&
            s.V(1) is { Hp: 12, Armor: 1 } && s.V(2) is { Hp: 12, Armor: 3 } && s.V(3) is { Hp: 11, Armor: 0 },
            "The last real recipient still takes the original full two damage and separately pays its one Armor, with no after-damage event for the prevented middle segment.");
        s.Cold();

        var origin = Scene.Start("chain"); PrepareChain(origin);
        origin.BeginCard(CardKind.ThunderSlash, [1]); origin.AtGuicai(1); origin.Cold();
        var redOrigin = origin.V().Hand.Single(card => card.Kind == CardKind.Dodge && card.Suit == Suit.Heart).Id;
        origin.Answer(c => c.Cards.SequenceEqual([redOrigin])); origin.Play();
        Need(origin.Events<JudgmentRequestedEvent>().Where(e => e.Reason == Reason).Select(e => e.TargetSeat).SequenceEqual([1]) &&
            origin.Events<ProgramJudgmentOutcomeRecordedEvent>().Length == 1 &&
            origin.Events<DamageAppliedEvent>().Length == 0 && origin.Events<AfterDamageEvent>().Length == 0 &&
            origin.Events<ChainedDamagePropagatedEvent>().Length == 0 &&
            origin.Events<ProgramDamagePreventedEvent>() is [{ TargetSeat: 1, Amount: 2 }] &&
            origin.V(1).Armor == 3 && origin.V(2).Armor == 3 && origin.V(3).Armor == 1,
            "A prevented origin never pays Armor or starts propagation, so later linked characters do not receive fictitious judgments.");
        origin.Cold();
        var originOnly = Scene.Start("chain-origin-only"); PrepareChain(originOnly);
        originOnly.BeginCard(CardKind.ThunderSlash, [1]); originOnly.AtGuicai(1); originOnly.Cold(); originOnly.SkipGuicai();
        for (var recipient = 1; recipient <= 3; recipient++)
        {
            originOnly.AtDamageChild();
            Need(originOnly.Events<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount))
                    .SequenceEqual(Enumerable.Range(1, recipient).Select(seat => (seat, 2))) &&
                originOnly.G.ResolutionStack.OfType<CardUseFrame>().Single().CardAttack is { RangePreventionVisitedTargets: null } &&
                originOnly.Events<JudgmentRequestedEvent>().Where(e => e.Reason == Reason).Select(e => e.TargetSeat).SequenceEqual([1]),
                "An active origin's black Lingdong judgment does not stamp unrelated chained recipients merely because their registry contains the capability.");
            originOnly.Cold(); originOnly.Continue();
        }
        originOnly.Play();
        Need(originOnly.Events<ProgramJudgmentOutcomeRecordedEvent>() is [{ Outcome.TargetSeat: 1 }] &&
            originOnly.Events<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(1, 2), (2, 2), (3, 2)]) &&
            originOnly.Events<AfterDamageEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(1, 2), (2, 2), (3, 2)]),
            "Only the actual origin holder judges once; the two recipients without Lingdong complete unchanged native two-point chain segments.");
        originOnly.Cold();
    }

    public static void NativeTianduKeepsClaimedEntityWhileReplacementsBypassAndRealDamageJudgesCold()
    {
        var s = Scene.Start("tiandu"); s.Use("armor-three", [0]);
        s.Begin("incoming", [2]);
        s.Reach(() => s.P is { Kind: DecisionKind.ProgramJudgmentTrigger, PlayerSeat: 0 } &&
            s.G.ResolutionStack.LastOrDefault() is ProgramJudgmentTriggerWindowFrame window &&
            window.Candidates[window.CandidateIndex].SkillId == Tiandu);
        var final = s.Events<JudgmentResolvedEvent>().Single(e => e.Reason == Reason);
        Need(final is { CardId: not null, Suit: Suit.Heart } &&
            s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == final.CardId).Location == CardLocation.Judgment(0),
            "Native Tiandu sees the original finalized red judgment card in its actual judgment zone.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-activate");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == ClaimWatch);
        var claim = s.Events<ProgramJudgmentCardClaimedEvent>().Single();
        Need(claim.SkillId == Tiandu && claim.CardId == final.CardId && claim.JudgmentFrameId == final.ResolutionId &&
            s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == claim.CardId).Location == CardLocation.Hand(0) &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Tiandu && frame.PendingMovementContinuation is not null),
            "The real claim pays the exact judged entity into hand and pauses in a genuine CardsGained child owned by Tiandu.");
        s.Cold(); s.Continue(); s.Play();
        var outcome = s.Events<ProgramJudgmentOutcomeRecordedEvent>().Single().Outcome;
        Need(outcome.CardId == claim.CardId && outcome.Suit == Suit.Heart &&
            s.V() is { Hp: 12, Armor: 3, HandCount: 1 } && s.V().Hand.Single().Id == claim.CardId &&
            s.Events<ProgramDamagePreventedEvent>() is [{ SkillId: Lingdong, TargetSeat: 0, Amount: 2 }] &&
            s.Events<DamageAppliedEvent>().Length == 0 && s.Events<AfterDamageEvent>().Length == 0 &&
            s.Events<ProgramJudgmentCardClaimedEvent>().Length == 1 &&
            s.G.CardMovements.Count(move => move.CardId == claim.CardId && move.From == CardLocation.Judgment(0) && move.To == CardLocation.Hand(0)) == 1 &&
            !s.G.CardMovements.Any(move => move.CardId == claim.CardId && move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile),
            "The frozen final red result still prevents damage after Tiandu claims it; cleanup never steals or discards the claimed entity, including after cold child replay.");
        s.Cold();
        VerifyClaimedCardRelocationAndLostHongyanKeepFinalRed();

        var j = Scene.Start("jueqing"); j.Use("armor-three", [3]); j.Use("lose-two", [3]); j.PlayCard(CardKind.Slash, [3]);
        Need(j.V(3) is { Hp: 9, Armor: 3 } && j.Events<ProgramSkillHpLostEvent>() is [{ TargetSeat: 3, Amount: 2 }] &&
            j.Events<DamageReplacedWithHpLossEvent>() is [{ TargetSeat: 3, Amount: 1 }] &&
            j.Events<JudgmentRequestedEvent>().Length == 0 && j.Events<ProgramJudgmentOutcomeRecordedEvent>().Length == 0 &&
            j.Events<DamageAppliedEvent>().Length == 0,
            "Real loseHp and the real native Jueqing Slash replacement bypass Lingdong and Armor without manufacturing damage or judgment.");
        j.Cold();
        var x = Scene.Start("maximum"); x.Use("armor-three", [3]); x.PlayCard(CardKind.Slash, [3]);
        Need(x.V(3) is { Hp: 11, MaxHp: 11, Armor: 3 } &&
            x.Events<CardDamageReplacedWithMaximumHpLossEvent>() is [{ Receipt.TargetSeat: 3, Receipt.Amount: 1 }] &&
            x.Events<JudgmentRequestedEvent>().Length == 0 && x.Events<ProgramJudgmentOutcomeRecordedEvent>().Length == 0 &&
            x.Events<DamageAppliedEvent>().Length == 0,
            "The genuine first-Slash maximum-HP replacement resolves before any Lingdong judgment and leaves Armor unspent.");
        x.Cold();
        var program = Scene.Start("program"); program.Use("incoming", [2]); program.Cold(); program.Use("source-less");
        Need(program.Events<JudgmentRequestedEvent>().Where(e => e.Reason == Reason).Count() == 2 &&
            program.Events<ProgramJudgmentOutcomeRecordedEvent>().Length == 2 &&
            program.Events<DamageAppliedEvent>() is [{ SourceSeat: 2, TargetSeat: 0, Amount: 2, SourceLess: false },
                { TargetSeat: 0, Amount: 2, SourceLess: true }] && program.V().Hp == 8 &&
            program.Events<AfterDamageEvent>().Select(e => e.Amount).SequenceEqual([2, 2]),
            "Actual program damage and source-less damage each start their own real black Lingdong judgment and complete their native damage-after flow once.");
        program.Cold();
        var sourceLessRed = Scene.Start("black-red"); sourceLessRed.Use("armor-three", [0]);
        var redPayment = sourceLessRed.V().Hand.Single().Id;
        sourceLessRed.Begin("source-less"); sourceLessRed.AtGuicai(0); sourceLessRed.Cold();
        sourceLessRed.Answer(c => c.Cards.SequenceEqual([redPayment])); sourceLessRed.Play();
        Need(sourceLessRed.Events<ProgramDamagePreventedEvent>() is
                [{ SkillId: Lingdong, SourceSeat: null, TargetSeat: 0, Amount: 2 }] &&
            sourceLessRed.Events<ProgramJudgmentOutcomeRecordedEvent>() is
                [{ Outcome: { CardId: var finalRed, Suit: Suit.Heart } }] && finalRed == redPayment &&
            sourceLessRed.Events<JudgmentResolvedEvent>() is [{ CardId: var judgedRed, Suit: Suit.Heart }] && judgedRed == redPayment &&
            sourceLessRed.V() is { Hp: 12, Armor: 3 } && sourceLessRed.Events<DamageAppliedEvent>().Length == 0 &&
            sourceLessRed.Events<AfterDamageEvent>().Length == 0 &&
            sourceLessRed.G.CreateCardZoneDiagnostics().Single(c => c.CardId == redPayment).Location == CardLocation.DiscardPile &&
            sourceLessRed.G.CardMovements.Count(move => move.CardId == redPayment && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1,
            "A genuine source-less damage attempt can be prevented by the real paid red Guicai judgment; its prevention keeps a null source, spends no Armor/HP and cleans the exact replacement entity once.");
        sourceLessRed.Cold();
    }

    private static void VerifyClaimedCardRelocationAndLostHongyanKeepFinalRed()
    {
        var s = Scene.Start("tiandu-relocated"); s.Use("armor-three", [0]); s.Begin("incoming", [2]);
        s.Reach(() => s.P is { Kind: DecisionKind.ProgramJudgmentTrigger, PlayerSeat: 0 } &&
            s.G.ResolutionStack.LastOrDefault() is ProgramJudgmentTriggerWindowFrame window &&
            window.Candidates[window.CandidateIndex].SkillId == Tiandu);
        var judged = s.Events<JudgmentResolvedEvent>().Single(e => e.Reason == Reason);
        Need(judged is { Suit: Suit.Heart, CardId: not null } && s.V().Skills!.Any(skill => skill.ContentId == Hongyan),
            "Real Hongyan makes the fixed printed Spade judgment finally Heart before Tiandu takes its original entity.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-activate");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == ClaimWatch);
        var claimed = s.Events<ProgramJudgmentCardClaimedEvent>().Single();
        Need(claimed.CardId == judged.CardId && s.V().Hand.Single().Id == claimed.CardId,
            "The nested disposal starts only after native Tiandu has actually paid the exact finalized judgment card into hand.");
        s.Cold(); s.Continue();
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true);
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([claimed.CardId]));
        if (s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") == true)
            s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == ClaimWatch && s.V().Skills?.Any(skill => skill.ContentId == Hongyan) != true);
        Need(s.V() is { Hp: 1, Armor: 3, HandCount: 0 } &&
            s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == claimed.CardId).Location == CardLocation.DiscardPile &&
            s.Events<ProgramOwnerSkillsReplacedEvent>() is [{ LostSkillIds: var lost }] && lost.SequenceEqual([Hongyan]) &&
            s.G.ResolutionStack.OfType<JudgmentFrame>().Single().Id == judged.ResolutionId,
            "The claimed card really leaves hand, a legal dying-entry recovery removes Hongyan, and the original finalized judgment remains suspended over its exact native descendants.");
        s.Cold(); s.Continue(); s.Play();
        Need(s.Events<ProgramJudgmentOutcomeRecordedEvent>() is [{ Outcome: { CardId: var cardId, Suit: Suit.Heart } }] && cardId == claimed.CardId &&
            s.Events<ProgramDamagePreventedEvent>() is [{ SkillId: Lingdong, TargetSeat: 0, Amount: 2 }] &&
            s.Events<DamageAppliedEvent>().Length == 0 && s.V() is { Hp: 1, Armor: 3, HandCount: 0 } &&
            s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == claimed.CardId).Location == CardLocation.DiscardPile &&
            s.Events<ProgramJudgmentCardClaimedEvent>().Length == 1 &&
            s.G.CardMovements.Count(move => move.CardId == claimed.CardId && move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile) == 1,
            "The old finalized Heart outcome survives actual card relocation and loss of Hongyan; Lingdong prevents damage without reclaiming the entity or repeating the nested discard/recovery.");
        s.Cold();
    }

    private static void PrepareChain(Scene s)
    {
        s.Use("armor-three", [1]); s.Use("armor-three", [2]); s.Use("armor-one", [3]);
        s.PlayCard(CardKind.IronChain, [1, 2]); s.PlayCard(CardKind.IronChain, [3]);
        Need(new[] { 1, 2, 3 }.All(seat => s.V(seat).IsChained), "Two real paid Iron Chains link all three actual recipients.");
        s.PlayCard(CardKind.Alcohol, []); s.Cold();
    }

    private static void VerifyCatalogIsolationAndLoaderBoundaries()
    {
        foreach (var difficulty in new[] { 1, 2, 3 })
        {
            var modern = StandardShanheCampaignCatalog.CreateDefinition(difficulty, StandardShanheCampaignCatalog.Returning2026EditionId);
            var expected = difficulty switch
            {
                1 => StandardShanheS5BossPackage.ZhangliaoBaseId,
                2 => StandardShanheS5BossPackage.ZhangliaoLingdongId,
                _ => StandardShanheS5BossPackage.ZhangliaoHuangkongId
            };
            Need(modern.Nodes.Single(n => n.Id == "hulao").Encounters!.Any(e => e.Enemies.Any(m => m.GeneralId == expected)) &&
                StandardShanheCampaignCatalog.CreateDefinition(difficulty).Nodes.SelectMany(n => n.Encounters ?? [])
                    .SelectMany(e => e.Enemies).All(m => m.GeneralId != StandardShanheS5BossPackage.ZhangliaoBaseId &&
                        m.GeneralId != StandardShanheS5BossPackage.ZhangliaoLingdongId && m.GeneralId != StandardShanheS5BossPackage.ZhangliaoHuangkongId),
                "The real local three-tier 2026 recipe selects its declared basic/Lingdong/Huangkong form without adding current S5 forms to the 2024 entry.");
        }
        var ordinary = StandardShanheCampaignCatalog.CreateBattleRegistry(new(83, 1, "fixture-lingdong-ordinary", "classic:guan-yu",
            4, 0, 0, 0, 0, [], [], [], [new("standard:cao-cao")], [], new string('b', 64)));
        Need(!ordinary.Skills.ContainsKey(Lingdong) && !ordinary.Generals.ContainsKey(StandardShanheS5BossPackage.ZhangliaoLingdongId) &&
            !ordinary.Skills.ContainsKey(StandardShanheS5BossPackage.HuangkongId),
            "An ordinary battle recipe does not silently load the new dedicated boss package.");
        string Resource(string suffix)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith('.' + suffix, StringComparison.Ordinal)))!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var rules = Resource("shanhe-s5-lingdong.rules.json");
        var presentation = Resource("shanhe-s5-lingdong.presentation.json");
        _ = SkillProgramCatalog.Load(rules, presentation);
        void Reject(Action<JsonArray> change)
        {
            var copy = JsonNode.Parse(rules)!; var effects = copy["skills"]![0]!["triggers"]![0]!["effects"]!.AsArray(); change(effects);
            var rejected = false;
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "Typed judgment outcomes cannot be read before production, forged from ordinary card bindings, reused or aimed at an unsupported damage participant.");
        }
        Reject(e => e[0]!["outcomeBind"] = "lingdong-cleanup");
        Reject(e => e[0]!.AsObject().Remove("outcomeBind"));
        Reject(e => e[1]!["condition"]!["sourceBind"] = "lingdong-cleanup");
        Reject(e => { var first = e[1]!.DeepClone(); e.RemoveAt(1); e.Insert(0, first); });
        Reject(e => { var duplicate = e[0]!.DeepClone(); duplicate["resultBind"] = "second-cleanup"; e.Insert(1, duplicate); });
        Reject(e => e[1]!["target"] = "selectedTarget");
        Reject(e => e[1]!["condition"] = new JsonObject { ["kind"] = "wounded" });
        Reject(e => e.Insert(1, JsonNode.Parse("""{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"lingdong-outcome"}""")));
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand command) { var r = G.Submit(command); Need(r.Accepted, r.Error?.Message ?? "Lingdong rejected a genuine native command."); }
        public void Begin(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Use(string id, int[]? targets = null) { Begin(id, targets); Play(); }
        public void Answer(Func<PromptChoice, bool> choose) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(choose).Id, G.Revision));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void SkipGuicai() => Answer(c => c.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip");
        public void AtGuicai(int target) => Reach(() => P is { Kind: DecisionKind.ProgramJudgmentReplacement } &&
            G.ResolutionStack.OfType<JudgmentFrame>().Single().TargetSeat == target);
        public void AtDamageChild() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void BeginCard(CardKind kind, int[] targets)
        {
            var action = G.GetHumanLegalActions().First(a => a.CardId is { } id && V().Hand.Any(c => c.Id == id && c.Kind == kind) &&
                a.ConversionSource is null && a.Kind != LegalActionKind.Recast && a.TargetSeats.SequenceEqual(targets));
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void PlayCard(CardKind kind, int[] targets) { BeginCard(kind, targets); Play(); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The bounded Lingdong fixture must not finish a match.");
                if (P?.PlayerSeat == 0)
                {
                    if (P.SkillPrompt?.SkillId is Watch or ClaimWatch) Continue();
                    else throw new InvalidOperationException("Unexpected Lingdong native boundary: " + Diagnostic());
                }
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded Lingdong continuation: " + Diagnostic());
        }
        private string Diagnostic()
        {
            var attack = G.ResolutionStack.OfType<CardUseFrame>().LastOrDefault()?.CardAttack;
            return JsonSerializer.Serialize(new
            {
                Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId },
                Seats = Enumerable.Range(0, 4).Select(seat => new { Seat = seat, V(seat).Hp, V(seat).Armor, V(seat).IsChained }),
                Judgments = Events<JudgmentResolvedEvent>(),
                Outcomes = Events<ProgramJudgmentOutcomeRecordedEvent>().Select(e => new { e.ProgramFrameId, e.Outcome.TargetSeat, e.Outcome.CardId, e.Outcome.Suit }),
                Damage = Events<DamageAppliedEvent>().Select(e => new { e.SourceSeat, e.TargetSeat, e.Amount, e.Nature, e.ArmorPayment }),
                Prevention = Events<ProgramDamagePreventedEvent>(), Chains = Events<ChainedDamagePropagatedEvent>(),
                ChainStates = Events<IronChainStateChangedEvent>(),
                Attack = attack is null ? null : new { attack.TargetSeat, attack.DamageAmount, attack.DamageAmountFinalized,
                    attack.ChainedTargetSeats, attack.ChainedTargetIndex, attack.IsChainPropagation, attack.BeforeDamageProgramsResolved,
                    attack.RangePreventionVisitedTargets },
                Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind })
            });
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold command replay preserves every actual judgment, outcome, native child, Armor receipt, private view and physical entity exactly.");
            var zones = G.CreateCardZoneDiagnostics();
            Need(zones.Select(c => c.CardId).Distinct().Count() == zones.Count, "Every actual Lingdong card still occupies exactly one zone.");
        }
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var own = new List<string> { Watch };
            if (scene is "black-red" or "red-black" or "tiandu" or "tiandu-relocated" or "program" or "empty") own.Add(Lingdong);
            if (scene is "black-red" or "red-black" or "chain" or "chain-origin-only") own.Add(Guicai);
            if (scene is "tiandu" or "tiandu-relocated") own.AddRange([Tiandu, ClaimWatch]);
            if (scene == "tiandu-relocated") own.AddRange([Hongyan, LoseColor]);
            if (scene == "jueqing") own.Add(Jueqing);
            var targetSkills = scene is "chain" or "jueqing" or "maximum" ? new[] { Lingdong } : [];
            ShanheStartingCard[] cards = scene switch
            {
                "black-red" => [new(CardKind.Dodge, Suit.Heart, 11)],
                "red-black" => [new(CardKind.Dodge, Suit.Spade, 11)],
                "chain" or "chain-origin-only" => [new(CardKind.IronChain, Suit.Club, 6), new(CardKind.IronChain, Suit.Spade, 8),
                    new(CardKind.Alcohol, Suit.Spade, 9), new(CardKind.ThunderSlash, Suit.Spade, 7),
                    new(CardKind.Dodge, Suit.Heart, 11), new(CardKind.Dodge, Suit.Club, 12)],
                "jueqing" or "maximum" => [new(CardKind.Slash, Suit.Spade, 6)], _ => []
            };
            var setup = new ShanheBattleSetup(83, 1, "fixture-lingdong-" + scene, Owner, 12, 0, 0, 0, 3, own, cards, [],
                [new(EnemyA, ExtraSkillIds: targetSkills), new(EnemyB, ExtraSkillIds: targetSkills)],
                [new(Ally, ExtraSkillIds: scene == "chain" ? targetSkills : scene == "chain-origin-only" ? [Lingdong] : [])], new string('c', 64))
            { TacticSkillIds = scene == "maximum" ? [Xiaoming] : [] };
            var game = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 3, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-lingdong", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in new[] { Lingdong, Guicai, Tiandu, Jueqing, Xiaoming, Hongyan }) b.AddSkill(Production.Value.GetSkill(id));
            object Activation(string id, int targets, params object[] effects) => new
            { id, minCards = 0, maxCards = 0, minTargets = targets, maxTargets = targets, targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            object Pause(string bind = "child") => new { op = "chooseOption", target = "owner", resultBind = bind, options = new[] { new { id = "continue" } } };
            var claimEffects = new List<object> { Pause() };
            if (scene == "tiandu-relocated") claimEffects.AddRange([
                new { op = "selectOwnedCards", target = "owner", amount = 1, zones = new[] { "hand" }, resultBind = "claimed-card" },
                new { op = "moveBoundCards", target = "owner", sourceBind = "claimed-card", destination = "discardPile", awaitMovementTriggers = true },
                new { op = "loseHp", target = "owner", amount = 12 }, Pause("after-loss")]);
            var definitions = new List<object>
            {
                new { id = Driver, revision = 1, activations = new[]
                {
                    Activation("incoming", 1, new { op = "damage", target = "owner", sourceRef = new { kind = "selectedTarget" }, amount = 2 }),
                    Activation("source-less", 0, new { op = "receiveOwnerDamage", target = "owner", amount = 2 }),
                    Activation("lose-two", 1, new { op = "loseHp", target = "selectedTarget", amount = 2 }),
                    Activation("armor-one", 1, new { op = "grantArmor", target = "selectedTarget", amount = 1 }),
                    Activation("armor-three", 1, new { op = "grantArmor", target = "selectedTarget", amount = 3 })
                } },
                new { id = Watch, revision = 1, triggers = new[] { new { id = "paid-damage", window = "afterDamageApplied", subject = scene == "chain-origin-only" ? "any" : "owner",
                    damageOccurrence = "perDamage", optional = false, effects = new[] { Pause() } } } },
                new { id = ClaimWatch, revision = 1, triggers = new[] { new { id = "paid-tiandu", window = "cardsGained", subject = "owner",
                    destinationZones = new[] { "hand" }, movementOccurrence = "perBatch", movementReasons = new[] { "skill-program.classic:tiandu.claimJudgmentCard" },
                    optional = false, usageScope = "game", usageLimit = 1, effects = claimEffects.ToArray() } } }
            };
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实灵动测试操作", description = "仅调用原生伤害、护甲和失去体力指令" } };
            if (scene == "tiandu-relocated")
            {
                definitions.Add(new { id = LoseColor, revision = 1, triggers = new[] { new { id = "real-loss-of-hongyan", window = "dyingEntering", subject = "owner",
                    optional = false, usageScope = "game", usageLimit = 1, effects = new object[] {
                        new { op = "recover", target = "owner", amount = 1 },
                        new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { Hongyan }, sourceBind = "standard:none" } } } } });
                labels[LoseColor] = new { name = "真实失去红颜", description = "只在合法濒死窗口回复一点并终止式移除红颜" };
            }
            foreach (var id in new[] { Watch, ClaimWatch }) labels[id] = new { name = "真实已支付子流程", description = "在原生支付完成后暂停",
                optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "真实判定拥有者", "", Driver, "shu", 12));
            foreach (var id in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(id, "实际参战角色", "", "standard:none", "qun", 12));
            b.AddDeck(new(Deck, "固定同色实体判定牌堆", scene == "empty" ? 1 : 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, scene == "empty" ? 4 : 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", scene is "red-black" or "tiandu" or "jueqing" or "maximum" ? Suit.Heart : Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实灵动结算", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
