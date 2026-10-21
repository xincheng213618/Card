using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouLuSuAnniversaryChecks
{
    private const string Hero="mou:lu-su",Plain="fixture:mlsa-plain",Driver="fixture:mlsa-driver",Quiet="fixture:mlsa-quiet",Hold="fixture:mlsa-played-complete",Deck="fixture:mlsa-deck";
    public static void ActualDrawGiftYangDamageEquipmentGainYinPlayedAndCold()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        ShanheStartingCard[] hand=[new(CardKind.Slash,Suit.Heart,3),new(CardKind.Slash,Suit.Club,4),new(CardKind.Slash,Suit.Spade,5),new(CardKind.Snatch,Suit.Diamond,6),new(CardKind.Crossbow,Suit.Club,7)];
        var setup=new ShanheBattleSetup(7,1,"mou-lu-su-native",Hero,2,0,0,0,0,[Driver,Hold],hand,[],
            [new(Plain,Hp:16){StartingHand=[]},new(Plain,Hp:6){StartingHand=Enumerable.Range(0,4).Select(i=>new ShanheStartingCard(CardKind.Slash,Suit.Spade,i+2)).ToArray()}],
            [new(Plain,Hp:16){StartingHand=Enumerable.Range(0,12).Select(i=>new ShanheStartingCard(CardKind.Dodge,Suit.Club,i+1)).ToArray()}],new string('e',64));
        var game=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=4},registry);
        PendingDecision? Prompt()=>Enumerable.Range(0,4).Select(s=>game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
        PlayerSnapshot Player(int seat)=>game.CreateSnapshot(0).Players[seat];
        T[] Events<T>()=>game.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        MouLuSuAnniversaryReceipt? Receipt()=>game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.MouLuSuAnniversary is not null)?.MouLuSuAnniversary;
        string Diagnostic()=>JsonSerializer.Serialize(new{game.State.TurnNumber,game.State.CurrentSeat,game.State.Phase,Prompt=Prompt(),Receipt=Receipt(),Players=game.CreateSnapshot(0).Players.Select(p=>new{p.Seat,p.GeneralId,p.Hp,p.MaxHp,p.HandCount}),Frames=game.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=game.Events.TakeLast(35).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why+Diagnostic());}
        void Submit(GameCommand command){try{var result=game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Native command rejected.");}catch(Exception ex){throw new InvalidOperationException(ex.Message+Diagnostic(),ex);}}
        void Answer(Func<PromptChoice,bool> pick){var p=Prompt();Need(p?.PlayerSeat==0,"Only the actual human chooser is answered.");var c=p!.Choices.FirstOrDefault(pick);Need(c is not null,"Required native Lu Su choice missing.");Submit(new AnswerPromptCommand(0,p.PromptId,c!.Id,game.Revision));}
        void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<200;i++)
            {
                if(done())return;Need(game.State.Winner==Winner.None,"Unexpected terminal result at "+line);
                if(Prompt() is {PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification}){Answer(c=>c.Cards.Count==0);continue;}
                if(Prompt() is {PlayerSeat:0}p)throw new InvalidOperationException("Unexpected actual human input at "+line+Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded actual Mengmou boundary not reached at "+line+Diagnostic());
        }
        void Play()=>Reach(()=>Prompt() is {PlayerSeat:0,Kind:DecisionKind.PlayCard});
        void Activate(string skill){Reach(()=>Prompt() is {PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"&&c.Parameters.GetValueOrDefault("skill-id")==skill));Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");}
        Submit(new StartGameCommand());Reach(()=>Receipt()?.Stage==MouLuSuAnniversaryStage.Opening&&Prompt()?.PlayerSeat==0);Answer(c=>c.Parameters.GetValueOrDefault("branch")=="yang");
        Activate("mou:mingshi");Reach(()=>Receipt()?.Stage==MouLuSuAnniversaryStage.Show&&Prompt()?.PlayerSeat==0);
        Need(Player(0).HandCount==9,"Current Mingshi adds two to the genuine normal Draw rather than replacing it: five opening cards become nine before showing.");
        var gift=Player(0).Hand.First(c=>c.Kind==CardKind.Slash).Id;Answer(c=>c.Cards.SequenceEqual([gift]));
        for(var n=0;n<2;n++)Answer(c=>c.Cards is [var id]&&Player(0).Hand.Any(h=>h.Id==id&&h.Kind==CardKind.Dodge));
        Reach(()=>Receipt()?.Stage==MouLuSuAnniversaryStage.Recipient&&Prompt()?.PlayerSeat==0);
        var shown=Events<MouLuSuAnniversaryShownEvent>().Select(e=>e.CardId).ToArray();
        Need(shown.Length==3&&shown.All(id=>game.CreateSnapshot(3).PublicRevealedCards?.Any(c=>c.Id==id)==true),"The three selected actual hand entities are publicly visible before the recipient chooses one.");
        Answer(c=>c.Targets.SequenceEqual([3]));Activate("mou:mengmou");Play();
        var yang=Events<MouLuSuAnniversaryAcceptedEvent>().Single();var uses=Events<MouLuSuAnniversaryUseIssuedEvent>();
        Need(yang is {Owner:0,Other:3,Yin:false,X:3}&&uses.Length==3&&uses.All(e=>e.Origin.Actor==3)&&Events<MouLuSuAnniversaryUseReturnedEvent>().Length==3&&Events<MouLuSuAnniversaryGiftEvent>() is [{Recipient:3,CardId:var actual}]&&actual==gift,
            "The actual recipient receives the first publicly shown Slash, then uses three genuine native Slash actions under frozen maxHP X=3 before returning to Mingshi.");
        Need(Player(0).Hp==2&&Player(3).Hp==9&&Events<DamageAppliedEvent>().Count(e=>e.SourceSeat==3&&e.TargetSeat==0&&e.Amount==1)==3&&Events<RecoveryAppliedEvent>().Count(e=>e.SourceSeat==0&&e.Amount==1&&(e.TargetSeat==0||e.TargetSeat==3))==6&&Player(0).HandCount==10,
            "Each actual requested Slash damage point immediately produces two genuine one-point recoveries; after the shown gift the owner is below the other role's twelve cards and really draws two more.");
        var weapon=Player(0).Hand.Single(c=>c.Kind==CardKind.Crossbow).Id;
        Submit(new UseProgramSkillCommand(0,Driver,"place",[weapon],[3],game.Revision,Prompt()!.PromptId));Play();
        Need(Player(3).Equipment.Any(c=>c.Id==weapon)&&Events<MouLuSuAnniversaryAcceptedEvent>().Length==1,"Native equipment placement changes the real equipment region without masquerading as a Hand acquisition.");
        var snatch=game.GetHumanLegalActions().FirstOrDefault(a=>a.Kind==LegalActionKind.Snatch&&a.TargetSeats.SequenceEqual([3])&&a.TargetCardId==weapon&&a.ConversionSource is null);
        Need(snatch is not null,"The visible equipped entity has a real native Snatch action.");
        Submit(new PlayCardCommand(0,snatch!.CardId!.Value,snatch.TargetSeats,game.Revision,Prompt()!.PromptId,snatch.PlayedCardKind,snatch.TargetCardId));
        Activate("mou:mengmou");Reach(()=>Prompt()?.SkillPrompt?.SkillId==Hold);
        var response=game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(f=>f.MouLuSuAnniversaryPlayed is {Window:SkillProgramTriggerWindow.CardResponseCompleted});
        var played=Receipt()!;var action=response.Action;
        Need(played is {Yin:true,Other:3,X:3,Index:0,Stage:MouLuSuAnniversaryStage.PlayedCompleted}&&action.Type==CardActionType.Response&&action.ActorSeat==3&&action.TargetSeats.Count==0&&action.PhysicalCards.Count==1&&
            game.CardMovements.Any(m=>m.CardId==action.PhysicalCards[0].CardId&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.ResponseFinished)&&
            Events<MouLuSuAnniversaryTransferInvoiceEvent>().Any(e=>e.Owner==3&&e.Recipient==0&&game.CardMovements.Any(m=>m.Sequence==e.EntrySequence&&m.CardId==weapon&&m.From==CardLocation.Equipment(3)&&m.To==CardLocation.Processing)),
            "Native equipped-card Snatch retains its exact original-owner invoice and triggers Yin; the first genuine Played response is already physically discarded before its Completed observer.");
        var before=SnapshotJson.Serialize(game.CreateSnapshot(0));var frameId=response.ParentFrameId;var responseId=action.ActionId;
        game=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
        Need(before==SnapshotJson.Serialize(game.CreateSnapshot(0))&&game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(f=>f.MouLuSuAnniversaryPlayed is { } r&&r.FrameId==frameId&&r.ActionId==responseId),"Cold at the actual Played completion preserves the exact already-paid Response action and its original typed parent.");
        Answer(c=>c.Parameters.GetValueOrDefault("option-id")=="continue");Play();
        var yin=Events<MouLuSuAnniversaryAcceptedEvent>().Single(e=>e.Yin);var completed=Events<MouLuSuAnniversaryPlayedEvent>().Where(e=>e.Completed).ToArray();
        Need(yin is {Owner:0,Other:3,X:3}&&completed.Length==2&&completed.Select(e=>e.ActionId).Distinct().Count()==2&&Events<CardActionAcceptedEvent>().Count(e=>completed.Any(p=>p.ActionId==e.Action.ActionId)&&e.Action.Type==CardActionType.Response)==2&&
            completed.All(e=>!Events<CardActionAcceptedEvent>().Any(u=>u.Action.ActionId==e.ActionId&&u.Action.Type==CardActionType.Use))&&Events<ProgramSkillHpLostEvent>().Count(e=>e.FrameId==yin.FrameId&&e.TargetSeat==3&&e.Amount==1)==1&&Player(3).Hp==8,
            "Both actually available Slashes are Played once through accepted and completed response windows, never fake Uses; the one missing response causes one native LoseHp after the cold return.");
        var extra=Player(0).Hand.First(c=>c.Kind==CardKind.Slash).Id;Submit(new UseProgramSkillCommand(0,Driver,"give",[extra],[3],game.Revision,Prompt()!.PromptId));Play();
        Need(Events<MouLuSuAnniversaryAcceptedEvent>().Length==2&&Events<MouLuSuAnniversaryUseIssuedEvent>().Length==3,"A further actual transfer in this same turn cannot reuse either the already consumed Yang or Yin branch.");
        var ordinary=game.GetHumanLegalActions().FirstOrDefault(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([3])&&a.ConversionSource is null);Need(ordinary is not null,"The requested native Slashes preserve the ordinary current-turn Slash allowance.");
        Submit(new PlayCardCommand(0,ordinary!.CardId!.Value,ordinary.TargetSeats,game.Revision,Prompt()!.PromptId,ordinary.PlayedCardKind,ordinary.TargetCardId));Play();
        Need(Player(3).Hp==7&&uses.All(e=>!Events<CardUseDebitRecordedEvent>().Any(d=>d.Debit.CardActionId==e.Origin.ActionId))&&Events<CardUseDebitRecordedEvent>().Any(e=>e.Debit.ActorSeat==0),"Only the final ordinary Slash spends its native quota; the three requested Uses spend no ordinary Slash debit and the native Played branch produces no Use debit.");
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-lu-su-anniversary",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);foreach(var kind in new[]{CardKind.Slash,CardKind.Dodge,CardKind.Snatch,CardKind.Crossbow}){var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mlsa-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
            var rules=$$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
                    {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gift"},{"op":"moveBoundCards","target":"owner","sourceBind":"gift","destination":"selectedTargetHand"}]}
                  ]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"{{Hold}}","revision":1,"triggers":[{"id":"real-played-completion","window":"cardResponseCompleted","ownerRelation":"observer","cardKinds":["slash"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
                ]}
                """;
            var labels=JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Driver]=new{name="真实区域转移",description="真实安置装备或交给实体。"},[Quiet]=new{name="安静同行",description="只为NPC跳过出牌。"},[Hold]=new{name="真实打出完成",description="真实打出已经弃置后的原生完成窗。",optionLabels=new Dictionary<string,string>{{"continue","继续"}}}}});
            var catalog=SkillProgramCatalog.Load(rules,labels);foreach(var id in new[]{Driver,Quiet,Hold})b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=catalog.Programs[id],ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new(Plain,"真实盟谋目标","","standard:none","wei",16,[Quiet]));
            b.AddDeck(new(Deck,"固定真实闪牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("fixture:mlsa-"+CardKind.Dodge,Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋鲁肃真实主流程",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Plain],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
