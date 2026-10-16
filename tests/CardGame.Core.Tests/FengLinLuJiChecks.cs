using CardGame.Core;
using CardGame.Content.Standard;
using System.Text.Json;
internal static class FengLinLuJiChecks
{
 public static void PhysicalStartGiftPrevention()
 {
  var(g,r)=Create();Reach(g,p=>p.SkillPrompt?.SkillId=="classic:yili");Require(g.CreateSnapshot(0).Players[0].Markers!.Single(m=>m.Kind==PlayerMarkerKind.Orange).Count==3,"Real game start grants three attributed oranges.");Replay(g,r);
  Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("pay")=="marker"));var before=State(g);Require(!g.Submit(new AnswerPromptCommand(1,Prompt(g)!.PromptId,Prompt(g)!.Choices[0].Id,g.Revision)).Accepted&&before==State(g),"Wrong actor rejects atomically.");Answer(g,c=>c.Parameters.GetValueOrDefault("pay")=="marker");Answer(g,c=>c.Targets.SequenceEqual([1]));ReachPlay(g);
  Require(Markers(g,0)==2&&Markers(g,1)==1,"Payment consumes the real source count before granting the other participant.");Require(g.CreateSnapshot(0).Players[0].Hand.Count==7,"The actual marked draw subject receives normal two plus exactly one.");Replay(g,r);
  var hp=g.CreateSnapshot(0).Players[1].Hp;var slash=g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.Slash&&a.TargetSeats.Contains(1)&&a.ConversionSource is not null);Accept(g,new PlayCardCommand(0,slash.CardId!.Value,slash.TargetSeats,g.Revision,Prompt(g)!.PromptId,CardKind.Slash){ConversionSource=slash.ConversionSource});ReachPlay(g);
  Require(Markers(g,1)==0&&g.CreateSnapshot(0).Players[1].Hp==hp&&g.Events.Any(e=>e.Payload is ProgramMarkerEventAppliedEvent {Operation:SkillProgramEffectOp.ConsumeMarkerPreventDamage,SubjectSeat:1}),"One actual orange prevents the entire real damage event.");Replay(g,r);
 }
 private static int Markers(GameEngine g,int seat)=>g.CreateSnapshot(0).Players[seat].Markers?.Where(m=>m.Kind==PlayerMarkerKind.Orange).Sum(m=>m.Count)??0;
 private static PendingDecision? Prompt(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
 private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
 private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate){for(var n=0;n<250;n++){if(Prompt(g)is{}p&&predicate(p))return;Drive(g);}throw new InvalidOperationException("Missing rule boundary: "+Prompt(g));}
 private static void Drive(GameEngine g){if(Prompt(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}p)Accept(g,new EndPlayPhaseCommand(0,g.Revision,p.PromptId));else if(Prompt(g)is{PlayerSeat:0})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip"||c.Parameters.GetValueOrDefault("response")=="let-die"||c.Cards.Count==0);else Accept(g,new AdvanceOneStepCommand(g.Revision));}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
 private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected");}
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack,Commands=CommandJson.Serialize(g.AcceptedCommands)});
 private static void Replay(GameEngine g,ContentRegistry r){Require(State(g)==State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r)),"Every viewer and exact parent/cursor replays.");Require(g.CreateCardZoneDiagnostics().Count==100&&g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==100,"Entities conserved.");}
 private static void Require(bool b,string s){if(!b)throw new InvalidOperationException(s);}
 private static (GameEngine,ContentRegistry) Create(bool multiple=false,bool zero=false,bool dying=false)
 {
  var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(multiple,zero,dying));var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-luji-check",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=20},r);Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:luji-owner",g.Revision,Prompt(g)!.PromptId));return(g,r);
 }
 private sealed class Fixture(bool multiple,bool zero,bool dying):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-luji",new Version(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:luji-driver","revision":1,"activations":[{"id":"kill-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":20}]},{"id":"suppress-damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"suppressGeneralSkill","target":"selectedTarget"},{"op":"damageOtherLiving","target":"owner","amount":1}]}],"viewAs":[{"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}]}]}""", """{"schemaVersion":3,"skills":{"fixture:luji-driver":{"name":"真实杀","description":"测试"}}}""");foreach(var s in catalog.Programs)b.AddSkill(new(s.Key,s.Key,"测试"){Program=s.Value});
   if(zero||dying){var op=zero?"changeParticipantMarker":"loseHp";var tail=zero?",\"marker\":\"orange\"":"";var amount=zero?-3:1;var extra=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:luji-boundary","revision":1,"triggers":[{"id":"boundary","window":"gameStarting","subject":"owner","priority":-100,"optional":false,"effects":[{"op":"{{op}}","target":"owner","amount":{{amount}}{{tail}}}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:luji-boundary":{"name":"真实初始边界","description":"测试"}}}""");foreach(var e in extra.Programs)b.AddSkill(new(e.Key,e.Key,"测试"){Program=e.Value});}
   b.AddGeneral(new("fixture:luji-owner","橘来源","supporter","classic:huaiju","wu",dying?1:10,zero||dying?["classic:yili","classic:zhenglun","fixture:luji-driver","fixture:luji-boundary"]:["classic:yili","classic:zhenglun","fixture:luji-driver"]));for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:luji-target-{i}","目标","supporter",multiple?"classic:huaiju":"standard:none","shu",20,multiple?["classic:yili"]:[]));
   b.AddDeck(new("fixture:luji-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard(dying?"standard:peach":"standard:crossbow",Suit.Spade,i%13+1)).ToArray()});b.AddMode(new("identity:classic-luji-check","测试",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:luji-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:luji-owner","fixture:luji-target-1","fixture:luji-target-2","fixture:luji-target-3"]));
  }
 }
}
