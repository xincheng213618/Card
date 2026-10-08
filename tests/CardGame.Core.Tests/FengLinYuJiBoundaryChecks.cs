using System.Collections;
using System.Reflection;
using CardGame.Core;

internal static partial class FengLinYuJiChecks
{
    public static void BorrowedSwordFactionProviderDeclaration()
    {
        NativeBorrowedSwordProviderOldRegistry();
        ContentRegistry? declarationRegistry = null;
        foreach(var challenge in new[]{false,true})
        {
            var(g,r)=Start("mixed-borrowed",registry:declarationRegistry);
            declarationRegistry = r;
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var players=(IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",flags)!.GetValue(g)!;
            var physical=players[0].Seat;var face=g.State.Players[0].Hand.First();var cost=new Card(face.Id,face.Kind,face.Suit,face.Rank);
            var peek=typeof(GameEngine).GetMethod("PeekDeclarationConversion",flags)!;
            var selected=typeof(GameEngine).GetField("_selectedResponseConversion",flags)!;
            var hasSelected=typeof(GameEngine).GetField("_hasSelectedResponseConversionChoice",flags)!;
            var oldSelected=selected.GetValue(g);var oldFlag=hasSelected.GetValue(g);
            hasSelected.SetValue(g,true);selected.SetValue(g,null);
            Require(peek.Invoke(g,[players[0],cost,CardKind.FireSlash,true,null]) is null,"An explicitly native-null published source cannot auto-declare a final Fan FireSlash.");
            var wusheng=r.GetSkill("classic:wusheng").Program!.ViewAs.First();
            selected.SetValue(g,new CardConversionSource("classic:wusheng",wusheng.Id,physical,"fixture-precise-source"));
            Require(peek.Invoke(g,[players[0],cost,CardKind.Slash,true,null]) is null,"An explicit other conversion is preserved instead of falling back to Guhuo.");
            selected.SetValue(g,oldSelected);hasSelected.SetValue(g,oldFlag);
            Driver(g,"equip-target",[1]);
            Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:yu-driver");var equipment=P(g)!;
            Accept(g,new AnswerPromptCommand(0,equipment.PromptId,equipment.Choices.First(c=>c.Cards.Count==1).Id,g.Revision));Settle(g);
            var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.BorrowedSword&&a.ConversionSource is null&&a.TargetSeats.First()==1);
            Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId));
            for(var step=0;step<100;step++)
            {
                var prompt=P(g);
                if(prompt is {Kind:DecisionKind.RespondSlash,PlayerSeat:0} && prompt.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="faction-slash-slash"))break;
                if(prompt is {Kind:DecisionKind.Nullification,PlayerSeat:0})
                    Accept(g,new AnswerPromptCommand(0,prompt.PromptId,prompt.Choices.Single(c=>c.Cards.Count==0).Id,g.Revision));
                else Accept(g,new AdvanceOneStepCommand(g.Revision));
            }
            var provider=P(g)!;Require(provider.Kind==DecisionKind.RespondSlash&&provider.PlayerSeat==0,"Real borrowed owner requests actual faction provider.");
            var choice=provider.Choices.First(c=>c.Parameters.GetValueOrDefault("conversion-skill-id")=="classic:guhuo");
            Accept(g,new AnswerPromptCommand(0,provider.PromptId,choice.Id,g.Revision));
            var declaration=g.ResolutionStack.OfType<CardDeclarationFrame>().Single();var id=declaration.Payment.Cost.CardId;
            Require(declaration.Return.Purpose==CardDeclarationPurpose.BorrowedSwordProvidedSlash&&declaration.Return.ActorSeat==1&&declaration.OwnerSeat==0&&
                !g.Events.Select(e=>e.Payload).OfType<FactionSlashResolvedEvent>().Any(e=>e.Succeeded),"Borrowed provider pays and suspends before successful faction fact.");Replay(g,r);
            if(challenge)Answer(g,"challenge");else while(g.ResolutionStack.LastOrDefault() is CardDeclarationChallengeFrame)Answer(g,"pass");
            if(challenge)
            {
                Settle(g);Require(!g.Events.Select(e=>e.Payload).OfType<FactionSlashResolvedEvent>().Any(e=>e.Succeeded)&&g.Events.Select(e=>e.Payload).OfType<BorrowedSwordResolvedEvent>().Any(e=>!e.UsedSlash),"Fake provider advances actual cursor and transfers weapon on exhaustion.");
            }
            else Require(g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.ActorSeat==1&&e.Action.ProviderSeat==0&&e.Action.RequesterSeat==1&&e.Action.PhysicalCards.Any(c=>c.CardId==id)),"Unchallenged provided real Slash retains exact actor/provider/requester.");
            Require(g.CardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1&&
                g.Events.Select(e=>e.Payload).OfType<SkillUsageConsumedEvent>().Count(e=>e.SkillOwnerSeat==0&&e.SkillId=="classic:guhuo")==1,"Borrowed provider consumes one payment and named quota.");Replay(g,r);Conserve(g);
        }
    }

    private static void NativeBorrowedSwordProviderOldRegistry()
    {
        var(g,r)=Start("mixed-borrowed",declarations:false);
        Require(!r.Skills.Values.Any(s=>s.Program?.ViewAs.Any(v=>v.DeclarationValidation is not null)==true),"Native borrowed fixture has no declaration capability.");
        for(var seat=1;seat<4;seat++)
            while(g.State.Players[seat].Hand.Any(c=>c.Kind==CardKind.Slash))
            {
                Driver(g,"strip-slash",[seat]);Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:yu-driver");var choice=P(g)!;
                Accept(g,new AnswerPromptCommand(0,choice.PromptId,choice.Choices.First(c=>c.Cards.Count==1).Id,g.Revision));Settle(g);
            }
        Driver(g,"equip-target",[1]);Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:yu-driver");var equipment=P(g)!;
        Accept(g,new AnswerPromptCommand(0,equipment.PromptId,equipment.Choices.First(c=>c.Cards.Count==1).Id,g.Revision));Settle(g);
        var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.BorrowedSword&&a.ConversionSource is null&&a.TargetSeats.First()==1);
        Accept(g,new PlayCardCommand(0,action.CardId!.Value,action.TargetSeats,g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.Kind==DecisionKind.RespondSlash&&p.PlayerSeat==0&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="faction-slash-slash"));Replay(g,r);
        var prompt=P(g)!;var native=prompt.Choices.First(c=>c.Cards.Count==1&&c.Parameters.GetValueOrDefault("conversion-skill-id") is null);var id=native.Cards.Single();
        Accept(g,new AnswerPromptCommand(0,prompt.PromptId,native.Id,g.Revision));Replay(g,r);
        Require(!g.ResolutionStack.OfType<CardDeclarationFrame>().Any()&&g.CardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1&&
            g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.ActorSeat==1&&e.Action.ProviderSeat==0&&e.Action.RequesterSeat==1&&e.Action.PhysicalCards.Any(c=>c.CardId==id)),"Old native provider preserves reconstructed continuation identity and pays once.");Conserve(g);
    }

    public static void NativeVirtualDuelOldRegistry()
    {
        var(g,r)=Start("standard:slash",declarations:false);
        Require(!r.Skills.Values.Any(s=>s.Program?.ViewAs.Any(v=>v.DeclarationValidation is not null)==true),"Old registry has no declaration capability.");
        for(var i=0;i<3;i++) { Driver(g,"poke",[1]);Settle(g); }
        Driver(g,"duel",[1,0]);Replay(g,r);
        var active=typeof(GameEngine).GetProperty("ActiveCardAttack",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(g)!;
        var validate=typeof(GameEngine).GetMethod("IsActiveAttackCardConsistent",BindingFlags.NonPublic|BindingFlags.Instance)!;
        Require(!(bool)validate.Invoke(g,[active,new[]{new Card(99999,CardKind.Slash,Suit.Spade,1)}])!,"Unknown extra Processing entity is rejected.");
        for(var attempt=0;attempt<2;attempt++)
        {
            Reach(g,p=>p.Kind==DecisionKind.RespondSlash&&p.PlayerSeat==0);
            var p=P(g)!;var c=attempt==0?p.Choices.First(c=>c.Cards.Count==1):p.Choices.Single(c=>c.Parameters.GetValueOrDefault("response")=="take-damage");
            Accept(g,new AnswerPromptCommand(0,p.PromptId,c.Id,g.Revision));Replay(g,r);
        }
        Settle(g);Replay(g,r);
        Require(g.Events.Select(e=>e.Payload).OfType<DuelResponseEvent>().Count(e=>e.UsedSlash)>=2&&
            g.Events.Select(e=>e.Payload).OfType<DuelResponseEvent>().Any(e=>e.ResponderSeat==0&&!e.UsedSlash),"Native Slash exchanges responders; failed response returns to original damage parent: "+string.Join(";",g.Events.Select(e=>e.Payload).OfType<DuelResponseEvent>().Select(e=>$"{e.ResponderSeat}:{e.UsedSlash}")));
        Conserve(g);
    }

    public static void CostChildAtomicPrivacyAndRestore()
    {
        var(g,r)=Start("standard:dodge",costChild:true);var before=g.State.Players[0].HandCount;
        var observed=new List<GameLogEntry>();g.LogAdded+=observed.Add;
        Use(g,CardKind.DrawTwo);
        Require(g.ResolutionStack.OfType<CardDeclarationFrame>().Single().Stage==CardDeclarationStage.Paying&&
            g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single().ResumeDeclarationFrameId is not null,"Cost has a real owning movement child before challenge.");
        Replay(g,r);var p=P(g)!;
        var checkpoint=GameCheckpointJson.Serialize(g.CreateCheckpoint());var revision=g.Revision;
        var rejected=g.Submit(new AnswerPromptCommand(0,p.PromptId,new ChoiceId("declaration.challenge"),revision));
        Require(!rejected.Accepted&&g.Revision==revision&&GameCheckpointJson.Serialize(g.CreateCheckpoint())==checkpoint,"Foreign child choice is rejected atomically.");
        var activate=p.Choices.Single(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Accept(g,new AnswerPromptCommand(0,p.PromptId,activate.Id,g.Revision));
        Reach(g,p=>g.ResolutionStack.LastOrDefault() is CardDeclarationChallengeFrame);Privacy(g,CardKind.Dodge);Replay(g,r);
        var paid=g.ResolutionStack.OfType<CardDeclarationFrame>().Single().Payment;
        var unrelated=g.CreateSnapshot(0).Players[0].Hand!.First(card=>card.Id!=paid.Cost.CardId);
        var describe=typeof(GameEngine).GetMethod("PublicDeclarationDescription",BindingFlags.NonPublic|BindingFlags.Instance)!;
        Require((string)describe.Invoke(g,[new Card(unrelated.Id,unrelated.Kind,unrelated.Suit,unrelated.Rank),"使用【闪】"])! == "使用【闪】","Another same-name entity retains its own public description.");
        var declarations=g.CreateSnapshot(1).CardDeclarations!;
        Require(declarations is IList<CardDeclarationSnapshot> outer&&outer.IsReadOnly&&declarations[0].TargetSeats is IList<int> inner&&inner.IsReadOnly,"Prepared declaration outer and nested collections are immutable.");
        var fact=g.Events.Select(e=>e.Payload).OfType<CardDeclarationCommittedEvent>().Single();
        Require(fact.TargetSeats is IList<int> target&&target.IsReadOnly,"Committed declaration collection is frozen.");
        while(g.ResolutionStack.LastOrDefault() is CardDeclarationChallengeFrame)Answer(g,"pass");
        Settle(g);Require(g.State.Players[0].HandCount==before+2,"Child draw completes once before actual DrawTwo; one payment is retained.");
        Require(observed.Where(l=>l.Type is "CardUsed" or "CardResponded" or "SkillTriggered").All(l=>!l.Message.Contains("【闪】")),"Observer BattleLog never receives unrevealed original cost name during acceptance and real executor continuation.");
        Replay(g,r);Conserve(g);
    }
}
