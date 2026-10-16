using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaiMaoChecks
{
    private const string Owner="fixture:cm-owner",Driver="fixture:cm-driver",Quiet="fixture:cm-quiet",Watch="fixture:cm-watch",Source="fixture:cm-source",Shield="fixture:cm-shield",Mode="identity:classic-cai-mao";
    private const string ExchangeReason="skill-program.cai-mao.zuolian.exchange";
    public static void WholeRevealDeclineHpLimitPrivacyAndMiuyanCold()
    {
        var(g,r)=Start("reveal");Use(g,"lower");Play(g);Need(V(g).Hp==2,"The actual HP loss fixes a genuine two-target budget.");
        Begin(g);var prompt=P(g)!;var before=State(g);
        Need(prompt is{PlayerSeat:0,IsPrivate:true}&&g.CreateSnapshot(1).PendingDecision is null&&!g.Submit(new AnswerPromptCommand(1,prompt.PromptId,prompt.Choices[0].Id,g.Revision)).Accepted&&State(g)==before,"Only the real source can answer its private target selection; a foreign reply changes no cards or state.");
        Select(g,0);g=Cold(g,r);Select(g,1);
        Need(P(g)!.Choices.All(c=>c.Targets.Count==0)&&Current(g)!.FrozenHp==2,"Selecting the current HP budget removes all further target choices, including otherwise eligible characters.");
        Done(g);var reveal=Current(g)!.Reveals.ToArray();
        Need(reveal.Length==2&&reveal.Select(e=>e.HolderSeat).SequenceEqual([0,1])&&reveal.All(e=>E<ProgramCardsRevealedEvent>(g).Any(v=>v.FrameId==Frame(g).Id&&v.Bind==e.Bind&&v.Cards is[var card]&&card.Id==e.CardId)),"Every selected role, including the source, has one real publicly revealed original hand entity before the single exchange decision.");
        Need(reveal.All(e=>E<MiuyanPhaseHandRevealedEvent>(g).Any(v=>v.CardId==e.CardId&&v.HolderSeat==e.HolderSeat)),"The generic native public Hand reveal naturally feeds Wang Guan's same-phase reveal ledger.");
        Need(P(g)!.Choices.Count==2&&P(g)!.Choices.All(c=>c.Targets.Count==0&&c.Cards.Count==0),"The whole-roster choice contains exactly exchange or decline, with no per-participant cherry-pick.");
        g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="decline");Play(g);
        Need(reveal.All(e=>V(g,e.HolderSeat).Hand.Any(c=>c.Id==e.CardId))&&g.CardMovements.All(m=>m.Reason.Value!=ExchangeReason)&&E<CaiMaoCompletedEvent>(g).Single().Exchanges==0,"Declining preserves all original entities and makes no fake gain, draw, discard or exchange payment.");
        before=State(g);Need(!g.Submit(new UseProgramSkillCommand(0,"ol:zuolian","whole-exchange",[],[],g.Revision,P(g)!.PromptId)).Accepted&&State(g)==before,"The actual Play quota is spent once even when the global exchange is declined.");_=Cold(g,r);
        {
            (g,r)=Start("empty");Use(g,"dump");Play(g);Begin(g);
            Need(P(g)!.Choices.All(c=>!c.Targets.Contains(0)),"A truly empty Hand cannot be selected, while the other real Hands remain eligible.");Done(g);Play(g);_=Cold(g,r);
        }
    }
    public static void RealExchangeStockPriorityPartialAndExhaustionCold()
    {
        var(g,r)=Start("stock");Use(g,"draw");Play(g);
        var fire=V(g).Hand.First(c=>c.Kind==CardKind.FireSlash).Id;var thunder=V(g).Hand.First(c=>c.Kind==CardKind.ThunderSlash).Id;
        Use(g,"drop",[fire]);Play(g);Use(g,"drop",[thunder]);Play(g);Begin(g);foreach(var seat in new[]{0,1,2,3})Select(g,seat);Done(g);
        var old=Current(g)!.Reveals.ToArray();g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="exchange");Play(g);
        var swaps=E<CaiMaoExchangeFrozenEvent>(g).Select(e=>e.Entity).ToArray();
        Need(swaps.Length==4&&swaps[0].NewCardId==fire&&swaps[0].Supply==CardLocation.DiscardPile&&swaps[1].NewCardId==thunder&&swaps[1].Supply==CardLocation.DiscardPile&&swaps.Skip(2).All(e=>e.NewKind==CardKind.FireSlash&&e.Supply==CardLocation.DrawPile),"The frozen whole exchange uses discard supply first, Fire before Thunder within that source, then real Fire entities from the deck.");
        Need(swaps.All(e=>old.Any(o=>o.CardId==e.OldCardId&&o.HolderSeat==e.HolderSeat)&&V(g,e.HolderSeat).Hand.Any(c=>c.Id==e.NewCardId)&&
            g.CardMovements.Count(m=>m.CardId==e.OldCardId&&m.From==CardLocation.Hand(e.HolderSeat)&&m.To==e.Supply&&m.Reason.Value==ExchangeReason)==1&&
            g.CardMovements.Count(m=>m.CardId==e.NewCardId&&m.From==e.Supply&&m.To==CardLocation.Hand(e.HolderSeat)&&m.Reason.Value==ExchangeReason)==1)&&E<CaiMaoExchangePaidEvent>(g).Single().Count==4,"Both sides of every pair move once in one native batch, with the original shown entity returned to the exact supply zone; nothing is replaced by a draw.");
        _=Cold(g,r);
        {
            (g,r)=Start("partial");for(var i=0;i<4;i++){Use(g,"draw");Play(g);}var only=V(g).Hand.First().Id;Use(g,"drop",[only]);Play(g);
            Begin(g);foreach(var seat in new[]{0,1,2,3})Select(g,seat);Done(g);var revealed=Current(g)!.Reveals.ToArray();g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="exchange");Play(g);
            var paid=E<CaiMaoExchangePaidEvent>(g).Single();var pair=E<CaiMaoExchangeFrozenEvent>(g).Single().Entity;
            Need(paid.Count==1&&pair.NewCardId==only&&pair.Supply==CardLocation.DiscardPile&&pair.HolderSeat==0&&revealed.Skip(1).All(e=>V(g,e.HolderSeat).Hand.Any(c=>c.Id==e.CardId)),"A single remaining actual supply yields one real pair after one whole-roster yes; returning a shown Fire Slash cannot recycle it as a second fresh supply within the same exchange.");
            _=Cold(g,r);
        }
        {
            (g,r)=Start("empty");Begin(g);Select(g,0);Select(g,1);Done(g);var shown=Current(g)!.Reveals.ToArray();Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="exchange");Play(g);
            Need(E<CaiMaoExchangePaidEvent>(g).Single().Count==0&&shown.All(e=>V(g,e.HolderSeat).Hand.Any(c=>c.Id==e.CardId))&&g.CardMovements.All(m=>m.Reason.Value!=ExchangeReason),"An actual deck and discard with no attribute Slash still reveal the chosen Hands but exchange no phantom material.");_=Cold(g,r);
        }
    }
    public static void BeforeDamageHpBudgetInitialChainAndPropagationCold()
    {
        var(g,r)=Start("jing-normal");Use(g,"lower-two");Play(g);Need(V(g).Hp==3,"The actual pre-damage HP is three.");Use(g,"hurt-two");Reach(g,()=>JingOffer(g));Activate(g);
        Need(Current(g)!.FrozenHp==3&&V(g).Hp==3,"The native damage is still pending and the budget uses HP before either point is applied.");
        var shield=Enumerable.Range(1,3).Single(s=>V(g,s).Skills!.Any(k=>k.Id==Shield));var peer=Enumerable.Range(1,3).First(s=>s!=shield);foreach(var seat in new[]{0,peer,shield})Select(g,seat);Need(P(g)!.Choices.All(c=>c.Targets.Count==0),"Three selected roles exhaust this exact before-damage HP budget.");g=Cold(g,r);Done(g);
        Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);Need(V(g).Hp==3&&E<DamageAppliedEvent>(g).Length==0,"A real entered-chain child occurs before the original native damage, with no fabricated separate hit.");g=Cold(g,r);Continue(g);Play(g);
        Need(V(g).Hp==1&&V(g).IsChained&&V(g,peer).IsChained&&!V(g,shield).IsChained&&E<CaiMaoToggleIssuedEvent>(g).Single(e=>e.TargetSeat==shield)is{Requested:true,Actual:false}&&E<DamageAppliedEvent>(g)is[{TargetSeat:0,Amount:2}],"All selected toggles execute once, the native no-chain policy remains effective, and the original two-point damage resumes once after the real children.");_=Cold(g,r);
        {
            (g,r)=Start("chain");Use(g,"chain",targets:[0]);Play(g);Use(g,"chain",targets:[1]);Play(g);var hp1=V(g,1).Hp;var hp2=V(g,2).Hp;
            Use(g,"self-fire");Reach(g,()=>JingOffer(g));Activate(g);Select(g,1);Select(g,2);g=Cold(g,r);Done(g);Play(g);
            var damage=E<DamageAppliedEvent>(g);
            Need(V(g,1).Hp==hp1&&V(g,2).Hp==hp2-1&&damage.Select(e=>e.TargetSeat).Order().SequenceEqual([0,2])&&damage.All(e=>e.Nature==DamageNature.Fire),"Before the first elemental HP loss, removing a prior chain member and adding another changes the genuine initial propagation set; the original source and damage nature remain native.");_=Cold(g,r);
        }
        {
            (g,r)=Start("chain");Use(g,"chain",targets:[0]);Play(g);Use(g,"chain",targets:[1]);Play(g);var hp2=V(g,2).Hp;
            Use(g,"peer-fire",targets:[1]);Reach(g,()=>JingOffer(g));Need(E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==1)&&!E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==0),"The native first hit has occurred, while Cai Mao's real propagated hit still opens its own before-damage window.");
            Activate(g);Select(g,1);Select(g,2);g=Cold(g,r);Done(g);Play(g);
            Need(V(g,2).Hp==hp2&&V(g,2).IsChained&&E<DamageAppliedEvent>(g).Select(e=>e.TargetSeat).SequenceEqual([1,0]),"Newly chained roles during a later propagated hit do not retroactively enter that hit's already-frozen propagation cursor.");_=Cold(g,r);
        }
    }
    public static void PaidExchangeAndToggleChildrenSourceLossDeathCold()
    {
        foreach(var scene in new[]{"loss","death","jing-loss","jing-death"})
        {
            var(g,r)=Start(scene);var jing=scene.StartsWith("jing-",StringComparison.Ordinal);
            if(jing){Use(g,"hurt");Reach(g,()=>JingOffer(g));Activate(g);Select(g,0);Select(g,1);Done(g);}
            else{Begin(g);Select(g,0);Select(g,1);Done(g);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="exchange");}
            Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);var frame=Frame(g);var id=frame.Id;var paid=E<CaiMaoExchangePaidEvent>(g).SingleOrDefault(e=>e.FrameId==id);
            Need(jing?Current(g)!.Cursor==1&&V(g).IsChained:paid is{Count:2}&&g.CardMovements.Count(m=>m.Reason.Value==ExchangeReason)==4,"The actual first toggle or complete two-person exchange is already paid before its native child can remove the source.");g=Cold(g,r);Continue(g);
            Reach(g,()=>E<CaiMaoCompletedEvent>(g).Any(e=>e.FrameId==id));
            Need(E<CaiMaoCompletedEvent>(g).Count(e=>e.FrameId==id)==1&&(jing?E<CaiMaoToggleIssuedEvent>(g).Count(e=>e.FrameId==id)==2&&V(g,1).IsChained:E<CaiMaoExchangePaidEvent>(g).Count(e=>e.FrameId==id)==1&&g.CardMovements.Count(m=>m.Reason.Value==ExchangeReason)==4),"The owning receipt resumes the remaining original effects once after real native children, without repaying any entity or resetting the original target roster.");
            if(scene.EndsWith("death",StringComparison.Ordinal))Need(!V(g).IsAlive&&g.State.Winner==Winner.None,"A true non-Lord death is resolved in the paid child without fabricating a new owner or abandoning the issued native continuation.");
            else Need(E<HpConditionalSkillGrantChangedEvent>(g).Any(e=>e.OwnerSeat==0&&e.SkillId==(jing?"ol:jingzhou":"ol:zuolian")&&!e.Added),"Actual HP loss removes the exact original conditional skill generation, while its paid operation retains the original typed return.");
            _=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame Frame(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.CaiMao is not null);
    private static CaiMaoReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.CaiMao is not null)?.CaiMao;
    private static bool JingOffer(GameEngine g)=>P(g)?.SkillPrompt?.SkillId=="ol:jingzhou"&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s)))+JsonSerializer.Serialize(g.ResolutionStack)+g.Events.Count;
    private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual Cai Mao command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Need(V(g,p.PlayerSeat).IsHuman,"Real AIs advance their own prompts.");var choice=p.Choices.FirstOrDefault(pick);Need(choice is not null,"Missing actual Cai Mao choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,choice!.Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Select(GameEngine g,int seat)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="target"&&c.Targets.SequenceEqual([seat]));
    private static void Done(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="done");
    private static void Begin(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,"ol:zuolian","whole-exchange",[],[],g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,string id,int[]? cards=null,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static string Diagnostic(GameEngine g)=>$"turn={g.State.TurnNumber};winner={g.State.Winner};prompt={P(g)?.Kind}/{P(g)?.PlayerSeat}/{P(g)?.SkillPrompt?.SkillId};stage={Current(g)?.Stage};cursor={Current(g)?.Cursor};hp={V(g).Hp};targets={string.Join(',',Current(g)?.Targets??[])}";
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<180;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Missing native Cai Mao boundary: "+Diagnostic(g));var p=P(g);
            if(p is null||!V(g,p.PlayerSeat).IsHuman)Accept(g,new AdvanceOneStepCommand(g.Revision));
            else if(p.Kind==DecisionKind.PlayCard)Accept(g,new EndPlayPhaseCommand(0,g.Revision,p.PromptId));
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))Continue(g);
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else throw new InvalidOperationException("Unexpected native Cai Mao boundary: "+Diagnostic(g));
        }
        throw new InvalidOperationException("Missing bounded Cai Mao boundary: "+Diagnostic(g));
    }
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves original revealed entities, stock choices, native damage cursor and once-paid continuations.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene.EndsWith("death",StringComparison.Ordinal)?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-cai-mao",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:cai-mao");if(scene=="reveal")CharacterFixtureContent.Register(b,"ol:wang-guan");
            var defs=new List<string>{
                $$$"""
                {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
                 {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                 {"id":"lower","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":3}]},
                 {"id":"lower-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]},
                 {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                 {"id":"hurt-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]},
                 {"id":"self-fire","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"nature":"fire"}]},
                 {"id":"peer-fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
                 {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                 {"id":"drop","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                 {"id":"dump","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]}
                """,
                $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""
            };
            var skills=new List<string>{Driver};var jing=scene.StartsWith("jing-",StringComparison.Ordinal);
            if(scene is"loss"or"jing-loss")
            {defs.Add($$"""{"id":"{{Source}}","revision":1,"hpSkillGrants":[{"id":"full","skillId":"{{(jing?"ol:jingzhou":"ol:zuolian")}}","minimumHp":5}]}""");skills.Add(Source);skills.Add(jing?"ol:zuolian":"ol:jingzhou");}
            else{skills.Add("ol:zuolian");skills.Add("ol:jingzhou");}
            if(scene is"loss"or"death"or"jing-loss"or"jing-death"or"jing-normal")
            {
                var trigger=scene.StartsWith("jing-",StringComparison.Ordinal)?"\"window\":\"characterEnteredChain\",\"subject\":\"owner\"":"\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\""+ExchangeReason+"\"],\"movementOccurrence\":\"perBatch\"";
                var tail=scene=="jing-normal"?"":scene.EndsWith("death",StringComparison.Ordinal)?",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}":",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}";
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-child",{{trigger}},"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}{{tail}}]}]}""");skills.Add(Watch);
            }
            defs.Add($$"""{"id":"{{Shield}}","revision":1,"cardPolicies":[{"id":"native-no-chain","kind":"preventEnteringChain"}]}""");
            var nodes=defs.Select(d=>JsonNode.Parse(d)!).ToArray();var labels=nodes.ToDictionary(d=>d["id"]!.GetValue<string>(),d=>d["id"]!.GetValue<string>()==Watch?(object)new{name="真实连环或移动子窗",description="真实付款和生命子链",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}:new{name=d["id"]!.GetValue<string>(),description="真实蔡瑁小夹具"});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:cm-pick-owner","本人","固定公开选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-100000000d)});b.AddGeneral(new(Owner,"真实蔡瑁","ol-cai-mao","fixture:cm-pick-owner","wei",4,skills,GeneralGender.Male));
            var peers=Enumerable.Range(0,3).Select(i=>"fixture:cm-peer-"+i).ToArray();foreach(var(peer,i)in peers.Select((peer,i)=>(peer,i)))
            {var pick="fixture:cm-pick-"+i;b.AddSkill(new(pick,"其他角色","固定公开选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>1000000d*(3-i))});b.AddGeneral(new(peer,"真实其他角色","supporter",pick,"shu",12,scene=="jing-normal"&&i==2?[Quiet,Shield]:[Quiet]));}
            b.AddCard(new("fixture:cm-fire","火杀","基本牌","真实实体",CardKind.FireSlash));b.AddCard(new("fixture:cm-thunder","雷杀","基本牌","真实实体",CardKind.ThunderSlash));
            var ids=scene=="partial"?new[]{"fixture:cm-fire"}:scene is"empty"or"reveal"or"jing-normal"or"chain"or"jing-loss"or"jing-death"?new[]{"standard:dodge"}:new[]{"fixture:cm-fire","fixture:cm-thunder","standard:dodge"};
            b.AddDeck(new("fixture:cm-deck","固定真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scene=="partial"?64:120).Select(i=>new ContentDeckPhysicalCard(ids[i%ids.Length],new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond}[i/ids.Length%4],i%13+1)).ToArray()});
            b.AddMode(new(Mode,"真实佐练精舟",4,4,scene.EndsWith("death",StringComparison.Ordinal)?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Renegade)]=1}:new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Renegade)]=3},"fixture:cm-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
