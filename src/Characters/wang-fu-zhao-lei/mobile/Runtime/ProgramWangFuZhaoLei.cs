using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record XunyiRelationGrant(long FrameId, CardConversionSource Source, string GameplayHash,
    int HolderSeat, long InitialWindowId);
public sealed record XunyiDamageReceipt(XunyiRelationGrant Relation, long DamageFrameId, long OriginalAttackId,
    int? ActualSourceSeat, int ActualTargetSeat, int ActualAmount, int? DiscardRecipientSeat, int? DrawRecipientSeat);
public sealed record XunyiRelationChangedEvent(XunyiRelationGrant Relation, int? PreviousHolderSeat) : IGameEvent;
public sealed record XunyiDamageQualifiedEvent(XunyiDamageReceipt Receipt) : IGameEvent;
public sealed record XunyiDamageIssuedEvent(long FrameId, XunyiDamageReceipt Receipt, int InstructionIndex) : IGameEvent;
public sealed record XunyiPointPaidEvent(long FrameId, long DamageFrameId, int StepIndex, int RecipientSeat,
    bool Draw, long Before, long After, int Count, int? CardId, CardLocation? From) : IGameEvent;
public sealed record XunyiDamageCompletedEvent(long FrameId, long DamageFrameId, int StepCount) : IGameEvent;
public enum XunyiStage { DiscardChoice, Children }
public sealed record XunyiDamagePending(XunyiDamageReceipt Receipt, int InstructionIndex, int StepIndex,
    XunyiStage Stage, long Before = 0, long After = 0, int Count = 0, int? CardId = null, CardLocation? From = null);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public XunyiDamagePending? Xunyi { get; init; }
}

internal interface IWangFuZhaoLeiProgramHost
{
    SkillProgramStepOutcome ExecuteXunyi(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal abstract class WangFuZhaoLeiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new WangFuZhaoLeiHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.SettleXunyiDamagePoint
        ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op switch
    {
        SkillProgramEffectOp.ArmXunyiRelation => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting), new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)],
        SkillProgramEffectOp.SettleXunyiDamagePoint => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied), new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)]
    };
}
internal sealed class ArmXunyiRelationDescriptor : WangFuZhaoLeiDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmXunyiRelation; }
internal sealed class SettleXunyiDamagePointDescriptor : WangFuZhaoLeiDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.SettleXunyiDamagePoint; }
internal sealed class MoveXunyiAfterPartnerDeathDescriptor : WangFuZhaoLeiDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.MoveXunyiAfterPartnerDeath; }
internal sealed class WangFuZhaoLeiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IWangFuZhaoLeiProgramHost>().ExecuteXunyi(frame, op);
}
internal static class WangFuZhaoLeiContract
{
    private static bool Own(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.ArmXunyiRelation or
        SkillProgramEffectOp.SettleXunyiDamagePoint or SkillProgramEffectOp.MoveXunyiAfterPartnerDeath;
    internal static void Activation(string path, SkillProgramActivation activation)
    {
        if (activation.Effects.Any(Own)) throw new InvalidOperationException(path + ": Xunyi requires its actual starting, damage or death producer.");
    }
    internal static void Trigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(Own)) return;
        var valid = trigger.Effects switch
        {
            [{ Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.OtherLiving }, { Op: SkillProgramEffectOp.ArmXunyiRelation }] =>
                trigger.Window == SkillProgramTriggerWindow.GameStarting && trigger.Subject == SkillProgramTriggerSubject.Owner && !trigger.Optional,
            [{ Op: SkillProgramEffectOp.SettleXunyiDamagePoint }] => trigger.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                trigger.Subject == SkillProgramTriggerSubject.Any && trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && !trigger.Optional &&
                trigger.OwnerRelation is null && trigger.RequireDamageSource is null && trigger.RequireNoCardConversion is null &&
                trigger.DamageCardKinds.Count == 0 && trigger.SourceSkillId is null && trigger.SourceViewAsId is null,
            [{ Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.OtherLiving }, { Op: SkillProgramEffectOp.MoveXunyiAfterPartnerDeath }] =>
                trigger.Window == SkillProgramTriggerWindow.CharacterDied && trigger.Subject == SkillProgramTriggerSubject.Owner && trigger.Optional,
            _ => false
        };
        if (!valid || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.UsageLimit is not null ||
            trigger.MarkerCost is not null || trigger.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
            throw new InvalidOperationException(path + ": Xunyi requires the complete actual relation and per-point native consequences.");
    }
}
