namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string JiaweiDiscard = "skill-program.yang-feng.jiawei.discard", JiaweiOwnerGain = "skill-program.yang-feng.jiawei.owner-gain",
        JiaweiTargetGain = "skill-program.yang-feng.jiawei.target-gain", JiaweiCleanup = "skill-program.yang-feng.jiawei.cleanup",
        QujiaGain = "skill-program.yang-feng.qujia.gain", JiaweiRevealBind = "jiawei-public-top";
    private static CardConversionSource YangFengSource(ProgramSkillFrame f) => new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private static bool IsJiaweiDamageCard(CardKind kind) => IsSlashCard(kind) || kind is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.Lightning or CardKind.UnexpectedAssault;
    private int YangFengCompletedTurns(int owner, int cutoff) => CompleteProgramEventHistory().Take(cutoff).OfType<TurnEndedEvent>().Count(e => e.ActorSeat == owner);
    private IReadOnlyList<YangFengMaterial> JiaweiDiscardCandidates(ProgramSkillFrame f,int target) => Array.AsReadOnly(
        BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,target,[CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.Judgment],OwnedCardMoveIntent.Discard,
            canSelect:(zone,card)=>!IsSelfHandCategoryDiscardForbidden(f.OwnerSeat,card,new(zone,target),OwnedCardMoveIntent.Discard))
        .Select(c => { var from = new CardLocation(Enum.Parse<CardZoneKind>(c.Parameters["source-zone"]),target);var slot = int.Parse(c.Parameters["slot-index"]);var card = _cardZones.CardsAt(from)[slot];return new YangFengMaterial(card.Id,card.Kind,from,slot,card.IsGeneralWeapon); }).ToArray());
    [ProgramCapability]
    private sealed class YangFengCapability(GameEngine engine) : IYangFengHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op) => engine.BeginYangFeng(f,op); }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27150)]
    private bool QualifyYangFeng(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
    {
        if(!trigger.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveQujiaLimitedTurnEnd))return true;
        return context.Window==SkillProgramTriggerWindow.AfterTurnEnded&&candidate.OwnerSeat==_currentSeat&&context.TargetSeat==candidate.OwnerSeat&&_phase==TurnPhase.Finished&&
            !ProgramEventHistory<YangFengStartedEvent>().Any(e=>e.Op==SkillProgramEffectOp.ResolveQujiaLimitedTurnEnd&&e.Source.OwnerSeat==candidate.OwnerSeat&&e.Source.SkillId==candidate.SkillId);
    }
    private SkillProgramStepOutcome BeginYangFeng(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);var jiawei = op == SkillProgramEffectOp.ResolveJiaweiDiscardReveal;
        if(f.YangFeng is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0) throw new InvalidOperationException("Yang Feng requires an exact unissued first instruction.");
        var cutoff = CompleteProgramEventHistory().Count();var completed = YangFengCompletedTurns(f.OwnerSeat,cutoff);var x = jiawei ? Math.Min(completed,_players[f.OwnerSeat].MaxHp) : 0;
        int target;long parent = 0;IReadOnlyList<YangFengMaterial> candidates = [];
        if(jiawei)
        {
            var a = ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Activation;
            if(a is null || f.TriggerId is not null || f.WindowContext is not null || f.SelectedTargetSeats is not [var selected] || !IsValidPlayerSeat(selected) || !_players[selected].IsAlive ||
                f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || _programPhaseUses.GetValueOrDefault((f.OwnerSeat,f.SkillId,a.UsageGroup)) != 1)
                throw new InvalidOperationException("Jiawei requires its real once-per-Play any-living target activation.");
            YangFengComposition.Activation(f.SkillId,a);target = selected;candidates = JiaweiDiscardCandidates(f,target);
        }
        else
        {
            if(f.TriggerId is null || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { Window:SkillProgramTriggerWindow.AfterTurnEnded } context ||
                f.OwnerSeat != _currentSeat || _phase != TurnPhase.Finished || context.TargetSeat != f.OwnerSeat ||
                _resolutionStack.OfType<DeferredTurnEndFrame>().LastOrDefault() is not {} ended || !MatchesAfterTurnEndedChild(ended,f,context) ||
                context.Facts is not { OwnerKilledThisTurn:false }||
                ProgramEventHistory<YangFengStartedEvent>().Any(e=>e.Op==op&&e.Source.OwnerSeat==f.OwnerSeat&&e.Source.SkillId==f.SkillId))
                throw new InvalidOperationException("Qujia requires its genuine no-kill completed own actual turn.");
            YangFengComposition.Trigger(f.SkillId,GetProgramTrigger(f));target = f.OwnerSeat;parent = context.ParentFrameId;
        }
        var required = Math.Min(x,candidates.Count);
        var r = new YangFengReceipt(YangFengSource(f),f.GameplayHash,op,_turnNumber,_currentSeat,_cardUseDebitPhaseInstanceId,parent,target,cutoff,completed,_players[f.OwnerSeat].MaxHp,x,required,
            jiawei ? YangFengStage.DiscardChoice : YangFengStage.QujiaChoice,candidates,[],[],[]);
        ReplaceRuntimeTop(f = f with { YangFeng = r });
        AdvanceEventRulesAndQueueFact(new YangFengStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ActualTurn,r.TurnOwner,r.Phase,parent,target,cutoff,completed,r.MaximumHp,x,required));
        if(jiawei && required == 0) { PayJiaweiDiscard(f);ContinueYangFeng(f.Id); }
        else PublishYangFeng(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> YangFengChoices(ProgramSkillFrame f)
    {
        var r = f.YangFeng!;
        if(r.Stage == YangFengStage.QujiaChoice) return Array.AsReadOnly(new[] { "extra","quota" }.Select(branch =>
            new PromptChoice(new($"yang-feng.{f.Id}.{branch}"),branch=="extra"?"执行一个额外回合":"本局使用【杀】的次数+1并获得一张【杀】",[],[],
                new Dictionary<string,string>{{"program-action","yang-feng"},{"branch",branch}})).ToArray());
        if(r.Stage != YangFengStage.DiscardChoice) return [];
        return Array.AsReadOnly(r.Candidates.Where(m=>!r.Selected.Any(s=>s.CardId==m.CardId)).Select(m =>
        {
            var hidden = m.From.Zone == CardZoneKind.Hand && m.From.OwnerSeat != f.OwnerSeat;
            return new PromptChoice(new($"yang-feng.{f.Id}.discard.{m.From.Zone}.{m.Slot}"),hidden?$"弃置目标第{m.Slot+1}张手牌":$"弃置【{GetAdvancedCard(m.CardId).DisplayName}】",
                hidden?[]:[m.CardId],[],new Dictionary<string,string>{{"program-action","yang-feng"},{"branch","discard"},{"source-zone",m.From.Zone.ToString()},{"slot-index",m.Slot.ToString()}});
        }).ToArray());
    }
    private void PublishYangFeng(ProgramSkillFrame f)
    {
        AssertYangFeng(f);var r = f.YangFeng!;var choices = YangFengChoices(f);
        if(choices.Count == 0) throw new InvalidOperationException("Yang Feng cannot publish an empty unpaid selection.");
        var skill = _contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger,f.OwnerSeat,r.Stage == YangFengStage.QujiaChoice ? "驱驾：选择本局一次的收益。" : $"假威：选择弃置目标的{r.RequiredDiscard}张牌（{r.Selected.Count}/{r.RequiredDiscard}）。",
            choices.SelectMany(c=>c.Cards).ToArray(),[],f.OwnerSeat)
        { PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=r.TargetSeat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("yang-feng")]
    private void AnswerYangFeng(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame { YangFeng:{} r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !YangFengChoices(f).Any(c=>AssistedChoicesEqual([c],[choice]))) throw new InvalidOperationException("Yang Feng requires his exact published private selection.");
        AssertYangFeng(f);ClearPendingDecision();ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if(r.Stage == YangFengStage.QujiaChoice)
        {
            AdvanceEventRulesAndQueueFact(new QujiaChoiceIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.ParentId,branch));
            ReplaceRuntimeTop(f = f with { YangFeng = r with { QujiaChoice = branch } });
            if(branch == "extra") { PendProgramExtraTurn(f,null);FinishYangFeng(f);return; }
            AdvanceEventRulesAndQueueFact(new QujiaSlashBonusGrantedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn));
            ReplaceRuntimeTop(f = f with { YangFeng = f.YangFeng! with { Stage = YangFengStage.QujiaGainChildren },PendingMovementContinuation = new(f.OwnerSeat,0,null) });
            var before = _movementSequence;_=EnsureDrawPile();
            var card = _cardZones.CardsAt(CardLocation.DrawPile).LastOrDefault(c=>IsSlashCard(c.Kind));
            var material = card is null ? null : new YangFengMaterial(card.Id,card.Kind,CardLocation.DrawPile,0);
            if(material is not null && _players[f.OwnerSeat].IsAlive) MoveYangFengEntities(f,[material],CardLocation.Hand(f.OwnerSeat),QujiaGain,YangFengStage.QujiaGainChildren,before);
            else RecordYangFengMovement(f.Id,YangFengStage.QujiaGainChildren,QujiaGain,before,0);
            ContinueYangFeng(f.Id);return;
        }
        if(branch != "discard" || r.Stage != YangFengStage.DiscardChoice) throw new InvalidOperationException("Unknown Yang Feng selection.");
        var materialSelected = r.Candidates.Single(m=>m.From.Zone.ToString()==choice.Parameters["source-zone"] && m.Slot.ToString()==choice.Parameters["slot-index"]);
        ReplaceRuntimeTop(f = f with { YangFeng = r with { Selected = r.Selected.Append(materialSelected).ToArray() } });
        AdvanceEventRulesAndQueueFact(new JiaweiMaterialSelectedEvent(f.Id,r.Selected.Count,materialSelected));
        if(f.YangFeng!.Selected.Count < r.RequiredDiscard) PublishYangFeng(f);else { PayJiaweiDiscard(f);ContinueYangFeng(f.Id); }
    }
    private void PayJiaweiDiscard(ProgramSkillFrame f)
    {
        var r=f.YangFeng!;if(r.Selected.Count!=r.RequiredDiscard || r.Selected.Any(m=>_cardZones.GetLocation(m.CardId)!=m.From)) throw new InvalidOperationException("Jiawei cannot substitute a frozen original discard entity.");
        ReplaceRuntimeTop(f=f with{YangFeng=r with{Stage=YangFengStage.DiscardChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        MoveYangFengEntities(f,r.Selected,CardLocation.DiscardPile,JiaweiDiscard,YangFengStage.DiscardChildren,_movementSequence);
    }
    private void MoveYangFengEntities(ProgramSkillFrame f,IReadOnlyList<YangFengMaterial> materials,CardLocation to,string reason,YangFengStage stage,long before)
    {
        if(materials.Count>0)
        {
            if(stage==YangFengStage.DiscardChildren)MoveProgramLogicalDiscard(f,f.OwnerSeat,materials.Select(m=>m.CardId).ToArray(),new(reason));
            else MoveProgramCardsFromMultipleSources(materials.Select(m=>m.CardId).ToArray(),to,new(reason));
        }
        for(var i=0;i<materials.Count;i++)
        {
            var m=materials[i];var move=_cardMovements.Single(v=>v.Sequence>before&&v.CardId==m.CardId&&v.Reason.Value==reason);
            AdvanceEventRulesAndQueueFact(new YangFengMaterialMovedEvent(f.Id,stage,i,m,move.To,move.Sequence));
        }
        RecordYangFengMovement(f.Id,stage,reason,before,materials.Count);
    }
    private void RecordYangFengMovement(long id,YangFengStage stage,string reason,long before,int count)
    {
        var f=GetActiveProgramFrame(id);var r=f.YangFeng!;var move=new YangFengMovement(r.Movements.Count,stage,reason,before,_movementSequence,count);
        ReplaceRuntimeTop(f with{YangFeng=r with{Movements=r.Movements.Append(move).ToArray()}});
        AdvanceEventRulesAndQueueFact(new YangFengMovementIssuedEvent(id,move));
    }
    private void RevealJiawei(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f=f with{YangFeng=f.YangFeng! with{Stage=YangFengStage.RevealChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var before=_movementSequence;RevealProgramTopCards(f.Id,f.OwnerSeat,checked(f.YangFeng!.X+1),null,JiaweiRevealBind,SkillProgramCardSetVisibility.Public);
        f=GetActiveProgramFrame(f.Id);var entities=GetProgramCardSet(f,JiaweiRevealBind).CardIds.Select((id,index)=>
        {var card=GetAdvancedCard(id);return new JiaweiRevealedEntity(new(id,card.Kind,CardLocation.Processing,index),IsJiaweiDamageCard(card.Kind),_cardMovements.Last(m=>m.CardId==id).Sequence);}).ToArray();
        ReplaceRuntimeTop(f with{YangFeng=f.YangFeng! with{Revealed=entities}});
        for(var i=0;i<entities.Length;i++) AdvanceEventRulesAndQueueFact(new JiaweiEntityRevealedEvent(f.Id,i,entities[i]));
        RecordYangFengMovement(f.Id,YangFengStage.RevealChildren,$"skill-program.{f.SkillId}.{GetProgramBindingId(f)}.reveal",before,entities.Length);
    }
    private bool IsJiaweiOriginalProcessing(JiaweiRevealedEntity e) => _cardZones.GetLocation(e.Material.CardId)==CardLocation.Processing && _cardMovements.Last(m=>m.CardId==e.Material.CardId).Sequence==e.RevealSequence;
    private void SettleJiawei(ProgramSkillFrame f,YangFengStage stage)
    {
        var r=f.YangFeng!;var cleanup=stage==YangFengStage.CleanupChildren;var recipient=stage==YangFengStage.OwnerGainChildren?f.OwnerSeat:r.TargetSeat;
        var entities=r.Revealed.Where(e=>IsJiaweiOriginalProcessing(e)&&(cleanup||e.DamageCard==(stage==YangFengStage.OwnerGainChildren))).Select(e=>e.Material).ToArray();
        if(!cleanup&&!_players[recipient].IsAlive) entities=[];
        ReplaceRuntimeTop(f=f with{YangFeng=r with{Stage=stage},PendingMovementContinuation=new(f.OwnerSeat,0,null),CardSetBindings=Array.AsReadOnly(f.CardSetBindings.Where(b=>b.Name!=JiaweiRevealBind).ToArray())});
        MoveYangFengEntities(f,entities,cleanup?CardLocation.DiscardPile:CardLocation.Hand(recipient),cleanup?JiaweiCleanup:stage==YangFengStage.OwnerGainChildren?JiaweiOwnerGain:JiaweiTargetGain,stage,_movementSequence);
    }
    private bool DrainYangFengChildren(long id) => TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueYangFeng(long id)
    {
        while(_resolutionStack.LastOrDefault() is ProgramSkillFrame{YangFeng:{}r}f&&f.Id==id)
        {
            AssertYangFeng(f);
            if(r.Stage is YangFengStage.DiscardChoice or YangFengStage.QujiaChoice) { if(_pendingDecision is null) PublishYangFeng(f);return; }
            if(DrainYangFengChildren(id)) return;
            ReplaceRuntimeTop(f=GetActiveProgramFrame(id) with{PendingMovementContinuation=null});
            if(r.Stage==YangFengStage.DiscardChildren) RevealJiawei(f);
            else if(r.Stage==YangFengStage.RevealChildren) SettleJiawei(f,YangFengStage.OwnerGainChildren);
            else if(r.Stage==YangFengStage.OwnerGainChildren) SettleJiawei(f,YangFengStage.TargetGainChildren);
            else if(r.Stage==YangFengStage.TargetGainChildren) SettleJiawei(f,YangFengStage.CleanupChildren);
            else
            {
                if(r.Stage==YangFengStage.CleanupChildren&&_players[f.OwnerSeat].IsAlive)
                {GrantProgramTurnRuleModifier(f,SkillRuleQuery.CardUseDistanceLimit,SkillRuleOperation.Unlimited,0,[]);AdvanceEventRulesAndQueueFact(new JiaweiDistanceGrantedEvent(f.Id,f.OwnerSeat,r.ActualTurn));}
                FinishYangFeng(GetActiveProgramFrame(id));return;
            }
        }
    }
    private void FinishYangFeng(ProgramSkillFrame f)
    {AdvanceEventRulesAndQueueFact(new YangFengCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{YangFeng=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27150)]
    private bool ResumeYangFeng(long id) {if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{YangFeng:not null}f||f.Id!=id)return false;ContinueYangFeng(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27150)]
    private bool ReturnYangFengMovement(ProgramSkillFrame f) {if(f.YangFeng is null)return false;ContinueYangFeng(f.Id);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveJiaweiDiscardReveal,SkillProgramEffectOp.ResolveQujiaLimitedTurnEnd)]
    private PromptChoice ChooseYangFengAi(PendingDecision p,ProgramSkillFrame f) => f.YangFeng!.Stage==YangFengStage.QujiaChoice ? p.Choices.First(c=>c.Parameters["branch"]=="extra") :
        p.Choices.OrderBy(c=>c.Cards.Count==0?2:GetKeepValue(GetAdvancedCard(c.Cards[0]),_players[f.YangFeng.TargetSeat])*(f.YangFeng.TargetSeat==f.OwnerSeat?1:-1)).ThenBy(c=>c.Id.Value,StringComparer.Ordinal).First();
    private int QujiaPermanentSlashBonus(int ownerSeat)
    {
        if(!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQujiaLimitedTurnEnd)) return 0;
        var grants=ProgramEventHistory<QujiaSlashBonusGrantedEvent>().Where(e=>e.Source.OwnerSeat==ownerSeat).ToArray();
        foreach(var grant in grants)
            if(ProgramEventHistory<QujiaChoiceIssuedEvent>().Count(e=>e.FrameId==grant.FrameId&&e.Source==grant.Source&&e.GameplayHash==grant.GameplayHash&&e.ActualTurn==grant.ActualTurn&&e.Choice=="quota")!=1 ||
                ProgramEventHistory<YangFengStartedEvent>().Count(e=>e.FrameId==grant.FrameId&&e.Source==grant.Source&&e.GameplayHash==grant.GameplayHash&&e.Op==SkillProgramEffectOp.ResolveQujiaLimitedTurnEnd&&e.ActualTurn==grant.ActualTurn)!=1 ||
                grants.Count(e=>e.FrameId==grant.FrameId)!=1) throw new InvalidOperationException("Qujia's permanent Slash benefit lost its unique original issued choice.");
        return grants.Length;
    }
}
