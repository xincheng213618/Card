namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string ShuziGiftReason = "skill-program.han-fu.shuzi.gift", ShuziReturnReason = "skill-program.han-fu.shuzi.return", ShuziBoardReason = "skill-program.han-fu.shuzi.board",
        KuangshouDrawReason = "skill-program.han-fu.kuangshou.draw", KuangshouDiscardReason = "skill-program.han-fu.kuangshou.discard";
    private static CardConversionSource HanFuSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private HanFuMaterial HanFuMaterialFor(int id)
    {
        var card = GetAdvancedCard(id); var from = _cardZones.GetLocation(id);
        return new(id, card.Kind, ProgramBasicCardName(from is { Zone: CardZoneKind.Hand, OwnerSeat: { } owner } ? AdvancedEffectiveHandKind(_players[owner], card) :
            from.Zone == CardZoneKind.Judgment ? GetJudgmentEffectiveCardKind(card) : card.Kind), from,card.IsGeneralWeapon);
    }
    private IReadOnlyList<HanFuMaterial> HanFuDiscardCards(ProgramSkillFrame f) => Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard)
        .Select(c => HanFuMaterialFor(c.Cards.Single())).Where(m => !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat,GetAdvancedCard(m.CardId),m.From,OwnedCardMoveIntent.Discard)).ToArray());
    private IReadOnlyList<HanFuMaterial> ShuziBoardCards(int target) => Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != target).SelectMany(p => GetEquipment(p).Concat(GetJudgment(p)))
        .Where(c => !c.IsGeneralWeapon && !IsForeignEquipmentDiscardPrevented(_cardZones.GetLocation(c.Id).OwnerSeat!.Value, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Transfer) &&
            CanMoveProgramCardToCorrespondingZone(c, _cardZones.GetLocation(c.Id).Zone, target)).Select(c => HanFuMaterialFor(c.Id)).ToArray());
    private int[] KuangshouDamagedSeats(int cutoff)
    {
        var events = CompleteProgramEventHistory().Take(cutoff).ToArray(); var start = Array.FindLastIndex(events, e => e is TurnStartedEvent t && t.TurnNumber == _turnNumber);
        return events.Skip(start + 1).OfType<DamageAppliedEvent>().Where(e => e.Amount > 0).Select(e => e.TargetSeat).Distinct().Order().ToArray();
    }
    [ProgramCapability]
    private sealed class HanFuCapability(GameEngine engine) : IHanFuHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginHanFu(f,op); }
    private SkillProgramStepOutcome BeginHanFu(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id); var shuzi = op == SkillProgramEffectOp.ResolveShuziExchange;
        if (f.HanFu is not null || f.InstructionIndex != 1) throw new InvalidOperationException("HanFu requires a new exact first instruction.");
        int target; long parent = 0; long? damage = null; var amount = 0; HanFuMaterial[]? gifts = null;
        if (shuzi)
        {
            var a = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Activation;
            if (a is null || f.TriggerId is not null || f.WindowContext is not null || f.SelectedTargetSeats is not [var recipient] || !IsValidPlayerSeat(recipient) || !_players[recipient].IsAlive ||
                f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || _programPhaseUses.GetValueOrDefault((f.OwnerSeat,f.SkillId,a.UsageGroup)) != 1 || f.SelectedCardIds.Count != 2 || f.SelectedCardIds.Distinct().Count() != 2)
                throw new InvalidOperationException("Shuzi lost its actual once-per-Play two-card activation and living recipient.");
            HanFuComposition.Activation(f.SkillId,a); target = recipient; gifts = f.SelectedCardIds.Select(HanFuMaterialFor).ToArray();
            if (gifts.Any(m => m.From.OwnerSeat != f.OwnerSeat || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || GetAdvancedCard(m.CardId).IsGeneralWeapon ||
                IsForeignEquipmentDiscardPrevented(f.OwnerSeat,GetAdvancedCard(m.CardId),m.From,OwnedCardMoveIntent.Transfer)))
                throw new InvalidOperationException("Shuzi requires two transferable actual owned hand or equipment entities.");
        }
        else
        {
            if (f.TriggerId is null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0 } context ||
                context.TargetSeat != f.OwnerSeat) throw new InvalidOperationException("Kuangshou requires its actual positive whole-damage observer.");
            target = f.OwnerSeat; parent = context.ParentFrameId; damage = context.DamageFrameId; amount = context.Amount;
        }
        var r = new HanFuReceipt(HanFuSource(f),f.GameplayHash,op,f.InstructionIndex,_turnNumber,_cardUseDebitPhaseInstanceId,target,parent,damage,amount,
            shuzi ? HanFuStage.GiftChildren : HanFuStage.DrawChildren,Gifts:gifts,Movements:[]);
        ReplaceRuntimeTop(f = f with { HanFu = r });
        AdvanceEventRulesAndQueueFact(new HanFuStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ActualTurn,r.PhaseInstance,target,parent,damage,amount));
        if (shuzi) PayHanFuCards(f,gifts!,CardLocation.Hand(target),ShuziGiftReason,HanFuStage.GiftChildren);
        else
        {
            var before = _movementSequence; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat,0,null) });
            var count = DrawCards(_players[f.OwnerSeat],3,true,new(KuangshouDrawReason)).Count; RecordHanFuMovement(f.Id,HanFuStage.DrawChildren,KuangshouDrawReason,before,count);
        }
        ContinueHanFu(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void RecordHanFuMovement(long id, HanFuStage stage, string reason, long before, int actual)
    {
        var f = GetActiveProgramFrame(id); var r = f.HanFu!; var m = new HanFuMovement(r.Movements!.Count,stage,reason,before,_movementSequence,actual);
        ReplaceRuntimeTop(f with { HanFu = r with { Movements = r.Movements.Append(m).ToArray() } }); AdvanceEventRulesAndQueueFact(new HanFuMovementIssuedEvent(id,m));
    }
    private void PayHanFuCards(ProgramSkillFrame f, IReadOnlyList<HanFuMaterial> cards, CardLocation destination, string reason, HanFuStage stage)
    {
        var before = _movementSequence; ReplaceRuntimeTop(f = f with { HanFu = f.HanFu! with { Stage = stage }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
        var ids = cards.Where(m => m.From != destination).Select(m => m.CardId).ToArray();
        if (ids.Length > 0)
        {
            if(stage==HanFuStage.DiscardChildren)MoveProgramLogicalDiscard(f,f.OwnerSeat,ids,new(reason));
            else MoveProgramCardsFromMultipleSources(ids,destination,new(reason));
        }
        for (var i = 0; i < cards.Count; i++)
        {
            var m = cards[i]; var move = _cardMovements.SingleOrDefault(move => move.Sequence > before && move.CardId == m.CardId && move.Reason.Value == reason);
            AdvanceEventRulesAndQueueFact(new HanFuMaterialPaidEvent(f.Id,stage,i,m,move?.To??destination,move?.Sequence));
        }
        RecordHanFuMovement(f.Id,stage,reason,before,ids.Length);
    }
    private IReadOnlyList<PromptChoice> HanFuChoices(ProgramSkillFrame f)
    {
        var r = f.HanFu!;
        PromptChoice Choice(string branch,string label,IReadOnlyList<int>? cards = null) => new(new($"hanfu.{f.Id}.{r.Stage}.{branch}.{string.Join('.',cards??[])}"),label,cards??[],[],new Dictionary<string,string> { ["program-action"]="han-fu",["branch"]=branch });
        if (r.Stage == HanFuStage.ReturnChoice) return Array.AsReadOnly(GetHand(_players[r.TargetSeat]).Select(c => Choice("return","交给韩馥【"+c.DisplayName+"】",[c.Id])).ToArray());
        if (r.Stage == HanFuStage.Benefit)
        {
            var result = new List<PromptChoice> { Choice("skip","不执行额外效果") };
            if (_players[r.TargetSeat].IsAlive) result.Add(Choice("damage","对其造成1点伤害"));
            if (ShuziBoardCards(r.TargetSeat).Count > 0) result.Add(Choice("board","将其他角色场上的一张牌移入其对应区域"));
            return Array.AsReadOnly(result.ToArray());
        }
        if (r.Stage == HanFuStage.BoardChoice) return Array.AsReadOnly(ShuziBoardCards(r.TargetSeat).Select(m => Choice("move","移动"+_players[m.From.OwnerSeat!.Value].Name+"的【"+GetAdvancedCard(m.CardId).DisplayName+"】",[m.CardId])).Prepend(Choice("skip","不移动")).ToArray());
        if (r.Stage == HanFuStage.DiscardChoice) return Array.AsReadOnly(r.DiscardCandidates!.Where(m => !r.SelectedDiscard!.Contains(m.CardId))
            .Select(m => Choice("discard",$"弃置【{GetAdvancedCard(m.CardId).DisplayName}】（{r.SelectedDiscard!.Count+1}/{r.RequiredDiscard}）",[m.CardId])).ToArray());
        return [];
    }
    private void PublishHanFuChoice(ProgramSkillFrame f)
    {
        var r = f.HanFu!; var choices = HanFuChoices(f); if (choices.Count == 0) { FinishHanFu(f); return; }
        var chooser = r.Stage == HanFuStage.ReturnChoice ? r.TargetSeat : f.OwnerSeat; var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger,chooser,r.Stage == HanFuStage.ReturnChoice ? "束辎：交给韩馥一张自己的手牌。" : r.Stage == HanFuStage.DiscardChoice ? $"恇守：弃置{r.RequiredDiscard}张牌。" : "束辎：选择额外效果。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(),[],f.OwnerSeat)
        { PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=chooser,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("han-fu")]
    private void AnswerHanFu(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HanFu: { } r } f || _pendingDecision?.PlayerSeat != (r.Stage==HanFuStage.ReturnChoice?r.TargetSeat:f.OwnerSeat) || !HanFuChoices(f).Any(c => AssistedChoicesEqual([c],[choice])))
            throw new InvalidOperationException("HanFu requires the original chooser's exact published private physical-card or benefit choice.");
        AssertHanFu(f); ClearPendingDecision(); var branch=choice.Parameters["branch"];
        if (branch=="skip") { FinishHanFu(f); return; }
        if (branch=="return")
        { var material=HanFuMaterialFor(choice.Cards.Single()); ReplaceRuntimeTop(f=f with { HanFu=r with { Returned=material } }); PayHanFuCards(f,[material],CardLocation.Hand(f.OwnerSeat),ShuziReturnReason,HanFuStage.ReturnChildren); ContinueHanFu(f.Id); return; }
        if (branch=="board") { ReplaceRuntimeTop(f=f with { HanFu=r with { Stage=HanFuStage.BoardChoice } }); PublishHanFuChoice(f); return; }
        if (branch=="move") { PayShuziBoard(f,HanFuMaterialFor(choice.Cards.Single())); ContinueHanFu(f.Id); return; }
        if (branch=="damage")
        {
            ReplaceRuntimeTop(f=f with { HanFu=r with { Stage=HanFuStage.DamageIssued } }); AdvanceEventRulesAndQueueFact(new ShuziDamageIssuedEvent(f.Id,f.OwnerSeat,r.TargetSeat,1)); BeginProgramSkillDamage(f,r.TargetSeat,1); return;
        }
        if (branch!="discard") throw new InvalidOperationException("Unknown HanFu choice.");
        var selected=r.SelectedDiscard!.Append(choice.Cards.Single()).ToArray(); ReplaceRuntimeTop(f=f with { HanFu=r with { SelectedDiscard=selected } });
        if (selected.Length<r.RequiredDiscard) { PublishHanFuChoice(f); return; }
        PayHanFuCards(f,selected.Select(id=>r.DiscardCandidates!.Single(m=>m.CardId==id)).ToArray(),CardLocation.DiscardPile,KuangshouDiscardReason,HanFuStage.DiscardChildren); ContinueHanFu(f.Id);
    }
    private void PayShuziBoard(ProgramSkillFrame f,HanFuMaterial m)
    {
        var r=f.HanFu!; var card=GetAdvancedCard(m.CardId); var destination=ProgramCorrespondingZoneLocation(card,m.From.Zone,r.TargetSeat); var before=_movementSequence; int? replacedId=null;
        ReplaceRuntimeTop(f=f with { HanFu=r with { Stage=HanFuStage.BoardChildren,Board=m },PendingMovementContinuation=new(f.OwnerSeat,0,null) });
        if (destination.Zone==CardZoneKind.Equipment)
        {
            var old=GetEquipment(_players[r.TargetSeat]).SingleOrDefault(c=>EquipmentCatalog.Get(c.Kind).Slot==EquipmentCatalog.Get(card.Kind).Slot); replacedId=old?.Id;
            if(old is not null) MoveCard(old,destination,CardLocation.DiscardPile,CardMoveReasons.EquipmentReplace);
        }
        MoveProgramCardsFromMultipleSources([m.CardId],destination,new(ShuziBoardReason));
        var movement=_cardMovements.Single(move=>move.Sequence>before && move.CardId==m.CardId && move.Reason.Value==ShuziBoardReason);
        AdvanceEventRulesAndQueueFact(new HanFuMaterialPaidEvent(f.Id,HanFuStage.BoardChildren,0,m,destination,movement.Sequence));
        if(destination.Zone==CardZoneKind.Equipment) AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(f.Id,r.TargetSeat,EquipmentCatalog.Get(card.Kind).Slot,card.Id,card.Kind,replacedId));
        RecordHanFuMovement(f.Id,HanFuStage.BoardChildren,ShuziBoardReason,before,1);
    }
    private void FreezeKuangshouDiscard(ProgramSkillFrame f)
    {
        var cutoff=CompleteProgramEventHistory().Count(); var seats=KuangshouDamagedSeats(cutoff); var candidates=HanFuDiscardCards(f); var required=Math.Min(seats.Length,candidates.Count);
        ReplaceRuntimeTop(f=f with { HanFu=f.HanFu! with { Stage=HanFuStage.DiscardChoice,HistoryCutoff=cutoff,DamagedSeats=seats,RequiredDiscard=required,DiscardCandidates=candidates,SelectedDiscard=[] } });
        AdvanceEventRulesAndQueueFact(new KuangshouQuotaFrozenEvent(f.Id,cutoff,seats.Length,required));
        if(required==0) { PayHanFuCards(f,[],CardLocation.DiscardPile,KuangshouDiscardReason,HanFuStage.DiscardChildren); ContinueHanFu(f.Id); }
        else PublishHanFuChoice(f);
    }
    private bool DrainHanFuChildren(long id)=>TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueHanFu(long id)
    {
        while(_resolutionStack.LastOrDefault() is ProgramSkillFrame { HanFu: { } r } f && f.Id==id)
        {
            AssertHanFu(f);
            if(r.Stage==HanFuStage.DamageIssued) { if(f.AttackAttempt is not null) return; FinishHanFu(f); return; }
            if(r.Stage is HanFuStage.ReturnChoice or HanFuStage.Benefit or HanFuStage.BoardChoice or HanFuStage.DiscardChoice) { if(_pendingDecision is null) PublishHanFuChoice(f); return; }
            if(DrainHanFuChildren(id)) return;
            ReplaceRuntimeTop(f=GetActiveProgramFrame(id) with { PendingMovementContinuation=null });
            if(_winner!=Winner.None || !_players[f.OwnerSeat].IsAlive || r.Op==SkillProgramEffectOp.ResolveShuziExchange && !_players[r.TargetSeat].IsAlive) { FinishHanFu(f); return; }
            if(r.Stage==HanFuStage.GiftChildren) { ReplaceRuntimeTop(f=f with { HanFu=f.HanFu! with { Stage=HanFuStage.ReturnChoice } }); PublishHanFuChoice(f); return; }
            if(r.Stage==HanFuStage.ReturnChildren && r.Returned is { } returned && r.Gifts!.Any(m=>m.Name==returned.Name)) { ReplaceRuntimeTop(f=f with { HanFu=f.HanFu! with { Stage=HanFuStage.Benefit } }); PublishHanFuChoice(f); return; }
            if(r.Stage==HanFuStage.DrawChildren) { FreezeKuangshouDiscard(f); return; }
            FinishHanFu(f); return;
        }
    }
    private void FinishHanFu(ProgramSkillFrame f)
    { AdvanceEventRulesAndQueueFact(new HanFuCompletedEvent(f.Id)); ReplaceRuntimeTop(f=f with { HanFu=null,PendingMovementContinuation=null }); FinishProgramSkill(f,true); }
    [ProgramContinuation(ProgramContinuationKind.Resume,27020)]
    private bool ResumeHanFu(long id) { if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HanFu:not null } f || f.Id!=id) return false; ContinueHanFu(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement,27020)]
    private bool ReturnHanFuMovement(ProgramSkillFrame f) { if(f.HanFu is null) return false; ContinueHanFu(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShuziExchange,SkillProgramEffectOp.ResolveKuangshouDamageDrawDiscard)]
    private PromptChoice AiHanFu(PendingDecision p,ProgramSkillFrame f)=>p.Choices.OrderByDescending(c=>c.Parameters.GetValueOrDefault("branch")=="damage")
        .ThenBy(c=>c.Parameters.GetValueOrDefault("branch")=="skip").ThenBy(c=>c.Id.Value,StringComparer.Ordinal).First();
}
