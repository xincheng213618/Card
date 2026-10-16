using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryCaoZhiChecks
{
    private const string Mode="identity:classic-boundary-cao-zhi-fixture";
    private const string Driver="fixture:co-driver", Gain="fixture:co-gain", Face="fixture:co-face", Committed="fixture:co-committed";
    public static void VirtualAlcoholWaitsForFaceChildrenAndUsesNoPhysicalEntity()
    {
        var (g,r)=Create(); var hand=g.State.Players[0].HandCount;
        Wine(g); Reach(g,p=>p.SkillPrompt?.SkillId==Face);
        var root=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="boundary:jiushi" && f.ProvenanceAlcohol is not null);
        Require(root.ProvenanceAlcohol is {Stage:ProgramProvenanceAlcoholStage.FaceCost,ChildFrameId:null} && g.State.Players[0].IsFaceDown && !Facts<AlcoholAppliedEvent>(g).Any(),"Actual face payment pauses before zero-entity Wine is issued.");
        Cold(g,r); Reject(g); Continue(g); Play(g);
        Require(g.State.Players[0].HandCount==hand && Facts<CardUseDeclaredEvent>(g).Single(e=>e.CardKind==CardKind.Alcohol).CardId==0 && Facts<AlcoholAppliedEvent>(g).Count()==1 && !g.CardMovements.Any(m=>m.Reason==CardMoveReasons.Use),"The face child returns to one actual Wine, with no physical card cost.");
        Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:jiushi"),"The back face does not offer a second unpaid activation."); Cold(g,r);
        Use(g,"incoming",targets:[1]); Reach(g,p=>HasActivation(p,"boundary:jiushi","flip-after-damage"));
        Cold(g,r); Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate"); Reach(g,p=>p.SkillPrompt?.SkillId==Face);
        Require(!g.State.Players[0].IsFaceDown && Facts<ProgramProvenanceFaceUpResetEvent>(g).Count()==1,"A true after-damage flip resets the outside segment at the actual face transition."); Cold(g,r); Continue(g); Play(g);
        Require(Facts<AlcoholAppliedEvent>(g).Count()==1,"Returning the damage/face children does not reissue the paid Wine."); Cold(g,r);

        var (dying,dr)=Create(fragile:true);
        Use(dying,"incoming",targets:[1]);
        Reach(dying,p=>p.SkillPrompt?.SkillId=="boundary:jiushi" && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="flip"));
        Cold(dying,dr); Answer(dying,c=>c.Parameters.GetValueOrDefault("option-id")=="flip"); Reach(dying,p=>p.SkillPrompt?.SkillId==Face);
        Require(dying.ResolutionStack.OfType<DyingFrame>().Any(f=>f.VictimSeat==0) && dying.State.Players[0].IsFaceDown,"Self-dying Wine owns its real face child before rescue."); Cold(dying,dr); Continue(dying); Play(dying);
        Require(dying.State.Players[0].IsAlive && dying.State.Players[0].Hp==1 && Facts<CardUseDeclaredEvent>(dying).Any(e=>e.CardKind==CardKind.Alcohol && e.CardId==0),"Existing zero-entity dying Alcohol recovers and returns to the actual damage producer."); Cold(dying,dr);
    }
    private static IEnumerable<T> Facts<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static bool HasActivation(PendingDecision p,string skill,string binding)=>p.PlayerSeat==0 && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("skill-id")==skill && c.Parameters.GetValueOrDefault("binding-id")==binding && c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Wine(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,"boundary:jiushi","virtual-alcohol",[],[],g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,string id,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate) { var p=P(g)!; Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision)); }
    private static void Play(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard && p.PlayerSeat==0);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate)
    { for(var i=0;i<260;i++) { if(P(g) is { } p && predicate(p)) return; Step(g); } throw new InvalidOperationException("Cao Zhi exact boundary missing: "+JsonSerializer.Serialize(P(g))); }
    private static void Until(GameEngine g,Func<bool> predicate)
    { for(var i=0;i<360;i++) { if(predicate()) return; Step(g); } throw new InvalidOperationException("Cao Zhi native/event boundary missing."); }
    private static void Step(GameEngine g)
    {
        var p=P(g);
        if(p is {PlayerSeat:0,Kind:DecisionKind.DiscardCards}) Accept(g,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));
        else if(p is {PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")) Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        else if(p is {PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="provenance-face-up")) Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="pass");
        else if(p is {PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")) Continue(g);
        else if(p is {PlayerSeat:0,Kind:DecisionKind.RescueDying}) Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
        else Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g,GameCommand command)
    { var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted,result.Error?.Message??"Rejected actual command."); }
    private static void Reject(GameEngine g) { var state=State(g); var p=P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new("unpublished"),g.Revision)).Accepted && State(g)==state,"Unpublished choice cannot repeat a paid entity or face cost."); }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new { Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g,ContentRegistry r)=>Require(State(g)==State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r)),"Four prepared views, private owning receipts, exact actual movement/history and real commands cold-restore identically.");
    private static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
    private static (GameEngine,ContentRegistry) Create(bool fragile=false,bool native=false,bool declaration=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(fragile,native,declaration));
        var g=GameEngine.CreateStandard(new GameOptions {Seed=31,PlayerCount=4,HumanSeat=native?-1:0,HumanRole=native?null:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=native?4:12},r);
        Accept(g,new StartGameCommand());
        if(native) Until(g,()=>g.State.Players.All(p=>p.GeneralId.StartsWith("fixture:co-",StringComparison.Ordinal)));
        else { Reach(g,p=>p.PlayerSeat==0 && p.Kind==DecisionKind.SelectGeneral); Accept(g,new SelectGeneralCommand(0,"fixture:co-owner",g.Revision,P(g)!.PromptId)); Play(g); }
        return(g,r);
    }
    private sealed class Fixture(bool fragile,bool native,bool declaration):IGameContentPackage
    {
        public PackageManifest Manifest {get;}=new("fixture-boundary-cao-zhi",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var rules=FixtureRules.Replace("$SCHEMA$",SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var names=new[]{Driver,Gain,Face,Committed,"fixture:co-quiet","fixture:co-multi","fixture:co-defense","fixture:co-native-face","fixture:co-counter"};
            var presentations=new Dictionary<string,object>();
            foreach(var id in names) presentations[id]=id is Gain or Face or Committed ? new {name=id,description="真实共享边界夹具",optionLabels=new Dictionary<string,string>{["continue"]="继续"}} : (object)new {name=id,description="真实共享边界夹具"};
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new {schemaVersion=3,skills=presentations}));
            foreach(var id in catalog.Programs.Keys) b.AddSkill(new(id,id,"真实共享边界夹具") {Program=catalog.Programs[id],Tags=id is "fixture:co-quiet" or "fixture:co-native-face" ? SkillTag.Locked:SkillTag.None});
            b.AddSkill(new("fixture:co-pick-owner","固定主公来源","公开选将评分") {SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?100000d:-100000d)});
            b.AddSkill(new("fixture:co-pick-other","固定其他角色","公开选将评分") {SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?-100000d:100000d)});
            var owner=new List<string>{"boundary:luoying","boundary:jiushi"};
            if(native) owner.AddRange(["fixture:co-quiet","fixture:co-native-face"]); else owner.AddRange([Driver,Gain,Face,Committed,"fixture:co-multi","fixture:co-counter"]);
            if(declaration) owner.Add("classic:guhuo");
            b.AddGeneral(new("fixture:co-owner","界曹植真实机制","supporter","fixture:co-pick-owner","wei",3,owner.ToArray()) {InitialHp=fragile?1:null});
            for(var i=1;i<4;i++) b.AddGeneral(new($"fixture:co-other-{i}","固定其他","supporter","fixture:co-pick-other","wei",12,["fixture:co-quiet","fixture:co-defense"]));
            b.AddDeck(new("fixture:co-deck","固定公开Club实体",4,2,[]) {PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(Mode,"界曹植真实命令",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Renegade)]=3},"fixture:co-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:co-owner","fixture:co-other-1","fixture:co-other-2","fixture:co-other-3"]));
        }
    }
    private const string FixtureRules="""
    {"schemaVersion":$SCHEMA$,"skills":[
     {"id":"fixture:co-driver","revision":1,"activations":[
      {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
      {"id":"lower-max","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"changeMaximumHp","target":"owner","amount":-2}]},
      {"id":"enemy-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
      {"id":"judge-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"startJudgment","target":"selectedTarget","judgmentReason":"fixture.provenance-judgment","resultBind":"judged","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"judged","destination":"discardPile"}]}]},
     {"id":"fixture:co-quiet","revision":1,"triggers":[{"id":"actual-discard","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","amount":2,"zones":["hand"],"resultBind":"cost"},{"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"discardPile","awaitMovementTriggers":true},{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
     {"id":"fixture:co-native-face","revision":1,"triggers":[{"id":"prepare-back-face","window":"afterNormalDraw","subject":"owner","priority":100,"optional":false,"effects":[{"op":"setFaceState","target":"owner","faceDown":true}]}]},
     {"id":"fixture:co-gain","revision":1,"triggers":[{"id":"claimed-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:luoying.provenance-claim"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:co-face","revision":1,"triggers":[{"id":"real-face-child","window":"characterTurnedOver","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"face-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:co-committed","revision":1,"triggers":[{"id":"committed","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"actual-use","options":[{"id":"continue"}]}]},{"id":"counterspell-completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["nullification"],"includeResponseUses":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"actual-counterspell","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:co-multi","revision":1,"viewAs":[{"id":"two-clubs","inputKinds":["slash"],"allowSameKind":true,"inputSuits":["club"],"outputKind":"slash","forPlay":true,"forResponse":false,"inputCount":2,"sameSuit":true,"extendedUse":true,"sourceZones":["hand","equipment"]}],"activations":[{"id":"double-slash","minCards":2,"maxCards":2,"sourceZones":["hand","equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-clubs","outputKind":"slash"}]}]},
     {"id":"fixture:co-counter","revision":1,"viewAs":[{"id":"club-counterspell","inputKinds":["slash"],"inputSuits":["club"],"outputKind":"nullification","forPlay":false,"forResponse":true,"sourceZones":["hand","equipment"]}]},
     {"id":"fixture:co-defense","revision":1,"viewAs":[{"id":"club-dodge","inputKinds":["slash"],"inputSuits":["club"],"outputKind":"dodge","forPlay":false,"forResponse":true,"sourceZones":["hand"]}]}]}
    """;
}
