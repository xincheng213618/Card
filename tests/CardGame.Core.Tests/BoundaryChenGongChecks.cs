using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryChenGongChecks
{
    private const string Mingce = "boundary:mingce-current";
    private const string Driver = "fixture:cg-driver";
    private const string GiftGain = "fixture:cg-gift-gain";
    private const string RewardGain = "fixture:cg-reward-gain";
    private const string Hp = "fixture:cg-hp";
    private const string Completed = "fixture:cg-completed";
    private const string Pulse = "fixture:cg-pulse";
    private const string Mode = "identity:classic-current-chen-gong";

    public static void SharedGiftEquipmentRecoveryAndBothGainChildrenCold()
    {
        foreach (var equipment in new[] { false, true })
        {
            var (g, r) = Create(equipment: equipment);
            int gift;
            var hp = g.State.Players[0].Hp;
            if (equipment)
            {
                var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
                gift = action.CardId!.Value;
                Accept(g, new PlayCardCommand(0, gift, action.TargetSeats, g.Revision, P(g)!.PromptId)); Play(g);
                Require(g.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == gift) && hp < g.State.Players[0].MaxHp,
                    "A real wounded owner equips the exact Silver Lion before giving its equipment entity.");
            }
            else gift = Hand(g, 0).First().Id;
            BeginGift(g, gift);
            if (equipment)
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Hp);
                var root = Root(g);
                Require(root.SharedSlashOffer is { Stage: SharedSlashOfferStage.GiftChildren } && g.State.Players[0].Hp == hp + 1 &&
                    g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(w => w.ResumeFrameId == root.Id && w.Continuation == PostEventContinuation.Program),
                    "The paid equipment gift owns its actual removal recovery before any recipient choice.");
                Once(g, root.Id, gift); Cold(g, r); Reject(g); Continue(g);
            }
            Reach(g, p => p.SkillPrompt?.SkillId == GiftGain);
            var paid = Root(g); var id = paid.Id;
            Require(paid.SharedSlashOffer is { Stage: SharedSlashOfferStage.GiftChildren, ActorSeat: 1 } &&
                Hand(g, 1).Any(c => c.Id == gift) && !Hand(g, 0).Any(c => c.Id == gift) &&
                g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.ResumeProgramFrameId == id && w.Batch.ParentFrameId == id &&
                    w.Batch.Movements is [var m] && m.CardId == gift && m.To == CardLocation.Hand(1)),
                "The exact gift commits once before its recipient gain child, retaining the original actor and real batch parent.");
            Cold(g, r); Reject(g); Continue(g); ReachOffer(g, 1);
            var ownerHand = g.State.Players[0].HandCount; var actorHand = g.State.Players[1].HandCount;
            Private(g, 1); Cold(g, r); Reject(g);
            Answer(g, choice => choice.Parameters.GetValueOrDefault("offer-option") == "draw");
            Reach(g, p => p.SkillPrompt?.SkillId == RewardGain && p.PlayerSeat == 0);
            Require(Root(g).SharedSlashOffer is { Stage: SharedSlashOfferStage.OwnerDrawIssued } &&
                g.State.Players[0].HandCount == ownerHand + 1 && g.State.Players[1].HandCount == actorHand &&
                Facts<SharedSlashDrawIssuedEvent>(g).Count(e => e.ProgramFrameId == id) == 1,
                "The owner draw is paid before its gain child; the recipient's second benefit is still unissued.");
            AssertGainOwner(g, id, 0); Once(g, id, gift); Cold(g, r); Continue(g);
            Reach(g, p => p.SkillPrompt?.SkillId == RewardGain && p.PlayerSeat == 1);
            Require(Root(g).SharedSlashOffer is { Stage: SharedSlashOfferStage.RecipientDrawIssued } &&
                g.State.Players[1].HandCount == actorHand + 1 && Facts<SharedSlashDrawIssuedEvent>(g).Count(e => e.ProgramFrameId == id) == 2,
                "Only after the first child returns does the exact recipient receive its own real draw.");
            AssertGainOwner(g, id, 1); Once(g, id, gift); Cold(g, r); Continue(g); Play(g);
            Require(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == id) &&
                Facts<ProgramSkillResolvedEvent>(g).Count(e => e.FrameId == id && e.Completed) == 1 &&
                !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Mingce),
                "The original active binding completes once and the real Play-phase usage is consumed.");
            Once(g, id, gift); Cold(g, r);
        }
    }

    public static void VirtualSlashActualDamageRescueCompletedAndCommittedSourceLossCold()
    {
        foreach (var scenario in new[] { "normal", "rescue", "suppression" })
        {
            var (g, r) = Create(rescue: scenario == "rescue", suppressAfterUse: scenario == "suppression");
            if (scenario == "rescue") { Use(g, "weaken-target", [2]); Play(g); Require(g.State.Players[2].Hp == 1, "The real pre-use HP cost leaves a one-HP victim."); }
            var gift = Hand(g, 0).First().Id; BeginGift(g, gift);
            Reach(g, p => p.SkillPrompt?.SkillId == GiftGain); Continue(g); ReachOffer(g, 1);
            Answer(g, c => c.Parameters.GetValueOrDefault("offer-option") == "slash"); ReachOffer(g, 0);
            Require(P(g)!.Choices.Where(c => c.Targets.Count != 0).All(c => c.Targets.Count == 1 && c.Targets[0] != 1) &&
                !P(g)!.Choices.Any(c => c.Targets.SequenceEqual([3])),
                "The owner chooses another actor's legal ordinary-range target, excluding the actor and distance two.");
            Cold(g, r); Reject(g); Answer(g, c => c.Targets.SequenceEqual([2]));
            if (scenario == "rescue")
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Pulse);
                var use = UseOwner(g); var root = Root(g);
                var dying = g.ResolutionStack.OfType<DyingFrame>().Single(f => f.VictimSeat == 2);
                Require(use.SharedSlashBenefit == root.SharedSlashOffer!.SlashReturn && use.CausedDamage &&
                    root.SharedSlashOffer.Stage == SharedSlashOfferStage.SlashIssued && g.State.Players[2].Hp == 0 &&
                    !Facts<SharedSlashDrawIssuedEvent>(g).Any(e => e.ProgramFrameId == root.Id) &&
                    dying.ParentFrameId != root.Id,
                    "The original real zero-entity Slash retains its typed program parent through the victim's exact Dying child; no benefit precedes rescue.");
                Once(g, root.Id, gift); Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 2);
                Require(UseOwner(g).Id == use.Id && g.State.Players[2].Hp == 3 &&
                    Root(g).SharedSlashOffer!.Stage == SharedSlashOfferStage.SlashIssued,
                    "The actual rescue recovery HP child remains within the same unfinished use.");
                Cold(g, r); Continue(g);
            }
            Reach(g, p => p.SkillPrompt?.SkillId == Completed);
            var owning = UseOwner(g); var paid = Root(g); var typed = owning.SharedSlashBenefit!;
            Require(typed.ProgramFrameId == paid.Id && typed.ActorSeat == 1 && typed.OriginalTargetSeat == 2 &&
                owning.Action is { ActorSeat: 1, ProviderSeat: 1, EffectiveKind: CardKind.Slash } action &&
                action.PhysicalCards.Count == 0 && action.ConversionChain.Count == 0 && owning.PhysicalCardIds is { Count: 0 } &&
                owning.CausedDamage && paid.SelectedTargetSeats.SequenceEqual([1]) &&
                g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.ParentFrameId == owning.Id && w.Continuation == ProgramCardContinuation.CompletedSlash) &&
                !Facts<SharedSlashDrawIssuedEvent>(g).Any(e => e.ProgramFrameId == paid.Id),
                "All actual Slash damage and the completed-card observer remain beneath the full owning zero-entity use, before either shared benefit.");
            Once(g, paid.Id, gift); Cold(g, r); Reject(g); Continue(g);
            if (scenario == "suppression")
            {
                Reach(g, p => Action(p, "select-target") && p.PlayerSeat == 1);
                Answer(g, c => c.Targets.SequenceEqual([0]));
            }
            Reach(g, p => p.SkillPrompt?.SkillId == RewardGain && p.PlayerSeat == 0);
            var id = paid.Id;
            Require(Facts<SharedSlashUseReturnedEvent>(g).Single(e => e.Return.ProgramFrameId == id) is { CausedDamage: true } &&
                !g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == owning.Id) &&
                Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == owning.Id) == 1 &&
                Facts<SharedSlashDrawIssuedEvent>(g).Count(e => e.ProgramFrameId == id) == 1,
                "The complete owning use returns its actual aggregate once before the first reward draw child.");
            if (scenario == "suppression")
                Require(Facts<CurrentTurnNonLockedSkillSuppressionIssuedEvent>(g).Any(e => e.Suppression.TargetSeat == 0 && e.Suppression.Source.OwnerSeat == 1),
                    "A real completed-use observer issues suppression against the original nonlocked source before its already-issued benefit returns.");
            AssertGainOwner(g, id, 0); Cold(g, r); Continue(g);
            Reach(g, p => p.SkillPrompt?.SkillId == RewardGain && p.PlayerSeat == 1);
            AssertGainOwner(g, id, 1); Cold(g, r); Continue(g); Play(g);
            Require(Facts<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 1 && e.TargetSeat == 2 && e.Amount == 1) == 1 &&
                Facts<SharedSlashDrawIssuedEvent>(g).Where(e => e.ProgramFrameId == id).Select(e => e.RecipientSeat).SequenceEqual([0, 1]) &&
                Facts<ProgramSkillResolvedEvent>(g).Count(e => e.FrameId == id) == 1 &&
                !g.CreateCardZoneDiagnostics().Any(z => z.Location == CardLocation.Processing),
                "Damage, complete-use return and both surviving participants' real gain children finish once with no fictitious material or orphan processing.");
            Once(g, id, gift); Cold(g, r);
        }
    }

    public static void DodgedAndPreventedVirtualSlashCannotBorrowPriorDamage()
    {
        foreach (var prevention in new[] { false, true })
        {
            var (g, r) = Create(dodge: !prevention, prevent: prevention);
            Use(g, "unrelated", [1]); Play(g);
            var prior = Facts<DamageAppliedEvent>(g).Last();
            Require(prior.SourceSeat == 1 && prior.TargetSeat == 0, "A real earlier damage event has the same future Slash actor but is outside its owning use.");
            var gift = Hand(g, 0).First().Id; BeginGift(g, gift);
            Reach(g, p => p.SkillPrompt?.SkillId == GiftGain); Continue(g); ReachOffer(g, 1);
            Answer(g, c => c.Parameters.GetValueOrDefault("offer-option") == "slash"); ReachOffer(g, 0); Answer(g, c => c.Targets.SequenceEqual([2]));
            if (!prevention)
            {
                Reach(g, p => p.Kind == DecisionKind.RespondDodge && p.PlayerSeat == 2);
                Require(P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "program-dodge"),
                    "The real target has a black-card Dodge conversion, not an injected dodged flag.");
                Cold(g, r); Accept(g, new AdvanceOneStepCommand(g.Revision));
            }
            Reach(g, p => p.SkillPrompt?.SkillId == Completed);
            var use = UseOwner(g); var root = Root(g); var id = root.Id;
            Require(!use.CausedDamage && !Facts<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 1 && e.TargetSeat == 2),
                "Actual Dodge or actual beforeDamage prevention leaves this exact use's aggregate false.");
            Cold(g, r); Continue(g); Play(g);
            Require(Facts<SharedSlashUseReturnedEvent>(g).Single(e => e.Return.ProgramFrameId == id) is { CausedDamage: false } &&
                !Facts<SharedSlashDrawIssuedEvent>(g).Any(e => e.ProgramFrameId == id) &&
                Facts<DamageAppliedEvent>(g).Last() == prior && Facts<ProgramSkillResolvedEvent>(g).Count(e => e.FrameId == id) == 1,
                "The exact false completion never borrows the last unrelated damage event to issue shared rewards.");
            Once(g, id, gift); Cold(g, r);
        }
    }

    public static void GiftIdentitySourceCancellationNativeAndClassicDefaults()
    {
        var (identity, identityRegistry) = Create(wine: true, intrinsicIdentity: true);
        var wine = Hand(identity, 0).First().Id; BeginGift(identity, wine);
        Reach(identity, p => p.SkillPrompt?.SkillId == GiftGain); Cold(identity, identityRegistry); Continue(identity); ReachOffer(identity, 1);
        Answer(identity, c => c.Parameters.GetValueOrDefault("offer-option") == "draw");
        Reach(identity, p => p.SkillPrompt?.SkillId == RewardGain); Continue(identity);
        Reach(identity, p => p.SkillPrompt?.SkillId == RewardGain); Continue(identity); Play(identity);
        Require(Facts<SharedSlashGiftCommittedEvent>(identity).Count() == 1 && Hand(identity, 1).Any(c => c.Id == wine && c.Kind == CardKind.Alcohol),
            "The already-issued intrinsic hand Alcohol-as-Slash identity is legal at selection and payment; gifting preserves the printed entity.");
        Cold(identity, identityRegistry);

        var (conversion, conversionRegistry) = Create(wine: true);
        var before = conversion.State.Players[0].HandCount;
        Accept(conversion, new UseProgramSkillCommand(0, Mingce, "offer-command", [], [1], conversion.Revision, P(conversion)!.PromptId)); Play(conversion);
        Require(Facts<SharedSlashGiftCommittedEvent>(conversion).Count() == 0 && conversion.State.Players[0].HandCount == before,
            "An optional red-card Slash conversion is not intrinsic entity identity and cannot turn a printed Alcohol into a legal gift.");
        Cold(conversion, conversionRegistry);

        var (cancel, cancelRegistry) = Create(suppressAtGift: true);
        var held = Hand(cancel, 0).First().Id; BeginGift(cancel, held); Reach(cancel, p => p.SkillPrompt?.SkillId == GiftGain);
        var paidId = Root(cancel).Id; Cold(cancel, cancelRegistry); Continue(cancel);
        Reach(cancel, p => Action(p, "select-target") && p.PlayerSeat == 1); Answer(cancel, c => c.Targets.SequenceEqual([0])); Play(cancel);
        Require(Hand(cancel, 1).Any(c => c.Id == held) && !Facts<SharedSlashUseIssuedEvent>(cancel).Any(e => e.Return.ProgramFrameId == paidId) &&
            !Facts<SharedSlashDrawIssuedEvent>(cancel).Any(e => e.ProgramFrameId == paidId) &&
            Facts<ProgramSkillResolvedEvent>(cancel).Single(e => e.FrameId == paidId) is { Completed: false },
            "Loss of the actual source before the recipient chooses cancels only the unissued tail and never refunds or repays the committed gift.");
        Once(cancel, paidId, held); Cold(cancel, cancelRegistry);

        var (classic, classicRegistry) = Create(classic: true);
        var cost = Hand(classic, 0).First().Id;
        Accept(classic, new UseProgramSkillCommand(0, "classic:mingce", "offer-command", [], [1], classic.Revision, P(classic)!.PromptId));
        Reach(classic, p => Action(p, "select-owned-cards")); Answer(classic, c => c.Cards.SequenceEqual([cost]));
        Reach(classic, p => Action(p, "virtual-slash-offer"));
        var ownerBefore = classic.State.Players[0].HandCount; var recipientBefore = classic.State.Players[1].HandCount;
        Cold(classic, classicRegistry); Answer(classic, c => c.Parameters.GetValueOrDefault("offer-option") == "draw"); Play(classic);
        Require(classic.State.Players[0].HandCount == ownerBefore && classic.State.Players[1].HandCount == recipientBefore + 1 &&
            !Facts<SharedSlashGiftCommittedEvent>(classic).Any() && !Facts<SharedSlashDrawIssuedEvent>(classic).Any(),
            "The classic opcode keeps its historical recipient-only draw and emits no new policy facts.");
        Cold(classic, classicRegistry);

        var (native, nativeRegistry) = Create(native: true);
        Until(native, () => native.State.Status == EngineStatus.Completed);
        var issued = Facts<SharedSlashGiftCommittedEvent>(native).ToArray();
        Require(issued.Length == 1 && Facts<ProgramSkillResolvedEvent>(native).Any(e => e.FrameId == issued[0].ProgramFrameId && e.Completed) &&
            Facts<SharedSlashDrawIssuedEvent>(native).Count(e => e.ProgramFrameId == issued[0].ProgramFrameId) == 2 &&
            native.CardMovements.Count(m => m.Reason.Value == "program.shared-slash.gift") == 1,
            "A fixed four-turn native match chooses, pays and finishes the real new active binding and public option without duplicated costs.");
        Cold(native, nativeRegistry);
        RejectUnsupportedShape();
    }

    private static void RejectUnsupportedShape()
    {
        var select = new { op = "selectOwnedCards", target = "owner", minimumCards = 1, maximumCards = 1, zones = new[] { "hand" }, resultBind = "gift" };
        var offer = new { op = "giveBoundCardThenOfferVirtualSlashOrSharedDraw", target = "selectedTarget", sourceBind = "gift" };
        var rules = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new[] {
            new { id = "fixture:cg-invalid", revision = 1, triggers = new[] { new { id = "invalid", window = "afterDamageApplied", subject = "owner", optional = false, effects = new object[] { select, offer } } } } } });
        var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object> { ["fixture:cg-invalid"] = new { name = "不合法伤害组合", description = "此节点只承诺active owning chain" } } });
        var rejected = false; try { SkillProgramCatalog.Load(rules, presentation); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "A shared gift cannot claim an unsupported nested damage-trigger owning contract.");
    }

    private static IEnumerable<T> Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static IReadOnlyList<CardSnapshot> Hand(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat].Hand;
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SharedSlashOffer is not null);
    private static CardUseFrame UseOwner(GameEngine g) => g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.SharedSlashBenefit is not null);
    private static void AssertGainOwner(GameEngine g, long frameId, int seat) =>
        Require(g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.ResumeProgramFrameId == frameId && w.Batch.ParentFrameId == frameId &&
            w.Batch.Movements is [var m] && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(seat)),
            "Each benefit's actual movement child returns to the original paid offer frame.");
    private static void Once(GameEngine g, long frameId, int id) =>
        Require(Facts<SharedSlashGiftCommittedEvent>(g).Count(e => e.ProgramFrameId == frameId) == 1 &&
            g.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Hand(1) && m.Reason.Value == "program.shared-slash.gift") == 1,
            "The exact gifted entity and public receipt are paid once across every child and cold return.");
    private static void BeginGift(GameEngine g, int cardId)
    {
        Accept(g, new UseProgramSkillCommand(0, Mingce, "offer-command", [], [1], g.Revision, P(g)!.PromptId));
        Reach(g, p => Action(p, "select-owned-cards")); Private(g, 0);
        Answer(g, c => c.Cards.SequenceEqual([cardId]));
    }
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static void ReachOffer(GameEngine g, int seat) => Reach(g, p => Action(p, "shared-slash-offer") && p.PlayerSeat == seat);
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var i = 0; i < 150; i++) { if (P(g) is { } p && predicate(p)) return; Step(g); } throw new InvalidOperationException("Chen Gong exact boundary missing: " + JsonSerializer.Serialize(P(g))); }
    private static void Until(GameEngine g, Func<bool> predicate)
    { for (var i = 0; i < 200; i++) { if (predicate()) return; Step(g); } throw new InvalidOperationException("Chen Gong native boundary missing."); }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else if (p?.SkillPrompt?.SkillId is GiftGain or RewardGain or Hp or Completed or Pulse) Continue(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Private(GameEngine g, int seat)
    { Require(P(g) is { IsPrivate: true } && P(g)!.PlayerSeat == seat, "The actual participant alone sees this prompt."); foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != seat)) Require(g.CreateSnapshot(viewer).PendingDecision is null, "Uninvolved prepared views receive no private choices."); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static void Reject(GameEngine g)
    { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "Unpublished choice cannot change a frozen paid receipt or any private view."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "Four prepared views, exact payment/true-use returns, all movement facts and command prefixes cold-restore identically.");
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }

    private static (GameEngine, ContentRegistry) Create(bool equipment = false, bool rescue = false, bool suppressAfterUse = false, bool dodge = false, bool prevent = false,
        bool wine = false, bool intrinsicIdentity = false, bool suppressAtGift = false, bool classic = false, bool native = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture(equipment, rescue, suppressAfterUse, dodge, prevent, wine, intrinsicIdentity, suppressAtGift, classic, native));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = native ? 4 : 8 }, r);
        Accept(g, new StartGameCommand());
        if (!native) { Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0); Accept(g, new SelectGeneralCommand(0, "fixture:cg-owner", g.Revision, P(g)!.PromptId)); Play(g); }
        return (g, r);
    }

    private sealed class Fixture(bool equipment, bool rescue, bool suppressAfterUse, bool dodge, bool prevent, bool wine,
        bool intrinsicIdentity, bool suppressAtGift, bool classic, bool native) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-chen-gong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var suppress = new object[] { new { op = "selectTarget", target = "owner", targetKind = "otherLiving" }, new { op = "issueCurrentTurnNonLockedSkillSuppression", target = "selectedTarget" } };
            var option = new { op = "chooseOption", target = "owner", resultBind = "seen", options = new[] { new { id = "continue" } } };
            object Observer(string id, string window, object[] effects, string[]? reasons = null) => new { id, revision = 1, triggers = new[] {
                new { id = "actual-child", window, subject = "owner", optional = false, destinationZones = reasons is null ? null : new[] { "hand" },
                    movementOccurrence = reasons is null ? null : "perBatch", movementReasons = reasons, effects } } };
            var skills = new List<object> {
                new { id = Driver, revision = 1, activations = new[] {
                    new { id = "weaken-target", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null,
                        effects = new object[] { new { op = "loseHp", target = "selectedTarget", amount = 7 } } },
                    new { id = "unrelated", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null,
                        effects = new object[] { new { op = "damage", target = "owner", amount = 1, sourceRef = new { kind = "selectedTarget" } } } } } },
                new { id = "fixture:cg-quiet", revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new object[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                Observer(GiftGain, "cardsGained", suppressAtGift ? new object[] { option }.Concat(suppress).ToArray() : [option], ["program.shared-slash.gift"]),
                Observer(RewardGain, "cardsGained", [option], ["program.shared-slash.owner-draw", "program.shared-slash.recipient-draw"]),
                Observer(Hp, "afterHpRecovered", [option]),
                new { id = Completed, revision = 1, triggers = new[] { new { id = "complete-use", window = "cardUseCompleted", ownerRelation = "actor", cardKinds = new[] { "slash" }, optional = false,
                    effects = suppressAfterUse ? new object[] { option }.Concat(suppress).ToArray() : [option] } } },
                new { id = Pulse, revision = 1, triggers = new[] { new { id = "actual-rescue", window = "selfDyingResponse", subject = "owner", optional = false, usageScope = "game", usageLimit = 1,
                    effects = new object[] { option, new { op = "recoverTo", target = "owner", numberExpression = "integerConstant", minimumValue = 3, clampToMaxHp = true } } } } },
                new { id = "fixture:cg-prevent", revision = 1, triggers = new[] { new { id = "prevent", window = "beforeDamageApplied", subject = "damageTarget", optional = false,
                    effects = new object[] { new { op = "preventCurrentDamage", target = "owner" } } } } }
            };
            var presentations = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, "fixture:cg-quiet", "fixture:cg-prevent" }) presentations[id] = new { name = id, description = "真实命令夹具" };
            foreach (var id in new[] { GiftGain, RewardGain, Hp, Completed, Pulse }) presentations[id] = new { name = id, description = "真实孩子暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var json = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            var rules = JsonNode.Parse(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills }, json))!;
            var driver = rules["skills"]!.AsArray().Single(skill => skill!["id"]!.GetValue<string>() == Driver)!;
            // Unlimited activations require an explicit null even when optional observer fields are omitted.
            foreach (var activation in driver["activations"]!.AsArray())
            {
                var node = activation!.AsObject();
                if (!node.ContainsKey("usesPerTurn")) node["usesPerTurn"] = null;
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(),
                JsonSerializer.Serialize(new { schemaVersion = 3, skills = presentations }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实命令夹具") { Program = catalog.Programs[id], Tags = id == "fixture:cg-quiet" ? SkillTag.Locked : SkillTag.None });
            b.AddSkill(new("fixture:cg-pick-owner", "固定主公", "公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:cg-pick-other", "固定其他", "公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? -100000d : 100000d) });
            var owner = new List<string> { classic ? "classic:mingce" : Mingce, Driver, RewardGain, Hp };
            if (wine) owner.Add("classic:wusheng");
            if (intrinsicIdentity) owner.Add("boundary:jinjiu-current");
            b.AddGeneral(new("fixture:cg-owner", "当前界陈宫机制", "supporter", "fixture:cg-pick-owner", "qun", 3, owner) { InitialHp = equipment ? 2 : 3 });
            for (var i = 1; i < 4; i++)
            {
                var other = new List<string> { "fixture:cg-quiet" };
                if (!native) { other.AddRange([GiftGain, RewardGain, Completed, Hp]); if (rescue) other.Add(Pulse); }
                if (dodge) other.Add("classic:qingguo");
                if (prevent) other.Add("fixture:cg-prevent");
                b.AddGeneral(new($"fixture:cg-other-{i}", "固定实际受赠者", "supporter", "fixture:cg-pick-other", "wei", 8, other));
            }
            b.AddDeck(new("fixture:cg-deck", "固定真实材料", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(i =>
                new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : wine ? "standard:alcohol" : "standard:slash", equipment || wine ? Suit.Heart : Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "界陈宫真实边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:cg-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:cg-owner", "fixture:cg-other-1", "fixture:cg-other-2", "fixture:cg-other-3"]));
        }
    }
}
