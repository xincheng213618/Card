using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2017XuShiChecks
{
    public static void ForeignEquipmentGiftAndAiReplay()
    {
        var (g,r)=Start("exchange",false);
        var equipment=g.GetHumanLegalActions().FirstOrDefault(a=>a.Kind==LegalActionKind.Equip);
        if(equipment is null){Driver(g,"supply");Reach(g,p=>p.Kind==DecisionKind.PlayCard);equipment=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);}
        Accept(g,new PlayCardCommand(0,equipment.CardId!.Value,equipment.TargetSeats,g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        var a=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="classic:wengua");var owner=a.ProgramSkillOwnerSeat!.Value;
        Require(a.SelectableCardIds.Contains(equipment.CardId.Value),"Foreign entry must accept a real equipped entity.");
        Reject(g,new UseProgramSkillCommand(0,a.ProgramSkillId!,a.ProgramActivationId!,[equipment.CardId.Value],[0],g.Revision,P(g)!.PromptId){SkillOwnerSeat=owner});
        Use(g,a,equipment.CardId.Value);
        Require(g.CardMovements.Any(m=>m.CardId==equipment.CardId&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.Hand(owner)&&m.Reason.Value=="program.deck-end.gift"),"Real equipped card must enter recipient hand before its exchange choice.");Replay(g,r);
        Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
        var draws=g.CardMovements.Where(m=>m.Reason.Value=="program.deck-end.draw").ToArray();
        Require(draws.Length==2&&draws.Select(m=>m.To.OwnerSeat).SequenceEqual(new int?[]{owner,0}),"Foreign exchange waits and draws once for owner then provider.");
        Require(g.GetHumanLegalActions().All(a=>a.ProgramSkillId!="classic:wengua")&&!g.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat!=0),"Provider use is consumed and AI makes its real endpoint and gain decisions.");Replay(g,r);Conserve(g,64);
    }
    private static(GameEngine,ContentRegistry)Start(string mode,bool self)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-deck-check",UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral&&p.PlayerSeat==0);
        Accept(g,new SelectGeneralCommand(0,self?"fixture:deck-owner":"fixture:deck-provider",g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);return(g,r);
    }
    private static IReadOnlyList<CardZoneDiagnostic>Zones(GameEngine g)=>g.CreateCardZoneDiagnostics();
    private static PendingDecision?P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(seat=>g.CreateSnapshot(seat,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Driver(GameEngine g,string id)=>Accept(g,new UseProgramSkillCommand(0,"fixture:deck-driver",id,[],[],g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,LegalAction a,int card)=>Accept(g,new UseProgramSkillCommand(0,a.ProgramSkillId!,a.ProgramActivationId!,[card],a.ProgramSkillOwnerSeat is null?[]:a.TargetSeats,g.Revision,P(g)!.PromptId){SkillOwnerSeat=a.ProgramSkillOwnerSeat});
    private static void Choose(GameEngine g,Func<PromptChoice,bool>match)=>Accept(g,new AnswerPromptCommand(P(g)!.PlayerSeat,P(g)!.PromptId,P(g)!.Choices.First(match).Id,g.Revision));
    private static void Tick(GameEngine g)
    {
        if(P(g) is {Kind:DecisionKind.RespondDodge,PlayerSeat:0})Choose(g,c=>c.Cards.Count==0);
        else if(P(g) is {Kind:DecisionKind.DiscardCards}d)Accept(g,new DiscardCardsCommand(d.PlayerSeat,d.ValidCardIds.Take(d.RequiredCardCount).ToArray(),d.PromptId,g.Revision));
        else if(P(g) is {Kind:DecisionKind.ProgramTrigger,PlayerSeat:0}p)Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate"||c.Parameters.GetValueOrDefault("option-id")=="continue"||c.Parameters.GetValueOrDefault("end")=="decline");
        else Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reach(GameEngine g,Func<PendingDecision,bool>match){for(var i=0;i<180;i++){if(P(g) is {}p&&match(p))return;Tick(g);}throw new InvalidOperationException("Missing deterministic boundary: "+P(g));}
    private static void Replay(GameEngine g,ContentRegistry r)
    {
        var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);
        Require(Enumerable.Range(0,4).All(seat=>SnapshotJson.Serialize(g.CreateSnapshot(seat,false))==SnapshotJson.Serialize(copy.CreateSnapshot(seat,false)))&&g.CardMovements.SequenceEqual(copy.CardMovements)&&g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).SequenceEqual(copy.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))),"All observer views, ordered entity movements, RNG-dependent draws and events must replay.");
    }
    private static void Reject(GameEngine g,GameCommand command){var before=GameCheckpointJson.Serialize(g.CreateCheckpoint());Require(!g.Submit(command).Accepted&&before==GameCheckpointJson.Serialize(g.CreateCheckpoint()),"Unpublished/duplicate input must reject atomically.");}
    private static void Accept(GameEngine g,GameCommand command){var round=CommandJson.Deserialize(CommandJson.Serialize([command])).Single();var result=g.Submit(round);Require(result.Accepted,"Fixture rejected: "+result.Error?.Message);}
    private static void Conserve(GameEngine g,int total)=>Require(Zones(g).Count==total&&Zones(g).Select(z=>z.CardId).Distinct().Count()==total,"All physical entities retain exactly one location.");
    private static void Require(bool c,string m){if(!c)throw new InvalidOperationException(m);}
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-xushi",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var driverActs=new List<object>{new{id="supply",minCards=0,maxCards=0,minTargets=0,maxTargets=0,targetKind="anyLiving",usesPerTurn=(int?)1,effects=new[]{new{op="draw",target="owner",amount=8}}}};
            if(mode=="cap-dead")driverActs.Add(new{id="kill",minCards=0,maxCards=0,minTargets=1,maxTargets=1,targetKind="otherLiving",usesPerTurn=1,effects=new[]{new{op="loseHp",target="selectedTarget",amount=20}}});
            var programs=new List<object>{new{id="fixture:deck-driver",revision=1,activations=driverActs}};
            programs.Add(new{id="fixture:gain-pause",revision=1,triggers=new[]{new{id="gain",window="cardsGained",subject="owner",destinationZones=new[]{"hand"},movementOccurrence="perBatch",movementReasons=new[]{"program.deck-end.draw"},optional=false,effects=new[]{new{op="chooseOption",target="owner",resultBind="gain",options=new[]{new{id="continue"}}}}}}});
            programs.Add(new{id="fixture:target-pause",revision=1,triggers=new[]{new{id="target",window="cardUseTargetsFinalized",ownerRelation="target",cardKinds=new[]{"slash","fireSlash","thunderSlash"},optional=false,effects=new[]{new{op="chooseOption",target="owner",resultBind="target",options=new[]{new{id="continue"}}}}}}});
            programs.Add(new{id="fixture:complete-pause",revision=1,triggers=new[]{new{id="complete",window="cardUseCompleted",ownerRelation="actor",cardKinds=new[]{"slash","fireSlash","thunderSlash"},optional=false,effects=mode=="replenish"?new object[]{new{op="selectOwnedCards",target="owner",amount=1,zones=new[]{"hand"},cardKinds=new[]{"slash"},resultBind="inserted"},new{op="moveBoundCards",target="owner",sourceBind="inserted",destination="drawPileTop",awaitMovementTriggers=true},new{op="chooseOption",target="owner",resultBind="complete",options=new[]{new{id="continue"}}}}:mode is "source-death" or "source-suppressed"?new object[]{new{op="loseHp",target="owner",amount=mode=="source-death"?20:1}}:new object[]{new{op="chooseOption",target="owner",resultBind="complete",options=new[]{new{id="continue"}}}}}}});
            programs.Add(new{id="fixture:gift-removal",revision=1,triggers=new[]{new{id="remove",window="cardsGained",subject="owner",destinationZones=new[]{"hand"},movementOccurrence="perBatch",movementReasons=new[]{"program.deck-end.gift"},optional=false,effects=new object[]{new{op="discardOwnedZoneCards",target="owner",zones=new[]{"hand"}},new{op="chooseOption",target="owner",resultBind="removed",options=new[]{new{id="continue"}}}}}}});
            programs.Add(new{id="fixture:return-slash",revision=1,triggers=new[]{new{id="return",window="afterDamageApplied",subject="owner",damageOccurrence="perDamage",optional=false,effects=new object[]{new{op="claimDamageCards",target="owner"},new{op="selectOwnedCards",target="owner",amount=1,zones=new[]{"hand"},cardKinds=new[]{"slash","fireSlash","thunderSlash"},resultBind="returned"},new{op="moveBoundCards",target="owner",sourceBind="returned",destination="drawPileTop",awaitMovementTriggers=true},new{op="chooseOption",target="owner",resultBind="return",options=new[]{new{id="continue"}}}}}}});
            programs.Add(new{id="fixture:empty-before-ending",revision=1,triggers=new[]{new{id="empty",window="playEnding",subject="owner",optional=false,effects=new[]{new{op="discardOwnedZoneCards",target="owner",zones=new[]{"hand"}}}}}});
            var presentation=new{schemaVersion=3,skills=programs.Select(p=>JsonSerializer.SerializeToElement(p).GetProperty("id").GetString()!).ToDictionary(id=>id,id=>(object)new{name=id,description="实际实体与暂停观察",optionLabels=id is "fixture:deck-driver" or "fixture:empty-before-ending" || id=="fixture:complete-pause" && mode is "source-death" or "source-suppressed" ? new Dictionary<string,string>() : new Dictionary<string,string>{{"continue","继续"}}})};
            var catalog=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=programs}),JsonSerializer.Serialize(presentation));
            foreach(var p in catalog.Programs)b.AddSkill(new(p.Key,p.Key,p.Key){Program=p.Value});
            b.AddSkill(new("fixture:hp-suppression","体力压制","在真实体力为19时压制其他武将技能"){SuppressionRule=new(19)});
            var exchange=mode is "exchange" or "remove-gift";
            b.AddGeneral(new("fixture:deck-owner","徐氏机制将","supporter",exchange?"classic:wengua":"classic:fuzhu","wu",mode=="threshold"?3:20,["fixture:gain-pause","fixture:complete-pause",..(mode=="source-suppressed"?new[]{"fixture:hp-suppression"}:Array.Empty<string>()),..(mode=="remove-gift"?new[]{"fixture:gift-removal"}:Array.Empty<string>())],GeneralGender.Female));
            b.AddGeneral(new("fixture:deck-provider","提供者","supporter","fixture:deck-driver","wei",mode is "game-over" or "source-loss"?1:20,["fixture:gain-pause","fixture:target-pause",..(mode=="returned"?new[]{"fixture:return-slash"}:Array.Empty<string>()),..(mode=="source-loss"?new[]{"classic:duanchang","fixture:empty-before-ending"}:Array.Empty<string>())],mode=="female"?GeneralGender.Female:GeneralGender.Male){InitialHp=mode is "game-over" or "source-loss"?1:null});
            for(var i=2;i<4;i++)b.AddGeneral(new($"fixture:deck-{i}","目标","supporter","standard:none","wei",20,[],GeneralGender.Male));
            b.AddDeck(new("fixture:deck","实际牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(exchange?i%4==0?"standard:qinggang_sword":"standard:dodge":mode=="no-slash"?"standard:dodge":i%6==0?"standard:slash":i%6==1?"standard:fire_slash":i%6==2?"standard:thunder_slash":"standard:dodge",(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-deck-check","牌堆机制",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:deck-owner","fixture:deck-provider","fixture:deck-2","fixture:deck-3"]));
        }
    }
}
