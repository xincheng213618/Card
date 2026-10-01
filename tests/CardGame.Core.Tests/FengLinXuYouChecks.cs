using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class FengLinXuYouChecks
{
    public static void BottomDrawAndAlternatingReplay()
    {
        var (control,_)=Create(bottom:false);ReachPlay(control);var (g,r)=Create();var before=Pile(control);ReachPlay(g);Require(control.CardMovements.Where(m=>m.Reason==CardMoveReasons.InitialDeal).Select(m=>m.CardId).SequenceEqual(g.CardMovements.Where(m=>m.Reason==CardMoveReasons.InitialDeal).Select(m=>m.CardId)),"Initial deal preserves its normal direction for all players.");
        var draw=g.CardMovements.Where(m=>m.Reason==CardMoveReasons.Draw&&m.To==CardLocation.Hand(0)).Select(m=>m.CardId).ToArray();
        Require(draw.SequenceEqual(before.Take(2)),"Normal draw takes bottom in actual order.");
        var bottom=Pile(g)[0];var start=g.CreateSnapshot(0).Players[0].Hand.Count;
        Activate(g,"classic:chenglue","alternate",[],[]);
        var f=g.ResolutionStack.OfType<ProgramSkillFrame>().Last();Require(Enumerable.Range(1,3).All(v=>g.CreateSnapshot(v).PendingDecision is null),"Other ordinary viewers cannot inspect the owner's private discard choices.");Require(f.AlternatingSuitTop is {Stage:"discard",Required:2},"Yang commits then draws once before real hand cost.");
        Require(g.CreateSnapshot(0).Players[0].Hand.Any(c=>c.Id==bottom)&&g.CreateSnapshot(0).Players[0].Hand.Count==start+1,"Skill draw also takes the actual bottom.");Replay(g,r);
        var state=State(g);var p=Prompt(g)!;var rejected=g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision));
        Require(rejected.Error is not null&&State(g)==state,"Wrong actor rejects atomically.");
        Answer(g,c=>c.Cards.Count==1);Replay(g,r);Answer(g,c=>c.Cards.Count==1);ReachPlay(g);
        Require(g.Events.Select(e=>e.Payload).OfType<PlayPhaseSuitAllowanceGrantedEvent>().Last().DiscardedCardIds.Count==2,"Only actual two hand costs grant suits.");
        var again=g.Submit(new UseProgramSkillCommand(0,"classic:chenglue","alternate",[],[],g.Revision,Prompt(g)!.PromptId));Require(again.Error is not null,"Own actual Play quota rejects second activation.");
        var turn=g.CreateSnapshot(0).TurnNumber;End(g);Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard&&g.CreateSnapshot(0).TurnNumber>turn);
        Activate(g,"classic:chenglue","alternate",[],[]);Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Last().AlternatingSuitTop is {Required:1},"Next activation is Yin, with one actual discard.");Answer(g,c=>c.Cards.Count==1);Replay(g,r);
    }
    public static void EquipmentAndFirstDecline()
    {
        var (g,r)=Create();ReachPlay(g);var equipment=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);Play(g,equipment);ActivateFirst(g);
        Require(g.CreateSnapshot(0).PublicRevealedCards.Any(c=>c.Id==equipment.CardId),"The already public used equipment is available to generic ordering UI.");Replay(g,r);
        var bottom=Pile(g)[0];Answer(g,c=>c.Cards.Contains(equipment.CardId!.Value));ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[0].Equipment.All(c=>c.Id!=equipment.CardId)&&Pile(g)[^1]==equipment.CardId,"Exact just-equipped entity returns to top.");
        Require(g.CreateSnapshot(0).Players[0].Hand.Any(c=>c.Id==bottom),"Cunmu reward draws bottom, not the placed top.");
        Play(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip));ReachPlay(g);
        Require(g.Events.Select(e=>e.Payload).OfType<AlternatingSuitStateCommittedEvent>().Count()==0,"Equipment test uses only completed operation.");
        Play(g,g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.Slash&&a.TargetSeats.Contains(1)));Reach(g,p=>p.SkillPrompt?.SkillId=="classic:shicai");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");ReachPlay(g);
        Activate(g,"fixture:xu-driver","draw",[],[]);ReachPlay(g);Play(g,g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.Slash&&a.TargetSeats.Contains(1)));ReachPlay(g);
        Require(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="classic:shicai"),"Declining first basic does not permit second basic to become first.");Replay(g,r);
        var (claimed,cr)=Create(claimed:true);ReachPlay(claimed);var first=claimed.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.Slash&&a.TargetSeats.Contains(1));Play(claimed,first);ReachPlay(claimed);
        Require(claimed.CreateCardZoneDiagnostics().Single(c=>c.CardId==first.CardId).Location==CardLocation.Hand(1),"The damage recipient really claims the physical use cost before completion.");
        Require(!claimed.CardMovements.Any(m=>m.Reason.Value is "skill-program.completed-top.place" or "skill-program.completed-top.draw"),"Unavailable claimed cost produces neither theft nor a free reward draw.");Replay(claimed,cr);
    }
    public static void MultiEntityOrderAndNestedDraw()
    {
        var (g,r)=Create(nested:true);ReachPlay(g);var ids=g.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c=>c.Id).ToArray();
        Activate(g,"fixture:xu-driver","multi",ids,[1]);ActivateFirst(g);
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Last().AlternatingSuitTop is {Required:2},"Actual virtual use retains both paid entities.");Replay(g,r);
        Answer(g,c=>c.Cards.Contains(ids[1]));Answer(g,c=>c.Cards.Contains(ids[0]));
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:xu-observer");Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.AlternatingSuitTop is {Stage:"reward-draw"}),"Reward draw cursor is committed before nested gain observer.");Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="choose-option");ReachPlay(g);
        Require(Pile(g).TakeLast(2).Reverse().SequenceEqual([ids[1],ids[0]]),"User chooses true top order, not silent physical-id order.");
        Require(g.CardMovements.Count(m=>m.Reason.Value=="skill-program.completed-top.place")==2,"Resumption never moves a paid entity twice.");Replay(g,r);
    }
    public static void PhaseSuitDistanceAndExpiry()
    {
        var (g,r)=Create(mixed:true);ReachPlay(g);Activate(g,"classic:chenglue","alternate",[],[]);
        var suit=g.CreateSnapshot(0).Players[0].Hand.GroupBy(c=>c.Suit).OrderByDescending(x=>x.Count()).First().Key; var first=Prompt(g)!.Choices.First(c=>g.CreateSnapshot(0).Players[0].Hand.Single(h=>h.Id==c.Cards[0]).Suit==suit);
        Answer(g,c=>c.Id==first.Id);Answer(g,c=>g.CreateSnapshot(0).Players[0].Hand.Single(h=>h.Id==c.Cards[0]).Suit==suit);ReachPlay(g);
        var fact=g.Events.Select(e=>e.Payload).OfType<PlayPhaseSuitAllowanceGrantedEvent>().Last();
        Require(fact.Suits is System.Collections.Generic.ICollection<Suit> fs&&fs.IsReadOnly&&fact.DiscardedCardIds is System.Collections.Generic.ICollection<int> fi&&fi.IsReadOnly,"Committed new collection-bearing fact freezes both nested lists.");
        var published=g.CreateSnapshot(0).Players[0].IssuedPlayPhaseSuitUseAllowances!;
        Require(published.Count==1&&published[0].OwnerSeat==0&&published[0].SkillId=="classic:chenglue"&&published[0].Suits.Contains(suit),"Issued suit policy is public with precise source and phase.");
        Require(published is System.Collections.Generic.ICollection<IssuedPlayPhaseSuitUseAllowance> pc&&pc.IsReadOnly&&published[0].Suits is System.Collections.Generic.ICollection<Suit> sc&&sc.IsReadOnly,"Prepared player view deeply freezes suit allowance lists.");Replay(g,r);
        var allowed=g.GetHumanLegalActions().Where(a=>a.PlayedCardKind==CardKind.Slash&&a.TargetSeats.Contains(2)).ToArray();
        Require(allowed.Any(a=>g.CreateSnapshot(0).Players[0].Hand.Single(c=>c.Id==a.CardId).Suit==suit),"Authorized physical suit crosses actual distance. grants="+JsonSerializer.Serialize(g.Events.Select(e=>e.Payload).OfType<PlayPhaseSuitAllowanceGrantedEvent>())+" actions="+JsonSerializer.Serialize(g.GetHumanLegalActions()));
        var a=allowed.First(x=>g.CreateSnapshot(0).Players[0].Hand.Single(c=>c.Id==x.CardId).Suit==suit);Play(g,a);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:shicai");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");ReachPlay(g);
        Activate(g,"fixture:xu-driver","lose-allowance-source",[],[]);ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[0].IssuedPlayPhaseSuitUseAllowances is {Count:1},"Paid phase allowance remains public after its source skill is lost.");
        Require(g.GetHumanLegalActions().Any(a=>a.PlayedCardKind==CardKind.Slash&&a.TargetSeats.Contains(2)),"Authorized suit remains unlimited after a real Slash and source loss.");
        var turn=g.CreateSnapshot(0).TurnNumber;End(g);Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0&&g.CreateSnapshot(0).TurnNumber>turn);
        Require(g.CreateSnapshot(0).Players[0].IssuedPlayPhaseSuitUseAllowances is null,"Published allowance expires with actual phase.");
        Require(g.GetHumanLegalActions().All(a=>a.PlayedCardKind!=CardKind.Slash||!a.TargetSeats.Contains(2)),"Suit policy expires at the actual phase boundary.");Replay(g,r);
    }
    public static void ActualResponseUseAndFirstCategory()
    {
        var (g,r)=Create(response:true);ReachPlay(g);Activate(g,"fixture:xu-driver","draw",[],[]);ReachPlay(g);
        var donor=g.CreateCardZoneDiagnostics().First(c=>c.Location.Zone==CardZoneKind.Hand&&c.Location.OwnerSeat is 1 or 3&&c.CardKind==CardKind.Slash).Location.OwnerSeat!.Value;
        Activate(g,"fixture:xu-driver","request",[],[donor]);Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.RespondDodge);
        var paid=Prompt(g)!.Choices.First(c=>c.Cards.Count==1).Cards.Single();Answer(g,c=>c.Cards.Contains(paid));ActivateFirst(g);
        Require(g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().CompletedResponseReturn is {IsCommitted:false},"True defense Dodge has the exact completed typed response parent.");Replay(g,r);
        Answer(g,c=>c.Cards.Contains(paid));ReachPlay(g);Require(Pile(g)[^1]==paid,"Completed Dodge's actual response cost returns to top once.");
        var placed=g.CardMovements.Count(m=>m.Reason.Value=="skill-program.completed-top.place");
        Play(g,g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.Slash&&a.TargetSeats.Contains(1)));ReachPlay(g);
        Require(g.CardMovements.Count(m=>m.Reason.Value=="skill-program.completed-top.place")==placed,"True Dodge consumes whole-turn first basic before later own Slash.");Replay(g,r);
    }
    public static void NativeAiAndStrictResources()
    {
        var (g,r)=Create(native:true);ReachPlay(g);Activate(g,"classic:chenglue","alternate",[],[]);while(Prompt(g)?.SkillPrompt?.SkillId=="classic:chenglue")Answer(g,c=>c.Cards.Count==1);Replay(g,r);
        var (ai,ar)=Create(aiOwner:true);for(var n=0;n<80&&!ai.Events.Any(e=>e.Payload is AlternatingSuitStateCommittedEvent);n++)Drive(ai);
        Require(ai.Events.Any(e=>e.Payload is AlternatingSuitStateCommittedEvent),"Normal AdvanceOneStep AI actually executes the new activation.");Replay(ai,ar);
        var assembly=typeof(StandardClassicGeneralPackage).Assembly;string Read(string suffix){using var s=assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.classic-xu-you."+suffix+".json")!;using var rd=new StreamReader(s);return rd.ReadToEnd();}
        foreach(var bad in new[]{Read("rules").Replace("cardUseCompleted","cardUseCommitted"),Read("rules").Replace("\"actor\"","\"target\"")}){var failed=false;try{SkillProgramCatalog.Load(bad,Read("presentation"));}catch(InvalidOperationException){failed=true;}Require(failed,"Completed operation rejects wrong window/actor resource.");}
    }
    private static int[] Pile(GameEngine g)=>g.CreateCardZoneDiagnostics().Where(d=>d.Location==CardLocation.DrawPile).OrderBy(d=>d.ZoneIndex).Select(d=>d.CardId).ToArray();
    private static PendingDecision? Prompt(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static string State(GameEngine g)=>JsonSerializer.Serialize(Enumerable.Range(0,4).SelectMany(s=>new[]{g.CreateSnapshot(s),g.CreateSnapshot(s,true)}))+JsonSerializer.Serialize(g.CreateCardZoneDiagnostics());
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(restored),"All-viewer checkpoint replay preserves exact paused state.");Require(g.CreateCardZoneDiagnostics().Count==100&&g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==100,"Physical entities conserved.");}
    private static void Accept(GameEngine g,GameCommand c){var x=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(x.Error is null,x.Error?.Message??"Rejected");}
    private static void Require(bool b,string s){if(!b)throw new InvalidOperationException(s);}
    private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate){for(var n=0;n<160;n++){if(Prompt(g)is{}p&&predicate(p))return;Drive(g);}throw new InvalidOperationException("Missing boundary "+Prompt(g));}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));
    private static void Play(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,Prompt(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    private static void Activate(GameEngine g,string skill,string id,int[] cards,int[] targets)=>Accept(g,new UseProgramSkillCommand(0,skill,id,cards,targets,g.Revision,Prompt(g)!.PromptId));
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
    private static void ActivateFirst(GameEngine g){Reach(g,p=>p.SkillPrompt?.SkillId=="classic:shicai"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");}
    private static void Drive(GameEngine g){if(Prompt(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard})End(g);else if(Prompt(g)is{PlayerSeat:0})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip"||c.Parameters.GetValueOrDefault("response")=="let-die"||c.Cards.Count<=1);else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static (GameEngine,ContentRegistry) Create(bool nested=false,bool mixed=false,bool native=false,bool aiOwner=false,bool bottom=true,bool response=false,bool claimed=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(nested,mixed,native,aiOwner,bottom,response,claimed));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-xu-check",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=20},r);
        Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,native?"classic:xu-you":"fixture:xu-owner",g.Revision,Prompt(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(bool nested,bool mixed,bool native,bool aiOwner,bool bottom,bool response,bool claimed):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-xu",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var rules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:xu-driver","revision":1,"viewAs":[{"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false},{"id":"multi","inputKinds":[],"inputSuits":[],"inputCount":2,"outputKind":"slash","forPlay":true,"forResponse":false}],"activations":[{"id":"lose-allowance-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:chenglue"],"sourceBind":"standard:none"}]},{"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]},{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},{"id":"multi","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"multi","outputKind":"slash"}]}]}]}""";
            var c=SkillProgramCatalog.Load(rules,"""{"schemaVersion":3,"skills":{"fixture:xu-driver":{"name":"真实动作","description":"测试"}}}""");b.AddSkill(new("fixture:xu-driver","真实动作","测试"){Program=c.Programs["fixture:xu-driver"]});
            if(nested){var n=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:xu-observer","revision":1,"triggers":[{"id":"placed","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.completed-top.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:xu-observer":{"name":"移动暂停","description":"测试","optionLabels":{"continue":"继续"}}}}""");b.AddSkill(new("fixture:xu-observer","移动暂停","测试"){Program=n.Programs["fixture:xu-observer"]});}
            b.AddGeneral(new("fixture:xu-owner","测试拥有者","supporter","classic:chenglue","qun",20,nested?["classic:shicai","classic:cunmu","fixture:xu-driver","fixture:xu-observer"]:bottom?["classic:shicai","classic:cunmu","fixture:xu-driver"]:["classic:shicai","fixture:xu-driver"]));
            b.AddSkill(new("fixture:xu-idle","测试静态","测试"));for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:xu-target-{i}","目标","supporter",aiOwner?"classic:chenglue":claimed&&i==1?"classic:jianxiong":"fixture:xu-idle","qun",20));
            b.AddDeck(new("fixture:xu-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard(response?i%3==0?"standard:slash":i%3==1?"standard:dodge":"standard:nullification":"standard:crossbow",mixed?(i%2==0?Suit.Spade:Suit.Heart):Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-xu-check","测试",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:xu-deck",GeneralCandidateCount:4,GeneralPoolIds:[native?"classic:xu-you":"fixture:xu-owner","fixture:xu-target-1","fixture:xu-target-2","fixture:xu-target-3"]));
        }
    }
}
