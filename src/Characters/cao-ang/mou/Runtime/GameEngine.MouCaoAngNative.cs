namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static bool MouCaDamageKind(CardKind kind)=>IsDamageCardKind(kind)||IsCompletedUndamagedDamageUse(kind)||kind==CardKind.Lightning;
    private bool IsMouCaExactCardQuery(CharacterState actor,int? selected)
    {
        if(selected is not{}id||!actor.IsAlive||!_cardZones.Contains(CardLocation.DiscardPile,id))return false;
        if(_resolutionStack.LastOrDefault()is ProgramSkillFrame{MouCaoAng:{}r}f&&MouCaoAng.Valid(f)&&f.SkillId==MouCaoAngRuntime.Zhiwang&&actor.Seat!=f.OwnerSeat&&MouCaDamageKind(GetAdvancedCard(id).Kind))
            return r.Stage==MouCaoAngStage.Material||r.Stage is MouCaoAngStage.UseChoice or MouCaoAngStage.IssuingUse&&r.Actor==actor.Seat&&r.CardId==id;
        return _resolutionStack.LastOrDefault()is CardUseFrame{MouCaoAngZhiwang:{}n}u&&n.Actor==actor.Seat&&n.CardId==id&&_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(x=>x.Id==n.FrameId)is{}parent&&MouCaUseRidesOn(u,parent);
    }
    private Card? MouCaQueryOriginalCard(CharacterState actor,int? id)=>IsMouCaExactCardQuery(actor,id)?GetAdvancedCard(id!.Value):null;
    private IReadOnlyList<LegalAction> MouCaPlans(ProgramSkillFrame f,int actor,int id)
    {
        if(!IsValidPlayerSeat(actor)||!IsMouCaExactCardQuery(_players[actor],id)||_winner!=Winner.None)return[];
        var card=GetAdvancedCard(id);
        return Array.AsReadOnly(BuildLegalActions(_players[actor],false,id).Where(a=>a.CardId==id&&a.ConversionSource is null&&(a.AdditionalConversionSources?.Count??0)==0&&a.MinCardCount<=1&&a.MaxCardCount<=1&&a.Kind is not(LegalActionKind.Recast or LegalActionKind.EndPlay or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.RevealGeneral)&&((a.PlayedCardKind??card.Kind)==card.Kind||card.Kind==CardKind.Slash&&a.PlayedCardKind==CardKind.FireSlash&&HasZhuqueFan(_players[actor]))).Select(a=>a with{TargetSeats=Array.AsReadOnly(a.TargetSeats.ToArray()),AdditionalConversionSources=Array.AsReadOnly((a.AdditionalConversionSources??[]).ToArray()),SelectableCardIds=Array.AsReadOnly(a.SelectableCardIds.ToArray()),SelectableTargetSeats=Array.AsReadOnly(a.SelectableTargetSeats.ToArray()),SelectableCardTargets=a.SelectableCardTargets is null?null:Array.AsReadOnly(a.SelectableCardTargets.ToArray()),SelectableTargetSequences=a.SelectableTargetSequences is null?null:Array.AsReadOnly(a.SelectableTargetSequences.Select(t=>(IReadOnlyList<int>)Array.AsReadOnly(t.ToArray())).ToArray())}).ToArray());
    }
    private IReadOnlyList<MouCaPair> MouCaPairs(ProgramSkillFrame f)
    {
        if(f.MouCaoAng is not{Stage:MouCaoAngStage.Material}||!MouCaoAng.Valid(f)||_winner!=Winner.None)return[];
        var cards=_cardZones.CardsAt(CardLocation.DiscardPile).Where(c=>MouCaDamageKind(c.Kind)).ToArray();
        return Array.AsReadOnly(cards.SelectMany(c=>_players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat&&MouCaPlans(f,p.Seat,c.Id).Count>0).Select(p=>new MouCaPair(c.Id,p.Seat,c.Kind,c.DisplayName))).ToArray());
    }
    private IReadOnlyList<int> MouCaExpectedTargets(MouCaNativeUse n)
    {
        var a=n.Action;var actor=_players[n.Actor];var card=ApplyProgramUseAppearance(actor,GetAdvancedCard(n.CardId),null);
        if(GuanduExpectedNativeTargets(actor,card,a)is{}guandu)return guandu;if(DragonBoatExpectedNativeTargets(actor,card,a)is{}dragon)return dragon;
        if(a.Kind is not(LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage))return GetImplicitSelfCardUseTargets(n.Kind,n.Actor,a.TargetSeats);
        return Array.AsReadOnly(Enumerable.Range(1,_playerCount-1).Select(offset=>_players[(n.Actor+offset)%_playerCount]).Where(p=>p.IsAlive&&!HasCardPolicy(p,SkillProgramCardPolicyKind.ExcludeGlobalTarget,n.Kind)&&!IsCardTargetProhibited(p,n.Kind,card.Suit,SuitColor(EffectiveSuit(actor,card)))&&!HasBeneficiarySuitShield(n.Actor,p.Seat,EffectiveSuit(actor,card))&&!TargetRules.IsDeclaredGlobalProhibited(new(n.Actor,p.Seat,n.Kind))).Select(p=>p.Seat).ToArray());
    }
    private void IssueMouCaUse(ProgramSkillFrame f)
    {
        if(!MouCaoAng.Valid(f)||f.MouCaoAng is not{Stage:MouCaoAngStage.IssuingUse,Native:{UseId:0,ActionId:0}n})throw new InvalidOperationException("Zhiwang requires its issued real discard entity and actual other actor.");
        var action=MouCaPlans(f,n.Actor,n.CardId).SingleOrDefault(a=>MouCaoAngRuntime.SameAction(a,n.Action));if(action is null)throw new InvalidOperationException("Zhiwang's material, actor or native legal target plan expired.");ExecuteAction(_players[n.Actor],action);
    }
    private void BindMouCaNativeUse(CardUseFrame use)
    {
        if(_resolutionStack.Count<2||_resolutionStack[^2]is not ProgramSkillFrame{MouCaoAng:{Stage:MouCaoAngStage.IssuingUse,Native:{UseId:0,ActionId:0}n}r}f)return;
        var targets=MouCaExpectedTargets(n);
        if(!MouCaoAng.Valid(f)||use.Action is not{Type:CardActionType.Use}a||a.ActorSeat!=n.Actor||a.ProviderSeat!=n.Actor||a.RequesterSeat is not null||a.ConversionChain.Count!=0||use.SourceSeat!=n.Actor||use.CardId!=n.CardId||use.CardKind!=n.Kind||a.PhysicalCards is not[{CardId:var id,From:var from}]||id!=n.CardId||from!=CardLocation.DiscardPile||use.PhysicalCardIds is not[var material]||material!=id||!use.TargetSeats.SequenceEqual(targets)||_cardZones.GetLocation(id)!=from)throw new InvalidOperationException("Zhiwang changed the exact discard entity, actual actor/provider or canonical original targets.");
        var bound=n with{UseId=use.Id,ActionId=a.ActionId,Targets=targets};ReplaceRuntimeFrame(f.Id,f with{MouCaoAng=r with{Stage=MouCaoAngStage.NativeUse,Native=bound}});UpdateLifecycleCardUse(use.Id,u=>u with{MouCaoAngZhiwang=bound});AdvanceEventRulesAndQueueFact(new MouCaUseIssuedEvent(f.Id,use.Id,a.ActionId,id,n.Actor));
    }
    private bool MouCaUseRidesOn(CardUseFrame u,ProgramSkillFrame f)=>MouCaoAng.Valid(f)&&u.MouCaoAngZhiwang is{}n&&f.MouCaoAng is{Stage:MouCaoAngStage.NativeUse or MouCaoAngStage.ReturnedUse,Native:{}p}&&MouCaoAngRuntime.SameUse(n,p)&&n.FrameId==f.Id&&n.Instruction==f.InstructionIndex&&n.Source==f.MouCaoAng.Source&&n.GameplayHash==f.GameplayHash&&n.Turn==_turnNumber&&u.Id==n.UseId&&u.CardId==n.CardId&&u.CardKind==n.Kind&&u.Action is{Type:CardActionType.Use}a&&a.ActionId==n.ActionId&&a.ProviderSeat==n.Actor&&ShownEntityUseActorMatches(u,n.Actor,n.Actor)&&a.PhysicalCards is[{CardId:var id,From:var from}]&&id==n.CardId&&from==CardLocation.DiscardPile&&u.PhysicalCardIds is[var material]&&material==id&&ProgramEventHistory<MouCaUseIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.UseId==u.Id&&e.ActionId==a.ActionId&&e.CardId==id&&e.Actor==n.Actor)==1;
    [NativeUseValidation(36700)]private void ValidateMouCaUse(CardUseFrame u){if(u.MouCaoAngZhiwang is{}n&&(u.Step!=ResolutionFrameStep.Completed||_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.FrameId)is not{}parent||!MouCaUseRidesOn(u,parent)))throw new InvalidOperationException("Zhiwang cannot retire another or incomplete native Use.");}
    [NativeUseReturn(36700)]private bool ReturnMouCaUse(CardUseFrame u)
    {
        if(u.MouCaoAngZhiwang is not{}n)return false;var f=GetActiveProgramFrame(n.FrameId);if(LifecycleCardUse(u.Id)is not null||u.Step!=ResolutionFrameStep.Completed||!MouCaUseRidesOn(u,f)||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==u.Id)!=1)throw new InvalidOperationException("Zhiwang requires its one completed original native child.");MouCaoAng.Returned(n,u.CardAttack is{Active:true});return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup,36700)]private bool ResumeMouCaUse(){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouCaoAng:{Stage:MouCaoAngStage.ReturnedUse}}f)return false;AdvanceRuntimeProgram(f.Id);return true;}
    private void PrepareMouCaDamageSource(IDamageAttempt attempt)
    {
        if(attempt is not CardAttackHandle attack||LifecycleCardUse(attack.ResolutionId)is not{MouCaoAngZhiwang:{}n}use||_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.FrameId)is not{}parent||!MouCaUseRidesOn(use,parent))return;
        // A chain target may reuse the preceding target's owned attack state.
        // Recompute for this actual victim, preserving native Lightning semantics.
        UpdateCardAttackState(attack.ResolutionId,s=>s! with{IsSourceLess=attack.TargetSeat==parent.OwnerSeat||s.IsDelayedJudgmentDamage&&s.EffectiveCardKind==CardKind.Lightning});
        if(attack.TargetSeat==parent.OwnerSeat&&!ProgramEventHistory<MouCaSourceLessEvent>().Any(e=>e.UseId==use.Id&&e.ActionId==n.ActionId&&e.Target==parent.OwnerSeat))AdvanceEventRulesAndQueueFact(new MouCaSourceLessEvent(use.Id,n.ActionId,parent.OwnerSeat,n.CardId));
    }
}
