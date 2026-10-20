using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ProgramLiushiDrawPayment(long FrameId, string SkillId, string SkillInstanceId,
    string GameplayHash, int OwnerSeat, long WindowId, int TargetSeat, int InstructionIndex,
    int RequestedCount, int ActualCount, long SequenceBefore, long SequenceAfter, long? AttemptId, bool Replaced);
public sealed record ProgramLiushiDrawPaidEvent(ProgramLiushiDrawPayment Payment) : IGameEvent;
public sealed record ProgramLiushiEffectAppliedEvent(long FrameId, long DamageWindowId, long DamageFrameId,
    long CardUseFrameId, long ActionId, int OwnerSeat, int TargetSeat, string SkillId, string SkillInstanceId,
    int MarkerCount) : IGameEvent;
public sealed record ProgramLiushiEffectClearedEvent(long FrameId, long WindowId, int OwnerSeat, int TargetSeat,
    int RemovedCount) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramLiushiDrawPayment? LiushiDrawPayment { get; init; }
}

internal interface ILiushiEffectHost
{
    void ApplyLiushiEffect(ProgramSkillFrame frame);
    void ClearLiushiEffect(ProgramSkillFrame frame);
}

internal abstract class LiushiEffectDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new LiushiEffectHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChangeAttributedMarker, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.ApplyLiushiEffect
            ? SkillProgramTriggerWindow.AfterDamageApplied : SkillProgramTriggerWindow.DiscardPhaseEnded)];
}
internal sealed class ApplyLiushiEffectDescriptor : LiushiEffectDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ApplyLiushiEffect; }
internal sealed class ClearLiushiEffectDescriptor : LiushiEffectDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClearLiushiEffect; }
internal sealed class LiushiEffectHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host)
    {
        var capability = host.GetCapability<ILiushiEffectHost>();
        if (Op == SkillProgramEffectOp.ApplyLiushiEffect) capability.ApplyLiushiEffect(f);
        else capability.ClearLiushiEffect(f);
        return SkillProgramStepOutcome.Continue;
    }
}

internal static class LiushiEffectContract
{
    internal static bool IsActivation(SkillProgramActivation a) =>
        a.MinCards == 1 && a.MaxCards == 1 && a.MinTargets == 1 && a.MaxTargets == 1 &&
        a.TargetKind == SkillProgramTargetKind.OtherLivingVirtualSlashTarget &&
        a.CardSuits.SequenceEqual([Suit.Heart]) && a.SourceZones.Count > 0 &&
        a.SourceZones.All(z => z is CardZoneKind.Hand or CardZoneKind.Equipment) &&
        a.Effects is [{ Op: SkillProgramEffectOp.CaptureSelectedCards, Target: SkillProgramEffectTarget.Owner,
                TargetReference: null, ResultBind: { } bind, Condition.Kind: SkillProgramConditionKind.Always },
            { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, TargetReference: null,
                SourceBind: { } moved, ExceptBind: null, Destination: SkillProgramCardDestination.DrawPileTop, DestinationZone: null,
                AwaitMovementTriggers: true, Condition.Kind: SkillProgramConditionKind.Always },
            { Op: SkillProgramEffectOp.UseVirtualCard, Target: SkillProgramEffectTarget.SelectedTarget, TargetReference: null,
                OutputKind: CardKind.Slash, UseCardActionWindows: true,
                TargetRestriction: SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget, Condition.Kind: SkillProgramConditionKind.Always }] && bind == moved;
    internal static bool Clears(SkillProgramTrigger t) => t.Effects is
        [{ Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, TargetReference: null,
           NumberExpression: SkillProgramNumberExpression.TurnOwnerDiscardPhaseHandDiscardCount, Condition.Kind: SkillProgramConditionKind.Always },
         { Op: SkillProgramEffectOp.ClearLiushiEffect, Condition.Kind: SkillProgramConditionKind.Always }];
    internal static void ValidateTrigger(string path, string skillId, SkillProgramTrigger t)
    {
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.ApplyLiushiEffect) &&
            (t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Source ||
             t.SourceSkillId != skillId || t.SourceViewAsId is null || t.Optional ||
             t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
             t.Effects is not [{ Op: SkillProgramEffectOp.ApplyLiushiEffect }]))
            throw new InvalidOperationException($"Invalid skill program at {path}: applying Liushi requires one mandatory conversion-source perDamage instruction.");
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.ClearLiushiEffect) &&
            (t.Window != SkillProgramTriggerWindow.DiscardPhaseEnded || t.Subject != SkillProgramTriggerSubject.Owner ||
             t.TurnOwnerScope != SkillProgramTurnOwnerScope.OtherLiving || t.Optional || !Clears(t)))
            throw new InvalidOperationException($"Invalid skill program at {path}: clearing Liushi requires a mandatory foreign discard-end Draw followed by its clear instruction.");
    }
}
