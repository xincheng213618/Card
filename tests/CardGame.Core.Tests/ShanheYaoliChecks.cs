using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheYaoliChecks
{
    private static readonly string[] Skills = ["shanhe:current-yaoli-2319", "shanhe:current-yaoli-2320", "shanhe:current-yaoli-2321",
        "shanhe:current-yaoli-2322", "shanhe:current-yaoli-2191", "shanhe:current-yaoli-2192"];
    private static readonly string[] Rewards = ["tactic-yaoli-current-2319", "tactic-yaoli-current-2320", "tactic-yaoli-current-2321",
        "tactic-yaoli-current-2322", "tactic-yaoli-current-2191", "tactic-yaoli-current-2192"];
    private static readonly int[] Bonuses = [1, 1, 2, 2, 1, 2];
    private static readonly int?[] Limits = [1, 3, 1, 2, null, null];
    private const string Owner = "fixture:yaoli-owner", Driver = "fixture:yaoli-driver", Deck = "fixture:yaoli-deck";
    private const string Health = "fixture:yaoli-health", Gain = "fixture:yaoli-gain", Gate = "fixture:yaoli-gate";
    private const string Mutation = "fixture:yaoli-mutation", Cost = "fixture:yaoli-cost", Quiet = "fixture:yaoli-quiet";
    private const string Kuanggu = "boundary:kuanggu", Blood = "shanhe:xueren", RecoveryDraw = "shanhe:shixue-1";
    private const string Zaiqi = "shanhe:s5-zaiqi-887", Jiuyuan = "boundary:jiuyuan", Yizheng = "ol:yizheng-cao-song";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new ShanheBattleSetup(83, 1, "fixture:yaoli-content", "classic:guan-yu", 4, 0, 0, 0, 0, [Zaiqi], [], [],
            [new("classic:cao-cao")], [], new string('c', 64)) { TacticSkillIds = Skills }));

    public static void RecoveryCallsStackSixVersionsAndRealPeachesResetAtActualTurnsCold()
    {
        VerifyPolicyLoader();
        VerifyProductionLoadout();
        var s = Scene.Start("stack"); s.Play();
        var firstTurn = s.Turn;
        var peaches = s.V().Hand.Where(card => card.Kind == CardKind.Peach).Select(card => card.Id).ToArray();
        Need(peaches.Length == 2 && s.V().Hp == 1, "Two real initial Peaches and a genuinely wounded owner precede any recovery call.");
        s.BeginPeach(); s.Until(s.AtHealth); CheckPayment(s, 1, 10, 1, 10, Eligible(1)); s.Cold(); s.Play();
        var resets = new[] { 10, 8, 6 };
        for (var ordinal = 2; ordinal <= 4; ordinal++)
        {
            s.Use("lose-" + resets[ordinal - 2]);
            Need(s.V().Hp == 1, "Only real HP-loss commands prepare the next distinct recovery; loss never consumes a recovery ordinal.");
            s.Begin("two"); s.Until(s.AtHealth);
            var modified = 2 + Eligible(ordinal).Sum(id => Bonuses[Array.IndexOf(Skills, id)]);
            CheckPayment(s, 2, modified, ordinal, modified, Eligible(ordinal)); s.Cold(); s.Play();
        }
        Need(s.E<ActualTurnRecoveryRecordedEvent>().Where(fact => fact.TargetSeat == 0 && fact.TurnNumber == firstTurn)
                .Select(fact => fact.Ordinal).SequenceEqual([1, 2, 3, 4]),
            "A ten-HP first recovery counts as one call; distinct versions have independent first/first-two/first-three/unlimited eligibility.");
        s.Use("lose-5"); s.EndPlay(); s.Until(() => s.Turn > firstTurn && s.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        s.BeginPeach(); s.Until(s.AtHealth); CheckPayment(s, 1, 10, 1, 10, Eligible(1)); s.Cold(); s.Play();
        s.Begin("two"); s.Until(s.AtHealth); CheckPayment(s, 2, 8, 2, 8, Eligible(2)); s.Play();
        s.Begin("two"); s.Until(s.AtHealth); CheckPayment(s, 2, 6, 3, 1, Eligible(3)); s.Cold(); s.Play();
        var positive = s.E<ActualTurnRecoveryRecordedEvent>().Length;
        var applied = s.E<RecoveryAppliedEvent>().Length; var bills = s.E<RecoveryAmountBonusAppliedEvent>().Length;
        s.Use("one");
        Need(s.V().Hp == 20 && s.E<ActualTurnRecoveryRecordedEvent>().Length == positive &&
            s.E<RecoveryAppliedEvent>().Length == applied && s.E<RecoveryAmountBonusAppliedEvent>().Length == bills,
            "A full-health recovery does not pay a bonus, issue a positive recovery fact or consume a new ordinal.");
        s.Use("lose-1"); s.Begin("one"); s.Until(s.AtHealth); CheckPayment(s, 1, 4, 4, 1, Eligible(4)); s.Cold(); s.Play();
        Need(peaches.All(id => s.G.CardMovements.Count(move => move.CardId == id && move.From == CardLocation.Hand(0) &&
                move.To == CardLocation.Processing) == 1 && s.G.CardMovements.Count(move => move.CardId == id &&
                move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1),
            "Both native Peach uses keep their exact original entity payments and cleanup once across cold recovery children.");
        s.PureViews(); s.Cold();
    }

    public static void LiveSourcesAndPaidChildrenPreserveEligibilityAndZeroRecoveryCold()
    {
        foreach (var scene in new[] { "startup-limited", "startup-unlimited", "ended-limited", "ended-unlimited" })
        {
            var s = Scene.Start(scene);
            if (scene.StartsWith("ended-", StringComparison.Ordinal)) { s.Play(); s.EndPlay(); }
            s.Until(s.AtHealth);
            var unlimited = scene.EndsWith("unlimited", StringComparison.Ordinal);
            Need(s.V().Hp == (unlimited ? 4 : 2) && s.E<ActualTurnRecoveryRecordedEvent>().Length == 0 &&
                s.E<RecoveryAppliedEvent>() is [{ Amount: var actual }] && actual == (unlimited ? 3 : 1),
                "Only unlimited policies modify a real startup or already-ended-turn recovery; neither creates an active-turn ordinal: " + s.Diagnostic());
            if (unlimited)
            {
                var bill = s.E<RecoveryAmountBonusAppliedEvent>().Single().Receipt;
                Need(bill.ActualTurnRecoveryOrdinal is null && bill.BaseRequestedAmount == 1 && bill.ModifiedRequestedAmount == 3 &&
                    bill.Sources.Select(source => source.Source.SkillId).SequenceEqual([Skills[5]]),
                    "The unlimited startup/ended payment freezes its one live source without inventing an active turn.");
            }
            else Need(s.E<RecoveryAmountBonusAppliedEvent>().Length == 0, "A limited policy cannot manufacture a turn-zero/ended bonus.");
            s.Cold();
        }

        var late = Scene.Start("late"); late.Play(); late.Begin("two"); late.Until(late.AtHealth);
        Need(late.E<ActualTurnRecoveryRecordedEvent>().Single() is { Ordinal: 1, ActualAmount: 2 } &&
            late.E<RecoveryAmountBonusAppliedEvent>().Length == 0, "The first real two-HP recovery counts even before the owner gains any medicine.");
        late.Play(); late.Use("grant-second");
        for (var ordinal = 2; ordinal <= 4; ordinal++)
        {
            late.Begin("one"); late.Until(late.AtHealth);
            if (ordinal <= 3) CheckPayment(late, 1, 2, ordinal, 2, [Skills[1]]);
            else Need(late.E<ActualTurnRecoveryRecordedEvent>().Last().Ordinal == 4 &&
                late.E<ActualTurnRecoveryRecordedEvent>().Last().ActualAmount == 1 && late.E<RecoveryAmountBonusAppliedEvent>().Length == 2,
                "Late acquisition uses the recipient's genuine prior recoveries instead of a new per-skill counter.");
            late.Cold(); late.Play();
        }

        var boundary = Scene.Start("source-boundary"); boundary.Play(); boundary.Use("lose-2");
        Need(!boundary.Has(Skills[1]) && boundary.V().Hp == 4, "A real HP-conditioned source is withdrawn before the next payment.");
        boundary.Begin("one"); boundary.Until(boundary.AtHealth);
        Need(boundary.Has(Skills[1]) && boundary.E<RecoveryAmountBonusAppliedEvent>().Length == 0 &&
            boundary.E<ActualTurnRecoveryRecordedEvent>().Single() is { Ordinal: 1, ActualAmount: 1 },
            "Gaining the source in the recovery's HP child cannot retroactively enhance its already-paid amount.");
        boundary.Cold(); boundary.Play(); boundary.Begin("one"); boundary.Until(boundary.AtHealth);
        CheckPayment(boundary, 1, 2, 2, 2, [Skills[1]]); boundary.Cold(); boundary.Play();

        foreach (var scene in new[] { "paid-loss", "paid-death" })
        {
            var s = Scene.Start(scene); s.Play(); s.Begin("one"); s.Until(() => s.AtMutation("paid"));
            CheckPayment(s, 1, 2, 1, 2, [Skills[1]]);
            var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Driver).Id;
            var billId = s.E<RecoveryAmountBonusAppliedEvent>().Single().RecoveryFrameId;
            s.Cold(); s.Continue();
            if (scene == "paid-loss")
            {
                s.Until(() => s.AtMutation("mutated"));
                Need(s.V().Hp == 2 && !s.Has(Skills[1]), "An actual recovery child loses three HP and withdraws its medicine source after payment.");
                s.Cold(); s.Play();
            }
            else
            {
                s.Until(() => !s.V().IsAlive && s.E<ProgramSkillResolvedEvent>().Any(fact => fact.FrameId == root));
                Need(s.G.State.Winner == Winner.None && s.V(1).IsAlive && s.E<PlayerDiedEvent>().Count(fact => fact.VictimSeat == 0) == 1 &&
                    !s.G.ResolutionStack.Any(frame => frame.Id == root),
                    "Actual dying/let-die returns the paid recovery producer with an ally still alive; game-over cleanup cannot conceal its continuation.");
            }
            Need(s.E<RecoveryAppliedEvent>() is [{ Amount: 2 }] && s.E<RecoveryAmountBonusAppliedEvent>() is [var bill] &&
                bill.RecoveryFrameId == billId && bill.Receipt.ModifiedRequestedAmount == 2 &&
                s.E<ActualTurnRecoveryRecordedEvent>() is [{ Ordinal: 1, RequestedAmount: 2, ActualAmount: 2 }] &&
                s.E<ProgramSkillResolvedEvent>().Where(fact => fact.FrameId == root).ToArray() is [var resolved] &&
                resolved is { OwnerSeat: 0, SkillId: Driver, ActivationId: "one" } && resolved.Completed == (scene == "paid-loss") &&
                !s.G.ResolutionStack.Any(frame => frame.Id == root),
                "Losing or dying in a true paid child neither refunds nor repeats the one frozen application: " + s.Diagnostic());
            s.Cold();
        }
    }

    public static void ZaiqiKuangguBloodBladeAndRecoveryDrawUseOneModifiedPaymentCold()
    {
        var s = Scene.Start("reaction"); s.Play(); s.Begin("one"); s.Play();
        Need(s.E<RecoveryAppliedEvent>().Select(fact => fact.Amount).SequenceEqual([3, 3, 2]) && s.V().Hp == 12 &&
            s.E<RecoveryAmountBonusAppliedEvent>().Select(fact => fact.Receipt.ModifiedRequestedAmount).SequenceEqual([3, 3, 3]) &&
            s.E<ActualTurnRecoveryRecordedEvent>().Select(fact => fact.Ordinal).SequenceEqual([1, 2, 3]) &&
            s.E<DamageAppliedEvent>().Length == 3 && s.E<DamageAppliedEvent>().All(fact => fact is { SourceSeat: 0, TargetSeat: 1, Amount: 1 }) &&
            s.E<ProgramBindingResolvedEvent>().Count(fact => fact.SkillId == RecoveryDraw && fact.Completed) == 3 && s.V().HandCount == 3 && s.ColdPaidChildren >= 3,
            "One boosted primitive recovery starts three real recovery events, three blood-blade damages and three recovery draws; boundary Kuanggu's recover option ends at full health: " + s.Diagnostic());
        var oldRecoveries = s.E<RecoveryAppliedEvent>().Length;
        s.Begin("damage", [1]); s.Until(() => s.Optional(Kuanggu)); s.Answer(choice => Action(choice) == "activate");
        s.Until(() => s.P?.SkillPrompt?.SkillId == Kuanggu && s.P.Choices.Any(choice => Option(choice) == "draw"));
        Need(s.P!.Choices.All(choice => Option(choice) != "recover"), "At full health the actual S5-compatible Kuanggu benefit offers draw, not an impossible recovery.");
        s.Cold(); s.Answer(choice => Option(choice) == "draw"); s.Play();
        Need(s.V().HandCount == 4 && s.E<RecoveryAppliedEvent>().Length == oldRecoveries &&
            s.E<ActualTurnRecoveryRecordedEvent>().Length == 3 && s.E<DamageAppliedEvent>().Length == 4 &&
            s.E<ProgramBindingResolvedEvent>().Count(fact => fact.SkillId == RecoveryDraw && fact.Completed) == 3,
            "Choosing the real boundary Kuanggu draw branch is not a recovery, cannot consume medicine and cannot fire blood blade or recovery-draw again.");
        s.Cold();

        var zaiqi = Scene.Start("zaiqi"); zaiqi.Until(() => zaiqi.Optional(Zaiqi)); zaiqi.Answer(choice => Action(choice) == "activate");
        zaiqi.Until(zaiqi.AtHealth);
        var reveal = zaiqi.E<ProgramCardsRevealedEvent>().Single(fact => fact.SkillId == Zaiqi);
        Need(reveal.Cards.Count == 9 && reveal.Cards.All(card => card.Suit == Suit.Heart) &&
            zaiqi.E<RecoveryAppliedEvent>() is [{ Amount: 8, RemainingHp: 12 }],
            "Real 887 reveals lost HP plus one hearts; the single requested nine-point recovery is modified before one maximum-HP clamp.");
        CheckPayment(zaiqi, 9, 11, 1, 8, [Skills[5]]); zaiqi.Cold(); zaiqi.Play();
        var ids = reveal.Cards.Select(card => card.Id).ToArray();
        Need(ids.All(id => zaiqi.G.CardMovements.Count(move => move.CardId == id && move.From == CardLocation.Processing &&
                move.To == CardLocation.DiscardPile && move.Reason.Value == "skill-program." + Zaiqi + ".MoveBoundCards") == 1) &&
            zaiqi.E<RecoveryAppliedEvent>().Length == 1 && zaiqi.E<DamageAppliedEvent>().Length == 1 &&
            zaiqi.E<ProgramBindingResolvedEvent>().Count(fact => fact.SkillId == RecoveryDraw && fact.Completed) == 1 && zaiqi.V().HandCount == 1,
            "The single native 887 payment returns its real recovery/gain children, discards every original heart once and triggers recovery-draw/blood-blade once, not per recovered HP.");
        zaiqi.Cold();
    }

    public static void RedirectedRecoveryAndYizhengSupportApplyFinalRecipientPoliciesOnceCold()
    {
        var redirect = Scene.Start("redirect"); redirect.Play(); redirect.Begin("one");
        redirect.Until(() => redirect.P?.Kind == DecisionKind.RecoveryReplacement);
        Need(redirect.P!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("recovery-action") == "redirect" && choice.Targets.SequenceEqual([1])),
            "The actual printed Lord-qualified boundary Jiuyuan publishes a real optional recovery destination to the human recovering actor.");
        redirect.Cold(); redirect.Answer(choice => choice.Parameters.GetValueOrDefault("recovery-action") == "redirect" && choice.Targets.SequenceEqual([1]));
        redirect.Until(() => redirect.P?.SkillPrompt?.SkillId == Gain);
        var bill = redirect.E<RecoveryAmountBonusAppliedEvent>().Single();
        Need(redirect.E<RecoveryReplacementChosenEvent>() is [{ RecoveringSeat: 0, BeneficiarySeat: 1 }] &&
            bill.Receipt is { SourceSeat: 0, TargetSeat: 1, BaseRequestedAmount: 1, ModifiedRequestedAmount: 4, HpBefore: 1, HpAfter: 5,
                ActualTurnRecoveryOrdinal: 1 } && bill.Receipt.Sources.Select(source => source.Source.SkillId).Order().SequenceEqual(new[] { Skills[1], Skills[5] }.Order()) &&
            bill.Receipt.Sources.All(source => source.Source.OwnerSeat == 1) &&
            redirect.V().Hp == 10 && redirect.V(1).Hp == 5 && redirect.V().HandCount == 1 &&
            redirect.E<ActualTurnRecoveryRecordedEvent>() is [{ TargetSeat: 1, TurnOwnerSeat: 0, Ordinal: 1, ActualAmount: 4 }] &&
            redirect.G.ResolutionStack.OfType<RecoveryReplacementFrame>().Any(frame => frame.Id == bill.RecoveryFrameId && frame.AmountBonus is not null),
            "The off-turn beneficiary's limited II and unlimited VI apply together, excluding the original actor's I; the genuine provider-draw child retains its already-paid replacement receipt.");
        redirect.Cold(); redirect.Play();
        Need(redirect.E<RecoveryAppliedEvent>().Length == 1 && redirect.G.CardMovements.Count(move => move.To == CardLocation.Hand(0) &&
            move.Reason.Value == "skill-program.recovery-replacement.reward") == 1, "Cold replacement reward returns neither recover nor pay the provider twice.");

        var support = Scene.Start("support"); support.Play(); support.EndPlay(); support.Until(() => support.Optional(Yizheng));
        support.Answer(choice => Action(choice) == "activate");
        support.Until(() => support.P?.SkillPrompt?.SkillId == Yizheng && support.P.Choices.Any(choice => choice.Targets.SequenceEqual([1])));
        support.Cold(); support.Answer(choice => choice.Targets.SequenceEqual([1]));
        support.Until(() => support.P?.SkillPrompt?.SkillId == Driver && support.P.Choices.Any(choice => choice.Targets.SequenceEqual([1])));
        support.Answer(choice => choice.Targets.SequenceEqual([1])); support.Until(() => support.P?.SkillPrompt?.SkillId == Cost);
        Need(support.E<CaoSongYizhengGrantedEvent>().Single().RecipientSeat == 1 &&
            support.E<CaoSongYizhengAmountPaidEvent>().Length == 1 && support.V().MaxHp == 19 && support.V(1).Hp == 1 &&
            support.E<RecoveryAmountBonusAppliedEvent>().Length == 0 && support.E<RecoveryAppliedEvent>().Length == 0,
            "Real native Yizheng pays its source's maximum HP and pauses in the actual health child before final medicine or HP application.");
        support.Cold(); support.Continue(); support.Until(() => support.AtDriver("support-tail"));
        var paid = support.E<RecoveryAmountBonusAppliedEvent>().Single();
        Need(support.E<CaoSongYizhengAmountIncreasedEvent>().Single().Recovery &&
            paid.Receipt is { TargetSeat: 1, BaseRequestedAmount: 2, ModifiedRequestedAmount: 4, HpBefore: 1, HpAfter: 5, ActualTurnRecoveryOrdinal: 1 } &&
            paid.Receipt.Sources.Select(source => source.Source.SkillId).SequenceEqual([Skills[5]]) &&
            support.E<RecoveryAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 1, Amount: 4, RemainingHp: 5 }] &&
            support.E<CaoSongYizhengAmountPaidEvent>().Length == 1,
            "Native support increases the frozen base first, then the finalized beneficiary's live medicine is added and clamped in the same sole recovery.");
        support.Cold(); support.Continue(); support.Until(() => !support.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Driver));
        Need(support.E<RecoveryAppliedEvent>().Length == 1 && support.E<RecoveryAmountBonusAppliedEvent>().Length == 1 &&
            support.E<CaoSongYizhengAmountPaidEvent>().Length == 1, "Returning from both genuine native support and paid medicine children does not repeat either cost or recovery.");
        support.Cold();
    }

    private static string[] Eligible(int ordinal) => Skills.Where((_, index) => Limits[index] is not { } limit || ordinal <= limit).ToArray();
    private static string Peer(int seat) => "fixture:yaoli-peer-" + seat;
    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice choice) => choice.Parameters.GetValueOrDefault("option-id");
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void CheckPayment(Scene s, int requested, int modified, int ordinal, int actual, IReadOnlyList<string> sources)
    {
        var fact = s.E<ActualTurnRecoveryRecordedEvent>().Last(entry => entry.TargetSeat == 0);
        var paid = s.E<RecoveryAmountBonusAppliedEvent>().Single(entry => entry.RecoveryFrameId == fact.ParentFrameId);
        var receipt = paid.Receipt;
        Need(fact.Ordinal == ordinal && fact.RequestedAmount == actual && fact.ActualAmount == actual && fact.TurnNumber == s.Turn &&
            fact.TurnOwnerSeat == 0 && receipt.BaseRequestedAmount == requested && receipt.ModifiedRequestedAmount == modified &&
            receipt.ActualTurnRecoveryOrdinal == ordinal && receipt.TargetSeat == 0 && receipt.HpAfter - receipt.HpBefore == actual &&
            receipt.AppliedRecoveryFrameId == fact.RecoveryFrameId && receipt.MaxHpAtApplication >= receipt.HpAfter &&
            receipt.Sources.Select(entry => entry.Source.SkillId).Order().SequenceEqual(sources.Order()) &&
            receipt.Sources.All(entry => entry.GameplayHash.Length == 64 && !string.IsNullOrEmpty(entry.Source.SkillInstanceId)) &&
            s.G.ResolutionStack.OfType<RecoveryReplacementFrame>().Any(frame => frame.Id == paid.RecoveryFrameId &&
                JsonSerializer.Serialize(frame.AmountBonus) == JsonSerializer.Serialize(receipt)),
            "One live-source bonus receipt is frozen on its exact recovery owner and equals the genuine actual-turn payment: " + s.Diagnostic());
    }

    private static void VerifyPolicyLoader()
    {
        string Read(string suffix)
        { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.shanhe-current-yaoli." + suffix)!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var original = JsonNode.Parse(Read("rules.json"))!; var presentation = Read("presentation.json");
        JsonObject Policy(JsonNode root) => root["skills"]![0]!["cardPolicies"]![0]!.AsObject();
        void Load(JsonNode node) => _ = SkillProgramCatalog.Load(node.ToJsonString(), presentation);
        Load(original);
        foreach (var mutate in new Action<JsonObject>[]
        {
            policy => policy["value"] = 0, policy => policy["value"] = 21,
            policy => policy["maximumRecoveryOrdinal"] = 0, policy => policy["maximumRecoveryOrdinal"] = -1,
            policy => policy["cardKinds"] = new JsonArray("peach"), policy => policy["requiredCardKinds"] = new JsonArray("peach"),
            policy => policy["condition"] = JsonNode.Parse("{\"kind\":\"wounded\"}")
        })
        {
            var copy = original.DeepClone(); mutate(Policy(copy)); var rejected = false;
            try { Load(copy); } catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException) { rejected = true; }
            Need(rejected, "The actual recovery-policy loader rejects an invalid value/limit/filter/condition rather than broadening its contract.");
        }
        var unrelated = original.DeepClone(); var other = Policy(unrelated); other["kind"] = "outgoingDistanceOne"; other["value"] = 0; other.Remove("maximumRecoveryOrdinal");
        Load(unrelated); other["maximumRecoveryOrdinal"] = 1; var wrongKind = false;
        try { Load(unrelated); } catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException) { wrongKind = true; }
        Need(wrongKind, "A known otherwise-valid distance policy cannot acquire a recovery-ordinal field; its positive control has loaded first.");
    }

    private static void VerifyProductionLoadout()
    {
        var current = StandardShanheCampaignCatalog.CreateCodexDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        var old = StandardShanheCampaignCatalog.CreateCodexDefinition(1, StandardShanheCampaignCatalog.LocalEditionId);
        var defaults = StandardShanheCampaignCatalog.GetDefaultCodexUnlockedRewardIds(current);
        Need(Rewards.Where((_, index) => index != 3).All(defaults.Contains) && !defaults.Contains(Rewards[3]) &&
            StandardShanheCampaignCatalog.CodexRewardIdsByNodeId[117] == Rewards[3] &&
            old.Rewards.All(reward => !Rewards.Contains(reward.Id)) &&
            Rewards.Select((id, index) => current.Rewards.Single(reward => reward.Id == id).TacticSkillId == Skills[index]).All(value => value),
            "All six source-backed versions are independent current rewards; only IV belongs to node 117, the five other default entries are availability rather than free grants, and 2024 is isolated.");
        var run = ShanheCampaignSession.Create(current, new(83, current.PlayerGeneralIds[0])
            { UseCodexRewardPool = true, UnlockedRewardIds = defaults, EquippedTacticIds = [Rewards[0]] }, StandardShanheCampaignCatalog.CreateBattleRegistry);
        for (var step = 0; step < 6 && run.Snapshot.Phase != ShanheCampaignPhase.Battle; step++)
        {
            ShanheCampaignCommand command = run.Snapshot.Phase switch
            {
                ShanheCampaignPhase.Map => new MoveShanheNodeCommand(run.Snapshot.AvailableNodes[0].Id, run.Revision),
                ShanheCampaignPhase.NodeChoice => new ChooseShanheNodeOptionCommand(run.Snapshot.NodeChoices.First(choice => choice.RewardId == "supplies").Id, null, run.Revision),
                _ => throw new InvalidOperationException("Unexpected equipped medicine route.")
            };
            var result = run.Submit(command); Need(result.Accepted, result.Error ?? "Equipped medicine command rejected.");
            var before = JsonSerializer.Serialize(run.Snapshot);
            run = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(run.CreateCheckpoint())), current, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(before == JsonSerializer.Serialize(run.Snapshot), "A real campaign cold-restores its frozen equipped medicine while traversing its opening event and route.");
        }
        var setup = run.PendingBattleSetup ?? throw new InvalidOperationException("The equipped medicine run did not issue a real first battle.");
        Need(setup.TacticSkillIds.SequenceEqual([Skills[0]]) && run.Snapshot.ExtraSkillIds.Count == 0,
            "Only genuinely equipped medicine reaches the actual battle recipe; the registry-only all-reward probe cannot grant the five unearned variants or occupy an extra-skill slot.");
        var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup); var battle = StandardShanheCampaignCatalog.CreateBattle(setup);
        var started = battle.Submit(new StartGameCommand()); Need(started.Accepted, started.Error?.Message ?? "Actual medicine encounter rejected.");
        Need(battle.CreateSnapshot(0).Players[0].Skills?.Count(skill => Skills.Contains(skill.ContentId)) == 1,
            "The real production encounter creates exactly its one equipped medicine source.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(battle.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, battle.PlayerCount).All(viewer => SnapshotJson.Serialize(battle.CreateSnapshot(viewer)) == SnapshotJson.Serialize(restored.CreateSnapshot(viewer))),
            "The actual earned production tactic, startup and all private views survive native cold replay.");
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public int ColdPaidChildren { get; private set; }
        public int Turn => G.State.TurnNumber;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(0).Players[seat];
        public T[] E<T>() => G.Events.Select(fact => fact.Payload).OfType<T>().ToArray();
        public bool Has(string id) => V().Skills?.Any(skill => skill.ContentId == id) == true;
        public bool AtHealth() => P?.SkillPrompt?.SkillId == Health;
        public bool AtMutation(string option) => P?.SkillPrompt?.SkillId == Mutation && P.Choices.Any(choice => Option(choice) == option);
        public bool AtDriver(string option) => P?.SkillPrompt?.SkillId == Driver && P.Choices.Any(choice => Option(choice) == option);
        public bool Optional(string id) => P?.SkillPrompt?.SkillId == id && P.Choices.Any(choice => Action(choice) == "activate");
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, (result.Error?.Message ?? "Actual medicine command rejected.") + " " + Diagnostic());
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var prompt = P ?? throw new InvalidOperationException("Missing actual medicine prompt."); Accept(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(predicate).Id, G.Revision)); }
        public void Continue() => Answer(choice => Option(choice) is "continue" or "tail" or "paid" or "mutated" or "outside" or "support-tail");
        public void Begin(string activation, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], G.Revision, P!.PromptId));
        public void Use(string activation) { Begin(activation); Play(); }
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void BeginPeach()
        {
            var card = V().Hand.First(card => card.Kind == CardKind.Peach);
            var action = G.GetHumanLegalActions().First(action => action.CardId == card.Id && action.PlayedCardKind is null or CardKind.Peach);
            Accept(new PlayCardCommand(0, card.Id, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void Play() => Until(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Until(Func<bool> stop)
        {
            for (var step = 0; step < 400; step++)
            {
                if (stop()) return;
                if (P is { PlayerSeat: 0 } prompt)
                {
                    if (prompt.SkillPrompt?.SkillId is Health or Gain or Mutation or Cost || AtDriver("tail") || AtDriver("outside"))
                    { if (AtHealth()) { Cold(); ColdPaidChildren++; } Continue(); continue; }
                    if (Optional(Kuanggu)) { Answer(choice => Action(choice) == (V().Hp < V().MaxHp ? "activate" : "skip")); continue; }
                    if (prompt.SkillPrompt?.SkillId == Kuanggu && prompt.Choices.Any(choice => Option(choice) == "recover"))
                    { Answer(choice => Option(choice) == "recover"); continue; }
                    if (prompt.Kind == DecisionKind.RescueDying) { Answer(choice => choice.Parameters.GetValueOrDefault("response") == "let-die"); continue; }
                    throw new InvalidOperationException("Unexpected actual medicine boundary: " + Diagnostic());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Bounded actual medicine boundary not reached: " + Diagnostic());
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, G.PlayerCount).Select(viewer => SnapshotJson.Serialize(G.CreateSnapshot(viewer))),
            Frames = G.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
            Facts = G.Events.Select(fact => JsonSerializer.Serialize(fact.Payload, fact.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands)
        });
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves frozen medicine sources/amount/ordinal, typed recovery ownership, exact payments, children and private views."); }
        public void PureViews()
        {
            var before = State();
            for (var repeat = 0; repeat < 2; repeat++) for (var viewer = 0; viewer < G.PlayerCount; viewer++)
                Need(G.CreateSnapshot(viewer).Players.Where(player => player.Seat != viewer).All(player => player.Hand.Count == 0), "Medicine facts do not reveal another seat's private card identities.");
            Need(before == State(), "Reading medicine-bearing snapshots cannot advance or repay any recovery.");
        }
        public string Diagnostic() => JsonSerializer.Serialize(new
        {
            scene, Turn, V().Hp, V().MaxHp, V().IsAlive,
            Prompt = P is null ? null : new { P.Kind, Skill = P.SkillPrompt?.SkillId, ChoiceIds = P.Choices.Select(choice => choice.Id.Value) },
            Recovery = E<ActualTurnRecoveryRecordedEvent>(), Bills = E<RecoveryAmountBonusAppliedEvent>().Select(fact => new
                { fact.RecoveryFrameId, fact.Receipt.TargetSeat, fact.Receipt.BaseRequestedAmount, fact.Receipt.ModifiedRequestedAmount,
                    fact.Receipt.ActualTurnRecoveryOrdinal, Sources = fact.Receipt.Sources.Select(source => source.Source.SkillId) }),
            Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind }),
            DriverResults = E<ProgramSkillResolvedEvent>().Where(fact => fact.SkillId == Driver)
                .Select(fact => new { fact.FrameId, fact.ActivationId, fact.Completed })
        });
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var count = scene is "reaction" or "zaiqi" or "redirect" ? 2 : 4;
            var allied = scene is "paid-death" or "support";
            var hp = scene switch { "reaction" or "zaiqi" => 4, "source-boundary" => 6, "paid-loss" or "paid-death" => 3, "redirect" => 10, "support" => 20, _ => 1 };
            var allies = allied ? new ShanheCombatantDefinition[] { new(Peer(1)) } : [];
            var enemies = Enumerable.Range(allied ? 2 : 1, allied ? 2 : count - 1).Select(seat => new ShanheCombatantDefinition(Peer(seat))
            { QualifiedPrintedLordSkillIds = scene == "redirect" && seat == 1 ? [Jiuyuan] : null }).ToArray();
            ShanheStartingCard[] cards = scene == "stack" ? [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Peach, Suit.Heart, 4)] : [];
            var setup = new ShanheBattleSetup(83, 1, "fixture:yaoli-" + scene, Owner, hp, 0, 0, 0, 0, [], cards, [], enemies, allies, new string('b', 64));
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = count, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var result = new Scene(game, registry, scene); result.Accept(new StartGameCommand()); return result;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-yaoli", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in Skills.Concat(new[] { Kuanggu, Blood, RecoveryDraw, Zaiqi, Jiuyuan, Yizheng })) b.AddSkill(Production.Value.GetSkill(id));
            var definitions = new List<Dictionary<string, object>>(); var labels = new Dictionary<string, object>();
            object Pause(string option) => new { op = "chooseOption", target = "owner", resultBind = option, options = new[] { new { id = option } } };
            object Effect(string op, int amount) => new { op, target = "owner", amount };
            object Activation(string id, params object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = 0,
                maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            void Add(string id, Dictionary<string, object> body, params string[] options)
            {
                body["id"] = id; body["revision"] = 1; definitions.Add(body);
                labels[id] = new { name = id, description = "真实恢复、来源变化与父帧归返，无伪造状态或付款事实", optionLabels = options.ToDictionary(option => option, option => option) };
            }
            var activations = new List<object> { Activation("one", Effect("recover", 1), Pause("tail")), Activation("two", Effect("recover", 2), Pause("tail")),
                Activation("grant-second", new { op = "grantSkills", target = "owner", skillIds = new[] { Skills[1] } }),
                new { id = "damage", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null,
                    effects = new object[] { new { op = "damage", target = "selectedTarget", amount = 1 }, Pause("tail") } } };
            foreach (var amount in new[] { 1, 2, 3, 5, 6, 8, 10 }) activations.Add(Activation("lose-" + amount, Effect("loseHp", amount)));
            var driver = new Dictionary<string, object> { ["activations"] = activations };
            if (scene.StartsWith("startup-", StringComparison.Ordinal) || scene.StartsWith("ended-", StringComparison.Ordinal))
                driver["triggers"] = new[] { new { id = "outside-real-turn", window = scene.StartsWith("startup-", StringComparison.Ordinal) ? "gameStarting" : "afterTurnEnded",
                    subject = "owner", optional = false, usageScope = "game", usageLimit = 1, effects = new[] { Effect("recover", 1) } } };
            if (scene == "support") driver["triggers"] = new[] { new { id = "after-real-yizheng", window = "turnEnding", subject = "owner", turnOwnerScope = "own",
                optional = false, priority = -100, usageScope = "game", usageLimit = 1,
                effects = new object[] { new { op = "selectTarget", target = "owner", targetKind = "otherLiving" }, new { op = "recover", target = "selectedTarget", amount = 1 }, Pause("support-tail") } } };
            Add(Driver, driver, scene == "support" ? ["tail", "support-tail"] : ["tail"]);
            Add(Health, new() { ["triggers"] = new[] { new { id = "real-positive-recovery", window = "afterHpRecovered", subject = "owner", hpChangeOccurrence = "perEvent",
                optional = false, priority = 100, effects = new[] { Pause("continue") } } } }, "continue");
            Add(Gain, new() { ["triggers"] = new[] { new { id = "real-native-draw-child", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" },
                movementOccurrence = "perBatch", movementReasons = new[] { "skill-program.recovery-replacement.reward", "skill-program." + RecoveryDraw + ".Draw", "skill-program." + Kuanggu + ".Draw" },
                optional = false, effects = new[] { Pause("continue") } } } }, "continue");
            Add(Cost, new() { ["triggers"] = new[] { new { id = "real-support-cost-child", window = "afterHealthChanged", subject = "owner", optional = false,
                effects = new[] { Pause("continue") } } } }, "continue");
            if (scene is "source-boundary" or "paid-loss" or "paid-death") Add(Gate, new() { ["hpSkillGrants"] = new[]
                { new { id = "real-hp-dependent-medicine", skillId = Skills[1], minimumHp = scene == "source-boundary" ? 5 : 3 } } });
            if (scene is "paid-loss" or "paid-death")
            {
                var effects = new List<object> { Pause("paid"), Effect("loseHp", scene == "paid-loss" ? 3 : 20) };
                if (scene == "paid-loss") effects.Add(Pause("mutated"));
                Add(Mutation, new() { ["triggers"] = new[] { new { id = "real-paid-child", window = "afterHpRecovered", subject = "owner", hpChangeOccurrence = "perEvent",
                    optional = false, priority = 200, usageScope = "game", usageLimit = 1, effects = effects.ToArray() } } }, scene == "paid-loss" ? ["paid", "mutated"] : ["paid"]);
            }
            Add(Quiet, new() { ["triggers"] = new[] { new { id = "quiet-real-turn", window = "afterNormalDraw", subject = "owner", optional = false,
                effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string> { Health, Gain };
            if (scene == "stack") own.AddRange(Skills);
            if (scene.EndsWith("limited", StringComparison.Ordinal)) own.Add(scene.EndsWith("unlimited", StringComparison.Ordinal) ? Skills[5] : Skills[0]);
            if (scene is "source-boundary" or "paid-loss" or "paid-death") own.Add(Gate);
            if (scene is "paid-loss" or "paid-death") own.Add(Mutation);
            if (scene is "reaction" or "zaiqi") own.AddRange([Skills[5], Blood, RecoveryDraw]);
            if (scene == "reaction") own.Add(Kuanggu);
            if (scene == "zaiqi") own.Add(Zaiqi);
            if (scene == "redirect") own.Add(Skills[0]);
            if (scene == "support") own.AddRange([Yizheng, Cost]);
            var maxHp = scene is "reaction" or "zaiqi" ? 12 : 20;
            b.AddGeneral(new(Owner, "药理真实支付者", "", Driver, "wu", maxHp, own));
            var count = scene is "reaction" or "zaiqi" or "redirect" ? 2 : 4;
            for (var seat = 1; seat < count; seat++) b.AddGeneral(new(Peer(seat), "药理真实同行或敌军", "", scene == "redirect" && seat == 1 ? Jiuyuan : Quiet,
                "wu", scene == "support" ? 12 : 20, scene == "redirect" && seat == 1 ? [Skills[1], Skills[5]] : scene == "support" && seat == 1 ? [Skills[5]] : [])
                { InitialHp = (scene is "redirect" or "support") && seat == 1 ? 1 : scene == "support" ? 12 : 20 });
            b.AddDeck(new(Deck, "固定实体牌堆", 0, 0, [])
                { PhysicalCards = Enumerable.Range(0, 112).Select(_ => new ContentDeckPhysicalCard("standard:dodge", scene == "zaiqi" ? Suit.Heart : Suit.Spade, 7)).ToArray() });
            var allied = scene is "paid-death" or "support";
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "药理真实恢复", count, count, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: Enumerable.Range(1, count - 1).Select(Peer).Prepend(Owner).ToArray(), ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = allied ? 2 : 1, [ShanheBattleSetup.EnemyTeamId] = allied ? 2 : count - 1 }));
        }
    }
}
