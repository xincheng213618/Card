using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record FeibaiDeclaredNameEvent(int ActualTurn, int TurnOwnerSeat, long OriginFrameId,
    long? ActionId, int? ProgramInstructionIndex, CardKind EffectiveKind) : IGameEvent
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SetupEpochId { get; init; }
}
public sealed record FeibaiUsePrefixCapturedEvent(long ActionId, int ActorSeat, int ActualTurn,
    int UseSequence, CardKind EffectiveKind, long? PreviousActionId, CardKind? PreviousKind,
    int NameLength, int PreviousNameLength, int X) : IGameEvent
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SetupEpochId { get; init; }
}
public sealed record FeibaiQualification(FeibaiUsePrefixCapturedEvent Prefix, CardConversionSource Source, string GameplayHash);
public sealed record FeibaiQualifiedEvent(FeibaiQualification Qualification) : IGameEvent;
public enum FeibaiStage { Selecting, ObtainChildren, Drawing, DrawChildren }
public sealed record FeibaiReceipt(FeibaiQualification Qualification, long WindowId, long NativeParentId,
    FeibaiStage Stage, long RandomBefore, long RandomAfter)
{
    private readonly IReadOnlyList<int> _sample = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Sample { get => _sample; init => _sample = Array.AsReadOnly(value.ToArray()); }
    public int? ObtainedCardId { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FeibaiReceipt? LeCaiYongFeibai { get; init; }
}
public sealed record FeibaiStartedEvent(long FrameId, long ActionId, CardConversionSource Source, string GameplayHash,
    long WindowId, long NativeParentId, long RandomBefore, long RandomAfter, int SampleCount) : IGameEvent;
public sealed record FeibaiSampleEntityEvent(long FrameId, int Index, int CardId) : IGameEvent;
public sealed record FeibaiObtainedEvent(long FrameId, int CardId, int Sequence) : IGameEvent;
public sealed record FeibaiDisabledEvent(int OwnerSeat, int ActualTurn, long FrameId) : IGameEvent;
public sealed record FeibaiFallbackDrawIssuedEvent(long FrameId, int OwnerSeat, int ActualTurn) : IGameEvent;
public sealed record FeibaiFallbackDrawReturnedEvent(long FrameId, long? AttemptFrameId, int ActualCount,
    int ReplacementCount, long Before, long After) : IGameEvent;
public sealed record FeibaiCompletedEvent(long FrameId, long ActionId, int? ObtainedCardId, bool Fallback) : IGameEvent;

internal interface ILeCaiYongFeibaiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveLeCaiYongFeibaiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLeCaiYongFeibai;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(8));
    public override ISkillProgramEffectHandler Handler { get; } = new LeCaiYongFeibaiHandler();
}
internal sealed class LeCaiYongFeibaiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLeCaiYongFeibai;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<ILeCaiYongFeibaiHost>().Resolve(frame);
}
