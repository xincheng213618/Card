namespace CardGame.Core;

public sealed record PaidFactionRequestCostRecovery(long RequestOwnerFrameId, int RequestOwnerSeat,
    FactionCardRequestPurpose Purpose, FactionResponsePolicySource PolicySource,
    int CostCardId, CardLocation CostLocation);

public sealed partial class GameEngine
{
    private bool TryPausePaidFactionRequestCost(FactionCardRequestHandle request, Card card, CardLocation from)
    {
        if (_resolutionStack.LastOrDefault() is not { PendingRecoveryAttempts.Count: > 0 } producer) return false;
        if (!request.CostPaid || !request.AwaitingProviders || request.CandidateIndex != 0 ||
            request.PolicySource is not { DiscardCost: 1 } policy || producer.PaidFactionRequestCostRecovery is not null)
            throw new InvalidOperationException("Faction request recovery lost its exact newly paid cost.");
        ReplaceRuntimeTop(producer with { PaidFactionRequestCostRecovery =
            new(request.OwnerFrameId, request.OwnerSeat, request.Purpose, policy, card.Id, from) });
        if (!TryBeginHpChangedProgramWindow(producer.Id, PostEventContinuation.FactionRequestCost))
            ContinuePaidFactionRequestCost(producer.Id);
        return true;
    }

    private void ContinuePaidFactionRequestCost(long producerId)
    {
        if (_resolutionStack.LastOrDefault() is not { } producer || producer.Id != producerId ||
            producer.PaidFactionRequestCostRecovery is not { } receipt ||
            ActiveFactionCardRequest is not { } request || !PaidFactionRequestMatches(request, receipt))
            throw new InvalidOperationException("Paid faction request lost its exact producer and frozen request.");
        if (TryBeginHpChangedProgramWindow(producerId, PostEventContinuation.FactionRequestCost) ||
            TryBeginCardsMovedProgramWindow(producerId)) return;
        ReplaceRuntimeTop(producer with { PaidFactionRequestCostRecovery = null });
        var owner = _players[request.OwnerSeat];
        var sourceLive = CardPolicies(owner, SkillProgramCardPolicyKind.FactionResponseRequest, requiredKind: request.RequiredKind)
            .Any(item => item.Source.SkillId == receipt.PolicySource.SkillId &&
                item.Source.SkillInstanceId == receipt.PolicySource.SkillInstanceId &&
                item.Policy.Id == receipt.PolicySource.PolicyId);
        if (_winner != Winner.None || !owner.IsAlive)
        {
            CancelPaidFactionRequestAfterRecovery(request);
            return;
        }
        if (request.TargetSeat is { } targetSeat && !_players[targetSeat].IsAlive &&
            (request.IsProgramSkillUse || request.IsQinglongCrescentBladeUse || request.IsBorrowedSwordUse))
        {
            if (request.IsBorrowedSwordUse) CompleteFactionSlashFailure(request);
            else CancelPaidFactionRequestAfterRecovery(request);
            return;
        }
        if (!sourceLive) CompleteFactionSlashFailure(request);
        else AdvanceFactionSlashCandidate();
    }

    private static bool PaidFactionRequestMatches(FactionCardRequestHandle request, PaidFactionRequestCostRecovery receipt) =>
        request.OwnerFrameId == receipt.RequestOwnerFrameId && request.OwnerSeat == receipt.RequestOwnerSeat &&
        request.Purpose == receipt.Purpose && request.PolicySource == receipt.PolicySource &&
        request.CostPaid && !request.ProviderRewarded && request.AwaitingProviders &&
        request.CandidateIndex == 0 && request.ActiveAttack is null && !request.AwaitingZhuqueFanChoice;

    private bool HasPaidFactionRequestCostRecovery(FactionCardRequestHandle? request)
    {
        if (request is null) return false;
        var producerIndex = _resolutionStack.FindLastIndex(frame => frame.PaidFactionRequestCostRecovery is { } receipt &&
            PaidFactionRequestMatches(request, receipt));
        if (producerIndex < 0) return false;
        for (var index = producerIndex + 1; index < _resolutionStack.Count; index++)
        {
            var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
            if (child is RecoveryReplacementFrame recovery && recovery.ParentFrameId == parent.Id &&
                recovery.Return.ResumeFrameId == parent.Id && recovery.Return.Continuation is
                    PostEventContinuation.FactionRequestCost or PostEventContinuation.RecoveryReplacement) continue;
            if (child is HpChangedTriggerWindowFrame hp && hp.ResumeFrameId == parent.Id && hp.Continuation is
                PostEventContinuation.FactionRequestCost or PostEventContinuation.RecoveryReplacement) continue;
            if (child is CardsMovedTriggerWindowFrame movement && movement.Batch.ParentFrameId == parent.Id &&
                (movement.ResumeFactionRequestCostFrameId == parent.Id || movement.ResumeRecoveryReplacementFrameId == parent.Id)) continue;
            if (DamageFrameRidesOn(child, parent) || DamageObserverRidesOn(child, parent) ||
                PileEquipmentFrameRidesOn(child, parent) || RandomEquipmentFrameRidesOn(child, parent)) continue;
            if (child is DyingFrame dying && parent is ProgramSkillFrame program &&
                dying.ParentFrameId == program.Id && dying.ResumesProgramSkill) continue;
            if (child is ProgramSkillFrame { WindowContext: { } response } && parent is DyingFrame dyingParent &&
                response.ParentFrameId == dyingParent.Id && response.Window is
                    SkillProgramTriggerWindow.DyingResponse or SkillProgramTriggerWindow.SelfDyingResponse) continue;
            return false;
        }
        return true;
    }

    private void CancelPaidFactionRequestAfterRecovery(FactionCardRequestHandle request)
    {
        var purpose = request.Purpose;
        var responseAttack = request.ResponseAttack;
        var borrowed = request.BorrowedSword;
        var qinglong = request.QinglongCrescentBlade;
        ActiveFactionCardRequest = null;
        if (request.IsProgramSkillUse)
        {
            if (request.IsAssistedProgramUse)
                CommitProgramChoiceResult(request.ProgramSkillFrameId!.Value, request.AssistedResultBind!, "declined",
                    request.OwnerSeat, "已付请求在回复子链后结束。");
            AdvanceRuntimeProgram(request.ProgramSkillFrameId!.Value);
            return;
        }
        AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(request.ResolutionId, request.OwnerSeat,
            false, null, null, null, request.IsBorrowedSwordUse || request.IsQinglongCrescentBladeUse, request.TargetSeat));
        if (_resolutionStack.LastOrDefault() is ResponseWindowFrame response)
            PopResponseWindow(response.ParentFrameId);
        if (purpose == FactionCardRequestPurpose.BorrowedSwordUse)
        { FinishBorrowedSword(borrowed!); return; }
        if (purpose == FactionCardRequestPurpose.QinglongCrescentBladeUse)
        {
            ActiveQinglongCrescentBlade = null;
            SetCardUseStep(qinglong!.Attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(qinglong.Attack.ResolutionId,
                qinglong.Attack.SourceSeat, qinglong.Attack.TargetSeat, false, [], null));
            CompleteAttack(qinglong.Attack); return;
        }
        CompleteAttack(responseAttack!);
    }

    private void AssertPaidFactionRequestCostRecoveries()
    {
        foreach (var producer in _resolutionStack.Where(frame => frame.PaidFactionRequestCostRecovery is not null))
        {
            var receipt = producer.PaidFactionRequestCostRecovery!;
            if (ActiveFactionCardRequest is not { } request || !PaidFactionRequestMatches(request, receipt) ||
                receipt.CostLocation != CardLocation.Equipment(receipt.RequestOwnerSeat) ||
                !_cardMovements.Any(move => move.CardId == receipt.CostCardId && move.From == receipt.CostLocation &&
                    move.To == CardLocation.DiscardPile && move.Reason.Value == "program.faction-request.cost") ||
                !HasPaidFactionRequestCostRecovery(request))
                throw new InvalidOperationException("Paid faction request recovery changed its committed cost or exact descendant chain.");
        }
    }
}

