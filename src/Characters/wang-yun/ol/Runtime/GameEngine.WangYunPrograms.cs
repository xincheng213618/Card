using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string WangYunPlanMoveReason="program.wang-yun.plan-payment";
    private static bool WangYunMaterial(Card card) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_wang_yun_ol_WangYunPrograms.WangYunMaterial(card);
    private bool WangYunActivationCardAllowed(SkillProgramActivation a,Card card)=>WangYunActivationCardAllowed(_currentSeat,a,card);
    private bool WangYunActivationCardAllowed(int ownerSeat,SkillProgramActivation a,Card card)
    {
        var features=ProgramInstructionResolver.Default.Features(a);
        if(features.HasOperation(SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash))return _cardZones.GetLocation(card.Id)==CardLocation.Hand(ownerSeat) &&
            !IsSelfHandCategoryDiscardForbidden(ownerSeat,card,CardLocation.Hand(ownerSeat),OwnedCardMoveIntent.Discard);
        return !features.HasOperation(SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial) || WangYunMaterial(card);
    }
    private CardConversionSource WangYunSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private SkillProgramEffect? WangYunPausedEffect(ProgramSkillFrame f)=>f.InstructionIndex<1?null:ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool WangYunActivationMatches(ProgramSkillFrame f,SkillProgramEffectOp op)=>f.TriggerId is null && f.WindowContext is null && f.InstructionIndex==1 &&
        f.OwnerSeat==_currentSeat && _phase==TurnPhase.Play && _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash==f.GameplayHash &&
        ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Instructions is[var instruction] && instruction.Op==op;
    private static Dictionary<string,string> WangYunArgs(ProgramSkillFrame f,string branch) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_wang_yun_ol_WangYunPrograms.WangYunArgs(f,branch);
    private int WangYunChooser(ProgramSkillFrame f)=>f.WangYunPlan?.Stage=="branch"?f.WangYunPlan.Actor:f.OwnerSeat;
    private void PublishWangYun(ProgramSkillFrame f)
    {
        if(!_players[f.OwnerSeat].IsAlive || f.WangYunPlan is{}r && !_players[r.Actor].IsAlive || _winner!=Winner.None){FinishWangYun(f);return;}
        var choices=f.WangYunPlan is not null?WangYunPlanChoices(f):WangYunTrickChoices(f);if(choices.Count==0){FinishWangYun(f);return;}
        var chooser=WangYunChooser(f);var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,chooser,skill.Name+"：选择本次真实结算的后续效果。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=chooser,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[chooser].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    [ProgramAnswer("wang-yun-plan")]
    private void ResolveWangYunChoice(PromptChoice selected)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.WangYunPlan is null && f.WangYunTrick is null || _pendingDecision?.PlayerSeat!=WangYunChooser(f) ||
            !AssistedChoicesEqual([selected],(f.WangYunPlan is not null?WangYunPlanChoices(f):WangYunTrickChoices(f)).Where(c=>c.Id==selected.Id).ToArray()))throw new InvalidOperationException("Wang Yun lost the exact actual chooser or current material/target choice.");
        AssertWangYun(f);ClearPendingDecision();
        if(f.WangYunTrick is not null){IssueWangYunTrick(f,selected);return;}
        var r=f.WangYunPlan!;switch(selected.Parameters["branch"])
        {
            case "target":ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Stage="branch",SlashTarget=selected.Targets.Single()}});PublishWangYun(f);return;
            case "transfer":ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Stage="transfer"}});PublishWangYun(f);return;
            case "give-weapon":
                var card=selected.Cards.Single();var recipient=selected.Targets.Single();ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Recipient=recipient}});
                PayWangYunPlanMovement(f,card,CardLocation.Equipment(r.Actor),CardLocation.Hand(recipient),"complete");return;
            case "slash":IssueWangYunSlash(f,selected);return;
            default:throw new InvalidOperationException("Unknown paid plan choice.");
        }
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,22900)]
    private bool ResumeWangYun(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id!=id || f.WangYunPlan is null && f.WangYunTrick is null && f.WangYunAwakening is null)return false;
        AssertWangYun(f);if(GainGiftHasFinalGameEnd())return true;
        if(f.PendingMovementContinuation is not null){if(!TryDrainFireTargetMovement(f))ReturnWangYunMovement(f);return true;}
        if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id))return true;
        f=GetActiveProgramFrame(id);
        if(f.WangYunAwakening is not null || f.WangYunTrick is{NativeReturned:true}){FinishWangYun(f);return true;}
        if(f.WangYunPlan is{}r && r.NativeReturned)
        {
            if(r.NativeUse!.Kind=="slash"){FinishWangYun(f);return true;}
            ReplaceRuntimeTop(f=f with{WangYunPlan=r with{Stage="target",NativeUse=null,NativeReturned=false,IssuingIds=Array.Empty<int>(),IssuingKind=null}});
        }
        if(!_players[f.OwnerSeat].IsAlive || f.WangYunPlan is{}p && !_players[p.Actor].IsAlive){ClearPendingDecision();FinishWangYun(f);return true;}
        if(f.WangYunPlan?.Stage=="weapon"){IssueWangYunWeapon(f);return true;}
        if(_pendingDecision is null)PublishWangYun(f);return true;
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 130)]
    private bool IsWangYunMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>e?.Op==SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash &&
        f.WangYunPlan is{Stage:"movement"} && p.SubjectSeat==f.OwnerSeat && p.BeforeCount==0 && p.CoverageResultBind is null;
    [ProgramContinuation(ProgramContinuationKind.Movement,22900)]
    private bool ReturnWangYunMovement(ProgramSkillFrame f)
    {
        if(f.WangYunPlan is not{}r)return false;if(f.PendingMovementContinuation is not{}p || !IsWangYunMovement(f,WangYunPausedEffect(f),p))throw new InvalidOperationException("Lianji consumed a foreign movement return.");
        AssertWangYun(f);if(GainGiftHasFinalGameEnd() || TryDrainFireTargetMovement(f))return true;
        ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null,WangYunPlan=r with{Stage=r.ReturnStage}});
        if(r.ReturnStage=="complete" || !_players[f.OwnerSeat].IsAlive || !_players[r.Actor].IsAlive)FinishWangYun(f);else IssueWangYunWeapon(f);return true;
    }
    private void FinishWangYun(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{WangYunPlan=null,WangYunTrick=null,WangYunAwakening=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramCapability]
    private sealed class WangYunProgramCapability(GameEngine engine) : IWangYunProgramHost
    {
        public SkillProgramStepOutcome ResolveLianjiPaidWeaponAndSlash(ProgramSkillFrame f)=>engine.ResolveLianjiPaidWeaponAndSlash(f);
        public SkillProgramStepOutcome AwakenFromIssuedLianjiSlashDamage(ProgramSkillFrame f)=>engine.AwakenFromIssuedLianjiSlashDamage(f);
        public SkillProgramStepOutcome UseThreeRandomTricksFromSelectedMaterial(ProgramSkillFrame f)=>engine.UseThreeRandomTricksFromSelectedMaterial(f);
    }
}
