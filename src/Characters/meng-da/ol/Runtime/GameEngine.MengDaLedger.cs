namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasGoudeCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveGoudeActualTurnEnd);
    private static CardConversionSource MengDaSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_meng_da_ol_MengDaLedger.MengDaSource(f);
    private IReadOnlyList<int> GoudeFactionSeats(int owner)
    {
        var faction=GetEffectiveFactionId(_players[owner]);
        return faction is null?[]:Array.AsReadOnly(_players.Where(p=>GetEffectiveFactionId(p)==faction).Select(p=>p.Seat).ToArray());
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27240)]
    private void ObserveGoudeZeroSlash(IGameEvent fact)
    {
        if(!HasGoudeCapability||_turnNumber<1||fact is not CardUseDeclaredEvent{CardId:0}declared||!IsSlashCard(declared.CardKind)||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u=>u.Id==declared.ResolutionId)is not{CardId:0,PhysicalCardIds.Count:0,Action:{Type:CardActionType.Use}action}use||
            action.ActorSeat!=declared.SourceSeat||use.SourceSeat!=declared.SourceSeat||action.EffectiveKind!=declared.CardKind||
            ProgramEventHistory<GoudeZeroSlashDeclaredEvent>().Any(e=>e.UseId==use.Id))return;
        AdvanceEventRulesAndQueueFact(new GoudeZeroSlashDeclaredEvent(_turnNumber,use.Id,action.ActionId,action.ActorSeat,action.ProviderSeat));
    }
    private int GoudeZeroSlashActor(GoudeZeroSlashDeclaredEvent observed,IReadOnlyList<IGameEvent> history)
    {
        if(history.OfType<CardUseDeclaredEvent>().Count(e=>e.ResolutionId==observed.UseId&&e.CardId==0&&IsSlashCard(e.CardKind)&&e.SourceSeat==observed.ActorSeat)!=1)
            throw new InvalidOperationException("Goude lost its true native zero-entity Slash declaration.");
        var actor=observed.ActorSeat;
        foreach(var e in history.OfType<ProgramCardUseActorReplacedEvent>().Where(e=>e.CardUseFrameId==observed.UseId))
        {
            if(e.PreviousActorSeat!=actor||e.OwnerSeat!=actor||e.ProviderSeat!=observed.ProviderSeat||e.FrameId<=observed.UseId||!IsValidPlayerSeat(e.ActorSeat)||e.ActorSeat==actor||
                !history.OfType<ProgramBindingStartedEvent>().Any(b=>b.FrameId==e.FrameId&&b.OwnerSeat==e.OwnerSeat&&b.SkillId==e.SkillId&&b.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized))
                throw new InvalidOperationException("Goude's zero-entity use lost its exact native actor-replacement chain.");
            actor=e.ActorSeat;
        }
        return actor;
    }
    private int GoudeExecutedMask(int turn,IReadOnlyList<int> sameFaction,int cutoff)
    {
        var history=CompleteProgramEventHistory().Take(cutoff).ToArray();var mask=0;
        if(history.OfType<NativeDrawInvocationRecordedEvent>().Any(e=>e.TurnNumber==turn&&e.ActualCount==1&&sameFaction.Contains(e.OwnerSeat)))mask|=(int)GoudeOption.Draw;
        if(history.OfType<NativeLogicalDiscardInvocationRecordedEvent>().Any(e=>e.TurnNumber==turn&&e.ActualCount==1&&e.HandCount==1&&(sameFaction.Contains(e.InitiatorSeat)||sameFaction.Contains(e.ExecutorSeat))))mask|=(int)GoudeOption.Discard;
        if(history.OfType<GoudeZeroSlashDeclaredEvent>().Any(e=>e.ActualTurn==turn&&sameFaction.Contains(GoudeZeroSlashActor(e,history))))mask|=(int)GoudeOption.Slash;
        if(history.OfType<NativeEffectiveFactionChangedEvent>().Any(e=>e.TurnNumber==turn&&e.PreviousFactionId!=e.CurrentFactionId&&sameFaction.Contains(e.TargetSeat)))mask|=(int)GoudeOption.Faction;
        return mask;
    }
    private bool ExactGoudeCandidate(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.AfterTurnEnded||context.OwnerSeat!=candidate.OwnerSeat||
            _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p=>p.Id==context.ParentFrameId)is not{}parent||
            !IsActualAfterTurnEndedParent(parent)||AfterTurnEndedCandidate(parent)!=candidate||context.SourceSeat!=parent.OwnerSeat||context.TargetSeat!=parent.OwnerSeat||
            context.Facts!=parent.AfterTurnEnded!.Items[parent.AfterTurnEnded.ItemIndex].Facts||context.OccurrenceIndex!=candidate.OccurrenceIndex)return false;
        var trigger=GetProgramTrigger(candidate);
        return trigger.Effects is[{Op:SkillProgramEffectOp.ResolveGoudeActualTurnEnd}]&&trigger.TurnOwnerScope==(candidate.OwnerSeat==parent.OwnerSeat?SkillProgramTurnOwnerScope.Own:SkillProgramTurnOwnerScope.OtherLiving);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27240)]
    private bool QualifyGoude(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
    {
        if(trigger.Effects is not[{Op:SkillProgramEffectOp.ResolveGoudeActualTurnEnd}])return true;
        if(!ExactGoudeCandidate(candidate,context)||!_players[candidate.OwnerSeat].IsAlive)return false;
        var seats=GoudeFactionSeats(candidate.OwnerSeat);var mask=GoudeExecutedMask(_turnNumber,seats,CompleteProgramEventHistory().Count());
        return mask is>0 and<15&&GoudeAvailableOptions(candidate.OwnerSeat,mask).Count>0;
    }
    private IReadOnlyList<int> GoudeDiscardTargets(int owner)=>Array.AsReadOnly(_players.Where(p=>p.IsAlive&&GoudeDiscardMaterials(owner,p.Seat).Count>0).Select(p=>p.Seat).ToArray());
    private IReadOnlyList<GoudeHandMaterial> GoudeDiscardMaterials(int owner,int target)
    {
        if(!IsValidPlayerSeat(owner)||!IsValidPlayerSeat(target))return [];
        var location=CardLocation.Hand(target);
        return Array.AsReadOnly(GetHand(_players[target]).Select((card,slot)=>(Card:card,Slot:slot))
            .Where(item=>!IsForeignEquipmentDiscardPrevented(owner,item.Card,location,OwnedCardMoveIntent.Discard)&&
                !IsSelfHandCategoryDiscardForbidden(owner,item.Card,location,OwnedCardMoveIntent.Discard))
            .Select(item=>new GoudeHandMaterial(item.Card.Id,item.Card.Kind,target,item.Slot,
                CurrentHandEntry(target,item.Card.Id)??throw new InvalidOperationException("Goude's actual Hand entity lost its native residency entry."))).ToArray());
    }
    private IReadOnlyList<int> GoudeSlashTargets(int owner)=>Array.AsReadOnly(_players.Where(p=>p.IsAlive&&CanUseVirtualSlashTarget(_players[owner],p)).Select(p=>p.Seat).ToArray());
    private IReadOnlyList<string> GoudeFactionChoices(int owner)=>Array.AsReadOnly(AvailableEffectiveFactionIds().Where(f=>f!=GetEffectiveFactionId(_players[owner])).ToArray());
    private IReadOnlyList<GoudeOption> GoudeAvailableOptions(int owner,int mask)=>Array.AsReadOnly(Enum.GetValues<GoudeOption>().Where(o=>(mask&(int)o)==0&&
        (o switch{GoudeOption.Discard=>GoudeDiscardTargets(owner).Count>0,GoudeOption.Slash=>GoudeSlashTargets(owner).Count>0,GoudeOption.Faction=>GoudeFactionChoices(owner).Count>0,_=>true})).ToArray());
}

