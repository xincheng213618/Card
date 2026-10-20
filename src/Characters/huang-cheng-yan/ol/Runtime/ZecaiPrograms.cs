namespace CardGame.Core;
public sealed record ProgramZecaiRoundSettledEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int SettledRound) : IGameEvent;
public sealed record ProgramZecaiSkillGrantedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TargetSeat, int RoundNumber, string GrantedSkillId, bool ExtraTurnPended) : IGameEvent;
public sealed record ProgramRoundTrickUsedEvent(int RoundNumber, int ActorSeat,
    long ActionId, CardKind EffectiveKind) : IGameEvent;

// 择才 settlement: at the owner's first turn start after a round boundary the
// finished round settles once; the limited activation offers the 集智 grant.
internal sealed class ZecaiRoundSettlementDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZecaiRoundSettlement;
    public override ISkillProgramEffectHandler Handler { get; } = new ZecaiRoundSettlementHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills,
        static (effect, context) => context.GrantSkills(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class ZecaiRoundSettlementHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZecaiRoundSettlement;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IHuangChengYanProgramHost>().ZecaiRoundSettlement(f, e);
}
