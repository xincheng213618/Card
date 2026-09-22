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
        public ProgramSkillFrame? GetActiveFrame(long frameId) =>
            engine._resolutionStack.LastOrDefault() is ProgramSkillFrame frame && frame.Id == frameId
                ? frame : null;

        public SkillProgram GetProgram(string skillId) =>
            engine._contentRegistry?.Skills.GetValueOrDefault(skillId)?.Program
                ?? throw new InvalidOperationException("A running program is missing its compiled definition.");

        public SkillProgramActorState GetActor(int seat)
        {
            var actor = engine._players[seat];
            return new(engine.CreateSkillContext(actor), actor.IsAlive);
        }

        public bool IsGameOver => engine._winner != Winner.None;

        public bool OwnsSkillInstance(int ownerSeat, string skillId, string skillInstanceId) =>
            engine.HasRuntimeSkillInstance(engine._players[ownerSeat], skillId, skillInstanceId);

        public bool OwnsHandCards(int ownerSeat, IReadOnlyList<int> cardIds) =>
            cardIds.All(id => engine.GetHand(engine._players[ownerSeat]).Any(card => card.Id == id));

        public bool EvaluateCondition(
            ProgramSkillFrame frame,
            SkillProgramCondition condition,
            PlayerSkillContext context) =>
            condition.Evaluate(
                context,
                bind => frame.PindianResultBindings.Single(item => item.Name == bind).SourceWon,
                stateId => engine.GetProgramBooleanState(frame, stateId),
                frame.WindowContext?.CardUse?.IsPublicRed);

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
        }

        public SkillProgramStepOutcome LoseHp(long frameId, string skillId, int targetSeat, int amount)
        {
            var target = engine._players[targetSeat];
            var lost = Math.Min(target.Hp, amount);
            target.Hp = Math.Max(0, target.Hp - amount);
            engine.QueueGameEvent(new ProgramSkillHpLostEvent(frameId, skillId, targetSeat, lost, target.Hp));
            if (target.Hp != 0) return SkillProgramStepOutcome.Continue;
            engine.BeginProgramSkillDying(frameId, target);
            return SkillProgramStepOutcome.AwaitChild;
        }

        public void MoveSelected(int ownerSeat, int targetSeat, IReadOnlyList<int> cardIds,
            bool toDiscard, CardMoveReason reason)
        {
            var selected = cardIds.Select(id =>
                engine.GetHand(engine._players[ownerSeat]).Single(card => card.Id == id)).ToArray();
            engine.MoveCards(selected, CardLocation.Hand(ownerSeat),
                toDiscard ? CardLocation.DiscardPile : CardLocation.Hand(targetSeat), reason);
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

        public void SetChainedState(ProgramSkillFrame frame, bool chained) =>
            engine.SetProgramChainedState(frame, chained);

        public void TurnOver(long frameId, int ownerSeat, int targetSeat) =>
            engine.TurnOverProgramTarget(frameId, ownerSeat, targetSeat);

        public void SetFaceState(long frameId, int ownerSeat, int targetSeat, bool faceDown) =>
            engine.SetProgramTargetFaceState(frameId, ownerSeat, targetSeat, faceDown);

        public SkillProgramStepOutcome StartJudgment(
            ProgramSkillFrame frame,
            int targetSeat,
            string reason,
            string resultBind,
            SkillProgramCardSetVisibility visibility) =>
            engine.StartProgramJudgment(frame, targetSeat, reason, resultBind, visibility);

        public void RevealTopCards(long frameId, int ownerSeat, int amount,
            SkillProgramNumberExpression? numberExpression, string resultBind,
            SkillProgramCardSetVisibility visibility) =>
            engine.RevealProgramTopCards(
                frameId, ownerSeat, amount, numberExpression, resultBind, visibility);

        public void FilterBoundCards(long frameId, string sourceBind, string resultBind,
            IReadOnlyList<Suit> suits) =>
            engine.FilterProgramBoundCards(frameId, sourceBind, resultBind, suits);

        public SkillProgramStepOutcome SelectCardSubset(long frameId, int ownerSeat, string sourceBind,
            string resultBind, int minimumCards, int maximumCards, int maximumRankSum,
            SkillProgramSubsetAiOrder aiOrder) =>
            engine.SelectProgramCardSubset(frameId, ownerSeat, sourceBind, resultBind, minimumCards,
                maximumCards, maximumRankSum, aiOrder);

        public void MoveBoundCards(long frameId, int ownerSeat, string sourceBind, string? exceptBind,
            SkillProgramCardDestination destination, CardMoveReason reason) =>
            engine.MoveProgramBoundCards(frameId, ownerSeat, sourceBind, exceptBind, destination, reason);

        public SkillProgramStepOutcome SelectTarget(
            long frameId,
            int ownerSeat,
            SkillProgramTargetKind targetKind,
            IReadOnlyList<CardZoneKind> zones) =>
            engine.SelectProgramTarget(frameId, ownerSeat, targetKind, zones);

        public SkillProgramStepOutcome SelectTargets(
            long frameId,
            int ownerSeat,
            SkillProgramTargetKind targetKind,
            int minimumTargets,
            int maximumTargets,
            SkillProgramTargetAiOrder aiOrder) =>
            engine.SelectProgramTargets(
                frameId, ownerSeat, targetKind, minimumTargets, maximumTargets, aiOrder);

        public SkillProgramStepOutcome SelectSourceCard(
            long frameId,
            int ownerSeat,
            IReadOnlyList<CardZoneKind> zones,
            string resultBind) =>
            engine.SelectProgramSourceCard(frameId, ownerSeat, zones, resultBind);

        public SkillProgramStepOutcome GiveBoundCard(
            long frameId,
            int ownerSeat,
            string sourceBind,
            SkillProgramTargetKind targetKind,
            CardMoveReason reason) =>
            engine.GiveProgramBoundCard(frameId, ownerSeat, sourceBind, targetKind, reason);

        public void ClaimDamageCards(long frameId, int ownerSeat, CardMoveReason reason) =>
            engine.ClaimProgramDamageCards(frameId, ownerSeat, reason);

        public void TakeRandomHandCardFromSelectedTargets(
            long frameId,
            int ownerSeat,
            int amountPerTarget,
            CardMoveReason reason) =>
            engine.TakeProgramRandomHandCards(
                frameId, ownerSeat, amountPerTarget, reason);

        public void AdjustNormalDraw(ProgramSkillFrame frame, int amount) =>
            engine.AdjustProgramNormalDraw(frame, amount);

        public void GrantTurnCardDamageModifier(
            ProgramSkillFrame frame,
            IReadOnlyList<CardKind> cardKinds,
            int amount) =>
            engine.GrantProgramTurnCardDamageModifier(frame, cardKinds, amount);

        public void GrantTurnCardActionProhibition(
            ProgramSkillFrame frame,
            IReadOnlyList<CardKind> cardKinds,
            IReadOnlyList<CardActionType> actionTypes) =>
            engine.GrantProgramTurnCardActionProhibition(frame, cardKinds, actionTypes);

        public void GrantTurnRuleModifier(
            ProgramSkillFrame frame,
            SkillRuleQuery query,
            SkillRuleOperation operation,
            int amount) =>
            engine.GrantProgramTurnRuleModifier(frame, query, operation, amount);

        public void GrantTurnCardTargetRestriction(
            ProgramSkillFrame frame,
            SkillProgramCardTargetRestriction restriction) =>
            engine.GrantProgramTurnCardTargetRestriction(frame, restriction);

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
            string? resultBind,
            CardMoveReason reason) =>
            engine.SelectAndMoveProgramOwnedCard(frame, chooser, cardOwner, zones, destination, resultBind, reason);

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
