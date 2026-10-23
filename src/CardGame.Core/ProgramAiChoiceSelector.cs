namespace CardGame.Core;

// Selects a published program choice through a bounded delegate. The engine keeps
// command submission, frame ownership, diagnostics and state publication.
internal sealed class ProgramAiChoiceSelector
{
    private readonly Func<SkillProgramEffectOp,
        Func<PendingDecision, ProgramSkillFrame, SkillProgramEffect, PromptChoice>?> _resolve;

    internal ProgramAiChoiceSelector(Func<SkillProgramEffectOp,
        Func<PendingDecision, ProgramSkillFrame, SkillProgramEffect, PromptChoice>?> resolve)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
    }

    internal PromptChoice Select(PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var select = _resolve(effect.Op) ?? throw UnsupportedInstruction(effect);
        return select(decision, frame, effect);
    }

    internal static InvalidOperationException UnsupportedInstruction(SkillProgramEffect effect) =>
        new($"The AI does not support suspended program instruction '{effect.Op}'.");

    internal static PromptChoice SelectByCoverage(PendingDecision decision, SkillProgramEffect effect,
        Func<int, bool> removalReducesCoverage)
    {
        if (effect.CoverageResultBind is null) return decision.Choices[0];
        return decision.Choices.OrderByDescending(choice => choice.Cards.Count == 1 &&
                removalReducesCoverage(choice.Cards[0]))
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
    }

    [ProgramAiChoice(
        SkillProgramEffectOp.JoinAdjacentRedCardTarget,
        SkillProgramEffectOp.ResolveDiscardBudgetParticipants,
        SkillProgramEffectOp.DiscardOutsideRangeAfterInsufficientUses,
        SkillProgramEffectOp.OfferCompletedFactionCostGift,
        SkillProgramEffectOp.SelectRelativeZoneDemandTarget,
        SkillProgramEffectOp.ExchangeOwnedCardThroughDeckEnd,
        SkillProgramEffectOp.DrawPublicSuitThenEscalatingDiscard,
        SkillProgramEffectOp.DeclareNameForTargetDefense,
        SkillProgramEffectOp.DrawAndDraftLowHandPopulation,
        SkillProgramEffectOp.ViewTopCardsAndObtainMatchingCards,
        SkillProgramEffectOp.RevealSelectedHandAgainstTarget,
        SkillProgramEffectOp.ExchangePrivatePile,
        SkillProgramEffectOp.DiscardParticipantCards,
        SkillProgramEffectOp.SpendMarkerOrLoseHp,
        SkillProgramEffectOp.SelectDistinctSuitHandDiscards,
        SkillProgramEffectOp.SuppressGeneralSkill,
        SkillProgramEffectOp.SelectChainedByMarker,
        SkillProgramEffectOp.SelectOneSelectedTarget,
        SkillProgramEffectOp.BeginPhaseNamePrediction,
        SkillProgramEffectOp.SettlePhaseNamePrediction,
        SkillProgramEffectOp.AllocateCurrentTurnDefaultStats,
        SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget,
        SkillProgramEffectOp.OfferCompletedCardGift,
        SkillProgramEffectOp.ApplyCurrentCardEnhancements,
        SkillProgramEffectOp.PayEquipmentColorDiscard,
        SkillProgramEffectOp.AddCurrentCardUseTarget,
        SkillProgramEffectOp.AlternatingSuitDrawDiscard,
        SkillProgramEffectOp.FirstCategoryCompletedTop,
        SkillProgramEffectOp.DiscardNonFinalTargetCardThenDraw,
        SkillProgramEffectOp.DiscardHandToNamedTurnCount,
        SkillProgramEffectOp.InitializePrivateGeneralLibrary,
        SkillProgramEffectOp.AcquirePrivateGeneralAvatar,
        SkillProgramEffectOp.ChoosePrivateGeneralAvatar,
        SkillProgramEffectOp.PeekTurnQuotaTop,
        SkillProgramEffectOp.UseDiamondDelayedOrDiscard,
        SkillProgramEffectOp.CompareSelectedHandWithHpHand,
        SkillProgramEffectOp.OfferFaceUpForOutsideClaims,
        SkillProgramEffectOp.MouZhitianView,
        SkillProgramEffectOp.MouZhijueUse,
        SkillProgramEffectOp.PutDiscardedCardsOnDrawPileTop,
        SkillProgramEffectOp.PutOwnOrPreviousFirstDiscardOnTop,
        SkillProgramEffectOp.DrawThenDiscardHandToMaximumHp,
        SkillProgramEffectOp.ResolveDeferredPrivateCardOffer,
        SkillProgramEffectOp.ResolveGameTargetHandHpChoice,
        SkillProgramEffectOp.DiscardTargetHpCardsAndDamage,
        SkillProgramEffectOp.XianlueRecord,
        SkillProgramEffectOp.XianlueRedistribute,
        SkillProgramEffectOp.DiscardSelectedParticipantCards,
        SkillProgramEffectOp.OfferBoundCardsForDamagePrevention,
        SkillProgramEffectOp.OfferVirtualSlashOrDraw,
        SkillProgramEffectOp.OfferVirtualBasicCard,
        SkillProgramEffectOp.RequestSlashAgainstChosenTarget,
        SkillProgramEffectOp.TakeSelectedTargetCards,
        SkillProgramEffectOp.GiveOwnedCardToOtherFinalTargetAndDraw,
        SkillProgramEffectOp.CollectFinalTargetCardInPublicPile,
        SkillProgramEffectOp.ObtainPublicPileCard,
        SkillProgramEffectOp.DiscardPublicZoneAfterHandPayment,
        SkillProgramEffectOp.SelectSourceCard,
        SkillProgramEffectOp.GiveBoundCard,
        SkillProgramEffectOp.UseVirtualOrdinaryTrick,
        SkillProgramEffectOp.ConfigureFriendDuelCategories,
        SkillProgramEffectOp.ConfigureFriendUseSuits,
        SkillProgramEffectOp.JuanxiaDeclareTricks,
        SkillProgramEffectOp.JuanxiaRetaliation)]
    private static PromptChoice SelectFirstChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices[0];

    [ProgramAiChoice(
        SkillProgramEffectOp.DrawAfterOtherDeathThenChooseMissingSkill,
        SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage,
        SkillProgramEffectOp.RandomEligibleOtherHandInspection,
        SkillProgramEffectOp.SettleZhuijiActualPlayDebt,
        SkillProgramEffectOp.DiscardHandOrUseEquipment,
        SkillProgramEffectOp.MoveFieldEquipment,
        SkillProgramEffectOp.SettleQingzhongActualPlayDebt,
        SkillProgramEffectOp.MarkUnmarkedOtherBeforeActualTurn,
        SkillProgramEffectOp.MarkLowestHpUnmarkedOtherAtRoundStart,
        SkillProgramEffectOp.ConsumeKuiAppendLegalTargets)]
    private static PromptChoice SelectFirstEnumerableChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.First();

    [ProgramAiChoice(
        SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol)]
    private static PromptChoice SelectFirstNonPassChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.First(choice => choice.Parameters.GetValueOrDefault("branch") != "pass");

    [ProgramAiChoice(
        SkillProgramEffectOp.CollectPublicPile)]
    private static PromptChoice SelectMostTargetsChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.OrderByDescending(choice => choice.Targets.Count).First();

    [ProgramAiChoice(
        SkillProgramEffectOp.RevealTargetHandCard,
        SkillProgramEffectOp.BijingPunishDiscardPhase,
        SkillProgramEffectOp.DaoshuGuessAndTake,
        SkillProgramEffectOp.YoudiBaitDiscard,
        SkillProgramEffectOp.GuanchaoChoosePattern,
        SkillProgramEffectOp.XunxianGiftUsedCard,
        SkillProgramEffectOp.QiaoliWeaponDamageDraw)]
    private static PromptChoice SelectOrdinalChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    [ProgramAiChoice(
        SkillProgramEffectOp.SelectCardSubset)]
    private static PromptChoice SelectMostCardsThenRankSumChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (effect.AiOrder != SkillProgramSubsetAiOrder.MostCardsThenRankSum)
            throw UnsupportedInstruction(effect);
        return decision.Choices
            .OrderByDescending(choice => choice.Cards.Count)
            .ThenByDescending(choice => int.Parse(
                choice.Parameters.GetValueOrDefault("rank-sum", "0"),
                System.Globalization.CultureInfo.InvariantCulture))
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
            .First();
    }

    [ProgramAiChoice(
        SkillProgramEffectOp.MoveBoundCards)]
    private static PromptChoice SelectDrawPileBottomChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (effect.Destination != SkillProgramCardDestination.DrawPileBottom)
            throw UnsupportedInstruction(effect);
        return decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
    }

    [ProgramAiChoice(
        SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick)]
    private static PromptChoice SelectPreferredDrawTwoChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices
            .OrderBy(choice => choice.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.DrawTwo) ? 0 : 1)
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
            .First();

    private static readonly string[] AichenRemovalPreference = ["recover", "loseHp", "discardTwo", "drawTwo"];

    [ProgramAiChoice(SkillProgramEffectOp.ClaimGrantedPhaseSlash)]
    private static PromptChoice SelectAiGrantedPhaseSlash(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option") == "deck") ??
        decision.Choices.FirstOrDefault(c => c.Cards.Count == 1) ?? decision.Choices.Single(c => c.Parameters.GetValueOrDefault("option") == "skip");

    [ProgramAiChoice(SkillProgramEffectOp.WeaponDiscardOrDamageBonus)]
    private static PromptChoice SelectAiWeaponDamage(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "finish") ??
        decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "card") ?? decision.Choices.Single();

    [ProgramAiChoice(
        SkillProgramEffectOp.AssignOrderedDragonPhoenixOnFirstActualTurn,
        SkillProgramEffectOp.TransferOwnedDragonOrPhoenixSeal,
        SkillProgramEffectOp.TransferDeadDragonPhoenixSeal,
        SkillProgramEffectOp.DistributeTopCardsAfterElementalChainOrigin,
        SkillProgramEffectOp.ConsumeDragonPhoenixForNativeYeyan)]
    private static PromptChoice SelectAiSimaHui(
        PendingDecision decision, ProgramSkillFrame f, SkillProgramEffect effect) =>decision.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("value")!="finish")??decision.Choices[0];

    internal static PromptChoice SelectAiJianjiChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>decision.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("option")!="decline")??decision.Choices.Last();

    [ProgramAiChoice(SkillProgramEffectOp.ResolveTargetChosenRankJudgment)]
    private static PromptChoice SelectAiZhangJi(
        PendingDecision p, ProgramSkillFrame f, SkillProgramEffect effect) =>p.Choices.Single(c=>c.Parameters["rank"]=="7");

    [ProgramAiChoice(SkillProgramEffectOp.GiveOffTurnUsedCards)]
    private static PromptChoice SelectAiOffTurnUsedCardGift(
        PendingDecision p, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        p.Choices.OrderBy(c => c.Targets.Single()).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();

    [ProgramAiChoice(SkillProgramEffectOp.YanjiaoSplitRevealedCards)]
    private static PromptChoice SelectAiYanjiaoSplit(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (decision.Choices[0].Parameters.GetValueOrDefault("stage") == "first")
            return decision.Choices
                .OrderByDescending(choice => choice.Cards.Count)
                .ThenByDescending(choice => int.Parse(
                    choice.Parameters.GetValueOrDefault("rank-sum", "0"),
                    System.Globalization.CultureInfo.InvariantCulture))
                .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
        return decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
    }

    [ProgramAiChoice(SkillProgramEffectOp.TunanUseRevealedCard)]
    private static PromptChoice SelectAiTunanChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (decision.Choices[0].Parameters.GetValueOrDefault("program-action") == "tunan-branch")
            return decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "use") ??
                decision.Choices[0];
        return decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
    }

    [ProgramAiChoice(SkillProgramEffectOp.ZhirenResolveFieldTiers)]
    private static PromptChoice SelectAiZhirenFieldChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) => decision.Choices
        .OrderBy(choice => choice.Parameters.GetValueOrDefault("action") == "skip" ? 1 : 0)
        .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    [ProgramAiChoice(SkillProgramEffectOp.ZecaiRoundSettlement)]
    private static PromptChoice SelectAiZecaiChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var leader = decision.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("unique-trick-leader") == "True");
        if (leader is not null) return leader;
        return decision.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "zecai-decline");
    }

    [ProgramAiChoice(SkillProgramEffectOp.ZhuLingZhanyiChooseCategory)]
    private static PromptChoice SelectAiZhanyiCategoryChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.OrderBy(choice => choice.Cards.Count)
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    [ProgramAiChoice(SkillProgramEffectOp.ZhuLingZhanyiEquipmentPunish)]
    private static PromptChoice SelectAiZhanyiPunishChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.OrderBy(choice => choice.Targets.Count)
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    [ProgramAiChoice(SkillProgramEffectOp.FengjiRoundChoice)]
    private static PromptChoice SelectAiFengjiChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("accept") == "false") ??
        decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    [ProgramAiChoice(SkillProgramEffectOp.ChangjiEndingDamageChoice)]
    private static PromptChoice SelectAiChangjiEndingChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (decision.Choices[0].Parameters.GetValueOrDefault("program-action") == "changji-discard")
            return decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
        var draw = decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "draw");
        var discard = decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "discard");
        var skip = decision.Choices.First(choice => choice.Parameters.GetValueOrDefault("branch") == "skip");
        return decision.TargetSeat == decision.PlayerSeat ? draw ?? skip : discard ?? skip;
    }

    [ProgramAiChoice(SkillProgramEffectOp.ZengouNullifyDodge)]
    private static PromptChoice SelectAiZengouCostChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var discard = decision.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("cost") == "discard");
        return discard ?? decision.Choices.First();
    }

    [ProgramAiChoice(SkillProgramEffectOp.KanggeHealVictim)]
    private static PromptChoice SelectAiKanggeHealChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.First(choice => choice.Parameters.GetValueOrDefault("decline") != "true");

    [ProgramAiChoice(SkillProgramEffectOp.QingliangChooseOption)]
    private static PromptChoice SelectAiQingliangChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (decision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "qingliang-suit"))
            return decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
        return decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("choice") == "draw") ??
            decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
    }

    // 援资 payoff prompt: the optional draw is always worth taking.
    [ProgramAiChoice(SkillProgramEffectOp.YuanziDamageDraw)]
    private static PromptChoice SelectAiYuanziDamageDrawChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "yuanzi-draw");

    [ProgramAiChoice(
        SkillProgramEffectOp.DiscardSameCategoryForExtraEffect,
        SkillProgramEffectOp.GuessRanksForDrawReplacement,
        SkillProgramEffectOp.DeathRecipientChooseSkill)]
    private static PromptChoice SelectAiExtraEffectsAndRankGuess(
        PendingDecision decision, ProgramSkillFrame f, SkillProgramEffect effect)
    {
        var r = f.ExtraEffectsAndRankGuess!;
        if (r.Stage == ExtraEffectsAndRankGuessStage.Guess) return decision.Choices.Single(c => c.Parameters["branch"] == (r.LastRank <= 7 ? "greater" : "less"));
        if (r.Stage == ExtraEffectsAndRankGuessStage.Cost) return decision.Choices.First();
        return decision.Choices.First();
    }

    [ProgramAiChoice(
        SkillProgramEffectOp.DepositPrivateWhirlUntilNextPlayEnding,
        SkillProgramEffectOp.DepositPersistentPrivateWhirl,
        SkillProgramEffectOp.ConsumePrivateWhirlForActualUse,
        SkillProgramEffectOp.ClearPrivateWhirlAtNextPlayEnding)]
    private static PromptChoice SelectAiPrivateWhirl(
        PendingDecision decision, ProgramSkillFrame f, SkillProgramEffect effect)
    {
        if (f.PrivateWhirl!.Stage == PrivateWhirlStage.SelectDeposit && f.PrivateWhirl.Selected.Count >= 3) return decision.Choices.Single(c => c.Parameters["branch"] == "confirm");
        return decision.Choices.FirstOrDefault(c => c.Cards.Count == 1) ?? decision.Choices.Last();
    }

    [ProgramAiChoice(
        SkillProgramEffectOp.ChooseHandCountIntervention,
        SkillProgramEffectOp.RevealHandColorDiscardAndTake,
        SkillProgramEffectOp.DrawThenPutOwnedCardOnTopParticipants,
        SkillProgramEffectOp.DrawTurnOwnerThenDiscardMaximumHandForDodge)]
    private static PromptChoice SelectAiProgramHandControlChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var stage = frame.HandControlDraft!.Stage;
        return decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("hand-control-action") != "finish" &&
            (stage != "participants" || frame.HandControlDraft.ParticipantSeats.Count < Math.Min(2, frame.HandControlDraft.Maximum))) ?? decision.Choices.Last();
    }

    [ProgramAiChoice(
        SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash,
        SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial)]
    private static PromptChoice SelectAiWangYun(
        PendingDecision p, ProgramSkillFrame f, SkillProgramEffect effect)
    {
        if(f.WangYunPlan?.Stage=="branch")return p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="slash")??p.Choices[0];
        return p.Choices[0];
    }

    [ProgramAiChoice(
        SkillProgramEffectOp.ExchangeFriendGainForDiscard,
        SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd)]
    private static PromptChoice SelectAiFriendPangTongChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        frame.FriendPangTong!.Stage == FriendPangTongStage.Select
            ? frame.FriendPangTong.Selected.Count < Math.Min(5, frame.FriendPangTong.Cards.Count)
                ? decision.Choices.FirstOrDefault(choice => choice.Cards.Count == 1) ?? decision.Choices.Last() : decision.Choices.Last()
            : decision.Choices.FirstOrDefault(choice => choice.Cards.Count == 1) ?? decision.Choices.Last();

    [ProgramAiChoice(
        SkillProgramEffectOp.OfferOriginalHandLossBenefit,
        SkillProgramEffectOp.InheritOriginalHandBonuses)]
    private static PromptChoice SelectAiOriginalHandEntityChoice(
        PendingDecision decision, ProgramSkillFrame f, SkillProgramEffect effect) =>
        decision.Choices.FirstOrDefault(c => c.Targets.Contains(f.OwnerSeat)) ??
        decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option") == "draw") ?? decision.Choices[0];

    [ProgramAiChoice(SkillProgramEffectOp.RevealUndamagedUseTargetHandAndDiscardSameColor)]
    private static PromptChoice SelectAiCompletedUndamagedTargetReveal(
        PendingDecision decision, ProgramSkillFrame f, SkillProgramEffect effect)
    {
        // Deliberately inspect only public seat/count and opaque slots, never the target's material invoice.
        if (f.CompletedUndamagedTargetReveal!.Stage == CompletedUndamagedTargetRevealStage.ChoosingTarget)
            return decision.Choices.OrderBy(c => c.Targets.Single() == f.OwnerSeat).ThenBy(c => c.Targets.Single()).First();
        return decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "slot") ?? decision.Choices.Single();
    }

    [ProgramAiChoice(SkillProgramEffectOp.HoldTargetCards)]
    private static PromptChoice SelectAiProgramHoldCards(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var draft = frame.HoldCardSelection ??
            throw new InvalidOperationException("The card-hold choice lost its draft.");
        if (draft.SelectedCardIds.Count >= draft.RequiredCount)
            return decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
        // Public equipment is held first; hidden hand slots follow in fixed order.
        return decision.Choices
            .Where(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards")
            .OrderByDescending(choice =>
                choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment))
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
            .First();
    }

    // 哀尘 removal: drop the lowest retention value first — recovery, then the
    // direct hp loss, then the two-card discard; the two-card draw outlasts all.
    [ProgramAiChoice(SkillProgramEffectOp.AichenRemoveOption)]
    private static PromptChoice SelectAiAichenRemoveChoice(
        PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices
            .OrderBy(choice => Array.IndexOf(AichenRemovalPreference,
                choice.Parameters.GetValueOrDefault("option")))
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
            .First();
}
