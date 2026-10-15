using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class ZhaoXiangChecks
{
    private const string Owner="fixture:zhao-xiang-owner",Driver="fixture:zhao-xiang-driver",Quiet="fixture:zhao-xiang-quiet",Watch="fixture:zhao-xiang-watch",Loss="fixture:zhao-xiang-loss",Mode="identity:classic-zhao-xiang-fixture";
    public static void ActualPaidPlayConversionDrawAndCumulativeRemovalCold()
    {
        var(g,r)=Start("conversion");Play(g);Need(!g.GetHumanLegalActions().Any(a=>a.ConversionSource?.SkillId=="ol:fanghun"),"Unpaid printed conversion is not actionable.");
        Use(g,"stock",[]);Play(g);var a=g.GetHumanLegalActions().First(a=>a.ConversionSource?.SkillId=="ol:fanghun" && a.TargetSeats.SequenceEqual([1]));var material=a.CardId!.Value;
        g=Cold(g,r);PlayCard(g,a);Play(g);
        var cost=E<ZhaoXiangConversionPayment>(g).Single();Need(cost.Removed==1 && cost.PhysicalCardId==material && cost.Source.OwnerSeat==0 && cost.InputKind==CardKind.Dodge && cost.OutputKind==CardKind.Slash &&
            E<ZhaoXiangConversionDrawPaidEvent>(g).Single().ActionId==cost.ActionId && E<ZhaoXiangConversionDrawPaidEvent>(g).Single().Actual==1 && Marker(g)==6,
            "One actual LongDan Dodge material spends exactly one MeiYing and draws exactly one card, even when the native Slash is dodged.");
        g=Cold(g,r);Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,()=>FuhanOffer(g));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>Selection(g));
        var frame=Root(g);Need(frame.ZhaoXiang!.Removed==6 && frame.ZhaoXiang.CumulativeRemoved==7 && frame.ZhaoXiang.Generals.Count==5,
            "Preparation sums the actual earlier one-marker conversion cost and current six-marker cost, rather than using the current marker total.");
        g=Cold(g,r);Answer(g,_=>true);Reach(g,()=>!g.ResolutionStack.Any(f=>f.Id==frame.Id));
        Need(V(g,0).MaxHp==7 && Marker(g)==0 && V(g,0).GeneralId==E<ZhaoXiangExchangeSelectedEvent>(g).Single().GeneralId && !V(g,0).Skills!.Any(s=>s.ContentId=="ol:fuhan") &&
            E<ZhaoXiangExchangeRecoveryIssuedEvent>(g).Count(e=>e.FrameId==frame.Id)==1 && E<ProgramBindingResolvedEvent>(g).Count(e=>e.FrameId==frame.Id)==1,
            "Real selected Shu template replaces printed identity/skills; max HP uses cumulative removed seven, and the already paid old-source native recovery tail returns once.");_=Cold(g,r);
    }
    public static void ActualZeroMarkerFiveShuPrivateReplacementAndLowerClamp()
    {
        var(g,r)=Start("zero");Reach(g,()=>FuhanOffer(g));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>Selection(g));var f=Root(g);var ids=f.ZhaoXiang!.Generals.ToArray();
        Need(f.ZhaoXiang.Removed==0 && f.ZhaoXiang.CumulativeRemoved==0 && ids.Length==5 && ids.Distinct().Count()==5 && ids.All(id=>r.Generals[id].FactionId=="shu") &&
            P(g)!.ValidContentIds.SequenceEqual(ids) && Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null),
            "Current wording permits zero markers; actual random five distinct available Shu templates remain a private owner-only choice.");
        var p=P(g)!;var revision=g.Revision;Need(!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted && g.Revision==revision,"A foreign actor cannot select the paid private general pool.");
        g=Cold(g,r);Need(Root(g).ZhaoXiang!.Generals.SequenceEqual(ids),"Cold replay freezes the original random templates instead of sampling again.");Answer(g,_=>true);Reach(g,()=>!g.ResolutionStack.Any(x=>x.Id==f.Id));
        Need(V(g,0).MaxHp==2 && ids.Contains(V(g,0).GeneralId) && V(g,0).GeneralId!=Owner && E<ZhaoXiangExchangeRecoveryIssuedEvent>(g).Count(e=>e.FrameId==f.Id)==1 &&
            E<ProgramBindingResolvedEvent>(g).Count(e=>e.FrameId==f.Id)==1,"Zero-cost replacement genuinely changes template and clamps max HP to two with one paid native tail.");_=Cold(g,r);
    }
    private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static int Marker(GameEngine g)=>V(g,0).Markers?.FirstOrDefault(m=>m.Kind==PlayerMarkerKind.MeiYing)?.Count??0;
    private static ProgramSkillFrame Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.ZhaoXiang is not null);
    private static bool Selection(GameEngine g)=>g.ResolutionStack.LastOrDefault() is ProgramSkillFrame{ZhaoXiang:{Stage:"selection"}} && P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="zhao-xiang-exchange")==true;
    private static bool FuhanOffer(GameEngine g)=>P(g)?.SkillPrompt?.SkillId=="ol:fuhan" && P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(c);Need(result.Accepted,(result.Error?.Message??"Rejected actual Zhao Xiang command.")+" "+Diagnostic(g));}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Use(GameEngine g,string binding,IReadOnlyList<int> targets)=>Accept(g,new UseProgramSkillCommand(0,Driver,binding,[],targets,g.Revision,P(g)!.PromptId));
    private static void PlayCard(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<180;i++)
        {
            if(done())return;var p=P(g);
            if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Bounded Zhao Xiang fixture did not reach expected native boundary. "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.Winner,Prompt=P(g),Frames=g.ResolutionStack,Players=Enumerable.Range(0,4).Select(s=>V(g,s)),Recent=g.Events.TakeLast(12).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay retains exact paid conversion, sampled candidates and replacement recovery cursor without re-paying.");return restored;}
    private static string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-zhao-xiang."+suffix)!;using var r=new StreamReader(s);return r.ReadToEnd();}
    private static (GameEngine,ContentRegistry)Start(string scenario)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario.EndsWith("death",StringComparison.Ordinal)?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("zhao-xiang-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,_=>id==Quiet?10000d:-10000d)});}
            Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
            var stock=scenario is "upper" or "recovery-death"?12:7;
            var defs=new List<string>{$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
                $$"""{"id":"{{Driver}}","revision":1,"activations":[{"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setMarkerAmount","target":"owner","marker":"meiYing","amount":{{stock}}}]},{"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":8}]},{"id":"non-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]}"""};
            var ownerSkills=new List<string>{"ol:fanghun","ol:fuhan"};
            if(scenario.StartsWith("conversion-",StringComparison.Ordinal))
            {
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.zhao-xiang.conversion-draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}""");ownerSkills.Add(Watch);
                if(scenario=="conversion-loss")
                {defs.Add($$"""{"id":"{{Loss}}","revision":1,"triggers":[{"id":"recover-lose","window":"dyingEntering","subject":"owner","optional":false,"priority":1000,"usageScope":"game","usageLimit":1,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:fanghun"],"sourceBind":"standard:none"}]}]}""");ownerSkills.Add(Loss);}
            }
            if(scenario=="recovery-death")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-recovery-child","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}""");
            var presentations=defs.Select(d=>(Id:JsonSerializer.Deserialize<JsonElement>(d).GetProperty("id").GetString()!,Choice:d.Contains("chooseOption",StringComparison.Ordinal))).ToDictionary(d=>d.Id,d=>new{name=d.Id,description="真实赵襄原生边界夹具",optionLabels=d.Choice?new Dictionary<string,string>{["continue"]="继续"}:new Dictionary<string,string>()});
            Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=presentations})));
            b.AddGeneral(new(Owner,"真实芳魂扶汉拥有者","supporter",Driver,"shu",12,ownerSkills,GeneralGender.Female));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:zhao-xiang-peer-{i}").ToArray();foreach(var id in peers)b.AddGeneral(new(id,"安静同桌","supporter",scenario=="response"?Driver:Quiet,"wei",12));
            var templates=Enumerable.Range(0,8).Select(i=>$"fixture:zhao-xiang-shu-{i}").ToArray();foreach(var id in templates)b.AddGeneral(new(id,"真实蜀替身"+id[^1],"supporter",scenario=="recovery-death"?Watch:"standard:none","shu",4));
            b.AddDeck(new("fixture:zhao-xiang-deck","固定实体基本牌",4,0,[]){PhysicalCards=Enumerable.Range(0,96).Select(_=>new ContentDeckPhysicalCard(scenario is "slash" or "response"?"standard:slash":"standard:dodge",Suit.Club,7)).ToArray()});
            b.AddMode(new(Mode,"真实赵襄固定夹具",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:zhao-xiang-deck",GeneralCandidateCount:12,GeneralPoolIds:[Owner,..peers,..templates]));
        }
    }
    private sealed class ProviderFixture:IGameContentPackage
    {
        internal const string Mode="identity:classic-zhao-xiang-provider",Lord="fixture:zhao-xiang-request-lord",Provider="fixture:zhao-xiang-request-provider",Request="fixture:zhao-xiang-jijiang",Stock="fixture:zhao-xiang-provider-stock";
        public PackageManifest Manifest{get;}=new("zhao-xiang-provider-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],
                Tags=id==Request?SkillTag.Lord:SkillTag.None,SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>id==Stock?(role==Role.Loyalist?20000d:-30000d):id==Quiet?10000d:-10000d)});}
            Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Request}}","revision":1,"cardPolicies":[{"id":"allied-slash","kind":"factionResponseRequest","requiredCardKinds":["slash"],"factionId":"shu","ownerRole":"lord"}],"activations":[
                {"id":"request-shu-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingSlashable","usesPerTurn":null,"condition":{"kind":"classicIdentityMode"},"effects":[{"op":"requestFactionCard","target":"selectedTarget","providerFactionId":"shu","requiredKind":"slash"}]},
                {"id":"empty-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","minimumCards":4,"maximumCards":4,"zones":["hand"],"resultBind":"target-hand"},{"op":"moveBoundCards","target":"owner","sourceBind":"target-hand","destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"{{Stock}}","revision":1,"triggers":[{"id":"initial-markers","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"setMarkerAmount","target":"owner","marker":"meiYing","amount":7}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            Add(SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Request]=new{name="真实激将请求",description="原生请求及真实清手付款"},[Stock]=new{name="真实初始梅影",description="原生初始标记"},[Quiet]=new{name="静默同桌",description="跳过出牌"}}})));
            b.AddGeneral(new(Lord,"真实请求主公","supporter",Request,"shu",12));b.AddGeneral(new(Provider,"真实芳魂供牌者","supporter",Stock,"shu",12,["ol:fanghun",Quiet],GeneralGender.Female));
            var peers=new[]{"fixture:zhao-xiang-request-peer-1","fixture:zhao-xiang-request-peer-2"};foreach(var id in peers)b.AddGeneral(new(id,"魏同桌","supporter",Quiet,"wei",12));
            b.AddDeck(new("fixture:zhao-xiang-provider-deck","固定供牌实体",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("standard:dodge",Suit.Club,7)).ToArray()});
            b.AddMode(new(Mode,"真实激将芳魂供牌",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:zhao-xiang-provider-deck",GeneralCandidateCount:4,GeneralPoolIds:[Lord,Provider,..peers]));
        }
    }
}
