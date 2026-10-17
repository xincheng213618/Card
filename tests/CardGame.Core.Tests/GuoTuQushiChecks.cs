using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuoTuQushiChecks
{
    private const string Owner="fixture:qushi-owner",Mode="identity:classic-qushi",Driver="fixture:qushi-driver",Quiet="fixture:qushi-quiet",Conditional="fixture:qushi-conditional",Watch="fixture:qushi-watch",Legacy="fixture:qushi-legacy",Qushi="ol:qushi";
    private static string Scene="";private static int Count=4;
    public static void PrivateSealsNativeDrawPrivacyOncePerPlayAndCold()
    {
        var(g,r)=Start("privacy");var hand=V(g,0).HandCount;Use(g,"draw-seal",skill:Qushi);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
        Need(V(g,0).HandCount==hand+1&&E<QushiStartedEvent>(g).Length==1&&E<QushiSealPlacedEvent>(g).Length==0,"The genuine Draw1 happens before any sealed entity is selected.");g=Cold(g,r);Continue(g);Reach(g,()=>Branch(g,"card"));
        var prompt=P(g)!;var identity=prompt.Choices.First(c=>c.Cards.Count==1).Cards.Single();var unchanged=State(g);
        Need(Enumerable.Range(1,Count-1).All(s=>g.CreateSnapshot(s).PendingDecision is null)&&!g.Submit(new AnswerPromptCommand(1,prompt.PromptId,prompt.Choices[0].Id,g.Revision)).Accepted&&State(g)==unchanged,"The private original-Hand invoice cannot be viewed or answered by another seat.");g=Cold(g,r);Choose(g,"card",identity);Choose(g,"holder",seat:1);Play(g);
        var seal=E<QushiSealPlacedEvent>(g).Single().Seal;Need(seal.CardId==identity&&seal.HolderSeat==1&&g.CreateCardZoneDiagnostics().Single(e=>e.CardId==identity).Location==seal.Location,"The actual selected Hand entity is privately deposited on the other character.");
        var issuer=g.CreateSnapshot(0).Players[1].NamedAmbushPiles!.Single(p=>p.SkillId==Qushi);Need(issuer.Count==1&&issuer.Entries is[{Card.Id:var id}]&&id==identity&&Enumerable.Range(1,Count-1).All(s=>g.CreateSnapshot(s).Players[1].NamedAmbushPiles!.Single(p=>p.SkillId==Qushi)is{Count:1,Entries:null}),"Only the issuer sees seal identity; its holder and all other viewers see count only.");
        Need(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId==Qushi),"One actual Play cannot activate Qushi twice.");g=Cold(g,r);End(g);Reach(g,()=>E<QushiSealRemovedEvent>(g).Any(e=>e.SealId==seal.SealId));
        Need(E<QushiSealRemovedEvent>(g).Count(e=>e.SealId==seal.SealId)==1&&g.CardMovements.Single(m=>m.CardId==identity&&m.Reason.Value=="skill-program.guo-tu.qushi.remove").To==CardLocation.DiscardPile,"A real holder Ending removes precisely the issued character-zone entity once.");_=Cold(g,r);
    }
    public static void SameTypeAndAllUseMaximumTargetsRemainIndependent()
    {
        foreach(var scene in new[]{"all-types-five","different-type","deleted-foreign-target"})
        {
            var(g,r)=HumanHolder(scene);var seal=E<QushiSealPlacedEvent>(g).Last(e=>e.Seal.HolderSeat==0).Seal;Need(seal.Category==(scene=="deleted-foreign-target"?SkillProgramCardCategory.Basic:SkillProgramCardCategory.Equipment),"The real other issuer sealed the original current scene entity category.");
            if(scene=="all-types-five"){Native(g,a=>a.ConversionSource is null&&a.CardId is{}id&&V(g,0).Hand.Any(c=>c.Id==id&&c.Kind==CardKind.Crossbow));Play(g);NativeKind(g,CardKind.ArrowBarrage);Play(g);}
            else if(scene=="deleted-foreign-target")
            {Native(g,a=>a.ConversionSource is null&&a.CardId is{}id&&V(g,0).Hand.Any(c=>c.Id==id&&c.Kind==CardKind.Slash)&&a.TargetSeats.SequenceEqual([1]));Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="ol:pijing");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="remove"&&c.Targets.SequenceEqual([1]))==true);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="remove"&&c.Targets.SequenceEqual([1]));g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="finish");Play(g);Need(E<PijingTargetsAppliedEvent>(g).Any(e=>e.FinalCount==0),"A genuine Pijing use removes its only foreign target.");}
            else{NativeKind(g,CardKind.DrawTwo);Play(g);}
            var turn=g.State.TurnNumber;g=Cold(g,r);End(g);Reach(g,()=>E<QushiRewardIssuedEvent>(g).Any(e=>e.SealId==seal.SealId));var reward=E<QushiRewardIssuedEvent>(g).Single(e=>e.SealId==seal.SealId);
            Need(reward.ActualTurn==turn&&reward.HolderSeat==0&&(scene=="all-types-five"?reward.SameCategory&&reward.MaximumTargets==5&&reward.DrawCount==5:scene=="deleted-foreign-target"?reward.SameCategory&&reward.MaximumTargets==0&&reward.DrawCount==0:!reward.SameCategory&&reward.MaximumTargets==1&&reward.DrawCount==0),"Same-type qualification and maximum targets among ALL uses are independent: "+JsonSerializer.Serialize(reward));
            Reach(g,()=>E<QushiCompletedEvent>(g).Any(e=>e.FrameId==reward.FrameId));Need(g.CardMovements.Count(m=>E<QushiMovementPaidEvent>(g).Any(p=>p.FrameId==reward.FrameId&&p.Stage==QushiStage.RewardChildren&&m.Sequence>p.Before&&m.Sequence<=p.After)&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(seal.Source.OwnerSeat)&&m.Reason.Value=="skill-program.guo-tu.qushi.draw")==reward.DrawCount,"Only the issuer receives the bounded native reward, after exact seal removal.");_=Cold(g,r);
        }
    }
    public static void TrueResponsesPlayedAndLegacyUsesKeepExactTurnHistory()
    {
        foreach(var scene in new[]{"response","played","legacy"})
        {
            var(g,r)=HumanHolder(scene);var seal=E<QushiSealPlacedEvent>(g).Last(e=>e.Seal.HolderSeat==0).Seal;var turn=g.State.TurnNumber;
            if(scene=="legacy")
            {
                Need(P(g)?.SkillPrompt?.SkillId==Legacy,"The real legacy substitution is offered in its own turn-start window.");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>P(g)?.Choices.Any(c=>c.Targets.SequenceEqual([1]))==true);Answer(g,c=>c.Targets.SequenceEqual([1]));Play(g);
                Need(E<QushiActualUseEvent>(g).Any(e=>e.ActualTurn==turn&&e.ActorSeat==0&&!e.ResponseUse&&e.ActionId==0&&e.EffectiveKind==CardKind.Slash&&e.TargetCount==1),"A true legacy Action-null Slash occupies native use history even without a modern accepted action.");
            }
            else
            {
                Use(g,scene=="response"?"incoming":"incoming-duel",[1]);
                if(scene=="response"){Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("offer-option")?.StartsWith("target-",StringComparison.Ordinal)==true)==true);Answer(g,c=>c.Targets.SequenceEqual([0]));Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});}
                else Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondSlash});
                g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);NativeKind(g,CardKind.DrawTwo);Play(g);
                Need(E<CardRespondedEvent>(g).Any(e=>e.ResponderSeat==0)&&E<QushiActualUseEvent>(g).Any(e=>e.ActualTurn==turn&&e.ActorSeat==0&&e.ResponseUse&&e.EffectiveKind==CardKind.Dodge)==(scene=="response"),"Dodge against a real Slash is a use; a Duel Slash is only played and never manufactures a basic use.");
            }
            g=Cold(g,r);End(g);Reach(g,()=>E<QushiRewardIssuedEvent>(g).Any(e=>e.SealId==seal.SealId));var due=E<QushiRewardIssuedEvent>(g).Single(e=>e.SealId==seal.SealId);
            Need(due.SameCategory==(scene!="played")&&due.MaximumTargets==1&&due.DrawCount==(scene=="played"?0:1),"Actual-turn native Use, pure played and implicit self targets retain their exact independent boundaries: "+JsonSerializer.Serialize(due));_=Cold(g,r);
        }
    }
    public static void IssuedSealSurvivesSourceLossAndNativeDeathChildren()
    {
        foreach(var scene in new[]{"paid-loss","dead-issuer","dead-holder"})
        {
            var(g,r)=Start(scene);Use(g,"draw-seal",skill:Qushi);Reach(g,()=>Branch(g,"card"));Choose(g,"card");Choose(g,"holder",seat:1);
            if(scene=="paid-loss"){Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);Need(E<QushiSealPlacedEvent>(g).Length==1,"The exact seal is physically paid before its native Hand-loss child.");g=Cold(g,r);Continue(g);}
            Play(g);var seal=E<QushiSealPlacedEvent>(g).Single().Seal;
            if(scene=="paid-loss")Need(!V(g,0).Skills!.Any(s=>s.Id==Qushi)&&g.CreateCardZoneDiagnostics().Single(e=>e.CardId==seal.CardId).Location==seal.Location,"Paid seal survives actual conditional source loss and drains its real HP/skill children.");
            if(scene=="dead-issuer"){Use(g,"die");Reach(g,()=>!V(g,0).IsAlive);Need(g.State.Winner==Winner.None,"A genuine non-lord issuer death preserves the live game and issued seal.");}
            if(scene=="dead-holder"){Use(g,"kill",[1]);Play(g);Need(!V(g,1).IsAlive&&E<QushiSealRemovedEvent>(g).Single(e=>e.SealId==seal.SealId)is{HolderDeath:true}&&g.CardMovements.Any(m=>m.CardId==seal.CardId&&m.From==seal.Location&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.guo-tu.qushi.holder-death"),"Holder death cleans the exact private character-zone entity through native death cleanup.");_=Cold(g,r);continue;}
            g=Cold(g,r);if(V(g,0).IsAlive)End(g);Reach(g,()=>E<QushiSealRemovedEvent>(g).Any(e=>e.SealId==seal.SealId));
            Need(E<QushiSealRemovedEvent>(g).Count(e=>e.SealId==seal.SealId)==1&&E<QushiStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.ResolveQushiIssuedHolderEnding)==1,"Loss/death of the original source cannot cancel or duplicate the mandatory issued removal.");if(scene=="dead-issuer")Need(!g.CardMovements.Any(m=>m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.guo-tu.qushi.draw"&&m.Sequence>seal.MovementSequence),"A dead issuer never draws a later issued reward.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,Count).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static bool Branch(GameEngine g,string branch)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="qushi"&&c.Parameters.GetValueOrDefault("branch")==branch)==true;
    private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message+" Scene="+Scene);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual Qushi command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;var c=p.Choices.FirstOrDefault(pick);Need(p.PlayerSeat==0&&c is not null,"Missing real human selection: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
    private static void Choose(GameEngine g,string branch,int? card=null,int? seat=null)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")==branch&&(card is null||c.Cards.SequenceEqual([card.Value]))&&(seat is null||c.Targets.SequenceEqual([seat.Value])));
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]? targets=null,string skill=Driver)=>Accept(g,new UseProgramSkillCommand(0,skill,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> pick){var a=g.GetHumanLegalActions().First(pick);Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});}
    private static void NativeKind(GameEngine g,CardKind kind)=>Native(g,a=>a.ConversionSource?.SkillId==Driver&&a.PlayedCardKind==kind);
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string method="",[CallerLineNumber]int line=0)
    {
        for(var i=0;i<650;i++){if(done())return;Need(g.State.Winner==Winner.None,"Unexpected game completion: "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0)Accept(g,new AdvanceOneStepCommand(g.Revision));
            else if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
            else if(p.SkillPrompt?.SkillId==Watch)Continue(g);
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else throw new InvalidOperationException($"Unexpected Qushi prompt {method}:{line}: "+Diagnostic(g));}
        throw new InvalidOperationException($"Missing Qushi boundary {method}:{line}: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{Scene,g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(4).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Seals=E<QushiSealPlacedEvent>(g),Rewards=E<QushiRewardIssuedEvent>(g),Frames=g.ResolutionStack.TakeLast(6).Select(f=>new{f.Id,Type=f.GetType().Name,Qushi=(f as ProgramSkillFrame)?.Qushi?.Stage})});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,Count).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(cold),"Cold replay preserves private seal entities, exact native children and true-use target history.");return cold;}
    private static(GameEngine,ContentRegistry) HumanHolder(string scene)
    {
        var(g,r)=Start(scene);End(g);Reach(g,()=>E<QushiSealPlacedEvent>(g).Any(e=>e.Seal.HolderSeat==0)&&P(g)is{PlayerSeat:0}p&&(scene=="legacy"?p.SkillPrompt?.SkillId==Legacy:p.Kind==DecisionKind.PlayCard));return(g,r);
    }
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        Scene=scene;Count=scene=="all-types-five"?6:4;var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene,Count));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=Count,HumanSeat=0,HumanRole=scene=="dead-issuer"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private static void RegisterOnlyActualQushi(IContentRegistryBuilder b)
    {
        // Retain the actual production program while excluding the unrelated Weijie
        // catalog capability which deliberately modernizes old virtual-use actions.
        var assembly=typeof(StandardContentPackage).Assembly;
        string Read(string suffix){var name=assembly.GetManifestResourceNames().Single(n=>n.EndsWith(suffix,StringComparison.Ordinal));using var stream=assembly.GetManifestResourceStream(name)!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
        var catalog=SkillProgramCatalog.Load(Read("ol-guo-tu.rules.json"),Read("ol-guo-tu.presentation.json"));var presentation=catalog.Presentations[Qushi];
        b.AddSkill(new(Qushi,presentation.Name,presentation.Description){Program=catalog.Programs[Qushi],ProgramPresentation=presentation});
    }
    private sealed class Fixture(string scene,int count):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-qushi",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            if(scene=="legacy")RegisterOnlyActualQushi(b);else CharacterFixtureContent.Register(b,"ol:guo-tu");if(scene=="deleted-foreign-target")CharacterFixtureContent.Register(b,"ol:liu-pan");var defs=new List<JsonNode>();
            void Skill(string id,string body)=>defs.Add(JsonNode.Parse("{\"id\":\""+id+"\",\"revision\":1,"+body+"}")!);
            string A(string id,string effect,int targets=0,string more="")=>"{\"id\":\""+id+"\",\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"otherLiving\",\"usesPerTurn\":null,"+more+"\"effects\":["+effect+"]}";
            var activations=new[]{A("die","{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}"),A("kill","{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}",1),A("incoming","{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}",1),A("incoming-duel","{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}",1,"\"usesPerPhase\":2,")};
            var conversions=new[]{"drawTwo","arrowBarrage","dodge"}.Select((kind,i)=>"{\"id\":\""+kind+"\",\"inputKinds\":[],\"inputSuits\":[],\"inputCount\":1,\"sourceZones\":[\"hand\"],\"outputKind\":\""+kind+"\",\"forPlay\":"+(kind=="dodge"?"false":"true")+",\"forResponse\":"+(kind=="dodge"?"true":"false")+(kind=="arrowBarrage"?",\"usesPerPhase\":1":"")+(kind=="dodge"?"":",\"singleCardTrickUse\":true")+"}");
            Skill(Driver,"\"modifiers\":[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":50,\"priority\":0}],\"activations\":["+string.Join(',',activations)+"],\"viewAs\":["+string.Join(',',conversions)+"]");
            Skill(Quiet,"\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]");
            if(scene=="paid-loss")Skill(Conditional,"\"hpSkillGrants\":[{\"id\":\"healthy\",\"skillId\":\"ol:qushi\",\"minimumHp\":9}]");
            if(scene is"privacy"or"paid-loss")Skill(Watch,"\"triggers\":[{\"id\":\"true-paid-child\",\"window\":\""+(scene=="privacy"?"cardsGained":"cardsMoved")+"\",\"subject\":\"owner\",\""+(scene=="privacy"?"destinationZones":"sourceZones")+"\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.guo-tu.qushi."+(scene=="privacy"?"draw":"seal")+"\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"paid\",\"options\":[{\"id\":\"continue\"}]}"+(scene=="paid-loss"?",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}":"")+"]}]");
            if(scene=="legacy")Skill(Legacy,"\"triggers\":[{\"id\":\"phase-substitution\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":true,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingVirtualSlashTarget\"},{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"judgment\",\"draw\"]},{\"op\":\"useVirtualCard\",\"target\":\"selectedTarget\",\"outputKind\":\"slash\",\"targetRestriction\":\"distanceUnlimitedAgainstTarget\"}]}]");
            var labels=defs.ToDictionary(n=>n["id"]!.GetValue<string>(),n=>new{name=n["id"]!.GetValue<string>(),description="真实趋势原生边界夹具",optionLabels=n["id"]!.GetValue<string>()==Watch?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()});
            var programs=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=defs}),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in programs.Programs)b.AddSkill(new(id,id,"真实趋势边界"){Program=p,ProgramPresentation=programs.Presentations[id]});
            var local=scene is"privacy"or"paid-loss"or"dead-issuer"or"dead-holder";var skills=new List<string>{Driver};if(scene=="deleted-foreign-target")skills.Add("ol:pijing");if(local)skills.Add(scene=="paid-loss"?Conditional:Qushi);if(scene is"privacy"or"paid-loss")skills.Add(Watch);if(scene=="legacy")skills.Add(Legacy);
            b.AddSkill(new("fixture:qushi-owner-pick","本人","固定选择"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-100000000d)});b.AddGeneral(new(Owner,"真实本人","ol-guo-tu","fixture:qushi-owner-pick","qun",scene=="paid-loss"?8:16,skills));
            var peers=Enumerable.Range(1,count-1).Select(i=>"fixture:qushi-peer-"+i).ToArray();foreach(var(id,i)in peers.Select((id,i)=>(id,i))){var pick="fixture:qushi-pick-"+i;b.AddSkill(new(pick,"其他角色","固定次序"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>1000000d*(count-i))});b.AddGeneral(new(id,"真实其他角色","supporter",pick,"wei",16,!local&&i==0?[Qushi]:[Quiet]));}
            var kind=scene is"response"or"played"or"legacy"or"deleted-foreign-target"?CardKind.Slash:CardKind.Crossbow;b.AddCard(new("fixture:qushi-card","原生实体","真实原生牌","固定实体",kind));b.AddDeck(new("fixture:qushi-deck","真实小牌堆",count,2,[]){PhysicalCards=Enumerable.Range(0,120).Select(i=>new ContentDeckPhysicalCard("fixture:qushi-card",new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond}[i%4],i%13+1)).ToArray()});var roles=new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),count-1}};if(scene=="dead-issuer"){roles[nameof(Role.Rebel)]=count-2;roles[nameof(Role.Renegade)]=1;}b.AddMode(new(Mode,"真实趋势模式",count,count,roles,"fixture:qushi-deck",GeneralCandidateCount:count,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
