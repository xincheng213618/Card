using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record BingzhongOriginalTargetEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record BingzhongTargetSelectedEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record BingzhongTargetsAppliedEvent(long FrameId, long UseId, long ActionId, CardConversionSource Source, string GameplayHash,
    int X, int Ordinal, int OriginalActor, int ProviderSeat, int OriginalCount, int AddedCount) : IGameEvent;
public sealed record BingzhongRedirectedEvent(long FrameId, long UseId, long ActionId, CardConversionSource Source, string GameplayHash,
    int InstructionIndex, int TargetIndex, int PreviousSeat, int Seat) : IGameEvent;
public sealed record BingzhongDrawPaidEvent(long FrameId, int X, int ActualCount, long Before, long After) : IGameEvent;
public sealed record BingzhongTargetTail(long FrameId, long UseId, long ActionId, CardConversionSource Source, string GameplayHash,
    int X, int Ordinal, int OriginalActor, int ProviderSeat, IReadOnlyList<int> OriginalTargets, IReadOnlyList<int> AddedTargets)
{
    private IReadOnlyList<int> _original = Array.AsReadOnly(OriginalTargets.ToArray());
    public IReadOnlyList<int> OriginalTargets { get => _original; init => _original = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _added = Array.AsReadOnly(AddedTargets.ToArray());
    public IReadOnlyList<int> AddedTargets { get => _added; init => _added = Array.AsReadOnly(value.ToArray()); }
}
internal sealed class ResolveBingzhongExtraTargetsDescriptor : YangFuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBingzhongExtraTargets; }
internal sealed class ResolveBingzhongAfterDamageDrawDescriptor : YangFuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBingzhongAfterDamageDraw; }
