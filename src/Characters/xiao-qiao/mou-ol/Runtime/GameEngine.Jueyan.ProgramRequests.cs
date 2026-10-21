using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouXqRequestAction="mou-xiao-qiao-requested-slash";
    private IReadOnlyList<PromptChoice> MouXqProgramSlashChoices(ProgramSkillFrame f,int actorSeat,IReadOnlyList<int> targets,string? resultBind=null)
    {
        var effect=GetPausedPrivateOfferEffect(f);
        if(effect is null||!IsValidPlayerSeat(actorSeat)||!_players[actorSeat].IsAlive||f.MouXiaoQiaoJueyanResponseGrant is not null||
            !_players[f.OwnerSeat].IsAlive||(!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)&&!HasIssuedFulueRequest(f)&&!IsChanshuangSlashRequest(f)&&!IsSayingSlashRequest(f))||
            !RoundOnceCardlessProgramActor(f,effect.Op,actorSeat)||resultBind is not null&&effect.ResultBind!=resultBind)return [];
        var allowed=RoundOnceCardlessProgramTargets(f,actorSeat,effect.Op);
        if(allowed.Length==0||targets.Any(t=>!allowed.Contains(t)))return [];
        var actor=_players[actorSeat];var candidate=CollectProgramTriggerCandidates(actor,SkillProgramTriggerWindow.PublicHandRevealed).FirstOrDefault(c=>c.SkillId==MouXiaoQiaoOlRuntime.Jueyan);
        if(candidate is null)return [];var source=new CardConversionSource(candidate.SkillId,"retained-hand-use",actorSeat,candidate.SkillInstanceId);var choices=new List<PromptChoice>();
        foreach(var card in GetHand(actor).Where(c=>IsSlashCard(AdvancedEffectiveHandKind(actor,c))&&!IsTurnHandCardRestricted(actor,c)&&MouXiaoQiaoOl.JueyanAvailable(actorSeat,c.Id)))
        foreach(var target in targets.Distinct())
        {
            var kind=AdvancedEffectiveHandKind(actor,card);
            if(!IsValidPlayerSeat(target)||!_players[target].IsAlive||target==actorSeat||HasBeneficiarySuitShield(actorSeat,target,Suit.None)||IsCardTargetProhibited(_players[target],kind,Suit.None)||
                !CanUseSlashTarget(actor,_players[target],new(0,kind,Suit.None,0),source,kind,ignoreDistance:f.FixedTargetSlash?.IgnoreDistance==true,noEffectiveRank:true,physicalCardIds:[]))continue;
            var p=new Dictionary<string,string>{["program-action"]=MouXqRequestAction,["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["instruction-index"]=f.InstructionIndex.ToString(CultureInfo.InvariantCulture),["output-kind"]=kind.ToString()};AddConversionParameters(p,source);
            choices.Add(new(new($"jueyan.request.{f.Id}.{f.InstructionIndex}.{actorSeat}.{card.Id}.{target}"),$"绝颜：展示并保留【{card.DisplayName}】，视为对{_players[target].Name}使用之。",[card.Id],[target],p));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private bool HasMouXqProgramSlash(CharacterState actor,IEnumerable<int> targets)=>_resolutionStack.LastOrDefault()is ProgramSkillFrame f&&MouXqProgramSlashChoices(f,actor.Seat,targets.ToArray()).Count>0;
    private PendingDecision MouXqProgramNativeDecision(PendingDecision p)=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==MouXqRequestAction)?
        p with{Choices=Array.AsReadOnly(p.Choices.Where(c=>c.Parameters.GetValueOrDefault("program-action")!=MouXqRequestAction).ToArray())}:p;
    [ProgramAnswer(MouXqRequestAction)]
    private void BeginMouXqProgramSlash(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame f||_pendingDecision is not{Kind:DecisionKind.ProgramTrigger}p||choice.Cards is not[var id]||choice.Targets is not[var target]||
            !Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("output-kind"),out var kind)||!p.Choices.Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Jueyan lost its actual native invited Slash choice.");
        var effect=GetPausedPrivateOfferEffect(f)??throw new InvalidOperationException("Jueyan lost its actual native Slash instruction.");
        if(!MouXqProgramSlashChoices(f,p.PlayerSeat,[target],effect.ResultBind).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Jueyan cannot change a native invitation's real actor or targets.");
        StartMouXqRequestedReveal(p,RequireConversionSource(choice),id,new(f.Id,DecisionKind.ProgramTrigger,kind,p.PlayerSeat,target,f.Id,effect.Op,f.InstructionIndex,
            new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId),f.GameplayHash,effect.ResultBind));
    }
    private bool IsMouXqProgramRequest(ProgramSkillFrame f,MouXiaoQiaoJueyanResponseRequest r)=>r.DecisionKind==DecisionKind.ProgramTrigger&&r.ParentFrameId==f.Id&&r.RequestInstruction==f.InstructionIndex&&
        r.RequestSource==new CardConversionSource(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId)&&r.RequestHash==f.GameplayHash&&r.RequestOperation is{}op&&
        _contentRegistry.GetSkill(f.SkillId).Program!.GameplayHash==f.GameplayHash&&RoundOnceCardlessProgramActor(f,op,r.ActorSeat)&&
        GetPausedPrivateOfferEffect(f)?.ResultBind==r.ResultBind&&RoundOnceCardlessProgramTargets(f,r.ActorSeat,op).Contains(r.TargetSeat);
    private void PrepareMouXqProgramSlash(MouXiaoQiaoJueyanResponseRequest r)
    {
        var f=GetActiveProgramFrame(r.ParentFrameId);if(!IsMouXqProgramRequest(f,r))throw new InvalidOperationException("Jueyan's accepted invitation lost its original producer.");
        if(IsSayingSlashRequest(f))PrepareSayingSuppliedSlash(f,r.ActorSeat,r.ActorSeat,r.TargetSeat,r.OutputKind,[],f.MouXiaoQiaoJueyanResponseGrant!.Source);
        if(IsChanshuangSlashRequest(f))PrepareChanshuangSuppliedSlash(f,r.ActorSeat,r.ActorSeat,r.TargetSeat,r.OutputKind,[],f.MouXiaoQiaoJueyanResponseGrant!.Source);
        if(r.RequestOperation==SkillProgramEffectOp.RequestSlashByTarget)CommitProgramChoiceResult(f.Id,r.ResultBind!,"used-slash",r.ActorSeat,"绝颜：展示并视为使用原手牌。");
        else if(r.RequestOperation==SkillProgramEffectOp.RequestSlashAgainstChosenTarget)
        {ReplaceRuntimeTop(f=f with{AssistedSlashRequest=null});CommitProgramChoiceResult(f.Id,r.ResultBind!,"used-slash",r.ActorSeat,"绝颜：展示并视为使用原手牌。");}
        else if(r.RequestOperation==SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner)ReplaceRuntimeTop(f with{FixedTargetSlash=f.FixedTargetSlash!with{Issued=true}});
        else if(r.RequestOperation==SkillProgramEffectOp.RequestLegalSlashByNearest)ReplaceRuntimeTop(f with{NearestLegalSlashRequest=null});
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,34001)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,34001)]
    private bool PreserveMouXqRequestedSlash(ProgramSkillFrame f)=>f.MouXiaoQiaoJueyanResponseGrant is{Request.DecisionKind:DecisionKind.ProgramTrigger}g&&
        g.Request.ParentFrameId==f.Id&&g.Request.RequestSource==new CardConversionSource(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId)&&g.Request.RequestHash==f.GameplayHash&&
        ProgramEventHistory<MouXiaoQiaoJueyanResponseGrantedEvent>().Count(e=>e.Grant==(g with{ActionId=0,Returned=false}))==1;
    [NativeUseReturn(34001)]
    private void ReturnMouXqProgramSlash(CardUseFrame use)
    {
        if(use.MouXiaoQiaoJueyanRescueOrigin is not{Request.DecisionKind:DecisionKind.ProgramTrigger}g)return;
        var f=GetActiveProgramFrame(g.Request.ParentFrameId);
        if(!PreserveMouXqRequestedSlash(f)||f.MouXiaoQiaoJueyanResponseGrant!.Returned||use.Action?.ActionId!=g.ActionId)throw new InvalidOperationException("Jueyan's native invited Slash cannot return twice.");
        ReplaceRuntimeTop(f with{MouXiaoQiaoJueyanResponseGrant=g with{Returned=true}});
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,34001)]
    private bool ResumeMouXqProgramSlash(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouXiaoQiaoJueyanResponseGrant:{Returned:true}g}f||f.Id!=id)return false;
        if(!PreserveMouXqRequestedSlash(f))throw new InvalidOperationException("Jueyan's native invitation lost its completed whole-use receipt.");
        ReplaceRuntimeTop(f=f with{MouXiaoQiaoJueyanResponseGrant=null});
        if(ResumeSayingSuppliedSlash(f)||ResumeChanshuangSuppliedSlash(f))return true;
        if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)&&!HasIssuedFulueRequest(f))FinishProgramSkill(f,true);
        else if(f.FixedTargetSlash is{Issued:true})TryResumeFixedSlashAndDeclaredDeck(id);else AdvanceRuntimeProgram(id);return true;
    }
}
