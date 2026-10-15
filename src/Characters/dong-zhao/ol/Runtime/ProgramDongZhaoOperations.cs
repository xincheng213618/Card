namespace CardGame.Core;

internal interface IDongZhaoProgramHost
{
    SkillProgramStepOutcome XianlueRecord(ProgramSkillFrame frame);
    SkillProgramStepOutcome XianlueRedistribute(ProgramSkillFrame frame);
    SkillProgramStepOutcome ZaowangResolve(ProgramSkillFrame frame);
}
internal abstract class DongZhaoDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (e,c) => c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 2, e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
}
internal sealed class XianlueRecordDescriptor : DongZhaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.XianlueRecord;
    public override ISkillProgramEffectHandler Handler { get; } = new XianlueRecordHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class XianlueRedistributeDescriptor : DongZhaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.XianlueRedistribute;
    public override ISkillProgramEffectHandler Handler { get; } = new XianlueRedistributeHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
}
internal sealed class ZaowangResolveDescriptor : DongZhaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZaowangResolve;
    public override ISkillProgramEffectHandler Handler { get; } = new ZaowangResolveHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}
public sealed class XianlueRecordHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.XianlueRecord;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) => h.GetCapability<IDongZhaoProgramHost>().XianlueRecord(f);
}
public sealed class XianlueRedistributeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.XianlueRedistribute;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) => h.GetCapability<IDongZhaoProgramHost>().XianlueRedistribute(f);
}
public sealed class ZaowangResolveHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZaowangResolve;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) => h.GetCapability<IDongZhaoProgramHost>().ZaowangResolve(f);
}
internal static class DongZhaoContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        var operations = t.Effects.Where(e => e.Op is SkillProgramEffectOp.XianlueRecord or SkillProgramEffectOp.XianlueRedistribute or SkillProgramEffectOp.ZaowangResolve).ToArray();
        if (operations.Length == 0) return;
        if (operations.Length != 1 || t.Effects.Count != 1 ||
            t.UsageScope is not null || t.UsageLimit is not null ||
            (operations[0].Op == SkillProgramEffectOp.XianlueRecord
                ? t.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || !t.Optional || t.Subject != SkillProgramTriggerSubject.Owner || t.OwnerRelation is not null ||
                    t.TurnOwnerScope is not (SkillProgramTurnOwnerScope.Own or SkillProgramTurnOwnerScope.OtherLiving)
                : operations[0].Op != SkillProgramEffectOp.XianlueRedistribute || t.Window != SkillProgramTriggerWindow.CardUseCompleted || t.Optional ||
                    t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || t.IncludeResponseUses || t.CardKinds.Count != 0 || t.CardCategories.Count != 0))
            throw new InvalidOperationException($"{path}: Xianlue requires its exact lord boundary or automatic actual completed-use receipt.");
    }
    internal static void ValidateActivation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e=>e.Op is SkillProgramEffectOp.XianlueRecord or SkillProgramEffectOp.XianlueRedistribute or SkillProgramEffectOp.ZaowangResolve)) return;
        if(a.MinCards!=0 || a.MaxCards!=0 || a.MinTargets!=1 || a.MaxTargets!=1 || a.TargetKind!=SkillProgramTargetKind.AnyLiving ||
            a.UsesPerGame!=1 || a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.Effects is not
            [{ Op: SkillProgramEffectOp.ZaowangResolve, Target: SkillProgramEffectTarget.Owner },
             { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1 },
             { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 3 }])
            throw new InvalidOperationException($"{path}: Zaowang must arm one selected actual recipient before exactly one recovery and three draws.");
    }
}
