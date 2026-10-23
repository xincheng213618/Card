namespace CardGame.Core;

internal interface IShanheS5LiJueProgramHost
{
    SkillProgramStepOutcome BeginPositiveRandomDamage(ProgramSkillFrame frame);
}

internal sealed class ChooseHpBoundOtherAndDealPositiveRandomDamageDescriptor : LiJueDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseHpBoundOtherAndDealPositiveRandomDamage;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.PublicControlValue(18));
    public override ISkillProgramEffectHandler Handler { get; } = new ShanheS5PositiveRandomDamageHandler();
}

internal sealed class ShanheS5PositiveRandomDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseHpBoundOtherAndDealPositiveRandomDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int subject, ISkillProgramEffectHost host) =>
        host.GetCapability<IShanheS5LiJueProgramHost>().BeginPositiveRandomDamage(frame);
}

internal static class ShanheS5LiJueContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ChooseHpBoundOtherAndDealPositiveRandomDamage)) return;
        if (trigger.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
            trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.AnyLiving || !trigger.Optional ||
            trigger.UsageLimit is not null || trigger.Effects is not [{ Op: SkillProgramEffectOp.ChooseHpBoundOtherAndDealPositiveRandomDamage }])
            throw new InvalidOperationException($"Invalid skill program at {path}: positive Langxi requires one optional effect at every living character's actual preparation window.");
    }
}
