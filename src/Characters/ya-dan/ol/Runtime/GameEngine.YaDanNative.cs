namespace CardGame.Core;
public sealed partial class GameEngine
{
    private ProgramSkillFrame? QingyaDiscardQueryOwner(int actor)=>_resolutionStack.LastOrDefault()is ProgramSkillFrame{YaDan:{Op:SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse,Stage:QingyaStage.NativeChoice or QingyaStage.NativeUse}r}f&&
        f.OwnerSeat==actor&&f.InstructionIndex==1&&f.GameplayHash==r.GameplayHash&&f.WindowContext?.ActualPhaseEnd?.EntitlementId==r.GrantId?f:null;
    private bool QingyaUnspentEntity(ProgramSkillFrame f,int id)=>f.YaDan is{Op:SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse}r&&
        r.Entities.Any(e=>e.CardId==id&&QingyaEntityStillAvailable(e));
    private IReadOnlyList<Card> AppendQingyaDiscardCards(CharacterState actor,IReadOnlyList<Card> held)
    {
        var f=QingyaDiscardQueryOwner(actor.Seat);if(f is null)return held;var r=f.YaDan!;
        return Array.AsReadOnly(held.Concat(_cardZones.CardsAt(CardLocation.DiscardPile).Where(c=>
            r.Stage==QingyaStage.NativeChoice?QingyaUnspentEntity(f,c.Id):r.SelectedAction?.CardId==c.Id)).DistinctBy(c=>c.Id).ToArray());
    }
    private Card? FindQingyaDiscardCard(CharacterState actor,int id)=>IsQingyaSelectedDiscard(actor.Seat,id)?_cardZones.CardsAt(CardLocation.DiscardPile).Single(c=>c.Id==id):null;
    private bool IsQingyaSelectedDiscard(int actor,int id)
    {
        if(id<=0||!_cardZones.Contains(CardLocation.DiscardPile,id))return false;
        if(QingyaDiscardQueryOwner(actor)is{}f)return f.YaDan!.Stage==QingyaStage.NativeChoice?QingyaUnspentEntity(f,id):f.YaDan.SelectedAction?.CardId==id;
        return _resolutionStack.LastOrDefault()is CardUseFrame{QingyaOrigin:{}origin}use&&origin.OriginalActor==actor&&origin.CardId==id&&IsIssuedQingyaUse(use.Id);
    }
    private bool IsQingyaDiscardQuery(CharacterState actor,int? id)=>id is>0&&QingyaDiscardQueryOwner(actor.Seat)is not null&&IsQingyaSelectedDiscard(actor.Seat,id.Value);
    private IReadOnlyList<LegalAction> QingyaNativeActions(ProgramSkillFrame f)=>Array.AsReadOnly(f.YaDan!.Entities.Where(QingyaEntityStillAvailable).SelectMany(entity=>
        BuildLegalActions(_players[f.OwnerSeat],includeProgramActions:false,selectedPhysicalCardId:entity.CardId))
        .Where(a=>a.CardId is>0&&f.YaDan.Entities.Any(e=>e.CardId==a.CardId)&&a.ConversionSource is null&&(a.AdditionalConversionSources?.Count??0)==0&&
            a.Kind is not(LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.RevealGeneral or LegalActionKind.EndPlay)).ToArray());
    private static QingyaAction FreezeQingyaAction(LegalAction a) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ya_dan_ol_YaDanNative.FreezeQingyaAction(a);
    private void AnswerQingyaNativeUse(ProgramSkillFrame f,PromptChoice choice)
    {
        if(choice.Parameters.GetValueOrDefault("branch")=="decline"){FinishYaDan(f);return;}
        var actions=QingyaNativeActions(f);
        if(!int.TryParse(choice.Parameters.GetValueOrDefault("action-index"),out var index)||index<0||index>=actions.Count||
            choice.Cards is not[var id]||actions[index].CardId!=id||!QingyaUnspentEntity(f,id))throw new InvalidOperationException("Qingya cannot substitute an original discard or bypass its current ordinary native legality.");
        var selected=FreezeQingyaAction(actions[index]);var r=f.YaDan!;
        ReplaceRuntimeTop(f with{YaDan=r with{Stage=QingyaStage.NativeUse,SelectedAction=selected,ReservedUseId=_resolutionSequence+1,Before=_movementSequence}});
        AdvanceEventRulesAndQueueFact(new QingyaNativeSelectedEvent(f.Id,selected.CardId,selected.Kind,selected.PlayedKind,selected.TargetCardId,selected.Targets.Count));
        for(var i=0;i<selected.Targets.Count;i++)AdvanceEventRulesAndQueueFact(new QingyaNativeTargetEvent(f.Id,i,selected.Targets[i]));
        ExecuteAction(_players[f.OwnerSeat],actions[index]);
    }
    private void BindQingyaNativeUse(CardUseFrame use)
    {
        var at=_resolutionStack.FindIndex(frame=>frame.Id==use.Id);
        if(at<1||_resolutionStack[at-1]is not ProgramSkillFrame{YaDan:{Op:SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse,Stage:QingyaStage.NativeUse}r}f||r.ReservedUseId!=use.Id)return;
        if(r.NativeOrigin is not null||r.SelectedAction is not{}selected||selected.CardId!=use.CardId||use.SourceSeat!=f.OwnerSeat||use.Action is not{Type:CardActionType.Use}action||
            action.ActorSeat!=f.OwnerSeat||action.ProviderSeat!=f.OwnerSeat||use.PhysicalCardIds is not[var id]||id!=selected.CardId||
            action.PhysicalCards is not[var material]||material.CardId!=id||material.From!=CardLocation.DiscardPile||
            !action.TargetSeats.SequenceEqual(selected.Targets)||!QingyaUnspentEntity(f,id)||r.GrantId is not{}grantId||r.DuePhase is null)
            throw new InvalidOperationException("Qingya must issue exactly its chosen original Discard entity through the real native Use.");
        var origin=new QingyaNativeOrigin(f.Id,grantId,r.Source,r.GameplayHash,r.ActualTurn,use.Id,action.ActionId,f.OwnerSeat,action.ProviderSeat,id,use.CardKind);
        ReplaceRuntimeFrame(f.Id,f with{YaDan=r with{NativeOrigin=origin}});
        UpdateLifecycleCardUse(use.Id,u=>u with{QingyaOrigin=origin});AdvanceEventRulesAndQueueFact(new QingyaNativeIssuedEvent(origin));
    }
    private bool IsIssuedQingyaUse(long id)
    {
        if(LifecycleCardUse(id)is not{QingyaOrigin:{}o,Action:{Type:CardActionType.Use}action,PhysicalCardIds:[var material]}use||
            o.UseId!=id||o.CardId!=use.CardId||o.CardId!=material||o.ActionId!=action.ActionId||o.ProviderSeat!=action.ProviderSeat||
            action.PhysicalCards is not[var cost]||cost.CardId!=material||cost.From!=CardLocation.DiscardPile||
            use.CurrentSlashFirePolicy is null&&use.CardKind!=o.Kind||action.EffectiveKind!=use.CardKind||
            ProgramEventHistory<QingyaNativeIssuedEvent>().Count(e=>e.Origin==o)!=1)return false;
        var at=_resolutionStack.FindIndex(frame=>frame.Id==id);
        if(at<1||_resolutionStack[at-1]is not ProgramSkillFrame{YaDan:{Stage:QingyaStage.NativeUse,NativeCompleted:false}r}f||
            f.Id!=o.ProgramFrameId||f.OwnerSeat!=o.OriginalActor||r.NativeOrigin!=o||r.GrantId!=o.GrantId||r.Source!=o.Source||
            r.GameplayHash!=o.GameplayHash||r.ActualTurn!=o.ActualTurn||r.SelectedAction?.CardId!=material||f.InstructionIndex!=1)return false;
        var actor=o.OriginalActor;
        foreach(var change in ProgramEventHistory<ProgramCardUseActorReplacedEvent>().Where(e=>e.CardUseFrameId==id))
        {
            if(change.PreviousActorSeat!=actor||change.OwnerSeat!=actor||change.ProviderSeat!=o.ProviderSeat||!IsValidPlayerSeat(change.ActorSeat)||change.FrameId<=id||
                !ProgramEventHistory<ProgramBindingStartedEvent>().Any(e=>e.FrameId==change.FrameId&&e.OwnerSeat==change.OwnerSeat&&e.SkillId==change.SkillId&&e.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized))return false;
            actor=change.ActorSeat;
        }
        return actor==use.SourceSeat&&actor==action.ActorSeat;
    }
    [NativeUseValidation(27260)]private void ValidateQingyaUse(CardUseFrame use)
    {if(use.QingyaOrigin is not null&&!IsIssuedQingyaUse(use.Id))throw new InvalidOperationException("Qingya's native use lost its issuing frame, original entity or actual actor chain.");}
    [NativeUseReturn(27260)]private void ReturnQingyaUse(CardUseFrame use)
    {
        if(use.QingyaOrigin is not{}origin)return;var f=GetActiveProgramFrame(origin.ProgramFrameId);var r=f.YaDan;
        if(r is not{Stage:QingyaStage.NativeUse,NativeCompleted:false}||r.NativeOrigin!=origin||use.Step!=ResolutionFrameStep.Completed||LifecycleCardUse(use.Id)is not null||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id)!=1||
            _cardMovements.Count(m=>m.Sequence>r.Before&&m.CardId==origin.CardId&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Processing&&(m.Reason==CardMoveReasons.Use||m.Reason==CardMoveReasons.IronChainUse||m.Reason==CardMoveReasons.EquipmentUse))!=1)
            throw new InvalidOperationException("Qingya lost its exact once-paid whole native return.");
        ReplaceRuntimeTop(f with{YaDan=r with{NativeCompleted=true,After=_movementSequence}});
        if(use.CardAttack is{Active:true})return;SettleQingyaNative(origin);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword,27260)]
    private bool ReturnQingyaAttack(AttackCompletionReceipt completion)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{YaDan:{Stage:QingyaStage.NativeUse,NativeCompleted:true,NativeOrigin:{}o}}||o.UseId!=completion.ResolutionId)return false;
        if(completion.ProgramFrameId is{}parent&&parent!=o.ProgramFrameId)throw new InvalidOperationException("Qingya's native attack returned to another program.");
        SettleQingyaNative(o);return true;
    }
    private void SettleQingyaNative(QingyaNativeOrigin origin)
    {
        var f=GetActiveProgramFrame(origin.ProgramFrameId);var r=f.YaDan!;
        if(!r.NativeCompleted||r.NativeOrigin!=origin||LifecycleCardUse(origin.UseId)is not null||ProgramEventHistory<QingyaNativeReturnedEvent>().Any(e=>e.Origin==origin))
            throw new InvalidOperationException("Qingya cannot finish before or twice after its exact whole native use.");
        ReplaceRuntimeTop(f with{YaDan=r with{Stage=QingyaStage.NativeChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        AdvanceEventRulesAndQueueFact(new QingyaNativeReturnedEvent(origin));
    }
}

