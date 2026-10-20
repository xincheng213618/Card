using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string XingwuDepositReason="skill-program.da-qiao-xiao-qiao.xingwu.deposit",XingwuPileReason="skill-program.da-qiao-xiao-qiao.xingwu.pile-remove",XingwuHandReason="skill-program.da-qiao-xiao-qiao.xingwu.hand-discard",XingwuEquipmentReason="skill-program.da-qiao-xiao-qiao.xingwu.equipment-discard";
    private static CardConversionSource DaQiaoXiaoQiaoSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_da_qiao_xiao_qiao_ol_DaQiaoXiaoQiao.DaQiaoXiaoQiaoSource(f);
    [ProgramCapability]private sealed class DaQiaoXiaoQiaoCapability(GameEngine e):IDaQiaoXiaoQiaoHost{public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,bool maintain)=>e.BeginDaQiaoXiaoQiao(f,maintain);}
    private bool DaQiaoXiaoQiaoParent(ProgramSkillFrame f)=>f.WindowContext is{Window:SkillProgramTriggerWindow.DiscardPhaseStarting}c&&c.OwnerSeat==f.OwnerSeat&&_currentSeat==f.OwnerSeat&&_phase==TurnPhase.Discard&&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId)is{Window:SkillProgramTriggerWindow.DiscardPhaseStarting,Continuation:ProgramLifecycleContinuation.CompleteDiscardPhase}p&&p.OwnerSeat==f.OwnerSeat&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count&&MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex]);
    private bool DaQiaoXiaoQiaoLive(ProgramSkillFrame f)=>_players[f.OwnerSeat].IsAlive&&_winner==Winner.None&&HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId);
    private SkillProgramStepOutcome BeginDaQiaoXiaoQiao(ProgramSkillFrame input,bool maintain)
    {
        var f=GetActiveProgramFrame(input.Id);
        if(maintain){if(f.SkillId!="ol:luoyan"||f.WindowContext?.Window!=SkillProgramTriggerWindow.GameStarting)throw new InvalidOperationException("Luoyan requires its native setup binding.");SynchronizeDaQiaoXiaoQiaoLuoyan();return SkillProgramStepOutcome.Continue;}
        if(f.SkillId!="ol:xingwu"||f.TriggerId!="discard-start-dance"||f.ActivationId!=f.TriggerId||f.InstructionIndex!=1||f.DaQiaoXiaoQiao is not null||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!DaQiaoXiaoQiaoParent(f))throw new InvalidOperationException("Xingwu requires its exact unpaid actual Discard starting frame.");
        foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);
        var pile=EnsurePublicPileSource(f,int.MaxValue);ReplaceRuntimeTop(f=f with{DaQiaoXiaoQiao=new(DaQiaoXiaoQiaoSource(f),f.GameplayHash,f.WindowContext!.ParentFrameId,_turnNumber,pile,DaQiaoXiaoQiaoStage.Deposit)});
        AdvanceEventRulesAndQueueFact(new DaQiaoXiaoQiaoStartedEvent(f.Id,DaQiaoXiaoQiaoSource(f),f.GameplayHash,f.WindowContext!.ParentFrameId,_turnNumber));PublishDaQiaoXiaoQiao(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<DaQiaoXiaoQiaoEntity> DaQiaoXiaoQiaoOwned(ProgramSkillFrame f,bool handOnly,bool discard)=>Array.AsReadOnly(GetHand(_players[f.OwnerSeat]).Concat(handOnly?[]:GetEquipment(_players[f.OwnerSeat])).Where(c=>
        !IsActiveProgramSourceEquipmentCard(f.OwnerSeat,f.SkillId,f.SkillInstanceId,c)&&(!discard||!IsSelfHandCategoryDiscardForbidden(f.OwnerSeat,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Discard)&&!IsForeignEquipmentDiscardPrevented(f.OwnerSeat,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Discard))).Select(c=>new DaQiaoXiaoQiaoEntity(c.Id,_cardZones.GetLocation(c.Id))).ToArray());
    private IReadOnlyList<DaQiaoXiaoQiaoEntity> DaQiaoXiaoQiaoPileCards(ProgramSkillFrame f)=>Array.AsReadOnly(PublicPileSources(f.OwnerSeat).Where(p=>p.SkillId=="ol:xingwu").SelectMany(p=>PublicPileCards(p).Select(c=>new DaQiaoXiaoQiaoEntity(c.Id,p.Location))).ToArray());
    private IReadOnlyList<PromptChoice> DaQiaoXiaoQiaoChoices(ProgramSkillFrame f)
    {
        var r=f.DaQiaoXiaoQiao!;var choices=new List<PromptChoice>();
        void Add(string branch,string label,int[]? cards=null,int[]? targets=null)=>choices.Add(new(new($"xingwu.{f.Id}.{r.Stage}.{branch}.{string.Join('.',cards??[])}.{string.Join('.',targets??[])}"),label,cards??[],targets??[],new Dictionary<string,string>{{"program-action","da-qiao-xiao-qiao"},{"branch",branch},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}}));
        if(r.Stage==DaQiaoXiaoQiaoStage.Deposit){foreach(var c in DaQiaoXiaoQiaoOwned(f,false,false))Add("deposit","将【"+GetAttackCard(c.Id).DisplayName+"】置为星舞",[c.Id]);Add("skip-deposit","不置牌，继续检查第二项");}
        if(r.Stage==DaQiaoXiaoQiaoStage.Burst){if(DaQiaoXiaoQiaoPileCards(f).Count>=3)Add("pile","移去三张星舞");if(DaQiaoXiaoQiaoOwned(f,true,true).Count>=2)Add("turnover","翻面并弃置两张手牌");Add("finish","不发动伤害项");}
        if(r.Stage is DaQiaoXiaoQiaoStage.PileCards or DaQiaoXiaoQiaoStage.HandCards){var cards=r.Stage==DaQiaoXiaoQiaoStage.PileCards?DaQiaoXiaoQiaoPileCards(f):DaQiaoXiaoQiaoOwned(f,true,true);foreach(var c in cards.Where(c=>!r.Selected.Contains(c)))Add("card","选择【"+GetAttackCard(c.Id).DisplayName+"】",[c.Id]);}
        if(r.Stage==DaQiaoXiaoQiaoStage.Target)foreach(var target in _players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat))Add("target","弃置 "+target.Name+" 的全部可弃装备，然后按性别造成伤害",targets:[target.Seat]);
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishDaQiaoXiaoQiao(ProgramSkillFrame f)
    {
        if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None||!f.DaQiaoXiaoQiao!.Paid&&!DaQiaoXiaoQiaoLive(f)){FinishDaQiaoXiaoQiao(f);return;}
        var choices=DaQiaoXiaoQiaoChoices(f);if(choices.Count==0){FinishDaQiaoXiaoQiao(f);return;}var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"星舞："+f.DaQiaoXiaoQiao!.Stage,choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("da-qiao-xiao-qiao")]
    private void AnswerDaQiaoXiaoQiao(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{DaQiaoXiaoQiao:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual([choice],DaQiaoXiaoQiaoChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Xingwu lost its exact private chooser or payable entity.");AssertDaQiaoXiaoQiao(f);ClearPendingDecision();var branch=choice.Parameters["branch"];
        if(r.Stage==DaQiaoXiaoQiaoStage.Deposit){if(branch=="skip-deposit"){ReplaceRuntimeTop(f=f with{DaQiaoXiaoQiao=r with{Stage=DaQiaoXiaoQiaoStage.Burst}});PublishDaQiaoXiaoQiao(f);return;}var c=DaQiaoXiaoQiaoOwned(f,false,false).Single(c=>c.Id==choice.Cards.Single());MoveDaQiaoXiaoQiao(f,DaQiaoXiaoQiaoStage.DepositChildren,[c],r.Pile.Location,XingwuDepositReason,false);ContinueDaQiaoXiaoQiao(f.Id);return;}
        if(r.Stage==DaQiaoXiaoQiaoStage.Burst)
        {
            if(branch=="finish"){FinishDaQiaoXiaoQiao(f);return;}
            ReplaceRuntimeTop(f=f with{DaQiaoXiaoQiao=r with{Branch=branch,Selected=[],Stage=branch=="pile"?DaQiaoXiaoQiaoStage.PileCards:DaQiaoXiaoQiaoStage.TurnoverChildren,Paid=r.Paid||branch=="turnover"}});
            if(branch=="turnover"){SetProgramTargetFaceState(f.Id,f.OwnerSeat,f.OwnerSeat,!_players[f.OwnerSeat].IsFaceDown);AdvanceEventRulesAndQueueFact(new DaQiaoXiaoQiaoTurnedEvent(f.Id,_players[f.OwnerSeat].IsFaceDown));ContinueDaQiaoXiaoQiao(f.Id);}else PublishDaQiaoXiaoQiao(f);return;
        }
        if(r.Stage is DaQiaoXiaoQiaoStage.PileCards or DaQiaoXiaoQiaoStage.HandCards)
        {var legal=r.Stage==DaQiaoXiaoQiaoStage.PileCards?DaQiaoXiaoQiaoPileCards(f):DaQiaoXiaoQiaoOwned(f,true,true);var selected=r.Selected.Append(legal.Single(c=>c.Id==choice.Cards.Single())).ToArray();ReplaceRuntimeTop(f=f with{DaQiaoXiaoQiao=r with{Selected=selected,Stage=selected.Length==(r.Branch=="pile"?3:2)?DaQiaoXiaoQiaoStage.Target:r.Stage}});PublishDaQiaoXiaoQiao(f);return;}
        if(r.Stage!=DaQiaoXiaoQiaoStage.Target)throw new InvalidOperationException("Unexpected Xingwu choice stage.");
        var target=choice.Targets.Single();var current=r.Branch=="pile"?DaQiaoXiaoQiaoPileCards(f):DaQiaoXiaoQiaoOwned(f,true,true);
        if(r.Selected.Count!=(r.Branch=="pile"?3:2)||r.Selected.Any(c=>!current.Contains(c))){FinishDaQiaoXiaoQiao(f);return;}
        ReplaceRuntimeTop(f=f with{DaQiaoXiaoQiao=r with{TargetSeat=target}});MoveDaQiaoXiaoQiao(f,DaQiaoXiaoQiaoStage.CostChildren,r.Selected,CardLocation.DiscardPile,r.Branch=="pile"?XingwuPileReason:XingwuHandReason,r.Branch!="pile");ContinueDaQiaoXiaoQiao(f.Id);
    }
    private void MoveDaQiaoXiaoQiao(ProgramSkillFrame f,DaQiaoXiaoQiaoStage stage,IReadOnlyList<DaQiaoXiaoQiaoEntity> cards,CardLocation to,string reason,bool discard)
    {
        var r=f.DaQiaoXiaoQiao!;var grain=cards.Any(c=>c.From.Zone==CardZoneKind.Equipment&&GetAttackCard(c.Id).Kind==CardKind.WoodenOx)?cards.Where(c=>c.From.Zone==CardZoneKind.Equipment&&GetAttackCard(c.Id).Kind==CardKind.WoodenOx).SelectMany(c=>GetWoodenOxGrain(_players[c.From.OwnerSeat!.Value]).Select(x=>x.Id)).ToArray():[];
        ReplaceRuntimeTop(f=f with{DaQiaoXiaoQiao=r with{Stage=stage,Paid=true,MovementStep=r.MovementStep+1,Before=_movementSequence,After=_movementSequence,Entities=cards,Grain=grain}});
        void Paid(long batch,IReadOnlyList<CardMovementRecord> moves){var active=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(active=active with{DaQiaoXiaoQiao=active.DaQiaoXiaoQiao! with{After=_movementSequence}});foreach(var m in moves)AdvanceEventRulesAndQueueFact(new DaQiaoXiaoQiaoEntityPaidEvent(f.Id,active.DaQiaoXiaoQiao!.MovementStep,m.CardId,m.From,m.To,m.Sequence));}
        if(cards.Count>0){if(discard)MoveProgramLogicalDiscard(f,f.OwnerSeat,cards.Select(c=>c.Id).ToArray(),new(reason),Paid);else MoveProgramCardsFromMultipleSources(cards.Select(c=>c.Id).ToArray(),to,new(reason),Paid);}
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{DaQiaoXiaoQiao=f.DaQiaoXiaoQiao! with{After=_movementSequence}});r=f.DaQiaoXiaoQiao!;AdvanceEventRulesAndQueueFact(new DaQiaoXiaoQiaoMovementPaidEvent(f.Id,r.MovementStep,stage,r.Before,r.After,cards.Count));
    }
    private bool DrainDaQiaoXiaoQiao(long id)=>TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id);
    private void ContinueDaQiaoXiaoQiao(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{DaQiaoXiaoQiao:{}r}f||f.Id!=id)return;AssertDaQiaoXiaoQiao(f);
        if(r.Stage is DaQiaoXiaoQiaoStage.Deposit or DaQiaoXiaoQiaoStage.Burst or DaQiaoXiaoQiaoStage.PileCards or DaQiaoXiaoQiaoStage.HandCards or DaQiaoXiaoQiaoStage.Target){if(_pendingDecision is null)PublishDaQiaoXiaoQiao(f);return;}
        if(DrainDaQiaoXiaoQiao(id))return;f=GetActiveProgramFrame(id);r=f.DaQiaoXiaoQiao!;
        if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive&&r.Stage is not(DaQiaoXiaoQiaoStage.CostChildren or DaQiaoXiaoQiaoStage.EquipmentChildren or DaQiaoXiaoQiaoStage.DamageChildren)){FinishDaQiaoXiaoQiao(f);return;}
        if(r.Stage==DaQiaoXiaoQiaoStage.DepositChildren){ReplaceRuntimeTop(f=f with{DaQiaoXiaoQiao=r with{Stage=DaQiaoXiaoQiaoStage.Burst}});PublishDaQiaoXiaoQiao(f);return;}
        if(r.Stage==DaQiaoXiaoQiaoStage.TurnoverChildren){if(DaQiaoXiaoQiaoOwned(f,true,true).Count<2){FinishDaQiaoXiaoQiao(f);return;}ReplaceRuntimeTop(f=f with{DaQiaoXiaoQiao=r with{Stage=DaQiaoXiaoQiaoStage.HandCards}});PublishDaQiaoXiaoQiao(f);return;}
        if(r.Stage==DaQiaoXiaoQiaoStage.CostChildren)
        {if(r.TargetSeat is not{}target||!_players[target].IsAlive){FinishDaQiaoXiaoQiao(f);return;}var equipment=GetEquipment(_players[target]).Where(c=>!IsForeignEquipmentDiscardPrevented(f.OwnerSeat,c,CardLocation.Equipment(target),OwnedCardMoveIntent.Discard)&&!IsActiveProgramSourceEquipmentCard(f.OwnerSeat,f.SkillId,f.SkillInstanceId,c)).Select(c=>new DaQiaoXiaoQiaoEntity(c.Id,CardLocation.Equipment(target))).ToArray();MoveDaQiaoXiaoQiao(f,DaQiaoXiaoQiaoStage.EquipmentChildren,equipment,CardLocation.DiscardPile,XingwuEquipmentReason,true);ContinueDaQiaoXiaoQiao(id);return;}
        if(r.Stage==DaQiaoXiaoQiaoStage.EquipmentChildren)
        {if(r.TargetSeat is not{}target||!_players[target].IsAlive){FinishDaQiaoXiaoQiao(f);return;}var amount=_players[target].Gender==GeneralGender.Female?1:2;ReplaceRuntimeTop(f=f with{DaQiaoXiaoQiao=r with{Stage=DaQiaoXiaoQiaoStage.DamageChildren,Damage=amount}});AdvanceEventRulesAndQueueFact(new DaQiaoXiaoQiaoDamageIssuedEvent(id,target,amount));_=BeginProgramSkillDamage(f,target,amount);return;}
        FinishDaQiaoXiaoQiao(f);
    }
    private void FinishDaQiaoXiaoQiao(ProgramSkillFrame f){var r=f.DaQiaoXiaoQiao!;AdvanceEventRulesAndQueueFact(new DaQiaoXiaoQiaoCompletedEvent(f.Id,r.Branch,r.TargetSeat));ReplaceRuntimeTop(f=f with{DaQiaoXiaoQiao=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27340)]private bool ResumeDaQiaoXiaoQiao(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{DaQiaoXiaoQiao:not null}f||f.Id!=id)return false;ContinueDaQiaoXiaoQiao(id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27340)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27340)]private bool ContinuePaidDaQiaoXiaoQiao(ProgramSkillFrame f)=>f.DaQiaoXiaoQiao is{Paid:true}&&ValidDaQiaoXiaoQiao(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveXingwuDepositAndBurst)]private PromptChoice ChooseDaQiaoXiaoQiaoAi(PendingDecision p,ProgramSkillFrame f)
    {var r=f.DaQiaoXiaoQiao!;if(r.Stage==DaQiaoXiaoQiaoStage.Deposit)return p.Choices.Where(c=>c.Cards.Count==1).OrderBy(c=>GetKeepValue(GetAttackCard(c.Cards[0]),_players[f.OwnerSeat])).FirstOrDefault()??p.Choices[0];if(r.Stage==DaQiaoXiaoQiaoStage.Burst)return p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="pile")??p.Choices.First(c=>c.Parameters["branch"]=="finish");if(r.Stage==DaQiaoXiaoQiaoStage.Target)return p.Choices.OrderBy(c=>AreProgramDistributionAllies(_players[f.OwnerSeat],_players[c.Targets[0]])).ThenByDescending(c=>GetEquipment(_players[c.Targets[0]]).Count).ThenBy(c=>c.Targets[0]).First();return p.Choices.OrderBy(c=>GetKeepValue(GetAttackCard(c.Cards[0]),_players[f.OwnerSeat])).First();}
}
