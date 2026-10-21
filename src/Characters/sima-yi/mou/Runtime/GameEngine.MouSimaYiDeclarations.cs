namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void IssueMouSmPlayedDeclaration(ProgramSkillFrame f)
    {
        if(!MouSimaYi.Valid(f)||f.MouSimaYi is not{Stage:MouSmStage.PlayedDeclaration,Played:{Source:{}source,Cards:[var cardId]}plan,Response:null,ResponseActionId:0}r||!MouSmPlayedPlans(f).Any(p=>SameMouSmPlayedPlan(p,plan))||ViewAsRule(source)?.DeclarationValidation is null||UnclaimedDeclarationPayment(r.Actor,cardId)is not null)throw new InvalidOperationException("Pingliao requires its original current declared red Basic choice.");
        if(!TryBeginCardDeclaration(_players[r.Actor],GetAdvancedCard(cardId),plan.Kind,source,DeclarationReturn(CardDeclarationPurpose.MouSimaYiPlayed,r.Actor,target:f.OwnerSeat,finalKind:plan.Kind),[]))throw new InvalidOperationException("Pingliao cannot bypass its required native declaration challenge.");
    }
    private bool MatchesMouSmPlayedDeclaration(ProgramSkillFrame f,CardDeclarationFrame d)
    {
        if(!MouSimaYi.Valid(f)||f.MouSimaYi is not{Stage:MouSmStage.PlayedDeclaration,Played:{Source:{}source,Cards:[var cardId]}plan,Response:null,ResponseActionId:0}r||d.OwnerSeat!=r.Actor||d.TurnNumber!=r.Turn||d.Return is not{Purpose:CardDeclarationPurpose.MouSimaYiPlayed,UseAction:null}ret||ret.ParentFrameId!=f.Id||ret.ParentKind!=f.Kind||ret.ParentStep!=f.Step||ret.ActorSeat!=r.Actor||ret.TargetSeat!=f.OwnerSeat||ret.FinalEffectiveKind!=plan.Kind||d.TargetSeats.Count!=0||d.Payment is not{Claimed:false}p||p.DeclarationId!=d.Id||p.OwnerFrameId!=d.Id||p.ProviderSeat!=r.Actor||p.ActorSeat!=r.Actor||p.Cost.CardId!=cardId||p.Cost.From!=CardLocation.Hand(r.Actor)||p.Source!=source||source.OwnerSeat!=r.Actor||ViewAsRule(source)is not{DeclarationValidation:not null}rule||rule.OutputKind!=p.DeclaredKind||!MatchesRequiredCard(p.DeclaredKind,plan.Kind)||(p.FrozenSuit is{}s?s is not(Suit.Heart or Suit.Diamond):p.Cost.EffectiveIsRed!=true))return false;
        return ProgramEventHistory<CardDeclarationCommittedEvent>().Count(e=>e.DeclarationId==d.Id&&e.OwnerSeat==r.Actor&&e.ActorSeat==r.Actor&&e.DeclaredKind==p.DeclaredKind&&e.TargetSeats.Count==0)==1&&_cardMovements.Count(m=>m.Sequence>r.Before&&m.CardId==cardId&&m.From==p.Cost.From&&m.To==CardLocation.Processing&&m.Reason.Value=="conversion.declaration.pay")==1;
    }
    private bool ReturnMouSmPlayedDeclaration(CardDeclarationFrame d,bool succeeded)
    {
        if(d.Return.Purpose!=CardDeclarationPurpose.MouSimaYiPlayed)return false;
        if(_resolutionStack.Any(x=>x.Id==d.Id)||_resolutionStack.LastOrDefault()is not ProgramSkillFrame f||f.Id!=d.Return.ParentFrameId||d.Stage!=CardDeclarationStage.Returning||!MatchesMouSmPlayedDeclaration(f,d)||ProgramEventHistory<MouSmDeclarationReturnedEvent>().Any(e=>e.DeclarationId==d.Id))throw new InvalidOperationException("Pingliao declaration returned to another or already completed native request.");
        if(succeeded)
        {
            if(d.Succeeded!=true||_cardZones.GetLocation(d.Payment.Cost.CardId)!=CardLocation.Processing||f.AcceptedDeclarationPayment is{Claimed:false})throw new InvalidOperationException("Pingliao cannot forge a passed declaration or repay an outstanding declaration entity.");
            ReplaceRuntimeFrame(f.Id,f=f with{AcceptedDeclarationPayment=d.Payment with{OwnerFrameId=f.Id}});
        }
        MouSimaYi.DeclarationReturned(f,d.Id,succeeded);return true;
    }
    private bool MouSmDeclarationEdge(int i)
    {
        if(i<1||_resolutionStack[i-1]is not CardDeclarationFrame d||_resolutionStack.SingleOrDefault(x=>x.Id==d.Return.ParentFrameId)is not ProgramSkillFrame f||!MatchesMouSmPlayedDeclaration(f,d))return false;
        if(_resolutionStack[i]is CardDeclarationChallengeFrame c)return d.Stage==CardDeclarationStage.Challenging&&c.ParentFrameId==d.Id&&c.SeatIndex>=0&&c.SeatIndex<c.EligibleSeats.Count&&c.EligibleSeats.Distinct().Count()==c.EligibleSeats.Count&&c.EligibleSeats.All(s=>IsValidPlayerSeat(s)&&s!=d.OwnerSeat);
        if(_resolutionStack[i]is CardsMovedTriggerWindowFrame m)return d.Stage is CardDeclarationStage.Paying or CardDeclarationStage.Cleaning&&d.ActiveChildFrameId==m.Id&&m.ResumeDeclarationFrameId==d.Id&&m.Batch.ParentFrameId==d.Id&&m.Batch.Movements is[var move]&&_cardMovements.Contains(move)&&move.CardId==d.Payment.Cost.CardId&&(d.Stage==CardDeclarationStage.Paying?move.From==d.Payment.Cost.From&&move.To==CardLocation.Processing&&move.Reason.Value=="conversion.declaration.pay":move.From==CardLocation.Processing&&move.To==CardLocation.DiscardPile&&move.Reason.Value is "conversion.declaration.failed" or "conversion.declaration.cancelled");
        return _resolutionStack[i]is ProgramLifecycleTriggerWindowFrame skills&&d.Stage==CardDeclarationStage.Granting&&skills.Window==SkillProgramTriggerWindow.SkillsChanged&&skills.ResumeProgramFrameId==d.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeCardDeclaration;
    }
}
