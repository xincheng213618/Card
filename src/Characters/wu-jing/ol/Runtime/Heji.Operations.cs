using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class UseHejiAtCompletedSingleTargetDescriptor() : WuJingDescriptor(SkillProgramEffectOp.UseHejiAtCompletedSingleTarget);
internal sealed class RewardHejiUnconvertedUseDescriptor() : WuJingDescriptor(SkillProgramEffectOp.RewardHejiUnconvertedUse);
public sealed record WuJingHejiOrigin(long ProgramFrameId, long UseId, long ActionId, long IncomingUseId, long IncomingActionId, CardConversionSource Source, string GameplayHash, int ActualTurn, int ActorSeat, int TargetSeat, CardKind Kind, bool RewardEligible)
{
    private IReadOnlyList<WuJingMaterial> _materials = []; private IReadOnlyList<CardConversionSource> _sources = [];
    public IReadOnlyList<WuJingMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardConversionSource> Conversions { get => _sources; init => _sources = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WuJingHejiUseState(WuJingHejiOrigin Origin, bool Rewarded = false);
public sealed record WuJingHejiNativeIssuedEvent(long ProgramFrameId, long UseId, long ActionId, long IncomingUseId, long IncomingActionId, CardConversionSource Source, string GameplayHash, int ActualTurn, int ActorSeat, int TargetSeat, CardKind Kind, int MaterialCount, bool RewardEligible) : IGameEvent;
public sealed record WuJingHejiMaterialIssuedEvent(long ProgramFrameId, long UseId, int Index, WuJingMaterial Material) : IGameEvent;
public sealed record WuJingHejiPaidEvent(long ProgramFrameId, long UseId, long Before, long After) : IGameEvent;
public sealed record WuJingHejiReturnedEvent(long ProgramFrameId, long UseId) : IGameEvent;
