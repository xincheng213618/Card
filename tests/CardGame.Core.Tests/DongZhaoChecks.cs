using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class DongZhaoChecks
{
    private const string TeamMode = "fixture:dong-team";
    private const string Owner = "fixture:dong-owner", Quiet = "fixture:dong-quiet", Driver = "fixture:dong-driver", Mode = "identity:classic-dong-fixture";
    public static void ActualZaowangPrivateGrantAndLoyalistSuccessionCold()
    {
        var (g,r) = Start(); Play(g);
        var heir = Enumerable.Range(1,3).Single(s => V(g,s).Role == Role.Loyalist);
        var hp = V(g,heir).MaxHp; var hand = V(g,heir).HandCount;
        Use(g,"ol:zaowang","make-king",[heir]); Play(g); g = Cold(g,r);
        Need(V(g,heir).MaxHp == hp + 1 && V(g,heir).Hp == hp + 1 && V(g,heir).HandCount == hand + 3,
            "One native selected recipient gains the exact maximum HP, recovery and three draws.");
        Need(g.CreateSnapshot(0).Players[heir].Role is null && !g.Events.Any(e => e.Payload is RoleRevealedEvent x && x.Seat == heir),
            "A private loyalist identity is not revealed by the public grant.");
        Use(g,Driver,"die",[]); Reach(g, () => E<IdentityExchangedEvent>(g).Any()); g = Cold(g,r);
        Need(V(g,heir).Role == Role.Lord && g.CreateSnapshot(1).Players[heir].Role == Role.Lord && V(g,0).Role == Role.Loyalist && !V(g,0).IsAlive && g.State.Winner == Winner.None,
            "The actual dying lord exchanges roles with the living armed loyalist before native winner evaluation.");
        Need(E<IdentityExchangedEvent>(g).Count() == 1 && E<PlayerDiedEvent>(g).Single().KillerSeat is null,
            "Cold recovery preserves one exact native death and source-free HP loss invents no killer.");
    }
    public static void ActualZaowangRebelKillerVictoryAndUnarmedControlCold()
    {
        foreach(var armed in new[]{true,false})
        {
            var (g,r) = Start(); Play(g); var rebel = Enumerable.Range(1,3).First(s => V(g,s).Role == Role.Rebel);
            if(armed) { Use(g,"ol:zaowang","make-king",[rebel]); Play(g); g = Cold(g,r); }
            Use(g,Driver,"kill",[rebel]); Reach(g, () => E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==rebel));
            g = Cold(g,r);
            Need(E<PlayerDiedEvent>(g).Single(e=>e.VictimSeat==rebel).KillerSeat == 0 && g.State.Winner == (armed?Winner.LordAndLoyalists:Winner.None),
                "Only an armed actual rebel killed by the real lord causes the special victory; another living rebel makes the unarmed control continue.");
            Need(armed ? E<WinnerDeterminedEvent>(g).Count()==1 : !E<WinnerDeterminedEvent>(g).Any(),"The native winner fact is emitted once only.");
        }
    }
    public static void ActualForeignLordTurnRecordAndOwnNonLordBoundaryCold()
    {
        var(g,r)=Start(humanRole:Role.Rebel);
        Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="ol:xianlue" && P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        Need(g.State.CurrentSeat!=0 && V(g,g.State.CurrentSeat).Role==Role.Lord,"A non-lord Dong Zhao receives the actual other lord turn-start record window.");
        g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="xianlue")==true);g=Cold(g,r);
        Answer(g,c=>c.Parameters["value"]==nameof(CardKind.Duel));Play(g);g=Cold(g,r);
        Need(E<XianlueRecordedEvent>(g).Count()==1 && g.State.CurrentSeat==0 && V(g,0).Role==Role.Rebel,
            "The other lord record completes once; the later own non-lord turn does not offer or consume another record.");
    }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool b,string message){if(!b)throw new InvalidOperationException(message);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Dong command rejected.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(pick).Id,g.Revision));}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g) is {PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Use(GameEngine g,string skill,string id,IReadOnlyList<int> targets)=>Accept(g,new UseProgramSkillCommand(0,skill,id,[],targets,g.Revision,P(g)!.PromptId));
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<100;i++)
        {
            if(done())return;var p=P(g);Need(g.State.Winner==Winner.None,"Dong fixture ended before its expected native boundary.");
            if(p is {PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p is {PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Dong bounded native fixture exhausted. "+JsonSerializer.Serialize(P(g)));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(cold),"Serialized accepted commands reconstruct private grants, native death and exactly paid benefits.");return cold;}
    private static (GameEngine,ContentRegistry) Start(bool team = false, string scenario = "normal", Role humanRole = Role.Lord)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=team ? null : humanRole,ModeId=team ? TeamMode : Mode,HumanTeamId=team ? "team:dong-a" : null,UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,UseInteractiveDiscard=false,MaxTurns=6},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g) is {Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("dong-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            string Embedded(string suffix) { using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-dong-zhao.{suffix}")!;using var reader=new StreamReader(s);return reader.ReadToEnd(); }
            var production=SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json"));
            foreach(var(id,program)in production.Programs)b.AddSkill(new(id,production.Presentations[id].Name,production.Presentations[id].Description)
            { Program=program,ProgramPresentation=production.Presentations[id],Tags=id=="ol:zaowang"?SkillTag.Limited:SkillTag.None });
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"activations":[{"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]},
            {"id":"challenge","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
            {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]}]},
            {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var node=JsonNode.Parse(rules)!;
            if(scenario != "normal") node["skills"]!.AsArray().Add(JsonNode.Parse($$"""
            {"id":"fixture:dong-watch","revision":1,"triggers":[{"id":"paid-child","window":"afterHpRecovered","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"watch","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}
            """));
            if(scenario=="paid-loss")node["skills"]!.AsArray().Add(JsonNode.Parse("""
            {"id":"fixture:dong-loss","revision":1,"triggers":[{"id":"source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"priority":1000,
            "effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:zaowang"],"sourceBind":"standard:none"}]}]}
            """));
            var labels=new Dictionary<string,object>{[Driver]=new{name="native death driver",description="native"},[Quiet]=new{name="quiet",description="quiet"}};
            if(scenario!="normal") labels["fixture:dong-watch"]=new{name="native paid child",description="native",optionLabels=new Dictionary<string,string>{["continue"]="continue"}};
            if(scenario=="paid-loss")labels["fixture:dong-loss"]=new{name="native dying recovery source loss",description="native"};
            var c=SkillProgramCatalog.Load(node.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,program)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=program,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,_=>id==Quiet?10000d:-10000d)});
            var ownerSkills=new List<string>{"ol:xianlue","ol:zaowang"};if(scenario!="normal")ownerSkills.Add("fixture:dong-watch");if(scenario=="paid-loss")ownerSkills.Add("fixture:dong-loss");
            b.AddGeneral(new(Owner,"董昭真实规则","supporter",Driver,"wei",12,ownerSkills,GeneralGender.Male));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:dong-peer-{i}").ToArray();foreach(var p in peers)b.AddGeneral(new(p,"other","supporter",Quiet,"wei",12));
            b.AddDeck(new("fixture:dong-deck","fixed tricks",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("standard:draw_two",Suit.Club,7)).ToArray()});
            b.AddMode(new(Mode,"native Dong identity",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:dong-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
            b.AddMode(new(TeamMode,"native Dong team",4,4,new Dictionary<string,int>(),"fixture:dong-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{["team:dong-a"]=2,["team:dong-b"]=2}));
        }
    }
}
