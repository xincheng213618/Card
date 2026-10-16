using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class YangFengChecks
{
    private const string Jiawei="ol:jiawei",Qujia="ol:qujia",Owner="fixture:yf-owner",Driver="fixture:yf-driver",Health="fixture:yf-health",Watch="fixture:yf-watch",Mode="identity:classic-yang-feng-fixture";
    public static void JiaweiCompletedTurnQuotaPartitionAndDistanceCold()
    {
        foreach(var scene in new[]{"partition-slash","partition-plain","partition-lightning","partition-surprise","partition-empty"})
        {
            var(g,r)=Start(scene);Need(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([2])),"An ordinary out-of-range target is unavailable before genuine Jiawei.");
            Jia(g,1);Play(g);var first=E<YangFengStartedEvent>(g).Single(e=>e.Op==SkillProgramEffectOp.ResolveJiaweiDiscardReveal);
            Need(first.CompletedTurns==0&&first.X==0&&first.RequiredDiscard==0&&!E<JiaweiMaterialSelectedEvent>(g).Any(),"The owner's current first turn is not an already executed turn; Jiawei's zero discard still resolves.");
            var firstRevealed=E<JiaweiEntityRevealedEvent>(g).Where(e=>e.FrameId==first.FrameId).ToArray();
            Need(firstRevealed.Length==(scene=="partition-empty"?0:1)&&E<ProgramCardsRevealedEvent>(g).Single(e=>e.FrameId==first.FrameId).Cards.Select(c=>c.Id).SequenceEqual(firstRevealed.Select(e=>e.Entity.Material.CardId)),"Even the zero/insufficient-deck branch publishes a genuine ordered reveal of only actual native top entities.");
            AssertPartition(g,first.FrameId,1);
            Need(g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([2])),"The completed real activation grants all-card use distance for this actual turn without changing physical distance.");
            var before=State(g);Need(!g.Submit(new UseProgramSkillCommand(0,Jiawei,"discard-reveal-partition",[],[0],g.Revision,P(g)!.PromptId)).Accepted&&State(g)==before,"A second activation in the same actual Play phase is rejected atomically.");g=Cold(g,r);
            Slash(g,2);Play(g);Need(E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==2&&e.Amount==1),"The genuine distance benefit permits a real remote native Slash and actual damage.");
            var turn=g.State.TurnNumber;End(g);SkipQujia(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&g.State.TurnNumber>turn);
            Need(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([2])),"Jiawei's distance grant expires at its original actual turn end.");
            Jia(g,0);Reach(g,()=>YangMenu(g,"discard"));Answer(g,c=>c.Cards.Count==1);Play(g);var second=E<YangFengStartedEvent>(g).Last(e=>e.Op==SkillProgramEffectOp.ResolveJiaweiDiscardReveal);
            Need(second.CompletedTurns==1&&second.X==1&&second.RequiredDiscard==1&&E<JiaweiMaterialSelectedEvent>(g).Count(e=>e.FrameId==second.FrameId)==1,"The next own actual turn freezes one completed own turn and pays exactly one original self card.");AssertPartition(g,second.FrameId,0);_=Cold(g,r);
        }
    }
    private static void AssertPartition(GameEngine g,long frameId,int target)
    {
        var revealed=E<JiaweiEntityRevealedEvent>(g).Where(e=>e.FrameId==frameId).ToArray();var paid=E<YangFengMaterialMovedEvent>(g).Where(e=>e.FrameId==frameId&&e.Stage is YangFengStage.OwnerGainChildren or YangFengStage.TargetGainChildren).ToArray();
        Need(paid.Length==revealed.Length&&revealed.All(e=>paid.Count(p=>p.Material==e.Entity.Material&&p.To==CardLocation.Hand(e.Entity.DamageCard?0:target))==1)&&
            revealed.All(e=>!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.YangFeng is not null)),"The exact publicly revealed original entities are partitioned once by actual damage-card identity, including delayed Lightning and Unexpected Assault, without replacing or orphaning a card.");
    }
    private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static bool Action(GameEngine g,string action)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action)==true;
    private static bool YangMenu(GameEngine g,string branch)=>Action(g,"yang-feng")&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")==branch);
    private static bool QujiaPrompt(GameEngine g)=>P(g)?.SkillPrompt?.SkillId==Qujia&&Action(g,"activate");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual Yang Feng command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;var choice=p.Choices.FirstOrDefault(pick);Need(p.PlayerSeat==0&&choice is not null,"Missing genuine human Yang Feng choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,choice!.Id,g.Revision));}
    private static void Continue(GameEngine g){if(P(g)?.PlayerSeat==0)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Jia(GameEngine g,int target)=>Accept(g,new UseProgramSkillCommand(0,Jiawei,"discard-reveal-partition",[],[target],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void SkipQujia(GameEngine g){Reach(g,()=>QujiaPrompt(g));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");}
    private static void Slash(GameEngine g,int target)=>Native(g,a=>a.Kind==LegalActionKind.Slash&&a.PlayedCardKind==CardKind.Slash&&a.TargetSeats.SequenceEqual([target]));
    private static void Native(GameEngine g,Func<LegalAction,bool> pick){var a=g.GetHumanLegalActions().FirstOrDefault(pick);Need(a is not null,"Missing native Yang Feng action: "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void Play(GameEngine g,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard},$"{caller}:{line} actual Play");
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string caller="",[CallerLineNumber]int line=0,[CallerArgumentExpression(nameof(done))]string? boundary=null)
    {
        for(var i=0;i<240;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"Yang Feng ended before {caller}:{line} {boundary}: "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p.SkillPrompt?.SkillId is Health or Watch)Continue(g);
            else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else throw new InvalidOperationException($"Unexpected Yang Feng {caller}:{line} {boundary}: "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing bounded Yang Feng {caller}:{line} {boundary}: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,g.State.Winner,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).GeneralId,V(g,s).Hp,V(g,s).MaxHp,V(g,s).HandCount,V(g,s).IsAlive}),Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(5).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(4).Select(f=>new{f.Id,Type=f.GetType().Name}),Facts=g.Events.TakeLast(4).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold replay preserves all actual Yang Feng views, original movement entities, turn-end receipts and once-paid child cursors.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene is"death"or"quota-death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);Need(V(g,0).GeneralId==Owner&&V(g,0).Hp==(scene is"death"or"quota-death"?12:13),"The real selected source uses the classic identity HP contract.");return(g,r);}
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-yang-feng",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:yang-feng");var loss=scene is"loss"or"quota-loss";var quota=scene.StartsWith("quota-",StringComparison.Ordinal);var death=scene is"death"or"quota-death";
            var defs=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"viewAs":[{"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true}],"activations":[
             {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
             {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
             {"id":"flip","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"owner"}]},
             {"id":"cap","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"changeMaximumHp","target":"owner","amount":-12}]},
             {"id":"regrant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:qujia"]}]},
             {"id":"weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":5}]}]}
            """,$$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]}""","""{"id":"fixture:yf-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            if(loss)defs.Add($$"""{"id":"fixture:yf-conditional","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"{{(quota?Qujia:Jiawei)}}","minimumHp":13}]}""");
            if(quota&&scene!="quota-empty"||scene is"loss"or"death")
            {
                var window=quota?"cardsGained":"cardsMoved";var filter=quota?""" "destinationZones":["hand"], """:""" "sourceZones":["hand"],"movementDiscardOnly":true, """;
                var tail=loss||death?$$""",{"op":"loseHp","target":"owner","amount":{{(death?20:1)}}}""":"";
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-paid-child","window":"{{window}}","subject":"owner",{{filter}}"movementOccurrence":"perBatch","movementReasons":["{{(quota?"skill-program.yang-feng.qujia.gain":"skill-program.yang-feng.jiawei.discard")}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}{{tail}}]}]}""");
            }
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实杨奉夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实原生规则"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:yf-pick","实际本人","固定公开选将评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});b.AddSkill(new("fixture:yf-unused","备用实体武将","不参与实际选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-200000d)});
            var own=new List<string>{Driver,Health,loss&&!quota?"fixture:yf-conditional":Jiawei,loss&&quota?"fixture:yf-conditional":Qujia};if(quota&&scene!="quota-empty"||scene is"loss"or"death")own.Add(Watch);
            b.AddGeneral(new(Owner,"实际杨奉","ol-yang-feng","fixture:yf-pick","qun",12,own,GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:yf-peer-{i}").ToArray();foreach(var peer in peers)b.AddGeneral(new(peer,"实际其他角色","supporter","fixture:yf-quiet","qun",12,[Health]));
            b.AddGeneral(new("fixture:yf-weapon","实际备用武器","supporter","fixture:yf-unused","qun",4,[]));
            var kind=scene switch{"lion"=>CardKind.SilverLion,"ox"=>CardKind.WoodenOx,"foreign"=>CardKind.Indulgence,"partition-lightning"=>CardKind.Lightning,"partition-surprise"=>CardKind.UnexpectedAssault,"partition-plain"or"partition-empty"or"extra"or"flip"or"weapon"or"quota-empty"=>CardKind.Crossbow,_=>CardKind.Slash};
            b.AddCard(new("fixture:yf-card","真实"+kind,kind is CardKind.Crossbow or CardKind.SilverLion or CardKind.WoodenOx?"装备牌":kind is CardKind.Lightning or CardKind.Indulgence or CardKind.UnexpectedAssault?"锦囊牌":"基本牌","真实原生实体",kind));
            b.AddDeck(new("fixture:yf-deck","固定同质真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scene is"partition-empty"or"quota-empty"?18:64).Select(_=>new ContentDeckPhysicalCard("fixture:yf-card",Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"杨奉原生小局面",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:yf-deck",GeneralCandidateCount:5,GeneralPoolIds:[Owner,..peers,"fixture:yf-weapon"]));
        }
    }
}
