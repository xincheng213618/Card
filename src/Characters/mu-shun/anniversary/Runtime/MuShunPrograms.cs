using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryMuShunHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryMuShunDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryMuShunHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
}
internal sealed class ResolveAnniversaryMuShunJinjianDescriptor : AnniversaryMuShunDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryMuShunJinjian;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage | ProgramContextCapability.Pindian;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied), new CreatePindianResult(MuShunComposition.Result)];
}
internal sealed class ResolveAnniversaryMuShunShizhaoDescriptor : AnniversaryMuShunDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryMuShunShizhao;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
}
internal sealed class AnniversaryMuShunHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryMuShunHost>().Resolve(f, op);
}
public enum MuShunContestStage { Choice, Contest, Returned, Declined, ContestChildren }
public sealed record MuShunJinjianReceipt(CardConversionSource Source, string Hash, long WindowId, long DamageFrameId, int ActualTurn, int? DamageSourceSeat,
    int MarkerBefore, int MarkerAfter, MuShunContestStage Stage = MuShunContestStage.Choice, long ContestId = 0,
    long MovementBefore = 0, long MovementAfter = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MuShunJinjianReceipt? MuShunJinjian { get; init; }
}
public sealed record MuShunJinjianIssuedEvent(long FrameId, CardConversionSource Source, string Hash, long WindowId, long DamageFrameId, int ActualTurn,
    int? DamageSourceSeat, int MarkerBefore, int MarkerAfter) : IGameEvent;
public sealed record MuShunContestIssuedEvent(long FrameId, long ContestId, int SourceSeat, int OpponentSeat, long MovementBefore) : IGameEvent;
public sealed record MuShunContestReturnedEvent(long FrameId, long ContestId, long MovementBefore, long MovementAfter) : IGameEvent;
public sealed record MuShunContestClosedEvent(long FrameId, long ContestId, bool Accepted, bool Won) : IGameEvent;
public sealed record MuShunShizhaoIssuedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentWindowId, long BatchId, int ActualTurn,
    int ActualTurnOwner, int MarkerBefore, int MarkerAfter, bool Draw) : IGameEvent;
public sealed record MuShunShizhaoDamageSpentEvent(long ArmId, long AttackId, int TargetSeat, int ActualTurn, long DamageFrameId) : IGameEvent;
public sealed record MuShunDamageReceipt(long AttackId, int TargetSeat, int ChainCursor, long ArmId, int InheritedAmount);
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MuShunDamageReceipt? MuShunDamage { get; init; } }
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MuShunDamageReceipt? MuShunDamage { get; init; } }
public sealed partial record JudgmentFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MuShunDamageReceipt? MuShunDamage { get; init; } }

internal static class MuShunComposition
{
    internal const string Jinjian = "anniversary:jinjian", Shizhao = "anniversary:shizhao", Damage = "actual-damage-jin", Loss = "outside-first-last-hand", Result = "jinjian-contest";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveAnniversaryMuShunJinjian or SkillProgramEffectOp.ResolveAnniversaryMuShunShizhao;
    [ProgramCatalogValidation(45300)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => Handles(e.Op))) return;
        var valid = p.Id == Jinjian ? p.Triggers is [{ Id: Damage, Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.Any,
            Optional: false, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage, UsageScope: null, UsageLimit: null, Condition.Kind: SkillProgramTriggerConditionKind.Always,
            Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryMuShunJinjian, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.Owner, Amount: 1, Condition: { Kind: SkillProgramConditionKind.PindianWon, SourceBind: Result } }] }] :
            p.Id == Shizhao && p.Triggers is [{ Id: Loss, Window: SkillProgramTriggerWindow.CardsMoved, Subject: SkillProgramTriggerSubject.Owner, Optional: false,
                MovementOccurrence: SkillProgramMovementOccurrence.PerBatch, UsageScope: SkillUsageScope.Turn, UsageLimit: 1, Effects: [
                    { Op: SkillProgramEffectOp.ResolveAnniversaryMuShunShizhao, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always },
                    { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 2, NumberExpression: null, Condition.Kind: SkillProgramConditionKind.Always }] } loss] &&
                loss.SourceZones.SequenceEqual([CardZoneKind.Hand]) && loss.DestinationZones.Count == 0 && loss.MovementReasons.Count == 0 && loss.ExcludedMovementReasons.Count == 0 &&
                loss.Condition is { Kind: SkillProgramTriggerConditionKind.All, Children: [
                    { Kind: SkillProgramTriggerConditionKind.Compare, Left.Kind: SkillProgramTriggerValueKind.SourceZoneCountBefore, Comparison: SkillProgramComparisonOperator.GreaterThan, Right: { Kind: SkillProgramTriggerValueKind.IntegerConstant, Value: 0 } },
                    { Kind: SkillProgramTriggerConditionKind.Compare, Left.Kind: SkillProgramTriggerValueKind.SourceZoneCountAfter, Comparison: SkillProgramComparisonOperator.Equal, Right: { Kind: SkillProgramTriggerValueKind.IntegerConstant, Value: 0 } }] };
        if (!valid || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0)
            throw new InvalidOperationException(path + ": Mu Shun requires mandatory per-damage Jin with optional native source Pindian, and the exact first outside-turn last-Hand loss branch.");
    }
}
