using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryChenLinChecks
{
    private const string Mode = "identity:classic-chen-lin-fixture", Driver = "fixture:cl-driver", Gain = "fixture:cl-gain", Hp = "fixture:cl-hp";
    private const string Pulse = "fixture:cl-pulse", Foreign = "fixture:cl-offer", Bifa = "ol:bifa", Songci = "ol:songci";

    public static void DeferredPrivateOfferTargetsNextActualStartAndExchangeReturnsOnce()
    {
        var (g, r) = Create(); Play(g); var id = g.CreateSnapshot(0).Players[0].Hand[0].Id;
        End(g); Reach(g, p => p.SkillPrompt?.SkillId == Bifa && Has(p, "activate"));
        Answer(g, P(g)!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Reach(g, p => p.SkillPrompt?.SkillId == Bifa && Has(p, "select-owned-cards"));
        g = Cold(g, r); Answer(g, P(g)!.Choices.Single(c => c.Cards.SequenceEqual([id])));
        Reach(g, p => p.SkillPrompt?.SkillId == Bifa && Has(p, "select-target"));
        g = Cold(g, r); Answer(g, P(g)!.Choices.Single(c => c.Targets.SequenceEqual([1])));
        ReachFrame(g, f => f.PrivateOffer?.Stage == PrivateOfferStage.ChoosingExchange);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PrivateOffer is { Stage: PrivateOfferStage.ChoosingExchange });
        var held = root.PrivateOffer!; var deposit = E<PrivateCardOfferDepositedEvent>(g).Single();
        Require(deposit.TargetSeat == 1 && root.WindowContext?.ParentFrameId == g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(w => w.DeferredPrivateOffers is not null).Id &&
            g.State.CurrentSeat == 1 && g.State.TurnNumber > deposit.CreatedTurn,
            "The exact private entity is offered at its target's next actual turn start, under a real due lifecycle candidate.");
        Require(g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == held.Location) == 1 &&
            !g.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == id), "Ending pays one real private deposit; ordinary turn-end return does not refund it early.");
        CheckPrivateHoldViews(g, 1, 0, id, due: true); g = Cold(g, r);
        Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var paid = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == root.Id).PrivateOffer!;
        Require(paid.Stage == PrivateOfferStage.ExchangeChildren && paid.PaymentFrom == CardLocation.Hand(1) && paid.PaymentCardId is { } payment &&
            g.CardMovements.Count(m => m.CardId == payment && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0)) == 1 &&
            !g.CardMovements.Any(m => m.CardId == id && m.From == held.Location && m.To == CardLocation.Hand(1)),
            "The native recipient's matching Hand payment is real, and its gain child completes before the deposited reward is issued.");
        g = Cold(g, r); Continue(g); ReachCondition(g, () => !g.ResolutionStack.Any(f => f.Id == root.Id));
        Require(E<PrivateCardOfferSegmentIssuedEvent>(g).Count(e => e.FrameId == root.Id && e.Stage == PrivateOfferStage.ExchangeChildren) == 1 &&
            E<PrivateCardOfferSegmentIssuedEvent>(g).Count(e => e.FrameId == root.Id && e.Stage == PrivateOfferStage.ObtainChildren) == 1 &&
            g.CardMovements.Count(m => m.CardId == id && m.From == held.Location && m.To == CardLocation.Hand(1)) == 1 &&
            E<ProgramBindingResolvedEvent>(g).Single(e => e.FrameId == root.Id).Completed,
            "The payment and reward children return to the original due binding once, without repaying the deposit or Hand exchange.");
        g = Cold(g, r);
    }

    public static void PrivateRemovalLosesRealHpAndRescueReturnsToOriginalDueBinding()
    {
        var (g, r) = Create(foreign: true); Play(g); Use(g, "lose-one"); Play(g);
        Require(g.State.Players[0].Hp == 1, "A real command leaves the human recipient at exactly one HP."); End(g);
        ReachCondition(g, () => E<PrivateCardOfferDepositedEvent>(g).Length == 1);
        var original = E<PrivateCardOfferDepositedEvent>(g).Single();
        var location = g.CreateCardZoneDiagnostics().Single(z => z.Location.PrivateTurnHold?.HoldId == original.FrameId).Location;
        var id = g.CardMovements.Single(m => m.To == location).CardId;
        CheckPrivateHoldViews(g, 0, 1, id, due: false); g = Cold(g, r);
        Reach(g, p => p.SkillPrompt?.SkillId == Foreign && Has(p, "deferred-private-offer"));
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PrivateOffer is { Stage: PrivateOfferStage.ChoosingExchange });
        CheckPrivateHoldViews(g, 0, 1, id, due: true); g = Cold(g, r);
        Answer(g, P(g)!.Choices.Single(c => c.Parameters.GetValueOrDefault("branch") == "remove"));
        Reach(g, p => p.SkillPrompt?.SkillId == Pulse);
        var dying = g.ResolutionStack.OfType<DyingFrame>().Single();
        Require(dying.ParentFrameId == root.Id && dying.VictimSeat == 0 && dying.Continuation == DyingContinuationKind.ProgramSkill &&
            g.State.Players[0].Hp == 0 && E<ProgramSkillHpLostEvent>(g).Single(e => e.FrameId == root.Id).Amount == 1 &&
            g.CardMovements.Count(m => m.CardId == id && m.From == location && m.To == CardLocation.DiscardPile) == 1,
            "Removal first moves the original entity; one actual HP loss enters the exact recipient's typed program Dying.");
        g = Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        Require(g.State.Players[0].Hp == 1 && g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.Change.TargetSeat == 0),
            "The mandatory test-only SelfDyingResponse performs a real recovery and pauses its HP child, without host state injection.");
        g = Cold(g, r); Continue(g); Play(g);
        Require(E<ProgramBindingResolvedEvent>(g).Single(e => e.FrameId == root.Id).Completed &&
            E<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == root.Id) == 1 &&
            g.CardMovements.Count(m => m.CardId == id && m.From == location && m.To == CardLocation.DiscardPile) == 1 &&
            !g.ResolutionStack.Any(f => f.Id == root.Id), "Actual rescue/HP children return once to the same foreign source due binding.");
        g = Cold(g, r);
    }

    public static void GameTargetHandHpDrawUsesPersistentPerTargetLedgerAndRealGainChildren()
    {
        var (g, r) = Create(); Play(g); var before = g.State.Players[1].HandCount; Use(g, "per-target-hand-hp", [1], Songci);
        Reach(g, p => p.SkillPrompt?.SkillId == Gain); var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.GameTargetHandHp is not null);
        var receipt = root.GameTargetHandHp!;
        Require(receipt.Draw && receipt.TargetSeat == 1 && receipt.ActualCount == 2 && g.State.Players[1].HandCount == before + 2 &&
            g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(m => m.Batch.ParentFrameId == root.Id && m.ResumeProgramFrameId == root.Id && m.Batch.Movements.Count == 1),
            "Draw2 consists of real single-card atomic batches; its first gain child holds the exact paid game-target action.");
        Freeze(receipt.CardIds); var mutable = new[] { 99 }; var frozen = receipt with { CardIds = mutable }; mutable[0] = 100;
        Require(frozen.CardIds[0] == 99, "Receipt init/with clones the input instead of exposing caller arrays.");
        g = Cold(g, r); Continue(g); Play(g);
        Require(!SongciTargets(g).Contains(1) && g.State.Players[1].HandCount < g.State.Players[1].Hp &&
            E<GameTargetHandHpChoiceIssuedEvent>(g).Count(e => e.TargetSeat == 1) == 1,
            "The same target remains below HP but is excluded by the whole-game issued ledger.");
        Use(g, "per-target-hand-hp", [0], Songci); Play(g);
        Require(!SongciTargets(g).Contains(0) && g.State.Players[0].HandCount > g.State.Players[0].Hp,
            "Including self uses the same per-target ledger even when its draw changes the future comparison to the discard branch.");
        var revision = g.Revision; var journal = CommandJson.Serialize(g.AcceptedCommands);
        Require(!g.Submit(new UseProgramSkillCommand(0, Songci, "per-target-hand-hp", [], [1], g.Revision, P(g)!.PromptId)).Accepted &&
            revision == g.Revision && journal == CommandJson.Serialize(g.AcceptedCommands), "A spent target command is rejected without a second issuance or journal mutation.");
        g = Cold(g, r); End(g); ReachCondition(g, () => P(g)?.Kind == DecisionKind.PlayCard && g.State.TurnNumber > 1);
        Require(!SongciTargets(g).Contains(0) && !SongciTargets(g).Contains(1), "A new actual turn/Play keeps both whole-game target keys.");
    }

    public static void GameTargetHandHpEquipmentDiscardDrainsRecoveryAfterSourceLoss()
    {
        foreach (var loseSource in new[] { false, true })
        {
            var (g, r) = Create(equipment: true, loseSource: loseSource); Play(g); Use(g, "equip", [0]); Play(g);
            var armor = g.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
            var hand = g.CreateSnapshot(0).Players[0].Hand[0].Id; var hp = g.State.Players[0].Hp;
            Require(hp == 2 && g.State.Players[0].HandCount > hp, "The real Lord fixture is injured and meets the discard branch.");
            Use(g, "per-target-hand-hp", [0], Songci); Reach(g, p => p.SkillPrompt?.SkillId == Songci && Has(p, "game-hand-hp-discard"));
            Answer(g, P(g)!.Choices.Single(c => c.Cards.SequenceEqual([armor])));
            Require(!g.CardMovements.Any(m => m.CardId == armor && m.To == CardLocation.DiscardPile), "The first private choice freezes material without partially paying the two-card cost.");
            g = Cold(g, r); Answer(g, P(g)!.Choices.Single(c => c.Cards.SequenceEqual([hand]))); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.GameTargetHandHp is not null);
            Require(root.GameTargetHandHp!.CardIds.SequenceEqual([armor, hand]) && root.GameTargetHandHp.ActualCount == 2 && g.State.Players[0].Hp == hp + 1 &&
                g.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1 &&
                g.CardMovements.Count(m => m.CardId == hand && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1 &&
                g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.ResumeFrameId == root.Id && h.Continuation == PostEventContinuation.AwaitedProgramMovement),
                "One real HE payment queues its Silver Lion recovery under the exact owning receipt before program completion.");
            g = Cold(g, r); Continue(g);
            if (loseSource)
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Hp && Has(p, "advanced-lifecycle"));
                Answer(g, P(g)!.Choices.Single(c => c.Parameters.GetValueOrDefault("advanced-value") == Songci));
                g = Cold(g, r); Answer(g, P(g)!.Choices.Single(c => c.Parameters.GetValueOrDefault("advanced-value") == "finish"));
            }
            Play(g);
            Require(E<GameTargetHandHpMovementIssuedEvent>(g).Count(e => e.FrameId == root.Id && !e.Draw && e.Count == 2) == 1 &&
                E<ProgramSkillResolvedEvent>(g).Single(e => e.FrameId == root.Id).Completed && !g.ResolutionStack.Any(f => f.Id == root.Id) &&
                g.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1 &&
                (!loseSource || !g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == Songci)),
                "Paid recovery/movement children drain after actual source removal without paying again or losing the whole-game issuance.");
            g = Cold(g, r);
        }
    }
    private static void CheckPrivateHoldViews(GameEngine g, int target, int source, int id, bool due)
    {
        for (var viewer = 0; viewer < 4; viewer++)
        {
            var s = g.CreateSnapshot(viewer); var pile = s.Players[target].PrivateTurnHolds!.Single(h => h.Count == 1);
            Require(pile.OwnerSeat == target && pile.DeferredSourceSeat == source && (pile.Cards?.Any(c => c.Id == id) == true) == (viewer == source || due && viewer == target),
                "The public count belongs to the logical target; only the source and exact due viewer know its card.");
            if (viewer != source && (!due || viewer != target)) Require(pile.Cards is null && !s.PublicRevealedCards.Any(c => c.Id == id) && !(s.PrivateRevealedCards?.Any(c => c.Id == id) ?? false),
                "A non-authorized prepared view contains neither hidden entity ID nor card identity.");
            Freeze(s.Players[target].PrivateTurnHolds!); if (pile.Cards is not null) Freeze(pile.Cards);
        }
    }
    private static int[] SongciTargets(GameEngine g) => g.GetHumanLegalActions().Where(a => a.ProgramSkillId == Songci).SelectMany(a => a.SelectableTargetSeats).Distinct().ToArray();
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).PendingDecision;
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Has(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static void Answer(GameEngine g, PromptChoice c) => Accept(g, new AnswerPromptCommand(0, P(g)!.PromptId, c.Id, g.Revision));
    private static void Continue(GameEngine g) => Answer(g, P(g)!.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
    private static void Use(GameEngine g, string activation, IReadOnlyList<int>? targets = null, string skill = Driver) => Accept(g, new UseProgramSkillCommand(0, skill, activation, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> condition) => ReachCondition(g, () => P(g) is { } p && condition(p));
    private static void ReachFrame(GameEngine g, Func<ProgramSkillFrame, bool> condition) => ReachCondition(g, () => g.ResolutionStack.OfType<ProgramSkillFrame>().Any(condition));
    private static void ReachCondition(GameEngine g, Func<bool> condition)
    { for (var step = 0; step < 240; step++) { if (condition()) return; Step(g); } throw new InvalidOperationException("Small fixed command prefix missed its exact boundary."); }
    private static void Step(GameEngine g)
    {
        if (P(g) is { } p)
        {
            if (p.Kind == DecisionKind.PlayCard) End(g);
            else if (p.Kind == DecisionKind.DiscardCards) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (Has(p, "skip")) Answer(g, p.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "skip"));
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (p.Kind is DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)
                Answer(g, p.Choices.Single(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("program-action") is null));
            else throw new InvalidOperationException("Unexpected fixed-fixture boundary " + p.Kind + "/" + p.SkillPrompt?.SkillId);
        }
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(g.CreateSnapshot(v))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "Accepted-journal cold replay rebuilds all four views, exact parent frames, private choices, movements and facts."); return restored; }
    private static void Freeze<T>(IReadOnlyList<T> list) { if (list is IList<T> writable && list.Count > 0) { try { writable[0] = writable[0]; } catch (NotSupportedException) { return; } throw new InvalidOperationException("Exposed collection is mutable."); } }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(bool foreign = false, bool equipment = false, bool loseSource = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(foreign, equipment, loseSource));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:cl-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private sealed class Fixture(bool foreign, bool equipment, bool loseSource) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-chen-lin", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse(FixtureRules)!; rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (loseSource) rules["skills"]![3]!["triggers"]![0]!["effects"]!.AsArray().Add(new JsonObject { ["op"] = "replaceSkillsOnAwakening", ["target"] = "owner", ["skillIds"] = new JsonArray(JsonValue.Create("fixture:cl-noop")) });
            var ids = rules["skills"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToArray();
            var descriptions = ids.ToDictionary(id => id, id => id is Gain or Hp or Pulse ? (object)new { name = id, description = "真实子选择", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } : new { name = id, description = "固定真实命令" });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = descriptions }));
            foreach (var id in ids) b.AddSkill(new(id, id, "真实固定能力") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:cl-noop", "失源后的原始替换", "No program"));
            b.AddSkill(new("fixture:cl-pick-owner", "固定主人", "Passive") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 10000d : -10000d) });
            b.AddSkill(new("fixture:cl-pick-peer", "固定他人", "Passive") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role != Role.Lord ? 10000d : -10000d) });
            var skills = new List<string> { Driver, Songci, Gain, Hp, "fixture:cl-skip-discard", "fixture:cl-pick-owner" }; if (!foreign) skills.Add(Bifa); else skills.Add(Pulse);
            b.AddGeneral(new("fixture:cl-owner", "陈琳共享真实命令", "supporter", Driver, "wei", 6, skills.Where(s => s != Driver).ToArray(), GeneralGender.Male) { InitialHp = foreign || equipment ? 1 : null });
            for (var i = 1; i < 4; i++)
            { var peer = new List<string> { "fixture:cl-quiet" }; if (foreign && i == 1) peer.Add(Foreign);
              b.AddGeneral(new($"fixture:cl-peer-{i}", "原参与角色", "supporter", "fixture:cl-pick-peer", "wei", 8, peer, GeneralGender.Female)); }
            var kind = equipment ? "classic:silver-lion" : "standard:slash";
            b.AddDeck(new("fixture:cl-deck", "固定实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 72).Select(_ => new ContentDeckPhysicalCard(kind, Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "陈琳完整私有延后/整局角色额度", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:cl-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:cl-owner", "fixture:cl-peer-1", "fixture:cl-peer-2", "fixture:cl-peer-3"]));
        }
    }
    private const string FixtureRules = """
    {"skills":[
      {"id":"fixture:cl-driver","revision":1,"activations":[
        {"id":"lose-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
        {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]}]},
      {"id":"fixture:cl-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
      {"id":"fixture:cl-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"any","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:songci.ResolveGameTargetHandHpChoice","skill-program.ol:bifa.ResolveDeferredPrivateCardOffer.exchange","skill-program.ol:bifa.ResolveDeferredPrivateCardOffer.obtain"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
      {"id":"fixture:cl-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
      {"id":"fixture:cl-pulse","revision":1,"triggers":[{"id":"pulse","window":"selfDyingResponse","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}]},
      {"id":"fixture:cl-skip-discard","revision":1,"triggers":[{"id":"skip","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]},
      {"id":"fixture:cl-offer","revision":1,"triggers":[
        {"id":"deposit","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"resultBind":"offer"},{"op":"selectTarget","target":"owner","targetKind":"otherLivingMale"},{"op":"depositBoundPrivateCardOffer","target":"owner","sourceBind":"offer","stateId":"resolve"}]},
        {"id":"resolve","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"resolveDeferredPrivateCardOffer","target":"owner"}]}]}
    ]}
    """;
}
