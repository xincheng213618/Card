using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheTaishiciChecks
{
    private const string Dulie = StandardShanheS5TaishiciPackage.DulieId, Douchan = StandardShanheS5TaishiciPackage.DouchanId;
    private const string Owner = "fixture:s5-tsc-owner", Actor = "fixture:s5-tsc-actor", Deck = "fixture:s5-tsc-deck";
    private const string Driver = "fixture:s5-tsc-driver", Damage = "fixture:s5-tsc-damage", Gain = "fixture:s5-tsc-gain", RangeGain = "fixture:s5-tsc-range-gain";
    private const string Gate = "fixture:s5-tsc-gate", Quiet = "fixture:s5-tsc-quiet", BeforeDraw = "fixture:s5-tsc-before-draw";
    private const string RangeReason = "skill-program." + Dulie + ".sole-incoming-range.draw";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new ShanheBattleSetup(83, 1, "fixture:s5-tsc-production", StandardShanheS5TaishiciPackage.TaishiciId, 4, 0, 0, 0, 0,
            [], [], [], [new("classic:cao-cao")], [], new string('b', 64)) { TacticSkillIds = [StandardShanheCampaignCatalog.CurrentJinnangJiSkillId] }));

    public static void IncomingSoleCardRepeatsBeforeLiveRangeDrawAndReplaysCold()
    {
        var duel = Scene.Start("duel"); duel.BeginReverseDuel(); duel.AcceptDulie();
        var grant = duel.E<SoleIncomingEffectIssuedEvent>().Single().Grant;
        for (var count = 1; count <= 2; count++)
        {
            duel.Until(() => duel.At(Damage) && duel.E<DamageAppliedEvent>().Length == count);
            Need(duel.E<SoleIncomingRangeDrawRequestedEvent>().Length == 0 && duel.E<SoleIncomingRangeDrawPaidEvent>().Length == 0 &&
                duel.E<CardUseFinishedEvent>().All(e => e.ResolutionId != grant.UseFrameId) && duel.V().Hp == 20 - count,
                "Each real native Duel effect drains its complete damage child before the deferred range draw or original Use can finish.");
            duel.Cold(); duel.Continue();
        }
        duel.Until(() => duel.At(BeforeDraw));
        var requesting = duel.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == grant.UseFrameId);
        var attempt = duel.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == requesting.Id);
        Need(requesting.SoleIncomingRangeDraw is { Stage: SoleIncomingRangeDrawStage.Requesting, Outcome: null, RequestedCount: 1, AttackRange: 1 } &&
            requesting.NativeDraw is { ReturnKind: NativeDrawAttemptReturnKind.SoleIncomingRange, RequestedCount: 1, AttemptReturned: false } producer &&
            producer.ActiveAttemptId == attempt.Id && attempt.Return.Kind == NativeDrawAttemptReturnKind.SoleIncomingRange &&
            attempt.Context is { RecipientSeat: 0, RequestedCount: 1 } && attempt.Context.Reason.Value == RangeReason &&
            duel.E<SoleIncomingRangeDrawPaidEvent>().Length == 0 && !duel.G.CardMovements.Any(m => m.Reason.Value == RangeReason) && !duel.Finished(grant.UseFrameId),
            "After both genuine Duel effects, the same owning Use issues the real deferred native Draw attempt and pauses in its pre-draw child before any range material or payment.");
        duel.Cold(); duel.Continue();
        duel.Until(() => duel.At(RangeGain)); duel.CheckRangePayment(grant, 1, 1);
        Need(duel.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == grant.UseFrameId).SoleIncomingRangeDraw!.Outcome!.AttemptFrameId == attempt.Id &&
            duel.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == attempt.Id && e.ParentFrameId == grant.UseFrameId && !e.Replaced && e.RequestedCount == 1) == 1,
            "Returning the real pre-draw child binds its exact attempt to the single paid native outcome once.");
        duel.Cold(); duel.Continue(); duel.Play(); duel.CheckUse(grant, 2);
        duel.BeginReverseDuel(); duel.Play();
        Need(duel.E<SoleIncomingEffectIssuedEvent>().Length == 1 && duel.E<DamageAppliedEvent>().Length == 3 && duel.E<SoleIncomingRangeDrawPaidEvent>().Length == 1,
            "A second genuinely incoming sole-target Duel in the same actual turn cannot spend Dulie again.");
        var turn = duel.G.State.TurnNumber; duel.EndPlay(); duel.Until(() => duel.G.State.TurnNumber > turn && duel.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        duel.BeginReverseDuel(); duel.AcceptDulie(); duel.Play();
        Need(duel.E<SoleIncomingEffectIssuedEvent>().Length == 2 && duel.E<SoleIncomingRangeDrawPaidEvent>().Length == 2 && duel.E<DamageAppliedEvent>().Length == 5,
            "The next genuine actual turn refreshes the one-use opportunity without creating another use for either extra effect."); duel.Cold();

        var basic = Scene.Start("basic"); basic.EndPlay(); basic.AcceptDulie();
        var slashGrant = basic.E<SoleIncomingEffectIssuedEvent>().Single().Grant; var original = basic.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == slashGrant.UseFrameId).CardId;
        for (var count = 1; count <= 2; count++)
        {
            basic.Until(() => basic.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
            var card = basic.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); basic.Answer(c => c.Cards.SequenceEqual([card]));
            basic.Until(() => basic.E<CardRespondedEvent>().Count(e => e.ResponderSeat == 0 && e.EffectiveCardKind == CardKind.Dodge) == count);
            if (count == 1) Need(basic.E<SoleIncomingRangeDrawPaidEvent>().Length == 0, "The first real Dodge response cannot pay the deferred draw before the second effect has resolved."); basic.Cold();
        }
        basic.Until(() => basic.At(RangeGain)); basic.CheckRangePayment(slashGrant, 1, 1); basic.Cold(); basic.Continue(); basic.Until(() => basic.Finished(slashGrant.UseFrameId));
        Need(basic.E<DamageAppliedEvent>().Length == 0 && basic.G.CardMovements.Where(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.CardKind == CardKind.Dodge).Select(m => m.CardId).Distinct().Count() == 2 &&
            basic.G.CardMovements.Count(m => m.CardId == original && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) == 1 &&
            basic.G.CardMovements.Count(m => m.CardId == original && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "One real enemy Slash requires two independently paid physical Dodge responses across the original and extra native effects, while its original material is paid and cleaned once."); basic.Cold();

        var strip = Scene.Start("dismantle"); var bow = strip.Card(CardKind.QilinBow, []); strip.Play();
        Need(strip.G.GetAttackRange(0) == 5 && strip.V().HandCount == 0, "The actual equipped Qilin Bow is the sole removable target material and initially gives range five.");
        strip.EndPlay(); strip.AcceptDulie(); var stripGrant = strip.E<SoleIncomingEffectIssuedEvent>().Single().Grant; strip.Cold(); strip.Until(() => strip.At(RangeGain));
        strip.CheckRangePayment(stripGrant, 1, 1);
        Need(strip.G.GetAttackRange(0) == 1 && strip.V().Equipment.Count == 0 &&
            strip.G.CardMovements.Count(m => m.CardId == bow && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Dismantlement) == 1 &&
            strip.G.CardMovements.Count(m => m.CardId == bow && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.DismantlementFinished) == 1 &&
            strip.E<TargetCardDiscardedEvent>().Count(e => e.ResolutionId == stripGrant.UseFrameId && e.SourceSeat == stripGrant.OriginalActorSeat && e.TargetSeat == 0 &&
                e.FromZone == CardZoneKind.Equipment && e.PublicCardId == bow && e.PublicCardKind == CardKind.QilinBow) == 1 &&
            strip.E<SoleIncomingEffectRoundCompletedEvent>().Count(e => e.UseFrameId == stripGrant.UseFrameId) == 1,
            "Real Dismantle removes the bow; its extra effect tolerates an empty legal-material set, and only after both effects does Dulie read current range one, rather than the old five. " +
            JsonSerializer.Serialize(new { BowId = bow, Range = strip.G.GetAttackRange(0), Equipment = strip.V().Equipment,
                DirectEquipmentDiscardCount = strip.G.CardMovements.Count(m => m.CardId == bow && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile),
                BowMovements = strip.G.CardMovements.Where(m => m.CardId == bow), TargetDiscards = strip.E<TargetCardDiscardedEvent>(),
                RoundStarted = strip.E<SoleIncomingEffectRoundStartedEvent>(), RoundCompleted = strip.E<SoleIncomingEffectRoundCompletedEvent>() }));
        strip.Cold(); strip.Continue(); strip.Until(() => strip.Finished(stripGrant.UseFrameId)); strip.Cold();

        var feng = Scene.Start("fengzi"); feng.EndPlay(); feng.AcceptDulie(); var combined = feng.E<SoleIncomingEffectIssuedEvent>().Single().Grant;
        feng.Until(() => feng.At(RangeGain)); feng.CheckRangePayment(combined, 7, 5);
        Need(feng.E<ExtraCardEffectIssuedEvent>().Count(e => e.CardUseFrameId == combined.UseFrameId && e.Source.SkillId == "ol:fengzi") == 1 &&
            feng.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == 3 &&
            feng.E<SoleIncomingEffectRoundStartedEvent>().Count(e => e.UseFrameId == combined.UseFrameId) == 1 &&
            feng.E<SoleIncomingEffectRoundCompletedEvent>().Count(e => e.UseFrameId == combined.UseFrameId) == 1 &&
            feng.G.CardMovements.Count(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.extra-effect.cost") == 1,
            "The real NPC Fengzi cost adds one whole native round and Dulie adds another: original plus two extras cause three damages under one Use, then one range-seven draw capped at five, without overwriting or multiplying either source.");
        feng.Cold(); feng.Continue(); feng.Until(() => feng.Finished(combined.UseFrameId)); feng.CheckUse(combined, 3); feng.Cold();

        foreach (var name in new[] { "unpaid-loss", "paid-loss" })
        {
            var s = Scene.Start(name); if (name == "paid-loss") s.Use("armor-two"); s.BeginReverseDuel(); s.AcceptDulie(); var issue = s.E<SoleIncomingEffectIssuedEvent>().Single().Grant;
            if (name == "unpaid-loss")
            {
                s.Until(() => s.At(Damage)); Need(!s.Has(Dulie) && s.V().Hp == 19 && s.E<SoleIncomingRangeDrawRequestedEvent>().Length == 0,
                    "The original real damage withdraws the exact HP-derived Dulie source before the deferred draw is issued."); s.Cold(); s.Continue(); s.Play();
                Need(s.E<DamageAppliedEvent>().Length == 2 && s.E<SoleIncomingEffectRoundCompletedEvent>().Length == 1 && s.E<SoleIncomingRangeDrawRequestedEvent>().Length == 0 &&
                    s.E<SoleIncomingRangeDrawCompletedEvent>() is [{ Requested: false }], "The already-issued extra effect survives source loss, while its still-unpaid deferred draw is skipped.");
            }
            else
            {
                s.Until(() => s.At(RangeGain)); s.CheckRangePayment(issue, 1, 1); s.Cold(); s.Continue();
                s.Until(() => s.At(RangeGain) && s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "lost"));
                Need(!s.Has(Dulie) && s.V().Hp == 19 && s.E<SoleIncomingRangeDrawPaidEvent>().Length == 1, "A genuine gain child withdraws Dulie after the native range draw has already paid."); s.Cold(); s.Continue(); s.Play();
                Need(s.E<SoleIncomingRangeDrawCompletedEvent>() is [{ Requested: true }] && s.E<SoleIncomingRangeDrawPaidEvent>().Length == 1 && s.E<DamageAppliedEvent>().Length == 2,
                    "Loss after draw payment drains the exact gain child and completes once without reissuing the draw or either damage.");
            }
            s.CheckUse(issue, 2); s.Cold();
        }
        VerifyLoaderBoundaries();
    }

    public static void PreparationSequentialDuelsGrantPersistentShortfallAndReplaysCold()
    {
        var s = Scene.Start("sequential", ready: false); s.Until(() => s.At(Gain));
        var started = s.E<SequentialDeckKindGrowthStartedEvent>().Single(); var root = s.SequentialRoot(); var first = s.E<SequentialDeckKindGrowthStepPaidEvent>().Single();
        Need(started.RequestedCount == 4 && started.OutputKind == CardKind.Duel && root.RequestedCount == 4 && root.ActualObtained == 1 && root.Steps.Count == 1 && root.Shortfall is null &&
            first.Step is { Index: 0, ActualObtained: 1 } && first.Step.Movements is [{ From.Zone: CardZoneKind.DrawPile, To: { Zone: CardZoneKind.Hand, OwnerSeat: 0 }, CardKind: CardKind.Duel }] &&
            s.E<SequentialDeckKindGrowthGrantedEvent>().Length == 0 && s.E<NativeDrawInvocationRecordedEvent>().Length == 0,
            "Preparation freezes maximum HP four and physically obtains only the first Duel, then waits for its real gain child; no bulk acquisition, Draw invoice or premature growth is invented.");
        Need(s.ObservedStepFrozen && Frozen(first.Step.Movements) && Frozen(root.Steps) && Frozen(root.Steps[0].Movements) && s.G.ObserverFailures.Count == 0,
            "The delivered event, committed history and pending owning step expose frozen movement collections; observers cannot mutate prepared physical-card evidence.");
        s.Privacy(); s.Cold(); s.Continue(); s.Until(() => s.At(Gain) && s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "grown"));
        Need(s.V().MaxHp == 5 && s.SequentialRoot() is { RequestedCount: 4, ActualObtained: 1, Shortfall: null } && s.E<SequentialDeckKindGrowthStepPaidEvent>().Length == 1,
            "The first real gain child increases maximum HP to five while the owning preparation remains frozen at four and still has not attempted its second Duel."); s.Cold(); s.Continue(); s.Play();
        Need(s.E<SequentialDeckKindGrowthStepPaidEvent>().Select(e => e.Step.CardId).Distinct().Count() == 2 &&
            s.E<SequentialDeckKindGrowthStepPaidEvent>().All(e => e.Step.ActualObtained == 1 && e.Step.Movements.Count == 1) &&
            s.E<SequentialDeckKindGrowthGrantedEvent>() is [{ RequestedCount: 4, ActualObtained: 2, Amount: 2 }] &&
            s.E<SequentialDeckKindGrowthCompletedEvent>() is [{ Attempts: 2, ActualObtained: 2, Shortfall: 2, Cancelled: false }] && s.G.GetAttackRange(0) == 3 &&
            s.V().Hand.Count(c => c.Kind == CardKind.Duel) == 2 && s.E<NativeDrawInvocationRecordedEvent>().Length == 0,
            "Exactly two real deck Duels enter hand in separate completed batches; the two-card shortfall permanently adds two range and two Slash uses, with no generated Duels or native Draw call."); s.Cold();
        for (var i = 0; i < 3; i++) { s.Card(CardKind.Slash, [1]); s.Play(); }
        Need(!s.CanSlash() && s.V().Hand.Any(c => c.Kind == CardKind.Slash) && s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1) == 3,
            "The persistent shortfall enables three actual paid Slashes in this Play, rather than only changing a descriptive number."); s.RejectSlash();
        var turn = s.G.State.TurnNumber; s.EndPlay(); s.Until(() => s.G.State.TurnNumber > turn && s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(s.E<SequentialDeckKindGrowthStartedEvent>().Select(e => e.RequestedCount).SequenceEqual([4, 5]) &&
            s.E<SequentialDeckKindGrowthGrantedEvent>().Select(e => e.Amount).SequenceEqual([2, 5]) && s.E<SequentialDeckKindGrowthStepPaidEvent>().Length == 2 &&
            s.G.GetAttackRange(0) == 8 && s.G.CreateSnapshot(0).DrawPileCount == 24,
            "The next actual preparation uses current maximum HP five, finds no remaining Duel without washing the discard pile, and accumulates five more permanent range/Slash uses."); s.Cold();
        s.Use("lose-douchan"); Need(!s.Has(Douchan) && s.G.GetAttackRange(0) == 8 && s.CanSlash(), "A real terminal skill replacement removes Douchan while its independently issued permanent growth remains."); s.Cold();

        var loss = Scene.Start("sequential-loss", ready: false); loss.Until(() => loss.At(Gain)); var issued = loss.E<SequentialDeckKindGrowthStartedEvent>().Single(); loss.Cold(); loss.Continue();
        loss.Until(() => loss.At(Gain) && loss.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "lost"));
        Need(loss.V().Hp == 3 && !loss.Has(Douchan) && loss.SequentialRoot().ActualObtained == 1 && loss.E<SequentialDeckKindGrowthGrantedEvent>().Length == 0,
            "The first actual obtain child can withdraw the HP-dependent source while its paid sequential instruction and remaining frozen count stay on the original frame.");
        loss.Cold(); loss.Continue(); loss.Play();
        Need(loss.E<SequentialDeckKindGrowthStepPaidEvent>().Count(e => e.ProgramFrameId == issued.ProgramFrameId) == 2 &&
            loss.E<SequentialDeckKindGrowthGrantedEvent>() is [{ Amount: 2, ActualObtained: 2 }] && loss.G.GetAttackRange(0) == 3 &&
            loss.E<SequentialDeckKindGrowthCompletedEvent>() is [{ Cancelled: false, Shortfall: 2 }], "A living source lost after the first paid obtain still finishes this exact preparation, granting its independent shortfall once."); loss.Cold();
    }

    public static void DouchanRangeFeedsDulieAndConditionalRecipesStayIsolatedCold()
    {
        var s = Scene.Start("combined");
        Need(s.E<SequentialDeckKindGrowthGrantedEvent>() is [{ RequestedCount: 4, ActualObtained: 0, Amount: 4 }] && s.G.GetAttackRange(0) == 5 && s.E<NativeDrawInvocationRecordedEvent>().Length == 0,
            "The actual two-skill combination starts with no deck Duel: its genuine preparation issues four permanent range/Slash growth without pretending that obtain is Draw.");
        s.BeginReverseDuel(); s.AcceptDulie(); var issue = s.E<SoleIncomingEffectIssuedEvent>().Single().Grant; s.Until(() => s.At(RangeGain)); s.CheckRangePayment(issue, 5, 5);
        Need(s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == 2 && s.V().Hp == 2 && s.E<SoleIncomingEffectRoundCompletedEvent>().Length == 1,
            "The exact permanent Douchan range feeds Dulie's live range draw only after both actual incoming Duel effects finish.");
        s.Privacy(); s.Cold(); s.Continue(); s.Play(); s.CheckUse(issue, 2);
        Need(s.E<SoleIncomingRangeDrawPaidEvent>() is [{ ActualCount: 5 }] && s.E<NativeDrawInvocationRecordedEvent>().Count(e => e.Reason == new CardMoveReason(RangeReason)) == 1,
            "One true five-card native Draw invoice and five original entities pay the deferred benefit once; preparation obtains never inflate its draw history."); s.Cold();
        VerifyActualRecipes();
    }

    private static void VerifyActualRecipes()
    {
        const string id = StandardShanheS5TaishiciPackage.TaishiciId;
        var old = StandardShanheCampaignCatalog.CreateDefinition(3, StandardShanheCampaignCatalog.LocalEditionId);
        Need(old.Nodes.SelectMany(n => n.Encounters ?? []).All(e => e.Enemies.All(m => m.GeneralId != id)), "The complete S5 Taishi Ci form is absent from 2024.");
        for (var tier = 1; tier <= 3; tier++)
        {
            var definition = StandardShanheCampaignCatalog.CreateDefinition(tier, StandardShanheCampaignCatalog.Returning2026EditionId);
            var encounter = definition.Nodes.Single(n => n.Id == "dagu").Encounters!.Single(e => e.Id == "dagu:taishici-s5");
            Need(encounter.Enemies is [{ GeneralId: id, Hp: null, MaxHpBonus: 0, InitialHandBonus: 0, DrawBonus: 0, SlashBonus: 0, StartingHand: null }] && (encounter.Allies?.Count ?? 0) == 0,
                "All three local tiers retain the complete raw four-HP two-skill form without invented growth, equipment or allies.");
            var setup = new ShanheBattleSetup(83, 1, "dagu", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], encounter.Enemies, [], ShanheCampaignContent.GetContentHash(definition));
            var r = StandardShanheCampaignCatalog.CreateBattleRegistry(setup); var g = StandardShanheCampaignCatalog.CreateBattle(setup); Need(g.Submit(new StartGameCommand()).Accepted, "The actual conditional Taishi Ci recipe starts.");
            var npc = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == id);
            Need(npc is { Hp: 4, MaxHp: 4, HandCount: 4, Armor: 0 } && npc.Equipment.Count == 0 && npc.Skills?.Select(k => k.ContentId).ToHashSet().SetEquals([Dulie, Douchan]) == true,
                "The actual catalog creates the real two production skills and ordinary four-card opening before that enemy's preparation, without initial equipment.");
            Need(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "The actual package, startup hand and both skills replay identically.");
            var plain = setup with { EnemyRoster = [new("classic:cao-cao")] }; var extra = plain with { ExtraSkillIds = [Dulie] };
            Need(!StandardShanheCampaignCatalog.CreateBattleRegistry(plain).Skills.ContainsKey(Dulie) && StandardShanheCampaignCatalog.CreateBattleRegistry(extra).Skills.ContainsKey(Dulie) &&
                !StandardShanheCampaignCatalog.CreateBattleRegistry(plain).Generals.ContainsKey(id) && r.Skills.ContainsKey(Douchan), "Warm actual-alias, extra-skill and ordinary recipes remain conditionally separated in both cache directions.");
        }
    }

    private static void VerifyLoaderBoundaries()
    {
        string Read(string suffix) { var a = typeof(StandardContentPackage).Assembly; using var stream = a.GetManifestResourceStream(a.GetManifestResourceNames().Single(n => n.EndsWith('.' + suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = Read("shanhe-s5-taishici.rules.json"); var presentation = Read("shanhe-s5-taishici.presentation.json"); _ = SkillProgramCatalog.Load(rules, presentation);
        foreach (var change in new Action<JsonNode>[] { n => n["skills"]![0]!["triggers"]![0]!["ownerRelation"] = "actor", n => n["skills"]![0]!["triggers"]![0]!["usageScope"] = "phase",
            n => n["skills"]![1]!["triggers"]![0]!["effects"]![0]!["outputKind"] = "slash", n => n["skills"]![1]!["triggers"]![0]!["window"] = "turnEnding" })
        {
            var copy = JsonNode.Parse(rules)!; change(copy); var rejected = false; try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The new operations preserve their strict actual incoming-sole-use/turn quota and own-preparation sequential Duel contracts.");
        }
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static bool Frozen<T>(IReadOnlyList<T> items)
    {
        if (items is not IList<T> list || !list.IsReadOnly || list.Count == 0) return false;
        try { list[0] = list[0]; return false; } catch (NotSupportedException) { return true; }
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, g.PlayerCount).Select(i => SnapshotJson.Serialize(g.CreateSnapshot(i))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
        g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(g.CreateCheckpoint()) });

    private sealed class Scene(GameEngine game, ContentRegistry registry, string name)
    {
        private readonly Dictionary<long, (DamageFrame Frame, long ActionId)> observedUseDamage = [];
        public GameEngine G { get; private set; } = game;
        public bool ObservedStepFrozen { get; private set; }
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Has(string skill) => V().Skills?.Any(s => s.ContentId == skill) == true;
        public bool At(string skill) => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.ContainsKey("option-id"));
        public void Accept(GameCommand c)
        {
            var result = G.Submit(c); if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message + " " + Diagnostic());
            // The real damage child pauses here before its final AfterDamage fact.
            // Preserve its exact native Use parent rather than treating a damage ID as a Use ID.
            foreach (var frame in G.ResolutionStack.OfType<DamageFrame>())
                if (G.ResolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == frame.ParentFrameId) is { Action: { } action })
                    observedUseDamage.TryAdd(frame.Id, (frame, action.ActionId));
        }
        public void Answer(Func<PromptChoice, bool> choose) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(choose).Id, G.Revision));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void AcceptDulie() { Until(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Dulie && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")); Cold(); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
        public void BeginReverseDuel() => Accept(new UseProgramSkillCommand(0, Driver, "reverse-duel", [], [1], G.Revision, P!.PromptId));
        public void Use(string id) { Accept(new UseProgramSkillCommand(0, Driver, id, [], [], G.Revision, P!.PromptId)); Play(); }
        public int Card(CardKind kind, int[]? targets = null)
        {
            targets ??= []; var legal = G.GetHumanLegalActions(); var action = legal.FirstOrDefault(a => a.Kind != LegalActionKind.Recast && a.ConversionSource is null && a.CardId is { } id && V().Hand.Any(c => c.Id == id && c.Kind == kind) &&
                (a.TargetSeats.SequenceEqual(targets) || targets.Length == 0 && kind == CardKind.QilinBow && a.TargetSeats.SequenceEqual([0])));
            if (action is null) throw new InvalidOperationException("Missing real card action " + kind + " " + Diagnostic());
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)); return action.CardId.Value;
        }
        public bool CanSlash() => G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash);
        public void RejectSlash() { var id = V().Hand.First(c => c.Kind == CardKind.Slash).Id; var before = State(G); Need(!G.Submit(new PlayCardCommand(0, id, [1], G.Revision, P!.PromptId, CardKind.Slash)).Accepted && State(G) == before, "The fourth native Slash is rejected atomically against the real persistent three-use budget."); }
        public bool Finished(long id) => E<CardUseFinishedEvent>().Any(e => e.ResolutionId == id);
        public ProgramSequentialDeckKindGrowthReceipt SequentialRoot() => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SequentialDeckKindGrowth is not null).SequentialDeckKindGrowth!;
        public void CheckRangePayment(SoleIncomingEffectGrant grant, int range, int count)
        {
            var use = G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == grant.UseFrameId);
            Need(E<SoleIncomingRangeDrawRequestedEvent>().Single(e => e.ProgramFrameId == grant.ProgramFrameId) is { RecipientSeat: 0, AttackRange: var actualRange, RequestedCount: var request } && actualRange == range && request == count &&
                E<SoleIncomingRangeDrawPaidEvent>().Single(e => e.ProgramFrameId == grant.ProgramFrameId) is { RecipientSeat: 0, ActualCount: var actual } && actual == count &&
                use.SoleIncomingRangeDraw is { Stage: SoleIncomingRangeDrawStage.Children, Outcome: { } outcome } && outcome.ActualDrawIds.Count == count &&
                outcome.ActualDrawIds.All(id => G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == RangeReason) == 1) && !Finished(grant.UseFrameId),
                "After all original and extra effects, the owning Use freezes its current range and exactly one real native draw outcome before its actual gain children. " + Diagnostic());
        }
        public void CheckUse(SoleIncomingEffectGrant grant, int effects)
        {
            var damage = observedUseDamage.Values.Where(d => d.Frame.ParentFrameId == grant.UseFrameId).ToArray();
            Need(damage.Length == effects && damage.All(d => d.ActionId == grant.ActionId && d.Frame.SourceSeat == grant.OriginalActorSeat && d.Frame.TargetSeat == grant.TargetSeat &&
                E<DamageRequestedEvent>().Count(e => e.ResolutionId == d.Frame.Id && e.SourceSeat == d.Frame.SourceSeat && e.TargetSeat == d.Frame.TargetSeat && e.Amount == d.Frame.Amount && e.Nature == d.Frame.Nature) == 1 &&
                E<DamageTriggerWindowOpenedEvent>().Count(e => e.DamageFrameId == d.Frame.Id && e.SourceSeat == d.Frame.SourceSeat && e.TargetSeat == d.Frame.TargetSeat && e.CardKind == grant.CardKind) == 1 &&
                E<AfterDamageEvent>().Count(e => e.ResolutionId == d.Frame.Id && e.SourceSeat == d.Frame.SourceSeat && e.TargetSeat == d.Frame.TargetSeat && e.Amount == d.Frame.Amount && e.Nature == d.Frame.Nature) == 1),
                "Every completed damage retains its observed exact native Use/Action parent and unique requested, trigger-window and AfterDamage facts. " + Diagnostic());
            Need(E<SoleIncomingEffectRoundStartedEvent>().Count(e => e.UseFrameId == grant.UseFrameId) == 1 &&
                E<SoleIncomingEffectRoundCompletedEvent>().Count(e => e.UseFrameId == grant.UseFrameId) == 1 && E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == grant.UseFrameId) == 1 &&
                E<CardUseFinishedEvent>().Count(e => e.ResolutionId == grant.UseFrameId) == 1 && E<SoleIncomingRangeDrawCompletedEvent>().Count(e => e.UseFrameId == grant.UseFrameId) == 1,
                "The same original native Use owns one added Dulie round, one deferred completion and one final finish. " + Diagnostic());
        }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Until(Func<bool> done)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) { Continue(); continue; }
                    if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification) { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    throw new InvalidOperationException("Unexpected bounded Taishi Ci boundary: " + Diagnostic());
                }
                Need(G.State.Winner == Winner.None, "The short Taishi Ci fixture must remain live. " + Diagnostic()); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The real Taishi Ci child/return boundary was not reached: " + Diagnostic());
        }
        public void Privacy() => Need(Enumerable.Range(0, G.PlayerCount).All(i => G.CreateSnapshot(i).Players.Where(p => p.Seat != i).All(p => p.Hand.Count == 0)) &&
            !SnapshotJson.Serialize(G.CreateSnapshot(1)).Contains("SequentialDeckKindGrowth", StringComparison.Ordinal), "Actual deck search/candidate receipts remain trusted; every foreign Hand stays private.");
        public void Cold() { var before = State(G); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State(G) == before, "Cold replay preserves original uses, independent extra-effect sources, deferred live range, native draw outcome, per-card obtains and permanent growth without repeating payment."); }
        public string Diagnostic() => JsonSerializer.Serialize(new { name, G.State.TurnNumber, G.State.Winner,
            Players = G.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.MaxHp, p.HandCount, p.Armor }), Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId, Choices = P.Choices.Select(c => c.Id) },
            Issued = E<SoleIncomingEffectIssuedEvent>(), Draw = E<SoleIncomingRangeDrawPaidEvent>(), Damage = E<DamageAppliedEvent>(), AfterDamage = E<AfterDamageEvent>(),
            ObservedUseDamage = observedUseDamage.Values.Select(d => new { d.Frame.Id, d.Frame.ParentFrameId, d.ActionId, d.Frame.SourceSeat, d.Frame.TargetSeat, d.Frame.Amount }),
            Declared = E<CardUseDeclaredEvent>(), Finished = E<CardUseFinishedEvent>(), ExtraStarted = E<SoleIncomingEffectRoundStartedEvent>(), ExtraCompleted = E<SoleIncomingEffectRoundCompletedEvent>(), RangeCompleted = E<SoleIncomingRangeDrawCompletedEvent>(),
            Growth = E<SequentialDeckKindGrowthGrantedEvent>(), Steps = E<SequentialDeckKindGrowthStepPaidEvent>().Select(e => new { e.ProgramFrameId, e.Step.Index, e.Step.CardId, e.Step.ActualObtained }),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind }) });
        public static Scene Start(string name, bool ready = true)
        {
            var sequential = name.StartsWith("sequential", StringComparison.Ordinal); var max = sequential || name == "combined" ? 4 : 20;
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(name));
            ShanheStartingCard[] hand = sequential ? Enumerable.Range(1, 4).Select(i => new ShanheStartingCard(CardKind.Slash, Suit.Spade, i)).ToArray() :
                name == "dismantle" ? [new(CardKind.QilinBow, Suit.Diamond, 5)] : name == "basic" ? [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Diamond, 3)] : [];
            ShanheStartingCard[] enemy = name == "basic" ? [new(CardKind.Slash, Suit.Club, 7)] : name == "dismantle" ? [new(CardKind.Dismantlement, Suit.Spade, 7)] :
                name == "fengzi" ? [new(CardKind.Duel, Suit.Club, 7), new(CardKind.Nullification, Suit.Club, 9)] : [];
            var setup = new ShanheBattleSetup(83, 1, "fixture:s5-tsc-" + name, Owner, max, 0, 0, 0, 0, [], hand, [], [new(Actor) { StartingHand = enemy }], [], new string('c', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 2, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scene(g, r, name);
            g.EventCommitted += envelope => { if (envelope.Payload is SequentialDeckKindGrowthStepPaidEvent paid) s.ObservedStepFrozen = Frozen(paid.Step.Movements); };
            s.Accept(new StartGameCommand()); if (ready) s.Play(); return s;
        }
    }

    private sealed class Fixture(string name) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-s5-taishici", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            // Register a real draw-count consumer to exercise native invoices, without granting its separate gameplay policy to anyone.
            foreach (var id in new[] { Dulie, Douchan, "ol:fengzi", StandardShanheCampaignCatalog.CurrentJinnangJiSkillId }) b.AddSkill(Production.Value.GetSkill(id));
            if (name == "dismantle" && ContentRegistry.Build(new StandardContentPackage()).Cards.Values.All(c => c.LegacyKind != CardKind.QilinBow))
                b.AddCard(Production.Value.Cards.Values.First(c => c.LegacyKind == CardKind.QilinBow));
            var defs = new List<Dictionary<string, object>>(); var labels = new Dictionary<string, object>();
            object Pause(string id) => new { op = "chooseOption", target = "owner", resultBind = id, options = new[] { new { id } } };
            void Add(string id, Dictionary<string, object> value, params string[] options) { value["id"] = id; value["revision"] = 1; defs.Add(value); labels[id] = new { name = id, description = "真实唯一目标、逐次获得与拥有帧归返", optionLabels = options.ToDictionary(x => x, x => x) }; }
            object Act(string id, object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            var driver = new Dictionary<string, object> { ["activations"] = new object[] { new { id = "reverse-duel", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null, usesPerPhase = 2,
                effects = new[] { new { op = "useSelectedActorDuel", target = "owner" } } }, Act("armor-two", [new { op = "grantArmor", target = "owner", amount = 2 }]),
                Act("lose-douchan", [new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { Douchan }, sourceBind = "standard:none" }]) } };
            if (name == "fengzi") driver["modifiers"] = new[] { new { id = "range-seven", query = "attackRange", operation = "add", value = 6, priority = 0 } };
            Add(Driver, driver);
            Add(Damage, new() { ["triggers"] = new[] { new { id = "actual-native-effect", window = "afterDamageApplied", subject = "owner", damageOccurrence = "perDamage", optional = false, effects = new[] { Pause("damage") } } } }, "damage");
            Add(BeforeDraw, new() { ["triggers"] = new[] { new { id = "real-deferred-native-attempt", window = "nativeDrawStarting", subject = "owner", condition = new { kind = "phaseIsPlay" },
                usageScope = "game", usageLimit = 1, optional = false, effects = new[] { Pause("before-draw") } } } }, "before-draw");
            var obtain = new List<object> { Pause("obtained") };
            if (name == "sequential") { obtain.Add(new { op = "changeMaximumHp", target = "owner", amount = 1 }); obtain.Add(Pause("grown")); }
            if (name == "sequential-loss") { obtain.Add(new { op = "loseHp", target = "owner", amount = 1 }); obtain.Add(Pause("lost")); }
            Add(Gain, new() { ["triggers"] = new[] { new { id = "real-first-obtain-child", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementOccurrence = "perBatch", usageScope = "game", usageLimit = 1, optional = false, effects = obtain.ToArray() } } },
                name == "sequential" ? ["obtained", "grown"] : name == "sequential-loss" ? ["obtained", "lost"] : ["obtained"]);
            var range = new List<object> { Pause("paid") }; if (name == "paid-loss") { range.Add(new { op = "loseHp", target = "owner", amount = 1 }); range.Add(Pause("lost")); }
            Add(RangeGain, new() { ["triggers"] = new[] { new { id = "real-paid-range-draw", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementOccurrence = "perBatch", movementReasons = new[] { RangeReason },
                usageScope = "game", usageLimit = 1, optional = false, effects = range.ToArray() } } }, name == "paid-loss" ? ["paid", "lost"] : ["paid"]);
            Add(Gate, new() { ["hpSkillGrants"] = new[] { new { id = "actual-live-source", skillId = name == "sequential-loss" ? Douchan : Dulie, minimumHp = name == "sequential-loss" ? 4 : 20 } } });
            Add(Quiet, new() { ["triggers"] = new[] { new { id = "quiet-real-npc", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var sequential = name.StartsWith("sequential", StringComparison.Ordinal); var own = new List<string> { Damage };
            if (sequential) own.AddRange([Gain, name == "sequential-loss" ? Gate : Douchan]); else own.AddRange([RangeGain, name is "unpaid-loss" or "paid-loss" ? Gate : Dulie]);
            if (name == "combined") own.Add(Douchan);
            if (name == "duel") own.Add(BeforeDraw);
            b.AddGeneral(new(Owner, "实际笃烈斗缠拥有者", "", Driver, "wu", sequential || name == "combined" ? 4 : 20, own));
            b.AddGeneral(new(Actor, "实际用牌来源", "", name is "basic" or "dismantle" or "fengzi" ? "standard:none" : Quiet, "wei", 20, name == "fengzi" ? ["ol:fengzi"] : []));
            var cards = Enumerable.Range(0, 24).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToList();
            if (sequential) cards.AddRange([new("standard:duel", Suit.Club, 3), new("standard:duel", Suit.Diamond, 11)]);
            b.AddDeck(new(Deck, "仅原生实体的小牌堆", 0, 0, []) { PhysicalCards = cards });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实笃烈与斗缠", 2, 2, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Actor], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }
}
