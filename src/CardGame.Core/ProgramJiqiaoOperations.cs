namespace CardGame.Core;
internal interface IJiqiaoProgramHost { SkillProgramStepOutcome ExecutePaidJiqiao(ProgramSkillFrame frame,SkillProgramEffect effect); }
internal sealed class DiscardEquipmentAndResolveJiqiaoDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DiscardEquipmentAndResolveJiqiao;
    public override ISkillProgramEffectHandler Handler{get;}=new JiqiaoHandler();
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,2,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","sourceBind","resultBind","visibility","condition");
        var e=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition(),sourceBind:r.RequiredIdentifier("sourceBind"),resultBind:r.RequiredIdentifier("resultBind"),visibility:r.RequiredEnum<SkillProgramCardSetVisibility>("visibility"));
        RequireAlways(e,r.Path);if(e.Visibility!=SkillProgramCardSetVisibility.Public || e.SourceBind==e.ResultBind)throw new InvalidOperationException("Jiqiao requires a distinct publicly revealed result set.");return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting),new RequireOwnedCardSet(e.SourceBind!,SkillProgramEffectTarget.Owner,null,[CardZoneKind.Hand,CardZoneKind.Equipment]),new MoveCardSet(e.SourceBind!,null,SkillProgramCardDestination.DiscardPile),new CreateCardSet(e.ResultBind!,int.MaxValue,true),new DeriveCardSet(e.ResultBind!,e.ResultBind!+".non-equipment",[],Categories:[SkillProgramCardCategory.Basic,SkillProgramCardCategory.Trick]),new MoveCardSet(e.ResultBind!+".non-equipment",null,SkillProgramCardDestination.OwnerHand),new MoveCardSet(e.ResultBind!,e.ResultBind!+".non-equipment",SkillProgramCardDestination.DiscardPile)];
}
internal sealed class JiqiaoHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.DiscardEquipmentAndResolveJiqiao;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int s,ISkillProgramEffectHost h)=>h.GetCapability<IJiqiaoProgramHost>().ExecutePaidJiqiao(f,e);
}
internal static class JiqiaoContract
{
    internal static void ValidateActivation(string path,SkillProgramActivation a)
    {if(a.Effects.Any(e=>e.Op==SkillProgramEffectOp.DiscardEquipmentAndResolveJiqiao))throw new InvalidOperationException($"Invalid skill program at {path}: Jiqiao requires actual Play-start.");}
    internal static void ValidateTrigger(string path,SkillProgramTrigger t)
    {
        if(!t.Effects.Any(e=>e.Op==SkillProgramEffectOp.DiscardEquipmentAndResolveJiqiao))return;
        if(t.Window!=SkillProgramTriggerWindow.PlayPhaseStarting || t.Subject!=SkillProgramTriggerSubject.Owner || t.OwnerRelation is not null || !t.Optional || t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own || t.UsageLimit is not null || t.Condition.Kind!=SkillProgramTriggerConditionKind.Always ||
            t.Effects is not [{Op:SkillProgramEffectOp.SelectOwnedCards} select,{Op:SkillProgramEffectOp.DiscardEquipmentAndResolveJiqiao} pay] ||
            select.Target!=SkillProgramEffectTarget.Owner || select.TargetReference is not null || !select.Zones.SequenceEqual(new[]{CardZoneKind.Hand,CardZoneKind.Equipment}) || select.MinimumCards!=0 || select.MaximumCards!=int.MaxValue || !select.AllowDecline || select.ResultBind!=pay.SourceBind || select.CardKinds.Count==0 ||
            select.CardKinds.Any(k=>!IsEquipment(k)))throw new InvalidOperationException($"Invalid skill program at {path}: Jiqiao requires one arbitrary equipment selection followed by its exact paid reveal.");
    }
    private static bool IsEquipment(CardKind k)=>EquipmentCatalog.IsEquipment(k) && k!=CardKind.GeneralWeapon;
}
