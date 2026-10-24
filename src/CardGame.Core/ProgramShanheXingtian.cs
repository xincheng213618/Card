namespace CardGame.Core;

internal interface IShanheXingtianHost { void ApplyShanheXingtianSeal(ProgramSkillFrame frame); }
internal sealed class ApplyShanheXingtianSealDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ApplyShanheXingtianSeal;
    public override ISkillProgramEffectHandler Handler { get; } = new ApplyShanheXingtianSealHandler();
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnHandColorRestriction,
        static (_, c) => c.PublicControlValue(5));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new ReadSelectedTarget(), new RequireTriggerWindows([SkillProgramTriggerWindow.CardUseBeforeTargetEffects])];

    [ProgramCatalogValidation(59300)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ApplyShanheXingtianSeal)))
            throw new InvalidOperationException(path + ": the S5 axe requires a native sole-target use trigger.");
        foreach (var t in p.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ApplyShanheXingtianSeal)))
            if (p.Id != "shanhe:xingtian-axe-20766-effect" || t.Window != SkillProgramTriggerWindow.CardUseBeforeTargetEffects ||
                t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.Optional || t.Effects.Count != 4 ||
                t.Effects[0] is not { Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.EventTarget } ||
                t.Effects[1] is not { Op: SkillProgramEffectOp.SelectOwnedCards, MinimumCards: 2, MaximumCards: 2, ResultBind: "axe-cost" } ||
                t.Effects[2] is not { Op: SkillProgramEffectOp.MoveBoundCards, SourceBind: "axe-cost",
                    Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true } ||
                t.Effects[3].Op != SkillProgramEffectOp.ApplyShanheXingtianSeal)
                throw new InvalidOperationException(path + ": the S5 axe requires its two-card native discard before the seal.");
    }
}
public sealed class ApplyShanheXingtianSealHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ApplyShanheXingtianSeal;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h)
    { h.GetCapability<IShanheXingtianHost>().ApplyShanheXingtianSeal(f); return SkillProgramStepOutcome.Continue; }
}
