namespace CardGame.Core;
public sealed record AlternatingChoiceBenefitResolvedEvent(int OwnerSeat,string SkillId,string SkillInstanceId,string StateId,string OptionId,int Amount,string? PendingOptionId) : IGameEvent;
public sealed record ConsecutiveTargetDeckGiftEvent(int OwnerSeat,string SkillId,string SkillInstanceId,string StateId,int TargetSeat,int OwnerTurnOrdinal,int? CardId,bool Consecutive) : IGameEvent;
public sealed partial class GameEngine
{
    private IEnumerable<IGameEvent> CompleteProgramEventHistory()=>_events.Select(e=>e.Payload).Concat(_pendingEvents);
    private sealed partial class ProgramSkillHost : IAlternatingBenefitsProgramHost
    {
        public void ApplyAlternatingChoiceBenefit(ProgramSkillFrame f,string sourceBind,string stateId)=>engine.ApplyAlternatingChoiceBenefit(f,sourceBind,stateId);
        public SkillProgramStepOutcome ObtainDeckCardWithConsecutiveTarget(ProgramSkillFrame f,int seat,SkillProgramEffect e)=>engine.ObtainDeckCardWithConsecutiveTarget(f,seat,e);
    }
    private void ApplyAlternatingChoiceBenefit(ProgramSkillFrame frame,string sourceBind,string stateId)
    {
        ValidateProgramTurnEffectGrant(frame);
        var choice=frame.ChoiceBindings.Single(b=>b.Name==sourceBind).OptionId;
        if(choice is not ("draw" or "targets")) throw new InvalidOperationException("Unknown alternating benefit.");
        var last=ProgramEventHistory<AlternatingChoiceBenefitResolvedEvent>().LastOrDefault(e=>e.OwnerSeat==frame.OwnerSeat && e.SkillId==frame.SkillId && e.SkillInstanceId==frame.SkillInstanceId && e.StateId==stateId);
        var switched=last?.PendingOptionId is { } pending && pending!=choice;
        var amount=switched?2:1;
        AdvanceEventRulesAndQueueFact(new AlternatingChoiceBenefitResolvedEvent(frame.OwnerSeat,frame.SkillId,frame.SkillInstanceId,stateId,choice,amount,switched?null:choice));
        if(choice=="draw")
        {
            var grant=_turnCardUseEffects.GrantRuleModifier(_turnNumber,_currentSeat,frame.Id,frame.InstructionIndex-1,CreateProgramTurnEffectSource(frame),SkillRuleQuery.DrawCount,SkillRuleOperation.Add,amount);
            AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(grant));
        }
        else GrantRedAdditionalTargets(frame,amount);
    }
    private IReadOnlyList<ProgramAlternatingChoiceStateSnapshot>? GetAlternatingChoiceStateSnapshot(CharacterState owner)
    {
        var states=GetSkillBindingShard(owner).ProgramInstances.SelectMany(i=>i.Program.Triggers.SelectMany(t=>t.Effects).Where(e=>e.Op==SkillProgramEffectOp.ApplyAlternatingChoiceBenefit).Select(e=>new {Instance=i,StateId=e.StateId!})).ToArray();
        if(states.Length==0) return null;
        return states.Select(s=>{
            var last=ProgramEventHistory<AlternatingChoiceBenefitResolvedEvent>().LastOrDefault(e=>e.OwnerSeat==owner.Seat && e.SkillId==s.Instance.SkillId && e.SkillInstanceId==s.Instance.SkillInstanceId && e.StateId==s.StateId);
            return new ProgramAlternatingChoiceStateSnapshot(s.Instance.SkillId,s.StateId,s.Instance.SkillInstanceId,last?.PendingOptionId,last?.PendingOptionId=="targets"?2:1,last?.PendingOptionId=="draw"?2:1);
        }).ToArray();
    }
    private SkillProgramStepOutcome ObtainDeckCardWithConsecutiveTarget(ProgramSkillFrame frame,int targetSeat,SkillProgramEffect effect)
    {
        if(frame.OwnerSeat!=_currentSeat || targetSeat==frame.OwnerSeat || !_players[targetSeat].IsAlive || frame.WindowContext?.Window!=SkillProgramTriggerWindow.TurnEnding)
            throw new InvalidOperationException("A deck gift needs the owner's ending and a living other target.");
        var ordinal=ProgramEventHistory<TurnStartedEvent>().Count(e=>e.ActorSeat==frame.OwnerSeat);
        var last=ProgramEventHistory<ConsecutiveTargetDeckGiftEvent>().LastOrDefault(e=>e.OwnerSeat==frame.OwnerSeat && e.SkillId==frame.SkillId && e.SkillInstanceId==frame.SkillInstanceId && e.StateId==effect.StateId);
        var repeated=last is not null && last.TargetSeat==targetSeat && last.OwnerTurnOrdinal==ordinal-1;
        var card=_cardZones.CardsAt(CardLocation.DrawPile).Reverse().FirstOrDefault(c=>effect.Suits.Contains(c.Suit) && effect.CardCategories.Any(category => MatchesSkillProgramCardCategory(c.Kind, category)));
        var index=_resolutionStack.FindIndex(f=>f.Id==frame.Id);
        ReplaceRuntimeFrame(_resolutionStack[index].Id, frame with {ChoiceBindings=frame.ChoiceBindings.Append(new ProgramChoiceResultBinding(effect.ResultBind!,repeated?"repeat":"new",targetSeat)).ToArray()});
        AdvanceEventRulesAndQueueFact(new ConsecutiveTargetDeckGiftEvent(frame.OwnerSeat,frame.SkillId,frame.SkillInstanceId,effect.StateId!,targetSeat,ordinal,card?.Id,repeated));
        if(card is not null)
        {
            MoveCard(card,CardLocation.DrawPile,CardLocation.Hand(targetSeat),new CardMoveReason("skill-program.consecutive-target.deck-gift"));
            return AwaitProgramBoundCardMovements(frame.Id,frame.OwnerSeat);
        }
        return SkillProgramStepOutcome.Continue;
    }
}
