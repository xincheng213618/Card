using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PlayPhaseTemporarySkillChecks
{
    private const string Owner = "fixture:phase-grant-owner";
    private const string Mode = "identity:classic-play-phase-grants";
    private const string Driver = "fixture:phase-grant-driver";
    private const string DonorA = "fixture:phase-grant-a";
    private const string DonorB = "fixture:phase-grant-b";
    private const string Counter = "fixture:phase-grant-counter";
    private const string Starting = "fixture:phase-grant-starting";
    private const string Gain = "fixture:phase-grant-gain";
    private const string Ending = "fixture:phase-grant-ending";
    private const string TurnEnding = "fixture:phase-grant-turn-ending";
    private const string BeforePlay = "fixture:phase-grant-before-play";
    private const string ExtraPlay = "fixture:phase-grant-extra-play";
    private const string Paid = "paid";
    private const string DrawReason = "skill-program.fixture:phase-grant-counter.Draw";
    private enum Scenario { Normal, BeforePreparation, EndingInsertion, Death, NonterminalDeath }

    public static void MultiplePhaseGrantSourcesShareInstanceStateQuotaAndPreservePermanentSource()
    {
        foreach (var permanent in new[] { false, true })
        {
            var (g, registry) = Start(Scenario.Normal, permanent);
            var original = permanent ? CounterState(g).SkillInstanceId : null;
            Require(HasCounter(g) == permanent, "Only the selected printed source exists before the first real grant.");
            ProgramPlayPhaseSkillsGrantedEvent? committed = null;
            GameSnapshot? prepared = null;
            g.EventCommitted += e => { if (e.Payload is ProgramPlayPhaseSkillsGrantedEvent grant) committed = grant; };
            g.StateChanged += snapshot => prepared = snapshot;
            var oldRevision = g.Revision;
            Reject(g, new UseProgramSkillCommand(1, DonorA, "grant", [], [], g.Revision, P(g)!.PromptId));
            Use(g, DonorA, "grant"); Play(g);
            var first = E<ProgramPlayPhaseSkillsGrantedEvent>(g).Single();
            Require(committed is not null && committed == first && first.Source.SkillId == DonorA &&
                first.Source.BindingId == "grant" && first.Source.OwnerSeat == 0 && first.RecipientSeat == 0 &&
                first.Expiry.TurnNumber == g.State.TurnNumber && first.Expiry.PhaseActorSeat == 0 &&
                first.Expiry.PhaseInstanceId > 0 && first.Grants is [var grant] && grant.SkillId == Counter &&
                grant.PhaseExpiry == first.Expiry && grant.TurnExpiry is null && grant.SourceId.StartsWith("phase:", StringComparison.Ordinal) &&
                HasCounter(g) && !CounterState(g).Value && (!permanent || CounterState(g).SkillInstanceId == original),
                "An actual owner activation publishes one exact phase source and reuses its existing printed runtime instance.");
            Frozen(first.Grants);
            Require(prepared is not null, "The accepted grant publishes its prepared player view.");
            var retained = prepared!;
            var retainedJson = SnapshotJson.Serialize(retained);
            FreezeView(retained);
            Reject(g, new UseProgramSkillCommand(0, Counter, "consume", [], [], oldRevision, P(g)!.PromptId));
            var handBefore = V(g).Hand.Select(c => c.Id).ToArray();
            g = Consume(g, registry);
            var instance = CounterState(g).SkillInstanceId;
            Use(g, DonorB, "grant"); Play(g);
            var issued = E<ProgramPlayPhaseSkillsGrantedEvent>(g);
            Require(issued.Length == 2 && issued.Select(e => e.Source.SkillId).SequenceEqual([DonorA, DonorB]) &&
                issued.All(e => e.RecipientSeat == 0 && e.Expiry == first.Expiry && e.Grants.Count == 1 &&
                    e.Grants[0].SkillId == Counter && e.Grants[0].SkillInstanceId == instance && e.Grants[0].PhaseExpiry == first.Expiry) &&
                issued.Select(e => e.Grants[0].GrantId).Distinct().Count() == 2 &&
                issued.Select(e => e.Grants[0].SourceId).Distinct().Count() == 2 &&
                CounterState(g).Value && (!permanent || instance == original) &&
                !CanUse(g, Counter, "consume") && E<ProgramSkillStartedEvent>(g).Count(e => e.SkillId == Counter) == 1 &&
                Draws(g).Length == 1 && V(g).HandCount == handBefore.Length + 1,
                "Two independent donor sources share one effective state and the already consumed phase quota, without a second draw.");
            Reject(g, new UseProgramSkillCommand(0, Counter, "consume", [], [], g.Revision, P(g)!.PromptId));
            Require(SnapshotJson.Serialize(retained) == retainedJson && !retained.Players[0].SkillRuntimeStates!
                    .Single(s => s.SkillId == Counter).BooleanStates!.Single().Value,
                "Previously prepared nested views remain unchanged after the real state cost and second grant.");
            Private(g);
            g = Cold(g, registry);
            Require(CounterState(g).Value && !CanUse(g, Counter, "consume") && Draws(g).Length == 1,
                "Cold command replay retains the shared instance, paid state and one consumed quota.");
        }
        ValidateBoundaryContracts();
    }

    public static void PlayEndingChildrenKeepPhaseGrantsUntilExactBoundaryCloses()
    {
        foreach (var permanent in new[] { false, true })
        {
            var (g, registry) = Start(Scenario.Normal, permanent);
            var original = permanent ? CounterState(g).SkillInstanceId : null;
            Use(g, DonorA, "grant"); Play(g); Use(g, DonorB, "grant"); Play(g);
            var grants = E<ProgramPlayPhaseSkillsGrantedEvent>(g);
            var expiry = grants[0].Expiry;
            var instance = grants[0].Grants.Single().SkillInstanceId;
            End(g); Reach(g, p => p.SkillPrompt?.SkillId == Ending && IsContinue(p));
            var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Ending);
            var parent = g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.Id == child.WindowContext!.ParentFrameId);
            Require(g.State.Phase == TurnPhase.Play && child.WindowContext?.Window == SkillProgramTriggerWindow.PlayEnding &&
                parent.Window == SkillProgramTriggerWindow.PlayEnding && parent.OwnerSeat == 0 &&
                HasCounter(g) && CounterState(g).SkillInstanceId == instance &&
                E<ProgramPlayPhaseSkillGrantExpiredEvent>(g).Length == 0 &&
                !E<ProgramBindingResolvedEvent>(g).Any(e => e.FrameId == child.Id),
                "Both phase grants remain owned while their real PlayEnding child is paused on its exact lifecycle parent.");
            WrongAnswer(g); Private(g); g = Cold(g, registry); Continue(g);
            Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards });
            AssertExpired(g, grants, expiry);
            Require(g.State.Phase == TurnPhase.Discard && !E<TurnEndedEvent>(g).Any(e => e.TurnNumber == expiry.TurnNumber) &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == child.Id && e.Completed) == 1 &&
                HasCounter(g) == permanent && (!permanent || CounterState(g).SkillInstanceId == original),
                "The completed PlayEnding return expires only the two temporary sources before the same turn's real Discard phase.");
            var discard = P(g)!;
            var cards = discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray();
            Require(cards.Length > 0 && cards.All(id => V(g).Hand.Any(c => c.Id == id)),
                "The small native fixture reaches a genuine nonempty hand-limit payment.");
            g = Cold(g, registry);
            Accept(g, new DiscardCardsCommand(0, cards, P(g)!.PromptId, g.Revision));
            Reach(g, p => p.SkillPrompt?.SkillId == TurnEnding && IsContinue(p));
            var endingChild = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == TurnEnding);
            Require(endingChild.WindowContext is { Window: SkillProgramTriggerWindow.TurnEnding } endingContext &&
                g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any(f => f.Id == endingContext.ParentFrameId) &&
                HasCounter(g) == permanent &&
                cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1) &&
                !E<TurnEndedEvent>(g).Any(e => e.TurnNumber == expiry.TurnNumber),
                "Phase sources stay absent through actual discard payment and the native TurnEnding child, before actual TurnEnded.");
            g = Cold(g, registry); Continue(g);
            Until(g, () => E<TurnEndedEvent>(g).Any(e => e.TurnNumber == expiry.TurnNumber && e.ActorSeat == 0));
            AssertExpired(g, grants, expiry);
            Require(HasCounter(g) == permanent && (!permanent || CounterState(g).SkillInstanceId == original),
                "Turn-end fallback cannot expire either source twice or remove the original printed skill.");
        }
    }

    public static void InsertedPlayAndOwnerDeathDoNotLeakPhaseGrantSources()
    {
        foreach (var scenario in new[] { Scenario.BeforePreparation, Scenario.EndingInsertion })
        {
            var (g, registry) = Start(scenario, permanent: true);
            var first = E<ProgramPlayPhaseSkillsGrantedEvent>(g).Single();
            var instance = CounterState(g).SkillInstanceId;
            Require(first.Source.SkillId == Starting && first.Source.BindingId == "own-phase-start" &&
                E<ProgramBindingStartedEvent>(g).Count(e => e.FrameId == first.FrameId && e.SkillId == Starting &&
                    e.Window == SkillProgramTriggerWindow.PlayPhaseStarting) == 1 &&
                first.Grants.Single().SkillInstanceId == instance && !CounterState(g).Value,
                "The real own PlayPhaseStarting adapter grants against its exact new phase identity and preserves the permanent instance.");
            g = Consume(g, registry); End(g);
            Reach(g, p => p.SkillPrompt?.SkillId == Ending && IsContinue(p));
            Require(CounterState(g).Value && E<ProgramPlayPhaseSkillGrantExpiredEvent>(g).Length == 0,
                "Neither pre-preparation Play nor an insertion-producing PlayEnding closes its grant before the ending child returns.");
            g = Cold(g, registry); Continue(g); Play(g);
            var second = E<ProgramPlayPhaseSkillsGrantedEvent>(g).Last();
            Require(E<ProgramPlayPhaseSkillsGrantedEvent>(g).Length == 2 && second.Source.SkillId == Starting &&
                second.Expiry.TurnNumber == first.Expiry.TurnNumber && second.Expiry.PhaseActorSeat == 0 &&
                second.Expiry.PhaseInstanceId != first.Expiry.PhaseInstanceId &&
                second.Grants.Single().SourceId != first.Grants.Single().SourceId &&
                second.Grants.Single().SkillInstanceId == instance && CounterState(g).SkillInstanceId == instance &&
                !CounterState(g).Value && CanUse(g, Counter, "consume") &&
                !E<TurnEndedEvent>(g).Any(e => e.TurnNumber == first.Expiry.TurnNumber),
                "A distinct inserted or subsequent normal Play receives a new exact source in the same turn, resets its phase quota/state, and retains the original permanent instance.");
            AssertExpired(g, [first], first.Expiry);
            Require(!E<ProgramPlayPhaseSkillGrantExpiredEvent>(g).Any(e => e.Grant == second.Grants.Single()) &&
                E<ProgramPhaseScheduledEvent>(g).Any(e => e.SkillId == (scenario == Scenario.BeforePreparation ? BeforePlay : ExtraPlay) && e.Started),
                "The native scheduled Play actually starts; expiry cannot leak the first source or prematurely remove the new source.");
            g = Consume(g, registry);
            Require(Draws(g).Length == 2 && E<ProgramSkillStartedEvent>(g).Count(e => e.SkillId == Counter) == 2 &&
                !CanUse(g, Counter, "consume"), "Each real phase pays one independent draw and one quota on the same permanent runtime instance.");
            End(g); Reach(g, p => p.SkillPrompt?.SkillId == Ending && IsContinue(p));
            g = Cold(g, registry); Continue(g);
            Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards });
            AssertExpired(g, [second], second.Expiry);
            Require(HasCounter(g) && CounterState(g).SkillInstanceId == instance,
                "Ending the second true Play removes its exact phase source and retains the permanent skill.");
        }

        foreach (var terminal in new[] { false, true })
        {
            var (dead, deadRegistry) = Start(terminal ? Scenario.Death : Scenario.NonterminalDeath, permanent: false);
            Use(dead, DonorA, "grant"); Play(dead); Use(dead, DonorB, "grant"); Play(dead);
            var deathGrants = E<ProgramPlayPhaseSkillsGrantedEvent>(dead);
            Use(dead, Driver, "die");
            Until(dead, () => dead.ResolutionStack.OfType<DyingFrame>().Any(f => f.VictimSeat == 0) || E<PlayerDiedEvent>(dead).Any(e => e.VictimSeat == 0));
            Require(dead.ResolutionStack.OfType<DyingFrame>().Any(f => f.VictimSeat == 0) &&
                E<PlayerDiedEvent>(dead).Length == 0 && HasCounter(dead) && E<ProgramPlayPhaseSkillGrantExpiredEvent>(dead).Length == 0,
                "A real lethal HP-loss activation pauses in native Dying while the current phase's two grants still exist.");
            dead = Cold(dead, deadRegistry);
            if (!terminal)
            {
                Reach(dead, p => p.SkillPrompt?.SkillId == Counter && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
                var deathChild = dead.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Counter);
                var deathWindow = dead.ResolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Single(f => f.Id == deathChild.WindowContext!.ParentFrameId);
                Require(E<PlayerDiedEvent>(dead).Count(e => e.VictimSeat == 0) == 1 && !V(dead).IsAlive &&
                    E<GameEndedEvent>(dead).Length == 0 && HasCounter(dead) &&
                    E<ProgramPlayPhaseSkillGrantExpiredEvent>(dead).Length == 0 &&
                    deathChild.WindowContext?.Window == SkillProgramTriggerWindow.OwnerDied &&
                    deathWindow.OwnerSeat == 0 && deathWindow.Candidates.Count(c => c.SkillId == Counter) == 1 &&
                    dead.ResolutionStack.OfType<DeathFrame>().Any(f => f.Id == deathWindow.DeathFrameId && f.VictimSeat == 0),
                    "A nonterminal real death retains the shared phase skill through its one exact native OwnerDied candidate and living-recipient selection.");
                WrongAnswer(dead); Private(dead); dead = Cold(dead, deadRegistry);
                var recipient = P(dead)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-target").Targets.Single();
                var recipientBefore = dead.CreateSnapshot(recipient).Players[recipient].HandCount;
                Answer(dead, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([recipient]));
                Until(dead, () => E<ProgramPlayPhaseSkillGrantExpiredEvent>(dead).Length != 0);
                Require(E<ProgramBindingResolvedEvent>(dead).Count(e => e.FrameId == deathChild.Id && e.SkillId == Counter &&
                        e.Window == SkillProgramTriggerWindow.OwnerDied && e.Activated && e.Completed) == 1 &&
                    dead.CardMovements.Count(m => m.Reason.Value == DrawReason && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(recipient)) == 1 &&
                    dead.CreateSnapshot(recipient).Players[recipient].HandCount == recipientBefore + 1 && E<GameEndedEvent>(dead).Length == 0,
                    "The cold-restored death skill pays its real foreign draw once and returns before exact phase expiry; two donor sources never duplicate it.");
            }
            else Until(dead, () => E<PlayerDiedEvent>(dead).Any(e => e.VictimSeat == 0));
            AssertExpired(dead, deathGrants, deathGrants[0].Expiry);
            Require(!V(dead).IsAlive && !HasCounter(dead) &&
                E<ProgramPlayPhaseSkillGrantExpiredEvent>(dead).All(e => e.Reason == "phase-actor-died") && Draws(dead).Length == 0,
                "After native death children return, both exact phase sources close without an unearned owner draw or delayed turn-end lifetime.");
            if (terminal)
            {
                Until(dead, () => E<GameEndedEvent>(dead).Length != 0);
                Require(E<GameEndedEvent>(dead).Length == 1 &&
                    !E<ProgramBindingStartedEvent>(dead).Any(e => e.SkillId == Counter && e.Window == SkillProgramTriggerWindow.OwnerDied),
                    "Real Lord death reaches terminal settlement without manufacturing a death trigger after the original winner gate.");
            }
            AssertExpired(dead, deathGrants, deathGrants[0].Expiry);
            _ = Cold(dead, deadRegistry);
        }
    }

    private static GameEngine Consume(GameEngine g, ContentRegistry registry)
    {
        var before = Draws(g).Length;
        Use(g, Counter, "consume");
        Reach(g, p => p.SkillPrompt?.SkillId == Gain && IsContinue(p));
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Counter);
        var window = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Batch.ParentFrameId == root.Id &&
            f.Batch.Movements.Any(m => m.Reason.Value == DrawReason));
        var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Gain);
        Require(root.InstructionIndex == 2 && root.PendingMovementContinuation is null &&
            window.ResumeProgramFrameId == root.Id && window.Batch.AwaitingProgramFrameId is null &&
            child.WindowContext is { Window: SkillProgramTriggerWindow.CardsGained } context && context.ParentFrameId == window.Id &&
            window.Batch.Movements is [var movement] && movement.From == CardLocation.DrawPile && movement.To == CardLocation.Hand(0) &&
            movement.Reason.Value == DrawReason && CounterState(g).Value && Draws(g).Length == before + 1 &&
            !E<ProgramSkillResolvedEvent>(g).Any(e => e.FrameId == root.Id),
            "The real state cost and one physical draw pause on the exact program's paid instruction and native gained-card child. " + Diagnostic(g));
        Frozen(window.Batch.Movements); WrongAnswer(g); Private(g); g = Cold(g, registry); Continue(g); Play(g);
        Require(E<ProgramSkillResolvedEvent>(g).Count(e => e.FrameId == root.Id && e.Completed) == 1 &&
            E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == child.Id && e.Completed) == 1 &&
            Draws(g).Length == before + 1 && !g.ResolutionStack.Any(f => f.Id == root.Id || f.Id == child.Id),
            "The restored native child returns once, advances its original cursor, and cannot draw or pay the state cost again.");
        return g;
    }

    private static void AssertExpired(GameEngine g, IReadOnlyList<ProgramPlayPhaseSkillsGrantedEvent> issued, SkillGrantPhaseExpiry expiry)
    {
        var expected = issued.SelectMany(e => e.Grants).ToArray();
        var expired = E<ProgramPlayPhaseSkillGrantExpiredEvent>(g).Where(e => e.Grant.PhaseExpiry == expiry).ToArray();
        var history = g.Events.Select(e => e.Payload).ToArray();
        var closed = Array.FindIndex(history, e => e is ProgramPlayPhaseSkillBoundaryClosedEvent b && b.Expiry == expiry);
        Require(E<ProgramPlayPhaseSkillBoundaryClosedEvent>(g).Count(e => e.Expiry == expiry) == 1 &&
            expired.Length == expected.Length && expected.All(grant => expired.Count(e => e.RecipientSeat == 0 && e.Grant == grant) == 1) &&
            closed >= 0 && history.Take(closed).OfType<ProgramPlayPhaseSkillGrantExpiredEvent>().All(e => e.Grant.PhaseExpiry != expiry),
            "One exact phase boundary closes before each independently issued source expires exactly once, without removing unrelated grants.");
    }

    private static void ValidateBoundaryContracts()
    {
        var normal = Rules();
        foreach (var mutate in new Action<JsonObject>[]
        {
            rules => Skill(rules, Starting)["triggers"]![0]!["window"] = "turnEnding",
            rules => Skill(rules, Starting)["triggers"]![0]!["subject"] = "any",
            rules => Skill(rules, Starting)["triggers"]![0]!["turnOwnerScope"] = "otherLiving",
            rules => Skill(rules, DonorA)["activations"]![0]!["effects"]![0]!["skillIds"] = new JsonArray(),
            rules => Skill(rules, DonorA)["activations"]![0]!["effects"]![0]!["condition"] = JsonNode.Parse("""{"kind":"hpAtLeast","value":1}""")
        })
        {
            var invalid = (JsonObject)normal.DeepClone(); mutate(invalid);
            var rejected = false;
            try { _ = Catalog(invalid); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Malformed phase-grant windows, foreign owners, empty skills and conditional instructions must be rejected by the real loader.");
        }
        var set = new CharacterSkillSet();
        var valid = new SkillGrant("phase:test:counter", Counter, "phase:test:instance", "phase:test", PhaseExpiry: new(1, 0, 1));
        Require(set.Grant(valid), "The public ownership API accepts a correctly identified phase source.");
        foreach (var invalid in new[]
        {
            valid with { GrantId = "phase:bad-turn", PhaseExpiry = new(0, 0, 1) },
            valid with { GrantId = "phase:bad-actor", PhaseExpiry = new(1, -1, 1) },
            valid with { GrantId = "phase:bad-id", PhaseExpiry = new(1, 0, 0) },
            valid with { GrantId = "phase:dual", SourceId = "turn:phase:dual", TurnExpiry = new(1, 0) }
        })
        {
            var before = set.Revision; var rejected = false;
            try { set.Grant(invalid); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && set.Revision == before && set.Grants.SequenceEqual([valid]),
                "Invalid or dual expiry identities cannot mutate the source-aware public ownership collection.");
        }
        Frozen(set.Grants);
    }

    private static (GameEngine, ContentRegistry) Start(Scenario scenario, bool permanent)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario, permanent));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = scenario == Scenario.NonterminalDeath ? Role.Rebel : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g);
        Require(V(g).HandCount == 4 && V(g).MaxHp < V(g).HandCount && V(g).IsAlive,
            "The fixed small physical deck starts with a genuine hand-limit discard later in the same turn.");
        return (g, registry);
    }
    private static PlayerSnapshot V(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool HasCounter(GameEngine g) => V(g).Skills!.Any(s => s.ContentId == Counter);
    private static ProgramBooleanStateSnapshot CounterState(GameEngine g) => V(g).SkillRuntimeStates!.Single(s => s.SkillId == Counter).BooleanStates!.Single(s => s.StateId == Paid);
    private static CardMovementRecord[] Draws(GameEngine g) => g.CardMovements.Where(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == DrawReason).ToArray();
    private static bool CanUse(GameEngine g, string skill, string activation) => g.GetHumanLegalActions().Any(a => a.ProgramSkillId == skill && a.ProgramActivationId == activation);
    private static bool IsContinue(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string skill, string activation)
    {
        Require(CanUse(g, skill, activation), "The real published activation is missing: " + skill + "/" + activation + " " + Diagnostic(g));
        Accept(g, new UseProgramSkillCommand(0, skill, activation, [], [], g.Revision, P(g)!.PromptId));
    }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    {
        var p = P(g)!; Require(p.PlayerSeat == 0, "Only the real human's published decision may be answered by the fixture.");
        Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(choose).Id, g.Revision));
    }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void WrongAnswer(GameEngine g)
    {
        var p = P(g)!;
        Reject(g, new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, g.Revision));
        Reject(g, new AnswerPromptCommand(0, p.PromptId, new ChoiceId("phase-grant-forged-choice"), g.Revision));
    }
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop) => Until(g, () => P(g) is { } p && stop(p));
    private static void Until(GameEngine g, Func<bool> stop)
    {
        for (var step = 0; step < 160; step++) { if (stop()) return; Step(g); }
        throw new InvalidOperationException("The bounded real phase-grant fixture missed its boundary: " + Diagnostic(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is not { PlayerSeat: 0 }) { Accept(g, new AdvanceOneStepCommand(g.Revision)); return; }
        if (IsContinue(p)) { Continue(g); return; }
        if (p.Kind == DecisionKind.DiscardCards)
        { Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision)); return; }
        if (p.Kind is DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)
        { Answer(g, c => c.Cards.Count == 0); return; }
        if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([0])))
        { Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([0])); return; }
        throw new InvalidOperationException("Unexpected genuine human phase-grant prompt: " + Diagnostic(g));
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "A real phase-grant command was rejected.");
    }
    private static void Reject(GameEngine g, GameCommand command)
    {
        var before = State(g);
        Require(!g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()).Accepted && State(g) == before,
            "A wrong actor, stale revision, forged choice or consumed quota rejects atomically without issuing or removing a source.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Command-only cold replay preserves four private views, exact phase/source identities, paid movement, frame cursors and once-only history.");
        return copy;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), g.CardMovements,
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static void Private(GameEngine g)
    {
        foreach (var seat in Enumerable.Range(0, 4))
            foreach (var player in g.CreateSnapshot(seat).Players.Where(player => player.Seat != seat))
                Require(player.Hand.Count == 0, "Phase-source publication and its draw child cannot expose another player's private physical hand.");
    }
    private static void FreezeView(GameSnapshot snapshot)
    {
        Frozen(snapshot.Players);
        foreach (var player in snapshot.Players)
        {
            Frozen(player.Hand); if (player.Skills is { } skills) Frozen(skills);
            if (player.SkillRuntimeStates is not { } states) continue;
            Frozen(states); foreach (var state in states) { Frozen(state.Usages); if (state.BooleanStates is { } values) Frozen(values); }
        }
    }
    private static void Frozen<T>(IReadOnlyList<T> list)
    {
        if (list is not IList<T> writable) return;
        var rejected = false;
        try { if (writable.Count == 0) writable.Add(default!); else writable[0] = writable[0]; }
        catch (NotSupportedException) { rejected = true; }
        Require(rejected, "Committed grant collections, native batches and prepared nested player views are immutable.");
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new
    {
        Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, p.SkillPrompt, Choices = p.Choices.Take(8) } : null,
        g.State.Phase, g.State.TurnNumber, g.State.CurrentSeat,
        Frames = g.ResolutionStack.TakeLast(8).Select(f => new { f.Id, Type = f.GetType().Name, Cursor = (f as ProgramSkillFrame)?.InstructionIndex }),
        Movements = g.CardMovements.TakeLast(6), Facts = g.Events.TakeLast(8).Select(e => new { e.Sequence, Type = e.Payload.GetType().Name, Payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) })
    });
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static JsonNode Skill(JsonObject rules, string id) => rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == id)!;
    private static SkillProgramCatalog Catalog(JsonObject rules)
    {
        var labels = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
        {
            var id = n!["id"]!.GetValue<string>(); var presentation = new Dictionary<string, object> { ["name"] = id, ["description"] = "真实阶段独立授技来源" };
            if (id is Gain or Ending or TurnEnding) presentation["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
            return (object)presentation;
        });
        return SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
    }
    private static JsonObject Rules()
    {
        var rules = JsonNode.Parse("""
        {"skills":[
          {"id":"fixture:phase-grant-driver","revision":1,"activations":[{"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]},
          {"id":"fixture:phase-grant-a","revision":1,"activations":[{"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantPlayPhaseSkills","target":"owner","skillIds":["fixture:phase-grant-counter"],"condition":{"kind":"always"}}]}]},
          {"id":"fixture:phase-grant-b","revision":1,"activations":[{"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantPlayPhaseSkills","target":"owner","skillIds":["fixture:phase-grant-counter"],"condition":{"kind":"always"}}]}]},
          {"id":"fixture:phase-grant-counter","revision":1,"states":[{"id":"paid","initialValue":false,"visibility":"public","resetScope":"playPhase","reacquirePolicy":"preserveUntilGameEnd"}],"activations":[{"id":"consume","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"setBooleanState","target":"owner","stateId":"paid","value":true},{"op":"draw","target":"owner","amount":1}]}],"triggers":[{"id":"real-death-tail","window":"ownerDied","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"draw","target":"selectedTarget","amount":1}]}]},
          {"id":"fixture:phase-grant-starting","revision":1,"triggers":[{"id":"own-phase-start","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"own","optional":false,"effects":[{"op":"grantPlayPhaseSkills","target":"owner","skillIds":["fixture:phase-grant-counter"],"condition":{"kind":"always"}}]}]},
          {"id":"fixture:phase-grant-gain","revision":1,"triggers":[{"id":"real-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fixture:phase-grant-counter.Draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
          {"id":"fixture:phase-grant-ending","revision":1,"triggers":[{"id":"real-ending-child","window":"playEnding","subject":"owner","optional":false,"priority":100,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
          {"id":"fixture:phase-grant-turn-ending","revision":1,"triggers":[{"id":"real-turn-ending-child","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
          {"id":"fixture:phase-grant-before-play","revision":1,"triggers":[{"id":"insert-before-preparation","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
          {"id":"fixture:phase-grant-extra-play","revision":1,"triggers":[{"id":"insert-at-ending","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"insertPhase","target":"selectedTarget","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
          {"id":"fixture:phase-grant-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
        ]}
        """)!.AsObject();
        rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
        return rules;
    }
    private sealed class Fixture(Scenario scenario, bool permanent) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-play-phase-skill-grants", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = Catalog(Rules());
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "精确阶段来源与真实子窗")
                {
                    Program = program, ProgramPresentation = catalog.Presentations[id],
                    // In the nonterminal Rebel case the native Lord chooses
                    // first. Reserve this manual-only driver for the human;
                    // no runtime action/response evaluation is changed.
                    SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => id == Driver ? -10000d : 0d)
                });
            var own = new List<string> { DonorA, DonorB, Gain, Ending, TurnEnding };
            if (permanent) own.Add(Counter);
            if (scenario is Scenario.BeforePreparation or Scenario.EndingInsertion) own.Add(Starting);
            if (scenario == Scenario.BeforePreparation) own.Add(BeforePlay);
            if (scenario == Scenario.EndingInsertion) own.Add(ExtraPlay);
            b.AddGeneral(new(Owner, "精确阶段授技拥有者", "supporter", Driver, "jin", 2, own));
            for (var peer = 1; peer <= 3; peer++)
                b.AddGeneral(new($"fixture:phase-grant-peer-{peer}", "固定原生其他角色", "supporter", "fixture:phase-grant-quiet", "wei", 4));
            b.AddDeck(new("fixture:phase-grant-deck", "不含救援牌的固定实体小牌堆", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "精确出牌阶段临时授技", 4, 4,
                scenario == Scenario.NonterminalDeath
                    ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }
                    : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:phase-grant-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Owner, "fixture:phase-grant-peer-1", "fixture:phase-grant-peer-2", "fixture:phase-grant-peer-3"]));
        }
    }
}
