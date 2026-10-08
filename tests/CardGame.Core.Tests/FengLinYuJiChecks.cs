using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static partial class FengLinYuJiChecks
{
    public static void AllPassPrivacyQuotaAndReplay()
    {
        var(g,r)=Start("standard:dodge"); var initial=g.State.Players[0].HandCount;
        Use(g,CardKind.DrawTwo); var frame=g.ResolutionStack.OfType<CardDeclarationFrame>().Single();var cost=frame.Payment.Cost.CardId;
        Require(g.CardMovements.Count(m=>m.CardId==cost&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1,"Actual hand payment occurs once before challenge.");
        Require(!g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.PhysicalCards.Any(c=>c.CardId==cost)),"No success action before verification.");
        Privacy(g,CardKind.Dodge);Replay(g,r); var seats=new List<int>();
        while(g.ResolutionStack.LastOrDefault() is CardDeclarationChallengeFrame){seats.Add(P(g)!.PlayerSeat);Answer(g,"pass");if(g.ResolutionStack.OfType<CardDeclarationFrame>().Any()){Privacy(g,CardKind.Dodge);Replay(g,r);}}
        Require(seats.Count==3&&seats.Distinct().Count()==3&&!seats.Contains(0),"All pass traverses each eligible other seat once and terminates.");
        Settle(g);Require(g.State.Players[0].HandCount==initial+1,"Unchallenged declared DrawTwo resumes the actual two-card effect after one cost.");
        Require(g.CardMovements.Count(m=>m.CardId==cost&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1,"No payment repeated on continuation.");
        Require(!g.GetHumanLegalActions().Any(a=>a.ConversionSource?.SkillId=="classic:guhuo"),"All declaration names share the named Turn quota.");Replay(g,r);Conserve(g);
    }
    public static void FirstChallengeTrueAndFalse()
    {
        foreach(var real in new[]{false,true})
        {
            var(g,r)=Start(real?"standard:draw_two":"standard:dodge");var initial=g.State.Players[0].HandCount;
            Use(g,CardKind.DrawTwo);var id=g.ResolutionStack.OfType<CardDeclarationFrame>().Single().Payment.Cost.CardId;
            var challenger=P(g)!.PlayerSeat;Replay(g,r);Answer(g,"challenge");Settle(g);
            Require(g.Events.Select(e=>e.Payload).OfType<CardDeclarationRevealedEvent>().Count()==1,"First challenge stops further seats and flips one entity.");
            Require(g.State.Players[0].HandCount==initial+(real?1:-1),"Only true declaration executes actual DrawTwo.");
            Require(g.CreateSnapshot(challenger).Players[challenger].Skills?.Any(s=>s.ContentId=="classic:chanyuan")==real,"True challenger acquires Chanyuan; fake grants no punishment.");
            Require(g.CardMovements.Count(m=>m.CardId==id&&m.To==CardLocation.DiscardPile)==1,"Both outcomes clean the actual entity once.");Replay(g,r);Conserve(g);
        }
    }
    public static void ActualDodgeDuelRescueParents()
    {
        foreach(var purpose in new[]{"dodge","duel","peach","alcohol"})
        {
            var(g,r)=Start(purpose=="peach"?"standard:peach":purpose=="alcohol"?"standard:alcohol":"standard:dodge");
            var hp=g.State.Players[0].Hp;
            if(purpose=="dodge") Driver(g,"poke",[1]);
            else if(purpose=="duel")Driver(g,"duel",[1,0]);
            else Driver(g,"dying");
            Reach(g,p=>p.PlayerSeat==0&&(p.Kind==DecisionKind.RespondDodge||p.Kind==DecisionKind.RespondSlash||p.Kind==DecisionKind.RescueDying));
            var prompt=P(g)!;var choice=prompt.Choices.First(c=>c.Parameters.GetValueOrDefault("conversion-skill-id")=="classic:guhuo");
            Accept(g,new AnswerPromptCommand(0,prompt.PromptId,choice.Id,g.Revision));
            Require(g.ResolutionStack.OfType<CardDeclarationFrame>().Any(),"Actual response/rescue suspends before successful action.");Replay(g,r);
            while(g.ResolutionStack.LastOrDefault() is CardDeclarationChallengeFrame)Answer(g,"pass");
            if(purpose=="dodge")Require(g.State.Players[0].Hp==hp,"Declared Dodge resumes the real incoming Slash without damage.");
            else if(purpose=="duel")Require(g.Events.Select(e=>e.Payload).OfType<DuelResponseEvent>().Any(e=>e.ResponderSeat==0&&e.UsedSlash),"Played Slash returns to real virtual Duel, not Use.");
            else Require(g.State.Players[0].Hp==1&&g.Events.Select(e=>e.Payload).OfType<DyingResponseEvent>().Any(e=>e.ResponderSeat==0&&(e.UsedPeach||e.UsedAlcohol)),"Dying-use returns after real recovery.");
            Replay(g,r);Conserve(g);
        }
    }
    public static void ActualCounterspellAndProviderParents()
    {
        var(g,r)=Start("mixed-counterspell");var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Duel&&a.ConversionSource is null);
        Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats.Count>0?a.TargetSeats:[a.TargetSeat!.Value],g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.Kind==DecisionKind.Nullification&&p.PlayerSeat==0);var p=P(g)!;var c=p.Choices.First(c=>c.Parameters.GetValueOrDefault("conversion-skill-id")=="classic:guhuo");
        Accept(g,new AnswerPromptCommand(0,p.PromptId,c.Id,g.Revision));var depth=g.ResolutionStack.OfType<NullificationWindowFrame>().Last().ChainDepth;Replay(g,r);
        while(g.ResolutionStack.LastOrDefault() is CardDeclarationChallengeFrame)Answer(g,"pass");
        Require(g.Events.Select(e=>e.Payload).OfType<NullificationRespondedEvent>().Any(e=>e.ResponderSeat==0&&e.ChainDepth==depth+1),"Real counterspell commits and toggles its actual window only after verification.");Replay(g,r);Conserve(g);
        var(provider,pr)=Start("standard:draw_two",true);Driver(provider,"request",[3]);
        Reach(provider,p=>provider.ResolutionStack.LastOrDefault() is CardDeclarationChallengeFrame);
        var declaration=provider.ResolutionStack.OfType<CardDeclarationFrame>().Last();Require(declaration.OwnerSeat!=0&&declaration.Return.ActorSeat==0,"Actual provider owns its payment and quota independently from requester.");Replay(provider,pr);
        while(provider.ResolutionStack.LastOrDefault() is CardDeclarationChallengeFrame)Answer(provider,"pass");
        Require(provider.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.ActorSeat==0&&e.Action.ProviderSeat==declaration.OwnerSeat&&e.Action.RequesterSeat==0),"Actual provided Slash preserves Actor/Provider/Requester provenance.");Replay(provider,pr);Conserve(provider);
    }
    private static void Driver(GameEngine g,string id,IReadOnlyList<int>? targets=null)=>Accept(g,new UseProgramSkillCommand(0,"fixture:yu-driver",id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Privacy(GameEngine g,CardKind costKind)
    {
        for(var viewer=0;viewer<4;viewer++)
        {
            var d=g.CreateSnapshot(viewer).CardDeclarations!.Single();Require(!d.IsRevealed&&d.RevealedCard is null&&(viewer==0?d.OwnerCost?.Kind==costKind:d.OwnerCost is null),"Only owner sees private cost; no viewer receives unrevealed face.");
            Require(d.TargetSeats is not int[],"Public nested targets detached and frozen.");
        }
        Require(g.Log.Where(l=>l.Type is "CardUsed" or "CardResponded" or "SkillTriggered").All(l=>!l.Message.Contains("【闪】")),"Public declaration logs hide unrevealed physical name.");
    }
    private static (GameEngine,ContentRegistry) Start(string card,bool providers=false,bool declarations=true,bool costChild=false,ContentRegistry? registry=null)
    {
        var r=registry??(declarations?ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(card,providers,declarations,costChild)):ContentRegistry.Build(new StandardContentPackage(),new Fixture(card,providers,declarations,costChild)));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=card=="mixed-borrowed"?"identity:classic-yuji-borrowed":"identity:yuji-check",UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:yuji",g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);return(g,r);
    }
    private static void Use(GameEngine g,CardKind kind)
    {
        var a=g.GetHumanLegalActions().First(a=>a.PlayedCardKind==kind&&a.ConversionSource?.SkillId=="classic:guhuo");
        Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats.Count>0?a.TargetSeats:a.TargetSeat is {} t?[t]:[],g.Revision,P(g)!.PromptId,kind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});
    }
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Answer(GameEngine g,string branch){var p=P(g)!;var c=p.Choices.Single(c=>c.Parameters.GetValueOrDefault("declaration-answer")==branch);Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c.Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> goal){for(var i=0;i<100;i++){if(P(g) is {} p&&goal(p))return;Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Fixed fixture boundary not reached: "+P(g)?.Kind+" / "+P(g)?.PlayerSeat+" / "+string.Join(",",g.ResolutionStack.Select(f=>f.Kind)));}
    private static void Settle(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected fixture input");}
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))==SnapshotJson.Serialize(restored.CreateSnapshot(s)))&&g.CardMovements.SequenceEqual(restored.CardMovements)&&JsonSerializer.Serialize(g.ResolutionStack)==JsonSerializer.Serialize(restored.ResolutionStack),"Four viewer and exact typed stack restore from accepted commands.");}
    private static void Conserve(GameEngine g)=>Require(g.CreateCardZoneDiagnostics().Count==80&&g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==80,"Single physical entity ledger.");
    private static void Require(bool condition,string text){if(!condition)throw new InvalidOperationException(text);}
    private sealed class Fixture(string card,bool providers,bool declarations,bool costChild):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-yuji",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:yu-driver","revision":1,"cardPolicies":[{"id":"fixture-providers","kind":"factionResponseRequest","requiredCardKinds":["slash"],"factionId":"qun","ownerRole":"lord"}],"activations":[{"id":"strip-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"cardKinds":["slash"],"count":1,"destination":"discardPile"}]},{"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]},{"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},{"id":"duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]},{"id":"dying","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]},{"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestFactionCard","target":"selectedTarget","providerFactionId":"qun","requiredKind":"slash"}]}]},{"id":"fixture:yu-retaliate","revision":1,"triggers":[{"id":"retaliate","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"eventSource"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:yu-driver":{"name":"真实子链","description":"真实子链"},"fixture:yu-retaliate":{"name":"反击","description":"反击"}}}""");
            foreach(var pair in catalog.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value});
            if(card=="mixed-borrowed")
            {
                if(!declarations)b.AddCard(new("classic:borrowed-sword","借刀杀人","锦囊牌","固定真实借刀",LegacyKind:CardKind.BorrowedSword));
                var policy=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:yu-faction","revision":1,"cardPolicies":[{"id":"allied-slash","kind":"factionResponseRequest","requiredCardKinds":["slash"],"factionId":"qun"}]}]}""","""{"schemaVersion":3,"skills":{"fixture:yu-faction":{"name":"真实供杀请求","description":"真实供杀请求"}}}""");
                b.AddSkill(new("fixture:yu-faction","真实供杀请求","真实供杀请求"){Program=policy.Programs["fixture:yu-faction"]});
            }
            b.AddSkill(new("fixture:yu-marker","固定候选","固定候选"){SelectionWeights=new Dictionary<Role,double>{[Role.Lord]=-1000}});
            if(costChild)
            {
                var c=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:yu-cost-child","revision":1,"triggers":[{"id":"pay-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["conversion.declaration.pay"],"optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:yu-cost-child":{"name":"真实支付子链","description":"真实支付子链"}}}""");
                foreach(var pair in c.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value});
            }
            b.AddGeneral(new("fixture:yuji","于吉机制","supporter",declarations?"classic:guhuo":"standard:none","qun",3,
                new[]{"fixture:yu-marker","fixture:yu-driver"}.Concat(declarations?["classic:hujia"]:Array.Empty<string>()).Concat(costChild?["fixture:yu-cost-child"]:Array.Empty<string>()).ToArray()));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:yu-{i}","其他"+i,"supporter","standard:none",card=="mixed-borrowed"?"qun":providers?"qun":i==1?"wei":i==2?"qun":"wu",4,card=="mixed-borrowed"?["fixture:yu-faction"]:providers?["fixture:yu-retaliate","classic:guhuo"]:declarations?["fixture:yu-retaliate"]:Array.Empty<string>()));
            b.AddDeck(new("fixture:yu-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard(card=="mixed-borrowed"?(declarations?(i%2==0?"classic:borrowed-sword":"standard:crossbow"):i%3==0?"classic:borrowed-sword":i%3==1?"standard:crossbow":"standard:slash"):card=="mixed-counterspell"?(i%2==0?"standard:duel":"standard:nullification"):card,(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new(card=="mixed-borrowed"?"identity:classic-yuji-borrowed":"identity:yuji-check","于吉机制",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:yu-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:yuji","fixture:yu-1","fixture:yu-2","fixture:yu-3"]));
        }
    }
}
