using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IReadOnlyList<PromptChoice> ShenLsChoices(ProgramSkillFrame f)
    {
        var r=f.ShenLuSu!;
        PromptChoice C(string option,string label,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>new(new("shen-lu-su."+f.Id+"."+option),label,cards??[],targets??[],
            new Dictionary<string,string>{{"program-action","shen-lu-su"},{"option",option},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}});
        if(r.Stage==ShenLuSuStage.SeatChoice)return r.OriginalOrder.Except(r.Order).Select(s=>C("seat:"+s,"下一座次：“"+_players[s].Name+"”",targets:[s])).ToArray();
        if(r.Stage==ShenLuSuStage.PeerChoice)return ShenLsPeers(f.OwnerSeat).Select(s=>C("peer:"+s,"与“"+_players[s].Name+"”随机平分双方手牌",targets:[s])).ToArray();
        if(r.Stage==ShenLuSuStage.GiftChoice)return BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,f.OwnerSeat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Transfer)
            .Where(c=>c.Cards is [var id]&&ShenLsGiveable(f.OwnerSeat).Contains(id)&&!r.Cards.Contains(id)).Select(c=>C("give:"+c.Cards.Single(),c.Description,c.Cards)).ToArray();
        return [];
    }
    private void PublishShenLs(ProgramSkillFrame f)
    {
        var choices=ShenLsChoices(f);if(choices.Count==0){FinishShenLs(f);return;}
        var skill=_contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,skill.Name+"：请选择。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=Array.AsReadOnly(choices.ToArray()),SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    [ProgramAnswer("shen-lu-su")] private void AnswerShenLs(PromptChoice chosen)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Shen Lu Su lost his active choice parent.");
        RequireShenLs(f);var r=f.ShenLuSu??throw new InvalidOperationException("Shen Lu Su lost his issued choice.");
        var current=ShenLsChoices(f).SingleOrDefault(c=>c.Id==chosen.Id&&c.Cards.SequenceEqual(chosen.Cards)&&c.Targets.SequenceEqual(chosen.Targets)&&c.Parameters.OrderBy(x=>x.Key).SequenceEqual(chosen.Parameters.OrderBy(x=>x.Key)))??
            throw new InvalidOperationException("Shen Lu Su requires the exact current private entity or public participant.");
        ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(r.Stage==ShenLuSuStage.SeatChoice)
        {
            var seat=current.Targets.Single();AdvanceEventRulesAndQueueFact(new ShenLuSuSeatChosenEvent(f.Id,r.Order.Count,seat));
            ReplaceRuntimeTop(f with{ShenLuSu=r with{Order=Array.AsReadOnly(r.Order.Append(seat).ToArray())}});
        }
        else if(r.Stage==ShenLuSuStage.GiftChoice)
        {
            var card=current.Cards.Single();AdvanceEventRulesAndQueueFact(new ShenLuSuGiftChosenEvent(f.Id,r.Cards.Count));
            ReplaceRuntimeTop(f with{ShenLuSu=r with{Cards=Array.AsReadOnly(r.Cards.Append(card).ToArray())}});
        }
        else if(r.Stage==ShenLuSuStage.PeerChoice){DivideShenLs(f,current.Targets.Single());return;}
        else throw new InvalidOperationException("Shen Lu Su cannot answer a paid movement.");
        ContinueShenLs(f.Id);
    }
    private void DivideShenLs(ProgramSkillFrame f,int target)
    {
        if(!ShenLsPeers(f.OwnerSeat).Contains(target))throw new InvalidOperationException("Zhimeng lost its legal other participant.");
        var pool=GetHand(_players[f.OwnerSeat]).Concat(GetHand(_players[target])).Select(c=>c.Id).ToArray();
        var random=pool.ToArray();for(var i=random.Length-1;i>0;i--){var j=_random.Next(i+1);(random[i],random[j])=(random[j],random[i]);}
        var ownerShare=random.Take((random.Length+1)/2).ToHashSet();
        var division=pool.Select((id,i)=>new ShenLuSuDivisionCard(i,id,_cardZones.GetLocation(id).OwnerSeat!.Value,ownerShare.Contains(id)?f.OwnerSeat:target)).ToArray();
        var r=f.ShenLuSu! with{Target=target,Amount=pool.Length,Cards=Array.AsReadOnly(pool),OwnerShare=Array.AsReadOnly(random.Take((random.Length+1)/2).ToArray()),Division=Array.AsReadOnly(division),Stage=ShenLuSuStage.DivideChildren};
        ReplaceRuntimeTop(f=f with{ShenLuSu=r});
        AdvanceEventRulesAndQueueFact(new ShenLuSuDivisionEvent(f.Id,target,pool.Length,ownerShare.Count));
        var transfers=pool.Where(id=>_cardZones.GetLocation(id).OwnerSeat!=(ownerShare.Contains(id)?f.OwnerSeat:target)).ToArray();
        if(transfers.Length==0){FinishShenLs(f);return;}
        MoveShenLs(f,transfers,ShenLuSuStage.DivideChildren,CardLocation.Hand(f.OwnerSeat),(c,_)=>CardLocation.Hand(ownerShare.Contains(c.Id)?f.OwnerSeat:target));
        ContinueShenLs(f.Id);
    }
    private static int[] ShenLsRotate(IReadOnlyList<int> order,int first)
    {var index=IndexOfTableSeat(order,first);return order.Skip(index).Concat(order.Take(index)).ToArray();}
    private void ApplyShenLsOrder(ProgramSkillFrame f)
    {
        var r=f.ShenLuSu!;
        if(!ShenLsMode||r.Stage!=ShenLuSuStage.SeatChoice||f.WindowContext is not{Window:SkillProgramTriggerWindow.GameStarting}w||
            !_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(p=>p.Id==w.ParentFrameId&&p.Window==w.Window)||
            !CurrentTableOrder().SequenceEqual(r.OriginalOrder)||r.Order.Count!=r.OriginalOrder.Count||r.Order.Distinct().Count()!=r.Order.Count||!r.Order.Order().SequenceEqual(r.OriginalOrder.Order())||
            r.FixedSeat!=(_modeDefinition.ModeKind==ContentModeKind.Identity?_players.Single(p=>p.Role==Role.Lord).Seat:-1)||
            r.FixedSeat>=0&&(_players[r.FixedSeat].Role!=Role.Lord||r.Order[0]!=r.FixedSeat))throw new InvalidOperationException("Tamo lost its frozen complete permutation or fixed lord position.");
        // Insert each chosen participant behind its frozen predecessor. The
        // identity lord is the anchor and is never the moved participant.
        for(var i=1;i<r.Order.Count;i++)
        {
            var seat=r.Order[i];var previous=r.Order[i-1];var remaining=CurrentTableOrder().Where(s=>s!=seat).ToArray();
            var next=remaining[(Array.IndexOf(remaining,previous)+1)%remaining.Length];
            if(TableStepSeat(previous,1)==seat)continue;
            AdvanceEventRulesAndQueueFact(new TableSeatMovedEvent(seat,previous,next,f.Id));
        }
        if(r.FixedSeat<0)
            while(CurrentTableOrder()[0]!=r.Order[0])
            {
                var current=CurrentTableOrder();
                AdvanceEventRulesAndQueueFact(new TableSeatMovedEvent(current[0],current[^1],current[1],f.Id));
            }
        if(!ShenLsRotate(CurrentTableOrder(),r.Order[0]).SequenceEqual(r.Order))throw new InvalidOperationException("Tamo did not install its complete real table order.");
        AdvanceEventRulesAndQueueFact(new ShenLuSuSeatsAppliedEvent(f.Id,r.ParentId,f.OwnerSeat,r.Order[0],r.Order.Count));
    }
    [ProgramPipelineHook(ProgramPipelinePhase.LifecycleWindowCompleted,38900)] private bool CompleteShenLsSetup(ProgramLifecycleTriggerWindowFrame frame)
    {
        if(!ShenLsTeam||frame.Window!=SkillProgramTriggerWindow.GameStarting||frame.Continuation!=ProgramLifecycleContinuation.CompleteGameStarting||
            _phase!=TurnPhase.NotStarted||_turnNumber!=0||_resolutionStack.Any(p=>p.Id==frame.Id))return false;
        var applied=ProgramEventHistory<ShenLuSuSeatsAppliedEvent>().LastOrDefault(e=>e.WindowId==frame.Id);
        if(applied is null)return false;
        var issued=ProgramEventHistory<ShenLuSuStartedEvent>().Where(e=>e.FrameId==applied.FrameId).ToArray();
        if(frame.OwnerSeat!=_currentSeat||applied.Count!=_playerCount||CurrentTableOrder()[0]!=applied.FirstSeat||
            issued is not[var source]||source.Operation!=SkillProgramEffectOp.ResolveShenLuSuTamo||source.ParentId!=frame.Id||source.Source.OwnerSeat!=applied.OwnerSeat||
            source.Source.SkillId!=ShenLuSuComposition.Tamo||source.GameplayHash!=_contentRegistry.GetSkill(ShenLuSuComposition.Tamo).Program!.GameplayHash||
            !frame.Candidates.Any(c=>c.OwnerSeat==applied.OwnerSeat&&c.SkillId==source.Source.SkillId&&c.SkillInstanceId==source.Source.SkillInstanceId&&c.BindingId==source.Source.BindingId&&c.GameplayHash==source.GameplayHash)||
            ProgramEventHistory<ProgramBindingResolvedEvent>().Count(e=>e.FrameId==applied.FrameId&&e.OwnerSeat==applied.OwnerSeat&&e.SkillId==ShenLuSuComposition.Tamo&&e.SkillInstanceId==source.Source.SkillInstanceId&&e.BindingId=="redistribute-seats"&&e.Activated&&e.Completed)!=1)
            throw new InvalidOperationException("Tamo lost its completed original setup permutation before selecting the real first actor.");
        _currentSeat=applied.FirstSeat;return true;
    }
    private static PromptChoice AiShenLs(PendingDecision p)=>p.Choices.First();
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShenLuSuTamo)] private PromptChoice AiShenLsTamo(PendingDecision p,ProgramSkillFrame f)=>AiShenLs(p);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShenLuSuDingzhou)] private PromptChoice AiShenLsDingzhou(PendingDecision p,ProgramSkillFrame f)=>AiShenLs(p);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShenLuSuZhimeng)] private PromptChoice AiShenLsZhimeng(PendingDecision p,ProgramSkillFrame f)=>AiShenLs(p);
}
