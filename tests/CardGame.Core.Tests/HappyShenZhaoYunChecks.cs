using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class HappyShenZhaoYunChecks
{
    private const string Hero="happy:shen-zhao-yun",Longhun="happy:longhun",Peer="fixture:happy-zyun-peer",Caster="fixture:happy-zyun-caster",Deck="fixture:happy-zyun-deck",
        NoDraw="fixture:happy-zyun-no-draw",Quiet="fixture:happy-zyun-quiet",Counter="fixture:happy-zyun-counter",Arrows="fixture:happy-zyun-arrows",DrawHold="fixture:happy-zyun-draw",GainHold="fixture:happy-zyun-gain";
    public static void TwoRedSingleUseAndBlackCurrentTurnObtainsResumeCold()
    {
        var s=Scene.Start();s.Play();var original=s.V(0).Hand.ToArray();
        var hearts=original.Where(c=>c.Suit==Suit.Heart).Select(c=>c.Id).ToArray();var diamonds=original.Where(c=>c.Suit==Suit.Diamond).Select(c=>c.Id).ToArray();var clubs=original.Where(c=>c.Suit==Suit.Club).OrderBy(c=>c.Rank).Select(c=>c.Id).ToArray();
        var lion=s.V(0).Equipment.Single(c=>c.Kind==CardKind.SilverLion).Id;var weapon=s.V(0).Equipment.Single(c=>c.Kind==CardKind.Crossbow).Id;
        s.Need(s.V(0).Hp==1&&s.V(0).MaxHp==4&&s.V(0).HandLimit==4,"The real fixture HP retains Juejing's additive three-card hand allowance.");
        s.Use("two-heart",hearts,[]);s.DrawBoundary("two-heart-draw");
        var peach=s.G.ResolutionStack.OfType<CardUseFrame>().Last(u=>u.Action?.ConversionChain.Any(c=>c.SkillId==Longhun&&c.BindingId=="2-heart")==true);var peachId=peach.Id;
        var firstDraw=s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f=>f.Context.ParentFrameId==s.Benefit!.Id);
        s.Need(peach.Action!.PhysicalCards.Select(c=>c.CardId).Order().SequenceEqual(hearts.Order())&&firstDraw.Context.RequestedCount==1,"A committed two-heart Peach issues one native draw against its original two-material use receipt.");
        s.Cold();s.Skip();s.Play();
        s.Need(s.V(0).Hp==3&&s.V(0).HandCount==original.Length-1&&s.E<RecoveryAppliedEvent>().Any(e=>e.TargetSeat==0&&e.Amount==2)&&s.Resolved("two-heart-draw")==1,
            "The two-heart native use recovers two and draws exactly one; its native child cold return never repeats either material.");
        s.Use("two-diamond",diamonds,[1,3]);s.DrawBoundary("two-diamond-draw");
        var fire=s.G.ResolutionStack.OfType<CardUseFrame>().Last(u=>u.Action?.ConversionChain.Any(c=>c.SkillId==Longhun&&c.BindingId=="2-diamond")==true);var fireId=fire.Id;
        var committed=s.G.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last(w=>w.Action.ActionId==fire.Action!.ActionId);
        s.Need(fire.Action!.EffectiveKind==CardKind.FireSlash&&fire.TargetSeats.SequenceEqual([1,3])&&fire.Action.PhysicalCards.Count==2&&
            committed.Candidates.Count(c=>c.SkillId==Longhun&&c.TriggerId=="two-diamond-draw")==1,
            "The real Shichou material adapter accepts two actual Fire Slash targets before commitment, while the static single-use contract freezes only one red draw candidate.");
        s.Skip();s.Play();s.Accept(new EndPlayPhaseCommand(0,s.G.Revision,s.P!.PromptId));
        s.Reach(()=>s.P is{PlayerSeat:0,Kind:DecisionKind.RespondDodge}&&s.G.State.CurrentSeat==0);
        var ownIncomingId=s.G.ResolutionStack.OfType<CardUseFrame>().Last(u=>u.SourceSeat==3&&u.CardKind==CardKind.Slash).Id;
        s.Dodge([clubs[0],lion]);s.TakeTarget(0);
        s.Reach(()=>s.P?.Choices.Any(c=>Action(c)=="select-and-move-owned-card"&&c.Cards.SequenceEqual([weapon]))==true);
        s.Need(s.V(0).Hp==4&&s.E<SilverLionRemovedRecoveryEvent>().Any(e=>e.PlayerSeat==0&&e.RecoveredAmount==1),"The genuine black equipment Dodge finishes its Silver Lion HP cost before opening its actual-use obtain.");
        s.Answer(c=>Action(c)=="select-and-move-owned-card"&&c.Cards.SequenceEqual([weapon]));s.GainBoundary();s.Skip();
        s.Reach(()=>s.E<CardUseFinishedEvent>().Any(e=>e.ResolutionId==ownIncomingId));
        s.Need(s.V(0).Hand.Any(c=>c.Id==weapon),"The own-turn black true use obtains its original weapon into hand. "+JsonSerializer.Serialize(new{weapon,Hand=s.V(0).Hand}));
        s.Need(s.V(0).Equipment.Count==0,"The Silver Lion material and obtained weapon both leave equipment. "+JsonSerializer.Serialize(s.V(0).Equipment));
        var ownWeaponMoves=s.G.CardMovements.Where(m=>m.CardId==weapon&&m.Reason.Value=="skill-program.happy:longhun.SelectAndMoveOwnedCard").ToArray();
        s.Need(ownWeaponMoves.Length==2&&ownWeaponMoves.Count(m=>m.From==CardLocation.Equipment(0)&&m.To==CardLocation.Processing)==1&&
            ownWeaponMoves.Count(m=>m.From==CardLocation.Processing&&m.To==CardLocation.Hand(0))==1&&ownWeaponMoves[0].From==CardLocation.Equipment(0)&&ownWeaponMoves[1].To==CardLocation.Hand(0)&&
            ownWeaponMoves[0].Sequence<ownWeaponMoves[1].Sequence&&ownWeaponMoves[0].TurnNumber==ownWeaponMoves[1].TurnNumber,
            "The authorized same-owner return pays Equipment(0) to Processing once, then obtains the same entity into Hand(0) once under the original skill movement reason. "+JsonSerializer.Serialize(ownWeaponMoves));
        s.Need(s.Resolved("two-club-obtain")==1&&s.Resolved("two-diamond-draw")==1,"Each real use resolves its one committed reward.");
        s.Need(s.E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&e.Amount==2&&e.Nature==DamageNature.Fire&&e.TargetSeat is 1 or 3)==2,"The multi-target two-red use deals exactly two fire damage to both original targets.");
        s.Need(s.E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==fireId)==1,"The original multi-target Fire Slash finishes once. "+JsonSerializer.Serialize(new{fireId,Finished=s.E<CardUseFinishedEvent>()}));
        s.Reach(()=>s.P is{PlayerSeat:0,Kind:DecisionKind.RespondDodge}&&s.G.State.CurrentSeat==3&&s.HasUse(CardKind.ArrowBarrage));
        s.Dodge([clubs[1],clubs[2]]);
        s.Reach(()=>s.P is{PlayerSeat:0,Kind:DecisionKind.RespondDodge}&&s.G.State.CurrentSeat==3&&s.HasUse(CardKind.Slash));
        s.Need(s.Resolved("two-club-obtain")==1,"The same two-club conversion purely played against native Arrow Barrage does not open a false use reward.");
        var incoming=s.G.ResolutionStack.OfType<CardUseFrame>().Last(u=>u.SourceSeat==3&&u.CardKind==CardKind.Slash);var incomingId=incoming.Id;
        s.Dodge([clubs[3],clubs[4]]);s.TakeTarget(3);
        s.Reach(()=>s.P?.Choices.Any(c=>Action(c)=="select-and-move-owned-card"&&c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand))==true);
        s.Need(s.P!.Choices.Where(c=>c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand)).All(c=>c.Cards.Count==0)&&s.Benefit!.SelectedTargetSeats.SequenceEqual([3]),"The other-turn black true-use reward freezes the actual turn owner and presents an opaque hand slot.");
        s.Answer(c=>Action(c)=="select-and-move-owned-card"&&c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand)&&c.Parameters.GetValueOrDefault("slot-index")=="0");s.GainBoundary();
        var claimed=s.G.CardMovements.Single(m=>m.From==CardLocation.Hand(3)&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.happy:longhun.SelectAndMoveOwnedCard");
        s.Cold();s.Skip();s.Reach(()=>s.E<CardUseFinishedEvent>().Any(e=>e.ResolutionId==incomingId));
        s.Need(s.Resolved("two-club-obtain")==2&&s.V(0).Hand.Any(c=>c.Id==claimed.CardId)&&s.G.CardMovements.Count(m=>m.CardId==claimed.CardId&&m.Reason==claimed.Reason)==1&&
            s.G.CardMovements.Count(m=>m.Sequence==claimed.Sequence&&m.CardId==claimed.CardId&&m.From==CardLocation.Hand(3)&&m.To==CardLocation.Hand(0)&&m.Reason==claimed.Reason)==1&&
            s.E<NativeDrawAttemptDecidedEvent>().Count(e=>e.AttemptFrameId==firstDraw.Id&&e.RequestedCount==1&&!e.Replaced)==1&&s.E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==peachId)==1&&
            hearts.Concat(diamonds).All(id=>s.G.CardMovements.Count(m=>m.CardId==id&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use)==1)&&
            clubs.Append(lion).All(id=>s.G.CardMovements.Count(m=>m.CardId==id&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Respond)==1)&&!s.E<DamageAppliedEvent>().Any(e=>e.TargetSeat==0),
            "Cold preserves both genuine-use obtains, excludes the pure response, pays all original materials once and returns to the original native Slash without damage or duplicate gains.");
    }
    private static string? Action(PromptChoice c)=>c.Parameters.GetValueOrDefault("program-action");
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-zhao-yun",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero,"ol:sp-ma-chao");
            foreach(var kind in new[]{CardKind.SilverLion,CardKind.Crossbow}){var cardDefinition=CardCatalog.Get(kind);b.AddCard(new("fixture:happy-zyun-card-"+kind,cardDefinition.DisplayName,cardDefinition.CategoryName,cardDefinition.Description,kind));}
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Counter}}","revision":1,"triggers":[{"id":"counter","isSkillInvocation":false,"window":"playEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"effects":[{"op":"useVirtualSlash","target":"owner","targetRef":{"kind":"eventTarget"} }]}]},
              {"id":"{{Arrows}}","revision":1,"triggers":[{"id":"arrows","isSkillInvocation":false,"window":"playPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"owner"},"outputKind":"arrowBarrage"}]}]},
              {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":false}]}]},
              {"id":"{{GainHold}}","revision":1,"triggers":[{"id":"gain","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.happy:longhun.SelectAndMoveOwnedCard"],"optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":false}]}]}
            ]}
            """;
            var labels=new[]{NoDraw,Quiet,Counter,Arrows,DrawHold,GainHold}.ToDictionary(id=>id,id=>new{name=id,description="真实龙魂使用、纯打出与原材料返回"});
            var c=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id]});
            b.AddGeneral(new(Peer,"实际回合安静角色","",Quiet,"wei",12));b.AddGeneral(new(Caster,"真实杀与万箭角色","",NoDraw,"wei",12,[Counter,Arrows]));
            b.AddDeck(new(Deck,"固定实体牌",0,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"欢乐龙魂小局",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer,Caster],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public ProgramSkillFrame? Benefit=>G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.SkillId==Longhun&&f.TriggerId is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public int Resolved(string id)=>E<ProgramCardTriggerResolvedEvent>().Count(e=>e.SkillId==Longhun&&e.TriggerId==id&&e.OwnerSeat==0&&e.Activated);
        public bool HasUse(CardKind kind)=>G.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.SourceSeat==3&&u.CardKind==kind);
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void Use(string activation,int[] cards,int[] targets)=>Accept(new UseProgramSkillCommand(0,Longhun,activation,cards,targets,G.Revision,P!.PromptId));
        public void DrawBoundary(string trigger)=>Reach(()=>P?.SkillPrompt?.SkillId==DrawHold&&Benefit?.TriggerId==trigger);
        public void GainBoundary()=>Reach(()=>P?.SkillPrompt?.SkillId==GainHold&&Benefit?.TriggerId=="two-club-obtain");
        public void TakeTarget(int seat){Reach(()=>P?.SkillPrompt?.SkillId==Longhun&&P.Choices.Any(c=>c.Targets.SequenceEqual([seat])));Answer(c=>c.Targets.SequenceEqual([seat]));}
        public void Dodge(int[] cards)=>Answer(c=>c.Parameters.GetValueOrDefault("conversion-skill-id")==Longhun&&c.Parameters.GetValueOrDefault("conversion-binding-id")=="2-club"&&c.Cards.Count==cards.Length&&c.Cards.ToHashSet().SetEquals(cards));
        public void Skip()=>Answer(c=>Action(c)=="skip");
        public void Answer(Func<PromptChoice,bool> predicate){var p=P!;var c=p.Choices.FirstOrDefault(predicate);Need(c is not null,"Missing exact current Happy Zhao Yun choice.");Accept(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,G.Revision));}
        public void Reach(Func<bool> done)
        {
            for(var n=0;n<220;n++)
            {
                if(done())return;
                if(P is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId is DrawHold or GainHold)Skip();
                else if(P is{PlayerSeat:0,Kind:DecisionKind.Nullification,IncomingCard:CardKind.ArrowBarrage,SourceSeat:3})Answer(c=>c.Parameters.GetValueOrDefault("response")=="pass");
                else if(P is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Happy Zhao Yun boundary "+JsonSerializer.Serialize(new{P,Benefit}));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Zhao Yun missed the bounded actual flow "+SnapshotJson.Serialize(G.CreateSnapshot(0)));
        }
        public void Cold()
        {
            var views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();var frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray();
            G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);
            Need(views.SequenceEqual(Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))))&&frames.SequenceEqual(G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType()))),"Cold retains the original material invoice, true-use source, actual-turn target and typed native child.");
        }
        public void Accept(GameCommand c){var r=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected Happy Zhao Yun command.");}
        public void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Benefit,Resolved=E<ProgramCardTriggerResolvedEvent>(),Damage=E<DamageAppliedEvent>()}));}
        public static Scene Start()
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var peer=new ShanheCombatantDefinition(Peer,Hp:12,ExtraSkillIds:[NoDraw]){StartingHand=[]};
            var caster=new ShanheCombatantDefinition(Caster,Hp:12){StartingHand=[new(CardKind.Slash,Suit.Spade,12),new(CardKind.Slash,Suit.Heart,11)]};
            var cards=new[]{new ShanheStartingCard(CardKind.Slash,Suit.Heart,2),new(CardKind.Dodge,Suit.Heart,3),new(CardKind.Slash,Suit.Diamond,4),new(CardKind.Dodge,Suit.Diamond,5)}
                .Concat(Enumerable.Range(0,5).Select(i=>new ShanheStartingCard(i%2==0?CardKind.Slash:CardKind.Dodge,Suit.Club,i+6))).ToArray();
            var setup=new ShanheBattleSetup(37,1,"happy-zhao-yun",Hero,1,2,0,0,0,[NoDraw,DrawHold,GainHold,"ol:shichou"],cards,[new(CardKind.SilverLion,Suit.Club,4),new(CardKind.Crossbow,Suit.Spade,1)],[peer,caster],[peer],new string('f',64)){DefaultGodFactionId="wei"};
            var g=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);
            var s=new Scene(g,registry);s.Accept(new StartGameCommand());return s;
        }
    }
}
