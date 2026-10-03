namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardUseFrame CurrentFinalTargetSlashOwner(ProgramSkillFrame frame)
    {
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized,
                CardUse: { } context, TargetSeat: { } target } ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == context.ParentCardUseFrameId) is not
                { Action: { Type: CardActionType.Use } action } owner ||
            action.ActionId != context.CardActionId || !IsSlashCard(action.EffectiveKind) ||
            frame.OwnerSeat != action.ActorSeat || context.ActorSeat != action.ActorSeat ||
            !action.EffectiveDesignatedTargetSeats.Contains(target) || !IsValidPlayerSeat(target))
            throw new InvalidOperationException("A current-target operation lost its exact Slash use and target.");
        return owner;
    }

    private SkillProgramStepOutcome ClaimCurrentUsePhysicalCards(ProgramSkillFrame frame)
    {
        var owner = CurrentFinalTargetSlashOwner(frame);
        var target = frame.WindowContext!.TargetSeat!.Value;
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive || !_players[target].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var effectIndex = frame.InstructionIndex - 1;
        var claims = owner.CurrentUsePhysicalClaims ?? [];
        if (claims.Any(claim => claim.ProducerFrameId == frame.Id && claim.EffectIndex == effectIndex))
            throw new InvalidOperationException("An already claimed use entity cannot be paid again.");
        var ids = owner.Action!.PhysicalCards.Select(cost => cost.CardId)
            .Where(id => _cardZones.GetLocation(id) == CardLocation.Processing &&
                !IsClaimedUseCardEntity(owner.Id, id)).ToArray();
        if (ids.Length == 0) return SkillProgramStepOutcome.Continue;
        var receipt = new ProgramCurrentUsePhysicalClaim(owner.Id, owner.Action.ActionId, frame.Id, effectIndex,
            frame.OwnerSeat, target, frame.SkillId, frame.SkillInstanceId, frame.GameplayHash, frame.TriggerId!,
            Array.AsReadOnly(ids));
        ReplaceRuntimeFrame(owner.Id, owner with { CurrentUsePhysicalClaims = Array.AsReadOnly(claims.Append(receipt).ToArray()) });
        // Issue the owning receipt before movements: a movement observer can
        // inspect or continue the parent use without treating these entities as leaks.
        AdvanceEventRulesAndQueueFact(new ProgramCurrentUsePhysicalCardsClaimedEvent(receipt));
        var batch = BeginCardMovementBatch([CardLocation.Processing], [CardLocation.Hand(target)]);
        var movements = new List<CardMovementRecord>();
        var committed = false;
        try
        {
            foreach (var id in ids)
            {
                var card = EntityAtCurrentLocation(id);
                _cardZones.Move(id, CardLocation.Processing, CardLocation.Hand(target));
                movements.Add(RecordMovement(card, CardLocation.Processing, CardLocation.Hand(target),
                    new("skill-program.current-use-physical-claim")));
            }
            committed = true;
        }
        finally { CompleteCardMovementBatch(batch, movements, committed); }
        return TryBeginCardsMovedProgramWindow(frame.Id) ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.Continue;
    }

    private bool IsClaimedUseCardEntity(long frameId, int cardId) => IsExchangedUseCardClaim(frameId, cardId) || IsCurrentUsePhysicalCardClaim(frameId, cardId);

    private bool IsCurrentUsePhysicalCardClaim(long frameId, int cardId) =>
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == frameId) is
            { Action: { } action, CurrentUsePhysicalClaims: { } claims } &&
        claims.Any(claim => claim.ActionId == action.ActionId && claim.CardUseFrameId == frameId && claim.CardIds.Contains(cardId));

    private void PreventCurrentTargetSlashCancellationByRule(ProgramSkillFrame frame)
    {
        var owner = CurrentFinalTargetSlashOwner(frame);
        var target = frame.WindowContext!.TargetSeat!.Value;
        if (!_players[target].IsAlive) return;
        var entries = owner.FinalTargetSlashReceipts ?? [];
        var effectIndex = frame.InstructionIndex - 1;
        if (entries.Any(item => item.ProducerFrameId == frame.Id && item.EffectIndex == effectIndex)) return;
        var receipt = new ProgramTargetSlashReceipt(owner.Action!.ActionId, frame.OwnerSeat, target, owner.Id,
            frame.Id, frame.SkillId, frame.SkillInstanceId, frame.GameplayHash, frame.TriggerId!, effectIndex, true, 0);
        ReplaceRuntimeFrame(owner.Id, owner with { FinalTargetSlashReceipts = Array.AsReadOnly(entries.Append(receipt).ToArray()) });
        AdvanceEventRulesAndQueueFact(new ProgramTargetSlashReceiptIssuedEvent(owner.Id, receipt));
    }

    private void AssertCurrentUsePhysicalClaims()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(use => use.CurrentUsePhysicalClaims is not null))
        {
            var claims = use.CurrentUsePhysicalClaims!;
            if (use.Action is not { Type: CardActionType.Use } action || !IsSlashCard(action.EffectiveKind) ||
                claims.Count == 0 || claims.DistinctBy(c => (c.ProducerFrameId, c.EffectIndex)).Count() != claims.Count ||
                claims.SelectMany(c => c.CardIds).Distinct().Count() != claims.Sum(c => c.CardIds.Count) ||
                claims.Any(c => c.CardUseFrameId != use.Id || c.ActionId != action.ActionId || c.ActorSeat != action.ActorSeat ||
                    !IsValidPlayerSeat(c.RecipientSeat) || c.ActorSeat == c.RecipientSeat ||
                    !action.EffectiveDesignatedTargetSeats.Contains(c.RecipientSeat) || c.ProducerFrameId <= 0 ||
                    c.CardIds.Count == 0 || c.CardIds.Distinct().Count() != c.CardIds.Count ||
                    c.CardIds.Any(id => !action.PhysicalCards.Any(cost => cost.CardId == id)) ||
                    string.IsNullOrEmpty(c.SkillInstanceId) ||
                    _contentRegistry.Skills.GetValueOrDefault(c.SkillId)?.Program is not { } program ||
                    program.GameplayHash != c.GameplayHash ||
                    program.Triggers.SingleOrDefault(t => t.Id == c.TriggerId) is not
                        { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, OwnerRelation: SkillProgramCardActionOwnerRelation.Actor } trigger ||
                    c.EffectIndex < 0 || c.EffectIndex >= trigger.Effects.Count ||
                    trigger.Effects[c.EffectIndex].Op != SkillProgramEffectOp.ClaimCurrentUsePhysicalCards ||
                    !_events.Select(e => e.Payload).Concat(_pendingEvents).OfType<ProgramCurrentUsePhysicalCardsClaimedEvent>()
                        .Any(e => (e.Claim with { CardIds = c.CardIds }) == c && e.Claim.CardIds.SequenceEqual(c.CardIds))))
                throw new InvalidOperationException("A claimed physical entity lost its still-resolving use and issuing instruction.");
        }
    }

    private sealed partial class ProgramSkillHost : ICurrentUsePhysicalClaimProgramHost
    {
        public SkillProgramStepOutcome ClaimCurrentUsePhysicalCards(ProgramSkillFrame frame) => engine.ClaimCurrentUsePhysicalCards(frame);
        public void PreventCurrentTargetSlashCancellationByRule(ProgramSkillFrame frame) => engine.PreventCurrentTargetSlashCancellationByRule(frame);
    }
}
