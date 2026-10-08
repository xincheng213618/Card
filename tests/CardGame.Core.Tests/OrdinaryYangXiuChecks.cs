using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryYangXiuChecks
{
    private const string Danlao = "ol:danlao", Jilei = "ol:jilei", Driver = "fixture:yx-driver";
    private const string Gain = "fixture:yx-gain", Entry = "fixture:yx-entry", Pulse = "fixture:yx-pulse", Hp = "fixture:yx-hp";
    private const string AppliedLoss = "fixture:yx-applied-loss";
    private const string SourceSuppression = "fixture:yx-source-suppression";
    private const string Mode = "identity:classic-ordinary-yang-xiu-fixture";
    public static void ActualMultiTargetTrickWaitsForDrawAndUniqueBorrowedHolderDoesNotOffer()
    {
        var (g, r) = Create(); var before = g.CreateSnapshot(0).Players[0].Hand.Count;
        PlayAction(g, a => a.Kind == LegalActionKind.IronChain && a.TargetSeats.Count == 2 && a.TargetSeats.Contains(0));
        var offer = g.ResolutionStack.OfType<ActualUseTargetWindowFrame>().Single();
        var owningUse = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == offer.ParentFrameId);
        var paid = owningUse.Action!.PhysicalCards.Single();
        Require(P(g)?.SkillPrompt?.SkillId == Danlao && offer.ReturnKind == ActualUseTargetReturnKind.OrdinaryTrick &&
            offer.Step == ResolutionFrameStep.AwaitingResponse && offer.TrickReturn?.EffectCardId == paid.CardId &&
            !g.ResolutionStack.OfType<ProgramSkillFrame>().Any() &&
            g.CardMovements.Count(m => m.CardId == paid.CardId && m.From == paid.From && m.To == CardLocation.Processing) == 1 &&
            g.CardMovements.Last(m => m.CardId == paid.CardId).To == CardLocation.Processing,
            "The real multi-target offer pauses before activation with its exact owning use and one paid Processing entity.");
        g = Cold(g, r);
        Require(g.ResolutionStack.OfType<ActualUseTargetWindowFrame>().Single().Id == offer.Id &&
            g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == owningUse.Id).Action!.ActionId == owningUse.Action.ActionId,
            "Cold replay retains the same pre-activation target offer and original paid action without a fabricated program child.");
        Activate(g, Danlao); Reach(g, p => p.SkillPrompt?.SkillId == Gain); Reject(g);
        var root = Root(g); var receipt = root.OwnTrickDraw!;
        Require(receipt.TargetCount == 2 && receipt.ActualDrawCount == 1 && !receipt.Applied &&
            g.CreateSnapshot(0).Players[0].Hand.Count == before &&
            !Facts<OwnTrickTargetNullifiedEvent>(g).Any() && g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == receipt.Use.CardUseFrameId),
            "One real material has been paid and Draw1 is committed, while its gain observer still owns the original target window before nullification.");
        g = Cold(g, r); Continue(g); Play(g);
        Require(Facts<OwnTrickTargetNullifiedEvent>(g).Single().CardUseFrameId == receipt.Use.CardUseFrameId &&
            !g.State.Players[0].IsChained && Facts<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == Danlao && e.Completed),
            "The same use is ineffective only for Yang Xiu after its exact draw child returns; other actual targets continue.");
        var (borrowed, br) = Create(borrowedTargets: true); Use(borrowed, "equip", [Peer(borrowed)]); Play(borrowed);
        PlayAction(borrowed, a => a.Kind == LegalActionKind.BorrowedSword && a.TargetSeats.Count == 2);
        Reach(borrowed, p => p.Kind == DecisionKind.RespondDodge && p.PlayerSeat == 0);
        var dodge = P(borrowed)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "dodge");
        var dodgeCard = dodge.Cards.Single();
        borrowed = Cold(borrowed, br); Reject(borrowed);
        Answer(borrowed, c => c.Id == dodge.Id); Play(borrowed);
        Require(!Facts<OwnTrickDrawIssuedEvent>(borrowed).Any() &&
            Facts<CardUseDeclaredEvent>(borrowed).Any(e => e.CardKind == CardKind.BorrowedSword) &&
            Facts<CardActionAcceptedEvent>(borrowed).Count(e => e.Action.Type == CardActionType.Response &&
                e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Dodge &&
                e.Action.PhysicalCards.Any(c => c.CardId == dodgeCard)) == 1,
            "A true borrowed-sword use contains one weapon holder plus a Slash victim, hence only one actual trick target and no multi-target draw offer; its genuine requested Slash and cold-restored published Dodge resolve once.");
        borrowed = Cold(borrowed, br); g = Cold(g, r);
    }
    public static void DeclaredCategoryBlocksOwnHandMaterialsButForeignDiscardAndEquipmentRemainLegal()
    {
        var (g, r) = Create(ownerHp: 6); Declare(g, "basic"); Play(g);
        var hand = g.CreateSnapshot(0).Players[0].Hand; var basic = hand.First(c => c.Kind == CardKind.Slash);
        Require(g.GetHumanLegalActions().All(a => a.CardId != basic.Id) &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Equip),
            "A declared basic hand category removes physical use and conversion-material offers, while equipment remains available.");
        var state = State(g); var prompt = P(g)!;
        Require(!g.Submit(new UseProgramSkillCommand(0, Driver, "discard", [basic.Id], [], g.Revision, prompt.PromptId)).Accepted && State(g) == state,
            "An unpublished protected self-discard activation is rejected before paying any card.");
        Use(g, "give", [Peer(g)], [basic.Id]); Play(g);
        Require(g.CardMovements.Any(m => m.CardId == basic.Id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(Peer(g))),
            "Giving a protected card is not using, responding with or discarding it.");
        var protectedBefore = g.CreateSnapshot(0).Players[0].Hand.Where(c => c.Kind == CardKind.Slash).Select(c => c.Id).ToArray();
        Use(g, "foreign-discard", [Peer(g)]); Play(g);
        Require(g.CardMovements.Any(m => protectedBefore.Contains(m.CardId) && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "Another actual participant may discard this owner's protected hand entity; the restriction is not a foreign HE protection.");
        var responseCount = Facts<CardRespondedEvent>(g).Count(e => e.ResponderSeat == 0);
        var hpBefore = g.State.Players[0].Hp;
        Use(g, "request", [Peer(g)]); Play(g);
        Require(g.State.Players[0].Hp == hpBefore - 1 && Facts<CardRespondedEvent>(g).Count(e => e.ResponderSeat == 0) == responseCount &&
            Facts<CardUseDeclaredEvent>(g).Any(e => e.SourceSeat == Peer(g) && e.CardKind == CardKind.Slash),
            "A true requested physical Slash hits: Longdan cannot offer a protected basic hand material as a Dodge response.");
        Declare(g, "trick"); Play(g);
        var policies = g.CreateSnapshot(0).Players[0].TurnHandCategoryRestrictions!;
        Require(policies.Select(p => p.Category).Order().SequenceEqual(new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick }.Order()),
            "Separate declarations union during the actual current turn.");
        var caller = policies.ToList(); var clone = g.CreateSnapshot(0).Players[0] with { TurnHandCategoryRestrictions = caller };
        caller.Clear(); var json = JsonSerializer.Deserialize<PlayerSnapshot>(JsonSerializer.Serialize(clone))!;
        Require(clone.TurnHandCategoryRestrictions!.Count == 2 && json.TurnHandCategoryRestrictions is IList<TurnHandCategoryRestriction> list && list.IsReadOnly,
            "Policy collections clone in init/with/JSON and expose no affected player's hidden hand IDs.");
        g = Cold(g, r);
    }
    public static void HandLimitCountsProtectedCardsAndDiscardsOnlyAvailableOverflow()
    {
        foreach (var allBasic in new[] { false, true })
        {
            var (g, r) = Create(allBasic: allBasic); Use(g, "draw"); Play(g); Declare(g, "basic"); Play(g);
            var hand = g.CreateSnapshot(0).Players[0].Hand; var allowed = hand.Where(c => c.Kind != CardKind.Slash).Select(c => c.Id).ToArray();
            var required = Math.Min(Math.Max(hand.Count - g.State.Players[0].Hp, 0), allowed.Length);
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            if (required > 0)
            {
                Reach(g, p => p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0);
                Require(P(g)!.RequiredCardCount == required && P(g)!.ValidCardIds.Order().SequenceEqual(allowed.Order()),
                    "Protected cards still count toward the HP hand limit; required discard is overflow capped only by the actual discardable set.");
                g = Cold(g, r); var p = P(g)!; Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(required).ToArray(), p.PromptId, g.Revision));
            }
            var turn = Facts<TurnStartedEvent>(g).Last(e => e.ActorSeat == 0).TurnNumber;
            Until(g, () => Facts<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber == turn));
            Require(g.CreateSnapshot(0).Players[0].Hand.Count == hand.Count - required &&
                g.CreateSnapshot(0).Players[0].TurnHandCategoryRestrictions is null,
                "Legal overflow is discarded once, all-protected overflow produces no impossible prompt, and the issued restrictions expire at this actual turn's end.");
            g = Cold(g, r);
        }
    }
    public static void DrawGainDamageDyingAndPaidSourceLossReturnToSameUseOnce()
    {
        foreach (var sourceLoss in new[] { false, true })
        {
            var (g, r) = Create(nested: !sourceLoss, sourceLoss: sourceLoss);
            PlayAction(g, a => a.Kind == LegalActionKind.IronChain && a.TargetSeats.Count == 2 && a.TargetSeats.Contains(0));
            Activate(g, Danlao); Reach(g, p => p.SkillPrompt?.SkillId == Gain); var root = Root(g); var use = root.OwnTrickDraw!.Use.CardUseFrameId;
            g = Cold(g, r); Continue(g);
            if (!sourceLoss)
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Entry);
                var damageWindow = g.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single(w =>
                    w.TriggerWindow == SkillProgramTriggerWindow.AfterDamageApplied);
                var nativeDamage = g.ResolutionStack.OfType<DamageFrame>().Single(d => d.Id == damageWindow.ParentFrameId);
                var gain = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Gain);
                var loss = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == AppliedLoss);
                var dying = g.ResolutionStack.OfType<DyingFrame>().Single(d => d.VictimSeat == 0);
                var entryWindow = g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(w =>
                    w.Window == SkillProgramTriggerWindow.DyingEntering && w.ResumeDyingFrameId == dying.Id);
                Require(g.State.Players[0].Hp == 0 && g.ResolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == 0) &&
                    Root(g).Id == root.Id && !Facts<OwnTrickTargetNullifiedEvent>(g).Any(),
                    "A real gain observer causes native damage and DyingEntering while the original multi-target trick and issued draw remain owned.");
                Require(nativeDamage.ParentFrameId == gain.Id && nativeDamage.Amount == 1 &&
                    damageWindow.SourceSeat == 0 && damageWindow.TargetSeat == 0 &&
                    loss.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied } context &&
                    context.ParentFrameId == damageWindow.Id && context.DamageFrameId == nativeDamage.Id &&
                    context.SourceSeat == 0 && context.TargetSeat == 0 && context.Amount == 1 &&
                    damageWindow.CandidateIndex >= 0 && damageWindow.CandidateIndex < damageWindow.Candidates.Count &&
                    damageWindow.Candidates[damageWindow.CandidateIndex].ProgramId == AppliedLoss &&
                    dying.ResumesProgramSkill && dying.ParentFrameId == loss.Id &&
                    g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Entry &&
                        f.WindowContext?.ParentFrameId == entryWindow.Id) &&
                    Facts<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == loss.Id && e.TargetSeat == 0 &&
                        e.Amount == 2 && e.RemainingHp == 0) == 1,
                    "Actual Damage1 retains its precise AfterDamage window while the current mandatory LoseHp2 observer owns ProgramSkill Dying and the separate DyingEntering prompt.");
                g = Cold(g, r); Continue(g); Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Pulse));
                Answer(g, c => c.Parameters.GetValueOrDefault("skill-id") == Pulse); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
                Require(Facts<CardUseDeclaredEvent>(g).Any(e => e.SourceSeat == 0 && e.CardKind == CardKind.Alcohol && e.CardId == 0),
                    "The actual self-dying response issues a zero-entity Alcohol use, whose recovery observer pauses before returning.");
                Require(g.ResolutionStack.OfType<DamageTriggerWindowFrame>().Any(w => w.Id == damageWindow.Id) &&
                    g.ResolutionStack.OfType<DyingFrame>().Any(d => d.Id == dying.Id && d.ParentFrameId == loss.Id) &&
                    Root(g).Id == root.Id && !Facts<OwnTrickTargetNullifiedEvent>(g).Any(),
                    "The cold-restored actual damage cursor and paid-loss Dying remain suspended through zero-entity Alcohol and its real recovery child.");
                g = Cold(g, r); Continue(g);
            }
            Play(g);
            if (sourceLoss)
                Require(!g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == Danlao) &&
                    g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == SourceSuppression) &&
                    Facts<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Gain && e.SkillIds.Contains(SourceSuppression)) == 1,
                    "The actual issued-draw gain child acquires one independent suppression source and makes Danlao unqualified without physically removing its grant.");
            Require(Facts<OwnTrickDrawIssuedEvent>(g).Count() == 1 && Facts<OwnTrickTargetNullifiedEvent>(g).Single().CardUseFrameId == use &&
                Facts<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == Danlao && e.Completed) &&
                g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "program.own-multi-target-trick.draw") == 1,
                "Issued draw, all native damage/rescue children and exact original target-nullification finish once, even when a child suppresses the source skill's qualification.");
            g = Cold(g, r);
        }
        var (native, nr) = Create(nativeDeclaration: true, ownerHp: 6); Use(native, "damage-peer", [Peer(native)]); Play(native);
        Require(Facts<HandCategoryRestrictionGrantedEvent>(native).Any(e => e.Restriction.AffectedSeat == 0 && e.Restriction.Source.OwnerSeat == Peer(native)),
            "Native AI declares a public category against the actual damage source using the same owning AfterDamage window.");
        native = Cold(native, nr); StrictContracts();
    }
    private static void StrictContracts()
    {
        foreach (var changes in new[] { "window", "subject", "amount" })
        {
            var n = System.Text.Json.Nodes.JsonNode.Parse("""
            {"schemaVersion":$SCHEMA$,"skills":[{"id":"fixture:yx-strict","revision":1,"triggers":[{"id":"draw","window":"otherActualUseTargeted","subject":"owner","optional":true,"effects":[{"op":"drawThenNullifyOwnMultiTargetTrick","target":"owner"}]}]}]}
            """.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)))!;
            var t = n["skills"]![0]!["triggers"]![0]!;
            if (changes == "window") t["window"] = "afterDamageApplied";
            if (changes == "subject") t["subject"] = "damageTarget";
            if (changes == "amount") t["effects"]![0]!["amount"] = 2;
            var rejected = false; try { SkillProgramCatalog.Load(n.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { ["fixture:yx-strict"] = new { name = "严格拥有帧", description = "拒收" } } })); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The new operation rejects unsupported windows, subjects and extra amount fields without widening old nodes.");
        }
    }
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().First(f => f.OwnTrickDraw is not null);
    private static PendingDecision? P(GameEngine g) => g.State.PendingDecision;
    private static IEnumerable<T> Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static int Peer(GameEngine g) => g.State.Players.First(p => p.Seat != 0 && p.IsAlive).Seat;
    private static void Use(GameEngine g, string binding, IReadOnlyList<int>? targets = null, IReadOnlyList<int>? cards = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, binding, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void PlayAction(GameEngine g, Func<LegalAction, bool> predicate)
    { var a = g.GetHumanLegalActions().First(predicate); var id = a.CardId ?? throw new InvalidOperationException("Expected actual physical use.");
      Accept(g, new PlayCardCommand(0, id, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources }); }
    private static void Declare(GameEngine g, string category)
    { Use(g, "self"); Activate(g, Jilei); Reach(g, p => p.SkillPrompt?.SkillId == Jilei && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == category));
      Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == category); }
    private static void Activate(GameEngine g, string skill)
    { Reach(g, p => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
      Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var i = 0; i < 180; i++) { if (P(g) is { } p && predicate(p)) return; Step(g); } throw new InvalidOperationException("Yang Xiu actual boundary missing: " + JsonSerializer.Serialize(P(g))); }
    private static void Until(GameEngine g, Func<bool> predicate)
    { for (var i = 0; i < 180; i++) { if (predicate()) return; Step(g); } throw new InvalidOperationException("Yang Xiu exact event boundary missing."); }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is not null && p.PlayerSeat != 0) Accept(g, new AdvanceOneStepCommand(g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge })
            Answer(g, c => c.Parameters.GetValueOrDefault("response") ==
                (p.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "dodge") ? "dodge" : "take-damage"));
        else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reject(GameEngine g)
    { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before,
        "Rejected unpublished input changes no prepared view, owning frame, actual movement or accepted command."); }
    private static void Accept(GameEngine g, GameCommand c)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected true command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored),
        "Four prepared private views, all owning frames/receipts, actual facts/movements and accepted command journal restore identically."); return restored; }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(bool allBasic = false, int ownerHp = 3, bool nested = false, bool sourceLoss = false, bool nativeDeclaration = false, bool borrowedTargets = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new Fixture(allBasic, ownerHp, nested, sourceLoss, nativeDeclaration, borrowedTargets));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Renegade, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:yx-owner", g.Revision, P(g)!.PromptId)); Play(g);
        if (sourceLoss)
        {
            Require(!g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == Danlao),
                "The source-loss fixture initially has no printed Danlao grant.");
            Use(g, "grant-danlao"); Play(g);
            var owner = g.CreateSnapshot(0).Players[0];
            Require(owner.Skills!.Any(s => s.Id == Danlao) && owner.SkillRuntimeStates!.Single(s => s.SkillId == Danlao).IsAcquired &&
                Facts<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.Contains(Danlao)) == 1,
                "One real activation independently grants Danlao before its issued draw and subsequent qualification suppression.");
        }
        return (g, r);
    }
    private sealed class Fixture(bool allBasic, int ownerHp, bool nested, bool sourceLoss, bool nativeDeclaration, bool borrowedTargets) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-yang-xiu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = FixtureRules.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            rules = rules.Replace("$GAIN_TAIL$", sourceLoss ? "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"fixture:yx-source-suppression\"]}" : nested ? "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}" : "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}");
            if (sourceLoss)
            {
                var sourceRules = JsonNode.Parse(rules)!;
                ((JsonArray)sourceRules["skills"]![0]!["activations"]!).Add(JsonNode.Parse("""{"id":"grant-danlao","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:danlao"]}]}"""));
                rules = sourceRules.ToJsonString();
            }
            var names = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, "fixture:yx-quiet", Gain, Entry, Pulse, Hp, AppliedLoss }) names[id] = id is Gain or Entry or Hp
                ? new { name = id, description = "真正拥有帧子窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                : (object)new { name = id, description = "固定真实命令能力" };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真正拥有帧小夹具") { Program = program, ProgramPresentation = catalog.Presentations[id],
                Tags = id is "fixture:yx-quiet" or Entry or AppliedLoss ? SkillTag.Locked : SkillTag.None });
            b.AddSkill(new(SourceSuppression, "已付摸牌来源资格抑制", "真实固定体力使原摸牌技能来源失去资格") { SuppressionRule = new(ownerHp), Tags = SkillTag.Locked });
            b.AddSkill(new("fixture:yx-selection", "固定其他角色", "公开选将偏好") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100000d) });
            var owner = new List<string> { Jilei, Gain, "classic:longdan" }; if (!sourceLoss) owner.Insert(0, Danlao); if (nested) owner.AddRange([Entry, Pulse, Hp, AppliedLoss]);
            b.AddGeneral(new("fixture:yx-owner", "实际杨修规则", "supporter", Driver, "wei", 8, owner) { InitialHp = ownerHp });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:yx-peer-{i}", "固定真正参与者", "supporter", "fixture:yx-selection", "wei", 8,
                nativeDeclaration ? ["fixture:yx-quiet", Jilei] : borrowedTargets ? ["fixture:yx-quiet", Danlao] : ["fixture:yx-quiet"]) { InitialHp = 6 });
            var cards = new[] { "standard:slash", "standard:iron_chain", "classic:borrowed-sword", "standard:crossbow" };
            b.AddDeck(new("fixture:yx-deck", "固定实体牌堆", 12, 2, []) { PhysicalCards = Enumerable.Range(0, 128).Select(i =>
                new ContentDeckPhysicalCard(allBasic ? "standard:slash" : cards[i % cards.Length], Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "杨修真实命令共享边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:yx-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:yx-owner", "fixture:yx-peer-1", "fixture:yx-peer-2", "fixture:yx-peer-3"]));
        }
    }
    private const string FixtureRules = """
    {"schemaVersion":$SCHEMA$,"skills":[
      {"id":"fixture:yx-driver","revision":1,"activations":[
        {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
        {"id":"damage-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
        {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]},
        {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"draw","target":"owner","amount":12}]},
        {"id":"discard","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","sourceZones":["hand"],"usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
        {"id":"give","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","sourceZones":["hand"],"usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]},
        {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
        {"id":"foreign-discard","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"selectedTarget"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"cardKinds":["slash"],"destination":"discardPile","resultBind":"removed","count":1,"awaitMovementTriggers":true}]}]},
      {"id":"fixture:yx-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},{"id":"quiet-discard","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]},
      {"id":"fixture:yx-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.own-multi-target-trick.draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},$GAIN_TAIL$]}]},
      {"id":"fixture:yx-entry","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
      {"id":"fixture:yx-applied-loss","revision":1,"triggers":[{"id":"loss","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"priority":100,"usageScope":"game","usageLimit":1,"effects":[{"op":"loseHp","target":"owner","amount":2}]}]},
      {"id":"fixture:yx-pulse","revision":1,"triggers":[{"id":"wine","window":"selfDyingResponse","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"useVirtualDyingAlcohol","target":"owner"},{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}]}]},
      {"id":"fixture:yx-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
    ]}
    """;
}
