using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record HappyQiaoCardMaximumProof(HappyQiaoDualHealth Before, HappyQiaoDualHealth After);

public sealed partial record CardDamageMaximumHpReplacementReceipt
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HappyQiaoCardMaximumProof? HappyQiaoMaximumProof { get; init; }
}

public sealed partial class GameEngine
{
    private HappyQiaoCardMaximumProof? PrepareHappyQiaoCardMaximumHp(IDamageAttempt attack, int newMaximum)
    {
        if (!HasHappyQiaoHealth(attack.TargetSeat)) return null;
        if (attack is not CardAttackHandle || _resolutionStack.LastOrDefault()?.Id != attack.ResolutionId ||
            attack.DamageWasApplied || attack.DamageAmountFinalized || attack.IsChainPropagation ||
            GetCardAttackState(attack.ResolutionId).MaximumHpReplacement is not null || newMaximum < 0)
            throw new InvalidOperationException("A Qiao card maximum replacement requires the real unpaid native attack.");
        var before = _players[attack.TargetSeat].HappyQiaoHealth!;
        return new(before, before.WithMaximum(newMaximum));
    }

    private bool MatchesHappyQiaoCardMaximumHp(CardDamageMaximumHpReplacementReceipt receipt) =>
        receipt is { HpBefore: null, HpAfter: null, HappyQiaoMaximumProof: { } proof } &&
        HasHappyQiaoHealth(receipt.TargetSeat) &&
        receipt.MaximumHpBefore == proof.Before.MaximumHp && receipt.MaximumHpAfter == proof.After.MaximumHp &&
        receipt.MaximumHpBefore > 0 && receipt.MaximumHpAfter >= 0 &&
        proof.After == proof.Before.WithMaximum(receipt.MaximumHpAfter);

    private bool ApplyHappyQiaoCardMaximumHp(long ownerId, CardDamageMaximumHpReplacementReceipt receipt)
    {
        if (receipt.HappyQiaoMaximumProof is not { } proof) return false;
        if (!MatchesHappyQiaoCardMaximumHp(receipt) || _resolutionStack.LastOrDefault()?.Id != ownerId ||
            GetCardAttackState(ownerId) is not { } attack || attack.MaximumHpReplacement != receipt ||
            attack.SourceSeat != receipt.SourceSeat || attack.TargetSeat != receipt.TargetSeat ||
            attack.DamageWasApplied || attack.DamageAmountFinalized || attack.IsChainPropagation ||
            _players[receipt.TargetSeat].HappyQiaoHealth != proof.Before ||
            ProgramEventHistory<CardDamageReplacedWithMaximumHpLossEvent>().Any(e => e.ParentResolutionId == ownerId))
            throw new InvalidOperationException("A Qiao maximum replacement lost its exact unpaid attack receipt.");
        // Maximum HP is one synchronized rule for both sisters. In particular,
        // this must not assign Hp through a query-selected/default single pool.
        _players[receipt.TargetSeat].MaxHp = receipt.MaximumHpAfter;
        if (_players[receipt.TargetSeat].HappyQiaoHealth != proof.After)
            throw new InvalidOperationException("Maximum HP replacement did not retain both original clamps.");
        return true;
    }

    private bool PublishHappyQiaoCardMaximumHp(long ownerId, CardDamageMaximumHpReplacementReceipt receipt)
    {
        if (receipt.HappyQiaoMaximumProof is not { } proof) return false;
        var target = _players[receipt.TargetSeat];
        if (!MatchesHappyQiaoCardMaximumHp(receipt) || GetCardAttackState(ownerId).MaximumHpReplacement != receipt ||
            ProgramEventHistory<CardDamageReplacedWithMaximumHpLossEvent>().Count(e => e.ParentResolutionId == ownerId && e.Receipt == receipt) != 1 ||
            ProgramEventHistory<HappyQiaoMaximumChangedEvent>().Any(e => e.ParentFrameId == ownerId && e.OwnerSeat == receipt.TargetSeat) ||
            (receipt.MaximumHpAfter == 0
                ? target.IsAlive || target.HappyQiaoHealth != new HappyQiaoDualHealth(0, 0, 0) ||
                  !ProgramEventHistory<HappyQiaoWholeDeathEvent>().Any(e => e.OwnerSeat == receipt.TargetSeat && e.Before == proof.After)
                : target.HappyQiaoHealth != proof.After))
            throw new InvalidOperationException("Qiao maximum facts must publish once from the issued clamps, after zero-maximum native death.");
        PublishHappyQiaoMaximumChange(ownerId, receipt.TargetSeat, proof.Before, proof.After, receipt.Source.SkillId);
        return true;
    }
}
