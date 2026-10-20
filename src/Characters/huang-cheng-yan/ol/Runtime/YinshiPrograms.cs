namespace CardGame.Core;
public sealed record ProgramYinshiDamagePreventedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TurnNumber, int Amount) : IGameEvent;
public sealed record ProgramYinshiJudgmentClaimedEvent(long FrameId, string SkillId, string BindingId,
    int ClaimantSeat, int SubjectSeat, int CardId) : IGameEvent;

// 隐世 ①: prevents the first damage of the turn that no colored game card caused.
internal sealed class YinshiPreventSourcelessDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YinshiPreventSourcelessDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new YinshiPreventSourcelessDamageHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PreventCurrentDamage,
        static (effect, context) => context.PreventCurrentDamage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}

public sealed class YinshiPreventSourcelessDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YinshiPreventSourcelessDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IHuangChengYanProgramHost>().YinshiPreventSourcelessDamage(f, e);
}

// 隐世 ②: claims the effective judgment card of any 八卦阵 judgment.
internal sealed class YinshiClaimBaguaJudgmentCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YinshiClaimBaguaJudgmentCard;
    public override ISkillProgramEffectHandler Handler { get; } = new YinshiClaimBaguaJudgmentCardHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Judgment;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner,
            1, new(SkillProgramConditionKind.Always, 0, []))));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.JudgmentFinalized)];
}

public sealed class YinshiClaimBaguaJudgmentCardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YinshiClaimBaguaJudgmentCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IHuangChengYanProgramHost>().YinshiClaimBaguaJudgmentCard(f, e);
}
