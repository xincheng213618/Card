using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2017XuShiChecks
{
    public static void SelfDeckEndsAndReplay()
    {
        foreach(var end in new[]{"top","bottom"})
        {
            var (g,r)=Start("exchange",true);var cards=Zones(g).Where(z=>z.Location==CardLocation.DrawPile).OrderBy(z=>z.ZoneIndex).ToArray();
            var a=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="classic:wengua"&&a.ProgramSkillOwnerSeat is null);
            var payment=a.SelectableCardIds.First();var hand=g.CreateSnapshot(0,true).Players[0].Hand.Count;
            Reject(g,new UseProgramSkillCommand(0,"classic:wengua",a.ProgramActivationId!,[payment,payment],[],g.Revision,P(g)!.PromptId));
            Use(g,a,payment);Require(P(g)!.IsPrivate && g.CreateSnapshot(1,false).PendingDecision is null,"Endpoint choice cannot expose private payment or deck faces.");
            Require(!g.CreateSnapshot(1,false).Players[0].Hand.Any(c=>c.Id==payment)&&!g.CreateSnapshot(1,false).PublicRevealedCards.Any(c=>c.Id==payment),"Unknown hand payment cannot leak into another observer or revealed pool.");
            Reject(g,new AnswerPromptCommand(0,P(g)!.PromptId,new("unpublished-deck-end"),g.Revision));Replay(g,r);
            Choose(g,c=>c.Parameters.GetValueOrDefault("end")==end);
            Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
            var draws=g.CardMovements.Where(m=>m.Reason.Value=="program.deck-end.draw").ToArray();
            Require(draws.Length==1 && draws[0].CardId==(end=="top"?cards[0].CardId:cards[^1].CardId)&&draws[0].To==CardLocation.Hand(0),"Self exchange draws exactly one real entity from the opposite endpoint.");
            Require(g.CardMovements.Count(m=>m.Reason.Value=="program.deck-end.gift")==0&&g.CreateSnapshot(0,true).Players[0].Hand.Count==hand,"Self exchange has no self gift and cannot draw twice.");
            Require(g.GetHumanLegalActions().All(a=>a.ProgramSkillId!="classic:wengua"),"Own phase entry is consumed once.");Replay(g,r);Conserve(g,64);
        }
    }
    public static void ForeignEquipmentGiftAndAiReplay()
    {
        var (g,r)=Start("exchange",false);
        var equipment=g.GetHumanLegalActions().FirstOrDefault(a=>a.Kind==LegalActionKind.Equip);
        if(equipment is null){Driver(g,"supply");Reach(g,p=>p.Kind==DecisionKind.PlayCard);equipment=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);}
        Accept(g,new PlayCardCommand(0,equipment.CardId!.Value,equipment.TargetSeats,g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        var a=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="classic:wengua");var owner=a.ProgramSkillOwnerSeat!.Value;
        Require(a.SelectableCardIds.Contains(equipment.CardId.Value),"Foreign entry must accept a real equipped entity.");
        Reject(g,new UseProgramSkillCommand(0,a.ProgramSkillId!,a.ProgramActivationId!,[equipment.CardId.Value],[0],g.Revision,P(g)!.PromptId){SkillOwnerSeat=owner});
        Use(g,a,equipment.CardId.Value);
        Require(g.CardMovements.Any(m=>m.CardId==equipment.CardId&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.Hand(owner)&&m.Reason.Value=="program.deck-end.gift"),"Real equipped card must enter recipient hand before its exchange choice.");Replay(g,r);
        Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
        var draws=g.CardMovements.Where(m=>m.Reason.Value=="program.deck-end.draw").ToArray();
        Require(draws.Length==2&&draws.Select(m=>m.To.OwnerSeat).SequenceEqual(new int?[]{owner,0}),"Foreign exchange waits and draws once for owner then provider.");
        Require(g.GetHumanLegalActions().All(a=>a.ProgramSkillId!="classic:wengua")&&!g.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat!=0),"Provider use is consumed and AI makes its real endpoint and gain decisions.");Replay(g,r);Conserve(g,64);
    }
    public static void GiftNestedRemovalAndRefusal()
    {
        foreach(var remove in new[]{false,true})
        {
            var (g,r)=Start(remove?"remove-gift":"exchange",true);
            Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
            Reach(g,p=>p.PlayerSeat==0 && (p.Choices.Any(c=>c.Parameters.ContainsKey("end")) || p.SkillPrompt?.SkillId=="fixture:gift-removal"));
            var gift=g.CardMovements.Last(m=>m.Reason.Value=="program.deck-end.gift");
            Require(gift.To==CardLocation.Hand(0),"AI really gives an owned entity before continuation.");Replay(g,r);
            if(remove)
            {
                Require(Zones(g).Single(z=>z.CardId==gift.CardId).Location==CardLocation.DiscardPile,"Nested gained observer removed the gifted entity through its real movement bind.");
                Choose(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
                Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
                Require(!g.CardMovements.Any(m=>m.CardId==gift.CardId&&m.Reason.Value=="program.deck-end.place"),"Moved gift cannot be placed by stale continuation.");
            }
            else
            {
                Choose(g,c=>c.Parameters.GetValueOrDefault("end")=="decline");
                for(var i=0;i<10&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.DeckEndExchange is not null);i++)Tick(g);
                Require(Zones(g).Single(z=>z.CardId==gift.CardId).Location==CardLocation.Hand(0)&&!g.CardMovements.Any(m=>m.Reason.Value=="program.deck-end.draw"),"Refusal retains gift and draws neither player.");
            }
            Replay(g,r);Conserve(g,64);
        }
    }
    public static void DeckSlashesResponsesCapAndShuffle()
    {
        foreach(var mode in new[]{"barrage","cap-dead"})
        {
        var(g,r)=Start(mode,false);
        if(mode=="cap-dead")
        {
            var sourceSeat=g.CreateSnapshot(0,true).Players.Single(p=>p.GeneralId=="fixture:deck-owner").Seat;
            var deadSeat=g.CreateSnapshot(0,true).Players.First(p=>p.Seat!=0&&p.Seat!=sourceSeat).Seat;
            Accept(g,new UseProgramSkillCommand(0,"fixture:deck-driver","kill",[],[deadSeat],g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
            Require(g.CreateSnapshot(0,true).Players.Count(p=>p.IsAlive)==3,"Cap fixture starts its real ending boundary with one eliminated seat.");
        }
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.PlayerSeat==0&&p.SkillPrompt?.SkillId=="fixture:target-pause");
        var parent=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.DeckSlashSequence is not null);
        var use=g.ResolutionStack.OfType<CardUseFrame>().Last();var source=parent.OwnerSeat;
        Require(use.SourceSeat==source&&use.TargetSeats.SequenceEqual([0])&&use.Action!.PhysicalCards.Single().From==CardLocation.DrawPile&&Zones(g).Single(z=>z.CardId==use.CardId).Location==CardLocation.Processing,"Barrage declares real deck card and a real target/use window under its exact parent.");Replay(g,r);
        Choose(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");Reach(g,p=>p.Kind==DecisionKind.RespondDodge&&p.PlayerSeat==0);
        var dodge=P(g)!.Choices.First(c=>c.Cards.Count==1);var dodgeId=dodge.Cards.Single();Replay(g,r);Choose(g,c=>c.Id==dodge.Id);
        for(var i=0;i<140&&!g.Events.Any(e=>e.Payload is DeckSlashSequenceShuffledEvent);i++)
        {
            if(P(g) is {Kind:DecisionKind.RespondDodge,PlayerSeat:0})Choose(g,c=>c.Cards.Count==0);else Tick(g);
            if(g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is {Continuation:ProgramCardContinuation.CompletedSlash})Replay(g,r);
        }
        var shuffled=g.Events.Select(e=>e.Payload).OfType<DeckSlashSequenceShuffledEvent>().Single();
        var declared=g.Events.Select(e=>e.Payload).OfType<CardUseDeclaredEvent>().Where(e=>e.SourceSeat==source&&e.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash).ToArray();
        Require(declared.Length==4&&shuffled.Uses==4&&declared.Select(e=>e.CardId).Distinct().Count()==4,"Initial four-player cap counts actual Slash declarations, never direct damage or replaying a used entity.");
        Require(declared.Select(e=>e.CardKind).Distinct().Count()==3,"All ordinary/fire/thunder physical Slash kinds must be actually declared: "+string.Join(",",declared.Select(e=>e.CardKind)));
        Require(g.CardMovements.Any(m=>m.CardId==dodgeId&&m.To==CardLocation.DiscardPile)&&g.Events.Any(e=>e.Payload is DamageAppliedEvent {TargetSeat:0}),"Native Dodge cost and later real damage both resolve.");
        Require(declared.All(e=>g.CardMovements.Any(m=>m.CardId==e.CardId&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile))&&g.CardMovements.Any(m=>m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle),"All used Slash entities finish and final wash really merges discard into draw.");
        Require(!g.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat!=0),"AI source activates Fuzhu and completes its gain/use listeners via native advancement.");Replay(g,r);Conserve(g,64);
        }
    }
    public static void DynamicDeckMembershipAndReplay()
    {
        foreach(var mode in new[]{"replenish","returned"})
        {
        var(g,r)=Start(mode,false);Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
        for(var i=0;i<150&&!g.Events.Any(e=>e.Payload is DeckSlashSequenceShuffledEvent);i++)
        {
            if(P(g) is {PlayerSeat:0,SkillPrompt.SkillId:"fixture:return-slash"} p && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards"))
            {var id=g.ResolutionStack.OfType<CardUseFrame>().Last().CardId;Choose(g,c=>c.Cards.SequenceEqual([id]));}
            else if(P(g) is {Kind:DecisionKind.RespondDodge,PlayerSeat:0})Choose(g,c=>c.Cards.Count==0);else Tick(g);
            if(g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is {Continuation:ProgramCardContinuation.CompletedSlash})Replay(g,r);
        }
        var wash=g.Events.Select(e=>e.Payload).OfType<DeckSlashSequenceShuffledEvent>().Single();
        var inserted=g.CardMovements.Where(m=>m.From==CardLocation.Hand(wash.OwnerSeat)&&m.To==CardLocation.DrawPile&&m.Reason.Value.Contains("MoveBoundCards",StringComparison.Ordinal)).ToArray();
        var uses=g.Events.Select(e=>e.Payload).OfType<CardUseDeclaredEvent>().Where(e=>e.SourceSeat==wash.OwnerSeat&&e.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash).ToArray();
        if(mode=="replenish")Require(inserted.Length>0&&uses.Length==4&&uses[1].CardId==inserted[0].CardId&&uses.Select(e=>e.CardId).Distinct().Count()==4,"After actual completed-use child moves a fresh Slash into deck top, next real declaration must discover it; declared IDs never repeat.");
        else Require(uses.Length==4&&uses.Select(e=>e.CardId).Distinct().Count()==4&&uses.All(u=>g.CardMovements.Any(m=>m.CardId==u.CardId&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.DrawPile)),"Already declared Slash returns through actual claim and top placement, completes legally outside Processing, and cannot be declared twice.");Replay(g,r);Conserve(g,64);
        }
    }
    public static void DeckSlashesGateExhaustionAndDeath()
    {
        foreach(var mode in new[]{"female","threshold","no-slash","source-death","source-suppressed","game-over"})
        {
            var(g,r)=Start(mode,false);var initialHp=g.CreateSnapshot(0,true).Players[0].Hp;Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
            for(var i=0;i<130&&!g.Events.Any(e=>e.Payload is TurnStartedEvent {ActorSeat:1}) && g.CreateSnapshot(0,true).Status!=EngineStatus.Completed;i++)
            {
                if(P(g) is {Kind:DecisionKind.RespondDodge,PlayerSeat:0})Choose(g,c=>c.Cards.Count==0);else Tick(g);
            }
            var wash=g.Events.Select(e=>e.Payload).OfType<DeckSlashSequenceShuffledEvent>().ToArray();
            if(mode is "female" or "threshold")Require(wash.Length==0&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.DeckSlashSequence is not null),"Female ending or excess deck count cannot offer Fuzhu.");
            else if(mode=="no-slash")Require(wash.Length==1&&wash[0].Uses==0,"An exhausted Slash membership still performs final real shuffle.");
            else if(mode=="source-death")Require(wash.Length==1&&wash[0].Uses==1&&!g.CreateSnapshot(0,true).Players[wash[0].OwnerSeat].IsAlive,"Source death during a real completed-use child cancels remaining declarations and washes exactly once.");
            else if(mode=="source-suppressed")Require(wash.Length==1&&wash[0].Uses==1&&g.CreateSnapshot(0,true).Players[wash[0].OwnerSeat].Hp==19&&g.CreateSnapshot(0,true).Players[wash[0].OwnerSeat].IsAlive,"Real HP-triggered passive skill suppression cancels subsequent Slash declarations while preserving source life.");
            else Require(wash.Length==1&&wash[0].Uses==initialHp&&!g.CreateSnapshot(0,true).Players[0].IsAlive&&g.CreateSnapshot(0,true).Status==EngineStatus.Completed&&g.ResolutionStack.Count==0,"Actual target death and game end cancel remaining deck uses, wash once and leave no live frame. mode="+mode+" wash="+string.Join(",",wash.Select(w=>w.Uses))+" hp="+g.CreateSnapshot(0,true).Players[0].Hp+" alive="+g.CreateSnapshot(0,true).Players[0].IsAlive+" status="+g.CreateSnapshot(0,true).Status+" frames="+string.Join(",",g.ResolutionStack.Select(f=>f.Kind+":"+f.Id)));
            Replay(g,r);Conserve(g,64);
        }
        SourceGrantLoss();
    }
    private static void SourceGrantLoss()
    {
        var(g,r)=Start("source-loss",true);var victim=g.CreateSnapshot(0,true).Players.Single(p=>p.GeneralId=="fixture:deck-provider").Seat;
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
        for(var i=0;i<160;i++)
        {
            if(P(g) is {PlayerSeat:0,Kind:DecisionKind.ProgramTrigger,SkillPrompt.SkillId:"classic:fuzhu"})
            {
                var ending=g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Last().OwnerSeat;
                if(ending==victim){Replay(g,r);Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");break;}
                Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            }
            else Tick(g);
        }
        for(var i=0;i<80&&!g.Events.Any(e=>e.Payload is DeckSlashSequenceShuffledEvent);i++)Tick(g);
        var wash=g.Events.Select(e=>e.Payload).OfType<DeckSlashSequenceShuffledEvent>().Single();
        Require(wash.OwnerSeat==0&&wash.Uses==1&&!g.CreateSnapshot(0,true).Players[victim].IsAlive&&
            g.Events.Any(e=>e.Payload is CharacterSkillsLostEvent lost&&lost.Seat==0&&lost.SourceSeat==victim&&lost.SkillIds.Contains("classic:fuzhu"))&&
            g.CreateSnapshot(0,true).Players[0].Skills is {Count:0},"Real target death runs Duanchang and removes the ongoing Slash source's exact grant; parent stops and washes once.");Replay(g,r);Conserve(g,64);
    }
    public static void ResourceContracts()
    {
        var asm=typeof(StandardContentPackage).Assembly;
        string Read(string suffix){using var s=asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n=>n.EndsWith(suffix,StringComparison.Ordinal)))!;using var reader=new StreamReader(s);return reader.ReadToEnd();}
        var rules=Read("classic-xu-shi.rules.json");var presentation=Read("classic-xu-shi.presentation.json");SkillProgramCatalog.Load(rules,presentation);
        foreach(var invalid in new[]{rules.Replace("\"minCards\": 1","\"minCards\": 0"),rules.Replace("\"usesPerPhase\": 1","\"usesPerPhase\": 2"),rules.Replace("\"usesPerTurn\": null","\"usesPerTurn\": 1"),rules.Replace("\"usesPerTurn\": null","\"usesPerGame\": 1"),rules.Replace("\"turnEnding\"","\"playEnding\"")})
        {var rejected=false;try{SkillProgramCatalog.Load(invalid,presentation);}catch(InvalidOperationException){rejected=true;}Require(rejected,"New operations reject ambiguous input bounds, per-phase scopes and wrong ending window.");}
    }
    private static(GameEngine,ContentRegistry)Start(string mode,bool self)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-deck-check",UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral&&p.PlayerSeat==0);
        Accept(g,new SelectGeneralCommand(0,self?"fixture:deck-owner":"fixture:deck-provider",g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);return(g,r);
    }
    private static IReadOnlyList<CardZoneDiagnostic>Zones(GameEngine g)=>g.CreateCardZoneDiagnostics();
    private static PendingDecision?P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(seat=>g.CreateSnapshot(seat,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Driver(GameEngine g,string id)=>Accept(g,new UseProgramSkillCommand(0,"fixture:deck-driver",id,[],[],g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,LegalAction a,int card)=>Accept(g,new UseProgramSkillCommand(0,a.ProgramSkillId!,a.ProgramActivationId!,[card],a.ProgramSkillOwnerSeat is null?[]:a.TargetSeats,g.Revision,P(g)!.PromptId){SkillOwnerSeat=a.ProgramSkillOwnerSeat});
    private static void Choose(GameEngine g,Func<PromptChoice,bool>match)=>Accept(g,new AnswerPromptCommand(P(g)!.PlayerSeat,P(g)!.PromptId,P(g)!.Choices.First(match).Id,g.Revision));
    private static void Tick(GameEngine g)
    {
        if(P(g) is {Kind:DecisionKind.RespondDodge,PlayerSeat:0})Choose(g,c=>c.Cards.Count==0);
        else if(P(g) is {Kind:DecisionKind.DiscardCards}d)Accept(g,new DiscardCardsCommand(d.PlayerSeat,d.ValidCardIds.Take(d.RequiredCardCount).ToArray(),d.PromptId,g.Revision));
        else if(P(g) is {Kind:DecisionKind.ProgramTrigger,PlayerSeat:0}p)Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate"||c.Parameters.GetValueOrDefault("option-id")=="continue"||c.Parameters.GetValueOrDefault("end")=="decline");
        else Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reach(GameEngine g,Func<PendingDecision,bool>match){for(var i=0;i<180;i++){if(P(g) is {}p&&match(p))return;Tick(g);}throw new InvalidOperationException("Missing deterministic boundary: "+P(g));}
    private static void Replay(GameEngine g,ContentRegistry r)
    {
        var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);
        Require(Enumerable.Range(0,4).All(seat=>SnapshotJson.Serialize(g.CreateSnapshot(seat,false))==SnapshotJson.Serialize(copy.CreateSnapshot(seat,false)))&&g.CardMovements.SequenceEqual(copy.CardMovements)&&g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).SequenceEqual(copy.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))),"All observer views, ordered entity movements, RNG-dependent draws and events must replay.");
    }
    private static void Reject(GameEngine g,GameCommand command){var before=GameCheckpointJson.Serialize(g.CreateCheckpoint());Require(!g.Submit(command).Accepted&&before==GameCheckpointJson.Serialize(g.CreateCheckpoint()),"Unpublished/duplicate input must reject atomically.");}
    private static void Accept(GameEngine g,GameCommand command){var round=CommandJson.Deserialize(CommandJson.Serialize([command])).Single();var result=g.Submit(round);Require(result.Accepted,"Fixture rejected: "+result.Error?.Message);}
    private static void Conserve(GameEngine g,int total)=>Require(Zones(g).Count==total&&Zones(g).Select(z=>z.CardId).Distinct().Count()==total,"All physical entities retain exactly one location.");
    private static void Require(bool c,string m){if(!c)throw new InvalidOperationException(m);}
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-xushi",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var driverActs=new List<object>{new{id="supply",minCards=0,maxCards=0,minTargets=0,maxTargets=0,targetKind="anyLiving",usesPerTurn=(int?)1,effects=new[]{new{op="draw",target="owner",amount=8}}}};
            if(mode=="cap-dead")driverActs.Add(new{id="kill",minCards=0,maxCards=0,minTargets=1,maxTargets=1,targetKind="otherLiving",usesPerTurn=1,effects=new[]{new{op="loseHp",target="selectedTarget",amount=20}}});
            var programs=new List<object>{new{id="fixture:deck-driver",revision=1,activations=driverActs}};
            programs.Add(new{id="fixture:gain-pause",revision=1,triggers=new[]{new{id="gain",window="cardsGained",subject="owner",destinationZones=new[]{"hand"},movementOccurrence="perBatch",movementReasons=new[]{"program.deck-end.draw"},optional=false,effects=new[]{new{op="chooseOption",target="owner",resultBind="gain",options=new[]{new{id="continue"}}}}}}});
            programs.Add(new{id="fixture:target-pause",revision=1,triggers=new[]{new{id="target",window="cardUseTargetsFinalized",ownerRelation="target",cardKinds=new[]{"slash","fireSlash","thunderSlash"},optional=false,effects=new[]{new{op="chooseOption",target="owner",resultBind="target",options=new[]{new{id="continue"}}}}}}});
            programs.Add(new{id="fixture:complete-pause",revision=1,triggers=new[]{new{id="complete",window="cardUseCompleted",ownerRelation="actor",cardKinds=new[]{"slash","fireSlash","thunderSlash"},optional=false,effects=mode=="replenish"?new object[]{new{op="selectOwnedCards",target="owner",amount=1,zones=new[]{"hand"},cardKinds=new[]{"slash"},resultBind="inserted"},new{op="moveBoundCards",target="owner",sourceBind="inserted",destination="drawPileTop",awaitMovementTriggers=true},new{op="chooseOption",target="owner",resultBind="complete",options=new[]{new{id="continue"}}}}:mode is "source-death" or "source-suppressed"?new object[]{new{op="loseHp",target="owner",amount=mode=="source-death"?20:1}}:new object[]{new{op="chooseOption",target="owner",resultBind="complete",options=new[]{new{id="continue"}}}}}}});
            programs.Add(new{id="fixture:gift-removal",revision=1,triggers=new[]{new{id="remove",window="cardsGained",subject="owner",destinationZones=new[]{"hand"},movementOccurrence="perBatch",movementReasons=new[]{"program.deck-end.gift"},optional=false,effects=new object[]{new{op="discardOwnedZoneCards",target="owner",zones=new[]{"hand"}},new{op="chooseOption",target="owner",resultBind="removed",options=new[]{new{id="continue"}}}}}}});
            programs.Add(new{id="fixture:return-slash",revision=1,triggers=new[]{new{id="return",window="afterDamageApplied",subject="owner",damageOccurrence="perDamage",optional=false,effects=new object[]{new{op="claimDamageCards",target="owner"},new{op="selectOwnedCards",target="owner",amount=1,zones=new[]{"hand"},cardKinds=new[]{"slash","fireSlash","thunderSlash"},resultBind="returned"},new{op="moveBoundCards",target="owner",sourceBind="returned",destination="drawPileTop",awaitMovementTriggers=true},new{op="chooseOption",target="owner",resultBind="return",options=new[]{new{id="continue"}}}}}}});
            programs.Add(new{id="fixture:empty-before-ending",revision=1,triggers=new[]{new{id="empty",window="playEnding",subject="owner",optional=false,effects=new[]{new{op="discardOwnedZoneCards",target="owner",zones=new[]{"hand"}}}}}});
            var presentation=new{schemaVersion=3,skills=programs.Select(p=>JsonSerializer.SerializeToElement(p).GetProperty("id").GetString()!).ToDictionary(id=>id,id=>(object)new{name=id,description="实际实体与暂停观察",optionLabels=id is "fixture:deck-driver" or "fixture:empty-before-ending" || id=="fixture:complete-pause" && mode is "source-death" or "source-suppressed" ? new Dictionary<string,string>() : new Dictionary<string,string>{{"continue","继续"}}})};
            var catalog=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=programs}),JsonSerializer.Serialize(presentation));
            foreach(var p in catalog.Programs)b.AddSkill(new(p.Key,p.Key,p.Key){Program=p.Value});
            b.AddSkill(new("fixture:hp-suppression","体力压制","在真实体力为19时压制其他武将技能"){SuppressionRule=new(19)});
            var exchange=mode is "exchange" or "remove-gift";
            b.AddGeneral(new("fixture:deck-owner","徐氏机制将","supporter",exchange?"classic:wengua":"classic:fuzhu","wu",mode=="threshold"?3:20,["fixture:gain-pause","fixture:complete-pause",..(mode=="source-suppressed"?new[]{"fixture:hp-suppression"}:Array.Empty<string>()),..(mode=="remove-gift"?new[]{"fixture:gift-removal"}:Array.Empty<string>())],GeneralGender.Female));
            b.AddGeneral(new("fixture:deck-provider","提供者","supporter","fixture:deck-driver","wei",mode is "game-over" or "source-loss"?1:20,["fixture:gain-pause","fixture:target-pause",..(mode=="returned"?new[]{"fixture:return-slash"}:Array.Empty<string>()),..(mode=="source-loss"?new[]{"classic:duanchang","fixture:empty-before-ending"}:Array.Empty<string>())],mode=="female"?GeneralGender.Female:GeneralGender.Male){InitialHp=mode is "game-over" or "source-loss"?1:null});
            for(var i=2;i<4;i++)b.AddGeneral(new($"fixture:deck-{i}","目标","supporter","standard:none","wei",20,[],GeneralGender.Male));
            b.AddDeck(new("fixture:deck","实际牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(exchange?i%4==0?"standard:qinggang_sword":"standard:dodge":mode=="no-slash"?"standard:dodge":i%6==0?"standard:slash":i%6==1?"standard:fire_slash":i%6==2?"standard:thunder_slash":"standard:dodge",(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-deck-check","牌堆机制",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:deck-owner","fixture:deck-provider","fixture:deck-2","fixture:deck-3"]));
        }
    }
}
