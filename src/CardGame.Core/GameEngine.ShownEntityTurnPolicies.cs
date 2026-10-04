namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool TracksShownEntityTurnPolicies=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.IssueShownEntityTurnPolicy);
    private bool CanOfferShownEntityTurnPolicy(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)=>
        !GetProgramTrigger(candidate).Effects.Any(e=>e.Op==SkillProgramEffectOp.IssueShownEntityTurnPolicy) ||
        context.Window==SkillProgramTriggerWindow.PlayPhaseStarting &&candidate.OwnerSeat==_turnProgression.OwnerSeat &&
        candidate.OwnerSeat==_currentSeat &&_phase==TurnPhase.Play &&GetHand(_players[candidate.OwnerSeat]).Count>0;

    private void IssueShownEntityTurnPolicy(ProgramSkillFrame frame,string bind,string stateId)
    {
        ValidateProgramTurnEffectGrant(frame);
        var owner=_players[frame.OwnerSeat];var set=GetProgramCardSet(frame,bind);
        if(frame.WindowContext is not {Window:SkillProgramTriggerWindow.PlayPhaseStarting} context ||
            context.OwnerSeat!=owner.Seat ||_currentSeat!=owner.Seat ||_turnProgression.OwnerSeat!=owner.Seat ||_phase!=TurnPhase.Play ||
            set.CardIds is not [var id] ||set.SourceLocations is not [var from] ||from!=CardLocation.Hand(owner.Seat) ||
            set.Visibility!=SkillProgramCardSetVisibility.Public ||set.FrozenRevealedSuit is not { } suit ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not {Window:SkillProgramTriggerWindow.PlayPhaseStarting} parent ||
            parent.OwnerSeat!=owner.Seat ||parent.CandidateIndex<0 ||parent.CandidateIndex>=parent.Candidates.Count ||
            !MountObserverCandidateMatches(frame,parent.Candidates[parent.CandidateIndex]))
            throw new InvalidOperationException("Shown entity policy lost its exact actual Play start and public single-card reveal.");
        if(!owner.IsAlive ||_winner!=Winner.None ||_cardZones.GetLocation(id)!=from ||!HasRuntimeSkillInstance(owner,frame.SkillId,frame.SkillInstanceId))return;
        if(CompleteProgramEventHistory().OfType<ProgramCardsRevealedEvent>().Count(e=>e.FrameId==frame.Id &&e.OwnerSeat==owner.Seat &&e.SkillId==frame.SkillId &&
            e.BindingId==GetProgramBindingId(frame) &&e.Bind==bind &&e.Cards.Count==1 &&e.Cards[0].Id==id)!=1)
            throw new InvalidOperationException("Shown entity policy requires its one actual public reveal fact.");
        if(CompleteProgramEventHistory().OfType<ShownEntityTurnPolicyGrantedEvent>().Any(e=>e.Policy.ProgramFrameId==frame.Id))
            throw new InvalidOperationException("The shown entity policy cannot be reissued by its completed instruction.");
        var source=CreateProgramTurnEffectSource(frame);
        var affected=_players.Where(p=>p.IsAlive &&p.Seat!=owner.Seat &&GetCombatDistance(owner.Seat,p.Seat)==1).Select(p=>p.Seat).ToArray();
        var sequences=new List<long>();
        if(suit is Suit.Spade or Suit.Club or Suit.Heart or Suit.Diamond)
            foreach(var target in affected)
            {
                var restriction=_turnCardUseEffects.GrantHandColorRestriction(_turnNumber,_turnProgression.OwnerSeat,frame.Id,
                    -1-target,source,target,IsRedSuit(suit),requiresChromaticSuit:true);
                sequences.Add(restriction.GrantSequence);
                AdvanceEventRulesAndQueueFact(new HandCardColorRestrictionGrantedEvent(restriction));
            }
        var policy=new ShownEntityTurnPolicy(frame.Id,frame.InstructionIndex-1,source,frame.GameplayHash,stateId,_turnNumber,
            _turnProgression.OwnerSeat,parent.Id,id,from,suit,affected,sequences);
        AdvanceEventRulesAndQueueFact(new ShownEntityTurnPolicyGrantedEvent(policy));
    }

    private bool ShownEntityPoliciesMatch(ShownEntityTurnPolicy a,ShownEntityTurnPolicy b)=>a.ProgramFrameId==b.ProgramFrameId &&
        a.EffectIndex==b.EffectIndex &&a.Source==b.Source &&a.GameplayHash==b.GameplayHash &&a.StateId==b.StateId &&
        a.TurnNumber==b.TurnNumber &&a.ActualTurnOwnerSeat==b.ActualTurnOwnerSeat &&a.ActualPlayStartingFrameId==b.ActualPlayStartingFrameId &&
        a.CardId==b.CardId &&a.OriginalFrom==b.OriginalFrom &&a.FrozenSuit==b.FrozenSuit &&
        a.AffectedSeats.SequenceEqual(b.AffectedSeats) &&a.RestrictionSequences.SequenceEqual(b.RestrictionSequences);

    private void IssueShownEntityUseBenefits(long useId,CardActionContext? action)
    {
        if(!TracksShownEntityTurnPolicies ||action is not {Type:CardActionType.Use} ||action.PhysicalCards.Count==0)return;
        var use=LifecycleCardUse(useId)??throw new InvalidOperationException("Shown entity benefit requires an actual owning use.");
        if(use.ShownEntityBenefits is not null ||use.SourceSeat!=action.ActorSeat ||use.Action?.ActionId!=action.ActionId ||
            use.PhysicalCardIds is null ||!use.PhysicalCardIds.SequenceEqual(action.PhysicalCards.Select(c=>c.CardId)))
            throw new InvalidOperationException("Shown entity benefit cannot change its actual accepted full material set.");
        var policies=CompleteProgramEventHistory().OfType<ShownEntityTurnPolicyGrantedEvent>().Select(e=>e.Policy)
            .Where(p=>p.TurnNumber==_turnNumber &&p.ActualTurnOwnerSeat==_turnProgression.OwnerSeat &&
                action.PhysicalCards.Any(c=>c.CardId==p.CardId))
            .GroupBy(p=>(p.Source.OwnerSeat,p.Source.SkillId,p.StateId,p.CardId)).Select(g=>g.First()).ToArray();
        if(policies.Length==0)return;
        // One actual Use receives +1 from this method even when two different
        // material entities were revealed in separate actual Play phases.
        var benefits=Array.AsReadOnly(policies.GroupBy(p=>(p.Source.OwnerSeat,p.Source.SkillId,p.StateId)).Select(g=>
            new ShownEntityUseBenefit(useId,action.ActionId,action.ActorSeat,action.ProviderSeat,g.First(),action.PhysicalCards)).ToArray());
        UpdateLifecycleCardUse(useId,f=>f with {ShownEntityBenefits=benefits});
        foreach(var benefit in benefits)
            AdvanceEventRulesAndQueueFact(new ShownEntityUseBenefitIssuedEvent(useId,action.ActionId,action.ActorSeat,action.ProviderSeat,benefit.Policy));
    }

    private IEnumerable<(CardUseEffectSource Source,int Amount)> GetShownEntityUseDamageBenefits(IDamageAttempt attack)
    {
        if(attack.IsChainPropagation ||attack.IsSourceLess ||attack.SourceSeat!=attack.CardUserSeat ||attack.EffectiveCardKind is null)return [];
        var use=LifecycleCardUse(attack.ResolutionId);
        if(use?.ShownEntityBenefits is not {Count:>0} benefits ||use.Action is not {Type:CardActionType.Use} action ||action.ActorSeat!=attack.CardUserSeat)return [];
        AssertShownEntityUseBenefits(use);
        return benefits.Where(b=>b.Policy.Source.OwnerSeat==attack.SourceSeat).Select(b=>(b.Policy.Source,1));
    }

    private void AssertShownEntityUseBenefits(CardUseFrame use)
    {
        if(use.ShownEntityBenefits is not { } benefits)return;
        if(!TracksShownEntityTurnPolicies ||benefits.Count==0 ||benefits is not System.Collections.IList {IsReadOnly:true} ||
            use.Action is not {Type:CardActionType.Use} action ||use.SourceSeat!=action.ActorSeat ||
            use.PhysicalCardIds is null ||!use.PhysicalCardIds.SequenceEqual(action.PhysicalCards.Select(c=>c.CardId)) ||
            action.PhysicalCards.Select(c=>c.CardId).Distinct().Count()!=action.PhysicalCards.Count ||
            benefits.GroupBy(b=>(b.Policy.Source.OwnerSeat,b.Policy.Source.SkillId,b.Policy.StateId)).Any(g=>g.Count()!=1))
            throw new InvalidOperationException("Shown entity benefit lost its exact actual use/material/source identity.");
        foreach(var b in benefits)
        {
            var p=b.Policy;
            if(b.CardUseFrameId!=use.Id ||b.ActionId!=action.ActionId ||!ShownEntityUseActorMatches(use,b.OriginalActorSeat,b.ProviderSeat) ||
                p.TurnNumber!=_turnNumber ||p.ActualTurnOwnerSeat!=_turnProgression.OwnerSeat ||p.OriginalFrom!=CardLocation.Hand(p.Source.OwnerSeat) ||
                b.PhysicalMaterials is not System.Collections.IList {IsReadOnly:true} ||!b.PhysicalMaterials.SequenceEqual(action.PhysicalCards) ||
                !action.PhysicalCards.Any(c=>c.CardId==p.CardId) ||p.AffectedSeats.Distinct().Count()!=p.AffectedSeats.Count ||
                p.AffectedSeats.Any(s=>!IsValidPlayerSeat(s) ||s==p.Source.OwnerSeat) ||p.RestrictionSequences.Distinct().Count()!=p.RestrictionSequences.Count ||
                CompleteProgramEventHistory().OfType<ShownEntityTurnPolicyGrantedEvent>().Count(e=>ShownEntityPoliciesMatch(e.Policy,p))!=1 ||
                CompleteProgramEventHistory().OfType<ShownEntityUseBenefitIssuedEvent>().Count(e=>e.CardUseFrameId==use.Id &&e.ActionId==action.ActionId &&
                    e.OriginalActorSeat==b.OriginalActorSeat &&e.ProviderSeat==b.ProviderSeat &&ShownEntityPoliciesMatch(e.Policy,p))!=1)
                throw new InvalidOperationException("Shown entity benefit lost its frozen granted policy and one true issuance fact.");
            AssertShownEntityPolicyOrigin(p);
        }
    }
    private bool ShownEntityUseActorMatches(CardUseFrame use,int originalActor,int provider)
    {
        if(use.Action is not {Type:CardActionType.Use} action ||action.ProviderSeat!=provider ||
            CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Count(e=>e.ResolutionId==use.Id &&e.SourceSeat==originalActor &&e.CardId==use.CardId)!=1)return false;
        var changes=CompleteProgramEventHistory().OfType<ProgramCardUseActorReplacedEvent>().Where(e=>e.CardUseFrameId==use.Id).ToArray();
        if(changes.Length>0 &&!CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e=>e.Action.ActionId==action.ActionId &&
            e.Action.Type==CardActionType.Use &&e.Action.ActorSeat==originalActor &&e.Action.ProviderSeat==provider &&e.Action.PhysicalCards.SequenceEqual(action.PhysicalCards)))return false;
        var actor=originalActor;
        foreach(var changed in changes)
        {
            if(changed.PreviousActorSeat!=actor ||changed.OwnerSeat!=actor ||changed.ProviderSeat!=action.ProviderSeat ||
                changed.FrameId<=use.Id ||!IsValidPlayerSeat(changed.ActorSeat) ||changed.ActorSeat==actor ||
                !CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Any(e=>e.FrameId==changed.FrameId &&e.SkillId==changed.SkillId &&
                    e.OwnerSeat==changed.OwnerSeat &&e.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized))return false;
            actor=changed.ActorSeat;
        }
        return actor==action.ActorSeat &&actor==use.SourceSeat;
    }
    private void AssertShownEntityPolicyOrigin(ShownEntityTurnPolicy p)
    {
        var program=_contentRegistry.GetSkill(p.Source.SkillId).Program;
        var trigger=program?.Triggers.SingleOrDefault(t=>t.Id==p.Source.BindingId);
        var chromatic=p.FrozenSuit is Suit.Spade or Suit.Club or Suit.Heart or Suit.Diamond;
        if(program?.GameplayHash!=p.GameplayHash ||trigger?.Effects is not [_,_,{Op:SkillProgramEffectOp.IssueShownEntityTurnPolicy,StateId:{ } state}] ||
            state!=p.StateId ||p.EffectIndex!=2 ||p.ProgramFrameId<=0 ||p.ActualPlayStartingFrameId<=0 ||p.ActualPlayStartingFrameId>=p.ProgramFrameId ||
            p.TurnNumber<1 ||p.ActualTurnOwnerSeat!=p.Source.OwnerSeat ||p.OriginalFrom!=CardLocation.Hand(p.Source.OwnerSeat) ||!Enum.IsDefined(p.FrozenSuit) ||
            p.AffectedSeats is not System.Collections.IList {IsReadOnly:true} ||p.RestrictionSequences is not System.Collections.IList {IsReadOnly:true} ||
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e=>e.FrameId==p.ProgramFrameId &&e.OwnerSeat==p.Source.OwnerSeat &&
                e.SkillId==p.Source.SkillId &&e.BindingId==p.Source.BindingId &&e.SkillInstanceId==p.Source.SkillInstanceId &&e.Window==SkillProgramTriggerWindow.PlayPhaseStarting)!=1 ||
            CompleteProgramEventHistory().OfType<ProgramCardsRevealedEvent>().Count(e=>e.FrameId==p.ProgramFrameId &&e.OwnerSeat==p.Source.OwnerSeat &&
                e.SkillId==p.Source.SkillId &&e.BindingId==p.Source.BindingId &&e.Cards.Count==1 &&e.Cards[0].Id==p.CardId)!=1 ||
            p.RestrictionSequences.Count!=(chromatic?p.AffectedSeats.Count:0))
            throw new InvalidOperationException("Shown entity policy lost its true source/reveal and actual own Play-start issuance.");
        for(var i=0;i<p.RestrictionSequences.Count;i++)
            if(CompleteProgramEventHistory().OfType<HandCardColorRestrictionGrantedEvent>().Count(e=>
                e.Restriction.GrantSequence==p.RestrictionSequences[i] &&e.Restriction.ParentFrameId==p.ProgramFrameId &&
                e.Restriction.TurnNumber==p.TurnNumber &&e.Restriction.TurnSeat==p.ActualTurnOwnerSeat &&e.Restriction.Source==p.Source &&
                e.Restriction.AffectedSeat==p.AffectedSeats[i] &&e.Restriction.IsRed==IsRedSuit(p.FrozenSuit) &&e.Restriction.RequiresChromaticSuit)!=1)
                throw new InvalidOperationException("Shown entity policy lost an exact public frozen recipient/color restriction.");
    }
    private void AssertShownEntityTurnPrograms()
    {foreach(var use in _resolutionStack.OfType<CardUseFrame>())AssertShownEntityUseBenefits(use);}
    private sealed partial class ProgramSkillHost:IShownEntityTurnPolicyHost
    {public void IssueShownEntityTurnPolicy(ProgramSkillFrame f,string bind,string stateId)=>engine.IssueShownEntityTurnPolicy(f,bind,stateId);}
}
