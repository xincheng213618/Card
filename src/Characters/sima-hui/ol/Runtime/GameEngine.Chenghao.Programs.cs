using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string SimaHuiViewReason="program.sima-hui.chenghao.view",SimaHuiGiftReason="program.sima-hui.chenghao.give";
    private SkillProgramStepOutcome BeginSimaHuiDistribution(ProgramSkillFrame f)
    {
        if(!TrySimaHuiChainOrigin(f.OwnerSeat,GetProgramTrigger(f),f.WindowContext!,out var proof))throw new InvalidOperationException("Chenghao requires actual applied elemental chain-origin damage.");
        var r=NewSimaHuiReceipt(f,"view-children") with{ChainOrigin=proof,MovementBefore=_movementSequence,MovementAfter=_movementSequence};ReplaceRuntimeTop(f=f with{SimaHui=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var cards=new List<int>();for(var i=0;i<=proof.LivingChainedCount;i++)if(DrawOneToProcessing(new(SimaHuiViewReason)) is{}card)cards.Add(card.Id);else break;
        ReplaceRuntimeTop(f=f with{SimaHui=r with{OriginalCards=cards,MovementAfter=_movementSequence}});AdvanceEventRulesAndQueueFact(new SimaHuiTopViewedEvent(f.Id,r.Source,r.GameplayHash,proof,f.SimaHui!.OriginalCards,r.MovementBefore,_movementSequence));AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private void IssueSimaHuiGift(ProgramSkillFrame f,int recipient)
    {
        var r=f.SimaHui!;var id=r.OriginalCards[r.Cursor];if(!_players[recipient].IsAlive || _cardZones.GetLocation(id)!=CardLocation.Processing)throw new InvalidOperationException("Chenghao lost its original privately viewed card.");
        ReplaceRuntimeTop(f=f with{SimaHui=r with{Stage="gift-children",ChosenHolder=recipient,MovementBefore=_movementSequence,MovementAfter=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        MoveCard(_cardZones.CardsAt(CardLocation.Processing).Single(c=>c.Id==id), CardLocation.Processing, CardLocation.Hand(recipient), new(SimaHuiGiftReason), NativeGiveCompletion(f.Id,f.OwnerSeat,recipient,[id],_movementSequence,null));
        ReplaceRuntimeTop(f=f with{SimaHui=f.SimaHui! with{MovementAfter=_movementSequence}});AdvanceEventRulesAndQueueFact(new SimaHuiCardDistributedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.Cursor,id,recipient,f.SimaHui!.MovementBefore,_movementSequence));AdvanceRuntimeProgram(f.Id);
    }
    private bool ReturnSimaHuiUnassignedCards(ProgramSkillFrame f)
    {
        var cards=f.SimaHui!.OriginalCards.Where(id=>_cardZones.GetLocation(id)==CardLocation.Processing).Select(id=>_cardZones.CardsAt(CardLocation.Processing).Single(c=>c.Id==id)).ToArray();
        if(cards.Length==0)return false;var r=f.SimaHui!;var before=_movementSequence;ReplaceRuntimeTop(f=f with{SimaHui=r with{Stage="return-children",MovementBefore=before,MovementAfter=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        MoveCards(cards,CardLocation.Processing,CardLocation.DrawPile,new("program.sima-hui.chenghao.return"));_cardZones.PlaceDrawPileCardsAtTop(cards.Select(c=>c.Id).ToArray());
        ReplaceRuntimeTop(f=f with{SimaHui=f.SimaHui! with{MovementAfter=_movementSequence}});AdvanceEventRulesAndQueueFact(new SimaHuiUnassignedReturnedEvent(f.Id,r.Source,r.GameplayHash,Array.AsReadOnly(cards.Select(c=>c.Id).ToArray()),before,_movementSequence));AdvanceRuntimeProgram(f.Id);return true;
    }
}
