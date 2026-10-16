namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasIssuedFulueRequest(ProgramSkillFrame f)
    {
        if(f.Fulue is not{Stage:FulueStage.CounterRequest,Counter:false,Option:{}option}r||f.SkillId!=r.Source.SkillId||f.SkillInstanceId!=r.Source.SkillInstanceId||f.GameplayHash!=r.GameplayHash||f.OwnerSeat!=r.UserSeat||f.InstructionIndex is not(1 or 2)||f.SelectedTargetSeats is not[var actor]||actor!=r.TargetSeat||
            f.WindowContext is not{Window:SkillProgramTriggerWindow.CardUseCompleted,CardUse:{}context}||context.ParentCardUseFrameId!=r.UseId||context.CardActionId!=r.ActionId||LifecycleCardUse(r.UseId)is not{}original||!FulueUseProof(original)||!FulueZeroDamage(original)||
            FulueOriginalEntry(original,f.OwnerSeat,f.SkillId,f.SkillInstanceId)is not{}entry||entry.Source!=r.Source||entry.UserSeat!=r.UserSeat||entry.TargetSeat!=actor||entry.Option!=option||
            ProgramEventHistory<FulueCounterRequestedEvent>().Where(e=>e.ProgramFrameId==f.Id).ToArray()is not[var started]||started.Invoice!=new FulueCounterInvoice(f.Id,r.UseId,r.ActionId,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,actor,r.UserSeat,OtherFulueOption(option)))return false;
        var op=GetPausedPrivateOfferEffect(f)?.Op;
        return f.InstructionIndex==1?op==SkillProgramEffectOp.RequestFulueZeroDamageCounterSlash:op==SkillProgramEffectOp.RequestSlashAgainstChosenTarget&&GetPausedPrivateOfferEffect(f)?.ResultBind==FulueCounterBind;
    }
    private bool TryBeginFulueAssistedSlash(ProgramSkillFrame frame,int actorSeat,string bind,out SkillProgramStepOutcome outcome)
    {
        outcome=SkillProgramStepOutcome.Continue;if(frame.Fulue?.Stage!=FulueStage.CounterRequest)return false;
        if(!HasIssuedFulueRequest(frame)||frame.InstructionIndex!=2||bind!=FulueCounterBind||actorSeat!=frame.Fulue.TargetSeat||frame.AssistedSlashRequest is not null)throw new InvalidOperationException("Fulue's real counter-Slash lost its original completed card and exact requested actor.");
        if(!_players[actorSeat].IsAlive||!_players[frame.Fulue.UserSeat].IsAlive||!AssistedPhysicalSlashTargets(actorSeat).Contains(frame.Fulue.UserSeat)){CommitProgramChoiceResult(frame.Id,bind,"declined",actorSeat,"反杀参与者已死亡或不在合法攻击范围内。");return true;}
        ReplaceRuntimeTop(frame=frame with{AssistedSlashRequest=new(actorSeat,frame.Fulue.UserSeat,ActorChoosesTarget:true)});PublishAssistedPhysicalSlashPrompt(frame);outcome=SkillProgramStepOutcome.AwaitChoice;return true;
    }
    private void IssueFulueCounterUse(long useId,CardActionContext action)
    {
        var index=_resolutionStack.FindIndex(f=>f.Id==useId);if(index<1||_resolutionStack[index-1]is not ProgramSkillFrame{Fulue:{Stage:FulueStage.CounterRequest}}parent)return;
        if(!HasIssuedFulueRequest(parent)||parent.InstructionIndex!=2||parent.Fulue is not{}r||action.Type!=CardActionType.Use||!IsSlashCard(action.EffectiveKind)||action.ActorSeat!=r.TargetSeat||!action.EffectiveDesignatedTargetSeats.SequenceEqual([r.UserSeat])||
            parent.ChoiceBindings.SingleOrDefault(b=>b.Name==FulueCounterBind)is not{OptionId:"used-slash"}answer||answer.ChooserSeat!=r.TargetSeat||LifecycleCardUse(useId)is not{FulueCounter:null}use||use.Action?.ActionId!=action.ActionId)
            throw new InvalidOperationException("Fulue cannot attach its other option to an unrelated counter card or provider.");
        var invoice=ProgramEventHistory<FulueCounterRequestedEvent>().Single(e=>e.ProgramFrameId==parent.Id).Invoice;
        UpdateLifecycleCardUse(useId,u=>u with{FulueCounter=invoice});AdvanceEventRulesAndQueueFact(new FulueCounterUseIssuedEvent(useId,action.ActionId,invoice));
    }
    private bool ValidFulueCounterInvoice(CardUseFrame use,FulueCounterInvoice r)
    {
        if(use.FulueCounter!=r||use.Action is not{Type:CardActionType.Use}action||!IsSlashCard(use.CardKind)||!ShownEntityUseActorMatches(use,r.ActorSeat,action.ProviderSeat)||r.ActualTurn!=_turnNumber||r.TurnOwner!=_currentSeat||
            ProgramEventHistory<FulueCounterUseIssuedEvent>().Where(e=>e.UseId==use.Id).ToArray()is not[var issued]||issued.ActionId!=action.ActionId||issued.Invoice!=(r with{Applied=false})||
            _resolutionStack.SingleOrDefault(f=>f.Id==r.ProgramFrameId)is not ProgramSkillFrame parent||!HasIssuedFulueRequest(parent)||parent.Fulue is not{}draft||draft.UseId!=r.OriginalUseId||draft.ActionId!=r.OriginalActionId||draft.Source!=r.Source||draft.GameplayHash!=r.GameplayHash||draft.TargetSeat!=r.ActorSeat||draft.UserSeat!=r.TargetSeat)return false;
        return parent.ChoiceBindings.SingleOrDefault(b=>b.Name==FulueCounterBind)is{OptionId:"used-slash"}answer&&answer.ChooserSeat==r.ActorSeat;
    }
}
