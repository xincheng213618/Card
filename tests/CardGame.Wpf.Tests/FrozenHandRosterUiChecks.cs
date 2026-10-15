using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;
internal static class FrozenHandRosterUiChecks
{
    private const string Skill="ol:lanjiang",Owner="fixture:frozen-ui-owner",Quiet="fixture:frozen-ui-quiet",Driver="fixture:frozen-ui-driver",Mode="identity:frozen-ui";
    public static void RealChooserAndMandatoryTailTargets(string output)
    {
        var other=Start(true);Reach(other.Game,()=>P(other.Game) is{PlayerSeat:0} p && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="frozen-hand-roster"));
        using(var model=Load(other.Game,other.Registry))
        {
            var game=Program.Engine(model);var prompt=game.CreateSnapshot(0).PendingDecision!;
            Program.Assert(prompt.SourceSeat!=0 && prompt.TargetSeat==0 && model.IsSkillSelectionPending && !model.IsSkillTargetSelectionPending &&
                model.SkillChoices.Count==2 && model.SkillChoices.All(c=>c.Targets.Count==0 && c.Cards.Count==0) &&
                model.CenterMessage.Contains(game.CreateSnapshot(0).Players[prompt.SourceSeat!.Value].Name,StringComparison.Ordinal),
                "A real other-player-owned roster displays the human participant's private yes/no choice and names the actual skill owner receiving the draw.");
            var before=game.CardMovements.Count;model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(c=>c.Parameters["branch"]=="decline"));
            Program.Assert(game.AcceptedCommands.OfType<AnswerPromptCommand>().Last() is{ActorSeat:0} answer && answer.PromptId==prompt.PromptId &&
                game.CardMovements.Count==before,"The real chooser declines for itself without issuing an owner draw or substituting the skill owner's seat.");
        }
        var own=Start(false);Reach(own.Game,()=>P(own.Game) is{PlayerSeat:0,Kind:DecisionKind.PlayCard});Accept(own.Game,new EndPlayPhaseCommand(0,own.Game.Revision,P(own.Game)!.PromptId));
        Reach(own.Game,()=>P(own.Game)?.SkillPrompt?.SkillId==Skill && P(own.Game)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        Answer(own.Game,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(own.Game,()=>Branch(own.Game,"draw"));Answer(own.Game,c=>c.Parameters.GetValueOrDefault("branch")=="draw");
        Reach(own.Game,()=>Branch(own.Game,"skip-damage"));Answer(own.Game,c=>c.Parameters.GetValueOrDefault("branch")=="skip-damage");Reach(own.Game,()=>Branch(own.Game,"reward"));
        using(var model=Load(own.Game,own.Registry))
        {
            var game=Program.Engine(model);var prompt=game.CreateSnapshot(0).PendingDecision!;var revision=game.Revision;
            Program.Assert(model.IsSkillTargetSelectionPending && model.CenterSkillChoices.Count==0 && model.SkillChoices.All(c=>c.Targets.Count==1 && c.Parameters["branch"]=="reward") &&
                model.Seats.Where(s=>s.IsLegalTarget).Select(s=>s.Seat).Order().SequenceEqual(prompt.Choices.Select(c=>c.Targets[0]).Order()),
                "The mandatory reward uses the existing portrait selection and only the exact current original-roster lower-hand targets.");
            var window=new MainWindow(model){Width=1120,Height=820,ShowActivated=false,ShowInTaskbar=false};var root=(FrameworkElement)window.Content;
            try
            {
                Program.Render(root,1120,820,Path.Combine(output,"frozen-hand-roster-mandatory-reward.png"));
                model.SelectTargetCommand.Execute(model.Seats.Single(s=>s.Seat==prompt.Choices[0].Targets[0]));
                Program.Assert(model.HasSelection && model.CanConfirmSelected && ((Button)window.FindName("PlayCardButton")).IsEnabled,
                    "A real permitted reward portrait enables bottom confirmation before any command is committed.");
                model.CancelActionCommand.Execute(null);
                Program.Assert(!model.HasSelection && model.IsSkillTargetSelectionPending && game.Revision==revision && game.CreateSnapshot(0).PendingDecision!.PromptId==prompt.PromptId,
                    "Cancel clears only the local mandatory reward draft; it cannot skip the native draw tail.");
                var choice=prompt.Choices[0];model.SelectTargetCommand.Execute(model.Seats.Single(s=>s.Seat==choice.Targets[0]));model.ConfirmSelectedCommand.Execute(null);
                Program.Assert(game.AcceptedCommands.OfType<AnswerPromptCommand>().Last().Choice==choice.Id &&
                    game.Events.Select(e=>e.Payload).OfType<FrozenHandRosterDrawPaidEvent>().Count(e=>e.ReturnStage=="complete")==1,
                    "The portrait draft submits the exact published reward choice and pays one real target draw.");
            }
            finally{window.Content=null;window.DataContext=null;window.Close();}
        }
    }
    private static MainViewModel Load(GameEngine game,ContentRegistry registry)
    {
        var store=new MemorySaveStore();store.Write(GameSaveSlot.Manual,new(1,DateTimeOffset.UtcNow,false,game.CreateCheckpoint()));
        var model=new MainViewModel(false,7,true,store,useExpandedContent:false,contentRegistry:registry){IsMotionEnabled=false,IsSoundEnabled=false};model.LoadManualGameCommand.Execute(null);return model;
    }
    private static PendingDecision?P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static bool Branch(GameEngine g,string b)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="frozen-hand-roster" && c.Parameters.GetValueOrDefault("branch")==b)==true;
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(c);Program.Assert(r.Accepted,r.Error?.Message??"Rejected native WPF roster fixture command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<160;i++){if(done())return;var p=P(g);
            if(p is{PlayerSeat:0,Kind:DecisionKind.PlayCard})Accept(g,new EndPlayPhaseCommand(0,g.Revision,p.PromptId));
            else if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));}
        throw new InvalidOperationException("Fixed WPF roster fixture did not reach expected decision. "+JsonSerializer.Serialize(new{Prompt=P(g),Frames=g.ResolutionStack}));
    }
    private static (GameEngine Game,ContentRegistry Registry)Start(bool otherOwner)
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());var game=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);
        Accept(game,new StartGameCommand());Reach(game,()=>P(game) is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(game,new SelectGeneralCommand(0,otherOwner?"fixture:frozen-ui-peer-0":Owner,game.Revision,P(game)!.PromptId));return(game,registry);
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("frozen-ui-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-wu-yan."+suffix)!;using var r=new StreamReader(s);return r.ReadToEnd();}
            void Add(SkillProgramCatalog cat){foreach(var(id,p)in cat.Programs)b.AddSkill(new(id,cat.Presentations[id].Name,cat.Presentations[id].Description){Program=p,ProgramPresentation=cat.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,r=>id==Quiet?10000d:r==Role.Rebel?20000d:-10000d)});}
            Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
            var rules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},{"id":"{{Driver}}","revision":1,"modifiers":[{"id":"distance","query":"outgoingDistance","operation":"add","value":-1,"priority":0}]}]}""";
            Add(SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Quiet]=new{name="安静同桌",description="固定跳出牌"},[Driver]=new{name="真实澜疆驱动",description="固定驱动"}}})));
            b.AddGeneral(new(Owner,"吾彦夹具拥有者","supporter",Driver,"wu",12,[Skill]));var peers=Enumerable.Range(0,4).Select(i=>$"fixture:frozen-ui-peer-{i}").ToArray();foreach(var id in peers)b.AddGeneral(new(id,"参与者"+id[^1],"supporter",Quiet,"wei",12));
            b.AddDeck(new("fixture:frozen-ui-deck","固定真实初始四杀",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(Mode,"真实澜疆界面",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:frozen-ui-deck",GeneralCandidateCount:5,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
