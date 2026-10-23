namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static HappyZuoCiFace HappyZcFace(Card card)=>new(card.Kind,card.Suit,card.Rank,card.PrintedName);
    private (int Owner,HappyZuoCiState State,HappyZuoCiEntity Entity)? HappyZcEntity(int id)
    {
        if(id<=0||!_generatedPhysicalCardIds.Contains(id))return null;
        foreach(var p in _players)
            if(p.HappyZuoCi is{}state&&state.Entities.SingleOrDefault(e=>e.CardId==id) is{}entity)return(p.Seat,state,entity);
        return null;
    }
    private string? HappyZuoCiPhantomCardLabel(int cardId)=>HappyZcEntity(cardId) is null?null:"幻术";
    private bool IsHappyZuoCiHandLimitExempt(CharacterState owner,int cardId)=>HappyZcEntity(cardId) is{ } found&&found.Owner==owner.Seat;
    private bool HappyZcSourceProof(int owner,HappyZuoCiState state)=>IsValidPlayerSeat(owner)&&state.Source is{SkillId:HappyZuoCiComposition.Huanshu,BindingId:"initial-phantoms"}&&
        state.Source.OwnerSeat==owner&&state.InitialFrameId>0&&state.Deck==new CardLocation(CardZoneKind.PrivatePersistentPile,owner,$"happy-huanshu:{state.Source.SkillInstanceId}")&&
        _contentRegistry.GetSkill(state.Source.SkillId).Program?.GameplayHash==state.GameplayHash&&
        ProgramEventHistory<HappyZuoCiPoolInitializedEvent>().Count(e=>e.FrameId==state.InitialFrameId&&e.Source==state.Source&&e.GameplayHash==state.GameplayHash)==1;
    private CardLocation NormalizeHappyZuoCiPhantomDestination(Card card,CardLocation from,CardLocation to)
    {
        if(from==to||HappyZcEntity(card.Id) is not{}found)return to;
        if(!HappyZcSourceProof(found.Owner,found.State))throw new InvalidOperationException("A phantom lost its unique original issued deck.");
        if(from==CardLocation.OutsideGame)return to==found.State.Deck?to:CardLocation.OutsideGame;
        if(to==CardLocation.OutsideGame||to.Zone==CardZoneKind.DiscardPile)return CardLocation.OutsideGame;
        // A foreign Hand must really receive the entity before the after-event
        // destruction. This preserves actual gain and the original give invoice.
        if(to.OwnerSeat==found.Owner||to.Zone is CardZoneKind.Hand or CardZoneKind.DrawPile or CardZoneKind.Processing or CardZoneKind.Judgment or
            CardZoneKind.BuquWound or CardZoneKind.Authority or CardZoneKind.Chunlao or CardZoneKind.PojunHold or CardZoneKind.PrivateReserve or
            CardZoneKind.PublicDeferredPile or CardZoneKind.PublicPersistentPile or CardZoneKind.PrivateTurnHold or CardZoneKind.RoundHold or CardZoneKind.PrivatePersistentPile)return to;
        return CardLocation.OutsideGame;
    }
    private HappyZuoCiPhantomDestinationPlan? PlanHappyZuoCiPhantomDestination(Card card,CardLocation from,CardLocation requestedTo,CardMoveReason reason)
    {
        if(HappyZcEntity(card.Id) is not{}found)return null;
        var actual=NormalizeHappyZuoCiPhantomDestination(card,from,requestedTo);
        if(actual==requestedTo)return null;
        if(found.Entity.Faces.Count==0||found.Entity.Faces[^1]!=HappyZcFace(card)||_cardZones.GetLocation(card.Id)!=from)
            throw new InvalidOperationException("A phantom destination plan requires its exact live physical face and source.");
        var gain=reason.Value=="skill-program.happy:huanshu.foreign-destroy"?_cardMovements.LastOrDefault(m=>m.CardId==card.Id&&m.To==from):null;
        if(reason.Value=="skill-program.happy:huanshu.foreign-destroy"&&(gain is null||from.Zone!=CardZoneKind.Hand||from.OwnerSeat==found.Owner||gain.Sequence!=_movementSequence))
            throw new InvalidOperationException("Foreign phantom destruction must immediately follow the real receiving movement.");
        return new(card.Id,found.Owner,found.State.InitialFrameId,found.State.Source,found.State.GameplayHash,found.Entity.Faces.Count-1,
            HappyZcFace(card),from,requestedTo,actual,reason,_movementSequence,CompleteProgramEventHistory().Count(),gain?.Sequence,gain?.To.OwnerSeat);
    }
    private bool HappyZcPlanProof(HappyZuoCiPhantomDestinationPlan plan)=>HappyZcEntity(plan.CardId) is{}found&&found.Owner==plan.Issuer&&
        HappyZcSourceProof(found.Owner,found.State)&&plan.InitialFrameId==found.State.InitialFrameId&&plan.Source==found.State.Source&&plan.GameplayHash==found.State.GameplayHash&&
        plan.ActualTo==CardLocation.OutsideGame&&plan.RequestedTo!=plan.ActualTo&&plan.FaceVersion>=0&&plan.FaceVersion<found.Entity.Faces.Count&&found.Entity.Faces[plan.FaceVersion]==plan.Face&&
        plan.SequenceBefore>=0&&plan.EventCountBefore>=0&&CompleteProgramEventHistory().Take(plan.EventCountBefore).OfType<HappyZuoCiPoolInitializedEvent>().Count(e=>e.FrameId==plan.InitialFrameId&&e.Source==plan.Source&&e.GameplayHash==plan.GameplayHash)==1&&
        (plan.OriginalForeignGainSequence is null?plan.OriginalRecipient is null:plan.OriginalRecipient is{}recipient&&recipient!=plan.Issuer&&plan.From==CardLocation.Hand(recipient)&&
            plan.Reason.Value=="skill-program.happy:huanshu.foreign-destroy"&&_cardMovements.Count(m=>m.Sequence==plan.OriginalForeignGainSequence&&m.CardId==plan.CardId&&m.To==plan.From)==1);
    private void ValidateHappyZuoCiPhantomMovement(CardMovementRecord movement)
    {
        if(movement.HappyZuoCiPhantomDestination is{}plan&&(!HappyZcPlanProof(plan)||movement.CardId!=plan.CardId||movement.CardKind!=plan.Face.Kind||
            movement.From!=plan.From||movement.To!=plan.ActualTo||movement.Reason!=plan.Reason||movement.Sequence<=plan.SequenceBefore||
            _cardMovements.Count(m=>m.Sequence==movement.Sequence&&m==movement)!=1))
            throw new InvalidOperationException("A phantom movement lost its frozen original recipient, physical face or historical issuer.");
    }
    private bool IsHappyZuoCiDestroyedMaterial(CardMovementRecord m)=>m.To==CardLocation.OutsideGame&&m.HappyZuoCiPhantomDestination is{}plan&&
        HappyZcPlanProof(plan)&&m.From==plan.From&&m.CardId==plan.CardId&&m.CardKind==plan.Face.Kind&&m.Reason==plan.Reason&&m.Sequence>plan.SequenceBefore&&
        _cardMovements.Count(actual=>actual.Sequence==m.Sequence&&actual==m)==1;
    private bool IsHappyZuoCiDestroyedMaterial(int cardId,CardLocation from,CardLocation to)=>to==CardLocation.OutsideGame&&
        _cardMovements.Any(m=>m.CardId==cardId&&m.To==to&&IsHappyZuoCiDestroyedMaterial(m)&&(m.From==from||m.HappyZuoCiPhantomDestination?.OriginalForeignGainSequence is{}sequence&&
            _cardMovements.Any(g=>g.Sequence==sequence&&g.CardId==cardId&&g.From==from)));
    private bool IsHappyZuoCiDestroyedMaterial(long useId,int cardId)=>LifecycleCardUse(useId) is{Action:{}action}&&action.PhysicalCards.SingleOrDefault(c=>c.CardId==cardId) is{}material&&
        _cardMovements.Any(m=>m.CardId==cardId&&m.CardKind==material.CardKind&&m.From==CardLocation.Processing&&IsHappyZuoCiDestroyedMaterial(m)&&
            m.HappyZuoCiPhantomDestination!.SequenceBefore>=_cardMovements.Where(p=>p.CardId==cardId&&p.From==material.From&&p.To==CardLocation.Processing).Select(p=>(long)p.Sequence).DefaultIfEmpty(long.MaxValue).Max());
    private IEnumerable<ProgramTriggerCandidate> CollectHappyZuoCiPhantomRewardCandidates(CardMovementBatchContext batch)
    {
        for(var i=0;i<batch.Movements.Count;i++)
            if(HappyZcForeignGain(batch.Movements[i]) is{}owner)
                foreach(var candidate in CollectProgramTriggerCandidates(_players[owner],SkillProgramTriggerWindow.CardsMoved).Where(c=>c.SkillId==HappyZuoCiComposition.Huanshu&&c.BindingId=="foreign-phantom"))
                    yield return candidate with{OccurrenceIndex=i};
    }
    private int? HappyZcForeignGain(CardMovementRecord m)=>IsHappyZuoCiDestroyedMaterial(m)&&m.HappyZuoCiPhantomDestination is{OriginalForeignGainSequence:not null,OriginalRecipient:{}recipient,Issuer:var owner}&&recipient!=owner?owner:null;
    private bool IsHappyZuoCiForeignRewardCandidate(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(candidate.SkillId!=HappyZuoCiComposition.Huanshu||candidate.BindingId!="foreign-phantom"||context.Window!=SkillProgramTriggerWindow.CardsMoved||
            context.OwnerSeat!=candidate.OwnerSeat||context.TargetSeat!=candidate.OwnerSeat||context.OccurrenceIndex!=candidate.OccurrenceIndex||
            context.MovementBatch is not{}batch||context.MovementIndex!=candidate.OccurrenceIndex||candidate.OccurrenceIndex<0||candidate.OccurrenceIndex>=batch.Movements.Count||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault() is not{}window||window.Id!=context.ParentFrameId||window.Batch!=batch||
            window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||window.Candidates[window.CandidateIndex]!=candidate||
            window.Contexts is not{}contexts||window.CandidateIndex>=contexts.Count||contexts[window.CandidateIndex]!=context)return false;
        var movement=batch.Movements[candidate.OccurrenceIndex];
        return HappyZcForeignGain(movement)==candidate.OwnerSeat&&context.SourceSeat==movement.HappyZuoCiPhantomDestination!.OriginalRecipient&&
            movement.HappyZuoCiPhantomDestination.OriginalForeignGainSequence is{}sequence&&sequence<movement.Sequence&&
            _cardMovements.Count(gain=>gain.Sequence==sequence&&gain.CardId==movement.CardId&&gain.To==CardLocation.Hand(context.SourceSeat!.Value))==1;
    }
    private ProgramSkillWindowContext CreateHappyZuoCiPhantomRewardContext(CardsMovedTriggerWindowFrame frame,ProgramTriggerCandidate candidate)
    {
        if(candidate.OccurrenceIndex<0||candidate.OccurrenceIndex>=frame.Batch.Movements.Count||HappyZcForeignGain(frame.Batch.Movements[candidate.OccurrenceIndex])!=candidate.OwnerSeat)
            throw new InvalidOperationException("A phantom reward requires its actual destroyed foreign-hand request.");
        if(frame.Contexts is{}contexts)
        {
            if(frame.CandidateIndex<0||frame.CandidateIndex>=frame.Candidates.Count||frame.CandidateIndex>=contexts.Count||frame.Candidates[frame.CandidateIndex]!=candidate||
                contexts[frame.CandidateIndex] is not{}frozen||frozen.Window!=SkillProgramTriggerWindow.CardsMoved||frozen.ParentFrameId!=frame.Id||frozen.MovementBatch!=frame.Batch||
                frozen.OwnerSeat!=candidate.OwnerSeat||frozen.TargetSeat!=candidate.OwnerSeat||frozen.MovementIndex!=candidate.OccurrenceIndex||frozen.OccurrenceIndex!=candidate.OccurrenceIndex||
                frozen.SourceSeat!=frame.Batch.Movements[candidate.OccurrenceIndex].HappyZuoCiPhantomDestination!.OriginalRecipient)
                throw new InvalidOperationException("A phantom reward lost its original frozen current-candidate context.");
            return frozen;
        }
        var movement=frame.Batch.Movements[candidate.OccurrenceIndex];
        return new(SkillProgramTriggerWindow.CardsMoved,frame.Id,candidate.OwnerSeat,SourceSeat:movement.HappyZuoCiPhantomDestination!.OriginalRecipient,
            TargetSeat:candidate.OwnerSeat,OccurrenceIndex:candidate.OccurrenceIndex,Facts:CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]),MovementBatch:frame.Batch,MovementIndex:candidate.OccurrenceIndex);
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent,42600)]
    private void CaptureHappyZuoCiNewPrototype(CardMovedEvent fact)
    {
        if(fact.From!=CardLocation.OutsideGame||fact.To.OwnerSeat is null||!_players.Any(p=>p.IsAlive&&p.HappyZuoCi is not null)||HappyZcEntity(fact.CardId) is not null)return;
        var face=HappyZcFace(GetAdvancedCard(fact.CardId));
        foreach(var p in _players)
            if(p.HappyZuoCi is{}state&&p.IsAlive&&!state.Entities.Any(e=>e.PrototypeId==fact.CardId))
                CreateHappyZcPoolCards(p.Seat,_resolutionStack.LastOrDefault()?.Id??p.HappyZuoCi.InitialFrameId,[new(fact.CardId,face)]);
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent,42601)]
    private void DestroyForeignHappyZuoCiPhantom(CardMovedEvent fact)
    {
        if(fact.To is not{Zone:CardZoneKind.Hand,OwnerSeat:{}recipient}||HappyZcEntity(fact.CardId) is not{}found||recipient==found.Owner)return;
        if(_cardZones.GetLocation(fact.CardId)!=fact.To||_cardMovements.LastOrDefault() is not{}movement||movement.CardId!=fact.CardId||movement.From!=fact.From||movement.To!=fact.To||movement.Reason!=fact.Reason)
            throw new InvalidOperationException("A foreign phantom must be destroyed immediately after its actual hand gain.");
        MoveCard(GetAdvancedCard(fact.CardId),fact.To,CardLocation.DiscardPile,new("skill-program.happy:huanshu.foreign-destroy"));
    }
    private void CreateHappyZcPoolCards(ProgramSkillFrame f,IReadOnlyList<HappyZuoCiPrototype> prototypes)=>CreateHappyZcPoolCards(f.OwnerSeat,f.Id,prototypes);
    private void CreateHappyZcPoolCards(int ownerSeat,long creationFrameId,IReadOnlyList<HappyZuoCiPrototype> prototypes)
    {
        var owner=_players[ownerSeat];var state=owner.HappyZuoCi!;var cards=new List<Card>();var entities=state.Entities.ToList();
        var next=checked(CreateCardZoneDiagnostics().Select(c=>c.CardId).DefaultIfEmpty(0).Max()+1);
        foreach(var p in prototypes)
        {
            var card=new Card(next++,p.Face.Kind,p.Face.Suit,p.Face.Rank){PrintedName=p.Face.PrintedName,GrantedSkillIds=Array.AsReadOnly(Array.Empty<string>())};
            _cardZones.AddGeneratedCard(card);_generatedPhysicalCardIds.Add(card.Id);cards.Add(card);
            entities.Add(new HappyZuoCiEntity(card.Id,p.PhysicalId,creationFrameId){Faces=[p.Face]});
        }
        owner.HappyZuoCi=state with{Entities=entities};
        if(cards.Count>0){MoveCards(cards,CardLocation.OutsideGame,state.Deck,new("skill-program.happy:huanshu.pool-create"));_cardZones.Shuffle(state.Deck,_random);}
    }
    private void ChangeHappyZcFace(CharacterState owner,Card original,HappyZuoCiFace face,bool manual)
    {
        var state=owner.HappyZuoCi??throw new InvalidOperationException("A phantom face needs its original deck.");
        var entity=state.Entities.Single(e=>e.CardId==original.Id);
        if(_cardZones.GetLocation(original.Id)!=CardLocation.Hand(owner.Seat)||entity.Faces.Count==0||entity.Faces[^1]!=HappyZcFace(original)||manual&&entity.ManuallyTransformed)
            throw new InvalidOperationException("Only the exact remaining own hand phantom can change face once manually.");
        _cardZones.ReplaceGeneratedCardFace(original,original with{Kind=face.Kind,Suit=face.Suit,Rank=face.Rank,PrintedName=face.PrintedName});
        owner.HappyZuoCi=state with{Entities=state.Entities.Select(e=>e.CardId==original.Id?e with{Faces=e.Faces.Append(face).ToArray(),ManuallyTransformed=e.ManuallyTransformed||manual}:e).ToArray()};
    }
}
