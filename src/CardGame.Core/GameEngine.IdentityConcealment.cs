using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed record IdentityConcealmentState(string GeneralId,int InitialHp);
public enum IdentityAppearanceEntry { ActualTurnStart, DamageDeduction, ProgramHpLoss }
public sealed record IdentityAppearanceReceipt(IdentityAppearanceEntry Entry,long? ParentFrameId,int OwnerSeat,int ActualTurnNumber,int ActualTurnOwnerSeat,
    string GeneralId,int RestoredHp,int MaximumHp,int RequestedDeduction,int? DamageSourceSeat,CardKind? CardKind,
    int? ObservedAttackAmount=null,int? NativeSourceSeat=null,bool SourceLess=false,DamageNature? Nature=null,long? NativeActionId=null,int? OriginalInstructionIndex=null);
public sealed record IdentityConcealmentStartedEvent(int OwnerSeat):IGameEvent;
public sealed record IdentityHpDeductionPreventedEvent(long AppearanceFrameId,int OwnerSeat,int RequestedDeduction,IdentityAppearanceEntry Entry,int? DamageSourceSeat):IGameEvent;
public sealed record IdentityGeneralAppearedEvent(long AppearanceFrameId,IdentityAppearanceReceipt Receipt):IGameEvent;
public sealed record IdentityAppearanceCompletedEvent(long AppearanceFrameId,int OwnerSeat):IGameEvent;
public sealed record GaolingRecoverySelectedEvent(long ProgramFrameId,long AppearanceFrameId,CardConversionSource Source,string GameplayHash,int TargetSeat):IGameEvent;
public sealed record GaolingRecoveryReceipt(long AppearanceFrameId,CardConversionSource Source,string GameplayHash,int TargetSeat);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public GaolingRecoveryReceipt? GaolingRecovery{get;init;}
}
public sealed record IdentityAppearanceFrame(long Id,IdentityAppearanceReceipt Receipt,long? ActiveWindowId=null,ResolutionFrameStep Step=ResolutionFrameStep.ResolvingEffect)
    :ResolutionFrame(Id,ResolutionFrameKind.IdentityAppearance,Step);
public sealed partial record ProgramSkillWindowContext
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IdentityAppearanceReceipt? IdentityAppearance{get;init;}
}
public sealed partial class GameEngine
{
    internal const string IdentityConcealedGeneralId="concealment:ol";
    private static GeneralDefinition IdentityConcealedGeneral()=>new(IdentityConcealedGeneralId,"隐匿","",[]);
    private bool GeneralHasIdentityConcealment(GeneralDefinition general)=>_modeDefinition.ModeKind==ContentModeKind.Identity &&
        general.Skills.Select(s=>s.ContentId).OfType<string>().Any(id=>_contentRegistry!.Skills.GetValueOrDefault(id)?.Program?.Triggers.Any(t=>
            t.Window==SkillProgramTriggerWindow.IdentityGeneralAppeared && t.Effects.Any(e=>e.Op==SkillProgramEffectOp.AfterActualIdentityAppearanceRecover))==true);
    private void InitializeSelectedIdentityConcealment(CharacterState player)
    {
        if(!GeneralHasIdentityConcealment(player.General) || player.IdentityConcealed)return;
        var initial=player.General.InitialHp??player.General.BaseHp;
        if(player.Role==Role.Lord)initial++;
        player.IdentityConcealment=new(player.General.Id,initial);player.GeneralRevealed=false;
        AdvanceEventRulesAndQueueFact(new IdentityConcealmentStartedEvent(player.Seat));
    }
    private bool TryBeginIdentityAppearanceForTurn(CharacterState player)
    {
        if(!player.IdentityConcealed)return false;
        if(_resolutionStack.Count!=0)throw new InvalidOperationException("Own-turn identity appearance needs its exact clean native boundary.");
        BeginIdentityAppearance(player,IdentityAppearanceEntry.ActualTurnStart,null,0,null,null);return true;
    }
    private bool TryBeginIdentityAppearanceForDamage(IDamageAttempt attack,int amount)
    {
        if(amount<=0 || !_players[attack.TargetSeat].IdentityConcealed)return false;
        if(_resolutionStack.LastOrDefault()?.Id!=attack.ResolutionId || CurrentDamageAttempt?.ResolutionId!=attack.ResolutionId)
            throw new InvalidOperationException("Identity appearance lost its actual native damage owner.");
        BeginIdentityAppearance(_players[attack.TargetSeat],IdentityAppearanceEntry.DamageDeduction,attack.ResolutionId,amount,attack.IsSourceLess?null:attack.SourceSeat,attack.EffectiveCardKind,attack);return true;
    }
    private bool TryBeginIdentityAppearanceForProgramHpLoss(long frameId,string skillId,int targetSeat,int amount)
    {
        if(amount<=0 || !_players[targetSeat].IdentityConcealed)return false;
        var frame=GetActiveProgramFrame(frameId);
        if(frame.SkillId!=skillId)throw new InvalidOperationException("Identity HP-loss appearance lost its owning program source.");
        BeginIdentityAppearance(_players[targetSeat],IdentityAppearanceEntry.ProgramHpLoss,frameId,amount,null,null);return true;
    }
    private void BeginIdentityAppearance(CharacterState player,IdentityAppearanceEntry entry,long? parent,int deduction,int? damageSource,CardKind? cardKind,IDamageAttempt? attack=null)
    {
        var state=player.IdentityConcealment??throw new InvalidOperationException("Identity appearance cannot repeat.");
        if(state.GeneralId!=player.General.Id)throw new InvalidOperationException("Concealment belongs to a different selected real template.");
        var receipt=new IdentityAppearanceReceipt(entry,parent,player.Seat,_turnNumber,_currentSeat,player.General.Id,
            Math.Clamp(state.InitialHp,1,player.MaxHp),player.MaxHp,deduction,damageSource,cardKind,
            attack?.DamageAmount,attack?.SourceSeat,attack?.IsSourceLess??false,attack is null?null:GetDamageNature(attack),
            attack is null?null:LifecycleCardUse(attack.ResolutionId)?.Action?.ActionId,
            parent is {} parentId && _resolutionStack.SingleOrDefault(f=>f.Id==parentId) is ProgramSkillFrame p?p.InstructionIndex:null);
        var id=++_resolutionSequence;PushRuntimeFrame(new IdentityAppearanceFrame(id,receipt));
        player.IdentityConcealment=null;player.Hp=receipt.RestoredHp;player.GeneralRevealed=true;
        if(deduction>0)AdvanceEventRulesAndQueueFact(new IdentityHpDeductionPreventedEvent(id,player.Seat,deduction,entry,damageSource));
        AdvanceEventRulesAndQueueFact(new IdentityGeneralAppearedEvent(id,receipt));AdvanceIdentityAppearance((IdentityAppearanceFrame)_resolutionStack.Last());
    }
    private void AdvanceIdentityAppearance(IdentityAppearanceFrame frame)
    {
        RequireIdentityAppearance(frame);
        if(frame.ActiveWindowId is not null)return;
        var player=_players[frame.Receipt.OwnerSeat];var facts=CaptureProgramTriggerFacts(player);
        var candidates=CollectEligibleProgramTriggerCandidates(player,SkillProgramTriggerWindow.IdentityGeneralAppeared,facts)
            .Where(c=>frame.Receipt.ActualTurnNumber > 0 && frame.Receipt.ActualTurnOwnerSeat!=c.OwnerSeat).ToArray();
        if(candidates.Length==0){CompleteIdentityAppearance(frame);return;}
        var window=new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence,player.Seat,SkillProgramTriggerWindow.IdentityGeneralAppeared,candidates,
            ProgramLifecycleContinuation.ResumeIdentityAppearance,facts){IdentityAppearanceFrameId=frame.Id};
        ReplaceRuntimeTop(frame with{ActiveWindowId=window.Id});PushRuntimeFrame(window);AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }
    private ProgramSkillWindowContext CreateIdentityAppearanceContext(ProgramLifecycleTriggerWindowFrame window,ProgramTriggerCandidate candidate)
    {
        var appearance=_resolutionStack.OfType<IdentityAppearanceFrame>().Single(f=>f.Id==window.IdentityAppearanceFrameId && f.ActiveWindowId==window.Id);
        return new(SkillProgramTriggerWindow.IdentityGeneralAppeared,window.Id,candidate.OwnerSeat,SourceSeat:appearance.Receipt.ActualTurnOwnerSeat,
            TargetSeat:appearance.Receipt.OwnerSeat,Facts:window.Facts){IdentityAppearance=appearance.Receipt};
    }
    private bool CanRunIdentityAppearanceCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>context.IdentityAppearance is {} r && r.OwnerSeat==c.OwnerSeat &&
        r.ActualTurnNumber > 0 && r.ActualTurnOwnerSeat!=c.OwnerSeat && r.ActualTurnNumber==_turnNumber && _resolutionStack.OfType<IdentityAppearanceFrame>().Any(f=>f.Receipt==r && f.ActiveWindowId==context.ParentFrameId);
    private void CompleteIdentityAppearance(IdentityAppearanceFrame frame)
    {
        RequireIdentityAppearance(frame);var r=frame.Receipt;
        AdvanceEventRulesAndQueueFact(new IdentityAppearanceCompletedEvent(frame.Id,r.OwnerSeat));PopResolutionFrame(frame.Id,ResolutionFrameKind.IdentityAppearance);
        if(r.Entry==IdentityAppearanceEntry.ActualTurnStart){ContinueTurnAfterIdentityAppearance(_players[r.OwnerSeat]);return;}
        if(r.ParentFrameId is not {} parent)throw new InvalidOperationException("Identity appearance lost its exact parent return.");
        if(r.Entry==IdentityAppearanceEntry.ProgramHpLoss){AdvanceRuntimeProgram(parent);return;}
        var attack=IdentityAppearanceAttack(parent)??throw new InvalidOperationException("Identity appearance lost its actual suspended damage attempt.");
        if(!IdentityAppearanceAttackMatches(r,attack))
            throw new InvalidOperationException("Identity appearance cannot close another native attack.");
        switch(attack)
        {
            case CardAttackHandle card: CompleteAttack(card);break;
            case ProgramAttackHandle program: CompleteProgramAttack(program);break;
            default:throw new InvalidOperationException("Appearance cannot complete an unsupported native attack owner.");
        }
    }
    private void ContinueTurnAfterIdentityAppearance(CharacterState player)
    {
        ConsumeEndRefillNextTurn(player);
        if(TryQueueHengyeRecovery(player))return;
        ResolveHengyeTurnStart(player);ContinueTurnAfterHengye(player);
    }
    private IDamageAttempt? IdentityAppearanceAttack(long id)=>_resolutionStack.SingleOrDefault(f=>f.Id==id) switch
    {
        ProgramSkillFrame{AttackAttempt:not null}=>new ProgramAttackHandle(this,id),
        ProgramSkillFrame{CardAttack.Active:true}=>new CardAttackHandle(this,id),
        CardUseFrame{CardAttack.Active:true}=>new CardAttackHandle(this,id),
        JudgmentFrame{CardAttack.Active:true}=>new CardAttackHandle(this,id),_=>null
    };
    private void ResumeIdentityAppearanceWindow(ProgramLifecycleTriggerWindowFrame window)
    {
        var frame=_resolutionStack.LastOrDefault()as IdentityAppearanceFrame??throw new InvalidOperationException("Appearance window returned to the wrong parent.");
        if(frame.Id!=window.IdentityAppearanceFrameId || frame.ActiveWindowId!=window.Id)throw new InvalidOperationException("Appearance window lost its typed return identity.");
        CompleteIdentityAppearance(frame);
    }
    private void RequireIdentityAppearance(IdentityAppearanceFrame frame)
    {
        var r=frame.Receipt;
        if(!Enum.IsDefined(r.Entry) || !IsValidPlayerSeat(r.OwnerSeat) || _players[r.OwnerSeat].IdentityConcealed || _players[r.OwnerSeat].General.Id!=r.GeneralId ||
            CompleteProgramEventHistory().OfType<IdentityGeneralAppearedEvent>().Count(e=>e.AppearanceFrameId==frame.Id && e.Receipt==r)!=1 ||
            CompleteProgramEventHistory().OfType<IdentityAppearanceCompletedEvent>().Any(e=>e.AppearanceFrameId==frame.Id))
            throw new InvalidOperationException("Identity appearance lost its exact once-revealed template and owning invoice.");
        var index=_resolutionStack.FindIndex(f=>f.Id==frame.Id);
        if(index<0 || r.Entry==IdentityAppearanceEntry.ActualTurnStart && (index!=0 || r.ParentFrameId is not null || r.OwnerSeat!=r.ActualTurnOwnerSeat) ||
            r.Entry!=IdentityAppearanceEntry.ActualTurnStart && (index<1 || _resolutionStack[index-1].Id!=r.ParentFrameId) ||
            r.Entry==IdentityAppearanceEntry.DamageDeduction && (IdentityAppearanceAttack(r.ParentFrameId!.Value) is not {} attack || !IdentityAppearanceAttackMatches(r,attack)) ||
            r.Entry==IdentityAppearanceEntry.ProgramHpLoss && (_resolutionStack[index-1] is not ProgramSkillFrame p || p.InstructionIndex!=r.OriginalInstructionIndex))
            throw new InvalidOperationException("Identity appearance lost its frozen native source, amount, nature, action or typed parent cursor.");
    }
    private bool IdentityAppearanceAttackMatches(IdentityAppearanceReceipt r,IDamageAttempt a)=>a.ResolutionId==r.ParentFrameId && a.TargetSeat==r.OwnerSeat &&
        a.SourceSeat==r.NativeSourceSeat && a.IsSourceLess==r.SourceLess && a.DamageAmount==r.ObservedAttackAmount && a.EffectiveCardKind==r.CardKind &&
        GetDamageNature(a)==r.Nature && (a.IsSourceLess?null:(int?)a.SourceSeat)==r.DamageSourceSeat &&
        LifecycleCardUse(a.ResolutionId)?.Action?.ActionId==r.NativeActionId;
    private SkillProgramStepOutcome ArmGaolingRecovery(ProgramSkillFrame input)
    {
        var f=GetActiveProgramFrame(input.Id);
        var appearance=_resolutionStack.OfType<IdentityAppearanceFrame>().Single(a=>a.ActiveWindowId==f.WindowContext?.ParentFrameId);
        if(f.SelectedTargetSeats is not [var target] || !_players[target].IsAlive || !_players[target].HasHpValue ||
            f.WindowContext?.IdentityAppearance!=appearance.Receipt || appearance.Receipt.OwnerSeat!=f.OwnerSeat || appearance.Receipt.ActualTurnOwnerSeat==f.OwnerSeat || f.GaolingRecovery is not null)
            throw new InvalidOperationException("Gaoling needs one actual wounded recipient after an off-turn native identity appearance.");
        var receipt=new GaolingRecoveryReceipt(appearance.Id,QimeiSource(f),f.GameplayHash,target);
        ReplaceRuntimeTop(f with{GaolingRecovery=receipt});AdvanceEventRulesAndQueueFact(new GaolingRecoverySelectedEvent(f.Id,appearance.Id,receipt.Source,f.GameplayHash,target));
        return SkillProgramStepOutcome.Continue;
    }
    private bool ValidGaolingRecovery(ProgramSkillFrame f)=>f.GaolingRecovery is {} r && r.Source.OwnerSeat==f.OwnerSeat && r.Source.SkillId==f.SkillId &&
        r.Source.SkillInstanceId==f.SkillInstanceId && r.Source.BindingId==f.TriggerId && r.GameplayHash==f.GameplayHash && f.SelectedTargetSeats is [var target] && target==r.TargetSeat &&
        f.WindowContext?.IdentityAppearance is {} a && a.OwnerSeat==f.OwnerSeat && a.ActualTurnOwnerSeat!=f.OwnerSeat &&
        _resolutionStack.OfType<IdentityAppearanceFrame>().Any(p=>p.Id==r.AppearanceFrameId && p.Receipt==a && p.ActiveWindowId==f.WindowContext.ParentFrameId) &&
        CompleteProgramEventHistory().OfType<GaolingRecoverySelectedEvent>().Count(e=>e.ProgramFrameId==f.Id && e.AppearanceFrameId==r.AppearanceFrameId && e.Source==r.Source && e.GameplayHash==r.GameplayHash && e.TargetSeat==r.TargetSeat)==1;
}
