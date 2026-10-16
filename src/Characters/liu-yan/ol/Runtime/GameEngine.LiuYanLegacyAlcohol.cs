using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum LiuYanLegacyAlcoholStage { Targets,Committed,Recovery }
public sealed record LiuYanLegacyAlcoholReturn(long UseId,long ProgramParentId,long DyingId,int InstructionIndex,CardConversionSource ProducerSource,string ProducerHash,long ActionId,LiuYanLegacyAlcoholStage Stage);
public sealed partial record CardUseFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public LiuYanLegacyAlcoholReturn? LiuYanLegacyAlcohol {get;init;}}
public sealed record LiuYanLegacyAlcoholWindowIssuedEvent(long UseId,long ProgramParentId,long DyingId,int InstructionIndex,CardConversionSource ProducerSource,string ProducerHash,long ActionId):IGameEvent;
public sealed record LiuYanLegacyAlcoholStageEvent(long UseId,long ActionId,LiuYanLegacyAlcoholStage Stage):IGameEvent;

public sealed partial class GameEngine
{
    private bool IsLiuYanLegacyAlcoholUse(long id)
    {
        if(!HasLiuYanCapability||LifecycleCardUse(id)is not{CardId:0,CardKind:CardKind.Alcohol,PhysicalCardIds.Count:0,LiuYanLegacyAlcohol:{}r,Action:{Type:CardActionType.Use}a}u||
           r.UseId!=id||a.ActionId!=r.ActionId||!Enum.IsDefined(r.Stage)||!IsExactLegacyActualUseCompletion(u)||!u.TargetSeats.SequenceEqual([u.SourceSeat])||a.EffectiveSuit!=Suit.None||a.EffectiveRank!=0)return false;
        var i=_resolutionStack.FindIndex(f=>f.Id==id);
        if(i<1||_resolutionStack[i-1]is not ProgramSkillFrame p||p.Id!=r.ProgramParentId||p.InstructionIndex!=r.InstructionIndex||p.GameplayHash!=r.ProducerHash||p.WindowContext?.ParentFrameId!=r.DyingId||p.OwnerSeat!=u.SourceSeat||
           r.ProducerSource!=new CardConversionSource(p.SkillId,GetProgramBindingId(p),p.OwnerSeat,p.SkillInstanceId)||GetPausedPrivateOfferEffect(p)?.Op!=SkillProgramEffectOp.UseVirtualDyingAlcohol||
           ProgramEventHistory<LiuYanLegacyAlcoholWindowIssuedEvent>().Count(e=>e==new LiuYanLegacyAlcoholWindowIssuedEvent(id,r.ProgramParentId,r.DyingId,r.InstructionIndex,r.ProducerSource,r.ProducerHash,r.ActionId))!=1)return false;
        return true;
    }
    [NativeUseValidation(27232)]private void ValidateLiuYanLegacyAlcohol(CardUseFrame use)
    {if(use.LiuYanLegacyAlcohol is not null&&!IsLiuYanLegacyAlcoholUse(use.Id))throw new InvalidOperationException("Tushe's legacy Wine lost its exact native producer and return.");}
    private bool TryBeginLiuYanLegacyAlcoholUse(long id,long parentId,long dyingId)
    {
        if(!HasLiuYanCapability||LifecycleCardUse(id)is not{Action:null,CardId:0,CardKind:CardKind.Alcohol,PhysicalCardIds.Count:0}u||!HasRuntimeSkill(_players[u.SourceSeat],"ol:tushe")||!LiuYanNoBasicHand(_players[u.SourceSeat]))return false;
        var i=_resolutionStack.FindIndex(f=>f.Id==id);
        if(i<1||i!=_resolutionStack.Count-1||_resolutionStack[i-1]is not ProgramSkillFrame p||p.Id!=parentId||p.OwnerSeat!=u.SourceSeat||p.WindowContext is not{Window:SkillProgramTriggerWindow.SelfDyingResponse}context||context.ParentFrameId!=dyingId||ActiveDying?.FrameId!=dyingId||!IsActualTurnLegacyVirtualUse(u))throw new InvalidOperationException("Tushe's legacy Wine requires its genuine accepted self-rescue producer.");
        u=NormalizeActualTurnLegacyCompletedUse(u,parentId);var r=new LiuYanLegacyAlcoholReturn(id,parentId,dyingId,p.InstructionIndex,new(p.SkillId,GetProgramBindingId(p),p.OwnerSeat,p.SkillInstanceId),p.GameplayHash,u.Action!.ActionId,LiuYanLegacyAlcoholStage.Targets);
        UpdateLifecycleCardUse(id,use=>use with{LiuYanLegacyAlcohol=r});AdvanceEventRulesAndQueueFact(new LiuYanLegacyAlcoholWindowIssuedEvent(id,parentId,dyingId,p.InstructionIndex,r.ProducerSource,r.ProducerHash,r.ActionId));
        if(TryBeginProgramCardWindow(null,u.Action,SkillProgramTriggerWindow.CardUseTargetsFinalized,[u.SourceSeat],ProgramCardContinuation.FinalizedSimpleCard))return true;
        ContinueLiuYanLegacyAlcohol(id);return true;
    }
    private void ContinueLiuYanLegacyAlcohol(long id)
    {
        if(!IsLiuYanLegacyAlcoholUse(id)||_resolutionStack.LastOrDefault()?.Id!=id)throw new InvalidOperationException("Tushe's legacy Wine lost its exact native target/commit return.");
        var u=LifecycleCardUse(id)!;var r=u.LiuYanLegacyAlcohol!;
        if(r.Stage==LiuYanLegacyAlcoholStage.Targets)
        {
            r=r with{Stage=LiuYanLegacyAlcoholStage.Committed};UpdateLifecycleCardUse(id,use=>use with{LiuYanLegacyAlcohol=r});AdvanceEventRulesAndQueueFact(new LiuYanLegacyAlcoholStageEvent(id,r.ActionId,r.Stage));TryMarkProgramUseCommitted(id);
            if(TryBeginProgramCardWindow(null,u.Action!,SkillProgramTriggerWindow.CardUseCommitted,[u.SourceSeat],ProgramCardContinuation.CommittedSimpleCard))return;
        }
        r=r with{Stage=LiuYanLegacyAlcoholStage.Recovery};UpdateLifecycleCardUse(id,use=>use with{LiuYanLegacyAlcohol=r});AdvanceEventRulesAndQueueFact(new LiuYanLegacyAlcoholStageEvent(id,r.ActionId,r.Stage));
        ResumeProgramVirtualDyingAlcoholRecovery(id,r.ProgramParentId,r.DyingId);
    }
    private bool TryReturnLiuYanLegacyAlcohol(ProgramCardTriggerWindowFrame w)
    {
        if(LifecycleCardUse(w.ParentFrameId)?.LiuYanLegacyAlcohol is not{}r)return false;
        if(!IsLiuYanLegacyAlcoholUse(w.ParentFrameId)||_resolutionStack.LastOrDefault()?.Id!=w.ParentFrameId||w.SimpleContinuation is not null||w.AttackOwnerFrameId is not null||w.CompletedResponseReturn is not null||w.Action.ActionId!=r.ActionId||
           w.Continuation!=(r.Stage==LiuYanLegacyAlcoholStage.Targets?ProgramCardContinuation.FinalizedSimpleCard:ProgramCardContinuation.CommittedSimpleCard)||r.Stage==LiuYanLegacyAlcoholStage.Recovery)throw new InvalidOperationException("Tushe's legacy Wine cannot replay a target or commit return.");
        ContinueLiuYanLegacyAlcohol(w.ParentFrameId);return true;
    }
    private bool IsLiuYanOriginalDyingRide()
    {
        if(ActiveDying is not{}dying)return false;var di=_resolutionStack.FindIndex(f=>f.Id==dying.FrameId);
        if(di<0)return false;
        foreach(var u in _resolutionStack.OfType<CardUseFrame>())
        {
            var ui=_resolutionStack.FindIndex(f=>f.Id==u.Id);
            if(ui<=di)continue;
            var physical=u.DyingResponse is{}response&&response.ResolutionId==dying.FrameId&&response.ResponderSeat==u.SourceSeat&&u.CardKind is CardKind.Peach or CardKind.Alcohol&&u.TargetSeats.SequenceEqual([dying.VictimSeat]);
            var legacy=u.LiuYanLegacyAlcohol is{}r&&r.DyingId==dying.FrameId&&r.Stage!=LiuYanLegacyAlcoholStage.Recovery&&IsLiuYanLegacyAlcoholUse(u.Id);
            if(!physical&&!legacy)continue;
            if(ui+1>=_resolutionStack.Count||_resolutionStack[ui+1]is not ProgramCardTriggerWindowFrame w||w.ParentFrameId!=u.Id||w.Action.ActionId!=u.Action?.ActionId||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count)continue;
            if(ui+2==_resolutionStack.Count)return legacy;
            if(_resolutionStack[ui+2]is not ProgramSkillFrame binding||!MountObserverCandidateMatches(binding,ToSharedCandidate(w.Candidates[w.CandidateIndex]))||binding.WindowContext?.ParentFrameId!=w.Id)continue;
            if(binding.LiuYan is not null&&ValidLiuYan(binding)&&(ui+3==_resolutionStack.Count||LiuYanObserverRoot()?.Id==binding.Id))return true;
            if(legacy&&binding.ZhuJunCuipoDraw is not null&&(ui+3==_resolutionStack.Count||ZhuJunObserverRoot()?.Id==binding.Id))return true;
        }
        return false;
    }
}
