using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record SunHaoCanshiGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwner, long DrawWindowId, int X) : IGameEvent;
public sealed record SunHaoCanshiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long GrantId, int ActualTurn, int TurnOwner, long WindowId, long ActionId, [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] long? UseId, int TargetCount, int GrantCount) : IGameEvent;
public sealed record SunHaoCanshiOriginalTargetEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record SunHaoCanshiDiscardedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Before, long After, int Index = 0) : IGameEvent;
public sealed record SunHaoCanshiFrozenGrantEvent(long FrameId, int Index, long GrantId) : IGameEvent;
public sealed record SunHaoCanshiSkippedEvent(long FrameId, int Index, long GrantId) : IGameEvent;
public sealed record SunHaoCanshiCompletedEvent(long FrameId, bool Paid) : IGameEvent;
public enum SunHaoCanshiStage { SelectDiscard, DiscardChildren }
public sealed record SunHaoCanshiReceipt(CardConversionSource Source, string GameplayHash, long GrantId, int ActualTurn, int TurnOwner, long WindowId,
    long ActionId, [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] long? UseId, SunHaoCanshiStage Stage, IReadOnlyList<int> OriginalTargets,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] SunHaoCanshiDiscardedEvent? Payment = null, int Cursor = 0)
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(OriginalTargets.ToArray());
    public IReadOnlyList<int> OriginalTargets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<long> _grants = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> GrantIds { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _grain = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> WoodenOxGrain { get => _grain; init => _grain = Array.AsReadOnly(value.ToArray()); }
}
internal sealed class GrantSunHaoCanshiDrawObligationDescriptor() : SunHaoDescriptor(SkillProgramEffectOp.GrantSunHaoCanshiDrawObligation, SkillProgramTriggerWindow.DrawPhaseStarting) { }
internal sealed class ResolveSunHaoCanshiUseDiscardDescriptor() : SunHaoDescriptor(SkillProgramEffectOp.ResolveSunHaoCanshiUseDiscard, SkillProgramTriggerWindow.CardUseCommitted) { }
