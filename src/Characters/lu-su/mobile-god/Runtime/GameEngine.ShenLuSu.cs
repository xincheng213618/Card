using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string ShenLsReason="skill-program.mobile-shen-lu-su.";
    private bool ShenLsTeam=>_modeDefinition.ModeKind==ContentModeKind.Team&&_modeDefinition.TeamActivity is null&&_modeDefinition.BossActivity is null&&
        _modeDefinition.HulaoActivity is null&&_modeDefinition.ShenwuActivity is null&&_modeDefinition.NianActivity is null&&_modeDefinition.HellActivity is null&&
        _modeDefinition.DragonBoatActivity is null&&_modeDefinition.GuanduActivity is null&&_modeDefinition.WenheActivity is null;
    private bool ShenLsMode=>_modeDefinition.ModeKind==ContentModeKind.Identity||ShenLsTeam;
    private static CardConversionSource ShenLsSource(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
    private int[] ShenLsField(int seat)=>GetEquipment(_players[seat]).Concat(GetJudgment(_players[seat])).Select(c=>c.Id).ToArray();
    private int[] ShenLsGiveable(int seat)=>GetHand(_players[seat]).Concat(GetEquipment(_players[seat])).Where(c=>!c.IsGeneralWeapon&&
        !IsForeignEquipmentDiscardPrevented(seat,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Transfer)).Select(c=>c.Id).ToArray();
    private bool ShenLsDingzhouTarget(int owner,int target)=>IsValidPlayerSeat(target)&&owner!=target&&_players[owner].IsAlive&&_players[target].IsAlive&&
        ShenLsField(target).Length is >0 and var count&&ShenLsGiveable(owner).Length>=count;
    private int[] ShenLsPeers(int owner)=>CurrentTableOrder().Where(s=>s!=owner&&_players[s].IsAlive&&(!ShenLsTeam||GetHand(_players[s]).Count<=GetHand(_players[owner]).Count+1)).ToArray();
    [ProgramCapability] private sealed class ShenLuSuCapability(GameEngine e):IShenLuSuHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginShenLs(f,op); }
    [ProgramCapability] private sealed class ShenLuSuSelection(GameEngine e):IShenLuSuSelection
    { public bool DingzhouTarget(int owner,int target)=>e.ShenLsDingzhouTarget(owner,target); }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,38900,SkillProgramEffectOp.ResolveShenLuSuTamo,SkillProgramEffectOp.ResolveShenLuSuZhimeng)]
    private bool ShenLsEligible(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)=>ShenLsMode&&
        (t.Effects[0].Op==SkillProgramEffectOp.ResolveShenLuSuTamo ? w.Window==SkillProgramTriggerWindow.GameStarting&&CurrentTableOrder().Count-(_modeDefinition.ModeKind==ContentModeKind.Identity?1:0)>1 :
        w.Window==SkillProgramTriggerWindow.AfterTurnEnded&&w.SourceSeat==c.OwnerSeat&&ShenLsPeers(c.OwnerSeat).Length>0);
    private SkillProgramStepOutcome BeginShenLs(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.ShenLuSu is not null||f.InstructionIndex!=1)throw new InvalidOperationException("Shen Lu Su requires his original unpaid instruction.");
        var target=op==SkillProgramEffectOp.ResolveShenLuSuDingzhou?f.SelectedTargetSeats.Single():-1;
        if(target>=0&&!ShenLsDingzhouTarget(f.OwnerSeat,target))throw new InvalidOperationException("Dingzhou requires a real other field and its exact payable exchange.");
        var stage=op==SkillProgramEffectOp.ResolveShenLuSuTamo?ShenLuSuStage.SeatChoice:op==SkillProgramEffectOp.ResolveShenLuSuDingzhou?ShenLuSuStage.GiftChoice:ShenLuSuStage.PeerChoice;
        var r=new ShenLuSuReceipt(ShenLsSource(f),f.GameplayHash,op,f.WindowContext?.ParentFrameId??0,stage){Target=target,Amount=target>=0?ShenLsField(target).Length:0,Before=_movementSequence,After=_movementSequence};
        if(stage==ShenLuSuStage.SeatChoice)
        {
            var order=CurrentTableOrder();var fixedSeat=_modeDefinition.ModeKind==ContentModeKind.Identity?_players.Single(p=>p.Role==Role.Lord).Seat:-1;
            r=r with{FixedSeat=fixedSeat,OriginalOrder=Array.AsReadOnly(order.ToArray()),Order=fixedSeat<0?[]:Array.AsReadOnly(new[]{fixedSeat})};
        }
        ReplaceRuntimeTop(f=f with{ShenLuSu=r});
        AdvanceEventRulesAndQueueFact(new ShenLuSuStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentId,target,r.Amount));
        ContinueShenLs(f.Id);return SkillProgramStepOutcome.AwaitChoice;
    }
    private bool DrainShenLs(long id)=>TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||
        TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginAdvancedSkillsChanged(id)||
        TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id);
    private void ContinueShenLs(long id)
    {
        while(_pendingDecision is null&&_resolutionStack.LastOrDefault() is ProgramSkillFrame{ShenLuSu:{}r}f&&f.Id==id)
        {
            RequireShenLs(f);if(DrainShenLs(id))return;
            f=GetActiveProgramFrame(id);r=f.ShenLuSu!;
            if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None){FinishShenLs(f);return;}
            if(r.Stage==ShenLuSuStage.GiftChildren)
            {
                if(!_players[r.Target].IsAlive){FinishShenLs(f);return;}
                var field=ShenLsField(r.Target);
                if(field.Length==0){FinishShenLs(f);return;}
                MoveShenLs(f,field,ShenLuSuStage.ObtainChildren,CardLocation.Hand(f.OwnerSeat),null);continue;
            }
            if(r.Stage is ShenLuSuStage.ObtainChildren or ShenLuSuStage.DivideChildren){FinishShenLs(f);return;}
            if(r.Stage==ShenLuSuStage.SeatChoice&&r.Order.Count==r.OriginalOrder.Count){ApplyShenLsOrder(f);FinishShenLs(f);return;}
            if(r.Stage==ShenLuSuStage.GiftChoice&&r.Cards.Count==r.Amount)
            {MoveShenLs(f,r.Cards,ShenLuSuStage.GiftChildren,CardLocation.Hand(r.Target),null);continue;}
            PublishShenLs(f);return;
        }
    }
    private void FinishShenLs(ProgramSkillFrame f)
    {ReplaceRuntimeTop(f=f with{ShenLuSu=null,PendingMovementContinuation=null});AdvanceRuntimeProgram(f.Id);}
    private void MoveShenLs(ProgramSkillFrame f,IReadOnlyList<int> cards,ShenLuSuStage stage,CardLocation destination,Func<Card,CardLocation,CardLocation>? perCard)
    {
        var before=_movementSequence;
        ReplaceRuntimeTop(f=f with{ShenLuSu=f.ShenLuSu! with{Stage=stage,Paid=false,Before=before,After=before,BatchId=0,MovementCount=cards.Count,MovementInvoices=[]},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var reason=ShenLsReason+stage;
        MoveProgramCardsFromMultipleSources(cards,destination,new(reason),(batch,moves)=>
        {
            var parent=GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(parent with{ShenLuSu=parent.ShenLuSu! with{Paid=true,BatchId=batch,After=moves[^1].Sequence,
                MovementInvoices=Array.AsReadOnly(moves.Select(m=>new ShenLuSuMoveInvoice(m.CardId,m.From,m.To,m.Sequence)).ToArray())}});
            AdvanceEventRulesAndQueueFact(new ShenLuSuMovementPaidEvent(f.Id,stage,batch,moves.Count,before,moves[^1].Sequence));
        },perCard,stage==ShenLuSuStage.ObtainChildren?CardMovementDisclosure.FaceUp:CardMovementDisclosure.FaceDown);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,38900)] private bool ResumeShenLs(long id)
    {if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{ShenLuSu:not null}f||f.Id!=id)return false;ContinueShenLs(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,38900)] private bool ReturnShenLsMovement(ProgramSkillFrame f)=>f.ShenLuSu is not null&&ResumeShenLs(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,38900)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,38900)] private bool KeepShenLsPaid(ProgramSkillFrame f)=>f.ShenLuSu is not null;
}
