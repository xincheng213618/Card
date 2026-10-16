using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class LvQianChecks
{
 private const string Owner="fixture:lvqian-general",Driver="fixture:lvqian-driver",Quiet="fixture:lvqian-quiet",Mode="identity:classic-lvqian-fixture";
 public static void ActualWeiluRepeatedSourceNextPlayAndExactRestoreCold()
 {
  var(g,r)=Start();Play(g);Use(g,"incoming",1);Play(g);Use(g,"incoming",1);Play(g);Need(E<WeiluFutureArmedEvent>(g).Count()==2&&!E<WeiluHpPaidEvent>(g).Any(),"Two actual other-source damage occurrences arm future obligations without paying early.");var before=V(g,1).Hp;End(g);Play(g);var paid=E<WeiluHpPaidEvent>(g).Single();Need(paid.TargetSeat==1&&paid.ActualHpLost==before-1&&V(g,1).Hp==1,"Repeated same source aggregates into one real loss to one at the next actual own Play.");g=Cold(g,r);var phase=paid.Phase;End(g);Reach(g,()=>E<WeiluDebtConsumedEvent>(g).Any(e=>e.ProducerFrameId==paid.FrameId));g=Cold(g,r);Need(V(g,1).Hp==before&&E<RecoveryAppliedEvent>(g).Any(e=>e.TargetSeat==1&&e.Amount==paid.ActualHpLost)&&E<WeiluDebtConsumedEvent>(g).Count(e=>e.ProducerFrameId==paid.FrameId&&e.Phase==phase)==1,"Only the exact paid phase ends by recovering the actual loss once.");
 }
 private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
 private static PendingDecision?P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
 private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(command);Need(result.Accepted,result.Error?.Message??"Lv Qian command rejected.");}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> test){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(test).Id,g.Revision));}
 private static void Reach(GameEngine g,Func<bool> done){for(var n=0;n<180;n++){if(done())return;var p=P(g);if(p?.PlayerSeat==0&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")is"pass"or"let-die"or"take-damage"))Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"pass"or"let-die"or"take-damage");else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Lv Qian boundary missing "+JsonSerializer.Serialize(new{Prompt=P(g)?.Kind,Frames=g.ResolutionStack.Select(f=>new{f.Id,f.Kind}),Recent=g.Events.TakeLast(8).Select(e=>e.Payload.GetType().Name)}));}
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{Kind:DecisionKind.PlayCard,PlayerSeat:0});
 private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
 private static void Use(GameEngine g,string id,int?target=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],target is{}t?[t]:[],g.Revision,P(g)!.PromptId));
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements});
 private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(restored)==State(g),"Real phase debts, original public gifts, native children and all private views reproduce across cold command-prefix restore.");return restored;}
 private static string Read(string suffix){using var stream=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-lv-qian."+suffix)!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
 private static(GameEngine,ContentRegistry)Start(bool rebel=false){var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture());var g=GameEngine.CreateStandard(new GameOptions{Seed=7,HumanSeat=0,HumanRole=rebel?Role.Rebel:Role.Lord,PlayerCount=4,ModeId=Mode,UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,UseInteractiveDiscard=false,MaxTurns=8},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral,PlayerSeat:0});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);}
 private sealed class Fixture:IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("lvqian-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>id==Quiet?10000d:-10000d)});}Add(SkillProgramCatalog.Load(Read("rules.json"),Read("presentation.json")));
   var activations=new List<object>();object Effect(string op,string target,int amount=1,object?sourceRef=null,string?nature=null)=>new{op,target,amount,sourceRef,nature};
   foreach(var(id,effects,targets)in new(string,object[],int)[]{("incoming",[Effect("damage","owner",sourceRef:new{kind="selectedTarget"})],1),("self-fire",[Effect("damage","selectedTarget",sourceRef:new{kind="selectedTarget"},nature:"fire")],1),("kill",[Effect("damage","selectedTarget",20)],1),("hurt",[Effect("loseHp","owner")],0),("suicide",[Effect("loseHp","owner",20)],0),("lose-weilu",[new{op="loseOwnerSkillsAndGrant",target="owner",skillIds=new[]{"ol:weilu"},sourceBind=Driver}],0),("lose-zengdao",[new{op="loseOwnerSkillsAndGrant",target="owner",skillIds=new[]{"ol:zengdao"},sourceBind=Driver}],0)})activations.Add(new{id,minCards=0,maxCards=0,minTargets=targets,maxTargets=targets,targetKind="otherLiving",usesPerTurn=(int?)null,effects});
   var quietRule=new
   {
    id=Quiet,revision=1,
    triggers=new[]
    {
     new
     {
      id="quiet",window="afterNormalDraw",subject="owner",optional=false,
      effects=new[]{new{op="skipTurnPhases",target="owner",phases=new[]{"play"}}}
     }
    }
   };
   var rules=JsonSerializer.Serialize(new
   {
    schemaVersion=SkillProgramCatalog.RulesSchemaVersion,
    skills=new object[]{new{id=Driver,revision=1,activations},quietRule}
   },new JsonSerializerOptions{DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull});
   var rulesNode=JsonNode.Parse(rules)!;foreach(var activation in rulesNode["skills"]![0]!["activations"]!.AsArray())activation!["usesPerTurn"]=null;rules=rulesNode.ToJsonString();
   var labels=new Dictionary<string,object>{{Driver,new{name="真实驱动",description="native"}},{Quiet,new{name="静默",description="native"}}};
   Add(SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));

   b.AddCard(new("fixture:lvqian-lion","白银狮子","装备牌","真实原生狮子移除回复",CardKind.SilverLion));
   b.AddGeneral(new(Owner,"真实吕虔","supporter",Driver,"wei",4,["ol:weilu","ol:zengdao"],GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>"fixture:lvqian-peer-"+i).ToArray();foreach(var id in peers)b.AddGeneral(new(id,"真实对手","supporter",Quiet,"qun",12,[]));b.AddDeck(new("fixture:lvqian-deck","small",4,0,[]){PhysicalCards=Enumerable.Range(0,40).Select(i=>new ContentDeckPhysicalCard(i==2?"standard:offensive_horse":"fixture:lvqian-lion",Suit.Heart,7)).ToArray()});b.AddMode(new(Mode,"真实吕虔",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}},"fixture:lvqian-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
