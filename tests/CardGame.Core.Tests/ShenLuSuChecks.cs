using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenLuSuChecks
{
    private const string Hero="mobile:shen-lu-su",Driver="fixture:shen-ls-driver",Quiet="fixture:shen-ls-quiet",Watch="fixture:shen-ls-watch",Mode="identity:classic-shen-ls-fixture";
    public static void NativeTableOrderAndDingzhouPaidCold()
    {
        var(g,r)=Start();Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Reach(g,p=>IsChoice(p,"seat:3"));Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="seat:3");g=Cold(g,r);
        Reach(g,p=>IsChoice(p,"seat:1"));Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="seat:1");
        Reach(g,p=>IsChoice(p,"seat:2"));Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="seat:2");Play(g);
        Need(g.CreateSnapshot(0).TableOrder!.SequenceEqual([0,3,1,2])&&E<TableSeatMovedEvent>(g).All(e=>e.Seat!=0),"Tamo installs the complete chosen real clockwise order while the actual Lord stays fixed and participant IDs keep their identity.");
        Skill(g,Driver,"draw",[]);Play(g);Skill(g,Driver,"equip",[1]);Play(g);Use(g,CardKind.Indulgence,[1]);Play(g);
        var field=g.CreateCardZoneDiagnostics().Where(c=>c.Location==CardLocation.Equipment(1)||c.Location==CardLocation.Judgment(1)).Select(c=>c.CardId).ToArray();
        Need(field.Length==2,"The real recipient owns one physically equipped weapon and one native delayed judgment entity.");
        var before=V(g,1).HandCount;Skill(g,"mobile:dingzhou","give-and-obtain",[1]);
        Reach(g,p=>IsChoice(p,"give:"));Answer(g,c=>true);Reach(g,p=>IsChoice(p,"give:"));Answer(g,c=>true);
        Reach(g,p=>p.SkillPrompt?.SkillId==Watch);
        var paid=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="mobile:dingzhou").ShenLuSu!;
        Need(paid is {Stage:ShenLuSuStage.GiftChildren,Paid:true,Amount:2}&&V(g,1).HandCount==before+2&&field.All(id=>g.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location.OwnerSeat==1),"Dingzhou really transfers exactly the original field count first; its gain observer interrupts before any field card is obtained.");
        g=Cold(g,r);Answer(g,c=>true);Play(g);
        var obtained=g.CardMovements.Where(e=>e.Reason.Value=="skill-program.mobile-shen-lu-su.ObtainChildren").ToArray();
        Need(obtained.Length==2&&E<ShenLuSuMovementPaidEvent>(g).Single(e=>e.Stage==ShenLuSuStage.ObtainChildren).Count==2&&obtained.Select(e=>e.CardId).Order().SequenceEqual(field.Order())&&
            field.All(id=>g.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location==CardLocation.Hand(0))&&!g.GetHumanLegalActions().Any(a=>a.SkillId=="mobile:dingzhou"),"After the exact paid child returns, every current equipment and judgment entity is obtained in one native batch and the actual Play budget remains consumed.");
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,_=>false,()=>E<TurnStartedEvent>(g).Any(e=>e.ActorSeat==3));
        Need(E<TurnStartedEvent>(g).First(e=>e.ActorSeat!=0).ActorSeat==3,"The engine's real next turn follows the reordered table rather than numerical participant IDs.");
        var(team,_)=Start(team:true);Answer(team,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        foreach(var seat in new[]{1,0,3,2}){Reach(team,p=>IsChoice(p,"seat:"+seat));Answer(team,c=>c.Parameters.GetValueOrDefault("option")=="seat:"+seat);}
        Reach(team,_=>false,()=>E<TurnStartedEvent>(team).Length>0);
        Need(team.CreateSnapshot(0).TableOrder!.SequenceEqual([1,0,3,2])&&E<TurnStartedEvent>(team).First().ActorSeat==1,
            "In an ordinary Team mode, Tamo reassigns the first public slot too; the completed native setup starts the real first turn at the chosen participant.");
    }
    public static void NativeZhimengRandomDivisionCold()
    {
        var(g,r)=Start();Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");Play(g);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.SkillPrompt?.SkillId=="mobile:zhimeng"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        Need(E<TurnEndedEvent>(g).Any(e=>e.ActorSeat==0),"Zhimeng is offered only after this owner's actual turn has really ended.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,p=>IsChoice(p,"peer:1"));Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="peer:1");
        Reach(g,p=>p.SkillPrompt?.SkillId==Watch);
        var frame=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="mobile:zhimeng");var receipt=frame.ShenLuSu!;
        var split=receipt.Division;
        Need(receipt is {Stage:ShenLuSuStage.DivideChildren,Paid:true,Target:1}&&receipt.Amount%2==1&&receipt.OwnerShare.Count==(receipt.Amount+1)/2&&
            V(g,0).HandCount==(receipt.Amount+1)/2&&V(g,1).HandCount==receipt.Amount/2&&
            split.Select(e=>e.CardId).Order().SequenceEqual(receipt.Cards.Order())&&split.All(e=>g.CreateCardZoneDiagnostics().Single(c=>c.CardId==e.CardId).Location==CardLocation.Hand(e.ToSeat))&&
            g.CreateSnapshot(2).PendingDecision is null,"The actual two hands are randomly pooled once, every physical entity belongs to its frozen recipient, the odd card goes to Shen Lu Su, and an unrelated viewer cannot read the private gain observer.");
        g=Cold(g,r);Answer(g,c=>true);Reach(g,_=>false,()=>!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="mobile:zhimeng"));
        Need(E<ShenLuSuDivisionEvent>(g).Count(e=>e.FrameId==frame.Id)==1&&E<ShenLuSuMovementPaidEvent>(g).Where(e=>e.FrameId==frame.Id).Select(e=>e.BatchId).Distinct().Count()==1,
            "Cold restoration returns the original paid simultaneous hand division without a second random sample or transfer batch.");
    }
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static bool IsChoice(PendingDecision p,string option)=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="shen-lu-su"&&c.Parameters.GetValueOrDefault("option")?.StartsWith(option,StringComparison.Ordinal)==true);
    private static void Skill(GameEngine g,string skill,string activation,int[] targets)=>Accept(g,new UseProgramSkillCommand(0,skill,activation,[],targets,g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,CardKind kind,int[] targets)
    {
        var ids=V(g).Hand.Where(c=>c.Kind==kind).Select(c=>c.Id).ToHashSet();var a=g.GetHumanLegalActions().First(a=>a.CardId is{}id&&ids.Contains(id)&&a.TargetSeats.SequenceEqual(targets));
        Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    }
    private static void Answer(GameEngine g,Func<PromptChoice,bool> select){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(select).Id,g.Revision));}
    private static void Play(GameEngine g)=>Reach(g,p=>p is {PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<PendingDecision,bool> ready,Func<bool>? state=null)
    {
        for(var n=0;n<250;n++)
        {
            if(state?.Invoke()==true)return;
            if(P(g)is{}p)
            {
                if(ready(p))return;
                if(p.Kind==DecisionKind.SelectFaction){Answer(g,_=>true);continue;}
                if(p.Kind==DecisionKind.ProgramTrigger||p.Kind==DecisionKind.SelectTargetCard)
                {
                    var choice=p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")??p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")??p.Choices.First();
                    Answer(g,c=>c.Id==choice.Id);continue;
                }
                if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying or DecisionKind.Nullification){Answer(g,c=>c.Cards.Count==0);continue;}
            }
            Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Shen Lu Su missed native boundary: "+JsonSerializer.Serialize(new{Prompt=P(g)?.Kind,Seat=P(g)?.PlayerSeat,Frames=g.ResolutionStack.Select(f=>new{f.Id,Type=f.GetType().Name})}));
    }
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected real Shen Lu Su command.");}
    private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"All private views, exact native parent frames, paid invoices and real entities cold-restore identically.");return restored;}
    private static (GameEngine,ContentRegistry) Start(bool team=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(team));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=team?null:Role.Lord,HumanTeamId=team?"a":null,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,Hero,g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.SkillPrompt?.SkillId=="mobile:tamo"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));return(g,r);
    }
    private sealed class Fixture(bool team):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-shen-ls",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b),Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"gained","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.mobile-shen-lu-su.GiftChildren","skill-program.mobile-shen-lu-su.DivideChildren"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"watch","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Watch};var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="Native Shen Lu Su fixture.",optionLabels=id==Watch?new Dictionary<string,string>{{"continue","继续"}}:[]})}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"Native fixture"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:shen-ls-peer-{i}","Quiet participant","supporter",Quiet,"wei",8){AdditionalSkillIds=[Watch]});
            var kinds=new[]{"standard:slash","standard:crossbow","standard:indulgence","standard:peach"};
            b.AddDeck(new("fixture:shen-ls-deck","Real mixed physical deck",3,2,[]){PhysicalCards=Enumerable.Range(0,160).Select(i=>new ContentDeckPhysicalCard(kinds[i%4],i%2==0?Suit.Heart:Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new(Mode,"Native Shen Lu Su",4,4,team?new Dictionary<string,int>():new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:shen-ls-deck",GeneralCandidateCount:4,GeneralPoolIds:[Hero,"fixture:shen-ls-peer-1","fixture:shen-ls-peer-2","fixture:shen-ls-peer-3"],ModeKind:team?ContentModeKind.Team:ContentModeKind.Identity,TeamCounts:team?new Dictionary<string,int>{{"a",2},{"b",2}}:null));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b):IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d)=>b.AddGeneral(d.Id==Hero?d with{AdditionalSkillIds=[..d.AdditionalSkillIds!,Driver,Watch]}:d);
        public void AddSkill(ContentSkillDefinition d)=>b.AddSkill(d);public void AddCard(ContentCardDefinition d)=>b.AddCard(d);public void AddDeck(ContentDeckRecipe d)=>b.AddDeck(d);public void AddMode(ContentModeDefinition d)=>b.AddMode(d);
    }
}
