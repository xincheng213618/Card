using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryCaiWenJiChecks
{
    private const string Beige = "boundary:beige";
    private const string Driver = "fixture:cwj-driver";
    private const string Final = "fixture:cwj-final";
    private const string Movement = "fixture:cwj-movement";
    private const string Gain = "fixture:cwj-gain";
    private const string Hp = "fixture:cwj-hp";
    private const string Flip = "fixture:cwj-flip";
    private const string Loss = "fixture:cwj-loss";
    private const string Entry = "fixture:cwj-entry";
    private const string GainDying = "fixture:cwj-gain-dying";
    private const string Reason = "skill.boundary.beige";
    private const string Payment = "skill-program.damage-judgment-suit.payment";
    private const string Mode = "identity:classic-boundary-cai-wen-ji-fixture";

    public static void DamageJudgmentBeforePaymentFourSuitsAndExactClaims()
    {
        foreach (var suit in new[] { Suit.Heart, Suit.Diamond, Suit.Club, Suit.Spade })
        {
            var (g, r) = Create(suit); var hp = g.State.Players[1].Hp;
            var targetHand = g.CreateSnapshot(1).Players[1].Hand.Count;
            var slash = Slash(g); Reach(g, p => Activation(p)); Cold(g, r); Reject(g);
            Require(!E<JudgmentRequestedEvent>(g).Any(j => j.Reason == Reason) && Paid(g).Length == 0,
                "The optional actual Slash damage candidate does not judge or discard before activation.");
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, p => p.SkillPrompt?.SkillId == Final); var root = Root(g); var judgment = g.ResolutionStack.OfType<JudgmentFrame>().Single();
            Require(root.DamageJudgmentSuitPayment is { PaidCardId: null, SourceSeat: 0, TargetSeat: 1 } draft &&
                draft.CardActionId == E<CardActionAcceptedEvent>(g).Single(e => e.Action.Type == CardActionType.Use && e.Action.PhysicalCards.Any(c => c.CardId == slash)).Action.ActionId &&
                draft.JudgmentCardId == judgment.CardId && judgment.TargetSeat == 1 && g.State.Players[1].Hp == hp - 1 && Paid(g).Length == 0,
                "The real injured target judges first; the original damage, action, candidate, final physical card and suit belong to one owning frame.");
            Cold(g, r); Continue(g); Reach(g, p => PaymentPrompt(p, "owner-payment")); Private(g); Cold(g, r); Reject(g);
            var paid = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
            Answer(g, c => c.Cards.SequenceEqual([paid])); Reach(g, p => p.SkillPrompt?.SkillId == Movement);
            var receipt = Paid(g).Single(); Require(receipt.PaidCardId == paid && receipt.JudgmentCardId == judgment.CardId && receipt.SuitMatched && receipt.RankMatched &&
                g.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == Payment) == 1 &&
                g.State.Players[1].Hp == hp - 1 && E<ProgramJudgmentSuitPaymentCardClaimedEvent>(g).Length == 0,
                "The actual HE cost commits once and pauses before any suit benefit or matching claim.");
            Cold(g, r); Reject(g); Continue(g);
            if (suit == Suit.Club)
            {
                for (var i = 0; i < 2; i++) { Reach(g, p => PaymentPrompt(p, "source-discard")); Private(g); Cold(g, r); Answer(g, c => c.Cards.Count == 1); }
            }
            else
            {
                var observer = suit == Suit.Heart ? Hp : suit == Suit.Diamond ? Gain : Flip;
                Reach(g, p => p.SkillPrompt?.SkillId == observer); Cold(g, r); Continue(g);
            }
            Finish(g);
            Require(E<ProgramJudgmentSuitPaymentCardClaimedEvent>(g).Count(c => c.FrameId == receipt.FrameId) == 2 &&
                g.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == paid) && g.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == receipt.JudgmentCardId) &&
                g.CardMovements.Count(m => m.CardId == paid && m.Reason.Value == Payment) == 1 &&
                !g.CardMovements.Any(m => m.CardId == receipt.JudgmentCardId && m.Reason == CardMoveReasons.JudgmentFinish) &&
                g.CardMovements.Count(m => m.CardId == slash && m.Reason == CardMoveReasons.UseFinished) == 1,
                "Same suit and rank independently claim both exact entities; child returns and original Slash cleanup never discard or pay them twice.");
            Require(suit switch
            {
                Suit.Heart => g.State.Players[1].Hp == hp,
                Suit.Diamond => g.CreateSnapshot(1).Players[1].Hand.Count == targetHand + 2,
                Suit.Club => g.CardMovements.Count(m => m.From.OwnerSeat == 0 && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.damage-judgment-suit.source-discard") == 2,
                _ => g.State.Players[0].IsFaceDown
            }, "Each finalized suit executes its actual recovery, draw, source discard or turnover effect.");
            foreach (var claim in E<ProgramJudgmentSuitPaymentCardClaimedEvent>(g))
                Require(g.CardMovements.Any(m => m.Sequence == claim.MovementSequence && m.CardId == claim.CardId && m.To == CardLocation.Hand(claim.OwnerSeat) &&
                    m.From == (claim.IsJudgmentCard ? CardLocation.Processing : CardLocation.DiscardPile)), "Public claims freeze the exact actual movement fact.");
            Cold(g, r);
        }
        var (rescue, rescueRegistry) = Create(Suit.Diamond, gainDying: true); Use(rescue, "draw-many"); Finish(rescue);
        Slash(rescue); ActivateToPayment(rescue); Answer(rescue, c => c.Cards.Count == 1);
        Reach(rescue, p => p.SkillPrompt?.SkillId == Entry); var dying = rescue.ResolutionStack.OfType<DyingFrame>().Single();
        var committed = Paid(rescue).Single();
        Require(dying.VictimSeat == 0 && dying.ResumesProgramSkill && rescue.State.Players[0].Hp == 0 &&
            Root(rescue).DamageJudgmentSuitPayment is { Stage: ProgramDamageJudgmentPaymentStage.ClaimChildren, JudgmentClaimIssued: true, PaymentClaimIssued: false } &&
            E<ProgramJudgmentSuitPaymentCardClaimedEvent>(rescue).Length == 1,
            "The first actual matching gain can enter Dying beneath its paid original Slash candidate before the second claim.");
        Cold(rescue, rescueRegistry); Continue(rescue); Reach(rescue, p => p.Kind == DecisionKind.RescueDying && p.PlayerSeat == 0);
        var peach = P(rescue)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "peach" && c.Cards.Count == 1).Cards.Single();
        Cold(rescue, rescueRegistry); Answer(rescue, c => c.Parameters.GetValueOrDefault("response") == "peach" && c.Cards.SequenceEqual([peach]));
        Reach(rescue, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 0); Cold(rescue, rescueRegistry); Continue(rescue); Finish(rescue);
        Require(rescue.State.Players[0].Hp == 1 && E<DyingResolvedEvent>(rescue).Single(e => e.ResolutionId == dying.Id).Survived &&
            E<ProgramJudgmentSuitPaymentCardClaimedEvent>(rescue).Length == 2 && Paid(rescue).Length == 1 &&
            rescue.CardMovements.Count(m => m.CardId == committed.PaidCardId && m.Reason.Value == Payment) == 1 &&
            E<CardActionAcceptedEvent>(rescue).Any(e => e.Action.Type == CardActionType.Use && e.Action.EffectiveKind == CardKind.Peach && e.Action.PhysicalCards.Any(c => c.CardId == peach)),
            "Real Dying entry, exact physical Peach recovery and HP observer return to the same paid frame; the deferred second claim is issued once."); Cold(rescue, rescueRegistry);
    }

    public static void IndependentSuitRankMatchesAndPreviouslyClaimedJudgment()
    {
        foreach (var suitMatch in new[] { false, true }) foreach (var rankMatch in new[] { false, true })
        {
            var (g, r) = Create(mixed: true); Use(g, "draw-many"); Finish(g); Slash(g); ActivateToPayment(g);
            var draft = Root(g).DamageJudgmentSuitPayment!;
            var cards = g.CreateSnapshot(0).Players[0].Hand;
            var choice = P(g)!.Choices.First(c => c.Cards.Count == 1 && cards.Single(candidateCard => candidateCard.Id == c.Cards.Single()) is { } card &&
                (card.Suit == draft.JudgmentSuit) == suitMatch && (card.Rank == draft.JudgmentRank) == rankMatch);
            var paid = choice.Cards.Single(); Cold(g, r); Answer(g, c => c.Id == choice.Id); Finish(g);
            var receipt = Paid(g).Single(); var claims = E<ProgramJudgmentSuitPaymentCardClaimedEvent>(g);
            Require(receipt.SuitMatched == suitMatch && receipt.RankMatched == rankMatch && claims.Count(c => c.IsJudgmentCard) == (suitMatch ? 1 : 0) &&
                claims.Count(c => !c.IsJudgmentCard) == (rankMatch ? 1 : 0) &&
                g.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == paid) == rankMatch &&
                g.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == receipt.JudgmentCardId) == suitMatch &&
                g.CardMovements.Count(m => m.CardId == receipt.JudgmentCardId && m.Reason == CardMoveReasons.JudgmentFinish) == (suitMatch ? 0 : 1),
                "Both independent equality tests use the finalized judgment and the original discarded entity, including each mismatch combination."); Cold(g, r);
        }
        var (claimed, registry) = Create(preclaim: true); Slash(claimed); Reach(claimed, Activation);
        Answer(claimed, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(claimed, p => PaymentPrompt(p, "owner-payment")); var judgmentReceipt = Root(claimed).DamageJudgmentSuitPayment!;
        var judgedId = judgmentReceipt.JudgmentCardId!.Value;
        Require(E<ProgramJudgmentCardClaimedEvent>(claimed).Any(e => e.JudgmentFrameId == judgmentReceipt.JudgmentFrameId &&
                e.SkillId == "fixture:cwj-preclaim" && e.OwnerSeat == 1 && e.CardId == judgedId) &&
            claimed.CardMovements.Count(m => m.CardId == judgedId && m.From == CardLocation.Judgment(1) &&
                m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.fixture:cwj-preclaim.claimJudgmentCard") == 1 &&
            claimed.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == judgedId),
            "A forced real finalized judgment observer first claims its subject's exact physical entity with a committed claim and actual movement."); Cold(claimed, registry);
        Answer(claimed, c => c.Cards.Count == 1); Finish(claimed);
        Require(Paid(claimed).Single().SuitMatched && E<ProgramJudgmentSuitPaymentCardClaimedEvent>(claimed).All(c => !c.IsJudgmentCard) &&
            claimed.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == judgedId) && !claimed.CardMovements.Any(m => m.CardId == judgedId && m.Reason == CardMoveReasons.JudgmentFinish),
            "Matching does not steal a judgment already claimed by its real observer, and cleanup respects that move."); Cold(claimed, registry);
    }

    public static void OptionalDeclinesAndActualSourceLossKeepOriginalDamage()
    {
        var (skip, skipRegistry) = Create(); Slash(skip); Reach(skip, Activation); Answer(skip, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Finish(skip);
        Require(!E<JudgmentRequestedEvent>(skip).Any(j => j.Reason == Reason) && Paid(skip).Length == 0 && E<DamageAppliedEvent>(skip).Count(d => d.SourceSeat == 0 && d.TargetSeat == 1) == 1,
            "Declining the initial opportunity preserves the already applied actual Slash without a judgment or payment."); Cold(skip, skipRegistry);
        var (decline, declineRegistry) = Create(); Slash(decline); ActivateToPayment(decline); var id = Root(decline).DamageJudgmentSuitPayment!.JudgmentCardId!.Value;
        Cold(decline, declineRegistry); Answer(decline, c => c.Parameters.GetValueOrDefault("payment-kind") == "decline"); Finish(decline);
        Require(Paid(decline).Length == 0 && E<ProgramJudgmentSuitPaymentCardClaimedEvent>(decline).Length == 0 && decline.State.Players[1].Hp == 5 &&
            decline.CardMovements.Count(m => m.CardId == id && m.Reason == CardMoveReasons.JudgmentFinish) == 1,
            "The second independent optional decline cleans the judgment once and grants neither a suit benefit nor matching cards."); Cold(decline, declineRegistry);
        foreach (var afterPayment in new[] { false, true })
        {
            var (g, r) = Create(suit: afterPayment ? Suit.Diamond : Suit.Heart, loss: afterPayment ? "paid" : "judged"); Slash(g); Reach(g, Activation); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            if (afterPayment) { Reach(g, p => PaymentPrompt(p, "owner-payment")); Answer(g, c => c.Cards.Count == 1); }
            Reach(g, p => p.SkillPrompt?.SkillId == Loss && p.Choices.Any(c => c.Targets.SequenceEqual([0]))); Cold(g, r);
            Answer(g, c => c.Targets.SequenceEqual([0])); Finish(g);
            Require(E<CurrentTurnNonLockedSkillSuppressionIssuedEvent>(g).Any(e => e.Suppression.TargetSeat == 0) &&
                Paid(g).Length == (afterPayment ? 1 : 0) &&
                E<ProgramJudgmentSuitPaymentCardClaimedEvent>(g).Length == (afterPayment ? 2 : 0) && g.State.Players[1].Hp == 5 &&
                (!afterPayment || g.CardMovements.Count(m => m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.damage-judgment-suit.draw") == 2),
                "A real observer invalidates the original nonLocked source: before payment it cancels, after the real cost its atomic benefit and claims finish without repaying."); Cold(g, r);
        }
        var (legacy, legacyRegistry) = Create(legacyVirtual: true); ActivateToPayment(legacy); var legacyDraft = Root(legacy).DamageJudgmentSuitPayment!;
        Require(legacyDraft.CardActionId is null && legacyDraft.LegacyVirtualProducerFrameId is { } producer &&
            legacy.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == producer && f.SkillId == "boundary:shensu") &&
            legacy.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == legacyDraft.CardUseFrameId && f.CardId == 0 && f.Action is null && f.PhysicalCardIds is { Count: 0 }),
            "Registered Shensu's real actionless virtual Slash supplies an exact zero-entity use and its paused typed producing program.");
        Cold(legacy, legacyRegistry); Answer(legacy, c => c.Cards.Count == 1); Finish(legacy);
        Require(Paid(legacy).Length == 1 && E<ProgramJudgmentSuitPaymentCardClaimedEvent>(legacy).Length == 2 && legacy.State.Players[1].Hp == 6 &&
            E<CardUseDeclaredEvent>(legacy).Any(e => e.ResolutionId == legacyDraft.CardUseFrameId && e.CardId == 0 && e.CardKind == CardKind.Slash && e.SourceSeat == 0) &&
            !E<CardActionAcceptedEvent>(legacy).Any(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Slash),
            "Real legacy virtual Slash damage triggers the new judgment and payment while its historical lack of CardAction remains unchanged."); Cold(legacy, legacyRegistry);
        var (other, otherRegistry) = Create(); Use(other, "plain-damage", [1]); Finish(other);
        Require(!E<ProgramBindingStartedEvent>(other).Any(e => e.SkillId == Beige) && !E<JudgmentRequestedEvent>(other).Any(e => e.Reason == Reason),
            "Program damage without an actual Slash use is not the registered opportunity."); Cold(other, otherRegistry);
    }

    public static void NativePaymentAndStrictOwningLoaderContract()
    {
        var (native, r) = Create(native: true);
        var paid = Paid(native).Length > 0;
        for (var i = 0; !paid && i < 80 && native.State.Status != EngineStatus.Completed; i++)
        { Accept(native, new AdvanceOneStepCommand(native.Revision)); paid = Paid(native).Length > 0; }
        Require(paid && E<ProgramJudgmentSuitPaymentCardClaimedEvent>(native).Length >= 2 &&
            E<CardActionAcceptedEvent>(native).Any(e => e.Action.Type == CardActionType.Use && e.Action.EffectiveKind == CardKind.Slash && e.Action.PhysicalCards.Count == 1) &&
            Paid(native).All(e => native.CardMovements.Any(m => m.Sequence == e.PaidMovementSequence && m.From == CardLocation.Hand(e.OwnerSeat) && m.To == CardLocation.DiscardPile && m.Reason.Value == Payment)),
            "Unattended native AI activates, chooses and pays real known matching cards; no manual AI answers, seed search or synthetic costs are used."); Cold(native, r);
        var valid = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:suit-loader","revision":1,"triggers":[{"id":"one","window":"afterDamageApplied","subject":"any","optional":true,"damageOccurrence":"perDamage","effects":[{"op":"judgeDamageTargetThenOfferSuitDiscard","target":"owner","judgmentReason":"fixture.judge","resultBind":"judged","visibility":"public"}]}]}]}""";
        const string presentation = """{"schemaVersion":3,"skills":{"fixture:suit-loader":{"name":"严格合同","description":"严格合同"}}}""";
        SkillProgramCatalog.Load(valid, presentation);
        foreach (var invalid in new[] { valid.Replace("\"subject\":\"any\"", "\"subject\":\"owner\""), valid.Replace("\"optional\":true", "\"optional\":false"),
            valid.Replace("\"perDamage\"", "\"perDamagePoint\""), valid.Replace("\"target\":\"owner\"", "\"target\":\"selectedTarget\""), valid.Replace("\"public\"", "\"private\"") })
        {
            var rejected = false; try { SkillProgramCatalog.Load(invalid, presentation); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The additive operation does not accept incompatible window/subject/occurrence, selection or privacy contracts.");
        }
    }

    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Beige);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static ProgramJudgmentSuitPaymentCommittedEvent[] Paid(GameEngine g) => E<ProgramJudgmentSuitPaymentCommittedEvent>(g);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Activation(PendingDecision p) => p.SkillPrompt?.SkillId == Beige && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool PaymentPrompt(PendingDecision p, string kind) => p.SkillPrompt?.SkillId == Beige && p.Choices.Any(c => c.Parameters.GetValueOrDefault("payment-kind") == kind);
    private static void ActivateToPayment(GameEngine g) { Reach(g, Activation); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, p => PaymentPrompt(p, "owner-payment")); }
    private static int Slash(GameEngine g)
    {
        var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1]));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, [1], g.Revision, P(g)!.PromptId, action.PlayedCardKind)); return action.CardId.Value;
    }
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> f) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(f).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Finish(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> f)
    { for (var i = 0; i < 160; i++) { if (P(g) is { } p && f(p)) return; Advance(g); } throw new InvalidOperationException("Fixed Cai Wen Ji command prefix missed its boundary: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is not null && new[] { Final, Movement, Gain, Hp, Flip, Entry, GainDying }.Contains(p.SkillPrompt?.SkillId) && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p?.SkillPrompt?.SkillId == "boundary:shensu" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is not null && PaymentPrompt(p, "source-discard")) Answer(g, c => c.Cards.Count == 1);
        else if (p is { Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(p.PlayerSeat, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static void Private(GameEngine g) { var p = P(g)!; Require(p.IsPrivate, "Payment input is private to its actual choosing owner."); for (var seat = 0; seat < 4; seat++) if (seat != p.PlayerSeat) Require(g.CreateSnapshot(seat).PendingDecision is null, "Other views hide exact private payment slots."); }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished choice rejects atomically."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "Four private snapshots, scalar owning receipts, events, actual movements and accepted commands cold-restore identically.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(Suit suit = Suit.Heart, bool mixed = false, bool preclaim = false, string? loss = null, bool native = false, bool gainDying = false, bool legacyVirtual = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(suit, mixed, preclaim, loss, native, gainDying, legacyVirtual));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = !native, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = native ? 4 : 6 }, r);
        Accept(g, new StartGameCommand());
        if (!native)
        {
            Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0); Accept(g, new SelectGeneralCommand(0, "fixture:cwj-owner", g.Revision, P(g)!.PromptId));
            if (legacyVirtual)
            {
                Reach(g, p => p.SkillPrompt?.SkillId == "boundary:shensu" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                Reach(g, p => p.SkillPrompt?.SkillId == "boundary:shensu" && p.Choices.Any(c => c.Targets.SequenceEqual([1]))); Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, Activation);
            }
            else Finish(g);
        }
        return (g, r);
    }

    private sealed class Fixture(Suit suit, bool mixed, bool preclaim, string? loss, bool native, bool gainDying, bool legacyVirtual) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-cai-wen-ji", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var lossWindow = loss == "paid" ? "cardsGained" : "judgmentFinalized";
            var lossFields = loss == "paid" ? "\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.damage-judgment-suit.draw\"]," : "\"judgmentReasons\":[\"" + Reason + "\"],\"minimumRank\":1,\"maximumRank\":13,\"suits\":[\"spade\",\"heart\",\"club\",\"diamond\"],\"excludedReasons\":[],";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"activations":[{"id":"draw-many","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":{{(gainDying ? 2 : 20)}}},{"op":"draw","target":"owner","amount":{{(gainDying ? 2 : 20)}}}]},{"id":"plain-damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
            {"id":"{{Final}}","revision":1,"triggers":[{"id":"final","window":"judgmentFinalized","subject":"owner","judgmentReasons":["{{Reason}}"],"minimumRank":1,"maximumRank":13,"suits":["spade","heart","club","diamond"],"excludedReasons":[],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Movement}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{Payment}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.damage-judgment-suit.draw","skill-program.damage-judgment-suit.claim-judgment","skill-program.damage-judgment-suit.reclaim-payment"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Hp}}","revision":1,"triggers":[{"id":"recover","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Flip}}","revision":1,"triggers":[{"id":"flip","window":"characterTurnedOver","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{GainDying}}","revision":1,"triggers":[{"id":"first-claim","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.damage-judgment-suit.claim-judgment"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":7},{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Loss}}","revision":1,"triggers":[{"id":"loss","window":"{{lossWindow}}","subject":"owner",{{lossFields}}"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"issueCurrentTurnNonLockedSkillSuppression","target":"selectedTarget"}]}]},
            {"id":"fixture:cwj-preclaim","revision":1,"triggers":[{"id":"claim","window":"judgmentFinalized","subject":"owner","judgmentReasons":["{{Reason}}"],"minimumRank":1,"maximumRank":13,"suits":["spade","heart","club","diamond"],"excludedReasons":[],"optional":false,"effects":[{"op":"claimJudgmentCard","target":"owner"}]}]}]}
            """;
            var descriptions = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, Final, Movement, Gain, Hp, Flip, Entry, GainDying, Loss, "fixture:cwj-preclaim" })
                descriptions[id] = new[] { Final, Movement, Gain, Hp, Flip, Entry, GainDying }.Contains(id)
                    ? new { name = id, description = "真实子窗口夹具", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                    : (object)new { name = id, description = "真实通用能力夹具" };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = descriptions }));
            foreach (var id in c.Programs.Keys) b.AddSkill(new(id, id, "真实通用能力夹具") { Program = c.Programs[id], Tags = id == Loss ? SkillTag.Locked : SkillTag.None });
            foreach (var owner in new[] { true, false }) b.AddSkill(new(owner ? "fixture:cwj-pick-owner" : "fixture:cwj-pick-other", "选将", "正式评分固定角色")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 100000d : -100000d) });
            var ownerSkills = native ? new List<string> { "fixture:cwj-pick-owner" } : new List<string> { Driver, Movement, Gain, Flip, "fixture:cwj-pick-owner" };
            if (gainDying) ownerSkills.AddRange([Hp, Entry, GainDying]);
            if (legacyVirtual) ownerSkills.Add("boundary:shensu");
            b.AddGeneral(new("fixture:cwj-owner", "真实悲歌来源", "supporter", Beige, "qun", 6, ownerSkills.ToArray()));
            for (var i = 1; i < 4; i++)
            {
                var skills = native ? new List<string>() : new List<string> { Final, Gain, Hp };
                if (i == 1 && loss is not null) skills.Add(Loss); if (i == 1 && preclaim) skills.Add("fixture:cwj-preclaim");
                b.AddGeneral(new($"fixture:cwj-other-{i}", "其他角色", "supporter", "fixture:cwj-pick-other", "wei", 6, skills.ToArray()));
            }
            b.AddDeck(new("fixture:cwj-deck", "固定实际杀", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                new ContentDeckPhysicalCard(gainDying && i % 2 == 1 ? "standard:peach" : "standard:slash", mixed ? i % 4 < 2 ? Suit.Heart : Suit.Club : suit, mixed ? i % 2 == 0 ? 7 : 8 : 7)).ToArray() });
            b.AddMode(new(Mode, "悲歌实际链", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:cwj-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:cwj-owner", "fixture:cwj-other-1", "fixture:cwj-other-2", "fixture:cwj-other-3"]));
        }
    }
}
