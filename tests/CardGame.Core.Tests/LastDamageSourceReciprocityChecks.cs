using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LastDamageSourceReciprocityChecks
{
    private const string Skill = "fixture:last-damage-source";
    private const string StateId = "last-source";
    private const string Driver = "fixture:last-damage-source-driver";
    private const string Retire = "fixture:last-damage-source-retire";
    private const string Inert = "fixture:last-damage-source-inert";
    private const string GainChild = "fixture:last-damage-source-gain-child";
    private const string DiscardChild = "fixture:last-damage-source-discard-child";
    private const string Mode = "fixture:last-damage-source-mode";
    private static string Reason(bool draw) => $"skill-program.{Skill}.last-damage-source.{(draw ? "draw" : "discard")}";

    public static void LatestSourceAndPhysicalRegrantKeepExactNativeReciprocity()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        LatestSourceAndNativeEquipmentPayment(Start(registry), registry);
        ExcludedDamageAndPhysicalRegrant(Start(registry), registry);
    }

    private static void LatestSourceAndNativeEquipmentPayment(GameEngine g, ContentRegistry registry)
    {
        foreach (var seat in new[] { 1, 2, 3 }) { UseDriver(g, "collect", [seat]); Play(g); }
        var hand = g.CreateSnapshot(0).Players[0].Hand;
        var armor = hand.First(c => c.Kind == CardKind.BaguaFormation).Id;
        UseDriver(g, "equip-other", [1], [armor]); Play(g);
        var slash = g.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.Slash).Id;
        UseDriver(g, "give", [2], [slash]); Play(g);
        Require(g.CreateSnapshot(1).Players[1].Hand.Count == 0 &&
            g.CreateSnapshot(1).Players[1].Equipment.Select(c => c.Id).SequenceEqual([armor]) &&
            g.CreateSnapshot(2).Players[2].Hand.Select(c => c.Id).SequenceEqual([slash]),
            "Real hand gifts and captured equipment placement leave the first source exactly one installed armor and the second source exactly one hand entity.");

        UseDriver(g, "incoming", [1]); Reach(g, IsDiscardChoice);
        var root = Root(g); var receipt = root.LastDamageSourceReciprocity!; var firstFrame = root.Id;
        AssertOrigin(g, root, LastDamageSourceReciprocityDirection.DiscardSource, 1, 0, 1);
        Require(receipt is { Stage: LastDamageSourceReciprocityStage.ChoosingDiscard, MovementIssued: false } &&
            receipt.EligibleMaterials is [{ CardId: var eligibleId, From: var from }] && eligibleId == armor && from == CardLocation.Equipment(1) &&
            P(g) is { PlayerSeat: 1, IsPrivate: true } p && p.Choices is [var choice] && choice.Cards.SequenceEqual([armor]) && choice.Targets.Count == 0 &&
            E<LastDamageSourceRecordedEvent>(g) is [{ SourceSeat: 1, PreviousSourceSeat: null, OwnerSeat: 0, StateId: StateId }],
            "The first real foreign damage records its actual source and publishes exactly that source's own mandatory equipment payment, without paying before its native choice.");
        Frozen(receipt.EligibleMaterials); Private(g);
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices.Single().Id, g.Revision));
        g = Cold(g, registry);
        Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, p => p.SkillPrompt?.SkillId == DiscardChild);
        var paid = Root(g).LastDamageSourceReciprocity!;
        Require(paid is { Stage: LastDamageSourceReciprocityStage.MovementChildren, MovementIssued: true, ActualCount: 1 } &&
            paid.PaidMaterial is { CardId: var paidId, From: var paidFrom } && paidId == armor && paidFrom == CardLocation.Equipment(1) &&
            !E<LastDamageSourceReciprocityCompletedEvent>(g).Any(e => e.FrameId == firstFrame),
            "Native AI pays the exact installed armor once; the owning damage receipt remains outstanding until its real equipment-loss observer returns.");
        AssertMovementChild(g, DiscardChild, firstFrame, false, CardLocation.Equipment(1), CardLocation.DiscardPile);
        Private(g); g = Cold(g, registry); Play(g); AssertSettled(g, firstFrame, false, armor, CardLocation.Equipment(1));

        var recordsBefore = E<LastDamageSourceRecordedEvent>(g).Length;
        UseDriver(g, "outgoing", [1]); Reach(g, p => p.SkillPrompt?.SkillId == GainChild);
        root = Root(g); var drawFrame = root.Id; var instance = root.SkillInstanceId;
        AssertOrigin(g, root, LastDamageSourceReciprocityDirection.DrawOwner, 0, 1, 1);
        Require(root.LastDamageSourceReciprocity is { MovementIssued: true, ActualCount: 1, Stage: LastDamageSourceReciprocityStage.MovementChildren } &&
            E<LastDamageSourceRecordedEvent>(g).Length == recordsBefore && !E<LastDamageSourceReciprocityCompletedEvent>(g).Any(e => e.FrameId == drawFrame),
            "Actual damage to the remembered source pays the owner's one card before its native gain child, without overwriting incoming-source memory.");
        AssertMovementChild(g, GainChild, drawFrame, true, CardLocation.DrawPile, CardLocation.Hand(0));
        Private(g);
        var stale = P(g)!;
        Reject(g, new AnswerPromptCommand(1, stale.PromptId, stale.Choices.Single().Id, g.Revision));
        Reject(g, new AnswerPromptCommand(0, stale.PromptId, new ChoiceId("unpublished-last-source-choice"), g.Revision));
        g = Cold(g, registry); Continue(g); Play(g);
        AssertSettled(g, drawFrame, true);
        Reject(g, new AnswerPromptCommand(0, stale.PromptId, stale.Choices.Single().Id, g.Revision));

        var secondFrame = Incoming(g, 2);
        AssertSettled(g, secondFrame, false, slash, CardLocation.Hand(2));
        var records = E<LastDamageSourceRecordedEvent>(g);
        Require(records.Length == 2 && records[1] is { SourceSeat: 2, PreviousSourceSeat: 1, Amount: 1 } &&
            records[1].DamageFrameId != records[0].DamageFrameId && records.All(e => e.ActualTurnNumber == g.State.TurnNumber && e.ActualTurnOwnerSeat == 0),
            "A different actual incoming source replaces the previous stable owner/skill/state memory in the same real turn.");
        var draws = DrawCount(g); var eligible = E<LastDamageSourceReciprocityEligibleEvent>(g).Length;
        var actual = E<DamageAppliedEvent>(g).Length;
        UseDriver(g, "outgoing", [1]); Play(g);
        Require(E<DamageAppliedEvent>(g).Length == actual + 1 && E<DamageAppliedEvent>(g).Last() is { SourceSeat: 0, TargetSeat: 1, Amount: 1 } &&
            DrawCount(g) == draws && E<LastDamageSourceReciprocityEligibleEvent>(g).Length == eligible,
            "The old source really takes damage after replacement, but receives no stale outgoing eligibility or extra draw.");
        UseDriver(g, "outgoing", [2]); Play(g);
        var latestDraw = E<LastDamageSourceReciprocityStartedEvent>(g).Last(e => e.Direction == LastDamageSourceReciprocityDirection.DrawOwner);
        Require(DrawCount(g) == draws + 1 && latestDraw is { SourceSeat: 0, TargetSeat: 2, RecordedSourceSeat: 2, ParticipantSeat: 0 } &&
            latestDraw.Source.SkillInstanceId == instance && E<LastDamageSourceRecordedEvent>(g).Length == 2,
            "Damage to the latest actual source draws exactly once from the same runtime grant; outgoing damage never records itself as a new incoming source.");
        AssertSettled(g, latestDraw.FrameId, true); AssertConservedAndNative(g); _ = Cold(g, registry);
    }

    private static void ExcludedDamageAndPhysicalRegrant(GameEngine g, ContentRegistry registry)
    {
        var firstFrame = Incoming(g, 1); AssertSettled(g, firstFrame, false);
        var original = E<LastDamageSourceReciprocityStartedEvent>(g).Single().Source.SkillInstanceId;
        var records = E<LastDamageSourceRecordedEvent>(g).Length;
        var remembered = E<LastDamageSourceRecordedEvent>(g).Single();
        var eligible = E<LastDamageSourceReciprocityEligibleEvent>(g).Length;
        var started = E<LastDamageSourceReciprocityStartedEvent>(g).Length;
        var damageBefore = E<DamageAppliedEvent>(g).Length;
        UseDriver(g, "self", []); Play(g);
        UseDriver(g, "source-less", []); Play(g);
        var excluded = E<DamageAppliedEvent>(g).Skip(damageBefore).ToArray();
        Require(excluded is [{ SourceSeat: 0, TargetSeat: 0, Amount: 1, SourceLess: false }, { TargetSeat: 0, Amount: 1, SourceLess: true }] &&
            E<LastDamageSourceRecordedEvent>(g).Length == records && E<LastDamageSourceReciprocityEligibleEvent>(g).Length == eligible &&
            E<LastDamageSourceReciprocityStartedEvent>(g).Length == started && g.CreateSnapshot(0).Players[0].Hp == 2,
            "Real self damage and real source-less damage each reduce HP, while neither overwrites the last foreign source nor manufactures either reciprocity direction.");
        UseDriver(g, "outgoing", [1]); Play(g);
        var beforeLossDraw = E<LastDamageSourceReciprocityStartedEvent>(g).Last();
        Require(beforeLossDraw.Direction == LastDamageSourceReciprocityDirection.DrawOwner && beforeLossDraw.RecordedSourceSeat == 1 &&
            beforeLossDraw.Source.SkillInstanceId == original, "The original remembered foreign source still earns the owner draw after both excluded real damage events.");
        AssertSettled(g, beforeLossDraw.FrameId, true);

        Accept(g, new UseProgramSkillCommand(0, Retire, "replace-source", [], [], g.Revision, P(g)!.PromptId)); Play(g);
        var replacement = E<ProgramOwnerSkillsReplacedEvent>(g).Single();
        Require(replacement.OwnerSeat == 0 && replacement.SkillId == Retire && replacement.GrantedSkillId == Inert &&
            replacement.LostSkillIds.SequenceEqual([Skill, Retire]) &&
            g.CreateSnapshot(0).Players[0].Skills!.All(s => s.Id != Skill && s.Id != Retire) &&
            E<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 0 && e.SourceSkillId == Retire && e.SkillIds.SequenceEqual([Inert])),
            "An independent terminal activation truly removes both original runtime grants and acquires an inert replacement; this is physical RemoveGrant rather than temporary suppression.");
        var absentStarted = E<LastDamageSourceReciprocityStartedEvent>(g).Length;
        var absentDamage = E<DamageAppliedEvent>(g).Length;
        UseDriver(g, "incoming", [2]); Play(g);
        Require(E<DamageAppliedEvent>(g).Length == absentDamage + 1 && E<DamageAppliedEvent>(g).Last() is { SourceSeat: 2, TargetSeat: 0, Amount: 1, SourceLess: false } &&
            g.CreateSnapshot(0).Players[0].Hp == 1 && E<LastDamageSourceRecordedEvent>(g).Length == records &&
            E<LastDamageSourceReciprocityEligibleEvent>(g).Length == eligible + 1 && E<LastDamageSourceReciprocityStartedEvent>(g).Length == absentStarted,
            "Another genuine incoming source while the skill is physically absent deals damage but neither updates old memory nor starts a discarded-source payment.");
        UseDriver(g, "recover", []); Play(g); UseDriver(g, "acquire", []); Play(g);
        Require(g.CreateSnapshot(0).Players[0].Hp == 5 && g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == Skill) &&
            E<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.SequenceEqual([Skill, Retire])) == 2,
            "Real recovery returns the custom Lord to HP5, and a second actual acquisition reinstalls the tested skill after the recorded physical removal.");
        UseDriver(g, "outgoing", [1]); Reach(g, p => p.SkillPrompt?.SkillId == GainChild);
        var resumed = Root(g); var resumedFrame = resumed.Id;
        AssertOrigin(g, resumed, LastDamageSourceReciprocityDirection.DrawOwner, 0, 1, 1);
        var removalSequence = g.Events.Single(e => e.Payload is ProgramOwnerSkillsReplacedEvent r && r.ResolutionId == replacement.ResolutionId).Sequence;
        var absentDamageSequence = g.Events.Single(e => e.Payload is DamageAppliedEvent { SourceSeat: 2, TargetSeat: 0, Amount: 1, SourceLess: false }).Sequence;
        var acquisitions = g.Events.Where(e => e.Payload is SkillsAcquiredEvent a && a.PlayerSeat == 0 && a.SourceSkillId == Driver &&
            a.SkillIds.SequenceEqual([Skill, Retire])).ToArray();
        var resumedSequence = g.Events.Single(e => e.Payload is LastDamageSourceReciprocityStartedEvent s && s.FrameId == resumedFrame).Sequence;
        var retained = E<LastDamageSourceRecordedEvent>(g);
        Require(original == $"acquired:{Driver}:{Skill}" && resumed.SkillInstanceId == original &&
            resumed.LastDamageSourceReciprocity!.Source.SkillInstanceId == resumed.SkillInstanceId &&
            retained.Length == records && retained.Single().SourceSeat == 1 && retained.SequenceEqual([remembered]) &&
            acquisitions.Length == 2 && acquisitions[0].Sequence < removalSequence && removalSequence < absentDamageSequence &&
            absentDamageSequence < acquisitions[1].Sequence && acquisitions[1].Sequence < resumedSequence,
            $"The physically reacquired grant from the same Driver retains its defined stable source identity and the exact pre-loss memory; actual damage while absent occurs between removal and the second acquisition without replacing memory. Original={original}, resumed={resumed.SkillInstanceId}, memories={string.Join(',', retained.Select(r => r.SourceSeat))}, removal={removalSequence}, absentDamage={absentDamageSequence}, acquisitions={string.Join(',', acquisitions.Select(e => e.Sequence))}, resumedStart={resumedSequence}.");
        Private(g); g = Cold(g, registry); Continue(g); Play(g); AssertSettled(g, resumedFrame, true);
        var draws = DrawCount(g); var eligibleAfterRegrant = E<LastDamageSourceReciprocityEligibleEvent>(g).Length;
        UseDriver(g, "outgoing", [2]); Play(g);
        Require(E<DamageAppliedEvent>(g).Last() is { SourceSeat: 0, TargetSeat: 2, Amount: 1 } && DrawCount(g) == draws &&
            E<LastDamageSourceReciprocityEligibleEvent>(g).Length == eligibleAfterRegrant && E<LastDamageSourceRecordedEvent>(g).Length == records,
            "The source seen only during physical absence is really damaged after regrant and earns no remembered-source draw.");
        AssertConservedAndNative(g); _ = Cold(g, registry);
    }

    private static long Incoming(GameEngine g, int source)
    {
        UseDriver(g, "incoming", [source]); Reach(g, IsDiscardChoice);
        var root = Root(g); AssertOrigin(g, root, LastDamageSourceReciprocityDirection.DiscardSource, source, 0, source);
        Require(P(g) is { IsPrivate: true } p && p.PlayerSeat == source && root.LastDamageSourceReciprocity!.EligibleMaterials.Count > 0 &&
            P(g)!.Choices.All(c => c.Cards.Count == 1 && c.Targets.Count == 0),
            "The actual foreign source receives its own private mandatory HE payment with one exact entity per choice.");
        Private(g); Play(g); return root.Id;
    }

    private static void AssertOrigin(GameEngine g, ProgramSkillFrame root, LastDamageSourceReciprocityDirection direction, int source, int target, int remembered)
    {
        var r = root.LastDamageSourceReciprocity!;
        var eligibility = E<LastDamageSourceReciprocityEligibleEvent>(g).Single(e => e.DamageFrameId == r.DamageFrameId && e.Direction == direction);
        var window = g.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single(w => w.Id == r.DamageWindowId);
        var candidate = window.Candidates[window.CandidateIndex];
        Require(root.OwnerSeat == 0 && root.SkillId == Skill && r.Source == new CardConversionSource(Skill, root.TriggerId!, 0, root.SkillInstanceId) &&
            r.GameplayHash == root.GameplayHash && r.StateId == StateId && r.Direction == direction && r.SourceSeat == source && r.TargetSeat == target &&
            r.RecordedSourceSeat == remembered && r.Amount == 1 && r.Nature == DamageNature.Normal && r.ParticipantSeat == (direction == LastDamageSourceReciprocityDirection.DrawOwner ? 0 : source) &&
            root.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied } context && context.ParentFrameId == window.Id &&
            context.DamageFrameId == r.DamageFrameId && window.ParentFrameId == r.DamageFrameId &&
            candidate.ProgramId == Skill && candidate.SkillInstanceId == root.SkillInstanceId && candidate.ProgramTriggerId == root.TriggerId &&
            eligibility.SkillInstanceId == root.SkillInstanceId && eligibility.GameplayHash == root.GameplayHash && eligibility.AttackFrameId == r.AttackFrameId &&
            eligibility.SourceSeat == source && eligibility.TargetSeat == target && eligibility.RecordedSourceSeat == remembered &&
            g.ResolutionStack.OfType<DamageFrame>().Any(d => d.Id == r.DamageFrameId && d.ParentFrameId == r.AttackFrameId && d.SourceSeat == source && d.TargetSeat == target) &&
            E<DamageRequestedEvent>(g).Any(e => e.ResolutionId == r.DamageFrameId && e.SourceSeat == source && e.TargetSeat == target && e.Amount == 1 && !e.SourceLess),
            "The receipt, actual damage producer, exact mandatory trigger candidate and frozen eligibility agree on owner, runtime source, gameplay hash, state, direction and real participant.");
    }

    private static void AssertMovementChild(GameEngine g, string childSkill, long frameId, bool draw, CardLocation from, CardLocation to)
    {
        var root = Root(g); var r = root.LastDamageSourceReciprocity!;
        var observer = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == childSkill);
        var batch = observer.WindowContext!.MovementBatch!;
        Require(root.Id == frameId && batch.ParentFrameId == frameId && batch.AwaitingProgramFrameId == frameId &&
            batch.Movements is [var moved] && moved.From == from && moved.To == to && moved.Reason.Value == Reason(draw) &&
            moved.Sequence > r.SequenceBefore && moved.Sequence == r.SequenceAfter && (draw || batch.Id == r.BatchId) &&
            batch.SourceCounts.Any(c => c.Location == from && c.CountBefore - c.CountAfter == 1) &&
            g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.Batch.Id == batch.Id && w.Batch.AwaitingProgramFrameId == frameId) &&
            !E<LastDamageSourceReciprocityCompletedEvent>(g).Any(e => e.FrameId == frameId),
            "The paid parent waits for the exact real movement batch and native observer, preserving actual before/after counts and sequence without early completion.");
        Frozen(batch.Movements); Frozen(batch.SourceCounts);
    }

    private static void AssertSettled(GameEngine g, long frameId, bool draw, int? id = null, CardLocation? from = null)
    {
        var start = E<LastDamageSourceReciprocityStartedEvent>(g).Single(e => e.FrameId == frameId);
        var paid = E<LastDamageSourceReciprocityMovementIssuedEvent>(g).Single(e => e.FrameId == frameId);
        var movements = g.CardMovements.Where(m => m.Sequence > paid.SequenceBefore && m.Sequence <= paid.SequenceAfter && m.Reason.Value == Reason(draw)).ToArray();
        Require(start.Source.SkillId == Skill && start.Source.OwnerSeat == 0 && start.StateId == StateId &&
            paid.Direction == (draw ? LastDamageSourceReciprocityDirection.DrawOwner : LastDamageSourceReciprocityDirection.DiscardSource) &&
            paid.ParticipantSeat == start.ParticipantSeat && paid.ActualCount == 1 && paid.SequenceAfter > paid.SequenceBefore &&
            movements is [var movement] && (!id.HasValue || movement.CardId == id) && (!from.HasValue || movement.From == from) &&
            (draw ? movement.From == CardLocation.DrawPile && movement.To == CardLocation.Hand(0) :
                movement.From.OwnerSeat == start.SourceSeat && movement.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && movement.To == CardLocation.DiscardPile) &&
            E<LastDamageSourceReciprocityCompletedEvent>(g).Single(e => e.FrameId == frameId).ActualCount == 1 &&
            E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == frameId && e.SkillId == Skill && e.Activated && e.Completed) == 1 &&
            !g.ResolutionStack.Any(f => f.Id == frameId),
            "The exact accepted damage binding settles one actual invoice, one movement fact and one completion after its children, with no duplicate payment or surviving owner frame.");
    }

    private static void AssertConservedAndNative(GameEngine g) => Require(g.CreateCardZoneDiagnostics().Count == 96 &&
        g.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == 96 &&
        g.AcceptedCommands.OfType<AnswerPromptCommand>().All(c => c.ActorSeat == 0) &&
        g.State.CurrentSeat == 0 && g.State.TurnNumber == 1,
        "All original physical entities remain in exactly one actual zone; AI participants pay and return through native Advance without manual AI answers or whole-match simulation.");
    private static int DrawCount(GameEngine g) => E<LastDamageSourceReciprocityMovementIssuedEvent>(g).Count(e => e.Direction == LastDamageSourceReciprocityDirection.DrawOwner);
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.LastDamageSourceReciprocity is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsDiscardChoice(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "last-damage-source-discard");
    private static void Continue(GameEngine g)
    {
        var p = P(g)!; Require(p.PlayerSeat == 0, "Only the human answers its own fixture child.");
        Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue").Id, g.Revision));
    }
    private static void UseDriver(GameEngine g, string activation, IReadOnlyList<int> targets, IReadOnlyList<int>? cards = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, activation, cards ?? [], targets, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var i = 0; i < 128; i++)
        {
            var p = P(g); if (p is not null && stop(p)) return;
            if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId == GainChild) Continue(g);
            else { Require(p is not { PlayerSeat: 0 }, $"Unexpected last-source human boundary {p?.Kind}/{p?.SkillPrompt?.SkillId}."); Accept(g, new AdvanceOneStepCommand(g.Revision)); }
        }
        throw new InvalidOperationException("The bounded fixed-seed last-source fixture did not reach its exact native boundary.");
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "The actual last-source command was rejected."); }
    private static void Reject(GameEngine g, GameCommand command)
    { var before = State(g); var result = g.Submit(command); Require(!result.Accepted && result.Error is not null && State(g) == before, "Wrong actors and unpublished or stale choices change no frame, payment, history, command prefix, entity or private view."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold accepted-command replay preserves all four player views, exact damage and movement frames, source memory, receipt, facts and physical invoices."); return copy;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Private(GameEngine g)
    {
        var before = State(g); var actor = P(g)!.PlayerSeat;
        foreach (var viewer in Enumerable.Range(0, 4))
        {
            var view = g.CreateSnapshot(viewer);
            Require(view.Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0) &&
                (viewer == actor ? view.PendingDecision is { IsPrivate: true } p && p.PlayerSeat == actor : view.PendingDecision is null),
                "Only the exact real participant receives the private selected-entity or native child decision; all other hands and choices stay hidden in every prepared view.");
            if (viewer != actor) continue;
            Frozen(view.PendingDecision!.Choices); Frozen(view.PendingDecision.ValidCardIds); Frozen(view.PendingDecision.ValidTargetSeats);
            foreach (var choice in view.PendingDecision.Choices) { Frozen(choice.Cards); Frozen(choice.Targets); Frozen(choice.ContentIds); }
        }
        Require(State(g) == before, "Readonly four-view mutation probes preserve the exact accepted prefix and private source payment.");
    }
    private static void Frozen<T>(IReadOnlyList<T> values)
    {
        Require(values is IList<T> { IsReadOnly: true }, "Exposed nested collections are detached and readonly.");
        try { ((IList<T>)values).Add(default!); } catch (NotSupportedException) { return; }
        throw new InvalidOperationException("An exposed frozen collection accepted observer mutation.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static GameEngine Start(ContentRegistry registry)
    {
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Require(P(g)!.ValidContentIds.Contains("fixture:last-damage-source-owner"), "The fixed small mode really offers its intended original owner.");
        Accept(g, new SelectGeneralCommand(0, "fixture:last-damage-source-owner", g.Revision, P(g)!.PromptId)); Play(g);
        UseDriver(g, "acquire", []); Play(g);
        Require(g.CreateSnapshot(0).Players[0].Hp == 5 && E<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 0 &&
            e.SourceSkillId == Driver && e.SkillIds.SequenceEqual([Skill, Retire])),
            "The non-classic small-mode Lord starts at actual HP5 and acquires both tested runtime grants through a real zero-material command."); return g;
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("last-damage-source-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Skill}}}","revision":1,"triggers":[
                {"id":"incoming","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"lastDamageSourceReciprocity","target":"owner","stateId":"{{{StateId}}}"}]},
                {"id":"outgoing","window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"lastDamageSourceReciprocity","target":"owner","stateId":"{{{StateId}}}"}]}]},
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"acquire","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{{Skill}}}","{{{Retire}}}"]}]},
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"outgoing","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","sourceRef":{"kind":"owner"},"amount":1}]},
                {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"owner"},"amount":1}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":5}]},
                {"id":"collect","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelectedTargetHand","target":"selectedTarget"}]},
                {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]},
                {"id":"equip-other","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"armor"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"armor"}]}]},
              {"id":"{{{Retire}}}","revision":1,"activations":[{"id":"replace-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{{Skill}}}","{{{Retire}}}"],"sourceBind":"{{{Inert}}}"}]}]},
              {"id":"{{{GainChild}}}","revision":1,"triggers":[{"id":"actual-gain","window":"cardsGained","subject":"owner","movementOccurrence":"perBatch","destinationZones":["hand"],"movementReasons":["{{{Reason(true)}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
              {"id":"{{{DiscardChild}}}","revision":1,"triggers":[{"id":"actual-payment","window":"cardsMoved","subject":"owner","movementOccurrence":"perOwnerBatch","sourceZones":["hand","equipment"],"movementReasons":["{{{Reason(false)}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"payment","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:last-damage-source-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            var presentations = rules["skills"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToDictionary(id => id,
                id => (object)new { name = id, description = "实际伤害来源、真实付款和原生返回" });
            foreach (var id in new[] { GainChild, DiscardChild }) presentations[id] = new
                { name = id, description = "真实移动子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "来源记忆与精确后继")
                { Program = program, ProgramPresentation = catalog.Presentations[id], Tags = id == Skill ? SkillTag.Locked : SkillTag.None });
            b.AddSkill(new(Inert, "独立替换来源", "真实移除后授予的无动作技能"));
            b.AddSkill(new("fixture:last-damage-source-pick", "固定原生选将", "小四人模式")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:last-damage-source-owner", "实际来源记忆拥有者", "supporter", Driver, "wei", 3, [GainChild]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:last-damage-source-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "真实其他角色", "supporter", "fixture:last-damage-source-pick", "shu", 6,
                [DiscardChild, "fixture:last-damage-source-quiet"]));
            var kinds = new[] { "standard:slash", "standard:duel", "standard:bagua" };
            b.AddDeck(new("fixture:last-damage-source-deck", "固定实际实体", 4, 0, [])
                { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(kinds[i % 3], Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "小四人来源记忆", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:last-damage-source-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:last-damage-source-owner", .. peers]));
        }
    }
}
