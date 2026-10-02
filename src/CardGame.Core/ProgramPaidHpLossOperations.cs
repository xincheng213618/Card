namespace CardGame.Core;

public sealed record ProgramHpLossQuantity(int InstructionIndex, string ResultBind, int OwnerSeat,
    int Maximum, int TurnNumber, int TurnOwnerSeat);
public sealed record ProgramPaidHpLossReceipt(int InstructionIndex, string ResultBind, int OwnerSeat,
    string SkillId, string BindingId, string SkillInstanceId, string GameplayHash, string UsageGroup,
    int TurnNumber, int TurnOwnerSeat, int Requested, int HpBefore, int HpAfter, int ActualLost,
    bool DrawIssued = false, bool DrawReturned = false, long? DistanceGrantSequence = null, long? SlashGrantSequence = null);

public sealed record ProgramHpLossModifierOrigin(long ProducerFrameId,int ProducerInstructionIndex,string ResultBind,
    int OwnerSeat,string SkillId,string BindingId,string SkillInstanceId,string GameplayHash,string UsageGroup,
    int TurnNumber,int TurnOwnerSeat,int Requested,int HpBefore,int HpAfter,int ActualLost);

internal static class PaidHpLossProgram
{
    internal static bool IsValidModifierOrigin(TurnRuleModifier m) => m.PaidHpLossOrigin is{ } p &&
        p.ProducerFrameId==m.ParentFrameId && p.ProducerFrameId>0 && p.ProducerInstructionIndex==1 && !string.IsNullOrWhiteSpace(p.ResultBind) &&
        p.OwnerSeat==m.Source.OwnerSeat && p.SkillId==m.Source.SkillId && p.BindingId==m.Source.BindingId && p.SkillInstanceId==m.Source.SkillInstanceId &&
        !string.IsNullOrWhiteSpace(p.SkillId) && !string.IsNullOrWhiteSpace(p.BindingId) && !string.IsNullOrWhiteSpace(p.SkillInstanceId) && !string.IsNullOrWhiteSpace(p.GameplayHash) && !string.IsNullOrWhiteSpace(p.UsageGroup) &&
        p.TurnNumber==m.TurnNumber && p.TurnNumber>0 && p.TurnOwnerSeat==m.TurnSeat && p.OwnerSeat==p.TurnOwnerSeat &&
        p.HpBefore>0 && p.Requested>0 && p.Requested<=p.HpBefore && p.HpAfter==p.HpBefore-p.Requested && p.ActualLost==p.HpBefore-p.HpAfter &&
        m.Operation==SkillRuleOperation.Add && m.AffectedSeat is null && (m.CardKinds?.Count??0)==0 &&
        (m.Query==SkillRuleQuery.OutgoingDistance && m.EffectIndex==2 && m.Amount==-p.ActualLost ||
         m.Query==SkillRuleQuery.SlashLimit && m.EffectIndex==3 && m.Amount==p.ActualLost);
    internal static bool IsOperation(SkillProgramEffect effect) => effect.Op is
        SkillProgramEffectOp.ChooseOwnerHpLoss or SkillProgramEffectOp.DrawPaidHpLoss or
        SkillProgramEffectOp.GrantPaidHpLossDistance or SkillProgramEffectOp.GrantPaidHpLossSlashLimit;
    internal static bool IsExactChain(IReadOnlyList<SkillProgramEffect> effects) => effects.Count == 4 &&
        effects[0].Op == SkillProgramEffectOp.ChooseOwnerHpLoss && effects[1].Op == SkillProgramEffectOp.DrawPaidHpLoss &&
        effects[2].Op == SkillProgramEffectOp.GrantPaidHpLossDistance && effects[3].Op == SkillProgramEffectOp.GrantPaidHpLossSlashLimit &&
        effects.All(e => e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always) &&
        effects[0].ResultBind is { Length: > 0 } bind && effects.Skip(1).All(e => e.SourceBind == bind);
    internal static void ValidateComposition(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, int selectedCardCount, bool selectedTarget, int targetMaximum)
    {
        if (!effects.Any(IsOperation)) return;
        if (window is not null || selectedCardCount != 0 || selectedTarget || targetMaximum != 0 || !IsExactChain(effects))
            throw new InvalidOperationException($"Invalid skill program at {path}: paid HP loss requires its exact owner-only quantity/Draw/distance/Slash four-instruction chain.");
    }
}

internal abstract class PaidHpLossOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        var producer = Op == SkillProgramEffectOp.ChooseOwnerHpLoss;
        r.AllowOnly("op", "target", producer ? "resultBind" : "sourceBind", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("Paid HP loss is owner-only.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition(),
            sourceBind: producer ? null : r.RequiredIdentifier("sourceBind"),
            resultBind: producer ? r.RequiredIdentifier("resultBind") : null);
        RequireAlways(effect, r.Path); return effect;
    }
    // The finite chain is validated as one exact owned resource contract.
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
internal sealed class ChooseOwnerHpLossOperationDescriptor : PaidHpLossOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseOwnerHpLoss;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseOwnerHpLossHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.LoseHp, static (_, c) => c.PaidHpLossEstimate(0));
}
internal sealed class DrawPaidHpLossOperationDescriptor : PaidHpLossOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawPaidHpLoss;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawPaidHpLossHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PaidHpLossEstimate(1));
}
internal sealed class GrantPaidHpLossDistanceOperationDescriptor : PaidHpLossOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantPaidHpLossDistance;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantPaidHpLossDistanceHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier, static (_, c) => c.PaidHpLossEstimate(2));
}
internal sealed class GrantPaidHpLossSlashLimitOperationDescriptor : PaidHpLossOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantPaidHpLossSlashLimit;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantPaidHpLossSlashLimitHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier, static (_, c) => c.PaidHpLossEstimate(3));
}
public sealed class ChooseOwnerHpLossHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseOwnerHpLoss;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h) => h.ChooseOwnerHpLoss(f,e.ResultBind!);
}
public sealed class DrawPaidHpLossHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawPaidHpLoss;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h) => h.DrawPaidHpLoss(f,e.SourceBind!);
}
public sealed class GrantPaidHpLossDistanceHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantPaidHpLossDistance;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h) {h.GrantPaidHpLoss(f,e.SourceBind!,true);return SkillProgramStepOutcome.Continue;}
}
public sealed class GrantPaidHpLossSlashLimitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantPaidHpLossSlashLimit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h) {h.GrantPaidHpLoss(f,e.SourceBind!,false);return SkillProgramStepOutcome.Continue;}
}
