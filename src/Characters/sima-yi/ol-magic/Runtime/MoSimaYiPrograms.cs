using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MoSimaYiStage { Acquiring, Children, ChoosingSkill, Granting, IssuingSlash, NativeSlash, Damage, ChoosingLieshi, Complete }
public sealed record MoSimaYiCause(long DamageId, long AttackOwnerId, int? SourceSeat, int TargetSeat,
    CardKind? CardKind, string? SkillId, string? SkillGameplayHash, int Round);
public sealed record MoSimaYiDodge(int CardId, CardLocation From);
public sealed record MoSimaYiRecorded(string? SkillId, CardKind? CardKind);
public sealed record MoSimaYiReceipt(CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Operation, MoSimaYiStage Stage, long WindowId, int Round, int Turn)
{
    public int Index { get; init; }
    public int? SelectedOption { get; init; }
    public int? FirstOption { get; init; }
    public int? TargetSeat { get; init; }
    public long NativeUseId { get; init; }
    public long NativeActionId { get; init; }
    public bool NativeReturned { get; init; }
    private readonly IReadOnlyList<string> _skills = Array.AsReadOnly(Array.Empty<string>());
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<string> Skills { get => _skills; init => _skills = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MoSimaYiReceipt? MoSimaYi { get; init; }
}
public sealed record MoSimaYiSlashOrigin(long FrameId, CardConversionSource Source, string GameplayHash,
    int Index, long UseId, long ActionId, int Actor, int Target);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MoSimaYiSlashOrigin? MoSimaYiSlash { get; init; }
}
public sealed record MoSimaYiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Operation, long WindowId, int Round, int Turn) : IGameEvent;
public sealed record MoSimaYiDodgeMarkedEvent(long FrameId, int OwnerSeat, int CardId, CardLocation From, long Sequence) : IGameEvent;
public sealed record MoSimaYiRecordAddedEvent(int OwnerSeat, MoSimaYiCause Cause, string? SkillId, CardKind? CardKind) : IGameEvent;
public sealed record MoSimaYiDamageCauseEvent(MoSimaYiCause Cause) : IGameEvent;
public sealed record MoSimaYiSkillFrozenEvent(long FrameId, int Index, string SkillId) : IGameEvent;
public sealed record MoSimaYiCandidateEvent(long FrameId, int Index, string SkillId) : IGameEvent;
public sealed record MoSimaYiSampledEvent(long FrameId, long RandomBefore, long RandomAfter, int Count) : IGameEvent;
public sealed record MoSimaYiSkillGrantedEvent(long FrameId, string SkillId, string GrantSourceId, bool Temporary) : IGameEvent;
public sealed record MoSimaYiSlashTargetEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record MoSimaYiSlashIssuedEvent(MoSimaYiSlashOrigin Origin) : IGameEvent;
public sealed record MoSimaYiSlashReturnedEvent(long FrameId, int Index, long UseId, long ActionId) : IGameEvent;
public sealed record MoSimaYiNameUsedEvent(long ActionId, int ActorSeat, int Turn, CardKind Kind) : IGameEvent;
public sealed record MoSimaYiLieshiChosenEvent(long FrameId, int ActorSeat, int Option) : IGameEvent;
public sealed record MoSimaYiCompletedEvent(long FrameId, SkillProgramEffectOp Operation) : IGameEvent;

internal interface IMoSimaYiHost
{
    SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal interface IMoSimaYiFacts : IProgramSelectionQuery
{
    int Round { get; }
    int Turn { get; }
    bool IsAlive(int seat);
    bool IsHuman(int seat);
    bool IsGameOver { get; }
    IReadOnlyList<T> History<T>() where T : IGameEvent;
    IReadOnlyList<CardConversionSource> GuiFuOwners();
    IReadOnlyList<string> EffectiveSkills(int owner);
    IReadOnlyList<int> RetaliationActors(int owner, int round);
    MoSimaYiDodge? FindDodge();
    string SkillName(string id);
    bool HasJudgmentArea(int actor);
}
internal interface IMoSimaYiNative : IProgramRuntimeOperations
{
    void ObtainDodge(ProgramSkillFrame frame, MoSimaYiDodge entity);
    bool DrainChildren(long frameId);
    void GrantSkill(ProgramSkillFrame frame, string skill, bool temporary);
    void EnterRumo(ProgramSkillFrame frame);
    IReadOnlyList<string> Sample(IReadOnlyList<string> skills, int count, long frameId);
    bool BeginSlash(ProgramSkillFrame frame, int actor, int target);
    void ApplyLieshi(ProgramSkillFrame frame, int actor, int option);
    bool FinishDamage(ProgramSkillFrame frame);
    void Complete(ProgramSkillFrame frame);
    void PublishState();
}
internal sealed class MoSimaYiHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IMoSimaYiHost>().Resolve(frame, operation);
}
internal abstract class MoSimaYiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MoSimaYiHandler(Op);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(6));
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.Frame.MoSimaYi is not null && c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class AcquireMoSimaYiGuifuDodgeDescriptor : MoSimaYiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AcquireMoSimaYiGuifuDodge;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.WholeActualUseDeclaration);
}
internal sealed class ResolveMoSimaYiMoubianDescriptor : MoSimaYiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoSimaYiMoubian; }
internal sealed class ResolveMoSimaYiZhouxiDescriptor : MoSimaYiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoSimaYiZhouxi; }
internal sealed class ResolveMoSimaYiRetaliationDescriptor : MoSimaYiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoSimaYiRetaliation; }
internal sealed class ResolveMoSimaYiLieshiDescriptor : MoSimaYiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoSimaYiLieshi;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard);
}
