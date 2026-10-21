using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenXunYuChecks
{
    private const string Owner="fixture:shen-xun-yu", Quiet="fixture:shen-xun-yu-quiet", Driver="fixture:shen-xun-yu-driver", Mode="identity:classic-shen-xun-yu-fixture";
    public static void TianzuoEightEntitiesDinghanAndLingceCold()
    {
        var (g,r)=Start(CardKind.DrawTwo);
        Reach(g,()=>g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.ShenXunYuEditing) && P(g)?.PlayerSeat==0);
        g=Cold(g,r); Answer(g,c=>c.Parameters.GetValueOrDefault("kind")==nameof(CardKind.Duel)); Play(g);
        Need(E<DinghanNameChangedEvent>(g).Any(e=>e.Owner==0 && e.Kind==CardKind.Duel && e.Added),"Dinghan's actual turn-start choice adds the selected trick name after cold restore.");
        var generated=E<TianzuoCardCreatedEvent>(g).ToArray();
        Need(generated.Length==8 && generated.Select(e=>e.CardId).Distinct().Count()==8 && generated.Select(e=>(e.Suit,e.Rank)).OrderBy(x=>x.Suit).ThenBy(x=>x.Rank).SequenceEqual(new[]{(Suit.Spade,2),(Suit.Spade,4),(Suit.Spade,6),(Suit.Spade,8),(Suit.Club,3),(Suit.Club,5),(Suit.Club,7),(Suit.Club,9)}.OrderBy(x=>x.Item1).ThenBy(x=>x.Item2)),"Tianzuo creates exactly the eight printed independent physical cards.");
        Need(generated.All(e=>g.CardMovements.Count(m=>m.CardId==e.CardId && m.From==CardLocation.OutsideGame && m.To==CardLocation.DrawPile)==1),"Each new card genuinely enters and is shuffled into the draw pile once.");
        DrawStock(g,3); var before=V(g,0).HandCount; var card=Hand(g).First(c=>c.Kind==CardKind.DrawTwo);
        UseCard(g,card.Id,0); Play(g);
        Need(E<DinghanTargetCanceledEvent>(g).Count(e=>e.Kind==CardKind.DrawTwo)==1 && V(g,0).HandCount==before,
            $"The first DrawTwo pays its entity, triggers real Lingce draw one, and Dinghan truly cancels its target (hand {before}->{V(g,0).HandCount}, deck {g.CreateSnapshot(0).DrawPileCount}).");
        g=Cold(g,r); before=V(g,0).HandCount; card=Hand(g).First(c=>c.Kind==CardKind.DrawTwo); UseCard(g,card.Id,0); Play(g);
        Need(V(g,0).HandCount==before+2 && E<DinghanTargetCanceledEvent>(g).Count(e=>e.Kind==CardKind.DrawTwo)==1,"A recorded name permits the next actual DrawTwo: Lingce one and native DrawTwo two, after its one-card payment.");
        g=Cold(g,r); Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
        Reach(g,()=>g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.ShenXunYuEditing) && P(g)?.PlayerSeat==0);
        g=Cold(g,r); Answer(g,c=>c.Parameters.GetValueOrDefault("kind")==nameof(CardKind.Duel)); Play(g);
        Need(E<DinghanNameChangedEvent>(g).Where(e=>e.Owner==0 && e.Kind==CardKind.Duel).Select(e=>e.Added).SequenceEqual([true,false]),"A later real turn-start removes exactly one previously recorded name.");
    }
    public static void QizhengWrongResponsePaysThenObtainsCold()
    {
        var(g,r)=Start(CardKind.Slash); Play(g); DrawStock(g); var card=Hand(g).First(c=>c.Kind==CardKind.QizhengXiangsheng); var targetBefore=V(g,1).HandCount; var hp=V(g,1).Hp;
        UseCard(g,card.Id,1); Reach(g,()=>Q(g)?.Stage==QizhengStage.Designation && P(g) is not null); g=Cold(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("value")=="regular");
        Reach(g,()=>Q(g)?.Stage==QizhengStage.Response && P(g)?.PlayerSeat==1);
        var prompt=g.CreateSnapshot(1).PendingDecision!;
        Need(!JsonSerializer.Serialize(prompt).Contains("regular",StringComparison.Ordinal) && !JsonSerializer.Serialize(prompt).Contains("ambush",StringComparison.Ordinal) && prompt.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="response"),"The target sees usable responses without the secret designation.");
        Need(g.CreateSnapshot(2).PendingDecision is null,"The other seats see no private designation or response materials.");
        g=Cold(g,r); Reach(g,()=>Q(g)?.Stage==QizhengStage.Gain && P(g)?.PlayerSeat==0);
        Need(E<QizhengResponsePaidEvent>(g).Last().Kind==CardKind.Slash && V(g,1).HandCount==targetBefore-1,"An incorrect Slash is still a real accepted response and pays the actual material before the obtain choice.");
        g=Cold(g,r); Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="hand"); Play(g);
        Need(V(g,1).Hp==hp && V(g,1).HandCount==targetBefore-2 && E<CardActionAcceptedEvent>(g).Any(e=>e.Action.Type==CardActionType.Response && e.Action.ActorSeat==1 && e.Action.EffectiveKind==CardKind.Slash),"The wrong response is not mistaken for success: regular troops obtain one additional real target card without damage.");
        Need(g.CardMovements.Any(m=>m.From==CardLocation.Hand(1) && m.To==CardLocation.Hand(0) && m.Reason.Value=="card.effect.qizheng.obtain"),"Qizheng's gain preserves its own native card identity and real movement reason."); _=Cold(g,r);
    }
    public static void QizhengAmbushUsesRealDamageAndReplay()
    {
        var(g,r)=Start(CardKind.QizhengXiangsheng); Play(g); var card=Hand(g).First(c=>c.Kind==CardKind.QizhengXiangsheng); var hp=V(g,1).Hp;
        UseCard(g,card.Id,1); Reach(g,()=>Q(g)?.Stage==QizhengStage.Designation && P(g) is not null); Answer(g,c=>c.Parameters.GetValueOrDefault("value")=="ambush");
        Reach(g,()=>Q(g)?.Stage==QizhengStage.Response && P(g)?.PlayerSeat==1); g=Cold(g,r); Play(g);
        Need(V(g,1).Hp==hp-1 && E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0 && e.TargetSeat==1 && e.Amount==1),"Without a Slash response, ambush uses the real native damage chain exactly once.");
        Need(g.CardMovements.Any(m=>m.CardId==card.Id && m.From==CardLocation.Processing && m.To==CardLocation.DiscardPile && m.Reason==CardMoveReasons.UseFinished),"The real Qizheng entity completes its use and reaches discard after damage.");
        var replay=GameReplay.Replay(g.CreateCheckpoint().Options,g.AcceptedCommands,r); Need(State(g)==State(replay),"Accepted-command replay reproduces generated cards, secret choice, real damage and exact movement history.");
    }
    private static QizhengReceipt? Q(GameEngine g)=>g.ResolutionStack.OfType<CardUseFrame>().LastOrDefault(f=>f.Qizheng is not null)?.Qizheng;
    private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
    private static IReadOnlyList<CardSnapshot> Hand(GameEngine g)=>V(g,0).Hand;
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(command);Need(result.Accepted,result.Error?.Message??"Native command rejected.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void DrawStock(GameEngine g,int rounds=6){for(var i=0;i<rounds;i++){Accept(g,new UseProgramSkillCommand(0,Driver,"stock",[],[],g.Revision,P(g)!.PromptId));Play(g);}}
    private static void UseCard(GameEngine g,int card,int target)
    {var action=g.GetHumanLegalActions().First(a=>a.CardId==card && (a.TargetSeats.Contains(target)||a.TargetSeat==target||a.Kind==LegalActionKind.DrawTwo));Accept(g,new PlayCardCommand(0,action.CardId!.Value,action.TargetSeats,g.Revision,P(g)!.PromptId,action.PlayedCardKind,action.TargetCardId) { ConversionSource=action.ConversionSource });}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g) is {PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<260;i++)
        {
            if(done())return;var p=P(g);
            if(p is {PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="select-god-faction")) Answer(g,c=>c.Parameters.GetValueOrDefault("faction-id")=="wei");
            else if(p is {PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="dinghan-edit"))Answer(g,c=>c.Parameters.GetValueOrDefault("kind")=="cancel");
            else if(p is {PlayerSeat:0,Kind:DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Shen Xun Yu expected native boundary not reached: "+JsonSerializer.Serialize(new{Prompt=P(g),Frames=g.ResolutionStack}));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Movements=g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold restore preserves original frames, private designation and exact real costs.");return restored;}
    private static (GameEngine,ContentRegistry) Start(CardKind kind)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(kind));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=6},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g) is {PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(CardKind kind):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-shen-xun-yu",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var rules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},{"id":"{{Driver}}","revision":1,"activations":[{"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]}]}]}""";
            var presentation=JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Quiet]=new{name="安静同桌",description="跳过出牌阶段。"},[Driver]=new{name="真实牌堆",description="通过原生摸牌获得固定牌堆。"}}});
            var c=SkillProgramCatalog.Load(rules,presentation);foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id]});
            b.AddGeneral(new(Owner,"神荀彧夹具","supporter","mobile:tianzuo","god",3,["mobile:lingce","mobile:dinghan",Driver],GeneralGender.Male));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:shen-xun-yu-peer-{i}").ToArray();foreach(var peer in peers)b.AddGeneral(new(peer,"安静同桌","supporter",Quiet,"wei",12));
            b.AddCard(new("fixture:shen-xun-yu-card",CardCatalog.Get(kind).DisplayName,CardCatalog.Get(kind).CategoryName,"真实牌堆实体",kind));
            b.AddDeck(new("fixture:shen-xun-yu-deck","固定真实牌堆",4,0,[]){PhysicalCards=Enumerable.Range(0,128).Select(_=>new ContentDeckPhysicalCard("fixture:shen-xun-yu-card",Suit.Club,7)).ToArray()});
            b.AddMode(new(Mode,"神荀彧原生夹具",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:shen-xun-yu-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
