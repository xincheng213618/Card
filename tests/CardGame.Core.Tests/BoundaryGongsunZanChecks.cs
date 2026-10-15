using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;
internal static class BoundaryGongsunZanChecks
{
    private const string Skill="boundary:qiaomeng-current";
    private static readonly Lazy<ContentRegistry> Classic=new(()=>ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage()));
    public static void RealZonesAndInputs()
    {
        foreach(var weapon in new[]{"standard:offensive_horse","standard:defensive_horse"})
        {
            var(g,r)=Start(equipment:weapon);Driver(g,"equip",[1]);Settle(g);var mount=g.State.Players[1].Equipment.Single();Slash(g);Offer(g);Cold(g,r);Activate(g);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="damage-target-mount"));
            var prompt=P(g)!;Require(prompt.Choices.Any(c=>c.Parameters["source-zone"]=="Hand"&&c.Cards.Count==0)&&prompt.Choices.Any(c=>c.Parameters["source-zone"]=="Equipment"&&c.Cards.Contains(mount.Id)),"HE opaque hand slots/public equipment identity are mature UI choices.");
            var before=GameCheckpointJson.Serialize(g.CreateCheckpoint());var result=g.Submit(new AnswerPromptCommand(0,prompt.PromptId,new ChoiceId("foreign-payment"),g.Revision));Require(!result.Accepted&&before==GameCheckpointJson.Serialize(g.CreateCheckpoint()),"Foreign payment rejection is atomic.");
            Evidence(g,"opaque-equipment-"+mount.Kind);Cold(g,r);Answer(g,c=>c.Cards.Contains(mount.Id));Settle(g);
            Require(g.State.Players[0].Hand.Any(c=>c.Id==mount.Id)&&Moves(g,mount.Id).Count(m=>m.Reason.Value=="skill-program.damage-target-mount.discard")==1&&Moves(g,mount.Id).Count(m=>m.Reason.Value=="skill-program.damage-target-mount.claim")==1,"Both printed mount slots are discarded and gained once.");Cold(g,r);
        }
        var(h,hr)=Start();Driver(h,"equip",[0]);Settle(h);var id=h.State.Players[0].Equipment.Single().Id;Driver(h,"give-board",[1]);Reach(h,p=>p.Choices.Any(c=>c.Cards.Contains(id)));Answer(h,c=>c.Cards.Contains(id));Settle(h);
        Slash(h);Offer(h);Activate(h);Reach(h,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="damage-target-mount"));var slot=h.CreateSnapshot(1).Players[1].Hand.Select((c,i)=>(c,i)).Single(x=>x.c.Id==id).i;
        Require(P(h)!.Choices.Single(c=>c.Parameters["source-zone"]=="Hand"&&c.Parameters["slot-index"]==slot.ToString()).Cards.Count==0,"A printed hand mount is not revealed before paying its opaque slot.");Cold(h,hr);Answer(h,c=>c.Parameters["source-zone"]=="Hand"&&c.Parameters["slot-index"]==slot.ToString());Settle(h);Require(h.State.Players[0].Hand.Any(c=>c.Id==id),"Printed Hand mount qualifies despite no equipped mount slot.");Cold(h,hr);
        var(j,jr)=Start(judgment:true);var indulgence=j.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Indulgence&&a.TargetSeat==1);Play(j,indulgence);Settle(j);var judgment=j.State.Players[1].Judgment.Single();Slash(j);Offer(j);Activate(j);Reach(j,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="damage-target-mount"));Cold(j,jr);Answer(j,c=>c.Cards.Contains(judgment.Id));Settle(j);Require(!j.State.Players[1].Judgment.Any()&&!j.State.Players[0].Hand.Any(c=>c.Id==judgment.Id)&&Moves(j,judgment.Id).Any(m=>m.From.Zone==CardZoneKind.Judgment&&m.To==CardLocation.DiscardPile),"Actual public Judgment card discards without nonmount claim.");Cold(j,jr);
    }
    public static void RealDamageProvenance()
    {
        var(g,r)=Start();Driver(g,"cardless",[1]);Settle(g);Require(!g.Events.Select(e=>e.Payload).OfType<ProgramSkillStartedEvent>().Any(e=>e.SkillId==Skill),"Real program cardless damage does not offer a mount discard.");Cold(g,r);
        var(z,zr)=Start();Driver(z,"virtual",[1]);Offer(z);Cold(z,zr);Activate(z);Reach(z,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="damage-target-mount"));Answer(z,c=>c.Parameters["source-zone"]=="Hand");Settle(z);
        Require(z.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.Type==CardActionType.Use&&e.Action.EffectiveKind==CardKind.Slash&&e.Action.PhysicalCards.Count==0)&&z.CardMovements.Any(m=>m.Reason.Value=="skill-program.damage-target-mount.discard"),"Real zero-entity virtual Slash Use qualifies without fabricated physical cost.");Cold(z,zr);Evidence(z,"actual-zero-entity-use");
        var(s,sr)=Start();Slash(s);Offer(s);Answer(s,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");Settle(s);Require(!s.CardMovements.Any(m=>m.Reason.Value.StartsWith("skill-program.damage-target-mount")),"Optional skip creates no payment or claim.");Cold(s,sr);
        var(c,cr)=Start(fire:true);Driver(c,"chain",[1]);Settle(c);Driver(c,"chain",[2]);Settle(c);Slash(c);Finish(c);
        Require(c.Events.Select(e=>e.Payload).OfType<ProgramBindingResolvedEvent>().Count(e=>e.SkillId==Skill)>=2,"Real elemental chain keeps owning Slash Use/source and independently offers each damaged actual victim.");Cold(c,cr);
        var(self,selfr)=Start(fire:true);Driver(self,"equip",[0]);Settle(self);var ownMount=self.State.Players[0].Equipment.Single().Id;
        Driver(self,"chain",[0]);Settle(self);Driver(self,"chain",[1]);Settle(self);Slash(self);Offer(self);Answer(self,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        Reach(self,p=>p.SkillPrompt?.SkillId==Skill&&self.ResolutionStack.OfType<DamageTriggerWindowFrame>().LastOrDefault()?.TargetSeat==0);Cold(self,selfr);Activate(self);Reach(self,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="damage-target-mount"));
        Require(P(self)!.Choices.Any(c=>c.Cards.Contains(ownMount)&&c.Targets.SequenceEqual([0])),"A real self-source elemental chain exposes only this opt-in's same-owner HEJ discard.");Cold(self,selfr);Answer(self,c=>c.Cards.Contains(ownMount));Settle(self);
        Require(self.State.Players[0].Hand.Any(c=>c.Id==ownMount)&&Moves(self,ownMount).Count(m=>m.From==CardLocation.Equipment(0)&&m.To==CardLocation.DiscardPile)==1,"Actual self victim pays its own mount once and gains that exact entity.");Cold(self,selfr);Evidence(self,"actual-self-source-chain");
        var(v,vr)=Start(judgment:true);Slash(v);Finish(v);Require(v.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.ActorSeat==0&&e.Action.Type==CardActionType.Use&&e.Action.EffectiveKind==CardKind.Slash&&e.Action.ConversionChain.Count>0),"Real red Indulgence converted by Wusheng remains actual Slash Use.");Cold(v,vr);
    }
    private static IReadOnlyList<CharacterState> Players(GameEngine g)=>(IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(g)!;
    private static IEnumerable<CardMovementRecord> Moves(GameEngine g,int id)=>g.CardMovements.Where(m=>m.CardId==id);
    private static void Cold(GameEngine g,ContentRegistry r){ReplayFourViews(g,r);var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(JsonSerializer.Serialize(g.CreateSnapshot(-1))==JsonSerializer.Serialize(cold.CreateSnapshot(-1)),"Spectator cold projection matches.");foreach(var v in Enumerable.Range(0,4))foreach(var p in g.CreateSnapshot(v).Players)if(p.Seat!=v)Require(p.Hand.Count==0,"Other players' hand entities remain private.");}
    private static PendingDecision? P(GameEngine g)=>g.CreateSnapshot(0).PendingDecision??g.CreateSnapshot(1).PendingDecision??g.CreateSnapshot(2).PendingDecision??g.CreateSnapshot(3).PendingDecision;
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Driver(GameEngine g,string id,int[]?targets=null)=>Accept(g,new UseProgramSkillCommand(0,"fixture:gz-driver",id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats.Count>0?a.TargetSeats:[a.TargetSeat!.Value],g.Revision,P(g)!.PromptId,a.PlayedCardKind){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});
    private static void Slash(GameEngine g)=>Play(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeat==1));
    private static void Offer(GameEngine g)=>Reach(g,p=>p.SkillPrompt?.SkillId==Skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate){for(var i=0;i<100;i++){if(P(g)is{}p&&predicate(p))return;Step(g);}throw new InvalidOperationException("Fixed fixture did not reach prompt: "+P(g)?.Prompt);}
    private static void Settle(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    private static void Finish(GameEngine g){for(var i=0;i<150;i++){if(g.State.Status==EngineStatus.Completed||P(g)is{Kind:DecisionKind.PlayCard,PlayerSeat:0})return;Step(g);}throw new InvalidOperationException("Finite fixture did not settle.");}
    private static void Step(GameEngine g)
    {
        var p=P(g);
        if(p is{Kind:DecisionKind.ProgramTrigger,PlayerSeat:0})
        {
            if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("advanced-value")==Skill))Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")==Skill);
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish"))Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish");
            else Answer(g,_=>true);
        }
        else if(p is{Kind:DecisionKind.DiscardCards,PlayerSeat:0})Accept(g,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));
        else if(p is{Kind:DecisionKind.RescueDying,PlayerSeat:0})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="skip");
        else Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static int Attempt;
    private static void Accept(GameEngine g,GameCommand cmd)
    {var before=GameCheckpointJson.Serialize(g.CreateCheckpoint());try{BoundaryLiDianChecks.Accept(g,cmd);}catch(Exception ex){if(Environment.GetEnvironmentVariable("CARD_GZ_EVIDENCE")is{Length:>0}root){var d=Path.Combine(root,"submit-failure-"+Interlocked.Increment(ref Attempt).ToString("D3"));Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,"checkpoint-before.json"),before);File.WriteAllText(Path.Combine(d,"attempt.json"),JsonSerializer.Serialize(cmd,cmd.GetType()));File.WriteAllText(Path.Combine(d,"faulted-stack.json"),JsonSerializer.Serialize(g.ResolutionStack));File.WriteAllText(Path.Combine(d,"exception.txt"),ex.ToString());}throw;}}
    private static void Evidence(GameEngine g,string label){if(Environment.GetEnvironmentVariable("CARD_GZ_EVIDENCE")is not{Length:>0}root)return;var d=Path.Combine(root,label);Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,"checkpoint.json"),GameCheckpointJson.Serialize(g.CreateCheckpoint()));File.WriteAllText(Path.Combine(d,"trusted-stack.json"),JsonSerializer.Serialize(g.ResolutionStack));for(var i=-1;i<4;i++)File.WriteAllText(Path.Combine(d,"view-"+i+".json"),JsonSerializer.Serialize(g.CreateSnapshot(i)));}
    private static (GameEngine,ContentRegistry) Start(string equipment="standard:defensive_horse",bool judgment=false,string? child=null,bool fire=false,bool native=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(equipment,judgment,child,fire,native));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=native?3:0,HumanRole=native?Role.Rebel:Role.Lord,ModeId="identity:classic-gz",UseInteractiveSetup=!native,AdvanceAfterHumanCommands=false,UseInteractiveDiscard=false,MaxTurns=5},r);Accept(g,new StartGameCommand());if(!native){Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:gz",g.Revision,P(g)!.PromptId));Settle(g);}return(g,r);
    }
    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}","{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];
    private static string Act(string id,string effects,int targets=0)=>"{\"id\":\""+id+"\",\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":"+effects+"}";
    private sealed class Fixture(string equipment,bool judgment,string?child,bool fire,bool native):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-gz",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            try{typeof(StandardClassicGeneralPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryGongsunZanContent")!.GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[b]);}catch(TargetInvocationException e){throw new InvalidOperationException(e.InnerException?.ToString(),e.InnerException);}
            b.AddSkill(Classic.Value.GetSkill("classic:wusheng"));
            var acts=Act("equip","[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"equipment\"}]",1)+","+Act("hurt","[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]")+","+Act("cardless","[{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}]",1)+","+Act("virtual","[{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}]",1)+","+Act("chain","[{\"op\":\"setChainedState\",\"target\":\"selectedTarget\",\"chained\":true}]",1)+","+Act("give-board","[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"equipment\"],\"count\":1,\"destination\":\"selectedTargetHand\",\"targetRef\":{\"kind\":\"selectedTarget\"},\"awaitMovementTriggers\":true}]",1);
            b.AddSkill(new("fixture:gz-driver","实际准备","实际准备"){Program=Load("fixture:gz-driver","\"activations\":["+acts+"]")});
            if(child!=null)
            {
                var effect=child=="victim-death"?"{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}":child=="gain-draw"?"{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}":child=="rescue"?"{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":5}":child=="gain-loss"?"{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}":child=="steal"?"{\"op\":\"claimMovedCards\",\"target\":\"owner\"}":child=="loss"?"{\"op\":\"replaceSkillsOnAwakening\",\"target\":\"owner\",\"skillIds\":[\"standard:none\"]}":"{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}";
                var gain=child.StartsWith("gain-");
                b.AddSkill(new("fixture:gz-child","实际移动子窗","实际移动子窗"){Program=Load("fixture:gz-child","\"triggers\":[{\"id\":\"child\",\"window\":\""+(gain?"cardsGained":"discardPileReceived")+"\",\"subject\":\"owner\","+(gain?"\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",":"")+"\"movementReasons\":[\"skill-program.damage-target-mount."+(gain?"claim":"discard")+"\"],\"usageScope\":\"turn\",\"usageLimit\":1,\"optional\":true,\"effects\":["+effect+"]}]")});
                if(child=="rescue")b.AddSkill(new("fixture:gz-hp","实际桃回复观察","实际桃回复观察"){Program=Load("fixture:gz-hp","\"triggers\":[{\"id\":\"hp\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":true,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]")});
                if(child=="loss")for(var i=0;i<6;i++)b.AddSkill(new("fixture:gz-dummy"+i,"拥有技能"+i,"拥有技能"));
            }
            var extra=new[]{"boundary:yicong-current"}.Concat(!native?["fixture:gz-driver"]:Array.Empty<string>()).Concat(judgment||child=="rescue"?["classic:wusheng"]:Array.Empty<string>()).Concat(child is"loss"or"death"or"gain-draw"or"gain-loss"or"victim-death"or"rescue"?["fixture:gz-child"]:Array.Empty<string>()).Concat(child=="rescue"?["fixture:gz-hp"]:Array.Empty<string>()).Concat(child=="loss"?Enumerable.Range(0,6).Select(i=>"fixture:gz-dummy"+i):Array.Empty<string>()).ToArray();
            b.AddGeneral(new("fixture:gz","公孙瓒","supporter",Skill,"qun",4,extra));
            for(var i=1;i<4;i++)b.AddGeneral(new("fixture:gz-"+i,"固定目标"+i,"supporter",native?Skill:child=="steal"&&i==2?"fixture:gz-child":"standard:none","wei",5));
            b.AddDeck(new("fixture:gz-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard(child=="rescue"?"standard:peach":judgment?"standard:indulgence":fire?"standard:fire_slash":"standard:slash",Suit.Heart,7)).Concat(Enumerable.Range(0,8).Select(i=>new ContentDeckPhysicalCard(equipment,Suit.Club,4))).ToArray()});
            b.AddMode(new("identity:classic-gz","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:gz-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:gz","fixture:gz-1","fixture:gz-2","fixture:gz-3"]));
        }
    }
}
