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
        var snapshots = _players.Select(player =>
        {
            var playerHand = GetHand(player);
            var canSeeRole = !IsNationalWarMode &&
                             (revealAll ||
                              IsTeamMode ||
                              player.Role == Role.Lord ||
                              player.RoleRevealed ||
                              player.Seat == viewerSeat);
            var canSeeHand = revealAll || player.Seat == viewerSeat;
            var hand = canSeeHand
                ? playerHand.Select(ToSnapshot).ToArray()
                : [];
            var equipment = GetEquipment(player)
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
            var canSeeSecondaryGeneral = player.SecondaryGeneralSelected &&
                                         (revealAll ||
                                          player.SecondaryGeneralRevealed ||
                                          player.Seat == viewerSeat);
            var secondaryGeneral = canSeeSecondaryGeneral ? player.SecondaryGeneral : null;
            var effectiveFactionId = GetEffectiveFactionId(player);
            var hasIdentityGodFaction = SupportsGodFactionSelection &&
                                        string.Equals(player.General.FactionId, "god", StringComparison.Ordinal) &&
                                        player.ChosenFactionId is not null;
            var canSeeFaction = effectiveFactionId is not null &&
                                (IsNationalWarMode
                                    ? revealAll || player.FactionRevealed || player.Seat == viewerSeat
                                    : (hasIdentityGodFaction || GetPrivateGeneralLibraryFaction(player) is not null) &&
                                      (revealAll || player.GeneralRevealed || player.Seat == viewerSeat));
            return new PlayerSnapshot(
                player.Seat,
                player.Name,
                player.IsHuman,
                canSeeRole ? player.Role : null,
                !IsNationalWarMode && (player.RoleRevealed || player.Role == Role.Lord),
                general.Id,
                general.Name,
                general.PortraitKey,
                player.Hp,
                player.MaxHp,
                player.IsAlive,
                playerHand.Count,
                hand,
                player.GeneralSelected && player.GeneralRevealed,
                player.HasAlcoholEffect)
            {
                IsFaceDown = player.IsFaceDown,
                TeamId = IsTeamMode && player.TeamRevealed ? player.TeamId : null,
                IsTeamRevealed = IsTeamMode && player.TeamRevealed,
                Equipment = Array.AsReadOnly(equipment),
                IsEquipmentAreaAbolished = player.EquipmentAreaAbolished,
                EquipmentSlotCapacities = player.EquipmentSlotCapacities.Count > 0
                    ? new Dictionary<EquipmentSlot, int>(player.EquipmentSlotCapacities) : null,
                ConfiguredConversionTiers = GetConfiguredConversionTierSnapshot(player),
                AlternatingChoiceStates = GetAlternatingChoiceStateSnapshot(player),
                BeneficiarySuitShields = _beneficiarySuitShields.Any(s => s.BeneficiarySeat == player.Seat) ? _beneficiarySuitShields.Where(s => s.BeneficiarySeat == player.Seat).ToArray() : null,
                DeferredHandAlignments = _deferredHandAlignments.Where(d => d.TargetSeat == player.Seat).ToArray() is { Length: > 0 } alignments ? alignments : null,
                TurnHandCategoryRestrictions = GetTurnHandCategoryRestrictionSnapshot(player.Seat),
                TurnHandLimitCardKindExemptions = GetTurnHandLimitCardKindExemptionsSnapshot(player.Seat),
                TurnSlashSuitAllowances = GetTurnSlashSuitAllowancesSnapshot(player.Seat),
                FirstRoundGameUsageRefunds = GetFirstRoundGameUsageRefundsSnapshot(player.Seat),
                ActualPlayPhaseCardUseState = TracksActualPlayPhaseCardUses && _phase == TurnPhase.Play && player.Seat == _currentSeat ? new(player.Seat, _turnNumber, _cardUseDebitPhaseInstanceId, GetActualPlayPhaseUseCount(player.Seat)) : null,
                IssuedPlayPhaseUseProhibitions = _issuedPlayPhaseUseProhibitions.Where(p => p.ActorSeat == player.Seat && HasIssuedPlayPhaseUseBan(player.Seat)).ToArray() is { Length: > 0 } issuedBans ? issuedBans : null,
                IssuedPlayPhaseSuitUseAllowances = GetIssuedPlayPhaseSuitUseAllowances(player.Seat),
                WoodenOxGrainCount = woodenOxGrain.Count,
                WoodenOxGrain = GetEquipment(player).Any(card => card.Kind == CardKind.WoodenOx) || woodenOxGrain.Count > 0
                    ? canSeeHand
                        ? Array.AsReadOnly(woodenOxGrain.Select(ToSnapshot).ToArray())
                        : Array.Empty<CardSnapshot>()
                    : null,
                IsChained = player.IsChained,
                Judgment = Array.AsReadOnly(judgment),
                IsJudgmentAreaAbolished = player.JudgmentAreaAbolished,
                BuquWounds = HasProgramSkill(player, "classic:buqu") || buquWounds.Count > 0
                    ? Array.AsReadOnly(buquWounds.Select(ToSnapshot).ToArray())
                    : null,
                AuthorityCount = authority.Count,
                PublicPersistentPileCards = PublicPileCards(player.Seat).Count > 0 ? PublicPileCards(player.Seat).Select(ToSnapshot).ToArray() : null,
                PublicPersistentPileCount = PublicPileCards(player.Seat).Count,
                PublicPersistentPileName = SinglePublicPileSource(player.Seat) is { } publicPile ? _contentRegistry.GetSkill(publicPile.SkillId).ProgramPresentation?.AuthorityName : null,
                PublicPersistentPileSkillId = SinglePublicPileSource(player.Seat)?.SkillId,
                PublicPersistentPiles = CreatePublicPersistentPileSnapshots(player.Seat),
                PrivateGeneralLibraries = ProjectPrivateGeneralLibraries(player,viewerSeat,revealAll),
                PrivateTurnHolds = ProjectPrivateTurnHolds(player.Seat, viewerSeat, revealAll),
                PublicDeferredPileName = _deferredPublicPileDeposits.FirstOrDefault(item => item.OwnerSeat == player.Seat) is { } deposit ? _contentRegistry.GetSkill(deposit.SkillId).ProgramPresentation?.AuthorityName : null,
                PublicDeferredPileCount = _cardZones.CardsAt(new CardLocation(CardZoneKind.PublicDeferredPile, player.Seat)).Count,
                PublicDeferredPileCards = _cardZones.CardsAt(new CardLocation(CardZoneKind.PublicDeferredPile, player.Seat)).Count > 0 ? Array.AsReadOnly(_cardZones.CardsAt(new CardLocation(CardZoneKind.PublicDeferredPile, player.Seat)).Select(ToSnapshot).ToArray()) : null,
                AuthorityName = canSeeGeneral
                    ? OwnedRuntimeSkills(player, general, includeAcquired: true)
                        .Select(skill => skill.ContentId is { } contentId
                            ? _contentRegistry.Skills.GetValueOrDefault(contentId)?.ProgramPresentation?.AuthorityName : null)
                        .FirstOrDefault(name => name is not null)
                    : null,
                PrivateReserveCount = _cardZones.CardsAt(new CardLocation(CardZoneKind.PrivateReserve, player.Seat)).Count,
                PrivateReserveCards = canSeeHand && (HasProgramPersistentZone(player, CardZoneKind.PrivateReserve) ||
                    _cardZones.CardsAt(new CardLocation(CardZoneKind.PrivateReserve, player.Seat)).Count > 0)
                    ? _cardZones.CardsAt(new CardLocation(CardZoneKind.PrivateReserve, player.Seat))
                    .Select(ToSnapshot).ToArray() : null,
                AuthorityCards = Array.AsReadOnly(authority.Select(ToSnapshot).ToArray()),
                ChunlaoCount = chunlao.Count,
                ChunlaoCards = HasProgramPersistentZone(player, CardZoneKind.Chunlao) || chunlao.Count > 0
                    ? Array.AsReadOnly(chunlao.Select(ToSnapshot).ToArray())
                    : null,
                PojunHoldCount = pojunHold.Count,
                PojunHoldCards = pojunHold.Count > 0
                    ? Array.AsReadOnly(pojunHold.Select(ToSnapshot).ToArray())
                    : null,
                Markers = player.Markers.Count > 0
                    ? Array.AsReadOnly(player.Markers
                        .OrderBy(marker => marker.Key)
                        .Select(marker => new PlayerMarkerSnapshot(
                            marker.Key,
                            PlayerMarkerCatalog.GetDisplayName(marker.Key),
                            marker.Value))
                        .ToArray())
                    : null,
                FactionId = canSeeFaction ? effectiveFactionId : null,
                IsFactionRevealed = IsNationalWarMode
                    ? player.FactionRevealed
                    : (hasIdentityGodFaction || GetPrivateGeneralLibraryFaction(player) is not null) && player.GeneralRevealed,
                SecondaryGeneralId = secondaryGeneral?.Id,
                SecondaryGeneralName = secondaryGeneral?.Name,
                SecondaryPortraitKey = secondaryGeneral?.PortraitKey,
                IsSecondaryGeneralPublic = IsNationalWarMode && player.SecondaryGeneralRevealed,
                Skills = canSeeGeneral
                    ? Array.AsReadOnly(OwnedRuntimeSkills(player, general, includeAcquired: true)
                        .Select(skill => skill with
                        {
                            Description = skill.Description
                        })
                        .ToArray())
                    : null,
                SecondarySkills = secondaryGeneral is not null
                    ? Array.AsReadOnly(OwnedRuntimeSkills(player, secondaryGeneral, includeAcquired: false)
                        .Select(skill => skill with
                        {
                            Description = skill.Description
                        })
                        .ToArray())
                    : null,
                SkillRuntimeStates = canSeeGeneral
                    ? Array.AsReadOnly(OwnedRuntimeSkills(player, general, includeAcquired: true)
                        .Select(skill => skill.ContentId)
                        .OfType<string>()
                        .Distinct(StringComparer.Ordinal)
                        .Select(skillId => CreateProgramAwareSkillStateSnapshot(player, skillId))
                        .ToArray())
                    : null
            };
        }).ToArray();

        var visibleDecision = _pendingDecision?.PlayerSeat == viewerSeat
            ? _pendingDecision
            : null;
        var programRevealedCards = GetProgramPublicCards();
        if (_resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.PublicPileDraft is { Stage: "pile" or "distribute" } pileDraft)
            programRevealedCards = programRevealedCards.Concat(PublicPileDraftCards(pileDraft).Select(ToSnapshot)).DistinctBy(card => card.Id).ToArray();
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { InstructionIndex: > 0 } pileFrame &&
            ProgramInstructionResolver.Default.Resolve(pileFrame,_contentRegistry.GetSkill(pileFrame.SkillId).Program!).GetPausedInstruction(pileFrame.InstructionIndex).Effect.Op == SkillProgramEffectOp.ObtainPublicPileCard)
            programRevealedCards = programRevealedCards.Concat(ReferencedPublicPileSources(pileFrame.OwnerSeat, ProgramInstructionResolver.Default.Resolve(pileFrame, _contentRegistry.GetSkill(pileFrame.SkillId).Program!).GetPausedInstruction(pileFrame.InstructionIndex).Effect.SkillIds.Single(), pileFrame.SkillInstanceId).SelectMany(PublicPileCards).Select(ToSnapshot)).DistinctBy(card=>card.Id).ToArray();
        var publicRevealedCards = _resolutionStack.LastOrDefault() is PindianFrame { Result: { } contest }
            ? _cardZones.CardsAt(CardLocation.Processing)
                .Where(card => card.Id == contest.SourceCardId || card.Id == contest.OpponentCardId)
                .Select(ToSnapshot).ToArray()
            : programRevealedCards.Count > 0
            ? programRevealedCards.ToArray()
            : ActiveGroupCard is { Effect: GroupCardEffect.PublicDraft } publicDraft
            ? publicDraft.RevealedCardIds
                .Select(cardId => _cardZones.CardsAt(CardLocation.Processing)
                    .Single(card => card.Id == cardId))
                .Select(ToSnapshot)
                .ToArray()
            : ActiveFireAttack is { FireAttackSelection.RevealedCardId: not null } fireAttack
                ? [ToSnapshot(GetFireAttackRevealedCard(fireAttack))]
                : Array.Empty<CardSnapshot>();

        if (_resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.AlternatingSuitTop is { Mode: "completed", Stage: "order" } orderedCosts)
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
            _cardZones.Count(CardLocation.Processing),
            _revision)
        {
            CardDeclarations = GetCardDeclarationSnapshots(viewerSeat, revealAll),
            WinnerTeamId = IsTeamMode ? _winnerTeamId : null,
            WinnerFactionId = IsNationalWarMode ? _winnerFactionId : null,
            ModeKind = _modeDefinition.ModeKind,
            PublicRevealedCards = Array.AsReadOnly(publicRevealedCards),
            PrivateRevealedCards = GetPrivatelyViewedCards(viewerSeat) is { Length: > 0 } privateCards ? Array.AsReadOnly(privateCards) : null,
            ProgramResponseExchangeStates = GetResponseExchangeStateSnapshots(),
            TurnProhibitedPhysicalCards = GetResponseEntityRestrictionSnapshots()
        });
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
        ProgramResponseExchangeStates = FreezeViewList(snapshot.ProgramResponseExchangeStates),
        TurnProhibitedPhysicalCards = snapshot.TurnProhibitedPhysicalCards is { } restrictions
            ? Array.AsReadOnly(restrictions.Select(item => item with { CardIds = FreezeViewList(item.CardIds)! }).ToArray()) : null
    };

    private static IReadOnlyList<T>? FreezeViewList<T>(IReadOnlyList<T>? values) =>
        values is null ? null : Array.AsReadOnly(values.ToArray());

    private static IReadOnlyDictionary<TKey, TValue>? FreezeViewDictionary<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue>? values) where TKey : notnull => values is null ? null :
        new System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>(values.ToDictionary(item => item.Key, item => item.Value));

    private static PlayerSnapshot FreezePlayer(PlayerSnapshot player) => player with
    {
        Hand = FreezeViewList(player.Hand)!,
        Equipment = FreezeViewList(player.Equipment)!,
        Judgment = FreezeViewList(player.Judgment)!,
        PrivateReserveCards = FreezeViewList(player.PrivateReserveCards),
        Markers = FreezeViewList(player.Markers),
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
