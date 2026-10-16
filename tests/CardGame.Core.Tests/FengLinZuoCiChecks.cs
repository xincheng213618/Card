using CardGame.Core;
using CardGame.Content.Standard;
using System.Reflection;
using System.Text.Json;
internal static class FengLinZuoCiChecks
{
    private static PendingDecision P(GameEngine g)=>g.PendingDecision??g.CreateSnapshot(0,true).PendingDecision!;
    private static void Require(bool b,string m){if(!b)throw new InvalidOperationException(m);}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(c);Require(r.Error is null,r.Error?.Message??"Rejected");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g);var c=p.Choices.FirstOrDefault(pick)??throw new InvalidOperationException("No choice: "+JsonSerializer.Serialize(p));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c.Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> stop){for(var n=0;n<90;n++){var p=P(g);if(p is not null&&stop(p))return;if(p is {PlayerSeat:0,Kind:DecisionKind.PlayCard})Accept(g,new EndPlayPhaseCommand(0,g.Revision,p.PromptId));else if(p is {PlayerSeat:0})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip"||c==p.Choices[0]);else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Missing boundary");}
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);string State(GameEngine x)=>JsonSerializer.Serialize(Enumerable.Range(0,4).Select(i=>x.CreateSnapshot(i)))+JsonSerializer.Serialize(x.ResolutionStack);Require(State(g)==State(restored),"Private libraries and owning selections replay at all four viewers.");}
    public static void OldRegistryHasNoLibraryShape()
    {
        var(_,r)=Start();var old=ContentRegistry.Build(new WithoutLibrary(r));var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:zc-fixture",UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,UseInteractiveDiscard=false},old);Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:zc-owner",g.Revision,P(g).PromptId));Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);Require(!g.Events.Any(e=>e.Payload is PrivateGeneralLibraryCountChangedEvent or PrivateGeneralLibraryDeclaredEvent),"No 1640 registry never captures new facts.");Require(!JsonSerializer.Serialize(g.CreateSnapshot(0)).Contains("PrivateGeneralLibraries"),"Null additive DTO omitted in old snapshot.");Replay(g,old);
    }
    private sealed class WithoutLibrary(ContentRegistry source):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:zc-old",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b){foreach(var c in source.Cards.Values)b.AddCard(c);foreach(var s in source.Skills.Values)b.AddSkill(s.Id is "classic:huashen" or "classic:xinsheng"?s with{Program=null}:s);foreach(var g in source.Generals.Values)b.AddGeneral(g);foreach(var d in source.Decks.Values)b.AddDeck(d);foreach(var m in source.Modes.Values)b.AddMode(m);}
    }
    private static (GameEngine,ContentRegistry) Start(bool multi=false,bool god=false,bool weidi=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(multi,god,weidi));var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=weidi?Role.Loyalist:Role.Lord,ModeId="identity:zc-fixture",UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,UseInteractiveDiscard=false,MaxTurns=4},r);Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:zc-owner",g.Revision,P(g).PromptId));return(g,r);
    }
    private sealed class Fixture(bool multi,bool god,bool weidi):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:zc",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var damage=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:zc-damage","revision":1,"activations":[{"id":"hit","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"damage","target":"owner","amount":2}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:zc-damage":{"name":"受伤夹具","description":"两点伤害"}}}""");b.AddSkill(new("fixture:zc-damage","受伤夹具","真实两点伤害"){Program=damage.Programs["fixture:zc-damage"]});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:zc-program","revision":1,"triggers":[{"id":"draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:zc-program":{"name":"原生摸牌夹具","description":"摸一张"}}}""");b.AddSkill(new("fixture:zc-program","原生摸牌夹具","摸一张"){Program=catalog.Programs["fixture:zc-program"]});b.AddSkill(new("fixture:zc-suppression","缠怨语义夹具","HP1压制其他技"){SuppressionRule=new(1),Tags=SkillTag.Locked});b.AddSkill(new("fixture:zc-limited","限定夹具","不能借"){Tags=SkillTag.Limited});
            var ids=new List<string>{"fixture:zc-owner"};b.AddGeneral(new("fixture:zc-owner","化身机制","supporter","classic:huashen","qun",3,["classic:xinsheng","fixture:zc-damage"],GeneralGender.Male));for(var i=0;i<11;i++){var id=$"fixture:zc-avatar-{i}";ids.Add(id);b.AddGeneral(new(id,"备用化身","supporter",multi?"classic:huashen":"standard:wusheng",god?"god":"wu",3,[..(multi?new[]{"standard:wusheng"}:Array.Empty<string>()),"fixture:zc-program","fixture:zc-suppression","fixture:zc-limited","classic:zhenliang",..(weidi?new[]{"classic:weidi","classic:hujia","classic:huangtian"}:Array.Empty<string>())],GeneralGender.Female));}
            b.AddDeck(new("fixture:zc-deck","固定红色牌",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard("standard:slash",Suit.Heart,i%13+1)).ToArray()});b.AddMode(new("identity:zc-fixture","测试",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:zc-deck",GeneralCandidateCount:12,GeneralPoolIds:ids));
        }
    }
}
