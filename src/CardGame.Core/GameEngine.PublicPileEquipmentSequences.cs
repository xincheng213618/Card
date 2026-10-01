namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost:IGameDomainProgramHost
    {public SkillProgramStepOutcome ExecuteGameDomain(SkillProgramEffect e,ProgramSkillFrame f)=>engine.ExecuteGameDomain(e,f);}
    private IEnumerable<Card> DomainOwnedCards(ProgramSkillFrame f,int seat)=>GetHand(_players[seat]).Concat(GetEquipment(_players[seat])).Where(c=>seat!=f.OwnerSeat||!IsActiveProgramSourceEquipmentCard(seat,f.SkillId,f.SkillInstanceId,c));
    // Association is fixed once: exact instance first, otherwise the ordinal-first
    // grant of the referenced skill with the consumer's SourceId. Do not combine piles.
    private (string? Instance,CardLocation? Location) SelectDomainReference(ProgramSkillFrame f,string skill)
    {
        var grants=_players[f.OwnerSeat].SkillGrants.Grants;
        var consumer=grants.FirstOrDefault(g=>g.SkillId==f.SkillId&&g.SkillInstanceId==f.SkillInstanceId);
        var selected=grants.Where(g=>g.SkillId==skill&&(g.SkillInstanceId==f.SkillInstanceId||consumer is not null&&g.SourceId==consumer.SourceId))
            .OrderByDescending(g=>g.SkillInstanceId==f.SkillInstanceId).ThenBy(g=>g.SkillInstanceId,StringComparer.Ordinal).ThenBy(g=>g.GrantId,StringComparer.Ordinal).FirstOrDefault();
        if(selected is null)return (null,null);
        return (selected.SkillInstanceId,_publicPersistentPiles.GetValueOrDefault((f.OwnerSeat,skill,selected.SkillInstanceId))?.Location);
    }
    private PublicPersistentPileSource? DomainReferencedPile(ProgramSkillFrame f,ProgramDomainCrossingDraft d)=>
        d.ReferenceSkillId is {} skill&&d.ReferenceSkillInstanceId is {} instance&&
        _publicPersistentPiles.GetValueOrDefault((f.OwnerSeat,skill,instance)) is {} source&&
        (d.ReferencePileLocation is null||source.Location==d.ReferencePileLocation)?source:null;
    private PublicPersistentPileSource? SequencePile(ProgramSkillFrame f)=>PublicPileSources(f.OwnerSeat).SingleOrDefault(s=>s.SkillId==f.SkillId&&s.SkillInstanceId==f.PileEquipment!.PileInstance&&s.Location==f.PileEquipment.PileLocation);
    private SkillProgramStepOutcome ExecuteGameDomain(SkillProgramEffect e,ProgramSkillFrame f)
    {
        if(e.Op==SkillProgramEffectOp.ResolveFirstGameDomainCrossing)
        {var fact=FirstDomainFact(f.WindowContext!)??throw new InvalidOperationException("A domain reward lost its first actual crossing.");var reference=SelectDomainReference(f,e.SkillIds.Single());ReplaceRuntimeTop(f=f with{DomainCrossing=new("target",fact.MovedOut,ReferenceSkillId:e.SkillIds.Single(),ReferenceSkillInstanceId:reference.Instance,ReferencePileLocation:reference.Location)});PublishDomainChoice(f);return SkillProgramStepOutcome.AwaitChoice;}
        var source=EnsurePublicPileSource(f,int.MaxValue);
        ReplaceRuntimeTop(f=f with{PileEquipment=new(e.Op==SkillProgramEffectOp.StoreArbitraryOwnedPublicPile?"store-choice":"gain",source.Location,source.SkillInstanceId,[])});
        if(e.Op==SkillProgramEffectOp.StoreArbitraryOwnedPublicPile){PublishDomainChoice(f);return SkillProgramStepOutcome.AwaitChoice;}
        var ids=PublicPileCards(source).Where(c=>!EquipmentCatalog.IsEquipment(c.Kind)).Select(c=>c.Id).ToArray();
        ReplaceRuntimeTop(f=f with{PileEquipment=f.PileEquipment! with{Stage="recipient"},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        if(ids.Length>0)MoveCards(ids.Select(id=>_cardZones.CardsAt(source.Location).Single(c=>c.Id==id)).ToArray(),source.Location,CardLocation.Hand(f.OwnerSeat),new("skill-program.public-pile.non-equipment-gain"));
        if(!TryBeginCardsMovedProgramWindow())ReturnRuntimeProgramMovement(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private bool CanUseSequenceEquipment(ProgramSkillFrame f,int seat,Card card)
    {
        var actor=_players[seat];return actor.IsAlive&&actor.Hp>0&&EquipmentCatalog.IsEquipment(card.Kind)&&_cardZones.GetLocation(card.Id)==f.PileEquipment!.PileLocation&&
            actor.EquipmentSlotCapacity(EquipmentCatalog.Get(card.Kind).Slot)>0&&!IsCardUseForbidden(seat,card.Kind,CardActionType.Use)&&!IsSelfTargetForbiddenAction(actor,card.Kind,[seat])&&!IsResponseEntityRestricted(seat,card.Id)&&!IsPlayPhasePhysicalCardRestricted(actor,card);
    }
    private IReadOnlyList<PromptChoice> DomainChoices(ProgramSkillFrame f)
    {
        Dictionary<string,string> Params(string action)=>new(){["program-action"]="game-domain",["domain-action"]=action,["frame-id"]=f.Id.ToString()};
        PromptChoice Choice(string action,string label,IReadOnlyList<int> cards,IReadOnlyList<int> targets)=>new(new($"game-domain.{f.Id}.{action}.cards-{string.Join('-',cards)}.targets-{string.Join('-',targets)}"),label,cards,targets,Params(action));
        if(f.DomainCrossing is {} d)
        {
            if(d.Stage=="target")return _players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat).Select(p=>Choice("target","选择 "+p.Name,[],[p.Seat])).ToArray();
            if(d.Stage=="discard")return DomainOwnedCards(f,d.ParticipantIndex==0?f.OwnerSeat:d.OtherSeat).Select(c=>Choice("discard","弃置【"+c.DisplayName+"】",[c.Id],[])).ToArray();
            if(d.Stage=="heal")
            {
                var x=DomainReferencedPile(f,d) is {} source?PublicPileCards(source).Count:0;
                return new[]{f.OwnerSeat,d.OtherSeat}.Where(s=>_players[s].IsAlive&&_players[s].Hp<_players[s].MaxHp&&GetHand(_players[s]).Count==x).Select(s=>Choice("heal","回复 "+_players[s].Name+" 1点体力",[],[s])).Append(Choice("skip-heal","不回复体力",[],[])).ToArray();
            }
        }
        var p=f.PileEquipment!;var pile=SequencePile(f);
        if(p.Stage=="store-choice")return DomainOwnedCards(f,f.OwnerSeat).Where(c=>!p.SelectedIds.Contains(c.Id)).Select(c=>Choice("store-card","存入【"+c.DisplayName+"】",[c.Id],[])).Append(Choice("store-finish","完成存牌",[],[])).ToArray();
        if(p.Stage=="recipient")return _players.Where(actor=>actor.IsAlive&&pile is not null&&PublicPileCards(pile).Any(c=>CanUseSequenceEquipment(f,actor.Seat,c))).Select(actor=>Choice("recipient","令 "+actor.Name+" 使用装备",[],[actor.Seat])).ToArray();
        if(p.Stage=="equip-choice")return pile is null?[]:PublicPileCards(pile).Where(c=>CanUseSequenceEquipment(f,p.RecipientSeat!.Value,c)).Select(c=>Choice("equip","使用【"+c.DisplayName+"】",[c.Id],[p.RecipientSeat!.Value])).ToArray();
        return [];
    }
    private void PublishDomainChoice(ProgramSkillFrame f)
    {
        var choices=DomainChoices(f);if(choices.Count==0){ResumeGameDomain(f.Id);return;}
        var seat=f.DomainCrossing is {Stage:"discard"} d?(d.ParticipantIndex==0?f.OwnerSeat:d.OtherSeat):f.PileEquipment is {Stage:"equip-choice",RecipientSeat:{} actor}?actor:f.OwnerSeat;
        var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,seat,skill.Description,choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),seat){PromptId=CreatePromptId(),IsPrivate=f.DomainCrossing is {Stage:"discard"}||f.PileEquipment is {Stage:"store-choice"},Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[seat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    private void ResolveGameDomainChoice(PromptChoice choice)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("A domain choice lost its owning frame.");
        if(choice.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString()||!DomainChoices(f).Any(c=>c.Id==choice.Id))throw new InvalidOperationException("A domain choice is stale.");
        ClearPendingDecision();var action=choice.Parameters["domain-action"];
        if(f.DomainCrossing is {} d)
        {
            if(action=="target"){ReplaceRuntimeTop(f=f with{DomainCrossing=d with{Stage="participants",OtherSeat=choice.Targets.Single()}});ResumeGameDomain(f.Id);return;}
            if(action=="discard")
            {
                var seat=d.ParticipantIndex==0?f.OwnerSeat:d.OtherSeat;var id=choice.Cards.Single();
                ReplaceRuntimeTop(f=f with{DomainCrossing=d with{Stage="participants",ParticipantIndex=d.ParticipantIndex+1},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
                MoveProgramCardsFromMultipleSources([id],CardLocation.DiscardPile,new("skill-program.game-domain.discard"));
                if(!TryBeginCardsMovedProgramWindow())ReturnRuntimeProgramMovement(f.Id);return;
            }
            if(action=="heal"||action=="skip-heal")
            {ReplaceRuntimeTop(f=f with{DomainCrossing=d with{Stage="complete"}});if(action=="heal")new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,choice.Targets.Single(),1,null,null);if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue)FinishGameDomain(f);return;}
        }
        var p=f.PileEquipment!;
        if(action=="store-card"){ReplaceRuntimeTop(f=f with{PileEquipment=p with{SelectedIds=Array.AsReadOnly(p.SelectedIds.Append(choice.Cards.Single()).ToArray())}});PublishDomainChoice(f);return;}
        if(action=="store-finish")
        {var ids=p.SelectedIds.Where(id=>DomainOwnedCards(f,f.OwnerSeat).Any(c=>c.Id==id)).ToArray();ReplaceRuntimeTop(f=f with{PileEquipment=p with{Stage="complete",SelectedIds=[]},PendingMovementContinuation=new(f.OwnerSeat,0,null)});MoveProgramCardsFromMultipleSources(ids,p.PileLocation,new("skill-program.public-pile.store-owned"));if(!TryBeginCardsMovedProgramWindow())ReturnRuntimeProgramMovement(f.Id);return;}
        if(action=="recipient"){ReplaceRuntimeTop(f=f with{PileEquipment=p with{Stage="equip-choice",RecipientSeat=choice.Targets.Single()}});PublishDomainChoice(f);return;}
        if(action=="equip")
        {
            var id=choice.Cards.Single();var card=_cardZones.CardsAt(p.PileLocation).Single(c=>c.Id==id);var actor=_players[p.RecipientSeat!.Value];
            ReplaceRuntimeTop(f=f with{PileEquipment=p with{Stage="equipment-use",ActiveCardId=id},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            var use=BeginCardUse(card,actor.Seat,[],conversionSource:new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId));
            f=_resolutionStack.OfType<ProgramSkillFrame>().Single(x=>x.Id==f.Id);ReplaceRuntimeFrame(f.Id,f with{PileEquipment=f.PileEquipment! with{ActiveUseFrameId=use}});
            MoveCard(card,p.PileLocation,CardLocation.Processing,CardMoveReasons.EquipmentUse);
            f=_resolutionStack.OfType<ProgramSkillFrame>().Single(x=>x.Id==f.Id);ReplaceRuntimeFrame(f.Id,f with{PileEquipment=f.PileEquipment! with{HadActualEquipmentUse=true}});
            if(!TryBeginEquipmentTargetPrograms(actor,card,use))CompleteEquipmentUse(actor,card,use);return;
        }
        throw new InvalidOperationException("Unrecognized domain choice.");
    }
    private void FinishGameDomain(ProgramSkillFrame f){ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with{DomainCrossing=null,PileEquipment=null});AdvanceRuntimeProgram(f.Id);}
    private bool ResumeGameDomain(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f||f.Id!=id||f.DomainCrossing is null&&f.PileEquipment is null)return false;
        if(f.PendingMovementContinuation is not null)return false;
        if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive){FinishGameDomain(f);return true;}
        if(f.DomainCrossing is {} d)
        {
            if(d.Stage is "target" or "discard" or "heal"){if(DomainChoices(f).Count==0){if(d.Stage=="discard"){ReplaceRuntimeTop(f=f with{DomainCrossing=d with{Stage="participants",ParticipantIndex=d.ParticipantIndex+1}});return ResumeGameDomain(id);}FinishGameDomain(f);return true;}if(_pendingDecision is null)PublishDomainChoice(f);return true;}
            if(d.Stage=="complete"){FinishGameDomain(f);return true;}
            while(d.ParticipantIndex<2)
            {
                var seat=d.ParticipantIndex==0?f.OwnerSeat:d.OtherSeat;
                if(!_players[seat].IsAlive){ReplaceRuntimeTop(f=f with{DomainCrossing=d=d with{ParticipantIndex=d.ParticipantIndex+1}});continue;}
                if(!d.MovedOut)
                {if(!DomainOwnedCards(f,seat).Any()){ReplaceRuntimeTop(f=f with{DomainCrossing=d=d with{ParticipantIndex=d.ParticipantIndex+1}});continue;}ReplaceRuntimeTop(f=f with{DomainCrossing=d with{Stage="discard"}});PublishDomainChoice(f);return true;}
                ReplaceRuntimeTop(f=f with{DomainCrossing=d with{ParticipantIndex=d.ParticipantIndex+1}});DrawProgramCards(f.Id,seat,1,null,null,SkillProgramCardSetVisibility.Private,new("skill-program.game-domain.draw"));if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.AwaitChild)return true;f=GetActiveProgramFrame(id);d=f.DomainCrossing!;
            }
            ReplaceRuntimeTop(f=f with{DomainCrossing=d with{Stage="heal"}});PublishDomainChoice(f);return true;
        }
        var p=f.PileEquipment!;
        if(p.Stage=="complete"||p.Stage=="hp-paid"){FinishGameDomain(f);return true;}
        if(p.Stage=="equipment-use")return true;
        if(p.Stage=="store-choice"){if(_pendingDecision is null)PublishDomainChoice(f);return true;}
        if(p.Stage is "recipient" or "equip-choice")
        {
            if(p.RecipientSeat is {} actor&&!_players[actor].IsAlive){FinishGameDomain(f);return true;}
            if(DomainChoices(f).Count>0){if(_pendingDecision is null)PublishDomainChoice(f);return true;}
            if(!p.HadActualEquipmentUse){FinishGameDomain(f);return true;}
            ReplaceRuntimeTop(f=f with{PileEquipment=p with{Stage="hp-paid"}});
            if(new ProgramSkillHost(this).LoseHp(f.Id,f.SkillId,p.RecipientSeat!.Value,1)==SkillProgramStepOutcome.Continue&&AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue)FinishGameDomain(f);return true;
        }
        throw new InvalidOperationException("A game-domain operation lost its typed cursor.");
    }
    private void ContinueProgramAfterPileEquipmentUse(long completedUseId)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame {PileEquipment:{Stage:"equipment-use"} p} f)return;
        if(p.ActiveUseFrameId!=completedUseId)throw new InvalidOperationException("An equipment use returned to a different owning sequence.");
        ReplaceRuntimeTop(f=f with{PileEquipment=p with{Stage="equip-choice",ActiveUseFrameId=null,ActiveCardId=null}});
        if(!TryBeginCardsMovedProgramWindow())ReturnRuntimeProgramMovement(f.Id);
    }
    private bool PileEquipmentFrameRidesOn(ResolutionFrame ride,ResolutionFrame beneath)=>
        ride is CardUseFrame {Action:{} action} use&&beneath is ProgramSkillFrame {PileEquipment:{Stage:"equipment-use",ActiveUseFrameId:{} id,ActiveCardId:{} card,RecipientSeat:{} recipient}} parent&&
        use.Id==id&&use.CardId==card&&action.ActorSeat==recipient&&EquipmentCatalog.IsEquipment(action.EffectiveKind)&&
        action.PhysicalCards is [var cost]&&cost.CardId==card&&cost.From==parent.PileEquipment.PileLocation&&
        action.ConversionChain.Any(s=>s.OwnerSeat==parent.OwnerSeat&&s.SkillId==parent.SkillId&&s.SkillInstanceId==parent.SkillInstanceId&&s.BindingId==GetProgramBindingId(parent));
    private PromptChoice SelectAiDomainChoice(PendingDecision pending,ProgramSkillFrame f)=>pending.Choices
        .OrderByDescending(c=>c.Targets.Count==1&&AreProgramDistributionAllies(_players[pending.PlayerSeat],_players[c.Targets[0]]))
        .ThenBy(c=>c.Parameters.GetValueOrDefault("domain-action")=="store-finish"?(f.PileEquipment?.SelectedIds.Count>=3?0:2):1)
        .ThenBy(c=>c.Cards.Count==1?GetKeepValue(_cardZones.CardsAt(_cardZones.GetLocation(c.Cards[0])).Single(x=>x.Id==c.Cards[0]),_players[pending.PlayerSeat]):0).First();
}
