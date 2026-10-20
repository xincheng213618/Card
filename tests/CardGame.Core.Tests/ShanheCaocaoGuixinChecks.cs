using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheCaocaoGuixinChecks
{
    private const string Skill = StandardShanheS5CaocaoPackage.GuixinId, Boss = StandardShanheS5CaocaoPackage.CaocaoId;
    private const string Owner = "fixture:s5-gx-owner", Peer = "fixture:s5-gx-peer", Deck = "fixture:s5-gx-deck", Driver = "fixture:s5-gx-driver";
    private const string Gain = "fixture:s5-gx-gain", Mutation = "fixture:s5-gx-mutation", Face = "fixture:s5-gx-face", Gate = "fixture:s5-gx-gate", Quiet = "fixture:s5-gx-quiet";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture:s5-gx-production", Boss, 3, 0, 0, 0, 0, [], [], [], [new("classic:cao-cao")], [], new string('d', 64))));

    public static void DamagePointsChooseIndependentNonemptyAreasAndTurnOverCold()
    {
        var s = Scene.Start("points"); s.PrepareRegions();
        var zones = new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment };
        Need(s.CardsAt(1, CardZoneKind.Hand).Length == 4 && s.CardsAt(1, CardZoneKind.Equipment).Length == 1 && s.CardsAt(1, CardZoneKind.Judgment).Length == 1,
            "The first clockwise character really owns four Hand entities, one placed equipment and one placed delayed trick before any random-area choice.");
        s.Use("armor-two"); s.BeginDamage(2); s.AtOffer(); s.Privacy(); s.Cold(); s.Activate(); s.Until(() => s.At(Gain));
        var issued = s.E<RandomAreaAcquisitionStartedEvent>().Single().Receipt; var paid = s.Paid(issued.FrameId).Single(); s.CheckStep(issued, paid);
        Need(issued is { InstructionIndex: 1, OccurrenceIndex: 0, DamageAmount: 2 } && issued.Source is { SkillId: Skill, OwnerSeat: 0 } && issued.OtherSeats.SequenceEqual([1, 2, 3]) &&
            paid.TargetSeat == 1 && paid.SeatIndex == 0 && paid.EligibleZones.SequenceEqual(zones) &&
            paid.Materials.Count == (paid.SelectedZone == CardZoneKind.Hand ? 4 : 1) && paid.ActualObtained == paid.Materials.Count &&
            s.Root.RandomAreaAcquisition is { Stage: RandomAreaAcquisitionStage.MovementChildren, NextSeatIndex: 0, Steps.Count: 1, TurnoverIssued: false } &&
            s.E<DamageAppliedEvent>() is [{ TargetSeat: 0, Amount: 2, ArmorPayment: { ArmorAbsorbed: 2, HpLossAmount: 0 } }],
            "A real two-point damage entirely paid by Armor still offers per-point Guixin; the first accepted point samples uniformly from three current nonempty regions, not six individual cards, and atomically pays every original entity in the selected region before gain children. " + s.Diagnostic());
        s.Cold(); s.Continue(); s.Until(() => s.At(Face));
        var firstSteps = s.Paid(issued.FrameId); foreach (var step in firstSteps) s.CheckStep(issued, step);
        Need(firstSteps.Select(p => p.TargetSeat).SequenceEqual([1, 2]) && firstSteps[1].EligibleZones.SequenceEqual([CardZoneKind.Hand]) && firstSteps[1].ActualObtained == 2 &&
            s.E<RandomAreaAcquisitionSeatSkippedEvent>().Any(e => e.ProgramFrameId == issued.FrameId && e.TargetSeat == 3) && Rng(s.G) == firstSteps[^1].RandomAfter && s.V().IsFaceDown &&
            s.E<RandomAreaAcquisitionTurnoverIssuedEvent>().Single(e => e.ProgramFrameId == issued.FrameId) is { FaceDownBefore: false, FaceDownAfter: true, Prevented: false },
            "The clockwise sweep finishes both populated characters, skips the genuinely empty final seat and only then issues one real native turnover.");
        s.Cold(); s.Continue(); s.AtOffer();
        Need(s.E<RandomAreaAcquisitionCompletedEvent>() is [{ ProgramFrameId: var completed, Regions: 2, TurnoverIssued: true, Cancelled: false }] && completed == issued.FrameId &&
            s.E<RandomAreaAcquisitionStartedEvent>().Length == 1 && s.V().IsFaceDown,
            "The second damage point is an independent real optional offer after the first entire acquisition and turnover have returned.");
        s.Cold(); s.Skip(); s.Play();
        Need(s.E<RandomAreaAcquisitionStartedEvent>().Length == 1 && s.E<RandomAreaAcquisitionTurnoverIssuedEvent>().Length == 1 && s.E<DamageAppliedEvent>().Length == 1,
            "Declining the second point neither repeats the first payment nor flips again or splits the actual damage into two damage occurrences."); s.Cold();

        var originalTurn = s.G.State.TurnNumber; s.EndPlay(); s.Until(() => s.At(Face) && !s.V().IsFaceDown && s.G.State.TurnNumber > originalTurn);
        var skippedTurn = s.G.State.TurnNumber; s.Cold(); s.Continue(); s.Until(() => s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == skippedTurn));
        var skippedFacts = s.G.Events.Select(e => e.Payload).SkipWhile(e => e is not TurnStartedEvent t || t.ActorSeat != 0 || t.TurnNumber != skippedTurn)
            .TakeWhile(e => e is not TurnEndedEvent t || t.ActorSeat != 0 || t.TurnNumber != skippedTurn).ToArray();
        Need(!s.V().IsFaceDown && skippedFacts.OfType<CharacterStateChangedEvent>().Any(e => e.Change.TargetSeat == 0 && e.Change.TurnedOver is { WasFaceDown: true, IsFaceDown: false }) &&
            skippedFacts.OfType<PhaseChangedEvent>().All(e => e.ActorSeat != 0 || e.Phase != TurnPhase.Play) && s.E<RandomAreaAcquisitionTurnoverIssuedEvent>().Length == 1,
            "The real next owner turn turns the general face up and skips its normal Play; this native skipped-turn restoration is not another Guixin turnover."); s.Cold();

        var both = Scene.Start("both"); both.BeginDamage(2); both.AtOffer(); both.Activate(); both.Until(() => both.At(Face)); both.Cold(); both.Continue();
        both.AtOffer(); var emptyRandom = Rng(both.G); both.Activate(); both.Until(() => both.At(Face));
        Need(both.E<RandomAreaAcquisitionStartedEvent>().Select(e => e.Receipt.OccurrenceIndex).SequenceEqual([0, 1]) &&
            both.E<RandomAreaAcquisitionStartedEvent>().Select(e => e.Receipt.FrameId).Distinct().Count() == 2 &&
            both.E<RandomAreaAcquisitionTurnoverIssuedEvent>().Select(e => (e.FaceDownBefore, e.FaceDownAfter)).SequenceEqual([(false, true), (true, false)]) && !both.V().IsFaceDown && Rng(both.G) == emptyRandom,
            "Accepting both actual points owns two independent compound instructions and two native turnovers, even when later seats have become empty.");
        both.Cold(); both.Continue(); both.Play();
        var frames = both.E<RandomAreaAcquisitionStartedEvent>().Select(e => e.Receipt.FrameId).ToArray();
        Need(both.E<RandomAreaAcquisitionCompletedEvent>().Count(e => frames.Contains(e.ProgramFrameId) && !e.Cancelled) == 2 &&
            both.Paid(frames[1]).Length == 0 && both.E<RandomAreaAcquisitionSeatSkippedEvent>().Count(e => e.ProgramFrameId == frames[1]) == 3,
            "A fully empty second sweep consumes no random-region step and still performs its independently accepted turnover once."); both.Cold();
        VerifyLoaderBoundaries();
    }

    public static void SequentialAreaChildrenUseCurrentZonesAndPreservePaidLossCold()
    {
        var s = Scene.Start("children"); s.PrepareRegions(); var lion = s.CardsAt(1, CardZoneKind.Equipment).Single().CardId;
        s.BeginDamage(1); s.AtOffer(); s.Activate(); s.Until(() => s.At(Mutation)); var issued = s.E<RandomAreaAcquisitionStartedEvent>().Single().Receipt;
        var first = s.Paid(issued.FrameId).Single(); s.CheckStep(issued, first);
        Need(first is { TargetSeat: 1, SelectedZone: CardZoneKind.Equipment, ActualObtained: 1 } && first.Materials.Single().CardId == lion &&
            s.E<RecoveryAppliedEvent>().Any(e => e.TargetSeat == 1 && e.Amount == 1 && e.RemainingHp == 19) && s.V(1).Hp == 19 &&
            s.Paid(issued.FrameId).Length == 1 && s.Root.RandomAreaAcquisition!.NextSeatIndex == 0 && s.CardsAt(3, CardZoneKind.Hand).Length == 0,
            "The real Silver Lion leaves the first person's equipment and its native recovery fully drains before this actual gained-card child; no later character has been visited yet.");
        s.Privacy(); s.Cold(); s.Continue(); s.Until(() => s.P?.Choices.Any(c => c.Targets.SequenceEqual([3])) == true); s.Answer(c => c.Targets.SequenceEqual([3]));
        s.Until(() => s.P?.Choices.Any(c => c.Cards.SequenceEqual([lion])) == true);
        Need(s.V().Hand is [{ Id: var original }] && original == lion, "The owned-card choice can only select the original actually acquired Lion entity.");
        s.Answer(c => c.Cards.SequenceEqual([lion]));
        s.Until(() => s.At(Mutation) && s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "changed"));
        Need(s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == lion).Location == CardLocation.Hand(3) && s.Paid(issued.FrameId).Length == 1 &&
            s.Root.RandomAreaAcquisition!.OtherSeats.SequenceEqual([1, 2, 3]) && s.Root.RandomAreaAcquisition.NextSeatIndex == 0,
            "A genuine select-owned-card and selected-target gift puts that same paid entity into the originally empty later seat, while the original clockwise roster and pending first-step return remain frozen.");
        s.Cold(); s.Continue(); s.Until(() => s.At(Gain) && s.Paid(issued.FrameId).Length == 2); s.Cold(); s.Continue();
        s.Until(() => s.At(Gain) && s.Paid(issued.FrameId).Length == 3); var last = s.Paid(issued.FrameId).Last(); s.CheckStep(issued, last);
        Need(last is { TargetSeat: 3, SelectedZone: CardZoneKind.Hand, ActualObtained: 1 } && last.EligibleZones.SequenceEqual([CardZoneKind.Hand]) && last.Materials.Single().CardId == lion &&
            s.Paid(issued.FrameId).Sum(e => e.ActualObtained) == 4 && s.G.CardMovements.Count(m => m.CardId == lion && m.To == CardLocation.Hand(0) && m.Reason.Value.Contains(".random-area-acquisition.", StringComparison.Ordinal)) == 2,
            "The last character is evaluated only when reached: its newly nonempty current Hand is sampled and the same real Lion can legitimately enter the owner again from that different person's area.");
        s.Cold(); s.Continue(); s.Until(() => s.At(Face)); s.Cold(); s.Continue(); s.Play();
        Need(s.E<RandomAreaAcquisitionCompletedEvent>() is [{ Regions: 3, ActualObtained: 4, TurnoverIssued: true, Cancelled: false }] &&
            s.E<RandomAreaAcquisitionTurnoverIssuedEvent>().Length == 1, "Every real child returns before the next person, and the final entire sweep flips once without globally deduplicating legitimate later gains."); s.Cold();

        var loss = Scene.Start("loss"); loss.BeginDamage(1); loss.AtOffer(); loss.Activate(); loss.Until(() => loss.At(Mutation)); var paidRoot = loss.E<RandomAreaAcquisitionStartedEvent>().Single().Receipt;
        loss.Cold(); loss.Continue(); loss.Until(() => loss.At(Mutation) && loss.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "lost"));
        Need(loss.V().Hp == 18 && !loss.HasSkill() && loss.Paid(paidRoot.FrameId).Length == 1 && !loss.V().IsFaceDown,
            "A real first-region gain child withdraws the exact HP-dependent source only after that complete batch was paid, while later people and turnover remain unpaid.");
        loss.Cold(); loss.Continue(); loss.Until(() => loss.At(Face)); loss.Cold(); loss.Continue(); loss.Play();
        Need(loss.Paid(paidRoot.FrameId).Select(e => e.TargetSeat).SequenceEqual([1, 2]) && loss.E<RandomAreaAcquisitionCompletedEvent>() is [{ Cancelled: false, TurnoverIssued: true, Regions: 2 }] &&
            loss.V().IsFaceDown && !loss.HasSkill(), "A living paid owner losing Guixin still completes this exact clockwise sweep and one turnover; the independently paid compound does not restart or vanish."); loss.Cold();
    }

    public static void PaidOwnerDeathAndConditionalRecipesStayIsolatedCold()
    {
        var s = Scene.Start("death"); s.BeginDamage(1); s.AtOffer(); s.Activate(); s.Until(() => s.At(Mutation));
        var issued = s.E<RandomAreaAcquisitionStartedEvent>().Single().Receipt; var first = s.Paid(issued.FrameId).Single(); s.CheckStep(issued, first); s.Cold(); s.Continue();
        s.Until(() => !s.V().IsAlive && s.E<RandomAreaAcquisitionCompletedEvent>().Any(e => e.ProgramFrameId == issued.FrameId) && s.G.ResolutionStack.All(f => f.Id != issued.FrameId));
        Need(s.G.State.Winner == Winner.None && s.V(1).IsAlive && s.E<PlayerDiedEvent>().Count(e => e.VictimSeat == 0) == 1 &&
            s.E<RandomAreaAcquisitionCompletedEvent>() is [{ ProgramFrameId: var completed, Regions: 1, ActualObtained: 1, TurnoverIssued: false, Cancelled: true }] && completed == issued.FrameId &&
            s.Paid(issued.FrameId).Length == 1 && s.E<RandomAreaAcquisitionTurnoverIssuedEvent>().Length == 0 && !s.V().IsFaceDown &&
            s.E<RecoveryAppliedEvent>().Length == 0 && s.G.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.From == CardLocation.DrawPile) == 0 &&
            s.CardsAt(2, CardZoneKind.Hand).Length == 2 && s.CardsAt(3, CardZoneKind.Hand).Length == 0,
            "The genuine gain-child death drains its already-paid first region while a real ally keeps the battle alive; no unvisited character, dead-owner Draw, remaining acquisition or final turnover is invented. " + s.Diagnostic()); s.Cold();
        VerifyActualRecipes();
    }

    private static void VerifyActualRecipes()
    {
        var old = StandardShanheCampaignCatalog.CreateDefinition(3, StandardShanheCampaignCatalog.LocalEditionId);
        Need(old.Nodes.SelectMany(n => n.Encounters ?? []).All(e => e.Enemies.All(m => m.GeneralId != Boss)), "The complete S5 Guixin-only boss is absent from 2024.");
        for (var tier = 1; tier <= 3; tier++)
        {
            var definition = StandardShanheCampaignCatalog.CreateDefinition(tier, StandardShanheCampaignCatalog.Returning2026EditionId);
            var encounter = definition.Nodes.Single(n => n.Id == "huanyuan").Encounters!.Single(e => e.Id == "huanyuan:caocao-s5");
            Need(encounter.Enemies is [{ GeneralId: Boss, Hp: null, MaxHpBonus: 0, InitialHandBonus: 0, DrawBonus: 0, SlashBonus: 0, StartingHand: null }] && (encounter.Allies?.Count ?? 0) == 0,
                "All three local tiers retain the raw three-HP single-skill source form, without higher-band Feiying, growth, fixed equipment or invented allies.");
            var setup = new ShanheBattleSetup(83, 1, "huanyuan", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], encounter.Enemies, [], ShanheCampaignContent.GetContentHash(definition));
            var r = StandardShanheCampaignCatalog.CreateBattleRegistry(setup); var g = StandardShanheCampaignCatalog.CreateBattle(setup); Need(g.Submit(new StartGameCommand()).Accepted, "The actual conditional Cao Cao recipe must start.");
            var npc = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == Boss);
            Need(npc is { Hp: 3, MaxHp: 3, HandCount: 4, Armor: 0 } && npc.Equipment.Count == 0 && npc.Skills?.Select(k => k.ContentId).SequenceEqual([Skill]) == true,
                "The actual encounter starts the native three-HP boss with only new Guixin and four ordinary opening cards.");
            Need(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "The actual package, startup and Guixin replay identically.");
            var plain = setup with { EnemyRoster = [new("classic:cao-cao")] }; var extra = plain with { ExtraSkillIds = [Skill] };
            Need(!StandardShanheCampaignCatalog.CreateBattleRegistry(plain).Skills.ContainsKey(Skill) && StandardShanheCampaignCatalog.CreateBattleRegistry(extra).Skills.ContainsKey(Skill) &&
                !StandardShanheCampaignCatalog.CreateBattleRegistry(plain).Generals.ContainsKey(Boss), "Warm alias, actual extra-skill and bare recipes keep their conditional package/cache boundaries in both directions.");
        }
    }

    private static void VerifyLoaderBoundaries()
    {
        string Read(string suffix) { var a = typeof(StandardContentPackage).Assembly; using var stream = a.GetManifestResourceStream(a.GetManifestResourceNames().Single(n => n.EndsWith('.' + suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = Read("shanhe-s5-caocao.rules.json"); var presentation = Read("shanhe-s5-caocao.presentation.json"); _ = SkillProgramCatalog.Load(rules, presentation);
        foreach (var change in new Action<JsonObject>[] { t => t["damageOccurrence"] = "perDamage", t => t["optional"] = false, t => t["subject"] = "damageSource",
            t => t["effects"]![0]!["zones"] = new JsonArray("hand", "equipment"), t => t["effects"]![0]!["target"] = "selectedTarget" })
        {
            var copy = JsonNode.Parse(rules)!; change(copy["skills"]![0]!["triggers"]![0]!.AsObject()); var rejected = false;
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The compound preserves its strict optional per-damage-point owner HEJ contract rather than accepting a different occurrence, owner, area domain or recipient.");
        }
    }

    private static bool Frozen<T>(IReadOnlyList<T> items) => items is IList<T> { IsReadOnly: true };
    private static uint Rng(GameEngine g) => ((DeterministicRandom)typeof(GameEngine).GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!).State;
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, g.PlayerCount).Select(i => SnapshotJson.Serialize(g.CreateSnapshot(i))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
        g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Random = Rng(g), Checkpoint = GameCheckpointJson.Serialize(g.CreateCheckpoint()) });

    private sealed class Scene(GameEngine game, ContentRegistry registry, string name)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public ProgramSkillFrame Root => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.RandomAreaAcquisition is not null);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public RandomAreaAcquisitionStepReceipt[] Paid(long frame) => E<RandomAreaAcquisitionRegionPaidEvent>().Where(e => e.ProgramFrameId == frame).Select(e => e.Step).ToArray();
        public CardZoneDiagnostic[] CardsAt(int seat, CardZoneKind zone) => G.CreateCardZoneDiagnostics().Where(c => c.Location == new CardLocation(zone, seat)).OrderBy(c => c.ZoneIndex).ToArray();
        public bool HasSkill() => V().Skills?.Any(s => s.ContentId == Skill) == true;
        public bool At(string skill) => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.ContainsKey("option-id"));
        public void Accept(GameCommand c) { var r = G.Submit(c); if (!r.Accepted) throw new InvalidOperationException((r.Error?.Message ?? "Rejected real Guixin command.") + " " + Diagnostic()); }
        public void Answer(Func<PromptChoice, bool> choose) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(choose).Id, G.Revision));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void AtOffer() => Until(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void BeginDamage(int amount) => Accept(new UseProgramSkillCommand(0, Driver, "hurt-" + amount, [], [2], G.Revision, P!.PromptId));
        public void Use(string id) { Accept(new UseProgramSkillCommand(0, Driver, id, [], [], G.Revision, P!.PromptId)); Play(); }
        public void PrepareRegions()
        {
            var lion = V().Hand.Single(c => c.Kind == CardKind.SilverLion).Id;
            Accept(new UseProgramSkillCommand(0, Driver, "place", [lion], [1], G.Revision, P!.PromptId)); Play();
            if (name != "points") return;
            var card = G.GetHumanLegalActions().First(a => a.CardId is { } id && V().Hand.Any(c => c.Id == id && c.Kind == CardKind.Indulgence) && a.TargetSeats.SequenceEqual([1]));
            Accept(new PlayCardCommand(0, card.CardId!.Value, card.TargetSeats, G.Revision, P!.PromptId, card.PlayedCardKind, card.TargetCardId)); Play();
        }
        public void CheckStep(ProgramRandomAreaAcquisitionReceipt origin, RandomAreaAcquisitionStepReceipt step)
        {
            var random = new DeterministicRandom(unchecked((int)step.RandomBefore)); var selected = step.EligibleZones[random.Next(step.EligibleZones.Count)];
            Need(step.SelectedZone == selected && step.RandomAfter == random.State && step.MovementBatchId > 0 && step.SequenceAfter > step.SequenceBefore && step.Materials.Count > 0 &&
                step.Materials.Select(c => c.CardId).Order().SequenceEqual(step.Movements.Select(m => m.CardId).Order()) && step.ActualObtained == step.Materials.Count &&
                step.Movements.All(m => m.From == new CardLocation(step.SelectedZone, step.TargetSeat) && m.To == CardLocation.Hand(0) &&
                    m.Reason.Value == $"skill-program.{Skill}.random-area-acquisition.{origin.FrameId}.{step.SeatIndex}.obtain" &&
                    G.CardMovements.Count(actual => actual.Sequence == m.Sequence && actual == m) == 1) &&
                Frozen(step.EligibleZones) && Frozen(step.Materials) && Frozen(step.Movements) && Frozen(origin.OtherSeats),
                "Each immutable paid step freezes exactly one Next(nonempty-region-count), its RNG transition, original whole-region materials and atomic native movement interval. " + Diagnostic());
        }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Until(Func<bool> done)
        {
            for (var i = 0; i < 240; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) { Continue(); continue; }
                    if (p.Kind == DecisionKind.RescueDying) { Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die"); continue; }
                    if (p.Kind is DecisionKind.Nullification or DecisionKind.RespondSlash or DecisionKind.RespondDodge) { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Skip(); continue; }
                    throw new InvalidOperationException("Unexpected real Guixin boundary: " + Diagnostic());
                }
                Need(G.State.Winner == Winner.None, "The small Guixin fixture must stay live. " + Diagnostic()); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded clockwise region/child/return was not reached: " + Diagnostic());
        }
        public void Privacy() => Need(Enumerable.Range(0, G.PlayerCount).All(i => G.CreateSnapshot(i).Players.Where(p => p.Seat != i).All(p => p.Hand.Count == 0)) &&
            Enumerable.Range(1, G.PlayerCount - 1).All(i => G.CreateSnapshot(i).PendingDecision is null) && !SnapshotJson.Serialize(G.CreateSnapshot(1)).Contains("RandomAreaAcquisition", StringComparison.Ordinal),
            "Random-region candidates and trusted materials never leak to player views; only the true owner sees its actual optional or owned-card prompt.");
        public void Cold() { var before = State(G); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State(G) == before, "Cold replay preserves clockwise roster, exact per-point/RNG choice, entire paid regions, native leave-equipment/gain/turnover children and current-zone timing without re-paying."); }
        public string Diagnostic() => JsonSerializer.Serialize(new { name, G.State.TurnNumber, G.State.Winner,
            Seats = G.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.IsAlive, p.HandCount, p.IsFaceDown }), Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId, Choices = P.Choices.Select(c => c.Id) },
            Started = E<RandomAreaAcquisitionStartedEvent>().Select(e => new { e.Receipt.FrameId, e.Receipt.OccurrenceIndex, e.Receipt.DamageAmount }),
            Paid = E<RandomAreaAcquisitionRegionPaidEvent>().Select(e => new { e.ProgramFrameId, e.Step.TargetSeat, e.Step.SelectedZone, e.Step.ActualObtained, e.Step.RandomBefore, e.Step.RandomAfter }),
            Completed = E<RandomAreaAcquisitionCompletedEvent>(), Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind }) });
        public static Scene Start(string name)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(name));
            ShanheStartingCard[] own = name == "points" ? [new(CardKind.SilverLion, Suit.Club, 1), new(CardKind.Indulgence, Suit.Heart, 6)] : name == "children" ? [new(CardKind.SilverLion, Suit.Club, 1)] : [];
            ShanheStartingCard[] first = name == "children" ? [] : Enumerable.Range(1, name == "points" ? 4 : 1).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Heart, i)).ToArray();
            var setup = new ShanheBattleSetup(83, 1, "fixture:s5-gx-" + name, Owner, 20, 0, 0, 0, 0, [], own, [],
                [new(Peer) { StartingHand = [new(CardKind.Dodge, Suit.Club, 8), new(CardKind.Dodge, Suit.Spade, 9)] }, new(Peer) { StartingHand = [] }],
                [new(Peer, Hp: name == "children" ? 18 : 20) { StartingHand = first }], new string('e', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scene(g, r, name); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }

    private sealed class Fixture(string name) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-s5-guixin", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            b.AddSkill(Production.Value.GetSkill(Skill)); var defs = new List<Dictionary<string, object>>(); var labels = new Dictionary<string, object>();
            object Pause(string id) => new { op = "chooseOption", target = "owner", resultBind = id, options = new[] { new { id } } };
            void Add(string id, Dictionary<string, object> value, params string[] options) { value["id"] = id; value["revision"] = 1; defs.Add(value); labels[id] = new { name = id, description = "真实按区随机、完整批次与逐人子流程", optionLabels = options.ToDictionary(x => x, x => x) }; }
            var acts = new List<object>();
            foreach (var n in new[] { 1, 2 }) acts.Add(new { id = "hurt-" + n, minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null,
                effects = new[] { new { op = "damage", target = "owner", sourceRef = new { kind = "selectedTarget" }, amount = n } } });
            acts.Add(new { id = "place", minCards = 1, maxCards = 1, sourceZones = new[] { "hand" }, cardCategories = new[] { "equipment" }, minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null,
                effects = new object[] { new { op = "captureSelectedCards", target = "owner", resultBind = "gear" }, new { op = "placeSelectedEquipment", target = "selectedTarget", sourceBind = "gear" } } });
            acts.Add(new { id = "armor-two", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "grantArmor", target = "owner", amount = 2 } } });
            Add(Driver, new() { ["activations"] = acts });
            Add(Gain, new() { ["triggers"] = new[] { new { id = "each-actual-region-child", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementOccurrence = "perBatch", optional = false, priority = -100, effects = new[] { Pause("continue") } } } }, "continue");
            var mutation = new List<object> { Pause("paid") };
            if (name == "children") mutation.AddRange([new { op = "selectTarget", target = "owner", targetKind = "otherLiving" },
                new { op = "selectAndMoveOwnedCard", target = "owner", chooserRef = new { kind = "owner" }, cardOwnerRef = new { kind = "owner" }, zones = new[] { "hand" }, count = 1,
                    destination = "selectedTargetHand", targetRef = new { kind = "selectedTarget" }, awaitMovementTriggers = true }, Pause("changed")]);
            if (name is "loss" or "death") mutation.Add(new { op = "loseHp", target = "owner", amount = name == "death" ? 20 : 1 });
            if (name == "loss") mutation.Add(Pause("lost"));
            Add(Mutation, new() { ["triggers"] = new[] { new { id = "first-real-gain-mutation", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementOccurrence = "perBatch",
                optional = false, priority = 100, usageScope = "game", usageLimit = 1, effects = mutation.ToArray() } } }, name == "children" ? ["paid", "changed"] : name == "loss" ? ["paid", "lost"] : ["paid"]);
            Add(Face, new() { ["triggers"] = new[] { new { id = "actual-turnover-child", window = "characterTurnedOver", subject = "owner", optional = false, effects = new[] { Pause("turned") } } } }, "turned");
            Add(Gate, new() { ["hpSkillGrants"] = new[] { new { id = "live-after-incoming-one", skillId = Skill, minimumHp = 19 } } });
            Add(Quiet, new() { ["triggers"] = new[] { new { id = "quiet-real-peer", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "真实区域归心拥有者", "", Driver, "wei", 20, name is "children" or "loss" or "death" ? [Gain, Face, Mutation, name == "loss" ? Gate : Skill] : [Gain, Face, Skill]));
            b.AddGeneral(new(Peer, "真实顺时针其他角色", "", Quiet, "wei", 20));
            var standard = ContentRegistry.Build(new StandardContentPackage());
            foreach (var kind in new[] { CardKind.SilverLion, CardKind.Indulgence })
                if (standard.Cards.Values.All(c => c.LegacyKind != kind)) { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:s5-gx-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            b.AddDeck(new(Deck, "固定原生实体与零自然摸牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实逐点区域归心", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
