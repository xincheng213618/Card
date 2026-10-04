using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryYuFanChecks
{
    private const string Zong = "boundary:zongxuan-current", Zhi = "boundary:zhiyan-current";
    private const string Driver = "fixture:yf-driver", Hp = "fixture:yf-hp", Entry = "fixture:yf-entry";
    private const string Mode = "identity:classic-yf-fixture";
    private const string DiscardReason = "skill-program.fixture:yf-driver.MoveBoundCards";
    private const string TopReason = "skill-program.boundary:zongxuan-current.PutDiscardedCardsOnDrawPileTop";

    public static void ActualOwnAndFirstPreviousDiscardKeepEntitiesOrderAndPrivacy()
    {
        var (g, r) = Create("standard:crossbow"); Play(g);
        Use(g, "equip", [0]); Play(g);
        var hand = g.CreateSnapshot(0).Players[0].Hand[0].Id;
        var equipment = g.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        BoundDiscard(g, "own-two", [hand, equipment]); Reach(g, p => Activation(p, Zong));
        var window = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Candidates.Any(c => c.SkillId == Zong));
        var candidate = window.Candidates.Single(c => c.SkillId == Zong);
        var context = window.Contexts![window.Candidates.ToList().IndexOf(candidate)];
        Require(window.Candidates.Count(c => c.SkillId == Zong) == 1 && context.Facts is { MovedCardCount: 2, FrozenPreviousLivingSeat: 3 } &&
            window.Batch.Movements.Select(m => m.From.Zone).Order().SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Order()),
            "One genuine mixed HE owner batch produces one union opportunity and freezes the predecessor independently of live card counts.");
        Reject(g); Cold(g, r); Activate(g, Zong); Reach(g, Top); PrivateTop(g); Cold(g, r);
        Answer(g, c => c.Cards.SequenceEqual([equipment])); Answer(g, c => c.Cards.SequenceEqual([hand]));
        FinishTop(g); Play(g); Cold(g, r);
        Use(g, "draw-two"); Play(g);
        var draws = g.CardMovements.Where(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)).TakeLast(2).Select(m => m.CardId);
        Require(draws.SequenceEqual([equipment, hand]) && new[] { equipment, hand }.All(id =>
            g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == DiscardReason) == 1 &&
            g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == TopReason) == 1),
            "The chosen first-next-draw order moves each original discarded entity once; returned child execution never repays the original cost.");

        var (j, jr) = Create("standard:indulgence"); Play(j);
        var delayed = j.CreateSnapshot(0).Players[0].Hand[0].Id;
        Accept(j, new PlayCardCommand(0, delayed, [3], j.Revision, P(j)!.PromptId)); Play(j);
        Require(j.CreateSnapshot(0).Players[3].Judgment.Any(c => c.Id == delayed) && Started(j, Zong) == 0,
            "A real delayed-card Use and payment create no synthetic discarded-hand opportunity.");
        Use(j, "discard-field", [3]); Reach(j, p => p.SkillPrompt?.SkillId == Driver);
        Answer(j, c => c.Cards.SequenceEqual([delayed])); Reach(j, p => Activation(p, Zong));
        var fact = E<DiscardNeighborhoodFrozenEvent>(j).Last(e => e.OwnerSeat == 0);
        Require(fact.PreviousLivingSeat == 3 && !fact.IncludesOwnDiscard && fact.IncludesFirstPreviousDiscard &&
            E<ActualTurnFirstOwnedDiscardBatchEvent>(j).Single(e => e.DiscardOwnerSeat == 3).BatchId == fact.BatchId,
            "The exact first actual predecessor judgment discard qualifies; an ordinary Use cleanup neither consumes firstness nor contributes a different entity.");
        Cold(j, jr); Activate(j, Zong); Reach(j, Top); PrivateTop(j); Answer(j, c => c.Cards.SequenceEqual([delayed])); FinishTop(j); Play(j);
        Require(j.CardMovements.Count(m => m.CardId == delayed && m.From == CardLocation.Judgment(3) && m.To == CardLocation.DiscardPile) == 1 &&
            j.CardMovements.Count(m => m.CardId == delayed && m.Reason.Value == TopReason) == 1,
            "The original public judgment entity is reclaimed once without rewriting its actual origin ledger."); Cold(j, jr);

        var (d, dRegistry) = Create(); Play(d);
        var action = d.GetHumanLegalActions().First(a => a.PlayedCardKind == CardKind.Dismantlement && a.TargetSeat == 3 && a.ConversionSource?.SkillId == Driver);
        var material = action.CardId!.Value;
        Accept(d, new PlayCardCommand(0, material, action.TargetSeats, d.Revision, P(d)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
        Reach(d, p => p.Kind == DecisionKind.SelectTargetCard && p.PlayerSeat == 0);
        Require(P(d)!.IsPrivate && P(d)!.Choices.All(c => c.Cards.Count == 0), "The real Dismantlement chooses an opaque current predecessor hand slot before its public discarded identity exists.");
        Cold(d, dRegistry); Answer(d, _ => true); Reach(d, p => Activation(p, Zong));
        var finish = d.CardMovements.Last(m => m.Reason == CardMoveReasons.DismantlementFinished);
        var begin = d.CardMovements.Last(m => m.CardId == finish.CardId && m.Sequence < finish.Sequence);
        Require(begin.From == CardLocation.Hand(3) && begin.To == CardLocation.Processing && finish.From == CardLocation.Processing &&
            finish.CardId != material, "The first-discard producer recovers the exact native Processing origin; the played conversion cost is a different physical entity.");
        Activate(d, Zong); Reach(d, Top); Answer(d, c => c.Cards.SequenceEqual([finish.CardId])); FinishTop(d); Play(d);
        Require(d.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            d.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            E<ActualTurnFirstOwnedDiscardBatchEvent>(d).All(e => e.DiscardOwnerSeat != 0) && Started(d, Zong) == 1 &&
            d.CardMovements.Count(m => m.CardId == finish.CardId && m.Reason.Value == TopReason) == 1,
            "The original converted trick continues and cleans up its own one paid cost; ordinary Use cleanup is excluded from both firstness and reclaimed predecessor cards."); Cold(d, dRegistry);
    }

    public static void FirstnessSurvivesDeclineSourceLossReacquireAndExtraTurn()
    {
        var (g, r) = Create(); Play(g);
        var first = g.CreateSnapshot(3).Players[3].Hand.Take(2).Select(c => c.Id).ToArray();
        BoundDiscard(g, "previous-two", first, [3]); Reach(g, p => Activation(p, Zong));
        var before = E<ActualTurnFirstOwnedDiscardBatchEvent>(g).Single(e => e.DiscardOwnerSeat == 3); Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(g);
        BoundDiscard(g, "previous-one", [g.CreateSnapshot(3).Players[3].Hand[0].Id], [3]); Play(g);
        Require(Started(g, Zong) == 0 && E<ActualTurnFirstOwnedDiscardBatchEvent>(g).Count(e => e.DiscardOwnerSeat == 3) == 1 &&
            E<DiscardNeighborhoodFrozenEvent>(g).Last(e => e.OwnerSeat == 0) is { IncludesFirstPreviousDiscard: false },
            "The first actual batch consumes the predecessor's qualification even when its entire optional opportunity is refused.");
        BoundDiscard(g, "own-one", [g.CreateSnapshot(0).Players[0].Hand[0].Id]); Reach(g, p => Activation(p, Zong)); Activate(g, Zong); Reach(g, Top);
        FinishTop(g); Play(g);
        Require(Started(g, Zong) == 1 && g.CardMovements.All(m => m.Reason.Value != TopReason),
            "Own discards remain unlimited; accepting and selecting the empty subset does not move cards or refund a committed discard."); Cold(g, r);

        var (lost, lr) = Create(); Play(lost); Use(lost, "lose-source"); Play(lost);
        BoundDiscard(lost, "previous-one", [lost.CreateSnapshot(3).Players[3].Hand[0].Id], [3]); Play(lost);
        Require(Started(lost, Zong) == 0 && E<ActualTurnFirstOwnedDiscardBatchEvent>(lost).Any(e => e.DiscardOwnerSeat == 3),
            "A genuine removed source does not erase the public first-discard fact or disclose a hidden grant in that fact."); Cold(lost, lr);
        Use(lost, "reacquire"); Play(lost);
        BoundDiscard(lost, "previous-one", [lost.CreateSnapshot(3).Players[3].Hand[0].Id], [3]); Play(lost);
        Require(Started(lost, Zong) == 0 && E<ActualTurnFirstOwnedDiscardBatchEvent>(lost).Count(e => e.DiscardOwnerSeat == 3) == 1,
            "A new real grant instance in the same actual turn does not renew the already consumed predecessor-first qualification."); Cold(lost, lr);

        var (extra, er) = Create(); Play(extra);
        BoundDiscard(extra, "previous-one", [extra.CreateSnapshot(3).Players[3].Hand[0].Id], [3]); Reach(extra, p => Activation(p, Zong));
        Answer(extra, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(extra); Use(extra, "extra"); Play(extra);
        Accept(extra, new EndPlayPhaseCommand(0, extra.Revision, P(extra)!.PromptId));
        Reach(extra, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 && E<TurnStartedEvent>(extra).Count() == 2);
        BoundDiscard(extra, "previous-one", [extra.CreateSnapshot(3).Players[3].Hand[0].Id], [3]); Reach(extra, p => Activation(p, Zong));
        Require(E<TurnStartedEvent>(extra).Select(e => e.ActorSeat).SequenceEqual([0, 0]) &&
            E<ActualTurnFirstOwnedDiscardBatchEvent>(extra).Where(e => e.DiscardOwnerSeat == 3).Select(e => e.ActualTurnNumber).Distinct().Count() == 2 &&
            E<DiscardNeighborhoodFrozenEvent>(extra).Last(e => e.OwnerSeat == 0).IncludesFirstPreviousDiscard,
            "An issued real extra turn has a distinct actual-turn identity and a fresh predecessor first batch; this is not phase insertion or a skill-instance reset.");
        Cold(extra, er); Answer(extra, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(extra); Cold(extra, er);
    }

    public static void RevealedEquipmentAndNonEquipmentHpChildrenResumeOnce()
    {
        var (g, r) = Create("standard:crossbow", observers: true); Play(g);
        Use(g, "hurt-one", [1]); Play(g); var hp = g.State.Players[1].Hp;
        EndToZhi(g); Activate(g, Zhi); Reach(g, p => p.SkillPrompt?.SkillId == Zhi && p.Choices.Any(c => c.Targets.SequenceEqual([1])));
        Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Zhi);
        var reveal = E<ProgramCardsRevealedEvent>(g).Single(e => e.SkillId == Zhi);
        var card = reveal.Cards.Single();
        Require(g.State.Players[1].Hp == hp + 1 && g.CreateSnapshot(0).Players[1].Equipment.Any(c => c.Id == card.Id) &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.Change.ParentFrameId == root.Id && f.Change.TargetSeat == 1) &&
            g.CardMovements.Count(m => m.CardId == card.Id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) == 1,
            "The actual revealed equipment is used by its recipient, paid once, and the real recovery child pauses the same exact Ending program before its final comparison.");
        Reject(g); g = RestoreAfterCold(g, r); Continue(g); Until(g, () => E<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == Zhi && e.Completed));
        Require(E<RevealedCardHpComparedEvent>(g).Single() is { RevealedKind: CardKind.Crossbow, LosesHp: false } &&
            E<ProgramSkillHpLostEvent>(g).All(e => e.SkillId != Zhi) &&
            E<CardUsedEvent>(g).Count(e => e.CardId == card.Id && e.SourceSeat == 1) == 1,
            "Equipment does not take the non-equipment HP branch, even when recovery makes the live HP unequal; the child return never repeats its use."); Cold(g, r);
        var frozen = JsonSerializer.Serialize(reveal);
        try { ((IList<CardSnapshot>)reveal.Cards)[0] = reveal.Cards[0] with { DisplayName = "mutation" }; } catch (NotSupportedException) { }
        Require(JsonSerializer.Serialize(reveal) == frozen, "The prior public reveal freezes its nested card list before history and observers, and the scalar comparison references that exact immutable entity metadata.");

        var (equal, eqr) = Create(); Play(equal); EndToZhi(equal); Activate(equal, Zhi);
        Reach(equal, p => p.SkillPrompt?.SkillId == Zhi && p.Choices.Any(c => c.Targets.SequenceEqual([0]))); Answer(equal, c => c.Targets.SequenceEqual([0]));
        Until(equal, () => E<RevealedCardHpComparedEvent>(equal).Any());
        Require(E<RevealedCardHpComparedEvent>(equal).Single() is { TargetSeat: 0, LosesHp: false } &&
            E<ProgramSkillHpLostEvent>(equal).All(e => e.SkillId != Zhi), "A self-recipient of a non-equipment draw has equal live HP and loses none."); Cold(equal, eqr);

        var (dying, dr) = Create("standard:peach", observers: true); Play(dying);
        for (var i = 0; i < 5; i++) { Use(dying, "hurt-one", [1]); Play(dying); }
        EndToZhi(dying); Activate(dying, Zhi); Reach(dying, p => p.SkillPrompt?.SkillId == Zhi && p.Choices.Any(c => c.Targets.SequenceEqual([1])));
        Answer(dying, c => c.Targets.SequenceEqual([1])); Reach(dying, p => p.SkillPrompt?.SkillId == Entry);
        root = dying.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Zhi);
        Require(dying.State.Players[1].Hp == 0 && dying.ResolutionStack.OfType<DyingFrame>().Single().ParentFrameId == root.Id &&
            E<ProgramSkillHpLostEvent>(dying).Count(e => e.SkillId == Zhi && e.TargetSeat == 1) == 1 &&
            E<RevealedCardHpComparedEvent>(dying).Single() is { RevealedKind: CardKind.Peach, OwnerHp: 4, TargetHp: 1, LosesHp: true },
            "The non-equipment comparison freezes the actual unequal live HP, loses one once, and owns its genuine Dying-entry child."); dying = RestoreAfterCold(dying, dr); Reject(dying); Continue(dying);
        Until(dying, () => E<CardActionAcceptedEvent>(dying).Any(e => e.Action.EffectiveKind == CardKind.Peach));
        var rescue = E<CardActionAcceptedEvent>(dying).Last(e => e.Action.EffectiveKind == CardKind.Peach).Action;
        dying = RestoreAfterCold(dying, dr);
        Until(dying, () => E<ProgramBindingResolvedEvent>(dying).Any(e => e.SkillId == Zhi && e.Completed));
        Require(rescue.Type == CardActionType.Use && rescue.EffectiveDesignatedTargetSeats.SequenceEqual([1]) && rescue.PhysicalCards.Count == 1 &&
            rescue.PhysicalCards.All(c => dying.CardMovements.Count(m => m.CardId == c.CardId && m.To == CardLocation.Processing) == 1) &&
            E<ProgramSkillHpLostEvent>(dying).Count(e => e.SkillId == Zhi) == 1 && dying.State.Players[1].Hp == 1,
            "The real physical Peach and its HP observer return through the original Ending parent without repeating the draw, comparison or HP cost."); Cold(dying, dr);
    }

    public static void PreviousEndingNativeChoicesAndStrictContracts()
    {
        var (g, r) = Create(); Play(g); EndToZhi(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Reach(g, p => Activation(p, Zhi) && E<TurnStartedEvent>(g).Last().ActorSeat == 3);
        var ending = g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
        var item = ending.Items.Single(i => i.Candidate?.SkillId == Zhi);
        Require(ending.OwnerSeat == 3 && item.Facts?.FrozenPreviousLivingSeat == 3 &&
            E<ProgramBindingResolvedEvent>(g).Where(e => e.SkillId == Zhi).Count() == 1,
            "The current skill offers at its frozen living predecessor's Ending, excluding the intervening two unrelated Ending phases."); Cold(g, r);
        Activate(g, Zhi); Reach(g, p => p.SkillPrompt?.SkillId == Zhi && p.Choices.Any(c => c.Targets.SequenceEqual([0])));
        Answer(g, c => c.Targets.SequenceEqual([0])); Until(g, () => E<RevealedCardHpComparedEvent>(g).Any()); Cold(g, r);

        var (native, nr) = Create(native: true);
        for (var i = 0; i < 100 && native.State.Status != EngineStatus.Completed &&
            !(Started(native, Zong) > 0 && E<RevealedCardHpComparedEvent>(native).Any()); i++) Accept(native, new AdvanceOneStepCommand(native.Revision));
        Require(Started(native, Zong) > 0 && native.CardMovements.Any(m => m.Reason.Value == TopReason) &&
            E<RevealedCardHpComparedEvent>(native).Any() && native.AcceptedCommands.All(c => c is not AnswerPromptCommand),
            "The fixed native source actually accepts the new public zero-cost top ordering and actual Ending draw/use through Start/Advance only; the estimate never reads private hands or deck contents."); Cold(native, nr);
        StrictContracts();
    }

    private static void StrictContracts()
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        string Read(string suffix) { using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("boundary-yu-fan." + suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = Read("rules.json"); var presentation = Read("presentation.json");
        foreach (var invalid in new[] { rules.Replace("\"perOwnerBatch\"", "\"perBatch\""),
            rules.Replace("\"movementDiscardOnly\": true", "\"movementDiscardOnly\": false"),
            rules.Replace("\"sourceZones\": [\"hand\", \"equipment\", \"judgment\"]", "\"sourceZones\": [\"hand\"]"),
            rules.Replace("\"ownOrPreviousLiving\"", "\"otherLiving\""),
            rules.Replace("\"sourceBind\": \"drawn\" }", "\"sourceBind\": \"equipment\" }") })
        {
            var rejected = false; try { SkillProgramCatalog.Load(invalid, presentation); } catch (InvalidOperationException) { rejected = true; }
            Require(invalid != rules && rejected, "New capabilities reject wrong movement origin, occurrence, scope and revealed-entity producer without weakening historical contracts.");
        }
    }

    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static int Started(GameEngine g, string skill) => E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == skill);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Activation(PendingDecision p, string skill) => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Top(PendingDecision p) => p.SkillPrompt?.SkillId == Zong && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "discard-top-finish");
    private static void Activate(GameEngine g, string skill) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill);
    private static void FinishTop(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "discard-top-finish");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void EndToZhi(GameEngine g) { Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Reach(g, p => Activation(p, Zhi)); }
    private static void Use(GameEngine g, string activation, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void BoundDiscard(GameEngine g, string activation, IReadOnlyList<int> ids, IReadOnlyList<int>? targets = null)
    { Use(g, activation, targets); foreach (var id in ids) { Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Cards.SequenceEqual([id]))); Answer(g, c => c.Cards.SequenceEqual([id])); } }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => Until(g, () => P(g) is { } p && predicate(p));
    private static void Until(GameEngine g, Func<bool> predicate)
    { for (var i = 0; i < 240; i++) { if (predicate()) return; Advance(g); } throw new InvalidOperationException("Fixed Yu Fan fixture did not reach its boundary: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && (Activation(p, Zong) || Activation(p, Zhi))) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p?.SkillPrompt?.SkillId == Hp) Continue(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) { RestoreAfterCold(g, r); }
    private static GameEngine RestoreAfterCold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(State(g) == State(restored), "Four private views, first actual discard facts, frozen living predecessor, selected top order, physical costs and owning typed children cold-restore exactly.");
        return restored;
    }
    private static void Reject(GameEngine g) { var p = P(g)!; var state = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && state == State(g), "Rejected input changes no original cost, frozen firstness, private choice, movement or history."); }
    private static void PrivateTop(GameEngine g)
    { var p = P(g)!; Require(p.PlayerSeat == 0 && p.IsPrivate && p.Choices.Where(c => c.Cards.Count > 0).All(c => c.Cards.All(id => g.CreateCardZoneDiagnostics().Any(x => x.CardId == id && x.Location == CardLocation.DiscardPile))), "Top ordering contains only original public actual discards."); for (var s = 1; s < 4; s++) Require(g.CreateSnapshot(s).PendingDecision is null, "Unrelated views do not receive the private selection/order prompt."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(string card = "standard:dodge", bool observers = false, bool native = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(card, observers, native));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = native ? 2 : 6 }, registry);
        Accept(g, new StartGameCommand());
        if (!native) { Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0); Accept(g, new SelectGeneralCommand(0, "fixture:yf-owner", g.Revision, P(g)!.PromptId)); }
        return (g, registry);
    }

    private sealed class Fixture(string card, bool observers, bool native) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-yu-fan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            string Bound(string id, int count, bool other = false) => $$"""
                {"id":"{{id}}","minCards":0,"maxCards":0,"minTargets":{{(other ? 1 : 0)}},"maxTargets":{{(other ? 1 : 0)}},"targetKind":"otherLiving","usesPerTurn":null,"effects":[
                {"op":"selectOwnedCards","target":"{{(other ? "selectedTarget" : "owner")}}","amount":{{count}},"zones":["hand","equipment"],"resultBind":"paid"},
                {"op":"moveBoundCards","target":"owner","sourceBind":"paid","destination":"discardPile","awaitMovementTriggers":true}]}
                """;
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"viewAs":[{"id":"ordinary-dismantlement","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"dismantlement","forPlay":true,"forResponse":false,"singleCardTrickUse":true,"useOnly":true}],"activations":[{{Bound("own-one",1)}},{{Bound("own-two",2)}},{{Bound("previous-one",1,true)}},{{Bound("previous-two",2,true)}},
            {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
            {"id":"hurt-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
            {"id":"draw-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2}]},
            {"id":"discard-field","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["judgment"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
            {"id":"lose-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Zong}}"],"sourceBind":"standard:none"}]},
            {"id":"reacquire","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnSkills","target":"owner","skillIds":["{{Zong}}"]}]},
            {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}]},
            {"id":"{{Hp}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实区域驱动", description = "正常选择、原子弃牌、失技及额外回合" },
                [Hp] = new { name = "实际回复子窗", description = "实际装备使用或实体桃的回复", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Entry] = new { name = "真实濒死入口", description = "可冷恢复的实际体力成本", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = labels }));
            foreach (var id in c.Programs.Keys) b.AddSkill(new(id, id, "真实通用能力夹具") { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            foreach (var owner in new[] { true, false }) b.AddSkill(new(owner ? "fixture:yf-pick-owner" : "fixture:yf-pick-other", "正式固定选将", "公开角色评分")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            b.AddGeneral(new("fixture:yf-owner", "真实纵玄直言来源", "supporter", Zong, "wu", 3,
                native ? [Zhi, "fixture:yf-pick-owner"] : [Zhi, Driver, "fixture:yf-pick-owner"]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:yf-other-{i}", "其他角色", "supporter", "fixture:yf-pick-other", "shu", 6,
                i == 1 && observers ? [Hp, Entry] : []));
            b.AddDeck(new("fixture:yf-deck", "固定真实实体", native ? 6 : 4, native ? 2 : 0, []) {
                PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard(card, Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "真实纵玄及直言", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:yf-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:yf-owner", "fixture:yf-other-1", "fixture:yf-other-2", "fixture:yf-other-3"]));
        }
    }
}
