namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasAnniversaryXiahouJie => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryXiahouJieZhuangdan);
    private static CardConversionSource AnniversaryXiahouJieSource(ProgramSkillFrame f) => new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private static ProgramTriggerCandidate AnniversaryXiahouJieCandidate(ProgramSkillFrame f) => new(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,0,f.WindowContext?.OccurrenceIndex??0);
    private bool IsAnniversaryXiahouJieGrantQualified(CharacterState owner,SkillGrant grant) => grant.SkillId!=AnniversaryXiahouJieComposition.Liedan || !HasAnniversaryXiahouJie ||
        !ProgramEventHistory<AnniversaryXiahouJieSuppressedEvent>().Any(e=>e.Source.OwnerSeat==owner.Seat && !ProgramEventHistory<AnniversaryXiahouJieSuppressionExpiredEvent>().Any(x=>x.FrameId==e.FrameId));
    private (long Inner,int Issued,int Expired) _anniversaryXiahouJieQualificationInputs;
    private long _anniversaryXiahouJieQualificationStamp;
    private long CaptureAnniversaryXiahouJieQualificationStamp(long underlying)
    {
        if(!HasAnniversaryXiahouJie) return underlying;
        var issued=ProgramEventHistory<AnniversaryXiahouJieSuppressedEvent>().Count;
        if(issued==0) return underlying;
        var input=(underlying,issued,ProgramEventHistory<AnniversaryXiahouJieSuppressionExpiredEvent>().Count);
        if(_anniversaryXiahouJieQualificationStamp==0 || input!=_anniversaryXiahouJieQualificationInputs)
        { _anniversaryXiahouJieQualificationInputs=input; _anniversaryXiahouJieQualificationStamp++; }
        return long.MinValue/16+_anniversaryXiahouJieQualificationStamp;
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent,45500)]
    private void ExpireAnniversaryXiahouJie(TurnEndedEvent ended)
    {
        if(!HasAnniversaryXiahouJie || ProgramEventHistory<AnniversaryXiahouJieSuppressedEvent>().Count==0) return;
        foreach(var issued in ProgramEventHistory<AnniversaryXiahouJieSuppressedEvent>().Where(e=>e.Source.OwnerSeat==ended.ActorSeat && e.ActualTurn<=ended.TurnNumber &&
            !ProgramEventHistory<AnniversaryXiahouJieSuppressionExpiredEvent>().Any(x=>x.FrameId==e.FrameId)).ToArray())
            AdvanceEventRulesAndQueueFact(new AnniversaryXiahouJieSuppressionExpiredEvent(issued.FrameId,ended.ActorSeat,ended.TurnNumber));
    }
    private bool IsAnniversaryXiahouJieUniqueHand(int owner) => _players[owner].IsAlive && _players.Where(p=>p.IsAlive && p.Seat!=owner).All(p=>GetHand(p).Count<GetHand(_players[owner]).Count);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,45500,SkillProgramEffectOp.ResolveAnniversaryXiahouJieLiedan,SkillProgramEffectOp.ResolveAnniversaryXiahouJieZhuangdan)]
    private bool CanOfferAnniversaryXiahouJie(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
    {
        if(!IsValidPlayerSeat(c.OwnerSeat) || !_players[c.OwnerSeat].IsAlive || w.OwnerSeat!=c.OwnerSeat || w.SourceSeat!=_currentSeat) return false;
        return t.Effects.Single().Op==SkillProgramEffectOp.ResolveAnniversaryXiahouJieLiedan
            ? w.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && (c.OwnerSeat!=_currentSeat || _players[c.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.XiahouJieLie)>=5)
            : w.Window==SkillProgramTriggerWindow.AfterTurnEnded && c.OwnerSeat!=_currentSeat && IsAnniversaryXiahouJieUniqueHand(c.OwnerSeat);
    }
    [ProgramCapability] private sealed class AnniversaryXiahouJieHost(GameEngine e) : IAnniversaryXiahouJieHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginAnniversaryXiahouJie(f,op); }
    private SkillProgramStepOutcome BeginAnniversaryXiahouJie(ProgramSkillFrame supplied,SkillProgramEffectOp operation)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.AnniversaryXiahouJie is not null || f.InstructionIndex!=1 || f.WindowContext is not {} context ||
            !CanOfferAnniversaryXiahouJie(AnniversaryXiahouJieCandidate(f),GetProgramTrigger(f),context))
            throw new InvalidOperationException("Xiahou Jie lost his original unpaid preparation or actual foreign turn end.");
        if(operation==SkillProgramEffectOp.ResolveAnniversaryXiahouJieZhuangdan)
        {
            if(_resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p=>p.Id==context.ParentFrameId) is not {} ended ||
                !IsActualAfterTurnEndedParent(ended) || !MountObserverCandidateMatches(f,AfterTurnEndedCandidate(ended)))
                throw new InvalidOperationException("Zhuangdan lost its actual end-of-turn candidate.");
            AdvanceEventRulesAndQueueFact(new AnniversaryXiahouJieSuppressedEvent(f.Id,AnniversaryXiahouJieSource(f),f.GameplayHash,_turnNumber,_currentSeat));
            return SkillProgramStepOutcome.Continue;
        }
        var own=f.OwnerSeat==_currentSeat;
        var r=new AnniversaryXiahouJieReceipt(AnniversaryXiahouJieSource(f),f.GameplayHash,context.ParentFrameId,_turnNumber,_currentSeat,
            own?AnniversaryXiahouJieStage.DirectDeath:AnniversaryXiahouJieStage.Comparing,DeathMarks:own?_players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.XiahouJieLie):0);
        ReplaceRuntimeTop(f=f with{AnniversaryXiahouJie=r});
        AdvanceEventRulesAndQueueFact(new AnniversaryXiahouJieStartedEvent(f.Id,r.Source,r.Hash,r.ParentId,r.ActualTurn,r.TurnOwner,own,r.DeathMarks));
        RequireAnniversaryXiahouJie(f);
        if(own)
        {
            AdvanceEventRulesAndQueueFact(new ProgramSkillCauseDeathDeclaredEvent(f.Id,f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.OwnerSeat));
            BeginPlayerDeath(f.Id,_players[f.OwnerSeat],null,null,null,causingProgramSkillFrameId:f.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        ContinueAnniversaryXiahouJie(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainAnniversaryXiahouJie(long id) => TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueAnniversaryXiahouJie(long id)
    {
        while(_resolutionStack.LastOrDefault() is ProgramSkillFrame {AnniversaryXiahouJie:{} r} f && f.Id==id)
        {
            RequireAnniversaryXiahouJie(f);
            if(r.Stage==AnniversaryXiahouJieStage.Drawing) return;
            if(r.Stage==AnniversaryXiahouJieStage.HpChildren && !r.HpPaid)
            {
                var outcome=ApplyProgramHpLoss(f.Id,f.SkillId,f.OwnerSeat,1,(before,after,actual)=>
                {
                    var active=GetActiveProgramFrame(id); var receipt=active.AnniversaryXiahouJie!;
                    ReplaceRuntimeTop(active with{AnniversaryXiahouJie=receipt with{HpPaid=true,HpBefore=before,HpAfter=after,ActualLoss=actual}});
                    AdvanceEventRulesAndQueueFact(new AnniversaryXiahouJieHpPaidEvent(id,before,after,actual));
                });
                if(outcome==SkillProgramStepOutcome.AwaitChild) return;
                f=GetActiveProgramFrame(id); r=f.AnniversaryXiahouJie!;
            }
            if(DrainAnniversaryXiahouJie(id)) return;
            f=GetActiveProgramFrame(id); r=f.AnniversaryXiahouJie!;
            if(r.Stage==AnniversaryXiahouJieStage.HpChildren)
            {
                if(_players[f.OwnerSeat].IsAlive)
                {
                    var owner=_players[f.OwnerSeat]; var marker=PlayerMarkerKind.XiahouJieLie; var count=owner.Markers.GetValueOrDefault(marker)+1;
                    owner.Markers[marker]=count; var key=(marker,f.OwnerSeat); owner.MarkerSourceCounts[key]=owner.MarkerSourceCounts.GetValueOrDefault(key)+1;
                    AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id,f.OwnerSeat,marker,1,count,f.OwnerSeat,"skill-program.anniversary:liedan.lie"));
                }
                FinishAnniversaryXiahouJie(f); return;
            }
            if(r.Stage is AnniversaryXiahouJieStage.MaximumChildren or AnniversaryXiahouJieStage.DirectDeath || !_players[f.OwnerSeat].IsAlive || _winner!=Winner.None)
            { FinishAnniversaryXiahouJie(f); return; }
            if(r.Stage==AnniversaryXiahouJieStage.DrawChildren)
            { ReplaceRuntimeTop(f=f with{AnniversaryXiahouJie=r with{Stage=AnniversaryXiahouJieStage.Comparing,Cursor=r.Cursor+1}}); r=f.AnniversaryXiahouJie!; }
            if(r.Cursor==3)
            {
                if(r.Comparisons.All(c=>c.Greater==true))
                {
                    var before=_players[f.OwnerSeat].MaxHp;
                    ReplaceRuntimeTop(f=f with{AnniversaryXiahouJie=r with{Stage=AnniversaryXiahouJieStage.MaximumChildren,MaximumBefore=before,MaximumAfter=before}});
                    ChangeProgramMaximumHp(f,1); var after=_players[f.OwnerSeat].MaxHp;
                    ReplaceRuntimeTop(f with{AnniversaryXiahouJie=f.AnniversaryXiahouJie! with{MaximumAfter=after}});
                    AdvanceEventRulesAndQueueFact(new AnniversaryXiahouJieMaximumEvent(id,before,after)); continue;
                }
                if(r.Comparisons.All(c=>c.Greater==false))
                { ReplaceRuntimeTop(f with{AnniversaryXiahouJie=r with{Stage=AnniversaryXiahouJieStage.HpChildren}}); continue; }
                FinishAnniversaryXiahouJie(f); return;
            }
            var ownerNow=_players[f.OwnerSeat]; var otherNow=_players[r.TurnOwner];
            int? ownValue=r.Cursor switch{0=>GetHand(ownerNow).Count,1=>UnambiguousHp(ownerNow),_=>GetEquipment(ownerNow).Count};
            int? otherValue=r.Cursor switch{0=>GetHand(otherNow).Count,1=>UnambiguousHp(otherNow),_=>GetEquipment(otherNow).Count};
            var comparison=new AnniversaryXiahouJieComparison(r.Cursor,ownValue,otherValue);
            ReplaceRuntimeTop(f=f with{AnniversaryXiahouJie=r with{Comparisons=r.Comparisons.Append(comparison).ToArray(),
                Stage=comparison.Greater==true?AnniversaryXiahouJieStage.Drawing:AnniversaryXiahouJieStage.Comparing,Cursor=comparison.Greater==true?r.Cursor:r.Cursor+1}});
            AdvanceEventRulesAndQueueFact(new AnniversaryXiahouJieComparedEvent(id,comparison.Index,ownValue,otherValue));
            if(comparison.Greater==true) { BeginOperationProgramDraw(f,f.OwnerSeat,1,AnniversaryXiahouJieDrawReason); return; }
        }
    }
    private static readonly CardMoveReason AnniversaryXiahouJieDrawReason=new("skill-program.anniversary:liedan.draw");
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryXiahouJieLiedan)]
    private void ReturnAnniversaryXiahouJieDraw(ProgramSkillFrame supplied,NativeDrawOutcome draw)
    {
        var f=GetActiveProgramFrame(supplied.Id); RequireAnniversaryXiahouJie(f); var r=f.AnniversaryXiahouJie!;
        if(r.Stage!=AnniversaryXiahouJieStage.Drawing || f.NativeDraw is not null || draw.RecipientSeat!=f.OwnerSeat || draw.RequestedCount!=1 || r.Draws.Any(d=>d.Index==r.Cursor) || draw.SequenceAfter!=_movementSequence)
            throw new InvalidOperationException("Liedan lost its once-issued native comparison draw.");
        ReplaceRuntimeTop(f=f with{AnniversaryXiahouJie=r with{Stage=AnniversaryXiahouJieStage.DrawChildren,Draws=r.Draws.Append(new(r.Cursor,draw)).ToArray()}});
        AdvanceEventRulesAndQueueFact(new AnniversaryXiahouJieDrawReturnedEvent(f.Id,r.Cursor,draw.ActualDrawIds.Count,draw.AttemptFrameId,draw.SequenceBefore,draw.SequenceAfter));
        ContinueAnniversaryXiahouJie(f.Id);
    }
    private void FinishAnniversaryXiahouJie(ProgramSkillFrame f)
    { AdvanceEventRulesAndQueueFact(new AnniversaryXiahouJieCompletedEvent(f.Id)); ReplaceRuntimeTop(f with{AnniversaryXiahouJie=null}); AdvanceRuntimeProgram(f.Id); }
    [ProgramContinuation(ProgramContinuationKind.Resume,45500)] private bool ResumeAnniversaryXiahouJie(long id)
    { if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame {AnniversaryXiahouJie:not null} f || f.Id!=id) return false; ContinueAnniversaryXiahouJie(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement,45500)] private bool ReturnAnniversaryXiahouJieMovement(ProgramSkillFrame f)=>f.AnniversaryXiahouJie is not null && ResumeAnniversaryXiahouJie(f.Id);
}
