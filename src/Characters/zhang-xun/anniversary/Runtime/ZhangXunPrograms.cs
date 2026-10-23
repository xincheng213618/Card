using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryZhangXunHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryZhangXunDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => Op == SkillProgramEffectOp.ArmAnniversaryZhangXunSuizheng ? Arm : Choose;
    private static readonly AnniversaryZhangXunHandler Arm = new(SkillProgramEffectOp.ArmAnniversaryZhangXunSuizheng), Choose = new(SkillProgramEffectOp.ChooseAnniversaryZhangXunSuizhengSlash);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTarget, static (_, c) => c.PublicControlValue(3));
}
internal sealed class ArmAnniversaryZhangXunSuizhengDescriptor : AnniversaryZhangXunDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmAnniversaryZhangXunSuizheng;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new ReadSelectedTarget()];
}
internal sealed class ChooseAnniversaryZhangXunSuizhengSlashDescriptor : AnniversaryZhangXunDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseAnniversaryZhangXunSuizhengSlash;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding), new SelectSingleTarget()];
}
internal sealed class AnniversaryZhangXunHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryZhangXunHost>().Resolve(f, operation);
}
public sealed record ZhangXunSuizhengLeaseEvent(long FrameId, CardConversionSource Source, string Hash, long EndingWindowId, int IssuedTurn, int RecipientSeat) : IGameEvent;
public sealed record ZhangXunSuizhengDamageWitness(int EventIndex, int TargetSeat);
public sealed record ZhangXunSuizhengFollowupReceipt(CardConversionSource Source, string Hash, long LeaseId, long ParentWindowId, int ActualTurn, int RecipientSeat,
    int PhaseInstance, int HistoryCutoff, int? SelectedTarget = null)
{
    private IReadOnlyList<ZhangXunSuizhengDamageWitness> _victims = Array.AsReadOnly(Array.Empty<ZhangXunSuizhengDamageWitness>());
    public IReadOnlyList<ZhangXunSuizhengDamageWitness> Victims { get => _victims; init => _victims = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhangXunSuizhengFollowupReceipt? ZhangXunSuizheng { get; init; }
}
public sealed record ZhangXunSuizhengFollowupStartedEvent(long FrameId, long LeaseId, long ParentWindowId, int ActualTurn, int RecipientSeat, int PhaseInstance, int HistoryCutoff, int TargetCount) : IGameEvent;
public sealed record ZhangXunSuizhengVictimIssuedEvent(long FrameId, int Ordinal, int DamageEventIndex, int TargetSeat) : IGameEvent;
public sealed record ZhangXunSuizhengTargetChosenEvent(long FrameId, long LeaseId, int TargetSeat) : IGameEvent;
public sealed record ZhangXunSuizhengPhaseClosedEvent(long LeaseId, long WindowId, int ActualTurn, int PhaseInstance, bool Activated) : IGameEvent;
internal static class AnniversaryZhangXunComposition
{
    internal const string Skill = "anniversary:suizheng", Arm = "arm-next-actual-turn", Followup = "issued-play-end-slash";
    [ProgramCatalogValidation(45200)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op is SkillProgramEffectOp.ArmAnniversaryZhangXunSuizheng or SkillProgramEffectOp.ChooseAnniversaryZhangXunSuizhengSlash)) return;
        if (p.Id != Skill || p.Triggers is not [
            { Id: Arm, Window: SkillProgramTriggerWindow.TurnEnding, Subject: SkillProgramTriggerSubject.Owner, Optional: true, IsSkillInvocation: true, TurnOwnerScope: SkillProgramTurnOwnerScope.Own, Effects: [
                { Op: SkillProgramEffectOp.SelectTarget, Target: SkillProgramEffectTarget.Owner, TargetKind: SkillProgramTargetKind.AnyLiving, Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.ArmAnniversaryZhangXunSuizheng, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] },
            { Id: Followup, Window: SkillProgramTriggerWindow.PlayEnding, Subject: SkillProgramTriggerSubject.Owner, Optional: true, IsSkillInvocation: false, TurnOwnerScope: SkillProgramTurnOwnerScope.AnyLiving, Effects: [
                { Op: SkillProgramEffectOp.ChooseAnniversaryZhangXunSuizhengSlash, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.UseVirtualSlash, Target: SkillProgramEffectTarget.SelectedTarget, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always }] }] ||
            p.Triggers.Any(t => t.UsageLimit is not null || t.UsageScope is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always) ||
            p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Suizheng requires its exact Ending grant and issued Play-ending native selected-target Slash continuation.");
    }
}
