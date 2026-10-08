namespace CardGame.Core;

public sealed partial class GameEngine
{
    private Card ReadDrawFundedDistinctBasicUseAppearance(long ownerFrameId, CardAppearanceReference appearance) =>
        appearance.Id == 0 && IsDrawFundedDistinctBasicUse(ownerFrameId) && LifecycleCardUse(ownerFrameId)!.CardKind == appearance.Kind &&
            appearance.Suit == Suit.None && appearance.Rank == 0 ? DrawFundedDistinctBasicRepresentation(ownerFrameId) : ReadTieredRoundUseAppearance(ownerFrameId, appearance);

    private bool SkipDrawFundedDistinctBasicFinishedMovement(long frameId, Card card)
    {
        if (card.Id != 0 || !IsDrawFundedDistinctBasicUse(frameId)) return false;
        if (card.Kind != LifecycleCardUse(frameId)!.CardKind) throw new InvalidOperationException("A paid logical basic card changed its finished name.");
        return true;
    }

    private bool IsDrawFundedDistinctBasicAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { } use || !IsIssuedDrawFundedDistinctBasicUse(use) || attack.Card is not null ||
            attack.PhysicalCards.Count != 0 || attack.EffectiveCardKind != use.CardKind || !IsSlashCard(use.CardKind) || attack.SourceSeat != use.SourceSeat) return false;
        var p = use.DrawFundedDistinctBasicUse!.Payment; var exactParentIds = Array.Empty<int>();
        if (p.Intent == DrawFundedDistinctBasicIntent.BorrowedSword)
        {
            if (ActiveBorrowedSword is not { ActiveAttack: { } child } b || !SameAttackOwner(child, attack) || b.ResolutionId != p.ParentFrameId ||
                b.WeaponOwnerSeat != p.Source.OwnerSeat || b.SlashTargetSeat != p.TargetSeat || use.Action?.ParentActionId != LifecycleCardUse(b.ResolutionId)?.Action?.ActionId) return false;
            exactParentIds = GetCardUsePhysicalCards(b.ResolutionId).Select(c => c.Id).ToArray();
        }
        else if (p.Intent == DrawFundedDistinctBasicIntent.Qinglong)
        {
            if (ActiveQinglongFollowup is not { } q || q.OuterResolutionId != p.ParentFrameId ||
                CompleteProgramEventHistory().OfType<QinglongCrescentBladeResolvedEvent>().LastOrDefault(e => e.ResolutionId == p.ParentFrameId) is not
                    { Used: true, SlashCardIds.Count: 0 } fact || fact.SourceSeat != p.Source.OwnerSeat || fact.TargetSeat != p.TargetSeat || fact.EffectiveSlashKind != p.EffectiveKind) return false;
            exactParentIds = q.NextAttack?.PhysicalCards.Where(c => _cardZones.GetLocation(c.Id) == CardLocation.Processing).Select(c => c.Id).ToArray() ?? [];
        }
        else if (p.Intent is DrawFundedDistinctBasicIntent.ProgramSlash or DrawFundedDistinctBasicIntent.ProgramNearestSlash or DrawFundedDistinctBasicIntent.AssistedSlash or DrawFundedDistinctBasicIntent.NearestLegalSlash)
        {
            if (attack.ProgramSkillCardUseFrameId != p.ParentFrameId || _resolutionStack.SingleOrDefault(f => f.Id == p.ParentFrameId) is not ProgramSkillFrame parent ||
                !DrawFundedDistinctBasicProgramActor(parent, p)) return false;
            var parentIndex = _resolutionStack.FindIndex(f => f.Id == parent.Id);
            exactParentIds = _resolutionStack.Take(parentIndex).OfType<CardUseFrame>().SelectMany(f => f.PhysicalCardIds ?? [])
                .Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).Distinct().ToArray();
        }
        else if (p.Intent != DrawFundedDistinctBasicIntent.Play || attack.ProgramSkillCardUseFrameId is not null) return false;
        return exactParentIds.All(id => processing.Any(c => c.Id == id)) && processing.Select(c => c.Id).Order().SequenceEqual(exactParentIds.Order());
    }

    private void AssertDrawFundedDistinctBasicUseCompletion(CardUseFrame use)
    {
        if (use.DrawFundedDistinctBasicUse is null && use.DrawFundedDistinctBasicResponse is null) return;
        if (use.DrawFundedDistinctBasicUse is not { } receipt || use.DrawFundedDistinctBasicResponse is not null || !IsIssuedDrawFundedDistinctBasicUse(use) || use.Step != ResolutionFrameStep.Completed ||
            _resolutionStack.LastOrDefault()?.Id != use.Id || use.PendingRecoveryAttempts is { Count: > 0 } || use.RecoveryPaidContinuation is not null ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.ParentFrameId == use.Id))
            throw new InvalidOperationException("A draw-funded Use cannot return before its exact native completion children.");
        var p = receipt.Payment;
        if (p.Intent is DrawFundedDistinctBasicIntent.ProgramSlash or DrawFundedDistinctBasicIntent.ProgramNearestSlash or DrawFundedDistinctBasicIntent.AssistedSlash or DrawFundedDistinctBasicIntent.NearestLegalSlash)
        {
            if (_resolutionStack.SingleOrDefault(f => f.Id == p.ParentFrameId) is not ProgramSkillFrame parent ||
                !DrawFundedDistinctBasicProgramActor(parent, p) || use.CardAttack?.ProgramSkillCardUseFrameId != parent.Id)
                throw new InvalidOperationException("A paid program Slash lost its original actor/instruction return.");
        }
    }

    private bool DrawFundedDistinctBasicProgramActor(ProgramSkillFrame parent, DrawFundedDistinctBasicPayment payment)
    {
        if (parent.Id != payment.ParentFrameId || parent.Id != payment.RequestFrameId || parent.InstructionIndex != payment.Cursor ||
            parent.InstructionIndex < 1 || _contentRegistry.Skills.GetValueOrDefault(parent.SkillId)?.Program is not { } definition ||
            definition.GameplayHash != parent.GameplayHash) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(parent, definition);
        if (parent.InstructionIndex > plan.Instructions.Count) return false;
        var op = plan.GetPausedInstruction(parent.InstructionIndex).Effect.Op;
        if (payment.Intent == DrawFundedDistinctBasicIntent.ProgramNearestSlash)
        {
            var key = "participant-cursor-" + (parent.InstructionIndex - 1);
            var cursors = parent.NumberBindings.Where(binding => binding.Name == key).ToArray();
            // The native participant walker advances this cursor before publishing
            // its request. It deliberately retains the activation's original targets.
            return op == SkillProgramEffectOp.RequestSlashByNearest && parent.ReexecuteParticipantInstruction &&
                cursors is [var cursor] && cursor.Value >= 1 && cursor.Value < _playerCount &&
                payment.Source.OwnerSeat == (parent.OwnerSeat + cursor.Value) % _playerCount;
        }
        return parent.SelectedTargetSeats.SequenceEqual([payment.Source.OwnerSeat]) && (payment.Intent switch
        {
            DrawFundedDistinctBasicIntent.ProgramSlash => op == SkillProgramEffectOp.RequestSlashByTarget,
            DrawFundedDistinctBasicIntent.AssistedSlash => op == SkillProgramEffectOp.RequestSlashAgainstChosenTarget,
            DrawFundedDistinctBasicIntent.NearestLegalSlash => op == SkillProgramEffectOp.RequestLegalSlashByNearest,
            _ => false
        });
    }

    private bool IsDrawFundedDistinctBasicProgramSelection(ProgramSkillFrame parent, SkillProgramEffect effect, DrawFundedDistinctBasicFrame paid)
    {
        if (!ValidDrawFundedDistinctBasicPayment(paid.Payment) || !DrawFundedDistinctBasicProgramActor(parent, paid.Payment) ||
            paid.OriginalDecision.PlayerSeat != paid.Payment.Source.OwnerSeat || paid.OriginalDecision.TargetSeat != paid.Payment.Source.OwnerSeat ||
            paid.OriginalDecision.PromptId != paid.Payment.OriginalPromptId || paid.OriginalDecision.Revision != paid.Payment.OriginalRevision) return false;
        var paused = ProgramInstructionResolver.Default.Resolve(parent, _contentRegistry.GetSkill(parent.SkillId).Program!).GetPausedInstruction(parent.InstructionIndex).Effect;
        if (paused != effect) return false;
        if (paid.Payment.Intent != DrawFundedDistinctBasicIntent.ProgramNearestSlash) return true;
        var native = DrawFundedDistinctBasicNativeDecision(RequestedDeckBasicNativeDecision(paid.OriginalDecision));
        return native.Kind == DecisionKind.ProgramTrigger && native.Choices.Count > 0 && native.Choices.All(choice =>
            choice.Parameters.GetValueOrDefault("frame-id") == parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
            choice.Parameters.GetValueOrDefault("seat") == paid.Payment.Source.OwnerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture)) &&
            native.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "request-slash-nearest-decline");
    }
    private void ReturnDrawFundedDistinctBasicUse(CardUseFrame completed)
    {
        if (completed.DrawFundedDistinctBasicUse is not { } receipt) return;
        if (_resolutionStack.Any(f => f.Id == completed.Id) || !IsDrawFundedDistinctBasicIssued(receipt))
            throw new InvalidOperationException("A draw-funded Use return must follow the exact owning-frame completion.");
        EmitDrawFundedDistinctBasicReturned(receipt);
    }
    private void EmitDrawFundedDistinctBasicReturned(DrawFundedDistinctBasicUseReceipt receipt)
    {
        if (CompleteProgramEventHistory().OfType<DrawFundedDistinctBasicReturnedEvent>().Any(e => e.PaymentFrameId == receipt.Payment.PaymentFrameId))
            throw new InvalidOperationException("A paid basic Use returned more than once.");
        var p = receipt.Payment;
        AdvanceEventRulesAndQueueFact(new DrawFundedDistinctBasicReturnedEvent(p.PaymentFrameId, receipt.OwnerFrameId, receipt.CardActionId, p.Source.OwnerSeat, p.Intent, p.EffectiveKind));
    }

    private bool DrawFundedDistinctBasicDyingRescueRide(int dyingIndex, DyingFrame dying)
    {
        if (dyingIndex < 0 || dyingIndex + 1 >= _resolutionStack.Count || _resolutionStack[dyingIndex] is not DyingFrame exact || exact != dying || ActiveDying?.FrameId != dying.Id) return false;
        if (_resolutionStack[dyingIndex + 1] is DrawFundedDistinctBasicFrame paid)
            return paid.Payment.Intent == DrawFundedDistinctBasicIntent.Dying && paid.Payment.ParentFrameId == dying.Id &&
                paid.Payment.RequestFrameId == dying.Id && paid.Payment.Source.OwnerSeat == dying.ResponderSeat && paid.Payment.Cursor == dying.ResponderIndex &&
                paid.Payment.TargetSeat == dying.VictimSeat && ValidDrawFundedDistinctBasicPayment(paid.Payment) && DrawFundedDistinctBasicPaidSuffix(dyingIndex + 1);
        if (_resolutionStack[dyingIndex + 1] is not CardUseFrame rescue || !IsIssuedDrawFundedDistinctBasicUse(rescue) ||
            rescue.DrawFundedDistinctBasicUse!.Payment is not { Intent: DrawFundedDistinctBasicIntent.Dying } p || p.ParentFrameId != dying.Id || p.Cursor != dying.ResponderIndex ||
            rescue.DyingResponse is not { } token || token.ResolutionId != dying.Id || token.ResponderSeat != dying.ResponderSeat || rescue.SourceSeat != dying.ResponderSeat ||
            rescue.CardKind is not (CardKind.Peach or CardKind.Alcohol) || !rescue.TargetSeats.SequenceEqual([dying.VictimSeat]) ||
            !rescue.Action!.EffectiveDesignatedTargetSeats.SequenceEqual([dying.VictimSeat]) ||
            (rescue.CardKind == CardKind.Peach ? !token.UsedPeach || token.PeachCardId != 0 || token.UsedAlcohol || token.AlcoholCardId is not null :
                !token.UsedAlcohol || token.AlcoholCardId != 0 || token.UsedPeach || token.PeachCardId is not null || rescue.SourceSeat != dying.VictimSeat) || token.UsedPeachPhysicalCardKind is not null) return false;
        for (var index = dyingIndex + 2; index < _resolutionStack.Count; index++)
            if (!ParticipantHandRescueObserverRide(_resolutionStack[index], _resolutionStack[index - 1], rescue)) return false;
        return true;
    }
}
