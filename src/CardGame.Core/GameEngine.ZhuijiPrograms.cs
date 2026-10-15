using System.Globalization;
using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ZhuijiPlayPhaseKey(int ActualTurnNumber,int OwnerSeat,int PhaseInstanceId);
public sealed record ZhuijiPlayGrant(long ProgramFrameId,CardConversionSource Source,string GameplayHash,int ActualTurnNumber,int PhaseInstanceId,bool RecoverThenDiscard,string EndingBindingId);
public sealed record ZhuijiPlayGrantIssuedEvent(ZhuijiPlayGrant Grant):IGameEvent;
public sealed record ZhuijiPlayDebtConsumedEvent(long ProgramFrameId,ZhuijiPlayGrant Grant,bool Paid):IGameEvent;
public sealed record ZhuijiDiscardPaidEvent(long ProgramFrameId,ZhuijiPlayGrant Grant,long Before,long After,int ActualCount):IGameEvent;
public enum ZhuijiDebtStage { ChoosingDiscard, DiscardChildren, HpChildren }
public sealed record ZhuijiDebtPending(ZhuijiPlayGrant Grant,int InstructionIndex,ZhuijiDebtStage Stage,long Before,long After,int ActualCount);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZhuijiPlayGrant? ZhuijiInitialPaid {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZhuijiDebtPending? ZhuijiDebt {get;init;}
}
public sealed partial record ProgramSkillWindowContext
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZhuijiPlayGrant? ZhuijiGrant {get;init;}
}
public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ArmZhuijiActualPlay(ProgramSkillFrame input)
    {
        var f=GetActiveProgramFrame(input.Id);
        if(f.WindowContext is not {Window:SkillProgramTriggerWindow.PlayPhaseStarting,SourceSeat:{} actor} || actor!=f.OwnerSeat || _currentSeat!=f.OwnerSeat ||
            f.ZhuijiInitialPaid is not null)throw new InvalidOperationException("Zhuiji needs its unpaid actual own Play-start.");
        var option=f.ChoiceBindings.Single(b=>b.Name=="zhuiji-choice").OptionId;
        if(option is not ("recover" or "draw"))throw new InvalidOperationException("Zhuiji lost its actual native choice.");
        var ending=_contentRegistry!.GetSkill(f.SkillId).Program!.Triggers.Single(t=>t.Effects is [{Op:SkillProgramEffectOp.SettleZhuijiActualPlayDebt}]);
        var grant=new ZhuijiPlayGrant(f.Id,new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId),f.GameplayHash,_turnNumber,_cardUseDebitPhaseInstanceId,option=="recover",ending.Id);
        _players[f.OwnerSeat].ZhuijiPlayDebts.Add(grant);
        ReplaceRuntimeTop(f with{ZhuijiInitialPaid=grant});AdvanceEventRulesAndQueueFact(new ZhuijiPlayGrantIssuedEvent(grant));return SkillProgramStepOutcome.Continue;
    }
    private IEnumerable<ZhuijiPlayGrant> UnsettledZhuijiGrants()
    {
        return _players.SelectMany(p=>p.ZhuijiPlayDebts);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 590)]
    private void ObserveZhuijiDebtTermination(IGameEvent payload)
    {
        var ended=payload as TurnEndedEvent;
        var died=payload as PlayerDiedEvent;
        if(ended is null && died is null)return;
        foreach(var owner in _players.Where(p=>p.ZhuijiPlayDebts.Count>0))
        foreach(var grant in owner.ZhuijiPlayDebts.Where(g=>died?.VictimSeat==owner.Seat || ended is not null && ended.ActorSeat==owner.Seat && ended.TurnNumber==g.ActualTurnNumber).ToArray())
        {
            owner.ZhuijiPlayDebts.Remove(grant);
            AdvanceEventRulesAndQueueFact(new ZhuijiPlayDebtConsumedEvent(grant.ProgramFrameId,grant,false));
        }
    }
    private IEnumerable<ProgramTriggerCandidate> IssuedZhuijiEndingCandidates(CharacterState owner)=>UnsettledZhuijiGrants().Where(g=>g.Source.OwnerSeat==owner.Seat &&
        g.ActualTurnNumber==_turnNumber && g.PhaseInstanceId==_cardUseDebitPhaseInstanceId).Select(g=>new ProgramTriggerCandidate(owner.Seat,g.Source.SkillId,g.EndingBindingId,g.Source.SkillInstanceId,g.GameplayHash,0));
    private ZhuijiPlayGrant? MatchingZhuijiGrant(ProgramTriggerCandidate c)=>UnsettledZhuijiGrants().SingleOrDefault(g=>g.Source.OwnerSeat==c.OwnerSeat &&
        g.Source.SkillId==c.SkillId && g.Source.SkillInstanceId==c.SkillInstanceId && g.GameplayHash==c.GameplayHash && g.EndingBindingId==c.BindingId &&
        g.ActualTurnNumber==_turnNumber && g.PhaseInstanceId==_cardUseDebitPhaseInstanceId);
    private ZhuijiPlayPhaseKey? CaptureZhuijiEndingPhase(CharacterState owner)=>owner.ZhuijiPlayDebts.Any(g=>g.ActualTurnNumber==_turnNumber && g.PhaseInstanceId==_cardUseDebitPhaseInstanceId)
        ?new(_turnNumber,owner.Seat,_cardUseDebitPhaseInstanceId):null;
    private ZhuijiPlayGrant? MatchingZhuijiGrant(ProgramTriggerCandidate c,ZhuijiPlayPhaseKey phase)=>UnsettledZhuijiGrants().SingleOrDefault(g=>g.Source.OwnerSeat==c.OwnerSeat &&
        g.Source.SkillId==c.SkillId && g.Source.SkillInstanceId==c.SkillInstanceId && g.GameplayHash==c.GameplayHash && g.EndingBindingId==c.BindingId &&
        g.ActualTurnNumber==phase.ActualTurnNumber && g.Source.OwnerSeat==phase.OwnerSeat && g.PhaseInstanceId==phase.PhaseInstanceId);
    private ProgramSkillWindowContext CreateZhuijiEndingContext(ProgramLifecycleTriggerWindowFrame f,ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>
        f.ZhuijiEndingPhase is {} phase ?context with{ZhuijiGrant=MatchingZhuijiGrant(c,phase)}:context;
    private bool HasIssuedZhuijiDebtCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>w.Window==SkillProgramTriggerWindow.PlayEnding &&
        w.SourceSeat==c.OwnerSeat && c.OwnerSeat==_currentSeat && w.ZhuijiGrant is {} g &&
        g.Source.SkillId==c.SkillId && g.Source.SkillInstanceId==c.SkillInstanceId && g.EndingBindingId==c.BindingId && g.GameplayHash==c.GameplayHash &&
        _players[c.OwnerSeat].ZhuijiPlayDebts.Contains(g);
    private SkillProgramStepOutcome SettleZhuijiActualPlayDebt(ProgramSkillFrame input)
    {
        var f=GetActiveProgramFrame(input.Id);var grant=f.WindowContext?.ZhuijiGrant??MatchingZhuijiGrant(new(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,0));
        if(grant is null)return SkillProgramStepOutcome.Continue;
        if(!_players[f.OwnerSeat].IsAlive){AdvanceEventRulesAndQueueFact(new ZhuijiPlayDebtConsumedEvent(f.Id,grant,false));return SkillProgramStepOutcome.Continue;}
        ReplaceRuntimeTop(f=f with{ZhuijiDebt=new(grant,f.InstructionIndex,grant.RecoverThenDiscard?ZhuijiDebtStage.ChoosingDiscard:ZhuijiDebtStage.HpChildren,0,0,0),PendingMovementContinuation=grant.RecoverThenDiscard?null:new(f.OwnerSeat,0,null)});
        if(grant.RecoverThenDiscard){PublishZhuijiDiscardPrompt(f);return SkillProgramStepOutcome.AwaitChoice;}
        AdvanceEventRulesAndQueueFact(new ZhuijiPlayDebtConsumedEvent(f.Id,grant,true));
        _players[f.OwnerSeat].ZhuijiPlayDebts.Remove(grant);
        var outcome=new ProgramSkillHost(this).LoseHp(f.Id,f.SkillId,f.OwnerSeat,1);
        if(outcome==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private void PublishZhuijiDiscardPrompt(ProgramSkillFrame f)
    {
        var available=GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(_players[f.OwnerSeat])).Where(c=>!c.IsGeneralWeapon).OrderBy(c=>c.Id).ToArray();
        var count=Math.Min(2,available.Length);var choices=new List<PromptChoice>();
        var sets=count<2?new[]{available.Select(c=>c.Id).ToArray()}:available.SelectMany((a,i)=>available.Skip(i+1).Select(b=>new[]{a.Id,b.Id})).ToArray();
        foreach(var ids in sets)choices.Add(new(new ChoiceId($"zhuiji.discard.{f.Id}.{string.Join('-',ids)}"),$"弃置{ids.Length}张牌",ids,[],new Dictionary<string,string>{["program-action"]="zhuiji-discard",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture)}));
        var s=_contentRegistry!.GetSkill(f.SkillId);_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"追姬：本次出牌阶段结束，弃置两张牌。",available.Select(c=>c.Id).ToArray(),[],f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=choices.AsReadOnly(),SkillPrompt=new(f.SkillId,s.Name,s.Name,s.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    [ProgramAnswer("zhuiji-discard")]
    private void ResolveZhuijiDiscardChoice(PromptChoice choice)
    {
        var f=_resolutionStack.LastOrDefault()as ProgramSkillFrame??throw new InvalidOperationException("Zhuiji lost its actual ending frame.");
        if(f.ZhuijiDebt is not {Stage:ZhuijiDebtStage.ChoosingDiscard} r || choice.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString(CultureInfo.InvariantCulture))throw new InvalidOperationException("Zhuiji discard belongs to a different already-paid stage.");
        var available=GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(_players[f.OwnerSeat])).Where(c=>!c.IsGeneralWeapon).ToArray();
        if(choice.Cards.Count!=Math.Min(2,available.Length) || choice.Cards.Distinct().Count()!=choice.Cards.Count || choice.Cards.Any(id=>!available.Any(c=>c.Id==id)))throw new InvalidOperationException("Zhuiji discard lost an actual owned card.");
        ClearPendingDecision();var before=_movementSequence;
        ReplaceRuntimeTop(f=f with{ZhuijiDebt=r with{Stage=ZhuijiDebtStage.DiscardChildren,Before=before,After=before,ActualCount=choice.Cards.Count},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        AdvanceEventRulesAndQueueFact(new ZhuijiPlayDebtConsumedEvent(f.Id,r.Grant,true));
        _players[f.OwnerSeat].ZhuijiPlayDebts.Remove(r.Grant);
        MoveProgramCardsFromMultipleSources(choice.Cards,CardLocation.DiscardPile,new("program.zhuiji.ending-discard"));
        ReplaceRuntimeTop(f=GetActiveProgramFrame(f.Id)with{ZhuijiDebt=f.ZhuijiDebt!with{After=_movementSequence}});
        AdvanceEventRulesAndQueueFact(new ZhuijiDiscardPaidEvent(f.Id,r.Grant,before,_movementSequence,choice.Cards.Count));AdvanceRuntimeProgram(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,22201)]
    private bool ResumeZhuijiDebt(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{ZhuijiDebt:{} r}f || f.Id!=id)return false;
        if(r.Stage==ZhuijiDebtStage.ChoosingDiscard){if(_pendingDecision is null)PublishZhuijiDiscardPrompt(f);return true;}
        if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id))return true;
        ReplaceRuntimeTop(f with{ZhuijiDebt=null,PendingMovementContinuation=null});AdvanceRuntimeProgram(id);return true;
    }
    private bool ValidZhuijiIssued(ProgramSkillFrame f)
    {
        var grant=f.ZhuijiInitialPaid??f.ZhuijiDebt?.Grant??f.WindowContext?.ZhuijiGrant;if(grant is null)return false;
        return grant.Source.OwnerSeat==f.OwnerSeat && grant.Source.SkillId==f.SkillId && grant.Source.SkillInstanceId==f.SkillInstanceId && grant.GameplayHash==f.GameplayHash &&
            CompleteProgramEventHistory().OfType<ZhuijiPlayGrantIssuedEvent>().Count(e=>e.Grant==grant)==1 &&
            (f.ZhuijiInitialPaid is not null ? grant.ProgramFrameId==f.Id && grant.ActualTurnNumber==_turnNumber && grant.PhaseInstanceId==_cardUseDebitPhaseInstanceId :
                f.WindowContext?.Window==SkillProgramTriggerWindow.PlayEnding && f.TriggerId==grant.EndingBindingId &&
                (f.ZhuijiDebt is {} debt ? debt.InstructionIndex==f.InstructionIndex : f.InstructionIndex is 0 or 1 && _players[f.OwnerSeat].ZhuijiPlayDebts.Contains(grant)));
    }
    private bool IsZhuijiMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.ZhuijiDebt is {} r &&
        e?.Op==SkillProgramEffectOp.SettleZhuijiActualPlayDebt && p.SubjectSeat==f.OwnerSeat && p.CoverageResultBind is null && ValidZhuijiIssued(f) &&
        (r.Stage==ZhuijiDebtStage.HpChildren ? CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Count(x=>x.FrameId==f.Id && x.SkillId==f.SkillId && x.TargetSeat==f.OwnerSeat && x.Amount==1)==1 :
         r.Stage==ZhuijiDebtStage.DiscardChildren && CompleteProgramEventHistory().OfType<ZhuijiDiscardPaidEvent>().Any(x=>x.ProgramFrameId==f.Id && x.Grant==r.Grant && x.Before==r.Before && x.After==r.After && x.ActualCount==r.ActualCount) &&
         _cardMovements.Count(m=>m.Sequence>r.Before && m.Sequence<=r.After && m.From.OwnerSeat==f.OwnerSeat && m.To==CardLocation.DiscardPile && m.Reason.Value=="program.zhuiji.ending-discard")==r.ActualCount);
}
