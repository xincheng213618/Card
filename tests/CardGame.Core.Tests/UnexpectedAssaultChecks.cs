using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class UnexpectedAssaultChecks
{
    private const string Pochu = "fixture:unexpected-pochu", Driver = "fixture:unexpected-driver";
    private const string Completed = "fixture:unexpected-completed", Suppress = "fixture:unexpected-suppress";
    private const string Counter = "fixture:unexpected-counter", Mode = "identity:unexpected-fixture";

    public static void OpaqueRevealDamageNullificationAndConversionLock()
    {
        foreach (var scenario in new[]
        {
            (Converted: false, UseSuit: Suit.Spade, TargetSuit: Suit.Spade),
            (Converted: false, UseSuit: Suit.Heart, TargetSuit: Suit.Spade),
            (Converted: true, UseSuit: Suit.Spade, TargetSuit: Suit.Spade),
            (Converted: true, UseSuit: Suit.Heart, TargetSuit: Suit.Spade),
            (Converted: true, UseSuit: Suit.None, TargetSuit: Suit.None),
            (Converted: true, UseSuit: Suit.None, TargetSuit: Suit.Spade),
            (Converted: true, UseSuit: Suit.Spade, TargetSuit: Suit.None),
            (Converted: true, UseSuit: Suit.Heart, TargetSuit: Suit.Heart)
        })
            VerifyReveal(scenario.Converted, scenario.UseSuit, scenario.TargetSuit);
        VerifyNullification();
        VerifySourceLossAndActualTurn();
        var (empty, _) = Create(emptyPeers: true); Play(empty);
        Require(!empty.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.UnexpectedAssault),
            "Neither a real card nor its hand conversion targets self or another character with no hand card.");
    }

    private static void VerifyReveal(bool converted, Suit useSuit, Suit targetSuit)
    {
        var (g, r) = Create(useSuit, targetSuit); Play(g);
        var action = Action(g, converted);
        var beforeHand = V(g, 0).Hand.Select(c => c.Id).ToArray();
        var targetHand = V(g, 1).Hand.Select(c => c.Id).ToArray();
        var hp = V(g, 1).Hp;
        Require(targetHand.Length == 4 && !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.UnexpectedAssault && a.TargetSeats.Contains(0)),
            "The source chooses another real character with a nonempty hand.");
        SubmitPlay(g, action); Reach(g, p => p.Kind == DecisionKind.SelectTargetCard && p.PlayerSeat == 0);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardKind == CardKind.UnexpectedAssault);
        var select = g.ResolutionStack.OfType<TargetCardSelectionFrame>().Single();
        var prompt = P(g)!;
        Require(select.ParentFrameId == use.Id && select.CandidateSlots.Count == targetHand.Length &&
            use.Action is { EffectiveKind: CardKind.UnexpectedAssault } declared && declared.EffectiveSuit == useSuit &&
            declared.PhysicalCards.Select(c => c.CardId).SequenceEqual([action.CardId!.Value]) &&
            (converted ? use.NoDamageSkillDisablePolicy?.Source == action.ConversionSource : use.NoDamageSkillDisablePolicy is null) &&
            F<UnexpectedAssaultRevealedEvent>(g).Length == 0 && F<CardsRevealedEvent>(g).Length == 0 &&
            prompt.IsPrivate && prompt.ValidCardIds.Count == 0 && prompt.Choices.Count == targetHand.Length &&
            prompt.Choices.All(c => c.Cards.Count == 0 && c.Targets.SequenceEqual([1]) &&
                c.Parameters.GetValueOrDefault("action") == "target-card-slot" && c.Parameters.ContainsKey("slot-index")),
            "The actual native use freezes its effective input suit before payment and offers only opaque source-side hand slots.");
        PrivateAndFrozen(g); Reject(g); g = Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == "0");
        Reach(g, p => p.SkillPrompt?.SkillId == Completed);
        var receipt = F<UnexpectedAssaultRevealedEvent>(g).Single().Receipt;
        var damaged = useSuit != Suit.None && useSuit != targetSuit;
        use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == use.Id);
        Require(receipt.CardUseFrameId == use.Id && receipt.ActionId == use.Action!.ActionId && receipt.SourceSeat == 0 &&
            receipt.TargetSeat == 1 && receipt.TargetIndex == 0 && receipt.RevealedCardId == targetHand[0] &&
            receipt.RevealedPrintedSuit == Suit.Spade && receipt.EffectiveUseSuit == useSuit && receipt.EffectiveRevealedSuit == targetSuit &&
            receipt.CanCauseDamage == damaged && use.UnexpectedAssaultReveal == receipt && use.CausedDamage == damaged &&
            V(g, 1).Hp == hp - (damaged ? 1 : 0) && V(g, 1).Hand.Select(c => c.Id).SequenceEqual(targetHand) &&
            F<DamageAppliedEvent>(g).Count(d => !d.SourceLess && d.SourceSeat == 0 && d.TargetSeat == 1 && d.Amount == 1 && d.Nature == DamageNature.Normal) == (damaged ? 1 : 0) &&
            F<CardsRevealedEvent>(g).Single() is { Cards: [var actual] } && actual.Id == targetHand[0] &&
            F<CardsRevealedEvent>(g).Single().Cards is System.Collections.IList { IsReadOnly: true },
            "One actual target hand card is publicly revealed without moving it; only a suited use with a different effective suit causes ordinary source-attributed damage.");
        Require(F<NoDamageSkillDisablePolicyIssuedEvent>(g).Length == (converted ? 1 : 0) &&
            F<CurrentTurnOwnSkillSuppressionIssuedEvent>(g).Count(e => e.Suppression.SuppressedSkillId == Pochu) == (converted && !damaged ? 1 : 0),
            "Whole-use damage controls the one accepted conversion disable; a naturally played card has no skill-disable policy.");
        PrivateAndFrozen(g); g = Cold(g, r); Continue(g); Play(g);
        Require(F<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id && e.CardId == action.CardId && e.CardKind == CardKind.UnexpectedAssault) == 1 &&
            g.CardMovements.Count(m => m.CardId == action.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            g.CardMovements.Count(m => m.CardId == action.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            !g.CardMovements.Any(m => targetHand.Contains(m.CardId) && m.From == CardLocation.Hand(1)) &&
            V(g, 0).Hand.Select(c => c.Id).ToHashSet().SetEquals(beforeHand.Where(id => id != action.CardId)) &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.UnexpectedAssault && a.ConversionSource is null) &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.UnexpectedAssault && a.ConversionSource?.SkillId == Pochu) == (!converted || damaged),
            "After one native completion and cold restore, only the original one hand cost is discarded and only a failed conversion is disabled for this actual turn.");
        _ = Cold(g, r);
    }

    private static void VerifyNullification()
    {
        var (g, r) = Create(Suit.Heart, counter: true); Play(g);
        var action = Action(g, true); var targetHand = V(g, 1).Hand.Select(c => c.Id).ToArray(); var hp = V(g, 1).Hp;
        SubmitPlay(g, action);
        Reach(g, p => p.Kind == DecisionKind.Nullification && p.PlayerSeat == 0 &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("conversion-binding-id") == "counter"));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardKind == CardKind.UnexpectedAssault);
        g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("conversion-binding-id") == "counter");
        Reach(g, p => p.SkillPrompt?.SkillId == Completed);
        Require(F<NullificationResolvedEvent>(g).Single(e => e.EffectCardId == action.CardId).EffectNullified &&
            F<UnexpectedAssaultRevealedEvent>(g).Length == 0 && F<TargetCardSelectionRequestedEvent>(g).Length == 0 &&
            F<CardsRevealedEvent>(g).Length == 0 && F<DamageAppliedEvent>(g).Length == 0 && V(g, 1).Hp == hp &&
            V(g, 1).Hand.Select(c => c.Id).SequenceEqual(targetHand) &&
            F<CurrentTurnOwnSkillSuppressionIssuedEvent>(g).Single().Suppression.CardUseFrameId == use.Id,
            "A real paid native Nullification cancels the whole trick before any hand slot or reveal, and the converted non-damaging use settles its exact once-only disable.");
        g = Cold(g, r); Continue(g); Play(g);
        Require(F<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
            g.CardMovements.Count(m => m.CardId == action.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "Nullified converted material completes and is discarded once through the original native use.");
    }

    private static void VerifySourceLossAndActualTurn()
    {
        var (g, r) = Create(sourceLoss: true); Play(g);
        Use(g, Driver, "regain"); Play(g);
        Require(V(g, 0).Hp == 5 && V(g, 0).Skills!.Any(s => s.Id == Pochu) &&
            F<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.Contains(Pochu)) == 1,
            "The fixed small identity mode starts its Lord at HP5 and genuinely acquires the convertible skill through the driver's published activation.");
        var action = Action(g, true); var oldSource = action.ConversionSource!; SubmitPlay(g, action);
        Reach(g, p => p.Kind == DecisionKind.SelectTargetCard && p.PlayerSeat == 0);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardKind == CardKind.UnexpectedAssault);
        var sourceSuppressed = !V(g, 0).Skills!.Any(s => s.Id == Pochu);
        var suppressorEnabled = V(g, 0).Skills!.Any(s => s.Id == Suppress);
        var exactPolicy = use.NoDamageSkillDisablePolicy?.Source == oldSource;
        var committedGrant = F<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 0 &&
            e.SourceSkillId == "fixture:unexpected-committed" && e.SkillIds.Contains(Suppress));
        var committedBinding = F<ProgramBindingStartedEvent>(g).Any(e => e.OwnerSeat == 0 &&
            e.SkillId == "fixture:unexpected-committed" && e.BindingId == "lose-source" && e.Window == SkillProgramTriggerWindow.CardUseCommitted);
        Require(V(g, 0).Hp == 5 && sourceSuppressed && suppressorEnabled && exactPolicy && committedGrant && committedBinding,
            "A real committed-use grant suppresses the accepted acquired conversion source before reveal; its owning frozen policy remains payable without rechecking live qualification: " +
            JsonSerializer.Serialize(new { sourceSuppressed, suppressorEnabled, exactPolicy, committedGrant, committedBinding,
                Self = V(g, 0), Source = oldSource, Policy = use.NoDamageSkillDisablePolicy, Acquisitions = F<SkillsAcquiredEvent>(g) }));
        g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == "0"); Play(g);
        Require(F<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
            F<CurrentTurnOwnSkillSuppressionIssuedEvent>(g).Single().Suppression.Source.SkillInstanceId == oldSource.SkillInstanceId,
            "The accepted use settles one no-damage disable even after losing its live conversion qualification.");
        Use(g, Suppress, "retire"); Play(g);
        Require(F<ProgramOwnerSkillsReplacedEvent>(g).Any(e => e.SkillId == Suppress && e.LostSkillIds.Contains(Pochu) && e.LostSkillIds.Contains(Suppress)),
            "The suppressor's legal terminal activation physically removes both old grants before reacquisition.");
        Use(g, Driver, "regain"); Play(g);
        Require(F<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.Contains(Pochu)) == 2 &&
            !g.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Pochu),
            "A physical loss and regrant cannot refresh the failed skill inside the same actual turn.");
        Use(g, Driver, "extra"); Play(g); End(g);
        Until(g, e => F<TurnStartedEvent>(e).Count(t => t.ActorSeat == 0) == 2 && P(e) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        var restoredActions = g.GetHumanLegalActions().Where(a => a.ConversionSource?.SkillId == Pochu).ToArray();
        // Ordinary acquired grants deliberately derive both grant and instance
        // IDs from their granting skill and acquired skill. Removing/regranting
        // through the same Driver therefore restores the same stable identity.
        var stableSourceRestored = restoredActions.Any(a => a.Kind == LegalActionKind.UnexpectedAssault &&
            a.TargetSeats.SequenceEqual([1]) && a.ConversionSource == oldSource);
        var policy = use.NoDamageSkillDisablePolicy!;
        var expiry = F<CurrentTurnOwnSkillSuppressionsExpiredEvent>(g);
        var exactOldTurnExpired = expiry is [var ended] && ended.TurnNumber == policy.TurnNumber && ended.TurnOwnerSeat == policy.TurnOwnerSeat;
        var newActualTurn = F<TurnStartedEvent>(g).Last(e => e.ActorSeat == 0).TurnNumber > policy.TurnNumber;
        Require(stableSourceRestored && exactOldTurnExpired && newActualTurn,
            "The same stable acquired source becomes usable only after the old actual turn ends, including an immediate extra turn: " +
            JsonSerializer.Serialize(new { stableSourceRestored, exactOldTurnExpired, newActualTurn, OldSource = oldSource,
                Policy = policy, Expiry = expiry, RestoredActions = restoredActions, Self = V(g, 0), Turns = F<TurnStartedEvent>(g) }));
        _ = Cold(g, r);
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static LegalAction Action(GameEngine g, bool converted) => g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.UnexpectedAssault &&
        a.TargetSeats.SequenceEqual([1]) && (converted ? a.ConversionSource?.SkillId == Pochu : a.ConversionSource is null));
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Real Unexpected Assault fixture command rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string skill, string id) => Accept(g, new UseProgramSkillCommand(0, skill, id, [], [], g.Revision, P(g)!.PromptId));
    private static void SubmitPlay(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind) { ConversionSource = a.ConversionSource });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => Until(g, e => P(e) is { } p && predicate(p));
    private static void Until(GameEngine g, Func<GameEngine, bool> predicate)
    {
        for (var step = 0; step < 160; step++)
        {
            if (predicate(g)) return;
            var p = P(g);
            if (p is { PlayerSeat: 0, Kind: DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) Continue(g);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fixed Unexpected Assault fixture did not reach its real boundary: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(restored) == State(g), "Four-view cold restoration preserves exact opaque slots, owning reveal, frozen policy, facts, movements and commands."); return restored; }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(0, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished opaque slot cannot reveal or move a hidden card."); }
    private static void PrivateAndFrozen(GameEngine g)
    {
        var p = P(g)!; Require(p.IsPrivate, "The actual source decision is private.");
        foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat))
            Require(g.CreateSnapshot(viewer).PendingDecision is null && g.CreateSnapshot(viewer).Players[p.PlayerSeat].Hand.Count == 0,
                "Another viewer receives neither the private source prompt nor source hand identities.");
        Require(g.CreateSnapshot(0).Players[1].Hand.Count == 0 &&
            p.ValidCardIds is System.Collections.IList { IsReadOnly: true } && p.ValidTargetSeats is System.Collections.IList { IsReadOnly: true } &&
            p.ValidContentIds is System.Collections.IList { IsReadOnly: true } && p.Choices is System.Collections.IList { IsReadOnly: true } &&
            p.Choices.All(c => c.Cards is System.Collections.IList { IsReadOnly: true } && c.Targets is System.Collections.IList { IsReadOnly: true } &&
                c.ContentIds is System.Collections.IList { IsReadOnly: true } && c.Parameters is System.Collections.IDictionary { IsReadOnly: true }),
            "The source cannot inspect the target hand through its prepared view, and every exposed nested prompt collection is frozen.");
    }
    private static (GameEngine, ContentRegistry) Create(Suit useSuit = Suit.Spade, Suit targetSuit = Suit.Spade, bool counter = false, bool sourceLoss = false, bool emptyPeers = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(useSuit, targetSuit, counter, sourceLoss, emptyPeers));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Require(P(g)!.ValidContentIds.Contains("fixture:unexpected-owner"), "The fixed real general choice contains the mechanism owner.");
        Accept(g, new SelectGeneralCommand(0, "fixture:unexpected-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private sealed class Fixture(Suit useSuit, Suit targetSuit, bool counter, bool sourceLoss, bool emptyPeers) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:unexpected", "1.0.0", "原生出其不意的真实展示与结算");
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:unexpected-pochu","revision":1,"viewAs":[{"id":"hand","inputCount":1,"sourceZones":["hand"],"inputKinds":[],"inputSuits":[],"outputKind":"unexpectedAssault","forPlay":true,"forResponse":false,"allowSameKind":true,"useEffectiveInputSuit":true,"disableSkillUntilTurnEndIfNoDamage":true}]},
              {"id":"fixture:unexpected-driver","revision":1,"activations":[
                {"id":"regain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["fixture:unexpected-pochu"]}]},
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}]},
              {"id":"fixture:unexpected-completed","revision":1,"triggers":[{"id":"done","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["unexpectedAssault"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"done","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:unexpected-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:unexpected-counter","revision":1,"viewAs":[{"id":"counter","inputKinds":[],"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true}]},
              {"id":"fixture:unexpected-committed","revision":1,"triggers":[{"id":"lose-source","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["unexpectedAssault"],"optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["fixture:unexpected-suppress"]}]}]},
              {"id":"fixture:unexpected-suppress","revision":1,"activations":[{"id":"retire","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["fixture:unexpected-pochu","fixture:unexpected-suppress"],"sourceBind":"fixture:unexpected-noop"}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            AddSuit("fixture:unexpected-owner-suit", useSuit); AddSuit("fixture:unexpected-peer-suit", targetSuit);
            if (emptyPeers) rules["skills"]![1]!["modifiers"] = JsonNode.Parse("""[{"id":"initial","query":"initialHandSize","operation":"add","value":4,"priority":0,"condition":{"kind":"always"}}]""");
            void AddSuit(string id, Suit suit)
            {
                if (suit == Suit.Spade) return;
                rules["skills"]!.AsArray().Add(JsonNode.Parse(JsonSerializer.Serialize(new { id, revision = 1,
                    cardPolicies = new[] { new { id = "effective-suit", kind = "rewriteSuit", inputSuit = "spade", outputSuit = suit.ToString().ToLowerInvariant() } } })));
            }
            var labels = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var label = new Dictionary<string, object> { ["name"] = "真实出其不意机制", ["description"] = "原生结算" };
                if (n!["id"]!.GetValue<string>() == Completed) label["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)label;
            });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = 3, skills = labels }));
            var unsupported = rules.DeepClone();
            unsupported["skills"]![0]!["viewAs"]![0]!["singleCardTrickUse"] = true;
            var rejected = false;
            try { _ = SkillProgramCatalog.Load(unsupported.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = 3, skills = labels })); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The accepted plain hand conversion cannot opt into the unsupported generic single-card-trick executor.");
            foreach (var program in catalog.Programs.Values)
                b.AddSkill(new(program.Id, program.Id, "共享真实机制") { Program = program, SuppressionRule = program.Id == Suppress ? new(5) : null });
            b.AddSkill(new("fixture:unexpected-noop", "真实替换", "无运行能力"));
            b.AddSkill(new("fixture:unexpected-selection", "固定候选", "固定小夹具") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var owner = sourceLoss ? new List<string> { Completed } : new List<string> { Driver, Completed };
            if (useSuit != Suit.Spade) owner.Add("fixture:unexpected-owner-suit");
            if (counter) owner.Add(Counter); if (sourceLoss) owner.Add("fixture:unexpected-committed");
            b.AddGeneral(new("fixture:unexpected-owner", "原生展示拥有者", "supporter", sourceLoss ? Driver : Pochu, "jin", 4, owner, GeneralGender.Male));
            for (var seat = 1; seat < 4; seat++)
                b.AddGeneral(new($"fixture:unexpected-peer-{seat}", "原生展示目标", "supporter", "fixture:unexpected-selection", "qun", 4,
                    targetSuit == Suit.Spade ? ["fixture:unexpected-quiet"] : ["fixture:unexpected-quiet", "fixture:unexpected-peer-suit"], GeneralGender.Male));
            b.AddDeck(new("fixture:unexpected-deck", "固定真实出其不意", emptyPeers ? 0 : 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:unexpected_assault", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "原生出其不意", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:unexpected-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:unexpected-owner", "fixture:unexpected-peer-1", "fixture:unexpected-peer-2", "fixture:unexpected-peer-3"]));
        }
    }
}
