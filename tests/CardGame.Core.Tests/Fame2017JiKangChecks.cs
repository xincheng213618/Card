using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class Fame2017JiKangChecks
{
    public static void RealRandomEquipReplacementNestedReplay()
    {
        var (g,r)=Create("equipment");
        var before=g.CreateSnapshot(0).Players[0].Hp;var beforeDraw=g.CardMovements.Count(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(0));
        Use(g,"damage",[0]);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:qingxian");Replay(g,r);RejectUnknown(g);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Answer(g,c=>c.Targets.Contains(0));Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="lose");
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:equipment-observer");Replay(g,r);RejectUnknown(g);Answer(g,c=>true);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Require(g.CreateSnapshot(0).Players[0].Hp==before-2 && g.CardMovements.Any(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Processing) && g.Events.Select(e=>e.Payload).OfType<EquipmentChangedEvent>().Count()==1,"Damage and choice lose real HP; random equipment uses a true deck entity through actual equipment use.");
        Require(g.CardMovements.Count(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(0))==beforeDraw+1,"One actual Club equipment use grants exactly Qingxian's one-card draw reward.");
        Replay(g,r);
        var original=g.Events.Select(e=>e.Payload).OfType<EquipmentChangedEvent>().Single().CardId;
        Use(g,"damage",[0]);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:qingxian");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Targets.Contains(0));Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="lose");
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:equipment-observer");Replay(g,r);RejectUnknown(g);Answer(g,c=>true);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Require(g.Events.Select(e=>e.Payload).OfType<EquipmentChangedEvent>().Last().ReplacedCardId==original && g.CardMovements.Any(m=>m.CardId==original&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.DiscardPile),"A second real deck equipment use replaces exactly the old physical entity, including the nested movement trigger.");Replay(g,r);
        Use(g,"heal",[0]);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Use(g,"damage",[0]);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:qingxian");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Targets.Contains(0));Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="recover");
        Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-and-move-owned-card"));Replay(g,r);Answer(g,c=>c.Cards.Count==1);Reach(g,p=>p.Kind==DecisionKind.PlayCard);Replay(g,r);
    }
    public static void NativeDeathBequestShieldAndExpiry()
    {
        foreach(var role in new[]{Role.Rebel,Role.Loyalist})
        {
            var (g,r)=Create("bequest");var donor=g.CreateSnapshot(0,true).Players.First(p=>p.Seat!=0&&p.Role==role).Seat;
            Use(g,"death",[donor]);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
            var gift=g.Events.Select(e=>e.Payload).OfType<RandomSkillGrantedEvent>().Single();var shield=g.Events.Select(e=>e.Payload).OfType<BeneficiarySuitShieldGrantedEvent>().Single().Shield;
            Require(gift.Source.OwnerSeat==donor&&!g.CreateSnapshot(0).Players[donor].IsAlive&&gift.BeneficiarySeat==shield.BeneficiarySeat&&new[]{"classic:jixian","classic:liexian","classic:rouxian","classic:hexian"}.Contains(gift.SkillId),"Native dying/death continuation grants one seeded actual residual skill to a living beneficiary.");
            Require(Enumerable.Range(0,4).All(v=>g.CreateSnapshot(v).Players[shield.BeneficiarySeat].BeneficiarySuitShields?.Single()==shield)&&g.CreateSnapshot(0).Players[shield.BeneficiarySeat].Skills!.Any(s=>s.ContentId==gift.SkillId),"All observers see the exact persistent source attribution and real acquired skill after its donor dies.");Replay(g,r);
            if(shield.BeneficiarySeat!=0)Require(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.Contains(shield.BeneficiarySeat)),"Other actors' actual Club Slash legal actions exclude the shielded beneficiary.");
            else
            {var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);Play(g,equip);Reach(g,p=>p.Kind==DecisionKind.PlayCard);Require(g.CardMovements.Any(m=>m.CardId==equip.CardId&&m.To==CardLocation.Equipment(0)),"The shield does not forbid the beneficiary's real self-use of Club equipment.");}
            if (role==Role.Rebel && shield.BeneficiarySeat!=0)
            {
                var arrow=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.ArrowBarrage);var hp=g.CreateSnapshot(0).Players.Select(p=>p.Hp).ToArray();Play(g,arrow);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
                var use=g.Events.Select(e=>e.Payload).OfType<CardUseDeclaredEvent>().Last(e=>e.CardId==arrow.CardId);
                Require(g.Events.Select(e=>e.Payload).OfType<TargetsConfirmedEvent>().Single(e=>e.ResolutionId==use.ResolutionId).TargetSeats.All(t=>t!=shield.BeneficiarySeat) && g.CreateSnapshot(0).Players[shield.BeneficiarySeat].Hp==hp[shield.BeneficiarySeat] && g.CreateSnapshot(0).Players.Any(p=>p.Seat!=0&&p.IsAlive&&p.Seat!=shield.BeneficiarySeat&&p.Hp==hp[p.Seat]-1), "A true Club global use omits the protected seat in frozen targets while another original seat receives its actual effect.");Replay(g,r);
            }
            var startCount=g.Events.Count(e=>e.Payload is TurnStartedEvent t&&t.ActorSeat==shield.BeneficiarySeat);
            Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));
            for(var step=0;step<100;step++)
            {
                if(g.Events.Count(e=>e.Payload is TurnStartedEvent t&&t.ActorSeat==shield.BeneficiarySeat)>startCount)break;
                if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.RespondDodge})Answer(g,c=>c.Cards.Count==0);else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.RespondSlash})Answer(g,c=>c.Cards.Count==0);else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");else Accept(g,new AdvanceOneStepCommand(g.Revision));
            }
            Require(g.CreateSnapshot(0).Players[shield.BeneficiarySeat].BeneficiarySuitShields is null,"Expiry follows the beneficiary's next actual TurnStarted, independent of dead source ownership.");Replay(g,r);
            Require(!g.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat!=0),"Death bequest activation and beneficiary selection run through native AI.");
        }
    }
    private static void Use(GameEngine g,string id,IReadOnlyList<int> targets)=>Accept(g,new UseProgramSkillCommand(0,"fixture:equipment-driver",id,[],targets,g.Revision,Prompt(g)!.PromptId));
    private static PendingDecision? Prompt(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Play(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,Prompt(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate)
    {
        for(var step=0;step<250;step++)
        {if(Prompt(g) is { } p && predicate(p))return;if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.RespondDodge})Answer(g,c=>c.Cards.Count==0);else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.RespondSlash})Answer(g,c=>c.Cards.Count==0);else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.ProgramTrigger}) Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip" || c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
        throw new InvalidOperationException("Fixed Ji Kang fixture did not reach requested prompt: "+JsonSerializer.Serialize(Prompt(g))+" frames="+JsonSerializer.Serialize(g.ResolutionStack));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Movements=g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(restored),"All observer/private snapshots, typed frames, entities and command JSON replay identically.");}
    private static void RejectUnknown(GameEngine g){var before=State(g);var p=Prompt(g)!;var result=g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new ChoiceId("fixture:illegal"),g.Revision));Require(!result.Accepted&&before==State(g),"Illegal input leaves every view, entity, event and accepted command untouched.");}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(r.Accepted,r.Error?.Message??"Rejected.");}
    private static void Require(bool b,string m){if(!b)throw new InvalidOperationException(m);}
    private static (GameEngine,ContentRegistry) Create(string mode)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:equipment-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:equipment-owner",g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard);return(g,r);
    }
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-equipment",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:equipment-driver","revision":1,"viewAs":[{"id":"borrowed","inputKinds":[],"inputSuits":[],"outputKind":"borrowedSword","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},{"id":"duel","inputKinds":[],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true,"excludeOwnerEffects":true}],"modifiers":[{"id":"range","query":"attackRange","operation":"set","value":3,"priority":0}],"activations":[{"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]},{"id":"all","minCards":1,"maxCards":64,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useAllHandCardsAsOrdinaryTrick","target":"owner","viewAsId":"all","outputKind":"arrowBarrage"}]},{"id":"death","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},{"id":"drain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":3}]},{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},{"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},{"id":"heal","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":1}]}]},{"id":"fixture:equipment-observer","revision":1,"triggers":[{"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardCategories":["equipment"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]}]},{"id":"movement","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementReasons":["equipment.enter","equipment.replace","skill-program.classic:qingxian.SelectAndMoveOwnedCard"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:equipment-driver":{"name":"实体装备驱动","description":"真实触发"},"fixture:equipment-observer":{"name":"装备移动观察","description":"嵌套暂停","optionLabels":{"continue":"继续"}}}}""");
            var bequest=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:bequest","revision":1,"triggers":[{"id":"gift","window":"ownerDied","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingMale"},{"op":"grantRandomSkillAndSuitShield","target":"selectedTarget","skillIds":["classic:jixian"],"suits":["{{(mode=="effective"?"heart":"club")}}"]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:bequest":{"name":"定向遗赠","description":"真实死亡公共机制"}}}""");
            foreach(var entry in bequest.Programs)b.AddSkill(new(entry.Key,entry.Key,entry.Key){Program=entry.Value});
            foreach(var p in catalog.Programs)b.AddSkill(new(p.Key,p.Key,p.Key){Program=p.Value});
            b.AddGeneral(new("fixture:equipment-owner","嵇康机制","supporter","fixture:equipment-driver","wei",4,mode is "jixian" or "liexian" or "rouxian" or "hexian"?["classic:"+mode,"fixture:equipment-observer"]:(mode=="effective"?["classic:hongyan","fixture:equipment-observer"]:mode=="caishi"?["classic:qingxian","classic:caishi","fixture:equipment-observer"]:["classic:qingxian","classic:juexiang","fixture:equipment-observer"]),mode.StartsWith("multi")||mode=="effective"?GeneralGender.Female:GeneralGender.Male));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:equipment-{i}","目标","supporter","standard:none","wei",4,(mode.StartsWith("multi")||mode=="effective")?["fixture:bequest"]:mode.StartsWith("bequest")?["classic:juexiang"]:[]));
            b.AddDeck(new("fixture:equipment-deck","真实装备池",4,2,[]){PhysicalCards=Enumerable.Range(0,90).Select(i=>new ContentDeckPhysicalCard(mode=="multi-borrowed"?"standard:crossbow":mode=="effective"||mode=="empty-equipment"?"standard:slash":mode=="rescue"&&i%2==0?"standard:peach":(mode.StartsWith("bequest")||mode.StartsWith("multi"))&&i%3==0?"standard:arrow_barrage":(mode.StartsWith("bequest")||mode.StartsWith("multi"))&&i%3==1?"standard:slash":"standard:crossbow",mode=="effective"?Suit.Spade:mode=="multi-mixed"&&i%2==0?Suit.Heart:Suit.Club,2)).ToArray()});
            b.AddMode(new("identity:equipment-fixture","机制",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:equipment-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:equipment-owner","fixture:equipment-1","fixture:equipment-2","fixture:equipment-3"]));
        }
    }
}
