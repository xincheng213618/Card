using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class FengLinHaoZhaoChecks
{
    public static void DynamicTwoEndsAndNativeDiscard()
    {
        var (g,r)=Create("dynamic");Use(g,"grow-owner",[]);Use(g,"grow-target",[1]);
        EndAndChoose(g,1);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:zhengu"&&p.PlayerSeat==1);Replay(g,r);RejectUnknown(g);
        Require(Enumerable.Range(0,4).All(v=>g.CreateSnapshot(v).Players[1].DeferredHandAlignments is [var remaining]&&remaining.DueKind==DeferredHandAlignmentDueKind.TargetNextActualTurnEnd),"Current due is consumed before the recipient's private native discard pause, leaving only its next actual-end due public.");
        Reach(g,p=>p.PlayerSeat==0&&p.SkillPrompt?.SkillId=="fixture:mutator");Replay(g,r);for(var i=0;i<3;i++)Answer(g,c=>c.Cards.Count==1);
        Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);var results=g.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentResolvedEvent>().ToArray();
        Require(results.Length==2&&results[0] is {OwnerHandCount:8,TargetHandCount:10,DiscardCount:2}&&results[1].OwnerHandCount==5&&results[1].DiscardCount==5,"Both actual ends read the owner's hand anew; the first discards to eight, the second to the changed five.");
        Require(!g.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat==1)&&g.CardMovements.Count(m=>m.From==CardLocation.Hand(1)&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.deferred-hand.discard")==7,"Native recipient AI discards exactly seven real hand entities across both ends.");Replay(g,r);
    }
    public static void DrawCapAndRealGainReplay()
    {
        foreach(var cap in new[]{false,true})
        {
            var(g,r)=Create("plain");if(cap){Use(g,"grow-owner",[]);Use(g,"grow-target-small",[1]);}
            EndAndChoose(g,1);Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);var first=g.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentResolvedEvent>().First();
            Require(cap?first is {OwnerHandCount:8,TargetHandCount:6,DrawCount:0,DiscardCount:0}:first is {OwnerHandCount:5,TargetHandCount:3,DrawCount:2},"The draw cap applies only to actual draws, never as a discard-to-five clamp.");Replay(g,r);
        }
    }
    public static void SkippedAndExtraActualEnds()
    {
        foreach(var mode in new[]{"skipped","extra"})
        {
            var(g,r)=Create(mode);Use(g,mode=="skipped"?"flip":"extra",[1]);EndAndChoose(g,1);
            Reach(g,p=>g.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentResolvedEvent>().Count()==2);
            var consumed=g.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentConsumedEvent>().ToArray();
            Require(consumed.Length==2&&consumed[1].Alignment.DueKind==DeferredHandAlignmentDueKind.TargetNextActualTurnEnd&&g.CreateSnapshot(0).Players[1].DeferredHandAlignments is null,"A face-up skipped turn and an actual extra turn both consume the recipient's next actual-end due.");Replay(g,r);
        }
    }
    public static void OrderedNestedAndNativeOwner()
    {
        var(g,r)=Create("overlap");Use(g,"grow-owner",[]);Use(g,"grow-target",[1]);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:double");Answer(g,c=>c.Targets.Contains(1));
        Reach(g,p=>p.PlayerSeat==1&&p.SkillPrompt?.SkillId=="fixture:observer");
        Require(g.ResolutionStack.First() is DeferredTurnEndFrame {Current:not null},"Real discard movement pauses under the exact finalized-end parent.");Replay(g,r);RejectUnknown(g);
        Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);var resolved=g.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentResolvedEvent>().ToArray();
        Require(resolved.Select(x=>x.Id).SequenceEqual(new long[]{1,3,2,4})&&resolved[0].DiscardCount==2&&resolved[1].DiscardCount==0,"Overlapping due entries use creation order and each delta is sampled only when its own item begins.");Replay(g,r);
        var(ai,ar)=Create("ai-owner");Accept(ai,new EndPlayPhaseCommand(0,ai.Revision,Prompt(ai)!.PromptId));Reach(ai,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(ai.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentScheduledEvent>().Any(x=>x.Alignment.Source.OwnerSeat!=0)&&!ai.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat!=0),"Actual owner AI activates and selects alignment targets with no artificial answers.");Replay(ai,ar);
    }
    public static void NativeSourcePrivateHumanRecipient()
    {
        var(g,r)=Create("ai-owner");Use(g,"grow-owner",[]);Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));
        Reach(g,p=>p.PlayerSeat==0&&p.SkillPrompt?.SkillId=="classic:zhengu"&&g.ResolutionStack.FirstOrDefault() is DeferredTurnEndFrame);
        var child=g.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        Require(child.OwnerSeat!=0&&child.OwnedCardSelection?.CardOwnerSeat==0,"Native source AI requests the real human recipient's private hand payment.");
        Require(Enumerable.Range(1,3).All(v=>g.CreateSnapshot(v).PendingDecision is null),"All ordinary other-seat snapshots conceal the recipient's private entity choices.");Replay(g,r);RejectUnknown(g);
        while(Prompt(g)?.PlayerSeat==0&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.OwnedCardSelection?.CardOwnerSeat==0))Answer(g,c=>c.Cards.Count==1);
        Replay(g,r);
    }
    public static void DeathCancelsAndResolverContracts()
    {
        foreach(var mode in new[]{"death-target","death-source"})
        {
            var(g,r)=Create(mode);Use(g,"grow-owner",[]);Use(g,"grow-target",[1]);EndAndChoose(g,1);
            if(mode=="death-source"){Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:source-death");Replay(g,r);Answer(g,c=>c.Targets.Contains(0));}
            Reach(g,p=>false,()=>g.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentCancelledEvent>().Any());
            Require(g.CreateSnapshot(0).Players.All(p=>p.DeferredHandAlignments is null)&&g.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentResolvedEvent>().Count()==1,"Real nested target/source death cancels the outstanding next-end due.");Replay(g,r);
        }
        var valid=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:contract","revision":1,"triggers":[{"id":"settle","window":"turnEnding","subject":"owner","optional":false,"deferredTurnEndOnly":true,"effects":[{"op":"resolveDeferredHandAlignment","target":"owner","resultBind":"discarded"}]}]}]}""";
        foreach(var invalid in new[]{valid.Replace("\"deferredTurnEndOnly\":true","\"deferredTurnEndOnly\":false"),valid.Replace("\"optional\":false","\"optional\":true"),valid.Replace("\"turnEnding\"","\"playPhaseStarting\""),valid.Replace("\"target\":\"owner\"","\"target\":\"selectedTarget\"")})
        {try{SkillProgramCatalog.Load(invalid,"""{"schemaVersion":3,"skills":{"fixture:contract":{"name":"契约","description":"严格"}}}""");throw new Exception("Forged deferred resolver unexpectedly loaded.");}catch(InvalidOperationException){}}
        var validSchedule=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:contract","revision":1,"triggers":[{"id":"schedule","window":"turnEnding","subject":"owner","optional":true,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"scheduleDeferredHandAlignment","target":"selectedTarget","stateId":"settle"}]},{"id":"settle","window":"turnEnding","subject":"owner","optional":false,"deferredTurnEndOnly":true,"effects":[{"op":"resolveDeferredHandAlignment","target":"owner","resultBind":"discarded"}]}]}]}""";
        SkillProgramCatalog.Load(validSchedule,"""{"schemaVersion":3,"skills":{"fixture:contract":{"name":"契约","description":"严格"}}}""");
        foreach(var invalid in new[]{validSchedule.Replace("\"targetKind\":\"otherLiving\"","\"targetKind\":\"anyLiving\""),validSchedule.Replace("\"id\":\"schedule\",\"window\":\"turnEnding\",\"subject\":\"owner\"","\"id\":\"schedule\",\"window\":\"turnEnding\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\"")})
        {try{SkillProgramCatalog.Load(invalid,"""{"schemaVersion":3,"skills":{"fixture:contract":{"name":"契约","description":"严格"}}}""");throw new Exception("An invalid deferred schedule unexpectedly loaded.");}catch(InvalidOperationException){}}
    }
    public static void GrantLossAndSuppressionMaturity()
    {
        var(g,r)=Create("grant");Use(g,"grant",[]);EndAndChoose(g,1);Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Require(g.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentCancelledEvent>().Count()==1&&g.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentResolvedEvent>().Count()==1,"The exact temporary source grant expires at the next actual TurnStarted, canceling the remaining due after its first legitimate resolution.");Replay(g,r);
        var(s,sr)=Create("suppressed");EndAndChoose(s,3);Reach(s,p=>p.SkillPrompt?.SkillId=="fixture:suppress");Answer(s,c=>c.Targets.Contains(0));Answer(s,c=>c.Id.Value.Contains("classic:zhengu"));
        Reach(s,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);var consumed=s.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentConsumedEvent>().ToArray();
        Require(consumed.Length==2&&consumed[0].Applied&&!consumed[1].Applied&&!s.Events.Select(e=>e.Payload).OfType<DeferredHandAlignmentCancelledEvent>().Any(),"After the first due, a real Play-phase suppression leaves the grant present and consumes its next mature due without effect.");Replay(s,sr);
    }
    private static void Use(GameEngine g,string id,IReadOnlyList<int> seats){Accept(g,new UseProgramSkillCommand(0,"fixture:driver",id,[],seats,g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);}
    private static void EndAndChoose(GameEngine g,int seat){Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.SkillPrompt?.SkillId=="classic:zhengu");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Targets.Contains(seat));}
    private static PendingDecision? Prompt(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Play(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,Prompt(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate,Func<bool>? terminal=null)
    {
        for(var step=0;step<250;step++)
        {if(Prompt(g) is { } p && predicate(p))return;if(terminal?.Invoke()==true)return;if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RescueDying})Answer(g,c=>c.Cards.Count==0);else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.RespondSlash})Answer(g,c=>c.Cards.Count==0);else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.ProgramTrigger}) Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip" || c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
        throw new InvalidOperationException("Fixed hand-alignment fixture did not reach requested prompt: "+JsonSerializer.Serialize(Prompt(g))+" frames="+JsonSerializer.Serialize(g.ResolutionStack));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Movements=g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(restored),"All observer/private snapshots, typed frames, entities and command JSON replay identically.");}
    private static void RejectUnknown(GameEngine g){var before=State(g);var p=Prompt(g)!;var result=g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new ChoiceId("fixture:illegal"),g.Revision));Require(!result.Accepted&&before==State(g),"Illegal input leaves every view, entity, event and accepted command untouched.");}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(r.Accepted,r.Error?.Message??"Rejected.");}
    private static void Require(bool b,string m){if(!b)throw new InvalidOperationException(m);}

    private static (GameEngine,ContentRegistry)Create(string mode)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-haozhao-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:owner",g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard);return(g,r);
    }
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-hand-alignment",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog=SkillProgramCatalog.Load($$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:driver","revision":1,"activations":[{"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnSkills","target":"owner","skillIds":["classic:zhengu"]}]},{"id":"flip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"selectedTarget"}]},{"id":"extra","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"pendExtraTurn","target":"owner","targetRef":{"kind":"selectedTarget"}}]},{"id":"grow-owner","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},{"id":"grow-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":7}]},{"id":"grow-target-small","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":3}]}]},{"id":"fixture:mutator","revision":1,"triggers":[{"id":"change","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"otherLiving","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","amount":3,"zones":["hand"],"resultBind":"selected"},{"op":"moveBoundCards","target":"owner","sourceBind":"selected","destination":"discardPile","awaitMovementTriggers":true}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:driver":{"name":"真实手数驱动","description":"实体抽牌"},"fixture:mutator":{"name":"手数改变","description":"第一次其他Play本人弃三张"}}}""");
            foreach(var pair in catalog.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value});
            var extras=SkillProgramCatalog.Load($$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:suppress","revision":1,"triggers":[{"id":"suppress","window":"playPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"suppressGeneralSkill","target":"selectedTarget"}]}]},{"id":"fixture:source-death","revision":1,"triggers":[{"id":"movement","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.deferred-hand.discard"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"loseHp","target":"selectedTarget","amount":20}]}]},{"id":"fixture:death","revision":1,"triggers":[{"id":"movement","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.deferred-hand.discard"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]},{"id":"fixture:double","revision":1,"triggers":[{"id":"schedule","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"scheduleDeferredHandAlignment","target":"selectedTarget","stateId":"settle"},{"op":"scheduleDeferredHandAlignment","target":"selectedTarget","stateId":"settle"}]},{"id":"settle","window":"turnEnding","subject":"owner","optional":false,"deferredTurnEndOnly":true,"effects":[{"op":"resolveDeferredHandAlignment","target":"owner","resultBind":"discarded"}]}]},{"id":"fixture:observer","revision":1,"triggers":[{"id":"movement","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.deferred-hand.discard"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:death":{"name":"目标死亡","description":"真实失血"},"fixture:source-death":{"name":"来源死亡","description":"真实失血"},"fixture:suppress":{"name":"压制","description":"真实禁用"},"fixture:double":{"name":"重复对齐","description":"公开机制重叠"},"fixture:observer":{"name":"真实移动观察","description":"暂停","optionLabels":{"continue":"继续"}}}}""");
            foreach(var pair in extras.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value});
            b.AddGeneral(new("fixture:owner","对齐","supporter","fixture:driver","wei",12,mode=="dynamic"?["classic:zhengu","fixture:mutator"]:mode=="overlap"?["fixture:double"]:mode is "ai-owner" or "grant"?[]:["classic:zhengu"]));for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:target-{i}","目标","supporter","standard:none","wei",12,mode=="suppressed"?["fixture:suppress"]:mode=="overlap"?["fixture:observer"]:mode=="death-target"&&i==1?["fixture:death"]:mode=="death-source"&&i==1?["fixture:source-death"]:mode=="ai-owner"?["classic:zhengu"]:[]));
            b.AddDeck(new("fixture:alignment-deck","真实实体",3,2,[]){PhysicalCards=Enumerable.Range(0,160).Select(i=>new ContentDeckPhysicalCard("standard:dodge",Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-haozhao-fixture","对齐",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:alignment-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:owner","fixture:target-1","fixture:target-2","fixture:target-3"]));
        }
    }
}
