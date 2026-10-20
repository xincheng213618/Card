namespace CardGame.Core;

// 烈节 second clause: the already discarded set prices a private source HE
// selection; the estimate credits the control value of stripping the damage
// source because the count is public once the first clause resolved.
internal sealed class LiejieSourceDiscardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.LiejieSourceDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new LiejieSourceDiscardHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardSelected,
        static (_, context) => context.PublicControlValue(5d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied),
         new ReadCardSet(effect.SourceBind!)];
}

public sealed class LiejieSourceDiscardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.LiejieSourceDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IWeiZiProgramHost>().LiejieSourceDiscard(f, e);
}

internal static class LiejieSourceDiscardContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.LiejieSourceDiscard)) return;
        if (trigger.Window != SkillProgramTriggerWindow.AfterDamageApplied || trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || trigger.Effects is not
            [{ Op: SkillProgramEffectOp.SelectOwnedCards } selection,
             { Op: SkillProgramEffectOp.MoveBoundCards } payment,
             { Op: SkillProgramEffectOp.Draw } draw,
             { Op: SkillProgramEffectOp.LiejieSourceDiscard } discard] ||
            selection.Target != SkillProgramEffectTarget.Owner || selection.TargetReference is not null ||
            selection.ResultBind is null || selection.MinimumCards != 1 || selection.MaximumCards != 3 ||
            !selection.Zones.Order().SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Order()) ||
            selection.NumberExpression is not null || selection.RequireExactCount == true || selection.AllowDecline ||
            payment.SourceBind != selection.ResultBind || payment.ExceptBind is not null ||
            payment.Target != SkillProgramEffectTarget.Owner || payment.Destination != SkillProgramCardDestination.DiscardPile ||
            draw.Target != SkillProgramEffectTarget.Owner || draw.TargetReference is not null ||
            draw.NumberExpression != SkillProgramNumberExpression.BoundCardCount || draw.SourceBind != selection.ResultBind ||
            draw.ResultBind is not null || discard.SourceBind != selection.ResultBind ||
            trigger.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
            throw new InvalidOperationException($"Invalid skill program at {path}: Liejie source discard requires its owner perDamage HE selection, real discard, bound draw and terminal source selection.");
    }
}
