using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<PromptChoice> HappyDingzhouChoices(ProgramSkillFrame frame)
    {
        var receipt=frame.HappyDingzhou!;
        PromptChoice Choice(string option,string description,IReadOnlyList<int>? cards=null)=>new(
            new("happy-dingzhou."+frame.Id+"."+option),description,cards??[],[receipt.TargetSeat],
            new Dictionary<string,string>{{"program-action","happy-dingzhou"},{"option",option},{"frame-id",frame.Id.ToString(CultureInfo.InvariantCulture)}});
        if(receipt.Stage==HappyDingzhouStage.BranchChoice)
        {
            var choices=new List<PromptChoice>();
            if(ShenLsDingzhouTarget(frame.OwnerSeat,receipt.TargetSeat))choices.Add(Choice("field","交给其场上牌数等量的牌，然后获得其场上所有牌"));
            choices.Add(Choice("hand","获得其所有手牌，然后交给其等量的牌"));
            return choices;
        }
        if(receipt.Stage is HappyDingzhouStage.FieldGiftChoice or HappyDingzhouStage.HandReturnChoice)
            return BuildOwnedCardPaymentChoices(frame.Id,frame.OwnerSeat,frame.OwnerSeat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Transfer)
                .Where(choice=>choice.Cards is[var card]&&!receipt.Chosen.Contains(card)&&ShenLsGiveable(frame.OwnerSeat).Contains(card))
                .Select(choice=>Choice("give:"+choice.Cards.Single(),choice.Description+"（"+(receipt.Chosen.Count+1)+"/"+
                    (receipt.RequiredReturnCount??receipt.Amount)+"）",choice.Cards)).ToArray();
        return [];
    }
    private void PublishHappyDingzhou(ProgramSkillFrame frame)
    {
        var choices=HappyDingzhouChoices(frame);
        if(choices.Count==0){FinishHappyDingzhou(frame);return;}
        var skill=_contentRegistry.GetSkill(frame.SkillId);
        ReplaceRuntimeTop(frame=frame with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,frame.OwnerSeat,"定州：请选择。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),[frame.HappyDingzhou!.TargetSeat],frame.OwnerSeat)
            {PromptId=CreatePromptId(),IsPrivate=true,Choices=Array.AsReadOnly(choices.ToArray()),SkillPrompt=new(frame.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[frame.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    [ProgramAnswer("happy-dingzhou")] private void AnswerHappyDingzhou(PromptChoice answer)
    {
        var frame=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Happy Dingzhou lost its original prompt frame.");
        AssertHappyDingzhou(frame);
        var receipt=frame.HappyDingzhou??throw new InvalidOperationException("Happy Dingzhou lost its private receipt.");
        var choice=HappyDingzhouChoices(frame).SingleOrDefault(c=>c.Id==answer.Id&&c.Cards.SequenceEqual(answer.Cards)&&c.Targets.SequenceEqual(answer.Targets)&&
            c.Parameters.OrderBy(p=>p.Key).SequenceEqual(answer.Parameters.OrderBy(p=>p.Key)))??throw new InvalidOperationException("Happy Dingzhou requires the current issued physical choice.");
        ClearPendingDecision();ReplaceRuntimeTop(frame=frame with{Step=ResolutionFrameStep.ResolvingEffect});
        if(receipt.Stage==HappyDingzhouStage.BranchChoice)
        {
            var field=choice.Parameters["option"]=="field";
            var cards=field?[]:GetHand(_players[receipt.TargetSeat]).Select(c=>c.Id).ToArray();
            var amount=field?ShenLsField(receipt.TargetSeat).Length:cards.Length;
            receipt=receipt with{Branch=field?1:2,Amount=amount,Obtained=cards,
                Stage=field?HappyDingzhouStage.FieldGiftChoice:HappyDingzhouStage.HandGainChildren};
            ReplaceRuntimeTop(frame=frame with{HappyDingzhou=receipt});
            AdvanceEventRulesAndQueueFact(new HappyDingzhouBranchChosenEvent(frame.Id,receipt.Branch,amount));
            if(!field)
            {
                if(cards.Length==0){FinishHappyDingzhou(frame);return;}
                MoveHappyDingzhou(frame,cards,HappyDingzhouStage.HandGainChildren,CardLocation.Hand(frame.OwnerSeat));
            }
        }
        else
        {
            var card=choice.Cards.Single();
            AdvanceEventRulesAndQueueFact(new HappyDingzhouCardChosenEvent(frame.Id,receipt.Chosen.Count));
            ReplaceRuntimeTop(frame with{HappyDingzhou=receipt with{Chosen=receipt.Chosen.Append(card).ToArray()}});
        }
        ContinueHappyDingzhou(frame.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyShenLuSuDingzhou)]
    private PromptChoice AiHappyDingzhou(PendingDecision decision,ProgramSkillFrame frame)=>decision.Choices.First();
}
