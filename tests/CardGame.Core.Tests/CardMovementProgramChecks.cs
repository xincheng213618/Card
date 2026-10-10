using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CardMovementProgramChecks
{
    private const int HumanSeat = 0;


    public static void AtomicBatchRunsPerCardAndPerBatchAndReplays()
    {
        var registry = Registry();
        var game = CreateAndSelect(registry, ScenarioPackage.ModeId, ScenarioPackage.OwnerGeneralId);
        ReachHumanPlay(game);
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var hand = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand;
        Require(hand.Count == 2, "The movement fixture must begin Play with exactly two hand cards.");
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == ScenarioPackage.SkillId &&
            candidate.ProgramActivationId == "discard-two");
        var used = game.Submit(new UseProgramSkillCommand(
            HumanSeat,
            action.ProgramSkillId!,
            action.ProgramActivationId!,
            hand.Select(card => card.Id).Order().ToArray(),
            [],
            game.Revision,
            play.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "The movement fixture activation was rejected.");

        var prompt = RequireSkillPrompt(game, ScenarioPackage.SkillId);
        var frame = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(item => item.TriggerId is null);
        var payment = parent.SelectedCardPayment ??
            throw new InvalidOperationException("The selected-card parent lost its payment receipt.");
        Require(payment is { Operation: SkillProgramEffectOp.DiscardSelected, MovementCommitted: true } &&
                payment.InstructionIndex == parent.InstructionIndex &&
                payment.CardIds.SequenceEqual(hand.Select(card => card.Id).Order()) &&
                payment.ActiveChildFrameId == frame.Id &&
                parent.SelectedCardPaymentResult is null && parent.PendingMovementContinuation is null,
            "The selected cards must have one parent-owned paid receipt while movement triggers run.");
        var handCount = frame.Batch.SourceCounts.Single(item => item.Location == CardLocation.Hand(HumanSeat));
        Require(frame.Batch.Movements.Count == 2 &&
                frame.Batch.Movements.All(item => item.From == CardLocation.Hand(HumanSeat)) &&
                handCount is { CountBefore: 2, CountAfter: 0 } &&
                frame.Batch.ParentFrameId is not null && frame.Batch.ParentBatchId is null &&
                frame.Candidates.Count == 3 &&
                frame.Candidates.Take(2).All(item => item.BindingId == "per-card") &&
                frame.Candidates.Take(2).Select(item => item.OccurrenceIndex).SequenceEqual([0, 1]) &&
                frame.Candidates[2] is { BindingId: "empty-batch", OccurrenceIndex: 0 } &&
                prompt.Choices.Count == 2,
            "One physical MoveCards call must freeze one two-card batch with two per-card and one per-batch candidates.");

        var serialized = JsonSerializer.Serialize(game.ResolutionStack);
        var roundTrip = JsonSerializer.Deserialize<ResolutionFrame[]>(serialized) ?? [];
        var restoredFrame = roundTrip.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault() ??
            throw new InvalidOperationException("The cards-moved frame lost its concrete type after serialization.");
        var restoredParent = roundTrip.OfType<ProgramSkillFrame>().Single(item => item.TriggerId is null);
        Require(restoredParent.SelectedCardPayment is { } restoredPayment &&
                restoredPayment.InstructionIndex == payment.InstructionIndex &&
                restoredPayment.Operation == payment.Operation &&
                restoredPayment.CardIds.SequenceEqual(payment.CardIds) &&
                restoredPayment.RecipientSeat == payment.RecipientSeat &&
                restoredPayment.MovementCommitted &&
                restoredPayment.ActiveChildFrameId == frame.Id &&
                restoredParent.SelectedCardPaymentResult is null &&
                restoredParent.PendingMovementContinuation is null,
            "A paused parent must round-trip its paid instruction and active child identity.");
        Require(restoredFrame.Id == frame.Id && restoredFrame.CandidateIndex == frame.CandidateIndex &&
                restoredFrame.Step == frame.Step && restoredFrame.Batch.Id == frame.Batch.Id &&
                restoredFrame.Batch.ParentFrameId == frame.Batch.ParentFrameId &&
                restoredFrame.Batch.ParentBatchId == frame.Batch.ParentBatchId &&
                restoredFrame.Batch.TurnNumber == frame.Batch.TurnNumber,
            "The cards-moved frame lost scalar parent, batch or cursor data after serialization.");
        Require(restoredFrame.Batch.Movements.SequenceEqual(frame.Batch.Movements),
            "The cards-moved frame lost physical movement records after serialization: " +
            $"expected={JsonSerializer.Serialize(frame.Batch.Movements)}, " +
            $"actual={JsonSerializer.Serialize(restoredFrame.Batch.Movements)}.");
        Require(restoredFrame.Batch.SourceCounts.SequenceEqual(frame.Batch.SourceCounts),
            "The cards-moved frame lost frozen source counts after serialization.");
        Require(restoredFrame.Candidates.SequenceEqual(frame.Candidates),
            "The cards-moved frame lost frozen candidates after serialization.");
        Require(JsonSerializer.Serialize(roundTrip) == serialized,
            "The cards-moved frame JSON must be stable after one round trip.");

        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(checkpoint, registry);
        for (var index = 0; index < 3; index++)
        {
            AnswerProgram(game, "activate");
            AnswerProgram(replay, "activate");
        }
        Require(game.ResolutionStack.Count == 0 && game.PendingDecision is null &&
                replay.ResolutionStack.Count == 0 && replay.PendingDecision is null,
            "The last movement trigger must return to a clean Play-phase continuation boundary.");
        AdvanceOne(game);
        AdvanceOne(replay);

        var completed = game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
            .Where(item => item.SkillId == ScenarioPackage.SkillId &&
                           item.Window == SkillProgramTriggerWindow.CardsMoved)
            .ToArray();
        Require(completed.Length == 3 && completed.All(item => item is { Activated: true, Completed: true }) &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count == 3 &&
                game.CardMovements.Count(item => item.To == CardLocation.Hand(HumanSeat) &&
                    item.Reason.Value.StartsWith(
                        $"skill-program.{ScenarioPackage.SkillId}.", StringComparison.Ordinal)) == 3 &&
                game.ResolutionStack.Count == 0 &&
                game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat },
            "The two per-card candidates and one empty-batch candidate must each execute once and resume Play: " +
            $"resolved={completed.Length}, completed={completed.Count(item => item.Completed)}, " +
            $"hand={game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count}, " +
            $"drawMoves={game.CardMovements.Count(item => item.To == CardLocation.Hand(HumanSeat) && item.Reason.Value.StartsWith($"skill-program.{ScenarioPackage.SkillId}.", StringComparison.Ordinal))}, " +
            $"stack={game.ResolutionStack.Count}, prompt={game.PendingDecision?.Kind.ToString() ?? "none"}.");
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "A checkpoint paused at the first card-movement prompt must replay to identical state and events.");

        var invalidated = GameReplay.Restore(checkpoint, registry);
        var owner = Players(invalidated)[HumanSeat];
        foreach (var grant in owner.SkillGrants.Grants
                     .Where(grant => grant.SkillId == ScenarioPackage.SkillId).ToArray())
            owner.SkillGrants.SetEnabled(grant.GrantId, false);
        AnswerProgram(invalidated, "activate");
        if (invalidated.PendingDecision is null) AdvanceOne(invalidated);
        var skipped = invalidated.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
            .Where(item => item.SkillId == ScenarioPackage.SkillId &&
                           item.Window == SkillProgramTriggerWindow.CardsMoved)
            .ToArray();
        Require(skipped.Length == 3 && skipped.All(item => item is { Activated: false, Completed: false }) &&
                invalidated.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count == 0 &&
                invalidated.ResolutionStack.Count == 0 &&
                invalidated.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat },
            "A skill instance lost while prompted must skip every frozen remaining occurrence without drawing.");

        VerifyArmorPaymentReturnsAfterRecoveryAndMovementWindows();
        VerifySlashPaymentReturnsBeforeItsEffect();
    }

    private static void VerifySlashPaymentReturnsBeforeItsEffect()
    {
        var program = SkillProgramCatalog.Load("""
            {"schemaVersion":62,"skills":[{"id":"fixture:card-movement","revision":1,
             "modifiers":[{"id":"payment-quota","query":"slashLimit","operation":"add","value":2,"priority":0}],
             "viewAs":[{"id":"top-cost","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],
               "outputKind":"fireSlash","forPlay":true,"forResponse":false,"useOnly":true,"costDestination":"drawPileTop"}],
             "triggers":[{"id":"paid-owner-batch","window":"cardsMoved","subject":"owner",
              "sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","optional":true,
              "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """, Presentation).Programs[ScenarioPackage.SkillId];
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new ScenarioPackage(program, slashPayment: true));
        foreach (var (materialCount, alternative) in new[] { (1, false), (2, false), (1, true) })
        {
            var game = CreateAndSelect(registry, ScenarioPackage.ModeId, ScenarioPackage.OwnerGeneralId);
            ReachHumanPlay(game);
            var prompt = RequirePrompt(game, DecisionKind.PlayCard);
            var cards = game.CreateSnapshot(HumanSeat).Players[HumanSeat].Hand.Take(materialCount).Select(card => card.Id).Order().ToArray();
            var result = alternative ? PlayAlternative(game, cards[0]) : materialCount == 1
                    ? game.Submit(new PlayCardCommand(HumanSeat, cards[0], [1], game.Revision, prompt.PromptId))
                    : game.Submit(new UseProgramSkillCommand(HumanSeat, "classic:fuhun", "two-hand-cards-as-slash",
                        cards, [1], game.Revision, prompt.PromptId));
            Require(result.Accepted, result.Error?.Message ?? "The physical Slash payment was rejected.");
            RequireSkillPrompt(game, ScenarioPackage.SkillId);
            var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
            var use = game.ResolutionStack.OfType<CardUseFrame>().Single();
            Require(use.RecoveryPaidContinuation?.Kind == RecoveryPaidCardUseKind.CommittedSlash &&
                    movement.ResumePaidCardUseFrameId == use.Id && movement.Batch.ParentFrameId == use.Id &&
                    movement.Batch.AwaitingProgramFrameId is null && movement.Batch.Movements.Count == materialCount &&
                    movement.Batch.Movements.Select(move => move.CardId).SequenceEqual(cards) &&
                    movement.Batch.Movements.All(move => move.From == CardLocation.Hand(HumanSeat) &&
                        move.To == (alternative ? CardLocation.DrawPile : CardLocation.Processing) && move.Reason == CardMoveReasons.Use) &&
                    use.Action!.PhysicalCards.Select(cost => cost.CardId).SequenceEqual(cards) &&
                    !game.Events.Any(item => item.Payload is DamageRequestedEvent),
                "One accepted Slash pays one exact physical owner batch and suspends its own Use before requesting damage.");

            VerifyPaidSlashBatchIssuance(game, use, movement);

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)) &&
                    JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(replay.ResolutionStack),
                "A Slash paused on its paid movement child restores its exact issued receipt, owning action and public journal.");
            foreach (var current in new[] { game, replay })
            {
                AnswerProgram(current, "activate");
                ReachHumanPlay(current);
                Require(cards.All(id => current.CardMovements.Count(move => move.CardId == id &&
                            move.From == CardLocation.Hand(HumanSeat) && move.To == (alternative ? CardLocation.DrawPile : CardLocation.Processing)) == 1 &&
                        current.CardMovements.Count(move => move.CardId == id &&
                            move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == (alternative ? 0 : 1)) &&
                        current.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Count(item =>
                            item.SourceSeat == HumanSeat && item.TargetSeat == 1 && item.Amount == 1) == 1 &&
                        current.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Count(item =>
                            item.SkillId == ScenarioPackage.SkillId && item.Activated && item.Completed) == 1,
                    "Payment observers resume the original Slash once without duplicating its materials, damage or cleanup.");
            }
            Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
                "Single- and multi-material Slash payments retain identical cold-replay returns after their observers.");

            if (alternative)
            {
                Require(game.CreateSnapshot(HumanSeat).Players[HumanSeat].Hand.Any(card => card.Id == cards[0]),
                    "The payment observer really draws the same top-deck cost back before its next use.");
                Require(PlayAlternative(game, cards[0]).Accepted, "The recycled alternative material can pay a second real Slash.");
                var nextMovement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
                var nextUse = game.ResolutionStack.OfType<CardUseFrame>().Single();
                VerifyPaidSlashBatchIssuance(game, nextUse, nextMovement, movement.Batch);
                var secondReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                Require(JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(secondReplay.ResolutionStack),
                    "The same recycled physical card retains a different exact issued batch on its second cold-restored use.");
                foreach (var current in new[] { game, secondReplay }) { AnswerProgram(current, "activate"); ReachHumanPlay(current); }
                Require(State(game) == State(secondReplay) && Events(game).SequenceEqual(Events(secondReplay)) &&
                        game.CardMovements.Count(move => move.CardId == cards[0] && move.From == CardLocation.Hand(HumanSeat) &&
                            move.To == CardLocation.DrawPile && move.Reason == CardMoveReasons.Use) == 2,
                    "Historical-batch rejection preserves both real payments and exact cold replay without repaying either use.");
            }
        }

        CommandResult PlayAlternative(GameEngine current, int cardId)
        {
            var legal = current.GetHumanLegalActions().First(action => action.CardId == cardId &&
                action.PlayedCardKind == CardKind.FireSlash && action.ConversionSource?.SkillId == ScenarioPackage.SkillId && action.TargetSeats.Contains(1));
            return current.Submit(new PlayCardCommand(HumanSeat, cardId, [1], current.Revision,
                current.PendingDecision!.PromptId, CardKind.FireSlash) { ConversionSource = legal.ConversionSource });
        }
    }

    private static void VerifyPaidSlashBatchIssuance(GameEngine game, CardUseFrame use,
        CardsMovedTriggerWindowFrame movement, CardMovementBatchContext? historical = null)
    {
        // This is a trusted-host corruption check of the final invariant. The
        // material movements remain genuine; only the unissued batch identity
        // and every frozen copy of that identity change together.
        var store = typeof(GameEngine).GetField("_resolutionStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var complete = store.GetType().GetMethod("CompleteTop")!;
        var push = store.GetType().GetMethod("Push")!;
        var replace = store.GetType().GetMethod("Replace")!;
        var assert = typeof(GameEngine).GetMethod("AssertCoreInvariants", BindingFlags.Instance | BindingFlags.NonPublic)!;
        assert.Invoke(game, null);
        Require(game.ResolutionStack.Last().Id == movement.Id, "The issuance negative check needs the actual unpaid observer chooser.");
        var receipt = use.PaymentMovementReceipt ?? throw new InvalidOperationException("A real paid Slash lost its frame-owned batch issuance receipt.");
        Require(receipt.OwnerFrameId == use.Id && receipt.ActionId == use.Action!.ActionId && receipt.Batch.Id == movement.Id &&
                receipt.Batch.Movements.SequenceEqual(movement.Batch.Movements) &&
                receipt.SequenceBefore == movement.Batch.Movements[0].Sequence - 1 && receipt.SequenceAfter == movement.Batch.Movements[^1].Sequence &&
                !ReferenceEquals(receipt.Batch.Movements, movement.Batch.Movements) &&
                receipt.Batch.Movements is System.Collections.IList { IsReadOnly: true } &&
                receipt.Batch.SourceCounts is System.Collections.IList { IsReadOnly: true } &&
                receipt.Batch.DestinationCounts is System.Collections.IList { IsReadOnly: true },
            "Issuance freezes the exact physical batch, owning Use/Action, sequence range and nested collection copies.");
        var foreignId = game.ResolutionStack.Max(frame => frame.Id) + 100;
        Reject(use, Rebatch(movement.Batch with { Id = foreignId }));
        foreach (var invalid in new[] { receipt with { OwnerFrameId = use.Id + 100 }, receipt with { ActionId = receipt.ActionId + 100 },
                     receipt with { SequenceBefore = receipt.SequenceBefore - 1 }, receipt with { SequenceAfter = receipt.SequenceAfter + 1 } })
            Reject(use with { PaymentMovementReceipt = invalid }, movement);
        Reject(use with { PaymentMovementReceipt = null }, movement);
        var action = use.Action!;
        var foreignAction = new CardActionContext(action.ActionId + 100, action.ParentActionId, action.Type, action.ActorSeat,
            action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind,
            action.TargetSeats, action.PhysicalCards, action.ConversionChain, action.DesignatedTargetSeats,
            action.EffectiveSuit, action.EffectiveRank, action.EffectiveIsRed, action.FactionOrigin);
        Reject(use with { Action = foreignAction }, movement);
        Reject(use, movement with { ResumeDeclarationFrameId = use.Id });
        Reject(use, Rebatch(movement.Batch with { OriginSkillInstanceId = "foreign-payment-origin" }));
        Reject(use, Rebatch(movement.Batch with { SourceCounts = movement.Batch.SourceCounts.Select((count, index) =>
            index == 0 ? count with { CountBefore = count.CountBefore + 1 } : count).ToArray() }));
        if (historical is not null)
        {
            var borrowed = historical with { ParentFrameId = use.Id };
            Require(historical.Id != movement.Id && historical.Movements.All(old => game.CardMovements[old.Sequence - 1] == old) &&
                    (bool)typeof(GameEngine).GetMethod("CardUsePaymentMaterialsMatch", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(game, [use, borrowed])!,
                "The historical negative uses genuine earlier payment records of the same recycled physical material that still pass the original material/ledger checks.");
            Reject(use, Rebatch(borrowed));
        }

        var conversionGrants = action.ConversionChain.SelectMany(source => Players(game)[source.OwnerSeat].SkillGrants.Grants
            .Where(grant => grant.SkillInstanceId == source.SkillInstanceId && grant.IsEnabled)
            .Select(grant => (source.OwnerSeat, Grant: grant))).ToArray();
        Require(action.ConversionChain.Count == 0 || conversionGrants.Length > 0,
            "Converted payment source-loss verification must disable an actual granted producer instance.");
        foreach (var item in conversionGrants) Players(game)[item.OwnerSeat].SkillGrants.SetEnabled(item.Grant.GrantId, false);
        try { assert.Invoke(game, null); }
        finally { foreach (var item in conversionGrants) Players(game)[item.OwnerSeat].SkillGrants.SetEnabled(item.Grant.GrantId, true); }
        assert.Invoke(game, null);

        CardsMovedTriggerWindowFrame Rebatch(CardMovementBatchContext batch) => movement with { Id = batch.Id, Batch = batch,
            Contexts = movement.Contexts!.Select(context => context with { ParentFrameId = batch.Id, MovementBatch = batch }).ToArray() };

        void Reject(CardUseFrame owner, CardsMovedTriggerWindowFrame forged)
        {
            replace.Invoke(store, [owner]);
            complete.Invoke(store, [movement.Id, movement.Kind]);
            push.Invoke(store, [forged]);
            var rejected = false;
            try { assert.Invoke(game, null); }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException invalid &&
                invalid.Message.Contains("card-use payment", StringComparison.Ordinal)) { rejected = true; }
            finally
            {
                complete.Invoke(store, [forged.Id, forged.Kind]);
                push.Invoke(store, [movement]);
                replace.Invoke(store, [use]);
            }
            Require(rejected, "A real paid Slash must reject foreign issuance, action, sequence, batch facts and conflicting returns in the final invariant even when frozen contexts and genuine physical ledger records agree.");
        }
    }

    public static void SelectedGiftContinuesAfterRecipientDeathAndReplays()
    {
        var rules = $$$$"""
            {"schemaVersion":{{{{SkillProgramCatalog.RulesSchemaVersion}}}},"skills":[
             {"id":"fixture:gift-survives-recipient","revision":1,"activations":[
              {"id":"gift","minCards":2,"maxCards":2,"minTargets":1,"maxTargets":1,
               "targetKind":"otherLiving","usesPerTurn":1,"effects":[
                {"op":"giveSelected","target":"selectedTarget","amount":2},
                {"op":"draw","target":"owner","amount":1}]}]},
             {"id":"fixture:gift-recipient-loss","revision":1,"triggers":[
              {"id":"loss","window":"cardsGained","subject":"owner","destinationZones":["hand"],
               "movementOccurrence":"perSourceOwner","optional":false,
               "condition":{"kind":"compare","left":{"kind":"movedCardCount"},
                "operator":"greaterThanOrEqual","right":{"kind":"integerConstant","value":2}},
               "effects":[{"op":"loseHp","target":"owner","amount":1}]}]}]}
            """;
        var presentation = $$$$"""
            {"schemaVersion":{{{{SkillProgramCatalog.PresentationSchemaVersion}}}},"skills":{
             "fixture:gift-survives-recipient":{"name":"Gift","description":"Fixture"},
             "fixture:gift-recipient-loss":{"name":"Loss","description":"Fixture"}}}
            """;
        var programs = SkillProgramCatalog.Load(rules, presentation).Programs;
        var registry = ContentRegistry.Build(new StandardContentPackage(), new RecipientDeathPackage(programs));
        var game = CreateAndSelect(registry, RecipientDeathPackage.ModeId, RecipientDeathPackage.OwnerGeneralId);
        ReachHumanPlay(game);
        var target = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat != HumanSeat && player.Role == Role.Loyalist).Seat;
        var cards = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Select(card => card.Id).Order().ToArray();
        Require(cards.Length == 2 && Players(game)[target].Hp == 1, $"The gift fixture must have two costs and a one-HP nonterminal recipient: cards={cards.Length}, target={target}, hp={Players(game)[target].Hp}, general={Players(game)[target].General.Id}, role={Players(game)[target].Role}.");
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Finish(game);
        Finish(replay);
        Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            "Recipient death during a paid gift must checkpoint-replay identical state and events.");

        void Finish(GameEngine engine)
        {
            var play = RequirePrompt(engine, DecisionKind.PlayCard);
            var result = engine.Submit(new UseProgramSkillCommand(HumanSeat, RecipientDeathPackage.GiftSkill,
                "gift", cards, [target], engine.Revision, play.PromptId));
            Require(result.Accepted, result.Error?.Message ?? "The gift was rejected.");
            for (var step = 0; step < 64 && engine.ResolutionStack.Count > 0; step++)
            {
                if (engine.PendingDecision is { } prompt)
                {
                    Require(prompt.Kind == DecisionKind.RescueDying, "The mandatory recipient loss must only pause for rescue.");
                    var none = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("response") == "let-die");
                    Require(engine.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, none.Id, engine.Revision)).Accepted,
                        "The no-rescue answer was rejected.");
                }
                else AdvanceOne(engine);
            }
            var snapshot = engine.CreateSnapshot(HumanSeat, revealAll: true);
            Require(!snapshot.Players[target].IsAlive && snapshot.Winner == Winner.None &&
                    snapshot.Players[HumanSeat].HandCount == 1 && engine.ResolutionStack.Count == 0,
                "A dead gift recipient must not cancel the living owner's one subsequent draw.");
            Require(cards.All(id => engine.CardMovements.Count(move => move.CardId == id &&
                        move.From == CardLocation.Hand(HumanSeat) && move.To == CardLocation.Hand(target)) == 1) &&
                    engine.CardMovements.Count(move => move.To == CardLocation.Hand(HumanSeat) &&
                        move.Reason.Value == $"skill-program.{RecipientDeathPackage.GiftSkill}.Draw") == 1,
                "The selected gift must pay once and execute its following draw exactly once.");
        }
    }

    private sealed class RecipientDeathPackage(IReadOnlyDictionary<string, SkillProgram> programs) : IGameContentPackage
    {
        public const string GiftSkill = "fixture:gift-survives-recipient";
        public const string OwnerGeneralId = "fixture:gift-survives-recipient-owner";
        public const string ModeId = "identity:classic-gift-recipient-death-5";
        public PackageManifest Manifest { get; } = new("gift-recipient-death", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, program) in programs)
                builder.AddSkill(new ContentSkillDefinition(id, id, "Fixture")
                { Program = program, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.Active });
            builder.AddGeneral(new ContentGeneralDefinition(OwnerGeneralId, "Giver", "supporter", GiftSkill, "wei", BaseHp: 4));
            var others = Enumerable.Range(1, 4).Select(index => $"fixture:gift-recipient-{index}").ToArray();
            foreach (var id in others)
                builder.AddGeneral(new ContentGeneralDefinition(id, "Recipient", "supporter", "fixture:gift-recipient-loss", "wei", BaseHp: 1));
            builder.AddDeck(new ContentDeckRecipe("fixture:gift-recipient-deck", "No rescue", 2, 0,
                [new ContentDeckCardCount("standard:crossbow", 64)]));
            builder.AddMode(new ContentModeDefinition(ModeId, "Gift recipient death", 5, 5,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }, "fixture:gift-recipient-deck",
                GeneralCandidateCount: 5, GeneralPoolIds: [OwnerGeneralId, .. others]));
        }
    }
    private static void VerifyArmorPaymentReturnsAfterRecoveryAndMovementWindows()
    {
        var rules = $$$$"""
            {"schemaVersion":{{{{SkillProgramCatalog.RulesSchemaVersion}}}},"skills":[{
              "id":"fixture:armor-payment","revision":1,
              "minimumRulesVersion":{{{{GameCheckpoint.CurrentRulesVersion}}}},
              "activations":[{"id":"discard-armor","minCards":1,"maxCards":1,
                "sourceZones":["equipment"],"minTargets":0,"maxTargets":0,
                "targetKind":"anyLiving","usesPerTurn":1,
                "effects":[{"op":"discardSelected","target":"owner","amount":1},
                           {"op":"draw","target":"owner","amount":1}]}],
              "triggers":[{"id":"recover","window":"afterHpRecovered","subject":"owner",
                "optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"armor-moved","window":"cardsMoved","subject":"owner",
                "sourceZones":["equipment"],"movementOccurrence":"perCard",
                "optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]
            }]}
            """;
        var presentation = $$$$"""
            {"schemaVersion":{{{{SkillProgramCatalog.PresentationSchemaVersion}}}},
             "skills":{"fixture:armor-payment":{"name":"Armor payment","description":"Fixture"}}}
            """;
        var program = SkillProgramCatalog.Load(rules, presentation).Programs[ArmorPaymentPackage.SkillId];
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new ArmorPaymentPackage(program));
        var game = CreateAndSelect(registry, ArmorPaymentPackage.ModeId, ArmorPaymentPackage.OwnerGeneralId);
        ReachHumanPlay(game);

        var store = (CardZoneStore)(typeof(GameEngine)
            .GetField("_cardZones", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The armor payment card store is unavailable."));
        var armor = game.CreateCardZoneDiagnostics().Single(card => card.CardKind == CardKind.SilverLion);
        _ = store.Move(armor.CardId, armor.Location, CardLocation.Equipment(HumanSeat));
        var owner = Players(game)[HumanSeat];
        owner.Hp = owner.MaxHp - 1;
        var handBefore = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].HandCount;
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var action = game.GetHumanLegalActions().Single(item =>
            item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == ArmorPaymentPackage.SkillId &&
            item.ProgramActivationId == "discard-armor");
        var result = game.Submit(new UseProgramSkillCommand(HumanSeat, ArmorPaymentPackage.SkillId,
            "discard-armor", [armor.CardId], [], game.Revision, play.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "The armor payment was rejected.");

        var hpWindow = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(item => item.TriggerId is null);
        Require(hpWindow.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                parent.SelectedCardPayment is
                { Operation: SkillProgramEffectOp.DiscardSelected, MovementCommitted: true } payment &&
                payment.ActiveChildFrameId == hpWindow.Id && parent.PendingMovementContinuation is null,
            "Silver Lion recovery must pause the paid parent before the movement trigger window.");
        var hpFrames = JsonSerializer.Deserialize<ResolutionFrame[]>(JsonSerializer.Serialize(game.ResolutionStack)) ?? [];
        var restoredParent = hpFrames.OfType<ProgramSkillFrame>().Single(item => item.TriggerId is null);
        Require(restoredParent.SelectedCardPayment?.ActiveChildFrameId == hpWindow.Id &&
                restoredParent.SelectedCardPaymentResult is null,
            "The HP child and parent's paid receipt must round-trip together.");
        AnswerProgram(game, "activate");
        var movementWindow = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(item => item.TriggerId is null);
        Require(parent.SelectedCardPayment?.ActiveChildFrameId == movementWindow.Id &&
                parent.SelectedCardPaymentResult is null,
            "The movement window must become the paid parent's current child after recovery.");
        AnswerProgram(game, "activate");
        var after = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];
        Require(after.Hp == after.MaxHp && after.Equipment.All(card => card.Id != armor.CardId) &&
                game.CreateCardZoneDiagnostics().Single(card => card.CardId == armor.CardId).Location ==
                    CardLocation.DiscardPile &&
                after.HandCount == handBefore + 3 && game.ResolutionStack.Count == 0 &&
                game.PendingDecision is null,
            "Recovery, movement trigger, and the parent's next effect must each finish once after armor payment.");
    }

    public static void NestedBatchesRetainImmediateParentIdentity()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new NestedScenarioPackage());
        var game = CreateAndSelect(registry, NestedScenarioPackage.ModeId, NestedScenarioPackage.OwnerGeneralId);
        ReachHumanPlay(game);

        var store = (CardZoneStore)(typeof(GameEngine)
            .GetField("_cardZones", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The movement fixture card store is unavailable."));
        var diagnostics = game.CreateCardZoneDiagnostics();
        var oxDiagnostic = diagnostics.Single(item => item.CardKind == CardKind.WoodenOx);
        var grainDiagnostic = diagnostics.First(item => item.CardKind == CardKind.Crossbow &&
            item.CardId != oxDiagnostic.CardId);
        var ox = store.CardsAt(oxDiagnostic.Location).Single(card => card.Id == oxDiagnostic.CardId);
        _ = store.Move(ox.Id, oxDiagnostic.Location, CardLocation.Equipment(HumanSeat));
        _ = store.Move(grainDiagnostic.CardId, grainDiagnostic.Location, CardLocation.WoodenOxGrain(HumanSeat));

        var moveCard = typeof(GameEngine).GetMethod(
            "MoveCard", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The movement fixture could not find MoveCard.");
        _ = moveCard.Invoke(game,
            [ox, CardLocation.Equipment(HumanSeat), CardLocation.Equipment(1), CardMoveReasons.WoodenOxTransfer, null, true]);

        var pending = (IReadOnlyList<CardMovementBatchContext>)(typeof(GameEngine)
            .GetField("_pendingCardsMovedBatches", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The movement batch queue is unavailable."));
        var outer = pending.Single(batch => batch.Movements.Any(item => item.CardId == ox.Id));
        var child = pending.Single(batch => batch.Movements.Any(item => item.CardId == grainDiagnostic.CardId));
        Require(outer.ParentBatchId is null && child.ParentBatchId == outer.Id && child.Id > outer.Id &&
                outer.SourceCounts.Single(item => item.Location == CardLocation.Equipment(HumanSeat)) is
                    { CountBefore: 1, CountAfter: 0 } &&
                child.SourceCounts.Single(item => item.Location == CardLocation.WoodenOxGrain(HumanSeat)) is
                    { CountBefore: 1, CountAfter: 0 } &&
                store.GetLocation(grainDiagnostic.CardId) == CardLocation.WoodenOxGrain(1),
            "A Wooden Ox hook must retain a distinct child batch linked to its immediate outer movement batch.");
    }

    private static ContentRegistry Registry()
    {
        var program = SkillProgramCatalog.Load(Rules, Presentation).Programs[ScenarioPackage.SkillId];
        return ContentRegistry.Build(new StandardContentPackage(), new ScenarioPackage(program));
    }

    private static GameEngine CreateAndSelect(ContentRegistry registry, string modeId, string generalId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 140119,
            PlayerCount = 5,
            ModeId = modeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The movement fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(generalId), "The movement fixture did not offer its owner.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat, generalId, game.Revision, selection.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The movement fixture could not select its owner.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Play.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The movement fixture could not advance.");
        }
        throw new InvalidOperationException("The movement fixture did not reach human Play.");
    }

    private static void AdvanceOne(GameEngine game)
    {
        var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(advanced.Accepted, advanced.Error?.Message ?? "The movement fixture could not advance.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static PendingDecision RequireSkillPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
            { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat, SkillPrompt.SkillId: var actual } prompt &&
        actual == skillId
            ? prompt
            : throw new InvalidOperationException(
                $"Expected ProgramTrigger for {skillId}, found {game.PendingDecision?.Kind} / " +
                $"{game.PendingDecision?.SkillPrompt?.SkillId}.");

    private static void AnswerProgram(GameEngine game, string action)
    {
        var prompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        var choice = prompt.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("program-action") == action);
        var answered = game.Submit(new AnswerPromptCommand(
            HumanSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "The movement program answer was rejected.");
    }

    private static IReadOnlyList<CharacterState> Players(GameEngine game) =>
        (IReadOnlyList<CharacterState>)(typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The movement fixture players are unavailable."));

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|" +
                        JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))
        .ToArray();

    private static void Reject(string rules, string expectedMessage) =>
        Reject(rules, Presentation, expectedMessage);

    private static void Reject(string rules, string presentation, string expectedMessage)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException(
            $"The invalid movement program was expected to mention '{expectedMessage}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string Rules = """
        {"schemaVersion":62,"skills":[{"id":"fixture:card-movement","revision":1,
        "minimumRulesVersion": 171,"modifiers":[],"viewAs":[],
        "activations":[{"id":"discard-two","minCards":2,"maxCards":2,"minTargets":0,
        "maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
        "effects":[{"op":"discardSelected","target":"owner","amount":2}]}],
        "triggers":[
        {"id":"per-card","window":"cardsMoved","subject":"owner","sourceZones":["hand"],
        "movementOccurrence":"perCard","optional":true,"priority":100,
        "effects":[{"op":"draw","target":"owner","amount":1}]},
        {"id":"empty-batch","window":"cardsMoved","subject":"owner","sourceZones":["hand"],
        "movementOccurrence":"perBatch","optional":true,"priority":0,
        "condition":{"kind":"all","children":[
        {"kind":"compare","left":{"kind":"sourceZoneCountBefore"},"operator":"greaterThan",
        "right":{"kind":"integerConstant","value":0}},
        {"kind":"compare","left":{"kind":"sourceZoneCountAfter"},"operator":"equal",
        "right":{"kind":"integerConstant","value":0}}]},
        "effects":[{"op":"draw","target":"owner","amount":1}]}],
        "contributions":[],"cardIdentities":[]}]}
        """;

    private const string Presentation =
        "{\"schemaVersion\":3,\"skills\":{\"fixture:card-movement\":{\"name\":\"Movement\",\"description\":\"Fixture\"}}}";

    private sealed class ScenarioPackage(SkillProgram program, bool slashPayment = false) : IGameContentPackage
    {
        public const string SkillId = "fixture:card-movement";
        public const string OwnerGeneralId = "fixture:card-movement-owner";
        public const string ModeId = "identity:card-movement-test-5";
        private const string DeckId = "fixture:card-movement-deck";

        public PackageManifest Manifest { get; } = new("card-movement-scenario", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new ContentSkillDefinition(SkillId, "Movement", "Fixture")
            {
                Program = program,
                ExecutionForms = SkillExecutionForm.Trigger,
                ActionForms = SkillActionForm.Active
            });
            var targets = Enumerable.Range(1, 4)
                .Select(index => $"fixture:card-movement-target-{index}").ToArray();
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "Movement Owner", "supporter", SkillId, "wei", BaseHp: 4,
                AdditionalSkillIds: slashPayment ? ["classic:fuhun"] : null));
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(
                    target, "Movement Target", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "Movement Deck",
                InitialHandSize: 2,
                DrawPerTurn: 0,
                [new ContentDeckCardCount(slashPayment ? "standard:slash" : "standard:crossbow", 64)]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "Card Movement Program Test",
                5,
                5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerGeneralId, .. targets]));
        }
    }

    private sealed class ArmorPaymentPackage(SkillProgram program) : IGameContentPackage
    {
        public const string SkillId = "fixture:armor-payment";
        public const string OwnerGeneralId = "fixture:armor-payment-owner";
        public const string ModeId = "identity:classic-armor-payment-test-5";
        private const string DeckId = "fixture:armor-payment-deck";

        public PackageManifest Manifest { get; } = new("armor-payment-scenario", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new ContentSkillDefinition(SkillId, "Armor payment", "Fixture")
            {
                Program = program,
                ExecutionForms = SkillExecutionForm.Trigger,
                ActionForms = SkillActionForm.Active
            });
            var targets = Enumerable.Range(1, 4)
                .Select(index => $"fixture:armor-payment-target-{index}").ToArray();
            builder.AddGeneral(new ContentGeneralDefinition(OwnerGeneralId, "Armor Owner", "supporter",
                SkillId, "wei", BaseHp: 4));
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "Armor Target", "supporter",
                    "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(DeckId, "Armor Deck", 2, 0,
                [new ContentDeckCardCount("classic:silver-lion", 1),
                 new ContentDeckCardCount("standard:crossbow", 63)]));
            builder.AddMode(new ContentModeDefinition(ModeId, "Armor Payment", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, DeckId, GeneralCandidateCount: 5, GeneralPoolIds: [OwnerGeneralId, .. targets]));
        }
    }

    private sealed class NestedScenarioPackage : IGameContentPackage
    {
        public const string OwnerGeneralId = "fixture:nested-movement-owner";
        public const string ModeId = "identity:classic-nested-movement-test-5";
        private const string DeckId = "fixture:nested-movement-deck";

        public PackageManifest Manifest { get; } = new(
            "nested-movement-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            var generals = Enumerable.Range(0, 5)
                .Select(index => index == 0 ? OwnerGeneralId : $"fixture:nested-movement-target-{index}")
                .ToArray();
            foreach (var general in generals)
                builder.AddGeneral(new ContentGeneralDefinition(
                    general, "Nested Movement", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "Nested Movement Deck",
                InitialHandSize: 2,
                DrawPerTurn: 0,
                [
                    new ContentDeckCardCount("classic:wooden-ox", 1),
                    new ContentDeckCardCount("standard:crossbow", 63)
                ]));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "Nested Card Movement Test",
                5,
                5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 5,
                GeneralPoolIds: generals));
        }
    }
}
