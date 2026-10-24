using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShiSunJunStage { Hand, RevealChildren, Branch, Payment, DiscardChildren = 9, CostChildren, DamageIssued, Drawing, DrawChildren, Finished }
public sealed record ShiSunJunReceipt(CardConversionSource Source, string Hash, int Turn, ActualDiscardRecoveryPhaseKey? Phase, int Target, ShiSunJunStage Stage)
{
    private IReadOnlyList<int> _hand = [], _payment = [];
    public IReadOnlyList<int> Hand { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Payment { get => _payment; init => _payment = Array.AsReadOnly(value.ToArray()); }
    public int Revealed { get; init; }
    public int Required { get; init; }
    public bool DamageBranch { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long DamageId { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiSunJunReceipt? ShiSunJun { get; init; } }
public sealed record ShiSunJunStartedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int Target) : IGameEvent;
public sealed record ShiSunJunRevealedEvent(long FrameId, int Target, int CardId) : IGameEvent;
public sealed record ShiSunJunBranchIssuedEvent(long FrameId, int Required, bool DamageBranch) : IGameEvent;
public sealed record ShiSunJunCostPaidEvent(long FrameId, int Count, long Before, long After) : IGameEvent;
public sealed record ShiSunJunBenefitIssuedEvent(long FrameId, int Owner, int Turn, long DamageId, ActualDiscardRecoveryPhaseKey? Phase) : IGameEvent;
public sealed record ShiSunJunDrawReturnedEvent(long FrameId, long DamageId, int ActualCount) : IGameEvent;
public sealed record ShiSunJunHandUseEvent(long UseId, long ActionId, int Actor, int Provider, int Turn, Suit Suit, bool First, bool Exempt) : IGameEvent;

internal interface IShiSunJunHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, bool benefit); }
internal sealed class ResolveMobileShiXiongtuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiXiongtu;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiSunJunHandler(false);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, c) => c.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLivingWithHand)];
}
internal sealed class ResolveMobileShiXiongtuDamageBenefitDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiXiongtuDamageBenefit;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiSunJunHandler(true);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class ShiSunJunHandler(bool benefit) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => benefit ? SkillProgramEffectOp.ResolveMobileShiXiongtuDamageBenefit : SkillProgramEffectOp.ResolveMobileShiXiongtu;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IShiSunJunHost>().Resolve(f, benefit);
}
