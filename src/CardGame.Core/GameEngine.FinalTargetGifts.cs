namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IFinalTargetGiftHost
    {
        public SkillProgramStepOutcome GiveOwnedCardToOtherFinalTargetAndDraw(ProgramSkillFrame frame)=>engine.BeginFinalTargetGift(frame);
    }
    private CardUseFrame FinalTargetGiftUse(ProgramSkillFrame frame)
    {
        if(frame.WindowContext is not {Window:SkillProgramTriggerWindow.CardUseTargetsFinalized,CardUse:{} use} ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f=>f.Id==use.ParentCardUseFrameId) is not {Action:{Type:CardActionType.Use} action} parent ||
            action.ActionId!=use.CardActionId || GetProgramCardCategory(action.EffectiveKind)!=SkillProgramCardCategory.Trick ||
            parent.Action!.EffectiveDesignatedTargetSeats.Distinct().Count()<=1 || !parent.Action.EffectiveDesignatedTargetSeats.Contains(frame.OwnerSeat))
            throw new InvalidOperationException("Final-target gift lost its actual multi-target Trick action.");
        return parent;
    }
    private bool CanOfferFinalTargetGift(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context,ProgramInstructionFeatures features)
    {
        if(!features.HasOperation(SkillProgramEffectOp.GiveOwnedCardToOtherFinalTargetAndDraw))return true;
        if(context.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized || context.CardUse is not {} use ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f=>f.Id==use.ParentCardUseFrameId) is not {Action:{Type:CardActionType.Use} action} parent ||
            action.ActionId!=use.CardActionId || GetProgramCardCategory(action.EffectiveKind)!=SkillProgramCardCategory.Trick)return false;
        var targets=FinalTargetGiftSeats(parent);
        return targets.Count>1 && targets.Contains(candidate.OwnerSeat) && targets.Any(s=>s!=candidate.OwnerSeat) &&
            GetHand(_players[candidate.OwnerSeat]).Count+GetEquipment(_players[candidate.OwnerSeat]).Count>0;
    }
    private IReadOnlyList<int> FinalTargetGiftSeats(CardUseFrame use)=>use.Action!.EffectiveDesignatedTargetSeats.Distinct()
        .Where(s=>_players[s].IsAlive && use.IneffectiveTargetSeats?.Contains(s)!=true).ToArray();
    private IReadOnlyList<PromptChoice> FinalTargetGiftChoices(ProgramSkillFrame frame)
    {
        var seats=FinalTargetGiftSeats(FinalTargetGiftUse(frame)).Where(s=>s!=frame.OwnerSeat);
        return seats.SelectMany(seat=>BuildOwnedCardPaymentChoices(frame.Id,frame.OwnerSeat,frame.OwnerSeat,[CardZoneKind.Hand,CardZoneKind.Equipment])
            .Select(c=>c with{Id=new($"final-gift.frame-{frame.Id}.target-{seat}.{c.Id.Value}"),Targets=[seat],Description=_players[seat].Name+"："+c.Description,
                Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="final-target-gift"}})).ToArray();
    }
    private SkillProgramStepOutcome BeginFinalTargetGift(ProgramSkillFrame frame)
    {
        var action=FinalTargetGiftUse(frame).Action!;
        var choices=FinalTargetGiftChoices(frame);if(choices.Count==0)return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame with{FinalTargetGift=new(action.ActionId,"choice")});
        var skill=_contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,frame.OwnerSeat,"交给另一名最终目标一张手牌或装备牌，然后摸牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),frame.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(frame.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[frame.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ResolveFinalTargetGiftChoice(PromptChoice selected)
    {
        var frame=_resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Final gift lost owning frame.");
        if(frame.FinalTargetGift is not {Stage:"choice"} draft || draft.ActionId!=FinalTargetGiftUse(frame).Action!.ActionId)throw new InvalidOperationException("Final gift lost action.");
        var valid=FinalTargetGiftChoices(frame).SingleOrDefault(c=>c.Id==selected.Id) ?? throw new InvalidOperationException("Final gift no longer names legal payment/recipient.");
        var from=new CardLocation(Enum.Parse<CardZoneKind>(valid.Parameters["source-zone"]),frame.OwnerSeat);
        var card=_cardZones.CardsAt(from)[int.Parse(valid.Parameters["slot-index"],System.Globalization.CultureInfo.InvariantCulture)];
        var recipient=valid.Targets.Single();var to=CardLocation.Hand(recipient);var reward=EquipmentCatalog.IsEquipment(card.Kind)?2:1;
        ClearPendingDecision();ReplaceRuntimeTop(frame with{FinalTargetGift=draft with{Stage="gift-movement",RecipientSeat=recipient,CardId=card.Id,Reward=reward}});
        var beforeOrdinal=_movementSequence;
        MoveCard(card,from,to,new("skill-program.final-target-gift.give"));
        var ordinal=_cardMovements.LastOrDefault(m=>m.Sequence>beforeOrdinal && m.CardId==card.Id && m.From==from && m.To==to && m.Reason.Value=="skill-program.final-target-gift.give")?.Sequence;
        frame=GetActiveProgramFrame(frame.Id);ReplaceRuntimeTop(frame=frame with{FinalTargetGift=frame.FinalTargetGift! with{ReceiptOrdinal=ordinal}});
        AdvanceEventRulesAndQueueFact(new ProgramFinalTargetGiftCommittedEvent(frame.Id,draft.ActionId,frame.OwnerSeat,frame.SkillId,frame.SkillInstanceId,recipient,card.Id,reward,ordinal));
        if(AwaitProgramBoundCardMovements(frame.Id,frame.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(frame.Id);
    }
    private bool ResumeFinalTargetGift(long frameId)
    {
        var frame=GetActiveProgramFrame(frameId);if(frame.FinalTargetGift is not {} draft)return false;
        if(frame.PendingMovementContinuation is not null)return false;
        if(draft.Stage=="choice")return true;
        if(draft.Stage=="gift-movement" && draft.ReceiptOrdinal is not null && _players[frame.OwnerSeat].IsAlive && _winner==Winner.None)
        {
            ReplaceRuntimeTop(frame=frame with{FinalTargetGift=draft with{Stage="reward-movement"}});
            DrawProgramCards(frame.Id,frame.OwnerSeat,draft.Reward,null,null,SkillProgramCardSetVisibility.Private,new("skill-program.final-target-gift.draw"));
            if(AwaitProgramBoundCardMovements(frame.Id,frame.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(frame.Id);return true;
        }
        ReplaceRuntimeTop(frame with{FinalTargetGift=null});AdvanceRuntimeProgram(frame.Id);return true;
    }
    private void AssertFinalTargetGift(ProgramSkillFrame frame,SkillProgramEffect effect)
    {
        if(frame.FinalTargetGift is not {} draft)return;
        if(effect.Op!=SkillProgramEffectOp.GiveOwnedCardToOtherFinalTargetAndDraw || draft.ActionId!=FinalTargetGiftUse(frame).Action!.ActionId ||
            draft.Stage is not ("choice" or "gift-movement" or "reward-movement") ||
            draft.Stage!="choice" && (draft.RecipientSeat is not {} recipient || recipient==frame.OwnerSeat || !IsValidPlayerSeat(recipient) || draft.CardId is null || draft.Reward is <1 or >2))
            throw new InvalidOperationException("Final gift lost instruction-owned receipt/reward cursor.");
    }
}
