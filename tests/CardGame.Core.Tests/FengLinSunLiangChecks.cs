using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class FengLinSunLiangChecks
{
    public static void DiscardBudgetDrawAndDamage()
    {
        foreach(var mode in new[]{"draw","damage"})
        {
            var (g,r)=Start("budget");
            if(mode=="damage") { Driver(g,"wound",[1]);Driver(g,"wound",[2]); }
            End(g);Reach(g,p=>p.Kind==DecisionKind.DiscardCards);var discard=P(g)!.RequiredCardCount;
            Accept(g,new DiscardCardsCommand(0,P(g)!.ValidCardIds.Take(discard).ToArray(),P(g)!.PromptId,g.Revision));
            Reach(g,p=>Action(p,"discard-budget"));var draft=g.ResolutionStack.OfType<ProgramSkillFrame>().Last().DiscardBudgetDraft!;
            Require(draft.Budget==discard&&discard==2,"Budget is the complete actual own discard occurrence count.");Replay(g,r);Choose(g,c=>c.Parameters.GetValueOrDefault("mode")==mode);
            foreach(var seat in mode=="draw"?new[]{0,1}:new[]{1,2})Choose(g,c=>c.Targets.SequenceEqual([seat]));
            Reject(g,new AnswerPromptCommand(1,P(g)!.PromptId,P(g)!.Choices.First().Id,g.Revision));Replay(g,r);Choose(g,c=>c.Parameters["mode"]=="finish");
            for(var i=0;i<60&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.DiscardBudgetDraft is not null);i++)Tick(g);
            var commit=g.Events.Select(e=>e.Payload).OfType<ProgramDiscardBudgetCommittedEvent>().Single();Require(commit.Mode==mode&&commit.Participants.Count==2&&commit.Budget==2,"Freeze mode/set/budget before sequential execution.");
            if(mode=="draw")Require(g.CardMovements.Count(m=>m.Reason.Value=="skill-program.discard-budget.draw")==2,"Each participant receives one actual top entity, including owner.");
            else Require(g.Events.Select(e=>e.Payload).OfType<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&commit.Participants.Any(p=>p.Seat==e.TargetSeat))==2,"Both low-HP participants take real damage/dying in the committed sequence.");
            Replay(g,r);Conserve(g);
        }
        var(zero,zr)=Start("budget");
        while(zero.State.Players[0].HandCount>zero.State.Players[0].Hp)
        {
            var card=zero.CreateCardZoneDiagnostics().First(c=>c.Location==CardLocation.Hand(0)).CardId;
            Accept(zero,new UseProgramSkillCommand(0,"fixture:sl-driver","trim",[card],[],zero.Revision,P(zero)!.PromptId));Reach(zero,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        }
        End(zero);Reach(zero,p=>Action(p,"discard-budget"));Require(zero.ResolutionStack.OfType<ProgramSkillFrame>().Last().DiscardBudgetDraft!.Budget==0&&!P(zero)!.Choices.Any(c=>c.Parameters["mode"]=="damage"),"Play discards do not enter actual discard-phase budget; zero budget offers no damage participants.");
        Choose(zero,c=>c.Parameters["mode"]=="draw");Require(P(zero)!.Choices.All(c=>c.Targets.Count==0),"Zero budget permits only empty draw completion/reset.");Replay(zero,zr);Choose(zero,c=>c.Parameters["mode"]=="finish");
        Require(zero.Events.Select(e=>e.Payload).OfType<ProgramDiscardBudgetCommittedEvent>().Single().Participants.Count==0&&!zero.CardMovements.Any(m=>m.Reason.Value=="skill-program.discard-budget.draw"),"Empty zero-budget mode causes no fabricated card draw.");Replay(zero,zr);Conserve(zero);
    }
    public static void RangePreventionAndMandatoryPrivateDiscard()
    {
        var (g,r)=Start("range");var before=g.State.Players[2].Hp;
        Driver(g,"damage",[2]);Require(g.State.Players[2].Hp==before&&g.Events.Select(e=>e.Payload).OfType<ProgramDamagePreventedEvent>().Any(),"Source's actual own Play damage is prevented when target range excludes source.");
        Driver(g,"loss",[2]);Require(g.State.Players[2].Hp==before-1,"HP loss is never damage prevention.");
        End(g);Reach(g,p=>Action(p,"outside-range-discard"));var prompt=P(g)!;
        Require(prompt.Choices.All(c=>c.Targets.SequenceEqual([2]))&&prompt.Choices.Any(c=>c.Cards.Count==0&&c.Parameters["source-zone"]=="Hand"),"Reverse attack-range predicate supplies only outside-range target and anonymous hand slots.");
        Require(g.CreateSnapshot(1,false).PendingDecision is null,"Other observer cannot inspect private forced discard choice.");Replay(g,r);Choose(g,c=>c.Cards.Count==0);Reach(g,p=>p.Kind==DecisionKind.DiscardCards||Action(p,"discard-budget"));
        Require(g.CardMovements.Any(m=>m.From==CardLocation.Hand(2)&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.classic:chezheng.ChooseOtherOwnedCardDiscard"),"Actual other owner's selected slot is truly discarded.");Conserve(g);
        var(chain,cr)=Start("chain");while(chain.State.Players[3].HandCount>0)Driver(chain,"strip",[3]);Driver(chain,"chain",[2]);Driver(chain,"chain",[3]);var hp=chain.State.Players[2].Hp;
        var fire=chain.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.CardId is {} id&&chain.CreateCardZoneDiagnostics().Any(c=>c.CardId==id&&c.CardKind==CardKind.FireSlash)&&a.TargetSeats.SequenceEqual([3]));Accept(chain,new PlayCardCommand(0,fire.CardId!.Value,fire.TargetSeats,chain.Revision,P(chain)!.PromptId,CardKind.FireSlash));Reach(chain,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(chain.State.Players[2].Hp==hp&&chain.Events.Select(e=>e.Payload).OfType<ProgramDamagePreventedEvent>().Any(e=>e.TargetSeat==2),"Actual physical Fire Slash chain propagation uses reverse live attack-range prevention.; hp="+hp+" current="+chain.State.Players[2].Hp+" events="+JsonSerializer.Serialize(chain.Events.Select(e=>e.Payload).Where(e=>e is DamageAppliedEvent or ProgramDamagePreventedEvent))+" skills="+JsonSerializer.Serialize(chain.State.Players[0].Skills));var prevented=chain.Events.Select(e=>e.Payload).OfType<ProgramDamagePreventedEvent>().Where(e=>e.TargetSeat==2).ToArray();Require(prevented.Length==1&&prevented[0].SourceSeat==0&&prevented[0].OwnerSeat==0&&chain.Events.Select(e=>e.Payload).OfType<ProgramBindingResolvedEvent>().Count(e=>e.FrameId==prevented[0].FrameId&&e.Window==SkillProgramTriggerWindow.BeforeDamageApplied)==1,"Chain target prevention resolves exactly once under its own real source/target window.");Replay(chain,cr);Conserve(chain);
    }
    public static void CompletedCostProviderAndLordReward()
    {
        foreach(var rejectGift in new[]{true,false})
        {
            var (g,r)=Start("gift",Role.Loyalist);var slash=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash);
            Accept(g,new PlayCardCommand(0,slash.CardId!.Value,slash.TargetSeats,g.Revision,P(g)!.PromptId));Reach(g,p=>Action(p,"faction-cost-gift"));
            var frame=g.ResolutionStack.OfType<ProgramSkillFrame>().Last();var d=frame.CompletedFactionGiftDraft!;
            Require(P(g)!.PlayerSeat==0&&d.CardIds.Contains(slash.CardId.Value)&&frame.OwnerSeat!=0&&g.State.Players[frame.OwnerSeat].Role==Role.Lord,"Provider owns gift choice and exact real completed Slash cost; distinct Lord owns source.");
            Require(Enumerable.Range(1,3).All(seat=>g.CreateSnapshot(seat,false).PendingDecision is null),"Private gift decision belongs only to actual user/provider, including a distinct human or AI Lord.");Replay(g,r);Reject(g,new AnswerPromptCommand(frame.OwnerSeat,P(g)!.PromptId,P(g)!.Choices[0].Id,g.Revision));Choose(g,c=>c.Parameters["mode"]==(rejectGift?"decline":"give"));
            Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
            var gifts=g.Events.Select(e=>e.Payload).OfType<CompletedFactionCostGiftedEvent>().ToArray();
            Require(rejectGift?gifts.Length==0:gifts.Length==1&&gifts[0].CardIds.Contains(slash.CardId.Value),"Decline doesn't consume; accepted gift records provider's one phase allowance.");
            if(!rejectGift)Require(g.CardMovements.Any(m=>m.CardId==slash.CardId&&m.To==CardLocation.Hand(frame.OwnerSeat))&&g.CardMovements.Count(m=>m.Reason.Value=="skill-program.faction-cost-gift.reward")==1&&g.Events.Select(e=>e.Payload).OfType<TurnRuleModifierGrantedEvent>().Any(),"Real transfer precedes actual reward draw and recipient Slash allowance.");
            Replay(g,r);Conserve(g);
        }
    }
    public static void NativeAiProviderHumanLord()
    {
        var (g,r)=Start("gift",Role.Lord);End(g);
        Reach(g,p=>Action(p,"faction-cost-gift")&&p.PlayerSeat==0);
        var f=g.ResolutionStack.OfType<ProgramSkillFrame>().Last();Require(f.CompletedFactionGiftDraft?.Stage=="reward"&&f.CompletedFactionGiftDraft.ProviderSeat!=0,"Native AI chooses its own actual Slash gift; human Lord independently decides reward.");
        Replay(g,r);Choose(g,c=>c.Parameters["mode"]=="decline");
        Require(g.Events.Select(e=>e.Payload).OfType<CompletedFactionCostGiftedEvent>().Any()&&!g.CardMovements.Any(m=>m.Reason.Value=="skill-program.faction-cost-gift.reward"),"Lord rejection preserves completed actual gift without reward.");Conserve(g);
    }
    public static void CommittedSourceDeathContinuation()
    {
        var(g,r)=Start("source-death",Role.Loyalist);Driver(g,"owner-low",[]);var lord=g.CreateSnapshot(0,true).Players.Single(p=>p.Role==Role.Lord).Seat;
        var desired=g.State.Players[0].Hp+g.State.Players[lord].Hp;
        while(g.State.Players[0].HandCount-g.State.Players[0].Hp>desired)
        {
            var card=g.CreateCardZoneDiagnostics().First(c=>c.Location==CardLocation.Hand(0)).CardId;
            Accept(g,new UseProgramSkillCommand(0,"fixture:sl-low","trim",[card],[],g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        }
        End(g);Reach(g,p=>p.Kind==DecisionKind.DiscardCards);var count=P(g)!.RequiredCardCount;Require(count==desired,"Real Play trims make the later discard budget exactly owner + Lord HP.");
        Accept(g,new DiscardCardsCommand(0,P(g)!.ValidCardIds.Take(count).ToArray(),P(g)!.PromptId,g.Revision));Reach(g,p=>Action(p,"discard-budget"));Choose(g,c=>c.Parameters["mode"]=="damage");Choose(g,c=>c.Targets.SequenceEqual([0]));
        var participants=g.ResolutionStack.OfType<ProgramSkillFrame>().Last().DiscardBudgetDraft!.Participants;
        var combination=Enumerable.Range(1,15).Select(mask=>participants.Where(p=>(mask&(1<<p.Seat))!=0).ToArray()).First(set=>set.Any(p=>p.Seat==0)&&set.Length>1&&set.Sum(p=>p.Hp)==count);
        var next=combination.First(p=>p.Seat!=0).Seat;
        foreach(var participant in combination.Where(p=>p.Seat!=0))Choose(g,c=>c.Targets.SequenceEqual([participant.Seat]));Replay(g,r);Choose(g,c=>c.Parameters["mode"]=="finish");
        for(var i=0;i<50&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.DiscardBudgetDraft is not null);i++)Tick(g);
        Require(!g.State.Players[0].IsAlive&&g.Events.Select(e=>e.Payload).OfType<DamageAppliedEvent>().Any(e=>e.SourceSeat==0&&e.TargetSeat==next),"Only the newly committed exact participant sequence survives its own source death, then damages the next participant.");Replay(g,r);Conserve(g);
    }
    public static void MultiCostPhaseGiftAndSourceLoss()
    {
        foreach(var mode in new[]{"multi","gift-loss","multi-extra"})
        {
            var(g,r)=Start(mode,Role.Loyalist);var action=g.GetHumanLegalActions().First(a=>a.ProgramSkillId=="fixture:sl-multi");
            var costs=action.SelectableCardIds.Take(2).ToArray();Accept(g,new UseProgramSkillCommand(0,"fixture:sl-multi","use",costs,[action.SelectableTargetSeats[0]],g.Revision,P(g)!.PromptId));Reach(g,p=>Action(p,"faction-cost-gift"));
            var source=g.ResolutionStack.OfType<ProgramSkillFrame>().Last().OwnerSeat;Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Last().CompletedFactionGiftDraft!.CardIds.SequenceEqual(costs),"Converted completed Slash binds every genuine physical cost.");Replay(g,r);Choose(g,c=>c.Parameters["mode"]=="give");
            if(mode=="gift-loss")
            {
                Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:sl-gift-loss");
                var giftParent=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.CompletedFactionGiftDraft is { Stage:"movement" });var moved=g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f=>f.Batch.ParentFrameId==giftParent.Id);
                Require(moved.Batch.ParentFrameId==giftParent.Id&&moved.Batch.OriginOwnerSeat==source&&moved.Batch.Movements.Select(m=>m.CardId).SequenceEqual(costs)&&giftParent.PendingMovementContinuation?.SubjectSeat==source,"Real gift batch and CardsGained child retain the exact Lord program parent and both physical costs; chooser remains child owner.");Replay(g,r);Tick(g);
            }
            Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
            Require(g.Events.Select(e=>e.Payload).OfType<CompletedFactionCostGiftedEvent>().Count(e=>e.ProviderSeat==0)==1&&costs.All(id=>g.CardMovements.Any(m=>m.CardId==id&&m.To==CardLocation.Hand(source))),"Both physical costs move once before reward, consuming one provider phase allowance.");
            if(mode=="gift-loss")Require(!g.CardMovements.Any(m=>m.Reason.Value=="skill-program.faction-cost-gift.reward"),"Exact Lord source suppressed by real gifted CardsGained HP loss cancels its reward.");
            else
            {
                var more=g.GetHumanLegalActions().First(a=>a.ProgramSkillId=="fixture:sl-multi");Accept(g,new UseProgramSkillCommand(0,"fixture:sl-multi","use",more.SelectableCardIds.Take(2).ToArray(),[more.SelectableTargetSeats[0]],g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
                Require(g.Events.Select(e=>e.Payload).OfType<CompletedFactionCostGiftedEvent>().Count(e=>e.ProviderSeat==0)==1,"Second real multi-cost Slash in same phase cannot refresh or duplicate gift across grants.");
                if(mode=="multi-extra")
                {
                    var first=g.Events.Select(e=>e.Payload).OfType<CompletedFactionCostGiftedEvent>().Single(e=>e.ProviderSeat==0);var turn=g.State.TurnNumber;
                    End(g);Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);Require(g.State.TurnNumber==turn,"Inserted Play ends into the same turn's normal Play.");
                    more=g.GetHumanLegalActions().First(a=>a.ProgramSkillId=="fixture:sl-multi");Accept(g,new UseProgramSkillCommand(0,"fixture:sl-multi","use",more.SelectableCardIds.Take(2).ToArray(),[more.SelectableTargetSeats[0]],g.Revision,P(g)!.PromptId));Reach(g,p=>Action(p,"faction-cost-gift"));Replay(g,r);Choose(g,c=>c.Parameters["mode"]=="give");Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
                    var phases=g.Events.Select(e=>e.Payload).OfType<CompletedFactionCostGiftedEvent>().Where(e=>e.ProviderSeat==0).ToArray();Require(phases.Length==2&&phases[1].TurnNumber==first.TurnNumber&&phases[1].PhaseInstanceId!=first.PhaseInstanceId,"Provider allowance refreshes only at a distinct actual Play phase, including two phases in one turn.");
                }
            }
            Replay(g,r);Conserve(g);
        }
    }
    public static void WholePhaseHeJRepeatedOccurrences()
    {
        var(g,r)=Start("occurrence");Driver(g,"supply",[]);
        foreach(var kind in new[]{LegalActionKind.Equip,LegalActionKind.Lightning})
        {
            var action=g.GetHumanLegalActions().First(a=>a.Kind==kind);Accept(g,new PlayCardCommand(0,action.CardId!.Value,action.TargetSeats,g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        }
        var eqj=g.CreateCardZoneDiagnostics().Where(c=>c.Location.OwnerSeat==0&&c.Location.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment).Select(c=>c.CardId).ToArray();Require(eqj.Length==2,"Fixed real fixture equipped one card and placed actual Lightning in J.");
        for(var seat=1;seat<4;seat++)while(g.State.Players[seat].HandCount>0)Driver(g,"strip",[seat]);
        End(g);Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:sl-phase");var cost=P(g)!.Choices.First(c=>c.Cards.Count==1).Cards.Single();Choose(g,c=>c.Cards.SequenceEqual([cost]));
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:sl-phase"&&p.Choices.Any(c=>c.Cards.SequenceEqual([cost])));Replay(g,r);Choose(g,c=>c.Cards.SequenceEqual([cost]));
        Reach(g,p=>p.Kind==DecisionKind.DiscardCards);var normal=P(g)!.RequiredCardCount;Accept(g,new DiscardCardsCommand(0,P(g)!.ValidCardIds.Take(normal).ToArray(),P(g)!.PromptId,g.Revision));Reach(g,p=>Action(p,"discard-budget"));
        var d=g.ResolutionStack.OfType<ProgramSkillFrame>().Last().DiscardBudgetDraft!;Require(d.Budget==normal+4&&g.CardMovements.Count(m=>m.CardId==cost&&m.Reason.Value=="skill-program.fixture:sl-phase.MoveBoundCards")==2&&eqj.All(id=>g.CardMovements.Any(m=>m.CardId==id&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.fixture:sl-phase.DiscardOwnedZoneCards")),"Whole phase counts owned H/E/J including both actual discard occurrences of one reclaimed entity, not distinct IDs or remaining discard-pile cards.");Replay(g,r);Choose(g,c=>c.Parameters["mode"]=="skip");Conserve(g);
    }
    public static void ResourceContracts()
    {
        foreach(var op in new[]{"resolveDiscardBudgetParticipants","preventOwnPlayOutsideTargetRangeDamage","discardOutsideRangeAfterInsufficientUses","offerCompletedFactionCostGift"})
        {
            var extra=op=="offerCompletedFactionCostGift"?",\"providerFactionId\":\"wu\"":"";
            var rules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:wrong","revision":1,"activations":[{"id":"x","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","effects":[{"op":"{{op}}","target":"owner"{{extra}}}]}]}]}""";
            var failed=false;try{SkillProgramCatalog.Load(rules,"""{"schemaVersion":3,"skills":{"fixture:wrong":{"name":"错误","description":"错误窗口"}}}""");}catch(InvalidOperationException){failed=true;}Require(failed,"New operation rejects fictitious activation window "+op);
        }
    }
    private static (GameEngine,ContentRegistry) Start(string mode,Role humanRole=Role.Lord)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=humanRole,ModeId="identity:classic-budget-gift-check",UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        g.EventCommitted += envelope =>
        {
            if(envelope.Payload is ProgramDiscardBudgetCommittedEvent budget) AssertFrozen(budget.Participants);
            if(envelope.Payload is CompletedFactionCostGiftedEvent gift) AssertFrozen(gift.CardIds);
        };
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral&&p.PlayerSeat==0);Accept(g,new SelectGeneralCommand(0,mode is "gift" or "multi" or "gift-loss" or "multi-extra" or "source-death"?P(g)!.ValidContentIds[0]:"fixture:sl-owner",g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);return(g,r);
    }
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static bool Action(PendingDecision p,string name)=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==name);
    private static void Driver(GameEngine g,string id,int[] targets){Accept(g,new UseProgramSkillCommand(0,id=="owner-low"?"fixture:sl-low":"fixture:sl-driver",id,[],targets,g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Choose(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;var c=p.Choices.FirstOrDefault(predicate)??throw new InvalidOperationException("No required choice "+JsonSerializer.Serialize(p));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c.Id,g.Revision));}
    private static void Tick(GameEngine g)
    {
        var p=P(g);if(p is not null&&p.PlayerSeat==0)
        {
            if(p.Kind==DecisionKind.PlayCard){End(g);return;}
            if(p.Kind==DecisionKind.DiscardCards){Accept(g,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));return;}
            if(p.Choices.Count>0){Choose(g,c=>c.Parameters.GetValueOrDefault("mode") is "skip" or "decline"||c.Parameters.GetValueOrDefault("option-id")=="continue"||c.Id==p.Choices[0].Id);return;}
            throw new InvalidOperationException("Unhandled fixture prompt "+p.Kind);
        }
        Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate){for(var i=0;i<140;i++){if(P(g) is {} p&&predicate(p))return;Tick(g);}throw new InvalidOperationException("Missing fixed boundary "+JsonSerializer.Serialize(P(g))+"; state="+SnapshotJson.Serialize(g.CreateSnapshot(0,true)));}
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(s=>SnapshotJson.Serialize(g.CreateSnapshot(s,false))==SnapshotJson.Serialize(restored.CreateSnapshot(s,false)))&&g.CardMovements.SequenceEqual(restored.CardMovements)&&g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).SequenceEqual(restored.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))),"All observer snapshots/events/physical moves replay accepted command JSON.");}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,"Fixture rejected "+result.Error?.Message+";state="+SnapshotJson.Serialize(g.CreateSnapshot(0,true)));}
    private static void Reject(GameEngine g,GameCommand c){var before=GameCheckpointJson.Serialize(g.CreateCheckpoint());Require(!g.Submit(c).Accepted&&before==GameCheckpointJson.Serialize(g.CreateCheckpoint()),"Wrong actor rejects atomically.");}
    private static void AssertFrozen<T>(IReadOnlyList<T> values)
    {
        Require(values is IList<T>,"New committed collection must expose a typed read-only IList.");
        var rejected=false;try{((IList<T>)values).Add(default!);}catch(NotSupportedException){rejected=true;}
        Require(rejected,"Observers cannot mutate a new committed fact's collection.");
    }
    private static void Conserve(GameEngine g)=>Require(g.ObserverFailures.Count==0&&g.CreateCardZoneDiagnostics().Count==64&&g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==64,"Real entities are conserved.");
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-sunliang",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var rules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:sl-driver","revision":1,"activations":[{"id":"trim","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},{"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},{"id":"loss","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},{"id":"strip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","amount":1,"zones":["hand"],"resultBind":"stripped"},{"op":"moveBoundCards","target":"owner","sourceBind":"stripped","destination":"discardPile","awaitMovementTriggers":true}]},{"id":"supply","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"draw","target":"owner","amount":8}]},{"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},{"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]}]}""";
            var c=SkillProgramCatalog.Load(rules,"""{"schemaVersion":3,"skills":{"fixture:sl-driver":{"name":"驱动","description":"真实阶段与伤害"}}}""");b.AddSkill(new("fixture:sl-driver","驱动","真实阶段"){Program=c.Programs["fixture:sl-driver"]});
            if(mode=="occurrence")
            {
                var phase=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:sl-phase","revision":1,"triggers":[{"id":"owned-discard","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"first"},{"op":"moveBoundCards","target":"owner","sourceBind":"first","destination":"discardPile","awaitMovementTriggers":true},{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"second"},{"op":"moveBoundCards","target":"owner","sourceBind":"second","destination":"discardPile","awaitMovementTriggers":true},{"op":"discardOwnedZoneCards","target":"owner","zones":["equipment","judgment"]}]}]},{"id":"fixture:sl-claim","revision":1,"triggers":[{"id":"claim","window":"discardPileReceived","subject":"owner","discardOwnerScope":"other","movementOccurrence":"perCard","movementReasons":["skill-program.fixture:sl-phase.MoveBoundCards"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"claimMovedCards","target":"owner"},{"op":"selectTarget","target":"owner","targetKind":"eventSource"},{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"returned"},{"op":"moveBoundCards","target":"owner","sourceBind":"returned","destination":"selectedTargetHand"}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:sl-phase":{"name":"阶段弃置","description":"真实HEJ与重复实体"},"fixture:sl-claim":{"name":"一次取回","description":"真实取回弃牌"}}}""");foreach(var p in phase.Programs)b.AddSkill(new(p.Key,p.Key,p.Key){Program=p.Value});
            }
            if(mode=="source-death")
            {
                var low=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:sl-low","revision":1,"activations":[{"id":"owner-low","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":2}]},{"id":"trim","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:sl-low":{"name":"低体力","description":"真实自失体力"}}}""");b.AddSkill(new("fixture:sl-low","低体力","失血"){Program=low.Programs["fixture:sl-low"]});
            }
            if(mode is "multi" or "gift-loss" or "multi-extra")
            {
                var multi=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:sl-multi","revision":1,"viewAs":[{"id":"pair","inputCount":2,"inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}],"activations":[{"id":"use","minCards":2,"maxCards":2,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingInAttackRange","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"pair","outputKind":"slash"}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:sl-multi":{"name":"双实体杀","description":"真实双实体转换"}}}""");b.AddSkill(new("fixture:sl-multi","双实体杀","转换"){Program=multi.Programs["fixture:sl-multi"]});
            }
            if(mode=="multi-extra")
            {
                var extra=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:sl-extra","revision":1,"triggers":[{"id":"extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:sl-extra":{"name":"额外出牌","description":"真实额外出牌阶段"}}}""");b.AddSkill(new("fixture:sl-extra","额外出牌","真实阶段"){Program=extra.Programs["fixture:sl-extra"]});
            }
            if(mode=="gift-loss")
            {
                var lost=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:sl-gift-loss","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.faction-cost-gift.obtain"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":2}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:sl-gift-loss":{"name":"赠予失源","description":"真实移动子链","optionLabels":{"continue":"继续"}}}}""");b.AddSkill(new("fixture:sl-gift-loss","赠予失源","移动"){Program=lost.Programs["fixture:sl-gift-loss"]});
            }
            if(mode=="gift-loss")b.AddSkill(new("fixture:sl-suppression","体力压制","9体力压制其他技能"){SuppressionRule=new(9)});
            for(var i=0;i<4;i++)b.AddGeneral(new(i==0?"fixture:sl-owner":$"fixture:sl-{i}","机制将","supporter",mode is "gift" or "multi" or "gift-loss" or "multi-extra"?"classic:lijun":mode=="source-death"||i==0?"classic:kuizhu":"standard:none","wu",mode is "gift" or "multi" or "gift-loss" or "multi-extra"?10:3,mode=="source-death"?["fixture:sl-low"]:mode is "multi" or "gift-loss" or "multi-extra"?["fixture:sl-multi",..(mode=="multi-extra"?new[]{"fixture:sl-extra"}:Array.Empty<string>()),..(mode=="gift-loss"?new[]{"fixture:sl-gift-loss","fixture:sl-suppression"}:Array.Empty<string>())]:i==0&&mode!="gift"?["fixture:sl-driver",..(mode is "range" or "chain"?new[]{"classic:chezheng"}:mode=="occurrence"?new[]{"fixture:sl-phase"}:Array.Empty<string>())]:mode=="occurrence"?["fixture:sl-claim"]:[],GeneralGender.Male));
            b.AddDeck(new("fixture:sl-deck","小实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(mode=="gift"&&i%2==0?"standard:slash":mode=="chain"&&i%2==0?"standard:fire_slash":mode=="occurrence"?i%4==0?"standard:qinggang_sword":i%4==1?"standard:lightning":"standard:dodge":"standard:dodge",(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-budget-gift-check","弃置预算与赠杀机制",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:sl-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:sl-owner","fixture:sl-1","fixture:sl-2","fixture:sl-3"]));
        }
    }
}
