using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheHuangkongChecks
{
    private const string Huangkong = StandardShanheS5BossPackage.HuangkongId;
    private const string Tuxi = StandardShanheS5BossPackage.TuxiId;
    private const string Duipai = "shanhe:duipai-2026", Hongde = "ol:hongde";
    private const string Owner = "fixture:huangkong-owner", Ally = "fixture:huangkong-ally";
    private const string EnemyA = "fixture:huangkong-enemy-a", EnemyB = "fixture:huangkong-enemy-b";
    private const string Driver = "fixture:huangkong-driver", EndWatch = "fixture:huangkong-discard-end";
    private const string GainWatch = "fixture:huangkong-paid-gain", Restrict = "fixture:huangkong-restrict";
    private const string LoseHuangkong = "fixture:huangkong-real-dying-loss";
    private const string Skip = "fixture:huangkong-skip", Quiet = "fixture:huangkong-quiet", Deck = "fixture:huangkong-deck";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture-huangkong-production", StandardShanheS5BossPackage.ZhangliaoHuangkongId,
            4, 0, 0, 0, 0, [], [], [], [new("standard:cao-cao")], [], new string('b', 64))));

    public static void PlannedDrawDoublesAfterYingziAndTeamAuraBeforeRealTuxiCold()
    {
        VerifyLoaderBoundaries();
        foreach (var yingzi in new[] { "boundary:yingzi", "classic:yingzi", "standard:yingzi" })
        {
            var s = Scene.Start(new("plan-" + yingzi) { BaseDraw = 2, Skills = [Huangkong, Tuxi, yingzi], AllySkills = [Duipai] });
            s.AtTuxi();
            var window = s.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>()
                .Single(f => f.Window == SkillProgramTriggerWindow.DrawPhaseStarting);
            Need(window.FrozenBaseDrawCount + window.NormalDrawAdjustment == 10 && !window.NormalDrawReplaced &&
                s.Events<ProgramNormalDrawAdjustedEvent>().Single(e => e.SkillId == Huangkong).Adjustment == 5 &&
                s.V().HandCount == 0, "Base two, the real teammate's two and the real Yingzi's one form five planned draws; Huangkong doubles the plan before any physical draw or Tuxi payment.");
            s.Cold(); s.Activate(); s.Reach(() => s.P?.Choices.Any(c => Action(c) == "select-targets") == true);
            Need(s.P!.Choices.Any(c => c.Targets.SequenceEqual([1, 2, 3])), "The actual ten-card plan admits the native three-target S5 Tuxi choice.");
            s.Cold(); s.Answer(c => c.Targets.SequenceEqual([1, 2, 3])); s.Play();
            AssertTuxiPayment(s, 7, 3);
            Need(s.Events<ProgramNormalDrawAdjustedEvent>().Single(e => e.SkillId == Tuxi).Adjustment == -3 && s.V().HandCount == 10,
                "Each actual one-card theft consumes one normal draw after doubling; seven real deck draws plus three original transferred entities give ten cards.");
            s.Cold();
        }

        var reduced = Scene.Start(new("classic-luoyi") { BaseDraw = 2,
            Skills = [Huangkong, Tuxi, "classic:luoyi", "standard:yingzi"] });
        reduced.AtTuxi();
        Need(reduced.Events<ProgramNormalDrawAdjustedEvent>().Where(e => e.SkillId is "classic:luoyi" or "standard:yingzi" or Huangkong)
            .Select(e => e.Adjustment).SequenceEqual([-1, 1, 2]),
            "Real classic Luoyi subtracts before Yingzi adds; Huangkong doubles the remaining two instead of a pre-Luoyi cached count.");
        reduced.Activate(); reduced.Reach(() => reduced.P?.Choices.Any(c => Action(c) == "select-targets") == true);
        reduced.Answer(c => c.Targets.SequenceEqual([1, 2, 3])); reduced.Play(); AssertTuxiPayment(reduced, 1, 3); reduced.Cold();

        foreach (var name in new[] { "zero", "replacement", "skip-draw" })
        {
            var s = Scene.Start(new(name) { BaseDraw = name == "zero" ? 0 : 2,
                Skills = name == "replacement" ? [Huangkong, Tuxi, "boundary:luoyi", "standard:yingzi"] : [Huangkong, Tuxi],
                SkipDraw = name == "skip-draw" });
            s.Play();
            Need(s.PhaseDraws().Length == 0 && !s.Events<ProgramNormalDrawAdjustedEvent>().Any(e => e.SkillId is Huangkong or Tuxi),
                "Zero normal budget, real boundary Luoyi replacement and a genuinely skipped Draw phase cannot be revived by Huangkong or Tuxi.");
            Need(name != "replacement" || s.V().HandCount == 3 &&
                !s.Events<ProgramNormalDrawAdjustedEvent>().Any(e => e.SkillId == "standard:yingzi"),
                "Boundary Luoyi's real revealed-three replacement finishes its candidate window, so later Yingzi, Huangkong and Tuxi do not run.");
            Need(name != "skip-draw" || s.Events<ProgramBindingResolvedEvent>().Single(e => e.SkillId == Skip) is
                    { OwnerSeat: 0, Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Activated: true, Completed: true } &&
                !s.Events<ProgramBindingStartedEvent>().Any(e => e.SkillId is Huangkong or Tuxi),
                "The actual preparation skip program completes once; its real two-card normal budget never enters either Huangkong's or Tuxi's draw-starting binding.");
            s.Cold();
        }
        VerifyMergedYingziDrawReachesNativeHongdeCold();
    }

    private static void VerifyMergedYingziDrawReachesNativeHongdeCold()
    {
        var s = Scene.Start(new("yingzi-hongde") { BaseDraw = 1, Skills = ["standard:yingzi", Hongde], AllySkills = [GainWatch] });
        s.Reach(() => s.P?.Choices.Any(c => Action(c) == "bu-zhi") == true);
        var hongde = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.BuZhi is not null);
        var original = hongde.WindowContext!.MovementBatch!.NativeDrawInvocation!;
        Need(original is { OwnerSeat: 0, RequestedCount: 2, ActualCount: 2 } && original.Reason == CardMoveReasons.Draw &&
            s.PhaseDraws().Length == 2 && s.Events<BuZhiStartedEvent>().Count(e => e.Source.SkillId == Hongde) == 1,
            "Migrated standard Yingzi plus normal Draw1 is one actual Draw2, which reaches the original Hongde batch-benefit opportunity exactly once.");
        Need(!s.P!.Choices.Any(c => c.Targets.SequenceEqual([0])), "The actual Hongde choices exclude its owner and offer only another living character.");
        var frameId = hongde.Id; s.Cold(); s.Answer(c => c.Targets.SequenceEqual([1]));
        s.Play();
        Need(s.Events<BuZhiMovementPaidEvent>().Single(e => e.FrameId == frameId) is { Target: 1, Actual: 1 } &&
            s.V().HandCount == 2 && s.V(1).HandCount == 2 &&
            s.G.CardMovements.Count(m => m.Reason.Value == "program.bu-zhi.draw" && m.To == CardLocation.Hand(1)) == 1,
            "Hongde pays one actual card to the legally selected other teammate through its genuine native gained-card child.");
        Need(s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == GainWatch) == 1 &&
            s.Events<ProgramBindingResolvedEvent>().Single(e => e.SkillId == GainWatch) is { OwnerSeat: 1, Activated: true, Completed: true },
            "The actual other teammate's gain observer completes once through the NPC choice path before returning to the original Draw phase.");
        s.Cold(); s.Play();
        Need(s.Events<BuZhiStartedEvent>().Count(e => e.Source.SkillId == Hongde) == 1 &&
            s.Events<BuZhiMovementPaidEvent>().Count(e => e.FrameId == frameId) == 1 && s.V().HandCount == 2 && s.V(1).HandCount == 2,
            "Cold recovery returns to the exact normal-draw obligation without repeating Yingzi's merged draw, Hongde's choice or its payment.");
        s.Cold();
    }

    public static void DoubledDiscardQuotaPaysFourWithoutChangingHandLimitAndResumesChildrenCold()
    {
        var s = Scene.Start(new("human-discard") { Hand = Cards(6), WatchOwnEnd = true }); s.Play();
        Need(s.V().HandCount == 6 && s.V().HandLimit == 4, "The real six-card hand still has the ordinary four-card limit.");
        s.EndPlay(); s.Reach(() => s.P is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 });
        Need(s.P!.RequiredCardCount == 4 && s.P.Prompt.Contains("手牌上限为 4") && s.P.ValidCardIds.Count == 6 && s.V().HandLimit == 4,
            "Huangkong owes (six minus four) times two actual discards, rather than changing the hand limit to two.");
        var chosen = s.P.ValidCardIds.Take(4).Order().ToArray(); s.Cold();
        foreach (var invalid in new[] { Array.Empty<int>(), new[] { chosen[0], chosen[0] }, s.P.ValidCardIds.Take(5).ToArray() })
        {
            var before = s.State(); var result = s.G.Submit(new DiscardCardsCommand(0, invalid, s.P!.PromptId, s.G.Revision));
            Need(!result.Accepted && result.Error?.Code == CommandErrorCode.InvalidChoice && s.State() == before,
                "Illegal doubled-discard quantities are rejected atomically without paying, advancing or replacing the real prompt.");
        }
        s.Discard(chosen); s.Reach(() => s.P?.SkillPrompt?.SkillId == EndWatch);
        Need(s.V().HandCount == 2 && s.Events<HandLimitDiscardedEvent>().Single() is { ActorSeat: 0 } discarded &&
            discarded.CardIds.SequenceEqual(chosen) && s.HandLimitPayments(0).Select(m => m.CardId).Order().SequenceEqual(chosen) &&
            s.Events<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.Reason == CardMoveReasons.HandLimitDiscard)
                .All(e => e is { ExecutorSeat: 0, ActualCount: 4, HandCount: 4 }),
            "The published four-card obligation pays the exact original entities once and enters the real discard-phase-end child with two cards remaining.");
        s.Cold(); s.Continue(); s.Reach(() => s.P?.SkillPrompt?.SkillId == EndWatch);
        Need(s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "paid") && s.V().HandCount == 3 &&
            s.V().Hp == 1 && s.V().Skills?.Any(skill => skill.ContentId == Huangkong) != true &&
            s.Events<ProgramOwnerSkillsReplacedEvent>().Length == 1 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                m.Reason.Value.Contains(EndWatch, StringComparison.Ordinal)) == 1,
            "The real phase-end child draws, enters dying, genuinely recovers and loses Huangkong; neither the new one-HP limit nor the now-default multiplier reopens its already-paid four-card obligation.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "paid");
        s.Reach(() => s.G.State.CurrentSeat != 0);
        Need(s.HandLimitPayments(0).Length == 4 && s.Events<HandLimitDiscardedEvent>().Length == 1 && s.V().HandCount == 3,
            "Returning through a paid child and cold replay neither repeats the discard nor demands the normal two-card alternative."); s.Cold();
    }

    public static void PartialDiscardKeepsFrozenDoubledQuotaCold()
    {
        var s = Scene.Start(new("human-discard") { Hand = Cards(6), WatchOwnEnd = true }); s.Play();
        s.EndPlay(); s.Reach(() => s.P is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 });
        var chosen = s.P!.ValidCardIds.Take(4).ToArray();
        s.Discard([chosen[0]]); s.Cold();
        Need(s.P is { Kind: DecisionKind.DiscardCards, RequiredCardCount: 3 } && s.V().HandCount == 5,
            "A one-card payment leaves three of the frozen four-card quota, even though recalculating the multiplier would demand two.");
        s.Discard([chosen[1]]); s.Cold();
        Need(s.P is { Kind: DecisionKind.DiscardCards, RequiredCardCount: 2 } && s.V().HandCount == 4,
            "Reaching the ordinary hand limit must not forgive the remaining doubled-discard debt.");
        s.Discard(chosen.Skip(2).ToArray()); s.Reach(() => s.P?.SkillPrompt?.SkillId == EndWatch);
        Need(s.HandLimitPayments(0).Length == 4 && s.V().HandCount == 2 &&
            s.Events<HandLimitDiscardedEvent>().SelectMany(e => e.CardIds).Order().SequenceEqual(chosen.Order()),
            "Paying the exact remaining debt enters the native phase-end child once without losing or repeating cards.");
        s.Cold();
    }

    public static void DiscardExemptionsProtectedCardsSkippedPhaseAndNpcUseSameQuota()
    {
        var exempt = Scene.Start(new("exempt") { Hand = [new(CardKind.Slash), new(CardKind.Slash), .. Cards(6)] }); exempt.Play();
        exempt.Use("exempt"); exempt.Play(); exempt.EndPlay(); exempt.Reach(() => exempt.P is { Kind: DecisionKind.DiscardCards });
        Need(exempt.P!.RequiredCardCount == 4 && exempt.P.ValidCardIds.Count == 6 &&
            exempt.P.ValidCardIds.All(id => exempt.V().Hand.Single(c => c.Id == id).Kind == CardKind.Dodge),
            "Two genuinely issued Slash exemptions are excluded before counted-six overflow is doubled; exempt cards are not offered for this payment.");
        exempt.Cold(); exempt.Discard(exempt.P.ValidCardIds.Take(4).ToArray());
        Need(exempt.HandLimitPayments(0).Length == 4 && exempt.V().HandCount == 4 && exempt.V().Hand.Count(c => c.Kind == CardKind.Slash) == 2,
            "The doubled counted-card obligation pays four eligible entities and preserves the two exempt Slashes."); exempt.Cold();

        var protectedCards = Scene.Start(new("protected") { Skills = [Huangkong, Restrict],
            Hand = [.. Enumerable.Range(0, 4).Select(_ => new ShanheStartingCard(CardKind.Slash)),
                new(CardKind.Dismantlement), new(CardKind.Dismantlement)] }); protectedCards.Play();
        protectedCards.Use("self-hit"); protectedCards.Reach(() => protectedCards.P?.SkillPrompt?.SkillId == Restrict);
        protectedCards.Activate(); protectedCards.Reach(() => protectedCards.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "basic") == true);
        protectedCards.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "basic"); protectedCards.Play();
        Need(protectedCards.V().Hp == 4 && protectedCards.Events<HandCategoryRestrictionGrantedEvent>().Single().Restriction.AffectedSeat == 0,
            "A real damage-source category restriction protects the owner's basic cards; the subsequent genuine recovery restores the ordinary four-card limit.");
        protectedCards.EndPlay(); protectedCards.Reach(() => protectedCards.P is { Kind: DecisionKind.DiscardCards });
        Need(protectedCards.P!.RequiredCardCount == 2 && protectedCards.P.ValidCardIds.Count == 2 &&
            protectedCards.P.ValidCardIds.All(id => protectedCards.V().Hand.Single(c => c.Id == id).Kind == CardKind.Dismantlement),
            "Six cards are counted, but the doubled four-card debt is capped at the two cards legally self-discardable; protected basic cards do not become legal costs.");
        protectedCards.Cold(); protectedCards.Discard(protectedCards.P.ValidCardIds.ToArray());
        Need(protectedCards.HandLimitPayments(0).Length == 2 && protectedCards.V().HandCount == 4 &&
            protectedCards.V().Hand.All(c => c.Kind == CardKind.Slash), "Only the two unprotected actual entities pay the capped obligation."); protectedCards.Cold();

        foreach (var name in new[] { "no-overflow", "skip-discard" })
        {
            var s = Scene.Start(new(name) { Hand = Cards(name == "no-overflow" ? 4 : 6), SkipDiscard = name == "skip-discard" }); s.Play();
            var count = s.V().HandCount; s.EndPlay(); s.Reach(() => s.G.State.CurrentSeat != 0);
            Need(s.HandLimitPayments(0).Length == 0 && s.Events<HandLimitDiscardedEvent>().Length == 0 && s.V().HandCount == count &&
                (name != "skip-discard" || s.Events<ProgramBindingResolvedEvent>().Single(e => e.SkillId == Skip) is
                    { OwnerSeat: 0, Window: SkillProgramTriggerWindow.DiscardPhaseStarting, Activated: true, Completed: true }),
                "Zero overflow and a genuinely skipped Discard phase issue no multiplied debt or fabricated payment."); s.Cold();
        }
        var npc = Scene.Start(new("npc-discard") { Skills = [], AllySkills = [Huangkong], AllyHand = Cards(6), WatchForeignEnd = true });
        npc.Play(); var originalNpcHand = npc.V(1).Hand.Select(c => c.Id).Order().ToArray();
        npc.EndPlay(); npc.Reach(() => npc.P?.SkillPrompt?.SkillId == EndWatch);
        Need(npc.G.State.CurrentSeat == 1 && npc.V(1).HandCount == 2 && npc.HandLimitPayments(1).Length == 4 &&
            npc.HandLimitPayments(1).Select(m => m.CardId).Distinct().Count() == 4 &&
            npc.HandLimitPayments(1).Select(m => m.CardId).Concat(npc.V(1).Hand.Select(c => c.Id)).Order().SequenceEqual(originalNpcHand) &&
            npc.Events<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.Reason == CardMoveReasons.HandLimitDiscard)
                .All(e => e is { ExecutorSeat: 1, ActualCount: 4, HandCount: 4 }),
            "The native NPC skips only its Play phase, then uses the same doubled four-card discard quantity as the human command path.");
        npc.Cold(); npc.Continue(); npc.Reach(() => npc.G.State.CurrentSeat != 1);
        Need(npc.HandLimitPayments(1).Length == 4, "Cold recovery from a genuine foreign discard-end observer cannot repeat the NPC's four-card payment."); npc.Cold();
    }

    private static void AssertTuxiPayment(Scene s, int deckDraws, int steals)
    {
        var reason = new CardMoveReason($"skill-program.{Tuxi}.{SkillProgramEffectOp.TakeRandomHandCardFromSelectedTargets}");
        var departures = s.G.CardMovements.Where(m => m.Reason == reason && m.From.Zone == CardZoneKind.Hand && m.To == CardLocation.Processing).ToArray();
        var arrivals = s.G.CardMovements.Where(m => m.Reason == reason && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0)).ToArray();
        Need(departures.Length == steals && departures.Select(m => m.From.OwnerSeat).SequenceEqual(Enumerable.Range(1, steals).Select(seat => (int?)seat)) &&
            arrivals.Length == steals && departures.Select(m => m.CardId).SequenceEqual(arrivals.Select(m => m.CardId)) &&
            departures.Zip(arrivals).All(pair => pair.First.Sequence < pair.Second.Sequence) && arrivals.Select(m => m.CardId).Distinct().Count() == steals &&
            s.PhaseDraws().Length == deckDraws && !arrivals.Select(m => m.CardId).Intersect(s.PhaseDraws().Select(m => m.CardId)).Any() &&
            s.Events<NativeDrawInvocationRecordedEvent>().Single(e => e.OwnerSeat == 0 && e.Reason == CardMoveReasons.Draw) is var invoice &&
            invoice.Requested == deckDraws && invoice.ActualCount == deckDraws,
            "Native S5 Tuxi pays each selected original hand card through Processing in target order and subtracts exactly those transfers from the single remaining normal-draw invoice.");
    }

    private static void VerifyLoaderBoundaries()
    {
        string Resource(string suffix)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith('.' + suffix, StringComparison.Ordinal)))!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var rules = Resource("shanhe-s5-huangkong.rules.json");
        var presentation = Resource("shanhe-s5-huangkong.presentation.json");
        _ = SkillProgramCatalog.Load(rules, presentation);
        void Reject(Action<JsonObject> change)
        {
            var copy = JsonNode.Parse(rules)!; change(copy["skills"]![0]!.AsObject()); var rejected = false;
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The shared planned-count adjustment and discard-quota multiplier reject incompatible context, amount, owner, condition and numeric declarations.");
        }
        Reject(s => s["modifiers"]![0]!["operation"] = "add");
        Reject(s => s["modifiers"]![0]!["value"] = 0);
        Reject(s => s["modifiers"]![0]!["value"] = 21);
        Reject(s => s["modifiers"]![0]!["valueExpression"] = "ownerLostHp");
        Reject(s => s["modifiers"]![0]!["condition"] = new JsonObject { ["kind"] = "wounded" });
        Reject(s => s["modifiers"]![0]!["recipientScope"] = "otherPublicTeamAllies");
        Reject(s => s["triggers"]![0]!["window"] = "playEnding");
        Reject(s => s["triggers"]![0]!["drawPhaseMode"] = "replacement");
        Reject(s => s["triggers"]![0]!["effects"]![0]!["amount"] = 0);
        Reject(s => s["triggers"]![0]!["effects"]![0]!["target"] = "selectedTarget");
        Reject(s =>
        {
            var effects = s["triggers"]![0]!["effects"]!.DeepClone(); s.Remove("triggers");
            s["activations"] = new JsonArray(new JsonObject { ["id"] = "invalid-draw-plan", ["minCards"] = 0, ["maxCards"] = 0,
                ["minTargets"] = 0, ["maxTargets"] = 0, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = effects });
        });
    }

    private static ShanheStartingCard[] Cards(int count) => Enumerable.Range(0, count).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Club, i % 13 + 1)).ToArray();
    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed record Case(string Name)
    {
        public int BaseDraw { get; init; }
        public IReadOnlyList<string> Skills { get; init; } = [Huangkong];
        public IReadOnlyList<string> AllySkills { get; init; } = [];
        public IReadOnlyList<ShanheStartingCard> Hand { get; init; } = [];
        public IReadOnlyList<ShanheStartingCard> AllyHand { get; init; } = Cards(1);
        public bool SkipDraw { get; init; }
        public bool SkipDiscard { get; init; }
        public bool WatchOwnEnd { get; init; }
        public bool WatchForeignEnd { get; init; }
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardMovementRecord[] PhaseDraws() => G.CardMovements.Where(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw).ToArray();
        public CardMovementRecord[] HandLimitPayments(int seat) => G.CardMovements.Where(m => m.From == CardLocation.Hand(seat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.HandLimitDiscard).ToArray();
        public void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "A genuine Huangkong command was rejected."); }
        public void Answer(Func<PromptChoice, bool> choose) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(choose).Id, G.Revision));
        public void Activate() => Answer(c => Action(c) == "activate");
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void AtTuxi() => Reach(() => P?.SkillPrompt?.SkillId == Tuxi);
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Use(string id) => Accept(new UseProgramSkillCommand(0, Driver, id, [], [], G.Revision, P!.PromptId));
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Discard(int[] ids) => Accept(new DiscardCardsCommand(0, ids, P!.PromptId, G.Revision));
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The bounded Huangkong fixture must not finish a match.");
                if (P?.PlayerSeat == 0)
                {
                    if (P.SkillPrompt?.SkillId is "classic:yingzi" or "classic:luoyi" or "boundary:luoyi") Activate();
                    else throw new InvalidOperationException("Unexpected Huangkong boundary: " + JsonSerializer.Serialize(new
                    { P.Kind, P.PlayerSeat, P.Prompt, Skill = P.SkillPrompt?.SkillId, Hand = V().HandCount,
                        Draws = Events<NativeDrawInvocationRecordedEvent>().Select(e => new { e.OwnerSeat, e.Requested, e.ActualCount, e.Reason }),
                        Adjustments = Events<ProgramNormalDrawAdjustedEvent>(), Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind }) }));
                }
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded Huangkong continuation.");
        }
        public string State() => JsonSerializer.Serialize(new
        { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint()) });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves the exact planned draw, native theft/discard payments, private hands and typed child returns.");
            var cards = G.CreateCardZoneDiagnostics(); Need(cards.Select(c => c.CardId).Distinct().Count() == cards.Count, "All real cards occupy exactly one zone.");
        }
        public static Scene Start(Case c)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(c));
            var skills = c.Skills.Concat(c.WatchOwnEnd || c.WatchForeignEnd ? [EndWatch] : Array.Empty<string>())
                .Concat(c.WatchOwnEnd ? [LoseHuangkong] : Array.Empty<string>())
                .Concat(c.SkipDraw || c.SkipDiscard ? [Skip] : Array.Empty<string>()).ToArray();
            var setup = new ShanheBattleSetup(83, 1, "fixture-huangkong-" + c.Name, Owner, 4, 0, 0, 0, 0,
                skills, c.Hand, [], [new(EnemyA) { StartingHand = Cards(1) }, new(EnemyB) { StartingHand = Cards(1) }],
                [new(Ally, ExtraSkillIds: c.AllySkills) { StartingHand = c.AllyHand }], new string('b', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false,
                MaxTurns = 3, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(Case c) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-huangkong", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            // The ordinary package already registers the migrated standard
            // Yingzi program. Keep that exact definition instead of copying it.
            foreach (var id in new[] { Huangkong, Tuxi, Duipai, "boundary:yingzi", "classic:yingzi", "classic:luoyi", "boundary:luoyi", Hongde })
                b.AddSkill(Production.Value.GetSkill(id));
            object Pause(string bind, string option) => new { op = "chooseOption", target = "owner", resultBind = bind, options = new[] { new { id = option } } };
            object Activation(string id, params object[] effects) => new
            { id, minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            var definitions = new List<object>
            {
                new { id = Driver, revision = 1, activations = new[] {
                    Activation("exempt", new { op = "grantTurnHandLimitCardKindExemption", target = "owner", cardKinds = new[] { "slash" } }),
                    Activation("self-hit", new { op = "damage", target = "owner", sourceRef = new { kind = "owner" }, amount = 1 }, new { op = "recover", target = "owner", amount = 1 }) } },
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "skip-play", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = GainWatch, revision = 1, triggers = new[] { new { id = "actual-hongde-payment", window = "cardsGained", subject = "owner",
                    movementOccurrence = "perBatch", destinationZones = new[] { "hand" }, movementReasons = new[] { "program.bu-zhi.draw" },
                    optional = false, usageScope = "game", usageLimit = 1, effects = new[] { Pause("paid-gain", "continue") } } } },
                new { id = Restrict, revision = 1, triggers = new[] { new { id = "protect-actual-source", window = "afterDamageApplied", subject = "owner",
                    requireDamageSource = true, damageOccurrence = "perDamage", optional = true, effects = new object[] {
                        new { op = "chooseOption", target = "owner", resultBind = "category", options = new[] { new { id = "basic" }, new { id = "trick" }, new { id = "equipment" } } },
                        new { op = "restrictDamageSourceHandCategory", target = "owner", cardCategories = new[] { "basic" }, condition = new { kind = "choiceIs", sourceBind = "category", optionId = "basic" } },
                        new { op = "restrictDamageSourceHandCategory", target = "owner", cardCategories = new[] { "trick" }, condition = new { kind = "choiceIs", sourceBind = "category", optionId = "trick" } },
                        new { op = "restrictDamageSourceHandCategory", target = "owner", cardCategories = new[] { "equipment" }, condition = new { kind = "choiceIs", sourceBind = "category", optionId = "equipment" } } } } } }
            };
            if (c.WatchOwnEnd) definitions.Add(new { id = EndWatch, revision = 1, triggers = new[] { new { id = "real-own-discard-end", window = "discardPhaseEnded",
                subject = "owner", turnOwnerScope = "own", allowOwnDiscardPhaseEnded = true, optional = false, usageScope = "game", usageLimit = 1,
                effects = new object[] { Pause("before-child", "continue"), new { op = "draw", target = "owner", amount = 1 },
                    new { op = "loseHp", target = "owner", amount = 4 }, Pause("after-child", "paid") } } } });
            if (c.WatchOwnEnd) definitions.Add(new { id = LoseHuangkong, revision = 1, triggers = new[] { new { id = "legal-dying-loss", window = "dyingEntering",
                subject = "owner", optional = false, usageScope = "game", usageLimit = 1, effects = new object[] {
                    new { op = "recover", target = "owner", amount = 1 },
                    new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { Huangkong }, sourceBind = "standard:none" } } } } });
            if (c.WatchForeignEnd) definitions.Add(new { id = EndWatch, revision = 1, triggers = new[] { new { id = "real-npc-discard-end", window = "discardPhaseEnded",
                subject = "owner", turnOwnerScope = "otherLiving", optional = false, usageScope = "game", usageLimit = 1, effects = new[] { Pause("after-npc-payment", "continue") } } } });
            if (c.SkipDraw || c.SkipDiscard) definitions.Add(new { id = Skip, revision = 1, triggers = new[] { new { id = "actual-skip", window = c.SkipDraw ? "turnStartBeforeNormalFlow" : "discardPhaseStarting",
                subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { c.SkipDraw ? "draw" : "discard" } } } } } });
            var labels = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实惶恐测试操作", description = "真实实体、伤害、恢复和手牌策略" },
                [Quiet] = new { name = "只跳过NPC出牌", description = "保留实际摸牌和弃牌阶段" },
                [GainWatch] = new { name = "实际弘德受益子窗口", description = "实际支付后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Restrict] = new { name = "真实来源类别保护", description = "使用既有合法伤害来源类别限制", optionLabels = new Dictionary<string, string> { ["basic"] = "基本", ["trick"] = "锦囊", ["equipment"] = "装备" } }
            };
            if (c.WatchOwnEnd || c.WatchForeignEnd) labels[EndWatch] = c.WatchOwnEnd
                ? new { name = "真实弃牌结束子窗口", description = "已支付弃牌后真正摸一张", optionLabels = new Dictionary<string, string> { ["continue"] = "继续", ["paid"] = "已支付" } }
                : new { name = "真实弃牌结束子窗口", description = "已支付NPC弃牌后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            if (c.SkipDraw || c.SkipDiscard) labels[Skip] = new { name = "真实阶段跳过", description = "使用既有实际阶段策略" };
            if (c.WatchOwnEnd) labels[LoseHuangkong] = new { name = "真实濒死失去惶恐", description = "只在合法濒死窗口回复一点并终止式失去惶恐" };
            var programs = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in programs.Programs) b.AddSkill(new(id, programs.Presentations[id].Name, programs.Presentations[id].Description)
                { Program = program, ProgramPresentation = programs.Presentations[id] });
            b.AddGeneral(new(Owner, "真实惶恐拥有者", "", Driver, "wei", 4));
            foreach (var id in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(id, "真实其他参战者", "", Quiet, "qun", 4));
            b.AddDeck(new(Deck, "固定实体摸牌堆", 0, c.BaseDraw, [])
                { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实惶恐计划和支付", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
