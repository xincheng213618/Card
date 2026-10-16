using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class FengLinChenDaoChecks
{
    public static void FirstUseAndIssuedPhaseBan()
    {
        var(g,r)=Create("slash");Grow(g);var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.Contains(2));var later=g.GetHumanLegalActions().First(x=>x.Kind==LegalActionKind.Snatch&&x.CardId!=a.CardId&&x.TargetSeats.Contains(1)&&x.ConversionSource is not null);
        Play(g,a);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:wanglie");Replay(g,r);Reject(g);
        Require(Enumerable.Range(1,3).All(v=>g.CreateSnapshot(v).PendingDecision is null),"Wanglie optional decision is private to the actual user.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
        Require(g.CreateSnapshot(0).Players[0].ActualPlayPhaseCardUseState?.UseCount==1&&g.CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions is [var ban]&&ban.ActorSeat==0,"One actual first Slash issues one phase-scoped use prohibition.");var published=g.CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions!;Require(published is System.Collections.Generic.IList<IssuedPlayPhaseUseProhibition> frozen&&frozen.IsReadOnly,"Prepared public issued phase facts cannot be rewritten by an observer.");
        Require(!g.GetHumanLegalActions().Any(a=>a.CardId is not null&&a.Kind!=LegalActionKind.Recast),"Remaining hand entities cannot be used in the issued phase.");Replay(g,r);
        var before=State(g);var res=g.Submit(new PlayCardCommand(0,later.CardId!.Value,later.TargetSeats,g.Revision,Prompt(g)!.PromptId,later.PlayedCardKind,later.TargetCardId){ConversionSource=later.ConversionSource});Require(!res.Accepted&&before==State(g),"A remaining owned entity and previously legal ordinary-trick conversion is rejected atomically by the issued phase ban.");
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);Require(g.CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions is null,"The issued prohibition ends with the actual phase.");Replay(g,r);
    }
    private static void Grow(GameEngine g){Accept(g,new UseProgramSkillCommand(0,"fixture:driver","grow",[],[],g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);}
    private static PendingDecision? Prompt(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Play(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,Prompt(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(pick).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> done)
    {
        for(var i=0;i<300;i++)
        {
            if(Prompt(g) is {} p&&done(p))return;
            if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.ProgramTrigger})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
            else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.FireAttackReveal})Answer(g,c=>c.Cards.Count==1);
            else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.RescueDying})Answer(g,c=>c.Cards.Count==0);
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fixed fixture prompt unreachable: "+JsonSerializer.Serialize(Prompt(g)));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(v=>SnapshotJson.Serialize(g.CreateSnapshot(v))).ToArray(),Frames=g.ResolutionStack,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Movements=g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(restored),"All observers, actual entity movements, typed parent frames and command JSON replay match.");}
    private static void Reject(GameEngine g){var s=State(g);var p=Prompt(g)!;Require(!g.Submit(new AnswerPromptCommand((p.PlayerSeat+1)%4,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&s==State(g),"Wrong actor answer is atomically rejected.");Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new ChoiceId("illegal"),g.Revision)).Accepted&&s==State(g),"Illegal choice is atomically rejected.");}
    private static void Require(bool b,string m){if(!b)throw new InvalidOperationException(m);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected");}
    private static (GameEngine,ContentRegistry)Create(string mode)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-chendao-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:owner",g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard);return(g,r);
    }
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-wanglie",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var c=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:driver","revision":1,"viewAs":[{"id":"snatch","inputKinds":[],"inputSuits":[],"outputKind":"snatch","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},{"id":"multi-nullif","inputKinds":[],"inputSuits":[],"inputCount":2,"extendedUse":true,"outputKind":"nullification","forPlay":false,"forResponse":true}],"activations":[{"id":"all","minCards":1,"maxCards":64,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useAllHandCardsAsOrdinaryTrick","target":"owner","viewAsId":"all","outputKind":"arrowBarrage"}]},{"id":"grow","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},{"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]},{"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]},{"id":"strip-nullif","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"cardKinds":["nullification"],"count":1,"destination":"discardPile"}]},{"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":3}]},{"id":"gift-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardKinds":["slash"],"count":1,"destination":"selectedTargetHand"}]},{"id":"lose-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:wanglie"],"sourceBind":"standard:none"}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:driver":{"name":"真实抽牌","description":"最小实体夹具"}}}""");b.AddSkill(new("fixture:driver","真实抽牌","实体夹具"){Program=c.Programs["fixture:driver"]});
            var observer=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:observer","revision":1,"triggers":[{"id":"payment","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:observer":{"name":"真实付款移动观察","description":"嵌套暂停","optionLabels":{"continue":"继续"}}}}""");b.AddSkill(new("fixture:observer","移动观察","实体窗口"){Program=observer.Programs["fixture:observer"]});
            b.AddGeneral(new("fixture:owner","陈到机制","supporter",mode is "ai-fire" or "ai-slash"?"standard:none":"classic:wanglie","shu",12,mode=="nested"?["fixture:driver","fixture:observer"]:mode=="slash-program-dodge"?["fixture:driver","classic:shifei"]:["fixture:driver"]));for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:target-{i}","目标","supporter",mode is "ai-fire" or "ai-slash"?"classic:wanglie":"standard:none","wei",12,[]));
            b.AddDeck(new("fixture:deck","真实实体",3,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(mode=="duel-played"?i%2==0?"standard:duel":"standard:slash":mode is "equipment" or "borrowed_sword"? i%3==0?"standard:crossbow":i%3==1?"standard:slash":"classic:borrowed-sword":i%3==0?"standard:"+(mode=="ai-fire"?"fire_attack":mode=="conversion"?"dodge":mode is "nested" or "ai-slash" or "slash-program-dodge"?"slash":mode):i%3==1?"standard:dodge":"standard:nullification",Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-chendao-fixture","机制",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:owner","fixture:target-1","fixture:target-2","fixture:target-3"]));
        }
    }
}
