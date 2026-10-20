namespace CardGame.Core;
public sealed partial class GameEngine
{
    private PromptChoice BaoXinChoice(ProgramSkillFrame f,string branch,string label,int? card=null,int? target=null)=>new(new($"bao-xin.{f.Id}.{branch}.{card??target??-1}"),label,card is{}id?[id]:[],target is{}seat?[seat]:[],new Dictionary<string,string>{{"program-action","bao-xin"},{"branch",branch}});
    private IReadOnlyList<PromptChoice> BaoXinChoices(ProgramSkillFrame f)
    {
        var r=f.BaoXin!;var list=new List<PromptChoice>();if(!_players[r.Holder].IsAlive)return list;
        if(r.Stage==BaoXinStage.Mode){list.Add(BaoXinChoice(f,"obtain","令受伤角色随机获得牌堆中的一张杀"));if(GetHand(_players[r.Holder]).Count>0&&_players.Any(p=>p.IsAlive&&p.Seat!=r.Holder))list.Add(BaoXinChoice(f,"gift","令受伤角色自行交出一张手牌，然后摸一张牌"));}
        if(r.Stage==BaoXinStage.GiftCard)foreach(var c in BuildOwnedCardPaymentChoices(f.Id,r.Holder,r.Holder,[CardZoneKind.Hand],OwnedCardMoveIntent.Transfer))list.Add(BaoXinChoice(f,"card",c.Description,c.Cards.Single()));
        if(r.Stage==BaoXinStage.GiftTarget)list.AddRange(_players.Where(p=>p.IsAlive&&p.Seat!=r.Holder).Select(p=>BaoXinChoice(f,"target",p.Name,target:p.Seat)));
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishBaoXin(ProgramSkillFrame f)
    {
        var r=f.BaoXin!;var choices=BaoXinChoices(f);if(choices.Count==0||_winner!=Winner.None){FinishBaoXin(f);return;}var chooser=r.Stage==BaoXinStage.Mode?f.OwnerSeat:r.Holder;
        if(!_players[chooser].IsAlive){FinishBaoXin(f);return;}ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,chooser,r.Stage==BaoXinStage.Mode?"毅谋：选择援助方式。":"毅谋：由受伤者选择真实手牌及接收者。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),r.Holder){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[chooser].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("bao-xin")]private void AnswerBaoXin(PromptChoice chosen)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{BaoXin:{}r}f||_pendingDecision?.PlayerSeat!=(r.Stage==BaoXinStage.Mode?f.OwnerSeat:r.Holder)||!BaoXinChoices(f).Any(c=>AssistedChoicesEqual([c],[chosen])))throw new InvalidOperationException("Yimou requires its exact current private chooser and original choice.");
        AssertBaoXin(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=chosen.Parameters["branch"];
        if(branch is"obtain"or"gift")
        {
            r=r with{Paid=true,Branch=branch=="obtain"?1:2};ReplaceRuntimeTop(f=f with{BaoXin=r});AdvanceEventRulesAndQueueFact(new BaoXinYimouBranchEvent(f.Id,r.Branch,r.Holder));
            if(branch=="gift"){ReplaceRuntimeTop(f=f with{BaoXin=r with{Stage=BaoXinStage.GiftCard}});PublishBaoXin(f);return;}
            var pool=_cardZones.CardsAt(CardLocation.DrawPile).Where(c=>IsSlashCard(c.Kind)).OrderBy(c=>c.Id).ToArray();if(pool.Length==0||!_players[r.Holder].IsAlive){FinishBaoXin(f);return;}
            var before=_random.State;var index=_random.Next(pool.Length);var card=pool[index];r=r with{Eligible=pool.Select(c=>c.Id).ToArray(),RandomBefore=before,RandomAfter=_random.State,RandomIndex=index,SelectedCard=card.Id};ReplaceRuntimeTop(f=f with{BaoXin=r});RecordBaoXinRandom(f,0,r.Holder);MoveBaoXin(f,[new(card.Id,card.Kind,card.Kind,CardLocation.DrawPile)],BaoXinStage.ObtainChildren,CardLocation.Hand(r.Holder),BaoXinObtain);ContinueBaoXin(f.Id);return;
        }
        if(branch=="card"){ReplaceRuntimeTop(f=f with{BaoXin=r with{SelectedCard=chosen.Cards.Single(),Stage=BaoXinStage.GiftTarget}});PublishBaoXin(f);return;}
        if(branch!="target"||r.SelectedCard is not{}selected||_cardZones.GetLocation(selected)!=CardLocation.Hand(r.Holder))throw new InvalidOperationException("Yimou cannot substitute another original private Hand entity.");
        var recipient=chosen.Targets.Single();ReplaceRuntimeTop(f=f with{BaoXin=r with{Recipient=recipient}});var gift=GetAdvancedCard(selected);MoveBaoXin(f,[new(gift.Id,gift.Kind,AdvancedEffectiveHandKind(_players[r.Holder],gift),CardLocation.Hand(r.Holder))],BaoXinStage.YimouGiftChildren,CardLocation.Hand(recipient),BaoXinYimouGift);ContinueBaoXin(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveBaoXinYimou)]private PromptChoice ChooseBaoXinAi(PendingDecision prompt,ProgramSkillFrame f)
    {var r=f.BaoXin!;if(r.Stage==BaoXinStage.Mode)return prompt.Choices.First(c=>c.Parameters["branch"]=="obtain");if(r.Stage==BaoXinStage.GiftCard)return prompt.Choices.OrderBy(c=>CardCatalog.Get(GetAdvancedCard(c.Cards.Single()).Kind).HandKeepValue).First();return prompt.Choices.OrderByDescending(c=>GetEffectiveFactionId(_players[c.Targets.Single()])==GetEffectiveFactionId(_players[r.Holder])).ThenBy(c=>GetHand(_players[c.Targets.Single()]).Count).ThenBy(c=>c.Targets.Single()).First();}
}
