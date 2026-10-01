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
    public static void RecoveryNativeAiRealDiscard()
    {
        var (g,r)=Create("equipment");Use(g,"wound",[0]);Reach(g,p=>p.Kind==DecisionKind.PlayCard);Use(g,"heal",[0]);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:qingxian");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Targets.Any(t=>t!=0));
        Reach(g,p=>p.Kind==DecisionKind.PlayCard);Require(!g.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat!=0),"The chosen beneficiary makes its own effect/equipment choices through actual AdvanceOneStep.");Require(g.CardMovements.Any(m=>m.From.Zone==CardZoneKind.Hand&&m.From.OwnerSeat!=0&&m.To==CardLocation.DiscardPile),"The normal AI recovery branch discards a real equipment hand entity.");Replay(g,r);
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
    public static void ResidualBranchesRealEntitiesWithoutClubReward()
    {
        foreach(var mode in new[]{"jixian","liexian","rouxian","hexian"})
        {
            var (g,r)=Create(mode);var beforeDraw=g.CardMovements.Count(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(0));
            if(mode is "jixian" or "rouxian")Use(g,"damage",[0]);else{Use(g,"wound",[0]);Reach(g,p=>p.Kind==DecisionKind.PlayCard);Use(g,"heal",[0]);}
            Reach(g,p=>p.SkillPrompt?.SkillId=="classic:"+mode);Replay(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Targets.Contains(mode is "jixian" or "rouxian"?0:1));
            if(mode=="rouxian"){Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-and-move-owned-card"));Answer(g,c=>c.Cards.Count==1);}
            Reach(g,p=>p.Kind==DecisionKind.PlayCard);
            Require(g.CardMovements.Count(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(0))==beforeDraw,"Residual skill Club equipment does not inherit Qingxian's draw reward.");
            Require(mode is "jixian" or "liexian"?g.Events.Select(e=>e.Payload).OfType<EquipmentChangedEvent>().Any():g.CardMovements.Any(m=>m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment&&m.To==CardLocation.DiscardPile),"Each actual damage/recovery residual branch consumes its real equipment use or discard.");Replay(g,r);
        }
    }
    public static void EmptyEquipmentPoolsAndLossDeathStop()
    {
        var (g,r)=Create("empty-equipment");Use(g,"damage",[0]);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:qingxian");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Targets.Contains(0));Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="lose");Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Require(!g.Events.Select(e=>e.Payload).OfType<EquipmentChangedEvent>().Any(),"No real deck equipment creates no fabricated use or Club draw.");Replay(g,r);
        var (death,dr)=Create("liexian");Use(death,"drain",[1]);Reach(death,p=>p.Kind==DecisionKind.PlayCard);Use(death,"wound",[0]);Reach(death,p=>p.Kind==DecisionKind.PlayCard);Use(death,"heal",[0]);Reach(death,p=>p.SkillPrompt?.SkillId=="classic:liexian");Answer(death,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(death,c=>c.Targets.Contains(1));Reach(death,p=>p.Kind==DecisionKind.PlayCard);
        Require(!death.CreateSnapshot(0).Players[1].IsAlive&&!death.Events.Select(e=>e.Payload).OfType<EquipmentChangedEvent>().Any(),"Actual HP-loss dying/death finishes before the random equipment step and stops the dead beneficiary's use.");Replay(death,dr);
    }
    public static void LossDyingRescueContinuesRealEquipment()
    {
        var (g,r)=Create("rescue");Use(g,"drain",[0]);Reach(g,p=>p.Kind==DecisionKind.PlayCard);Use(g,"damage",[0]);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:qingxian");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Targets.Contains(0));Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="lose");Reach(g,p=>p.Kind==DecisionKind.RescueDying&&p.PlayerSeat==0);Replay(g,r);RejectUnknown(g);
        var peach=g.CreateSnapshot(0).Players[0].Hand.First(c=>c.Kind==CardKind.Peach);Answer(g,c=>c.Cards.Contains(peach.Id));Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Require(g.CreateSnapshot(0).Players[0].IsAlive&&g.Events.Select(e=>e.Payload).OfType<EquipmentChangedEvent>().Any(),"A true Peach rescue completes the dying child, then the already-started loss branch uses its true random deck equipment.");Replay(g,r);
    }
    public static void RegistryAndContracts()
    {
        foreach(var mode in new[]{"multi","multi-mixed"})
        {
            var (g,r)=Create(mode);var donor=g.CreateSnapshot(0,true).Players.First(p=>p.Seat!=0&&p.Role==Role.Rebel).Seat;Use(g,"death",[donor]);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
            var shield=g.Events.Select(e=>e.Payload).OfType<BeneficiarySuitShieldGrantedEvent>().Single().Shield;Require(shield.BeneficiarySeat!=0,"Fixed native beneficiary is another living seat: "+mode+" -> "+shield.BeneficiarySeat);
            if(mode=="multi")
            {
                var arrow=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.ArrowBarrage&&a.ConversionSource is null);var hp=g.CreateSnapshot(0).Players[shield.BeneficiarySeat].Hp;Play(g,arrow);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
                var use=g.Events.Select(e=>e.Payload).OfType<CardUseDeclaredEvent>().Last(e=>e.CardId==arrow.CardId);Require(!g.Events.Select(e=>e.Payload).OfType<TargetsConfirmedEvent>().Single(e=>e.ResolutionId==use.ResolutionId).TargetSeats.Contains(shield.BeneficiarySeat)&&g.CreateSnapshot(0).Players[shield.BeneficiarySeat].Hp==hp,"Actual native global use filters its frozen targets without rejecting other targets.");Replay(g,r);
                Require(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Duel&&a.TargetSeats.Contains(shield.BeneficiarySeat)),"Actual same-suit single-card conversion cannot designate the shielded beneficiary.");var duel=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Duel&&a.ConversionSource?.BindingId=="duel");Play(g,duel);Reach(g,p=>p.Kind==DecisionKind.PlayCard);Require(g.CardMovements.Count(m=>m.CardId==duel.CardId&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1,"Actual single-card ordinary trick conversion pays one real physical entity.");Replay(g,r);
            }
            var cards=g.CreateSnapshot(0).Players[0].Hand.Select(c=>c.Id).ToArray();var suits=g.CreateSnapshot(0).Players[0].Hand.Select(c=>c.Suit).Distinct().ToArray();Require(mode=="multi"?suits is [Suit.Club]:suits.Length>1,"Actual multi-entity costs have the intended common or mixed suit.");
            Accept(g,new UseProgramSkillCommand(0,"fixture:equipment-driver","all",cards,[],g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("view-as-id")=="all"));Replay(g,r);RejectUnknown(g);
            var option=Prompt(g)!.Choices.Single();Require(option.Targets.Contains(shield.BeneficiarySeat)==(mode=="multi-mixed"),"Same-suit conversion excludes the protected target; mixed physical suits have no suit and retain it.");Answer(g,c=>true);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
            var converted=g.Events.Select(e=>e.Payload).OfType<ProgramViewAsConvertedEvent>().Last();Require(converted.PhysicalCardIds.Order().SequenceEqual(cards.Order())&&converted.TargetSeats.Contains(shield.BeneficiarySeat)==(mode=="multi-mixed")&&cards.All(id=>g.CardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1),"Actual multi-card use freezes exact filtered targets and consumes each true entity exactly once.");Replay(g,r);
        }
        {
            var (g,r)=Create("effective");var donor=g.CreateSnapshot(0,true).Players.First(p=>p.Seat!=0&&p.Role==Role.Rebel).Seat;Use(g,"death",[donor]);Reach(g,p=>p.Kind==DecisionKind.PlayCard);var shield=g.Events.Select(e=>e.Payload).OfType<BeneficiarySuitShieldGrantedEvent>().Single().Shield;
            Require(shield.Suit==Suit.Heart&&shield.BeneficiarySeat!=0&&g.CreateSnapshot(0).Players[0].Hand.All(c=>c.Suit==Suit.Spade),"The fixture uses actual spade entities and a public heart shield.");Require(!g.GetHumanLegalActions().Any(a=>a.TargetSeats.Contains(shield.BeneficiarySeat)&&a.Kind is LegalActionKind.Slash or LegalActionKind.Duel),"Hongyan's effective heart suit forbids both native and single-card converted uses against the heart shield.");
            var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash);var before=State(g);var refused=g.Submit(new PlayCardCommand(0,action.CardId!.Value,[shield.BeneficiarySeat],g.Revision,Prompt(g)!.PromptId));Require(!refused.Accepted&&State(g)==before,"A protected target command rejects atomically before physical payment.");Play(g,action);Reach(g,p=>p.Kind==DecisionKind.PlayCard);Require(g.CardMovements.Any(m=>m.CardId==action.CardId&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing),"The effective-suit policy still permits actual use against an unprotected legal target.");Replay(g,r);
        }
        {
            var (g,r)=Create("multi-borrowed");var donor=g.CreateSnapshot(0,true).Players.First(p=>p.Seat!=0&&p.Role==Role.Rebel).Seat;Use(g,"death",[donor]);Reach(g,p=>p.Kind==DecisionKind.PlayCard);var shield=g.Events.Select(e=>e.Payload).OfType<BeneficiarySuitShieldGrantedEvent>().Single().Shield;var holder=g.CreateSnapshot(0).Players.First(p=>p.IsAlive&&p.Seat!=0&&p.Seat!=shield.BeneficiarySeat).Seat;
            foreach(var seat in new[]{holder,shield.BeneficiarySeat}){Use(g,"equip-target",[seat]);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-and-move-owned-card"));Answer(g,c=>true);Reach(g,p=>p.Kind==DecisionKind.PlayCard);}
            Require(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.BorrowedSword&&a.TargetSeats[0]==shield.BeneficiarySeat),"The protected weapon holder is a prohibited trick target.");var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.BorrowedSword&&a.TargetSeats[0]==holder&&a.TargetSeats[1]==shield.BeneficiarySeat);Play(g,action);Replay(g,r);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
            Require(g.Events.Select(e=>e.Payload).OfType<CardUseDeclaredEvent>().Any(e=>e.CardId==action.CardId&&e.CardKind==CardKind.BorrowedSword)&&g.CardMovements.Count(m=>m.CardId==action.CardId&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1,"Actual borrowed-sword conversion permits the protected slash victim, consumes one true cost, and settles its native AI response.");Replay(g,r);
        }
        {
            var (g,r)=Create("caishi");Use(g,"wound",[0]);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
            Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.PlayerSeat==0&&p.SkillPrompt?.SkillId=="classic:caishi");
            Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="recover");Reach(g,p=>p.Kind==DecisionKind.PlayCard);
            Require(Enumerable.Range(0,4).All(v=>g.CreateSnapshot(v).Players[0].SkillRuntimeStates!.Single(x=>x.SkillId=="classic:caishi").PublicRuleStates!.Any(x=>x.Kind==ProgramPublicRuleStateKind.SelfTargetProhibition&&x.Value==1))&&g.GetHumanLegalActions().All(a=>a.Kind!=LegalActionKind.Equip),"Actual Caishi recovery makes the recipient's self-use prohibition public and forbids native equipment use.");Replay(g,r);
            Require(g.CreateCardZoneDiagnostics().Any(c=>c.Location==CardLocation.DrawPile&&EquipmentCatalog.IsEquipment(c.CardKind)),"The actual deck contains true equipment entities, rather than an artificially empty equipment pool.");
            Use(g,"damage",[0]);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:qingxian");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Targets.Contains(0));Replay(g,r);RejectUnknown(g);
            var random=typeof(GameEngine).GetField("_random",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(g)!;
            var rng=random.GetType().GetProperty("State")!;var beforeRng=(uint)rng.GetValue(random)!;var beforeMoves=g.CardMovements.Count;var beforeEquipment=g.Events.Count(e=>e.Payload is EquipmentChangedEvent);var beforeHp=g.CreateSnapshot(0).Players[0].Hp;
            Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="lose");Reach(g,p=>p.Kind==DecisionKind.PlayCard);
            Require(g.CreateSnapshot(0).Players[0].Hp==beforeHp-1&&g.CardMovements.Count==beforeMoves&&g.Events.Count(e=>e.Payload is EquipmentChangedEvent)==beforeEquipment&&(uint)rng.GetValue(random)! == beforeRng,"The started loss branch loses actual HP, but Caishi removes all random equipment candidates before movement/RNG: no equipment use, true deck movement or fabricated Club draw reward.");Replay(g,r);
        }
        foreach(var effects in new[]{"[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"used\"}]","[{\"op\":\"grantRandomSkillAndSuitShield\",\"target\":\"selectedTarget\",\"skillIds\":[\"classic:jixian\"],\"suits\":[\"club\"]}]"})
        {
            var rejected=false;try{SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:invalid","revision":1,"activations":[{"id":"invalid","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","effects":{{effects}}}]}]}""","""{"schemaVersion":3,"skills":{"fixture:invalid":{"name":"invalid","description":"invalid"}}}""");}catch(InvalidOperationException){rejected=true;}Require(rejected,"Shared resource contracts reject missing selected target and death-only bequest outside its real death window.");
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
