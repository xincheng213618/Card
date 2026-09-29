namespace CardGame.Core;

public sealed partial class GameEngine
{
    /// <summary>
    /// Bridges shared game primitives to the independent program executor.
    /// No skill identities, trigger rules or instruction dispatch belong here.
    /// </summary>
    private sealed class ProgramSkillHost(GameEngine engine) :
        ISkillProgramExecutionHost, ISkillProgramEffectHost
    {
        public int ResolveParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference) =>
            engine.ResolveProgramParticipant(frame, reference);

        public ProgramSkillFrame? GetActiveFrame(long frameId) =>
            engine._resolutionStack.LastOrDefault() is ProgramSkillFrame frame && frame.Id == frameId
                ? frame : null;

        public SkillProgram GetProgram(string skillId) =>
            engine._contentRegistry.Skills.GetValueOrDefault(skillId)?.Program
                ?? throw new InvalidOperationException("A running program is missing its compiled definition.");

        public SkillProgramActorState GetActor(int seat)
        {
            var actor = engine._players[seat];
            return new(engine.CreateSkillContext(actor), actor.IsAlive);
        }

        public bool IsGameOver => engine._winner != Winner.None;

        public bool OwnsSkillInstance(int ownerSeat, string skillId, string skillInstanceId) =>
            engine.HasRuntimeSkillInstance(engine._players[ownerSeat], skillId, skillInstanceId);

        public bool OwnsCards(int ownerSeat, IReadOnlyList<int> cardIds,
            IReadOnlyList<CardZoneKind> sourceZones) =>
            cardIds.All(id => sourceZones.Any(zone =>
                engine._cardZones.CardsAt(new CardLocation(zone, ownerSeat)).Any(card => card.Id == id)));

        public bool EvaluateCondition(
            ProgramSkillFrame frame,
            SkillProgramCondition condition,
            PlayerSkillContext context)
        {
            var selectedTarget = frame.SelectedTargetSeats.Count == 1
                ? engine.CreateSkillContext(engine._players[frame.SelectedTargetSeats[0]])
                : null;
            return condition.Evaluate(
                context,
                selectedTarget,
                bind => frame.PindianResultBindings.Single(item => item.Name == bind).SourceWon,
                stateId => engine.GetProgramBooleanState(frame, stateId),
                frame.WindowContext?.CardUse?.IsPublicRed,
                bind => frame.ChoiceBindings.SingleOrDefault(item => item.Name == bind)?.OptionId,
                bind => engine.AreProgramBoundCardsSameColor(frame, bind),
                (bind, categories) => engine.DoProgramBoundCardsMatchCategories(frame, bind, categories),
                (bind, kinds) => engine.DoProgramBoundCardsMatchKinds(frame, bind, kinds),
                bind => IsProgramAttackRangeCoverageDecreased(frame, bind),
                bind => engine.CountChooserProgramBoundCards(frame, bind, context.Seat),
                frame.SelectedCardIds.Count,
                (cardBind, choiceBind) => engine.DoesProgramFrozenSuitMatchChoice(frame, cardBind, choiceBind),
                (bind, suits) => engine.DoProgramBoundCardsMatchSuits(frame, bind, suits),
                (bind, _) => engine.DoProgramBoundCardCategoryMatchAction(frame, bind));
        }

        public bool TryStartPostInstructionWindow(long frameId) =>
            engine.TryBeginHpChangedProgramWindow(frameId, PostEventContinuation.Program) ||
            engine.TryBeginCardsMovedProgramWindow(frameId);

        public void UpdateFrame(ProgramSkillFrame frame)
        {
            if (GetActiveFrame(frame.Id) is null)
                throw new InvalidOperationException("A program can only advance its active frame.");
            engine._resolutionStack[^1] = frame;
        }

        public void Complete(ProgramSkillFrame frame, bool completed, string? reason = null)
        {
            if (reason is not null) engine.AddLog("ActiveSkill", reason, frame.OwnerSeat);
            engine.FinishProgramSkill(frame, completed);
        }

        public void Draw(long frameId, int ownerSeat, int targetSeat, int amount,
            SkillProgramNumberExpression? numberExpression, string? resultBind,
            SkillProgramCardSetVisibility visibility, CardMoveReason reason) =>
            engine.DrawProgramCards(
                frameId, targetSeat, amount, numberExpression, resultBind, visibility, reason);

        public void DrawSelectedTargets(long frameId, int amount, CardMoveReason reason) =>
            engine.DrawProgramSelectedTargets(frameId, amount, reason);

        public void DrawBoundCardCount(long frameId, int ownerSeat, int targetSeat, string sourceBind,
            string? resultBind, SkillProgramCardSetVisibility visibility, CardMoveReason reason)
        {
            var count = engine.GetProgramCardSet(engine.GetActiveProgramFrame(frameId), sourceBind).CardIds.Count;
            engine.DrawProgramCards(frameId, targetSeat, count, null, resultBind, visibility, reason);
        }

        public void Recover(long frameId, int ownerSeat, int targetSeat, int amount,
            SkillProgramNumberExpression? numberExpression, string? sourceBind)
        {
            var target = engine._players[targetSeat];
            var requested = numberExpression switch
            {
                null => amount,
                SkillProgramNumberExpression.BoundCardCount when sourceBind is not null =>
                    engine.GetProgramCardSet(engine.GetActiveProgramFrame(frameId), sourceBind).CardIds.Count,
                _ => throw new InvalidOperationException(
                    $"Unsupported recovery number expression '{numberExpression}'.")
            };
            var recovered = Math.Min(requested, target.MaxHp - target.Hp);
            if (recovered <= 0) return;
            var recovery = engine.BeginRecovery(frameId, ownerSeat, targetSeat, recovered);
            target.Hp += recovered;
            engine.QueueGameEvent(new RecoveryAppliedEvent(ownerSeat, targetSeat, recovered, target.Hp));
            engine.PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
            if (engine.GetActiveProgramFrame(frameId).WindowContext?.JudgmentReplacement is { } replacement)
            {
                var active = engine.GetActiveProgramFrame(frameId);
                engine._resolutionStack[^1] = active with
                {
                    WindowContext = active.WindowContext! with
                    {
                        JudgmentReplacement = replacement with { RecoveredHp = replacement.RecoveredHp + recovered }
                    }
                };
            }
        }

        public void RecoverSelectedTargets(long frameId, int ownerSeat, int amount)
        {
            var frame = engine.GetActiveProgramFrame(frameId);
            foreach (var seat in frame.SelectedTargetSeats)
                if (engine._players[seat].IsAlive)
                    Recover(frameId, ownerSeat, seat, amount, null, null);
        }

        public SkillProgramStepOutcome LoseHp(long frameId, string skillId, int targetSeat, int amount)
        {
            var target = engine._players[targetSeat];
            var before = target.Hp;
            var lost = Math.Min(target.Hp, amount);
            target.Hp = Math.Max(0, target.Hp - amount);
            engine.RecordHpChange(frameId, null, targetSeat, before, target.Hp, HpChangeKind.Loss);
            engine.QueueGameEvent(new ProgramSkillHpLostEvent(frameId, skillId, targetSeat, lost, target.Hp));
            if (target.Hp != 0) return SkillProgramStepOutcome.Continue;
            engine.BeginProgramSkillDying(frameId, target);
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome Damage(ProgramSkillFrame frame, int targetSeat, int amount,
            ProgramParticipantReference? sourceReference = null, DamageNature? nature = null) =>
            engine.BeginProgramSkillDamage(frame, targetSeat, amount, sourceReference, nature);

        public void ReplaceJudgment(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ReplaceProgramJudgment(frame, effect);

        public SkillProgramStepOutcome ClaimJudgmentCard(ProgramSkillFrame frame) =>
            engine.ClaimProgramJudgmentCard(frame);

        public SkillProgramStepOutcome ReorderTopCards(ProgramSkillFrame frame, int maximumCards,
            SkillProgramNumberExpression? numberExpression) =>
            engine.BeginProgramTopReorder(frame, maximumCards, numberExpression);

        public SkillProgramStepOutcome RepeatJudgment(ProgramSkillFrame frame, string reason,
            string resultBind, IReadOnlyList<Suit> successSuits) =>
            engine.BeginProgramRepeatedJudgment(frame, reason, resultBind, successSuits);

        public SkillProgramStepOutcome SkipTurnPhases(ProgramSkillFrame frame,
            IReadOnlyList<SkillProgramTurnPhase> phases) =>
            engine.SkipProgramTurnPhases(frame, phases);

        public SkillProgramStepOutcome UseVirtualCard(ProgramSkillFrame frame, int targetSeat,
            CardKind cardKind, bool ignoreDistance) =>
            engine.BeginProgramVirtualCardUse(frame, targetSeat, cardKind, ignoreDistance);

        public SkillProgramStepOutcome UseBoundCardByTarget(ProgramSkillFrame frame, int targetSeat,
            string sourceBind) =>
            engine.UseProgramBoundCardByTarget(frame, targetSeat, sourceBind);

        public SkillProgramStepOutcome RequestSlashByTarget(ProgramSkillFrame frame, int targetSeat,
            string resultBind) =>
            engine.RequestProgramSlashByTarget(frame, targetSeat, resultBind);

        public void PendExtraTurn(ProgramSkillFrame frame, int? targetSeat = null) =>
            engine.PendProgramExtraTurn(frame, targetSeat);

        public void ClaimDeathCleanupCards(ProgramSkillFrame frame) =>
            engine.ClaimProgramDeathCleanupCards(frame);

        public SkillProgramStepOutcome BindDiscardPhaseDiscards(ProgramSkillFrame frame, string resultBind) =>
            engine.BindProgramDiscardPhaseDiscards(frame, resultBind);

        public SkillProgramStepOutcome Pindian(ProgramSkillFrame frame, int targetSeat) =>
            engine.BeginProgramSkillPindian(frame, targetSeat);

        public SkillProgramStepOutcome MoveSelected(ProgramSkillFrame frame, int targetSeat, IReadOnlyList<int> cardIds,
            bool toDiscard, CardMoveReason reason)
        {
            var program = GetProgram(frame.SkillId);
            var activation = program.Activations.Single(item => item.Id == frame.ActivationId);
            var selected = cardIds.Select(id =>
            {
                var locations = activation.SourceZones
                    .Select(zone => new CardLocation(zone, frame.OwnerSeat))
                    .Where(location => engine._cardZones.CardsAt(location).Any(card => card.Id == id))
                    .ToArray();
                if (locations.Length != 1)
                    throw new InvalidOperationException("A selected program cost no longer has one valid owner location.");
                return (Card: engine._cardZones.CardsAt(locations[0]).Single(card => card.Id == id),
                    Location: locations[0]);
            }).ToArray();
            var active = engine.GetActiveProgramFrame(frame.Id);
            if (active.PendingMovementContinuation is not null)
                throw new InvalidOperationException("A selected-card movement is already awaiting its trigger window.");
            engine._resolutionStack[^1] = active with
            {
                PendingMovementContinuation = new ProgramMovementContinuation(frame.OwnerSeat, 0, null)
            };
            var destination = toDiscard ? CardLocation.DiscardPile : CardLocation.Hand(targetSeat);
            foreach (var group in selected.GroupBy(item => item.Location))
                engine.MoveCards(group.Select(item => item.Card).ToArray(), group.Key, destination, reason);
            if (!engine.TryBeginCardsMovedProgramWindow())
                engine.CompleteAwaitedProgramMovement(frame.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome InsertPhase(ProgramSkillFrame frame, TurnPhase phase,
            SkillProgramPhaseContinuation continuation) =>
            engine.ScheduleProgramPhase(frame, phase, continuation);

        public void RecoverTo(long frameId, int ownerSeat, int targetSeat,
            SkillProgramNumberExpression expression, int minimumValue, bool clampToMaxHp) =>
            engine.RecoverProgramTargetTo(frameId, ownerSeat, targetSeat, expression, minimumValue, clampToMaxHp);

        public void DiscardOwnedZoneCards(
            ProgramSkillFrame frame,
            IReadOnlyList<CardZoneKind> zones,
            CardMoveReason reason) =>
            engine.DiscardProgramOwnedZoneCards(frame, zones, reason);

        public void SetChainedState(ProgramSkillFrame frame, bool chained, int? targetSeat = null) =>
            engine.SetProgramChainedState(frame, chained, targetSeat ?? frame.OwnerSeat);

        public SkillProgramStepOutcome ChooseOption(ProgramSkillFrame frame, int chooserSeat,
            string resultBind, IReadOnlyList<SkillProgramChoiceOption> options) =>
            engine.ChooseProgramOption(frame, chooserSeat, resultBind, options);

        public SkillProgramStepOutcome SelectOwnedCards(ProgramSkillFrame frame, int cardOwnerSeat,
            int amount, SkillProgramNumberExpression? expression, IReadOnlyList<CardZoneKind> zones, string resultBind,
            int minimumCards, int maximumCards, IReadOnlyList<CardKind> cardKinds, IReadOnlyList<Suit> suits) =>
            engine.SelectProgramOwnedCards(frame, cardOwnerSeat, amount, expression, zones, resultBind,
                minimumCards, maximumCards, cardKinds, suits);

        public SkillProgramStepOutcome HoldTargetCards(ProgramSkillFrame frame, int chooserSeat, int holderSeat,
            IReadOnlyList<CardZoneKind> zones, string resultBind, int minimumCards) =>
            engine.HoldProgramTargetCards(frame, chooserSeat, holderSeat, zones, resultBind, minimumCards);

        public SkillProgramStepOutcome RevealTargetHandCard(ProgramSkillFrame frame,
            ProgramParticipantReference chooser, ProgramParticipantReference cardOwner,
            string resultBind, SkillProgramRevealMode mode,
            IReadOnlyList<Suit> eligibleSuits, bool allowDecline) =>
            engine.RevealProgramTargetHandCard(frame, chooser, cardOwner, resultBind, mode,
                eligibleSuits, allowDecline);

        public void CaptureSelectedCards(ProgramSkillFrame frame, string resultBind) =>
            engine.CaptureProgramSelectedCards(frame, resultBind);

        public void RevealBoundCards(ProgramSkillFrame frame, string sourceBind) =>
            engine.RevealProgramBoundCards(frame, sourceBind);

        public void UseBoundCardAsDyingAlcohol(ProgramSkillFrame frame, string sourceBind, CardMoveReason reason) =>
            engine.UseProgramBoundCardAsDyingAlcohol(frame, sourceBind, reason);

        public void UseVirtualDyingAlcohol(ProgramSkillFrame frame) =>
            engine.UseProgramVirtualDyingAlcohol(frame);

        public void ClaimMovedCards(ProgramSkillFrame frame) =>
            engine.ClaimProgramMovedCards(frame);

        public void RevealUniqueRankForDying(ProgramSkillFrame frame, CardZoneKind zone, int rescueHp) =>
            engine.RevealProgramUniqueRankForDying(frame, zone, rescueHp);

        public void RedirectCurrentDamage(ProgramSkillFrame frame, string sourceBind,
            bool drawLostHpAfterDamage) =>
            engine.RedirectProgramCurrentDamage(frame, sourceBind, drawLostHpAfterDamage);

        public SkillProgramStepOutcome ChooseDifferentCategoryDiscard(
            ProgramSkillFrame frame,
            ProgramParticipantReference chooser,
            ProgramParticipantReference cardOwner,
            IReadOnlyList<CardZoneKind> zones,
            string sourceBind,
            string resultBind,
            CardMoveReason reason) =>
            engine.ChooseProgramDifferentCategoryDiscard(
                frame, chooser, cardOwner, zones, sourceBind, resultBind, reason);

        public void ChangeMaximumHp(ProgramSkillFrame frame, int amount) =>
            engine.ChangeProgramMaximumHp(frame, amount);

        public void GrantSkills(ProgramSkillFrame frame, IReadOnlyList<string> skillIds) =>
            engine.GrantProgramSkills(frame, skillIds);

        public void GrantTurnSkills(ProgramSkillFrame frame, IReadOnlyList<string> skillIds) =>
            engine.GrantProgramTurnSkills(frame, skillIds);

        public SkillProgramStepOutcome UseSelectedCardsAs(
            ProgramSkillFrame frame,
            int targetSeat,
            string viewAsId,
            CardKind outputKind) =>
            engine.UseProgramSelectedCardsAs(frame, targetSeat, viewAsId, outputKind);

        public SkillProgramStepOutcome UseAllHandCardsAsOrdinaryTrick(
            ProgramSkillFrame frame,
            string viewAsId) =>
            engine.UseProgramAllHandCardsAsOrdinaryTrick(frame, viewAsId);

        public SkillProgramStepOutcome StartVirtualDuel(ProgramSkillFrame frame) =>
            engine.BeginProgramVirtualDuel(frame);

        public SkillProgramStepOutcome RequestFactionCard(ProgramSkillFrame frame, int targetSeat,
            string providerFactionId, CardKind requiredKind)
        {
            engine.BeginProgramFactionCardRequest(frame.Id, frame.OwnerSeat, targetSeat,
                providerFactionId, requiredKind);
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome TransferRandomOwnedCard(ProgramSkillFrame frame, int targetSeat, string resultBind) =>
            engine.TransferProgramRandomOwnedCard(frame, targetSeat, resultBind);

        public SkillProgramStepOutcome ExchangeSelectedTargetHands(ProgramSkillFrame frame) =>
            engine.ExchangeProgramSelectedTargetHands(frame);

        public void AccumulateSelectedCardCount(ProgramSkillFrame frame, string usageId,
            int threshold, string resultBind) =>
            engine.AccumulateProgramSelectedCardCount(frame, usageId, threshold, resultBind);

        public void TurnOver(long frameId, int ownerSeat, int targetSeat) =>
            engine.TurnOverProgramTarget(frameId, ownerSeat, targetSeat);

        public void SetFaceState(long frameId, int ownerSeat, int targetSeat, bool faceDown) =>
            engine.SetProgramTargetFaceState(frameId, ownerSeat, targetSeat, faceDown);

        public SkillProgramStepOutcome StartJudgment(
            ProgramSkillFrame frame,
            int targetSeat,
            string reason,
            string resultBind,
            SkillProgramCardSetVisibility visibility,
            int sourceSeat) =>
            engine.StartProgramJudgment(frame, targetSeat, reason, resultBind, visibility, sourceSeat);

        public SkillProgramStepOutcome ChooseOwnCardDiscard(
            ProgramSkillFrame frame,
            ProgramParticipantReference? chooser,
            IReadOnlyList<CardZoneKind> zones,
            CardMoveReason reason) =>
            engine.ChooseProgramOwnCardDiscard(frame, chooser, zones, reason);

        public void RevealTopCards(long frameId, int ownerSeat, int amount,
            SkillProgramNumberExpression? numberExpression, string resultBind,
            SkillProgramCardSetVisibility visibility) =>
            engine.RevealProgramTopCards(
                frameId, ownerSeat, amount, numberExpression, resultBind, visibility);

        public void FilterBoundCards(long frameId, string sourceBind, string resultBind,
            IReadOnlyList<Suit> suits, ProgramParticipantReference? effectiveSuitFor = null,
            IReadOnlyList<SkillProgramCardCategory>? categories = null,
            IReadOnlyList<EquipmentSlot>? equipmentSlots = null,
            IReadOnlyList<CardKind>? cardKinds = null, string? matchSuitOfBind = null) =>
            engine.FilterProgramBoundCards(frameId, sourceBind, resultBind, suits, effectiveSuitFor,
                categories ?? [], equipmentSlots ?? [], cardKinds ?? [], matchSuitOfBind);

        public SkillProgramStepOutcome SelectCardSubset(long frameId, int ownerSeat, string sourceBind,
            string resultBind, int minimumCards, int maximumCards, int maximumRankSum,
            SkillProgramSubsetAiOrder aiOrder, bool allowFewerWhenInsufficient, bool onePerSuit) =>
            engine.SelectProgramCardSubset(frameId, ownerSeat, sourceBind, resultBind, minimumCards,
                maximumCards, maximumRankSum, aiOrder, allowFewerWhenInsufficient, onePerSuit);

        public SkillProgramStepOutcome MoveBoundCards(long frameId, int ownerSeat, string sourceBind, string? exceptBind,
            SkillProgramCardDestination destination, CardZoneKind? destinationZone, CardMoveReason reason) =>
            engine.MoveProgramBoundCards(frameId, ownerSeat, sourceBind, exceptBind, destination,
                destinationZone, reason);

        public SkillProgramStepOutcome SelectTarget(
            long frameId,
            int ownerSeat,
            SkillProgramTargetKind targetKind,
            IReadOnlyList<CardZoneKind> zones) =>
            engine.SelectProgramTarget(frameId, ownerSeat, targetKind, zones, marker: null);

        public SkillProgramStepOutcome SelectTarget(
            long frameId,
            int ownerSeat,
            SkillProgramTargetKind targetKind,
            IReadOnlyList<CardZoneKind> zones,
            PlayerMarkerKind? marker,
            ProgramParticipantReference? actorReference = null,
            bool skipIfNoTarget = false) =>
            engine.SelectProgramTarget(frameId, ownerSeat, targetKind, zones, marker,
                actorReference, skipIfNoTarget);

        public void ChangeAttributedMarker(
            ProgramSkillFrame frame,
            ProgramParticipantReference target,
            PlayerMarkerKind marker,
            int amount) =>
            engine.ChangeProgramAttributedMarker(frame, target, marker, amount);

        public SkillProgramStepOutcome CauseDeathUnlessBoundCardKind(
            ProgramSkillFrame frame,
            string sourceBind,
            IReadOnlyList<CardKind> excludedCardKinds) =>
            engine.CauseProgramDeathUnlessBoundCardKind(frame, sourceBind, excludedCardKinds);

        public SkillProgramStepOutcome SelectTargets(
            long frameId,
            int ownerSeat,
            SkillProgramTargetKind targetKind,
            int minimumTargets,
            int maximumTargets,
            SkillProgramNumberExpression? numberExpression,
            SkillProgramTargetAiOrder aiOrder) =>
            engine.SelectProgramTargets(
                frameId, ownerSeat, targetKind, minimumTargets, maximumTargets, numberExpression, aiOrder);

        public SkillProgramStepOutcome SelectSourceCard(
            long frameId,
            int ownerSeat,
            SkillProgramCardSource cardSource,
            IReadOnlyList<CardZoneKind> zones,
            string resultBind,
            IReadOnlyList<EquipmentSlot> equipmentSlots,
            bool skipIfNoCards,
            bool allowSameSource) =>
            engine.SelectProgramSourceCard(frameId, ownerSeat, cardSource, zones, resultBind,
                equipmentSlots, skipIfNoCards, allowSameSource);

        public SkillProgramStepOutcome GiveBoundCard(
            long frameId,
            int ownerSeat,
            string sourceBind,
            SkillProgramTargetKind targetKind,
            CardMoveReason reason) =>
            engine.GiveProgramBoundCard(frameId, ownerSeat, sourceBind, targetKind, reason);

        public SkillProgramStepOutcome DistributeOwnedCards(
            ProgramSkillFrame frame,
            IReadOnlyList<CardZoneKind> zones,
            string sourceBind,
            SkillProgramTargetKind targetKind,
            bool allowDeclineBeforeFirst,
            CardMoveReason reason) =>
            engine.DistributeProgramOwnedCards(
                frame, zones, sourceBind, targetKind, allowDeclineBeforeFirst, reason);

        public SkillProgramStepOutcome RequestAttackRangeAid(
            ProgramSkillFrame frame,
            CardMoveReason reason) =>
            engine.RequestProgramAttackRangeAid(frame, reason);

        public void ClaimDamageCards(long frameId, int ownerSeat, CardMoveReason reason) =>
            engine.ClaimProgramDamageCards(frameId, ownerSeat, reason);

        public void TakeRandomHandCardFromSelectedTargets(
            long frameId,
            int ownerSeat,
            int amountPerTarget,
            CardMoveReason reason) =>
            engine.TakeProgramRandomHandCards(
                frameId, ownerSeat, amountPerTarget, reason);

        public void TakeRandomCardFromEveryOtherCharacter(
            long frameId,
            int ownerSeat,
            IReadOnlyList<CardZoneKind> zones,
            CardMoveReason reason) =>
            engine.TakeProgramRandomCardFromEveryOtherCharacter(
                frameId, ownerSeat, zones, reason);

        public void AdjustNormalDraw(ProgramSkillFrame frame, int amount) =>
            engine.AdjustProgramNormalDraw(frame, amount);

        public void GrantTurnCardDamageModifier(
            ProgramSkillFrame frame,
            IReadOnlyList<CardKind> cardKinds,
            int amount,
            SkillProgramDamageModifierExpiration expiration,
            SkillProgramDamageModifierSourceScope sourceScope) =>
            engine.GrantProgramTurnCardDamageModifier(frame, cardKinds, amount, expiration, sourceScope);

        public void GrantTurnCardActionProhibition(
            ProgramSkillFrame frame,
            IReadOnlyList<CardKind> cardKinds,
            IReadOnlyList<CardActionType> actionTypes) =>
            engine.GrantProgramTurnCardActionProhibition(frame, cardKinds, actionTypes);

        public void GrantTurnHandColorRestriction(
            ProgramSkillFrame frame,
            string sourceBind,
            int targetSeat) =>
            engine.GrantProgramTurnHandColorRestriction(frame, sourceBind, targetSeat);

        public void GrantTurnHandCardProhibition(ProgramSkillFrame frame, int targetSeat) =>
            engine.GrantProgramTurnHandCardProhibition(frame, targetSeat);

        public void AbolishOwnerAreas(ProgramSkillFrame frame, IReadOnlyList<CardZoneKind> zones) =>
            engine.AbolishProgramOwnerAreas(frame, zones);

        public void LoseDeathSourceSkills(ProgramSkillFrame frame) =>
            engine.LoseProgramDeathSourceSkills(frame);

        public void PreventCurrentDamage(ProgramSkillFrame frame) =>
            engine.PreventProgramCurrentDamage(frame);

        public void NullifyCurrentCardEffect(ProgramSkillFrame frame) =>
            engine.NullifyCurrentProgramCardEffect(frame);
        public void NullifySelectedCardEffects(ProgramSkillFrame frame) =>
            engine.NullifySelectedProgramCardEffects(frame);
        public void ProhibitCurrentResponse(ProgramSkillFrame frame) =>
            engine.ProhibitCurrentProgramResponse(frame);
        public void RedirectCurrentAttack(ProgramSkillFrame frame, int targetSeat) =>
            engine.RedirectCurrentProgramAttack(frame, targetSeat);

        public void GrantTurnRuleModifier(
            ProgramSkillFrame frame,
            SkillRuleQuery query,
            SkillRuleOperation operation,
            int amount,
            IReadOnlyList<CardKind> cardKinds) =>
            engine.GrantProgramTurnRuleModifier(frame, query, operation, amount, cardKinds);

        public void GrantTurnCardTargetRestriction(
            ProgramSkillFrame frame,
            SkillProgramCardTargetRestriction restriction) =>
            engine.GrantProgramTurnCardTargetRestriction(frame, restriction, frame.OwnerSeat);

        public void GrantTurnCardTargetRestriction(
            ProgramSkillFrame frame,
            SkillProgramCardTargetRestriction restriction,
            int targetSeat) =>
            engine.GrantProgramTurnCardTargetRestriction(frame, restriction, targetSeat);

        public void GrantTurnCardConversion(
            ProgramSkillFrame frame,
            string sourceBind,
            SkillProgramCardColorRelation colorRelation,
            CardKind outputKind) =>
            engine.GrantProgramTurnCardConversion(frame, sourceBind, colorRelation, outputKind);
        public SkillProgramStepOutcome SelectAndMoveOwnedCard(
            ProgramSkillFrame frame,
            ProgramParticipantReference chooser,
            ProgramParticipantReference cardOwner,
            IReadOnlyList<CardZoneKind> zones,
            SkillProgramCardDestination destination,
            ProgramParticipantReference? destinationRef,
            string? resultBind,
            CardMoveReason reason, IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
            bool skipIfNoCards = false, bool allowSameOwnerHandReturn = false,
            string? coverageResultBind = null, bool awaitMovementTriggers = false,
            bool revealBeforeMove = false, IReadOnlyList<CardKind>? cardKinds = null,
            bool prohibitReplacingEquipment = false) =>
            engine.SelectAndMoveProgramOwnedCard(frame, chooser, cardOwner, zones, destination, destinationRef, resultBind,
                reason, cardCategories, skipIfNoCards, allowSameOwnerHandReturn,
                coverageResultBind, awaitMovementTriggers, revealBeforeMove, cardKinds,
                prohibitReplacingEquipment);

        public SkillProgramStepOutcome ChooseOtherOwnedCardDiscard(
            ProgramSkillFrame frame,
            ProgramParticipantReference chooser,
            IReadOnlyList<CardZoneKind> zones,
            CardMoveReason reason) =>
            engine.ChooseProgramOtherOwnedCardDiscard(frame, chooser, zones, reason);

        public SkillProgramStepOutcome RestorePhaseHandDiscards(
            ProgramSkillFrame frame,
            ProgramParticipantReference chooser,
            ProgramParticipantReference phaseOwner) =>
            engine.RestoreProgramPhaseHandDiscards(frame, chooser, phaseOwner);

        public void RefundCardUseDebit(ProgramSkillFrame frame) =>
            engine.RefundProgramCardUseDebit(frame);

        public SkillProgramStepOutcome StartPindian(
            ProgramSkillFrame frame,
            ProgramParticipantReference opponentReference,
            string resultBind,
            SkillProgramCardSetVisibility visibility) =>
            engine.StartProgramPindian(frame, opponentReference, resultBind, visibility);

        public void SetBooleanState(ProgramSkillFrame frame, string stateId, bool value) =>
            engine.SetProgramBooleanState(frame, stateId, value);

        public void ToggleBooleanState(ProgramSkillFrame frame, string stateId) =>
            engine.SetProgramBooleanState(frame, stateId, !engine.GetProgramBooleanState(frame, stateId));

        public void GrantDirectedTurnCardPolicy(
            ProgramSkillFrame frame,
            ProgramParticipantReference actorReference,
            ProgramParticipantReference targetReference,
            IReadOnlyList<CardKind> cardKinds,
            DirectedTurnCardPolicyEffect effects) =>
            engine.GrantProgramDirectedTurnCardPolicy(
                frame, actorReference, targetReference, cardKinds, effects);
    }
}
