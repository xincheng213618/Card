namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void IssueGoudeSlashUse(long useId,ProgramSkillFrame parent,CardActionContext action)
    {
        if(parent.Goude is not{Stage:GoudeStage.NativeSlash,Option:GoudeOption.Slash,NativeOrigin:null,TargetSeat:{}target}r)return;
        var useIndex=_resolutionStack.FindIndex(frame=>frame.Id==useId);
        if(useIndex<1||useIndex!=_resolutionStack.Count-1||_resolutionStack[useIndex-1]is not ProgramSkillFrame f||f!=parent)
            throw new InvalidOperationException("Goude must issue its native Slash from the exact immediate original program parent.");
        if(f.Goude!=r||f.InstructionIndex!=1||GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.ResolveGoudeActualTurnEnd||
            LifecycleCardUse(useId)is not{CardId:0,CardKind:CardKind.Slash,PhysicalCardIds.Count:0}use||use.Action?.ActionId!=action.ActionId||
            action.Type!=CardActionType.Use||action.ActorSeat!=f.OwnerSeat||action.ProviderSeat!=f.OwnerSeat||action.PhysicalCards.Count!=0||
            !action.TargetSeats.SequenceEqual([target])||!action.ConversionChain.SequenceEqual([r.Source])||
            ProgramEventHistory<GoudeSlashIssuedEvent>().Any(e=>e.Origin.ProgramFrameId==f.Id))
            throw new InvalidOperationException("Goude cannot bind an unrelated or duplicate native zero-entity Slash.");
        var origin=new GoudeSlashOrigin(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,useId,action.ActionId,f.OwnerSeat,action.ProviderSeat,target);
        ReplaceRuntimeFrame(f.Id,f with{Goude=r with{NativeOrigin=origin}});
        UpdateLifecycleCardUse(useId,u=>u with{GoudeSlash=origin});AdvanceEventRulesAndQueueFact(new GoudeSlashIssuedEvent(origin));
    }
    private bool IsGoudeSlashUse(long useId)
    {
        if(LifecycleCardUse(useId)is not{GoudeSlash:{}o,CardId:0,PhysicalCardIds.Count:0,Action:{Type:CardActionType.Use}action}use||
            !IsSlashCard(use.CardKind)||action.EffectiveKind!=use.CardKind||action.ActionId!=o.ActionId||action.ProviderSeat!=o.ProviderSeat||action.PhysicalCards.Count!=0||
            !action.ConversionChain.Contains(o.Source)||o.UseId!=useId||o.Source.OwnerSeat!=o.OriginalActor||o.OriginalActor!=o.ProviderSeat||
            o.ActualTurn!=_turnNumber||o.TurnOwner!=_turnProgression.OwnerSeat||
            _contentRegistry.GetSkill(o.Source.SkillId).Program?.GameplayHash!=o.GameplayHash||
            ProgramEventHistory<GoudeSlashIssuedEvent>().Count(e=>e.Origin==o)!=1)return false;
        var useIndex=_resolutionStack.FindIndex(x=>x.Id==useId);
        if(useIndex<1||_resolutionStack[useIndex-1]is not ProgramSkillFrame{Goude:{Stage:GoudeStage.NativeSlash,NativeCompleted:false}r}f||
            f.Id!=o.ProgramFrameId||r.NativeOrigin!=o||r.Source!=o.Source||r.GameplayHash!=o.GameplayHash||
            r.Option!=GoudeOption.Slash||r.TargetSeat!=o.OriginalTarget||f.InstructionIndex!=1)return false;
        var actor=o.OriginalActor;
        foreach(var changed in ProgramEventHistory<ProgramCardUseActorReplacedEvent>().Where(e=>e.CardUseFrameId==useId))
        {
            if(changed.PreviousActorSeat!=actor||changed.OwnerSeat!=actor||changed.ProviderSeat!=o.ProviderSeat||changed.FrameId<=useId||
                !IsValidPlayerSeat(changed.ActorSeat)||changed.ActorSeat==actor||
                !ProgramEventHistory<ProgramBindingStartedEvent>().Any(e=>e.FrameId==changed.FrameId&&e.SkillId==changed.SkillId&&e.OwnerSeat==changed.OwnerSeat&&e.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized))return false;
            actor=changed.ActorSeat;
        }
        return use.SourceSeat==actor&&action.ActorSeat==actor;
    }
    [NativeUseValidation(27240)]private void ValidateGoudeNativeUse(CardUseFrame use)
    {if(use.GoudeSlash is not null&&!IsGoudeSlashUse(use.Id))throw new InvalidOperationException("Goude's native Slash lost its exact issuing parent, zero materials or real actor chain.");}
    [NativeUseReturn(27240)]private void ReturnGoudeNativeUse(CardUseFrame use)
    {
        if(use.GoudeSlash is not{}o)return;
        var f=GetActiveProgramFrame(o.ProgramFrameId);
        if(f.Goude is not{Stage:GoudeStage.NativeSlash,NativeCompleted:false}r||r.NativeOrigin!=o||use.Step!=ResolutionFrameStep.Completed||LifecycleCardUse(use.Id)is not null||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id)!=1)
            throw new InvalidOperationException("Goude cannot return a native Slash before its whole genuine Use finishes.");
        ReplaceRuntimeTop(f with{Goude=r with{NativeCompleted=true}});
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword,27240)]
    private bool ReturnGoudeAttack(AttackCompletionReceipt completion)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Goude:{Stage:GoudeStage.NativeSlash,NativeCompleted:true,NativeOrigin:{}o}r}f||completion.ResolutionId!=o.UseId)return false;
        if(completion.ProgramFrameId!=f.Id||LifecycleCardUse(o.UseId)is not null||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==o.UseId)!=1||
            ProgramEventHistory<GoudeSlashReturnedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("Goude lost its exact once-only native whole-Slash return.");
        AdvanceEventRulesAndQueueFact(new GoudeSlashReturnedEvent(f.Id,o.UseId));
        ReplaceRuntimeTop(f with{Goude=r with{Stage=GoudeStage.NativeChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        ContinueMengDa(f.Id);return true;
    }
    private bool HasGoudeCompletedSlashChild(ProgramCardTriggerWindowFrame window)
    {
        if(window.Continuation!=ProgramCardContinuation.CompletedSlash||LifecycleCardUse(window.ParentFrameId)is not{GoudeSlash:{}o}use||
            !IsGoudeSlashUse(use.Id)||window.Action.ActionId!=o.ActionId)return false;
        var at=_resolutionStack.FindIndex(f=>f.Id==window.Id);
        return at>1&&_resolutionStack[at-1].Id==use.Id&&_resolutionStack[at-2]is ProgramSkillFrame{Goude.NativeOrigin:{}origin}f&&f.Id==o.ProgramFrameId&&origin==o;
    }
}

