namespace CardGame.Core;

public sealed record IssuedPlayPhaseSuitUseAllowance(int OwnerSeat, string SkillId, string SkillInstanceId,
    int TurnNumber, int PhaseInstanceId, IReadOnlyList<Suit> Suits);

public sealed partial class GameEngine
{
    private readonly Dictionary<(int Turn,int Actor,string Skill,SkillProgramCardCategory Category),long> _firstTurnCategoryUses=[];
    private void ObserveFirstTurnCategoryUse(IGameEvent payload)
    {
        if(payload is not CardUseDeclaredEvent declared)return;
        var use=_resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f=>f.Id==declared.ResolutionId);
        if(use?.Action is { } action){RecordFirstTurnCategoryUse(action);return;}
        foreach(var skillId in _contentRegistry.ProgramDependencies.GetTriggerOperationSkillIds(SkillProgramEffectOp.FirstCategoryCompletedTop))
            _firstTurnCategoryUses.TryAdd((_turnNumber,declared.SourceSeat,skillId,GetProgramCardCategory(declared.CardKind)),-declared.ResolutionId);
    }
    private void RecordFirstTurnCategoryUse(CardActionContext action)
    {
        foreach(var skillId in _contentRegistry.ProgramDependencies.GetTriggerOperationSkillIds(SkillProgramEffectOp.FirstCategoryCompletedTop))
            _firstTurnCategoryUses.TryAdd((_turnNumber,action.ActorSeat,skillId,GetProgramCardCategory(action.EffectiveKind)),action.ActionId);
    }
    private bool IsFirstCategoryUse(int owner,string skill,CardActionContext action)=>action.ActorSeat==owner &&
        _firstTurnCategoryUses.GetValueOrDefault((_turnNumber,owner,skill,GetProgramCardCategory(action.EffectiveKind)),-1)==action.ActionId;
    private CardActionContext CompletedTopAction(ProgramSkillFrame f)=>_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w=>w.Id==f.WindowContext!.ParentFrameId).Action;
    private int[] AvailableCompletedTopCosts(CardActionContext a,long parentFrameId)=>a.PhysicalCards.Where(c=>
    {
        var at=_cardZones.GetLocation(c.CardId);
        return at.Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile ||
            at==CardLocation.Equipment(a.ActorSeat) && EquipmentCatalog.IsEquipment(a.EffectiveKind) &&
            CompleteProgramEventHistory().OfType<EquipmentChangedEvent>().Any(e=>e.ResolutionId==parentFrameId&&e.PlayerSeat==a.ActorSeat&&e.CardId==c.CardId&&e.CardKind==a.EffectiveKind);
    }).Select(c=>c.CardId).Distinct().ToArray();
    private bool HasPhaseSuitAllowance(int seat,Suit? suit)=>suit is { } s && s!=Suit.None && _phase==TurnPhase.Play && seat==_currentSeat &&
        CompleteProgramEventHistory().OfType<PlayPhaseSuitAllowanceGrantedEvent>().Any(e=>e.OwnerSeat==seat&&e.TurnNumber==_turnNumber&&e.PhaseInstanceId==_cardUseDebitPhaseInstanceId&&e.Suits.Contains(s));
    private IReadOnlyList<IssuedPlayPhaseSuitUseAllowance>? GetIssuedPlayPhaseSuitUseAllowances(int seat)
    {
        if (_phase != TurnPhase.Play || seat != _currentSeat) return null;
        var issued = CompleteProgramEventHistory().OfType<PlayPhaseSuitAllowanceGrantedEvent>()
            .Where(e => e.OwnerSeat == seat && e.TurnNumber == _turnNumber && e.PhaseInstanceId == _cardUseDebitPhaseInstanceId)
            .Select(e => new IssuedPlayPhaseSuitUseAllowance(e.OwnerSeat, e.SkillId, e.SkillInstanceId, e.TurnNumber, e.PhaseInstanceId, e.Suits)).ToArray();
        return issued.Length == 0 ? null : issued;
    }
    private Suit? PhysicalGroupSuit(CharacterState owner,IReadOnlyList<Card> cards) { var suits=cards.Select(c=>EffectiveSuit(owner,c)).Distinct().ToArray(); return suits.Length==1?suits[0]:null; }
    private bool HasPhaseSuitAllowance(CharacterState owner,Card card)=>HasPhaseSuitAllowance(owner.Seat,EffectiveSuit(owner,card));
    private sealed partial class ProgramSkillHost : IAlternatingSuitAndCompletedTopHost
    {
        public SkillProgramStepOutcome BeginAlternatingSuitDrawDiscard(ProgramSkillFrame f)=>engine.BeginAlternatingSuitDrawDiscard(f);
        public SkillProgramStepOutcome BeginFirstCategoryCompletedTop(ProgramSkillFrame f)=>engine.BeginFirstCategoryCompletedTop(f);
    }
    private SkillProgramStepOutcome BeginAlternatingSuitDrawDiscard(ProgramSkillFrame f)
    {
        if(f.ActivationId is null||f.OwnerSeat!=_currentSeat||_phase!=TurnPhase.Play||f.AlternatingSuitTop is not null)throw new InvalidOperationException("Alternating suit draw requires clean own Play activation.");
        var yin=CompleteProgramEventHistory().OfType<AlternatingSuitStateCommittedEvent>().LastOrDefault(e=>e.OwnerSeat==f.OwnerSeat&&e.SkillId==f.SkillId)?.NextYin==true;
        AdvanceEventRulesAndQueueFact(new AlternatingSuitStateCommittedEvent(f.OwnerSeat,f.SkillId,f.SkillInstanceId,!yin,_turnNumber,_cardUseDebitPhaseInstanceId));
        f=f with {AlternatingSuitTop=new("alternating","draw",yin?1:2,[],[])};ReplaceRuntimeTop(f);
        DrawProgramCards(f.Id,f.OwnerSeat,yin?2:1,null,null,SkillProgramCardSetVisibility.Private,new("skill-program.alternating-suit.draw"));
        return AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue?ResumeAlternatingSuitTopImmediately(f.Id):SkillProgramStepOutcome.AwaitChild;
    }
    private SkillProgramStepOutcome ResumeAlternatingSuitTopImmediately(long id){AdvanceRuntimeProgram(id);return SkillProgramStepOutcome.AwaitChild;}
    private SkillProgramStepOutcome BeginFirstCategoryCompletedTop(ProgramSkillFrame f)
    {
        var window=_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w=>w.Id==f.WindowContext!.ParentFrameId);
        var a=window.Action;
        if(!IsFirstCategoryUse(f.OwnerSeat,f.SkillId,a)||f.WindowContext!.Window!=SkillProgramTriggerWindow.CardUseCompleted)throw new InvalidOperationException("Completed top requires exact first-category use.");
        var ids=AvailableCompletedTopCosts(a,window.ParentFrameId);
        if(ids.Length==0)return SkillProgramStepOutcome.Continue;
        f=f with {AlternatingSuitTop=new("completed","order",ids.Length,ids,[],a.ActionId)};ReplaceRuntimeTop(f);PublishAlternatingSuitTop(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private bool ResumeAlternatingSuitTop(long id)
    {
        var f=GetActiveProgramFrame(id);var d=f.AlternatingSuitTop;if(d is null)return false;
        if(!QuotaTopSourceValid(f)){ReplaceRuntimeTop(f with{AlternatingSuitTop=null});return false;}
        if(d.Stage=="draw"&&d.Mode=="alternating")
        {
            f=f with{AlternatingSuitTop=d with{Stage="discard",Required=Math.Min(d.Required,GetHand(_players[f.OwnerSeat]).Count),CardIds=GetHand(_players[f.OwnerSeat]).Select(c=>c.Id).ToArray()}};ReplaceRuntimeTop(f);PublishAlternatingSuitTop(f);return true;
        }
        if(d.Stage=="discard-movement")
        {
            ReplaceRuntimeTop(f with{AlternatingSuitTop=null});return false;
        }
        if(d.Stage=="top-movement")
        {
            f=f with{AlternatingSuitTop=d with{Stage="reward-draw"}};ReplaceRuntimeTop(f);
            DrawProgramCards(f.Id,f.OwnerSeat,1,null,null,SkillProgramCardSetVisibility.Private,new("skill-program.completed-top.draw"));
            if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(f.Id);return true;
        }
        if(d.Stage=="reward-draw"){ReplaceRuntimeTop(f with{AlternatingSuitTop=null});return false;}
        return false;
    }
    private void PublishAlternatingSuitTop(ProgramSkillFrame f)
    {
        var d=f.AlternatingSuitTop!;
        if(d.SelectedIds.Count==d.Required){CommitAlternatingSuitTop(f);return;}
        var choices=d.CardIds.Except(d.SelectedIds).Select(id=>
        {
            var c=_cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c=>c.Id==id);
            return new PromptChoice(new($"suit-top.{f.Id}.{d.Stage}.{id}"),$"{(d.Mode=="alternating"?"弃置":"置顶次序")}【{c.DisplayName}】{GetSuitDisplayName(c.Suit)}{c.RankText}",[id],[],new Dictionary<string,string>{{"program-action","alternating-suit-top"},{"frame-id",f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}});
        }).ToArray();
        var skill=_contentRegistry.GetSkill(f.SkillId);_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,d.Mode=="alternating"?"选择要弃置的手牌":"依次选择牌堆顶顺序（先选为顶牌）",choices.SelectMany(c=>c.Cards).ToArray(),[],f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=d.Mode=="alternating",Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    private void ResolveAlternatingSuitTopChoice(PromptChoice c)
    {
        var f=GetActiveProgramFrame(long.Parse(c.Parameters["frame-id"],System.Globalization.CultureInfo.InvariantCulture));var d=f.AlternatingSuitTop!;
        if(!_pendingDecision!.Choices.Any(x=>x.Id==c.Id)||c.Cards.Count!=1||!d.CardIds.Except(d.SelectedIds).Contains(c.Cards[0]))throw new InvalidOperationException("Suit/top choice lost exact frame cursor.");
        ClearPendingDecision();f=f with{AlternatingSuitTop=d with{SelectedIds=d.SelectedIds.Append(c.Cards[0]).ToArray()}};ReplaceRuntimeTop(f);PublishAlternatingSuitTop(f);
    }
    private void CommitAlternatingSuitTop(ProgramSkillFrame f)
    {
        var d=f.AlternatingSuitTop!;var destination=d.Mode=="alternating"?CardLocation.DiscardPile:CardLocation.DrawPile;
        var ids=d.SelectedIds.ToArray();
        if(d.Mode=="alternating"&&ids.Any(id=>_cardZones.GetLocation(id)!=CardLocation.Hand(f.OwnerSeat)))throw new InvalidOperationException("Discard lost true owned hand costs.");
        if(d.Mode=="completed")
        {
            var w=_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w=>w.Id==f.WindowContext!.ParentFrameId);
            if(w.Action.ActionId!=d.ActionId||!ids.All(id=>AvailableCompletedTopCosts(w.Action,w.ParentFrameId).Contains(id)))throw new InvalidOperationException("Completed top costs are no longer claimable.");
        }
        var suits=d.Mode=="alternating"?ids.Select(id=>EffectiveSuit(_players[f.OwnerSeat],_cardZones.CardsAt(CardLocation.Hand(f.OwnerSeat)).Single(c=>c.Id==id))).Where(s=>s!=Suit.None).Distinct().ToArray():[];
        f=f with{AlternatingSuitTop=d with{Stage=d.Mode=="alternating"?"discard-movement":"top-movement",PaidSuits=suits}};ReplaceRuntimeTop(f);
        var moves=ids.Select(id=>{var at=_cardZones.GetLocation(id);return (Card:_cardZones.CardsAt(at).Single(c=>c.Id==id),From:at);}).ToArray();
        var batch=BeginCardMovementBatch(moves.Select(m=>m.From),[destination]);var records=new List<CardMovementRecord>();var committed=false;
        try{foreach(var m in moves)_cardZones.Move(m.Card.Id,m.From,destination);if(d.Mode=="completed")_cardZones.PlaceDrawPileCardsAtTop(ids);foreach(var m in moves)records.Add(RecordMovement(m.Card,m.From,destination,new(d.Mode=="alternating"?"skill-program.alternating-suit.discard":"skill-program.completed-top.place")));committed=true;}
        finally{CompleteCardMovementBatch(batch,records,committed);}
        if(d.Mode=="alternating")
            AdvanceEventRulesAndQueueFact(new PlayPhaseSuitAllowanceGrantedEvent(f.OwnerSeat,f.SkillId,f.SkillInstanceId,_turnNumber,_cardUseDebitPhaseInstanceId,suits,ids));
        if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(f.Id);
    }
    private void AssertAlternatingSuitTop(ProgramSkillFrame f,SkillProgramEffect paused)
    {
        if(f.AlternatingSuitTop is not { } d)return;
        if(paused.Op!=(d.Mode=="alternating"?SkillProgramEffectOp.AlternatingSuitDrawDiscard:SkillProgramEffectOp.FirstCategoryCompletedTop)||
            d.Mode is not ("alternating" or "completed")||d.Stage is not ("draw" or "discard" or "order" or "discard-movement" or "top-movement" or "reward-draw")||
            d.Required<0||d.CardIds.Distinct().Count()!=d.CardIds.Count||d.SelectedIds.Distinct().Count()!=d.SelectedIds.Count||d.SelectedIds.Any(id=>!d.CardIds.Contains(id))||d.SelectedIds.Count>d.Required)
            throw new InvalidOperationException("Alternating suit/top operation lost its typed instruction and cursor.");
        if(d.Mode=="completed"&&(f.WindowContext?.Window!=SkillProgramTriggerWindow.CardUseCompleted||CompletedTopAction(f).ActionId!=d.ActionId||!IsFirstCategoryUse(f.OwnerSeat,f.SkillId,CompletedTopAction(f))))
            throw new InvalidOperationException("Completed top operation lost its exact first use parent.");
        if(ReferenceEquals(f,_resolutionStack.LastOrDefault())&&d.Stage is "discard" or "order"&&
            (_pendingDecision is not {Kind:DecisionKind.ProgramTrigger} p||p.PlayerSeat!=f.OwnerSeat||p.Choices.Count!=d.CardIds.Count-d.SelectedIds.Count||p.Choices.Any(c=>c.Cards.Count!=1||!d.CardIds.Except(d.SelectedIds).Contains(c.Cards[0]))))
            throw new InvalidOperationException("Alternating suit/top prompt lost its exact choices.");
    }
}
