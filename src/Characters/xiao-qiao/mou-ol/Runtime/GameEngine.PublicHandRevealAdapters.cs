namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramViewContributor(ProgramViewContribution.InvariantProcessing,34000)]
    private IReadOnlyList<Card> PublicRevealInvariantProcessing(IReadOnlyList<Card> cards)
    {
        var suspended=_resolutionStack.Where(f=>MouXiaoQiaoOl.PublicRevealSuspends(f.Id)).SelectMany(f=>f switch
        {
            CardUseFrame u=>u.Action?.PhysicalCards.Select(c=>c.CardId)??u.PhysicalCardIds??[],
            ProgramSkillFrame p=>p.CardAttack?.PhysicalCardIds??[],
            JudgmentFrame j=>j.CardAttack?.PhysicalCardIds??[],
            _=>Enumerable.Empty<int>()
        }).ToHashSet();
        return suspended.Count==0?cards:Array.AsReadOnly(cards.Where(c=>!suspended.Contains(c.Id)).ToArray());
    }
    // These adapters return to the producer's existing native stage. They do not
    // repeat its reveal, accepted choice, payment, or first-suit claim.
    [PublicHandRevealReturn("qixi-guessed-hand")]
    private void ReturnQixiPublicHand(ProgramSkillFrame f)
    {
        if (f.MouGanNing is not { Stage: MouGanNingStage.Discard })
            throw new InvalidOperationException("Qixi lost its accepted correct-guess reveal.");
        PublishMouGn(f);
    }
    [PublicHandRevealReturn("guolun-foreign-hand")]
    private void ReturnGuolunPublicHand(ProgramSkillFrame f)
    {
        if (f.SpPangTong is not { Stage: "exchange", RevealedCard: not null })
            throw new InvalidOperationException("Guolun lost its selected original Hand entity.");
        PublishSpPangTong(f);
    }
    [PublicHandRevealReturn("phase-name-hand")]
    private void ReturnPhasePredictionPublicHand(ProgramSkillFrame f)
    {
        if (f.PhaseNamePrediction is not { Stage: PhaseNamePredictionStage.Guessing, RevealedCardId: not null })
            throw new InvalidOperationException("A name prediction lost its accepted blind reveal.");
        PublishPredictionChoice(f);
    }
    [PublicHandRevealReturn("xiashu-selected-hand")]
    private void ReturnXiashuPublicHand(ProgramSkillFrame f)
    {
        if (f.XiashuPaid is not { Stage: XiashuStage.OwnerClaim })
            throw new InvalidOperationException("Xiashu lost its frozen recipient Hand reveal.");
        if (!_players[f.OwnerSeat].IsAlive || !_players[f.XiashuPaid.Invoice.RecipientSeat].IsAlive) FinishXiashu(f); else PublishXiashuDecision(f);
    }
    [PublicHandRevealReturn("qingliang-hand")]
    private void ReturnQingliangPublicHand(ProgramSkillFrame f)
    {
        if (f.RuiJiQingliang is not { Stage: "option" })
            throw new InvalidOperationException("Qingliang lost its issued whole-Hand reveal.");
        PresentQingliangOptionPrompt(f);
    }
    [PublicHandRevealReturn("diou-retained-hand")]
    private void ReturnDiouPublicHand(ProgramSkillFrame f)
    {
        if (f.Diou is not { Paid: true, Stage: DiouStage.UseChoice })
            throw new InvalidOperationException("Diou lost its paid retained-Hand reveal.");
        PublishDiou(f);
    }
    [PublicHandRevealReturn("hezhong-direction-hand")]
    private void ReturnHezhongPublicHand(ProgramSkillFrame f)
    {
        if (f.FeiYi is not { Paid: true, Greater: not null, Stage: FeiYiStage.DirectionChoice })
            throw new InvalidOperationException("Hezhong lost its accepted direction and revealed card.");
        BeginFeiYiDraw(f, true);
    }
    [PublicHandRevealReturn("hand-comparison-source")]
    private void ReturnHandComparisonSource(ProgramSkillFrame f)
    {
        if (f.HandComparisonDraft is not { Stage: "reveal" })
            throw new InvalidOperationException("A Hand comparison lost its frozen source and target roster.");
        PublishHandComparison(f);
    }
    [PublicHandRevealReturn("fire-attack-retained-payment")]
    private void ReturnFireAttackRetainedPublicHand(CardUseFrame pending)
    {
        var r=pending.PublicHandRevealSelection??throw new InvalidOperationException("FireAttack lost its retained payment reveal.");
        if(r.ReturnKey!="fire-attack-retained-payment"||r.ActionId!=pending.Action?.ActionId||r.ActorSeat!=pending.SourceSeat||pending.CardKind!=CardKind.FireAttack)
            throw new InvalidOperationException("FireAttack changed its original accepted source and retained card.");
        ReplaceRuntimeTop(pending=pending with{PublicHandRevealSelection=null});
        var source=_players[pending.SourceSeat];var target=_players[GetFireAttackTargetSeat(pending)];
        var revealed=GetAdvancedCard(pending.FireAttackSelection!.RevealedCardId!.Value);var matchingDiscard=GetAdvancedCard(r.CardId);var revealInstead=true;
        AdvanceEventRulesAndQueueFact(new FireAttackResolvedEvent(
            pending.Id,
            source.Seat,
            target.Seat,
            revealed.Id,
            revealed.Suit,
            matchingDiscard.Id,
            CausedDamage: true));
        AddLog(
            "CardEffect",
            $"{source.Name} {(revealInstead ? "展示" : "弃置")}【{matchingDiscard.DisplayName}】，{target.Name} 受到 1 点火焰伤害。",
            source.Seat,
            target.Seat);

        var attackCard = GetFireAttackEffectCard(pending);
        SetFireAttackSelection(pending.Id, null);
        var attack = new CardAttackHandle(this,
            pending.Id,
            source.Seat,
            target.Seat,
            (IsProgramVirtualOrdinaryTrickUse(pending.Id) || IsTieredRoundZeroFireAttackUse(pending.Id) || IsMouZhijueZeroUse(pending.Id) || IsDiouZeroUse(pending.Id) || IsMouXqJueyanUse(pending.Id) || IsQinLangXianyingUse(pending.Id)) ? null : attackCard,
            damageAmount: 1,
            playedCardKind: CardKind.FireAttack,
            physicalCards: GetCardUsePhysicalCards(pending.Id));
        ActiveCardAttack = attack;
        if (!ApplyAttackDamage(attack))
        {
            CompleteAttack(attack);
        }
    }

}
