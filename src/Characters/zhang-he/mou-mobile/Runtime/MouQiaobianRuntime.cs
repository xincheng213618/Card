using System.Globalization;
namespace CardGame.Core;

[ProgramRuntimeModule,ProgramCapability]
internal sealed partial class MouQiaobianRuntime(IProgramRuntimeFrames frames,IProgramRuntimeEvents events,
    IProgramRuntimePrompts prompts,IMouZhangHeFacts facts,IMouZhangHeNative native,IProgramOperationDraws draws):IMouZhangHeHost
{
    internal const string Qiaobian="mou-mobile:qiaobian",Liaoshi="mou-mobile:liaoshi",Action="mou-zhang-he";
    internal const string CostReason="skill-program.mou-mobile:qiaobian.cost",ObtainReason="skill-program.mou-mobile:qiaobian.obtain",
        FieldReason="skill-program.mou-mobile:qiaobian.field",DrawReason="skill-program.mou-mobile:qiaobian.draw";
    private static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId!,f.OwnerSeat,f.SkillInstanceId);
    public SkillProgramStepOutcome Start(ProgramSkillFrame f,SkillProgramEffectOp operation)
    {
        if(f.MouZhangHe is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
        if(!facts.ExactStart(f,operation))throw new InvalidOperationException("Qiaobian requires its exact native phase boundary and current binding.");
        var r=new MouZhangHeReceipt(Source(f),f.GameplayHash,operation,facts.Turn,f.WindowContext!.ParentFrameId);
        if(operation==SkillProgramEffectOp.PrepareMouZhangHeQiaobian)r=r with{Stage=MouZhangHeStage.CostCards};
        else if(operation==SkillProgramEffectOp.ResolveMouZhangHeSkippedPhase)
        {
            if(frames.ParentOf(f.Id)is not ProgramLifecycleTriggerWindowFrame{MouZhangHeSkip:{ } skip})
                throw new InvalidOperationException("Qiaobian's reward must follow its paid phase's real skip.");
            r=r with{Accepted=true,Entitlement=skip.Entitlement,Stage=skip.Entitlement.Phase switch
            {
                SkillProgramTurnPhase.Draw=>MouZhangHeStage.ObtainTargets,
                SkillProgramTurnPhase.Play=>MouZhangHeStage.FieldCard,
                SkillProgramTurnPhase.Discard=>MouZhangHeStage.Drawing,
                _=>MouZhangHeStage.Complete
            },TargetSeat=f.OwnerSeat};
        }
        else
        {
            var owed=EndingEntitlement(f.OwnerSeat);
            if(owed is null)throw new InvalidOperationException("Qiaobian's Ending must retain the actual skipped Discard entitlement.");
            r=r with{Accepted=true,Entitlement=owed,Stage=MouZhangHeStage.Complete};
        }
        frames.ReplaceTop(f=f with{MouZhangHe=r});
        events.AdvanceFact(new MouZhangHeStartedEvent(f.Id,r.Source,r.GameplayHash,operation,r.ActualTurn,r.WindowId));
        if(operation==SkillProgramEffectOp.ResolveMouZhangHeEnding)
        {
            events.AdvanceFact(new MouZhangHeEndingConsumedEvent(f.Id,r.Entitlement!.PlanFrameId,f.OwnerSeat,r.ActualTurn));
            if(facts.Player(f.OwnerSeat).Alive&&facts.Player(f.OwnerSeat).HandCount>=facts.Seats.Count(s=>facts.Player(s).Alive))
            {
                frames.ReplaceTop(f=f with{MouZhangHe=r with{Stage=MouZhangHeStage.ExtraPlay}});
                if(!native.BeginExtraPlay(f))throw new InvalidOperationException("Qiaobian cannot detach its actual Ending parent for the earned Play.");
                return SkillProgramStepOutcome.AwaitChild;
            }
        }
        Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f)=>f.MouZhangHe is{}r&&f.SkillId==Qiaobian&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&
        facts.Program(f.SkillId)?.GameplayHash==f.GameplayHash&&r.ActualTurn==facts.Turn&&r.WindowId==f.WindowContext?.ParentFrameId&&f.InstructionIndex==1&&
        facts.NativeParent(f)&&r.Cards.Count<=3&&r.Cards.Select(c=>c.From.Zone).Distinct().Count()==r.Cards.Count&&
        r.Cards.Select(c=>c.CardId).Distinct().Count()==r.Cards.Count&&r.Cards.All(c=>c.From.OwnerSeat==f.OwnerSeat&&c.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)&&
        r.Phases.Count<=r.Cards.Count&&r.Phases.Distinct().Count()==r.Phases.Count&&r.PaidCount>=0&&r.PaidCount<=r.Cards.Count&&
        r.Targets.Count<=2&&r.Targets.Distinct().Count()==r.Targets.Count&&r.Targets.All(s=>s!=f.OwnerSeat)&&
        facts.History<MouZhangHeStartedEvent>().Count(e=>e==new MouZhangHeStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.ActualTurn,r.WindowId))==1;
    private bool Drain(ProgramSkillFrame f)=>native.Drain(f);
    internal void Continue(long id)
    {
        while(prompts.Current is null&&frames.Top is ProgramSkillFrame{MouZhangHe:{}r}f&&f.Id==id)
        {
            if(!Valid(f))throw new InvalidOperationException("Qiaobian lost its original issuer, native parent, payment or actual-turn receipt.");
            if(r.Stage==MouZhangHeStage.ExtraPlay||r.Stage==MouZhangHeStage.Drawing&&f.NativeDraw is not null)return;
            if(r.Stage is MouZhangHeStage.CostChildren or MouZhangHeStage.ObtainChildren or MouZhangHeStage.FieldChildren or MouZhangHeStage.DrawChildren)
            {
                if(Drain(f))return;
                f=frames.ActiveProgram(id);r=f.MouZhangHe!;
                var next=r.Stage switch
                {
                    MouZhangHeStage.CostChildren=>MouZhangHeStage.PayCost,
                    MouZhangHeStage.ObtainChildren=>MouZhangHeStage.ObtainCard,
                    MouZhangHeStage.FieldChildren=>facts.Player(f.OwnerSeat).AllCardCount<=r.OwnerCountBefore&&
                        r.TargetSeat>=0&&facts.Player(r.TargetSeat).Alive&&facts.Player(r.TargetSeat).FieldCount>r.TargetFieldBefore&&
                        facts.History<MouZhangHeFieldMovedEvent>().Any(e=>e.FrameId==f.Id&&e.CardId==r.CardId&&e.ToSeat==r.TargetSeat)
                        ?MouZhangHeStage.Beneficiary:MouZhangHeStage.Complete,
                    _=>MouZhangHeStage.Complete
                };
                frames.ReplaceTop(f=f with{PendingMovementContinuation=null,MouZhangHe=r=r with{Stage=next}});
            }
            if(facts.Finished||!facts.Player(f.OwnerSeat).Alive){Complete(f);return;}
            if(r.Stage==MouZhangHeStage.PayCost)
            {
                if(r.Cursor<r.Cards.Count&&facts.Discardable(f.OwnerSeat).Contains(r.Cards[r.Cursor]))
                {
                    var card=r.Cards[r.Cursor];
                    frames.ReplaceTop(f=f with{MouZhangHe=r with{Stage=MouZhangHeStage.CostChildren,CardId=card.CardId,From=card.From,
                        Before=facts.MovementSequence,After=facts.MovementSequence,Cursor=r.Cursor+1},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
                    native.Discard(f,card);continue;
                }
                var paid=r.Phases.Take(r.PaidCount).ToArray();
                if(paid.Length>0)
                {
                    native.ApplySkips(f,paid);
                    foreach(var phase in paid)events.AdvanceFact(new MouZhangHeSkipIssuedEvent(new(f.Id,r.Source,r.GameplayHash,r.ActualTurn,phase)));
                }
                Complete(frames.ActiveProgram(id));return;
            }
            if(r.Stage==MouZhangHeStage.ObtainCard)
            {
                if(r.Cursor>=r.Targets.Count){Complete(f);return;}
                var target=r.Targets[r.Cursor];
                if(!facts.Player(target).Alive||facts.HandChoices(f,target).Count==0)
                {frames.ReplaceTop(f with{MouZhangHe=r with{Cursor=r.Cursor+1}});continue;}
            }
            if(r.Stage==MouZhangHeStage.Drawing)
            {
                var count=r.Entitlement?.Phase==SkillProgramTurnPhase.Play?2:1;
                draws.BeginOperationProgramDraw(f,r.TargetSeat,count,new(DrawReason));return;
            }
            if(r.Stage==MouZhangHeStage.Complete){Complete(f);return;}
            if(r.Stage==MouZhangHeStage.FieldCard&&facts.FieldCards.Count==0){Complete(f);return;}
            if(r.Stage==MouZhangHeStage.FieldTarget&&!facts.Seats.Any(s=>s!=r.From?.OwnerSeat&&facts.CanMoveField(r.CardId,s))){Complete(f);return;}
            Publish(f);return;
        }
    }
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r=f.MouZhangHe!;var result=new List<PromptChoice>();
        switch(r.Stage)
        {
            case MouZhangHeStage.CostCards:
                foreach(var card in facts.Discardable(f.OwnerSeat).Where(c=>!r.Cards.Any(x=>x.From.Zone==c.From.Zone)||r.Cards.Contains(c)))
                    result.Add(Choice(f,"cost",r.Cards.Contains(card)?"取消这张弃牌":"选择一张区域牌弃置",[card.CardId],[]));
                result.Add(Choice(f,"confirm",r.Cards.Count==0?"不弃牌、不跳阶段":"确认弃牌，选择等量阶段",[],[]));break;
            case MouZhangHeStage.Phases:
                foreach(var phase in new[]{SkillProgramTurnPhase.Judgment,SkillProgramTurnPhase.Draw,SkillProgramTurnPhase.Play,SkillProgramTurnPhase.Discard})
                    if(r.Phases.Count<r.Cards.Count||r.Phases.Contains(phase))result.Add(Choice(f,phase.ToString(),"选择跳过"+PhaseName(phase),[],[]));
                if(r.Phases.Count==r.Cards.Count)result.Add(Choice(f,"confirm","确认阶段并实际弃牌",[],[]));break;
            case MouZhangHeStage.ObtainTargets:
                foreach(var seat in facts.Seats.Where(s=>s!=f.OwnerSeat&&facts.Player(s).Alive&&facts.Player(s).HandCount>0&&!r.Targets.Contains(s)&&r.Targets.Count<2))
                    result.Add(Choice(f,"target","选择一名其他角色获得其一张手牌",[],[seat]));
                result.Add(Choice(f,"confirm",r.Targets.Count==0?"不获得手牌":"确认获得手牌",[],[]));break;
            case MouZhangHeStage.ObtainCard:
                foreach(var c in facts.HandChoices(f,r.Targets[r.Cursor]))result.Add(c with{Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]=Action,["branch"]="obtain"}});break;
            case MouZhangHeStage.FieldCard:
                foreach(var card in facts.FieldCards.Where(c=>facts.Seats.Any(s=>s!=c.From.OwnerSeat&&facts.CanMoveField(c.CardId,s))))
                    result.Add(Choice(f,"field","选择场上一张牌移动",[card.CardId],[]));
                result.Add(Choice(f,"decline","不移动场上的牌",[],[]));break;
            case MouZhangHeStage.FieldTarget:
                foreach(var seat in facts.Seats.Where(s=>s!=r.From?.OwnerSeat&&facts.CanMoveField(r.CardId,s)))
                    result.Add(Choice(f,"destination","将所选牌置入该角色对应区域",[],[seat]));break;
            case MouZhangHeStage.Beneficiary:
                result.Add(Choice(f,"draw","令场上牌数增加的角色摸两张牌",[],[r.TargetSeat]));
                result.Add(Choice(f,"decline","不令其摸牌",[],[]));break;
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private static string PhaseName(SkillProgramTurnPhase p)=>p switch{SkillProgramTurnPhase.Judgment=>"判定阶段",SkillProgramTurnPhase.Draw=>"摸牌阶段",SkillProgramTurnPhase.Play=>"出牌阶段",_=>"弃牌阶段"};
    private static PromptChoice Choice(ProgramSkillFrame f,string branch,string label,IReadOnlyList<int> cards,IReadOnlyList<int> targets)=>new(
        new($"mou-zhang-he.{f.Id}.{f.MouZhangHe!.Stage}.{branch}.{string.Join('.',cards)}.{string.Join('.',targets)}"),label,cards,targets,
        new Dictionary<string,string>{["program-action"]=Action,["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["branch"]=branch});
    private static bool Same(PromptChoice a,PromptChoice b)=>a.Id==b.Id&&a.Cards.SequenceEqual(b.Cards)&&a.Targets.SequenceEqual(b.Targets)&&
        a.Parameters.Count==b.Parameters.Count&&a.Parameters.All(k=>b.Parameters.GetValueOrDefault(k.Key)==k.Value);
    private void Publish(ProgramSkillFrame f)
    {
        var choices=Choices(f);
        if(choices.Count==0){Complete(f);return;}
        prompts.Set(new(DecisionKind.ProgramTrigger,f.OwnerSeat,"巧变：选择本次费用、跳过阶段或对应奖励。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),
            choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=prompts.NextId(),IsPrivate=true,Choices=choices,
            SkillPrompt=new(Qiaobian,"巧变","巧变","弃置各区域至多一张牌；在对应阶段实际跳过后结算奖励。")},facts.Player(f.OwnerSeat).Human);
        native.PublishState();
    }
    [ProgramAnswer(Action)]
    private void Answer(PromptChoice selected)
    {
        if(frames.Top is not ProgramSkillFrame{MouZhangHe:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=f.OwnerSeat||!Choices(f).Any(c=>Same(c,selected)))
            throw new InvalidOperationException("Only the original Qiaobian owner may choose its current canonical option.");
        prompts.Clear();var branch=selected.Parameters["branch"];
        if(r.Stage==MouZhangHeStage.CostCards)
        {
            if(branch=="confirm")
            {
                if(r.Cards.Count==0){Complete(f);return;}
                frames.ReplaceTop(f with{MouZhangHe=r with{Stage=MouZhangHeStage.Phases}});
            }
            else
            {
                var card=facts.Discardable(f.OwnerSeat).Single(c=>c.CardId==selected.Cards.Single());
                frames.ReplaceTop(f with{MouZhangHe=r with{Cards=r.Cards.Contains(card)?r.Cards.Where(c=>c!=card).ToArray():r.Cards.Append(card).ToArray()}});
            }
        }
        else if(r.Stage==MouZhangHeStage.Phases)
        {
            if(branch=="confirm")
            {
                if(r.Cards.Any(c=>!facts.Discardable(f.OwnerSeat).Contains(c))){Complete(f);return;}
                frames.ReplaceTop(f with{MouZhangHe=r with{Accepted=true,Stage=MouZhangHeStage.PayCost}});
            }
            else
            {
                var phase=Enum.Parse<SkillProgramTurnPhase>(branch);
                frames.ReplaceTop(f with{MouZhangHe=r with{Phases=r.Phases.Contains(phase)?r.Phases.Where(p=>p!=phase).ToArray():r.Phases.Append(phase).ToArray()}});
            }
        }
        else if(r.Stage==MouZhangHeStage.ObtainTargets)
            frames.ReplaceTop(f with{MouZhangHe=branch=="confirm"?r with{Stage=MouZhangHeStage.ObtainCard}:r with{Targets=r.Targets.Append(selected.Targets.Single()).ToArray()}});
        else if(r.Stage==MouZhangHeStage.ObtainCard)
        {
            var target=r.Targets[r.Cursor];var card=facts.ResolveHandChoice(selected,target);
            frames.ReplaceTop(f=f with{MouZhangHe=r with{Stage=MouZhangHeStage.ObtainChildren,CardId=card,From=new(CardZoneKind.Hand,target),
                TargetSeat=target,Cursor=r.Cursor+1,Before=facts.MovementSequence,After=facts.MovementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            native.Obtain(f,target,card);
        }
        else if(r.Stage==MouZhangHeStage.FieldCard)
        {
            if(branch=="decline"){Complete(f);return;}
            var card=facts.FieldCards.Single(c=>c.CardId==selected.Cards.Single());
            frames.ReplaceTop(f with{MouZhangHe=r with{Stage=MouZhangHeStage.FieldTarget,CardId=card.CardId,From=card.From}});
        }
        else if(r.Stage==MouZhangHeStage.FieldTarget)
        {
            var target=selected.Targets.Single();
            frames.ReplaceTop(f=f with{MouZhangHe=r with{Stage=MouZhangHeStage.FieldChildren,TargetSeat=target,
                OwnerCountBefore=facts.Player(f.OwnerSeat).AllCardCount,TargetFieldBefore=facts.Player(target).FieldCount,
                Before=facts.MovementSequence,After=facts.MovementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            native.MoveField(f,r.CardId,target);
        }
        else if(r.Stage==MouZhangHeStage.Beneficiary)
        {
            if(branch=="decline"){Complete(f);return;}
            frames.ReplaceTop(f with{MouZhangHe=r with{Stage=MouZhangHeStage.Drawing}});
        }
        Continue(f.Id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouZhangHeSkippedPhase)]
    private void DrawReturned(ProgramSkillFrame f,NativeDrawOutcome o)
    {
        if(f.MouZhangHe is not{Stage:MouZhangHeStage.Drawing}r||!Valid(f)||o.RecipientSeat!=r.TargetSeat||o.RequestedCount!=(r.Entitlement?.Phase==SkillProgramTurnPhase.Play?2:1))
            throw new InvalidOperationException("Qiaobian's actual phase draw must return to its exact recipient and requested amount.");
        frames.ReplaceTop(f=f with{MouZhangHe=r with{Stage=MouZhangHeStage.DrawChildren,Draw=o,Before=o.SequenceBefore,After=o.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        foreach(var card in o.ActualDrawIds)
        {
            var m=facts.Movements.Single(m=>m.CardId==card&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(o.RecipientSeat)&&m.Sequence>o.SequenceBefore&&m.Sequence<=o.SequenceAfter&&m.Reason==DrawReason);
            events.AdvanceFact(new MouZhangHeObtainedEvent(f.Id,o.RecipientSeat,m.Sequence));
        }
        events.AdvanceFact(new MouZhangHeDrawReturnedEvent(f.Id,o.RecipientSeat,o.RequestedCount,o.ActualDrawIds.Count,o.ReplacementGainedIds.Count,o.AttemptFrameId));
        Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f)
    {
        events.AdvanceFact(new MouZhangHeCompletedEvent(f.Id));
        frames.ReplaceTop(f=f with{MouZhangHe=null,PendingMovementContinuation=null});native.Finish(f);
    }
    [ProgramAiChoice(SkillProgramEffectOp.PrepareMouZhangHeQiaobian,SkillProgramEffectOp.ResolveMouZhangHeSkippedPhase,SkillProgramEffectOp.ResolveMouZhangHeEnding)]
    private PromptChoice Ai(PendingDecision d,ProgramSkillFrame f)
    {
        var r=f.MouZhangHe!;
        if(r.Stage==MouZhangHeStage.CostCards)
            return d.Choices.First(c=>c.Parameters["branch"]=="confirm");
        if(r.Stage==MouZhangHeStage.ObtainTargets)
            return d.Choices.FirstOrDefault(c=>c.Targets.Count==1&&!facts.Friendly(f.OwnerSeat,c.Targets[0]))??d.Choices.Last();
        if(r.Stage==MouZhangHeStage.FieldCard)return d.Choices.Last();
        if(r.Stage==MouZhangHeStage.Beneficiary)return d.Choices.First(c=>c.Parameters["branch"]==(facts.Friendly(f.OwnerSeat,r.TargetSeat)?"draw":"decline"));
        return d.Choices.First();
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,34200)]
    private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouZhangHe:not null}f||f.Id!=id)return false;Continue(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,34200)]
    private bool Movement(ProgramSkillFrame f)=>f.MouZhangHe is not null&&Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,34200)]
    private void Invariant(ProgramSkillFrame f){if(f.MouZhangHe is not null&&!Valid(f))throw new InvalidOperationException("Invalid Qiaobian owning receipt.");}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,34200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,34200)]
    private bool Paid(ProgramSkillFrame f)=>f.MouZhangHe is{Accepted:true}&&Valid(f)||f.SkillId==Qiaobian&&f.ActivationId==f.TriggerId&&f.SelectedCardIds.Count==0&&f.SelectedTargetSeats.Count==0&&f.InstructionIndex is 0 or 1&&f.WindowContext is{}context&&
        IssuedCandidate(new(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,context.OccurrenceIndex),context);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection,34200,SkillProgramEffectOp.PrepareMouZhangHeQiaobian,SkillProgramEffectOp.ResolveMouZhangHeEnding)]
    private bool Collect(ProgramTriggerLegalityContext c)=>c.Trigger.Id=="plan-skips"?facts.Discardable(c.OwnerSeat).Count>0:EndingEntitlement(c.OwnerSeat)is not null;
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish,34200)]
    private void ValidateSkipWindows()
    {
        foreach(var w in frames.OfType<ProgramLifecycleTriggerWindowFrame>().Where(w=>w.Window==SkillProgramTriggerWindow.ActualPhaseSkipped||w.MouZhangHeSkip is not null))
        {
            if(w.MouZhangHeSkip is not{}s||w.Window!=SkillProgramTriggerWindow.ActualPhaseSkipped||w.Continuation!=ProgramLifecycleContinuation.CompleteActualSkippedPhase||
                w.OwnerSeat!=s.NativeReturn.OwnerSeat||w.OwnerSeat!=s.Entitlement.Source.OwnerSeat||s.NativeReturn.ActualTurn!=facts.Turn||s.Entitlement.ActualTurn!=facts.Turn||
                s.NativeReturn.Phase!=s.Entitlement.Phase||w.Candidates.Count!=1||w.CandidateIndex is <0 or >1||
                !facts.History<MouZhangHeSkipIssuedEvent>().Any(e=>e.Entitlement==s.Entitlement)||
                facts.History<MouZhangHeSkipObservedEvent>().Count(e=>e.WindowId==w.Id&&e.Entitlement==s.Entitlement&&e.NativeReturn==s.NativeReturn)!=1)
                throw new InvalidOperationException("An actual skipped-phase window lost its immutable paid entitlement and exact native return.");
        }
    }
    [ProgramPipelineHook(ProgramPipelinePhase.ActualPhaseSkipped,34200)]
    private bool ActualSkipped(ProgramActualPhaseSkipContext c)
    {
        if(c.ActualTurn!=facts.Turn||c.OwnerSeat!=facts.TurnOwner||!facts.Player(c.OwnerSeat).Alive||facts.Finished)return false;
        var observed=facts.History<MouZhangHeSkipObservedEvent>().ToArray();
        var q=facts.History<MouZhangHeSkipIssuedEvent>().Select(e=>e.Entitlement).FirstOrDefault(q=>q.ActualTurn==c.ActualTurn&&q.Source.OwnerSeat==c.OwnerSeat&&q.Phase==c.Phase&&
            !observed.Any(e=>e.Entitlement==q)&&facts.Program(q.Source.SkillId)?.GameplayHash==q.GameplayHash);
        if(q is null)return false;native.OpenSkipWindow(q,c);return true;
    }
    [ProgramPipelineHook(ProgramPipelinePhase.LifecycleWindowCompleted,34200)]
    private bool SkipWindowCompleted(ProgramLifecycleTriggerWindowFrame w)
    {
        if(w.MouZhangHeSkip is not{}s)return false;
        if(w.Window!=SkillProgramTriggerWindow.ActualPhaseSkipped||w.Continuation!=ProgramLifecycleContinuation.CompleteActualSkippedPhase||
            w.CandidateIndex!=w.Candidates.Count||!facts.History<MouZhangHeSkipObservedEvent>().Any(e=>e.WindowId==w.Id&&e.Entitlement==s.Entitlement&&e.NativeReturn==s.NativeReturn))
            throw new InvalidOperationException("A completed skipped phase window lost its exact native return.");
        events.AdvanceFact(new MouZhangHeSkipReturnedEvent(w.Id,s.Entitlement.PlanFrameId,s.Entitlement.Phase));native.ResumeSkip(w);return true;
    }
    [ProgramWindowReturn(SkillProgramTriggerWindow.ActualPhaseSkipped)]
    private void ReturnSkippedBinding(ProgramSkillFrame f,bool completed)
    {
        if(frames.Top is not ProgramLifecycleTriggerWindowFrame{MouZhangHeSkip:not null}w||w.Id!=f.WindowContext?.ParentFrameId||w.CandidateIndex!=0||w.Candidates.Count!=1||
            w.Candidates[0].SkillInstanceId!=f.SkillInstanceId||w.Candidates[0].GameplayHash!=f.GameplayHash)
            throw new InvalidOperationException("Qiaobian must return to its one actual skipped-phase candidate.");
        native.AdvanceLifecycleCursor(w);frames.Advance(w.Id);
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate,34200)]
    private bool IssuedCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext x)
    {
        if(c.SkillId!=Qiaobian||facts.Program(Qiaobian)?.GameplayHash!=c.GameplayHash||!facts.Player(c.OwnerSeat).Alive||c.OwnerSeat!=facts.TurnOwner)return false;
        if(x.Window==SkillProgramTriggerWindow.ActualPhaseSkipped&&c.BindingId=="actual-skipped-phase"&&frames.Find(x.ParentFrameId)is ProgramLifecycleTriggerWindowFrame{MouZhangHeSkip:{}s}w)
            return w.CandidateIndex==0&&w.Candidates.Count==1&&w.Candidates[0]==c&&s.Entitlement.Source.OwnerSeat==c.OwnerSeat&&s.Entitlement.Source.SkillInstanceId==c.SkillInstanceId&&
                s.Entitlement.GameplayHash==c.GameplayHash&&s.NativeReturn.ActualTurn==facts.Turn&&x.SourceSeat==c.OwnerSeat&&x.TargetSeat==c.OwnerSeat&&
                facts.History<MouZhangHeSkipObservedEvent>().Count(e=>e.WindowId==w.Id&&e.Entitlement==s.Entitlement&&e.NativeReturn==s.NativeReturn)==1;
        return x.Window==SkillProgramTriggerWindow.TurnEnding&&c.BindingId=="ending-extra-play"&&EndingEntitlement(c.OwnerSeat)is{}q&&q.Source.SkillInstanceId==c.SkillInstanceId&&
            frames.Find(x.ParentFrameId)is TurnEndingBoundaryFrame ending&&ending.ItemIndex>=0&&ending.ItemIndex<ending.Items.Count&&ending.Items[ending.ItemIndex].Candidate==c;
    }
    internal MouZhangHeSkipEntitlement? EndingEntitlement(int owner)=>facts.History<MouZhangHeSkipObservedEvent>().Select(e=>e.Entitlement).FirstOrDefault(q=>q.Source.OwnerSeat==owner&&
        q.ActualTurn==facts.Turn&&q.Phase==SkillProgramTurnPhase.Discard&&!facts.History<MouZhangHeEndingConsumedEvent>().Any(e=>e.PlanFrameId==q.PlanFrameId&&e.OwnerSeat==owner&&e.ActualTurn==facts.Turn));
    [ProgramPipelineHook(ProgramPipelinePhase.AppendTurnEndingItems,34200)]
    private void AppendEnding(List<TurnEndingBoundaryItem> items)
    {
        var owner=facts.TurnOwner;var q=EndingEntitlement(owner);
        if(q is null||!facts.Player(owner).Alive)return;
        var c=new ProgramTriggerCandidate(owner,Qiaobian,"ending-extra-play",q.Source.SkillInstanceId!,q.GameplayHash,0);
        items.RemoveAll(i=>i.Candidate is{}old&&old.OwnerSeat==owner&&old.SkillId==Qiaobian&&old.BindingId==c.BindingId);
        items.Add(new(TurnEndingBoundaryItemKind.Program,c.Priority,$"mou-qiaobian-ending:{q.PlanFrameId}",c,null));
    }
    [ProgramPipelineHook(ProgramPipelinePhase.ResumeScheduledPhase,34200)]
    private bool ResumeExtra()
    {
        if(facts.Schedule is not{Frame.MouZhangHe:{Stage:MouZhangHeStage.ExtraPlay,ExtraReturn:not null}}s)return false;
        native.ResumeExtraPlay(s,false);return true;
    }
    [ProgramPipelineHook(ProgramPipelinePhase.CancelScheduledPhase,34200)]
    private bool CancelExtra()
    {
        if(facts.Schedule is not{Frame.MouZhangHe:{Stage:MouZhangHeStage.ExtraPlay,ExtraReturn:not null}}s)return false;
        native.ResumeExtraPlay(s,true);return true;
    }
}
