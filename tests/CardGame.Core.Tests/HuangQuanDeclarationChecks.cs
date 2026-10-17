using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class HuangQuanDeclarationChecks
{
 private const string Owner="fixture:huang-quan-declaration-owner",Quiet="fixture:huang-quan-declaration-quiet",Mode="identity:huang-quan-declaration-fixture";
 public static void ActualRequestedDeclarationSuccessAndChallengedFailureCold()
 {
  foreach(var challenged in new[]{false,true})
  {
   var(g,registry)=Start();Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
   Accept(g,new UseProgramSkillCommand(0,"ol:jianji","draw-and-offer-exact-card",[],[1],g.Revision,P(g)!.PromptId));
   Reach(g,()=>P(g)is{PlayerSeat:0}p && p.Choices.Any(c=>c.Parameters.ContainsKey("declaration-answer")));
   var root=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.HuangQuan?.Kind=="jianji");var declaration=g.ResolutionStack.OfType<CardDeclarationFrame>().Single();var material=declaration.Payment.Cost.CardId;
   Need(declaration.OwnerSeat==1 && declaration.Return.ActorSeat==1 && declaration.Return.ParentFrameId==root.Id && root.HuangQuan!.NativeUse is{UseId:0,ActionId:0} && root.HuangQuan.CardIds.SequenceEqual([material]) && declaration.Payment.Cost.CardKind==CardKind.Dodge && declaration.Payment.DeclaredKind!=CardKind.Dodge,"The real recipient AI autonomously declares its exact drawn nonmatching entity through the native owned declaration parent.");
   Need(g.CardMovements.Count(m=>m.CardId==material && m.From==CardLocation.Hand(1) && m.To==CardLocation.Processing && m.Reason.Value=="conversion.declaration.pay")==1 && !E<JianjiUseIssuedEvent>(g).Any(),"One native declaration payment precedes all challenges, without a fabricated Use.");
   foreach(var viewer in Enumerable.Range(0,4)){var view=g.CreateSnapshot(viewer).CardDeclarations!.Single();Need(!view.IsRevealed && view.RevealedCard is null && (viewer==1?view.OwnerCost?.Id==material:view.OwnerCost is null),"Only the declaration provider sees its unrevealed drawn cost, including against the original giver.");}
   g=Cold(g,registry);Answer(g,c=>c.Parameters.GetValueOrDefault("declaration-answer")== (challenged?"challenge":"pass"));Reach(g,()=>E<HuangQuanCompletedEvent>(g).Any(e=>e.FrameId==root.Id));g=Cold(g,registry);
   var completion=E<HuangQuanCompletedEvent>(g).Single(e=>e.FrameId==root.Id);
   Need(E<HuangQuanDrawPaidEvent>(g).Count(e=>e.FrameId==root.Id)==1 && E<ProgramSkillResolvedEvent>(g).Count(e=>e.FrameId==root.Id && e.Completed)==1 && !g.ResolutionStack.Any(f=>f.Id==root.Id || f.Id==declaration.Id),"Original draw and typed declaration return finish once after cold recovery.");
   Need(g.CardMovements.Count(m=>m.CardId==material && m.From==CardLocation.Hand(1) && m.To==CardLocation.Processing)==1,"Success or failure never repays the drawn material.");
   if(challenged)
   {
    Need(E<YuandiDiscardEnteredEvent>(g).Single(e=>e.CardId==material).FromTrueUse==false,"A failed native declaration is a non-Use discard even when printed-name observers are enabled.");
    Need(E<CardDeclarationRevealedEvent>(g).Count(e=>e.DeclarationId==declaration.Id && e.ChallengerSeat==0 && !e.Succeeded && e.Card.Id==material)==1 && g.CardMovements.Any(m=>m.CardId==material && m.From==CardLocation.Processing && m.To==CardLocation.DiscardPile && m.Reason.Value=="conversion.declaration.failed") && !E<JianjiUseIssuedEvent>(g).Any() && !E<JianjiUseReturnedEvent>(g).Any() && completion.NativeActionId is null,"A human's genuine false challenge discards the one real cost and returns the Jianji parent without inventing an accepted use or action ID.");
   }
   else
   {
    var issued=E<JianjiUseIssuedEvent>(g).Single().Receipt;
    var actor=issued.ActorSeat==1;var originalEntity=issued.CardId==material;var source=issued.Action.ConversionSource==declaration.Payment.Source;
    var actualAccepted=E<CardActionAcceptedEvent>(g).Where(e=>e.Action.ActionId==issued.ActionId).Select(e=>e.Action).ToArray();
    var accepted=actualAccepted.Any(a=>a.ActorSeat==1 && a.ProviderSeat==1 && a.PhysicalCards is[var cost] && cost.CardId==material && cost.From==CardLocation.Hand(1));
    var nativeFinish=E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==issued.UseId);var typedReturn=E<JianjiUseReturnedEvent>(g).Count(e=>e.Receipt.ActionId==issued.ActionId);var completionIdentity=completion.NativeActionId==issued.ActionId;
    var diagnostic=JsonSerializer.Serialize(new{actor,originalEntity,source,accepted,nativeFinish,typedReturn,completionIdentity,issued.Action.Kind,issued.Action.PlayedCardKind,issued.UseId,issued.ActionId,Material=material,ActualAccepted=actualAccepted,completion.NativeActionId});
    Need(actor,"Declaration native actor remains the actual recipient: "+diagnostic);
    Need(originalEntity,"Declaration native use retains the exact drawn entity: "+diagnostic);
    Need(source,"Declaration native use retains its original conversion source: "+diagnostic);
    Need(accepted,"Declaration produces its genuine native accepted header with recipient actor/provider and original Hand material: "+diagnostic);
    Need(nativeFinish==1,"Declaration native use emits its finish exactly once: "+diagnostic);
    Need(typedReturn==1,"Declaration native use returns to its original parent exactly once: "+diagnostic);
    Need(completionIdentity,"Declaration parent completion preserves the native action ID: "+diagnostic);
    Need(E<YuandiDiscardEnteredEvent>(g).Single(e=>e.CardId==material).FromTrueUse,"A successful native declared use retains its real original entity Use provenance through cleanup.");
   }
  }
 }
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual declaration bridge command.");}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Need(p.PlayerSeat==0,"Only the actual human may answer a fixture decision.");Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
 private static void Reach(GameEngine g,Func<bool> done)
 {
  for(var i=0;i<180;i++)
  {
   if(done())return;var p=P(g);
   if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
   else if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("declaration-answer")=="pass"))Answer(g,c=>c.Parameters.GetValueOrDefault("declaration-answer")=="pass");
   else if(p is{PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass" or "let-die");
   else Accept(g,new AdvanceOneStepCommand(g.Revision));
  }
  throw new InvalidOperationException("Bounded requested declaration boundary missing: "+JsonSerializer.Serialize(new{Prompt=P(g),Frames=g.ResolutionStack,Recent=g.Events.TakeLast(10).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))}));
 }
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
 private static GameEngine Cold(GameEngine g,ContentRegistry registry){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);Need(State(g)==State(cold),"Cold accepted commands preserve private declaration payment, native challenge cursor and original Jianji return.");return cold;}
 private static string Embedded(string bundle,string suffix){using var stream=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms."+bundle+"."+suffix)!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
 private static (GameEngine,ContentRegistry)Start()
 {
  var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Reach(g,()=>P(g)is{PlayerSeat:0} && P(g)!.SkillPrompt?.SkillId=="ol:dianhu");Answer(g,c=>c.Targets.SequenceEqual([1]));return(g,registry);
 }
 private sealed class Fixture:IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("huang-quan-declaration-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder builder)
  {
   void Add(SkillProgramCatalog catalog){foreach(var(id,p)in catalog.Programs)builder.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>id==Quiet?10000d:-10000d)});}
   foreach(var bundle in new[]{"ol-huang-quan","classic-yu-ji","ol-xie-ling-yu"})Add(SkillProgramCatalog.Load(Embedded(bundle,"rules.json"),Embedded(bundle,"presentation.json")));
   Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"priority":0,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Quiet]=new{name="安静原生同桌",description="仅跳过真实自身出牌阶段"}}})));
   builder.AddGeneral(new(Owner,"原生谏计拥有者","supporter","ol:dianhu","shu",12,["ol:jianji"],GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:huang-quan-declaration-peer-{i}").ToArray();foreach(var id in peers)builder.AddGeneral(new(id,"原生蛊惑受赠者","supporter",Quiet,"qun",12,["classic:guhuo"],GeneralGender.Male));
   builder.AddDeck(new("fixture:huang-quan-declaration-deck","固定实际闪实体",4,0,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("standard:dodge",Suit.Heart,7)).ToArray()});
   builder.AddMode(new(Mode,"原生谏计声明组合",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:huang-quan-declaration-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
