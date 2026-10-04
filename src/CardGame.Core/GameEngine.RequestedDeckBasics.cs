using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<(CardConversionSource Source,SkillProgramCardPolicy Policy)> RequestedDeckBasicSources(CharacterState actor) =>
        actor.IsAlive && actor.Seat!=_currentSeat && _winner==Winner.None
            ? CardPolicies(actor,SkillProgramCardPolicyKind.PrivateTopBasicRequest)
                .Select(p=>(new CardConversionSource(p.Source.SkillId,p.Policy.Id,actor.Seat,p.Source.SkillInstanceId),p.Policy))
            : [];
    private bool HasRequestedDeckBasicSource(CharacterState actor,CardKind required) => RequestedDeckBasicSources(actor)
        .Any(p=>p.Policy.CardKinds.Contains(required) || IsSlashCard(required)&&p.Policy.CardKinds.Contains(CardKind.Slash));

    private (RequestedDeckBasicIntent Intent,long OwnerId,long RequestId,int Cursor)? RequestedDeckBasicContext(PendingDecision p,ResolutionFrame? suppliedTop=null)
    {
        var top=suppliedTop??_resolutionStack.LastOrDefault(); if(top is null || top is RequestedDeckBasicFrame)return null;
        long owner=top is ResponseWindowFrame response ? response.ParentFrameId : top.Id;
        if(p.Kind==DecisionKind.RescueDying && ActiveDying is { } d && d.ResponderSeat==p.PlayerSeat && top.Id==d.Id)
            return(RequestedDeckBasicIntent.Dying,d.Id,top.Id,d.ResponderIndex);
        if(p.Kind==DecisionKind.RespondDodge)
        {
            if(ActiveFactionDefense is { } fd && fd.CurrentCandidateSeat==p.PlayerSeat)
                return(RequestedDeckBasicIntent.FactionDodge,owner,top.Id,fd.CandidateIndex);
            if(ActiveGroupCard is {Effect:GroupCardEffect.ResponseAttack,CurrentAttack:{ } g} && g.TargetSeat==p.PlayerSeat)
                return(RequestedDeckBasicIntent.Group,owner,top.Id,g.SuccessfulDodgeResponses);
            if(ActiveCardAttack is { } a && a.TargetSeat==p.PlayerSeat)return(RequestedDeckBasicIntent.Dodge,owner,top.Id,a.SuccessfulDodgeResponses);
        }
        if(p.Kind==DecisionKind.RespondSlash)
        {
            if(ActiveFactionCardRequest is {AwaitingProviders:true} fr && fr.CurrentCandidateSeat==p.PlayerSeat)
                return(RequestedDeckBasicIntent.FactionSlash,owner,top.Id,fr.CandidateIndex);
            if(ActiveBorrowedSword is {AwaitingSlashChoice:true} bs && bs.WeaponOwnerSeat==p.PlayerSeat)
                return(RequestedDeckBasicIntent.BorrowedSword,owner,top.Id,0);
            if(ActiveDuel is { } duel && duel.ResponderSeat==p.PlayerSeat)return(RequestedDeckBasicIntent.Duel,owner,top.Id,0);
            if(ActiveGroupCard is {Effect:GroupCardEffect.ResponseAttack,CurrentAttack:{ } ga} && ga.TargetSeat==p.PlayerSeat)
                return(RequestedDeckBasicIntent.Group,owner,top.Id,0);
        }
        if(p.Kind==DecisionKind.QinglongCrescentBlade && ActiveQinglongCrescentBlade is { } q && q.Attack.SourceSeat==p.PlayerSeat)
            return(RequestedDeckBasicIntent.Qinglong,owner,top.Id,0);
        if(p.Kind==DecisionKind.ProgramTrigger && top is ProgramSkillFrame program)
        {
            var actions=p.Choices.Select(c=>c.Parameters.GetValueOrDefault("program-action")).ToArray();
            if(actions.Contains("request-slash-decline"))return(RequestedDeckBasicIntent.ProgramSlash,program.Id,program.Id,program.InstructionIndex);
            if(actions.Contains("request-slash-nearest-decline"))return(RequestedDeckBasicIntent.ProgramNearestSlash,program.Id,program.Id,program.InstructionIndex);
            if(program.AssistedSlashRequest is {TargetSeat:not null} assisted && assisted.ActorSeat==p.PlayerSeat)
                return(RequestedDeckBasicIntent.AssistedSlash,program.Id,program.Id,program.InstructionIndex);
            if(program.NearestLegalSlashRequest is {AwaitingFaction:false} nearest && nearest.ActorSeat==p.PlayerSeat)
                return(RequestedDeckBasicIntent.NearestLegalSlash,program.Id,program.Id,program.InstructionIndex);
        }
        return null;
    }
    private CardKind RequestedDeckBasicKind(PendingDecision p,RequestedDeckBasicIntent intent) => intent switch
    {
        RequestedDeckBasicIntent.Dying=>CardKind.Peach,
        RequestedDeckBasicIntent.Dodge or RequestedDeckBasicIntent.FactionDodge=>CardKind.Dodge,
        RequestedDeckBasicIntent.Group=>ActiveGroupCard!.RequiredCardKind!.Value,
        _=>CardKind.Slash
    };
    private PendingDecision? AddRequestedDeckBasicChoices(PendingDecision? p)
    {
        if(p is null || !_started || _resolutionStack.LastOrDefault() is RequestedDeckBasicFrame ||
            p.Choices.Any(c=>c.Parameters.ContainsKey("deck-basic-source")) || RequestedDeckBasicContext(p) is not { } context)return p;
        var actor=_players[p.PlayerSeat];var required=RequestedDeckBasicKind(p,context.Intent);
        if(context.Intent==RequestedDeckBasicIntent.Dying ?
            !CanUsePeachToRescue(actor.Seat,ActiveDying!.VictimSeat) && !(actor.Seat==ActiveDying.VictimSeat &&
                !HasSelfCardTargetProhibition(actor.Seat) && !IsCardUseForbidden(actor.Seat,CardKind.Alcohol,CardActionType.Use)) :
            IsCardUseForbidden(actor.Seat,required,RequestedDeckBasicActionType(context.Intent)))return p;
        var owner=_resolutionStack.Single(f=>f.Id==context.OwnerId);
        if(owner.RequestedDeckBasicMaterial is { } seen && seen.OriginalPromptId==p.PromptId && seen.RequestFrameId==context.RequestId && seen.ActorSeat==actor.Seat && seen.Cursor==context.Cursor)return p;
        var sources=RequestedDeckBasicSources(actor).Where(s=>s.Policy.CardKinds.Contains(required)).ToArray();
        if(sources.Length==0)return p;
        // Older AI producers use PromptId zero. Only this opt-in real need gets
        // an issued prompt token, so a later need is not confused with a refusal.
        if(p.PromptId==default)p=p with {PromptId=CreatePromptId()};
        var choices=p.Choices.ToList();
        foreach(var (source,_) in sources)
        {
            var skill=_contentRegistry.GetSkill(source.SkillId);var parameters=new Dictionary<string,string>{["deck-basic-source"]="activate"};
            AddConversionParameters(parameters,source);
            choices.Add(new(new($"deck-basic.activate.{source.SkillId}.{source.BindingId}.{source.SkillInstanceId}"),$"发动【{skill.Name}】，观看牌堆顶可用基本牌",[],[],parameters));
        }
        return p with {Choices=Array.AsReadOnly(choices.ToArray())};
    }
    private bool IsRequestedDeckBasicAnswer(ChoiceId id) => _pendingDecision?.Choices.Any(c=>c.Id==id && c.Parameters.ContainsKey("deck-basic-source"))==true;
    private CommandResult SubmitRequestedDeckBasicAnswer(int actor,PromptId prompt,ChoiceId choice)
    {
        if(!_started)return Reject(CommandErrorCode.NotStarted,"游戏尚未开始。");
        if(_pendingDecision is not { } p || p.PlayerSeat!=actor || p.PromptId!=prompt)return Reject(CommandErrorCode.InvalidPrompt,"顶牌选择必须回答当前精确提示。");
        var selected=p.Choices.SingleOrDefault(c=>c.Id==choice);
        if(selected is null)return Reject(CommandErrorCode.InvalidChoice,"顶牌选择未公布。");
        return Accept(()=>{ResolveRequestedDeckBasicAnswer(selected);AdvanceRulesAndPublishState();if(_options.AdvanceAfterHumanCommands)AdvanceToHumanBoundary();});
    }
    private void ResolveRequestedDeckBasicAnswer(PromptChoice c)
    {
        if(c.Parameters.GetValueOrDefault("deck-basic-source")=="activate")
        {BeginRequestedDeckBasicView(c);return;}
        var view=_resolutionStack.LastOrDefault() as RequestedDeckBasicFrame??throw new InvalidOperationException("A private top view lost its typed request owner.");
        AssertRequestedDeckBasicFrame(view);
        if(!AssistedChoicesEqual([c],RequestedDeckBasicChoices(view).Where(x=>x.Id==c.Id).ToArray()))throw new InvalidOperationException("The private top choice changed.");
        var r=view.Receipt;
        if(c.Parameters["deck-basic-source"]=="decline")
        {
            PopResolutionFrame(view.Id,ResolutionFrameKind.RequestedDeckBasic);
            var owner=_resolutionStack.Single(f=>f.Id==r.OwnerFrameId);ReplaceRuntimeFrame(owner.Id,owner with {RequestedDeckBasicMaterial=r});
            _pendingDecision=RequestedDeckBasicNativeDecision(view.OriginalDecision);
            _status=r.Intent==RequestedDeckBasicIntent.Dying?EngineStatus.AwaitingHumanDying:
                _players[r.ActorSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;return;
        }
        var card=_cardZones.CardsAt(CardLocation.DrawPile).Single(x=>x.Id==c.Cards.Single());
        PopResolutionFrame(view.Id,ResolutionFrameKind.RequestedDeckBasic);
        var original=_resolutionStack.Single(f=>f.Id==r.OwnerFrameId);
        ReplaceRuntimeFrame(original.Id,original with {RequestedDeckBasicMaterial=r with {SelectedCardId=card.Id,SelectedKind=card.Kind,Claiming=true}});
        _pendingDecision=RequestedDeckBasicNativeDecision(view.OriginalDecision);
        DispatchRequestedDeckBasicMaterial(r.Intent,card,c.Targets);
    }
    private void BeginRequestedDeckBasicView(PromptChoice choice)
    {
        var original=_pendingDecision??throw new InvalidOperationException("A deck-basic offer requires a current real need.");
        var source=RequireConversionSource(choice);var context=RequestedDeckBasicContext(original)??throw new InvalidOperationException("The basic need no longer has its original producer.");
        var actor=_players[original.PlayerSeat];var policy=RequestedDeckBasicSources(actor).SingleOrDefault(s=>s.Source==source).Policy;
        if(policy is null || !original.Choices.Any(c=>c.Id==choice.Id && AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("The optional top view source lost current qualification.");
        var count=GetHand(actor).Count==0?policy.Value*2:policy.Value;
        // This is the same bounded refresh used by actual draws. Never fill a short nonempty pile by moving looked-at cards.
        EnsureDrawPile();
        var ids=_cardZones.CardsAt(CardLocation.DrawPile).Reverse().Take(count).Select(c=>c.Id).ToArray();
        var viewId=++_resolutionSequence;
        var r=new RequestedDeckBasicMaterial(context.OwnerId,context.RequestId,original.PromptId,original.Revision,context.Intent,
            actor.Seat,context.Cursor,source,_contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash,ids.Length,null,null){ViewFrameId=viewId};
        var view=new RequestedDeckBasicFrame(viewId,context.RequestId,r,original,ids);
        ClearPendingDecision();PushRuntimeFrame(view);
        AdvanceEventRulesAndQueueFact(new RequestedDeckBasicViewedEvent(view.Id,r.OwnerFrameId,actor.Seat,source.SkillId,ids.Length,r.Intent));
        PublishRequestedDeckBasicView(view);
    }
    private int[] RequestedDeckBasicTargets(RequestedDeckBasicFrame view,Card card)
    {
        var r=view.Receipt;var actor=_players[r.ActorSeat];
        var targets=r.Intent switch
        {
            RequestedDeckBasicIntent.BorrowedSword=>new[]{ActiveBorrowedSword!.SlashTargetSeat},
            RequestedDeckBasicIntent.Qinglong=>new[]{ActiveQinglongCrescentBlade!.Attack.TargetSeat},
            RequestedDeckBasicIntent.ProgramSlash=>new[]{((ProgramSkillFrame)_resolutionStack.Single(f=>f.Id==r.OwnerFrameId)).OwnerSeat},
            RequestedDeckBasicIntent.AssistedSlash=>new[]{((ProgramSkillFrame)_resolutionStack.Single(f=>f.Id==r.OwnerFrameId)).AssistedSlashRequest!.TargetSeat!.Value},
            RequestedDeckBasicIntent.ProgramNearestSlash or RequestedDeckBasicIntent.NearestLegalSlash=>view.OriginalDecision.ValidTargetSeats.ToArray(),
            _=>Array.Empty<int>()
        };
        return targets.Where(t=>_players[t].IsAlive && (r.Intent==RequestedDeckBasicIntent.Qinglong
            ? CanUseQinglongCrescentBladeTarget(actor,_players[t],card.Kind,SuitColor(EffectiveSuit(actor,card)),false,EffectiveSuit(actor,card),SpecificSlashRank(actor,card,card.Kind))
            : CanUseSlashTarget(actor,_players[t],card,null,card.Kind,physicalCardIds:[card.Id]))).ToArray();
    }
    private bool RequestedDeckBasicCardMatches(RequestedDeckBasicFrame view,Card card)
    {
        var r=view.Receipt;var actor=_players[r.ActorSeat];
        if(r.Intent==RequestedDeckBasicIntent.Dying)
        {
            var dying=_resolutionStack.OfType<DyingFrame>().Single(d=>d.Id==r.OwnerFrameId);
            return card.Kind==CardKind.Peach && CanUsePeachToRescue(actor.Seat,dying.VictimSeat) && !HasBeneficiarySuitShield(actor.Seat,dying.VictimSeat,EffectiveSuit(actor,card)) ||
                card.Kind==CardKind.Alcohol && actor.Seat==dying.VictimSeat && !HasSelfCardTargetProhibition(actor.Seat) && !IsCardUseForbidden(actor.Seat,CardKind.Alcohol,CardActionType.Use);
        }
        var required=RequestedDeckBasicKind(view.OriginalDecision,r.Intent);
        if(required==CardKind.Slash?!IsSlashCard(card.Kind):card.Kind!=required)return false;
        if(IsCardUseForbidden(actor.Seat,card.Kind,RequestedDeckBasicActionType(r.Intent)))return false;
        if(r.Intent is RequestedDeckBasicIntent.BorrowedSword or RequestedDeckBasicIntent.Qinglong or RequestedDeckBasicIntent.ProgramSlash or RequestedDeckBasicIntent.ProgramNearestSlash or RequestedDeckBasicIntent.AssistedSlash or RequestedDeckBasicIntent.NearestLegalSlash)
            return RequestedDeckBasicTargets(view,card).Length>0;
        if(r.Intent==RequestedDeckBasicIntent.FactionSlash && ActiveFactionCardRequest is { } faction)
            return IsRedSlashProviderPaymentLegal(faction,card.Kind,[card]);
        return true;
    }
    private IReadOnlyList<PromptChoice> RequestedDeckBasicChoices(RequestedDeckBasicFrame view)
    {
        var list=new List<PromptChoice>();
        Dictionary<string,string> Parameters(string branch)=>new(){["deck-basic-source"]=branch,["deck-frame-id"]=view.Id.ToString(CultureInfo.InvariantCulture)};
        foreach(var id in view.CardIds)
        {
            var card=_cardZones.CardsAt(CardLocation.DrawPile).Single(c=>c.Id==id);
            if(!RequestedDeckBasicCardMatches(view,card))continue;
            var targets=RequestedDeckBasicTargets(view,card);
            if(targets.Length==0)list.Add(new(new($"deck-basic.{view.Id}.{id}"),$"使用或打出【{card.DisplayName}】",[id],[],Parameters("card")));
            else foreach(var target in targets)list.Add(new(new($"deck-basic.{view.Id}.{id}.{target}"),$"对 {_players[target].Name} 使用【{card.DisplayName}】",[id],[target],Parameters("card")));
        }
        list.Add(new(new($"deck-basic.{view.Id}.decline"),"不使用或打出顶牌",[],[],Parameters("decline")));
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishRequestedDeckBasicView(RequestedDeckBasicFrame view)
    {
        var skill=_contentRegistry.GetSkill(view.Receipt.Source.SkillId);var choices=RequestedDeckBasicChoices(view);
        _pendingDecision=new(DecisionKind.ProgramTrigger,view.Receipt.ActorSeat,$"{skill.Name}：观看牌堆顶{view.CardIds.Count}张牌，选择符合本次需求的基本牌。",choices.SelectMany(c=>c.Cards).ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),view.OriginalDecision.SourceSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(skill.Id,skill.Name,skill.Name,skill.Description)};
        _status=view.Receipt.Intent==RequestedDeckBasicIntent.Dying && _players[view.Receipt.ActorSeat].IsHuman
            ?EngineStatus.AwaitingHumanDying:_players[view.Receipt.ActorSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    private void DispatchRequestedDeckBasicMaterial(RequestedDeckBasicIntent intent,Card card,IReadOnlyList<int> targets)
    {
        var actor=_players[_pendingDecision!.PlayerSeat];
        switch(intent)
        {
            case RequestedDeckBasicIntent.Dodge:
                var attack=ActiveCardAttack!;PopResponseWindow(attack.ResolutionId);SetCardUseStep(attack.ResolutionId,ResolutionFrameStep.ResolvingEffect);ClearPendingDecision();ResolveDodgeResponse(attack,actor,card);break;
            case RequestedDeckBasicIntent.Group:
                var group=ActiveGroupCard!;PopResponseWindow(group.ResolutionId);SetCardUseStep(group.ResolutionId,ResolutionFrameStep.ResolvingEffect);ClearPendingDecision();ResolveGroupResponse(group,actor,card);break;
            case RequestedDeckBasicIntent.Duel:
                var duel=ActiveDuel!;PopResponseWindow(duel.ResolutionId);SetResponseParentStep(duel.ResolutionId,ResolutionFrameStep.ResolvingEffect);ClearPendingDecision();ResolveDuelResponse(duel,actor,card);break;
            case RequestedDeckBasicIntent.Dying:
                ClearPendingDecision();ApplyDyingResponse(actor,card.Kind==CardKind.Peach,card.Kind==CardKind.Peach?card.Id:null,card.Kind==CardKind.Alcohol,card.Kind==CardKind.Alcohol?card.Id:null);break;
            case RequestedDeckBasicIntent.FactionDodge:
                ResolveFactionDefenseCandidateResponse(ActiveFactionDefense!,true,false,card.Id);break;
            case RequestedDeckBasicIntent.FactionSlash:
                ResolveFactionSlashCandidateResponse(ActiveFactionCardRequest!,true,card.Id,card.Kind);break;
            case RequestedDeckBasicIntent.BorrowedSword:ResolveBorrowedSwordSlashChoice(ActiveBorrowedSword!,card,card.Kind);break;
            case RequestedDeckBasicIntent.Qinglong:
                ResolveQinglongCrescentBladeChoice(new(new("deck-basic.qinglong"),"使用杀",[card.Id],targets,
                    new Dictionary<string,string>{["action"]="qinglong-slash",["response-card-kind"]=card.Kind.ToString()}));break;
            default:DispatchRequestedDeckProgramSlash(intent,card,targets);break;
        }
    }
    private void DispatchRequestedDeckProgramSlash(RequestedDeckBasicIntent intent,Card card,IReadOnlyList<int> targets)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("A deck basic program request lost its original owner.");
        var e=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        var p=new Dictionary<string,string>{["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["card-id"]=card.Id.ToString(CultureInfo.InvariantCulture)};
        if(intent==RequestedDeckBasicIntent.ProgramSlash)
        {p["program-action"]="request-slash";p["result-bind"]=e.ResultBind!;ResolveProgramRequestSlashChoice(new(new("deck-basic.request"),"使用杀",[card.Id],targets,p));}
        else if(intent==RequestedDeckBasicIntent.ProgramNearestSlash)
        {p["program-action"]="request-slash-nearest";p["seat"]=_pendingDecision!.PlayerSeat.ToString(CultureInfo.InvariantCulture);p["target-seat"]=targets.Single().ToString(CultureInfo.InvariantCulture);ResolveProgramRequestSlashByNearestChoice(new(new("deck-basic.nearest"),"使用杀",[card.Id],targets,p));}
        else
        {
            var choices=intent==RequestedDeckBasicIntent.AssistedSlash?AssistedPhysicalSlashChoices(f):NearestLegalSlashChoices(f);
            var selected=choices.Single(c=>c.Cards.SequenceEqual(new[]{card.Id}) && c.Targets.SequenceEqual(targets) && !c.Parameters.ContainsKey("conversion-skill-id") &&
                c.Parameters.GetValueOrDefault("effective-kind")==card.Kind.ToString());
            if(intent==RequestedDeckBasicIntent.AssistedSlash)ResolveAssistedPhysicalSlashChoice(selected);else ResolveNearestLegalSlashChoice(selected);
        }
    }
    private IEnumerable<Card> SelectedRequestedDeckBasicCards(CharacterState actor) => _resolutionStack
        .Where(f=>f.RequestedDeckBasicMaterial is {Claiming:true,SelectedCardId:not null} r && r.OwnerFrameId==f.Id && r.ActorSeat==actor.Seat)
        .Where(HasValidRequestedDeckBasicMaterial).Select(f=>f.RequestedDeckBasicMaterial!).Where(r=>_cardZones.GetLocation(r.SelectedCardId!.Value)==CardLocation.DrawPile)
        .Select(r=>_cardZones.CardsAt(CardLocation.DrawPile).Single(c=>c.Id==r.SelectedCardId && c.Kind==r.SelectedKind));
    private IReadOnlyList<Card> AppendRequestedDeckBasicCards(CharacterState actor,IReadOnlyList<Card> original)
    {
        var selected=SelectedRequestedDeckBasicCards(actor).ToArray();
        return selected.Length==0?original:original.Concat(selected).DistinctBy(c=>c.Id).ToArray();
    }
    private bool IsSelectedRequestedDeckBasicMaterial(int actor,int id)=>SelectedRequestedDeckBasicCards(_players[actor]).Any(c=>c.Id==id);
    private CardSnapshot[] RequestedDeckBasicPrivateCards(int viewer) => _resolutionStack.OfType<RequestedDeckBasicFrame>()
        .Where(f=>f.Receipt.ActorSeat==viewer).SelectMany(f=>f.CardIds)
        .Select(id=>ToSnapshot(_cardZones.CardsAt(CardLocation.DrawPile).Single(c=>c.Id==id))).ToArray();
    private bool TryAdvanceRequestedDeckBasicAi()
    {
        if(_pendingDecision is not { } prompt || _players[prompt.PlayerSeat].IsHuman)return false;
        if(_resolutionStack.LastOrDefault() is RequestedDeckBasicFrame view)
        {
            var selected=RequestedDeckBasicChoices(view).FirstOrDefault(c=>c.Parameters["deck-basic-source"]=="card")??RequestedDeckBasicChoices(view).Last();
            ResolveRequestedDeckBasicAnswer(selected);AdvanceRulesAndPublishState();return true;
        }
        var activate=prompt.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("deck-basic-source")=="activate");
        if(activate is null)
        {
            if(prompt.Kind!=DecisionKind.RescueDying || ActiveDying is not { } dying ||
                _resolutionStack.LastOrDefault()?.Id!=dying.Id || dying.ResponderSeat!=prompt.PlayerSeat ||
                !(HasRequestedDeckBasicSource(_players[prompt.PlayerSeat],CardKind.Peach) ||
                  dying.RequestedDeckBasicMaterial is {Intent:RequestedDeckBasicIntent.Dying,Claiming:false} attempted &&
                  attempted.ActorSeat==prompt.PlayerSeat && attempted.Cursor==dying.ResponderIndex && attempted.OriginalPromptId==prompt.PromptId))return false;
            // The original request remains declined on its Dying owner. Consume
            // the mature native response while skipping this opt-in entry once.
            ClearPendingDecision();RunOneDyingStep(skipRequestedDeckBasic:true);return true;
        }
        // Activation does not inspect hidden cards. Once activated, only the viewer sees legal matching choices.
        ResolveRequestedDeckBasicAnswer(activate);AdvanceRulesAndPublishState();return true;
    }
    private void CaptureRequestedDeckBasicPaid(Card card,CardLocation from,CardLocation to)
    {
        if(from!=CardLocation.DrawPile || to!=CardLocation.Processing)return;
        var owner=_resolutionStack.SingleOrDefault(f=>f.RequestedDeckBasicMaterial is {Claiming:true} r && r.SelectedCardId==card.Id);
        if(owner?.RequestedDeckBasicMaterial is not { } receipt)return;
        if(receipt.SelectedKind!=card.Kind || receipt.OwnerFrameId!=owner.Id)throw new InvalidOperationException("A top basic payment changed its exact original material.");
        if(receipt.PaidMovementSequence is not null)throw new InvalidOperationException("A top basic request cannot pay twice.");
        ReplaceRuntimeFrame(owner.Id,owner with {RequestedDeckBasicMaterial=receipt with {PaidMovementSequence=_movementSequence}});
        AdvanceEventRulesAndQueueFact(new RequestedDeckBasicPaidEvent(owner.Id,receipt.ActorSeat,card.Id,card.Kind,receipt.Source.SkillId,receipt.Intent));
    }
}
