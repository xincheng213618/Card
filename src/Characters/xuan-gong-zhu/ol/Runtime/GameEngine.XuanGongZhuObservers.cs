namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void RequireIssuedXuanGongZhu(ProgramSkillFrame f)
    {
        if(f.GaolingRecovery is not null && !ValidGaolingRecovery(f))throw new InvalidOperationException("Gaoling lost its exact actual appearance and selected recovery invoice.");
        if(f.QimeiDraw is not null)RequireQimeiDraw(f);
        if(f.ZhuijiInitialPaid is null && f.ZhuijiDebt is null)return;
        if(!ValidZhuijiIssued(f))throw new InvalidOperationException("Zhuiji lost its actual source, phase and unique issued grant.");
        if(f.ZhuijiDebt is not {} r)return;
        var facts=CompleteProgramEventHistory().OfType<ZhuijiPlayDebtConsumedEvent>().Where(e=>e.Grant==r.Grant).ToArray();
        if(!Enum.IsDefined(r.Stage) || r.Stage==ZhuijiDebtStage.ChoosingDiscard && facts.Length!=0 ||
            r.Stage!=ZhuijiDebtStage.ChoosingDiscard && (facts is not [var paid] || paid.ProgramFrameId!=f.Id || !paid.Paid))
            throw new InvalidOperationException("Zhuiji debt lost its exact once-paid ending invoice.");
        if(r.Stage==ZhuijiDebtStage.DiscardChildren && !IsZhuijiMovement(f,GetPausedPrivateOfferEffect(f),new(f.OwnerSeat,0,null)))
            throw new InvalidOperationException("Zhuiji ending discard lost its actual native physical movements.");
    }
    private bool CanContinueIssuedXuanGongZhu(ProgramSkillFrame f)=>ValidQimeiDraw(f) || ValidZhuijiIssued(f) || ValidGaolingRecovery(f);
    private bool XuanGongZhuFirstChild(ProgramSkillFrame root,ResolutionFrame child)
    {
        if(ValidGaolingRecovery(root) && root.GaolingRecovery is {} g)
        {
            if(child is HpChangedTriggerWindowFrame gaolingHp)return gaolingHp.Change.ParentFrameId==root.Id && gaolingHp.ResumeFrameId==root.Id && gaolingHp.Continuation==PostEventContinuation.Program &&
                gaolingHp.Change.Kind==HpChangeKind.Recovery && gaolingHp.Change.SourceSeat==root.OwnerSeat && gaolingHp.Change.TargetSeat==g.TargetSeat && gaolingHp.Change.Amount==1;
            if(child is RecoveryReplacementFrame gaolingRecovery && TypedRecoveryPausedEffect(root) is {} gaolingEffect && gaolingEffect.CompiledInstruction is RecoverProgramInstruction gaolingInstruction)
                return gaolingRecovery.Attempt.TargetSeat==g.TargetSeat && gaolingRecovery.Attempt.Amount==1 && DirectProgramRecoveryReturnMatches(root,gaolingEffect,gaolingInstruction,gaolingRecovery);
        }
        if(ValidQimeiDraw(root) && root.QimeiDraw is {} q && child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId==root.Id && moved.Batch.AwaitingProgramFrameId==root.Id && moved.Batch.OriginOwnerSeat==root.OwnerSeat &&
                moved.Batch.OriginSkillId==root.SkillId && moved.Batch.OriginSkillInstanceId==root.SkillInstanceId && moved.Batch.Movements.Count>0 &&
                moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>q.Before && m.Sequence<=q.After && m.To==CardLocation.Hand(q.Receipt.BeneficiarySeat) && m.Reason.Value=="program.qimei.equal-draw");
        if(!ValidZhuijiIssued(root))return false;
        if(root.ZhuijiDebt is {Stage:ZhuijiDebtStage.DiscardChildren} d && child is CardsMovedTriggerWindowFrame discard)
            return discard.Batch.ParentFrameId==root.Id && discard.Batch.AwaitingProgramFrameId==root.Id && discard.Batch.OriginSkillId==root.SkillId &&
                discard.Batch.OriginSkillInstanceId==root.SkillInstanceId && discard.Batch.Movements.Count>0 && discard.Batch.Movements.All(m=>_cardMovements.Contains(m) &&
                    m.Sequence>d.Before && m.Sequence<=d.After && m.From.OwnerSeat==root.OwnerSeat && m.To==CardLocation.DiscardPile && m.Reason.Value=="program.zhuiji.ending-discard");
        if(root.ZhuijiDebt is {Stage:ZhuijiDebtStage.HpChildren} && child is DyingFrame dying)
            return dying.ParentFrameId==root.Id && dying.VictimSeat==root.OwnerSeat && CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Any(e=>e.FrameId==root.Id && e.TargetSeat==root.OwnerSeat && e.Amount==1 && e.RemainingHp==0);
        if(child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId==root.Id && hp.ResumeFrameId==root.Id && hp.Change.TargetSeat==root.OwnerSeat && hp.Change.Amount==1 &&
                (root.ZhuijiInitialPaid is {RecoverThenDiscard:true} && hp.Continuation==PostEventContinuation.Program && hp.Change.Kind==HpChangeKind.Recovery && hp.Change.SourceSeat==root.OwnerSeat ||
                 root.ZhuijiDebt is {Stage:ZhuijiDebtStage.HpChildren} && hp.Continuation==PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind==HpChangeKind.Loss && hp.Change.SourceSeat is null);
        if(root.ZhuijiInitialPaid is {RecoverThenDiscard:true} && child is RecoveryReplacementFrame recovery && TypedRecoveryPausedEffect(root) is {} effect && effect.CompiledInstruction is RecoverProgramInstruction instruction)
            return recovery.Attempt.Amount==1 && recovery.Attempt.TargetSeat==root.OwnerSeat && DirectProgramRecoveryReturnMatches(root,effect,instruction,recovery);
        if(root.ZhuijiInitialPaid is {RecoverThenDiscard:false} && child is CardsMovedTriggerWindowFrame draw && TypedRecoveryPausedEffect(root) is {Op:SkillProgramEffectOp.Draw,Amount:2})
            return draw.Batch.ParentFrameId==root.Id && draw.Batch.OriginOwnerSeat==root.OwnerSeat && draw.Batch.OriginSkillId==root.SkillId && draw.Batch.OriginSkillInstanceId==root.SkillInstanceId &&
                draw.Batch.Movements.Count is >0 and <=2 && draw.Batch.Movements.All(m=>m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(root.OwnerSeat) && _cardMovements.Contains(m));
        return false;
    }
    private ProgramSkillFrame? XuanGongZhuObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.XuanGongZhuFirstChild(r,c),static(e,i)=>e.HasMovementObserverSuffix(i));
    private bool HasXuanGongZhuDamageObserver(long id)=>XuanGongZhuObserverRoot() is {} r && _resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && _resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    private bool IsXuanGongZhuDying()=>ActiveDying is {} d && XuanGongZhuObserverRoot() is {} r && _resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    private bool TryAdvanceXuanGongZhuSubtree()=>_pendingDecision is null && XuanGongZhuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    private bool AllowsXuanGongZhuNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess || observer.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id!=observer.Id || XuanGongZhuObserverRoot() is not {} root || root.Id==observer.Id || observer.WindowContext?.Window is not
            (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged))return false;
        var e=GetPausedPrivateOfferEffect(observer);return e is {Op:SkillProgramEffectOp.Damage} && e.Amount==amount && e.ActorReference==source && e.DamageNature==nature &&
            target==(e.TargetReference is {} reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target));
    }
    [ProgramCapability]
    private sealed class XuanGongZhuProgramCapability(GameEngine engine) : IXuanGongZhuProgramHost
    {
        public SkillProgramStepOutcome ArmQimeiPair(ProgramSkillFrame f)=>engine.ArmQimeiPair(f);
        public SkillProgramStepOutcome SettleQimeiPairEquality(ProgramSkillFrame f)=>engine.SettleQimeiPairEquality(f);
        public SkillProgramStepOutcome OfferZhuijiActualPlayChoice(ProgramSkillFrame f)=>engine.ArmZhuijiActualPlay(f);
        public SkillProgramStepOutcome SettleZhuijiActualPlayDebt(ProgramSkillFrame f)=>engine.SettleZhuijiActualPlayDebt(f);
        public SkillProgramStepOutcome AfterActualIdentityAppearanceRecover(ProgramSkillFrame f)=>engine.ArmGaolingRecovery(f);
    }
}
