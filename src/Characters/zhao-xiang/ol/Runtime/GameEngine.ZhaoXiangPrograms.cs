using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string ZhaoXiangDrawReason="program.zhao-xiang.conversion-draw";
    private bool DrainZhaoXiangChildren(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume,22601)]
    private bool ResumeZhaoXiang(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{ZhaoXiang:not null}f || f.Id!=id)return false;
        AssertZhaoXiang(f);if(GainGiftHasFinalGameEnd())return true;
        if(f.PendingMovementContinuation is not null){if(!TryDrainFireTargetMovement(f))ReturnZhaoXiangMovement(f);return true;}
        if(DrainZhaoXiangChildren(f))return true;f=GetActiveProgramFrame(id);var r=f.ZhaoXiang!;
        if(!_players[f.OwnerSeat].IsAlive || _winner!=Winner.None){ClearPendingDecision();FinishZhaoXiang(f);return true;}
        if(r.Stage=="selection"){if(_pendingDecision is null)PublishZhaoXiangExchange(f);return true;}
        if(r.Stage=="replacement-children")
        {
            var owner=_players[f.OwnerSeat];var request=owner.HasHpValue?Math.Min(1,Math.Max(0,owner.MaxHp-owner.Hp)):0;
            ReplaceRuntimeTop(f=f with{ZhaoXiang=r with{Stage="recovery-children",RecoveryRequested=request}});
            AdvanceEventRulesAndQueueFact(new ZhaoXiangExchangeRecoveryIssuedEvent(f.Id,f.OwnerSeat,request));
            if(request>0)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,request,null,null);
            AdvanceRuntimeProgram(f.Id);return true;
        }
        if(r.Stage=="recovery-children"){FinishZhaoXiang(f);return true;}
        throw new InvalidOperationException("Zhao Xiang resumed an unrelated native stage.");
    }
    private SkillProgramEffect? ZhaoXiangPausedEffect(ProgramSkillFrame f)=>f.InstructionIndex<1?null:ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 370)]
    private bool IsZhaoXiangMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>e?.Op==SkillProgramEffectOp.DrawAfterPaidMeiYingConversion &&
        f.ZhaoXiang is{Stage:"movement",ActionId:not null} && p.SubjectSeat==f.OwnerSeat && p.BeforeCount==0 && p.CoverageResultBind is null;
    [ProgramContinuation(ProgramContinuationKind.Movement,22602)]
    private bool ReturnZhaoXiangMovement(ProgramSkillFrame f)
    {
        if(f.ZhaoXiang is null)return false;
        if(f.PendingMovementContinuation is not{}p || !IsZhaoXiangMovement(f,ZhaoXiangPausedEffect(f),p))throw new InvalidOperationException("MeiYing draw consumed a foreign movement return.");
        AssertZhaoXiang(f);if(GainGiftHasFinalGameEnd() || TryDrainFireTargetMovement(f))return true;
        FinishZhaoXiang(f);return true;
    }
    private void FinishZhaoXiang(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{ZhaoXiang=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    private bool ValidZhaoXiang(ProgramSkillFrame f)
    {
        if(f.ZhaoXiang is not{}r || f.InstructionIndex!=1 || r.InstructionIndex!=f.InstructionIndex || r.Source!=new CardConversionSource(f.SkillId,f.TriggerId!,f.OwnerSeat,f.SkillInstanceId) ||
            r.GameplayHash!=f.GameplayHash || r.ParentFrameId!=f.WindowContext?.ParentFrameId || r.Turn!=_turnNumber)return false;
        var events=CompleteProgramEventHistory().ToArray();
        if(r.ActionId is{}actionId)
        {
            if(r.Stage!="movement" || ZhaoXiangPausedEffect(f)?.Op!=SkillProgramEffectOp.DrawAfterPaidMeiYingConversion || ZhaoXiangActionPayment(f)?.ActionId!=actionId || r.Before>r.After)return false;
            return events.OfType<ZhaoXiangConversionDrawPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray() is[var paid] && paid.ActionId==actionId && paid.OwnerSeat==f.OwnerSeat &&
                paid.Before==r.Before && paid.After==r.After && paid.Actual==r.ActualDraw && r.ActualDraw is >=0 and <=1 &&
                _cardMovements.Count(m=>m.Sequence>r.Before && m.Sequence<=r.After && m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(f.OwnerSeat) && m.Reason.Value==ZhaoXiangDrawReason)==r.ActualDraw;
        }
        if(!ZhaoXiangPreparationParentMatches(f) || ZhaoXiangPausedEffect(f)?.Op!=SkillProgramEffectOp.ExchangeFiveShuBySpentMeiYing || r.Generals.Count!=5 || r.Generals.Distinct(StringComparer.Ordinal).Count()!=5 ||
            r.Generals.Any(id=>!_contentRegistry.Generals.TryGetValue(id,out var general) || general.FactionId!="shu" || id==r.OriginalGeneralId) ||
            r.Stage is not("selection" or "replacement-children" or "recovery-children"))return false;
        if(events.OfType<ZhaoXiangExchangePaidEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not[var cost] || cost!=new ZhaoXiangExchangePaidEvent(f.Id,r.Source,r.GameplayHash,r.ParentFrameId,r.Turn,r.Removed,r.CumulativeRemoved,ZhaoXiangPoolFingerprint(r.Generals)) ||
            r.Removed<0 || r.CumulativeRemoved<r.Removed || ZhaoXiangCumulativeRemoved(f.OwnerSeat)!=r.CumulativeRemoved)return false;
        var selected=events.OfType<ZhaoXiangExchangeSelectedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(r.Stage=="selection")return r.SelectedGeneralId is null && selected.Length==0;
        if(r.SelectedGeneralId is not{}chosen || !r.Generals.Contains(chosen) || r.MaximumAfter!=Math.Clamp(r.CumulativeRemoved,2,8) ||
            selected is not[var choice] || choice!=new ZhaoXiangExchangeSelectedEvent(f.Id,f.OwnerSeat,r.OriginalGeneralId,chosen,r.MaximumBefore,r.MaximumAfter))return false;
        var recovery=events.OfType<ZhaoXiangExchangeRecoveryIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        return r.Stage=="replacement-children"?recovery.Length==0:recovery is[var request] && request.OwnerSeat==f.OwnerSeat && request.Requested==r.RecoveryRequested && r.RecoveryRequested is >=0 and <=1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 410)]
    private void AssertZhaoXiang(ProgramSkillFrame f)
    {
        if(f.ZhaoXiang is null)return;if(!ValidZhaoXiang(f))throw new InvalidOperationException("Zhao Xiang lost its actual conversion payment, five sampled Shu templates or once-issued recovery.");
        if(ReferenceEquals(f,_resolutionStack.LastOrDefault()) && _pendingDecision is{}p && f.ZhaoXiang.Stage=="selection" &&
            (p.PlayerSeat!=f.OwnerSeat || !p.IsPrivate || !AssistedChoicesEqual(p.Choices,ZhaoXiangExchangeChoices(f))))throw new InvalidOperationException("Private five-Shu choice leaked or changed its paid candidate set.");
    }
    [ProgramCapability]
    private sealed class ZhaoXiangProgramCapability(GameEngine engine) : IZhaoXiangProgramHost
    {
        public void GainMeiYingAfterActualSlashDamage(ProgramSkillFrame f)=>engine.GainMeiYingAfterActualSlashDamage(f);
        public SkillProgramStepOutcome ExchangeFiveShuBySpentMeiYing(ProgramSkillFrame f)=>engine.ExchangeFiveShuBySpentMeiYing(f);
        public SkillProgramStepOutcome DrawAfterPaidMeiYingConversion(ProgramSkillFrame f)=>engine.DrawAfterPaidMeiYingConversion(f);
    }
}
