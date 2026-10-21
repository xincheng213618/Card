using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouJiangWeiAnniversaryStage { Initial, Victim, Source, Discard, DiscardChildren, Damage, Drawing, DrawChildren, Recovery, RecoveryChildren }
public sealed record MouJiangWeiAnniversaryCard(int Id, CardLocation From, string Label);
public sealed record MouJiangWeiAnniversaryReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    string JuemouInstance, long ParentId, int Turn, bool Yin, bool Modified, long ActionId = 0, long CauseFrameId = 0, long DyingId = 0)
{
    public MouJiangWeiAnniversaryStage Stage { get; init; }
    public bool Paid { get; init; }
    public int Victim { get; init; } = -1;
    public int DamageSource { get; init; } = -1;
    public MouJiangWeiAnniversaryCard? Card { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int Requested { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouJiangWeiAnniversaryReceipt? MouJiangWeiAnniversary { get; init; }
}
public sealed record MouJiangWeiAnniversaryStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int Turn, bool Yin, bool Modified, long ActionId, long CauseFrameId, long DyingId) : IGameEvent;
public sealed record MouJiangWeiAnniversaryPairEvent(long FrameId, int Victim, int Source) : IGameEvent;
public sealed record MouJiangWeiAnniversaryDamageIssuedEvent(long FrameId, int Victim, int Source) : IGameEvent;
public sealed record MouJiangWeiAnniversaryDiscardedEvent(long FrameId, int Chooser, int Holder, int CardId, CardLocation From, long Sequence) : IGameEvent;
public sealed record MouJiangWeiAnniversaryHandChosenEvent(long FrameId, int CardId, uint RandomBefore, uint RandomAfter, int CandidateCount) : IGameEvent;
public sealed record MouJiangWeiAnniversaryDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouJiangWeiAnniversaryRecoveryIssuedEvent(long FrameId, int Amount, int TargetHp, long DyingId) : IGameEvent;
public sealed record MouJiangWeiAnniversaryCompletedEvent(long FrameId) : IGameEvent;
internal interface IMouJiangWeiAnniversaryRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal interface IMouJiangWeiAnniversaryFacts : IProgramSelectionQuery
{
    int Turn { get; } long MovementSequence { get; } bool Finished { get; }
    bool Alive(int seat); bool Human(int seat); int Hp(int seat); int MaxHp(int seat); string Name(int seat);
    bool Friendly(int owner, int other); IReadOnlyList<int> Living();
    string Instance(int owner); bool State(int owner, string instance, string state);
    SkillProgram Program(string id); IEnumerable<T> History<T>() where T : IGameEvent;
    bool Parent(ProgramSkillFrame frame); bool CanTrigger(ProgramTriggerCandidate c, ProgramSkillWindowContext w, SkillProgramEffectOp operation);
    (long Cause, long Dying)? CausalDying(int owner, ProgramSkillWindowContext context);
    long Action(ProgramSkillWindowContext context);
    IReadOnlyList<MouJiangWeiAnniversaryCard> Discardable(int chooser, int holder);
}
internal interface IMouJiangWeiAnniversaryNative : IProgramRuntimeOperations
{
    void Finish(ProgramSkillFrame frame); void PublishState(); bool Drain(ProgramSkillFrame frame);
    void State(ProgramSkillFrame frame, string state, bool value);
    MouJiangWeiAnniversaryCard RandomHand(ProgramSkillFrame frame, IReadOnlyList<MouJiangWeiAnniversaryCard> cards);
    void Discard(ProgramSkillFrame frame); void Damage(ProgramSkillFrame frame); void Recover(ProgramSkillFrame frame, int amount);
}
internal abstract class MouJiangWeiAnniversaryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouJiangWeiAnniversaryHandler(Op);
    public override ProgramOperationInteraction Interaction => Op is SkillProgramEffectOp.InitializeMouJiangWeiJuemou or SkillProgramEffectOp.ResolveMouJiangWeiJuemou ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class InitializeMouJiangWeiJuemouDescriptor : MouJiangWeiAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeMouJiangWeiJuemou; }
internal sealed class ResolveMouJiangWeiJuemouDescriptor : MouJiangWeiAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouJiangWeiJuemou; }
internal sealed class RestoreMouJiangWeiJuemouDescriptor : MouJiangWeiAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RestoreMouJiangWeiJuemou; }
internal sealed class ResolveMouJiangWeiFuzhanDescriptor : MouJiangWeiAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouJiangWeiFuzhan; }
internal sealed class MouJiangWeiAnniversaryHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouJiangWeiAnniversaryRuntime>().Resolve(f, operation);
}
