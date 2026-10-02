namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IDistinctFactionDiscardHost
    { public SkillProgramStepOutcome BeginDistinctFactionDiscards(ProgramSkillFrame f) => engine.BeginDistinctFactionDiscards(f); }
    private bool HasDiscardableHe(int seat) => GetHand(_players[seat]).Count + GetEquipment(_players[seat]).Count > 0;
    private bool HasPayableDistinctFactionOwnerHe(int seat,string skillId,string instanceId) =>
        GetHand(_players[seat]).Count>0 || GetEquipment(_players[seat]).Any(c=>!IsActiveProgramSourceEquipmentCard(seat,skillId,instanceId,c));
    private bool DistinctFactionPlanValid(int owner,IReadOnlyList<int> seats) => seats.Distinct().Count()==seats.Count &&
        seats.All(s=>IsValidPlayerSeat(s)&&s!=owner&&_players[s].IsAlive&&HasDiscardableHe(s)) &&
        seats.Select(s=>GetEffectiveFactionId(_players[s])).Distinct(StringComparer.Ordinal).Count()==seats.Count;
    private bool DistinctFactionOwnerValid(ProgramSkillFrame f) =>
        _players[f.OwnerSeat].IsAlive && _winner==Winner.None && HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId);
    private void CancelDistinctFactionDiscards(ProgramSkillFrame f,string reason)
    {
        ClearPendingDecision();
        CancelProgramBindingAndCleanup(f,reason);
    }
    private SkillProgramStepOutcome BeginDistinctFactionDiscards(ProgramSkillFrame f)
    {
        if(f.DistinctFactionDiscards is not null)throw new InvalidOperationException("A participant discard already owns a draft.");
        if(!DistinctFactionOwnerValid(f)||!HasPayableDistinctFactionOwnerHe(f.OwnerSeat,f.SkillId,f.SkillInstanceId))
        {CancelDistinctFactionDiscards(f,"弃牌持有人、来源或必付牌已失效，技能剩余步骤取消。");return SkillProgramStepOutcome.AwaitChild;}
        ReplaceRuntimeTop(f with {DistinctFactionDiscards=new("select",Array.AsReadOnly(Array.Empty<int>()),Array.AsReadOnly(Array.Empty<int>()),0,Array.AsReadOnly(Array.Empty<ProgramDiscardReceipt>()))});
        PublishDistinctFactionSelection(GetActiveProgramFrame(f.Id));return SkillProgramStepOutcome.AwaitChoice;
    }
    private void PublishDistinctFactionSelection(ProgramSkillFrame f)
    {
        var d=f.DistinctFactionDiscards!;var choices=new List<PromptChoice>();
        foreach(var p in _players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat&&HasDiscardableHe(p.Seat)))
            if(DistinctFactionPlanValid(f.OwnerSeat,d.SelectedSeats.Append(p.Seat).ToArray()))
                choices.Add(new(new("participant-discard.select."+p.Seat),"选择 "+p.Name,[],[p.Seat],new Dictionary<string,string>{{"program-action","distinct-faction-select"}}));
        choices.Add(new(new("participant-discard.finish"),"完成选择（可不选择其他角色）",[],[],new Dictionary<string,string>{{"program-action","distinct-faction-finish"}}));
        PublishDistinctFactionPrompt(f,"选择有效势力互异的其他角色",choices);
    }
    private void PublishDistinctFactionPrompt(ProgramSkillFrame f,string text,IReadOnlyList<PromptChoice> choices)
    {
        var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,text,Array.AsReadOnly(choices.SelectMany(c=>c.Cards).Distinct().ToArray()),[],f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description),Choices=Array.AsReadOnly(choices.ToArray())};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    private IReadOnlyList<int> FreezeDiscardParticipantOrder(int owner,IReadOnlyList<int> chosen)
    {
        var order=new List<int>{owner};var seat=owner;
        for(var i=0;i<_playerCount-1;i++){seat=FindNextAliveSeat(seat);if(seat==owner)break;if(chosen.Contains(seat))order.Add(seat);}
        if(order.Count!=chosen.Count+1)throw new InvalidOperationException("Participant plan lost its actual action order.");
        return Array.AsReadOnly(order.ToArray());
    }
    private void ResolveDistinctFactionDiscardChoice(PromptChoice choice)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Missing participant discard frame.");
        var d=f.DistinctFactionDiscards??throw new InvalidOperationException("Missing participant discard draft.");
        var op=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect.Op;
        if(op!=SkillProgramEffectOp.DiscardDistinctFactionParticipants)throw new InvalidOperationException("Participant discard lost its exact instruction.");
        if(!DistinctFactionOwnerValid(f)||d.Stage=="select"&&!HasPayableDistinctFactionOwnerHe(f.OwnerSeat,f.SkillId,f.SkillInstanceId))
        {CancelDistinctFactionDiscards(f,"弃牌持有人、来源或必付牌已失效，技能剩余步骤取消。");return;}
        if(d.Stage=="select")
        {
            if(choice.Parameters.GetValueOrDefault("program-action")=="distinct-faction-select")
            {
                var selected=d.SelectedSeats.Concat(choice.Targets).ToArray();
                if(choice.Targets.Count!=1||!DistinctFactionPlanValid(f.OwnerSeat,selected))throw new InvalidOperationException("Participants must be distinct living owners with different effective factions and HE.");
                ClearPendingDecision();ReplaceRuntimeTop(f with{DistinctFactionDiscards=d with{SelectedSeats=Array.AsReadOnly(selected)}});PublishDistinctFactionSelection(GetActiveProgramFrame(f.Id));return;
            }
            if(choice.Parameters.GetValueOrDefault("program-action")!="distinct-faction-finish"||!DistinctFactionPlanValid(f.OwnerSeat,d.SelectedSeats))throw new InvalidOperationException("Discard participant selection became invalid.");
            ClearPendingDecision();ReplaceRuntimeTop(f with{DistinctFactionDiscards=d with{Stage="discard",ParticipantSeats=FreezeDiscardParticipantOrder(f.OwnerSeat,d.SelectedSeats)}});AdvanceRuntimeProgram(f.Id);return;
        }
        if(d.Stage!="discard"||d.ParticipantIndex>=d.ParticipantSeats.Count)throw new InvalidOperationException("Participant cost cursor is unavailable.");
        var owner=d.ParticipantSeats[d.ParticipantIndex];
        if(!_players[owner].IsAlive||!HasDiscardableHe(owner)){CancelOrSkipDistinctFactionPayment(f,d);return;}
        if(choice.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)||choice.Parameters.GetValueOrDefault("card-owner-seat")!=owner.ToString(System.Globalization.CultureInfo.InvariantCulture)||
           !Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"),out var zone)||zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||!int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"),out var slot))throw new InvalidOperationException("Participant discard does not match its published opaque source slot.");
        var source=new CardLocation(zone,owner);var cards=_cardZones.CardsAt(source);
        if(slot<0||slot>=cards.Count){CancelOrSkipDistinctFactionPayment(f,d);return;}
        var card=cards[slot];var hidden=zone==CardZoneKind.Hand&&owner!=f.OwnerSeat;
        if(!hidden&&(choice.Cards.Count!=1||choice.Cards[0]!=card.Id))throw new InvalidOperationException("Public participant cost identity changed.");
        if(zone==CardZoneKind.Equipment&&owner==f.OwnerSeat&&IsActiveProgramSourceEquipmentCard(owner,f.SkillId,f.SkillInstanceId,card)){CancelOrSkipDistinctFactionPayment(f,d);return;}
        // Capture the source owner's effective suit before the entity leaves its original HE zone.
        var suit=EffectiveSuit(_players[owner],card);
        var paidDestination=card.IsGeneralWeapon&&source.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:CardLocation.DiscardPile;
        var paidReason=new CardMoveReason("skill-program."+f.SkillId+".participant-discard");ClearPendingDecision();
        ReplaceRuntimeTop(f with{DistinctFactionDiscards=d with{Stage="discard-children",ParticipantIndex=d.ParticipantIndex+1},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var beforeMovement=_cardMovements.Count;
        MoveCard(card,source,CardLocation.DiscardPile,paidReason);
        var movement=_cardMovements.Skip(beforeMovement).Single(m=>m.CardId==card.Id&&m.From==source&&m.To==paidDestination&&m.Reason==paidReason);
        if(movement.CardId!=card.Id||movement.From!=source||movement.To!=paidDestination||movement.Reason!=paidReason)throw new InvalidOperationException("A discard receipt requires its actual movement.");
        f=GetActiveProgramFrame(f.Id);d=f.DistinctFactionDiscards!;
        ReplaceRuntimeTop(f with{DistinctFactionDiscards=d with{Receipts=Array.AsReadOnly(d.Receipts.Append(new ProgramDiscardReceipt(owner,card.Id,source,suit,movement.Sequence,movement.To==CardLocation.DiscardPile?null:movement.To)).ToArray())}});
        if(!TryBeginCardsMovedProgramWindow())ReturnRuntimeProgramMovement(f.Id);
    }
    private void CancelOrSkipDistinctFactionPayment(ProgramSkillFrame f,ProgramDistinctFactionDiscardDraft d)
    {
        ClearPendingDecision();
        // Owner payment is mandatory; only later unavailable participants may be skipped.
        if(d.ParticipantIndex==0)
        {CancelDistinctFactionDiscards(f,"本人必付牌已失效，技能剩余步骤取消。");return;}
        ReplaceRuntimeTop(f with{DistinctFactionDiscards=d with{ParticipantIndex=d.ParticipantIndex+1}});
        AdvanceRuntimeProgram(f.Id);
    }
    private PromptChoice SelectAiDistinctFactionDiscard(PendingDecision decision,ProgramSkillFrame f)
    {
        if(f.DistinctFactionDiscards!.Stage=="select")
        {
            var (id,thought)=_aiBrains[decision.PlayerSeat].ChooseHostileHandTargets(CreateSnapshot(decision.PlayerSeat),decision.Choices,_thoughtSequence++);
            AddThought(thought);return decision.Choices.Single(c=>c.Id==id);
        }
        var view=CreateSnapshot(decision.PlayerSeat);
        var visible=view.Players.SelectMany(p=>p.Equipment.Concat(p.Seat==decision.PlayerSeat?p.Hand:[])).ToDictionary(c=>c.Id);
        // Opaque foreign Hand slots share one estimate; only published faces participate in cost ordering.
        return decision.Choices.OrderBy(c=>c.Cards.Count==1&&visible.TryGetValue(c.Cards[0],out var card)?CardCatalog.Get(card.Kind).HandKeepValue:0d)
            .ThenBy(c=>c.Id.Value,StringComparer.Ordinal).First();
    }
    private bool ResumeDistinctFactionDiscards(long id)
    {
        var f=GetActiveProgramFrame(id);var d=f.DistinctFactionDiscards;if(d is null)return false;
        if(d.Stage is "discard-children" or "reward-children")
        {
            if(f.PendingMovementContinuation is not null)return true;
            if(TryBeginCardsMovedProgramWindow())return true;
            d=d with{Stage=d.Stage=="discard-children"?"discard":"reward"};ReplaceRuntimeTop(f with{DistinctFactionDiscards=d});f=GetActiveProgramFrame(id);
        }
        if(!DistinctFactionOwnerValid(f))
        {CancelDistinctFactionDiscards(f,"弃牌持有人或来源已失效，技能剩余步骤取消。");return true;}
        if(d.Stage=="select")
        {
            if(!HasPayableDistinctFactionOwnerHe(f.OwnerSeat,f.SkillId,f.SkillInstanceId))
                CancelDistinctFactionDiscards(f,"本人必付牌已失效，技能剩余步骤取消。");
            return true;
        }
        while(d.Stage=="discard"&&d.ParticipantIndex<d.ParticipantSeats.Count)
        {
            var seat=d.ParticipantSeats[d.ParticipantIndex];
            var costs=_players[seat].IsAlive?BuildOwnedCardPaymentChoices(id,f.OwnerSeat,seat,[CardZoneKind.Hand,CardZoneKind.Equipment]):[];
            if(costs.Count==0 && d.ParticipantIndex==0)
            {CancelDistinctFactionDiscards(f,"本人必付牌已失效，技能剩余步骤取消。");return true;}
            if(costs.Count==0){d=d with{ParticipantIndex=d.ParticipantIndex+1};ReplaceRuntimeTop(f with{DistinctFactionDiscards=d});continue;}
            PublishDistinctFactionPrompt(f,"依行动顺序弃置 "+_players[seat].Name+" 的一张牌",costs.Select(c=>c with{Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="distinct-faction-discard"}}).ToArray());return true;
        }
        if(d.Stage=="discard"){d=d with{Stage="reward"};ReplaceRuntimeTop(f with{DistinctFactionDiscards=d});}
        if(d.Stage=="reward")
        {
            var rewards=d.Receipts.Where(p=>p.EffectiveSuit==Suit.Spade).Select(p=>p.OwnerSeat).Distinct().ToArray();
            while(d.RewardIndex<rewards.Length)
            {
                var seat=rewards[d.RewardIndex];d=d with{RewardIndex=d.RewardIndex+1};
                if(!_players[seat].IsAlive){ReplaceRuntimeTop(f with{DistinctFactionDiscards=d});continue;}
                ReplaceRuntimeTop(f with{DistinctFactionDiscards=d with{Stage="reward-children"},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
                DrawProgramCards(id,seat,1,null,null,SkillProgramCardSetVisibility.Private,new("skill-program."+f.SkillId+".participant-reward"));
                if(!TryBeginCardsMovedProgramWindow())ReturnRuntimeProgramMovement(id);return true;
            }
            ReplaceRuntimeTop(f with{DistinctFactionDiscards=null});return false;
        }
        return true;
    }
    private void AssertDistinctFactionDiscardDraft(ProgramSkillFrame f,SkillProgramEffect paused)
    {
        if(f.DistinctFactionDiscards is not{} d)return;
        if(paused.Op!=SkillProgramEffectOp.DiscardDistinctFactionParticipants||f.WindowContext is not null||d.Stage is not("select" or "discard" or "discard-children" or "reward" or "reward-children")||
           d.SelectedSeats.Distinct().Count()!=d.SelectedSeats.Count||d.SelectedSeats.Any(s=>!IsValidPlayerSeat(s)||s==f.OwnerSeat)||
           d.ParticipantSeats.Distinct().Count()!=d.ParticipantSeats.Count||d.ParticipantSeats.Any(s=>!IsValidPlayerSeat(s))||d.ParticipantIndex<0||d.ParticipantIndex>d.ParticipantSeats.Count||
           d.Receipts.Select(p=>p.OwnerSeat).Distinct().Count()!=d.Receipts.Count||d.Receipts.Any(p=>!d.ParticipantSeats.Contains(p.OwnerSeat)||p.Source.OwnerSeat!=p.OwnerSeat||p.Source.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||!Enum.IsDefined(p.EffectiveSuit)||p.MovementSequence<=0||p.Destination is{} dest&&!(dest==CardLocation.OutsideGame&&p.Source.Zone==CardZoneKind.Equipment&&_cardZones.CardsAt(_cardZones.GetLocation(p.CardId)).Single(c=>c.Id==p.CardId).IsGeneralWeapon))||d.RewardIndex<0||d.RewardIndex>d.Receipts.Count(p=>p.EffectiveSuit==Suit.Spade))
            throw new InvalidOperationException("Participant discard lost its owning paid plan/cursor/receipt.");
        if(d.Stage=="select")
        {
            if(d.ParticipantSeats.Count!=0||d.ParticipantIndex!=0||d.Receipts.Count!=0||d.RewardIndex!=0)
                throw new InvalidOperationException("Unpaid participant selection cannot contain a paid plan or receipt.");
            return;
        }
        if(d.ParticipantSeats.Count!=d.SelectedSeats.Count+1||d.ParticipantSeats[0]!=f.OwnerSeat||
           !d.ParticipantSeats.Skip(1).ToHashSet().SetEquals(d.SelectedSeats)||
           d.ParticipantIndex>0&&d.Receipts.FirstOrDefault()?.OwnerSeat!=f.OwnerSeat||
           d.Receipts.Any(p=>Array.IndexOf(d.ParticipantSeats.ToArray(),p.OwnerSeat)>=d.ParticipantIndex)||
           d.Receipts.Select(p=>Array.IndexOf(d.ParticipantSeats.ToArray(),p.OwnerSeat)).Order().SequenceEqual(d.Receipts.Select(p=>Array.IndexOf(d.ParticipantSeats.ToArray(),p.OwnerSeat)))==false||
           d.Receipts.Select(p=>p.MovementSequence).Order().SequenceEqual(d.Receipts.Select(p=>p.MovementSequence))==false||
           d.Receipts.Any(p=>!_cardMovements.Any(m=>m.Sequence==p.MovementSequence&&m.CardId==p.CardId&&m.From==p.Source&&m.To==(p.Destination??CardLocation.DiscardPile)&&m.Reason.Value=="skill-program."+f.SkillId+".participant-discard"))||
           d.Stage=="discard-children"&&(d.ParticipantIndex==0||d.Receipts.LastOrDefault()?.OwnerSeat!=d.ParticipantSeats[d.ParticipantIndex-1])||
           d.Stage is "reward" or "reward-children"&&d.ParticipantIndex!=d.ParticipantSeats.Count||
           d.Stage=="reward-children"&&d.RewardIndex==0)
            throw new InvalidOperationException("Paid receipt must join its real discard movement and exact frozen participant cursor.");
    }
}
