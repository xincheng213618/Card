namespace CardGame.Core;

internal interface IShanheZhanhunDaoProgramHost
{
    SkillProgramStepOutcome ResolveShanheZhanhunDao(ProgramSkillFrame frame);
}

internal sealed class ResolveShanheZhanhunDaoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheZhanhunDao;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveShanheZhanhunDaoHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PreventCurrentDamage,
        static (effect, context) => context.PreventCurrentDamage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}

public sealed class ResolveShanheZhanhunDaoHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheZhanhunDao;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IShanheZhanhunDaoProgramHost>().ResolveShanheZhanhunDao(frame);
}

internal static class ShanheZhanhunDaoComposition
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveShanheZhanhunDao)) return;
        if (trigger.Window != SkillProgramTriggerWindow.BeforeDamageApplied || trigger.Subject != SkillProgramTriggerSubject.Any ||
            !trigger.Optional || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.ResolveShanheZhanhunDao }] ||
            trigger.CardKinds.Count != 0 || trigger.DamageCardKinds.Count != 0 ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null)
            throw new InvalidOperationException(path + ": Zhanhun Dao requires its sole optional any-participant before-damage operation without card or usage filters.");
    }
}

public sealed record ShanheZhanhunDaoResolvedEvent(long FrameId, long BeforeDamageFrameId, long DamageOwnerFrameId,
    int SourceSeat, int TargetSeat, int PreventedAmount, int MaximumHpBefore, int MaximumHpAfter) : IGameEvent;
