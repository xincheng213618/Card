namespace CardGame.Core;
public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost:IPublicPileColorHost
    {public SkillProgramStepOutcome ExecutePublicPileColor(SkillProgramEffect e,ProgramSkillFrame f)=>engine.ExecutePublicPileColor(e,f);}
    private IEnumerable<PublicPersistentPileSource> SameGrantColorPiles(int seat,string sourceSkill,string consumerInstance)
    {
        var consumer=_players[seat].SkillGrants.Grants.FirstOrDefault(g=>g.SkillInstanceId==consumerInstance);
        return ReferencedPublicPileSources(seat,sourceSkill,consumerInstance).Where(s=>s.SkillInstanceId==consumerInstance || consumer is not null && _players[seat].SkillGrants.Grants.Any(g=>g.SkillId==s.SkillId&&g.SkillInstanceId==s.SkillInstanceId&&g.SourceId==consumer.SourceId)).Where(s=>PublicPileCards(s).Count==1);
    }
    private static bool? SuitColor(Suit? suit)=>suit is Suit.Heart or Suit.Diamond ? true : suit is Suit.Club or Suit.Spade ? false : null;
    private bool? PileColor(PublicPersistentPileSource s)=>SuitColor(EffectiveSuit(_players[s.OwnerSeat],PublicPileCards(s).Single()));
    private bool CanActivatePublicPileColor(CharacterState owner,string skill,ProgramInstructionFeatures features)
    {
        if(!features.HasOperation(SkillProgramEffectOp.PublicPileColorDamage))return true;
        if(GetProgramConversionPolarity(owner.Seat,skill)!=SkillPolarity.Yang)return false;
        return GetSkillBindingShard(owner).ProgramInstances.Where(i=>i.SkillId==skill).Any(i=>ColorDamageChoices(0,owner.Seat,skill,i.SkillInstanceId,features.First(SkillProgramEffectOp.PublicPileColorDamage)!).Count>0);
    }
    private IReadOnlyList<PromptChoice> ColorDamageChoices(long frameId,int owner,string skill,string instance,SkillProgramEffect e)
    {
        var result=new List<PromptChoice>();
        foreach(var pile in SameGrantColorPiles(owner,e.SkillIds.Single(),instance))
        foreach(var card in GetHand(_players[owner]).Concat(GetEquipment(_players[owner])).Where(c=>SuitColor(EffectiveSuit(_players[owner],c)) is {} color&&color==PileColor(pile)&&!IsActiveProgramSourceEquipmentCard(owner,skill,instance,c)))
        foreach(var target in GetProgramTargetSeats(owner,SkillProgramTargetKind.OtherLivingInAttackRange))
            result.Add(new(new($"public-pile-color.frame-{frameId}.pile-{PublicPileIdentity(pile.SkillId,pile.SkillInstanceId)}.card-{card.Id}.target-{target}"),"弃置【"+PublicPileCardLabel(card)+"】，对 "+_players[target].Name+" 造成1点伤害。",[card.Id],[target],new Dictionary<string,string>{["program-action"]="public-pile-color",["frame-id"]=frameId.ToString(),["pile-instance"]=pile.SkillInstanceId}));
        return result;
    }
    private ActionCardsDiscardedEvent? ColorActionFact(ProgramSkillWindowContext c)=>c.MovementBatch is {} b?CompleteProgramEventHistory().OfType<ActionCardsDiscardedEvent>().LastOrDefault(e=>e.BatchId==b.Id):null;
    private bool CanOfferPublicPileColor(CharacterState owner,ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
    {
        if(!trigger.Effects.Any(e=>e.Op==SkillProgramEffectOp.RewardDiscardedActionColor))return true;
        var e=trigger.Effects.Single(x=>x.Op==SkillProgramEffectOp.RewardDiscardedActionColor);
        var fact=ColorActionFact(context);
        return fact is {IsRed:{}} && fact.ActorSeat==owner.Seat && fact.TurnNumber==_turnNumber && owner.Seat!=_currentSeat && GetProgramConversionPolarity(owner.Seat,candidate.SkillId)==SkillPolarity.Yin && SameGrantColorPiles(owner.Seat,e.SkillIds.Single(),candidate.SkillInstanceId).Any(p=>PileColor(p)==fact.IsRed) && !CompleteProgramEventHistory().OfType<ActionColorRewardOfferedEvent>().Any(x=>x.OwnerSeat==owner.Seat&&x.SkillId==candidate.SkillId&&x.ActionId==fact.ActionId&&(x.ParentFrameId!=context.ParentFrameId||x.SkillInstanceId!=candidate.SkillInstanceId));
    }
    private void RecordPublicPileColorOffer(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(GetProgramTrigger(candidate).Effects.Any(e=>e.Op==SkillProgramEffectOp.RewardDiscardedActionColor) && ColorActionFact(context) is {} fact && !CompleteProgramEventHistory().OfType<ActionColorRewardOfferedEvent>().Any(e=>e.OwnerSeat==candidate.OwnerSeat&&e.SkillId==candidate.SkillId&&e.ActionId==fact.ActionId))
            AdvanceEventRulesAndQueueFact(new ActionColorRewardOfferedEvent(candidate.OwnerSeat,candidate.SkillId,candidate.SkillInstanceId,context.ParentFrameId,fact.ActionId));
    }
    private SkillProgramStepOutcome ExecutePublicPileColor(SkillProgramEffect e,ProgramSkillFrame frame)
    {
        if(!_players[frame.OwnerSeat].IsAlive)return SkillProgramStepOutcome.Continue;
        if(e.Op==SkillProgramEffectOp.StoreBoundHandInPublicPile)
        {
            var cards=GetProgramCardSet(frame,e.SourceBind!);if(cards.CardIds.Count==0)return SkillProgramStepOutcome.Continue;
            var id=cards.CardIds.Single();if(_cardZones.GetLocation(id)!=CardLocation.Hand(frame.OwnerSeat))throw new InvalidOperationException("Initial public reserve requires a current owned hand entity.");
            var source=EnsurePublicPileSource(frame,1);if(PublicPileCards(source).Count!=0)throw new InvalidOperationException("Initial reserve is already occupied.");
            ReplaceRuntimeTop(frame with {PendingMovementContinuation=new(frame.OwnerSeat,0,null)});
            MoveCard(GetHand(_players[frame.OwnerSeat]).Single(c=>c.Id==id),CardLocation.Hand(frame.OwnerSeat),source.Location,new("skill-program.public-pile.initial-hand"));
            if(!TryBeginCardsMovedProgramWindow())ReturnRuntimeProgramMovement(frame.Id);return SkillProgramStepOutcome.AwaitChild;
        }
        IReadOnlyList<PromptChoice> choices;
        if(e.Op==SkillProgramEffectOp.PublicPileColorDamage)choices=ColorDamageChoices(frame.Id,frame.OwnerSeat,frame.SkillId,frame.SkillInstanceId,e);
        else
        {
            var fact=ColorActionFact(frame.WindowContext!)??throw new InvalidOperationException("Action color reward lost its actual discard batch.");

            choices=_players.Where(p=>p.IsAlive).Select(p=>new PromptChoice(new($"public-pile-color.frame-{frame.Id}.target-{p.Seat}"),"令 "+p.Name+" 摸一张牌。",[],[p.Seat],new Dictionary<string,string>{["program-action"]="public-pile-color",["frame-id"]=frame.Id.ToString()})).ToArray();
        }
        if(choices.Count==0)return SkillProgramStepOutcome.Continue;
        var skill=_contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,frame.OwnerSeat,skill.Description,choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),frame.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=e.Op==SkillProgramEffectOp.PublicPileColorDamage,Choices=choices,SkillPrompt=new(frame.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[frame.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ResolvePublicPileColorChoice(PromptChoice choice)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Color choice lost its program frame.");
        if(choice.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString()||choice.Targets.Count!=1)throw new InvalidOperationException("Color choice lost its exact cursor.");
        var e=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        if(e.Op==SkillProgramEffectOp.PublicPileColorDamage)
        {
            if(GetProgramConversionPolarity(f.OwnerSeat,f.SkillId)!=SkillPolarity.Yang || !ColorDamageChoices(f.Id,f.OwnerSeat,f.SkillId,f.SkillInstanceId,e).Any(c=>c.Id==choice.Id))throw new InvalidOperationException("Color cost no longer qualifies.");
            var source=SameGrantColorPiles(f.OwnerSeat,e.SkillIds.Single(),f.SkillInstanceId).Single(s=>s.SkillInstanceId==choice.Parameters["pile-instance"]);
            var id=choice.Cards.Single();var at=_cardZones.GetLocation(id);var card=_cardZones.CardsAt(at).Single(c=>c.Id==id);
            ClearPendingDecision();CommitProgramConversionPolarity(f);f=GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(f with{PublicPileColorPayment=new(id,at,source.Location,source.SkillInstanceId,PileColor(source)!.Value,choice.Targets.Single()),PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            MoveCard(card,at,CardLocation.DiscardPile,new("skill-program.public-pile.color-payment"));
            if(!TryBeginCardsMovedProgramWindow())ReturnRuntimeProgramMovement(f.Id);return;
        }
        if(e.Op!=SkillProgramEffectOp.RewardDiscardedActionColor||GetProgramConversionPolarity(f.OwnerSeat,f.SkillId)!=SkillPolarity.Yin||!_players[choice.Targets.Single()].IsAlive)throw new InvalidOperationException("Color reward lost its polarity or recipient.");
        ClearPendingDecision();CommitProgramConversionPolarity(f);f=GetActiveProgramFrame(f.Id);
        DrawProgramCards(f.Id,choice.Targets.Single(),1,null,null,SkillProgramCardSetVisibility.Private,new("skill-program.public-pile.action-color.draw"));
        // Draw recipient can differ from owner; generic draw already retains its continuation.
        if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(f.Id);
    }
    private bool ResumePublicPileColorPayment(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame {PublicPileColorPayment:{}} f||f.Id!=id||f.PendingMovementContinuation is not null)return false;
        var payment=f.PublicPileColorPayment!;ReplaceRuntimeTop(f with{PublicPileColorPayment=null});
        if(!_players[f.OwnerSeat].IsAlive||!_players[payment.TargetSeat].IsAlive){AdvanceRuntimeProgram(id);return true;}
        BeginProgramSkillDamage(GetActiveProgramFrame(id),payment.TargetSeat,1,new ProgramParticipantReference(ProgramParticipantRef.Owner));return true;
    }
    private PromptChoice SelectAiPublicPileColorChoice(PendingDecision p,ProgramSkillFrame f)=>p.Choices.OrderByDescending(c=>c.Targets.Count==1&&(c.Cards.Count==0?AreProgramDistributionAllies(_players[f.OwnerSeat],_players[c.Targets[0]]):!AreProgramDistributionAllies(_players[f.OwnerSeat],_players[c.Targets[0]]))).ThenBy(c=>c.Cards.Count==1?GetKeepValue(_cardZones.CardsAt(_cardZones.GetLocation(c.Cards[0])).Single(x=>x.Id==c.Cards[0]),_players[f.OwnerSeat]):0).ThenBy(c=>c.Targets[0]).First();
}
