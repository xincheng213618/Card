using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class TangZiChecks
{
 private const string Owner="fixture:tang-zi-owner",Driver="fixture:tang-zi-driver",Quiet="fixture:tang-zi-quiet",Legacy="fixture:tang-zi-legacy-hp",Mode="identity:classic-tang-zi-fixture";
 public static void ActualPrivateExactXunxunReorderAndNormalDrawCold()
 {
  var(g,r)=Start();Play(g);Use(g,"damage",[1]);Play(g);var ownTurn=E<TurnStartedEvent>(g).Count(e=>e.ActorSeat==0);End(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="boundary:xunxun");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>P(g)?.Kind==DecisionKind.ProgramTopReorder);
  var frame=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.TopReorder is not null);var viewed=frame.TopReorder!.ViewedCardIds.ToArray();var hand=V(g,0).Hand.Select(c=>c.Id).ToArray();Need(viewed.Length==4 && frame.TopReorder.RequiredTopCount==2 && frame.HpConditionalSourceReceipt is not null && !V(g,0).Hand.Any(c=>viewed.Contains(c.Id)),"Real725 examines four private original deck entities before normal draw, with its typed derived source.");
  foreach(var viewer in Enumerable.Range(1,3))Need(g.CreateSnapshot(viewer).PendingDecision?.ValidCardIds.Any(viewed.Contains)!=true,"Foreign viewers do not receive the exact private viewed cards.");g=Cold(g,r);
  var ordered=new[]{viewed[1],viewed[0],viewed[3],viewed[2]};foreach(var id in ordered){Answer(g,c=>c.Cards.SequenceEqual([id]));if(P(g)?.Kind==DecisionKind.ProgramTopReorder)g=Cold(g,r);}Play(g);
  Need(ordered.Take(2).All(id=>V(g,0).Hand.Any(c=>c.Id==id)) && V(g,0).Hand.Count==hand.Length+2 && !V(g,0).Hand.Any(c=>ordered.Skip(2).Contains(c.Id)) && E<TurnStartedEvent>(g).Count(e=>e.ActorSeat==0)==ownTurn+1,"Two chosen top cards are gained by the unchanged native normal draw, while bottom cards are not directly gained.");_=Cold(g,r);
 }
 private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(c);Need(result.Accepted,result.Error?.Message??"Rejected Tang Zi command.");}
 private static void Answer(GameEngine g,Func<PromptChoice,bool>choose){var p=P(g)!;Need(p.PlayerSeat==0,"Fixture only answers the actual human.");Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
 private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
 private static void Use(GameEngine g,string id,IReadOnlyList<int>targets)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets,g.Revision,P(g)!.PromptId));
 private static void PlayCard(GameEngine g,int id)=>Accept(g,new PlayCardCommand(0,id,[],g.Revision,P(g)!.PromptId));
 private static void Reach(GameEngine g,Func<bool>done)
 {
  for(var n=0;n<240;n++){if(done())return;var p=P(g);if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"pass"or"take-damage"or"let-die");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
  throw new InvalidOperationException("Tang Zi boundary missing: "+JsonSerializer.Serialize(new{Prompt=P(g),Frames=g.ResolutionStack,Players=Enumerable.Range(0,4).Select(s=>V(g,s))}));
 }
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray()});
 private static GameEngine Cold(GameEngine g,ContentRegistry registry){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);Need(State(g)==State(cold),"Cold commands preserve live conditional instances, original private top ordering and native payments.");return cold;}
 private static string Embedded(string bundle,string suffix){using var stream=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms."+bundle+"."+suffix)!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
 private static(GameEngine,ContentRegistry)Start(string scenario="normal")
 {var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=16},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);}
 private sealed class Fixture(string scenario):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("tang-zi-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   void Add(SkillProgramCatalog catalog,IReadOnlySet<string>?only=null){foreach(var(id,p)in catalog.Programs.Where(x=>only is null||only.Contains(x.Key)))b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>id==Quiet?10000d:-10000d)});}
   Add(SkillProgramCatalog.Load(Embedded("ol-tang-zi","rules.json"),Embedded("ol-tang-zi","presentation.json")));Add(SkillProgramCatalog.Load(Embedded("boundary-li-dian","rules.json"),Embedded("boundary-li-dian","presentation.json")),new HashSet<string>{"boundary:xunxun"});
   if(scenario=="hidden")Add(SkillProgramCatalog.Load(Embedded("ol-xuan-gong-zhu","rules.json"),Embedded("ol-xuan-gong-zhu","presentation.json")));
   var defs=new List<string>{$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"priority":0,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",$$"""{"id":"{{Driver}}","revision":1,"activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},{"id":"recover","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":1}]},{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},{"id":"remove-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:xingzhao"],"sourceBind":"standard:none"}]}]}"""};
   var own=new List<string>{"ol:xingzhao"};if(scenario=="independent"){own.Add(Legacy);defs.Add($$"""{"id":"{{Legacy}}","revision":1,"hpSkillGrants":[{"id":"old-hp-only","skillId":"boundary:xunxun","minimumHp":1}]}""");}
   var labels=defs.Select(d=>JsonDocument.Parse(d).RootElement.GetProperty("id").GetString()!).ToDictionary(id=>id,id=>new{name=id,description="原生唐咨夹具"});Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));
   b.AddGeneral(new(Owner,"真实唐咨拥有者","supporter",Driver,"wei",12,own,GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:tang-zi-peer-{i}").ToArray();for(var i=0;i<3;i++)b.AddGeneral(new(peers[i],"原生同桌","supporter",Quiet,"wei",12,scenario=="hidden"&&i==0?["ol:gaoling"]:[],GeneralGender.Male));
   b.AddCard(new("fixture:tang-zi-lion","白银狮子","装备牌","实际失装回复原生防具",CardKind.SilverLion));
   b.AddDeck(new("fixture:tang-zi-deck","固定原生牌",4,2,[]){PhysicalCards=scenario=="armor"?Enumerable.Range(0,48).Select(_=>new ContentDeckPhysicalCard("fixture:tang-zi-lion",Suit.Club,7)).Concat(Enumerable.Range(0,48).Select(_=>new ContentDeckPhysicalCard("standard:bagua",Suit.Heart,7))).ToArray():Enumerable.Range(0,96).Select(i=>new ContentDeckPhysicalCard("standard:slash",new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond}[i%4],i%13+1)).ToArray()});
   b.AddMode(new(Mode,"唐咨原生夹具",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:tang-zi-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
