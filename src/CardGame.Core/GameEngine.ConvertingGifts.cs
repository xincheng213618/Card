namespace CardGame.Core;
public sealed partial class GameEngine
{
    private readonly List<GiftHandRetentionObligation> _giftHandRetentionObligations = [];
    private long _giftHandRetentionSequence;
    private sealed partial class ProgramSkillHost : IConvertingGiftProgramHost
    {
        public SkillProgramStepOutcome GiveSelectedOwnedCardAndDamage(ProgramSkillFrame f,int limit)=>engine.BeginConvertingGiftDamage(f,limit);
        public SkillProgramStepOutcome ObserveDamageSourceHandAndGive(ProgramSkillFrame f,int limit)=>engine.BeginDamageSourceObservationGift(f,limit);
        public SkillProgramStepOutcome DrawToHandCount(ProgramSkillFrame f,int seat,int limit)=>engine.DrawProgramToHandCount(f,seat,limit);
    }
    private bool CanActivateConvertingGift(CharacterState owner,string skillId,ProgramInstructionFeatures features) =>
        !features.HasOperation(SkillProgramEffectOp.GiveSelectedOwnedCardAndDamage) || GetProgramConversionPolarity(owner.Seat,skillId)==SkillPolarity.Yang;
    private bool CanOfferConvertingGift(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context,ProgramInstructionFeatures features) =>
        !features.HasOperation(SkillProgramEffectOp.ObserveDamageSourceHandAndGive) ||
        GetProgramConversionPolarity(candidate.OwnerSeat,candidate.SkillId)==SkillPolarity.Yin &&
        context.Window==SkillProgramTriggerWindow.AfterDamageApplied && context.TargetSeat==candidate.OwnerSeat &&
        ActiveDamageTrigger?.Id==context.ParentFrameId && CurrentDamageAttempt is { IsSourceLess: false, DamageAmount: > 0 } &&
        context.SourceSeat is {} source && source!=candidate.OwnerSeat &&
        _players[source].IsAlive && GetHand(_players[candidate.OwnerSeat]).Count+GetEquipment(_players[candidate.OwnerSeat]).Count>0;
    private SkillProgramStepOutcome DrawProgramToHandCount(ProgramSkillFrame f,int seat,int limit)
    {
        if (!_players[seat].IsAlive) return SkillProgramStepOutcome.Continue;
        DrawProgramCards(f.Id,seat,Math.Max(0,limit-GetHand(_players[seat]).Count),null,null,SkillProgramCardSetVisibility.Private,new("skill-program.hand-count.draw"));
        return AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat);
    }
    private SkillProgramStepOutcome BeginConvertingGiftDamage(ProgramSkillFrame f,int limit)
    {
        var active=GetActiveProgramFrame(f.Id);
        if(active.ConvertingGift is not null || active.TriggerId is not null || active.SelectedCardIds is not [var cardId] || active.SelectedTargetSeats is not [var target] ||
            !GetProgramTargetSeats(active.OwnerSeat,SkillProgramTargetKind.OtherLivingHighestHand).Contains(target) || !OwnedGiftCard(active.OwnerSeat,cardId) ||
            GetProgramConversionPolarity(active.OwnerSeat,active.SkillId)!=SkillPolarity.Yang) throw new InvalidOperationException("Gift damage lost its accepted legal activation.");
        CommitProgramConversionPolarity(active);active=GetActiveProgramFrame(f.Id);
        active=active with{ConvertingGift=new(false,"gift-movement",target,limit,[],cardId)};ReplaceRuntimeTop(active);
        MoveConvertingGiftCard(active,cardId);
        if(AwaitProgramBoundCardMovements(active.Id,active.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool OwnedGiftCard(int owner,int cardId)
    {
        var location=_cardZones.GetLocation(cardId);return location.OwnerSeat==owner && location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment;
    }
    private void MoveConvertingGiftCard(ProgramSkillFrame f,int cardId)
    {
        var draft=f.ConvertingGift!;var from=_cardZones.GetLocation(cardId);var card=_cardZones.CardsAt(from).Single(c=>c.Id==cardId);
        MoveCard(card,from,CardLocation.Hand(draft.RecipientSeat),new("skill-program.converting-gift.give"));
        var ordinal=_cardMovements.LastOrDefault(m=>m.CardId==cardId && m.From==from && m.To==CardLocation.Hand(draft.RecipientSeat) && m.Reason.Value=="skill-program.converting-gift.give")?.Sequence;
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{ConvertingGift=f.ConvertingGift! with{GiftCardId=cardId,GiftOrdinal=ordinal}});
        if(draft.Observe && ordinal is {} moved)
        {
            var obligation=new GiftHandRetentionObligation(++_giftHandRetentionSequence,new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId),_turnNumber,draft.RecipientSeat,cardId,moved,draft.HandLimit);
            _giftHandRetentionObligations.Add(obligation);AppendGiftRetentionToActiveEnding(obligation);AdvanceEventRulesAndQueueFact(new GiftHandRetentionScheduledEvent(obligation));
        }
    }
    private SkillProgramStepOutcome BeginDamageSourceObservationGift(ProgramSkillFrame f,int limit)
    {
        f=GetActiveProgramFrame(f.Id);
        if(f.ConvertingGift is not null || f.WindowContext is not {Window:SkillProgramTriggerWindow.AfterDamageApplied,SourceSeat:{} source} ||
            source==f.OwnerSeat || f.WindowContext.TargetSeat!=f.OwnerSeat || ActiveDamageTrigger?.Id!=f.WindowContext.ParentFrameId ||
            CurrentDamageAttempt is not { IsSourceLess: false, DamageAmount: > 0 } || !_players[source].IsAlive || GetProgramConversionPolarity(f.OwnerSeat,f.SkillId)!=SkillPolarity.Yin)
            throw new InvalidOperationException("Private observation lost its exact other damage source.");
        var ids=Array.AsReadOnly(GetHand(_players[source]).Select(c=>c.Id).ToArray());
        ReplaceRuntimeTop(f=f with{ConvertingGift=new(true,"observe",source,limit,ids)});
        AdvanceEventRulesAndQueueFact(new ProgramPrivateHandObservedEvent(f.Id,f.OwnerSeat,source,ids.Count));
        PublishConvertingGift(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private CardSnapshot[] GetConvertingGiftPrivatelyViewedCards(int viewerSeat)=>_resolutionStack.OfType<ProgramSkillFrame>()
        .Where(f=>f.OwnerSeat==viewerSeat && f.ConvertingGift is {Observe:true,Stage:"observe"})
        .SelectMany(f=>f.ConvertingGift!.ObservedHandIds.Select(id=>_cardZones.CardsAt(CardLocation.Hand(f.ConvertingGift.RecipientSeat)).SingleOrDefault(c=>c.Id==id)))
        .Where(c=>c is not null).Select(c=>ToSnapshot(c!)).ToArray();
    private IReadOnlyList<PromptChoice> ConvertingGiftChoices(ProgramSkillFrame f)
    {
        var d=f.ConvertingGift!; Dictionary<string,string> P(string action)=>new(){["program-action"]="converting-gift",["gift-action"]=action,["frame-id"]=f.Id.ToString()};
        if(d.Stage=="observe")return [new(new($"converting-gift.{f.Id}.observed"),"已观看手牌，选择赠牌",[],[],P("observed"))];
        if(d.Stage=="give-choice")return GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(_players[f.OwnerSeat])).Select(c=>new PromptChoice(new($"converting-gift.{f.Id}.give.{c.Id}"),$"交给 {_players[d.RecipientSeat].Name}【{c.DisplayName}】",[c.Id],[d.RecipientSeat],P("give"))).ToArray();
        if(d.Stage=="reward-choice")return _players.Where(p=>p.IsAlive).Select(p=>new PromptChoice(new($"converting-gift.{f.Id}.reward.{p.Seat}"),$"令 {p.Name} 将手牌摸至{d.HandLimit}",[],[p.Seat],P("reward"))).Append(new(new($"converting-gift.{f.Id}.reward.skip"),"不发动摸牌奖励",[],[],P("reward-skip"))).ToArray();
        throw new InvalidOperationException("Gift stage has no player choices.");
    }
    private void PublishConvertingGift(ProgramSkillFrame f)
    {
        var choices=ConvertingGiftChoices(f);if(choices.Count==0){FinishConvertingGift(f);return;}
        var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,f.ConvertingGift!.Stage=="observe" ? $"{skill.Name}：观看伤害来源手牌。":$"{skill.Name}：选择结算。",choices.SelectMany(c=>c.Cards).ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private void ResolveConvertingGiftChoice(PromptChoice choice)
    {
        var f=GetActiveProgramFrame(_resolutionStack[^1].Id);var d=f.ConvertingGift??throw new InvalidOperationException("Gift choice lost its owner draft.");
        if(choice.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString() || !ConvertingGiftChoices(f).Any(c=>c.Id==choice.Id))throw new InvalidOperationException("Gift choice is stale.");
        ClearPendingDecision();
        if(!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)){FinishConvertingGift(f);return;}
        if(d.Stage=="observe")
        {ReplaceRuntimeTop(f=f with{ConvertingGift=d with{Stage="give-choice",ObservedHandIds=[]}});PublishConvertingGift(f);return;}
        if(d.Stage=="give-choice")
        {
            if(!_players[d.RecipientSeat].IsAlive || choice.Cards is not [var id] || !OwnedGiftCard(f.OwnerSeat,id)){FinishConvertingGift(f);return;}
            CommitProgramConversionPolarity(f);f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{ConvertingGift=f.ConvertingGift! with{Stage="gift-movement",ObservedHandIds=[],GiftCardId=id}});
            MoveConvertingGiftCard(f,id);if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(f.Id);return;
        }
        if(d.Stage=="reward-choice")
        {
            if(choice.Parameters.GetValueOrDefault("gift-action")=="reward-skip"){FinishConvertingGift(f);return;}
            if(choice.Targets is not [var recipient] || !_players[recipient].IsAlive)throw new InvalidOperationException("Gift death reward target is invalid.");
            ReplaceRuntimeTop(f=f with{ConvertingGift=d with{Stage="reward-movement"}});
            DrawProgramToHandCount(f,recipient,d.HandLimit);if(_resolutionStack.LastOrDefault()?.Id==f.Id)AdvanceRuntimeProgram(f.Id);return;
        }
        throw new InvalidOperationException("Invalid gift response stage.");
    }
    private bool TryResumeConvertingGift(long frameId)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id!=frameId || f.ConvertingGift is not {} d)return false;
        if(f.PendingMovementContinuation is not null) return false;
        f=GetActiveProgramFrame(frameId);d=f.ConvertingGift!;
        if(d.Stage is "observe" or "give-choice" or "reward-choice")return true;
        if(f.AttackAttempt is not null)return false;
        if(_winner!=Winner.None || !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)){FinishConvertingGift(f);return true;}
        if(d.Stage=="gift-movement")
        {
            if(d.Observe || d.GiftOrdinal is null || !_players[d.RecipientSeat].IsAlive){FinishConvertingGift(f);return true;}
            ReplaceRuntimeTop(f=f with{ConvertingGift=d with{Stage="damage"}});BeginProgramSkillDamage(f,d.RecipientSeat,1);return true;
        }
        if(d.Stage=="damage")
        {
            if(!d.ExactDamageDeath){FinishConvertingGift(f);return true;}
            ReplaceRuntimeTop(f=f with{ConvertingGift=d with{Stage="reward-choice"}});PublishConvertingGift(f);return true;
        }
        if(d.Stage=="reward-movement"){FinishConvertingGift(f);return true;}
        throw new InvalidOperationException("Unrecognized converting gift cursor.");
    }
    private void FinishConvertingGift(ProgramSkillFrame f)
    {ReplaceRuntimeTop(f with{ConvertingGift=null});AdvanceRuntimeProgram(f.Id);}
    private void MarkConvertingGiftExactDeath(long deathFrameId,int victimSeat)
    {
        foreach(var due in _giftHandRetentionObligations.Where(d=>d.Source.OwnerSeat==victimSeat).ToArray())
        {_giftHandRetentionObligations.Remove(due);AdvanceEventRulesAndQueueFact(new GiftHandRetentionConsumedEvent(due.Id,victimSeat,false));}
        var death=_resolutionStack.OfType<DeathFrame>().Single(f=>f.Id==deathFrameId);
        var dying=_resolutionStack.OfType<DyingFrame>().SingleOrDefault(f=>f.Id==death.ParentFrameId);
        if(dying is not {Continuation:DyingContinuationKind.Damage})return;
        var damage=_resolutionStack.OfType<DamageFrame>().SingleOrDefault(f=>f.Id==dying.ParentFrameId);
        if(damage is null || damage.TargetSeat!=victimSeat || damage.Amount<=0)return;
        var program=_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==damage.ParentFrameId);
        if(program?.ConvertingGift is not {Observe:false,Stage:"damage"} d || d.RecipientSeat!=victimSeat || damage.SourceSeat!=program.OwnerSeat)return;
        ReplaceRuntimeFrame(program.Id,program with{ConvertingGift=d with{ExactDamageDeath=true}});
    }
    private static TurnEndingBoundaryItem CreateGiftRetentionEndingItem(GiftHandRetentionObligation due)=>
        new(TurnEndingBoundaryItemKind.GiftRetention,0,$"program:{due.Source.SkillId}:{due.Source.BindingId}:{due.Source.SkillInstanceId}:due:{due.Id}",RetentionId:due.Id,RetentionOwnerSeat:due.Source.OwnerSeat);
    private void AppendGiftRetentionEndingItems(List<TurnEndingBoundaryItem> items)
    {
        foreach(var due in _giftHandRetentionObligations.Where(d=>d.CreatedTurn==_turnNumber).OrderBy(d=>d.Id))
            items.Add(CreateGiftRetentionEndingItem(due));
    }
    private void AppendGiftRetentionToActiveEnding(GiftHandRetentionObligation due)
    {
        var frame=_resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(f=>f.TurnNumber==due.CreatedTurn && f.OwnerSeat==_currentSeat);
        if(frame is null || frame.Items.Any(item=>item.RetentionId==due.Id))return;
        // Keep the running item and completed prefix stable while its child adds a new due.
        var suffix=frame.Items.Skip(frame.ItemIndex+1).Append(CreateGiftRetentionEndingItem(due))
            .OrderBy(item=>(((item.Candidate?.OwnerSeat ?? item.RetentionOwnerSeat!.Value)-frame.OwnerSeat+_players.Count)%_players.Count))
            .ThenByDescending(item=>item.Priority).ThenBy(item=>item.StableIdentity,StringComparer.Ordinal);
        var items=Array.AsReadOnly(frame.Items.Take(frame.ItemIndex+1).Concat(suffix).ToArray());
        ReplaceRuntimeFrame(frame.Id,frame with{Items=items});
    }
    private bool ConsumeGiftRetentionEndingItem(TurnEndingBoundaryFrame frame,TurnEndingBoundaryItem item)
    {
        var due=_giftHandRetentionObligations.SingleOrDefault(d=>d.Id==item.RetentionId);
        if(due is null){AdvanceTurnEndingBoundaryCursor(frame);return false;}
        if(due.CreatedTurn!=_turnNumber || item.RetentionOwnerSeat!=due.Source.OwnerSeat)throw new InvalidOperationException("Gift retention obligation lost its exact current Ending.");
        _giftHandRetentionObligations.Remove(due);
        var applied=_players[due.Source.OwnerSeat].IsAlive && !_cardMovements.Any(m=>m.Sequence>due.GiftOrdinal && m.CardId==due.CardId && m.From==CardLocation.Hand(due.RecipientSeat) && m.To!=m.From);
        AdvanceEventRulesAndQueueFact(new GiftHandRetentionConsumedEvent(due.Id,due.Source.OwnerSeat,applied));
        if(applied)
        {
            DrawCards(_players[due.Source.OwnerSeat],Math.Max(0,due.HandLimit-GetHand(_players[due.Source.OwnerSeat]).Count),true,new("skill-program.gift-retention.draw"));
            if(TryBeginCardsMovedProgramWindow())return true;
        }
        AdvanceTurnEndingBoundaryCursor(frame);return false;
    }
    private void ExpireGiftRetentionObligations()
    {
        foreach(var due in _giftHandRetentionObligations.Where(d=>d.CreatedTurn<=_turnNumber).ToArray())
        {_giftHandRetentionObligations.Remove(due);AdvanceEventRulesAndQueueFact(new GiftHandRetentionConsumedEvent(due.Id,due.Source.OwnerSeat,false));}
    }
    private void AssertConvertingGift(ProgramSkillFrame f,SkillProgramEffect paused)
    {
        if(f.ConvertingGift is not {} d)return;
        if(paused.Op!=(d.Observe?SkillProgramEffectOp.ObserveDamageSourceHandAndGive:SkillProgramEffectOp.GiveSelectedOwnedCardAndDamage) || d.HandLimit!=paused.Amount || d.RecipientSeat==f.OwnerSeat || !IsValidPlayerSeat(d.RecipientSeat) || d.ObservedHandIds.Distinct().Count()!=d.ObservedHandIds.Count ||
            d.Stage is not ("observe" or "give-choice" or "gift-movement" or "damage" or "reward-choice" or "reward-movement"))throw new InvalidOperationException("Converting gift has invalid instruction-owned draft.");
        if(d.ObservedHandIds.Count>0 && (!d.Observe || d.Stage!="observe"))throw new InvalidOperationException("Private hand observation escaped its viewer-owned cursor.");
    }
}
