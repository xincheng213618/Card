namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static bool SunYuanMovementStage(SunYuanStage s)=>s is SunYuanStage.ObtainChildren or SunYuanStage.DiscardChildren or SunYuanStage.ReclaimChildren or SunYuanStage.NativeChildren;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27490)]
    private void AssertSunYuan(ProgramSkillFrame f)
    {
        if(f.SunYuan is not{}r)return;
        if(f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||r.Source!=SunYuanSource(f)||r.GameplayHash!=f.GameplayHash||GetPausedPrivateOfferEffect(f)?.Op!=r.Op||!SunYuanComposition.Handles(r.Op)||f.WindowContext is not{}c||r.WindowId!=c.ParentFrameId||!SunYuanParent(f)||r.ActualTurn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||!IsValidPlayerSeat(r.Holder)||!Enum.IsDefined(r.Stage)||r.Required<0||r.Before<0||r.After<r.Before||r.After>_movementSequence||r.Selected.Distinct().Count()!=r.Selected.Count||r.Materials.Select(x=>x.CardId).Distinct().Count()!=r.Materials.Count||r.Grain.Distinct().Count()!=r.Grain.Count||
            ProgramEventHistory<SunYuanStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Op==r.Op&&e.WindowId==r.WindowId&&e.ActualTurn==r.ActualTurn&&e.TurnOwner==r.TurnOwner&&e.Holder==(r.Op==SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply?f.OwnerSeat:r.Holder)&&e.DamageFrameId==r.DamageFrameId&&e.DamageSource==r.DamageSource&&e.DamageAmount==r.DamageAmount&&e.FrozenLostHp==r.FrozenLostHp&&e.Required==r.Required)!=1)throw new InvalidOperationException("Sun Yuan lost her exact native parent, immutable source, original damage, material set or actual turn.");
        var selected=ProgramEventHistory<SunYuanSelectedEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Index).ToArray();if(selected.Length!=r.Selected.Count||selected.Where((e,i)=>e.Index!=i||e.CardId!=r.Selected[i]).Any())throw new InvalidOperationException("Sun Yuan replaced an original opaque selection.");
        var paid=ProgramEventHistory<SunYuanPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(r.Paid?paid is not[var invoice]||invoice.OwnerSeat!=f.OwnerSeat||invoice.ActualTurn!=r.ActualTurn||invoice.TurnOwner!=r.TurnOwner||invoice.Direction!=(r.Op==SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply?"saying":f.TriggerId)||invoice.Count!=r.Selected.Count:paid.Length!=0)throw new InvalidOperationException("Sun Yuan lost her accepted once-only payment or zero-cost direction proof.");
        if(r.Op==SkillProgramEffectOp.ResolveDongxinDamageDiscardAndReclaim)
        {
            if(f.SkillId!="ol:dongxin"||c.Window!=SkillProgramTriggerWindow.AfterDamageApplied||c.TargetSeat!=r.Holder||c.SourceSeat!=r.DamageSource||c.Amount!=r.DamageAmount||r.DamageFrameId!=c.DamageFrameId||r.Required>r.FrozenLostHp+1||r.Selected.Count>r.Required||f.TriggerId is not("dealt"or"received")||(f.TriggerId=="dealt"?r.DamageSource!=f.OwnerSeat:r.Holder!=f.OwnerSeat)||r.Paid&&ProgramEventHistory<SunYuanPaidEvent>().Count(e=>e.OwnerSeat==f.OwnerSeat&&e.ActualTurn==r.ActualTurn&&e.TurnOwner==r.TurnOwner&&e.Direction==f.TriggerId)!=1)throw new InvalidOperationException("Dongxin lost its two independent native per-damage budgets.");
        }
        else if(f.SkillId!="ol:saying"||c.Window!=SkillProgramTriggerWindow.PlayPhaseStarting||f.TriggerId!="play-start-obtain"||r.Selected.Count>1||r.Stage!=SunYuanStage.Target&&(r.Holder==f.OwnerSeat||ProgramEventHistory<SunYuanTargetEvent>().Count(e=>e.FrameId==f.Id&&e.Holder==r.Holder)!=1))throw new InvalidOperationException("Saying lost its original other holder and genuine Play-start binding.");
        var material=ProgramEventHistory<SunYuanMaterialEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Index).ToArray();if(material.Length!=r.Materials.Count||material.Where((e,i)=>e.Index!=i||e.CardId!=r.Materials[i].CardId||e.Kind!=r.Materials[i].Kind||e.EffectiveKind!=r.Materials[i].EffectiveKind||e.From!=r.Materials[i].From||e.GeneralWeapon!=r.Materials[i].GeneralWeapon).Any())throw new InvalidOperationException("Sun Yuan lost her frozen last-known native material identities.");
        if(f.PendingMovementContinuation is not null)
        {
            if(!IsSunYuanMovement(f,GetPausedPrivateOfferEffect(f),f.PendingMovementContinuation))throw new InvalidOperationException("Sun Yuan lost her typed movement return.");
            if(r.Stage!=SunYuanStage.NativeChildren&&(ProgramEventHistory<SunYuanMovementIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.Step==r.Step&&e.Stage==r.Stage&&e.Before==r.Before&&e.After==r.After&&e.Count==r.ActualCount)!=1||_cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After).Any(m=>!IsSunYuanPaidMovement(f,m))))throw new InvalidOperationException("Sun Yuan lost an exact primary movement or native Wooden Ox spill.");
        }
        if(r.Stage==SunYuanStage.DamageChildren&&f.AttackAttempt is{}attack&&(r.DamageSource!=attack.TargetSeat||attack.SourceSeat!=f.OwnerSeat||attack.DamageAmount<0||attack.SourceLess||ProgramEventHistory<DongxinDamageIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.SourceSeat==f.OwnerSeat&&e.TargetSeat==attack.TargetSeat)!=1))throw new InvalidOperationException("Dongxin cannot substitute a different original damage source.");
        if(r.Stage is SunYuanStage.SlashIssuing or SunYuanStage.NativeUse or SunYuanStage.NativeChildren)
        {
            if(r.Selection is not{}s||s.ActorSeat!=r.Holder||s.TargetSeat!=f.OwnerSeat||!IsSlashCard(s.Kind)||ProgramEventHistory<SayingSlashPreparedEvent>().Count(e=>e.FrameId==f.Id&&e.SelectionHash==SunYuanHash(s))!=1)throw new InvalidOperationException("Saying lost its real supplier and frozen native selection.");
            if(r.Stage!=SunYuanStage.SlashIssuing&&(r.Native is not{}o||o.Selection!=s||o.Source!=r.Source||o.GameplayHash!=r.GameplayHash||ProgramEventHistory<SayingSlashIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.UseId==o.UseId&&e.ActionId==o.ActionId&&e.OriginalActionHash==SunYuanHash(o.OriginalAction))!=1))throw new InvalidOperationException("Saying lost its exact issued whole Use.");
        }
    }
    private bool IsSunYuanMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation pending)=>f.SunYuan is{Paid:true}r&&SunYuanMovementStage(r.Stage)&&pending==f.PendingMovementContinuation&&pending.SubjectSeat==f.OwnerSeat&&pending.BeforeCount==0&&pending.CoverageResultBind is null&&effect?.Op==r.Op;
    private bool IsSunYuanPaidMovement(ProgramSkillFrame f,CardMovementRecord m)
    {
        var r=f.SunYuan!;if(m.Sequence<=r.Before||m.Sequence>r.After||!_cardMovements.Contains(m))return false;
        if(ProgramEventHistory<SunYuanEntityMovedEvent>().Any(e=>e.FrameId==f.Id&&e.Step==r.Step&&e.Stage==r.Stage&&e.CardId==m.CardId&&e.Kind==m.CardKind&&e.From==m.From&&e.To==m.To&&e.Sequence==m.Sequence))return true;
        return r.Grain.Contains(m.CardId)&&m.From==CardLocation.WoodenOxGrain(r.Holder)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard&&r.Materials.Any(c=>c.Kind==CardKind.WoodenOx&&c.From==CardLocation.Equipment(r.Holder));
    }
    private bool IsSunYuanRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>f.SunYuan is{}r&&r.Stage is SunYuanStage.ObtainChildren or SunYuanStage.DiscardChildren&&RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.SourceSeat==r.Holder&&recovery.Attempt.TargetSeat==r.Holder&&recovery.Attempt.Amount==1&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&r.Materials.Any(c=>c.Kind==CardKind.SilverLion&&c.From==CardLocation.Equipment(r.Holder))&&recovery.Attempt.Completion.MoveReason?.Value==(r.Stage==SunYuanStage.ObtainChildren?SayingObtainReason:DongxinDiscardReason);
    private bool SunYuanFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.SunYuan is not{Paid:true}r)return false;AssertSunYuan(f);
        if(SayingEquipmentSlotPaymentRidesOn(f,child))return true;
        if(child is CardUseFrame use)return r.Stage==SunYuanStage.NativeUse&&use.SayingSlash==r.Native&&IsIssuedSayingUse(use);
        if(child is DrawFundedDistinctBasicFrame funded)return IsSayingSlashRequest(f)&&TryGetSayingSlashRequest(f,out var actor,out var targets)&&funded.Payment.ParentFrameId==f.Id&&funded.Payment.RequestFrameId==f.Id&&funded.Payment.Cursor==f.InstructionIndex&&funded.Payment.Source.OwnerSeat==actor&&funded.Payment.TargetSeat is{}target&&targets.Contains(target)&&ValidDrawFundedDistinctBasicPayment(funded.Payment);
        if(child is ChainedStateBasicFrame chained)return IsSayingSlashRequest(f)&&TryGetSayingSlashRequest(f,out var actor,out var targets)&&chained.Payment.ParentFrameId==f.Id&&chained.Payment.RequestFrameId==f.Id&&chained.Payment.Cursor==f.InstructionIndex&&chained.Payment.Source.OwnerSeat==actor&&chained.Payment.TargetSeat is{}target&&targets.Contains(target)&&ValidChainedStateBasicPayment(chained.Payment);
        if(child is ProgramSkillFrame{Weijie:{}wj}draft)return IsSayingSlashRequest(f)&&TryGetSayingSlashRequest(f,out var actor,out var targets)&&wj.Need.OwnerFrameId==f.Id&&wj.Need.RequestFrameId==f.Id&&wj.Need.ProviderSeat==actor&&wj.Need.Targets.SequenceEqual(targets)&&WeijieParent(draft);
        if(child is ProgramSkillFrame{Zonghu:{}zh}gift)return IsSayingSlashRequest(f)&&TryGetSayingSlashRequest(f,out var actor,out var targets)&&gift.ParentFrameId==f.Id&&zh.Need.OwnerFrameId==f.Id&&zh.Need.RequestFrameId==f.Id&&zh.Need.InstructionIndex==f.InstructionIndex&&zh.Need.RequestOp==r.Op&&zh.Need.ActorSeat==actor&&zh.Need.ProviderSeat==actor&&zh.Need.Targets.SequenceEqual(targets)&&ZonghuParent(gift);
        if(child is DamageFrame damage)return r.Stage==SunYuanStage.DamageChildren&&damage.ParentFrameId==f.Id&&f.AttackAttempt is{}a&&damage.SourceSeat==a.SourceSeat&&damage.TargetSeat==a.TargetSeat&&damage.Amount==a.DamageAmount;
        if(child is BeforeDamageProgramWindowFrame before)return r.Stage==SunYuanStage.DamageChildren&&(before.ContinuationAttackResolutionId??before.ParentFrameId)==f.Id&&f.AttackAttempt is not null;
        if(child is RecoveryReplacementFrame recovery)return IsSunYuanRecoveryParent(f,recovery);
        if(child is HpChangedTriggerWindowFrame hp)return SunYuanMovementStage(r.Stage)&&hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement;
        if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills)return skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        if(child is ProgramLifecycleTriggerWindowFrame state&&state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange)return state.ResumeProgramFrameId==f.Id&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id);
        if(child is not CardsMovedTriggerWindowFrame moved)return false;
        return r.Stage==SunYuanStage.NativeChildren?moved.ResumeProgramFrameId==f.Id&&IsSayingNativeSettlementBatch(f,moved.Batch):SunYuanMovementStage(r.Stage)&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>IsSunYuanPaidMovement(f,m));
    }
    private ProgramSkillFrame? SunYuanObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.SunYuanFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(g,n)=>g.IsMovementObserverEdge(n)||g.IsYanPuDyingExitEdge(n)||g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n],g._resolutionStack[n-1]),static(g,n,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    private bool HasSunYuanDamageObserver(long id)=>SunYuanObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasSunYuanBeforeDamageObserver(long id)=>SunYuanObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool IsSunYuanDying()=>ActiveDying is{}d&&SunYuanObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsSunYuanNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id)return false;
        if(f.SunYuan is{Paid:true,Stage:SunYuanStage.DamageChildren,DamageSource:{}victim}r)return target==victim&&amount==1&&source is null&&nature==DamageNature.Normal&&!sourceLess&&ProgramEventHistory<DongxinDamageIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.SourceSeat==f.OwnerSeat&&e.TargetSeat==victim)==1;
        if(SunYuanObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27490)]private bool AdvanceSunYuanChildren()=>_pendingDecision is null&&SunYuanObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
