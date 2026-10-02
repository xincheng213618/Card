using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class AfterTurnEndedChecks
{
    public static void DueBeforeWindowAndNestedReplay()
    {
        var (g, registry) = Create("due");
        Use(g, "grow", [1]);
        End(g);
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:due");
        Answer(g, c => c.Targets.SequenceEqual([1]));
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:movement");
        var dueParent = (DeferredTurnEndFrame)g.ResolutionStack.First();
        Require(dueParent.Current is not null && dueParent.AfterTurnEnded is null,
            "The real deferred discard child precedes capture of the new window.");
        Replay(g, registry);
        Reject(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:after-own");
        var parent = (DeferredTurnEndFrame)g.ResolutionStack.First();
        Require(parent.Current is null && parent.ItemIndex == parent.DueIds.Count && parent.AfterTurnEnded is { ItemIndex: 0, CurrentChild: null },
            "All old due children finish before one frozen after-end candidate list.");
        Require(g.CreateSnapshot(0).Phase == TurnPhase.Finished && g.Events.Last(e => e.Payload is TurnEndedEvent).Payload is TurnEndedEvent { TurnNumber: 1, ActorSeat: 0 },
            "The optional window follows the real end and precedes the next turn.");
        Require(parent.AfterTurnEnded!.Items is IList<TurnEndingBoundaryItem> items && items.IsReadOnly,
            "The frozen boundary items cannot be mutated through their collection interface.");
        Replay(g, registry);
        Reject(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:gain");
        var active = (DeferredTurnEndFrame)g.ResolutionStack.First();
        Require(active.AfterTurnEnded?.CurrentChild is { } current && g.ResolutionStack[1] is ProgramSkillFrame child &&
            current.FrameId == child.Id && current.Candidate.SkillInstanceId == child.SkillInstanceId && child.WindowContext?.ParentFrameId == active.Id,
            "An actual gain child suspends the exact owning after-end binding.");
        Replay(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(g.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.Window == SkillProgramTriggerWindow.AfterTurnEnded && e.OwnerSeat == 0) == 1 &&
            g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.fixture:after-own.Draw") == 1,
            "The paid after-end draw is neither repeated nor recaptured on child return.");
        var events = g.Events.Select(e => e.Payload).ToArray();
        Require(Array.FindIndex(events, e => e is DeferredHandAlignmentResolvedEvent) < Array.FindIndex(events, e => e is ProgramBindingStartedEvent b && b.Window == SkillProgramTriggerWindow.AfterTurnEnded),
            "Existing due settlement remains ahead of the new window.");
        Replay(g, registry);
    }

    public static void SkippedAndExtraActualEnds()
    {
        var (skipped, sr) = Create("skipped");
        Use(skipped, "flip", [1]);
        End(skipped);
        Reach(skipped, p => p.SkillPrompt?.SkillId == "fixture:after-other");
        var parent = (DeferredTurnEndFrame)skipped.ResolutionStack.First();
        Require(parent is { OwnerSeat: 1, Skipped: true, TurnNumber: 2 } && skipped.CreateSnapshot(0).Phase == TurnPhase.Finished,
            "A turned-face-up skipped actual turn runs the same post-end boundary.");
        Replay(skipped, sr);
        Answer(skipped, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Accept(skipped, new AdvanceOneStepCommand(skipped.Revision));
        Require(skipped.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>().Count(e => e.Window == SkillProgramTriggerWindow.AfterTurnEnded) == 1,
            "Declining consumes the frozen occurrence once.");
        Replay(skipped, sr);

        var (extra, er) = Create("extra");
        Use(extra, "extra", [0]);
        End(extra);
        Reach(extra, p => p.SkillPrompt?.SkillId == "fixture:after-own");
        Require(extra.CreateSnapshot(0).TurnNumber == 1, "The extra turn has not begun inside the prior after-end prompt.");
        Answer(extra, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Reach(extra, p => p.Kind == DecisionKind.PlayCard);
        Require(extra.CreateSnapshot(0) is { CurrentSeat: 0, TurnNumber: 2 }, "The scheduled extra actual turn begins only after the window.");
        End(extra);
        Reach(extra, p => p.SkillPrompt?.SkillId == "fixture:after-own");
        Require(extra.ResolutionStack.First() is DeferredTurnEndFrame { OwnerSeat: 0, TurnNumber: 2, Skipped: false },
            "The actual extra turn receives its own post-end occurrence.");
        Replay(extra, er);
    }

    public static void OldRegistryShapeAndExactParent()
    {
        var (old, registry) = Create("old");
        Use(old, "grow", [1]); End(old);
        Reach(old, p => p.SkillPrompt?.SkillId == "fixture:due");
        Answer(old, c => c.Targets.SequenceEqual([1]));
        Reach(old, p => p.SkillPrompt?.SkillId == "fixture:movement");
        Require(old.ResolutionStack.First() is DeferredTurnEndFrame { AfterTurnEnded: null, Prelude: null } &&
            !JsonSerializer.Serialize(old.ResolutionStack.First(), typeof(ResolutionFrame)).Contains("Prelude", StringComparison.Ordinal) &&
            !JsonSerializer.Serialize(old.ResolutionStack.First(), typeof(ResolutionFrame)).Contains("AfterTurnEnded", StringComparison.Ordinal),
            "A registry without the capability preserves the previous due-frame JSON shape.");
        Replay(old, registry);
        Answer(old, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(old, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(!old.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Any(e => e.Window == SkillProgramTriggerWindow.AfterTurnEnded),
            "An old registry does not introduce a new program window.");

        var (g, _) = Create("extra"); End(g);
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:after-own");
        var parent = (DeferredTurnEndFrame)g.ResolutionStack.First();
        var candidate = parent.AfterTurnEnded!.Items[0].Candidate!;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var context = (ProgramSkillWindowContext)typeof(GameEngine).GetMethod("CreateAfterTurnEndedContext", flags)!.Invoke(g, [parent, candidate])!;
        var trigger = g.ContentRegistry.GetSkill(candidate.SkillId).Program!.Triggers.Single(t => t.Id == candidate.BindingId);
        bool Eligible(ProgramTriggerCandidate c, ProgramSkillWindowContext x) => (bool)typeof(GameEngine).GetMethod("CanRunAfterTurnEndedCandidate", flags)!.Invoke(g, [c, x, trigger])!;
        Require(Eligible(candidate, context) && !Eligible(candidate, context with { ParentFrameId = parent.Id + 1 }) &&
            !Eligible(candidate, context with { Window = SkillProgramTriggerWindow.TurnEnding }) &&
            !Eligible(candidate, context with { SourceSeat = 1 }) && !Eligible(candidate with { SkillInstanceId = "wrong" }, context),
            "The actual frozen parent, ended owner and source instance are independently required.");
        Reject(g);
    }

    public static void ReturnMovementPreludeBeforeWindow()
    {
        var (g, registry) = Create("return");
        HoldOne(g);
        End(g);
        Reach(g, p => p.SkillPrompt?.SkillId is "fixture:return-gain" or "fixture:after-own");
        Require(Prompt(g)!.SkillPrompt?.SkillId == "fixture:return-gain",
            "Real Pojun return gain children must finish before the optional after-end window is captured.");
        Require(g.ResolutionStack.First() is DeferredTurnEndFrame parent && parent.AfterTurnEnded is null && parent.Current is null,
            "The return observer remains beneath its exact owning prelude, with no due or After window yet.");
        CheckPreludeReceipt(g);
        Replay(g, registry); Reject(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:return-nested");
        Require(g.ResolutionStack.First() is DeferredTurnEndFrame { AfterTurnEnded: null },
            "A gain observer's true draw child remains in the prelude before capture.");
        Replay(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:after-own");
        Require(g.ResolutionStack.First() is DeferredTurnEndFrame { DueIds.Count: 0, AfterTurnEnded: not null } &&
            g.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == "fixture:return-gain") == 1 &&
            g.CardMovements.Count(m => m.Reason == CardMoveReasons.PojunHoldReturn) == 1 &&
            g.CardMovements.Count(m => m.Reason.Value == "skill-program.fixture:return-gain.Draw") == 1,
            "One return and nested draw complete once before the no-due After window.");
        Replay(g, registry);

        var (old, oldRegistry) = Create("return-old"); HoldOne(old); End(old);
        Reach(old, p => p.SkillPrompt?.SkillId == "fixture:return-gain");
        Require(old.ResolutionStack.First() is CardsMovedTriggerWindowFrame && old.CreateSnapshot(0).Phase == TurnPhase.NotStarted,
            "Without the new capability the established return movement ordering and frame shape stay unchanged.");
        Require(!JsonSerializer.Serialize(old.ResolutionStack.First(), typeof(ResolutionFrame)).Contains("DeferredTurnEndReturn", StringComparison.Ordinal),
            "The old movement child does not serialize the new nullable parent-return field.");
        Replay(old, oldRegistry);
    }

    private static void CheckPreludeReceipt(GameEngine g)
    {
        var parent = (DeferredTurnEndFrame)g.ResolutionStack[0];
        var child = (CardsMovedTriggerWindowFrame)g.ResolutionStack[1];
        var batch = child.Batch;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        bool Matches(DeferredTurnEndFrame p, CardsMovedTriggerWindowFrame c) => (bool)typeof(GameEngine)
            .GetMethod("MatchesDeferredTurnEndPreludeChild", flags)!.Invoke(g, [p, c])!;
        Require(Matches(parent, child) && Matches(parent, child with { Batch = batch with {
            Movements = Array.AsReadOnly(batch.Movements.ToArray()), SourceCounts = Array.AsReadOnly(batch.SourceCounts.ToArray()),
            DestinationCounts = Array.AsReadOnly(batch.DestinationCounts!.ToArray()) } }),
            "The typed return compares complete ordered contents, independent of collection reference identity.");
        Require(batch.Movements is IList<CardMovementRecord> m && m.IsReadOnly &&
            batch.SourceCounts is IList<CardMovementSourceCount> sc && sc.IsReadOnly &&
            batch.DestinationCounts is IList<CardMovementSourceCount> dc && dc.IsReadOnly &&
            child.Candidates is IList<ProgramTriggerCandidate> candidates && candidates.IsReadOnly &&
            child.Contexts is IList<ProgramSkillWindowContext> contexts && contexts.IsReadOnly,
            "Owning prelude, candidates and contexts expose frozen collections.");
        var receipt = child.DeferredTurnEndReturn!;
        Require(!Matches(parent, child with { DeferredTurnEndReturn = receipt with { ParentFrameId = parent.Id + 1 } }) &&
            !Matches(parent, child with { DeferredTurnEndReturn = receipt with { OwnerSeat = 2 } }) &&
            !Matches(parent, child with { DeferredTurnEndReturn = receipt with { TurnNumber = receipt.TurnNumber + 1 } }) &&
            !Matches(parent, child with { DeferredTurnEndReturn = receipt with { BatchId = receipt.BatchId + 1 } }) &&
            !Matches(parent with { Prelude = parent.Prelude! with { Completed = true } }, child) &&
            !Matches(parent, child with { Id = child.Id + 1 }) && !Matches(parent, child with { ResumeProgramFrameId = parent.Id }),
            "Actual stage, immediate typed parent, owner, turn, batch and child identity are separately required.");
        foreach (var altered in new[] {
            batch with { ParentFrameId = 99 }, batch with { ParentBatchId = 99 }, batch with { TurnNumber = 99 },
            batch with { AwaitingProgramFrameId = 99 }, batch with { OriginSkillId = "wrong" },
            batch with { OriginSkillInstanceId = "wrong" }, batch with { OriginOwnerSeat = 2 },
            batch with { Movements = Array.AsReadOnly(batch.Movements.Select(m => m with { CardId = m.CardId + 1 }).ToArray()) },
            batch with { SourceCounts = Array.AsReadOnly(batch.SourceCounts.Select(c => c with { CountAfter = c.CountAfter + 1 }).ToArray()) },
            batch with { DestinationCounts = Array.AsReadOnly(batch.DestinationCounts!.Select(c => c with { CountBefore = c.CountBefore + 1 }).ToArray()) } })
            Require(!Matches(parent, child with { Batch = altered }), "A same-id batch cannot substitute different scalar facts or movement/count contents.");
        var ordered = batch with { Movements = Array.AsReadOnly(new[] { batch.Movements[0], batch.Movements[0] with { Sequence = 999 } }) };
        var orderedParent = parent with { Prelude = parent.Prelude! with { CurrentChild = parent.Prelude.CurrentChild! with { Batch = ordered } } };
        Require(Matches(orderedParent, child with { Batch = ordered }) &&
            !Matches(orderedParent, child with { Batch = ordered with { Movements = Array.AsReadOnly(ordered.Movements.Reverse().ToArray()) } }),
            "The full batch comparison preserves movement order.");
    }

    private static void HoldOne(GameEngine g)
    {
        Accept(g, new UseProgramSkillCommand(0, "fixture:driver", "hold", [], [1], g.Revision, Prompt(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:driver");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(g.CardMovements.Count(m => m.To.Zone == CardZoneKind.PojunHold && m.To.OwnerSeat == 1) == 1,
            "The actual hold instruction moved one physical target card into its Pojun hold.");
    }

    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Accept(GameEngine g, GameCommand c)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());
        Require(result.Accepted, result.Error?.Message ?? "Rejected.");
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = Prompt(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision));
    }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, Prompt(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets)
    {
        Accept(g, new UseProgramSkillCommand(0, "fixture:driver", id, [], targets, g.Revision, Prompt(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 90; i++)
        {
            var p = Prompt(g); if (p is not null && predicate(p)) return;
            if (p is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger })
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("option-id") == "continue");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fixed boundary fixture did not reach its next prompt: " + JsonSerializer.Serialize(Prompt(g)));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static void Replay(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)),
        "All four prepared viewer states, owning frames, events, physical entities and commands restore identically.");
    private static void Reject(GameEngine g)
    {
        var before = State(g); var p = Prompt(g)!;
        Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("fixture:invalid"), g.Revision)).Accepted && before == State(g),
            "An illegal input cannot change the owning end cursor or any observable state.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(string mode)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(mode));
        if(mode is "old" or "return-old")r=ContentRegistry.Build(new WithoutAfterWindow(r));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:classic-after-end-fixture", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Accept(g, new SelectGeneralCommand(0, "fixture:owner", g.Revision, Prompt(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard); return (g, r);
    }
    // A negative catalog excludes both capabilities that opt into the actual-end movement prelude.
    // These discarded programs are never in the fixed fixture roster; retain their IDs.
    private sealed class WithoutAfterWindow(ContentRegistry source):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:after-end-no-window",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            foreach(var c in source.Cards.Values)b.AddCard(c);
            foreach(var s in source.Skills.Values)b.AddSkill(s.Program is {} p && (p.Triggers.Any(t=>t.Window==SkillProgramTriggerWindow.AfterTurnEnded || t.Effects.Any(e=>e.Op==SkillProgramEffectOp.HoldOwnerHandUntilTurnEnd)) || p.Activations.Any(a=>a.Effects.Any(e=>e.Op==SkillProgramEffectOp.HoldOwnerHandUntilTurnEnd)))?s with{Program=null}:s);
            foreach(var g in source.Generals.Values)b.AddGeneral(g);foreach(var d in source.Decks.Values)b.AddDeck(d);foreach(var m in source.Modes.Values)b.AddMode(m);
        }
    }
    private sealed class Fixture(string mode) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-after-end", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var after = mode is "old" or "return-old" ? "" : """
                ,{"id":"fixture:after-own","revision":1,"triggers":[{"id":"own","window":"afterTurnEnded","subject":"owner","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}
                ,{"id":"fixture:after-other","revision":1,"triggers":[{"id":"other","window":"afterTurnEnded","subject":"owner","turnOwnerScope":"otherLiving","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}
                """;
            var presentation = System.Text.Json.Nodes.JsonNode.Parse("""{"schemaVersion":3,"skills":{"fixture:driver":{"name":"真实驱动","description":"抽牌翻面和额外回合"},"fixture:due":{"name":"旧due","description":"真实对齐"},"fixture:movement":{"name":"移动暂停","description":"子链","optionLabels":{"continue":"继续"}},"fixture:gain":{"name":"获得暂停","description":"子链","optionLabels":{"continue":"继续"}},"fixture:after-own":{"name":"结束后","description":"真实抽牌"},"fixture:after-other":{"name":"他人结束后","description":"真实抽牌"},"fixture:return-gain":{"name":"返还观察","description":"实际返还和抽牌","optionLabels":{"continue":"继续"}},"fixture:return-nested":{"name":"返还抽牌子链","description":"暂停","optionLabels":{"continue":"继续"}}}}""")!;
            if (mode is "old" or "return-old") { presentation["skills"]!.AsObject().Remove("fixture:after-own"); presentation["skills"]!.AsObject().Remove("fixture:after-other"); }
            var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                {"id":"fixture:driver","revision":1,"activations":[
                {"id":"grow","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":8}]},
                {"id":"flip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"selectedTarget"}]},
                {"id":"hold","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"holdTargetCards","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"minimumCards":1,"resultBind":"held"}]},
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"pendExtraTurn","target":"owner","targetRef":{"kind":"selectedTarget"}}]}]},
                {"id":"fixture:due","revision":1,"triggers":[{"id":"schedule","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"scheduleDeferredHandAlignment","target":"selectedTarget","stateId":"settle"}]},{"id":"settle","window":"turnEnding","subject":"owner","optional":false,"deferredTurnEndOnly":true,"effects":[{"op":"resolveDeferredHandAlignment","target":"owner","resultBind":"discarded"}]}]},
                {"id":"fixture:movement","revision":1,"triggers":[{"id":"pause","window":"discardPileReceived","subject":"owner","movementReasons":["skill-program.deferred-hand.discard"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"choice","options":[{"id":"continue"}]}]}]},
                {"id":"fixture:gain","revision":1,"triggers":[{"id":"pause","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"choice","options":[{"id":"continue"}]}]}]}
                ,{"id":"fixture:return-gain","revision":1,"triggers":[{"id":"pause","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.pojun.return"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"choice","options":[{"id":"continue"}]},{"op":"draw","target":"owner","amount":1}]}]}
                ,{"id":"fixture:return-nested","revision":1,"triggers":[{"id":"pause","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fixture:return-gain.Draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"choice","options":[{"id":"continue"}]}]}]}
                {{{after}}}]}
                """, presentation.ToJsonString());
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, pair.Key) { Program = pair.Value });
            b.AddGeneral(new("fixture:owner", "边界", "supporter", "fixture:driver", "wei", 12,
                mode is "due" or "old" ? mode == "old" ? ["fixture:due", "fixture:movement"] : ["fixture:due", "fixture:movement", "fixture:gain", "fixture:after-own"] :
                mode == "skipped" ? ["fixture:after-other"] : mode == "return-old" ? [] : ["fixture:after-own"]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:target-{i}", "目标", "supporter", "standard:none", "wei", 12, i == 1 && mode is "return" or "return-old" ? ["fixture:return-gain", "fixture:return-nested"] : []));
            b.AddDeck(new("fixture:after-end-deck", "实体", 3, 2, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new("identity:classic-after-end-fixture", "边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:after-end-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:owner", "fixture:target-1", "fixture:target-2", "fixture:target-3"]));
        }
    }
}
