using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NativeFactionChecks
{
    private const string Owner="fixture:nf-owner",Driver="fixture:nf-driver",A="fixture:nf-avatar-a",B="fixture:nf-avatar-b",Borrow="fixture:nf-borrow",Quiet="fixture:nf-quiet",Goude="ol:goude",Mode="identity:classic-native-faction";
    public static void AvatarAcquisitionRedeclarationSourceLossAndSetupCold()
    {
        {
            var(g,r)=Start("single");Need(E<NativeEffectiveFactionChangedEvent>(g).Length==0&&V(g).FactionId=="wu","The mandatory initial avatar declaration is setup, not an actual-turn faction change.");
            ChangeWithGoude(g,"jin");NextPlay(g);var library=Libraries(g).Single();var declared=E<PrivateGeneralLibraryDeclaredEvent>(g).Length;var changes=E<NativeEffectiveFactionChangedEvent>(g).Length;
            Use(g,"hurt");Play(g);var grown=Libraries(g).Single();
            Need(grown.Source==library.Source&&grown.Count==library.Count+1&&grown.RevealedGeneralId==library.RevealedGeneralId&&grown.DeclaredSkillId==library.DeclaredSkillId&&
                E<PrivateGeneralLibraryDeclaredEvent>(g).Length==declared&&E<PrivateGeneralLibraryCountChangedEvent>(g).Last().Count==grown.Count&&
                V(g).FactionId=="jin"&&E<NativeEffectiveFactionChangedEvent>(g).Length==changes,
                "A true damage-triggered acquisition changes only the unused avatar inventory; its library revision cannot clear Goude's explicit effective-faction override.");
            g=Cold(g,r);End(g);Reach(g,()=>AvatarOffer(g,A));Activate(g);Declare(g,library.RevealedGeneralId!,library.DeclaredSkillId!);Play(g);
            var reset=E<NativeEffectiveFactionChangedEvent>(g).Where(e=>e.TargetSeat==0&&e.PreviousFactionId=="jin"&&e.CurrentFactionId=="wu").ToArray();
            Need(reset is[var actual]&&actual.Cause=="avatar-state"&&actual.Source?.SkillId==A&&V(g).FactionId=="wu"&&
                E<PrivateGeneralLibraryDeclaredEvent>(g).Length==declared+1&&Libraries(g).Single().RevealedGeneralId==library.RevealedGeneralId,
                "Actually redeclaring the very same avatar and skill restores its declared faction and emits one genuine difference fact; no selected identity change is required.");
            g=Cold(g,r);ChangeWithGoude(g,"jin");NextPlay(g);changes=E<NativeEffectiveFactionChangedEvent>(g).Length;Use(g,"lose");Play(g);
            Need(Libraries(g).Count==0&&V(g).FactionId=="shu"&&E<PrivateGeneralLibraryRemovedEvent>(g).Count(e=>e.Source==library.Source)==1&&
                E<NativeEffectiveFactionChangedEvent>(g).Skip(changes).ToArray()is[{TargetSeat:0,PreviousFactionId:"jin",CurrentFactionId:"shu",Cause:"avatar-state"}],
                "A real terminal owner-skill replacement removes the effective avatar source, clears its old explicit override, and returns to the current base faction exactly once.");
            _=Cold(g,r);
        }
        {
            var(g,r)=Start("dual");var libraries=Libraries(g);Need(libraries.Count==2&&V(g).FactionId=="wu","Two independent genuine private libraries have both completed their setup declarations.");
            var effective=libraries[0];var other=libraries[1];ChangeWithGoude(g,"jin");var changes=E<NativeEffectiveFactionChangedEvent>(g).Length;
            Reach(g,()=>AvatarOffer(g,other.Source.CapabilitySkillId));Activate(g);Declare(g,other.RevealedGeneralId!,other.DeclaredSkillId!);Play(g);
            Need(V(g).FactionId=="jin"&&E<NativeEffectiveFactionChangedEvent>(g).Length==changes&&Libraries(g)[0].Source==effective.Source&&
                E<PrivateGeneralLibraryDeclaredEvent>(g).Last().Source==other.Source,
                "A real declaration by the non-effective second private library cannot clear the effective first library's Goude override.");
            g=Cold(g,r);changes=E<NativeEffectiveFactionChangedEvent>(g).Length;Use(g,"fatal");Reach(g,()=>E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0));
            Need(!V(g).IsAlive&&g.State.Winner==Winner.None&&Libraries(g).Count==0&&V(g).FactionId=="shu"&&
                E<NativeEffectiveFactionChangedEvent>(g).Skip(changes).ToArray()is[{TargetSeat:0,PreviousFactionId:"jin",CurrentFactionId:"shu",Cause:"avatar-state"}],
                "The effective avatar source's real non-Lord death removes both libraries and restores the current base faction with one actual difference fact.");
            _=Cold(g,r);
        }
        {
            var(g,r)=Start("god");
            Need(E<GodFactionSelectedEvent>(g).Any(e=>e.ActorSeat==0&&e.FactionId=="shu")&&V(g).FactionId=="wu"&&
                E<PrivateGeneralLibraryDeclaredEvent>(g).Any(e=>e.Source.OwnerSeat==0&&e.FactionId=="wu")&&E<NativeEffectiveFactionChangedEvent>(g).Length==0,
                "Public god-faction setup and the mandatory god-avatar faction declaration precede actual-turn tracking and cannot qualify Goude as an in-turn faction mutation.");
            _=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g)=>g.CreateSnapshot(0).Players[0];
    private static IReadOnlyList<PrivateGeneralLibrarySnapshot> Libraries(GameEngine g)=>V(g).PrivateGeneralLibraries??[];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static bool AvatarOffer(GameEngine g,string skill)=>P(g)?.PlayerSeat==0&&P(g)?.SkillPrompt?.SkillId==skill&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected true native-faction command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Need(p.PlayerSeat==0,"A real AI advances its own private choice.");var c=p.Choices.FirstOrDefault(pick);Need(c is not null,"Missing native-faction choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Declare(GameEngine g,string general,string skill)
    {Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="private-general-library"&&c.Parameters.GetValueOrDefault("value")==general);Answer(g,c=>c.Parameters.GetValueOrDefault("value")==skill);if(P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("value")=="wu")==true)Answer(g,c=>c.Parameters.GetValueOrDefault("value")=="wu");}
    private static void Use(GameEngine g,string id)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],[],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void NextPlay(GameEngine g){var turn=g.State.TurnNumber;Reach(g,()=>g.State.TurnNumber>turn&&P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});}
    private static void ChangeWithGoude(GameEngine g,string faction)
    {
        if(E<NativeEffectiveFactionChangedEvent>(g).Any(e=>e.TurnNumber==g.State.TurnNumber&&e.TargetSeat==0&&e.PreviousFactionId!=e.CurrentFactionId))
        {
            var changedTurn=g.State.TurnNumber;End(g);NextPlay(g);
            Need(g.State.TurnNumber>changedTurn&&!E<NativeEffectiveFactionChangedEvent>(g).Any(e=>e.TurnNumber==g.State.TurnNumber&&e.TargetSeat==0),"A genuine avatar redeclaration has already spent this actual turn's faction option; decline its optional ending offer and wait for a fresh actual turn before requesting another change.");
        }
        Use(g,"draw");Play(g);End(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Goude&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Activate(g);
        Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option")==GoudeOption.Faction.ToString())==true);Answer(g,c=>c.Parameters.GetValueOrDefault("option")==GoudeOption.Faction.ToString());
        Answer(g,c=>c.Parameters.GetValueOrDefault("faction")==faction);Reach(g,()=>V(g).FactionId==faction&&E<GoudeCompletedEvent>(g).Any(e=>E<GoudeStartedEvent>(g).Any(s=>s.FrameId==e.FrameId&&s.ActualTurn==g.State.TurnNumber)));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.Phase,g.State.Winner,Faction=V(g).FactionId,Libraries=Libraries(g).Select(l=>new{l.Source,l.Count,l.RevealedGeneralId,l.DeclaredSkillId}),Prompt=P(g)is{}p?new{p.Kind,p.PlayerSeat,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(4).Select(c=>c.Parameters)}:null});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<260;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Missing actual native-faction boundary: "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind==DecisionKind.SelectFaction)Answer(g,c=>c.Parameters.GetValueOrDefault("faction-id")=="shu");
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="private-general-library"))
            {var selected=p.Choices.First(c=>c.Parameters.GetValueOrDefault("value") is not null).Parameters["value"];Declare(g,selected,Borrow);}
            else throw new InvalidOperationException("Unexpected native-faction boundary: "+Diagnostic(g));
        }
        throw new InvalidOperationException("Missing bounded native-faction boundary: "+Diagnostic(g));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s)))+JsonSerializer.Serialize(g.ResolutionStack)+JsonSerializer.Serialize(g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())));
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold replay preserves the private library source identity and actual faction differences without cache injection.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=scene=="dual"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=20},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-native-faction",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:meng-da");
            var defs=new List<string>
            {
                $$"""{"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]},{"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},{"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},{"id":"fatal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":20}]}]}""",
                $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""
            };
            foreach(var id in new[]{A,B})defs.Add($$"""{"id":"{{id}}","revision":1,"triggers":[{"id":"initial","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"initializePrivateGeneralLibrary","target":"owner","amount":2,"excludedSkillTags":["limited","awakening","lord"]}]},{"id":"prepare","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"effects":[{"op":"choosePrivateGeneralAvatar","target":"owner","excludedSkillTags":["limited","awakening","lord"]}]},{"id":"grow","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamagePoint","optional":false,"effects":[{"op":"acquirePrivateGeneralAvatar","target":"owner","amount":1,"skillIds":["{{id}}"],"excludedSkillTags":["limited","awakening","lord"]}]}]}""");
            var driver=JsonNode.Parse(defs[0])!;
            driver["activations"]!.AsArray().Single(a=>a!["id"]!.GetValue<string>()=="lose")!["effects"]=JsonNode.Parse($$"""[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{A}}"],"sourceBind":"standard:none"}]""");
            defs[0]=driver.ToJsonString();
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="实际化身与势力夹具"});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实原生化身行为"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new(Borrow,"化身声明","无副作用的完整技能"));b.AddSkill(new("fixture:nf-pick","本人","固定公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-100000000d)});
            var skills=new List<string>{Driver,Goude,A};if(scene=="dual")skills.Add(B);
            b.AddGeneral(new(Owner,"实际化身本人","supporter","fixture:nf-pick",scene=="god"?"god":"shu",12,skills,GeneralGender.Male));
            var peers=Enumerable.Range(0,3).Select(i=>"fixture:nf-peer-"+i).ToArray();foreach(var(peer,i)in peers.Select((peer,i)=>(peer,i)))
            {var pick="fixture:nf-pick-"+i;b.AddSkill(new(pick,"其他角色","固定公开选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>1000000d*(3-i))});b.AddGeneral(new(peer,"其他角色","supporter",pick,"wei",12,[Quiet]));}
            var avatars=Enumerable.Range(0,12).Select(i=>"fixture:nf-general-"+i).ToArray();foreach(var id in avatars)b.AddGeneral(new(id,"备用化身","supporter",Borrow,scene=="god"?"god":"wu",4,[],GeneralGender.Female));
            b.AddCard(new("fixture:nf-card","真实装备","装备牌","固定无救牌",CardKind.Crossbow));b.AddDeck(new("fixture:nf-deck","固定实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,120).Select(i=>new ContentDeckPhysicalCard("fixture:nf-card",Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"实际化身势力变化",4,4,scene=="dual"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Renegade)]=1}:new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:nf-deck",GeneralCandidateCount:16,GeneralPoolIds:[Owner,..peers,..avatars]));
        }
    }
}
