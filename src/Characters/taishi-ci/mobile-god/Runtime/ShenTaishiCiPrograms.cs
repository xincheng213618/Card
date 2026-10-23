using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ShenTaishiCiPoweiStage { Choice, MovementChildren, DamageChildren }
public sealed record ShenTaishiCiPoweiReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    int ActualTurn, int TargetSeat, int InstructionIndex, ShenTaishiCiPoweiStage Stage,
    string? Branch = null, int? CardId = null, long Before = 0, long After = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShenTaishiCiPoweiReceipt? ShenTaishiCiPowei { get; init; }
}
public sealed record ShenTaishiCiPoweiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, int ActualTurn, int TargetSeat, int InstructionIndex) : IGameEvent;
public sealed record ShenTaishiCiPoweiPaidEvent(long FrameId, string Branch, int CardId, CardLocation From,
    CardLocation To, long Sequence) : IGameEvent;
public sealed record ShenTaishiCiPoweiMissionEvent(long FrameId, int OwnerSeat, bool Success) : IGameEvent;
public sealed record ShenTaishiCiPoweiRangeEvent(long FrameId, int OwnerSeat, int AttackerSeat, int ActualTurn) : IGameEvent;
public sealed record ShenTaishiCiDulieCancelledEvent(long FrameId, long UseId, long ActionId, int OwnerSeat, int RemainingTargetCount) : IGameEvent;
public sealed record ShenTaishiCiDulieRetainedTargetEvent(long FrameId, long UseId, int Index, int TargetSeat) : IGameEvent;

internal interface IShenTaishiCiHost
{
    SkillProgramStepOutcome Powei(ProgramSkillFrame frame);
    void Dulie(ProgramSkillFrame frame);
}
internal sealed class ResolveShenTaishiCiPoweiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShenTaishiCiPowei;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new ShenTaishiCiPoweiHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.GameStarting, SkillProgramTriggerWindow.TurnBeforeStart,
            SkillProgramTriggerWindow.OtherActualTurnStarted, SkillProgramTriggerWindow.AfterDamageApplied,
            SkillProgramTriggerWindow.DyingEntering])];
}
internal sealed class ShenTaishiCiPoweiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShenTaishiCiPowei;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IShenTaishiCiHost>().Powei(frame);
}
internal sealed class CancelShenTaishiCiDulieDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.CancelShenTaishiCiDulie;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new ShenTaishiCiDulieHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.NullifyCurrentCardEffect, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new ReadCardSet("dulie-judgment")];
}
internal sealed class ShenTaishiCiDulieHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.CancelShenTaishiCiDulie;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host)
    { host.GetCapability<IShenTaishiCiHost>().Dulie(frame); return SkillProgramStepOutcome.Continue; }
}

internal static class ShenTaishiCiComposition
{
    [ProgramCatalogValidation(38200)]
    internal static void Validate(string path, SkillProgram p)
    {
        // This edition shares the exact finalized-judgment/cancellation primitive;
        // its complete immutable shape is checked by HappyTaishiCiComposition.
        if (p.Id == "happy:dulie") return;
        if (p.Id is not ("mobile-god:dulie" or "mobile-god:powei" or "mobile-god:shenzhu"))
        {
            if (p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e =>
                e.Op is SkillProgramEffectOp.ResolveShenTaishiCiPowei or SkillProgramEffectOp.CancelShenTaishiCiDulie))
                throw new InvalidOperationException(path + ": Shen Taishi Ci operations require their complete native skills.");
            return;
        }
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.HpSkillGrants.Count != 0)
            throw new InvalidOperationException(path + ": mobile Shen Taishi Ci requires its trigger-only official version.");
        var ids = p.Id switch
        {
            "mobile-god:dulie" => new[] { "higher-hp-slash-heart" },
            "mobile-god:powei" => ["initialize", "rotate", "success", "damage-removes-wei", "marked-turn", "failure"],
            _ => ["physical-slash-choice"]
        };
        if (!p.Triggers.Select(t => t.Id).SequenceEqual(ids) || p.Triggers.Any(t => t.Optional || t.UsageLimit is not null || t.MarkerCost is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always))
            throw new InvalidOperationException(path + ": Shen Taishi Ci requires every official locked trigger and exactly one mission outcome.");
        foreach (var t in p.Triggers)
        {
            var window = t.Id switch
            {
                "higher-hp-slash-heart" => SkillProgramTriggerWindow.CardUseTargetsFinalized,
                "initialize" => SkillProgramTriggerWindow.GameStarting,
                "rotate" or "success" => SkillProgramTriggerWindow.TurnBeforeStart,
                "damage-removes-wei" => SkillProgramTriggerWindow.AfterDamageApplied,
                "marked-turn" => SkillProgramTriggerWindow.OtherActualTurnStarted,
                "failure" => SkillProgramTriggerWindow.DyingEntering,
                _ => SkillProgramTriggerWindow.CardUseCompleted
            };
            if (t.Window != window) throw new InvalidOperationException(path + ": Shen Taishi Ci lost its native actual occurrence.");
            if (p.Id == "mobile-god:dulie" && (t.OwnerRelation != SkillProgramCardActionOwnerRelation.Target ||
                !t.CardKinds.ToHashSet().SetEquals([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) ||
                t.Effects is not [{ Op: SkillProgramEffectOp.StartJudgment, Target: SkillProgramEffectTarget.Owner,
                    JudgmentReason: "skill.mobile-god.dulie", ResultBind: "dulie-judgment", OutcomeBind: "dulie-outcome", Visibility: SkillProgramCardSetVisibility.Public },
                    { Op: SkillProgramEffectOp.CancelShenTaishiCiDulie, Target: SkillProgramEffectTarget.Owner },
                    { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, SourceBind: "dulie-judgment", Destination: SkillProgramCardDestination.DiscardPile }] ||
                t.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always)))
                throw new InvalidOperationException(path + ": Dulie requires the exact incoming Slash, finalized public judgment, true target cancellation and owned material cleanup pipeline.");
            if (p.Id == "mobile-god:shenzhu" && (t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
                !t.CardKinds.ToHashSet().SetEquals([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash])))
                throw new InvalidOperationException(path + ": Shenzhu requires the owner's actual completed Slash use.");
            if (p.Id == "mobile-god:powei" && (t.Subject != (t.Id == "damage-removes-wei" ? SkillProgramTriggerSubject.Any : SkillProgramTriggerSubject.Owner) ||
                t.Effects.Count != (t.Id == "failure" ? 4 : t.Id == "success" ? 2 : 1) || t.Effects[0].Op != SkillProgramEffectOp.ResolveShenTaishiCiPowei ||
                t.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always || e.Target != SkillProgramEffectTarget.Owner) ||
                t.Id == "damage-removes-wei" && t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
                t.Id == "success" && (t.Effects[1].Op != SkillProgramEffectOp.GrantSkills || !t.Effects[1].SkillIds.SequenceEqual(["mobile-god:shenzhu"])) ||
                t.Id == "failure" && (t.Effects[1].Op != SkillProgramEffectOp.RecoverTo || t.Effects[1].MinimumValue != 1 || t.Effects[1].NumberExpression != SkillProgramNumberExpression.IntegerConstant ||
                    t.Effects[2].Op != SkillProgramEffectOp.ResolveShenTaishiCiPowei || t.Effects[3].Op != SkillProgramEffectOp.DiscardOwnedZoneCards || !t.Effects[3].Zones.SequenceEqual([CardZoneKind.Equipment]))))
                throw new InvalidOperationException(path + ": Powei requires its full initialization, migration, damage, optional payment and success/failure pipeline.");
        }
    }
}
