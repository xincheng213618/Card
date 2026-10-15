using System.Collections;
using System.Reflection;
using CardGame.Core;

internal static partial class FengLinYuJiChecks
{

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
}
