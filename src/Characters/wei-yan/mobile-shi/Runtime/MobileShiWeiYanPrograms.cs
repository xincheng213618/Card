namespace CardGame.Core;

public sealed record MobileShiWeiYanPhaseGrantEvent(long FrameId, int OwnerSeat, int TurnNumber, int PhaseInstanceId,
    int DiscardedCards, int LostHp) : IGameEvent
{
    public CardConversionSource? Source { get; init; }
}
public sealed record MobileShiWeiYanUseEvent(long UseId, long ActionId, int OwnerSeat, int TurnNumber, int PhaseInstanceId,
    int Ordinal, bool DistanceAndNoResponse, bool NoCount) : IGameEvent;
public sealed record MobileShiYinzhanQualifiedEvent(long Id, long UseId, int OwnerSeat, int TargetSeat,
    bool HpQualified, bool HandQualified) : IGameEvent;
public sealed record MobileShiYinzhanAppliedEvent(long QualificationId) : IGameEvent;
public sealed record MobileShiYinzhanClaimedEvent(long QualificationId, long FrameId) : IGameEvent;
public sealed record MobileShiWeiYanMissionEvent(long FrameId, int OwnerSeat, bool Succeeded) : IGameEvent;
public sealed record MobileShiKuangguSlashGrantedEvent(long FrameId, int OwnerSeat, int TurnNumber, int PhaseInstanceId) : IGameEvent;

public sealed record MobileShiWeiYanPending(string Operation, int Stage, int TurnNumber, int PhaseInstanceId,
    int HpLoss = 0, int TargetSeat = -1, int? CardId = null, long ClaimId = 0, bool Chengshi = false)
{
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    public MobileShiWeiYanPending? MobileShiWeiYan { get; init; }
}

internal interface IMobileShiWeiYanHost
{
    SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal sealed class MobileShiWeiYanHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => host.GetCapability<IMobileShiWeiYanHost>().Resolve(frame, operation);
}
internal abstract class MobileShiWeiYanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op switch
    {
        SkillProgramEffectOp.ResolveMobileShiZhuangshi => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)],
        SkillProgramEffectOp.ResolveMobileShiYinzhan => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted), new RequireContext(ProgramContextCapability.CardAction)],
        SkillProgramEffectOp.ResolveMobileShiZhongao => [new RequireTriggerWindows([SkillProgramTriggerWindow.CharacterDied, SkillProgramTriggerWindow.DyingEntering])],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]
    };
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler => new MobileShiWeiYanHandler(Op);
}
internal sealed class ResolveMobileShiZhuangshiDescriptor : MobileShiWeiYanDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiZhuangshi; }
internal sealed class ResolveMobileShiYinzhanDescriptor : MobileShiWeiYanDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiYinzhan; }
internal sealed class ResolveMobileShiZhongaoDescriptor : MobileShiWeiYanDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiZhongao; }
internal sealed class ResolveMobileShiKuangguDescriptor : MobileShiWeiYanDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiKuanggu; }
