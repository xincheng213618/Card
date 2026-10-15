using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class TangZiChecks
{
 private const string Owner="fixture:tang-zi-owner",Driver="fixture:tang-zi-driver",Quiet="fixture:tang-zi-quiet",Legacy="fixture:tang-zi-legacy-hp",Mode="identity:classic-tang-zi-fixture";
 public static void StrictPopulationGrantAndActualThresholdSourceLifetimeCold()
 {
  var rules=JsonNode.Parse(Embedded("ol-tang-zi","rules.json"))!;foreach(var invalid in new JsonNode?[]{JsonValue.Create(0),JsonValue.Create(17),JsonValue.Create("1")})
  {var bad=rules.DeepClone();bad["skills"]![0]!["hpSkillGrants"]![0]!["minimumLivingWounded"]=invalid?.DeepClone();var rejected=false;try{_=SkillProgramCatalog.Load(bad.ToJsonString(),Embedded("ol-tang-zi","presentation.json"));}catch(InvalidOperationException){rejected=true;}Need(rejected,"Population threshold rejects0,17 and strings through the real parser.");}
  var old=rules.DeepClone();var entry=old["skills"]![0]!["hpSkillGrants"]![0]!.AsObject();entry.Remove("minimumLivingWounded");entry["minimumHp"]=1;var oldRule=SkillProgramCatalog.Load(old.ToJsonString(),Embedded("ol-tang-zi","presentation.json")).Programs["ol:xingzhao"].HpSkillGrants.Single();Need(oldRule.MinimumLivingWounded is null && !JsonSerializer.Serialize(oldRule).Contains("MinimumLivingWounded",StringComparison.Ordinal) && oldRule.Matches(1) && !oldRule.Matches(0),"Absent new metadata retains old HP condition and canonical null omission.");
  var(g,r)=Start();Play(g);Need(!HasXunxun(g),"No wounded living character means no derived Xunxun.");Use(g,"damage",[1]);Play(g);Need(HasXunxun(g),"Another character's real damage immediately grants the exact linked skill.");var instance=E<HpConditionalSkillGrantChangedEvent>(g).Single(e=>e.Added && e.Source.CapabilitySkillId=="ol:xingzhao").SkillInstanceId;g=Cold(g,r);
  Use(g,"recover",[1]);Play(g);Need(!HasXunxun(g),"Actual full recovery removes the population-qualified grant.");Use(g,"damage",[2]);Play(g);Need(HasXunxun(g) && E<HpConditionalSkillGrantChangedEvent>(g).Where(e=>e.Added && e.Source.CapabilitySkillId=="ol:xingzhao").All(e=>e.SkillInstanceId==instance),"Threshold exit and reentry preserve the same effective source instance.");
  Use(g,"remove-source",[]);Play(g);Need(!HasXunxun(g) && !V(g,0).Skills!.Any(s=>s.ContentId=="ol:xingzhao"),"True source removal revokes only its qualified derived skill.");_=Cold(g,r);
  var(i,ir)=Start("independent");Play(i);Need(HasXunxun(i),"An independent existing HP-only grant remains active with zero global wounded.");Use(i,"damage",[1]);Play(i);Use(i,"remove-source",[]);Play(i);Need(V(i,0).Skills!.Count(s=>s.ContentId=="boundary:xunxun")==1 && E<HpConditionalSkillGrantChangedEvent>(i).Any(e=>e.Source.CapabilitySkillId==Legacy && e.Added),"Source loss never deletes or duplicates independently owned Xunxun and old HP rules ignore population.");_=Cold(i,ir);
 }
 public static void ActualPrivateExactXunxunReorderAndNormalDrawCold()
 {
  var(g,r)=Start();Play(g);Use(g,"damage",[1]);Play(g);var ownTurn=E<TurnStartedEvent>(g).Count(e=>e.ActorSeat==0);End(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="boundary:xunxun");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>P(g)?.Kind==DecisionKind.ProgramTopReorder);
  var frame=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.TopReorder is not null);var viewed=frame.TopReorder!.ViewedCardIds.ToArray();var hand=V(g,0).Hand.Select(c=>c.Id).ToArray();Need(viewed.Length==4 && frame.TopReorder.RequiredTopCount==2 && frame.HpConditionalSourceReceipt is not null && !V(g,0).Hand.Any(c=>viewed.Contains(c.Id)),"Real725 examines four private original deck entities before normal draw, with its typed derived source.");
  foreach(var viewer in Enumerable.Range(1,3))Need(g.CreateSnapshot(viewer).PendingDecision?.ValidCardIds.Any(viewed.Contains)!=true,"Foreign viewers do not receive the exact private viewed cards.");g=Cold(g,r);
  var ordered=new[]{viewed[1],viewed[0],viewed[3],viewed[2]};foreach(var id in ordered){Answer(g,c=>c.Cards.SequenceEqual([id]));if(P(g)?.Kind==DecisionKind.ProgramTopReorder)g=Cold(g,r);}Play(g);
  Need(ordered.Take(2).All(id=>V(g,0).Hand.Any(c=>c.Id==id)) && V(g,0).Hand.Count==hand.Length+2 && !V(g,0).Hand.Any(c=>ordered.Skip(2).Contains(c.Id)) && E<TurnStartedEvent>(g).Count(e=>e.ActorSeat==0)==ownTurn+1,"Two chosen top cards are gained by the unchanged native normal draw, while bottom cards are not directly gained.");_=Cold(g,r);
 }
 public static void ActualEquipmentUseRewardsBeforeSilverLionRecoveryCold()
 {
  var(g,r)=Start("armor");Play(g);Use(g,"draw",[]);Play(g);var lion=V(g,0).Hand.First(c=>c.Kind==CardKind.SilverLion);var bagua=V(g,0).Hand.First(c=>c.Kind==CardKind.BaguaFormation);PlayCard(g,lion.Id);Play(g);Need(!EquipmentRewards(g).Any(),"Native equipment use at zero wounded produces no reward.");
  Use(g,"damage",[0]);Play(g);Use(g,"damage",[1]);Play(g);Need(V(g,0).Hp==V(g,0).MaxHp-1 && V(g,1).Hp==V(g,1).MaxHp-1,"Two actual wounded participants qualify before armor replacement.");
  var n=g.CardMovements.Count;PlayCard(g,bagua.Id);Play(g);g=Cold(g,r);var reward=EquipmentRewards(g).Single();var events=g.Events.Select(e=>e.Payload).ToArray();var rewardIndex=Array.FindIndex(events,e=>ReferenceEquals(e,reward));var install=Array.FindIndex(events,e=>e is EquipmentChangedEvent equip && equip.CardId==bagua.Id);
  Need(reward.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized && rewardIndex>=0 && install>rewardIndex && V(g,0).Hp==V(g,0).MaxHp && V(g,0).Equipment.Any(c=>c.Id==bagua.Id),"True equipment use pays the locked draw in finalized-target stage before actual SilverLion replacement recovery lowers the wounded population to one.");
  Need(g.CardMovements.Skip(n).Count(m=>m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(0))==1,"The native equipment reward draws one actual entity exactly once.");
  var next=V(g,0).Hand.First(c=>c.Kind==CardKind.BaguaFormation);PlayCard(g,next.Id);Play(g);Need(EquipmentRewards(g).Count()==1,"A later genuine equipment use at one wounded does not inherit the earlier two-wounded qualification.");_=Cold(g,r);
 }
 public static void ActualDiscardThresholdAndUndefinedHpPopulationCold()
 {
  var(g,r)=Start();Play(g);Use(g,"draw",[]);Play(g);foreach(var seat in new[]{1,2,3}){Use(g,"damage",[seat]);Play(g);}var count=V(g,0).Hand.Count;var turn=g.State.TurnNumber;var skippedBefore=E<ProgramBindingResolvedEvent>(g).Count(e=>e.SkillId=="ol:xingzhao" && e.BindingId=="wounded-skip-discard" && e.Completed);End(g);Reach(g,()=>E<ProgramBindingResolvedEvent>(g).Count(e=>e.SkillId=="ol:xingzhao" && e.BindingId=="wounded-skip-discard" && e.Completed)==skippedBefore+1);
  Need(V(g,0).Hand.Count==count && E<ProgramBindingResolvedEvent>(g).Count(e=>e.SkillId=="ol:xingzhao" && e.BindingId=="wounded-skip-discard" && e.Window==SkillProgramTriggerWindow.DiscardPhaseStarting && e.Activated && e.Completed)==skippedBefore+1,"At actual discard entry, three wounded skip the real phase once and retain the excess hand.");g=Cold(g,r);NextOwnPlayAfterEnd(g);Use(g,"recover",[3]);Play(g);turn=g.State.TurnNumber;var before=V(g,0).Hand.Count;End(g);Reach(g,()=>E<TurnStartedEvent>(g).Any(e=>e.ActorSeat!=0 && e.TurnNumber>turn));
  Need(E<ProgramBindingResolvedEvent>(g).Count(e=>e.SkillId=="ol:xingzhao" && e.BindingId=="wounded-skip-discard" && e.Activated && e.Completed)==skippedBefore+1 && V(g,0).Hand.Count<before,"Healing to two before actual entry removes the skip and genuine native discards occur.");_=Cold(g,r);
  var(h,hr)=Start("hidden");Play(h);var hidden=Enumerable.Range(1,3).Single(s=>V(h,s).IdentityConcealed);Need(V(h,hidden).HpUndefined && !HasXunxun(h),"A genuine concealed undefined-HP participant does not manufacture population qualification.");var normal=Enumerable.Range(1,3).First(s=>s!=hidden);Use(h,"damage",[normal]);Play(h);Need(HasXunxun(h) && V(h,hidden).HpUndefined,"Only the actual defined-HP wound qualifies while the concealed character remains undefined.");_=Cold(h,hr);
 }
 private static bool HasXunxun(GameEngine g)=>V(g,0).Skills!.Any(s=>s.ContentId=="boundary:xunxun");
 private static IEnumerable<ProgramBindingResolvedEvent>EquipmentRewards(GameEngine g)=>E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId=="ol:xingzhao" && e.BindingId=="equipment-wounded-draw" && e.Activated && e.Completed);
 private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(c);Need(result.Accepted,result.Error?.Message??"Rejected Tang Zi command.");}
 private static void Answer(GameEngine g,Func<PromptChoice,bool>choose){var p=P(g)!;Need(p.PlayerSeat==0,"Fixture only answers the actual human.");Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
 private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
 private static void NextOwnPlayAfterEnd(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
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
