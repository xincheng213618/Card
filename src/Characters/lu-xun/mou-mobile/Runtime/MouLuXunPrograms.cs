using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MouLuXunStage { HoldChoice, HoldChildren, NameChoice, UseChoice, IssuingUse, NativeUse, NativeReturned, ViewChildren, Distribution, GiftChildren, ReturnChildren, Complete }

public sealed record MouLuXunUsePlan(LegalActionKind ActionKind, int? TargetCardId, CardKind? RequiredKind, bool Adjusted)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouLuXunReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    long WindowId, int ActualTurn, int TurnOwner, MouLuXunStage Stage, int Limit = 0, CardKind? Kind = null,
    long UseId = 0, long ActionId = 0, int Cursor = 0, long Before = 0, long After = 0, bool Paid = false)
{
    public string GrantSourceId { get; init; } = "";
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>()), _viewed = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Viewed { get => _viewed; init => _viewed = Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CardLocation? HoldLocation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuXunUsePlan? Plan { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuXunReceipt? MouLuXun { get; init; }
}
public sealed record MouLuXunStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Operation, long WindowId, int ActualTurn, int TurnOwner, int Limit, string GrantSourceId) : IGameEvent;
public sealed record MouLuXunNameRecordedEvent(long FrameId, int OwnerSeat, CardKind Kind, long WindowId, long ActionId, int ActualTurn, int Count) : IGameEvent;
public sealed record MouLuXunNameRemovedEvent(long FrameId, int OwnerSeat, CardKind Kind, int ActualTurn) : IGameEvent;
public sealed record MouLuXunEntityMovedEvent(long FrameId, int Index, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouLuXunNativeIssuedEvent(long FrameId, long UseId, long ActionId, int OwnerSeat, CardKind Kind) : IGameEvent;
public sealed record MouLuXunNativeReturnedEvent(long FrameId, long UseId, bool HadAttack) : IGameEvent;
public sealed record MouLuXunTopViewedEvent(long FrameId, int Index, int CardId, long Sequence) : IGameEvent;
public sealed record MouLuXunCompletedEvent(long FrameId) : IGameEvent;

// Opt-in on the immutable operation, leaving the older sole-target hold contract unchanged.
internal interface IProgramPerTargetTrickEffectOperation { }
internal interface IMouLuXunHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal sealed class MouLuXunHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IMouLuXunHost>().Resolve(f, operation);
}
internal abstract class MouLuXunDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouLuXunHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(5));
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.RecordMouLuXunQianxun ? SkillProgramTriggerWindow.CardEffectBeforeApply :
            Op == SkillProgramEffectOp.UseMouLuXunRecordedTrick ? SkillProgramTriggerWindow.PlayPhaseStarting : SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class RecordMouLuXunQianxunDescriptor : MouLuXunDescriptor, IProgramPerTargetTrickEffectOperation
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordMouLuXunQianxun; }
internal sealed class UseMouLuXunRecordedTrickDescriptor : MouLuXunDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseMouLuXunRecordedTrick; }
internal sealed class DistributeMouLuXunLianyingDescriptor : MouLuXunDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DistributeMouLuXunLianying; }
