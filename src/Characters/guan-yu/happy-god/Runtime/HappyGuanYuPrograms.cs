namespace CardGame.Core;

internal static class HappyGuanYuComposition
{
    internal const string Wushen="happy:wushen",Wuhun="happy:wuhun";
    internal static bool NightmareJudgment(SkillProgramTrigger t)=>!t.Optional&&t.Subject==SkillProgramTriggerSubject.Owner&&
        t.Condition.Kind==SkillProgramTriggerConditionKind.Always&&t.UsageScope is null&&t.MarkerCost is null&&
        t.Effects is[{Op:SkillProgramEffectOp.SelectTarget,Target:SkillProgramEffectTarget.Owner,TargetKind:SkillProgramTargetKind.MaximumOtherAttributedMarker,Marker:PlayerMarkerKind.Nightmare},
            {Op:SkillProgramEffectOp.StartJudgment,Target:SkillProgramEffectTarget.SelectedTarget,JudgmentReason:"skill.happy-wuhun",ResultBind:"nightmare-judgment",Visibility:SkillProgramCardSetVisibility.Public},
            {Op:SkillProgramEffectOp.LoseHp,Target:SkillProgramEffectTarget.SelectedTarget,Amount:5,Condition:{Kind:SkillProgramConditionKind.Not,Children:[{Kind:SkillProgramConditionKind.BoundCardsMatchKinds,SourceBind:"nightmare-judgment"}safe]}},
            {Op:SkillProgramEffectOp.MoveBoundCards,Target:SkillProgramEffectTarget.Owner,SourceBind:"nightmare-judgment",ExceptBind:null,Destination:SkillProgramCardDestination.DiscardPile,Condition.Kind:SkillProgramConditionKind.Always}]&&
        safe.CardKinds.SequenceEqual([CardKind.Peach,CardKind.PeachGarden])&&t.Effects.Take(2).All(e=>e.Condition.Kind==SkillProgramConditionKind.Always);
    [ProgramCatalogValidation(41880)]internal static void Validate(string path,SkillProgram p)
    {
        if(p.Id is not(Wushen or Wuhun))return;
        var valid=p.Id==Wushen?
            p.ViewAs is[{Id:"heart-hand-as-slash",InputCount:1,OutputKind:CardKind.Slash,ForPlay:true,ForResponse:true,UseOnly:false,Condition.Kind:SkillProgramConditionKind.Always}v]&&v.SourceZones.SequenceEqual([CardZoneKind.Hand])&&
            v.InputKinds.Count==0&&v.InputSuits.SequenceEqual([Suit.Heart])&&p.CardIdentities.Count==0&&p.Triggers.Count==0&&p.CardPolicies is[{Id:"heart-slash-distance",Kind:SkillProgramCardPolicyKind.IgnoreSlashUseDistanceBySuit,InputSuit:Suit.Heart,Condition.Kind:SkillProgramConditionKind.Always}d]&&
            d.CardKinds.SequenceEqual([CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash]):
            p.ViewAs.Count==0&&p.CardPolicies.Count==0&&p.CardIdentities.Count==0&&p.Triggers is[
                {Id:"damage-nightmare",Window:SkillProgramTriggerWindow.DamageAppliedBeforeDying,Subject:SkillProgramTriggerSubject.Owner,DamageOccurrence:SkillProgramDamageOccurrence.PerDamagePoint,Optional:false,
                    Effects:[{Op:SkillProgramEffectOp.ChangeAttributedMarker,Target:SkillProgramEffectTarget.Owner,TargetReference.Kind:ProgramParticipantRef.EventSource,Marker:PlayerMarkerKind.Nightmare,Amount:1}]},
                {Id:"survived-nightmare",Window:SkillProgramTriggerWindow.DyingExited}exited,
                {Id:"death-nightmare",Window:SkillProgramTriggerWindow.OwnerDied}died]&&NightmareJudgment(exited)&&NightmareJudgment(died);
        if(!valid||p.Activations.Count!=0||p.Modifiers.Count!=0||p.BooleanStates.Count!=0||p.Contributions.Count!=0)
            throw new InvalidOperationException(path+": Happy Guan Yu requires voluntary hand-only heart conversions, heart Slash distance and exact survived/death five-HP nightmare judgments without clearing markers.");
    }
}

public sealed partial class GameEngine
{
    private bool HasHappyGuanYuHeartSlashCapability=>_contentRegistry.Skills.ContainsKey(HappyGuanYuComposition.Wushen);
    [ProgramDamageHook(ProgramDamagePhase.FactModifiers,41880)]
    private IEnumerable<(CardUseEffectSource Source,int Amount)> HappyGuanYuHeartSlashDamage(IDamageAttempt attack)
    {
        if(attack.IsChainPropagation||attack.EffectiveCardKind is not{}kind||!IsSlashCard(kind)||
            LifecycleCardUse(attack.ResolutionId) is not{Action:{Type:CardActionType.Use,EffectiveSuit:Suit.Heart}action}use||
            use.CardKind!=kind||action.EffectiveKind!=kind||use.SourceSeat!=action.ActorSeat||action.ActorSeat!=attack.CardUserSeat)return [];
        return GetSkillBindingShard(_players[action.ActorSeat]).GetCardPolicies(SkillProgramCardPolicyKind.IgnoreSlashUseDistanceBySuit)
            .Where(b=>b.Source.SkillId==HappyGuanYuComposition.Wushen&&b.Policy.Id=="heart-slash-distance")
            .Select(b=>(new CardUseEffectSource(b.Source.SkillId,b.Policy.Id,action.ActorSeat,b.Source.SkillInstanceId),1));
    }
    private bool IsHappyGuanYuDyingExitCandidate(ProgramTriggerCandidate c,int victim)=>c.OwnerSeat==victim&&c.SkillId==HappyGuanYuComposition.Wuhun&&c.BindingId=="survived-nightmare";
    private bool IsHappyGuanYuDyingExitContext(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!IsHappyGuanYuDyingExitCandidate(c,c.OwnerSeat)||context.Window!=SkillProgramTriggerWindow.DyingExited||context.SourceSeat!=c.OwnerSeat||context.TargetSeat!=c.OwnerSeat||
            !_players[c.OwnerSeat].IsAlive||_players[c.OwnerSeat].Hp<=0||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not
                {Window:SkillProgramTriggerWindow.DyingExited,Continuation:ProgramLifecycleContinuation.ResumeYanPuDyingExit,YanPuDyingExit:{}ret}parent||
            parent.OwnerSeat!=c.OwnerSeat||ret.VictimSeat!=c.OwnerSeat||ret.Round!=_roundNumber||ret.Turn!=_turnNumber||ret.TurnOwner!=_currentSeat||
            parent.CandidateIndex<0||parent.CandidateIndex>=parent.Candidates.Count||parent.Candidates[parent.CandidateIndex]!=c)return false;
        var index=_resolutionStack.FindIndex(f=>f.Id==parent.Id);
        return index>0&&_resolutionStack[index-1].Id==ret.DyingParentFrameId&&
            ProgramEventHistory<DyingResolvedEvent>().Count(e=>e.ResolutionId==ret.DyingFrameId&&e.VictimSeat==c.OwnerSeat&&e.Survived)==1;
    }
    private bool IsHappyGuanYuDyingExitReturn(ProgramSkillFrame f)=>f.WindowContext is{}context&&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is{}parent&&
        parent.CandidateIndex>=0&&parent.CandidateIndex<parent.Candidates.Count&&MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex])&&
        IsHappyGuanYuDyingExitContext(parent.Candidates[parent.CandidateIndex],context);
}
