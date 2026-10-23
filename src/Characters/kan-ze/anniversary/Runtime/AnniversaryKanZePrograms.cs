using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IAnniversaryKanZeHost
{
    void Arm(ProgramSkillFrame frame, int target);
    void Bind(ProgramSkillFrame frame);
}

internal abstract class AnniversaryKanZeDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler => new AnniversaryKanZeHandler(operation);
    public override ProgramSkillInstruction? Compile(SkillProgramEffect effect) => operation == SkillProgramEffectOp.BindAnniversaryKanZeKuanshiDamage
        ? new RecoverProgramInstruction(SkillProgramEffectTarget.Owner, new FixedProgramAmount(1), new(ProgramParticipantRef.EventTarget)) : null;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) =>
        operation == SkillProgramEffectOp.BindAnniversaryKanZeKuanshiDamage && context.State.CanContinueAfterOwnerDeath(context.Frame);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover,
        static (_, context) => context.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        if (operation == SkillProgramEffectOp.BindAnniversaryKanZeKuanshiDamage) return ParseOwnerAlways(r);
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException("Anniversary Kuanshi requires the native selected living recipient.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => operation == SkillProgramEffectOp.ArmAnniversaryKanZeKuanshi
        ? [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.AnyLiving), new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)]
        : [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class ArmAnniversaryKanZeKuanshiDescriptor() : AnniversaryKanZeDescriptor(SkillProgramEffectOp.ArmAnniversaryKanZeKuanshi);
internal sealed class BindAnniversaryKanZeKuanshiDamageDescriptor() : AnniversaryKanZeDescriptor(SkillProgramEffectOp.BindAnniversaryKanZeKuanshiDamage);
internal sealed class AnniversaryKanZeHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        var capability = host.GetCapability<IAnniversaryKanZeHost>();
        if (operation == SkillProgramEffectOp.ArmAnniversaryKanZeKuanshi) capability.Arm(frame, targetSeat);
        else
        {
            capability.Bind(frame);
            host.Recover(frame.Id, frame.OwnerSeat, host.ResolveParticipant(frame, new(ProgramParticipantRef.EventTarget)), 1, null, null);
        }
        return SkillProgramStepOutcome.Continue;
    }
}

internal static class AnniversaryKanZeComposition
{
    internal const string Kuanshi = "anniversary:kuanshi", Arm = "arm-until-next-actual-turn", Reward = "second-damage-point";
    [ProgramCatalogValidation(42900)]
    internal static void Validate(string path, SkillProgram program)
    {
        var own = program.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ArmAnniversaryKanZeKuanshi or SkillProgramEffectOp.BindAnniversaryKanZeKuanshiDamage);
        if (!own) return;
        if (program.Id != Kuanshi || program.Triggers.Count != 2 || program.Activations.Count != 0 || program.ViewAs.Count != 0 ||
            program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.BooleanStates.Count != 0 || program.Contributions.Count != 0)
            throw new InvalidOperationException(path + ": anniversary Kuanshi requires exactly its ending grant and issued after-damage recovery.");
        foreach (var t in program.Triggers)
        {
            if (t.Priority != 0 || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageScope is not null || t.UsageLimit is not null ||
                t.MarkerCost is not null || t.OwnerRelation is not null || t.NamedUsageGroup is not null ||
                t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.Suits.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 ||
                t.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
                throw new InvalidOperationException(path + ": anniversary Kuanshi's quotas and damage threshold belong to its exact issued grants.");
            var valid = t.Id switch
            {
                Arm => t.Window == SkillProgramTriggerWindow.TurnEnding && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional &&
                    t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && t.Effects is
                    [{ Op: SkillProgramEffectOp.SelectTarget, Target: SkillProgramEffectTarget.Owner, TargetKind: SkillProgramTargetKind.AnyLiving },
                     { Op: SkillProgramEffectOp.ArmAnniversaryKanZeKuanshi, Target: SkillProgramEffectTarget.SelectedTarget }],
                Reward => t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Subject == SkillProgramTriggerSubject.Any && !t.Optional &&
                    !t.IsSkillInvocation && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && t.Effects is
                    [{ Op: SkillProgramEffectOp.BindAnniversaryKanZeKuanshiDamage, Target: SkillProgramEffectTarget.Owner, TargetReference: null }],
                _ => false
            };
            if (!valid) throw new InvalidOperationException(path + ": anniversary Kuanshi requires its exact selected ending grant and native Recover1 on the issued damage recipient.");
        }
    }
}

// Every public field is scalar or an immutable scalar record; no card identities or hidden choices enter these receipts.
public sealed record AnniversaryKuanshiGrant(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TargetSeat);
public sealed record AnniversaryKuanshiArmedEvent(AnniversaryKuanshiGrant Grant) : IGameEvent;
public sealed record AnniversaryKuanshiExpiredEvent(long GrantId, int ActualTurn) : IGameEvent;
public sealed record AnniversaryKuanshiDamageIssuedEvent(long GrantId, long DamageFrameId, int ActualTurn, int TargetSeat,
    int PreviousDamage, int CurrentDamage, int Occurrence) : IGameEvent;
public sealed record AnniversaryKuanshiRecoveryBoundEvent(long FrameId, long GrantId, long DamageFrameId, int ActualTurn) : IGameEvent;
public sealed record AnniversaryKuanshiRecoveryReceipt(AnniversaryKuanshiGrant Grant, AnniversaryKuanshiDamageIssuedEvent Damage,
    long WindowId, int InstructionIndex);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnniversaryKuanshiRecoveryReceipt? AnniversaryKuanshi { get; init; }
}
