using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string DynamicDiscardDamageBlocked = "dynamic-discard-damage-blocked";
    private const string DynamicDiscardDamageReason = "program.dynamic-target-hp.discard";
    private sealed partial class ProgramSkillHost : IDynamicDiscardDamageHost
    { public SkillProgramStepOutcome DiscardTargetHpCardsAndDamage(ProgramSkillFrame f) => engine.BeginDynamicDiscardDamage(f); }

    private bool DynamicDiscardDamageSourceValid(ProgramSkillFrame f) => _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId) &&
        EnabledActivationPrograms(_players[f.OwnerSeat]).Any(p=>p.Id==f.SkillId);
    private Card[] DynamicDiscardDamageCards(int owner,string skill,string instance) =>
        GetHand(_players[owner]).Concat(GetEquipment(owner)).Where(c =>
            !c.IsGeneralWeapon &&
            !IsForeignEquipmentDiscardPrevented(owner,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Discard) &&
            !IsActiveProgramSourceEquipmentCard(owner,skill,instance,c)).ToArray();
    private bool CanStartDynamicDiscardDamage(CharacterState owner,string skill) =>
        _skillRuntimeState.GetUsage(owner.Seat,skill,DynamicDiscardDamageBlocked,SkillUsageScope.Turn)==0 &&
        _players.Any(p=>CanSelectDynamicDiscardDamageTarget(owner,skill,p.Seat));
    private bool CanSelectDynamicDiscardDamageTarget(CharacterState owner,string skill,int target) =>
        owner.IsAlive && _players[target].IsAlive && target!=owner.Seat && _players[target].Hp>0 &&
        IsWithinAttackRange(owner.Seat,target) &&
        DynamicDiscardDamageCards(owner.Seat,skill,GetRuntimeSkillInstanceId(owner,skill)).Length>=_players[target].Hp;

    private SkillProgramStepOutcome BeginDynamicDiscardDamage(ProgramSkillFrame f)
    {
        var active=GetActiveProgramFrame(f.Id);
        if(active.DynamicDiscardDamage is not null || active.SelectedTargetSeats is not [var target] ||
            !DynamicDiscardDamageSourceValid(active) || !CanStartDynamicDiscardDamage(_players[active.OwnerSeat],active.SkillId) ||
            !CanSelectDynamicDiscardDamageTarget(_players[active.OwnerSeat],active.SkillId,target))
            throw new InvalidOperationException("The variable discard attack requires one current legal target and unpaid source.");
        var r=new DynamicDiscardDamageReceipt(active.InstructionIndex,target,Math.Max(0,_players[target].Hp),
            GetCombatDistance(active.OwnerSeat,target),GetAttackRange(active.OwnerSeat),_turnNumber,
            DynamicDiscardDamageStage.Choosing,[],[]);
        ReplaceRuntimeTop(active=active with {DynamicDiscardDamage=r});
        PublishDynamicDiscardDamage(active);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> DynamicDiscardDamageChoices(ProgramSkillFrame f)
    {
        var r=f.DynamicDiscardDamage!;
        var choices=DynamicDiscardDamageCards(f.OwnerSeat,f.SkillId,f.SkillInstanceId)
            .Where(c=>!r.CardIds.Contains(c.Id)).Select(c=>new PromptChoice(new($"target-hp.{f.Id}.{c.Id}"),
                $"弃置【{c.DisplayName}】（{r.CardIds.Count+1}/{r.FrozenTargetHp}）",[c.Id],[],
                new Dictionary<string,string>{["program-action"]="dynamic-discard-damage",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["branch"]="card"})).ToList();
        choices.Add(new(new($"target-hp.{f.Id}.cancel"),"取消本次发动",[],[],
            new Dictionary<string,string>{["program-action"]="dynamic-discard-damage",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["branch"]="cancel"}));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishDynamicDiscardDamage(ProgramSkillFrame f)
    {
        var skill=_contentRegistry.GetSkill(f.SkillId);var choices=DynamicDiscardDamageChoices(f);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,$"{skill.Name}：弃置{f.DynamicDiscardDamage!.FrozenTargetHp}张牌。",choices.SelectMany(c=>c.Cards).ToArray(),[],f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    private void ResolveDynamicDiscardDamageChoice(PromptChoice c)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Variable payment lost its parent.");
        var r=f.DynamicDiscardDamage??throw new InvalidOperationException("Variable payment lost its unpaid receipt.");
        if(r.Stage!=DynamicDiscardDamageStage.Choosing || !DynamicDiscardDamageSourceValid(f) ||
            !AssistedChoicesEqual([c],DynamicDiscardDamageChoices(f).Where(x=>x.Id==c.Id).ToArray()))
            throw new InvalidOperationException("Variable payment no longer matches its published source or choice.");
        if(c.Parameters["branch"]=="cancel") {ClearPendingDecision();ReplaceRuntimeTop(f with {DynamicDiscardDamage=null});FinishProgramSkill(GetActiveProgramFrame(f.Id),false);return;}
        if(c.Cards is not [var id])throw new InvalidOperationException("Select one exact owned discard material.");
        var from=_cardZones.GetLocation(id);var ids=r.CardIds.Append(id).ToArray();var locations=r.OriginalLocations.Append(from).ToArray();
        if(ids.Length<r.FrozenTargetHp){ReplaceRuntimeTop(f=f with {DynamicDiscardDamage=r with {CardIds=ids,OriginalLocations=locations}});ClearPendingDecision();PublishDynamicDiscardDamage(f);return;}
        if(ids.Length!=r.FrozenTargetHp || ids.Distinct().Count()!=ids.Length || !_players[r.TargetSeat].IsAlive ||
            _players[r.TargetSeat].Hp!=r.FrozenTargetHp || GetCombatDistance(f.OwnerSeat,r.TargetSeat)!=r.FrozenDistance ||
            GetAttackRange(f.OwnerSeat)!=r.FrozenAttackRange || !IsWithinAttackRange(f.OwnerSeat,r.TargetSeat) ||
            ids.Select((value,index)=>(value,index)).Any(x=>_cardZones.GetLocation(x.value)!=locations[x.index] ||
                !DynamicDiscardDamageCards(f.OwnerSeat,f.SkillId,f.SkillInstanceId).Any(card=>card.Id==x.value)))
            throw new InvalidOperationException("The unpaid target HP, range or complete HE cost changed.");
        var before=_cardMovements.LastOrDefault()?.Sequence??0;
        ClearPendingDecision();ReplaceRuntimeTop(f with {DynamicDiscardDamage=r with {Stage=DynamicDiscardDamageStage.Paid,
            CardIds=ids,OriginalLocations=locations,PaymentSequenceBefore=before}});
        MoveProgramCardsFromMultipleSources(ids,CardLocation.DiscardPile,new(DynamicDiscardDamageReason));
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeFrame(f.Id,f=f with {DynamicDiscardDamage=f.DynamicDiscardDamage! with {PaymentSequenceAfter=_cardMovements.LastOrDefault()?.Sequence??before}});
        AdvanceEventRulesAndQueueFact(new DynamicDiscardDamagePaidEvent(f.Id,f.OwnerSeat,r.TargetSeat,r.FrozenTargetHp,r.ActualTurn,ids));
        if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.Continue)AdvanceRuntimeProgram(f.Id);
    }
    private bool ReturnDynamicDiscardDamageMovement(ProgramSkillFrame f)
    {
        if(f.DynamicDiscardDamage is null)return false;
        AssertDynamicDiscardDamage(f);
        if(f.DynamicDiscardDamage.Stage!=DynamicDiscardDamageStage.Paid ||
            f.PendingMovementContinuation is not {SubjectSeat:var subject,BeforeCount:0,CoverageResultBind:null} || subject!=f.OwnerSeat ||
            !ValidDynamicDiscardDamagePayments(f))
            throw new InvalidOperationException("The paid target-HP cost lost its exact owning movement return.");
        if(TryDrainDynamicDiscardDamageCost(f))return true;
        ReplaceRuntimeTop(f with {PendingMovementContinuation=null});
        // A committed cost survives source suppression/death. The already
        // frozen living target receives the attack; a winner/dead target ends it.
        AdvanceRuntimeProgram(f.Id);return true;
    }
    private bool TryDrainDynamicDiscardDamageCost(ProgramSkillFrame f)
    {
        if(f.DynamicDiscardDamage is not {Stage:DynamicDiscardDamageStage.Paid} ||
            !IsDynamicDiscardDamageMovement(f,
                ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect,
                f.PendingMovementContinuation ?? throw new InvalidOperationException("The paid discard drain requires its movement return.")))
            throw new InvalidOperationException("The discard drain lost its exact paid instruction and ledger.");
        return TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(f.Id);
    }
    private bool IsDynamicDiscardDamageMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation movement) =>
        effect?.Op==SkillProgramEffectOp.DiscardTargetHpCardsAndDamage && f.DynamicDiscardDamage is {Stage:DynamicDiscardDamageStage.Paid} r &&
        f.InstructionIndex==r.InstructionIndex && f.SelectedTargetSeats is [var target] && target==r.TargetSeat &&
        movement.SubjectSeat==f.OwnerSeat && movement.BeforeCount==0 && movement.CoverageResultBind is null && ValidDynamicDiscardDamagePayments(f);
    private bool ResumeDynamicDiscardDamage(long id)
    {
        var f=GetActiveProgramFrame(id);if(f.DynamicDiscardDamage is not { } r)return false;
        AssertDynamicDiscardDamage(f);
        if(r.Stage==DynamicDiscardDamageStage.Choosing)return true;
        if(r.Stage==DynamicDiscardDamageStage.Paid)
        {
            if(f.PendingMovementContinuation is not null)
            {
                if(TryDrainDynamicDiscardDamageCost(f))return true;
                ReturnDynamicDiscardDamageMovement(f);return true;
            }
            if(!_players[r.TargetSeat].IsAlive || _winner!=Winner.None){FinishProgramSkill(f,true);return true;}
            ReplaceRuntimeTop(f=f with {DynamicDiscardDamage=r with {Stage=DynamicDiscardDamageStage.DamageIssued}});
            BeginProgramSkillDamage(f,r.TargetSeat,1);return true;
        }
        if(r.Stage==DynamicDiscardDamageStage.DamageIssued)
        {
            if(f.AttackAttempt is not null){CompleteDamageAttack(new ProgramAttackHandle(this,id));return true;}
            throw new InvalidOperationException("Paid variable damage lost its typed attack completion.");
        }
        if(r.Stage==DynamicDiscardDamageStage.DamageCompleted)
        {
            if(TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.Program))return true;
            if(_winner!=Winner.None){FinishProgramSkill(GetActiveProgramFrame(id),true);return true;}
            if(r.DyingSurvived!=true){FinishProgramSkill(f,true);return true;}
            if(r.DamageFrameId is not { } damageId || r.DyingFrameId is not { } dyingId)throw new InvalidOperationException("Survival cost lost exact damage/Dying.");
            if(_skillRuntimeState.GetUsage(f.OwnerSeat,f.SkillId,DynamicDiscardDamageBlocked,SkillUsageScope.Turn)==0)
            {
                if(!_skillRuntimeState.TryConsumeUsage(f.OwnerSeat,f.SkillId,DynamicDiscardDamageBlocked,SkillUsageScope.Turn,1))throw new InvalidOperationException("Survival prohibition already consumed.");
                AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(f.OwnerSeat,f.SkillId,DynamicDiscardDamageBlocked,SkillUsageScope.Turn,1));
            }
            ReplaceRuntimeTop(f=f with {DynamicDiscardDamage=r with {Stage=DynamicDiscardDamageStage.PenaltyIssued}});
            AdvanceEventRulesAndQueueFact(new DynamicDiscardDamagePenaltyEvent(f.Id,f.OwnerSeat,r.ActualDamageTargetSeat??r.TargetSeat,damageId,dyingId,r.ActualTurn));
            if(_players[f.OwnerSeat].IsAlive && _players[f.OwnerSeat].Hp>0 &&
                new ProgramSkillHost(this).LoseHp(f.Id,f.SkillId,f.OwnerSeat,1)==SkillProgramStepOutcome.AwaitChild)return true;
            AdvanceRuntimeProgram(id);return true;
        }
        if(r.Stage==DynamicDiscardDamageStage.PenaltyIssued)
        {
            if(TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.Program))return true;
            FinishProgramSkill(GetActiveProgramFrame(id),true);return true;
        }
        throw new InvalidOperationException("Invalid variable discard-damage stage.");
    }
    private void CaptureDynamicDiscardDamageFrame(long damageId,long ownerId,int target)
    {
        if(_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==ownerId) is not {DynamicDiscardDamage.Stage:DynamicDiscardDamageStage.DamageIssued} f)return;
        if(f.DynamicDiscardDamage!.DamageFrameId is not null)throw new InvalidOperationException("Variable attack unexpectedly issued another native damage frame.");
        ReplaceRuntimeFrame(f.Id,f with {DynamicDiscardDamage=f.DynamicDiscardDamage with {DamageFrameId=damageId,ActualDamageTargetSeat=target}});
    }
    private void CaptureDynamicDiscardDamageDying(long dyingId,long damageId,int victim)
    {
        var f=_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(x=>x.DynamicDiscardDamage is { } r && r.DamageFrameId==damageId);
        if(f?.DynamicDiscardDamage is not { } r)return;
        if(r.ActualDamageTargetSeat!=victim || r.DyingFrameId is not null)throw new InvalidOperationException("Variable attack changed its exact Dying recipient.");
        ReplaceRuntimeFrame(f.Id,f with {DynamicDiscardDamage=r with {DyingFrameId=dyingId}});
    }
    private void CaptureDynamicDiscardDamageSurvival(DyingCompletionReceipt completion,bool survived)
    {
        var f=_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(x=>x.DynamicDiscardDamage?.DyingFrameId==completion.FrameId);
        if(f?.DynamicDiscardDamage is not { } r)return;
        if(completion.Continuation!=DyingContinuationKind.Damage || completion.ParentFrameId!=r.DamageFrameId || completion.VictimSeat!=r.ActualDamageTargetSeat || r.DyingSurvived is not null)
            throw new InvalidOperationException("Variable attack Dying returned from a different native producer.");
        ReplaceRuntimeFrame(f.Id,f with {DynamicDiscardDamage=r with {DyingSurvived=survived}});
    }
    private ProgramSkillFrame CaptureDynamicDiscardDamageCompleted(ProgramSkillFrame f)
    {
        if(f.DynamicDiscardDamage is not {Stage:DynamicDiscardDamageStage.DamageIssued} r)return f;
        AdvanceEventRulesAndQueueFact(new DynamicDiscardDamageCompletedEvent(f.Id,f.OwnerSeat,r.TargetSeat,r.DamageFrameId,
            r.ActualDamageTargetSeat,r.DyingFrameId,r.DyingSurvived,r.ActualTurn));
        return f with {DynamicDiscardDamage=r with {Stage=DynamicDiscardDamageStage.DamageCompleted}};
    }
    private bool ValidDynamicDiscardDamagePayments(ProgramSkillFrame f)
    {
        var r=f.DynamicDiscardDamage!;
        if(r.CardIds.Count!=r.FrozenTargetHp || r.CardIds.Count!=r.OriginalLocations.Count || r.CardIds.Distinct().Count()!=r.CardIds.Count)return false;
        return r.CardIds.Select((id,i)=>(id,i)).All(x=>_cardMovements.Count(m=>m.CardId==x.id && m.Sequence>r.PaymentSequenceBefore &&
            m.Sequence<=r.PaymentSequenceAfter && m.From==r.OriginalLocations[x.i] && m.Reason.Value==DynamicDiscardDamageReason &&
            m.To==CardLocation.DiscardPile)==1);
    }
    private void AssertDynamicDiscardDamage(ProgramSkillFrame f)
    {
        if(f.DynamicDiscardDamage is not { } r)return;
        var effect=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        if(effect.Op!=SkillProgramEffectOp.DiscardTargetHpCardsAndDamage || f.TriggerId is not null || effect.Target!=SkillProgramEffectTarget.SelectedTarget ||
            f.SelectedTargetSeats is not [var target] || target!=r.TargetSeat || r.InstructionIndex!=f.InstructionIndex || r.ActualTurn!=_turnNumber ||
            r.FrozenTargetHp<1 || r.FrozenDistance<1 || r.FrozenAttackRange<r.FrozenDistance ||
            !Enum.IsDefined(r.Stage) || r.Stage==DynamicDiscardDamageStage.Finished ||
            r.CardIds.Count!=r.OriginalLocations.Count || r.CardIds.Count>r.FrozenTargetHp || r.CardIds.Distinct().Count()!=r.CardIds.Count ||
            r.OriginalLocations.Any(l=>l.OwnerSeat!=f.OwnerSeat || l.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            r.Stage!=DynamicDiscardDamageStage.Choosing && !ValidDynamicDiscardDamagePayments(f))throw new InvalidOperationException("Variable discard damage lost its frozen owning payment.");
        var facts=CompleteProgramEventHistory().ToArray();
        if(r.Stage==DynamicDiscardDamageStage.Choosing)
        {
            if(r.PaymentSequenceBefore!=0 || r.PaymentSequenceAfter!=0 || r.DamageFrameId is not null || r.ActualDamageTargetSeat is not null ||
                r.DyingFrameId is not null || r.DyingSurvived is not null || f.AttackAttempt is not null || f.AttackReturn is not null)
                throw new InvalidOperationException("An unpaid variable cost cannot contain damage or survival state.");
            if(ReferenceEquals(f,_resolutionStack.LastOrDefault()) && (_pendingDecision is not {IsPrivate:true} p || p.PlayerSeat!=f.OwnerSeat ||
                !AssistedChoicesEqual(p.Choices,DynamicDiscardDamageChoices(f))))throw new InvalidOperationException("Variable cost lost its exact private choices.");
            return;
        }
        if(facts.OfType<DynamicDiscardDamagePaidEvent>().Count(e=>e.FrameId==f.Id && e.OwnerSeat==f.OwnerSeat && e.TargetSeat==r.TargetSeat &&
            e.FrozenHp==r.FrozenTargetHp && e.ActualTurn==r.ActualTurn && e.CardIds.SequenceEqual(r.CardIds))!=1)
            throw new InvalidOperationException("Variable cost lost its one real paid fact.");
        if(r.Stage==DynamicDiscardDamageStage.Paid && (r.DamageFrameId is not null || r.DyingFrameId is not null || f.AttackAttempt is not null))
            throw new InvalidOperationException("Cost children precede issuance of the variable attack.");
        if(r.Stage==DynamicDiscardDamageStage.DamageIssued && (f.AttackAttempt is not { } attack || f.AttackReturn is null ||
            attack.SourceSeat!=f.OwnerSeat || r.ActualDamageTargetSeat is { } actualTarget && attack.TargetSeat!=actualTarget ||
            r.ActualDamageTargetSeat is null && attack.TargetSeat!=r.TargetSeat &&
            !_resolutionStack.OfType<BeforeDamageProgramWindowFrame>().Any(w=>w.ParentFrameId==f.Id && w.TargetSeat==r.TargetSeat && w.RedirectedTargetSeat==attack.TargetSeat)))
            throw new InvalidOperationException("Issued variable damage lost its original typed attack.");
        if(r.DamageFrameId is { } damage && (r.ActualDamageTargetSeat is not { } damageVictim ||
            facts.OfType<DamageRequestedEvent>().Count(e=>e.ResolutionId==damage && e.SourceSeat==f.OwnerSeat && e.TargetSeat==damageVictim &&
                e.Amount>0 && !e.SourceLess)!=1))throw new InvalidOperationException("Variable damage lost its exact native issuance.");
        if(r.DyingFrameId is { } capturedDying && (r.DamageFrameId is null || r.ActualDamageTargetSeat is not { } dyingVictim ||
            facts.OfType<PlayerDyingEvent>().Count(e=>e.ResolutionId==capturedDying && e.VictimSeat==dyingVictim && e.KillerSeat==f.OwnerSeat)!=1))
            throw new InvalidOperationException("Variable survival cost cannot borrow another child's Dying.");
        if(r.DyingSurvived is { } survived && (r.DyingFrameId is not { } survivalDying || r.ActualDamageTargetSeat is not { } survivalVictim ||
            facts.OfType<DyingResolvedEvent>().Count(e=>e.ResolutionId==survivalDying && e.VictimSeat==survivalVictim && e.Survived==survived)!=1))
            throw new InvalidOperationException("Variable survival state lost its exact Dying completion.");
        if(r.Stage is DynamicDiscardDamageStage.DamageCompleted or DynamicDiscardDamageStage.PenaltyIssued)
        {
            if(f.AttackAttempt is not null || f.AttackReturn is not null ||
                facts.OfType<DynamicDiscardDamageCompletedEvent>().Count(e=>e.FrameId==f.Id && e.OwnerSeat==f.OwnerSeat && e.OriginalTargetSeat==r.TargetSeat &&
                    e.DamageFrameId==r.DamageFrameId && e.ActualTargetSeat==r.ActualDamageTargetSeat && e.DyingFrameId==r.DyingFrameId &&
                    e.Survived==r.DyingSurvived && e.ActualTurn==r.ActualTurn)!=1 ||
                r.DamageFrameId is { } completedDamage && !facts.OfType<AfterDamageEvent>().Any(e=>e.ResolutionId==completedDamage &&
                    e.SourceSeat==f.OwnerSeat && e.TargetSeat==r.ActualDamageTargetSeat))
                throw new InvalidOperationException("Variable attack tail precedes its exact native damage completion.");
        }
        var penalties=facts.OfType<DynamicDiscardDamagePenaltyEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(r.Stage==DynamicDiscardDamageStage.PenaltyIssued ? penalties.Length!=1 || r.DyingSurvived!=true ||
            penalties[0].DamageFrameId!=r.DamageFrameId || penalties[0].DyingFrameId!=r.DyingFrameId || penalties[0].TargetSeat!=r.ActualDamageTargetSeat ||
            penalties[0].ActualTurn!=r.ActualTurn || penalties[0].OwnerSeat!=f.OwnerSeat : penalties.Length!=0)
            throw new InvalidOperationException("Variable attack survival penalty must issue exactly once after its own survivor.");
    }
}
