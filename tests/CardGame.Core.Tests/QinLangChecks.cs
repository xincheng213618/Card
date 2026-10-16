using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class QinLangChecks
{
    private const string Skill="ol:xianying",Owner="fixture:ql-owner",Driver="fixture:ql-driver",Quiet="fixture:ql-quiet",Watch="fixture:ql-watch",Commit="fixture:ql-commit",Health="fixture:ql-health",Grain="fixture:ql-grain",Source="fixture:ql-source",Foreign="fixture:ql-foreign",Mode="identity:classic-qin-lang-fixture";
    private const string DrawReason="skill-program.qin-lang.xianying.draw",DiscardReason="skill-program.qin-lang.xianying.discard";
    public static void DrawDiscardCountsRoundResetAndPreparationCold()
    {
        var(g,r)=Start();var hand=V(g).HandCount;Activate(g);DiscardBoundary(g);
        Need(V(g).HandCount==hand+2&&E<QinLangXianyingDrawnEvent>(g)is[{Count:2}],"Preparation really draws two before an arbitrary HE selection, including zero.");
        var p=P(g)!;var state=State(g);
        Need(p is{PlayerSeat:0,IsPrivate:true}&&g.CreateSnapshot(1).PendingDecision is null&&
            !g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==state,"Only the original chooser can see and answer its private payment, with rejection preserving all state.");
        g=Cold(g,r);Pay(g,0);Play(g);
        Need(E<QinLangXianyingDiscardPaidEvent>(g)is[{Count:0}]&&!E<QinLangXianyingGrantedEvent>(g).Any(),"Zero is a real once-per-round discarded count, and an empty set has no card name to imitate.");
        Use(g,"hurt-two");Activate(g);DiscardBoundary(g);
        Need(!P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="pay"),"The already used zero count is absent from the next real damage-instance choice.");
        Pay(g,1);Play(g);Use(g,"hurt");Activate(g);DiscardBoundary(g);var one=Current(g)!.Cards.First();Answer(g,c=>c.Cards.SequenceEqual([one.CardId]));
        Need(!P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="pay"),"One is also forbidden after it has actually been paid this round.");
        Answer(g,c=>c.Cards.SequenceEqual([one.CardId]));Pay(g,2);Play(g);
        Need(E<QinLangXianyingDiscardPaidEvent>(g).Select(e=>e.Count).SequenceEqual([0,1,2])&&E<QinLangXianyingStartedEvent>(g).Count(e=>!e.Ending)==3&&
            E<DamageAppliedEvent>(g).Any(e=>e.Amount==2),"A two-point damage event offers one draw-discard, rather than one per damage point; every actual cost count is unique.");
        End(g);Reach(g,()=>g.State.TurnNumber>1&&Optional(g));Activate(g);DiscardBoundary(g);
        Need(Current(g)!.RoundNumber>E<QinLangXianyingDiscardPaidEvent>(g)[0].RoundNumber&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="pay"),"The next actual round resets the count ledger and permits zero again.");
        g=Cold(g,r);Pay(g,0);Play(g);Need(E<QinLangXianyingDiscardPaidEvent>(g).Count(e=>e.Count==0)==2,"Cold restoration preserves two distinct round payments of zero.");_=Cold(g,r);
    }
    public static void IndependentForeignTurnEndZeroMaterialUsesCold()
    {
        var(g,r)=Start("foreign");Skip(g);Play(g);Use(g,"draw");Play(g);End(g);Activate(g);DiscardBoundary(g);Pay(g,1,CardKind.DrawTwo);
        Activate(g);DiscardBoundary(g);Pay(g,2,CardKind.Slash);Ending(g);var turn=g.State.TurnNumber;var grants=Current(g)!.Grants.ToArray();
        Need(turn>1&&grants.Length==2&&grants.All(e=>e.ActualTurn==turn&&e.TurnOwner!=0)&&E<TurnEndedEvent>(g).Any(e=>e.TurnNumber==turn),"Two separate actual foreign-turn damage instances retain independent promises for that foreign actual turn's real end. "+JsonSerializer.Serialize(new{turn,grants,damage=E<DamageAppliedEvent>(g)}));
        var before=V(g).HandCount;UseGrant(g,CardKind.DrawTwo);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Commit);var use=g.ResolutionStack.OfType<CardUseFrame>().Last();
        Need(use is{CardId:0,CardKind:CardKind.DrawTwo,PhysicalCardIds.Count:0,Action:{PhysicalCards.Count:0,EffectiveSuit:Suit.None,EffectiveRank:0}}&&use.TargetSeats.SequenceEqual([0]),"The selected saved name produces an actual colorless zero-entity native DrawTwo.");
        var first=use.Id;g=Cold(g,r);Continue(g);Ending(g);
        Need(V(g).HandCount==before+2&&E<QinLangXianyingUseReturnedEvent>(g).Count(e=>e.Origin.CardUseFrameId==first)==1,"The real trick draws two, completes its ordinary native lifecycle, then returns once to the remaining promise.");
        UseGrant(g,CardKind.Slash,1);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Commit);var slash=g.ResolutionStack.OfType<CardUseFrame>().Last();var action=slash.Action!.ActionId;g=Cold(g,r);Continue(g);
        Reach(g,()=>E<QinLangXianyingUseReturnedEvent>(g).Length==2);
        Need(E<QinLangXianyingGrantConsumedEvent>(g).Count(e=>e.UseIssued)==2&&E<QinLangXianyingUseIssuedEvent>(g).Select(e=>e.Origin.GrantFrameId).Order().SequenceEqual(grants.Select(e=>e.FrameId).Order())&&
            (E<CardActionAcceptedEvent>(g).Any(e=>e.Action.ParentActionId==action&&e.Action.EffectiveKind==CardKind.Dodge)||
            E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==1)),"Both independent saved cards really return through native response or damage, without consuming a physical Slash.");
        Need(E<TurnEndedEvent>(g).Count(e=>e.TurnNumber==turn)==1&&E<TurnStartedEvent>(g).All(e=>e.TurnNumber<=turn),"No duplicate whole turn-end or next actual turn starts before the original ending debt finishes.");_=Cold(g,r);
    }
    public static void NativeTargetsQuotaResponsesAndImitationDomainCold()
    {
        foreach(var scenario in new[]{"slash-response","duel","alcohol","peach","equipment","delayed","five-grains"})
        {
            var(g,r)=Start(scenario);Skip(g);Play(g);Use(g,"draw");Play(g);
            int? duelTarget=null;
            if(scenario=="duel")
            {duelTarget=Enumerable.Range(1,3).First(s=>V(g,s).Hand.Any(c=>c.Kind==CardKind.Slash));Use(g,"wound-peer",[duelTarget.Value]);Play(g);}
            if(scenario=="slash-response"){Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1])));Play(g);}
            var beforeHp=V(g).Hp;Use(g,"hurt");Activate(g);DiscardBoundary(g);
            var kind=scenario switch{"slash-response"=>CardKind.FireSlash,"duel"=>CardKind.Duel,"alcohol"=>CardKind.Alcohol,"peach"=>CardKind.Peach,"equipment"=>CardKind.Crossbow,"five-grains"=>CardKind.FiveGrains,_=>CardKind.Indulgence};
            Pay(g,1,kind);Play(g);End(g);
            if(scenario is"equipment"or"delayed")
            {Reach(g,()=>g.State.TurnNumber>1);Need(E<QinLangXianyingDiscardPaidEvent>(g).Length==1&&!E<QinLangXianyingGrantedEvent>(g).Any()&&!E<QinLangXianyingUseIssuedEvent>(g).Any(),"A real equipped-card or delayed-trick name is discarded but cannot be converted into an illegal end-of-turn use.");_=Cold(g,r);continue;}
            Ending(g);
            var saved=scenario=="slash-response"?CardKind.Slash:kind;
            if(scenario=="slash-response")Need(P(g)!.Choices.Where(c=>c.Parameters.GetValueOrDefault("branch")=="use").All(c=>!c.Targets.Contains(2)),"Exhausted normal quota does not remove the saved Slash, while its actual distance-two target remains illegal.");
            int? target=scenario=="slash-response"?1:duelTarget;
            UseGrant(g,saved,target);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Commit);var native=g.ResolutionStack.OfType<CardUseFrame>().Last();var actionId=native.Action!.ActionId;
            Need(native.CardId==0&&native.PhysicalCardIds is{Count:0}&&native.CardKind==saved,"Every selected saved basic or ordinary trick uses its genuine native zero-material card family.");
            if(scenario=="duel")Need(V(g,target!.Value).Skills!.Any(s=>s.ContentId=="fixture:ql-black-trick"),"The real target has native black-trick prohibition, while this genuine colorless Duel remains a legal declared use.");
            g=Cold(g,r);Continue(g);Reach(g,()=>E<QinLangXianyingUseReturnedEvent>(g).Any(e=>e.Origin.CardUseFrameId==native.Id));
            if(scenario=="slash-response")Need(E<CardUseDebitRecordedEvent>(g).Length==1&&E<CardActionAcceptedEvent>(g).Any(e=>e.Action.ParentActionId==actionId&&e.Action.EffectiveKind==CardKind.Dodge&&e.Action.PhysicalCards.Count>0),"The ordinary physical Slash debits once; the saved Slash normalizes elemental name and really accepts an entity Dodge without another debit.");
            if(scenario=="duel")Need(E<DuelResponseEvent>(g).Any(e=>e.ResolutionId==native.Id&&e.ResponderSeat==target&&e.UsedSlash&&e.SlashCardId>0)&&E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==target&&e.TargetSeat==0),"The real wounded AI chooses its physical Slash, then the original user declines and actually suffers reverse-source Duel damage. "+JsonSerializer.Serialize(new{target,responses=E<DuelResponseEvent>(g)}));
            if(scenario=="peach")Need(V(g).Hp==beforeHp&&E<RecoveryAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==0),"The saved Peach performs native recovery after actual damage.");
            if(scenario=="alcohol")
            {Reach(g,()=>g.State.TurnNumber>1);Need(E<AlcoholAppliedEvent>(g).Any(e=>e.ResolutionId==native.Id)&&E<AlcoholExpiredEvent>(g).Any(e=>e.PlayerSeat==0)&&!V(g).HasAlcoholEffect,"A true after-TurnEnded Alcohol effect expires at the same actual ending tail, before the next actual turn.");}
            if(scenario=="five-grains")Need(native.TargetSeats.SequenceEqual([0,1,2,3])&&E<CardsRevealedEvent>(g).Single(e=>e.ResolutionId==native.Id).Cards.Count==1&&E<HarvestCardSelectedEvent>(g).Count(e=>e.ResolutionId==native.Id)==1,"A genuinely exhausted pile retains all four declared native FiveGrains targets, reveals and picks only the one real discarded entity, and returns without inventing the other three cards.");
            _=Cold(g,r);
        }
        {
            var(g,r)=Start("fuman");Skip(g);Play(g);End(g);
            Reach(g,()=>E<GiftedSlashHandPolicyGrantedEvent>(g).Any(e=>e.Policy.RecipientSeat==0));
            var gift=E<GiftedSlashHandPolicyGrantedEvent>(g).First(e=>e.Policy.RecipientSeat==0).Policy;
            Reach(g,()=>g.State.TurnNumber>1&&Optional(g));Activate(g);DiscardBoundary(g);
            var card=Current(g)!.Cards.Single(c=>c.CardId==gift.CardId);
            Need(card.Kind==CardKind.Peach&&card.EffectiveName==CardKind.Slash&&V(g).Hand.Any(c=>c.Id==gift.CardId&&c.Kind==CardKind.Peach),"Real OL Fuman keeps the printed Peach entity in Hand while its continuous identity is actually Slash before Xianying pays it.");
            g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([gift.CardId]));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="pay");Play(g);
            Need(E<QinLangXianyingEntityDiscardedEvent>(g).Single(e=>e.CardId==gift.CardId)is{Kind:CardKind.Peach,Name:CardKind.Slash}&&E<QinLangXianyingGrantedEvent>(g).Last().Name==CardKind.Slash,
                "The real printed Peach departure proves its original entity, while the frozen last known effective name grants Slash rather than Peach after the hand identity expires.");
            End(g);Ending(g);UseGrant(g,CardKind.Slash,1);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Commit);g=Cold(g,r);Continue(g);Reach(g,()=>E<QinLangXianyingUseReturnedEvent>(g).Any());_=Cold(g,r);
        }
    }
    public static void PaidDrawDiscardLossDeathLionOxAndEndReturnCold()
    {
        foreach(var scenario in new[]{"loss","death","lion","ox"})
        {
            var(g,r)=Start(scenario);
            if(scenario is"loss"or"death")
            {
                Activate(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);var f=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.QinLangXianying is not null);g=Cold(g,r);Continue(g);
                if(scenario=="death")
                {Reach(g,()=>!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(x=>x.Id==f.Id));Need(!V(g).IsAlive&&g.State.Winner==Winner.None&&E<QinLangXianyingDrawnEvent>(g).Length==1&&!E<QinLangXianyingDiscardPaidEvent>(g).Any(),"Actual paid draw children may kill the non-Lord actor; the original draw is retained once and no dead payer discards or imitates.");_=Cold(g,r);continue;}
                DiscardBoundary(g);Need(V(g).Skills!.All(s=>s.ContentId!=Skill),"The real draw-child HP loss has removed the conditional source before the payment choice.");
                g=Cold(g,r);Pay(g,1,CardKind.Slash);Play(g);End(g);Ending(g);UseGrant(g,CardKind.Slash,1);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Commit);g=Cold(g,r);Continue(g);Reach(g,()=>E<QinLangXianyingUseReturnedEvent>(g).Length==1);
                Need(E<QinLangXianyingDrawnEvent>(g).Length==1&&E<QinLangXianyingDiscardPaidEvent>(g).Length==1&&E<QinLangXianyingGrantConsumedEvent>(g).Count(e=>e.UseIssued)==1,"The precise already-paid generation survives real skill loss, pays once, and retains its actual-turn-end native use.");_=Cold(g,r);continue;
            }
            Skip(g);Play(g);Use(g,"draw");Play(g);var kind=scenario=="lion"?CardKind.SilverLion:CardKind.WoodenOx;var equip=V(g).Hand.First(c=>c.Kind==kind).Id;
            Use(g,"place",[0],[equip]);Play(g);int? grain=null;
            if(scenario=="ox"){grain=V(g).Hand.First(c=>c.Id!=equip).Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain.Value],[],g.Revision,P(g)!.PromptId));Play(g);}
            Use(g,"hurt");Activate(g);DiscardBoundary(g);Answer(g,c=>c.Cards.SequenceEqual([equip]));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="pay");Reach(g,()=>P(g)?.SkillPrompt?.SkillId==(scenario=="lion"?Health:Grain));
            Need(E<QinLangXianyingDiscardPaidEvent>(g).Length==1&&g.CardMovements.Count(m=>m.CardId==equip&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.DiscardPile&&m.Reason.Value==DiscardReason)==1,"A real equipped entity is atomically discarded once before its native removal descendants.");
            g=Cold(g,r);Continue(g);Play(g);
            if(scenario=="lion")Need(V(g).Hp==V(g).MaxHp&&E<SilverLionRemovedRecoveryEvent>(g).Any(e=>e.Reason.Value==DiscardReason),"The actual discarded SilverLion heals through its native recovery child exactly once.");
            else Need(g.CardMovements.Count(m=>m.CardId==grain&&m.From==CardLocation.WoodenOxGrain(0)&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard)==1&&V(g).WoodenOxGrainCount==0&&E<DamageAppliedEvent>(g).Count(e=>e.SourceSeat==0&&e.TargetSeat==0)==2,"Ox grain really spills once; its nested actual damage child returns to the original paid receipt without repeating the equipment discard.");
            Need(E<QinLangXianyingDiscardPaidEvent>(g).Length==1&&!E<QinLangXianyingGrantedEvent>(g).Any(),"Equipment removal children do not change the discarded card's actual name into an imitation or cause another payment.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static QinLangXianyingReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.QinLangXianying is not null)?.QinLangXianying;
    private static bool Optional(GameEngine g)=>P(g)?.SkillPrompt?.SkillId==Skill&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static string State(GameEngine g)=>JsonSerializer.Serialize(g.ResolutionStack)+JsonSerializer.Serialize(g.CreateSnapshot(0))+g.Events.Count;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected actual Xianying command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Need(V(g,p.PlayerSeat).IsHuman,"Only the actual human chooser answers its prompt.");var c=p.Choices.FirstOrDefault(pick);Need(c is not null,"Missing exact Xianying option: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Activate(GameEngine g){Reach(g,()=>Optional(g));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");}
    private static void Skip(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
    private static void Continue(GameEngine g){if(V(g,P(g)!.PlayerSeat).IsHuman)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void DiscardBoundary(GameEngine g)=>Reach(g,()=>Current(g)?.Stage==QinLangXianyingStage.DiscardChoice);
    private static void Ending(GameEngine g)=>Reach(g,()=>Current(g)?.Stage==QinLangXianyingStage.EndChoice);
    private static void Pay(GameEngine g,int count,CardKind? kind=null)
    {var cards=Current(g)!.Cards.Where(c=>kind is null||c.Kind==kind).Take(count).ToArray();Need(cards.Length==count,"The small real fixture must supply the requested original HE cost: "+kind);foreach(var c in cards)Answer(g,p=>p.Cards.SequenceEqual([c.CardId]));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="pay");}
    private static void UseGrant(GameEngine g,CardKind kind,int? target=null)
    {var id=Current(g)!.Grants.First(grant=>grant.Name==kind&&!E<QinLangXianyingGrantConsumedEvent>(g).Any(e=>e.GrantFrameId==grant.FrameId)).FrameId.ToString();Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="use"&&c.Parameters.GetValueOrDefault("grant-id")==id&&(target is null||c.Targets.SequenceEqual([target.Value])));}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,string id,int[]? targets=null,int[]? cards=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});
    private static string Diagnostic(GameEngine g)=>$"turn={g.State.TurnNumber};winner={g.State.Winner};prompt={P(g)?.Kind}/{P(g)?.PlayerSeat}/{P(g)?.SkillPrompt?.SkillId};stage={Current(g)?.Stage};paid={E<QinLangXianyingDiscardPaidEvent>(g).Length};grants={E<QinLangXianyingGrantedEvent>(g).Length};uses={E<QinLangXianyingUseIssuedEvent>(g).Length}";
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var step=0;step<200;step++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Missing actual Xianying boundary: "+Diagnostic(g));var p=P(g);
            if(p is not null&&!V(g,p.PlayerSeat).IsHuman)Accept(g,new AdvanceOneStepCommand(g.Revision));
            else if(p is{Kind:DecisionKind.PlayCard})End(g);
            else if(Optional(g))Skip(g);
            else if(Current(g)?.Stage==QinLangXianyingStage.EndChoice)Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="finish");
            else if(p is{Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Cards.Count==0);
            else if(p is{Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p is{Kind:DecisionKind.SelectHarvestCard})Answer(g,_=>true);
            else if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")==true)Continue(g);
            else if(p is not null)throw new InvalidOperationException("Unexpected actual Xianying prompt: "+Diagnostic(g));
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing bounded Xianying boundary: "+Diagnostic(g));
    }
    private static GameEngine Cold(GameEngine g,ContentRegistry r)
    {var next=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(next),"Cold command replay preserves original hidden choices, counts and native child returns exactly.");return next;}
    private static(GameEngine,ContentRegistry)Start(string scenario="plain")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario=="death"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Reach(g,()=>Optional(g));return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-qin-lang",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:qin-lang");
            if(scenario=="fuman")CharacterFixtureContent.Register(b,"ol:ma-zhong");
            var rules=JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
              {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
              {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
              {"id":"hurt-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]},
              {"id":"wound-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{{Commit}}}","revision":1,"triggers":[{"id":"actual-use","window":"cardUseCommitted","ownerRelation":"actor","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native","options":[{"id":"continue"}]}]}]},
             {"id":"{{{Health}}}","revision":1,"triggers":[{"id":"real-recovery","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var labels=new Dictionary<string,object>{[Driver]=new{name="真实准备",description="原生小夹具"},[Quiet]=new{name="真实其他阶段",description="跳过出牌"},[Commit]=new{name="真正原生使用",description="实际用牌子窗",optionLabels=new Dictionary<string,string>{["continue"]="继续"}},[Health]=new{name="真实回复",description="原生HP子窗",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}};
            void Add(string id,string body,bool choice=false){rules["skills"]!.AsArray().Add(JsonNode.Parse("{\"id\":\""+id+"\",\"revision\":1,"+body+"}"));labels[id]=choice?new{name=id,description="原生后继",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}:new{name=id,description="真实规则"};}
            var skills=new List<string>{Driver,Commit,Health};
            if(scenario=="loss"){Add(Source,"\"hpSkillGrants\":[{\"id\":\"full\",\"skillId\":\"ol:xianying\",\"minimumHp\":9}]");skills.Add(Source);}else skills.Add(Skill);
            if(scenario is"loss"or"death")
            {
                var tail=scenario=="loss"?"{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}":"{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}";
                Add(Watch,"\"triggers\":[{\"id\":\"real-paid-draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\""+DrawReason+"\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"paid\",\"options\":[{\"id\":\"continue\"}]},"+tail+"]}]",true);skills.Add(Watch);
            }
            if(scenario=="ox")
            {Add(Grain,"\"triggers\":[{\"id\":\"actual-grain\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"grain\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]}]",true);skills.Add(Grain);}
            if(scenario=="foreign")Add(Foreign,"\"triggers\":[{\"id\":\"real-foreign-hurts\",\"window\":\"turnEnding\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}]}]");
            if(scenario=="duel")Add("fixture:ql-black-trick","\"cardPolicies\":[{\"id\":\"black-trick-targets\",\"kind\":\"prohibitBlackTrickTarget\"}]");
            var catalog=SkillProgramCatalog.Load(rules.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,program)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=program,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:ql-pick-owner","本人","固定选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-100000000d)});b.AddGeneral(new(Owner,"真实秦朗","ol-qin-lang","fixture:ql-pick-owner","wei",8,skills));
            var peers=Enumerable.Range(0,3).Select(i=>"fixture:ql-peer-"+i).ToArray();
            foreach(var(peer,i)in peers.Select((peer,i)=>(peer,i)))
            {var pick="fixture:ql-pick-"+i;b.AddSkill(new(pick,"其他角色","固定公开权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>1000000d*(3-i))});b.AddGeneral(new(peer,"真实其他角色","supporter",pick,"qun",12,scenario=="foreign"&&i==0?[Quiet,Foreign]:scenario=="fuman"&&i==0?["ol:fuman"]:scenario=="duel"?[Quiet,"fixture:ql-black-trick"]:[Quiet]));}
            foreach(var(id,kind)in new[]{("duel",CardKind.Duel),("alcohol",CardKind.Alcohol),("crossbow",CardKind.Crossbow),("indulgence",CardKind.Indulgence),("lion",CardKind.SilverLion),("ox",CardKind.WoodenOx),("fire",CardKind.FireSlash),("grains",CardKind.FiveGrains)})b.AddCard(new("fixture:ql-"+id,id,"真实牌","原生实体",kind));
            var ids=scenario switch{"foreign"=>new[]{"standard:slash","standard:draw_two"},"slash-response"=>["standard:slash","fixture:ql-fire","standard:dodge"],"duel"=>["fixture:ql-duel","standard:slash"],"alcohol"=>["fixture:ql-alcohol"],"peach"or"fuman"=>["standard:peach"],"equipment"=>["fixture:ql-crossbow"],"delayed"=>["fixture:ql-indulgence"],"five-grains"=>["fixture:ql-grains"],"lion"or"ox"=>["standard:slash","fixture:ql-lion","fixture:ql-ox"],_=>["standard:slash"]};
            b.AddDeck(new("fixture:ql-deck","固定真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scenario=="five-grains"?20:160).Select(i=>new ContentDeckPhysicalCard(ids[i%ids.Length],new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond}[i/ids.Length%4],i%13+1)).ToArray()});
            b.AddMode(new(Mode,"实际贤膺命令",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[scenario is"foreign"or"duel"?nameof(Role.Rebel):nameof(Role.Renegade)]=3},"fixture:ql-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
