using System.IO;
using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class PopulationTopReorderScenario
{
    internal const string Skill = "boundary:guanxing-current";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static (GameEngine Game, ContentRegistry Registry) Start(int players=4,int remaining=32,int draw=0,string mode="",bool pausePrepare=true)
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(players,remaining,draw,mode));
        var game=GameEngine.CreateStandard(new GameOptions {Seed=31,PlayerCount=players,HumanSeat=0,HumanRole=Role.Lord,
            ModeId="identity:classic-population",UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=6},registry);
        Accept(game,new StartGameCommand());Accept(game,new SelectGeneralCommand(0,mode is "scheduled-play" or "scheduled-draw" or "extra" or "death" || mode.StartsWith("population-",StringComparison.Ordinal)?"fixture:population-owner":"boundary:zhuge-liang",game.Revision,Pending(game)!.PromptId));
        if(pausePrepare)Reach(game,p=>p.SkillPrompt?.SkillId==Skill);return(game,registry);
    }
    internal static PendingDecision? Pending(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,g.CreateSnapshot(0).Players.Count).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p!=null);
    internal static ProgramSkillFrame Frame(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Last();
    internal static object Zones(GameEngine g)=>typeof(GameEngine).GetField("_cardZones",Flags)!.GetValue(g)!;
    internal static CharacterState[] Players(GameEngine g)=>((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",Flags)!.GetValue(g)!).ToArray();
    internal static int[] Pile(GameEngine g)=>((IReadOnlyList<Card>)Zones(g).GetType().GetMethod("CardsAt")!.Invoke(Zones(g),[CardLocation.DrawPile])!).Reverse().Select(c=>c.Id).ToArray();
    internal static bool Eligible(GameEngine g,string? instance=null)=>(bool)typeof(GameEngine).GetMethod("GetProgramBooleanState",Flags,null,[typeof(int),typeof(string),typeof(string),typeof(string)],null)!.Invoke(g,
        [0,Skill,instance??Players(g)[0].SkillGrants.Grants.Single(x=>x.SkillId==Skill).SkillInstanceId,"eligible-end-reorder"])!;
    internal static string? Action(PromptChoice c)=>c.Parameters.GetValueOrDefault("program-action");
    internal static void Accept(GameEngine g,GameCommand command){var r=g.Submit(command);if(!r.Accepted)Capture(g,command);Require(r.Accepted,r.Error?.Message??"Command rejected.");}
    internal static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=Pending(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    internal static void Activate(GameEngine g)=>Answer(g,c=>Action(c)=="activate");
    internal static void Skip(GameEngine g)=>Answer(g,c=>Action(c)=="skip");
    internal static void Reach(GameEngine g,Func<PendingDecision,bool> goal){for(var i=0;i<96;i++){if(Pending(g)is{}p&&goal(p))return;Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Fixed population fixture did not reach prompt: "+Pending(g)?.Prompt);}
    internal static void Play(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    internal static void End(GameEngine g){var p=Pending(g)!;Accept(g,new EndPlayPhaseCommand(p.PlayerSeat,g.Revision,p.PromptId));}
    internal static void AdvanceUntil(GameEngine g,Func<GameEngine,bool> goal){for(var i=0;i<96;i++){if(goal(g))return;var p=Pending(g);if(p?.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Cards.Count==0);else if(p?.Kind==DecisionKind.DiscardCards)Accept(g,new DiscardCardsCommand(p.PlayerSeat,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));else Accept(g,new AdvanceOneStepCommand(g.Revision));}Capture(g);throw new InvalidOperationException("Fixed progression did not reach goal: "+Pending(g)?.Kind+" / "+Pending(g)?.Prompt);}
    internal static void FinishBottom(GameEngine g,ContentRegistry? r=null){Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="finish-top");while(Pending(g)?.Kind==DecisionKind.ProgramTopReorder){if(r!=null)Replay(g,r);Answer(g,c=>c.Cards.Count==1);}}
    internal static void Replay(GameEngine g,ContentRegistry r)
    {
        var rev=g.Revision;var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);
        Require(JsonSerializer.Serialize(g.CreateCheckpoint().Commands)==JsonSerializer.Serialize(restored.CreateCheckpoint().Commands),"Cold prefix preserves accepted commands.");
        foreach(var viewer in Enumerable.Range(0,g.CreateSnapshot(0).Players.Count).Append(-1))Require(JsonSerializer.Serialize(g.CreateSnapshot(viewer))==JsonSerializer.Serialize(restored.CreateSnapshot(viewer)),"Every actual viewer and spectator matches cold restoration.");
        Require(Pile(g).SequenceEqual(Pile(restored))&&g.Revision==rev,"Cold exact pile and pure projection remain stable.");
    }
    internal static void Witness(GameEngine g,string name)
    {
        if(Environment.GetEnvironmentVariable("CARD_POPULATION_EVIDENCE") is not {} root)return;
        var dir=Path.Combine(root,name);if(Directory.Exists(dir))throw new InvalidOperationException("Witness output must be new: "+dir);Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"accepted-prefix.checkpoint.json"),GameCheckpointJson.Serialize(g.CreateCheckpoint()));
        File.WriteAllText(Path.Combine(dir,"trusted-stack.json"),JsonSerializer.Serialize(g.ResolutionStack.Select(f=>new{f.Id,f.Kind,Type=f.GetType().Name,TypedFrame=JsonSerializer.Serialize(f,f.GetType())})));
        File.WriteAllText(Path.Combine(dir,"actual-views.json"),JsonSerializer.Serialize(Enumerable.Range(0,g.CreateSnapshot(0).Players.Count).Append(-1).ToDictionary(v=>v,v=>g.CreateSnapshot(v))));
        Console.WriteLine("Actual command witness: "+name+" revision="+g.Revision);
    }
    internal static void Capture(GameEngine g,GameCommand? attempt=null)
    {
        if(Environment.GetEnvironmentVariable("CARD_POPULATION_EVIDENCE") is not {} root)return;
        var dir=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"accepted-prefix.checkpoint.json"),GameCheckpointJson.Serialize(g.CreateCheckpoint()));
        File.WriteAllText(Path.Combine(dir,"trusted-stack.json"),JsonSerializer.Serialize(g.ResolutionStack.Select(f=>new{f.Id,f.Kind,Type=f.GetType().Name,TypedFrame=JsonSerializer.Serialize(f,f.GetType())})));
        File.WriteAllText(Path.Combine(dir,"attempt.json"),attempt is null?"null":JsonSerializer.Serialize(attempt,attempt.GetType()));
        File.WriteAllText(Path.Combine(dir,"owner-view.json"),JsonSerializer.Serialize(g.CreateSnapshot(0)));
        Console.WriteLine("Frozen prefix evidence: "+dir);
    }
    internal static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    internal sealed class Fixture(int players,int remaining,int draw,string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:population",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var a=typeof(StandardContentPackage).Assembly;
            var classic=(ContentSkillDefinition)a.GetType("CardGame.Content.Standard.EmbeddedSkillProgramCatalog")!.GetMethod("Definition",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,["passive-card-rules","classic:kongcheng"])!;
            b.AddSkill(classic with{Tags=SkillTag.Locked,ExecutionForms=SkillExecutionForm.State});
            a.GetType("CardGame.Content.Standard.BoundaryZhugeLiangContent")!.GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[b]);
            var defs=new List<string>();
            if(mode.StartsWith("population-",StringComparison.Ordinal))
            {
                var deaths=4-int.Parse(mode["population-".Length..]);
                var triggers=string.Join(',',Enumerable.Range(1,deaths).Select(n=>$$"""{"id":"reduce-{{n}}","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"priority":40,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"loseHp","target":"selectedTarget","amount":20}]}"""));
                defs.Add($$"""{"id":"fixture:population-reduction","revision":1,"triggers":[{{triggers}}]}""");
            }
            if(mode is "scheduled-play" or "scheduled-draw")defs.Add(mode=="scheduled-play"
                ? """{"id":"fixture:population-schedule","revision":1,"triggers":[{"id":"schedule","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"priority":40,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}"""
                : """{"id":"fixture:population-schedule","revision":1,"triggers":[{"id":"schedule","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"priority":40,"effects":[{"op":"replaceJudgmentPhase","target":"owner"}]}]}""");
            if(mode is "extra" or "death")defs.Add(mode=="extra"
                ? """{"id":"fixture:population-ending","revision":1,"triggers":[{"id":"extra","window":"turnEnding","subject":"owner","optional":true,"priority":-1,"usageScope":"game","usageLimit":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}]}"""
                : """{"id":"fixture:population-ending","revision":1,"triggers":[{"id":"die","window":"turnEnding","subject":"owner","optional":true,"priority":40,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]}""");
            if(defs.Count>0)
            {
                var presentation=JsonSerializer.Serialize(new{schemaVersion=3,skills=defs.Select(d=>JsonDocument.Parse(d).RootElement.GetProperty("id").GetString()!).ToDictionary(id=>id,id=>new{name=id,description=id})});
                var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",presentation);
                foreach(var(id,program)in catalog.Programs)b.AddSkill(new(id,id,id){Program=program});
                b.AddGeneral(new("fixture:population-owner","附加时序探针","supporter",Skill,"shu",3,defs.Select(d=>JsonDocument.Parse(d).RootElement.GetProperty("id").GetString()!).ToArray()));
            }
            for(var i=1;i<players;i++)b.AddGeneral(new($"fixture:population-{i}","其他"+i,"supporter",mode=="ai"?Skill:"standard:none","wei",mode=="ai"?2:3));
            b.AddDeck(new("fixture:population-deck","固定",4,draw,[]){PhysicalCards=Enumerable.Range(0,players*4+remaining).Select(i=>new ContentDeckPhysicalCard("standard:dodge",new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond}[i%4],i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-population","固定",players,players,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),players-1}},"fixture:population-deck",GeneralCandidateCount:players,
                GeneralPoolIds:[defs.Count>0?"fixture:population-owner":"boundary:zhuge-liang",..Enumerable.Range(1,players-1).Select(i=>$"fixture:population-{i}")]));
        }
    }
}
