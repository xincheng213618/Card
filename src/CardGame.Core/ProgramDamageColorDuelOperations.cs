namespace CardGame.Core;

public sealed record ProgramDamageAppearanceReceipt(int InstructionIndex, long DamageWindowFrameId,
    long DamageFrameId, int OwnerSeat, int? SourceSeat, CardKind? EffectiveCardKind, bool HasCard,
    bool IsRed, int? RecipientSeat, bool DrawIssued);
public sealed record ProgramSelectedActorDuelOrigin(long ParentProgramFrameId, int InstructionIndex,
    int OwnerSeat, int InitialActorSeat, string SkillId, string ActivationId, string SkillInstanceId,
    string GameplayHash, int TurnNumber, bool AttackStarted = false);
internal interface IDamageColorDuelProgramHost
{
    SkillProgramStepOutcome DrawByDamageCardColor(ProgramSkillFrame frame);
    SkillProgramStepOutcome UseSelectedActorDuel(ProgramSkillFrame frame);
}
internal sealed class DrawByDamageCardColorDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawByDamageCardColor;
    public override ISkillProgramEffectHandler Handler { get; } = new DamageColorDuelHandler(SkillProgramEffectOp.DrawByDamageCardColor);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_,c)=>c.DamageAppearanceDraw());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","condition");
        if(r.RequiredEnum<SkillProgramEffectTarget>("target")!=SkillProgramEffectTarget.Owner) throw new InvalidOperationException("Damage appearance Draw is owner-only.");
        var e=new SkillProgramEffect(Op,SkillProgramEffectTarget.Owner,0,r.Condition()); RequireAlways(e,r.Path);return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.DamageAppliedBeforeDying)];
}
internal sealed class UseSelectedActorDuelDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseSelectedActorDuel;
    public override ISkillProgramEffectHandler Handler { get; } = new DamageColorDuelHandler(SkillProgramEffectOp.UseSelectedActorDuel);
    public override ProgramOperationLegalityPolicy LegalityPolicy => ProgramOperationLegalityPolicy.OtherRecipient;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_,c)=>c.SelectedActorDuel());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","condition");
        if(r.RequiredEnum<SkillProgramEffectTarget>("target")!=SkillProgramEffectTarget.Owner) throw new InvalidOperationException("Selected actor Duel is owned by its program owner.");
        var e=new SkillProgramEffect(Op,SkillProgramEffectTarget.Owner,0,r.Condition());RequireAlways(e,r.Path);return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[new ReadSelectedTarget()];
}
internal sealed class DamageColorDuelHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int targetSeat,ISkillProgramEffectHost host)=>
        op==SkillProgramEffectOp.DrawByDamageCardColor ? ((IDamageColorDuelProgramHost)host).DrawByDamageCardColor(frame) : ((IDamageColorDuelProgramHost)host).UseSelectedActorDuel(frame);
}
