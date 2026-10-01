namespace CardGame.Core;
internal interface IConvertingGiftProgramHost
{
    SkillProgramStepOutcome GiveSelectedOwnedCardAndDamage(ProgramSkillFrame frame, int handLimit);
    SkillProgramStepOutcome ObserveDamageSourceHandAndGive(ProgramSkillFrame frame, int handLimit);
    SkillProgramStepOutcome DrawToHandCount(ProgramSkillFrame frame, int seat, int handLimit);
}
internal sealed record RequireSingleOwnedActivationGift : ProgramResourceOperation;
internal sealed record ConvertingGiftInstruction(bool Observe, int HandLimit) : ProgramSkillInstruction;
internal abstract class ConvertingGiftDescriptor : ProgramOperationDescriptorBase
{
    public override bool UsesConversionPolarity => true;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (e,c) => { if(e.Op==SkillProgramEffectOp.ObserveDamageSourceHandAndGive)c.ObserveDamageGift(e.Amount);else c.ConvertingGiftDamage(e.Amount); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var limit = r.RequiredInt("amount"); if (limit is < 1 or > 16) throw new InvalidOperationException("Gift reward hand limit must be 1..16.");
        var e = new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),limit,r.Condition()); RequireAlways(e,r.Path); return e;
    }
    public override ProgramSkillInstruction Compile(SkillProgramEffect e) => new ConvertingGiftInstruction(Op == SkillProgramEffectOp.ObserveDamageSourceHandAndGive,e.Amount);
}
internal sealed class GiveSelectedOwnedCardAndDamageDescriptor : ConvertingGiftDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveSelectedOwnedCardAndDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveSelectedOwnedCardAndDamageHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireSingleOwnedActivationGift(),new ReadSelectedTarget()];
}
internal sealed class ObserveDamageSourceHandAndGiveDescriptor : ConvertingGiftDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObserveDamageSourceHandAndGive;
    public override ISkillProgramEffectHandler Handler { get; } = new ObserveDamageSourceHandAndGiveHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class DrawToHandCountDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawToHandCount;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawToHandCountHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.DrawToHandCount(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","amount","condition");var limit=r.RequiredInt("amount");
        if(limit is < 0 or > 16)throw new InvalidOperationException("Hand limit must be 0..16.");
        var target=r.RequiredEnum<SkillProgramEffectTarget>("target");
        if(target is not (SkillProgramEffectTarget.Owner or SkillProgramEffectTarget.SelectedTarget))throw new InvalidOperationException("Hand limit draw needs one participant.");
        return new(Op,target,limit,r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => e.Target==SkillProgramEffectTarget.SelectedTarget ? [new ReadSelectedTarget()]:[];
}
public sealed class GiveSelectedOwnedCardAndDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.GiveSelectedOwnedCardAndDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int s,ISkillProgramEffectHost h)=>((IConvertingGiftProgramHost)h).GiveSelectedOwnedCardAndDamage(f,e.Amount);
}
public sealed class ObserveDamageSourceHandAndGiveHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ObserveDamageSourceHandAndGive;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int s,ISkillProgramEffectHost h)=>((IConvertingGiftProgramHost)h).ObserveDamageSourceHandAndGive(f,e.Amount);
}
public sealed class DrawToHandCountHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawToHandCount;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int s,ISkillProgramEffectHost h)=>((IConvertingGiftProgramHost)h).DrawToHandCount(f,s,e.Amount);
}
public sealed record ProgramConvertingGiftDraft(bool Observe, string Stage,int RecipientSeat,int HandLimit,
    IReadOnlyList<int> ObservedHandIds,int? GiftCardId=null,int? GiftOrdinal=null,bool ExactDamageDeath=false);
public sealed record GiftHandRetentionObligation(long Id,CardConversionSource Source,int CreatedTurn,int RecipientSeat,
    int CardId,int GiftOrdinal,int HandLimit);
public sealed record GiftHandRetentionScheduledEvent(GiftHandRetentionObligation Obligation):IGameEvent;
public sealed record GiftHandRetentionConsumedEvent(long Id,int OwnerSeat,bool Applied):IGameEvent;
public sealed record ProgramPrivateHandObservedEvent(long FrameId,int ViewerSeat,int SourceSeat,int CardCount):IGameEvent;
