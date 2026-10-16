using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class BuZhiChecks
{
    private const string Owner="fixture:bu-zhi-owner",Quiet="fixture:bu-zhi-quiet",Driver="fixture:bu-zhi-driver",Watch="fixture:bu-zhi-watch",Loss="fixture:bu-zhi-loss",Mode="identity:classic-bu-zhi-fixture";
    public static void ActualNativeHandGainAndWholeHandLossOfferOnce()
    {
        var(g,r)=Start("gain");Play(g);var before=E<BuZhiStartedEvent>(g).Count();
        var a=g.GetHumanLegalActions().First(a=>a.CardId is{}id && V(g,0).Hand.Any(c=>c.Id==id && c.Kind==CardKind.DrawTwo) && a.TargetSeats.Count==0);
        Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId));Reach(g,()=>Stage(g,"beneficiary"));
        var id=Root(g).Id;Need(Root(g).BuZhi!.BatchId is not null && P(g)!.Choices.All(c=>c.Targets.Count==0 || c.Targets[0]!=0),"Native two-card hand gain offers one optional other-player benefit, never self.");
        var prompt=P(g)!;var revision=g.Revision;Need(!g.Submit(new AnswerPromptCommand(1,prompt.PromptId,prompt.Choices[0].Id,g.Revision)).Accepted && revision==g.Revision,"A foreign player cannot select the owner benefit.");
        g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));Play(g);
        Need(E<BuZhiStartedEvent>(g).Count()==before+1 && E<BuZhiMovementPaidEvent>(g).Count(e=>e.FrameId==id)==1 && E<ProgramBindingResolvedEvent>(g).Count(e=>e.FrameId==id)==1,"Actual gain payment issues the chosen draw once and returns its binding once.");
        var starts=E<BuZhiStartedEvent>(g).Count();Use(g,Driver,"dump",[]);
        Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards")==true);
        while(P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards")==true)
            Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards");
        if(P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards")==true)
            Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards");
        Reach(g,()=>Stage(g,"beneficiary"));
        Need(E<BuZhiStartedEvent>(g).Count()==starts+1 && Root(g).WindowContext!.MovementBatch!.Movements.Count(m=>m.From==CardLocation.Hand(0))>=2,"Whole-hand real payment qualifies as one loss batch, without net-hand approximation.");
        g=Cold(g,r);Answer(g,c=>Branch(c,"decline"));Play(g);Need(E<BuZhiStartedEvent>(g).Count()==starts+1,"Declining consumes only this native batch offer.");
        Use(g,Driver,"refill",[]);Play(g);Need(V(g,0).HandCount==2,"A real separate native draw invocation refills exactly two hand entities after atomic loss.");
        var syncStarts=E<BuZhiStartedEvent>(g).Count();Use(g,Driver,"sync-dump",[]);Reach(g,()=>Stage(g,"beneficiary"));var syncId=Root(g).Id;
        Need(V(g,0).HandCount==0 && E<ProgramOwnedZoneCardsDiscardedEvent>(g).Any(e=>e.OwnerSeat==0 && e.BindingId=="sync-dump" && e.CardCount==2) &&
            Root(g).WindowContext!.MovementBatch!.Movements.Count==1 && E<BuZhiStartedEvent>(g).Count()==syncStarts+1,
            "One real synchronous owned-zone discard API call loses two entities and offers once at its final original one-card batch, without merging historical batches.");
        g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));Play(g);
        Need(E<BuZhiMovementPaidEvent>(g).Count(e=>e.FrameId==syncId)==1 && E<ProgramBindingResolvedEvent>(g).Count(e=>e.FrameId==syncId)==1 && E<BuZhiStartedEvent>(g).Count()==syncStarts+1,
            "Synchronous native loss invocation retains its once-only entitlement, one paid benefit and one exact trigger return through cold restoration.");
    }
    private static bool Branch(PromptChoice c,string b)=>c.Parameters.GetValueOrDefault("branch")==b;
    private static ProgramSkillFrame Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.BuZhi is not null);
    private static bool Stage(GameEngine g,string s)=>g.ResolutionStack.LastOrDefault() is ProgramSkillFrame{BuZhi:not null}f && f.BuZhi.Stage==s && P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="bu-zhi")==true;
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(c);Need(result.Accepted,(result.Error?.Message??"Rejected actual Bu Zhi command.")+" "+Diagnostic(g));}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Use(GameEngine g,string skill,string binding,IReadOnlyList<int> targets)=>Accept(g,new UseProgramSkillCommand(0,skill,binding,[],targets,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<160;i++)
        {
            if(done())return;var p=P(g);
            if(p?.Choices.Any(c=>Branch(c,"decline"))==true && p.PlayerSeat==0)Answer(g,c=>Branch(c,"decline"));
            else if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fixed native Bu Zhi fixture did not reach its boundary. "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.Winner,Prompt=P(g),Frames=g.ResolutionStack,Players=Enumerable.Range(0,4).Select(s=>V(g,s)),Recent=g.Events.TakeLast(12).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay retains actual private chooser and paid native receipt without replaying payment.");return restored;}
    private static string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-bu-zhi."+suffix)!;using var r=new StreamReader(s);return r.ReadToEnd();}
    private static (GameEngine,ContentRegistry)Start(string scenario="normal")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario.EndsWith("death",StringComparison.Ordinal)?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=6},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("bu-zhi-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog cat){foreach(var(id,p)in cat.Programs)b.AddSkill(new(id,cat.Presentations[id].Name,cat.Presentations[id].Description){Program=p,ProgramPresentation=cat.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,_=>id==Quiet?10000d:-10000d)});}
            Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
            var defs=new List<string>{$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
                $$"""{"id":"{{Driver}}","revision":1,"activations":[{"id":"dump","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":2,"maximumCards":null,"zones":["hand"],"resultBind":"dumped"},{"op":"moveBoundCards","target":"owner","sourceBind":"dumped","destination":"discardPile","awaitMovementTriggers":true}]},{"id":"equip-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},{"id":"sync-dump","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},{"id":"refill","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2}]},{"id":"kill-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]}]}"""};
            var skills=new List<string>{"ol:hongde","ol:dingpan"};
            if(scenario.StartsWith("source-",StringComparison.Ordinal))
            {
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.bu-zhi.draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}""");skills.Add(Watch);
                if(scenario=="source-loss")
                {defs.Add($$"""{"id":"{{Loss}}","revision":1,"triggers":[{"id":"recover-and-lose","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:dingpan"],"sourceBind":"standard:none"}]}]}""");skills.Add(Loss);}
            }
            var presentations=defs.Select(d=>(Id:JsonSerializer.Deserialize<JsonElement>(d).GetProperty("id").GetString()!,Choice:d.Contains("chooseOption",StringComparison.Ordinal))).ToDictionary(d=>d.Id,d=>new{name=d.Id,description="原生步骘固定夹具",optionLabels=d.Choice?new Dictionary<string,string>{["continue"]="继续"}:new Dictionary<string,string>()});
            Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=presentations})));
            b.AddGeneral(new(Owner,"真实步骘能力拥有者","supporter",Driver,"wu",scenario=="self-death"?1:12,skills));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:bu-zhi-peer-{i}").ToArray();foreach(var id in peers)b.AddGeneral(new(id,"普通同桌","supporter",Quiet,"wei",12));
            b.AddDeck(new("fixture:bu-zhi-deck","固定四牌",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard(scenario=="gain"?"standard:draw_two":"standard:crossbow",Suit.Club,7)).ToArray()});
            b.AddMode(new(Mode,"原生步骘固定夹具",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:bu-zhi-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
