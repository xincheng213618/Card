namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidQinLang(ProgramSkillFrame f)
    {
        if(f.QinLangXianying is not{}r||!HasQinLangCapability||f.SkillId!="ol:xianying"||f.TriggerId is null||f.ActivationId!=f.TriggerId||r.Source!=QinLangSource(f)||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=f.InstructionIndex||f.InstructionIndex!=1||
            r.ActualTurn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||r.RoundNumber!=_roundNumber||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.PendingMovementContinuation is not null||!Enum.IsDefined(r.Stage)||
            r.Before<0||r.After<r.Before||r.After>_movementSequence||r.ActualDrawCount is<0 or>2||r.Cards.Select(c=>c.CardId).Distinct().Count()!=r.Cards.Count||r.Selected.Select(c=>c.CardId).Distinct().Count()!=r.Selected.Count||r.Selected.Any(c=>!r.Cards.Contains(c))||
            ProgramEventHistory<QinLangXianyingStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var started]||started!=new QinLangXianyingStartedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,r.RoundNumber,r.Grants.Count>0))return false;
        var ending=r.Grants.Count>0;
        if(ending)
        {
            if(f.TriggerId is not("turn-end-own"or"turn-end-foreign")||f.WindowContext is not{Window:SkillProgramTriggerWindow.AfterTurnEnded}context||
               _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p=>p.Id==context.ParentFrameId)is not{}parent||!IsActualAfterTurnEndedParent(parent)||
               parent.AfterTurnEnded?.CurrentChild?.FrameId!=f.Id||!MountObserverCandidateMatches(f,AfterTurnEndedCandidate(parent))||context.SourceSeat!=parent.OwnerSeat||context.TargetSeat!=parent.OwnerSeat||
               context.Facts!=parent.AfterTurnEnded.Items[parent.AfterTurnEnded.ItemIndex].Facts||
               r.Grants.Select(g=>g.FrameId).Distinct().Count()!=r.Grants.Count||r.Grants.Any(g=>!SameQinLangSource(g.Source,r.Source)||g.ActualTurn!=r.ActualTurn||g.TurnOwner!=r.TurnOwner||g.GameplayHash!=r.GameplayHash||
                   ProgramEventHistory<QinLangXianyingGrantedEvent>().Count(e=>e==g)!=1)||r.Stage is not(QinLangXianyingStage.EndChoice or QinLangXianyingStage.NativeUse or QinLangXianyingStage.NativeChildren))return false;
        }
        else
        {
            if(f.TriggerId is not("preparation"or"after-damage")||f.WindowContext?.Window!=(f.TriggerId=="preparation"?SkillProgramTriggerWindow.TurnStartBeforeNormalFlow:SkillProgramTriggerWindow.AfterDamageApplied)||!r.Paid||
                ProgramEventHistory<QinLangXianyingDrawnEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var drawn]||drawn.OwnerSeat!=f.OwnerSeat||drawn.Count!=r.ActualDrawCount||drawn.Before<0||drawn.After<drawn.Before||drawn.After>_movementSequence||
                _cardMovements.Count(m=>m.Sequence>drawn.Before&&m.Sequence<=drawn.After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==QinLangDrawReason)!=drawn.Count)return false;
            var paid=ProgramEventHistory<QinLangXianyingDiscardPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray();var entities=ProgramEventHistory<QinLangXianyingEntityDiscardedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
            if(r.Stage==QinLangXianyingStage.DiscardChildren)
            {
                if(paid is not[var invoice]||invoice!=new QinLangXianyingDiscardPaidEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.RoundNumber,r.Selected.Count,r.Before,r.After)||
                    ProgramEventHistory<QinLangXianyingDiscardPaidEvent>().Count(e=>e.Source.OwnerSeat==f.OwnerSeat&&e.RoundNumber==r.RoundNumber&&e.Count==r.Selected.Count)!=1||entities.Length!=r.Selected.Count||
                    r.Selected.Any(c=>c.EffectiveName is null||entities.Count(e=>e.CardId==c.CardId&&e.Kind==c.Kind&&e.From==c.From&&e.Name==c.EffectiveName)!=1)||entities.Any(e=>!_cardMovements.Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.CardKind==e.Kind&&m.From==e.From&&IsQinLangDiscardMovement(r,m)))||
                    _cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After).Any(m=>!IsQinLangDiscardMovement(r,m)))return false;
            }
            else if(paid.Length!=0||entities.Length!=0||r.Stage is not(QinLangXianyingStage.DrawChildren or QinLangXianyingStage.DiscardChoice))return false;
        }
        var issued=ProgramEventHistory<QinLangXianyingUseIssuedEvent>().Where(e=>e.Origin.ProgramFrameId==f.Id).ToArray();var returned=ProgramEventHistory<QinLangXianyingUseReturnedEvent>().Where(e=>e.Origin.ProgramFrameId==f.Id).ToArray();
        if(issued.Select(e=>e.Origin.CardUseFrameId).Distinct().Count()!=issued.Length||issued.Select(e=>e.Origin.GrantFrameId).Distinct().Count()!=issued.Length||
            issued.Any(e=>e.Origin.Source!=r.Source||e.Origin.GameplayHash!=r.GameplayHash||e.Origin.ActorSeat!=f.OwnerSeat||e.Origin.ActualTurn!=r.ActualTurn||!r.Grants.Any(g=>g.FrameId==e.Origin.GrantFrameId&&g.Name==e.Origin.CardKind)||
                ProgramEventHistory<QinLangXianyingGrantConsumedEvent>().Count(c=>c.ProgramFrameId==f.Id&&c.GrantFrameId==e.Origin.GrantFrameId&&c.UseIssued)!=1)||
            returned.Select(e=>e.Origin.CardUseFrameId).Distinct().Count()!=returned.Length||returned.Any(e=>issued.Count(i=>i.Origin==e.Origin)!=1))return false;
        if(r.Stage==QinLangXianyingStage.NativeUse)return r.Use is{}current&&issued.Count(e=>e.Origin==current)==1&&returned.All(e=>e.Origin!=current)&&issued.Length==returned.Length+1;
        if(r.Stage==QinLangXianyingStage.NativeChildren)return r.Use is{}completed&&returned.Count(e=>e.Origin==completed)==1&&issued.Length==returned.Length;
        return r.Use is null&&issued.Length==returned.Length;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27170)]private void AssertQinLang(ProgramSkillFrame f)
    {if(f.QinLangXianying is not null&&!ValidQinLang(f))throw new InvalidOperationException("Xianying lost its actual turn, source generation, frozen payment, distinct round count or typed native use return.");}
}
