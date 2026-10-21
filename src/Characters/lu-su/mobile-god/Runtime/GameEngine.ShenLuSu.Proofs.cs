namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment,38900)]
    private bool ShenLsMovementProof(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation pending)
    {
        if(f.ShenLuSu is not{Paid:true,BatchId:>0,MovementCount:>0}r||effect?.Op!=r.Operation||pending!=f.PendingMovementContinuation||
            pending.SubjectSeat!=f.OwnerSeat||pending.BeforeCount!=0||pending.CoverageResultBind is not null||r.After<=r.Before||r.After>_movementSequence||
            r.Stage is not(ShenLuSuStage.GiftChildren or ShenLuSuStage.ObtainChildren or ShenLuSuStage.DivideChildren))return false;
        var paid=r.MovementInvoices;
        if(ProgramEventHistory<ShenLuSuMovementPaidEvent>().Count(e=>e==new ShenLuSuMovementPaidEvent(f.Id,r.Stage,r.BatchId,r.MovementCount,r.Before,r.After))!=1)return false;
        if(paid.Count!=r.MovementCount||paid.Select(e=>e.CardId).Distinct().Count()!=paid.Count||paid.Max(e=>e.Sequence)!=r.After||
            paid.Any(e=>e.Sequence<=r.Before||e.Sequence>r.After||_cardMovements.Count(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.From==e.From&&m.To==e.To&&m.Reason.Value==ShenLsReason+r.Stage)!=1))return false;
        if(r.Stage==ShenLuSuStage.GiftChildren)return r.Operation==SkillProgramEffectOp.ResolveShenLuSuDingzhou&&paid.Select(e=>e.CardId).Order().SequenceEqual(r.Cards.Order())&&
            r.Cards.Count==r.Amount&&paid.All(e=>e.From.OwnerSeat==f.OwnerSeat&&e.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment&&e.To==CardLocation.Hand(r.Target));
        if(r.Stage==ShenLuSuStage.ObtainChildren)return r.Operation==SkillProgramEffectOp.ResolveShenLuSuDingzhou&&paid.All(e=>e.From.OwnerSeat==r.Target&&
            e.From.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment&&e.To==(GetAdvancedCard(e.CardId).IsGeneralWeapon&&e.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:CardLocation.Hand(f.OwnerSeat)));
        var division=r.Division;
        return r.Operation==SkillProgramEffectOp.ResolveShenLuSuZhimeng&&division.Count==r.Cards.Count&&division.Count(e=>e.ToSeat==f.OwnerSeat)==(r.Amount+1)/2&&
            ProgramEventHistory<ShenLuSuDivisionEvent>().Count(e=>e==new ShenLuSuDivisionEvent(f.Id,r.Target,r.Amount,r.OwnerShare.Count))==1&&
            division.Select(e=>e.CardId).Order().SequenceEqual(r.Cards.Order())&&division.All(e=>e.FromSeat==f.OwnerSeat||e.FromSeat==r.Target)&&
            division.All(e=>e.ToSeat==(r.OwnerShare.Contains(e.CardId)?f.OwnerSeat:r.Target))&&
            division.Where(e=>e.FromSeat!=e.ToSeat).All(e=>paid.Count(p=>p.CardId==e.CardId&&p.From==CardLocation.Hand(e.FromSeat)&&p.To==CardLocation.Hand(e.ToSeat))==1)&&
            paid.Count==division.Count(e=>e.FromSeat!=e.ToSeat);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,38900)] private void RequireShenLs(ProgramSkillFrame f)
    {
        if(f.ShenLuSu is not{}r)return;
        if(r.Source!=ShenLsSource(f)||r.GameplayHash!=f.GameplayHash||f.InstructionIndex!=1||!Enum.IsDefined(r.Stage)||r.ParentId!=(f.WindowContext?.ParentFrameId??0)||
            r.Before<0||r.After<r.Before||r.After>_movementSequence||r.Cards.Count!=r.Cards.Distinct().Count()||r.Order.Count!=r.Order.Distinct().Count()||
            ProgramEventHistory<ShenLuSuStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Operation==r.Operation&&e.ParentId==r.ParentId)!=1)
            throw new InvalidOperationException("Shen Lu Su lost his original source, content, native parent or immutable selected entities.");
        if(r.ParentId!=0)
        {
            var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);
            if(index<1||_resolutionStack[index-1].Id!=r.ParentId||_resolutionStack[index-1] switch
            {
                ProgramLifecycleTriggerWindowFrame p=>p.Window!=SkillProgramTriggerWindow.GameStarting||p.CandidateIndex<0||p.CandidateIndex>=p.Candidates.Count||!MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex]),
                DeferredTurnEndFrame p=>!IsActualAfterTurnEndedParent(p)||!MountObserverCandidateMatches(f,AfterTurnEndedCandidate(p)),_=>true
            })throw new InvalidOperationException("Shen Lu Su lost his exact suspended native setup or actual-turn-ended candidate.");
        }
        if(r.Operation==SkillProgramEffectOp.ResolveShenLuSuTamo)
        {
            if(r.Stage!=ShenLuSuStage.SeatChoice||r.OriginalOrder.Count!=_playerCount||r.OriginalOrder.Distinct().Count()!=_playerCount||r.Order.Except(r.OriginalOrder).Any()||
                r.FixedSeat>=0&&(r.Order.Count==0||r.Order[0]!=r.FixedSeat)||r.Order.Skip(r.FixedSeat>=0?1:0).Select((s,i)=>(s,i)).Any(x=>
                    ProgramEventHistory<ShenLuSuSeatChosenEvent>().Count(e=>e.FrameId==f.Id&&e.Index==x.i+(r.FixedSeat>=0?1:0)&&e.Seat==x.s)!=1))
                throw new InvalidOperationException("Tamo lost its original participants, fixed slot or issued permutation choices.");
        }
        if(r.Operation==SkillProgramEffectOp.ResolveShenLuSuDingzhou&&(r.Target==f.OwnerSeat||!IsValidPlayerSeat(r.Target)||r.Amount<1||r.Cards.Count>r.Amount||
            r.Stage is not(ShenLuSuStage.GiftChoice or ShenLuSuStage.GiftChildren or ShenLuSuStage.ObtainChildren)||
            ProgramEventHistory<ShenLuSuStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Target==r.Target&&e.Amount==r.Amount)!=1||
            r.Cards.Select((id,i)=>(id,i)).Any(x=>ProgramEventHistory<ShenLuSuGiftChosenEvent>().Count(e=>e.FrameId==f.Id&&e.Index==x.i)!=1)))
            throw new InvalidOperationException("Dingzhou lost its exact once-selected recipient, frozen exchange count or physical payment choices.");
        if(r.Operation==SkillProgramEffectOp.ResolveShenLuSuZhimeng&&(r.Stage is not(ShenLuSuStage.PeerChoice or ShenLuSuStage.DivideChildren)||
            r.Stage==ShenLuSuStage.DivideChildren&&(r.Target==f.OwnerSeat||!IsValidPlayerSeat(r.Target)||r.Amount!=r.Cards.Count||
                r.OwnerShare.Count!=(r.Amount+1)/2||r.OwnerShare.Distinct().Count()!=r.OwnerShare.Count||r.OwnerShare.Except(r.Cards).Any())))
            throw new InvalidOperationException("Zhimeng lost its original two participants or exact random pooled partition.");
        if(f.PendingMovementContinuation is{}pending&&!ShenLsMovementProof(f,ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Instructions[0],pending))
            throw new InvalidOperationException("Shen Lu Su lost his exact once-paid native movement invoice.");
    }
}
