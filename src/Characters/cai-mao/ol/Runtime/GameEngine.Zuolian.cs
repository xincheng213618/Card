namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void PayCaiMaoExchange(ProgramSkillFrame f)
    {
        var r=f.CaiMao!;var used=new HashSet<int>();var exchanges=new List<CaiMaoExchange>();
        foreach(var old in r.Reveals)
        {
            if(!_players[old.HolderSeat].IsAlive||CurrentHandEntry(old.HolderSeat,old.CardId)!=old.HandEntry)continue;
            Card? card=null;CardLocation from=default;
            foreach(var zone in new[]{CardLocation.DiscardPile,CardLocation.DrawPile})
            {
                foreach(var kind in new[]{CardKind.FireSlash,CardKind.ThunderSlash})
                {
                    var eligible=_cardZones.CardsAt(zone).Where(c=>c.Kind==kind&&!used.Contains(c.Id)).OrderBy(c=>c.Id).ToArray();
                    if(eligible.Length==0)continue;card=eligible[_random.Next(eligible.Length)];from=zone;break;
                }
                if(card is not null)break;
            }
            if(card is null)break;used.Add(card.Id);
            exchanges.Add(new(old.HolderSeat,old.CardId,old.PrintedKind,old.HandEntry,card.Id,card.Kind,from));
        }
        ReplaceRuntimeTop(f=f with{CaiMao=r=r with{Stage=CaiMaoStage.ExchangeChildren,Exchanges=exchanges.ToArray(),Before=_movementSequence,After=_movementSequence}});
        for(var i=0;i<exchanges.Count;i++)AdvanceEventRulesAndQueueFact(new CaiMaoExchangeFrozenEvent(f.Id,i,exchanges[i]));
        var replacements=exchanges.Where(e=>e.Supply==CardLocation.DrawPile).ToDictionary(e=>e.NewCardId,e=>e.OldCardId);
        var desiredDeck=_cardZones.CardsAt(CardLocation.DrawPile).Select(c=>replacements.GetValueOrDefault(c.Id,c.Id)).ToArray();
        if(exchanges.Count>0)
        {
            var map=exchanges.SelectMany(e=>new[]{(Id:e.OldCardId,To:e.Supply),(Id:e.NewCardId,To:CardLocation.Hand(e.HolderSeat))}).ToDictionary(e=>e.Id,e=>e.To);
            MoveProgramCardsFromMultipleSources(map.Keys.ToArray(),CardLocation.Processing,new(CaiMaoExchangeReason),(batch,moves)=>
            {
                var current=GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(current with{CaiMao=current.CaiMao! with{After=_movementSequence}});
                AdvanceEventRulesAndQueueFact(new CaiMaoExchangePaidEvent(f.Id,batch,r.Before,_movementSequence,exchanges.Count));
            },(card,_)=>map[card.Id]);
            if(replacements.Count>0)_cardZones.ReorderDrawPileTop(_cardZones.CardsAt(CardLocation.DrawPile).Reverse().Select(c=>c.Id).ToArray(),desiredDeck.Reverse().ToArray(),[]);
        }
        else AdvanceEventRulesAndQueueFact(new CaiMaoExchangePaidEvent(f.Id,0,r.Before,r.Before,0));
        ContinueCaiMao(f.Id);
    }
}
