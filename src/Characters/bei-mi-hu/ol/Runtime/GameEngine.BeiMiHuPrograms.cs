using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string BeiMiHuDrawReason="program.bei-mi-hu.guju-draw";
    private static bool IsBeiMiHuOp(SkillProgramEffectOp op)=>(int)op is >=23200 and <=23205;
    private static SkillProgramEffectOp? BeiMiHuOperation(SkillProgramTrigger t)=>t.Effects.Count==1 && IsBeiMiHuOp(t.Effects[0].Op)?t.Effects[0].Op:null;
    private CardConversionSource BeiMiHuSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private int BeiMiHuRound=>CompleteProgramEventHistory().OfType<RoundStartedEvent>().LastOrDefault()?.RoundNumber??_roundNumber;
    private int BeiMiHuReceiptTurn(ProgramSkillFrame f)=>f.WindowContext?.BeforeTurnPreludeProof?.UpcomingTurnNumber??_turnNumber;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 390)]
    private bool CanOfferBeiMiHuLifecycle(int owner,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
    {
        var op=BeiMiHuOperation(trigger);if(op is null)return true;
        if(op==SkillProgramEffectOp.MarkUnmarkedOtherBeforeActualTurn)return context.Window==SkillProgramTriggerWindow.TurnBeforeStart &&
            context.BeforeTurnPreludeProof is{}proof && proof.UpcomingOwnerSeat==owner && ValidBeiMiHuBeforeTurnPrelude(proof) && BeiMiHuUnmarkedOthers(owner).Length>0;
        if(op==SkillProgramEffectOp.MarkLowestHpUnmarkedOtherAtRoundStart)return context.Window==SkillProgramTriggerWindow.RoundStarting && context.BeforeTurnPreludeProof is{}roundProof && ValidBeiMiHuBeforeTurnPrelude(roundProof) && BeiMiHuRound>0 &&
            BeiMiHuLowestUnmarkedOthers(owner).Length>0 && !CompleteProgramEventHistory().OfType<BeiMiHuKuiPaidEvent>().Any(e=>e.Source.OwnerSeat==owner && e.Kind=="round" && e.Round==BeiMiHuRound);
        if(op==SkillProgramEffectOp.AwakenFromActualGujuCards)return context.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && owner==_currentSeat &&
            _players[owner].HasHpValue && BeiMiHuActualCards(owner)>=7;
        return true;
    }
    private bool ExactBeiMiHuLifecycleParent(ProgramSkillFrame f)
    {
        var c=f.WindowContext;return f.TriggerId is not null && c is not null && f.InstructionIndex==1 && _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(p=>p.Id==c.ParentFrameId) is{}p &&
            p.Window==c.Window && p.CandidateIndex>=0 && p.CandidateIndex<p.Candidates.Count && MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex]) &&
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash==f.GameplayHash &&
            (c.Window is not(SkillProgramTriggerWindow.TurnBeforeStart or SkillProgramTriggerWindow.RoundStarting) || c.BeforeTurnPreludeProof is{}proof && proof==p.BeforeTurnPreludeProof &&
             (c.Window!=SkillProgramTriggerWindow.TurnBeforeStart || proof.UpcomingOwnerSeat==f.OwnerSeat) && ValidBeiMiHuBeforeTurnPrelude(proof));
    }
    private BeiMiHuReceipt NewBeiMiHuReceipt(ProgramSkillFrame f,string stage)=>new(){Source=BeiMiHuSource(f),GameplayHash=f.GameplayHash,ParentFrameId=f.WindowContext!.ParentFrameId,
        Turn=BeiMiHuReceiptTurn(f),Round=f.WindowContext.BeforeTurnPreludeProof?.ActualRoundNumber??BeiMiHuRound,InstructionIndex=f.InstructionIndex,Stage=stage};
    private SkillProgramStepOutcome BeginBeiMiHuOperation(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);if(f.BeiMiHu is not null || f.InstructionIndex!=1 || !IsBeiMiHuOp(op))throw new InvalidOperationException("Bei Mi Hu lost its original unpaid instruction.");
        return op switch
        {
            SkillProgramEffectOp.MarkUnmarkedOtherBeforeActualTurn or SkillProgramEffectOp.MarkLowestHpUnmarkedOtherAtRoundStart=>BeginBeiMiHuMark(f,op),
            SkillProgramEffectOp.DrawAfterActualKuiDamage=>BeginBeiMiHuGuju(f),
            SkillProgramEffectOp.AwakenFromActualGujuCards=>BeginBeiMiHuAwakening(f),
            _=>BeginBeiMiHuCardTargets(f,op)
        };
    }
    private void PayBeiMiHuKui(ProgramSkillFrame f,int target,int delta,string kind)
    {
        var r=f.BeiMiHu!;var old=_players[target].Markers.GetValueOrDefault(PlayerMarkerKind.Kui);if(delta==1?old!=0:old!=1)throw new InvalidOperationException("The accepted public binary Kui payment changed.");
        SetStrategicMarker(target,PlayerMarkerKind.Kui,f.OwnerSeat,old+delta,f);
        AdvanceEventRulesAndQueueFact(new BeiMiHuKuiPaidEvent(f.Id,r.Source,r.GameplayHash,r.ParentFrameId,r.Turn,r.Round,kind,target,delta,old+delta));
    }
    private IReadOnlyList<PromptChoice> BeiMiHuChoices(ProgramSkillFrame f)
    {
        var r=f.BeiMiHu!;Dictionary<string,string>Args(string branch)=>new(){["program-action"]="bei-mi-hu",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["branch"]=branch};
        if(r.Stage is "round-choice" or "before-turn-choice")return Array.AsReadOnly(r.Candidates.Where(s=>_players[s].IsAlive && _players[s].Markers.GetValueOrDefault(PlayerMarkerKind.Kui)==0).Select(s=>new PromptChoice(new($"bei-mi-hu.{f.Id}.mark-{s}"),$"令 {_players[s].Name} 获得傀",[],[s],Args("mark"))).ToArray());
        if(r.Stage=="append-choice")
        {
            var use=RequireBeiMiHuCardParent(f,out _);var choices=r.Choices.Where(c=>c.Targets.Count>0 && !r.SelectedTargets.Where((_,i)=>use.CardKind!=CardKind.BorrowedSword || i%2==0).Contains(c.Targets[0]) &&
                BeiMiHuTargetGroupLegal(use,c.Targets)).ToList();choices.Add(new(new($"bei-mi-hu.{f.Id}.finish"),"完成本次追加目标",[],[],Args("finish")));return Array.AsReadOnly(choices.ToArray());
        }
        return [];
    }
    private void PublishBeiMiHuChoice(ProgramSkillFrame f)
    {
        if(!_players[f.OwnerSeat].IsAlive || _winner!=Winner.None){FinishBeiMiHu(f);return;}var choices=BeiMiHuChoices(f);if(choices.Count==0){FinishBeiMiHu(f);return;}
        var skill=_contentRegistry.GetSkill(f.SkillId);_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"卑弥呼：选择本次真实结算的角色。",[],choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=false,TargetSeat=f.OwnerSeat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("bei-mi-hu")]
    private void ResolveBeiMiHuChoice(PromptChoice selected)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{BeiMiHu:{}r}f || _pendingDecision?.PlayerSeat!=f.OwnerSeat || !AssistedChoicesEqual([selected],BeiMiHuChoices(f).Where(c=>c.Id==selected.Id).ToArray()))throw new InvalidOperationException("Bei Mi Hu lost its actual chooser and frozen public candidates.");
        AssertBeiMiHu(f);ClearPendingDecision();
        if(selected.Parameters["branch"]=="mark")
        {
            var target=selected.Targets.Single();PayBeiMiHuKui(f,target,1,r.Stage=="round-choice"?"round":"before-turn");FinishBeiMiHu(f);return;
        }
        if(selected.Parameters["branch"]=="append")
        {
            ReplaceRuntimeTop(f=f with{BeiMiHu=r with{SelectedTargets=r.SelectedTargets.Concat(selected.Targets).ToArray()}});PublishBeiMiHuChoice(f);return;
        }
        CompleteBeiMiHuTargetAddition(f);
    }
    private SkillProgramEffect? BeiMiHuPausedEffect(ProgramSkillFrame f)=>f.InstructionIndex<1?null:ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool DrainBeiMiHuChildren(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume,23200)]
    private bool ResumeBeiMiHu(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{BeiMiHu:{}r}f || f.Id!=id)return false;AssertBeiMiHu(f);
        if(GainGiftHasFinalGameEnd())return true;if(f.PendingMovementContinuation is not null){if(!TryDrainFireTargetMovement(f))ReturnBeiMiHuMovement(f);return true;}
        if(DrainBeiMiHuChildren(f))return true;f=GetActiveProgramFrame(id);r=f.BeiMiHu!;
        if(!_players[f.OwnerSeat].IsAlive || _winner!=Winner.None){ClearPendingDecision();FinishBeiMiHu(f);return true;}
        if(r.Stage is "round-choice" or "before-turn-choice" or "append-choice"){if(_pendingDecision is null)PublishBeiMiHuChoice(f);return true;}
        if(r.Stage=="maximum-children")
        {
            var owner=_players[f.OwnerSeat];var requested=owner.HasHpValue?Math.Min(1,Math.Max(0,owner.MaxHp-owner.Hp)):0;ReplaceRuntimeTop(f=f with{BeiMiHu=r with{Stage="recovery-children",RecoveryRequested=requested}});
            AdvanceEventRulesAndQueueFact(new BeiMiHuRecoveryIssuedEvent(f.Id,f.OwnerSeat,requested));if(requested>0)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,requested,null,null);AdvanceRuntimeProgram(f.Id);return true;
        }
        if(r.Stage=="recovery-children")
        {
            var targets=BeiMiHuUnmarkedOthers(f.OwnerSeat);ReplaceRuntimeTop(f=f with{BeiMiHu=r with{Stage="grants-children",SelectedTargets=targets}});foreach(var target in targets)PayBeiMiHuKui(f,target,1,"awakening");
            GrantProgramSkills(f,["ol:canshi"]);foreach(var grant in _players[f.OwnerSeat].SkillGrants.Grants.Where(g=>g.SkillId=="ol:guju").ToArray())_players[f.OwnerSeat].SkillGrants.RemoveGrant(grant.GrantId);
            AdvanceEventRulesAndQueueFact(new BeiMiHuAwakeningCompletedEvent(f.Id,f.OwnerSeat,targets,"ol:guju","ol:canshi"));AdvanceRuntimeProgram(f.Id);return true;
        }
        FinishBeiMiHu(f);return true;
    }
    private void FinishBeiMiHu(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{BeiMiHu=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramCapability]
    private sealed class BeiMiHuProgramCapability(GameEngine engine) : IBeiMiHuProgramHost
    {
        public SkillProgramStepOutcome BeginBeiMiHuOperation(ProgramSkillFrame f,SkillProgramEffectOp op)=>engine.BeginBeiMiHuOperation(f,op);
    }
}
