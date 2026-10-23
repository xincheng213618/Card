namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HappyDcOriginalCandidate(ProgramSkillFrame f)
    {
        if(f.WindowContext is not{}c||c.OwnerSeat!=f.OwnerSeat||f.ActivationId!=f.TriggerId||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.SkillInstanceId==f.SkillInstanceId&&e.BindingId==f.TriggerId&&e.Window==c.Window)!=1)return false;
        return _resolutionStack.SingleOrDefault(p=>p.Id==c.ParentFrameId) switch
        {
            DeferredTurnEndFrame p=>f.TriggerId=="owner-ended"&&MatchesAfterTurnEndedChild(p,f,c),
            ProgramCardTriggerWindowFrame w=>f.TriggerId=="first-slash-target"&&c.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized&&c.CardUse?.CardActionId==w.Action.ActionId&&
                w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&w.Candidates[w.CandidateIndex] is{}candidate&&candidate.OwnerSeat==f.OwnerSeat&&candidate.SkillId==f.SkillId&&candidate.TriggerId==f.TriggerId&&candidate.SkillInstanceId==f.SkillInstanceId&&candidate.GameplayHash==f.GameplayHash,
            _=>false
        };
    }
    private bool ValidHappyDc(ProgramSkillFrame f)
    {
        if(f.HappyDiaoChan is not{}r||r.Source!=HappyDcSource(f)||r.GameplayHash!=f.GameplayHash||r.ParentId!=(f.WindowContext?.ParentFrameId??0)||r.ActualTurn!=_turnNumber||r.ActualTurnOwner!=_currentSeat||
            !Enum.IsDefined(r.Stage)||GetPausedPrivateOfferEffect(f)?.Op!=r.Operation||f.InstructionIndex!=1||r.Before<0||r.After<r.Before||r.After>_movementSequence||
            r.Viewed is not System.Collections.IList{IsReadOnly:true}||r.Nonwinners is not System.Collections.IList{IsReadOnly:true}||r.Moves is not System.Collections.IList{IsReadOnly:true}||
            ProgramEventHistory<HappyDiaoChanStartedEvent>().Count(e=>e==new HappyDiaoChanStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.ParentId,r.ActualTurn,r.ActualTurnOwner))!=1)return false;
        if(r.Operation==SkillProgramEffectOp.ResolveHappyDiaoChanMeihun)
        {
            if(f.SkillId!=HappyDiaoChanComposition.Meihun||!HappyDcOriginalCandidate(f)||r.Stage is not(HappyDiaoChanStage.Target or HappyDiaoChanStage.MeihunSuit or HappyDiaoChanStage.ViewHand or HappyDiaoChanStage.GiveChildren)||r.Cost is not null||r.PindianId!=0)return false;
            if(r.Stage!=HappyDiaoChanStage.Target&&(!IsValidPlayerSeat(r.Target)||r.Target==f.OwnerSeat))return false;
            if(r.Stage is HappyDiaoChanStage.ViewHand or HappyDiaoChanStage.GiveChildren)
            {
                if(r.Suit==Suit.None||ProgramEventHistory<HappyDiaoChanDeclaredEvent>().Count(e=>e.FrameId==f.Id&&e.TargetSeat==r.Target&&e.Suit==r.Suit)!=1)return false;
                if(r.Stage==HappyDiaoChanStage.ViewHand&&(r.Viewed.Distinct().Count()!=r.Viewed.Count||ProgramEventHistory<ProgramPrivateHandViewedEvent>().Count(e=>e.FrameId==f.Id&&e.ViewerSeat==f.OwnerSeat&&e.SourceSeat==r.Target&&e.SkillId==f.SkillId&&e.Count==r.Viewed.Count)!=1))return false;
            }
        }
        else
        {
            if(r.Operation!=SkillProgramEffectOp.ResolveHappyDiaoChanHuoxin||f.SkillId!=HappyDiaoChanComposition.Huoxin||f.TriggerId is not null||f.ActivationId!="charm-contest"||
                ProgramEventHistory<ProgramSkillStartedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.ActivationId==f.ActivationId)!=1||
                r.Stage is HappyDiaoChanStage.Target or HappyDiaoChanStage.MeihunSuit or HappyDiaoChanStage.ViewHand)return false;
            if(r.Stage==HappyDiaoChanStage.Cost)return r.Cost is null&&r.PindianId==0&&r.Result is null;
            if(r.Cost is not{}cost||cost.From.OwnerSeat!=f.OwnerSeat||cost.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||r.CostBefore<0||r.CostAfter<=r.CostBefore||r.CostAfter>_movementSequence||r.CostBatch<=0||
                ProgramEventHistory<HappyDiaoChanCostPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not[var paid]||paid.BatchId!=r.CostBatch||paid.CardId!=cost.CardId||paid.From!=cost.From||paid.Sequence!=r.CostAfter||
                _cardMovements.Count(m=>m.Sequence==paid.Sequence&&m.CardId==paid.CardId&&m.CardKind==cost.CardKind&&m.From==paid.From&&m.To==paid.To&&m.Reason.Value==HappyDcDiscard)!=1)return false;
            if(r.Stage is not(HappyDiaoChanStage.CostChildren or HappyDiaoChanStage.Pair))
            {
                if(r.PindianId<=f.Id||!IsValidPlayerSeat(r.PairSource)||!IsValidPlayerSeat(r.PairOpponent)||r.PairSource==r.PairOpponent||
                    ProgramEventHistory<HappyDiaoChanPindianIssuedEvent>().Count(e=>e==new HappyDiaoChanPindianIssuedEvent(f.Id,r.PindianId,r.PairSource,r.PairOpponent))!=1)return false;
                if(r.Stage!=HappyDiaoChanStage.Pindian)
                {
                    if(r.Result is not{}result||result.SourceSeat!=r.PairSource||result.OpponentSeat!=r.PairOpponent||
                        _resolutionStack.Any(x=>x.Id==r.PindianId)||ProgramEventHistory<PindianResultDeterminedEvent>().Count(e=>e.FrameId==r.PindianId&&e.SkillId==f.SkillId&&e.Result==result)!=1||
                        f.PindianResultBindings.SingleOrDefault(b=>b.Name==HappyDcPindian) is not{}binding||binding.SourceSeat!=result.SourceSeat||binding.OpponentSeat!=result.OpponentSeat||binding.SourceRank!=result.SourceRank||binding.OpponentRank!=result.OpponentRank||binding.SourceWon!=result.SourceWon||
                        !r.Nonwinners.SequenceEqual(new[]{result.SourceSeat,result.OpponentSeat}.Where(s=>!result.WonBy(s)))||r.NonwinnerIndex<0||r.NonwinnerIndex>r.Nonwinners.Count||
                        ProgramEventHistory<HappyDiaoChanPindianReturnedEvent>().Count(e=>e==new HappyDiaoChanPindianReturnedEvent(f.Id,r.PindianId,result.SourceSeat,result.OpponentSeat,result.SourceRank,result.OpponentRank))!=1)return false;
                    if(r.Stage is HappyDiaoChanStage.Nonwinner or HappyDiaoChanStage.GiveChildren)
                    {
                        if(r.Suit==Suit.None||ProgramEventHistory<HappyDiaoChanDeclaredEvent>().Count(e=>e.FrameId==f.Id&&e.TargetSeat==-1&&e.Suit==r.Suit)!=1)return false;
                        if(r.Nonwinners.Take(r.NonwinnerIndex).Where((s,i)=>ProgramEventHistory<HappyDiaoChanNonwinnerChosenEvent>().Count(e=>e.FrameId==f.Id&&e.Index==i&&e.Seat==s&&e.Suit==r.Suit)!=1&&_players[s].IsAlive).Any())return false;
                    }
                }
            }
        }
        if(r.Stage==HappyDiaoChanStage.GiveChildren)
        {
            if(r.TransferOrdinal<1||r.Moves.Select(m=>m.CardId).Distinct().Count()!=r.Moves.Count||ProgramEventHistory<HappyDiaoChanTransferIssuedEvent>().Count(e=>e==new HappyDiaoChanTransferIssuedEvent(f.Id,r.TransferOrdinal,r.Target,f.OwnerSeat,r.Moves.Count,r.IsViewedTake,r.Before,r.After))!=1)return false;
            if(r.Operation==SkillProgramEffectOp.ResolveHappyDiaoChanHuoxin&&(r.NonwinnerIndex>=r.Nonwinners.Count||r.Target!=r.Nonwinners[r.NonwinnerIndex]||ProgramEventHistory<HappyDiaoChanNonwinnerChosenEvent>().Count(e=>e.FrameId==f.Id&&e.Index==r.NonwinnerIndex&&e.Seat==r.Target&&e.Suit==r.Suit&&e.Gave)!=1))return false;
            if(r.Moves.Any(m=>m.From.OwnerSeat!=r.Target||m.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||m.To!=CardLocation.Hand(f.OwnerSeat)||m.Sequence<=r.Before||m.Sequence>r.After||
                _cardMovements.Count(p=>p.CardId==m.CardId&&p.Sequence==m.Sequence&&p.From==m.From&&p.To==m.To&&p.Reason.Value==(r.IsViewedTake?HappyDcTake:HappyDcGive))!=1))return false;
            if(r.IsViewedTake&&(r.Moves.Count!=1||!r.Viewed.Contains(r.Moves[0].CardId)))return false;
        }
        return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,42220)]private void AssertHappyDc(ProgramSkillFrame f)
    {if(f.HappyDiaoChan is not null&&!ValidHappyDc(f))throw new InvalidOperationException("Happy Diao Chan lost the original binding, paid owned discard, native completed pair, private hand or transfer invoice.");}
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost,42220)]private bool HappyDcMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>
        f.HappyDiaoChan is{}r&&effect?.Op==r.Operation&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&
        r.Stage is HappyDiaoChanStage.CostChildren or HappyDiaoChanStage.PindianChildren or HappyDiaoChanStage.GiveChildren&&ValidHappyDc(f);
    private bool HasHappyDiaoChanPrompt()=>_resolutionStack.LastOrDefault() is ProgramSkillFrame{HappyDiaoChan:{}r}f&&
        r.Stage is HappyDiaoChanStage.Target or HappyDiaoChanStage.MeihunSuit or HappyDiaoChanStage.ViewHand or HappyDiaoChanStage.Cost or HappyDiaoChanStage.Pair or HappyDiaoChanStage.HuoxinSuit or HappyDiaoChanStage.Nonwinner&&
        _pendingDecision is{Kind:DecisionKind.ProgramTrigger}p&&ValidHappyDc(f)&&p.PlayerSeat==HappyDcChooser(f)&&AssistedChoicesEqual(p.Choices,HappyDcChoices(f));
    private bool IsHappyDiaoChanPindianSettlementBatch(ProgramSkillFrame f,CardMovementBatchContext batch)
    {
        if(f.HappyDiaoChan is not{Stage:HappyDiaoChanStage.PindianChildren,Result:{}result}r||!ValidHappyDc(f)||batch.ParentFrameId!=r.PindianId||batch.AwaitingProgramFrameId is not null||
            batch.TurnNumber!=r.ActualTurn||batch.OriginSkillId!=f.SkillId||batch.OriginSkillInstanceId!=f.SkillInstanceId||batch.OriginOwnerSeat!=f.OwnerSeat||batch.Movements.Count is<1 or>2)return false;
        return batch.Movements.All(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&_cardMovements.Count(x=>x==m)==1&&(m.CardId==result.SourceCardId||m.CardId==result.OpponentCardId)&&
            (m.To==CardLocation.Processing&&(m.Reason==CardMoveReasons.PindianReveal||m.Reason.Value=="pindian.reserve-top")||m.From==CardLocation.Processing&&(m.Reason==CardMoveReasons.PindianFinish||m.Reason.Value=="program.pindian.claim")));
    }
    private bool HappyDiaoChanStructuralEdge(ResolutionFrame child,ResolutionFrame parent)
    {
        if(parent is not ProgramSkillFrame{HappyDiaoChan:{}r}f||!ValidHappyDc(f))return false;
        if(child is PindianFrame p)return r.Stage==HappyDiaoChanStage.Pindian&&p.Id==r.PindianId&&p.ParentFrameId==f.Id&&p.SourceSeat==r.PairSource&&p.OpponentSeat==r.PairOpponent&&p.SkillId==f.SkillId&&p.ProgramResultBind==HappyDcPindian;
        if(child is CardsMovedTriggerWindowFrame moved)return moved.ResumeProgramFrameId==f.Id&&(IsHappyDiaoChanPindianSettlementBatch(f,moved.Batch)||ProgramMovementReturnMatches(f,moved));
        return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,42220)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,42220)]private bool KeepHappyDcPaid(ProgramSkillFrame f)=>f.HappyDiaoChan is{}r&&(r.Cost is not null||r.Stage==HappyDiaoChanStage.GiveChildren)&&ValidHappyDc(f);
}
