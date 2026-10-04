using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
namespace CardGame.Core.Tests;

internal static class BoundaryMaDaiChecks
{
    private const string Mode="identity:classic-boundary-ma-dai", Driver="fixture:md-driver", Qianxi="boundary:qianxi";

    public static void ShownColorBansHandMaterialsButKeepsEquipmentResponses()
    {
        var (g,r)=Create(equipment:true);var shown=g.State.Players[0].Hand.First().Id;
        g=Show(g,r,shown);Play(g);var policy=OwnerPolicies(g).Single();var target=Peer(g);
        Require(policy.FrozenSuit==Suit.Spade &&policy.AffectedSeats.Order().SequenceEqual(new[]{1,2,3}) &&
            policy.RestrictionSequences.Count==3 &&policy.RestrictionSequences.All(seq=>Facts<HandCardColorRestrictionGrantedEvent>(g).Any(e=>
                e.Restriction.GrantSequence==seq &&e.Restriction.RequiresChromaticSuit &&!e.Restriction.IsRed)),
            "Actual Mashu distance and the publicly shown black entity freeze all three current distance-one hand bans.");
        var equipment=g.State.Players[0].Hand.First(c=>c.Id!=shown).Id;
        Use(g,"place",[equipment],[target]);Play(g);
        var action=g.GetHumanLegalActions().Single(a=>a.Kind==LegalActionKind.Slash &&a.CardId==shown &&a.TargetSeats.SequenceEqual([target]) &&
            a.ConversionSource?.SkillId=="fixture:md-equipment-use");
        Accept(g,new PlayCardCommand(0,shown,[target],g.Revision,P(g)!.PromptId,CardKind.Slash){ConversionSource=action.ConversionSource});
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:md-committed");
        var use=BenefitUse(g);var useId=use.Id;
        Require(use.ShownEntityBenefits is [{ Policy.CardId:var id }] &&id==shown,"The actual converted physical Use owns one displayed-entity benefit before payment children.");
        g=Cold(g,r);Continue(g);
        Reach(g,p=>p.Kind==DecisionKind.RespondDodge &&p.PlayerSeat==target);
        Require(P(g)!.Choices.Any(c=>c.Cards.SequenceEqual([equipment])) &&
            P(g)!.Choices.SelectMany(c=>c.Cards).All(id=>g.CreateCardZoneDiagnostics().Single(z=>z.CardId==id).Location==CardLocation.Equipment(target)),
            "The target cannot use any black Hand material, while its real equipped SilverLion remains a legal converted Dodge.");
        g=Cold(g,r);Accept(g,new AdvanceOneStepCommand(g.Revision));
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:md-hp");
        Require(g.ResolutionStack.OfType<CardUseFrame>().Any(f=>f.Id==useId &&f.ShownEntityBenefits is {Count:1}) &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any() &&
            !Facts<CardUseFinishedEvent>(g).Any(e=>e.ResolutionId==useId),
            "The already-issued shown benefit stays on its genuine Use while an equipment-response recovery child pauses.");
        Reject(g);g=Cold(g,r);Continue(g);Play(g);
        Require(Facts<CardRespondedEvent>(g).Any(e=>e.ResponderSeat==target &&e.CardId==equipment &&e.EffectiveCardKind==CardKind.Dodge) &&
            !Facts<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0 &&e.TargetSeat==target) &&
            Facts<ShownEntityUseBenefitIssuedEvent>(g).Count(e=>e.CardUseFrameId==useId)==1 &&
            Facts<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==useId)==1 &&
            g.CardMovements.Count(m=>m.CardId==shown &&m.To==CardLocation.Processing &&m.Reason==CardMoveReasons.Use)==1 &&
            g.CardMovements.Count(m=>m.CardId==equipment &&m.From==CardLocation.Equipment(target) &&m.To==CardLocation.Processing)==1,
            "A real equipment Dodge cancels damage and returns the cold paid chain with both entity payments and Use completion exactly once.");
        _=Cold(g,r);
    }

    public static void ShownEntityDamageFreezesFullMultiMaterialUseAndColdReturn()
    {
        var (g,r)=Create();var shown=g.State.Players[0].Hand.First().Id;g=Show(g,r,shown);Play(g);
        var other=g.State.Players[0].Hand.First(c=>c.Id!=shown).Id;var target=Peer(g);
        Accept(g,new UseProgramSkillCommand(0,"fixture:md-multi","two-as-fire",[shown,other],[target],g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:md-committed");
        var use=BenefitUse(g);var useId=use.Id;var b=use.ShownEntityBenefits!.Single();
        var input=b.PhysicalMaterials.ToList();var clone=b with{PhysicalMaterials=input};input.Clear();
        Require(use.Action is {Type:CardActionType.Use,EffectiveKind:CardKind.FireSlash,PhysicalCards.Count:2} action &&
            action.PhysicalCards.Select(c=>c.CardId).SequenceEqual([shown,other]) &&b.PhysicalMaterials.SequenceEqual(action.PhysicalCards) &&
            clone.PhysicalMaterials.Count==2 &&clone.PhysicalMaterials is System.Collections.IList{IsReadOnly:true},
            "The owning Use freezes the complete two-material conversion, including the undisplayed private material, and explicit init detaches caller lists.");
        Require(Facts<ShownEntityUseBenefitIssuedEvent>(g).Single(e=>e.CardUseFrameId==useId).Policy.CardId==shown,
            "The new public benefit fact publishes the already shown entity, without a list of undisplayed material IDs.");
        Reject(g);g=Cold(g,r);Continue(g);
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:md-damage");
        Require(Facts<DamageAppliedEvent>(g).Single(e=>e.SourceSeat==0 &&e.TargetSeat==target) is {Amount:2,Nature:DamageNature.Fire} &&
            Facts<ProgramCardDamageModifiedEvent>(g).Count(e=>e.ResolutionId==useId &&e.Source.SkillId==Qianxi &&e.BaseAmount==1 &&e.ModifiedAmount==2)==1 &&
            g.ResolutionStack.OfType<CardUseFrame>().Any(f=>f.Id==useId &&f.ShownEntityBenefits!.Count==1),
            "Actual fire damage receives one +1 from the exact shown material, with its ordered damage child still below the unfinished owning Use.");
        g=Cold(g,r);Continue(g);Play(g);
        Require(Facts<ShownEntityUseBenefitIssuedEvent>(g).Count(e=>e.CardUseFrameId==useId)==1 &&
            Facts<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==useId)==1 &&
            new[]{shown,other}.All(id=>g.CardMovements.Count(m=>m.CardId==id &&m.To==CardLocation.Processing &&m.Reason==CardMoveReasons.Use)==1) &&
            !g.ResolutionStack.Any(f=>f.Id==useId),
            "Continued cold restoration cannot repay either material, duplicate the per-Use bonus or complete the Use twice.");
        _=Cold(g,r);
    }

    public static void ExtraPlayFreezesIndependentCohortsAndExpiresOnActualTurnEnd()
    {
        var (g,r)=Create(extraPlay:true);var turn=g.State.TurnNumber;var first=g.State.Players[0].Hand.First().Id;
        g=Show(g,r,first);Play(g);var firstPolicy=OwnerPolicies(g).Single();Use(g,"further");Play(g);
        Require(Facts<TurnRuleModifierGrantedEvent>(g).Any(e=>e.Modifier.Source.OwnerSeat==0 &&e.Modifier.Query==SkillRuleQuery.OutgoingDistance &&e.Modifier.Amount==2) &&
            firstPolicy.AffectedSeats.Count==3 &&!Facts<TurnCardUseEffectsExpiredEvent>(g).Any(e=>e.GrantSequences.Any(firstPolicy.RestrictionSequences.Contains)),
            "A real distance modifier changes later combat eligibility while the already-issued whole-turn cohort remains frozen.");
        End(g);ReachQianxi(g);var second=g.State.Players[0].Hand.First(c=>c.Id!=first).Id;
        g=Show(g,r,second);Play(g);var policies=OwnerPolicies(g).ToArray();var next=policies.Last();
        Require(policies.Length==2 &&next.TurnNumber==turn &&firstPolicy.TurnNumber==turn &&
            next.ActualPlayStartingFrameId!=firstPolicy.ActualPlayStartingFrameId &&next.CardId==second &&next.AffectedSeats.Count==0 &&
            !Facts<TurnCardUseEffectsExpiredEvent>(g).Any(e=>e.GrantSequences.Any(firstPolicy.RestrictionSequences.Contains)),
            "The inserted Play and normal Play are separate genuine starting parents; the second distance-empty issue does not revoke the first same-turn ban.");
        g=Cold(g,r);End(g);
        Until(g,()=>Facts<TurnCardUseEffectsExpiredEvent>(g).Any(e=>e.TurnNumber==turn &&e.TurnSeat==0 &&
            firstPolicy.RestrictionSequences.All(e.GrantSequences.Contains)));
        Require(Facts<TurnEndedEvent>(g).Any(e=>e.ActorSeat==0 &&e.TurnNumber==turn) &&
            Facts<ShownEntityTurnPolicyGrantedEvent>(g).Count(e=>e.Policy.ActualTurnOwnerSeat==0 &&e.Policy.TurnNumber==turn)==2,
            "Both actual phases issue once, and the genuine turn boundary expires the old hand bans rather than a Slash-debit or temporary child boundary.");
        _=Cold(g,r);
    }

    public static void NativeRevealAndForeignProviderUseKeepPublicPoliciesDetached()
    {
        var (ai,ar)=Create(native:true);Play(ai);
        var native=Facts<ShownEntityTurnPolicyGrantedEvent>(ai).First(e=>e.Policy.Source.OwnerSeat!=0).Policy;
        var mutable=native.AffectedSeats.ToList();var frozen=native with{AffectedSeats=mutable};mutable.Clear();
        Require(frozen.AffectedSeats.SequenceEqual(native.AffectedSeats) &&frozen.AffectedSeats is System.Collections.IList{IsReadOnly:true} &&
            Facts<ProgramCardsRevealedEvent>(ai).Any(e=>e.FrameId==native.ProgramFrameId &&e.OwnerSeat==native.Source.OwnerSeat &&e.Cards.Single().Id==native.CardId),
            "Native AI uses the shared real own-Play reveal and public policy; explicit init keeps the issued cohort detached.");
        _=Cold(ai,ar);

        var (g,r)=Create(provider:true);var shown=g.State.Players[0].Hand.First().Id;g=Show(g,r,shown);Play(g);
        var provider=Peer(g);var target=Enumerable.Range(1,3).First(s=>s!=provider);
        // The fixture's visible source-suit rewrite lets its real red supplied
        // Slash survive the frozen black Hand ban; no hidden ID is injected.
        foreach(var seat in Enumerable.Range(1,3))
        {
            for(var n=0;g.State.Players[seat].HandCount>0 &&n<12;n++)
            {
                Use(g,"strip",targets:[seat]);
                Reach(g,p=>p.SkillPrompt?.SkillId==Driver &&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-and-move-owned-card"));
                Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="select-and-move-owned-card");Play(g);
            }
            Require(g.State.Players[seat].HandCount==0,"Real opaque one-card discard commands empty only actual provider candidate hands.");
        }
        Use(g,"give",[shown],[provider]);Play(g);
        Require(g.CreateCardZoneDiagnostics().Single(c=>c.CardId==shown).Location==CardLocation.Hand(provider),"The shown entity reaches the different provider through a real gift movement.");
        Use(g,"request",targets:[target]);Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:md-committed");
        var use=BenefitUse(g);var useId=use.Id;
        Require(use.Action is {Type:CardActionType.Use,ActorSeat:0} action &&action.ProviderSeat==provider &&
            action.PhysicalCards is [{CardId:var id,From:var from}] &&id==shown &&from==CardLocation.Hand(provider) &&
            use.SourceSeat==0 &&use.ShownEntityBenefits!.Single().Policy.CardId==shown,
            "A genuine owner Use provided by another character retains that real provider and complete material origin, while earning the displayed entity benefit.");
        g=Cold(g,r);Continue(g);Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:md-damage");
        Require(Facts<DamageAppliedEvent>(g).Single(e=>e.SourceSeat==0 &&e.TargetSeat==target).Amount==2,
            "The current text follows the true user and damage source, without imposing an invented same-provider restriction.");
        g=Cold(g,r);Continue(g);Play(g);
        Require(Facts<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==useId)==1 &&
            Facts<ShownEntityUseBenefitIssuedEvent>(g).Count(e=>e.CardUseFrameId==useId)==1 &&
            g.CardMovements.Count(m=>m.CardId==shown &&m.From==CardLocation.Hand(provider) &&m.To==CardLocation.Processing)==1,
            "A cold faction-provided physical Slash pays the provider's actual entity once and returns its original typed parent once.");
        _=Cold(g,r);
    }

    private static IEnumerable<T> Facts<T>(GameEngine g) where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static IEnumerable<ShownEntityTurnPolicy> OwnerPolicies(GameEngine g)=>Facts<ShownEntityTurnPolicyGrantedEvent>(g).Select(e=>e.Policy).Where(p=>p.Source.OwnerSeat==0);
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static int Peer(GameEngine g)=>g.State.Players.First(p=>p.Seat!=0 &&p.Role!=Role.Lord).Seat;
    private static CardUseFrame BenefitUse(GameEngine g)=>g.ResolutionStack.OfType<CardUseFrame>().Last(f=>f.ShownEntityBenefits is {Count:>0});
    private static void ReachQianxi(GameEngine g)=>Reach(g,p=>p.SkillPrompt?.SkillId==Qianxi &&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
    private static GameEngine Show(GameEngine g,ContentRegistry r,int id)
    {
        ReachQianxi(g);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards"));
        Require(P(g) is {IsPrivate:true,PlayerSeat:0} &&Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null ||
            g.CreateSnapshot(s).PendingDecision is {Choices.Count:0,ValidCardIds.Count:0}),
            "Before the genuine reveal, other prepared player views contain no private Hand selection IDs.");
        Reject(g);g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([id]));
        Until(g,()=>OwnerPolicies(g).Any(p=>p.CardId==id));return g;
    }
    private static void Use(GameEngine g,string binding,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>
        Accept(g,new UseProgramSkillCommand(0,Driver,binding,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,p=>p is {Kind:DecisionKind.PlayCard,PlayerSeat:0});
    private static void Continue(GameEngine g)
    {if(P(g)!.PlayerSeat==0)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose)
    {var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> done)
    {for(var i=0;i<150;i++){if(P(g) is { } p &&done(p))return;Step(g);}throw new InvalidOperationException("Boundary MaDai actual prompt missing: "+JsonSerializer.Serialize(P(g)));}
    private static void Until(GameEngine g,Func<bool> done)
    {for(var i=0;i<150;i++){if(done())return;Step(g);}throw new InvalidOperationException("Boundary MaDai actual event boundary missing.");}
    private static void Step(GameEngine g)
    {
        var p=P(g);
        if(p is {PlayerSeat:0} &&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))Continue(g);
        else if(p is {PlayerSeat:0,Kind:DecisionKind.DiscardCards})Accept(g,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));
        else if(p is {PlayerSeat:0} &&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        else if(p is {PlayerSeat:0} &&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="take-damage"))Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="take-damage");
        else if(p is {PlayerSeat:0,Kind:DecisionKind.PlayCard})throw new InvalidOperationException("An exact MaDai boundary was passed into normal Play.");
        else Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g,GameCommand c)
    {var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected actual MaDai command.");}
    private static void Reject(GameEngine g)
    {var before=State(g);var p=P(g)!;Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new("not-published"),g.Revision)).Accepted &&State(g)==before,"Unpublished selection leaves all four prepared views, true owning frames and accepted commands unchanged.");}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static GameEngine Cold(GameEngine g,ContentRegistry r)
    {var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(restored),"Four prepared private/public views, accepted commands, actual movements, policy and full owning material receipts cold restore identically.");return restored;}
    private static void Require(bool okay,string why){if(!okay)throw new InvalidOperationException(why);}

    private static (GameEngine,ContentRegistry) Create(bool equipment=false,bool extraPlay=false,bool native=false,bool provider=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(equipment,extraPlay,native,provider));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Renegade,ModeId=Mode,
            UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=5},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p is {Kind:DecisionKind.SelectGeneral,PlayerSeat:0});
        Accept(g,new SelectGeneralCommand(0,"fixture:md-owner",g.Revision,P(g)!.PromptId));
        if(native)Play(g);else ReachQianxi(g);return(g,r);
    }
    private sealed class Fixture(bool equipment,bool extraPlay,bool native,bool provider):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-boundary-ma-dai",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var data=JsonNode.Parse(FixtureRules.Replace("$SCHEMA$",SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)))!;
            var presentations=new Dictionary<string,object>();
            foreach(var node in data["skills"]!.AsArray())
            {
                var id=node!["id"]!.GetValue<string>();
                presentations[id]=id is "fixture:md-committed" or "fixture:md-hp" or "fixture:md-damage"
                    ?(object)new{name=id,description="真实共享能力夹具",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}
                    :new{name=id,description="真实共享能力夹具"};
            }
            var programs=SkillProgramCatalog.Load(data.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=3,skills=presentations})).Programs;
            foreach(var (id,program) in programs)b.AddSkill(new(id,id,"真实共享能力夹具"){Program=program,Tags=id is "fixture:md-quiet" or "fixture:md-extra"?SkillTag.Locked:SkillTag.None});
            b.AddSkill(new("fixture:md-selection","固定角色","公开选将偏好"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>100000d)});
            var owner=new List<string>{"classic:mashu","fixture:md-multi","fixture:md-equipment-use","fixture:md-committed"};
            if(!native)owner.Add(Qianxi);if(extraPlay)owner.Add("fixture:md-extra");
            b.AddGeneral(new("fixture:md-owner","界马岱机制","supporter",Driver,"shu",8,owner){InitialHp=8});
            for(var i=1;i<4;i++)
            {
                var skills=new List<string>{"fixture:md-hp","fixture:md-damage"};
                if(equipment)skills.Add("fixture:md-equipment-response");
                if(provider)skills.Add("classic:hongyan");
                if(native)skills.AddRange([Qianxi,"classic:mashu","fixture:md-committed"]);else skills.Add("fixture:md-quiet");
                b.AddGeneral(new($"fixture:md-peer-{i}","固定其他角色","supporter","fixture:md-selection","shu",8,skills){InitialHp=equipment?3:8});
            }
            b.AddDeck(new("fixture:md-deck","固定真实实体",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard(equipment?"classic:silver-lion":"standard:slash",Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new(Mode,"界马岱真实命令",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Renegade)]=3},"fixture:md-deck",GeneralCandidateCount:4,
                GeneralPoolIds:["fixture:md-owner","fixture:md-peer-1","fixture:md-peer-2","fixture:md-peer-3"]));
        }
    }
    private const string FixtureRules="""
    {"schemaVersion":$SCHEMA$,"skills":[
     {"id":"fixture:md-driver","revision":1,"cardPolicies":[{"id":"real-provided-slash","kind":"factionResponseRequest","requiredCardKinds":["slash"],"factionId":"shu"}],"activations":[
      {"id":"place","usesPerTurn":null,"minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"actual-equipment"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"actual-equipment"}]},
      {"id":"further","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"outgoingDistance","ruleOperation":"add","amount":2}]},
      {"id":"strip","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile"}]},
      {"id":"give","usesPerTurn":null,"minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]},
      {"id":"request","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingSlashable","effects":[{"op":"requestFactionCard","target":"selectedTarget","providerFactionId":"shu","requiredKind":"slash"}]}]},
     {"id":"fixture:md-equipment-use","revision":1,"viewAs":[{"id":"actual-equipment-slash","inputKinds":["silverLion"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"extendedUse":true,"sourceZones":["hand","equipment"]}]},
     {"id":"fixture:md-equipment-response","revision":1,"viewAs":[{"id":"actual-equipment-dodge","inputKinds":["silverLion"],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true,"extendedUse":true,"sourceZones":["hand","equipment"]}]},
     {"id":"fixture:md-multi","revision":1,"viewAs":[{"id":"two-as-fire","inputKinds":["slash"],"inputSuits":[],"outputKind":"fireSlash","forPlay":true,"forResponse":false,"inputCount":2,"extendedUse":true,"sourceZones":["hand"]}],"activations":[{"id":"two-as-fire","usesPerTurn":null,"minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-as-fire","outputKind":"fireSlash"}]}]},
     {"id":"fixture:md-quiet","revision":1,"triggers":[{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},{"id":"quiet-discard","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]},
     {"id":"fixture:md-committed","revision":1,"triggers":[{"id":"actual-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash","fireSlash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"committed-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:md-hp","revision":1,"triggers":[{"id":"actual-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:md-damage","revision":1,"triggers":[{"id":"actual-damage","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"damage-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:md-extra","revision":1,"triggers":[{"id":"actual-extra-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}
    ]}
    """;
}
