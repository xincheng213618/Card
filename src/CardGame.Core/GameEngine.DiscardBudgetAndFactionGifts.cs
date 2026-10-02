namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IDiscardBudgetAndFactionGiftHost
    {
        public SkillProgramStepOutcome ResolveDiscardBudgetParticipants(ProgramSkillFrame f)=>engine.BeginDiscardBudget(f);
        public SkillProgramStepOutcome PreventOwnPlayOutsideTargetRangeDamage(ProgramSkillFrame f)=>engine.PreventOwnPlayOutsideRange(f);
        public SkillProgramStepOutcome DiscardOutsideRangeAfterInsufficientUses(ProgramSkillFrame f)=>engine.BeginOutsideRangeDiscard(f);
        public SkillProgramStepOutcome OfferCompletedFactionCostGift(ProgramSkillFrame f,string faction)=>engine.BeginCompletedFactionGift(f,faction);
    }
    private bool BudgetGiftSourceValid(ProgramSkillFrame f)=>_players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId) && EnabledSkillPrograms(_players[f.OwnerSeat]).Any(p=>p.Id==f.SkillId);
    private Dictionary<string,string> BudgetGiftParameters(ProgramSkillFrame f,string action,string mode)=>new()
    { ["program-action"]=action,["frame-id"]=f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),["mode"]=mode };
    private void PublishBudgetGiftPrompt(ProgramSkillFrame f,int chooser,string text,IReadOnlyList<PromptChoice> choices,bool privacy=false)
    {
        var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,chooser,text,choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        { PromptId=CreatePromptId(),IsPrivate=privacy,TargetSeat=chooser,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description) };
        _status=_players[chooser].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    private SkillProgramStepOutcome BeginDiscardBudget(ProgramSkillFrame f)
    {
        if(f.WindowContext?.Window!=SkillProgramTriggerWindow.DiscardPhaseEnded || f.OwnerSeat!=_currentSeat || _phase!=TurnPhase.Discard)
            throw new InvalidOperationException("Discard budget requires its actual own completed discard phase.");
        var budget=_fullDiscardPhaseSuitTurn==_turnNumber?_fullDiscardPhaseSuits.Count:0;
        f=f with { DiscardBudgetDraft=new(budget,_players.Where(p=>p.IsAlive).Select(p=>new ProgramDiscardBudgetParticipant(p.Seat,p.Hp)).ToArray(),Selected:[]) };
        ReplaceRuntimeTop(f);PublishBudgetGiftPrompt(f,f.OwnerSeat,$"本弃牌阶段弃置了 {budget} 张牌，请选择效果。",DiscardBudgetChoices(f));return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> DiscardBudgetChoices(ProgramSkillFrame f)
    {
        var d=f.DiscardBudgetDraft!;var selected=d.Selected!;var choices=new List<PromptChoice>();
        void Add(string key,string label,IReadOnlyList<int> targets)=>choices.Add(new(new($"discard-budget.{f.Id}.{key}"),label,[],targets,BudgetGiftParameters(f,"discard-budget",key)));
        if(d.Mode is null){Add("draw",$"令至多 {d.Budget} 名角色各摸一张牌",[]);if(d.Budget>0)Add("damage",$"对体力值合计为 {d.Budget} 的角色各造成1点伤害",[]);Add("skip","不发动",[]);return choices;}
        var sum=d.Participants.Where(p=>selected.Contains(p.Seat)).Sum(p=>p.Hp);
        foreach(var p in d.Participants.Where(p=>!selected.Contains(p.Seat) && (d.Mode=="draw" ? selected.Count<d.Budget : p.Hp>0&&sum+p.Hp<=d.Budget)))
            Add($"seat:{p.Seat}",$"选择 {_players[p.Seat].Name}（体力 {p.Hp}）",[p.Seat]);
        if(d.Mode=="draw" || selected.Count>0&&sum==d.Budget)Add("finish","确认目标并依次结算",selected);
        Add("reset","重新选择模式",[]);return choices;
    }
    private void ResolveDiscardBudgetChoice(PromptChoice c)
    {
        var f=GetActiveProgramFrame(long.Parse(c.Parameters["frame-id"]));var d=f.DiscardBudgetDraft!;
        c=DiscardBudgetChoices(f).Single(x=>x.Id==c.Id);ClearPendingDecision();
        if(!BudgetGiftSourceValid(f)){FinishProgramSkill(f,false);return;}
        var mode=c.Parameters["mode"];
        if(mode=="skip"){ReplaceRuntimeTop(f with { DiscardBudgetDraft=null });AdvanceRuntimeProgram(f.Id);return;}
        if(mode=="reset")d=d with { Mode=null,Selected=[] };
        else if(mode is "draw" or "damage")d=d with { Mode=mode,Selected=[] };
        else if(mode=="finish")
        {
            d=d with { Committed=true,Cursor=0 };f=f with { DiscardBudgetDraft=d };ReplaceRuntimeTop(f);
            AdvanceEventRulesAndQueueFact(new ProgramDiscardBudgetCommittedEvent(f.Id,f.OwnerSeat,d.Budget,d.Mode!,d.Selected!.Select(seat=>d.Participants.Single(p=>p.Seat==seat)).ToArray()));
            AdvanceRuntimeProgram(f.Id);return;
        }
        else d=d with { Selected=d.Selected!.Append(c.Targets.Single()).ToArray() };
        f=f with { DiscardBudgetDraft=d };ReplaceRuntimeTop(f);PublishBudgetGiftPrompt(f,f.OwnerSeat,d.Mode=="draw"
            ? $"请选择至多 {d.Budget} 名角色。已选 {d.Selected!.Count} 名。"
            : $"请选择体力值合计为 {d.Budget} 的角色。已选体力合计 {d.Participants.Where(p=>d.Selected!.Contains(p.Seat)).Sum(p=>p.Hp)}。",DiscardBudgetChoices(f));
    }
    private bool ResumeBudgetGift(long id)
    {
        var f=GetActiveProgramFrame(id);
        if(f.DiscardBudgetDraft is { Committed:true } d)
        {
            if(_winner!=Winner.None){ReplaceRuntimeTop(f with { DiscardBudgetDraft=null });return false;}
            var cursor=d.Cursor;while(cursor<d.Selected!.Count&&!_players[d.Selected[cursor]].IsAlive)cursor++;
            if(cursor>=d.Selected.Count){ReplaceRuntimeTop(f with { DiscardBudgetDraft=null });return false;}
            var seat=d.Selected[cursor];f=f with { DiscardBudgetDraft=d with { Cursor=cursor+1 } };ReplaceRuntimeTop(f);
            if(d.Mode=="damage")BeginProgramSkillDamage(f,seat,1);
            else {DrawProgramCards(f.Id,seat,1,null,null,SkillProgramCardSetVisibility.Private,new("skill-program.discard-budget.draw"));if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(f.Id);}
            return true;
        }
        if(f.CompletedFactionGiftDraft is { } gift && gift.Stage is "movement" or "draw")
        {
            if(!CompletedFactionGiftLive(f,gift)){ReplaceRuntimeTop(f with { CompletedFactionGiftDraft=null });return false;}
            if(gift.Stage=="movement")
            {
                f=f with { CompletedFactionGiftDraft=gift with { Stage="reward" } };ReplaceRuntimeTop(f);
                PublishBudgetGiftPrompt(f,f.OwnerSeat,"令赠牌角色摸一张牌且本回合杀次数+1？",CompletedFactionGiftChoices(f));return true;
            }
            var granted=_turnCardUseEffects.GrantRuleModifier(_turnNumber,_currentSeat,f.Id,f.InstructionIndex-1,CreateProgramTurnEffectSource(f),SkillRuleQuery.SlashLimit,SkillRuleOperation.Add,1,affectedSeat:gift.ProviderSeat);
            AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(granted));ReplaceRuntimeTop(f with { CompletedFactionGiftDraft=null });return false;
        }
        return false;
    }
    private SkillProgramStepOutcome PreventOwnPlayOutsideRange(ProgramSkillFrame f)
    {
        if(f.WindowContext is not { Window:SkillProgramTriggerWindow.BeforeDamageApplied,SourceSeat:{ } source,TargetSeat:{ } target })throw new InvalidOperationException("Range prevention requires its real damage window.");
        if(source==f.OwnerSeat && source==_currentSeat && _phase==TurnPhase.Play && !IsWithinAttackRange(target,source))PreventProgramCurrentDamage(f);
        return SkillProgramStepOutcome.Continue;
    }
    private int[] OutsideRangePlayers(int owner)=>_players.Where(p=>p.IsAlive&&p.Seat!=owner&&!IsWithinAttackRange(p.Seat,owner)).Select(p=>p.Seat).ToArray();
    private IReadOnlyList<PromptChoice> OutsideRangeDiscardChoices(ProgramSkillFrame f)=>BuildOtherOwnedCardDiscardChoices(f.Id,f.OwnerSeat,[CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.Judgment],new($"skill-program.{f.SkillId}.ChooseOtherOwnedCardDiscard"))
        .Where(c=>OutsideRangePlayers(f.OwnerSeat).Contains(c.Targets.Single())).Select(c=>c with { Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="outside-range-discard"} }).ToArray();
    private SkillProgramStepOutcome BeginOutsideRangeDiscard(ProgramSkillFrame f)
    {
        if(f.WindowContext?.Window!=SkillProgramTriggerWindow.PlayEnding||_phase!=TurnPhase.Play||f.OwnerSeat!=_currentSeat)throw new InvalidOperationException("Range discard requires its own actual play end.");
        var choices=OutsideRangeDiscardChoices(f);
        if(GetActualPlayPhaseUseCount(f.OwnerSeat)>=OutsideRangePlayers(f.OwnerSeat).Length||choices.Count==0)return SkillProgramStepOutcome.Continue;
        PublishBudgetGiftPrompt(f,f.OwnerSeat,"使用牌数不足，弃置攻击范围不包含你的角色一张牌。",choices,true);return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ResolveOutsideRangeDiscard(PromptChoice c)
    {
        var f=GetActiveProgramFrame(long.Parse(c.Parameters["frame-id"]));
        if(HasForeignDiscardCapability && c.Targets is [var publishedOwner] &&
            Enum.TryParse<CardZoneKind>(c.Parameters.GetValueOrDefault("source-zone"),out var publishedZone) &&
            int.TryParse(c.Parameters.GetValueOrDefault("slot-index"),out var publishedSlot) &&
            publishedSlot>=0 && publishedSlot<_cardZones.CardsAt(new(publishedZone,publishedOwner)).Count &&
            IsForeignEquipmentDiscardPrevented(f.OwnerSeat,_cardZones.CardsAt(new(publishedZone,publishedOwner))[publishedSlot],new(publishedZone,publishedOwner),OwnedCardMoveIntent.Discard))
        {ClearPendingDecision();CancelProgramBindingAndCleanup(f,"公布的弃牌选择已失效。");return;}
        c=OutsideRangeDiscardChoices(f).Single(x=>x.Id==c.Id);ClearPendingDecision();
        if(!BudgetGiftSourceValid(f)){FinishProgramSkill(f,false);return;}
        var owner=c.Targets.Single();var zone=Enum.Parse<CardZoneKind>(c.Parameters["source-zone"]);var slot=int.Parse(c.Parameters["slot-index"]);var from=new CardLocation(zone,owner);var card=_cardZones.CardsAt(from)[slot];
        if(IsForeignEquipmentDiscardPrevented(f.OwnerSeat,card,from,OwnedCardMoveIntent.Discard)){CancelProgramBindingAndCleanup(f,"公布的弃牌选择已失效。");return;}MoveCard(card,from,CardLocation.DiscardPile,new($"skill-program.{f.SkillId}.ChooseOtherOwnedCardDiscard"));if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(f.Id);
    }
    private CardActionContext FactionGiftAction(ProgramSkillFrame f)=>f.WindowContext is { Window:SkillProgramTriggerWindow.CardUseCompleted,CardUse:{ } use }?
        _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w=>w.Id==f.WindowContext.ParentFrameId&&w.Action.ActionId==use.CardActionId).Action:throw new InvalidOperationException("Faction gift lost its actual completed action.");
    private string? CompletedFactionGiftFaction(ProgramSkillFrame f)=>ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect.ProviderFactionId;
    private bool CompletedFactionGiftLive(ProgramSkillFrame f,ProgramCompletedFactionGiftDraft d)=>BudgetGiftSourceValid(f)&&HasSkillRoleQualification(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId,Role.Lord) && _players[d.ProviderSeat].IsAlive&&GetEffectiveFactionId(_players[d.ProviderSeat])==CompletedFactionGiftFaction(f)&&d.ProviderSeat!=f.OwnerSeat&&_phase==TurnPhase.Play&&_currentSeat==d.ProviderSeat&&d.PhaseInstanceId==_cardUseDebitPhaseInstanceId && FactionGiftAction(f) is { Type:CardActionType.Use } action && action.ActionId==d.ActionId&&action.ActorSeat==d.ProviderSeat&&IsSlashCard(action.EffectiveKind);
    private int[] AvailableFactionGiftCosts(CardActionContext a)=>a.PhysicalCards.Where(p=>_cardZones.GetLocation(p.CardId).Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile).Select(p=>p.CardId).ToArray();
    private bool FactionGiftUsed(int provider)=>EventsSinceLastBoundary(e=>e is PhaseChangedEvent or TurnStartedEvent).OfType<CompletedFactionCostGiftedEvent>().Any(e=>e.ProviderSeat==provider&&e.TurnNumber==_turnNumber&&e.PhaseInstanceId==_cardUseDebitPhaseInstanceId);
    private SkillProgramStepOutcome BeginCompletedFactionGift(ProgramSkillFrame f,string faction)
    {
        var action=FactionGiftAction(f);var provider=_players[action.ActorSeat];var ids=AvailableFactionGiftCosts(action);
        var d=new ProgramCompletedFactionGiftDraft(action.ActionId,provider.Seat,_cardUseDebitPhaseInstanceId,ids);
        if(!CompletedFactionGiftLive(f,d)||GetEffectiveFactionId(provider)!=faction||FactionGiftUsed(provider.Seat)||ids.Length==0)return SkillProgramStepOutcome.Continue;
        f=f with { CompletedFactionGiftDraft=d };ReplaceRuntimeTop(f);PublishBudgetGiftPrompt(f,provider.Seat,$"将本次【杀】的牌交给 {_players[f.OwnerSeat].Name}？",CompletedFactionGiftChoices(f),true);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> CompletedFactionGiftChoices(ProgramSkillFrame f)
    {
        var d=f.CompletedFactionGiftDraft!;var reward=d.Stage=="reward";var action=reward?"reward":"give";
        return [new(new($"faction-gift.{f.Id}.{action}"),reward?"令赠牌角色摸一张牌且杀次数+1":"交出本次【杀】的牌",reward?[]:d.CardIds,[reward?d.ProviderSeat:f.OwnerSeat],BudgetGiftParameters(f,"faction-cost-gift",action)),
            new(new($"faction-gift.{f.Id}.decline"),reward?"不奖励":"不赠予",[],[],BudgetGiftParameters(f,"faction-cost-gift","decline"))];
    }
    private void ResolveCompletedFactionGift(PromptChoice c)
    {
        var f=GetActiveProgramFrame(long.Parse(c.Parameters["frame-id"]));var d=f.CompletedFactionGiftDraft!;c=CompletedFactionGiftChoices(f).Single(x=>x.Id==c.Id);ClearPendingDecision();
        if(c.Parameters["mode"]=="decline"||!CompletedFactionGiftLive(f,d)){ReplaceRuntimeTop(f with { CompletedFactionGiftDraft=null });AdvanceRuntimeProgram(f.Id);return;}
        if(d.Stage=="reward")
        {
            f=f with { CompletedFactionGiftDraft=d with { Stage="draw" } };ReplaceRuntimeTop(f);
            DrawProgramCards(f.Id,d.ProviderSeat,1,null,null,SkillProgramCardSetVisibility.Private,new("skill-program.faction-cost-gift.reward"));if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(f.Id);return;
        }
        var effect=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        if(GetEffectiveFactionId(_players[d.ProviderSeat])!=effect.ProviderFactionId||FactionGiftUsed(d.ProviderSeat)||!AvailableFactionGiftCosts(FactionGiftAction(f)).SequenceEqual(d.CardIds))throw new InvalidOperationException("Faction gift lost its exact phase, physical costs or faction.");
        AdvanceEventRulesAndQueueFact(new CompletedFactionCostGiftedEvent(f.Id,d.ActionId,d.ProviderSeat,f.OwnerSeat,f.SkillId,f.SkillInstanceId,_turnNumber,d.PhaseInstanceId,d.CardIds));
        f=f with { CompletedFactionGiftDraft=d with { Stage="movement" } };ReplaceRuntimeTop(f);
        var moves=d.CardIds.Select(id=>{var from=_cardZones.GetLocation(id);return (Card:_cardZones.CardsAt(from).Single(card=>card.Id==id),From:from);}).ToArray();
        var batch=BeginCardMovementBatch(moves.Select(m=>m.From),[CardLocation.Hand(f.OwnerSeat)]);var records=new List<CardMovementRecord>();var committed=false;
        try {foreach(var m in moves)_cardZones.Move(m.Card.Id,m.From,CardLocation.Hand(f.OwnerSeat));foreach(var m in moves)records.Add(RecordMovement(m.Card,m.From,CardLocation.Hand(f.OwnerSeat),new("skill-program.faction-cost-gift.obtain")));committed=true;}
        finally {CompleteCardMovementBatch(batch,records,committed);}
        if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(f.Id);
    }
    private void AssertBudgetGiftDraft(ProgramSkillFrame f,SkillProgramEffect paused)
    {
        if(f.DiscardBudgetDraft is { } d)
        {
            if(paused.Op!=SkillProgramEffectOp.ResolveDiscardBudgetParticipants||f.WindowContext?.Window!=SkillProgramTriggerWindow.DiscardPhaseEnded||d.Budget<0||d.Participants.Any(p=>!IsValidPlayerSeat(p.Seat))||d.Participants.Select(p=>p.Seat).Distinct().Count()!=d.Participants.Count||d.Selected is null||d.Selected.Distinct().Count()!=d.Selected.Count||d.Selected.Any(seat=>!d.Participants.Any(p=>p.Seat==seat))||d.Cursor<0||d.Cursor>d.Selected.Count||d.Mode is not (null or "draw" or "damage"))throw new InvalidOperationException("Discard budget lost its frozen selection or effect cursor.");
            var selected=d.Selected.Select(seat=>d.Participants.Single(p=>p.Seat==seat)).ToArray();
            if(d.Mode=="draw"&&selected.Length>d.Budget || d.Mode=="damage"&&(selected.Any(p=>p.Hp<=0)||selected.Sum(p=>p.Hp)>d.Budget) || d.Committed&&(d.Mode is null||d.Mode=="damage"&&(selected.Length==0||selected.Sum(p=>p.Hp)!=d.Budget)))throw new InvalidOperationException("Discard selection exceeded its frozen participant budget.");
            if(d.Committed && !CompleteProgramEventHistory().OfType<ProgramDiscardBudgetCommittedEvent>().Any(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.Budget==d.Budget&&e.Mode==d.Mode&&e.Participants.SequenceEqual(selected)))throw new InvalidOperationException("Discard sequence lost its exact public commitment.");
            if(!d.Committed&&ReferenceEquals(f,_resolutionStack.LastOrDefault())&&(_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual(_pendingDecision.Choices,DiscardBudgetChoices(f))))throw new InvalidOperationException("Discard budget lost its legal prompt.");
        }
        if(f.CompletedFactionGiftDraft is { } gift)
        {
            var a=FactionGiftAction(f);
            if(paused.Op!=SkillProgramEffectOp.OfferCompletedFactionCostGift||gift.ActionId!=a.ActionId||!IsValidPlayerSeat(gift.ProviderSeat)||gift.PhaseInstanceId<=0||gift.ProviderSeat!=a.ActorSeat||gift.CardIds.Count==0||gift.CardIds.Distinct().Count()!=gift.CardIds.Count||gift.CardIds.Any(id=>!a.PhysicalCards.Any(p=>p.CardId==id))||gift.Stage is not ("offer" or "movement" or "reward" or "draw"))throw new InvalidOperationException("Faction gift lost its exact action/cost parent.");
            if(gift.Stage!="offer"&&!CompleteProgramEventHistory().OfType<CompletedFactionCostGiftedEvent>().Any(e=>e.FrameId==f.Id&&e.ActionId==gift.ActionId&&e.ProviderSeat==gift.ProviderSeat&&e.RecipientSeat==f.OwnerSeat&&e.SkillInstanceId==f.SkillInstanceId&&e.PhaseInstanceId==gift.PhaseInstanceId&&e.CardIds.SequenceEqual(gift.CardIds)))throw new InvalidOperationException("Faction reward lost its accepted real gift.");
            if(ReferenceEquals(f,_resolutionStack.LastOrDefault())&&gift.Stage is "offer" or "reward"&&(_pendingDecision?.PlayerSeat!=(gift.Stage=="offer"?gift.ProviderSeat:f.OwnerSeat)||!AssistedChoicesEqual(_pendingDecision.Choices,CompletedFactionGiftChoices(f))))throw new InvalidOperationException("Faction gift lost its exact chooser and legal prompt.");
        }
    }
}
