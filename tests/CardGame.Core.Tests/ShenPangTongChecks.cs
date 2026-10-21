using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenPangTongChecks
{
    private const string Hero="anniversary:shen-pang-tong",Driver="fixture:shen-pt-driver",Quiet="fixture:shen-pt-quiet",Watch="fixture:shen-pt-watch",Mode="identity:classic-shen-pt-fixture";
    public static void NativeLuansuoAndFengliao()
    {
        var(g,r)=Start();
        var quick=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=false,AdvanceAfterHumanCommands=false},r);
        Accept(quick,new StartGameCommand());var quickHero=quick.CreateSnapshot(0).Players.Single(p=>p.GeneralId==Hero);
        Need(quickHero.MaxHp==1&&quickHero.Hp==1,"The same real general also initializes at one maximum and one HP through the constructor-assigned setup path.");
        Need(V(g).MaxHp==1&&V(g).Hp==1,"Kunyu fixes the actual Lord's initial maximum and HP at one.");
        Need(Enumerable.Range(0,4).All(s=>V(g,s).Hand.Count>0&&V(g,s).Hand.All(c=>c.IdentityKind==CardKind.IronChain)),"Before any discard, Luansuo gives every role's actual current hand the mandatory Iron Chain identity.");
        Skill(g,"draw");Play(g);
        var before=V(g,1).HandCount;var cutoff=g.CardMovements.Count;
        Skill(g,"discard-other",[1]);Play(g);
        Need(V(g,1).HandCount==before&&!g.CardMovements.Skip(cutoff).Any(m=>m.From==CardLocation.Hand(1)&&m.To==CardLocation.DiscardPile),"A real foreign hand-discard instruction cannot discard any hand card during Luansuo; its native no-payment branch leaves every real entity in hand.");
        UseChain(g,[1,2]);Play(g);
        Need(!E<ShenPangTongStartedEvent>(g).Any()&&V(g,1).IsChained&&V(g,2).IsChained,"One native use with two real designated targets does not trigger or switch Fengliao.");
        var suits=V(g).Hand.ToDictionary(c=>c.Id,c=>c.Suit);var thirdHand=V(g,3).HandCount;var first=UseChain(g,[3]);Play(g);
        Need(E<ShenPangTongDrawReturnedEvent>(g) is[{TargetSeat:3,ActualCount:1}]&&V(g,3).HandCount==thirdHand+1&&V(g,3).IsChained,"The first unique actual target receives one native draw before Iron Chain really chains it.");
        var suit=suits[first];
        Need(Enumerable.Range(0,4).All(s=>V(g,s).Hand.Where(c=>c.Suit==suit).All(c=>c.IdentityKind is null))&&V(g).Hand.Any(c=>c.Suit!=suit&&c.IdentityKind==CardKind.IronChain),"The used physical card really enters the discard pile; that suit stops being forced in every current hand while different suits remain Iron Chains.");
        var hp=Enumerable.Range(1,3).ToDictionary(s=>s,s=>V(g,s).Hp);UseChain(g,[1]);Play(g);
        Need(Enumerable.Range(1,3).All(s=>V(g,s).Hp==hp[s]-1)&&V(g,1).IsChained&&!V(g,2).IsChained&&!V(g,3).IsChained&&
            E<ChainedDamagePropagatedEvent>(g).Count(e=>e.SourceSeat==0&&e.Nature==DamageNature.Fire)==2&&
            E<ShenPangTongStartedEvent>(g).Where(e=>e.Operation==SkillProgramEffectOp.ResolveShenPangTongFengliao).Select(e=>e.Polarity).SequenceEqual([SkillPolarity.Yang,SkillPolarity.Yin]),"The second actual unique use takes Yin and its same native program attack really propagates through both other chained roles; after fire unchains them, the original Iron Chain chains its sole target again.");
        var count=E<ShenPangTongStartedEvent>(g).Length;
        var recast=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Recast&&a.ConversionSource?.SkillId=="anniversary:luansuo");
        Accept(g,new RecastCardCommand(0,recast.CardId!.Value,g.Revision,P(g)!.PromptId){ConversionSource=recast.ConversionSource});Play(g);
        Need(E<ShenPangTongStartedEvent>(g).Length==count&&E<CardRecastEvent>(g).Any(e=>e.CardId==recast.CardId),"A mandatory Iron Chain identity can really be recast; native recast remains a non-use and does not switch Fengliao.");
        var remaining=V(g).HandCount;Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,_=>false,()=>E<TurnEndedEvent>(g).Any(e=>e.ActorSeat==0));
        Need(V(g).HandCount==remaining&&V(g).Hand.All(c=>c.IdentityKind is null),"The actual discard phase owes no forbidden hand payment even above one HP; Luansuo expires when the real turn ends.");
    }
    public static void NativeKunyuDeathPaymentCold()
    {
        var(g,r)=Start();Skill(g,"grow");Play(g);Skill(g,"shrink");Play(g);
        Need(V(g).MaxHp==1&&V(g).Hp==1&&E<MaximumHpChangedEvent>(g).All(e=>e.PlayerSeat!=0||e.MaximumHp==1&&e.Delta==0),"Real maximum-HP gain and loss instructions both keep Kunyu at exactly one without a false health payment.");
        for(var ordinal=1;ordinal<=2;ordinal++)
        {
            Skill(g,"die");Reach(g,p=>p.SkillPrompt?.SkillId==Watch);
            var death=g.ResolutionStack.OfType<DeathFrame>().Single(d=>d.VictimSeat==0);
            var paid=death.ShenPangTongRevival;
            Need(!V(g).IsAlive&&V(g).HandCount==0&&death.ShenPangTongOutcomeDeferred&&!death.OwnerDiedProgramsResolved&&
                paid is{RemovedCardId:>0}&&g.CreateCardZoneDiagnostics().Single(c=>c.CardId==paid.RemovedCardId).Location==CardLocation.OutsideGame&&
                E<PlayerDiedEvent>(g).Count(e=>e.VictimSeat==0)==ordinal&&!E<WinnerDeterminedEvent>(g).Any(),"Kunyu pays after the actual Lord's real death and complete hand cleanup, while winner evaluation waits on the original paid removal child.");
            if(ordinal==1)g=Cold(g,r);
            Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");Play(g);
            Need(V(g).IsAlive&&V(g).MaxHp==1&&V(g).Hp==1&&E<ShenPangTongRemovalPaidEvent>(g).Length==ordinal&&E<ShenPangTongRevivedEvent>(g).Length==ordinal&&
                E<ShenPangTongRevivalGrantedEvent>(g).Length==ordinal&&!E<WinnerDeterminedEvent>(g).Any()&&g.ResolutionStack.All(x=>x is not DeathFrame),"The exact original payment returns once through the retired death window, revives the Lord at one HP, and resumes the native program without a premature loss or a duplicate removal.");
        }
        for(var n=0;n<8;n++){Skill(g,"draw");Play(g);}
        Need(!g.CreateCardZoneDiagnostics().Any(c=>c.Location==CardLocation.DrawPile&&c.CardKind is CardKind.FireSlash or CardKind.FireAttack),"The fixture empties the real remaining draw pile by an actual native draw.");
        Skill(g,"die");Reach(g,_=>false,()=>E<GameEndedEvent>(g).Length>0);
        Need(!V(g).IsAlive&&E<PlayerDiedEvent>(g).Count(e=>e.VictimSeat==0)==3&&E<ShenPangTongRevivedEvent>(g).Length==2&&E<ShenPangTongRemovalPaidEvent>(g).Length==2&&E<WinnerDeterminedEvent>(g).Length==1,"With no actual fire damage card in the draw pile, the locked death trigger cannot pay or revive and the same native death determines the winner once.");
    }
    private static PlayerSnapshot V(GameEngine g,int s=0)=>g.CreateSnapshot(s).Players.Single(p=>p.Seat==s);
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static void Skill(GameEngine g,string activation,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,activation,[],targets??[],g.Revision,P(g)!.PromptId));
    private static int UseChain(GameEngine g,int[] targets)
    {
        var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.IronChain&&a.ConversionSource?.SkillId=="anniversary:luansuo"&&a.TargetSeats.SequenceEqual(targets));
        Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});return a.CardId.Value;
    }
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Play(GameEngine g)=>Reach(g,p=>p is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<PendingDecision,bool> ready,Func<bool>? state=null)
    {
        for(var n=0;n<250;n++)
        {
            if(state?.Invoke()==true)return;
            if(P(g)is{}p)
            {
                if(ready(p))return;
                if(p.Kind==DecisionKind.SelectFaction){Answer(g,_=>true);continue;}
                if(p.Kind is DecisionKind.ProgramTrigger or DecisionKind.SelectTargetCard)
                {var c=p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")??p.Choices.First();Answer(g,x=>x.Id==c.Id);continue;}
                if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying or DecisionKind.Nullification){Answer(g,c=>c.Cards.Count==0);continue;}
            }
            Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Shen Pang Tong missed native boundary: "+JsonSerializer.Serialize(new{Prompt=P(g)?.Kind,Seat=P(g)?.PlayerSeat,Frames=g.ResolutionStack.Select(f=>new{f.Id,Type=f.GetType().Name})}));
    }
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected real Shen Pang Tong command.");}
    private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"The exact private views, original dead-owner window, paid removal child, physical zones and accepted commands cold-restore identically.");return restored;}
    private static (GameEngine,ContentRegistry) Start()
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture());var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,Hero,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-shen-pt",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b),Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"grow","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"changeMaximumHp","target":"owner","amount":2}]},
                {"id":"shrink","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"changeMaximumHp","target":"owner","amount":-1}]},
                {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"discard-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","skipIfNoCards":true}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"after-paid-rebirth","window":"ownerDied","subject":"owner","optional":true,"priority":0,"effects":[{"op":"chooseOption","target":"owner","resultBind":"watch","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Watch};var cat=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="Native Shen Pang Tong fixture.",optionLabels=id==Watch?new Dictionary<string,string>{{"continue","继续"}}:[]})}));
            foreach(var(id,p)in cat.Programs)b.AddSkill(new(id,id,"Native fixture"){Program=p,ProgramPresentation=cat.Presentations[id]});
            for(var n=1;n<4;n++)b.AddGeneral(new($"fixture:shen-pt-peer-{n}","Quiet participant","supporter",Quiet,"wei",8));
            var kinds=new[]{"standard:slash","standard:dodge","standard:fire_slash","standard:fire_attack","standard:iron_chain","standard:crossbow","standard:peach","standard:duel"};
            b.AddDeck(new("fixture:shen-pt-deck","Real mixed physical deck",4,2,[]){PhysicalCards=Enumerable.Range(0,96).Select(i=>new ContentDeckPhysicalCard(kinds[i%kinds.Length],(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new(Mode,"Native Shen Pang Tong",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:shen-pt-deck",GeneralCandidateCount:4,GeneralPoolIds:[Hero,"fixture:shen-pt-peer-1","fixture:shen-pt-peer-2","fixture:shen-pt-peer-3"]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b):IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d)=>b.AddGeneral(d.Id==Hero?d with{AdditionalSkillIds=[..d.AdditionalSkillIds!,Driver,Watch]}:d);
        public void AddSkill(ContentSkillDefinition d)=>b.AddSkill(d);public void AddCard(ContentCardDefinition d)=>b.AddCard(d);public void AddDeck(ContentDeckRecipe d)=>b.AddDeck(d);public void AddMode(ContentModeDefinition d)=>b.AddMode(d);
    }
}
