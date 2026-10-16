using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum JiqiaoPaidStage { CostChildren,RevealChildren,ClaimChildren,CleanupChildren }
public sealed record JiqiaoPaidInvoice(CardConversionSource Source,string GameplayHash,long WindowId,int ActualTurnNumber,int PhaseInstanceId,int InstructionIndex,string CostBind,string RevealBind,int PaidCount,long CostBefore,long CostAfter);
public sealed record JiqiaoCostPaidEvent(long ProgramFrameId,JiqiaoPaidInvoice Invoice):IGameEvent;
public sealed record JiqiaoStageIssuedEvent(long ProgramFrameId,JiqiaoPaidStage Stage,long Before,long After,int ActualCount):IGameEvent;
public sealed record JiqiaoCompletedEvent(long ProgramFrameId,int PaidCount,int RevealedCount,int ClaimedCount,bool OwnerAlive):IGameEvent;
public sealed record JiqiaoPaidPending(JiqiaoPaidInvoice Invoice,JiqiaoPaidStage Stage,long Before,long After,int ActualCount,int RevealedCount=0,int ClaimedCount=0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public JiqiaoPaidPending? JiqiaoPaid{get;init;}
}
public sealed partial class GameEngine
{
    private const string JiqiaoCostReason="program.jiqiao.cost",JiqiaoClaimReason="program.jiqiao.obtain",JiqiaoRemainderReason="program.jiqiao.remainder";
    private SkillProgramStepOutcome ExecutePaidJiqiao(ProgramSkillFrame input,SkillProgramEffect effect)
    {
        var f=GetActiveProgramFrame(input.Id);
        if(f.JiqiaoPaid is not null || f.WindowContext is not {Window:SkillProgramTriggerWindow.PlayPhaseStarting,SourceSeat:{} actor} w || actor!=f.OwnerSeat || actor!=_currentSeat)throw new InvalidOperationException("Jiqiao needs its unpaid genuine own Play-start.");
        var cost=GetProgramCardSet(f,effect.SourceBind!);var ids=cost.CardIds;
        if(ids.Distinct().Count()!=ids.Count || ids.Any(id=>_cardZones.GetLocation(id) is not {OwnerSeat:{} s,Zone:CardZoneKind.Hand or CardZoneKind.Equipment} || s!=f.OwnerSeat || !EquipmentCatalog.IsEquipment(JiqiaoCard(id).Kind) || JiqiaoCard(id).IsGeneralWeapon || IsSelfHandCategoryDiscardForbidden(f.OwnerSeat,JiqiaoCard(id),_cardZones.GetLocation(id),OwnedCardMoveIntent.Discard)))throw new InvalidOperationException("Jiqiao selected cards ceased to be actual owner equipment.");
        if(ids.Count==0){SetProgramCardSet(f.Id,effect.ResultBind!,[],SkillProgramCardSetVisibility.Public);return SkillProgramStepOutcome.Continue;}
        var before=_movementSequence;
        var invoice=new JiqiaoPaidInvoice(new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId),f.GameplayHash,w.ParentFrameId,_turnNumber,_cardUseDebitPhaseInstanceId,f.InstructionIndex,effect.SourceBind!,effect.ResultBind!,ids.Count,before,before);
        ReplaceRuntimeTop(f=f with{JiqiaoPaid=new(invoice,JiqiaoPaidStage.CostChildren,before,before,ids.Count),PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        MoveProgramLogicalDiscard(f,f.OwnerSeat,ids,new(JiqiaoCostReason));
        invoice=invoice with{CostAfter=_movementSequence};
        ReplaceRuntimeTop(f=GetActiveProgramFrame(f.Id) with{JiqiaoPaid=f.JiqiaoPaid! with{Invoice=invoice,After=_movementSequence}});
        AdvanceEventRulesAndQueueFact(new JiqiaoCostPaidEvent(f.Id,invoice));IssueJiqiaoStage(f);AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private Card JiqiaoCard(int id)=>_cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c=>c.Id==id);
    private void IssueJiqiaoStage(ProgramSkillFrame f)
    {var r=f.JiqiaoPaid!;AdvanceEventRulesAndQueueFact(new JiqiaoStageIssuedEvent(f.Id,r.Stage,r.Before,r.After,r.ActualCount));}
    [ProgramContinuation(ProgramContinuationKind.Resume,22500)]
    private bool ResumePaidJiqiao(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{JiqiaoPaid:{} r} f || f.Id!=id)return false;
        RequireIssuedJiqiao(f);
        if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id))return true;
        var before=_movementSequence;
        if(r.Stage==JiqiaoPaidStage.CostChildren)
        {
            if(!_players[f.OwnerSeat].IsAlive){CompletePaidJiqiao(f);return true;}
            ReplaceRuntimeTop(f=f with{JiqiaoPaid=r with{Stage=JiqiaoPaidStage.RevealChildren,Before=before,After=before,ActualCount=0}});
            RevealProgramTopCards(id,f.OwnerSeat,checked(r.Invoice.PaidCount*2),null,r.Invoice.RevealBind,SkillProgramCardSetVisibility.Public);
            FilterProgramBoundCards(id,r.Invoice.RevealBind,r.Invoice.RevealBind+".non-equipment",[],null,[SkillProgramCardCategory.Basic,SkillProgramCardCategory.Trick],[],[],null);
            var count=GetProgramCardSet(GetActiveProgramFrame(id),r.Invoice.RevealBind).CardIds.Count;
            ReplaceRuntimeTop(f=GetActiveProgramFrame(id)with{JiqiaoPaid=f.JiqiaoPaid! with{After=_movementSequence,ActualCount=count,RevealedCount=count}});IssueJiqiaoStage(f);AdvanceRuntimeProgram(id);return true;
        }
        var remaining=GetProgramCardSet(f,r.Invoice.RevealBind).CardIds.Where(cid=>_cardZones.GetLocation(cid)==CardLocation.Processing).ToArray();
        if(r.Stage==JiqiaoPaidStage.RevealChildren)
        {
            var claim=_players[f.OwnerSeat].IsAlive?remaining.Where(cid=>!EquipmentCatalog.IsEquipment(JiqiaoCard(cid).Kind)).ToArray():[];
            ReplaceRuntimeTop(f=f with{JiqiaoPaid=r with{Stage=JiqiaoPaidStage.ClaimChildren,Before=before,After=before,ActualCount=claim.Length,ClaimedCount=claim.Length}});
            if(claim.Length>0)MoveProgramCardsFromMultipleSources(claim,CardLocation.Hand(f.OwnerSeat),new(JiqiaoClaimReason));
            ReplaceRuntimeTop(f=GetActiveProgramFrame(id)with{JiqiaoPaid=f.JiqiaoPaid!with{After=_movementSequence}});IssueJiqiaoStage(f);AdvanceRuntimeProgram(id);return true;
        }
        if(r.Stage==JiqiaoPaidStage.ClaimChildren)
        {
            ReplaceRuntimeTop(f=f with{JiqiaoPaid=r with{Stage=JiqiaoPaidStage.CleanupChildren,Before=before,After=before,ActualCount=remaining.Length}});
            if(remaining.Length>0)MoveProgramCardsFromMultipleSources(remaining,CardLocation.DiscardPile,new(JiqiaoRemainderReason));
            ReplaceRuntimeTop(f=GetActiveProgramFrame(id)with{JiqiaoPaid=f.JiqiaoPaid!with{After=_movementSequence}});IssueJiqiaoStage(f);AdvanceRuntimeProgram(id);return true;
        }
        CompletePaidJiqiao(f);return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement,22500)]
    private bool ReturnPaidJiqiaoMovement(ProgramSkillFrame f)=>f.JiqiaoPaid is not null && ResumePaidJiqiao(f.Id);
    private void CompletePaidJiqiao(ProgramSkillFrame f)
    {
        var r=f.JiqiaoPaid!;AdvanceEventRulesAndQueueFact(new JiqiaoCompletedEvent(f.Id,r.Invoice.PaidCount,r.RevealedCount,r.ClaimedCount,_players[f.OwnerSeat].IsAlive));
        ReplaceRuntimeTop(f=f with{JiqiaoPaid=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);
    }
}
