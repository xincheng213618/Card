namespace CardGame.Core;

// Public skill identities and an equipped card are public; no random generator
// state, hand identities or private card selections enter these scalar facts.
public sealed record AnqiFeirenSuppressedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long DamageWindowId, long DamageFrameId, int WeaponCardId, int TargetSeat, int TurnNumber,
    int TurnOwnerSeat, string SuppressedSkillId, int RandomIndex, int CandidateCount) : IGameEvent;
public sealed record AnqiFeirenExpiredEvent(long SuppressionFrameId, int TargetSeat, int TurnNumber) : IGameEvent;

internal interface IAnqiFeirenHost
{
    SkillProgramStepOutcome ResolveAnqiFeiren(ProgramSkillFrame frame);
}

internal sealed class ResolveAnqiFeirenDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnqiFeiren;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveAnqiFeirenHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}

public sealed class ResolveAnqiFeirenHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnqiFeiren;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IAnqiFeirenHost>().ResolveAnqiFeiren(frame);
}

internal static class AnqiFeirenComposition
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveAnqiFeiren)) return;
        if (trigger.Window != SkillProgramTriggerWindow.BeforeDamageApplied || trigger.Subject != SkillProgramTriggerSubject.Any ||
            trigger.Optional || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.ResolveAnqiFeiren }] ||
            trigger.CardKinds.Count != 0 || trigger.DamageCardKinds.Count != 0 || trigger.UsageLimit is not null ||
            trigger.UsageScope is not null || trigger.DynamicUsageLimit is not null || trigger.NamedUsageGroup is not null ||
            trigger.MarkerCost is not null || trigger.ChoiceGroup is not null)
            throw new InvalidOperationException(path + ": Anqi Feiren requires its sole mandatory native before-damage operation without card or generic usage filters.");
    }
}
