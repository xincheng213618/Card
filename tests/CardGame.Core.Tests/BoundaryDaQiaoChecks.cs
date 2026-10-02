using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class BoundaryDaQiaoChecks
{
    public static void UseAndSharedPhaseReplay()
    {
        var (g,r)=Create(); var p=Prompt(g)!;
        var action=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="boundary:guose");
        var card=action.SelectableCardIds.First(); var initial=g.CreateSnapshot(0).Players[0].Hand.Count;
        Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[card],[],g.Revision,p.PromptId));
        Require(g.CreateSnapshot(1).PendingDecision?.Choices.Count is null or 0,"Only owner sees private branch choices.");
        Replay(g,r); Reject(g); Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="use"&&c.Targets.SequenceEqual([1])); ReachPlay(g);
        Require(g.CardMovements.Count(m=>m.CardId==card&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1,"Use pays exact physical HE once.");
        Require(g.CardMovements.Any(m=>m.CardId==card&&m.To==CardLocation.Judgment(1)),"The converted delayed card truly enters Judgment.");
        Require(g.CreateSnapshot(0).Players[0].Hand.Count==initial,"Use cost and completed refill balance one hand card.");
        Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:guose"),"The branch shares one actual Play phase limit.");
        Replay(g,r);
    }
    public static void AtomicMixedDiscardReplay()
    {
        var (g,r)=Create(); var initial=g.CreateSnapshot(0).Players[0].Hand.Count;
        var setup=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Indulgence&&a.TargetSeat==1&&a.ConversionSource?.SkillId=="classic:guose");
        var judgment=setup.CardId!.Value;
        Accept(g,new PlayCardCommand(0,judgment,[1],g.Revision,Prompt(g)!.PromptId,CardKind.Indulgence){ConversionSource=setup.ConversionSource}); ReachPlay(g);
        var action=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="boundary:guose"); var payment=action.SelectableCardIds.First();
        var before=State(g); Require(!g.Submit(new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[judgment],[],g.Revision,Prompt(g)!.PromptId)).Accepted&&before==State(g),"A Judgment entity cannot fake the owner payment.");
        Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[payment],[],g.Revision,Prompt(g)!.PromptId)); Replay(g,r); Reject(g);
        Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="discard"&&c.Cards.SequenceEqual([judgment])); ReachPlay(g);
        var moves=g.CardMovements.Where(m=>m.CardId==payment||m.CardId==judgment).Where(m=>m.Reason.Value=="skill-program.diamond-delayed.discard").ToArray();
        Require(moves.Length==2&&moves.All(m=>m.To==CardLocation.DiscardPile)&&moves.Any(m=>m.From==CardLocation.Hand(0))&&moves.Any(m=>m.From==CardLocation.Judgment(1)),"Both exact mixed-source entities really discard once.");
        Require(g.CreateSnapshot(0).Players[0].Hand.Count==initial-1,"Only the two real payments earn one refill."); Replay(g,r);
    }
    public static void EquipmentChildrenBeforeDraw()
    {
        var(g,r)=Create("equipment");var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);
        Accept(g,new PlayCardCommand(0,equip.CardId!.Value,[],g.Revision,Prompt(g)!.PromptId));ReachPlay(g);
        var before=g.CreateSnapshot(0).Players[0].Hand.Count;
        Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[equip.CardId.Value],[],g.Revision,Prompt(g)!.PromptId));
        Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="use"&&c.Targets.SequenceEqual([1]));
        for(var i=0;i<64&&Prompt(g)?.SkillPrompt?.SkillId!="fixture:dq-child";i++)Accept(g,new AdvanceOneStepCommand(g.Revision));
        Require(Prompt(g)?.SkillPrompt?.SkillId=="fixture:dq-child","A real equipment-leave movement child pauses the owning use.");
        Require(g.CreateSnapshot(0).Players[0].Hand.Count==before,"Guose refill waits for all original-use movement children.");
        Replay(g,r);Reject(g);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[0].Hand.Count==before+2,"The child draws one then Guose draws one exactly once.");
        Require(g.CardMovements.Count(m=>m.CardId==equip.CardId&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.Processing)==1,"Equipped Diamond physical cost pays once.");Replay(g,r);
    }
    public static void NullifiedUseStillDraws()
    {
        var(g,r)=Create("nullification");var a=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="boundary:guose");var id=a.SelectableCardIds.First();var before=g.CreateSnapshot(0).Players[0].Hand.Count;
        Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[id],[],g.Revision,Prompt(g)!.PromptId));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="use"&&c.Targets.SequenceEqual([1]));
        for(var i=0;i<64&&!(Prompt(g) is {Kind:DecisionKind.Nullification,PlayerSeat:0});i++)Accept(g,new AdvanceOneStepCommand(g.Revision));
        Require(Prompt(g) is {Kind:DecisionKind.Nullification,PlayerSeat:0},"Real owner response can negate the declared use."); Replay(g,r);
        var owning=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.DiamondDelayed is not null);
        var checker=typeof(GameEngine).GetMethod("AssertDiamondDelayed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        foreach(var invalid in new[]{owning with{DiamondDelayed=owning.DiamondDelayed! with{UseFrameId=owning.DiamondDelayed.UseFrameId+1}},owning with{DiamondDelayed=owning.DiamondDelayed! with{Source=CardLocation.Equipment(0)}}})
        {
            var unchanged=State(g);var rejected=false;
            try{checker.Invoke(g,[invalid,r.GetSkill("boundary:guose").Program!.Activations.Single().Effects.Single()]);}
            catch(System.Reflection.TargetInvocationException e) when(e.InnerException is InvalidOperationException){rejected=true;}
            Require(rejected&&State(g)==unchanged,"Wrong child identity or frozen physical cost source is rejected without mutating the true owning frame.");
        }
        Answer(g,c=>c.Cards.Count==1);
        ReachPlay(g);Require(g.Events.Select(e=>e.Payload).OfType<NullificationResolvedEvent>().Any(e=>e.EffectNullified),"Actual Nullification negates the converted delayed effect.");
        Require(g.CardMovements.Any(m=>m.CardId==id&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile)&&!g.CardMovements.Any(m=>m.CardId==id&&m.To==CardLocation.Judgment(1)),"Negated physical use cleans up Processing rather than placing a delayed card.");
        Require(g.CreateSnapshot(0).Players[0].Hand.Count==before-1,"Completed negated use still earns exactly one Guose refill.");Replay(g,r);
        Require(!JsonSerializer.Serialize(new ProgramSkillFrame(1,0,"old","old","hash",0,[],[])).Contains("DiamondDelayed"),"The new nullable draft is omitted from old frame JSON.");
    }
    public static void ExtraPlayRefreshAndEffectiveSuit()
    {
        var(g,r)=Create("extra");var turn=g.CreateSnapshot(0).TurnNumber;
        var a=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="boundary:guose");Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[a.SelectableCardIds.First()],[],g.Revision,Prompt(g)!.PromptId));
        Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="use"&&c.Targets.SequenceEqual([1]));ReachPlay(g);
        Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:guose"),"Extra Play consumes its own one quota.");
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));ReachPlay(g);
        Require(g.CreateSnapshot(0).TurnNumber==turn&&g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:guose"),"Following actual normal Play refreshes the shared quota within the same turn.");Replay(g,r);
        var(e,er)=Create("effective");var physical=e.CreateSnapshot(0).Players[0].Hand.First();Require(physical.Suit==Suit.Spade,"Fixture payment is physically printed Spade.");
        var effective=e.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="boundary:guose");Require(effective.SelectableCardIds.Contains(physical.Id),"Capability uses existing effective Diamond rewrite.");
        Accept(e,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[physical.Id],[],e.Revision,Prompt(e)!.PromptId));Replay(e,er);Answer(e,c=>c.Parameters.GetValueOrDefault("branch")=="use");ReachPlay(e);Replay(e,er);
        var(n,nr)=Create("non-diamond");var before=State(n);Require(!n.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:guose"),"No Diamond follow-up hides activation without consuming quota.");
        Require(!n.Submit(new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[n.CreateSnapshot(0).Players[0].Hand.First().Id],[],n.Revision,Prompt(n)!.PromptId)).Accepted&&State(n)==before,"Non-Diamond payment rejects atomically.");Replay(n,nr);
    }
    public static void PaidSourceLossAndOwnerDeath()
    {
        foreach(var mode in new[]{"equipment-loss","equipment-death"})
        {
            var(g,r)=Create(mode=="equipment-loss"?"equipment":mode);var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);Accept(g,new PlayCardCommand(0,equip.CardId!.Value,[],g.Revision,Prompt(g)!.PromptId));ReachPlay(g);
            var before=g.CreateSnapshot(0).Players[0].Hand.Count;
            Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[equip.CardId.Value],[],g.Revision,Prompt(g)!.PromptId));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="use");
            for(var i=0;i<64&&Prompt(g)?.SkillPrompt?.SkillId!="fixture:dq-child";i++)Accept(g,new AdvanceOneStepCommand(g.Revision));
            Require(Prompt(g)?.SkillPrompt?.SkillId=="fixture:dq-child","Real payment child is the interruption boundary.");Replay(g,r);
            if(mode=="equipment-loss")
            {
                // Existing public grant-model mutation: replay is asserted before this external lifecycle audit only.
                var owner=((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!)[0];
                owner.SkillGrants.RemoveGrant(owner.SkillGrants.Grants.Single(q=>q.SkillId=="boundary:guose").GrantId);
            }
            Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
            if(mode=="equipment-loss")ReachPlay(g);else for(var i=0;i<64&&g.ResolutionStack.Count>0;i++)Accept(g,new AdvanceOneStepCommand(g.Revision));
            Require(g.CardMovements.Count(m=>m.CardId==equip.CardId&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.Processing)==1,"Canceled continuation never repays original equipped cost.");
            if(mode=="equipment-loss")Require(g.CreateSnapshot(0).Players[0].Hand.Count==before+1&&!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:guose"),"Source loss after actual payment cancels refill.");
            else Require(!g.CreateSnapshot(0).Players[0].IsAlive&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.DiamondDelayed is not null),"Actual lethal HP-loss child completes dying/death and cancels owner refill.");if(mode!="equipment-loss")Replay(g,r);
        }
    }
    public static void PaidTerminalStopsRefill()
    {
        var(g,r)=Create("equipment-terminal");var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);
        Accept(g,new PlayCardCommand(0,equip.CardId!.Value,[],g.Revision,Prompt(g)!.PromptId));ReachPlay(g);var before=g.CreateSnapshot(0).Players[0].Hand.Count;
        Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[equip.CardId.Value],[],g.Revision,Prompt(g)!.PromptId));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="use");
        for(var i=0;i<64&&Prompt(g)?.SkillPrompt?.SkillId!="fixture:dq-child";i++)Accept(g,new AdvanceOneStepCommand(g.Revision));
        Require(Prompt(g)?.SkillPrompt?.SkillId=="fixture:dq-child","Real equipment payment exposes the terminal movement child.");Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        for(var i=0;i<192&&g.ResolutionStack.Count>0;i++)
        {
            if(Prompt(g) is {PlayerSeat:0} p&&p.Choices.Any(c=>c.Targets.Count==1))Answer(g,c=>c.Targets.Count==1);
            else if(Prompt(g)?.SkillPrompt?.SkillId=="fixture:dq-child")Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        var view=g.CreateSnapshot(0);
        Require(view.Winner!=Winner.None&&view.Players[0].IsAlive&&view.Players.Skip(1).All(p=>!p.IsAlive),"Actual nested HP-loss/death resolution reaches a winner while Guose owner lives.");
        Require(view.Players[0].Hand.Count==before,"Terminal payment child cannot start a new Guose refill for a surviving owner.");
        Require(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.DiamondDelayed is not null),"Terminal continuation retires its owning draft.");
        Require(g.CardMovements.Count(m=>m.CardId==equip.CardId&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.Processing)==1,"Terminal use pays the physical Equipment exactly once.");Replay(g,r);
    }
    public static void BranchAcceptRechecksTargetAndSource()
    {
        foreach(var mode in new[]{"target","source"})
        {
            var(g,r)=Create();var a=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="boundary:guose");
            Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[a.SelectableCardIds.First()],[],g.Revision,Prompt(g)!.PromptId));Replay(g,r);
            var choice=Prompt(g)!.Choices.First(c=>c.Parameters.GetValueOrDefault("branch")=="use"&&c.Targets.SequenceEqual([1]));
            var players=(IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;
            // Host lifecycle audit only after this point: external state changes do not enter the command journal.
            if(mode=="target")players[1].JudgmentAreaAbolished=true;
            else players[0].SkillGrants.RemoveGrant(players[0].SkillGrants.Grants.Single(q=>q.SkillId=="boundary:guose").GrantId);
            var before=State(g);var rejected=false;
            try{typeof(GameEngine).GetMethod("ResolveDiamondDelayedChoice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(g,[choice]);}
            catch(System.Reflection.TargetInvocationException e)when(e.InnerException is InvalidOperationException){rejected=true;}
            Require(rejected&&State(g)==before,"Exact stale "+mode+" choice rejects before clearing its prompt, payment, refill or additional state changes.");
        }
    }
    public static void OwnJudgmentRemoval()
    {
        var(g,r)=Create("self");var initialTurn=g.CreateSnapshot(0).TurnNumber;
        for(var i=0;i<96;i++)
        {
            if(Prompt(g)?.Kind==DecisionKind.PlayCard&&g.CreateSnapshot(0).CurrentSeat==0)
            {
                if(g.CreateSnapshot(0).TurnNumber>initialTurn)break;
                Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));
            }
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        Require(g.CreateSnapshot(0).Players[0].Judgment.Count>0,"Real foreign use placed an Indulgence on the owner's Judgment before extra Play: "+string.Join(";",g.CardMovements.Where(m=>m.To.Zone==CardZoneKind.Judgment).Select(m=>$"{m.CardId}/{m.To.OwnerSeat}/{m.TurnNumber}")));
        var judgment=g.CreateSnapshot(0).Players[0].Judgment.First();var a=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="boundary:guose");var cost=a.SelectableCardIds.First();var before=g.CreateSnapshot(0).Players[0].Hand.Count;
        Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[cost],[],g.Revision,Prompt(g)!.PromptId));Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="discard"&&c.Cards.SequenceEqual([judgment.Id])&&c.Targets.SequenceEqual([0]));ReachPlay(g);
        Require(g.CardMovements.Any(m=>m.CardId==judgment.Id&&m.From==CardLocation.Judgment(0)&&m.To==CardLocation.DiscardPile),"Self Judgment is a true mixed-source discard.");
        Require(g.CreateSnapshot(0).Players[0].Hand.Count==before,"Owner cost is discarded and refilled once.");Replay(g,r);
    }
    public static void SourceEquipmentCostExclusion()
    {
        var(g,r)=Create("source-equipment");var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);
        Accept(g,new PlayCardCommand(0,equip.CardId!.Value,[],g.Revision,Prompt(g)!.PromptId));ReachPlay(g);Replay(g,r);
        // Audit the existing exact equipment-grant model; external grant mutation is not journal replay.
        var owner=((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!)[0];
        owner.SkillGrants.RemoveGrant(owner.SkillGrants.Grants.Single(q=>q.SkillId=="boundary:guose").GrantId);
        var equipmentId=$"equipment:xingtian:{equip.CardId}";
        foreach(var old in owner.SkillGrants.Grants.Where(q=>q.GrantId==equipmentId).ToArray())owner.SkillGrants.RemoveGrant(old.GrantId);
        owner.SkillGrants.Grant(new(equipmentId,"boundary:guose",equipmentId,equipmentId));
        owner.SkillGrants.Grant(new("zzz:guose","boundary:guose","zzz:guose","fixture:extra-source"));
        var a=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="boundary:guose");
        Require(!a.SelectableCardIds.Contains(equip.CardId.Value),"Exact source Equipment is excluded from both-branch activation candidates.");
        var before=State(g);Require(!g.Submit(new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[equip.CardId.Value],[],g.Revision,Prompt(g)!.PromptId)).Accepted&&State(g)==before,"Forged source-Equipment cost rejects before quota or payment.");
        var hand=a.SelectableCardIds.First();Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[hand],[],g.Revision,Prompt(g)!.PromptId));
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single().SkillInstanceId==equipmentId,"Accepted alternative Hand cost freezes the actual equipment source instance.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="use");ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[0].Equipment.Any(c=>c.Id==equip.CardId),"Using a different cost preserves source Equipment.");
        owner.SkillGrants.RemoveGrant(equipmentId);owner.SkillGrants.RemoveGrant("zzz:guose");
        owner.SkillGrants.Grant(new("replacement:guose","boundary:guose","replacement:guose","fixture:replacement"));
        Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:guose"),"Same-owner multiple grants and loss/regrant cannot reset the named phase quota.");
    }
    public static void MixedEquipmentDiscardChildren()
    {
        var(g,r)=Create("equipment");var setup=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Indulgence&&a.TargetSeat==1&&a.ConversionSource?.SkillId=="classic:guose");
        Accept(g,new PlayCardCommand(0,setup.CardId!.Value,[1],g.Revision,Prompt(g)!.PromptId,CardKind.Indulgence){ConversionSource=setup.ConversionSource});ReachPlay(g);
        var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);Accept(g,new PlayCardCommand(0,equip.CardId!.Value,[],g.Revision,Prompt(g)!.PromptId));ReachPlay(g);var before=g.CreateSnapshot(0).Players[0].Hand.Count;
        Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[equip.CardId.Value],[],g.Revision,Prompt(g)!.PromptId));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="discard"&&c.Cards.SequenceEqual([setup.CardId.Value]));
        Require(Prompt(g)?.SkillPrompt?.SkillId=="fixture:dq-child"&&g.CreateSnapshot(0).Players[0].Hand.Count==before,"Both mixed entities have discarded before the actual equipment child, with no early refill.");
        var outer=g.ResolutionStack.OfType<ProgramSkillFrame>().First(f=>f.DiamondDelayed is not null);var batch=g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single().Batch;
        Require(batch.ParentFrameId==outer.Id&&batch.Movements.Count==2&&batch.Movements.Select(m=>m.CardId).SequenceEqual([equip.CardId.Value,setup.CardId.Value]),"Atomic batch belongs to the exact owning program and freezes ordered equipment plus Judgment entities.");
        Require(batch.Movements is IList<CardMovementRecord> list&&list.IsReadOnly,"The prepared movement collection is read-only.");
        Replay(g,r);Reject(g);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[0].Hand.Count==before+2,"Equipment child then Guose refill each draw exactly one.");
        Require(g.CreateSnapshot(0).Players[1].Judgment.Count==0,"Converted delayed identity leaves the actual Judgment area.");
        var kinds=(IReadOnlyDictionary<int,CardKind>)typeof(GameEngine).GetField("_judgmentEffectiveCardKinds",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;
        Require(!kinds.ContainsKey(setup.CardId.Value),"Removing a converted delayed card clears its inherited effective-kind ledger.");Replay(g,r);
    }
    private static SkillProgramCatalog LoadFixture(string rules,string presentation)
    {
        var r=JsonNode.Parse(rules)!;r["schemaVersion"]=SkillProgramCatalog.RulesSchemaVersion;
        var p=JsonNode.Parse(presentation)!;p["schemaVersion"]=SkillProgramCatalog.PresentationSchemaVersion;
        return SkillProgramCatalog.Load(r.ToJsonString(),p.ToJsonString());
    }
    private static void Require(bool v,string m){if(!v)throw new InvalidOperationException(m);}
    private static PendingDecision? Prompt(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Command rejected.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> find){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(find).Id,g.Revision));}
    private static void ReachPlay(GameEngine g){for(var i=0;i<64;i++){if(Prompt(g)?.Kind==DecisionKind.PlayCard&&g.CreateSnapshot(0).CurrentSeat==0)return;if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.Nullification} n) Accept(g,new AnswerPromptCommand(0,n.PromptId,n.Choices.First(c=>c.Cards.Count==0).Id,g.Revision)); else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Fixed fixture did not reach actual Play.");}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static void Replay(GameEngine g,ContentRegistry r)=>Require(State(g)==State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r)),"Four views, typed frames, events, movements and journal restore identically.");
    private static void Reject(GameEngine g){var b=State(g);var p=Prompt(g)!;Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new("fixture:invalid"),g.Revision)).Accepted&&State(g)==b,"Illegal options are atomic.");}
    private static (GameEngine,ContentRegistry) Create(string mode="default")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=1,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-dq-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:dq-owner",g.Revision,Prompt(g)!.PromptId));ReachPlay(g);return(g,r);
    }
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-dq",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            if(mode=="nullification")
            {
                var counter=LoadFixture("""{"skills":[{"id":"fixture:dq-counter","revision":1,"viewAs":[{"id":"diamond-counter","sourceZones":["hand"],"inputKinds":["dodge"],"inputSuits":["diamond"],"outputKind":"nullification","forPlay":false,"forResponse":true}]}]}""","""{"skills":{"fixture:dq-counter":{"name":"固定无懈","description":"仅本人固定实体响应"}}}""");
                b.AddSkill(new("fixture:dq-counter","固定无懈","固定实体响应"){Program=counter.Programs["fixture:dq-counter"]});
            }
            if(mode.StartsWith("equipment",StringComparison.Ordinal))
            {
                var childRules="""{"skills":[{"id":"fixture:dq-child","revision":1,"triggers":[{"id":"movement","window":"cardsMoved","subject":"owner","movementOccurrence":"perBatch","sourceZones":["equipment"],"optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}""";
                if(mode=="equipment-death")childRules=childRules.Replace("{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}","{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":4}");
                if(mode=="equipment-terminal")
                {
                    var terminal=JsonNode.Parse(childRules)!;var triggers=terminal["skills"]![0]!["triggers"]!.AsArray();var template=triggers[0]!.DeepClone();triggers.Clear();
                    for(var n=0;n<3;n++){var trigger=template.DeepClone();trigger["id"]="terminal-"+n;trigger["effects"]=JsonNode.Parse("""[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"loseHp","target":"selectedTarget","amount":20}]""");triggers.Add(trigger);}
                    childRules=terminal.ToJsonString();
                }
                var cat=LoadFixture(childRules,"""{"skills":{"fixture:dq-child":{"name":"付款子链","description":"装备真实离区摸一张"}}}""");
                b.AddSkill(new("fixture:dq-child","付款子链","付款子链"){Program=cat.Programs["fixture:dq-child"]});
            }
            if(mode=="effective")
            {
                var rewrite=LoadFixture("""{"skills":[{"id":"fixture:dq-suit","revision":1,"cardPolicies":[{"id":"rewrite","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"diamond"}]}]}""","""{"skills":{"fixture:dq-suit":{"name":"固定有效花色","description":"黑桃有效方片"}}}""");
                b.AddSkill(new("fixture:dq-suit","固定有效花色","固定有效花色"){Program=rewrite.Programs["fixture:dq-suit"]});
            }
            b.AddGeneral(new("fixture:dq-owner","固定大乔","supporter","boundary:guose","wu",mode=="equipment-death"?3:20,new[]{"classic:guose","boundary:liuli"}.Concat(mode.StartsWith("equipment",StringComparison.Ordinal)?["fixture:dq-child"]:mode=="nullification"?["fixture:dq-counter"]:mode=="effective"?["fixture:dq-suit"]:mode is "extra" or "self"?["classic:dangxian"]:Array.Empty<string>()).ToArray(),GeneralGender.Female));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:dq-target-{i}","固定目标","supporter","standard:none","wei",20,[]));
            b.AddDeck(new("fixture:dq-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard(mode=="source-equipment"?"special:xingtian-axe":mode.StartsWith("equipment",StringComparison.Ordinal)?"standard:crossbow":mode=="self"?"standard:indulgence":"standard:dodge",mode is "effective" or "non-diamond"?Suit.Spade:Suit.Diamond,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-dq-fixture","固定",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[mode is "self" or "equipment-terminal"?nameof(Role.Rebel):nameof(Role.Renegade)]=3},"fixture:dq-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:dq-owner","fixture:dq-target-1","fixture:dq-target-2","fixture:dq-target-3"]));
        }
    }
}
