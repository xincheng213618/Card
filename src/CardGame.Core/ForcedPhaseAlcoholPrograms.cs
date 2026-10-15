using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ProgramPhaseAlcoholStage { Paid, WineIssued, DamageIssued }
public sealed record ProgramPhaseAlcoholReceipt(int PaymentInstruction, int WineInstruction,
    long PhaseParentFrameId, int ActorSeat, int TurnNumber, int CardId, long PaymentSequenceBefore,
    long PaymentSequenceAfter, string PaymentReason, ProgramPhaseAlcoholStage Stage, long? WineFrameId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool WineSkipped = false);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramPhaseAlcoholReceipt? PhaseAlcohol { get; init; }
}

internal interface IForcedPhaseAlcoholHost
{
    SkillProgramStepOutcome UsePhaseOwnerVirtualAlcohol(ProgramSkillFrame frame, int damageToOther);
}
internal sealed class UsePhaseOwnerVirtualAlcoholDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UsePhaseOwnerVirtualAlcohol;
    public override ISkillProgramEffectHandler Handler { get; } = new UsePhaseOwnerVirtualAlcoholHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs,
        static (e, c) => c.PhaseOwnerAlcohol(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "damageToOther", "condition");
        var damage = r.RequiredInt("damageToOther");
        if (damage is < 0 or > 20) throw new InvalidOperationException($"Invalid skill program at {r.Path}: damageToOther must be 0..20.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), damage, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class UsePhaseOwnerVirtualAlcoholHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UsePhaseOwnerVirtualAlcohol;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IForcedPhaseAlcoholHost>().UsePhaseOwnerVirtualAlcohol(f, e.Amount);
}
internal static class ForcedPhaseAlcoholContract
{
    internal static bool IsChain(IReadOnlyList<SkillProgramEffect> effects) =>
        effects is [var select, var move, var wine] &&
        select is { Op: SkillProgramEffectOp.SelectOwnedCards, Target: SkillProgramEffectTarget.Owner,
            TargetReference: null, MinimumCards: 1, MaximumCards: 1, NumberExpression: null,
            AllowDecline: false, Condition.Kind: SkillProgramConditionKind.Always } &&
        select.Zones.SequenceEqual([CardZoneKind.Hand]) && select.CardKinds.Count == 0 && select.Suits.Count == 0 &&
        select.CardCategories.Count == 0 && select.EquipmentSlots.Count == 0 &&
        move is { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner,
            Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true,
            ExceptBind: null, Condition.Kind: SkillProgramConditionKind.Always } && move.SourceBind == select.ResultBind &&
        wine is { Op: SkillProgramEffectOp.UsePhaseOwnerVirtualAlcohol, Condition.Kind: SkillProgramConditionKind.Always };

    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.UsePhaseOwnerVirtualAlcohol) &&
            (trigger.Window != SkillProgramTriggerWindow.PlayPhaseStarting || trigger.Subject != SkillProgramTriggerSubject.Owner ||
             trigger.TurnOwnerScope is not (SkillProgramTurnOwnerScope.Own or SkillProgramTurnOwnerScope.OtherLiving) ||
             !trigger.Optional || !IsChain(trigger.Effects)))
            throw new InvalidOperationException($"Invalid skill program at {path}: phase Wine requires an optional actual Play-start owner window, one exact hand discard with drained children, and a terminal Wine use.");
        if (trigger.Effects.Any(e => e.NumberExpression == SkillProgramNumberExpression.ActualTurnDeathBenefits) &&
            (trigger.Window != SkillProgramTriggerWindow.AfterTurnEnded || trigger.Subject != SkillProgramTriggerSubject.Owner ||
             trigger.TurnOwnerScope is not (SkillProgramTurnOwnerScope.Own or SkillProgramTurnOwnerScope.OtherLiving) ||
             !trigger.Optional || trigger.Condition is not { Kind: SkillProgramTriggerConditionKind.Compare,
                Left.Kind: SkillProgramTriggerValueKind.ActualTurnDeathCount, Comparison: SkillProgramComparisonOperator.GreaterThan,
                Right.Kind: SkillProgramTriggerValueKind.IntegerConstant, Right.Value: 0 } ||
             trigger.Effects is not [{ Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner,
                 NumberExpression: SkillProgramNumberExpression.ActualTurnDeathBenefits, Condition.Kind: SkillProgramConditionKind.Always }]))
            throw new InvalidOperationException($"Invalid skill program at {path}: death benefits require one optional owner draw after the exact actual turn ended.");
    }
}
