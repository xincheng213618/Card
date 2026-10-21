namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouSimaYiRuntime MouSimaYi => GetRuntimeModule<MouSimaYiRuntime>();
    [ProgramCapability] private sealed class MouSmFacts(GameEngine e):IMouSimaYiFacts
    {
        public int Turn=>e._turnNumber; public int TurnOwner=>e._turnProgression.OwnerSeat; public int PhaseId=>e._cardUseDebitPhaseInstanceId; public long Sequence=>e._movementSequence; public bool Finished=>e._winner!=Winner.None;
        public bool Alive(int s)=>e.IsValidPlayerSeat(s)&&e._players[s].IsAlive; public bool Human(int s)=>e._players[s].IsHuman; public string Name(int s)=>e._players[s].Name;
        public ContentSkillDefinition Skill(string s)=>e._contentRegistry.GetSkill(s); public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
        public int Keep(int s,int id)=>GetKeepValue(e.GetAdvancedCard(id),e._players[s]);
        public double DamageValue(int s,int t)=>e._aiBrains[s].ScoreProgramTarget(e.CreateSnapshot(s),t,new(0,0,1,0,0,0,false,false));
        public bool ActualPlay(int s)=>s==e._currentSeat&&e._phase==TurnPhase.Play&&e.CurrentActualDiscardRecoveryPhase()is{Kind:ActualDiscardRecoveryPhaseKind.Play}p&&p.ActorSeat==s&&e.IsRecordedActualDiscardRecoveryPhase(p);
        public bool Yin(int s,string instance)=>History<MouSmPolarityEvent>().LastOrDefault(x=>x.Source.OwnerSeat==s&&x.Source.SkillId==MouSimaYiComposition.Quanmou&&x.Source.SkillInstanceId==instance)?.Yin??false;
        public IReadOnlyList<int> InRange(int s)=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive&&p.Seat!=s&&e.IsWithinAttackRange(s,p.Seat)).Select(p=>p.Seat).Order().ToArray());
        public IReadOnlyList<int> Living()=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive).Select(p=>p.Seat).ToArray());
        public IReadOnlyList<MouSmEntity> Gifts(ProgramSkillFrame f,int s)=>!Alive(s)?[]:Array.AsReadOnly(new[]{CardZoneKind.Hand,CardZoneKind.Equipment}.SelectMany(z=>e._cardZones.CardsAt(new(z,s)).Where(c=>!e.IsForeignEquipmentDiscardPrevented(s,c,new(z,s),OwnedCardMoveIntent.Transfer)).Select(c=>new MouSmEntity(c.Id,c.Kind,new(z,s),c.DisplayName))).ToArray());
        public IReadOnlyList<MouSmPlayedPlan> Plays(ProgramSkillFrame f)=>e.MouSmPlayedPlans(f);
        public bool Declaration(MouSmPlayedPlan plan)=>plan.Source is{}source&&e.ViewAsRule(source)?.DeclarationValidation is not null;
        public bool Parent(ProgramSkillFrame f)
        {
            if(f.TriggerId is null)return f.SkillId==MouSimaYiComposition.Quanmou&&f.ActivationId=="in-range-give"&&f.WindowContext is null&&f.SelectedCardIds.Count==0&&f.SelectedTargetSeats.Count==0&&History<ProgramSkillStartedEvent>().Count(p=>p.FrameId==f.Id&&p.SkillId==f.SkillId&&p.OwnerSeat==f.OwnerSeat&&p.ActivationId==f.ActivationId)==1;
            var n=e._resolutionStack.FindIndex(x=>x.Id==f.Id);if(n<1||f.WindowContext is not{}w||w.OwnerSeat!=f.OwnerSeat||e._resolutionStack[n-1].Id!=w.ParentFrameId)return false;
            return e._resolutionStack[n-1]switch{
                ProgramCardTriggerWindowFrame p=>p.MouSimaYiPrivate is not null&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count&&p.Candidates[p.CandidateIndex]is{}c&&c.OwnerSeat==f.OwnerSeat&&c.SkillId==f.SkillId&&c.TriggerId==f.TriggerId&&c.SkillInstanceId==f.SkillInstanceId&&c.GameplayHash==f.GameplayHash&&e.ValidMouSmPrivateWindow(p),
                BeforeDamageProgramWindowFrame p=>p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count&&MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex].Candidate),
                DamageTriggerWindowFrame p=>e.DamageProgramMatchesWindow(f,p),
                ProgramLifecycleTriggerWindowFrame p=>p.Window==w.Window&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count&&MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex]),_=>false};
        }
        public MouSmPrivateUse? PrivateUse(int owner,ProgramSkillWindowContext? w)
        {
            if(w?.Window!=SkillProgramTriggerWindow.CardUseCommitted||w.OwnerSeat!=owner||e._resolutionStack.SingleOrDefault(x=>x.Id==w.ParentFrameId)is not ProgramCardTriggerWindowFrame{MouSimaYiPrivate:{}back}p||!e.ValidMouSmPrivateWindow(p)||p.Action.ActorSeat!=owner||p.CandidateIndex<0||p.CandidateIndex>=p.Candidates.Count||p.Candidates[p.CandidateIndex].SkillId!=MouSimaYiComposition.Pingliao)return null;
            var use=e.LifecycleCardUse(back.UseId)!;return new(use.Id,p.Action.ActionId,use.TargetSeats);
        }
        public MouSmDebtIssuedEvent? Debt(int owner,ProgramSkillWindowContext? w,bool yin)
        {
            if(w is null||w.SourceSeat!=owner||w.TargetSeat is not{}target||w.Amount<=0||w.OwnerSeat!=owner)return null;
            var parent=e._resolutionStack.SingleOrDefault(x=>x.Id==w.ParentFrameId);
            if(yin?(w.Window!=SkillProgramTriggerWindow.AfterDamageApplied||parent is not DamageTriggerWindowFrame d||d.SourceSeat!=owner||d.TargetSeat!=target):(w.Window!=SkillProgramTriggerWindow.BeforeDamageApplied||parent is not BeforeDamageProgramWindowFrame b||b.Prevented||b.SourceSeat!=owner||b.TargetSeat!=target))return null;
            return History<MouSmDebtIssuedEvent>().FirstOrDefault(d=>d.Source.OwnerSeat==owner&&d.Target==target&&d.Yin==yin&&d.Turn==Turn&&d.TurnOwner==TurnOwner&&d.PhaseId==PhaseId&&e._phase==TurnPhase.Play&&Skill(d.Source.SkillId).Program?.GameplayHash==d.GameplayHash&&History<MouSmStartedEvent>().Count(s=>s.FrameId==d.FrameId&&s.Source==d.Source&&s.GameplayHash==d.GameplayHash&&s.Operation==SkillProgramEffectOp.ResolveMouSimaYiQuanmou)==1&&!History<MouSmDebtConsumedEvent>().Any(c=>c.DebtId==d.FrameId));
        }
    }
    [ProgramCapability] private sealed class MouSmNative(GameEngine e):IMouSimaYiNative
    {
        public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true); public void Publish()=>e.AdvanceRulesAndPublishState();
        public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||e.TryBeginAdvancedSkillsChanged(f.Id)||e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Give(ProgramSkillFrame f)
        {
            if(!e.MouSimaYi.Valid(f)||f.MouSimaYi is not{Stage:MouSmStage.GiveChildren,Materials:[var c]}r||!new MouSmFacts(e).Gifts(f,r.Target).Contains(c))throw new InvalidOperationException("Quanmou requires the giver's current real entity.");
            e.MoveProgramCardsFromMultipleSources([c.Id],CardLocation.Hand(f.OwnerSeat),new(MouSimaYiRuntime.GiveReason),(_,moves)=>{
                if(moves is not[var m]||m.CardId!=c.Id||m.CardKind!=c.Kind||m.From!=c.From||m.To!=CardLocation.Hand(f.OwnerSeat)||m.Sequence<=r.Before)throw new InvalidOperationException("Quanmou lost the exact native Give movement.");
                var own=e.GetActiveProgramFrame(f.Id);e.ReplaceRuntimeFrame(f.Id,own with{MouSimaYi=own.MouSimaYi! with{After=m.Sequence}});e.AdvanceEventRulesAndQueueFact(new MouSmGivePaidEvent(f.Id,r.Target,c.Id,m.Sequence));e.RecordNativeGivenCards(f.Id,r.Target,f.OwnerSeat,[c.Id],r.Before);
            });
        }
        public void Prevent(ProgramSkillFrame f)=>e.PreventProgramCurrentDamage(f);
        public void Damage(ProgramSkillFrame f,int target)=>e.BeginProgramSkillDamage(f,target,1);
        public void Disclose(ProgramSkillFrame f)=>e.DiscloseMouSmTargets(f);
        public void ProhibitHand(ProgramSkillFrame f,int target)
        {
            if(!e.MouSimaYi.Valid(f)||f.MouSimaYi is not{Operation:SkillProgramEffectOp.ResolveMouSimaYiPingliao}r||!r.Targets.Contains(target)||r.PlayedSeats.Contains(target)||target==f.OwnerSeat||!e.IsValidPlayerSeat(target)||!e._players[target].IsAlive||e.LifecycleCardUse(r.UseId)?.MouSimaYiSecret is not{Released:true})throw new InvalidOperationException("Pingliao prohibits only the exact original living target which did not Play a red Basic.");
            e.ValidateProgramTurnEffectGrant(f);
            foreach(var red in new[]{false,true})
            {
                var grant=e._turnCardUseEffects.GrantHandColorRestriction(e._turnNumber,e._currentSeat,f.Id,-2*target-(red?2:1),CreateProgramTurnEffectSource(f),target,red);
                e.AdvanceEventRulesAndQueueFact(new HandCardColorRestrictionGrantedEvent(grant));
            }
        }
        public void Declare(ProgramSkillFrame f)=>e.IssueMouSmPlayedDeclaration(f);
        public void Play(ProgramSkillFrame f)=>e.IssueMouSmPlayed(f);
        public void CompletePlayed(ProgramSkillFrame f)=>e.BeginMouSmPlayedCompleted(f);
    }
    private IEnumerable<RuleQueryContribution> MouSmSlashContributions(CharacterState player,SkillRuleQuery query)=>query!=SkillRuleQuery.SlashLimit?[]:ProgramEventHistory<MouSmSlashAllowanceEvent>().Where(x=>x.Source.OwnerSeat==player.Seat&&x.Turn==_turnNumber&&x.TurnOwner==_turnProgression.OwnerSeat&&x.PhaseId==_cardUseDebitPhaseInstanceId&&_phase==TurnPhase.Play&&ProgramEventHistory<MouSmStartedEvent>().Count(s=>s.FrameId==x.FrameId&&s.Source==x.Source&&s.Operation==SkillProgramEffectOp.ResolveMouSimaYiPingliao)==1).Select(x=>(RuleQueryContribution)new FiniteRuleQueryContribution($"mou-sima-yi:{x.FrameId}",SkillRuleOperation.Add,1));
}
