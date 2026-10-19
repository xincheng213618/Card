using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record WuZhangFeiRankCycleChangedEvent(int OwnerSeat, long CycleId, int RankMask,
    int CompletionTarget, int PaidGates, string Reason, long ParentId) : IGameEvent;
public sealed record WuZhangFeiXianlueObservedEntity(int CardId, CardKind PrintedKind, int Rank);
public enum WuZhangFeiXianlueStage { Observing, Drawing, DrawChildren }
public sealed record WuZhangFeiXianlueReceipt(CardConversionSource Source, string GameplayHash,
    int TargetSeat, int ActualTurn, int PhaseInstance, long OriginCycleId, int BeforeMask,
    int ObservedMask, int EarnedGates, WuZhangFeiXianlueStage Stage)
{
    private readonly IReadOnlyList<WuZhangFeiXianlueObservedEntity> _hand = Array.AsReadOnly(Array.Empty<WuZhangFeiXianlueObservedEntity>());
    public IReadOnlyList<WuZhangFeiXianlueObservedEntity> Hand
    { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    public int ReturnedGates { get; init; }
    public int? CurrentGate { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WuZhangFeiXianlueReceipt? WuZhangFeiXianlue { get; init; }
}
public sealed record WuZhangFeiXianlueStartedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int TargetSeat, int ActualTurn, int PhaseInstance, long OriginCycleId,
    int BeforeMask, int HandCount) : IGameEvent;
public sealed record WuZhangFeiXianlueObservedEvent(long FrameId, int OwnerSeat, int TargetSeat,
    long OriginCycleId, int BeforeMask, int AddedMask, int EarnedGates) : IGameEvent;
public sealed record WuZhangFeiXianlueDrawIssuedEvent(long FrameId, long OriginCycleId, int Gate) : IGameEvent;
public sealed record WuZhangFeiXianlueDrawReturnedEvent(long FrameId, long OriginCycleId, int Gate,
    int ActualCount, int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record WuZhangFeiXianlueCompletedEvent(long FrameId, long OriginCycleId) : IGameEvent;
public sealed record WuZhangFeiDyingReducedEvent(long DyingFrameId, long DamageFrameId, int OwnerSeat,
    int VictimSeat, long CycleId, int BeforeTarget, int AfterTarget) : IGameEvent;
public sealed record WuZhangFeiHaoxianResetEvent(int OwnerSeat, long CompletedCycleId, long ParentId,
    string UsageGroup, int Before, int After) : IGameEvent;

internal interface IWuZhangFeiXianlueHost { SkillProgramStepOutcome Observe(ProgramSkillFrame frame); }
internal interface IWuZhangFeiXianlueSelection : IProgramSelectionQuery
{ bool CanObserve(int ownerSeat, int targetSeat); }
[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 29061, SkillProgramEffectOp.ResolveWuZhangFeiXianlue)]
internal sealed class WuZhangFeiXianlueTargetFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries) =>
        queries.Get<IWuZhangFeiXianlueSelection>().CanObserve(context.OwnerSeat, targetSeat);
}
internal sealed class ResolveWuZhangFeiXianlueDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhangFeiXianlue;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Reveal,
        static (_, context) => context.PublicControlValue(4));
    public override ISkillProgramEffectHandler Handler { get; } = new WuZhangFeiXianlueHandler();
}
internal sealed class WuZhangFeiXianlueHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhangFeiXianlue;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IWuZhangFeiXianlueHost>().Observe(frame);
}
