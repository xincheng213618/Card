namespace CardGame.Core;

/// <summary>Immutable admission to existing true-use receipts; no second payment or runtime state.</summary>
internal sealed class HappyLonghunCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "happy:longhun";
    public bool AllowsSingleActionInstance => true;
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        var suit=t.SourceViewAsId switch{"2-heart"=>"heart","2-diamond"=>"diamond","2-club"=>"club","2-spade"=>"spade",_=>null};
        var red=suit is "heart" or "diamond";
        if(suit is null||t.Id!="two-"+suit+(red?"-draw":"-obtain")||t.SourceSkillId!=SkillId||t.Window!=SkillProgramTriggerWindow.CardUseCommitted||
            t.OwnerRelation!=SkillProgramCardActionOwnerRelation.ConversionSource||t.Subject is not null||t.Optional||!t.IncludeResponseUses||!t.AllowNoEventTarget||t.SingleActionInstance!=red||t.IsSkillInvocation||
            t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageScope is not null||t.UsageLimit is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||t.MarkerCost is not null||
            t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.RequireNoCardConversion is not null||t.Priority!=0||
            (red?t.Effects is not[{Op:SkillProgramEffectOp.Draw,Target:SkillProgramEffectTarget.Owner,TargetReference:null,Amount:1,NumberExpression:null,SourceBind:null,ResultBind:null,Condition.Kind:SkillProgramConditionKind.Always}]:
                t.Effects is not[
                    {Op:SkillProgramEffectOp.SelectTarget,Target:SkillProgramEffectTarget.Owner,TargetReference:null,TargetKind:SkillProgramTargetKind.CurrentTurnPlayer,Condition.Kind:SkillProgramConditionKind.Always},
                    {Op:SkillProgramEffectOp.SelectAndMoveOwnedCard,Target:SkillProgramEffectTarget.Owner,TargetReference:null,Amount:1,ChooserRef.Kind:ProgramParticipantRef.Owner,CardOwnerRef.Kind:ProgramParticipantRef.SelectedTarget,
                     Destination:SkillProgramCardDestination.OwnerHand,Zones:[CardZoneKind.Hand,CardZoneKind.Equipment],SkipIfNoCards:true,AwaitMovementTriggers:true,AllowDecline:false,Condition.Kind:SkillProgramConditionKind.NotOwnTurn},
                    {Op:SkillProgramEffectOp.SelectAndMoveOwnedCard,Target:SkillProgramEffectTarget.Owner,TargetReference.Kind:ProgramParticipantRef.CardProvider,Amount:1,ChooserRef.Kind:ProgramParticipantRef.CardProvider,CardOwnerRef.Kind:ProgramParticipantRef.Owner,
                     Destination:SkillProgramCardDestination.SelectedTargetHand,Zones:[CardZoneKind.Hand,CardZoneKind.Equipment],SkipIfNoCards:true,AwaitMovementTriggers:true,AllowSameOwnerHandReturn:true,AllowDecline:false,Condition.Kind:SkillProgramConditionKind.OwnTurn}]))
            throw new InvalidOperationException("Happy Longhun requires exactly one compulsory draw for a two-red actual use, or the current actual-turn owner's one-card hand/equipment obtain for a two-black actual use.");
    }
}

internal static class HappyLonghunComposition
{
    [ProgramCatalogValidation(41640)] internal static void Validate(string path,SkillProgram p)
    {
        if(p.Id!="happy:longhun")return;
        var expected=new Dictionary<string,(Suit Suit,CardKind Kind,int Count)>(StringComparer.Ordinal);
        foreach(var count in new[]{1,2})
        foreach(var(suit,kind)in new[]{(Suit.Heart,CardKind.Peach),(Suit.Diamond,CardKind.FireSlash),(Suit.Club,CardKind.Dodge),(Suit.Spade,CardKind.Nullification)})
            expected.Add(count+"-"+suit.ToString().ToLowerInvariant(),(suit,kind,count));
        if(p.ViewAs.Count!=8||p.ViewAs.Any(v=>!expected.TryGetValue(v.Id,out var e)||v.OutputKind!=e.Kind||v.InputCount!=e.Count||v.InputCountExpression is not null||v.VariableInputCount||v.SameSuit!=(e.Count==2)||
            !v.InputSuits.SequenceEqual([e.Suit])||v.InputKinds.Count!=0||v.InputCategories.Count!=0||!v.SourceZones.SequenceEqual([CardZoneKind.Hand,CardZoneKind.Equipment])||!v.ExtendedUse||v.AllowChainedInput||
            !v.ForResponse||v.ForPlay!=(e.Kind is CardKind.Peach or CardKind.FireSlash)||v.Condition.Kind!=SkillProgramConditionKind.Always||
            v.DamageBonus!=(e.Count==2&&e.Kind==CardKind.FireSlash?1:0)||v.RecoveryBonus!=(e.Count==2&&e.Kind==CardKind.Peach?1:0))||
            p.Triggers.Count!=4||p.Triggers.Select(t=>t.SourceViewAsId).Distinct().Count()!=4||p.Activations.Count!=2||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0)
            throw new InvalidOperationException(path+": Happy Longhun requires eight original one/two same-suit hand/equipment conversions and their four exact actual-use benefits.");
        var contract=new HappyLonghunCommittedUseContract();foreach(var trigger in p.Triggers)contract.ValidateTrigger(trigger);
    }
}
