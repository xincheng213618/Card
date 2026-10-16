using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiuYanChecks
{
    private const string Owner="fixture:ly-owner",Driver="fixture:ly-driver",Quiet="fixture:ly-quiet",Health="fixture:ly-health",Watch="fixture:ly-watch",Source="fixture:ly-source",Legacy="fixture:ly-legacy",Rescue="fixture:ly-rescue",Mode="identity:classic-liu-yan-fixture";
    public static void ActualTargetsPostPaymentAndEffectiveHandCold()
    {
        foreach(var scenario in new[]{"draw","barrage"})
        {
            var(g,r)=Start(scenario);var kind=scenario=="draw"?CardKind.DrawTwo:CardKind.ArrowBarrage;var before=V(g).HandCount;Native(g,kind);
            Reach(g,()=>Optional(g));var parent=g.ResolutionStack.OfType<CardUseFrame>().Last();var declared=parent.Action!.EffectiveDesignatedTargetSeats.Count;
            Need(declared==(scenario=="draw"?1:3),"The native declaration has its real self or global role targets.");g=Cold(g,r);Activate(g);Play(g);
            var drawn=E<LiuYanTusheDrawnEvent>(g).Single();Need(drawn.Requested==declared&&drawn.Count==declared&&E<LiuYanStartedEvent>(g).Single().OriginalUseId==parent.Id,"Tushe draws once from the actual finalized role-target count after the physical use has been paid.");
            Need(V(g).HandCount>=before-1+declared&&g.CardMovements.Any(m=>m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use),"The actual native cost and reward are real card movements.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("diamond");var card=V(g).Hand.First().Id;Limu(g,card);Play(g);
            Need(E<LiuYanTusheDrawnEvent>(g).Length==0,"Unpaid remaining effective basic hand cards block Tushe even after a basic diamond was used as Indulgence.");
            Use(g,"clear-judgment");Play(g);foreach(var id in V(g).Hand.Skip(1).Select(c=>c.Id).ToArray()){Use(g,"drop",[id]);Play(g);}
            var last=V(g).Hand.Single().Id;Limu(g,last);Reach(g,()=>Optional(g));Need(V(g).HandCount==0,"Qualification reads the actual hand after the last basic material leaves it.");g=Cold(g,r);Activate(g);Play(g);
            Need(E<LiuYanTusheDrawnEvent>(g).Single().Requested==1&&E<LiuYanLimuReturnedEvent>(g).Length==2,"Self-targeted Limu can truly trigger Tushe, and both native uses return once.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("equipment");Native(g,CardKind.Crossbow);Play(g);Need(E<LiuYanStartedEvent>(g).Length==0,"An actual equipment use with no basic hand cards does not trigger Tushe.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int s=0)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static bool Optional(GameEngine g)=>P(g)?.SkillPrompt?.SkillId=="ol:tushe"&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(g.ResolutionStack)+JsonSerializer.Serialize(g.CreateSnapshot(0))+g.Events.Count;
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected actual Liu Yan command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Need(V(g,p.PlayerSeat).IsHuman,"The real AI advances its own decisions.");var c=p.Choices.FirstOrDefault(pick);Need(c is not null,"Missing actual Liu Yan choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]? cards=null,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Limu(GameEngine g,int id)=>Accept(g,new UseProgramSkillCommand(0,"ol:limu","self-indulgence",[id],[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,CardKind kind,int? target=null)
    {var a=g.GetHumanLegalActions().FirstOrDefault(a=>(a.PlayedCardKind??(a.CardId is{}id?V(g).Hand.FirstOrDefault(c=>c.Id==id)?.Kind:null))==kind&&(target is null||a.TargetSeats.SequenceEqual([target.Value])));Need(a is not null,"Missing native "+kind+": "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static string Diagnostic(GameEngine g)=>$"turn={g.State.TurnNumber};winner={g.State.Winner};prompt={P(g)?.Kind}/{P(g)?.PlayerSeat}/{P(g)?.SkillPrompt?.SkillId};stage={g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.LiuYan is not null)?.LiuYan?.Stage};hand={string.Join(',',V(g).Hand.Select(c=>c.Kind))}";
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<180;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Missing actual Liu Yan boundary: "+Diagnostic(g));var p=P(g);
            if(p is not null&&!V(g,p.PlayerSeat).IsHuman)Accept(g,new AdvanceOneStepCommand(g.Revision));
            else if(p is{Kind:DecisionKind.PlayCard})Accept(g,new EndPlayPhaseCommand(0,g.Revision,p.PromptId));
            else if(p is{Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Cards.Count==0);
            else if(p is{Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")==true)Continue(g);
            else if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")==true)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p is not null)throw new InvalidOperationException("Unexpected actual Liu Yan boundary: "+Diagnostic(g));
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing bounded Liu Yan boundary: "+Diagnostic(g));
    }
    private static GameEngine Cold(GameEngine g,ContentRegistry r)
    {var next=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(next),"Cold replay preserves the exact original source, physical material, target count and once-only native return.");return next;}
    private static(GameEngine,ContentRegistry)Start(string scenario)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario=="death"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));if(scenario=="legacy-slash")Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Legacy);else Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-liu-yan",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:liu-yan");
            var defs=new List<string>
            {
                $$$"""
                {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"fatal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":9}]},
                {"id":"drop","minCards":1,"maxCards":1,"sourceZones":["hand","equipment"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                {"id":"clear-judgment","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["judgment"]}]}]}
                """,
                $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
                $$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"actual-hp","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}"""
            };
            var skills=new List<string>{Driver,Health,"ol:tushe"};
            if(scenario=="legacy-slash"){defs.Add($$"""{"id":"{{Legacy}}","revision":1,"triggers":[{"id":"phase-substitution","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"skipTurnPhases","target":"owner","phases":["judgment","draw"]},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget"}]}]}""");skills.Add(Legacy);}
            if(scenario=="legacy-wine"){defs.Add($$"""{"id":"{{Rescue}}","revision":1,"triggers":[{"id":"self-wine","window":"selfDyingResponse","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"useVirtualDyingAlcohol","target":"owner"}]}]}""");skills.Add(Rescue);}
            if(scenario=="loss"){defs.Add($$"""{"id":"{{Source}}","revision":1,"hpSkillGrants":[{"id":"full","skillId":"ol:limu","minimumHp":9}]}""");skills.Add(Source);}else skills.Add("ol:limu");
            if(scenario is"loss"or"death")
            {
                var tail=scenario=="loss"?"{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}":"{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}";
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"native-paid","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["indulgence"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{{tail}}]}]}""");skills.Add(Watch);
            }
            var nodes=defs.Select(d=>JsonNode.Parse(d)!).ToArray();var labels=nodes.ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实原生刘焉小夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x=>x,x=>x)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,labels[id].name,"实际原生夹具"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:ly-pick-owner","本人","固定选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-100000000d)});b.AddGeneral(new(Owner,"实际刘焉","ol-liu-yan","fixture:ly-pick-owner","qun",8,skills));
            var peers=Enumerable.Range(0,3).Select(i=>"fixture:ly-peer-"+i).ToArray();foreach(var(peer,i)in peers.Select((peer,i)=>(peer,i)))
            {var pick="fixture:ly-pick-"+i;b.AddSkill(new(pick,"其他角色","固定公开选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>1000000d*(3-i))});b.AddGeneral(new(peer,"实际其他角色","supporter",pick,"wei",12,[Quiet]));}
            foreach(var(id,kind)in new[]{("barrage",CardKind.ArrowBarrage),("lion",CardKind.SilverLion),("ox",CardKind.WoodenOx),("crossbow",CardKind.Crossbow),("sword",CardKind.QinggangSword),("nullification",CardKind.Nullification),("snatch",CardKind.Snatch)})b.AddCard(new("fixture:ly-"+id,id,"真实牌","原生实体",kind));
            var ids=scenario switch{"draw"or"legacy-slash"or"legacy-wine"=>new[]{"standard:draw_two"},"barrage"=>["fixture:ly-barrage"],"equipment"=>["fixture:ly-crossbow"],"lion"=>["fixture:ly-lion"],"ox"or"grain"=>["fixture:ly-ox"],"range"=>["standard:slash","standard:dodge"],"distance"=>["fixture:ly-sword","standard:dodge","fixture:ly-snatch"],"counterspell"=>["fixture:ly-nullification"],_=>["standard:dodge"]};
            b.AddDeck(new("fixture:ly-deck","固定真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,160).Select(i=>new ContentDeckPhysicalCard(ids[i%ids.Length],Suit.Diamond,i%13+1)).ToArray()});
            b.AddMode(new(Mode,"真实图射立牧",4,4,scenario=="death"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Renegade)]=1}:new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Renegade)]=3},"fixture:ly-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
