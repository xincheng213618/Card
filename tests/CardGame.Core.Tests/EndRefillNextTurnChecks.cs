using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class EndRefillNextTurnChecks
{
    private const string Gang = "ol:gangzhi", Bei = "ol:beizhan", Driver = "fixture:end-refill-driver";
    private const string Watch = "fixture:end-refill-watch", Loss = "fixture:end-refill-loss", Owner = "fixture:end-refill-owner";
    private const string Mode = "identity:classic-end-refill";
    private static string Reason => $"skill-program.{Bei}.end-refill-next-turn.draw";

    public static void ExactEntryAndFrozenIssuedEvents()
    {
        var original = Embedded("rules.json"); var labels = Embedded("presentation.json");
        _ = SkillProgramCatalog.Load(original, labels);
        foreach (var mutate in new Action<JsonObject>[]
        {
            t => t["window"] = "turnEnding", t => t["optional"] = false, t => t["turnOwnerScope"] = "otherLiving",
            t => t["usageScope"] = "game", t => t["effects"]![0]!["amount"] = 5,
            t => t["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}"""))
        })
        {
            var invalid = JsonNode.Parse(original)!; mutate(invalid["skills"]![1]!["triggers"]![0]!.AsObject());
            var rejected = false; try { _ = SkillProgramCatalog.Load(invalid.ToJsonString(), labels); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "Only the exact optional own completed-turn instruction is admitted.");
        }
        var targets = new List<int> { 0, 1 }; var parameters = new Dictionary<string, string> { ["program-action"] = "end-refill-next-turn" };
        var receipt = new EndRefillNextTurnReceipt { Targets = targets, Choices = [new(new("frozen"), "frozen", [], targets, parameters)] };
        var fact = (EndRefillNextTurnOfferedEvent)CommittedEventProjection.Freeze(new EndRefillNextTurnOfferedEvent(1, receipt));
        targets.Add(3); parameters["program-action"] = "changed";
        Need(fact.Receipt.Targets.SequenceEqual([0, 1]) && fact.Receipt.Choices[0].Parameters["program-action"] == "end-refill-next-turn" &&
            fact.Receipt.Targets is ICollection<int> { IsReadOnly: true } && fact.Receipt.Choices is ICollection<PromptChoice> { IsReadOnly: true } &&
            fact.Receipt.Choices[0].Targets is ICollection<int> { IsReadOnly: true } && fact.Receipt.Choices[0].Parameters is IDictionary<string, string> { IsReadOnly: true },
            "Offered and committed refill facts detach every nested target, choice and parameter collection.");
        var issued = (EndRefillNextTurnIssuedEvent)CommittedEventProjection.Freeze(new EndRefillNextTurnIssuedEvent(2, receipt));
        Need(issued.Receipt.Targets is ICollection<int> { IsReadOnly: true }, "The issued delayed source retains immutable participant identity.");
    }

    public static void ActualEndRefillCappedAndNextTurnMostIncludesTie()
    {
        foreach (var target in new[] { 0, 1 })
        {
            var (g, registry) = Start(peerHp: 8); var firstTurn = g.CreateSnapshot(0).TurnNumber;
            if (target == 0) { Use(g, "trim", cards: V(g, 0).Hand.Take(V(g, 0).HandCount - 2).Select(c => c.Id).ToArray()); Play(g); }
            End(g); Reach(g, () => Optional(P(g), Bei));
            Need(E<TurnEndedEvent>(g).Any(e => e.TurnNumber == firstTurn && e.ActorSeat == 0), "The skill is offered after the actual own TurnEnded fact.");
            g = Cold(g, registry); Activate(g); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "end-refill-next-turn") == true);
            Need(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([0])) && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([1])), "Self and every living other seat remain legal even when no draw is needed.");
            var before = V(g, target).HandCount; var cap = Math.Min(V(g, target).MaxHp, 5);
            Answer(g, c => c.Targets.SequenceEqual([target]));
            Reach(g, () => E<EndRefillNextTurnCompletedEvent>(g).Any());
            var issued = E<EndRefillNextTurnIssuedEvent>(g).Single();
            Need(issued.Receipt.DrawRequested == Math.Max(0, cap - before) && issued.Receipt.DrawActual == issued.Receipt.DrawRequested &&
                g.CardMovements.Count(m => m.Reason.Value == Reason && m.To == CardLocation.Hand(target)) == issued.Receipt.DrawActual,
                "Refill uses the actual chosen hand and MaxHP, draws real private cards, and caps the resulting hand at five.");
            g = Cold(g, registry);
            Reach(g, () => E<EndRefillNextTurnConsumedEvent>(g).Any(e => e.ProgramFrameId == issued.ProgramFrameId));
            var consumed = E<EndRefillNextTurnConsumedEvent>(g).Single(e => e.ProgramFrameId == issued.ProgramFrameId);
            Need(consumed.ActualTurnNumber > firstTurn && consumed.TargetSeat == target &&
                consumed.Restricted == (consumed.HandCount >= consumed.GreatestLivingHandCount), "The selected seat's next actual turn consumes the original issued source and uses a dynamic greatest-hand comparison including ties.");
            Need(E<CardTargetRestrictionGrantedEvent>(g).Count(e => e.Restriction.ParentFrameId == issued.ProgramFrameId) == (consumed.Restricted ? 1 : 0),
                "The due restriction is granted exactly once through the native turn target policy.");
            _ = Cold(g, registry);
        }
        var (tie, tieRegistry) = Start(peerHp: 3); End(tie); Reach(tie, () => Optional(P(tie), Bei)); Activate(tie);
        Reach(tie, () => P(tie)?.Choices.Any(c => c.Targets.SequenceEqual([1])) == true); Answer(tie, c => c.Targets.SequenceEqual([1]));
        Reach(tie, () => E<EndRefillNextTurnConsumedEvent>(tie).Any());
        Need(E<EndRefillNextTurnIssuedEvent>(tie).Single().Receipt.DrawActual == 0 && E<EndRefillNextTurnConsumedEvent>(tie).Single() is
            { HandCount: 4, GreatestLivingHandCount: 4, Restricted: true }, "A full hand still schedules the next turn, and tied most qualifies.");
        _ = Cold(tie, tieRegistry);
    }

    public static void ActualGainChildrenColdReturnAndLostSourceStillDue()
    {
        foreach (var child in new[] { "change-hand", "damage", "lose-source" })
        {
            var (g, registry) = Start(peerHp: 8, child: child); Use(g, "trim", cards: V(g, 0).Hand.Take(V(g, 0).HandCount - 2).Select(c => c.Id).ToArray()); Play(g);
            End(g); Reach(g, () => Optional(P(g), Bei)); Activate(g); Reach(g, () => P(g)?.Choices.Any(c => c.Targets.SequenceEqual([0])) == true);
            Answer(g, c => c.Targets.SequenceEqual([0])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.EndRefillNextTurn is not null);
            Need(root.PendingMovementContinuation is not null && root.EndRefillNextTurn!.Stage == EndRefillNextTurnStage.MovementChildren &&
                E<EndRefillNextTurnIssuedEvent>(g).Count() == 1 && !E<EndRefillNextTurnCompletedEvent>(g).Any(), "The actual gain child pauses after the single issued draw and before its typed completion.");
            g = Cold(g, registry); Continue(g);
            if (child == "lose-source")
            {
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Loss); g = Cold(g, registry); Continue(g);
            }
            Reach(g, () => E<EndRefillNextTurnCompletedEvent>(g).Any());
            Need(E<EndRefillNextTurnIssuedEvent>(g).Count() == 1 && g.CardMovements.Count(m => m.Reason.Value == Reason) == 2 &&
                E<EndRefillNextTurnCompletedEvent>(g).Count() == 1, "Nested gain, damage or genuine skill replacement returns without issuing another refill.");
            if (child == "lose-source") Need(V(g, 0).Skills!.All(s => s.ContentId != Bei), "The source skill was really replaced by its legal Dying entry.");
            g = Cold(g, registry); Reach(g, () => E<EndRefillNextTurnConsumedEvent>(g).Any());
            Need(E<EndRefillNextTurnConsumedEvent>(g).Count() == 1, "The issued next-turn obligation survives loss of its original skill source.");
            _ = Cold(g, registry);
        }
    }

    public static void ActualNextTurnSkippedConsumesAndSelfOnlyExpires()
    {
        var (extra, extraRegistry) = Start(); var prior = extra.CreateSnapshot(0).TurnNumber;
        Use(extra, "extra-turn"); Play(extra); End(extra); Reach(extra, () => Optional(P(extra), Bei)); Activate(extra);
        Reach(extra, () => P(extra)?.Choices.Any(c => c.Targets.SequenceEqual([0])) == true); Answer(extra, c => c.Targets.SequenceEqual([0]));
        Reach(extra, () => E<EndRefillNextTurnConsumedEvent>(extra).Any());
        Need(E<EndRefillNextTurnConsumedEvent>(extra).Single() is { TargetSeat: 0, Restricted: true } extraDue &&
            extraDue.ActualTurnNumber == prior + 1 && E<TurnStartedEvent>(extra).Last().ActorSeat == 0,
            "The immediate actual extra turn consumes the selected seat's obligation without waiting for a regular seat rotation.");
        _ = Cold(extra, extraRegistry);
        var (skipped, skipRegistry) = Start(); Use(skipped, "turn-over"); Play(skipped); End(skipped);
        Reach(skipped, () => Optional(P(skipped), Bei)); Activate(skipped); Reach(skipped, () => P(skipped)?.Choices.Any(c => c.Targets.SequenceEqual([0])) == true);
        Answer(skipped, c => c.Targets.SequenceEqual([0])); Reach(skipped, () => E<EndRefillNextTurnConsumedEvent>(skipped).Any());
        var due = E<EndRefillNextTurnConsumedEvent>(skipped).Single();
        Reach(skipped, () => E<TurnEndedEvent>(skipped).Any(e => e.ActorSeat == 0 && e.TurnNumber == due.ActualTurnNumber));
        Need(!V(skipped, 0).IsFaceDown && E<EndRefillNextTurnConsumedEvent>(skipped).Count() == 1, "A real face-down skipped next turn consumes the due once rather than carrying it to another turn.");
        _ = Cold(skipped, skipRegistry);
        var (self, registry) = Start(card: "standard:slash"); End(self); Reach(self, () => Optional(P(self), Bei)); Activate(self);
        Reach(self, () => P(self)?.Choices.Any(c => c.Targets.SequenceEqual([0])) == true); Answer(self, c => c.Targets.SequenceEqual([0])); Play(self);
        var restriction = E<EndRefillNextTurnConsumedEvent>(self).Single();
        Need(restriction.Restricted && V(self, 0).Hand.Any(c => c.Kind == CardKind.Slash) &&
            self.GetHumanLegalActions().All(a => a.Kind != LegalActionKind.Slash || a.TargetSeats.All(s => s == 0)), "The actual next self turn filters every foreign Slash card target through the native SelfOnly policy.");
        var trim = V(self, 0).Hand.Take(V(self, 0).HandCount - 1).Select(c => c.Id).ToArray();
        Use(self, "trim", cards: trim); Play(self); Need(self.GetHumanLegalActions().All(a => a.Kind != LegalActionKind.Slash || a.TargetSeats.All(s => s == 0)), "Dropping below the most-hand threshold does not remove the already granted turn restriction.");
        End(self); Reach(self, () => Optional(P(self), Bei)); Answer(self, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(self);
        Need(E<EndRefillNextTurnConsumedEvent>(self).Count() == 1 && self.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Any(s => s != 0)),
            "The restriction expires with that actual turn and no second activation is invented.");
        _ = Cold(self, registry);
    }

    public static void ActualBidirectionalHpReplacementSelfSourceLessAndChain()
    {
        foreach (var activation in new[] { "outgoing", "incoming" })
        {
            var (g, registry) = Start(); var target = activation == "outgoing" ? 1 : 0; var before = V(g, target).Hp;
            Use(g, activation, targets: [1]); Play(g);
            var replaced = E<OtherParticipantDamageReplacedEvent>(g).Single();
            Need(V(g, target).Hp == before - 2 && replaced.Source.OwnerSeat == 0 && replaced.DamageSourceSeat == (activation == "outgoing" ? 0 : 1) &&
                replaced.TargetSeat == target && replaced.BaseAmount == 2 && !E<DamageAppliedEvent>(g).Any(),
                "Both outgoing and incoming real other-participant attacks become two HP loss, while retaining policy and actual source separately.");
            _ = Cold(g, registry);
        }
        foreach (var activation in new[] { "self-damage", "source-less" })
        {
            var (g, registry) = Start(); Use(g, activation); Play(g);
            Need(!E<OtherParticipantDamageReplacedEvent>(g).Any() && E<DamageAppliedEvent>(g).Single().SourceLess == (activation == "source-less"),
                "Self damage and damage without an attributed source remain genuine damage.");
            _ = Cold(g, registry);
        }
        var (chain, chainRegistry) = Start(); Use(chain, "chain", targets: [0]); Play(chain); Use(chain, "chain", targets: [1]); Play(chain);
        Use(chain, "self-fire"); Play(chain);
        Need(E<DamageAppliedEvent>(chain).Count() == 1 && E<DamageAppliedEvent>(chain).Single().TargetSeat == 0 &&
            E<OtherParticipantDamageReplacedEvent>(chain).Single() is { TargetSeat: 1, ChainPropagation: true } && !V(chain, 0).IsChained && !V(chain, 1).IsChained,
            "Genuine self fire starts the native chain, while its other-seat propagated portion independently converts to HP loss.");
        _ = Cold(chain, chainRegistry);
        var (loss, lossRegistry) = Start(); Use(loss, "chain", targets: [0]); Play(loss); Use(loss, "chain", targets: [1]); Play(loss);
        Use(loss, "outgoing-fire", targets: [1]); Play(loss);
        Need(!E<DamageAppliedEvent>(loss).Any() && V(loss, 0).IsChained && V(loss, 1).IsChained, "An initial other-seat HP replacement neither unchains nor propagates elemental damage.");
        _ = Cold(loss, lossRegistry);
    }

    public static void ActualAlcoholSlashMaterialsAndDamageOnlyArmorBoundaries()
    {
        foreach (var armorKind in new[] { CardKind.Tengjia, CardKind.SilverLion })
        {
            var (g, registry) = Start(card: "fixture:end-refill-mixed");
            // Four fixed twenty-card draws consume the small fixture's remaining deck.
            // Every native material is prepared through commands and movement receipts.
            for (var count = 0; count < 4; count++) { Use(g, "draw20"); Play(g); }
            var weapon = V(g, 0).Hand.First(c => c.Kind == CardKind.GudingBlade).Id;
            var armor = V(g, 0).Hand.First(c => c.Kind == armorKind).Id;
            var fire = V(g, 0).Hand.First(c => c.Kind == CardKind.FireSlash).Id;
            var wine = V(g, 0).Hand.First(c => c.Kind == CardKind.Alcohol).Id;
            PlayNative(g, g.GetHumanLegalActions().Single(a => a.CardId == weapon && a.Kind == LegalActionKind.Equip)); Play(g);
            Use(g, "place", cards: [armor], targets: [1]); Play(g);
            Use(g, "strip", targets: [1]); Play(g);
            Need(V(g, 1).HandCount == 0 && V(g, 1).Equipment.Any(c => c.Id == armor) && V(g, 0).Equipment.Any(c => c.Id == weapon),
                "The actual empty-hand recipient and both physical damage-only equipment conditions are prepared before the real Slash.");
            Use(g, "chain", targets: [0]); Play(g); Use(g, "chain", targets: [1]); Play(g);
            PlayNative(g, g.GetHumanLegalActions().Single(a => a.CardId == wine && a.Kind == LegalActionKind.Alcohol)); Play(g);
            var before = V(g, 1).Hp;
            PlayNative(g, g.GetHumanLegalActions().Single(a => a.CardId == fire && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]))); Play(g);
            var replaced = E<OtherParticipantDamageReplacedEvent>(g).Single();
            Need(replaced.BaseAmount == 2 && V(g, 1).Hp == before - 2 && !E<GudingBladeDamageIncreasedEvent>(g).Any() &&
                !E<TengjiaFireDamageIncreasedEvent>(g).Any() && !E<SilverLionDamageCappedEvent>(g).Any() && !E<DamageAppliedEvent>(g).Any() &&
                V(g, 0).IsChained && V(g, 1).IsChained, "Alcohol's actual two-point card base is retained; damage-only blade/armor/prevention and a new chain do not run after HP replacement.");
            Need(g.CreateCardZoneDiagnostics().Single(z => z.CardId == fire).Location == CardLocation.DiscardPile &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == replaced.AttackFrameId) == 1,
                "The real physical Slash still pays its unique original material and finishes exactly one use after HP loss.");
            _ = Cold(g, registry);
        }
    }

    private static string Embedded(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-shen-pei.{suffix}")!; using var r = new StreamReader(stream); return r.ReadToEnd(); }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Accept(GameEngine g, GameCommand cmd) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([cmd])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native refill command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string activation, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => Accept(g,
        new UseProgramSkillCommand(0, Driver, activation, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void PlayNative(GameEngine g, LegalAction action) => Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision,
        P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 160; step++)
        {
            if (done()) return;
            if (P(g) is { PlayerSeat: 0 } p)
            {
                if (Optional(p, Bei)) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RescueDying or DecisionKind.Nullification or DecisionKind.RespondSlash or DecisionKind.RespondDodge)
                    Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die" or "take-damage");
                else throw new InvalidOperationException($"Unexpected native refill prompt {p.Kind}: {p.Prompt}");
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed refill fixture did not reach its bounded actual boundary.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "Cold replay preserves exact target drafts, issued draws, nested child cursors and delayed next-turn source."); return cold; }
    private static void Need(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Start(int peerHp = 8, string child = "none", string card = "standard:dodge")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(peerHp, child, card));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(int peerHp, string child, string card) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("end-refill-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var content = SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json"));
            foreach (var (id, program) in content.Programs) b.AddSkill(new(id, content.Presentations[id].Name, content.Presentations[id].Description) { Program = program, ProgramPresentation = content.Presentations[id], Tags = id == Gang ? SkillTag.Locked : SkillTag.None });
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"trim","minCards":1,"maxCards":64,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trim"},{"op":"moveBoundCards","target":"owner","sourceBind":"trim","destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"outgoing","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
                {"id":"self-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"self-fire","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"nature":"fire"}]},
                {"id":"outgoing-fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                {"id":"turn-over","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"owner"}]},
                {"id":"extra-turn","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"draw20","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"strip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"selectedTarget","zones":["hand"],"amount":64}]},
                {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Reason}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var effects = rules["skills"]![1]!["triggers"]![0]!["effects"]!.AsArray();
            if (child == "change-hand") effects.Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}"""));
            if (child == "damage") effects.Add(JsonNode.Parse("""{"op":"damage","target":"owner","amount":1}"""));
            if (child == "lose-source")
            {
                effects.Add(JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":4}"""));
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Loss}}","revision":1,"triggers":[{"id":"source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":4,"clampToMaxHp":true},{"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Bei}}"],"sourceBind":"standard:none"}]}]}
                """));
            }
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实规则准备", description = "真实规则实体准备" },
                [Watch] = new { name = "获得子窗", description = "真实已付获得子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            if (child == "lose-source") labels[Loss] = new { name = "濒死源替换", description = "真实濒死回复及源替换", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var fixture = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in fixture.Programs) b.AddSkill(new(id, fixture.Presentations[id].Name, fixture.Presentations[id].Description) { Program = program, ProgramPresentation = fixture.Presentations[id] });
            var skills = new List<string> { Bei, Driver }; if (child != "none") skills.Add(Watch); if (child == "lose-source") skills.Add(Loss);
            b.AddGeneral(new(Owner, "实际审配技能拥有者", "supporter", Gang, "qun", 3, skills, GeneralGender.Male));
            const string peerSkill = "fixture:end-refill-peer-skill";
            var quiet = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{peerSkill}}","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [peerSkill] = new { name = "固定其他角色", description = "固定其他角色不主动杀" } } }));
            b.AddSkill(new(peerSkill, "固定其他角色", "固定其他角色选择") { Program = quiet.Programs[peerSkill], ProgramPresentation = quiet.Presentations[peerSkill], SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:end-refill-peer-{i}").ToArray(); foreach (var peer in peers) b.AddGeneral(new(peer, "实际其他角色", "supporter", peerSkill, "wei", peerHp));
            if (card == "fixture:end-refill-mixed")
            {
                b.AddCard(new("fixture:end-refill-blade", "古锭刀", "装备牌", "真实古锭刀", CardKind.GudingBlade));
                b.AddCard(new("fixture:end-refill-vine", "藤甲", "装备牌", "真实藤甲", CardKind.Tengjia));
                b.AddCard(new("fixture:end-refill-lion", "白银狮子", "装备牌", "真实白银狮子", CardKind.SilverLion));
            }
            var sequence = card == "fixture:end-refill-mixed" ? new[] { "fixture:end-refill-blade", "fixture:end-refill-vine", "fixture:end-refill-lion", "standard:fire_slash", "standard:alcohol", "standard:peach" } : [card];
            b.AddDeck(new("fixture:end-refill-deck", "固定实体牌", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(sequence[i % sequence.Length], Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "实际回合和伤害替代", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:end-refill-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
