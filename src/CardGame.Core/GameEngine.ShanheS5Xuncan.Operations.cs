using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ShanheS5YunshenReceipt(CardConversionSource Source, string GameplayHash, int OriginalTargetSeat,
    int? ActorSeat = null, int? SlashTargetSeat = null, long? UseId = null, bool Returned = false);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShanheS5YunshenReceipt? ShanheS5Yunshen { get; init; }
}

public sealed record ShanheS5FirstElementalDamageEvent(int TurnNumber, long DamageFrameId, int TargetSeat, DamageNature Nature) : IGameEvent;
public sealed record ShanheS5SkillTargetEvent(long FrameId, CardConversionSource Source, string GameplayHash, int TargetSeat) : IGameEvent;
public sealed record ShanheS5FenchaiBoundEvent(long ProducerFrameId, int OwnerSeat, string FenchaiSkillId, string FenchaiSkillInstanceId, int TargetSeat) : IGameEvent;
public sealed record ShanheS5YunshenIssuedEvent(long FrameId, long UseId, long ActionId, int ActorSeat, int TargetSeat) : IGameEvent;
public sealed record ShanheS5JudgmentEffectEvent(long FrameId, long JudgmentFrameId, string Stage, int TargetSeat, bool LightningHit, int Requested) : IGameEvent;
public sealed record ShanheS5RefillRequestedEvent(long FrameId, int TargetSeat, int Requested) : IGameEvent;
public sealed record ShanheS5XuncanDrawPaidEvent(long FrameId, int TargetSeat, int Requested, int Actual, long Before, long After) : IGameEvent;

internal interface IShanheS5XuncanHost
{ SkillProgramStepOutcome ResolveShanheS5Xuncan(ProgramSkillFrame frame, SkillProgramEffect effect); }

internal sealed class ResolveShanheS5XuncanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheS5Xuncan;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveShanheS5XuncanHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeDrawInvocation,
        Activated: ProgramTrackedFact.NativeActionAppearance);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (e, c) => { if (e.StateId is "shangshen-refill" or "leijie" or "yunshen-slash") c.PublicControlValue(2); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stage", "sourceBind", "condition");
        var stage = r.RequiredIdentifier("stage");
        if (stage is not ("yunshen-bind" or "yunshen-slash" or "shangshen-bind" or "shangshen-lightning" or "shangshen-refill" or "leijie") ||
            r.Has("sourceBind") != (stage is "shangshen-lightning" or "leijie"))
            throw new InvalidOperationException(r.Path + ": Xuncan requires its exact stage and judgment outcome binding.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            stateId: stage, sourceBind: r.OptionalIdentifier("sourceBind"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => e.StateId switch
    {
        "yunshen-bind" or "yunshen-slash" => [new ReadSelectedTarget()],
        "leijie" => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow), new ReadSelectedTarget()],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]
    };
}
public sealed class ResolveShanheS5XuncanHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheS5Xuncan;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IShanheS5XuncanHost>().ResolveShanheS5Xuncan(f, e);
}

internal static class ShanheS5XuncanComposition
{
    private static bool Own(SkillProgramEffect e, string stage) => e.Op == SkillProgramEffectOp.ResolveShanheS5Xuncan && e.StateId == stage;
    internal static void ValidateActivation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveShanheS5Xuncan)) return;
        if (a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving ||
            a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.UsesPerPhaseExpression is not null ||
            a.Effects is not [var bind, { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1 }, var slash] ||
            !Own(bind, "yunshen-bind") || !Own(slash, "yunshen-slash") || a.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
            throw new InvalidOperationException(path + ": Yunshen requires exactly one other target, one recovery and one directional native Cold Slash per Play phase.");
    }
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveShanheS5Xuncan)) return;
        var valid = t.Optional && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null &&
            t.DynamicUsageLimit is null && t.MarkerCost is null && t.NamedUsageGroup is null && t.ChoiceGroup is null && t.RequiredPolarity is null &&
            t.Effects.All(e => e.Condition.Kind == SkillProgramConditionKind.Always);
        if (t.Effects is [var bind, var judgment, var cleanup, var lightning, var refill] && Own(bind, "shangshen-bind"))
            valid &= t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Subject == SkillProgramTriggerSubject.Any &&
                t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && t.DamageCardKinds.Count == 0 &&
                Judgment(judgment, cleanup, lightning, "shangshen-lightning", SkillProgramEffectTarget.Owner, "shanhe.s5.shangshen.lightning") && Own(refill, "shangshen-refill");
        else if (t.Effects is [{ Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.AnyLiving }, var judge, var clean, var resolve] && Own(resolve, "leijie"))
            valid &= t.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && t.Subject == SkillProgramTriggerSubject.Owner &&
                t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && Judgment(judge, clean, resolve, "leijie", SkillProgramEffectTarget.SelectedTarget, "shanhe.s5.leijie");
        else valid = false;
        if (!valid) throw new InvalidOperationException(path + ": Xuncan/Caoshi require their exact first-elemental or own-preparation judgment, cleanup and native result sequence.");
    }
    private static bool Judgment(SkillProgramEffect j, SkillProgramEffect c, SkillProgramEffect r, string stage, SkillProgramEffectTarget target, string reason) =>
        j.Op == SkillProgramEffectOp.StartJudgment && j.Target == target && j.SourceRef is null && j.JudgmentReason == reason && j.OutcomeBind is { } outcome &&
        c.Op == SkillProgramEffectOp.MoveBoundCards && c.Target == SkillProgramEffectTarget.Owner && c.Destination == SkillProgramCardDestination.DiscardPile &&
        c.ExceptBind is null && c.SourceBind == j.ResultBind && Own(r, stage) && r.SourceBind == outcome;
}
