using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class FengLinLuZhiChecks
{
    public static void InitialReserveAndEndingExchange()
    {
        var(g,r)=Start();Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lz-gain");
        Require(g.CreateCardZoneDiagnostics().Count(z=>z.Location==CardLocation.Hand(0))==6,"GameStarting performs real draw two before reserve selection.");Replay(g,r);
        Answer(g,c=>true);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:mingren"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards"));
        var id=P(g)!.Choices.First(c=>c.Cards.Count==1).Cards[0];Answer(g,c=>c.Cards.Contains(id));ReachPlay(g);
        Require(Pile(g).Single().CardId==id,"Selected hand entity, not draw-pile top, is the reserve.");Replay(g,r);
        var initial=g.CardMovements.Count(m=>m.Reason.Value=="skill-program.public-pile.initial-hand");Require(initial==1,"Initial store executes once.");
        End(g);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:mingren"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("stage")=="owned"));var replacement=P(g)!.Choices.First(c=>c.Cards.Count==1).Cards[0];Answer(g,c=>c.Cards.Contains(replacement));Replay(g,r);Answer(g,c=>c.Cards.Contains(id));
        Require(Pile(g).Single().CardId==replacement&&g.CreateCardZoneDiagnostics().Single(z=>z.CardId==id).Location==CardLocation.Hand(0),"Ending swaps exactly one real hand card atomically.");Replay(g,r);
    }
    public static void PaidColorDamageNestedReplay()
    {
        var(g,r)=Start();Init(g);var before=g.CreateSnapshot(0).Players[1].Hp;
        Accept(g,new UseProgramSkillCommand(0,"classic:zhenliang","reserve-color-damage",[],[],g.Revision,P(g)!.PromptId));
        var cost=P(g)!.Choices.First(c=>c.Targets.SequenceEqual([1])).Cards.Single();Answer(g,c=>c.Cards.Contains(cost)&&c.Targets.SequenceEqual([1]));
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lz-cost");Require(g.CreateSnapshot(0).Players[1].Hp==before,"Damage waits for real paid-card movement child.");
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.PublicPileColorPayment is {CardId:var i}&&i==cost),"Payment and color are retained by the owning frame.");Replay(g,r);
        Require(Polarity(g)==SkillPolarity.Yin,"Successful Yang flips exactly once before nested cost.");Answer(g,c=>true);ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[1].Hp==before-1&&g.CardMovements.Count(m=>m.CardId==cost&&m.Reason.Value=="skill-program.public-pile.color-payment")==1,"Resume pays once and deals exactly one actual damage.");
        Require(g.GetHumanLegalActions().All(a=>a.ProgramSkillId!="classic:zhenliang"),"Same Play phase no repeated Yang activation.");Replay(g,r);
    }
    public static void ActualResponseDiscardAndOncePerAction()
    {
        var(g,r)=Start();Init(g);Accept(g,new UseProgramSkillCommand(0,"classic:zhenliang","reserve-color-damage",[],[],g.Revision,P(g)!.PromptId));Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lz-cost");Answer(g,c=>true);ReachPlay(g);
        End(g);Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.RespondDodge);
        var choice=P(g)!.Choices.First(c=>c.Cards.Count==2);var ids=choice.Cards.ToArray();Answer(g,c=>c.Id==choice.Id);
        Reach(g,p=>p.SkillPrompt?.SkillId=="classic:zhenliang"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        Require(ids.All(id=>g.CardMovements.Any(m=>m.CardId==id&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.ResponseFinished)),"A true two-entity Dodge has actually entered Discard before reward.");Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");var hand=g.CreateSnapshot(0).Players[0].HandCount;Answer(g,c=>c.Targets.SequenceEqual([0]));
        Require(g.CreateSnapshot(0).Players[0].HandCount==hand+1&&Polarity(g)==SkillPolarity.Yang,"One response action gives one draw and flips once.");
        var facts=g.Events.Select(e=>e.Payload).OfType<ActionCardsDiscardedEvent>().Where(e=>e.ActorSeat==0&&e.TurnNumber==g.State.TurnNumber).ToArray();Require(facts.Length==2&&facts.Select(f=>f.ActionId).Distinct().Count()==1,"Two physical cleanup batches retain one exact accepted response action.");
        Require(g.Events.Count(e=>e.Payload is ActionColorRewardOfferedEvent)==1,"Two entities offer the conversion once.");Replay(g,r);
    }
    public static void MixedResponseDoesNotReward()
    {
        var(g,r)=Start(true);Init(g);Accept(g,new UseProgramSkillCommand(0,"classic:zhenliang","reserve-color-damage",[],[],g.Revision,P(g)!.PromptId));Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lz-cost");Answer(g,c=>true);ReachPlay(g);End(g);Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.RespondDodge);
        var cards=g.CreateSnapshot(0,true).Players[0].Hand;
        var choice=P(g)!.Choices.First(c=>c.Cards.Count==2&&cards.Where(x=>c.Cards.Contains(x.Id)).Select(x=>x.Suit is Suit.Heart or Suit.Diamond).Distinct().Count()==2);var ids=choice.Cards.ToArray();Answer(g,c=>c.Id==choice.Id);
        for(var n=0;n<20&&!ids.All(id=>g.CardMovements.Any(m=>m.CardId==id&&m.Reason==CardMoveReasons.ResponseFinished));n++)Accept(g,new AdvanceOneStepCommand(g.Revision));
        Require(ids.All(id=>g.CardMovements.Any(m=>m.CardId==id&&m.Reason==CardMoveReasons.ResponseFinished)),"Mixed physical response really completes.");
        Require(g.Events.Select(e=>e.Payload).OfType<ActionCardsDiscardedEvent>().Where(e=>e.ActorSeat==0).All(e=>e.IsRed is null)&&!g.Events.Any(e=>e.Payload is ActionColorRewardOfferedEvent),"Mixed effective colors cannot match a single reserve color.");Replay(g,r);
    }
    public static void ActualDiscardEntryBoundaries()
    {
        foreach(var boundary in new[]{2})
        {
            var(g,r)=Start(boundary:boundary);Init(g);Accept(g,new UseProgramSkillCommand(0,"classic:zhenliang","reserve-color-damage",[],[],g.Revision,P(g)!.PromptId));Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lz-cost");Answer(g,c=>true);ReachPlay(g);End(g);Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.RespondDodge);
            var choice=P(g)!.Choices.First(c=>c.Cards.Count==2);var ids=choice.Cards.ToArray();Answer(g,c=>c.Id==choice.Id);

            {
                Reach(g,p=>p.SkillPrompt?.SkillId=="classic:funan");var parent=g.ResolutionStack.OfType<CardUseFrame>().Last().Id;Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
                for(var n=0;n<20&&!ids.All(id=>g.CreateCardZoneDiagnostics().Single(z=>z.CardId==id).Location==CardLocation.Hand(1));n++){if(P(g) is {} pending)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip"||c.Cards.Count==0);else Accept(g,new AdvanceOneStepCommand(g.Revision));}
                for(var n=0;n<40&&!g.Events.Any(e=>e.Payload is CardUseFinishedEvent f&&f.ResolutionId==parent);n++)
                {if(P(g) is {PlayerSeat:0})Answer(g,c=>c.Cards.Count==0);else Accept(g,new AdvanceOneStepCommand(g.Revision));}
                Require(g.Events.Any(e=>e.Payload is CardUseFinishedEvent f&&f.ResolutionId==parent),"The actual incoming Use and response cleanup finish before the no-entry assertion.");
                Require(ids.All(id=>g.CreateCardZoneDiagnostics().Single(z=>z.CardId==id).Location==CardLocation.Hand(1))&&!ids.Any(id=>g.CardMovements.Any(m=>m.CardId==id&&m.To==CardLocation.DiscardPile)),"Funan transfers actual response entities before discard entry.");
                Require(!g.Events.Any(e=>e.Payload is ActionColorRewardOfferedEvent)&&Polarity(g)==SkillPolarity.Yin,"Never-entered response has no color reward.");Replay(g,r);
            }
        }
    }
    public static void StrictResourcesAndLegacyColorAbi()
    {
        var a=new CardActionContext(1,null,CardActionType.Use,0,0,null,null,null,CardKind.Slash,[],[],[]);Require(!JsonSerializer.Serialize(a).Contains("EffectiveIsRed"),"Legacy context omits additive color fact.");
        var(legacy,legacyRegistry)=Start(legacy:true);ReachPlay(legacy);End(legacy);var oldGate=false;
        try{Reach(legacy,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.RespondDodge);}catch(InvalidOperationException ex) when(ex.Message.StartsWith("The response card ")&&ex.Message.EndsWith("cannot be used as Dodge.")){oldGate=true;}
        Require(oldGate,"Registry without action-color op retains original rejection of multi-entity response gate.");
        var asm=typeof(StandardClassicGeneralPackage).Assembly;string Read(string kind){using var s=asm.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.classic-lu-zhi."+kind+".json")!;using var reader=new StreamReader(s);return reader.ReadToEnd();}
        foreach(var bad in new[]{Read("rules").Replace("gameStarting","drawPhaseStarting"),Read("rules").Replace("discardPileReceived","cardsGained"),Read("rules").Replace("turnEnding","drawPhaseStarting")}){var failed=false;try{SkillProgramCatalog.Load(bad,Read("presentation"));}catch(InvalidOperationException){failed=true;}Require(failed,"Wrong reserve/action window is rejected by generic resources.");}
    }
    private static SkillPolarity? Polarity(GameEngine g)=>g.CreateSnapshot(0).Players[0].SkillRuntimeStates!.Single(s=>s.SkillId=="classic:zhenliang").Polarity;
    private static IReadOnlyList<CardZoneDiagnostic> Pile(GameEngine g)=>g.CreateCardZoneDiagnostics().Where(z=>z.Location.Zone==CardZoneKind.PublicPersistentPile&&z.Location.OwnerSeat==0).ToArray();
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(i=>g.CreateSnapshot(i,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Require(bool b,string text){if(!b)throw new InvalidOperationException(text);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(c);Require(result.Error is null,result.Error?.Message??"Rejected");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);string State(GameEngine x)=>JsonSerializer.Serialize(Enumerable.Range(0,4).Select(i=>x.CreateSnapshot(i,true)))+JsonSerializer.Serialize(x.CreateCardZoneDiagnostics())+JsonSerializer.Serialize(x.ResolutionStack);Require(State(g)==State(restored),"Paused native all-viewer state and typed frames replay exactly.");Require(g.CreateCardZoneDiagnostics().Select(z=>z.CardId).Distinct().Count()==80,"All physical entities conserved.");}
    private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Reach(GameEngine g,Func<PendingDecision,bool> stop){for(var n=0;n<120;n++){if(P(g)is{}p&&stop(p))return;if(P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard})End(g);else if(P(g)is{PlayerSeat:0})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip"||c.Parameters.GetValueOrDefault("response")=="let-die"||c.Parameters.GetValueOrDefault("response")=="take-damage"||c.Parameters.GetValueOrDefault("token")=="finish"||c.Cards.Count<=1);else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Missing boundary "+JsonSerializer.Serialize(P(g)));}
    private static void Init(GameEngine g){Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lz-gain");Answer(g,c=>true);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:mingren"&&p.Choices.Any(c=>c.Cards.Count==1));Answer(g,c=>c.Cards.Count==1);ReachPlay(g);}
    private static (GameEngine,ContentRegistry) Start(bool mixed=false,bool legacy=false,int boundary=0)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new Fixture(mixed,legacy,boundary),legacy?new LegacyEmpty():new StandardClassicGeneralPackage());var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="fixture:lz-mode",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:lz-owner",g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class LegacyEmpty:IGameContentPackage
    {public PackageManifest Manifest{get;}=new("fixture:lz-legacy-empty",new Version(1,0,0),[]);public void Register(IContentRegistryBuilder b){}}
    private sealed class Fixture(bool mixed,bool legacy,int boundary):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:lz",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var c=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:lz-response","revision":1,"viewAs":[{"id":"dodge-two","inputKinds":[],"inputSuits":[],"inputCount":2,"outputKind":"dodge","forPlay":false,"forResponse":true}]},{"id":"fixture:lz-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.classic:mingren.Draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"resume","options":[{"id":"continue"}]}]}]},{"id":"fixture:lz-cost","revision":1,"triggers":[{"id":"paid","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementOccurrence":"perBatch","movementReasons":["skill-program.public-pile.color-payment"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"resume","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:lz-response":{"name":"两牌应答","description":"测试"},"fixture:lz-gain":{"name":"开局摸牌子链","description":"测试","optionLabels":{"continue":"继续"}},"fixture:lz-cost":{"name":"真实费用子链","description":"测试","optionLabels":{"continue":"继续"}}}}""");foreach(var item in c.Programs)b.AddSkill(new(item.Key,item.Key,item.Key){Program=item.Value});
            b.AddGeneral(new("fixture:lz-owner","卢植机制","supporter",legacy?"fixture:lz-response":"classic:mingren","qun",20,legacy?[]:["classic:zhenliang","fixture:lz-gain","fixture:lz-cost","fixture:lz-response"]));for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:lz-target-{i}","目标","supporter","standard:none","qun",20,i==1&&boundary==2?["classic:funan"]:[]));
            b.AddDeck(new("fixture:lz-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard(i%3==0?"standard:dodge":"standard:slash",i%2==0?Suit.Spade:mixed?Suit.Heart:Suit.Club,i%13+1)).ToArray()});b.AddMode(new("fixture:lz-mode","测试",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:lz-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:lz-owner","fixture:lz-target-1","fixture:lz-target-2","fixture:lz-target-3"]));
        }
    }
}
