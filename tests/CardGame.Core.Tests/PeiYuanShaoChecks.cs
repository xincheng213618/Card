using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PeiYuanShaoChecks
{
    private const string Skill="ol:fulue",Owner="fixture:py-owner",Driver="fixture:py-driver",Health="fixture:py-health",Watch="fixture:py-watch",Mode="identity:classic-pei-yuan-shao-fixture";
    public static void FulueSingleFinalTargetOptionsWholeDamageAndCold()
    {
        {
            var(g,r)=Start("plain");Draw(g);Native(g,a=>a.Kind==LegalActionKind.BarbarianAssault);Play(g);
            Need(!E<FulueOptionExecutedEvent>(g).Any(),"A real multi-target damage trick cannot qualify as a sole designated target.");
            Slash(g,1);Choose(g,FulueOption.Damage);g=Cold(g,r);Play(g);var damage=E<FulueOptionExecutedEvent>(g).Single().Entry;
            Need(Total(g,damage.UseId)==2,"The real native Slash receives one card-wide damage increase.");
            Native(g,a=>a.Kind==LegalActionKind.Duel&&a.TargetSeats.SequenceEqual([1]));Reach(g,()=>FulueMenu(g));Need(P(g)!.Choices.All(c=>c.Parameters.GetValueOrDefault("branch")!="Damage"),"The already executed damage option is absent for this actual turn.");Choose(g,FulueOption.Obtain);Reach(g,()=>FulueObtain(g));var before=State(g);var p=P(g)!;
            Need(p.IsPrivate&&p.Choices.All(c=>c.Cards.Count==0)&&Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null)&&!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==before,"The true foreign-hand obtain menu exposes opaque slots only to its actual owner and rejects a peer answer atomically.");g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");Play(g);
            Need(E<FulueOptionExecutedEvent>(g).Select(e=>e.Entry.Option).SequenceEqual([FulueOption.Damage,FulueOption.Obtain])&&E<FulueObtainPaidEvent>(g).Length==1,"The two independent real options each execute once and native obtain precedes the Duel.");
            Slash(g,1);Play(g);Need(E<FulueOptionExecutedEvent>(g).Length==2&&!E<FulueCounterRequestedEvent>(g).Any(),"Exhausted options do not reoffer on a later ordinary Slash, and actual positive damage prevents a counter request.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("chain");Draw(g);Use(g,"chain",[1]);Play(g);Use(g,"chain",[2]);Play(g);Native(g,a=>a.Kind==LegalActionKind.Slash&&a.PlayedCardKind==CardKind.FireSlash&&a.TargetSeats.SequenceEqual([1]));Choose(g,FulueOption.Damage);Play(g);var entry=E<FulueOptionExecutedEvent>(g).Single().Entry;
            Need(E<CompletedUndamagedUseDamageRecordedEvent>(g).Where(e=>e.CardUseFrameId==entry.UseId).ToArray()is[{Amount:2},{Amount:2,ChainPropagation:true}]&&Total(g,entry.UseId)==4&&!E<FulueCounterRequestedEvent>(g).Any(),"The same real elemental Use includes actual chain damage without adding its Fulue bonus a second time during propagation.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("unique-aoe");Draw(g);foreach(var target in new[]{2,3}){Use(g,"kill",[target]);Reach(g,()=>!V(g,target).IsAlive);Play(g);}Native(g,a=>a.Kind==LegalActionKind.BarbarianAssault);Choose(g,FulueOption.Damage);Play(g);
            Need(E<FulueOptionExecutedEvent>(g) is[{Entry:{TargetSeat:1}}]&&E<FulueOptionExecutedEvent>(g).Single().Entry.UserSeat==0&&Total(g,E<FulueOptionExecutedEvent>(g).Single().Entry.UseId)==2,"A genuine AOE with exactly one actually designated living target qualifies without pretending a multi-target AOE is a single-target trick.");_=Cold(g,r);
        }
    }
    public static void FulueOpaqueObtainEquipmentPaidChildrenAndLossCold()
    {
        foreach(var scene in new[]{"lion","ox","loss","death"})
        {
            var(g,r)=Start(scene);Draw(g);int? equipment=null,grain=null;
            if(scene=="lion"){Use(g,"equip-peer",[1]);Reach(g,()=>Action(g,"select-and-move-owned-card"));Answer(g,c=>c.Cards.Count==1);Play(g);equipment=V(g,1).Equipment.Single().Id;Use(g,"hurt",[1]);Play(g);}
            if(scene=="ox"){Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);equipment=V(g,0).Equipment.Single().Id;grain=V(g,0).Hand[0].Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain.Value],[1],g.Revision,P(g)!.PromptId));Play(g);Need(V(g,1).Equipment.Any(c=>c.Id==equipment)&&V(g,1).WoodenOxGrain!.Any(c=>c.Id==grain),"The genuine Ox command transfers its original equipment and stored grain to the future Slash target.");}
            var hp=V(g,1).Hp;Slash(g,1);Choose(g,FulueOption.Obtain);Reach(g,()=>FulueObtain(g));g=Cold(g,r);Answer(g,c=>equipment is{}id?c.Cards.SequenceEqual([id]):c.Parameters.GetValueOrDefault("slot-index")=="0");
            if(scene=="lion"){Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);Need(V(g,1).Hp==hp+1&&E<FulueObtainPaidEvent>(g).Length==1,"The original target's real Silver Lion recovery pauses beneath an already-paid obtain, before the Slash resolves.");g=Cold(g,r);Continue(g);}
            if(scene is"loss"or"death")
            {Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,r);Continue(g);if(scene=="death"){Reach(g,()=>!V(g,0).IsAlive);Need(g.State.Winner==Winner.None&&E<FulueObtainPaidEvent>(g).Length==1&&!E<FulueCounterUseIssuedEvent>(g).Any(),"Real non-Lord source death drains its already-paid obtain without manufacturing a counter against a dead target.");_=Cold(g,r);continue;}Need(V(g,0).Skills!.All(s=>s.ContentId!=Skill),"The actual gained-card child removes the original conditional Fulue source before its issued card completes.");}
            Play(g);var paid=E<FulueObtainPaidEvent>(g).Single();Need(g.CardMovements.Count(m=>m.Sequence==paid.MoveSequence&&m.CardId==paid.Material.CardId&&m.From==paid.Material.From&&m.To==CardLocation.Hand(0))==1,"The original target entity is obtained exactly once across all real children and replay.");
            if(grain is{}gr)Need(g.CardMovements.Any(m=>m.CardId==gr&&m.From==CardLocation.WoodenOxGrain(1)&&m.To==CardLocation.DiscardPile)&&paid.Material.CardId!=gr,"Removing the genuine target Ox drains native grain cleanup rather than treating grain as the obtained material.");
            if(scene=="loss")Need(E<FulueCounterUseIssuedEvent>(g).Length==1&&E<FulueOptionExecutedEvent>(g).Any(e=>e.Entry.Counter&&e.Entry.Option==FulueOption.Damage),"The originally issued zero-damage consequence and the other unused option survive true original-source loss.");_=Cold(g,r);
        }
    }
    public static void FulueZeroDamageCounterSlashOppositeQuotaAndNativeReturnCold()
    {
        foreach(var usedOther in new[]{false,true})
        {
            var(g,r)=Start("counter");Draw(g);
            if(usedOther){Native(g,a=>a.Kind==LegalActionKind.Duel&&a.TargetSeats.SequenceEqual([1]));Choose(g,FulueOption.Damage);Play(g);}
            Slash(g,1);Choose(g,usedOther?FulueOption.Obtain:FulueOption.Damage);if(usedOther){Reach(g,()=>FulueObtain(g));Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");}
            Reach(g,()=>P(g)is{PlayerSeat:1,Kind:DecisionKind.ProgramTrigger}&&Action(g,"assisted-physical-slash"));Need(g.CreateSnapshot(0).PendingDecision is null,"The original target's actual optional normal-range Slash request is private to that target.");g=Cold(g,r);
            Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);var counter=E<FulueCounterUseIssuedEvent>(g).Single();
            Need(E<FulueCounterRequestedEvent>(g).Length==1&&counter.Invoice.ActorSeat==1&&counter.Invoice.TargetSeat==0&&counter.Invoice.Source.OwnerSeat==0&&Total(g,counter.Invoice.OriginalUseId)==0&&E<CardUseDeclaredEvent>(g).Any(e=>e.ResolutionId==counter.UseId&&e.SourceSeat==1)&&E<CardUseFinishedEvent>(g).Any(e=>e.ResolutionId==counter.UseId)&&!g.ResolutionStack.OfType<CardUseFrame>().Any(),"Only the original whole-Use zero damage opens a genuine reversed native Slash and drains it back to the original completed parent.");
            Need(usedOther?E<FulueOptionExecutedEvent>(g).Count(e=>e.Entry.Counter)==0:E<FulueOptionExecutedEvent>(g).Any(e=>e.Entry.Counter&&e.Entry.Option==FulueOption.Obtain&&e.Entry.UserSeat==1&&e.Entry.TargetSeat==0),"The target executes the original owner's other still-unused option; an already consumed other option suppresses only that benefit, retaining the real counter Slash.");
            Need(E<FulueOptionExecutedEvent>(g).Length==2&&E<CardUseDebitRecordedEvent>(g).Count(e=>e.Debit.Query==SkillRuleQuery.SlashLimit)==1,"The shared original-turn option quota is paid once each, while the requested counter uses its real native extra-use contract and a true Dodge creates no new Fulue trigger.");_=Cold(g,r);
        }
    }
    public static void FulueCounterConversionsRangeDeathAndPrivacyCold()
    {
        foreach(var scene in new[]{"counter-range","counter-multi","counter-virtual","counter-funded-loss"})
        {
            var(g,r)=Start(scene);Draw(g);var target=scene=="counter-range"?2:1;
            if(scene=="counter-multi"){Use(g,"equip-peer",[target]);Reach(g,()=>Action(g,"select-and-move-owned-card"));Answer(g,c=>c.Cards.Count==1);Play(g);Need(V(g,target).Equipment.Single().Kind==CardKind.ZhangbaSerpentSpear,"The real target holds the native Zhangba before its possible multi-material counter Use.");}
            Slash(g,target);Choose(g,scene=="counter-funded-loss"?FulueOption.Obtain:FulueOption.Damage);
            if(scene=="counter-funded-loss")
            {Reach(g,()=>FulueObtain(g));Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,r);Continue(g);Need(V(g,0).Skills!.All(s=>s.ContentId!=Skill),"The genuine original obtain child removes the original conditional Fulue source before the target's draw-funded counter request.");}
            if(scene=="counter-range")
            {Play(g);Need(E<FulueCounterRequestedEvent>(g).Length==1&&!E<FulueCounterUseIssuedEvent>(g).Any()&&E<FulueOptionExecutedEvent>(g).Length==1,"An original long-range Slash does not grant the target a distance exemption for its real reverse Slash, and the impossible native request declines without publishing an invalid draft.");_=Cold(g,r);continue;}
            Reach(g,()=>P(g)?.PlayerSeat==target&&Action(g,"assisted-physical-slash"));var p=P(g)!;var before=State(g);
            Need(p.IsPrivate&&g.CreateSnapshot(0).PendingDecision is null&&!g.Submit(new AnswerPromptCommand(0,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==before,"A nonchooser cannot answer or inspect the actual counter user's native private payment menu.");g=Cold(g,r);
            if(scene=="counter-funded-loss")
            {Need(V(g,target).Hp==2&&V(g,target).Hand.Count==2&&V(g,target).Hand.All(c=>c.Suit==Suit.Spade)&&P(g)!.Choices.Any(c=>c.Parameters.ContainsKey("draw-funded-basic")),"After the genuine obtain and original Slash's real Dodge, the target has exactly two same-color hand cards at two HP and its native counter menu actually offers draw-funded payment: "+Diagnostic(g));Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="fixture:py-funded-watch");Need(E<DrawFundedDistinctBasicPaidEvent>(g).Length==1&&g.ResolutionStack.OfType<DrawFundedDistinctBasicFrame>().Count()==1&&!E<FulueCounterUseIssuedEvent>(g).Any(),"After original-source loss, a genuine draw-funded counter pays its actual draw once and pauses on the original payment frame before declaring its zero-entity Use.");g=Cold(g,r);}
            Play(g);
            var issued=E<FulueCounterUseIssuedEvent>(g).Single();var accepted=E<CardActionAcceptedEvent>(g).Single(e=>e.Action.ActionId==issued.ActionId).Action;Need(accepted.ActorSeat==target&&accepted.ProviderSeat==target&&accepted.PhysicalCards.Count==(scene=="counter-multi"?2:0)&&E<FulueOptionExecutedEvent>(g).Any(e=>e.Entry.Counter&&e.Entry.Option==(scene=="counter-funded-loss"?FulueOption.Damage:FulueOption.Obtain))&&E<CardUseFinishedEvent>(g).Any(e=>e.ResolutionId==issued.UseId),"The genuine counter supports native Zhangba multi-material, round-limited and draw-funded zero-entity Slash use with its original native parent, complete other benefit and return: "+scene);
            if(scene=="counter-funded-loss")Need(E<DrawFundedDistinctBasicPaidEvent>(g).Length==1&&E<DrawFundedDistinctBasicReturnedEvent>(g).Length==1&&E<FulueCounterUseIssuedEvent>(g).Length==1&&V(g,0).Skills!.All(s=>s.ContentId!=Skill),"The cold paid draw returns exactly once to the still-issued original Fulue request after actual source loss, without a second payment or restoring the skill.");
            _=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static int Total(GameEngine g,long id)=>E<CompletedUndamagedUseDamageRecordedEvent>(g).Where(e=>e.CardUseFrameId==id).Sum(e=>e.Amount);
    private static bool Action(GameEngine g,string action)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action)==true;
    private static bool FulueMenu(GameEngine g)=>P(g)?.SkillPrompt?.SkillId==Skill&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch") is"Damage"or"Obtain");
    private static bool FulueObtain(GameEngine g)=>P(g)?.SkillPrompt?.SkillId==Skill&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="obtain");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var a=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(a.Accepted,a.Error?.Message??"Rejected actual Fulue command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;var c=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==0&&c is not null,"Missing actual human Fulue choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g){if(P(g)?.PlayerSeat==0)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Use(GameEngine g,string id,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Draw(GameEngine g){Use(g,"draw");Play(g);}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Slash(GameEngine g,int target)=>Native(g,a=>a.Kind==LegalActionKind.Slash&&a.PlayedCardKind==CardKind.Slash&&a.TargetSeats.SequenceEqual([target]));
    private static void Choose(GameEngine g,FulueOption option){Reach(g,()=>FulueMenu(g));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")==option.ToString());}
    private static void Native(GameEngine g,Func<LegalAction,bool> predicate){var a=g.GetHumanLegalActions().FirstOrDefault(predicate);Need(a is not null,"Missing native Fulue action: "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void Play(GameEngine g,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard},$"{caller}:{line} actual Play");
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string caller="",[CallerLineNumber]int line=0,[CallerArgumentExpression(nameof(done))]string? boundary=null)
    {
        for(var i=0;i<240;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"Fulue ended before {caller}:{line} {boundary}: "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0&&c.Parameters.GetValueOrDefault("program-action")!="kenshang-request-open");
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p.SkillPrompt?.SkillId is Health or Watch)Continue(g);
            else if(Action(g,"fulue")&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="skip");
            else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else throw new InvalidOperationException($"Unexpected Fulue {caller}:{line} {boundary}: "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing bounded Fulue {caller}:{line} {boundary}: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,g.State.Winner,Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(5).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(4).Select(f=>new{f.Id,Type=f.GetType().Name}),Facts=g.Events.TakeLast(4).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold replay preserves all actual Fulue views, native Use/whole-damage/option invoices and once-paid child returns.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene=="death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);}
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-pei-yuan-shao",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:pei-yuan-shao");var counter=scene.StartsWith("counter",StringComparison.Ordinal)||scene=="loss";var sourceLoss=scene is"loss"or"counter-funded-loss";
            var defs=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0},{"id":"range","query":"attackRange","operation":"set","value":4,"priority":0},{"id":"slashes","query":"slashLimit","operation":"add","value":6,"priority":0}],"viewAs":[
             {"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},
             {"id":"fire","inputKinds":["slash"],"inputSuits":[],"outputKind":"fireSlash","forPlay":true,"forResponse":false},
             {"id":"duel","inputKinds":[],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true,"allowSameKind":true},
             {"id":"aoe","inputKinds":[],"inputSuits":[],"outputKind":"barbarianAssault","forPlay":true,"forResponse":false,"singleCardTrickUse":true,"allowSameKind":true}],"activations":[
             {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
             {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
             {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
             {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
             {"id":"equip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]}]}
            """,$$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]}""","""{"id":"fixture:py-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            if(counter&&scene is not("counter-multi"or"counter-virtual"or"counter-funded-loss"))defs.Add("""{"id":"fixture:py-response-user","revision":1,"viewAs":[{"id":"actual-slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true}]}""");
            if(scene=="counter-virtual")defs.Add("""{"id":"fixture:py-response-user","revision":1,"viewAs":[{"id":"round-slash","inputKinds":[],"inputSuits":[],"inputCount":0,"sourceZones":[],"outputKind":"slash","forPlay":true,"forResponse":false,"roundOnceCardlessBasic":{"usageGroup":"py-counter","usesPerRound":1}}]}""");
            if(scene=="counter-funded-loss")
            {defs.Add("""{"id":"fixture:py-response-user","revision":1,"viewAs":[{"id":"funded-slash","inputKinds":[],"inputSuits":[],"inputCount":0,"sourceZones":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true,"drawFundedDistinctBasic":{"methodLedgerId":"fixture:py-counter-funded"}}]}""");defs.Add("""{"id":"fixture:py-funded-watch","revision":1,"triggers":[{"id":"actual-paid-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.draw-funded-distinct-basic.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-paid","options":[{"id":"continue"}]}]}]}""");}
            if(sourceLoss)defs.Add($$"""{"id":"fixture:py-conditional","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"{{Skill}}","minimumHp":13}]}""");
            if(sourceLoss||scene=="death")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-paid-obtain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.pei-yuan-shao.fulue.obtain"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":{{(scene=="death"?20:1)}}}]}]}""");
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实复掠夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实夹具"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:py-pick","实际本人","固定公开选将评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});
            var own=new List<string>{Driver,Health,sourceLoss?"fixture:py-conditional":Skill};if(sourceLoss||scene=="death")own.Add(Watch);b.AddGeneral(new(Owner,"实际裴元绍","ol-pei-yuan-shao","fixture:py-pick","qun",12,own,GeneralGender.Male));
            var peers=new[]{"fixture:py-peer-a","fixture:py-peer-b","fixture:py-peer-c"};foreach(var peer in peers){var skills=new List<string>{Health};if(counter&&scene!="counter-multi")skills.Add("fixture:py-response-user");if(scene=="counter-funded-loss")skills.Add("fixture:py-funded-watch");b.AddGeneral(new(peer,"实际反杀角色","supporter","fixture:py-quiet","qun",12,skills){InitialHp=scene=="counter-funded-loss"?2:null});}
            var kinds=scene switch{"lion"=>new[]{CardKind.SilverLion},"ox"=>[CardKind.WoodenOx],"chain"=>[CardKind.Slash],"counter-multi"=>[CardKind.ZhangbaSerpentSpear,CardKind.Dodge,CardKind.Dodge,CardKind.Dodge],_ when counter||scene=="death"=>[CardKind.Dodge],_=>[CardKind.Crossbow]};
            foreach(var kind in kinds.Distinct())b.AddCard(new("fixture:py-card-"+kind,"真实"+kind,kind is CardKind.SilverLion or CardKind.WoodenOx or CardKind.Crossbow or CardKind.ZhangbaSerpentSpear?"装备牌":"基本牌","真实原生实体",kind));
            b.AddDeck(new("fixture:py-deck","固定有界实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard("fixture:py-card-"+kinds[i%kinds.Length],Suit.Spade,7)).ToArray()});b.AddMode(new(Mode,"复掠原生小局面",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:py-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
