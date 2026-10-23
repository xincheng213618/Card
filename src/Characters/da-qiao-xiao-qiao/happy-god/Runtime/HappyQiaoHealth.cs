using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum HappyQiaoPool { DaQiao, XiaoQiao }
public sealed record HappyQiaoDualHealth(int DaQiaoHp, int XiaoQiaoHp, int MaximumHp)
{
    public int Hp(HappyQiaoPool pool) => pool == HappyQiaoPool.DaQiao ? DaQiaoHp : XiaoQiaoHp;
    public bool AnyWounded => DaQiaoHp < MaximumHp || XiaoQiaoHp < MaximumHp;
    public bool AnyDying => DaQiaoHp <= 0 || XiaoQiaoHp <= 0;
    public int HandLimitBase => checked(DaQiaoHp + XiaoQiaoHp);
    internal HappyQiaoDualHealth WithHp(HappyQiaoPool pool, int hp) => pool == HappyQiaoPool.DaQiao ? this with { DaQiaoHp = hp } : this with { XiaoQiaoHp = hp };
    internal HappyQiaoDualHealth WithMaximum(int maximum) => this with
    { MaximumHp = maximum, DaQiaoHp = Math.Min(DaQiaoHp, maximum), XiaoQiaoHp = Math.Min(XiaoQiaoHp, maximum) };
}

public sealed partial class CharacterState
{
    public HappyQiaoDualHealth? HappyQiaoHealth { get; private set; }
    internal Func<CharacterState, HappyQiaoPool?>? HappyQiaoPoolQuery { get; set; }
    private Func<CharacterState, HappyQiaoPool?>? _happyQiaoHealthWritePool;
    private bool _happyQiaoHealthSealed;

    internal void BindHappyQiaoHealth(Func<CharacterState, HappyQiaoPool?> query, Func<CharacterState, HappyQiaoPool?> writePool)
    {
        if (General.Id != "happy:shen-da-qiao-xiao-qiao") return;
        HappyQiaoHealth ??= new(_hp, _hp, _maxHp);
        HappyQiaoPoolQuery = query;
        _happyQiaoHealthWritePool = writePool;
        _happyQiaoHealthSealed = true;
    }

    private int ReadHappyQiaoHpOrOrdinary(int ordinary)
    {
        if (HappyQiaoHealth is not { } health) return ordinary;
        if (HappyQiaoPoolQuery?.Invoke(this) is { } pool) return health.Hp(pool);
        if (health.DaQiaoHp == health.XiaoQiaoHp) return health.DaQiaoHp;
        throw new InvalidOperationException("Independent Qiao HP requires an explicit owning health/query receipt; it has no implicit sum, minimum or default sister.");
    }

    private bool TryWriteHappyQiaoHp(int hp)
    {
        if (HappyQiaoHealth is not { } health) return false;
        if (_happyQiaoHealthWritePool?.Invoke(this) is { } pool)
            HappyQiaoHealth = health.WithHp(pool, hp);
        else if (!_happyQiaoHealthSealed)
            HappyQiaoHealth = health with { DaQiaoHp = hp, XiaoQiaoHp = hp };
        else throw new InvalidOperationException("Independent Qiao HP cannot be changed without the original frame's selected pool.");
        return true;
    }

    private void SynchronizeHappyQiaoMaximum(int maximum)
    {
        if (HappyQiaoHealth is { } health) HappyQiaoHealth = health.WithMaximum(maximum);
    }

    internal void CompleteHappyQiaoWholeDeath()
    {
        if (HappyQiaoHealth is not { } health) throw new InvalidOperationException("Whole Qiao death requires the actual dual-health character.");
        HappyQiaoHealth = health with { DaQiaoHp = 0, XiaoQiaoHp = 0 };
    }
}

internal static class HappyQiaoRecoveryProof
{
    internal static int HpBefore(RecoveryReplacementFrame frame)
    {
        if (frame.Attempt.HpBefore is { } ordinary && frame.Attempt.HappyQiaoBefore is null) return ordinary;
        if (frame.Attempt.HpBefore is null && frame.Attempt.HappyQiaoBefore is { } before &&
            frame.HappyQiaoHealth is { Pool: { } pool, Released: false } r && r.ParentFrameId == frame.Id &&
            r.TargetSeat == frame.Attempt.TargetSeat && r.Kind == HpChangeKind.Recovery && r.Before == before)
            return before.Hp(pool);
        throw new InvalidOperationException("Recovery HP-before requires its ordinary invoice or exact selected Qiao request.");
    }
}

public enum HappyQiaoHealthReturnKind { Damage, Recovery, Program, Query, RecoveryRequest, RecoverTo }
public sealed record HappyQiaoHealthReceipt(long Id, long ParentFrameId, ResolutionFrameKind ParentKind,
    int TargetSeat, int? SourceSeat, HpChangeKind Kind, int Amount, HappyQiaoHealthReturnKind ReturnKind,
    string OccurrenceKey, int InstructionIndex, HappyQiaoDualHealth Before, long? AttackResolutionId = null,
    HappyQiaoPool? Pool = null, bool Applied = false, int? HpAfter = null, bool Released = false);
public sealed record HappyQiaoQueryReceipt(long Id, long ParentFrameId, ResolutionFrameKind ParentKind, int TargetSeat,
    string QueryKey, int InstructionIndex, HappyQiaoDualHealth Before, HappyQiaoPool? Pool = null);

public abstract partial record ResolutionFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HappyQiaoHealthReceipt? HappyQiaoHealth { get; init; }
    private readonly IReadOnlyList<HappyQiaoQueryReceipt>? _happyQiaoQueries;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<HappyQiaoQueryReceipt>? HappyQiaoQueries { get => _happyQiaoQueries; init => _happyQiaoQueries = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HappyQiaoActiveQueryKey { get; init; }
}
public sealed partial record DyingFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HappyQiaoPool? HappyQiaoPool { get; init; }
}
public sealed partial record RecoveryFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HappyQiaoPool? HappyQiaoPool { get; init; }
}
[JsonDerivedType(typeof(HappyQiaoHealthChoiceFrame), "happy-qiao-health-choice")]
public abstract partial record ResolutionFrame;
public sealed record HappyQiaoHealthChoiceFrame(long Id, long ParentFrameId, long RequestId, int OwnerSeat,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect, bool IsQuery = false)
    : ResolutionFrame(Id, ResolutionFrameKind.HappyQiaoHealthChoice, Step), IProgramChildResolutionFrame;

public sealed record HappyQiaoHealthRequestedEvent(long RequestId, long ParentFrameId, int OwnerSeat,
    HpChangeKind Kind, int Amount, HappyQiaoHealthReturnKind ReturnKind, string OccurrenceKey) : IGameEvent;
public sealed record HappyQiaoHealthPoolSelectedEvent(long RequestId, long ParentFrameId, int OwnerSeat,
    HappyQiaoPool Pool, int HpBefore, int MaximumBefore) : IGameEvent;
public sealed record HappyQiaoHealthAppliedEvent(long RequestId, long ParentFrameId, int OwnerSeat,
    HappyQiaoPool Pool, HpChangeKind Kind, int HpBefore, int HpAfter) : IGameEvent;
public sealed record HappyQiaoHealthSelectionReleasedEvent(long RequestId, long ParentFrameId) : IGameEvent;
public sealed record HappyQiaoWholeDeathEvent(long DeathFrameId, int OwnerSeat, HappyQiaoDualHealth Before) : IGameEvent;
public sealed record HappyQiaoMaximumChangedEvent(long ParentFrameId, int OwnerSeat, HappyQiaoDualHealth Before, HappyQiaoDualHealth After, string SourceId) : IGameEvent;
