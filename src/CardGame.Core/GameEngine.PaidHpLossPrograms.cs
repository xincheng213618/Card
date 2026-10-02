using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginHpLossQuantity(ProgramSkillFrame frame,string bind)
    {
        var active=GetActiveProgramFrame(frame.Id);var owner=_players[frame.OwnerSeat];
        if(active.HpLossQuantity is not null || active.PaidHpLossReceipt is not null || !owner.IsAlive || owner.Hp<=0 || _currentSeat!=owner.Seat)
            throw new InvalidOperationException("HP quantity requires its unpaid owner activation.");
        ReplaceRuntimeTop(active with {HpLossQuantity=new(active.InstructionIndex,bind,owner.Seat,owner.Hp,_turnNumber,_currentSeat)});
        var skill=_contentRegistry.GetSkill(frame.SkillId);
        var choices=Enumerable.Range(1,owner.Hp).Select(n=>new PromptChoice(new ChoiceId($"hp-loss.frame-{frame.Id}.{n}"),$"失去{n}点体力",[],[],
            new Dictionary<string,string>{{"program-action","hp-loss-quantity"},{"frame-id",frame.Id.ToString(CultureInfo.InvariantCulture)},{"result-bind",bind},{"quantity",n.ToString(CultureInfo.InvariantCulture)}})).ToArray();
        _pendingDecision=new PendingDecision(DecisionKind.ProgramTrigger,owner.Seat,$"【{skill.Name}】请选择失去的体力。",[],[],owner.Seat)
        {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=owner.Seat,SkillPrompt=new(frame.SkillId,skill.Name,$"{skill.Name} · 选择体力",skill.Description),Choices=Array.AsReadOnly(choices)};
        _status=owner.IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ResolveHpLossQuantity(PromptChoice choice)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("The quantity lost its frame.");
        var d=f.HpLossQuantity??throw new InvalidOperationException("The quantity lost its draft.");
        if(_pendingDecision is not {Kind:DecisionKind.ProgramTrigger} decision || decision.PlayerSeat!=f.OwnerSeat ||
            choice.Cards.Count!=0 || choice.Targets.Count!=0 || choice.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString(CultureInfo.InvariantCulture) ||
            choice.Parameters.GetValueOrDefault("result-bind")!=d.ResultBind ||
            !int.TryParse(choice.Parameters.GetValueOrDefault("quantity"),NumberStyles.None,CultureInfo.InvariantCulture,out var n) || n<1 || n>d.Maximum)
            throw new InvalidOperationException("The quantity does not match its owning prompt.");
        ClearPendingDecision();var owner=_players[f.OwnerSeat];
        if(!owner.IsAlive || owner.Hp!=d.Maximum || _turnNumber!=d.TurnNumber || _currentSeat!=d.TurnOwnerSeat || !HasRuntimeSkillInstance(owner,f.SkillId,f.SkillInstanceId))
        {CancelProgramBindingAndCleanup(f,"技能实例或体力在付款前失效，剩余步骤取消。");return;}
        var plan=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!);
        var before=owner.Hp;owner.Hp=Math.Max(0,before-n);
        // Freeze actual physical HP apply before any fact, rule callback or child.
        ReplaceRuntimeTop(f with {HpLossQuantity=null,PaidHpLossReceipt=new(d.InstructionIndex,d.ResultBind,f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,plan.Activation!.UsageGroup,_turnNumber,_currentSeat,n,before,owner.Hp,before-owner.Hp)});
        RecordHpChange(f.Id,null,f.OwnerSeat,before,owner.Hp,HpChangeKind.Loss);
        AdvanceEventRulesAndQueueFact(new ProgramSkillHpLostEvent(f.Id,f.SkillId,f.OwnerSeat,before-owner.Hp,owner.Hp));
        if(owner.Hp==0) BeginProgramSkillDying(f.Id,owner);else AdvanceRuntimeProgram(f.Id);
    }
    private PromptChoice SelectAiHpLossQuantity(PendingDecision d)
    {
        var wanted=Math.Min(2,Math.Max(1,_players[d.PlayerSeat].Hp-1));
        return d.Choices.Single(c=>c.Parameters.GetValueOrDefault("quantity")==wanted.ToString(CultureInfo.InvariantCulture));
    }
    private bool HasValidPaidHpLossReceipt(ProgramSkillFrame f,ProgramExecutionPlan plan)
    {
        var p=f.PaidHpLossReceipt;
        return p is not null && PaidHpLossProgram.IsExactChain(plan.Instructions) && plan.Activation is{UsesPerGame:1,MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0} a &&
            f.TriggerId is null && f.WindowContext is null && f.HpLossQuantity is null && p.InstructionIndex==1 && p.ResultBind==plan.Instructions[0].ResultBind &&
            p.OwnerSeat==f.OwnerSeat && p.SkillId==f.SkillId && p.BindingId==GetProgramBindingId(f) && p.SkillInstanceId==f.SkillInstanceId && p.GameplayHash==f.GameplayHash && p.UsageGroup==a.UsageGroup &&
            p.TurnNumber==_turnNumber && p.TurnOwnerSeat==_currentSeat && p.TurnOwnerSeat==f.OwnerSeat &&
            _skillRuntimeState.GetUsage(f.OwnerSeat,f.SkillId,a.UsageGroup,SkillUsageScope.Game)==1 &&
            p.Requested>0 && p.Requested<=p.HpBefore && p.HpAfter==p.HpBefore-p.Requested && p.HpAfter>=0 && p.ActualLost==p.HpBefore-p.HpAfter &&
            p.ActualLost>0 && f.InstructionIndex is >=1 and <=4 &&
            HasExactPaidHpLossGrant(f,p,p.DistanceGrantSequence,2,SkillRuleQuery.OutgoingDistance,-p.ActualLost) &&
            HasExactPaidHpLossGrant(f,p,p.SlashGrantSequence,3,SkillRuleQuery.SlashLimit,p.ActualLost) &&
            (!p.DrawIssued || f.InstructionIndex>=2) && (!p.DrawReturned || p.DrawIssued) &&
            (p.DistanceGrantSequence is null || p.DrawReturned && f.InstructionIndex>=3) &&
            (p.SlashGrantSequence is null || p.DistanceGrantSequence is not null && f.InstructionIndex==4);
    }
    private bool HasExactPaidHpLossGrant(ProgramSkillFrame f,ProgramPaidHpLossReceipt p,long? sequence,int index,SkillRuleQuery query,int amount)
    {
        if(sequence is null)return !_turnCardUseEffects.RuleModifiers.Any(m=>m.ParentFrameId==f.Id&&m.EffectIndex==index);
        var m=_turnCardUseEffects.RuleModifiers.SingleOrDefault(m=>m.GrantSequence==sequence);
        return m is not null&&m.ParentFrameId==f.Id&&m.EffectIndex==index&&m.TurnNumber==p.TurnNumber&&m.TurnSeat==p.TurnOwnerSeat&&m.Source==CreateProgramTurnEffectSource(f)&&m.Query==query&&m.Operation==SkillRuleOperation.Add&&m.Amount==amount&&m.AffectedSeat is null&&(m.CardKinds?.Count??0)==0&&m.PaidHpLossOrigin==CreatePaidHpLossModifierOrigin(f,p);
    }
    private static ProgramHpLossModifierOrigin CreatePaidHpLossModifierOrigin(ProgramSkillFrame f,ProgramPaidHpLossReceipt p)=>
        new(f.Id,p.InstructionIndex,p.ResultBind,p.OwnerSeat,p.SkillId,p.BindingId,p.SkillInstanceId,p.GameplayHash,p.UsageGroup,p.TurnNumber,p.TurnOwnerSeat,p.Requested,p.HpBefore,p.HpAfter,p.ActualLost);
    private void AssertPaidHpLossModifiers()
    {
        foreach(var m in _turnCardUseEffects.RuleModifiers.Where(m=>m.PaidHpLossOrigin is not null))
        {
            var p=m.PaidHpLossOrigin!;var program=_contentRegistry.GetSkill(p.SkillId).Program;
            var activation=program?.Activations.SingleOrDefault(a=>a.Id==p.BindingId);
            if(!PaidHpLossProgram.IsValidModifierOrigin(m)||program?.GameplayHash!=p.GameplayHash||activation is not{UsesPerGame:1,MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0}||!PaidHpLossProgram.IsExactChain(activation.Effects)||activation.Effects[0].ResultBind!=p.ResultBind||activation.UsageGroup!=p.UsageGroup||_skillRuntimeState.GetUsage(p.OwnerSeat,p.SkillId,p.UsageGroup,SkillUsageScope.Game)!=1||p.TurnNumber!=_turnNumber||p.TurnOwnerSeat!=_currentSeat)
                throw new InvalidOperationException("A paid HP loss modifier lost its frozen real producer and turn.");
        }
    }
    private bool CanContinueIssuedHpLoss(ProgramSkillFrame f)
    {
        if(!_players[f.OwnerSeat].IsAlive || f.PaidHpLossReceipt is null)return false;
        var plan=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!);
        return HasValidPaidHpLossReceipt(f,plan);
    }
    private SkillProgramStepOutcome DrawProgramPaidHpLoss(ProgramSkillFrame f,string bind)
    {
        var a=GetActiveProgramFrame(f.Id);var p=a.PaidHpLossReceipt??throw new InvalidOperationException("Actual HP loss receipt missing.");
        var plan=ProgramInstructionResolver.Default.Resolve(a,_contentRegistry.GetSkill(a.SkillId).Program!);
        if(!HasValidPaidHpLossReceipt(a,plan)||a.InstructionIndex!=2||p.ResultBind!=bind||p.DrawIssued)throw new InvalidOperationException("HP loss Draw is not its exact once-paid tail.");
        ReplaceRuntimeTop(a with {PaidHpLossReceipt=p with {DrawIssued=true}});
        DrawProgramCards(f.Id,f.OwnerSeat,p.ActualLost,null,null,SkillProgramCardSetVisibility.Private,CardMoveReasons.Draw);
        a=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(a with {PaidHpLossReceipt=a.PaidHpLossReceipt! with {DrawReturned=true}});
        return SkillProgramStepOutcome.Continue;
    }
    private void GrantProgramPaidHpLoss(ProgramSkillFrame f,string bind,bool distance)
    {
        var a=GetActiveProgramFrame(f.Id);var p=a.PaidHpLossReceipt??throw new InvalidOperationException("Actual HP loss receipt missing.");
        var plan=ProgramInstructionResolver.Default.Resolve(a,_contentRegistry.GetSkill(a.SkillId).Program!);
        if(!HasValidPaidHpLossReceipt(a,plan)||p.ResultBind!=bind||!p.DrawReturned||a.InstructionIndex!=(distance?3:4)||
            (distance?p.DistanceGrantSequence is not null:p.DistanceGrantSequence is null||p.SlashGrantSequence is not null))throw new InvalidOperationException("HP loss grant is not its exact issued tail.");
        ValidateProgramTurnEffectGrant(a);
        var grant=_turnCardUseEffects.GrantRuleModifier(_turnNumber,_currentSeat,a.Id,a.InstructionIndex-1,CreateProgramTurnEffectSource(a),
            distance?SkillRuleQuery.OutgoingDistance:SkillRuleQuery.SlashLimit,SkillRuleOperation.Add,distance?-p.ActualLost:p.ActualLost,hpLossOrigin:CreatePaidHpLossModifierOrigin(a,p));
        ReplaceRuntimeTop(a with {PaidHpLossReceipt=distance?p with {DistanceGrantSequence=grant.GrantSequence}:p with {SlashGrantSequence=grant.GrantSequence}});
        AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(grant));
    }
    private void AssertPaidHpLossState(ProgramSkillFrame f,ProgramExecutionPlan plan)
    {
        if(f.PaidHpLossReceipt is not null && !HasValidPaidHpLossReceipt(f,plan))throw new InvalidOperationException("Actual HP loss receipt lost its exact owner, source, turn or stage.");
        if(f.HpLossQuantity is { } d)
        {
            if(!PaidHpLossProgram.IsExactChain(plan.Instructions)||plan.Activation?.UsesPerGame!=1||f.InstructionIndex!=1||d.InstructionIndex!=1||f.PaidHpLossReceipt is not null||d.OwnerSeat!=f.OwnerSeat||d.Maximum<=0||d.ResultBind!=plan.Instructions[0].ResultBind||d.TurnNumber!=_turnNumber||d.TurnOwnerSeat!=_currentSeat||d.TurnOwnerSeat!=f.OwnerSeat||
                !ReferenceEquals(f,_resolutionStack.LastOrDefault())||_pendingDecision is not {Kind:DecisionKind.ProgramTrigger} p||p.PlayerSeat!=f.OwnerSeat||p.Choices.Count!=d.Maximum||p.Choices.Where((c,i)=>c.Cards.Count!=0||c.Targets.Count!=0||c.Parameters.GetValueOrDefault("program-action")!="hp-loss-quantity"||c.Parameters.GetValueOrDefault("quantity")!=(i+1).ToString(CultureInfo.InvariantCulture)||c.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString(CultureInfo.InvariantCulture)||c.Parameters.GetValueOrDefault("result-bind")!=d.ResultBind).Any())
                throw new InvalidOperationException("The HP quantity lost its exact finite owning prompt.");
        }
    }
    private sealed partial class ProgramSkillHost
    {
        public bool CanContinuePaidHpLoss(ProgramSkillFrame frame)=>engine.CanContinueIssuedHpLoss(frame);
        public SkillProgramStepOutcome ChooseOwnerHpLoss(ProgramSkillFrame frame,string resultBind)=>engine.BeginHpLossQuantity(frame,resultBind);
        public SkillProgramStepOutcome DrawPaidHpLoss(ProgramSkillFrame frame,string sourceBind)=>engine.DrawProgramPaidHpLoss(frame,sourceBind);
        public void GrantPaidHpLoss(ProgramSkillFrame frame,string sourceBind,bool distance)=>engine.GrantProgramPaidHpLoss(frame,sourceBind,distance);
    }
}
