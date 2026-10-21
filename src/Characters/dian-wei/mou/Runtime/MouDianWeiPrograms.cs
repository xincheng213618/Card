using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouDianWeiStage { Drawing, DrawChildren, Opponent, Pindian, PindianChildren, Result, ResultChildren, Slash, RecoveryChildren, LossChildren }
public sealed record MouDianWeiSlash(long ProgramId, CardConversionSource Source, string GameplayHash, int Index, long ContestId, long UseId, long ActionId, int Actor)
{
    private IReadOnlyList<int> _targets = [];
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouDianWeiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, int ActualTurn, int TurnOwner)
{
    public MouDianWeiStage Stage { get; init; }
    public int Requested { get; init; }
    public int X { get; init; }
    public int Index { get; init; }
    public int Opponent { get; init; } = -1;
    public long ContestId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public MouDianWeiSlash? Slash { get; init; }
    public int HpBefore { get; init; }
    public int ActualLoss { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDianWeiReceipt? MouDianWei { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDianWeiSlash? MouDianWei { get; init; }
}
public sealed record MouDianWeiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, int ActualTurn, int TurnOwner, int Requested) : IGameEvent;
public sealed record MouDianWeiDrawReturnedEvent(long FrameId, int Requested, int ActualDrawn, int ReplacementGained, int X, long? AttemptId) : IGameEvent;
public sealed record MouDianWeiContestIssuedEvent(long FrameId, int Index, long ContestId, int Opponent, string Bind) : IGameEvent;
public sealed record MouDianWeiContestReturnedEvent(long FrameId, int Index, long ContestId, bool Won) : IGameEvent;
public sealed record MouDianWeiSlashIssuedEvent(long ProgramId, int Index, long ContestId, long UseId, long ActionId, int Actor, int Target, int TargetIndex) : IGameEvent;
public sealed record MouDianWeiSlashReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouDianWeiDebtIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwner, long RecoveryId, int ActualRecovered) : IGameEvent;
public sealed record MouDianWeiDebtPaidEvent(long FrameId, CardConversionSource Source, int ActualTurn, int TurnOwner, int Debt, int ActualLost) : IGameEvent;
public sealed record MouDianWeiCompletedEvent(long FrameId) : IGameEvent;
internal interface IMouDianWeiRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal interface IMouDianWeiFacts : IProgramSelectionQuery
{
    int Turn { get; } int TurnOwner { get; } long Sequence { get; } bool Finished { get; }
    int Hp(int seat); int MaxHp(int seat); int Hand(int seat); bool Alive(int seat); bool Human(int seat); string Name(int seat);
    IEnumerable<T> History<T>() where T : IGameEvent;
    SkillProgram? Program(string id); bool Parent(ProgramSkillFrame frame, SkillProgramEffectOp operation);
    IReadOnlyList<int> Opponents(int owner); IReadOnlyList<int> NotWon(int owner); bool CanSlash(int actor, int target);
    PindianResult Result(ProgramSkillFrame frame);
}
internal interface IMouDianWeiNative : IProgramRuntimeOperations
{
    void Finish(ProgramSkillFrame frame); void Publish(); bool Drain(ProgramSkillFrame frame);
    void Pindian(ProgramSkillFrame frame); void Slash(ProgramSkillFrame frame, int actor, IReadOnlyList<int> targets);
    void Recover(ProgramSkillFrame frame); void Lose(ProgramSkillFrame frame, int amount);
}
internal abstract class MouDianWeiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouDianWeiHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveMouDianWeiKuangzhan ? [] :
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.ResolveMouDianWeiKangyongStart ? SkillProgramTriggerWindow.TurnStartBeforeNormalFlow : SkillProgramTriggerWindow.AfterTurnEnded)];
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance, Activated: ProgramTrackedFact.NativeActionAppearance);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouDianWeiKuangzhanDescriptor : MouDianWeiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDianWeiKuangzhan; }
internal sealed class ResolveMouDianWeiKangyongStartDescriptor : MouDianWeiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDianWeiKangyongStart; }
internal sealed class ResolveMouDianWeiKangyongEndDescriptor : MouDianWeiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDianWeiKangyongEnd; }
internal sealed class MouDianWeiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouDianWeiRuntime>().Resolve(f, op);
}
