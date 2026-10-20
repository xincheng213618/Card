using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record WuZhangFeiUseRankCapturedEvent(long ActionId, int ActorSeat, int ProviderSeat,
    int MaterialCount, int RankSum) : IGameEvent;
public sealed record WuZhangFeiResponseMaterialRank(int CardId, CardKind PrintedKind, CardLocation From, int Rank);
public sealed record WuZhangFeiResponseRankPayment(long CaptureId, long NativeParentId, int ProviderSeat,
    CardKind EffectiveKind, int Before, int RankSum)
{
    private readonly IReadOnlyList<WuZhangFeiResponseMaterialRank> _materials = Array.AsReadOnly(Array.Empty<WuZhangFeiResponseMaterialRank>());
    public IReadOnlyList<WuZhangFeiResponseMaterialRank> Materials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public abstract partial record ResolutionFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WuZhangFeiResponseRankPayment? WuZhangFeiResponseRanks { get; init; }
}
public sealed record WuZhangFeiResponseRankPreparedEvent(long CaptureId, long NativeParentId, int ProviderSeat,
    CardKind EffectiveKind, int Before, int MaterialCount, int RankSum) : IGameEvent;
public sealed record WuZhangFeiResponseMaterialRankEvent(long CaptureId, int Index,
    WuZhangFeiResponseMaterialRank Material) : IGameEvent;
public sealed record WuZhangFeiResponseRankBoundEvent(long CaptureId, long NativeParentId, long ActionId) : IGameEvent;
public sealed record WuZhangFeiUseQualification(long ActionId, CardConversionSource Source,
    string GameplayHash, int ProviderSeat, int MaterialCount, int RankSum);
public sealed record WuZhangFeiUseQualifiedEvent(WuZhangFeiUseQualification Qualification) : IGameEvent;
public enum WuZhangFeiUseStage { Selecting, MovementChildren }
public sealed record WuZhangFeiUseReceipt(WuZhangFeiUseQualification Qualification,
    long WindowId, long NativeParentId, WuZhangFeiUseStage Stage, long RandomBefore, long RandomAfter)
{
    private readonly IReadOnlyList<int> _sample = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Sample { get => _sample; init => _sample = Array.AsReadOnly(value.ToArray()); }
    public int? ObtainedCardId { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WuZhangFeiUseReceipt? WuZhangFeiUse { get; init; }
}
public sealed record WuZhangFeiUseStartedEvent(long FrameId, WuZhangFeiUseQualification Qualification,
    long WindowId, long NativeParentId, long RandomBefore, long RandomAfter, int SampleCount) : IGameEvent;
public sealed record WuZhangFeiSampleEntityEvent(long FrameId, int Index, int CardId) : IGameEvent;
public sealed record WuZhangFeiSampleObtainedEvent(long FrameId, int CardId, int MovementSequence) : IGameEvent;
public sealed record WuZhangFeiUseCompletedEvent(long FrameId, long ActionId, int? ObtainedCardId) : IGameEvent;

internal interface IWuZhangFeiUseHost { SkillProgramStepOutcome Sample(ProgramSkillFrame frame); }
internal sealed class ResolveWuZhangFeiUseSampleDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhangFeiUseSample;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(8));
    public override ISkillProgramEffectHandler Handler { get; } = new WuZhangFeiUseHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class WuZhangFeiUseHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhangFeiUseSample;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IWuZhangFeiUseHost>().Sample(frame);
}
