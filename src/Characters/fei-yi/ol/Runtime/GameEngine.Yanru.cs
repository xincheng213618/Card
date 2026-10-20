using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool FeiYiYanruAvailable(CharacterState actor)
    {
        var odd=GetHand(actor).Count%2!=0;
        return _winner==Winner.None&&actor.IsAlive&&actor.Seat==_currentSeat&&actor.Seat==_turnProgression.OwnerSeat&&_phase==TurnPhase.Play&&
            !ProgramEventHistory<FeiYiYanruPaidEvent>().Any(e=>e.Source.OwnerSeat==actor.Seat&&e.Turn==_turnNumber&&e.PhaseInstance==_cardUseDebitPhaseInstanceId&&e.Odd==odd);
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount,26600,SkillProgramEffectOp.YanruParityDrawDiscard)]
    private bool AllowsFeiYiYanru(CharacterState actor)=>FeiYiYanruAvailable(actor);
    private bool FeiYiCanDiscard(int owner,Card card)=>!card.IsGeneralWeapon&&!IsSelfHandCategoryDiscardForbidden(owner,card,CardLocation.Hand(owner),OwnedCardMoveIntent.Discard);
    private void PayFeiYiYanru(ProgramSkillFrame f)
    {
        var r=f.FeiYi!;
        if(r.Paid||!FeiYiSourceCurrent(f)||!FeiYiYanruAvailable(_players[f.OwnerSeat])||r.Odd!=(GetHand(_players[f.OwnerSeat]).Count%2!=0)||r.PhaseInstance!=_cardUseDebitPhaseInstanceId)
            throw new InvalidOperationException("Yanru must revalidate and consume exactly its accepted parity in this actual Play.");
        ReplaceRuntimeTop(f with{FeiYi=r with{Paid=true}});
        AdvanceEventRulesAndQueueFact(new FeiYiYanruPaidEvent(f.Id,r.Source,r.GameplayHash,r.Turn,r.TurnOwner,r.PhaseInstance,r.Odd));
    }
    private void BeginFeiYiDiscard(ProgramSkillFrame f)
    {
        var r=f.FeiYi!;if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None){CompleteFeiYi(f,r.Paid);return;}
        var hand=GetHand(_players[f.OwnerSeat]);var candidates=hand.Where(c=>FeiYiCanDiscard(f.OwnerSeat,c)).Select(c=>c.Id).ToArray();
        var minimum=hand.Count/2;
        ReplaceRuntimeTop(f=f with{FeiYi=r with{Stage=FeiYiStage.DiscardChoice,Minimum=minimum,HandCount=hand.Count,Candidates=candidates,Selected=[]}});
        // Native forced discard exhausts the legal set if fewer than the required
        // count are legally discardable; its fact still records the true bound.
        if(candidates.Length<minimum){ReplaceRuntimeTop(f=f with{FeiYi=f.FeiYi! with{Selected=candidates}});PayFeiYiDiscard(f);return;}
        PublishFeiYiChoice(f);
    }
    private void PayFeiYiDiscard(ProgramSkillFrame f)
    {
        var r=f.FeiYi!;if(!r.Paid){PayFeiYiYanru(f);f=GetActiveProgramFrame(f.Id);r=f.FeiYi!;}
        if(GetHand(_players[f.OwnerSeat]).Count!=r.HandCount||r.Selected.Distinct().Count()!=r.Selected.Count||r.Selected.Any(id=>!r.Candidates.Contains(id)||!_cardZones.Contains(CardLocation.Hand(f.OwnerSeat),id)||!FeiYiCanDiscard(f.OwnerSeat,GetAdvancedCard(id)))||r.Selected.Count<Math.Min(r.Minimum,r.Candidates.Count))throw new InvalidOperationException("Yanru lost its genuine minimum-half hand discard.");
        var before=_movementSequence;ReplaceRuntimeTop(f=f with{FeiYi=r with{Stage=FeiYiStage.DiscardChildren,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        if(r.Selected.Count>0)MoveProgramLogicalDiscard(f,f.OwnerSeat,r.Selected,new(FeiYiDiscardReason));
        RecordFeiYiMovement(f.Id,before,FeiYiStage.DiscardChildren,r.Minimum);ContinueFeiYi(f.Id);
    }
}
