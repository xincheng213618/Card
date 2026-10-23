using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record HappyQiaoCardMaximumHealthReceipt(long UseId, long? ActionId,
    CardDamageMaximumHpReplacementReceipt Replacement, long DaQiaoChangeId, long XiaoQiaoChangeId);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HappyQiaoCardMaximumHealthReceipt? HappyQiaoCardMaximumHealth { get; init; }
}
public sealed record HappyQiaoCardMaximumHealthStartedEvent(HappyQiaoCardMaximumHealthReceipt Receipt) : IGameEvent;
public sealed record HappyQiaoCardMaximumHealthReturnedEvent(HappyQiaoCardMaximumHealthReceipt Receipt, long? LastChangeId) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TryBeginHappyQiaoCardMaximumHealth(long attackId)
    {
        if (GetCardAttackState(attackId).MaximumHpReplacement is not { HappyQiaoMaximumProof: { } proof } replacement) return false;
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || use.Id != attackId || use.HappyQiaoCardMaximumHealth is not null ||
            !MatchesHappyQiaoCardMaximumHp(replacement) || LastDamageSourceOriginalAttack(attackId) is not CardAttackHandle original ||
            original.SourceSeat != replacement.SourceSeat || original.TargetSeat != replacement.TargetSeat || original.DamageWasApplied || original.DamageAmountFinalized)
            throw new InvalidOperationException("Qiao card maximum observers require the exact unpaid native Use completion.");
        if (ProgramEventHistory<HappyQiaoCardMaximumHealthReturnedEvent>().Any(e => e.Receipt.UseId == attackId)) return false;
        var changes = _pendingHpChanges.Where(c => c.ParentFrameId == attackId && c.TargetSeat == replacement.TargetSeat && c.Kind == HpChangeKind.MaximumHp).ToArray();
        if (proof.Before.MaximumHp == proof.After.MaximumHp)
        {
            if (changes.Length != 0) throw new InvalidOperationException("An unchanged Qiao maximum cannot invent pool changes.");
            return false;
        }
        var da = changes.SingleOrDefault(c => c.HappyQiaoPool == HappyQiaoPool.DaQiao);
        var xiao = changes.SingleOrDefault(c => c.HappyQiaoPool == HappyQiaoPool.XiaoQiao);
        if (changes.Length != 2 || da is null || xiao is null || !HappyQiaoMaximumChangeMatches(attackId, replacement, da) ||
            !HappyQiaoMaximumChangeMatches(attackId, replacement, xiao) || da.Id >= xiao.Id)
            throw new InvalidOperationException("Card maximum replacement lost one of its original ordered pool invoices.");
        var receipt = new HappyQiaoCardMaximumHealthReceipt(use.Id, use.Action?.ActionId, replacement, da.Id, xiao.Id);
        ReplaceRuntimeTop(use with { HappyQiaoCardMaximumHealth = receipt });
        AdvanceEventRulesAndQueueFact(new HappyQiaoCardMaximumHealthStartedEvent(receipt));
        if (TryBeginHpChangedProgramWindow(use.Id, PostEventContinuation.HappyQiaoCardMaximumHealth)) return true;
        // No registered observer qualified; both real invoices were drained by
        // the native window collector, so ordinary card completion can continue.
        ReplaceRuntimeTop(((CardUseFrame)_resolutionStack[^1]) with { HappyQiaoCardMaximumHealth = null });
        AdvanceEventRulesAndQueueFact(new HappyQiaoCardMaximumHealthReturnedEvent(receipt, null));
        return false;
    }

    private bool HappyQiaoMaximumChangeMatches(long useId, CardDamageMaximumHpReplacementReceipt replacement, HpChangeContext change) =>
        replacement.HappyQiaoMaximumProof is { } proof && change is { HappyQiaoPool: { } pool, Kind: HpChangeKind.MaximumHp } &&
        change.ParentFrameId == useId && change.TargetSeat == replacement.TargetSeat && change.SourceSeat is null &&
        change.Amount == Math.Abs(proof.After.MaximumHp - proof.Before.MaximumHp) &&
        change.HpBefore == proof.Before.Hp(pool) && change.HpAfter == proof.After.Hp(pool);

    private bool IsHappyQiaoCardMaximumHealthReturn(ResolutionFrame parent, HpChangedTriggerWindowFrame health) =>
        parent is CardUseFrame { HappyQiaoCardMaximumHealth: { } receipt } use && receipt.UseId == use.Id &&
        use.Action?.ActionId == receipt.ActionId && use.CardAttack?.MaximumHpReplacement == receipt.Replacement &&
        MatchesHappyQiaoCardMaximumHp(receipt.Replacement) && receipt.DaQiaoChangeId > use.Id && receipt.XiaoQiaoChangeId > receipt.DaQiaoChangeId &&
        health.Continuation == PostEventContinuation.HappyQiaoCardMaximumHealth && health.ResumeFrameId == use.Id &&
        health.CardId is null && health.CardKind is null && health.Id == health.Change.Id &&
        health.Id == (health.Change.HappyQiaoPool == HappyQiaoPool.DaQiao ? receipt.DaQiaoChangeId : receipt.XiaoQiaoChangeId) &&
        HappyQiaoMaximumChangeMatches(use.Id, receipt.Replacement, health.Change) &&
        LastDamageSourceOriginalAttack(use.Id) is CardAttackHandle original && original.SourceSeat == receipt.Replacement.SourceSeat &&
        original.TargetSeat == receipt.Replacement.TargetSeat && !original.DamageWasApplied && !original.DamageAmountFinalized &&
        ProgramEventHistory<CardDamageReplacedWithMaximumHpLossEvent>().Count(e => e.ParentResolutionId == use.Id && e.Receipt == receipt.Replacement) == 1 &&
        ProgramEventHistory<HappyQiaoCardMaximumHealthStartedEvent>().Count(e => e.Receipt == receipt) == 1 &&
        !ProgramEventHistory<HappyQiaoCardMaximumHealthReturnedEvent>().Any(e => e.Receipt == receipt);

    private bool HappyQiaoCardMaximumHealthStructuralEdge(ResolutionFrame parent, ResolutionFrame child) =>
        child is HpChangedTriggerWindowFrame hp && IsHappyQiaoCardMaximumHealthReturn(parent, hp);

    private bool HasHappyQiaoCardMaximumHealthContinuation(long attackId)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == attackId);
        return index >= 0 && index + 1 < _resolutionStack.Count &&
            _resolutionStack[index + 1] is HpChangedTriggerWindowFrame hp && IsHappyQiaoCardMaximumHealthReturn(_resolutionStack[index], hp) &&
            Enumerable.Range(index + 2, _resolutionStack.Count - index - 2).All(i =>
                HappyQiaoDamageHealthStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
                HappyQiaoCardMaximumHealthStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]) || IsMovementObserverEdge(i));
    }

    private void AssertHappyQiaoCardMaximumHealth()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(f => f.HappyQiaoCardMaximumHealth is not null))
            if (!HasHappyQiaoCardMaximumHealthContinuation(use.Id))
                throw new InvalidOperationException("Qiao card maximum lost its exact native Use and continuously typed health subtree.");
    }

    private void ReturnHappyQiaoCardMaximumHealth(HpChangedTriggerWindowFrame health)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || !IsHappyQiaoCardMaximumHealthReturn(use, health) ||
            _pendingHpChanges.Any(c => c.ParentFrameId == use.Id && c.TargetSeat == use.HappyQiaoCardMaximumHealth!.Replacement.TargetSeat && c.Kind == HpChangeKind.MaximumHp) ||
            LastDamageSourceOriginalAttack(use.Id) is not CardAttackHandle original)
            throw new InvalidOperationException("The last Qiao maximum observer must return to its original replaced native attack.");
        var receipt = use.HappyQiaoCardMaximumHealth!;
        ReplaceRuntimeTop(use with { HappyQiaoCardMaximumHealth = null });
        AdvanceEventRulesAndQueueFact(new HappyQiaoCardMaximumHealthReturnedEvent(receipt, health.Change.Id));
        CompleteDamageAttack(original);
    }

    private IReadOnlyList<Card> SeparateHappyQiaoCardMaximumHealthChildProcessing(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (processing.Count == 0 || !HasHappyQiaoCardMaximumHealthContinuation(attack.ResolutionId)) return processing;
        var root = _resolutionStack.FindIndex(f => f.Id == attack.ResolutionId);
        var childIds = new HashSet<int>();
        for (var i = root + 2; i + 1 < _resolutionStack.Count; i++)
        {
            if (_resolutionStack[i] is not ProgramSkillFrame { HappyQiao: { Stage: HappyQiaoStage.NativeUse or HappyQiaoStage.ReturnedUse } receipt } frame ||
                _resolutionStack[i + 1] is not CardUseFrame use || !HappyQiaoNativeUseRidesOn(use, frame) || use.Action is not { } action) continue;
            foreach (var cost in action.PhysicalCards)
            {
                if (_cardZones.GetLocation(cost.CardId) != CardLocation.Processing) continue;
                var movement = _cardMovements.LastOrDefault(m => m.CardId == cost.CardId);
                if (movement is null || movement.Sequence <= receipt.Before || movement.From != cost.From || movement.To != CardLocation.Processing ||
                    movement.CardKind != cost.CardKind || movement.Reason != CardMoveReasons.Use && movement.Reason != CardMoveReasons.EquipmentUse ||
                    attack.PhysicalCards.Any(c => c.Id == cost.CardId)) return processing;
                childIds.Add(cost.CardId);
            }
        }
        return childIds.Count == 0 ? processing : processing.Where(card => !childIds.Contains(card.Id)).ToArray();
    }
}
