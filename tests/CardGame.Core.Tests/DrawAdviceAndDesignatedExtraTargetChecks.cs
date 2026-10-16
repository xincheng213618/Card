using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class DrawAdviceAndDesignatedExtraTargetChecks
{
    private const string Mode = "identity:classic-draw-advice-designation";
    private const string Owner = "fixture:draw-advice-owner";
    private const string Driver = "fixture:draw-advice-driver";
    private const string Prep = "fixture:draw-advice-preparation";
    private const string DrawChild = "fixture:draw-advice-gain";
    private const string CostChild = "fixture:draw-advice-cost";
    private const string HpChild = "fixture:draw-advice-hp";
    private const string CompleteChild = "fixture:draw-advice-completion";
    private const string Bizheng = "ol:bizheng";
    private const string Yidian = "ol:yidian";

    public static void DrawAdviceFreezesBothQualificationsAfterRealDrawAndPaysHeOnce()
    {
        for (var variant = 0; variant < 4; variant++) ExerciseAdvice(variant);
    }

    private static void Add(ref GameEngine g, ContentRegistry registry, CardUseFrame original, IReadOnlyList<int> extra)
    {
        var old = P(g)!; var choice = old.Choices.Single(c => c.Targets.SequenceEqual(extra)); var revision = g.Revision;
        Answer(g, c => c.Id == choice.Id);
        var resolved = Facts<DesignatedExtraTargetResolvedEvent>(g).Single(e => e.CardUseFrameId == original.Id);
        Require(resolved.ActionId == original.Action!.ActionId && resolved.OriginalTargetSeats.SequenceEqual(original.TargetSeats) &&
                resolved.AddedTargetSeats.SequenceEqual(extra) && resolved.ResultTargetSeats.SequenceEqual(original.TargetSeats.Concat(extra)),
            "One accepted published choice appends the frozen additional target or compound pair to the same action exactly once.");
        Frozen(resolved.OriginalTargetSeats); Frozen(resolved.AddedTargetSeats); Frozen(resolved.ResultTargetSeats);
        Reject(g, new AnswerPromptCommand(0, old.PromptId, choice.Id, revision)); g = Cold(g, registry);
    }

    private static (GameEngine, ContentRegistry) Start(CardKind kind, int advice = -1, bool forbidden = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(kind, advice, forbidden));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = kind == CardKind.BorrowedSword ? 31 : 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        if (advice >= 0) PrepareAdvice(g, advice);
        Reach(g, advice >= 0 ? p => p?.SkillPrompt?.SkillId == Bizheng : IsPlay); return (g, registry);
    }

    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(PendingDecision? p) => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard };
    private static bool IsContinue(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        Require(P(g)?.PlayerSeat == 0, "A human Answer never impersonates a native AI chooser.");
        var choice = P(g)!.Choices.FirstOrDefault(predicate); Require(choice is not null, "Required published choice is absent: " + Diagnostic(g));
        Accept(g, new AnswerPromptCommand(0, P(g)!.PromptId, choice!.Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision?, bool> stop)
    {
        for (var step = 0; step < 320; step++) { if (stop(P(g))) return; Step(g); }
        throw new InvalidOperationException("Bounded native fixture missed its boundary: " + Diagnostic(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is not { PlayerSeat: 0 }) { Accept(g, new AdvanceOneStepCommand(g.Revision)); return; }
        if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) { Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); return; }
        if (p.Kind is DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash) { Answer(g, c => c.Cards.Count == 0); return; }
        if (p.Kind == DecisionKind.SelectTargetCard) { Answer(g, _ => true); return; }
        if (p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"))
        { Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"); return; }
        throw new InvalidOperationException("Unexpected human boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { Prompt = P(g), g.State.CurrentSeat, g.State.Phase,
        LastFacts = g.Events.TakeLast(12).Select(e => new { e.Sequence, Type = e.Payload.GetType().Name, Payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) }).ToArray() });
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
    private static void Reject(GameEngine g, GameCommand command)
    { var before = State(g); var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(!result.Accepted && result.Error is not null && before == State(g), "Wrong actor, unpublished or stale choice must reject atomically."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Require(State(g) == State(restored), "Cold accepted-command replay preserves all player views, exact owning children, frozen targets and paid movements."); return restored; }
    private static void Frozen<T>(IReadOnlyList<T> list)
    { Require(list.Count > 0 && list is IList<T> { IsReadOnly: true }, "A genuine published nonempty collection is frozen."); var rejected = false; try { ((IList<T>)list)[0] = list[0]; } catch (NotSupportedException) { rejected = true; } Require(rejected, "Published collections reject observer mutation."); }
    private static void Private(GameEngine g, int actor)
    { Require(P(g) is { IsPrivate: true, PlayerSeat: var actual } && actual == actor && Enumerable.Range(0, 4).Where(s => s != actor).All(s => g.CreateSnapshot(s).PendingDecision is null && g.CreateSnapshot(s).Players[actor].Hand.Count == 0), "Only the actual payer sees its private choices and foreign Hand faces remain redacted."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void PrepareAdvice(GameEngine g, int variant)
    {
        // These are genuine Preparation instructions. No hand, HP or equipment state is injected.
        for (var step = 0; step < 160; step++)
        {
            if (Facts<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == Prep && e.BindingId == "prepare" && e.Completed)) return;
            var p = P(g);
            if (p is not { PlayerSeat: 0 }) { Step(g); continue; }
            if (p?.SkillPrompt?.SkillId != Prep) { Step(g); continue; }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"))
                Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            else if (p.Choices.Any(c => c.Targets.Count > 0))
            {
                var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Last();
                var effect = ProgramInstructionResolver.Default.Resolve(frame, g.ContentRegistry.GetSkill(Prep).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
                Answer(g, c => c.Targets.SequenceEqual([effect.TargetKind == SkillProgramTargetKind.AnyLiving ? 0 : 1]));
            }
            else if (p.Choices.Any(c => c.Cards.Count == 1)) Answer(g, c => c.Cards.Count == 1);
            else Answer(g, c => c.Parameters.GetValueOrDefault("program-action") is "finish" or "finish-owned-cards");
        }
        throw new InvalidOperationException("Native preparation did not finish within its fixed bound: " + Diagnostic(g));
    }

    private static void ExerciseAdvice(int variant)
    {
        var (g, registry) = Start(CardKind.Dodge, advice: variant);
        var initial = g.CreateSnapshot(0).Players;
        var ownerHand = initial[0].HandCount; var recipientHand = initial[1].HandCount;
        var armor = initial[0].Equipment.SingleOrDefault()?.Id;
        Require(ownerHand == ((variant & 1) != 0 ? 8 : 0) && recipientHand == ((variant & 2) != 0 ? 8 : 0),
            "Real Preparation payments, rather than injected hand or HP state, establish the four fixed qualification branches.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p?.SkillPrompt?.SkillId == Bizheng && p.Choices.Any(c => c.Targets.Count > 0));
        Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, p => IsContinue(p, DrawChild));
        var parent = Advice(g); var receipt = parent.DrawAdviceReceipt!; var parentId = parent.Id;
        var firstChild = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == DrawChild);
        var firstPrompt = P(g)!.PromptId;
        Require(parent.OwnerSeat == 0 && parent.SkillId == Bizheng && parent.InstructionIndex == 2 &&
                parent.WindowContext is { Window: SkillProgramTriggerWindow.DrawPhaseEnded } context &&
                context.ParentFrameId == receipt.ParentLifecycleFrameId &&
                g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(f => f.Id == receipt.ParentLifecycleFrameId) &&
                receipt.Source == new CardConversionSource(Bizheng, parent.TriggerId!, 0, parent.SkillInstanceId) &&
                receipt.GameplayHash == parent.GameplayHash && receipt.ActualTurnOwnerSeat == 0 && receipt.RecipientSeat == 1 &&
                receipt.DrawActual == 2 && receipt.Stage == DrawAdviceStage.Drawing && !receipt.QualificationsFrozen &&
                Draws(g, receipt).Length == 2 && Facts<DrawAdviceDrawIssuedEvent>(g).Count(e => e.FrameId == parentId && e.ActualCount == 2 &&
                    e.SequenceBefore == receipt.DrawBefore && e.SequenceAfter == receipt.DrawAfter) == 1 &&
                !Facts<DrawAdviceQualifiedEvent>(g).Any(e => e.FrameId == parentId),
            "True DrawPhaseEnded issues two native private entities once and suspends its exact original lifecycle parent before qualifying either participant.");
        AssertAdviceDrawChild(g, parentId); g = Cold(g, registry); Step(g);
        Reach(g, p => IsContinue(p, DrawChild) && p!.PromptId != firstPrompt);
        AssertAdviceDrawChild(g, parentId); var secondChild = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == DrawChild);
        Require(secondChild.Id != firstChild.Id && !Advice(g).DrawAdviceReceipt!.QualificationsFrozen &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == firstChild.Id && e.Completed) == 1,
            "Each one-card draw batch has its own real child; the first child returns once without redrawing or prematurely freezing the threshold.");
        g = Cold(g, registry); Step(g); Reach(g, _ => Facts<DrawAdviceQualifiedEvent>(g).Any(e => e.FrameId == parentId));
        var qualified = Facts<DrawAdviceQualifiedEvent>(g).Single(e => e.FrameId == parentId);
        Require(qualified.OwnerHandCount == ownerHand && qualified.OwnerMaxHp == initial[0].MaxHp &&
                qualified.RecipientHandCount == recipientHand + 2 && qualified.RecipientMaxHp == initial[1].MaxHp &&
                qualified.OwnerMustDiscard == ((variant & 1) != 0) && qualified.RecipientMustDiscard == ((variant & 2) != 0),
            "After both real gain children finish, one fact freezes both public hand/max-HP qualifications for the none, owner, recipient and both branches.");
        var costChildren = new List<long>();
        if (qualified.OwnerMustDiscard) costChildren.Add(PayAdviceParticipant(ref g, registry, parentId, 0, armor));
        if (qualified.RecipientMustDiscard) costChildren.Add(PayAdviceParticipant(ref g, registry, parentId, 1, null));
        Reach(g, IsPlay); g = Cold(g, registry);
        var resolved = Facts<DrawAdviceResolvedEvent>(g).Single(e => e.FrameId == parentId);
        var payments = Facts<DrawAdviceDiscardIssuedEvent>(g).Where(e => e.FrameId == parentId).ToArray();
        Require(resolved.OwnerDiscardActual == (qualified.OwnerMustDiscard ? 2 : 0) &&
                resolved.RecipientDiscardActual == (qualified.RecipientMustDiscard ? 2 : 0) &&
                payments.Select(e => e.DiscardSeat).SequenceEqual(new[] { 0, 1 }.Where(s => s == 0 ? qualified.OwnerMustDiscard : qualified.RecipientMustDiscard)) &&
                payments.All(e => e.ActualCount == 2 && e.CardIds.Count == 2) &&
                costChildren.All(id => Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == id && e.Completed) == 1) &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == parentId && e.Completed) == 1 &&
                Facts<DrawAdviceDrawIssuedEvent>(g).Count(e => e.FrameId == parentId) == 1 &&
                Facts<DrawAdviceQualifiedEvent>(g).Count(e => e.FrameId == parentId) == 1 && Draws(g, receipt).Length == 2 &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == parentId),
            "Owner then recipient pay their own frozen HE cost in text order; all native children return once without requalifying, redrawing or repaying the original DrawEnd instruction.");
        foreach (var payment in payments)
            Require(payment.CardIds.All(id => g.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile &&
                m.Sequence > payment.SequenceBefore && m.Sequence <= payment.SequenceAfter && m.Reason.Value == AdviceDiscardReason) == 1),
                "Each issued participant receipt proves one actual atomic discard per selected physical entity.");
        if (qualified.OwnerMustDiscard)
            Require(armor is { } lion && payments.Single(e => e.DiscardSeat == 0).CardIds.Contains(lion) &&
                    g.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == AdviceDiscardReason) == 1 &&
                    g.CreateSnapshot(0).Players[0].Hp == initial[0].Hp + 1 &&
                    Facts<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1,
                "The owner's real Silver Lion equipment cost heals one HP through its native child, without converting discard-two into hand-only payment.");
    }

    private const string AdviceDrawReason = "skill-program.ol:bizheng.DrawTwoThenDiscardTwoIfOverMaxHp.draw";
    private const string AdviceDiscardReason = "skill-program.ol:bizheng.DrawTwoThenDiscardTwoIfOverMaxHp.discard";
    private static ProgramSkillFrame Advice(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DrawAdviceReceipt is not null);
    private static CardMovementRecord[] Draws(GameEngine g, DrawAdviceReceipt receipt) => g.CardMovements.Where(m => m.From == CardLocation.DrawPile &&
        m.To == CardLocation.Hand(1) && m.Reason.Value == AdviceDrawReason && m.Sequence > receipt.DrawBefore && m.Sequence <= receipt.DrawAfter).ToArray();
    private static void AssertAdviceDrawChild(GameEngine g, long parentId)
    {
        var parent = Advice(g); var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == DrawChild);
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Id == child.WindowContext!.ParentFrameId);
        Require(parent.Id == parentId && child.OwnerSeat == 1 && movement.Batch.ParentFrameId == parentId &&
                movement.Batch.OriginOwnerSeat == 0 && movement.Batch.OriginSkillId == Bizheng && movement.Batch.OriginSkillInstanceId == parent.SkillInstanceId &&
                movement.Batch.Movements is [var draw] && draw.From == CardLocation.DrawPile && draw.To == CardLocation.Hand(1) && draw.Reason.Value == AdviceDrawReason &&
                child.WindowContext!.MovementBatch?.Id == movement.Batch.Id,
            "The recipient's private gain child has the exact original program movement parent, source instance and one-card physical draw batch.");
        Private(g, 1); Frozen(P(g)!.Choices); Frozen(movement.Batch.Movements);
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision));
    }

    private static long PayAdviceParticipant(ref GameEngine g, ContentRegistry registry, long parentId, int seat, int? armor)
    {
        var sawArmorRecovery = false;
        for (var step = 0; step < 96; step++)
        {
            var p = P(g);
            if (IsContinue(p, HpChild)) { sawArmorRecovery |= p!.PlayerSeat == 0; g = Cold(g, registry); Step(g); continue; }
            if (IsContinue(p, CostChild))
            {
                var parent = Advice(g); var r = parent.DrawAdviceReceipt!;
                var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == CostChild);
                var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Id == child.WindowContext!.ParentFrameId);
                var issued = Facts<DrawAdviceDiscardIssuedEvent>(g).Single(e => e.FrameId == parentId && e.DiscardSeat == seat);
                Require(parent.Id == parentId && parent.InstructionIndex == 2 && r.Stage == DrawAdviceStage.Discarding && r.QualificationsFrozen &&
                        r.ParticipantCursor == seat && r.SelectedDiscardCardIds.Count == 2 && r.SelectedSourceLocations.Count == 2 &&
                        issued.CardIds.SequenceEqual(r.SelectedDiscardCardIds) && issued.BatchId == r.LastDiscardBatchId &&
                        issued.SequenceBefore == r.LastDiscardBefore && issued.SequenceAfter == r.LastDiscardAfter &&
                        child.OwnerSeat == seat && movement.Batch.Id == r.LastDiscardBatchId && movement.Batch.ParentFrameId == parentId &&
                        child.WindowContext!.MovementBatch?.Id == movement.Batch.Id &&
                        movement.Batch.Movements.Select(m => m.CardId).SequenceEqual(r.SelectedDiscardCardIds) &&
                        movement.Batch.Movements.All(m => m.From.OwnerSeat == seat && m.To == CardLocation.DiscardPile && m.Reason.Value == AdviceDiscardReason),
                    "A forced HE discard freezes the selected entities and locations on its original receipt, pays one exact native batch, then suspends on that participant's real child.");
                Require(seat != 0 || armor is null || sawArmorRecovery, "The paid Silver Lion's actual recovery child was cold-restored before the mixed HE movement child.");
                Frozen(r.SelectedDiscardCardIds); Frozen(r.SelectedSourceLocations); Frozen(issued.CardIds); Frozen(movement.Batch.Movements); Private(g, seat);
                g = Cold(g, registry); Step(g); return child.Id;
            }
            if (p?.SkillPrompt?.SkillId == Bizheng && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "draw-advice-discard"))
            {
                var receipt = Advice(g).DrawAdviceReceipt!;
                Require(receipt.QualificationsFrozen && receipt.Stage == DrawAdviceStage.ChoosingDiscard && receipt.ParticipantCursor == seat &&
                        receipt.RequiredDiscardCount == 2 && p.PlayerSeat == seat, "Only the actual frozen participant chooses its own two-card HE cost.");
                Private(g, seat); Frozen(p.Choices);
                if (receipt.SelectedDiscardCardIds.Count > 0) { Frozen(receipt.SelectedDiscardCardIds); Frozen(receipt.SelectedSourceLocations); }
                g = Cold(g, registry);
                if (seat == 0)
                {
                    var oldPrompt = P(g)!; var oldRevision = g.Revision;
                    var locations = g.CreateCardZoneDiagnostics().ToDictionary(z => z.CardId, z => z.Location);
                    var chosen = receipt.SelectedDiscardCardIds.Count == 0 && armor is { } equipment ? equipment :
                        P(g)!.Choices.First(c => c.Cards.Count == 1 && locations[c.Cards[0]] == CardLocation.Hand(0)).Cards[0];
                    Reject(g, new AnswerPromptCommand(1, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision));
                    Answer(g, c => c.Cards.SequenceEqual([chosen]));
                    Reject(g, new AnswerPromptCommand(0, oldPrompt.PromptId, oldPrompt.Choices.First().Id, oldRevision));
                }
                else { Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision)); Step(g); }
                continue;
            }
            Step(g);
        }
        throw new InvalidOperationException("Frozen participant payment missed its real child: " + Diagnostic(g));
    }

    private sealed class Fixture(CardKind kind, int advice, bool forbidden) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-draw-advice-designation", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var prep = new List<string>();
            if (advice >= 0)
            {
                if ((advice & 1) == 0) prep.AddRange(["{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"numberExpression\":\"allOwnedZoneCards\",\"zones\":[\"hand\"],\"resultBind\":\"empty-owner\"}",
                    "{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"empty-owner\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}"]);
                if ((advice & 2) == 0) prep.AddRange(["{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"}",
                    "{\"op\":\"selectOwnedCards\",\"target\":\"selectedTarget\",\"numberExpression\":\"allOwnedZoneCards\",\"zones\":[\"hand\"],\"resultBind\":\"empty-recipient\"}",
                    "{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"empty-recipient\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}"]);
                if (prep.Count == 0) prep.Add("{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"ready\",\"options\":[{\"id\":\"continue\"}]}");
            }
            var equipmentPreparation = advice >= 0 && (advice & 1) != 0 ? """
                ,{"id":"equip-preparation","window":"turnStartBeforeNormalFlow","subject":"owner","priority":100,"usageScope":"game","usageLimit":1,"optional":false,"effects":[
                    {"op":"selectTarget","target":"owner","targetKind":"anyLiving"},{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"},{"op":"loseHp","target":"owner","amount":1}]}
                """ : "";
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"viewAs":[
                {"id":"as-fire","inputKinds":["slash"],"inputSuits":[],"outputKind":"fireSlash","forPlay":true,"forResponse":false},
                {"id":"as-thunder","inputKinds":["slash"],"inputSuits":[],"outputKind":"thunderSlash","forPlay":true,"forResponse":false,"usesPerPhase":1},
                {"id":"duel-response","inputKinds":["duel"],"inputSuits":[],"outputKind":"slash","forPlay":false,"forResponse":true}],"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"draw-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"discard-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"resultBind":"gate"},{"op":"moveBoundCards","target":"owner","sourceBind":"gate","destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"{{{Prep}}}","revision":1,"triggers":[{"id":"prepare","window":"turnStartBeforeNormalFlow","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{{{string.Join(',', prep.Count > 0 ? prep : ["{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"ready\",\"options\":[{\"id\":\"continue\"}]}"])}}}]} {{{equipmentPreparation}}} ]},
              {"id":"{{{DrawChild}}}","revision":1,"triggers":[{"id":"actual-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:bizheng.DrawTwoThenDiscardTwoIfOverMaxHp.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-child","options":[{"id":"continue"}]}]}]},
              {"id":"{{{CostChild}}}","revision":1,"triggers":[{"id":"actual-cost","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementDiscardOnly":true,"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.ol:bizheng.DrawTwoThenDiscardTwoIfOverMaxHp.discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-child","options":[{"id":"continue"}]}]}]},
              {"id":"{{{HpChild}}}","revision":1,"triggers":[{"id":"real-hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-child","options":[{"id":"continue"}]}]}]},
              {"id":"{{{CompleteChild}}}","revision":1,"triggers":[{"id":"real-complete","window":"cardUseCompleted","ownerRelation":"observer","singleActionInstance":true,"cardKinds":["slash","fireSlash","thunderSlash","snatch","ironChain","peach","alcohol","borrowedSword","duel"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"complete-child","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:draw-advice-prohibited","revision":1,"cardPolicies":[{"id":"real-prohibition","kind":"prohibitTarget","cardKinds":["slash","fireSlash","thunderSlash","snatch"]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Prep, DrawChild, CostChild, HpChild, CompleteChild, "fixture:draw-advice-prohibited" }.ToDictionary(id => id,
                    id => new { name = id, description = "真实阶段、实体付款与精确目标子窗", optionLabels =
                        id is Driver or "fixture:draw-advice-prohibited" || id == Prep && advice >= 0 && !prep.Any(e => e.Contains("\"op\":\"chooseOption\"", StringComparison.Ordinal))
                            ? new Dictionary<string, string>() : new Dictionary<string, string> { ["continue"] = "继续" } }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "共享机制行为夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:draw-advice-peer", "固定其他角色", "选将对照") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -10000d : 10000d) });
            b.AddGeneral(new(Owner, "实际共享技能拥有者", "supporter", advice >= 0 ? Bizheng : Yidian, "wu", advice >= 0 ? 3 : 12,
                advice >= 0 ? [Prep, Driver, DrawChild, CostChild, HpChild] : [Driver, HpChild, CompleteChild]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:draw-advice-peer-{i}").ToArray();
            for (var i = 0; i < peers.Length; i++) b.AddGeneral(new(peers[i], "真实其他角色", "supporter", "fixture:draw-advice-peer", "wei", advice >= 0 ? 3 : 12,
                forbidden && i == 2 ? ["fixture:draw-advice-prohibited", DrawChild, CostChild, HpChild] : [DrawChild, CostChild, HpChild]));
            var definition = kind switch { CardKind.Slash => "standard:slash", CardKind.Dodge => "standard:dodge", CardKind.Peach => "standard:peach",
                CardKind.Alcohol => "standard:alcohol", CardKind.Snatch => "standard:snatch", CardKind.IronChain => "standard:iron_chain",
                CardKind.BorrowedSword => "classic:borrowed-sword", CardKind.Indulgence => "standard:indulgence", CardKind.Crossbow => "standard:crossbow",
                CardKind.Duel => "standard:duel", _ => throw new InvalidOperationException("Fixture card has no native definition.") };
            // The Borrowed Sword fixture has no Slash or counterspell, so both native weapon-transfer branches execute.
            b.AddDeck(new("fixture:draw-advice-deck", "固定实体小牌库", advice >= 0 ? 8 : 4, 0, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(
                advice >= 0 ? (i % 3 == 0 ? "classic:silver-lion" : "standard:dodge") : kind == CardKind.BorrowedSword ? (i % 3 == 0 ? "standard:crossbow" : i % 3 == 1 ? definition : "standard:dodge") : definition, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "真实摸牌结束与指定目标扩展", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:draw-advice-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
