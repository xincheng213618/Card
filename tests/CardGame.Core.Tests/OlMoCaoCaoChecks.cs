using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class OlMoCaoCaoChecks
{
    private const string Hero="ol:mo-cao-cao", Driver="fixture:mcc-driver", Quiet="fixture:mcc-quiet", Ally="fixture:mcc-ally", EnemyA="fixture:mcc-enemy-a", EnemyB="fixture:mcc-enemy-b", Deck="fixture:mcc-deck";
    public static void NativeEquipmentAndBachao()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var setup=new ShanheBattleSetup(7,1,"magic-cao-cao",Hero,4,0,0,0,0,[Driver],
            [new(CardKind.Snatch,Suit.Spade,3),new(CardKind.Snatch,Suit.Club,4),new(CardKind.Slash,Suit.Club,7),new(CardKind.Slash,Suit.Spade,8),new(CardKind.Slash,Suit.Club,9),
             new(CardKind.Alcohol,Suit.Spade,9),new(CardKind.Alcohol,Suit.Club,10),new(CardKind.DrawTwo,Suit.Club,1),new(CardKind.Crossbow,Suit.Club,2),new(CardKind.BaiBiDao,Suit.Diamond,13),new(CardKind.Dodge,Suit.Club,3)],[],
            [new(EnemyA,Hp:8){StartingHand=[new(CardKind.Alcohol,Suit.Spade,3)]},new(EnemyB,Hp:8){StartingHand=[new(CardKind.Peach,Suit.Heart,4)]}],
            [new(Ally,Hp:8){StartingHand=[new(CardKind.Crossbow,Suit.Club,5)]}],new string('a',64));
        var g=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,
            ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=4},registry);
        PendingDecision? P()=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
        T[] E<T>()=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        string Diagnostic()=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.Phase,Prompt=P(),Owner=g.CreateSnapshot(0).Players[0],AttackRange=g.GetAttackRange(0),
            Players=g.CreateSnapshot(0).Players.Select(p=>new{p.Seat,p.Hp,p.MaxHp,p.IsAlive,p.HandCount,p.Equipment}),
            Actions=P()?.Kind==DecisionKind.PlayCard ? g.GetHumanLegalActions().Select(a=>new{a.Kind,a.CardId,a.TargetSeats,a.ConversionSource}).ToArray() : null,
            Debits=E<CardUseDebitRecordedEvent>(),Damage=E<DamageAppliedEvent>(),Issued=E<MoCaoCaoEquipmentIssuedEvent>(),Uses=E<CardUseDeclaredEvent>(),
            Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=g.Events.TakeLast(24).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why+Diagnostic());}
        void Accept(GameCommand c){var r=g.Submit(c);Need(r.Accepted,r.Error?.Message??"Native command rejected.");}
        void Answer(Func<PromptChoice,bool> choose){var p=P()!;Need(p.PlayerSeat==0,"Only actual Human input is submitted.");var c=p.Choices.FirstOrDefault(choose);Need(c is not null,"Published choice missing.");Accept(new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
        void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<200;i++)
            {
                if(done())return;Need(g.State.Winner==Winner.None,"Native game ended at "+line);
                if(P() is not {PlayerSeat:0} p){Accept(new AdvanceOneStepCommand(g.Revision));continue;}
                if(p.Kind==DecisionKind.SelectTargetCard){Answer(c=>true);continue;}
                if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification){Answer(c=>c.Cards.Count==0 && c.Parameters.GetValueOrDefault("response") is not "bagua");continue;}
                throw new InvalidOperationException("Unexpected Human input at "+line+Diagnostic());
            }
            throw new InvalidOperationException("Bounded Magic Cao Cao native flow not reached at "+line+Diagnostic());
        }
        void Play()=>Reach(()=>P() is {PlayerSeat:0,Kind:DecisionKind.PlayCard});
        void Use(LegalAction a)=>Accept(new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P()!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
        void Drive(string id,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>Accept(new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P()!.PromptId));
        Accept(new StartGameCommand());Reach(()=>P()?.SkillPrompt?.SkillId=="ol:mo-bachao");
        var initial=E<MoCaoCaoEquipmentIssuedEvent>().Single();
        Need(initial.Hp==4 && EquipmentCatalog.Get(initial.Weapon).WeaponAttackRange==4 && g.GetAttackRange(0)==4 && initial.Armor==CardKind.SilverLion &&
            g.CreateSnapshot(0).Players[0].Equipment.Count==0,"GameStart genuinely issues the range4 weapon and four-character armor without adding physical entities.");
        Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Reach(()=>P() is {PlayerSeat:0} && g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.MoCaoCaoBachao?.Stage==MoCaoCaoBachaoStage.Target);
        Need(E<MoCaoCaoBachaoCommittedEvent>() is [{Count:3}] && E<MoCaoCaoBachaoGiftEvent>().Any(e=>e.ParticipantSeat==1 && e.NonBasic && e.Arrived) &&
            P()!.Choices.All(c=>!c.Targets.Contains(1)),"All private commitments precede native transfers; the actual allied nonbasic gift protects that participant.");
        var view=SnapshotJson.Serialize(g.CreateSnapshot(0));
        g=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);
        Need(view==SnapshotJson.Serialize(g.CreateSnapshot(0)) && E<MoCaoCaoBachaoCommittedEvent>().Length==1,"One representative pending target choice restores its paid gifts and exact owner without repaying.");
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="target" && c.Targets.SequenceEqual([0]));Play();
        var frozen=E<MoCaoCaoHandPrivilegedEvent>().Select(e=>e.CardId).ToHashSet();
        var issue=E<MoCaoCaoEquipmentIssuedEvent>().Last();
        Need(g.CreateSnapshot(0).Players[0].Hp==3 && issue.Hp==3 && EquipmentCatalog.Get(issue.Weapon).WeaponAttackRange==3 && g.GetAttackRange(0)==3 && (issue.Armor is CardKind.BaguaFormation or CardKind.RenwangShield) && EquipmentCatalog.Get(issue.Armor).DisplayName.Length==3 &&
            E<MoCaoCaoBachaoSelfIssuedEvent>().Length==1 && E<DamageAppliedEvent>().Any(e=>e.SourceSeat==0 && e.TargetSeat==0 && e.Amount==1) &&
            E<OlMoRumoEnteredEvent>().Any(e=>e.OwnerSeat==0),"Real self damage and all children return before freezing current Hand and entering existing Rumo.");
        var count=E<MoCaoCaoEquipmentIssuedEvent>().Length;
        for(var i=0;i<3;i++){_ = g.GetHumanLegalActions();_ = g.CreateSnapshot(0);}
        Need(E<MoCaoCaoEquipmentIssuedEvent>().Length==count,"Pure equipment/range and snapshot queries never reroll issued identities.");
        var gear=g.CreateSnapshot(0).Players[0].Hand.First(c=>c.Kind==CardKind.Crossbow).Id;
        Need(g.GetHumanLegalActions().All(a=>a.CardId!=gear || a.Kind!=LegalActionKind.Equip) &&
            g.GetHumanLegalActions().Any(a=>a.CardId==gear && a.Kind==LegalActionKind.DrawTwo && a.ConversionSource?.SkillId=="ol:mo-fuzai"),"Actual equipment is restricted to the two real native conversions.");
        Use(g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Snatch && a.TargetSeats.SequenceEqual([2]) && frozen.Contains(a.CardId!.Value)));Play();
        var ordinaryWine=g.CreateSnapshot(0).Players[0].Hand.Single(c=>c.Kind==CardKind.Alcohol && !frozen.Contains(c.Id)).Id;
        for(var i=0;i<2;i++)
        {
            Use(g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Alcohol && frozen.Contains(a.CardId!.Value)));Play();
            Need(g.CreateSnapshot(0).Players[0].HasAlcoholEffect,"Each real frozen wine applies its native next-Slash benefit.");
            Use(g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]) && frozen.Contains(a.CardId!.Value)));Play();
        }
        var wines=E<CardUseDeclaredEvent>().Where(e=>e.SourceSeat==0 && e.CardKind==CardKind.Alcohol).ToArray();
        Need(E<CardUseDeclaredEvent>().Count(e=>e.SourceSeat==0 && e.CardKind==CardKind.Slash)==2 && wines.Length==2 &&
            wines.All(w=>E<CardUseFinishedEvent>().Count(f=>f.ResolutionId==w.ResolutionId)==1 && E<AlcoholAppliedEvent>().Count(e=>e.ResolutionId==w.ResolutionId)==1) &&
            g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Alcohol && a.CardId==ordinaryWine),
            "Two frozen physical wines and Slashes genuinely finish without spending ordinary quotas; the real newly obtained unprivileged wine still has its normal first use.");
        Use(g.GetHumanLegalActions().First(a=>a.CardId==gear && a.Kind==LegalActionKind.DrawTwo && a.ConversionSource?.SkillId=="ol:mo-fuzai"));Play();
        var later=g.CreateSnapshot(0).Players[0].Hand.Where(c=>c.Kind==CardKind.Slash && !frozen.Contains(c.Id)).Select(c=>c.Id).ToArray();
        Need(later.Length>=2,"The converted real equipment genuinely draws new Slash entities.");
        var ordinarySlash=g.GetHumanLegalActions().FirstOrDefault(a=>a.Kind==LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]) && later.Contains(a.CardId!.Value));
        Need(ordinarySlash is not null,"A new genuine Hand Slash needs its ordinary first legal Use against the still-living actual target; inspect issued equipment, actual debits, damage and legal targets.");
        Use(ordinarySlash!);Play();
        Need(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash && later.Contains(a.CardId!.Value)) &&
            g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash && frozen.Contains(a.CardId!.Value)),"Later drawn entities keep ordinary quota while the previously signed Hand still has its own material-specific exemption.");
        var blade=g.CreateSnapshot(0).Players[0].Hand.Single(c=>c.Kind==CardKind.BaiBiDao).Id;
        Drive("place",[blade],[0]);Play();
        Need(g.CreateSnapshot(0).Players[0].Equipment.Any(c=>c.Id==blade),"Native placement creates a real weapon; Fuzai does not fake a transfer holder.");
        Use(g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash && a.TargetSeats.SequenceEqual([3]) && frozen.Contains(a.CardId!.Value)));
        Reach(()=>P()?.SkillPrompt?.SkillId=="equipment:baibi-dao");Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Reach(()=>P()?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="mo-cao-cao" && c.Parameters.GetValueOrDefault("branch")=="card")==true);
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="card");Play();
        Need(E<MoCaoCaoBaibiIssuedEvent>() is [{Claim.PhysicalWeaponId:>0}] && E<MoCaoCaoBaibiObtainedEvent>() is [{Arrived:true}] &&
            E<MoCaoCaoBaibiIssuedEvent>()[0].Claim.PhysicalWeaponId==blade,"A genuine named weapon ability issues its own native Slash-damage proof and really obtains an opaque target Hand card without pretending to hold Fuzai.");
        Drive("discard",[blade]);Play();
        Need(E<MoCaoCaoEquipmentIssuedEvent>().Length==count && g.CreateSnapshot(0).Players[0].Equipment.Count==0 &&
            g.CreateSnapshot(0).Players[0].ActiveEffects!.Any(e=>e.SkillId=="ol:mo-fuzai" && e.Description.Contains(EquipmentCatalog.Get(issue.Weapon).DisplayName,StringComparison.Ordinal)),"Real equipment departure restores the prior issued virtual identities without rerolling or virtual Silver Lion removal recovery.");
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mo-cao-cao",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,[Hero,"ol:mo-sun-quan"]);
            foreach(var kind in new[]{CardKind.Slash,CardKind.Dodge,CardKind.Peach,CardKind.Alcohol,CardKind.Snatch,CardKind.DrawTwo,CardKind.Crossbow,CardKind.BaiBiDao})
            {var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mcc-card-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
            var defs=new object[]{
                new{id=Quiet,revision=1,triggers=new[]{new{id="quiet",window="afterNormalDraw",subject="owner",optional=false,effects=new[]{new{op="skipTurnPhases",target="owner",phases=new[]{"play"}}}}}},
                new{id=Driver,revision=1,activations=new object[]{
                    new{id="place",minCards=1,maxCards=1,sourceZones=new[]{"hand"},cardCategories=new[]{"equipment"},minTargets=1,maxTargets=1,targetKind="anyLiving",usesPerTurn=(int?)null,effects=new object[]{new{op="captureSelectedCards",target="owner",resultBind="gear"},new{op="placeSelectedEquipment",target="selectedTarget",sourceBind="gear"}}},
                    new{id="discard",minCards=1,maxCards=1,sourceZones=new[]{"equipment"},minTargets=0,maxTargets=0,targetKind="anyLiving",usesPerTurn=(int?)null,effects=new[]{new{op="discardSelected",target="owner",amount=1}}}
                }}
            };
            var labels=new Dictionary<string,object>{[Quiet]=new{name="安静同行",description="跳过真实出牌阶段"},[Driver]=new{name="实体装备驱动",description="原生放置及弃置装备"}};
            var catalog=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=defs}),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p) in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            foreach(var peer in new[]{Ally,EnemyA,EnemyB})b.AddGeneral(new(peer,"真实同行","supporter",Quiet,"wei",8));
            b.AddDeck(new(Deck,"固定真实Slash库存",0,0,[]){PhysicalCards=Enumerable.Range(0,24).Select(_=>new ContentDeckPhysicalCard("fixture:mcc-card-Slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"魔曹操完整主流程",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Ally,EnemyA,EnemyB],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
