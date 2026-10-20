namespace CardGame.Core;

// Snapshot construction and visibility rules live beside the observer projection.
public sealed partial class GameEngine
{
    /// <summary>
    /// Creates a viewer-safe snapshot. Pass revealAll only for diagnostics/tests or
    /// an explicit post-game reveal screen.
    /// </summary>
    private GameSnapshot ProjectPlayerView(int viewerSeat, bool revealAll)
    {
        using var queryScope = BeginActionQuery();
        var snapshots = new PlayerSnapshot[_players.Count];
        for (var playerIndex = 0; playerIndex < _players.Count; playerIndex++)
        {
            var player = _players[playerIndex];
            var playerHand = GetHand(player);
            var canSeeRole = !IsNationalWarMode &&
                             (revealAll ||
                              IsTeamMode ||
                              player.Role == Role.Lord ||
                              player.RoleRevealed ||
                              player.Seat == viewerSeat);
            var canSeeHand = revealAll || player.Seat == viewerSeat;
            var hand = canSeeHand
                ? playerHand.Select(card => ProjectHandCard(player, card)).ToArray()
                : [];
            var playerEquipment = GetEquipment(player);
            var equipment = playerEquipment
                .Select(ToSnapshot)
                .ToArray();
            var judgment = GetJudgment(player)
                .Select(ToJudgmentSnapshot)
                .ToArray();
            var woodenOxGrain = GetWoodenOxGrain(player);
            var buquWounds = GetBuquWounds(player);
            var authority = GetAuthority(player);
            var chunlao = _cardZones.CardsAt(CardLocation.Chunlao(player.Seat));
            var pojunHold = _cardZones.CardsAt(CardLocation.PojunHold(player.Seat));
            var canSeeGeneral = player.GeneralSelected &&
                                (revealAll || player.GeneralRevealed || player.Seat == viewerSeat);
            var general = canSeeGeneral ? player.General : CreateHiddenGeneral();
            if (player.IdentityConcealed && !revealAll && player.Seat != viewerSeat)
                general = IdentityConcealedGeneral();
            var canSeeSecondaryGeneral = player.SecondaryGeneralSelected &&
                                         (revealAll ||
                                          player.SecondaryGeneralRevealed ||
                                          player.Seat == viewerSeat);
            var secondaryGeneral = canSeeSecondaryGeneral ? player.SecondaryGeneral : null;
            var effectiveFactionId = GetEffectiveFactionId(player);
            var hasIdentityGodFaction = SupportsGodFactionSelection &&
                                        string.Equals(player.General.FactionId, "god", StringComparison.Ordinal) &&
                                        player.ChosenFactionId is not null;
            var hasActivityFaction = _modeDefinition.ShenwuActivity is not null || _modeDefinition.NianActivity is not null || IsDragonBoatActivity || IsGuanduActivity;
            var canSeeFaction = effectiveFactionId is not null &&
                                (IsNationalWarMode
                                    ? revealAll || player.FactionRevealed || player.Seat == viewerSeat
                                    : (hasIdentityGodFaction || hasActivityFaction || HasNativeEffectiveFactionDeclaration(player) || GetPrivateGeneralLibraryFaction(player) is not null) &&
                                      (revealAll || player.GeneralRevealed || player.Seat == viewerSeat));
            var primarySkills = canSeeGeneral
                ? player.IdentityConcealed ? general.Skills.ToArray()
                    : OwnedRuntimeSkills(player, general, includeAcquired: true).ToArray() : null;
            if (_modeDefinition.WenheActivity is not null)
                primarySkills = primarySkills?.Where(skill => IsWenhePlayerSkillVisible(player, skill, viewerSeat, revealAll)).ToArray();
            var secondarySkills = secondaryGeneral is not null
                ? OwnedRuntimeSkills(player, secondaryGeneral, includeAcquired: false).ToArray() : null;
            var publicPile = SinglePublicPileSource(player.Seat);
            var publicPileCards = publicPile is not null ? PublicPileCards(publicPile) : Array.Empty<Card>();
            var deferredPileCards = _cardZones.CardsAt(new CardLocation(CardZoneKind.PublicDeferredPile, player.Seat));
            var reserveCards = _cardZones.CardsAt(new CardLocation(CardZoneKind.PrivateReserve, player.Seat));
            snapshots[playerIndex] = new PlayerSnapshot(
                player.Seat,
                player.Name,
                player.IsHuman,
                canSeeRole ? player.Role : null,
                !IsNationalWarMode && (player.RoleRevealed || player.Role == Role.Lord),
                general.Id,
                general.Name,
                general.PortraitKey,
                player.HasHpValue ? player.Hp : 0,
                player.HasHpValue ? player.MaxHp : 0,
                player.IsAlive,
                playerHand.Count,
                hand,
                player.GeneralSelected && player.GeneralRevealed,
                player.HasAlcoholEffect)
            {
                IdentityConcealed = player.IdentityConcealed,
                Armor = player.Armor,
                SurvivalRank = WenheSurvivalRank(player.Seat),
                WenheChampionMarkUsed = IsSurvivalMode && WenheChampionMarkUsed(player.Seat) ? true : null,
                HpUndefined = !player.HasHpValue,
                HandLimit = _setupComplete && player.Seat == _currentSeat && player.HasHpValue
                    ? GetHandLimit(player) : null,
                AlcoholDamageBonus = player.AlcoholDamageBonus >= 2 ? player.AlcoholDamageBonus : null,
                IsFaceDown = player.IsFaceDown,
                ActiveEffects = ProjectActivePlayerEffects(player, primarySkills, secondarySkills),
                OriginalHandEntities = ProjectOriginalHandEntities(player.Seat, viewerSeat, revealAll),
                TeamId = IsTeamMode && player.TeamRevealed ? player.TeamId : null,
                IsTeamRevealed = IsTeamMode && player.TeamRevealed,
                Equipment = equipment,
                IsEquipmentAreaAbolished = player.EquipmentAreaAbolished,
                EquipmentSlotCapacities = player.EquipmentSlotCapacities.Count > 0
                    ? new Dictionary<EquipmentSlot, int>(player.EquipmentSlotCapacities) : null,
                ConfiguredConversionTiers = GetConfiguredConversionTierSnapshot(player),
                AlternatingChoiceStates = GetAlternatingChoiceStateSnapshot(player),
                BeneficiarySuitShields = _beneficiarySuitShields.Count > 0 &&
                    _beneficiarySuitShields.Where(s => s.BeneficiarySeat == player.Seat).ToArray() is { Length: > 0 } shields ? shields : null,
                DeferredHandAlignments = _deferredHandAlignments.Count > 0 &&
                    _deferredHandAlignments.Where(d => d.TargetSeat == player.Seat).ToArray() is { Length: > 0 } alignments ? alignments : null,
                TurnHandCategoryRestrictions = GetTurnHandCategoryRestrictionSnapshot(player.Seat),
                MouCaoPiControls = MouCaoPiControlsSnapshot(player.Seat),
                TurnHandLimitCardKindExemptions = GetTurnHandLimitCardKindExemptionsSnapshot(player.Seat),
                TurnSlashSuitAllowances = GetTurnSlashSuitAllowancesSnapshot(player.Seat),
                FirstRoundGameUsageRefunds = GetFirstRoundGameUsageRefundsSnapshot(player.Seat),
                JudgedRankSlashThreshold = JudgedRankSlashSnapshot(player.Seat),
                ActualPlayPhaseCardUseState = TracksActualPlayPhaseCardUses && _phase == TurnPhase.Play && player.Seat == _currentSeat ? new(player.Seat, _turnNumber, _cardUseDebitPhaseInstanceId, GetActualPlayPhaseUseCount(player.Seat)) : null,
                IssuedPlayPhaseUseProhibitions = _issuedPlayPhaseUseProhibitions.Count > 0 && HasIssuedPlayPhaseUseBan(player.Seat) &&
                    _issuedPlayPhaseUseProhibitions.Where(p => p.ActorSeat == player.Seat).ToArray() is { Length: > 0 } issuedBans ? issuedBans : null,
                IssuedPlayPhaseSuitUseAllowances = GetIssuedPlayPhaseSuitUseAllowances(player.Seat),
                WoodenOxGrainCount = woodenOxGrain.Count,
                WoodenOxGrain = playerEquipment.Any(card => card.Kind == CardKind.WoodenOx) || woodenOxGrain.Count > 0
                    ? canSeeHand
                        ? woodenOxGrain.Select(card => ProjectFuleHandCard(player, card, ToSnapshot(card))).ToArray()
                        : Array.Empty<CardSnapshot>()
                    : null,
                IsChained = player.IsChained,
                Judgment = judgment,
                IsJudgmentAreaAbolished = player.JudgmentAreaAbolished,
                BuquWounds = HasProgramSkill(player, "classic:buqu") || buquWounds.Count > 0
                    ? buquWounds.Select(ToSnapshot).ToArray()
                    : null,
                AuthorityCount = authority.Count,
                PublicPersistentPileCards = publicPileCards.Count > 0 ? publicPileCards.Select(ToSnapshot).ToArray() : null,
                PublicPersistentPileCount = publicPileCards.Count,
                PublicPersistentPileName = publicPile is not null ? _contentRegistry.GetSkill(publicPile.SkillId).ProgramPresentation?.AuthorityName : null,
                PublicPersistentPileSkillId = publicPile?.SkillId,
                SourceCurses = CreateSourceCurseSnapshots(player.Seat),
                PublicPersistentPiles = CreatePublicPersistentPileSnapshots(player.Seat),
                PrivateGeneralLibraries = ProjectPrivateGeneralLibraries(player,viewerSeat,revealAll),
                PrivateTurnHolds = ProjectPrivateTurnHolds(player.Seat, viewerSeat, revealAll),
                NamedAmbushPiles = IncludeChenglieSnapshots(IncludeQushiSealSnapshots(ProjectNamedAmbushPiles(player.Seat, viewerSeat, revealAll), player.Seat, viewerSeat, revealAll), player.Seat, viewerSeat, revealAll),
                PrivateWhirlPiles = ProjectPrivateWhirlPiles(player.Seat, viewerSeat, revealAll),
                PublicDeferredPileName = _deferredPublicPileDeposits.Count > 0 &&
                    _deferredPublicPileDeposits.FirstOrDefault(item => item.OwnerSeat == player.Seat) is { } deposit ? _contentRegistry.GetSkill(deposit.SkillId).ProgramPresentation?.AuthorityName : null,
                PublicDeferredPileCount = deferredPileCards.Count,
                PublicDeferredPileCards = deferredPileCards.Count > 0 ? deferredPileCards.Select(ToSnapshot).ToArray() : null,
                AuthorityName = ProjectAuthorityName(primarySkills),
                PrivateReserveCount = reserveCards.Count,
                PrivateReserveCards = canSeeHand && (HasProgramPersistentZone(player, CardZoneKind.PrivateReserve) ||
                    reserveCards.Count > 0)
                    ? reserveCards.Select(ToSnapshot).ToArray() : null,
                AuthorityCards = authority.Select(ToSnapshot).ToArray(),
                ChunlaoCount = chunlao.Count,
                ChunlaoCards = HasProgramPersistentZone(player, CardZoneKind.Chunlao) || chunlao.Count > 0
                    ? chunlao.Select(ToSnapshot).ToArray()
                    : null,
                RoundHoldCount = _cardZones.CardsAt(CardLocation.RoundHold(player.Seat)).Count,
                PojunHoldCount = pojunHold.Count,
                PojunHoldCards = pojunHold.Count > 0
                    ? pojunHold.Select(ToSnapshot).ToArray()
                    : null,
                Markers = AppendMouHuaXiongWei(player.Seat, AppendMouLixianProgressMarkers(player, canSeeGeneral, AppendMoQuanyuPublicMarkers(player.Seat, AppendOlMoRumoMarker(player.Seat, player.Markers.Count > 0 || player.Shanjia is { LossMarks: > 0 }
                    ? player.Markers.Where(marker => marker.Key != PlayerMarkerKind.ShanjiaLoss)
                        .Concat(player.Shanjia is { LossMarks: > 0 } shanjia
                            ? [new KeyValuePair<PlayerMarkerKind, int>(PlayerMarkerKind.ShanjiaLoss, shanjia.LossMarks)]
                            : Array.Empty<KeyValuePair<PlayerMarkerKind, int>>())
                        .OrderBy(marker => marker.Key)
                        .Select(marker => new PlayerMarkerSnapshot(
                            marker.Key,
                            PlayerMarkerCatalog.GetDisplayName(marker.Key),
                            marker.Value))
                        .ToArray()
                    : null)))),
                FactionId = canSeeFaction ? effectiveFactionId : null,
                IsFactionRevealed = IsNationalWarMode
                    ? player.FactionRevealed
                    : (hasIdentityGodFaction || hasActivityFaction || HasNativeEffectiveFactionDeclaration(player) || GetPrivateGeneralLibraryFaction(player) is not null) && player.GeneralRevealed,
                SecondaryGeneralId = secondaryGeneral?.Id,
                SecondaryGeneralName = secondaryGeneral?.Name,
                SecondaryPortraitKey = secondaryGeneral?.PortraitKey,
                IsSecondaryGeneralPublic = IsDualGeneralMode && player.SecondaryGeneralRevealed,
                Skills = primarySkills,
                SecondarySkills = secondarySkills,
                SkillRuntimeStates = primarySkills is not null
                    ? primarySkills.Concat(secondarySkills ?? [])
                        .Select(skill => skill.ContentId)
                        .OfType<string>()
                        .Distinct(StringComparer.Ordinal)
                        .Select(skillId => CreateProgramAwareSkillStateSnapshot(player, skillId))
                        .ToArray()
                    : null
            };
        }

        var visibleDecision = _pendingDecision?.PlayerSeat == viewerSeat
            ? _pendingDecision
            : null;
        // Publish only the responder's remaining selection bounds, including restored
        // older prompts. The private runtime draft stays on its owning frame.
        if (visibleDecision is { Kind: DecisionKind.ProgramTrigger } &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame { OwnedCardSelection: { } owned } &&
            visibleDecision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") is "select-owned-cards" or "finish-owned-cards"))
            visibleDecision = visibleDecision with
            {
                RequiredCardCount = owned.RequiredCount - owned.SelectedCardIds.Count,
                MinimumCardCount = owned.AllowEarlyFinish ? Math.Max(0, owned.MinimumCount - owned.SelectedCardIds.Count) : null
            };
        if (visibleDecision is not null) visibleDecision = ProjectCardPaymentCount(visibleDecision);
        ProgramSkillFrame? lastProgram = null;
        for (var index = _resolutionStack.Count - 1; index >= 0; index--)
            if (_resolutionStack[index] is ProgramSkillFrame program) { lastProgram = program; break; }
        IReadOnlyList<CardSnapshot> programRevealedCards = lastProgram is null
            ? Array.Empty<CardSnapshot>()
            : GetProgramPublicCards().Concat(GainGiftPublicLostCards()).DistinctBy(card => card.Id).ToArray();
        programRevealedCards = programRevealedCards.Concat(_resolutionStack.OfType<ProgramSkillFrame>()
            .Where(f => f.ExtraEffectsAndRankGuess?.Operation == SkillProgramEffectOp.GuessRanksForDrawReplacement)
            .SelectMany(f => f.ExtraEffectsAndRankGuess!.RevealedCardIds)
            .Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).Select(id => ToSnapshot(GetAttackCard(id))))
            .DistinctBy(card => card.Id).ToArray();
        if (lastProgram?.PublicPileDraft is { Stage: "pile" or "distribute" } pileDraft)
            programRevealedCards = programRevealedCards.Concat(PublicPileDraftCards(pileDraft).Select(ToSnapshot)).DistinctBy(card => card.Id).ToArray();
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { InstructionIndex: > 0 } pileFrame)
        {
            var effect = ProgramInstructionResolver.Default.Resolve(pileFrame,
                _contentRegistry.GetSkill(pileFrame.SkillId).Program!).GetPausedInstruction(pileFrame.InstructionIndex).Effect;
            if (effect.Op == SkillProgramEffectOp.ObtainPublicPileCard)
                programRevealedCards = programRevealedCards.Concat(ReferencedPublicPileSources(pileFrame.OwnerSeat,
                    effect.SkillIds.Single(), pileFrame.SkillInstanceId).SelectMany(PublicPileCards).Select(ToSnapshot))
                    .DistinctBy(card => card.Id).ToArray();
        }
        IReadOnlyList<CardSnapshot> publicRevealedCards = _resolutionStack.LastOrDefault() is PindianFrame { Result: { } contest }
            ? _cardZones.CardsAt(CardLocation.Processing)
                .Where(card => card.Id == contest.SourceCardId || card.Id == contest.OpponentCardId)
                .Select(ToSnapshot).ToArray()
            : programRevealedCards.Count > 0
            ? programRevealedCards
            : ActiveGroupCard is { Effect: GroupCardEffect.PublicDraft } publicDraft
            ? publicDraft.RevealedCardIds
                .Select(cardId => _cardZones.CardsAt(CardLocation.Processing)
                    .Single(card => card.Id == cardId))
                .Select(ToSnapshot)
                .ToArray()
            : ActiveFireAttack is { FireAttackSelection.RevealedCardId: not null } fireAttack
                ? [ToSnapshot(GetFireAttackRevealedCard(fireAttack))]
                : Array.Empty<CardSnapshot>();

        if (lastProgram?.AlternatingSuitTop is { Mode: "completed", Stage: "order" } orderedCosts)
            publicRevealedCards = publicRevealedCards.Concat(orderedCosts.CardIds.Except(orderedCosts.SelectedIds).Select(id => ToSnapshot(_cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c=>c.Id==id)))).DistinctBy(c=>c.Id).ToArray();

        return FreezePlayerView(new GameSnapshot(
            revealAll ? _options.Seed : null,
            _options.HumanSeat,
            _status,
            _winner,
            _turnNumber,
            _currentSeat,
            _phase,
            _cardZones.Count(CardLocation.DrawPile),
            _cardZones.Count(CardLocation.DiscardPile),
            snapshots,
            visibleDecision,
            WithoutChengliePrivateProcessing(_cardZones.CardsAt(CardLocation.Processing)).Count(card =>
                !IsNishouHeldProcessingCard(card.Id) && (revealAll || !IsSimultaneousPrivateTopCard(card.Id))),
            _revision)
        {
            CardDeclarations = GetCardDeclarationSnapshots(viewerSeat, revealAll),
            WinnerSeat = _winnerSeat,
            WenheRule = GetWenheRuleSnapshot(),
            WenheAspiration = GetWenheAspirationSnapshot(viewerSeat),
            GuanduActivity = GetGuanduSnapshot(),
            DragonBoatStandings = GetDragonBoatStandings(),
            DragonBoatDraft = GetDragonBoatDraft(viewerSeat),
            WinnerTeamId = IsTeamMode ? _winnerTeamId : null,
            WinnerFactionId = IsNationalWarMode ? _winnerFactionId : null,
            ModeKind = _modeDefinition.ModeKind,
            PublicRevealedCards = GetPubliclyRevealedHandCards().Concat(TangTangPublicShownCardIds().Concat(ZhouQunPublicShownCardIds()).Select(id => ToSnapshot(GetAdvancedCard(id)) with
                { PublicHandOwnerSeat = _cardZones.GetLocation(id).Zone == CardZoneKind.Hand ? _cardZones.GetLocation(id).OwnerSeat : null })).Concat(publicRevealedCards).Concat(ChengliePublicCards()).Concat(FriendPangTongPublicCards()).Concat(CurrentLuYuShengRevealedCards()).Concat(MouZuifengPublicCards())
                .Where(card => revealAll || !IsSimultaneousPrivateTopCard(card.Id))
                .DistinctBy(card => card.Id).ToArray(),
            PrivateRevealedCards = GetPrivatelyViewedCards(viewerSeat).Concat(GuanduShicaiPrivatelyViewedCards(viewerSeat)).Concat(GetCaoYingPrivateCards(viewerSeat)).Concat(GetZhugeGuoPrivatelyViewedCards(viewerSeat)).Concat(LiuBaPrivatelyViewedBottomCards(viewerSeat)).Concat(RuanYuPrivatelyViewedBottomCards(viewerSeat)).Concat(NishouPrivatelyHeldCards(viewerSeat)).Concat(ZhouQunPrivatelyViewedTopCards(viewerSeat)).Concat(MouZuifengPrivatelyViewedCards(viewerSeat)).Concat(WuZhangFeiPrivateCards(viewerSeat)).Concat(FeibaiPrivateCards(viewerSeat))
                .DistinctBy(card => card.Id).ToArray() is { Length: > 0 } privateCards ? privateCards : null,
            FriendPredictions = GetFriendPredictionViews(viewerSeat),
            ProgramResponseExchangeStates = GetResponseExchangeStateSnapshots(),
            TurnProhibitedPhysicalCards = GetResponseEntityRestrictionSnapshots()
        });
    }

    private CardSnapshot ProjectHandCard(CharacterState owner, Card card)
    {
        var snapshot = ToSnapshot(card);
        var identity = GetProgramCardIdentityMatches(owner, card).FirstOrDefault();
        snapshot = identity is null ? snapshot : snapshot with
        {
            IdentityKind = identity.Identity.OutputKind,
            IdentitySkillName = _contentRegistry.GetSkill(identity.Source.SkillId).Name
        };
        if (JuewuForeignHandRank(owner, card) is { } juewuRank)
            snapshot = snapshot with { Rank = juewuRank, RankText = juewuRank.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        return ProjectWenhePoisonHandCard(owner, card, ProjectFuleHandCard(owner, card, snapshot));
    }

    private string? ProjectAuthorityName(IReadOnlyList<GeneralSkillDefinition>? skills)
    {
        if (skills is null) return null;
        foreach (var skill in skills)
            if (skill.ContentId is { } contentId &&
                _contentRegistry.Skills.GetValueOrDefault(contentId)?.ProgramPresentation?.AuthorityName is { } name)
                return name;
        return null;
    }

    // Freeze detached projection values once, before the command commit exposes them
    // to both its result and every observer. Never freeze live content/state in place.
    private static GameSnapshot FreezePlayerView(GameSnapshot snapshot) => snapshot with
    {
        CardDeclarations = snapshot.CardDeclarations is { } declarations
            ? Array.AsReadOnly(declarations.Select(item => item with { TargetSeats = FreezeViewList(item.TargetSeats)! }).ToArray()) : null,
        Players = Array.AsReadOnly(snapshot.Players.Select(FreezePlayer).ToArray()),
        PendingDecision = snapshot.PendingDecision is { } decision ? CloneDecision(decision) : null,
        PublicRevealedCards = FreezeViewList(snapshot.PublicRevealedCards)!,
        PrivateRevealedCards = FreezeViewList(snapshot.PrivateRevealedCards),
        FriendPredictions = snapshot.FriendPredictions is { } forecasts
            ? Array.AsReadOnly(forecasts.Select(forecast => forecast with { Values = FreezeViewList(forecast.Values)! }).ToArray()) : null,
        ProgramResponseExchangeStates = FreezeViewList(snapshot.ProgramResponseExchangeStates),
        TurnProhibitedPhysicalCards = snapshot.TurnProhibitedPhysicalCards is { } restrictions
            ? Array.AsReadOnly(restrictions.Select(item => item with { CardIds = FreezeViewList(item.CardIds)! }).ToArray()) : null
    };

    private static IReadOnlyList<T>? FreezeViewList<T>(IReadOnlyList<T>? values) =>
        values is null ? null : values.Count == 0 ? EmptyViewList<T>.Value : Array.AsReadOnly(values.ToArray());

    private static class EmptyViewList<T>
    {
        internal static readonly IReadOnlyList<T> Value = Array.AsReadOnly(Array.Empty<T>());
    }

    private static IReadOnlyDictionary<TKey, TValue>? FreezeViewDictionary<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue>? values) where TKey : notnull => values is null ? null :
        values.Count == 0 ? System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>.Empty :
            new System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>(values.ToDictionary(item => item.Key, item => item.Value));

    private static PlayerSnapshot FreezePlayer(PlayerSnapshot player) => player with
    {
        OriginalHandEntities = player.OriginalHandEntities is { } originalHand
            ? Array.AsReadOnly(originalHand.Select(item => item with { CardIds = FreezeViewList(item.CardIds) }).ToArray()) : null,
        SourceCurses = FreezeViewList(player.SourceCurses),
        Hand = FreezeViewList(player.Hand)!,
        Equipment = FreezeViewList(player.Equipment)!,
        Judgment = FreezeViewList(player.Judgment)!,
        PrivateReserveCards = FreezeViewList(player.PrivateReserveCards),
        NamedAmbushPiles = player.NamedAmbushPiles is { } ambushes
            ? Array.AsReadOnly(ambushes.Select(p => p with { Entries = FreezeViewList(p.Entries) }).ToArray()) : null,
        PrivateWhirlPiles = player.PrivateWhirlPiles is { } whirls
            ? Array.AsReadOnly(whirls.Select(p => p with { Cards = FreezeViewList(p.Cards) }).ToArray()) : null,
        Markers = FreezeViewList(player.Markers),
        ActiveEffects = FreezeViewList(player.ActiveEffects),
        WoodenOxGrain = FreezeViewList(player.WoodenOxGrain),
        BuquWounds = FreezeViewList(player.BuquWounds),
        AuthorityCards = FreezeViewList(player.AuthorityCards),
        ChunlaoCards = FreezeViewList(player.ChunlaoCards),
        PublicDeferredPileCards = FreezeViewList(player.PublicDeferredPileCards),
        PublicPersistentPileCards = FreezeViewList(player.PublicPersistentPileCards),
        PublicPersistentPiles = player.PublicPersistentPiles is { } piles
            ? Array.AsReadOnly(piles.Select(pile => pile with { Cards = FreezeViewList(pile.Cards)! }).ToArray()) : null,
        PrivateGeneralLibraries = player.PrivateGeneralLibraries is {} libraries ? Array.AsReadOnly(libraries.Select(l => l with { GeneralIds = FreezeViewList(l.GeneralIds) }).ToArray()) : null,
        PrivateTurnHolds = player.PrivateTurnHolds is { } holds ? Array.AsReadOnly(holds.Select(h => h with { Cards = FreezeViewList(h.Cards) }).ToArray()) : null,
        PojunHoldCards = FreezeViewList(player.PojunHoldCards),
        EquipmentSlotCapacities = FreezeViewDictionary(player.EquipmentSlotCapacities),
        ConfiguredConversionTiers = FreezeViewDictionary(player.ConfiguredConversionTiers),
        AlternatingChoiceStates = FreezeViewList(player.AlternatingChoiceStates),
        BeneficiarySuitShields = FreezeViewList(player.BeneficiarySuitShields),
        DeferredHandAlignments = FreezeViewList(player.DeferredHandAlignments),
        TurnHandCategoryRestrictions = FreezeViewList(player.TurnHandCategoryRestrictions),
        TurnHandLimitCardKindExemptions = player.TurnHandLimitCardKindExemptions is { } kindExemptions ?
            Array.AsReadOnly(kindExemptions.Select(policy => policy with
                { CardKinds = FreezeViewList(policy.CardKinds)! }).ToArray()) : null,
        TurnSlashSuitAllowances = FreezeViewList(player.TurnSlashSuitAllowances),
        FirstRoundGameUsageRefunds = FreezeViewList(player.FirstRoundGameUsageRefunds),
        IssuedPlayPhaseUseProhibitions = FreezeViewList(player.IssuedPlayPhaseUseProhibitions),
        IssuedPlayPhaseSuitUseAllowances = player.IssuedPlayPhaseSuitUseAllowances is null ? null :
            Array.AsReadOnly(player.IssuedPlayPhaseSuitUseAllowances.Select(a => a with { Suits = Array.AsReadOnly(a.Suits.ToArray()) }).ToArray()),
        Skills = player.Skills is { } skills ? Array.AsReadOnly(skills.Select(FreezeViewSkill).ToArray()) : null,
        SecondarySkills = player.SecondarySkills is { } secondary ? Array.AsReadOnly(secondary.Select(FreezeViewSkill).ToArray()) : null,
        SkillRuntimeStates = player.SkillRuntimeStates is { } states ? Array.AsReadOnly(states.Select(state => state with
        {
            Usages = FreezeViewList(state.Usages)!,
            BooleanStates = FreezeViewList(state.BooleanStates),
            PublicRuleStates = FreezeViewList(state.PublicRuleStates),
            DirectedPolicies = state.DirectedPolicies is { } policies ? Array.AsReadOnly(policies.Select(policy => policy with
                { CardKinds = FreezeViewList(policy.CardKinds)! }).ToArray()) : null,
            ActionProhibitions = state.ActionProhibitions is { } prohibitions ? Array.AsReadOnly(prohibitions.Select(policy => policy with
            {
                CardKinds = FreezeViewList(policy.CardKinds)!,
                ActionTypes = FreezeViewList(policy.ActionTypes)!,
                Suits = FreezeViewList(policy.Suits)
            }).ToArray()) : null
        }).ToArray()) : null
    };

    private static GeneralSkillDefinition FreezeViewSkill(GeneralSkillDefinition skill) => skill with
    {
        SelectionWeights = FreezeViewDictionary(skill.SelectionWeights),
        ViewAsOpportunities = skill.ViewAsOpportunities is { } opportunities
            ? Array.AsReadOnly(opportunities.Select(item => item with
            {
                InputKinds = FreezeViewList(item.InputKinds)!,
                InputSuits = FreezeViewList(item.InputSuits)!
            }).ToArray()) : null
    };

}
