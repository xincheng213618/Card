using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class AHuiNanChecks
{
    private const string Skill="ol:jueman",Owner="fixture:ahn-owner",Driver="fixture:ahn-driver",Quiet="fixture:ahn-quiet",Commit="fixture:ahn-commit",Response="fixture:ahn-response",Watch="fixture:ahn-watch",Late="fixture:ahn-late",Source="fixture:ahn-source",Rescue="fixture:ahn-rescue",Mode="identity:classic-a-hui-nan-fixture";
    private const string DrawReason="skill-program.a-hui-nan.jueman.draw";
    public static void FirstTwoUsersDrawAndActualTurnLedgerCold()
    {
        foreach(var scenario in new[]{"one","own-two","mixed"})
        {
            var(g,r)=Start(scenario);Use(g,"draw");Play(g);NativeKind(g,CardKind.Alcohol);Play(g);
            if(scenario=="own-two"){NativeKind(g,CardKind.Slash,1);Play(g);}
            if(scenario=="mixed")Incoming(g,1,2);
            var turn=g.State.TurnNumber;var before=V(g).HandCount;g=Cold(g,r);End(g);FinishTurn(g,turn);
            var started=E<AHuiNanJuemanStartedEvent>(g).Single(e=>e.ActualTurn==turn);var draws=E<AHuiNanJuemanDrawnEvent>(g).Where(e=>e.FrameId==started.FrameId).ToArray();
            Need(started.BasicCount==(scenario=="one"?1:2)&&started.FirstTwoOwnerUses==(scenario=="own-two"?2:1)&&draws.Length==(scenario=="mixed"?1:0)&&!E<AHuiNanJuemanUseIssuedEvent>(g).Any(),"Only exactly one of two real users draws; a missing second card or two own uses grants neither branch. "+Diagnostic(g));
            if(scenario=="mixed")Need(draws[0].Count==1&&V(g).HandCount==before+1&&g.CardMovements.Count(m=>m.Sequence>draws[0].Before&&m.Sequence<=draws[0].After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(0)&&m.Reason.Value==DrawReason)==1,"The locked reward is one genuine deck-to-hand move at the actual ending boundary.");
            Need(E<TurnEndedEvent>(g).Count(e=>e.TurnNumber==turn)==1,"A locked reward does not emit a second whole-turn end.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("mixed");Incoming(g,1,0);NativeKind(g,CardKind.Alcohol);Play(g);var turn=g.State.TurnNumber;End(g);FinishTurn(g,turn);
            Need(E<AHuiNanJuemanStartedEvent>(g).Single(e=>e.ActualTurn==turn).FirstTwoOwnerUses==1&&E<AHuiNanJuemanDrawnEvent>(g).Single().Count==1,"The user order can be other then owner, with the same exact-one reward.");
            Reach(g,()=>P(g)is{Kind:DecisionKind.PlayCard,PlayerSeat:0}&&g.State.TurnNumber>turn);var next=g.State.TurnNumber;End(g);FinishTurn(g,next);
            Need(E<AHuiNanJuemanStartedEvent>(g).Single(e=>e.ActualTurn==next).BasicCount==0&&E<AHuiNanJuemanDrawnEvent>(g).Length==1,"A new actual turn starts an empty ledger; the prior turn's two users cannot leak into it.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int s=0)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static AHuiNanJuemanReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.AHuiNanJueman is not null)?.AHuiNanJueman;
    private static string State(GameEngine g)=>JsonSerializer.Serialize(g.ResolutionStack)+JsonSerializer.Serialize(g.CreateSnapshot(0))+g.Events.Count;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected genuine Jueman command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Need(V(g,p.PlayerSeat).IsHuman,"Only the real human chooser answers.");var c=p.Choices.FirstOrDefault(pick);Need(c is not null,"Missing Jueman fixture choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Use(GameEngine g,string id,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void NativeKind(GameEngine g,CardKind kind,int? target=null)
    {var a=g.GetHumanLegalActions().FirstOrDefault(a=>(a.PlayedCardKind??(a.CardId is{}id?V(g).Hand.FirstOrDefault(c=>c.Id==id)?.Kind:null))==kind&&(target is null||a.TargetSeats.SequenceEqual([target.Value])));Need(a is not null,"Missing native "+kind+": "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void Incoming(GameEngine g,int actor,int target,bool dodge=false)
    {
        Use(g,"incoming",[actor]);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.ProgramTrigger}&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("offer-option")=="target-"+target));
        Answer(g,c=>c.Parameters.GetValueOrDefault("offer-option")=="target-"+target);
        if(dodge){Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});Answer(g,c=>c.Cards.Count==1);}
        Play(g);
    }
    private static void FinishTurn(GameEngine g,int turn)=>Reach(g,()=>!g.ResolutionStack.OfType<DeferredTurnEndFrame>().Any(p=>p.TurnNumber==turn)&&E<TurnEndedEvent>(g).Any(e=>e.TurnNumber==turn));
    private static string Diagnostic(GameEngine g)=>$"turn={g.State.TurnNumber};winner={g.State.Winner};prompt={P(g)?.Kind}/{P(g)?.PlayerSeat}/{P(g)?.SkillPrompt?.SkillId};stage={Current(g)?.Stage};first3={JsonSerializer.Serialize(E<AHuiNanBasicUseRecordedEvent>(g).TakeLast(3))}";
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var step=0;step<160;step++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Missing actual Jueman boundary: "+Diagnostic(g));var p=P(g);
            if(p is not null&&!V(g,p.PlayerSeat).IsHuman)Accept(g,new AdvanceOneStepCommand(g.Revision));
            else if(p is{Kind:DecisionKind.PlayCard})End(g);
            else if(p is{Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Cards.Count==0);
            else if(p is{Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")==true)Continue(g);
            else if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")==true)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p is not null)throw new InvalidOperationException("Unexpected actual Jueman fixture prompt: "+Diagnostic(g));
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing bounded Jueman boundary: "+Diagnostic(g));
    }
    private static GameEngine Cold(GameEngine g,ContentRegistry r)
    {var next=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(next),"Cold replay preserves private first-three history, issued source and exact native child/cursor.");return next;}
    private static(GameEngine,ContentRegistry)Start(string scenario)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario=="paid-death"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-a-hui-nan",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:a-hui-nan");
            var definitions=new List<string>
            {
                $$$"""
                {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"viewAs":[{"id":"wine-as-slash","inputKinds":["alcohol"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}],"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
                {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                {"id":"recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":1}]},
                {"id":"dying","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":13}]},
                {"id":"gain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:jueman"]}]}]}
                """,
                $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
                $$"""{"id":"{{Commit}}","revision":1,"triggers":[{"id":"actual-use","window":"cardUseCommitted","ownerRelation":"actor","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native","options":[{"id":"continue"}]}]}]}""",
                $$"""{"id":"{{Response}}","revision":1,"triggers":[{"id":"actual-response","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["slash","dodge"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"response","options":[{"id":"continue"}]}]}]}"""
            };
            var skills=new List<string>{Driver,Commit,Response};
            if(scenario is"paid-loss"or"native-loss"or"unpaid-loss")
            {definitions.Add($$"""{"id":"{{Source}}","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"ol:jueman","minimumHp":13}]}""");skills.Add(Source);}
            else if(scenario!="historical")skills.Add(Skill);
            if(scenario is"paid-loss"or"paid-death")
            {
                var amount=scenario=="paid-death"?20:1;
                definitions.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["{{DrawReason}}"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":{{amount}}}]}]}""");skills.Add(Watch);
            }
            if(scenario=="native-loss")
            {
                definitions.Add($$"""{"id":"{{Late}}","revision":1,"triggers":[{"id":"real-native","window":"cardUseCommitted","ownerRelation":"actor","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":1}]}]}""");
                definitions.Add($$"""{"id":"fixture:ahn-arm","revision":1,"triggers":[{"id":"arm","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Late}}"]}]}]}""");skills.Add("fixture:ahn-arm");
            }
            if(scenario=="rescue")
            {definitions.Add($$"""{"id":"{{Rescue}}","revision":1,"triggers":[{"id":"self-wine","window":"selfDyingResponse","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"useVirtualDyingAlcohol","target":"owner"}]}]}""");skills.Add(Rescue);}
            var names=definitions.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实原生小夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x=>x,x=>x)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',definitions)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=names}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,names[id].name,"实际蟨蛮驱动"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            const string pick="fixture:ahn-owner-pick";b.AddSkill(new(pick,"本人","固定公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-100000000d)});b.AddGeneral(new(Owner,"实际阿会喃","ol-a-hui-nan",pick,"qun",12,skills));
            var peers=Enumerable.Range(1,3).Select(i=>"fixture:ahn-peer-"+i).ToArray();foreach(var(peer,i)in peers.Select((peer,i)=>(peer,i)))
            {var id="fixture:ahn-pick-"+i;b.AddSkill(new(id,"其他角色","固定公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>1000000d*(3-i))});b.AddGeneral(new(peer,"真实其他角色","supporter",id,"wei",12,[Quiet]));}
            foreach(var(id,kind)in new[]{("wine",CardKind.Alcohol),("fire",CardKind.FireSlash),("thunder",CardKind.ThunderSlash)})b.AddCard(new("fixture:ahn-"+id,id,"真实基本牌","原生实体",kind));
            var card=scenario switch{"fire"or"native-loss"or"historical"=>"fixture:ahn-fire","thunder"=>"fixture:ahn-thunder","peach"or"full-peach"=>"standard:peach","third-dodge"or"dodge"=>"standard:dodge","played"or"rescue"=>"standard:slash",_=>"fixture:ahn-wine"};
            b.AddDeck(new("fixture:ahn-deck","小固定真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard(card,new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond}[i%4],i%13+1)).ToArray()});
            b.AddMode(new(Mode,"蟨蛮实际命令",4,4,scenario=="paid-death"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Renegade)]=1}:new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:ahn-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
