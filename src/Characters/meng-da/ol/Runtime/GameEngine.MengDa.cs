namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string GoudeDrawReason="skill-program.meng-da.goude.draw",GoudeDiscardReason="skill-program.meng-da.goude.discard";
    [ProgramCapability]private sealed class MengDaCapability(GameEngine e):IMengDaHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f)=>e.BeginGoude(f);}
    private SkillProgramStepOutcome BeginGoude(ProgramSkillFrame input)
    {
        var f=GetActiveProgramFrame(input.Id);var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority);
        if(f.Goude is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.WindowContext is not{}context||
            !QualifyGoude(c,GetProgramTrigger(f),context))throw new InvalidOperationException("Goude requires a true native completed-turn opportunity with an unexecuted legal option.");
        var cutoff=CompleteProgramEventHistory().Count();var seats=GoudeFactionSeats(f.OwnerSeat);var faction=GetEffectiveFactionId(_players[f.OwnerSeat])!;
        var r=new GoudeReceipt(MengDaSource(f),f.GameplayHash,_turnNumber,_turnProgression.OwnerSeat,context.ParentFrameId,cutoff,faction,GoudeExecutedMask(_turnNumber,seats,cutoff),GoudeStage.Option){FactionSeats=seats};
        ReplaceRuntimeTop(f=f with{Goude=r});
        AdvanceEventRulesAndQueueFact(new GoudeStartedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,r.WindowId,cutoff,faction,r.ExecutedMask));
        for(var i=0;i<seats.Count;i++)AdvanceEventRulesAndQueueFact(new GoudeFactionSeatFrozenEvent(f.Id,i,seats[i]));
        PublishGoude(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> GoudeChoices(ProgramSkillFrame f)
    {
        var r=f.Goude!;
        Dictionary<string,string> Args(string branch)=>new(){["program-action"]="meng-da-goude",["branch"]=branch};
        PromptChoice Seat(int seat)=>new(new($"goude.{f.Id}.{r.Stage}.{seat}"),_players[seat].Name,[],[seat],Args("target"));
        if(r.Stage==GoudeStage.Option)return Array.AsReadOnly(GoudeAvailableOptions(f.OwnerSeat,r.ExecutedMask).Select(o=>new PromptChoice(new($"goude.{f.Id}.option.{o}"),
            o switch{GoudeOption.Draw=>"摸一张牌",GoudeOption.Discard=>"弃置一名角色一张手牌",GoudeOption.Slash=>"视为使用一张【杀】",_=>"变更势力"},[],[],
            new Dictionary<string,string>(Args("option")){["option"]=o.ToString()})).ToArray());
        if(r.Stage==GoudeStage.DiscardTarget)return Array.AsReadOnly(GoudeDiscardTargets(f.OwnerSeat).Select(Seat).ToArray());
        if(r.Stage==GoudeStage.SlashTarget)return Array.AsReadOnly(GoudeSlashTargets(f.OwnerSeat).Select(Seat).ToArray());
        if(r.Stage==GoudeStage.Faction)return Array.AsReadOnly(GoudeFactionChoices(f.OwnerSeat).Select(id=>new PromptChoice(new($"goude.{f.Id}.faction.{id}"),GetFactionName(id),[],[],
            new Dictionary<string,string>(Args("faction")){["faction"]=id})).ToArray());
        if(r.Stage==GoudeStage.DiscardCard)return Array.AsReadOnly(r.Materials.Select(m=>new PromptChoice(new($"goude.{f.Id}.hand.{m.Slot}"),
            m.HolderSeat==f.OwnerSeat?$"弃置【{GetAdvancedCard(m.CardId).DisplayName}】":$"弃置第{m.Slot+1}张手牌",m.HolderSeat==f.OwnerSeat?[m.CardId]:[],[],
            new Dictionary<string,string>(Args("card")){["slot-index"]=m.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture)})).ToArray());
        return [];
    }
    private void PublishGoude(ProgramSkillFrame f)
    {
        AssertMengDa(f);var choices=GoudeChoices(f);
        if(choices.Count==0||!_players[f.OwnerSeat].IsAlive){FinishGoude(f);return;}
        var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"苟得：执行本回合当前同势力角色尚未执行的一项。",choices.SelectMany(c=>c.Cards).ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=GoudePrivateChoice(f),TargetSeat=f.Goude!.TargetSeat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    private static bool GoudePrivateChoice(ProgramSkillFrame f)=>f.Goude!.Stage==GoudeStage.DiscardCard;
    [ProgramAnswer("meng-da-goude")]
    private void AnswerGoude(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Goude:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||
            !GoudeChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Goude requires its exact published option, target, faction or opaque Hand slot.");
        AssertMengDa(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(!_players[f.OwnerSeat].IsAlive){FinishGoude(f);return;}
        if(r.Stage==GoudeStage.Option)
        {
            var option=Enum.Parse<GoudeOption>(choice.Parameters["option"]);
            AdvanceEventRulesAndQueueFact(new GoudeOptionChosenEvent(f.Id,option));
            var stage=option switch{GoudeOption.Draw=>GoudeStage.MovementChildren,GoudeOption.Discard=>GoudeStage.DiscardTarget,GoudeOption.Slash=>GoudeStage.SlashTarget,_=>GoudeStage.Faction};
            ReplaceRuntimeTop(f=f with{Goude=r with{Option=option,Stage=stage}});
            if(option!=GoudeOption.Draw){PublishGoude(f);return;}
            ReplaceRuntimeTop(f=f with{PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            var before=_movementSequence;var actual=DrawCards(_players[f.OwnerSeat],1,true,new(GoudeDrawReason)).Count;
            RecordGoudePayment(f.Id,before,actual,null,null);ContinueMengDa(f.Id);return;
        }
        if(r.Stage==GoudeStage.Faction)
        {
            var faction=choice.Parameters["faction"];var previous=GetEffectiveFactionId(_players[f.OwnerSeat])!;
            ReplaceRuntimeTop(f=f with{Goude=r with{NewFaction=faction,Stage=GoudeStage.MovementChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            AdvanceEventRulesAndQueueFact(new GoudeFactionChosenEvent(f.Id,previous,faction));
            var before=_movementSequence;ChangeNativeEffectiveFaction(f,faction);RecordGoudePayment(f.Id,before,0,null,null);ContinueMengDa(f.Id);return;
        }
        if(r.Stage is GoudeStage.DiscardTarget or GoudeStage.SlashTarget)
        {
            var target=choice.Targets.Single();AdvanceEventRulesAndQueueFact(new GoudeTargetChosenEvent(f.Id,r.Option!.Value,target));
            if(r.Stage==GoudeStage.SlashTarget)
            {
                ReplaceRuntimeTop(f=f with{Goude=r with{TargetSeat=target,Stage=GoudeStage.NativeSlash}});
                if(UseTriggeredProgramVirtualSlash(f,target)!=SkillProgramStepOutcome.AwaitChild)FinishGoude(GetActiveProgramFrame(f.Id));
                return;
            }
            var cards=GoudeDiscardMaterials(f.OwnerSeat,target);
            ReplaceRuntimeTop(f=f with{Goude=r with{TargetSeat=target,Stage=GoudeStage.DiscardCard,Materials=cards}});
            for(var i=0;i<cards.Count;i++)AdvanceEventRulesAndQueueFact(new GoudeHandMaterialFrozenEvent(f.Id,i,cards[i]));
            PublishGoude(f);return;
        }
        if(r.Stage!=GoudeStage.DiscardCard||r.TargetSeat is not{}holder)throw new InvalidOperationException("Goude cannot pay an unissued branch.");
        var selected=r.Materials.Single(m=>m.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture)==choice.Parameters["slot-index"]);
        if(CurrentHandEntry(holder,selected.CardId)!=selected.HandEntry||!GoudeDiscardMaterials(f.OwnerSeat,holder).Contains(selected))throw new InvalidOperationException("Goude cannot substitute its original selected hand entity.");
        var paymentBefore=_movementSequence;
        ReplaceRuntimeTop(f=f with{Goude=r with{PaidMaterial=selected,Stage=GoudeStage.MovementChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var logical=CaptureNativeLogicalDiscardInvocation(f.OwnerSeat,f.OwnerSeat,[selected.CardId],new CardMoveReason(GoudeDiscardReason),f.Id);
        MoveProgramCardsFromMultipleSources([selected.CardId],CardLocation.DiscardPile,new(GoudeDiscardReason));
        CompleteNativeLogicalDiscardInvocation(logical);
        var movement=_cardMovements.Single(m=>m.Sequence>paymentBefore&&m.CardId==selected.CardId&&m.From==CardLocation.Hand(holder)&&m.To==CardLocation.DiscardPile&&m.Reason.Value==GoudeDiscardReason);
        RecordGoudePayment(f.Id,paymentBefore,1,selected,movement.Sequence);ContinueMengDa(f.Id);
    }
    private void RecordGoudePayment(long id,long before,int count,GoudeHandMaterial? material,long? sequence)
    {
        var f=GetActiveProgramFrame(id);var r=f.Goude!;
        ReplaceRuntimeTop(f with{Goude=r with{Before=before,After=_movementSequence,ActualCount=count}});
        AdvanceEventRulesAndQueueFact(new GoudeMovementPaidEvent(id,r.Option!.Value,before,_movementSequence,count,material,sequence));
    }
    private void ContinueMengDa(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Goude:{}r}f||f.Id!=id)return;AssertMengDa(f);
        if(r.Stage is not(GoudeStage.MovementChildren or GoudeStage.NativeChildren))
        {if(r.Stage!=GoudeStage.NativeSlash&&_pendingDecision is null)PublishGoude(f);return;}
        if(TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||
            TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return;
        FinishGoude(GetActiveProgramFrame(id));
    }
    private void FinishGoude(ProgramSkillFrame f)
    {AdvanceEventRulesAndQueueFact(new GoudeCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{Goude=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27240)]
    private bool ResumeMengDa(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Goude:not null}f||f.Id!=id)return false;ContinueMengDa(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27240)]
    private bool ReturnMengDaMovement(ProgramSkillFrame f){if(f.Goude is null)return false;ContinueMengDa(f.Id);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveGoudeActualTurnEnd)]
    private PromptChoice ChooseGoudeAi(PendingDecision p,ProgramSkillFrame f)
    {
        var r=f.Goude!;
        if(r.Stage==GoudeStage.Option)return p.Choices.OrderBy(c=>c.Parameters.GetValueOrDefault("option")==nameof(GoudeOption.Draw)?0:c.Parameters.GetValueOrDefault("option")==nameof(GoudeOption.Discard)?1:c.Parameters.GetValueOrDefault("option")==nameof(GoudeOption.Slash)?2:3).First();
        if(r.Stage==GoudeStage.Faction)return p.Choices.OrderByDescending(c=>_players.Count(v=>v.IsAlive&&GetEffectiveFactionId(v)==c.Parameters["faction"])).ThenBy(c=>c.Id.Value,StringComparer.Ordinal).First();
        if(r.Stage is GoudeStage.DiscardTarget or GoudeStage.SlashTarget)return p.Choices.OrderByDescending(c=>_aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat),c.Targets[0],new SkillProgramAiHint(1,0,0,0,0,0,false,false))).First();
        return p.Choices.OrderBy(c=>c.Cards.Count>0?GetKeepValue(GetAdvancedCard(c.Cards[0]),_players[f.OwnerSeat]):2).First();
    }
}

