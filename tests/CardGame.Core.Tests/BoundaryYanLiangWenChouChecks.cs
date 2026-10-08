using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryYanLiangWenChouChecks
{
    private const string Skill = "boundary:shuangxiong-current", Driver = "fixture:ylwc-driver",
        Moved = "fixture:ylwc-moved", Gain = "fixture:ylwc-gain", Entry = "fixture:ylwc-entry", Face = "fixture:ylwc-face";
    private const string Mode = "identity:classic-ylwc-fixture";
    private const string Cost = "program.paid-color-conversion.discard", Claim = "program.actual-turn-damage-entity.claim";

    public static void RealColorPaymentDuelBackDamageAndMandatoryEndingClaim()
    {
        var (g, r) = Create(); DrawOffer(g); Reject(g); g = Restore(g, r); Activate(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Cards.Count == 1));
        var own = g.CreateSnapshot(0).Players[0].Hand;
        var black = own.First(c => c.Suit is Suit.Club or Suit.Spade);
        Require(own.Any(c => c.Suit is Suit.Heart or Suit.Diamond), "The fixed real deck supplies both colors without seed searches.");
        var before = g.CardMovements.Count; Answer(g, c => c.Cards.SequenceEqual([black.Id])); Play(g);
        var paid = E<ProgramPaidColorConversionPaidEvent>(g).Single();
        var grant = E<CardConversionGrantedEvent>(g).Single(e => e.Conversion.PaidColorOrigin is not null).Conversion;
        Require(paid.Origin.CardId == black.Id && paid.Origin.EffectiveSuit == black.Suit && grant.PaidColorOrigin == paid.Origin &&
            grant.TurnNumber == 1 && grant.Source.SkillId == Skill && g.CardMovements.Count(m => m.CardId == black.Id && m.Reason.Value == Cost) == 1 &&
            g.CardMovements.Count == before + 1, "One actual HE discard freezes color and issues one exact actual-turn conversion.");
        Require(g.GetLegalActions().Where(a => a.ConversionSource?.SkillId == Skill).All(a =>
            g.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == a.CardId).Suit is Suit.Heart or Suit.Diamond),
            "Only the opposite actual color receives the new Duel source.");
        var duel = g.GetLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.TargetSeat == 1 && a.ConversionSource?.SkillId == Skill);
        PlayAction(g, duel); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash);
        var responses = E<CardActionAcceptedEvent>(g).Where(e => e.Action.ActorSeat == 1 && e.Action.Type == CardActionType.Response &&
            e.Action.EffectiveKind == CardKind.Slash).SelectMany(e => e.Action.PhysicalCards).Select(c => c.CardId).ToArray();
        Require(responses.Length > 0, "The native opponent answered this converted Duel with a physical Slash.");
        g = Restore(g, r); Pass(g); Play(g);
        var origin = E<ActualTurnCardDamageEntityEvent>(g).Single(e => e.VictimSeat == 0 && e.EffectiveKind == CardKind.Duel);
        Require(origin.CardActorSeat == 0 && origin.ProviderSeat == 0 && origin.DamageSourceSeat == 1 &&
            origin.CardId == duel.CardId && origin.MaterialCount == 1 && responses.All(id => id != origin.CardId),
            "Duel back-damage freezes its original Duel cost, never the Slash responses.");
        End(g); Reach(g, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Gain);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
        Require(root.TriggerId == "ending-causal-damage-entities" && root.ActualTurnDamageClaim!.CardIds.SequenceEqual([duel.CardId!.Value]) &&
            g.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == duel.CardId), "The independent mandatory Ending claim pauses only after actual gain.");
        AssertClaimBatch(g, root);
        var frozen = root.ActualTurnDamageClaim!.CardIds as IList<int>;
        var blocked = false; try { frozen![0] = -1; } catch (NotSupportedException) { blocked = true; }
        Require(blocked, "The frame collection is copied and immutable before public ResolutionStack exposure.");
        Reject(g); g = Restore(g, r); Continue(g); Until(g, () => Ended(g, 0));
        Require(E<ActualTurnDamageEntityClaimedEvent>(g).Count(e => e.CardId == duel.CardId) == 1 &&
            g.CardMovements.Count(m => m.CardId == duel.CardId && m.Reason.Value == Claim) == 1,
            "A restored child never repays the color cost, repeats the entity claim or double-cleans the original use.");
        g = Restore(g, r);
    }

    public static void MultipleOriginalMaterialsAlreadyMovedAndEmptyVirtualResponses()
    {
        var (g, r) = Create(); DrawOffer(g); Skip(g); Play(g);
        var originals = g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).Order().ToArray(); AllHandDuel(g, originals); Play(g);
        var damage = E<ActualTurnCardDamageEntityEvent>(g).Where(e => e.VictimSeat == 0 && e.EffectiveKind == CardKind.Duel).ToArray();
        Require(originals.Length >= 2 && damage.Length == originals.Length && damage.Select(e => e.CardId).SequenceEqual(originals) &&
            damage.All(e => e.MaterialCount == originals.Length) && !E<ProgramPaidColorConversionPaidEvent>(g).Any(),
            "Every original material has its own cause; mandatory Ending needs neither the optional payment nor its color source.");
        End(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        AssertClaimBatch(g, g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill));
        g = Restore(g, r); Continue(g); Until(g, () => Ended(g, 0));
        Require(originals.All(id => E<ActualTurnDamageEntityClaimedEvent>(g).Count(e => e.CardId == id) == 1), "All original Duel materials are obtained once.");

        var (taken, tr) = Create(preclaim: true); DrawOffer(taken); Skip(taken); Play(taken);
        var costs = taken.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray(); AllHandDuel(taken, costs);
        Reach(taken, p => p.SkillPrompt?.SkillId == "classic:jianxiong" &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Activate(taken); Play(taken); taken = Restore(taken, tr); End(taken); Until(taken, () => Ended(taken, 0));
        Require(costs.All(id => taken.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == id)) && !E<ActualTurnDamageEntityClaimedEvent>(taken).Any(),
            "A mature Jianxiong preclaim removes original costs from Processing; the new Ending neither grabs them from Hand nor repeats cleanup.");

        var (zero, zr) = Create(); DrawOffer(zero); Skip(zero); Play(zero); Use(zero, "enemy-duel", [1]);
        Reach(zero, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash);
        var response = P(zero)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "slash");
        Answer(zero, c => c.Id == response.Id); Reach(zero, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash);
        zero = Restore(zero, zr); Pass(zero); Play(zero);
        Require(E<ActualTurnCardDamageEmptyEvent>(zero).Any(e => e.VictimSeat == 0 && e.EffectiveKind == CardKind.Duel) &&
            !E<ActualTurnCardDamageEntityEvent>(zero).Any(e => response.Cards.Contains(e.CardId)),
            "The actual zero-entity Duel causes damage, while its real Slash responses create no causal materials.");
        End(zero); Until(zero, () => Ended(zero, 0)); Require(!E<ActualTurnDamageEntityClaimedEvent>(zero).Any(), "Zero-entity damage invents no claim.");
    }

    public static void LightningHeldEntitySourceLossAndGainDyingColdReturn()
    {
        var (g, r) = Create(lightning: true); DrawOffer(g); Skip(g); Play(g);
        var action = g.GetLegalActions().First(a => a.Kind == LegalActionKind.Lightning); PlayAction(g, action); Play(g); End(g);
        Reach(g, p => p.PlayerSeat == 0 && ActivateChoice(p));
        var hit = E<LightningResolvedEvent>(g).Single(e => e.CardId == action.CardId && e.Hit);
        var cause = E<ActualTurnCardDamageEntityEvent>(g).Single(e => e.CardId == action.CardId && e.VictimSeat == 0 && e.DelayedJudgment);
        Require(cause.EffectiveKind == CardKind.Lightning && cause.ActualTurnNumber == g.State.TurnNumber &&
            cause.CausalFrameId == hit.ResolutionId && hit.JudgmentCardId != cause.CardId,
            "A Lightning hit freezes its held delayed entity and actual source-less flag, excluding the separately revealed judgment card.");
        g = Restore(g, r); Skip(g); Play(g); End(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        Require(E<ActualTurnDamageEntityClaimedEvent>(g).Any(e => e.CardId == action.CardId) &&
            !E<ActualTurnDamageEntityClaimedEvent>(g).Any(e => e.CardId == hit.JudgmentCardId), "Ending obtains the finished Lightning alone.");
        g = Restore(g, r); Continue(g);

        var (lost, lr) = Create(sourceLoss: true); DrawOffer(lost); Activate(lost);
        Reach(lost, p => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Cards.Count == 1));
        var card = P(lost)!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); Answer(lost, c => c.Cards.SequenceEqual([card]));
        Reach(lost, p => p.SkillPrompt?.SkillId == Moved); Reject(lost); lost = Restore(lost, lr);
        var paidRoot = lost.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PaidColorConversion is not null).Id;
        Continue(lost); Reach(lost, p => p.SkillPrompt?.SkillId == Moved &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("choice") == Skill));
        lost = Restore(lost, lr); Answer(lost, c => c.Parameters.GetValueOrDefault("choice") == Skill);
        Until(lost, () => E<ProgramBindingResolvedEvent>(lost).Any(e => e.FrameId == paidRoot));
        Require(!lost.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == Skill) && lost.CardMovements.Count(m => m.CardId == card && m.Reason.Value == Cost) == 1 &&
            !E<CardConversionGrantedEvent>(lost).Any(e => e.Conversion.PaidColorOrigin is not null) &&
            E<ProgramSkillSuppressedEvent>(lost).Single(e => e.SkillId == Skill && e.Suppressed) is { OwnerSeat: 0, TargetSeat: 0 } &&
            !E<ProgramBindingResolvedEvent>(lost).Single(e => e.FrameId == paidRoot).Completed,
            "Actual selected source suppression in the restored discard child cancels the unissued conversion and preserves its paid entity and exact return.");

        var (dying, dr) = Create(gainDying: true); DrawOffer(dying); Skip(dying); Play(dying);
        var materials = dying.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray(); AllHandDuel(dying, materials); Play(dying); End(dying);
        Reach(dying, p => p.SkillPrompt?.SkillId == Entry); dying = Restore(dying, dr); Continue(dying);
        Until(dying, () => dying.State.Status == EngineStatus.Completed);
        Require(materials.All(id => dying.CardMovements.Count(m => m.CardId == id && m.Reason.Value == Claim) == 1),
            "A real gain→HP-loss→Dying-entry→rescue-or-death chain continues from a restored engine without repeating claims.");

        var (damaged, dm) = Create(gainDying: true, gainDamage: true); DrawOffer(damaged); Skip(damaged); Play(damaged);
        var originalDamageMaterials = damaged.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray();
        AllHandDuel(damaged, originalDamageMaterials); Play(damaged); End(damaged);
        Reach(damaged, p => p.SkillPrompt?.SkillId == Entry); damaged = Restore(damaged, dm); Continue(damaged);
        Reach(damaged, p => p.SkillPrompt?.SkillId == "classic:jiushi" &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "flip"));
        damaged = Restore(damaged, dm); Answer(damaged, c => c.Parameters.GetValueOrDefault("option-id") == "flip");
        Reach(damaged, p => p.SkillPrompt?.SkillId == Face);
        Require(damaged.ResolutionStack.OfType<DyingFrame>().Any(f => f.Continuation == DyingContinuationKind.Damage && f.VictimSeat == 0) &&
            damaged.State.Players[0].IsFaceDown, "A real damage SelfDyingResponse owns the classic Jiushi face observer after the paid claim.");
        damaged = Restore(damaged, dm); Continue(damaged);
        Until(damaged, () => damaged.State.Status == EngineStatus.Completed);
        Require(originalDamageMaterials.All(id => damaged.CardMovements.Count(m => m.CardId == id && m.Reason.Value == Claim) == 1) &&
            E<ActualTurnCardDamageEntityEvent>(damaged).All(e => e.EffectiveKind == CardKind.Duel),
            "The same paid claim admits a real nested Program damage/Dying subtree and pure skill damage invents no extra materials.");
    }

    public static void RealEquipmentConversionNativeAndStrictOperationContracts()
    {
        var (g, r) = Create(); DrawOffer(g); Activate(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Cards.Count == 1));
        var payment = g.CreateSnapshot(0).Players[0].Hand.First(c => c.Suit is Suit.Heart or Suit.Diamond);
        Answer(g, c => c.Cards.SequenceEqual([payment.Id])); Play(g); Use(g, "gear", [0]); Play(g);
        var gear = g.CreateSnapshot(0).Players[0].Equipment.Single();
        var converted = g.GetLegalActions().Single(a => a.CardId == gear.Id && a.Kind == LegalActionKind.Duel && a.TargetSeat == 1 && a.ConversionSource?.SkillId == Skill);
        PlayAction(g, converted); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash);
        g = Restore(g, r); Pass(g); Play(g);
        var used = E<CardActionAcceptedEvent>(g).First(e => e.Action.Type == CardActionType.Use && e.Action.EffectiveKind == CardKind.Duel &&
            e.Action.PhysicalCards.Any(c => c.CardId == gear.Id)).Action;
        Require(used.PhysicalCards.Single().From == CardLocation.Equipment(0) &&
            g.CardMovements.Count(m => m.CardId == gear.Id && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1,
            "The opposite-color equipment pays one genuine Duel material through mature native removal.");
        End(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain); g = Restore(g, r); Continue(g);
        Require(E<ActualTurnDamageEntityClaimedEvent>(g).Any(e => e.CardId == gear.Id), "The real equipment Duel also reaches independent Ending.");

        var (native, _) = Create(native: true);
        for (var i = 0; i < 120 && native.State.Status != EngineStatus.Completed && !E<ProgramPaidColorConversionPaidEvent>(native).Any(); i++)
            Accept(native, new AdvanceOneStepCommand(native.Revision));
        var paid = E<ProgramPaidColorConversionPaidEvent>(native).FirstOrDefault();
        Require(paid is not null && native.CardMovements.Count(m => m.CardId == paid.Origin.CardId && m.Reason.Value == Cost) == 1 &&
            E<CardConversionGrantedEvent>(native).Any(e => e.Conversion.PaidColorOrigin == paid.Origin),
            "The native owner truly pays the generic program and receives a legal conversion without manually answering bot prompts.");
        foreach (var trigger in new[] {
            """{"id":"wrong-ending","window":"turnEnding","subject":"owner","optional":true,"effects":[{"op":"claimActualTurnDamageEntities","target":"owner"}]}""",
            """{"id":"wrong-payment","window":"drawPhaseEnded","subject":"owner","optional":true,"effects":[{"op":"selectOwnedCards","target":"owner","amount":2,"zones":["hand","equipment"],"resultBind":"paid"},{"op":"discardBoundCardForOppositeTurnDuel","target":"owner","sourceBind":"paid"}]}""" })
        {
            var failed = false;
            try { SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:ylwc-invalid\",\"revision\":1,\"triggers\":[" + trigger + "]}]}",
                """{"schemaVersion":3,"skills":{"fixture:ylwc-invalid":{"name":"严格合同","description":"无旁路"}}}"""); }
            catch (InvalidOperationException) { failed = true; }
            Require(failed, "The new operation rejects optional mandatory claims and non-single costs without weakening classic contracts.");
        }
    }

    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Ended(GameEngine g, int seat) => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == seat);
    private static bool ActivateChoice(PendingDecision p) => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void AssertClaimBatch(GameEngine g, ProgramSkillFrame root)
    {
        var claim = root.ActualTurnDamageClaim!;
        var window = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(frame => frame.Batch.ParentFrameId == root.Id);
        var moves = window.Batch.Movements;
        Require(window.Batch.AwaitingProgramFrameId == root.Id && window.ResumeProgramFrameId is null &&
            moves.Select(move => move.CardId).SequenceEqual(claim.CardIds) &&
            moves.All(move => move.From == CardLocation.DiscardPile && move.To == CardLocation.Hand(root.OwnerSeat) && move.Reason.Value == Claim) &&
            window.Batch.SourceCounts.Single(count => count.Location == CardLocation.DiscardPile) is { } source &&
            source.CountBefore - source.CountAfter == claim.CardIds.Count &&
            window.Batch.DestinationCounts!.Single(count => count.Location == CardLocation.Hand(root.OwnerSeat)) is { } target &&
            target.CountAfter - target.CountBefore == claim.CardIds.Count &&
            moves.All(move => E<ActualTurnDamageEntityClaimedEvent>(g).Count(fact => fact.ProgramFrameId == root.Id &&
                fact.CardId == move.CardId && fact.MovementSequence == move.Sequence) == 1),
            "All frozen damage entities share one real awaited movement batch, exact before/after counts and one causal claim fact per ledger entry.");
    }

    private static void DrawOffer(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && ActivateChoice(p));
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Skip(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Pass(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Use(GameEngine g, string activation, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets, g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void PlayAction(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value,
        a.TargetSeats.Count > 0 ? a.TargetSeats : a.TargetSeat is { } target ? [target] : [], g.Revision, P(g)!.PromptId, a.PlayedCardKind)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void AllHandDuel(GameEngine g, IReadOnlyList<int> ids)
    { Accept(g, new UseProgramSkillCommand(0, Driver, "all-hand-duel", ids, [], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Targets.SequenceEqual([1]))); Answer(g, c => c.Targets.SequenceEqual([1])); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var i = 0; i < 160; i++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); } throw new InvalidOperationException("Fixed Yan Liang Wen Chou boundary not reached: " + JsonSerializer.Serialize(P(g))); }
    private static void Until(GameEngine g, Func<bool> done)
    { for (var i = 0; i < 160; i++) { if (done()) return; Advance(g); } throw new InvalidOperationException("Fixed Yan Liang Wen Chou continuation not completed."); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && ActivateChoice(p)) Skip(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "pass");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass")) Pass(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Real command rejected."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Restore(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "Four private views, scalar original damage causes, paid entities and exact children survive real JSON restore."); return restored; }
    private static void Reject(GameEngine g)
    { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && before == State(g), "Unpublished input preserves private choices, cost, claims and command history."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool sourceLoss = false, bool lightning = false, bool preclaim = false, bool native = false, bool gainDying = false, bool gainDamage = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(sourceLoss, lightning, preclaim, native, gainDying, gainDamage));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode,
            HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand());
        if (!native) { Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral); Accept(g, new SelectGeneralCommand(0, "fixture:ylwc-owner", g.Revision, P(g)!.PromptId)); }
        return (g, registry);
    }
    private sealed class Fixture(bool sourceLoss, bool lightning, bool preclaim, bool native, bool gainDying, bool gainDamage) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-yan-liang-wen-chou", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var terminal = sourceLoss ? ",{\"op\":\"suppressGeneralSkill\",\"target\":\"owner\"}" : "";
            var gain = gainDying ? gainDamage ? """{"op":"damage","target":"owner","amount":7}""" :
                """{"op":"loseHp","target":"owner","amount":7}""" : """{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}""";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}],"activations":[
              {"id":"gear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
              {"id":"enemy-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
              {"id":"all-hand-duel","minCards":1,"maxCards":64,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useAllHandCardsAsOrdinaryTrick","target":"owner","viewAsId":"all-hand-duel","outputKind":"duel"}]}]},
            {"id":"{{Moved}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementReasons":["{{Cost}}"],"movementOccurrence":"perOwnerBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}{{terminal}}]}]},
            {"id":"{{Gain}}","revision":1,"triggers":[{"id":"claim","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["{{Claim}}"],"movementOccurrence":"perBatch","optional":false,"effects":[{{gain}}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Face}}","revision":1,"triggers":[{"id":"face","window":"characterTurnedOver","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实成本驱动", description = "成熟实际装备与有实体或零实体决斗" },
                [Moved] = new { name = "实际付款观察", description = "同一原弃牌批次", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Gain] = gainDying ? new { name = "真实gain损失", description = "实际收益致濒死" } :
                    (object)new { name = "领取实体子链", description = "真实收益移牌", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Entry] = new { name = "实际濒死入口", description = "真实来源回返", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Face] = new { name = "实际九诗翻面", description = "伤害濒死子链", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = labels }));
            foreach (var id in c.Programs.Keys) b.AddSkill(new(id, id, "真实通用能力") { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            foreach (var owner in new[] { true, false }) b.AddSkill(new(owner ? "fixture:ylwc-owner-weight" : "fixture:ylwc-other-weight", "固定角色选将", "正式公开角色权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            var extra = new List<string> { "fixture:ylwc-owner-weight" };
            if (!native) { extra.Add(Driver); extra.Add(Gain); } if (sourceLoss) extra.Add(Moved); if (preclaim) extra.Add("classic:jianxiong"); if (gainDying) extra.Add(Entry);
            if (gainDamage) { extra.Add("classic:jiushi"); extra.Add(Face); }
            b.AddGeneral(new("fixture:ylwc-owner", "当前双雄来源", "supporter", Skill, "qun", 6, extra));
            // Two points of existing injury make the native Duel policy use
            // its real Slash, exposing the original user's back-damage turn.
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:ylwc-other-{i}", "真实对手", "supporter", "fixture:ylwc-other-weight", "wei", 8, []) { InitialHp = 6 });
            b.AddDeck(new("fixture:ylwc-deck", "固定真实双色实体", 4, 0, []) {
                PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(lightning ? "standard:lightning" :
                    i >= 80 ? "standard:crossbow" : "standard:slash", lightning ? Suit.Spade : i >= 80 || i % 2 == 0 ? Suit.Club : Suit.Heart, lightning ? 5 : 7)).ToArray() });
            b.AddMode(new(Mode, "实际当前双雄", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:ylwc-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ylwc-owner", "fixture:ylwc-other-1", "fixture:ylwc-other-2", "fixture:ylwc-other-3"]));
        }
    }
}
