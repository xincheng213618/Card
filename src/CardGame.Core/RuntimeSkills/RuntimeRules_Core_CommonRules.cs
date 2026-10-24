namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CommonRules
{
    internal static void ValidateOptions(GameOptions options)
    {
        if (!AiDecisionPolicy.IsSupported(options.AiPolicyVersion))
        {
            throw new ArgumentOutOfRangeException(nameof(options.AiPolicyVersion),
                $"Supported AI policy versions are 1 through {AiDecisionPolicy.CurrentVersion}.");
        }

        if (options.PlayerCount is not (4 or 5 or 6 or 8) &&
            !(options.ModeId == WushuangMode.ModeId && options.PlayerCount == 2) &&
            !(options.ShanheBattle is not null && options.PlayerCount is >= 2 and <= 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.PlayerCount),
                "The built-in adapters currently support 4, 5, 6, or 8 players.");
        }

        if (options.HumanSeat < -1 || options.HumanSeat >= options.PlayerCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.HumanSeat),
                $"HumanSeat must be -1 or a seat from 0 through {options.PlayerCount - 1}.");
        }

        if (options.HumanSeat == -1 && options.HumanRole is not null)
        {
            throw new ArgumentException("HumanRole must be null when HumanSeat is -1.", nameof(options));
        }

        if (options.MaxTurns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxTurns));
        }
        if (options.HumanSeats is { } humans &&
            (humans.Count == 0 || humans.Distinct().Count() != humans.Count ||
             humans.Any(seat => seat < 0 || seat >= options.PlayerCount) || !humans.Contains(options.HumanSeat)))
            throw new ArgumentException("HumanSeats must contain distinct valid seats including HumanSeat.", nameof(options));
        if (options.HumanSeats is not null && options.ConfirmOpeningHand)
            throw new ArgumentException("Multiplayer opening-hand rerolls are not supported.", nameof(options));
    }

    internal static void ValidateModeOptions(
        GameOptions options,
        ContentModeDefinition mode)
    {
        if (options.HumanSeats is not null && (mode.ModeKind != ContentModeKind.Identity || options.ShanheBattle is not null))
            throw new ArgumentException("Multiplayer currently supports identity tables only.", nameof(options));
        if (options.HumanGuanduRole is not null && mode.GuanduActivity is null)
            throw new ArgumentException("Guandu role is only valid in Guandu.", nameof(options.HumanGuanduRole));
        if (options.WenheAspirationRank is { } goal &&
            (mode.WenheActivity is null || options.HumanSeat < 0 || goal is not (1 or 2 or 4 or 6)))
            throw new ArgumentException("Wenhe aspiration requires a human seat in Wenhe and a rank of 1, 2, 4 or 6.", nameof(options));
        if (mode.ModeKind == ContentModeKind.Survival)
        {
            if (options.HumanRole is not null || options.HumanTeamId is not null)
                throw new ArgumentException("Independent survival contestants have neither identity choices nor teams.", nameof(options));
            return;
        }
        if (mode.ModeKind == ContentModeKind.Team)
        {
            if (options.HumanRole is not null)
            {
                throw new ArgumentException(
                    "HumanRole must be null in a public-team mode.",
                    nameof(options.HumanRole));
            }

            if (mode.TeamCounts is null || mode.TeamCounts.Count != (mode.DragonBoatActivity is null ? 2 : 4) ||
                mode.TeamCounts.Values.Sum() != options.PlayerCount)
            {
                throw new InvalidOperationException(
                    $"Mode '{mode.Id}' is not supported by the two-team adapter for {options.PlayerCount} players.");
            }

            if (options.HumanSeat < 0 && options.HumanTeamId is not null)
            {
                throw new ArgumentException(
                    "HumanTeamId requires a human seat.",
                    nameof(options.HumanTeamId));
            }

            if (options.HumanTeamId is not null &&
                !mode.TeamCounts.ContainsKey(options.HumanTeamId))
            {
                throw new ArgumentException(
                    $"Team '{options.HumanTeamId}' is not defined by mode '{mode.Id}'.",
                    nameof(options.HumanTeamId));
            }
            if (mode.BossActivity is { } boss && options.HumanTeamId is { } selectedTeam && selectedTeam != boss.PlayerTeamId)
                throw new ArgumentException("The local player must join the allied team in a boss activity.", nameof(options.HumanTeamId));
            if (mode.ShenwuActivity is { } shenwu && options.HumanTeamId is { } selectedShenwuTeam && selectedShenwuTeam != shenwu.PlayerTeamId)
                throw new ArgumentException("The local player must join the allied team in Shenwu.", nameof(options.HumanTeamId));
        }
        else if (mode.ModeKind == ContentModeKind.NationalWarLite)
        {
            if (options.HumanRole is not null)
            {
                throw new ArgumentException(
                    "HumanRole must be null in a national-war mode.",
                    nameof(options.HumanRole));
            }

            if (options.HumanTeamId is not null)
            {
                throw new ArgumentException(
                    "HumanTeamId is not valid in a national-war mode.",
                    nameof(options.HumanTeamId));
            }

            if (mode.FactionCounts is null ||
                mode.FactionCounts.Count < 2 ||
                mode.FactionCounts.Values.Sum() != options.PlayerCount)
            {
                throw new InvalidOperationException(
                    $"Mode '{mode.Id}' must define at least two factions totaling {options.PlayerCount} players.");
            }
        }
        else if (options.HumanTeamId is not null)
        {
            throw new ArgumentException(
                "HumanTeamId is only valid in a public-team mode.",
                nameof(options.HumanTeamId));
        }
    }

    internal static List<Role> CreateIdentityRoles(ContentModeDefinition mode, int playerCount)
    {
        var roles = new List<Role>();
        foreach (var role in Enum.GetValues<Role>())
        {
            if (!mode.RoleCounts.TryGetValue(role.ToString(), out var count))
            {
                continue;
            }

            if (count < 0)
            {
                throw new InvalidOperationException($"Mode '{mode.Id}' has a negative {role} count.");
            }

            for (var index = 0; index < count; index++)
            {
                roles.Add(role);
            }
        }

        var unknownRoles = mode.RoleCounts.Keys
            .Where(key => !Enum.TryParse<Role>(key, ignoreCase: false, out _))
            .ToArray();
        if (unknownRoles.Length > 0)
        {
            throw new InvalidOperationException(
                $"Mode '{mode.Id}' contains unknown role ids: {string.Join(", ", unknownRoles)}.");
        }

        if (roles.Count != playerCount || roles.Count(role => role == Role.Lord) != 1)
        {
            throw new InvalidOperationException(
                $"Mode '{mode.Id}' does not provide exactly one valid {playerCount}-seat identity distribution.");
        }

        return roles;
    }

    internal static string FormatRoleSummary(ContentModeDefinition mode)
    {
        var roleNames = new Dictionary<Role, string>
        {
            [Role.Lord] = "主公",
            [Role.Loyalist] = "忠臣",
            [Role.Rebel] = "反贼",
            [Role.Renegade] = "内奸"
        };
        return string.Join(
            "、",
            Enum.GetValues<Role>()
                .Where(role => mode.RoleCounts.GetValueOrDefault(role.ToString()) > 0)
                .Select(role => $"{mode.RoleCounts[role.ToString()]} {roleNames[role]}"));
    }

    internal static string FormatTeamSummary(ContentModeDefinition mode) =>
        string.Join(
            "、",
            mode.TeamCounts!
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Value} {CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.GetTeamName(entry.Key, mode)}"));

    internal static string FormatFactionSummary(ContentModeDefinition mode) =>
        string.Join(
            "、",
            mode.FactionCounts!
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Value} {CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.GetFactionName(entry.Key)}"));

    internal static string GetTeamName(string teamId, ContentModeDefinition? mode = null) => teamId switch
    {
        "team:cao" => "曹操军",
        "team:yuan" => "袁绍军",
        "team:blue" => "青队",
        "team:red" => "赤队",
        "team:wei" => "魏军",
        "team:shu" => "蜀军",
        "team:wu" => "吴军",
        "team:qun" => "群雄",
        "team:allies" => "联合军",
        Campaigns.ShanheBattleSetup.PlayerTeamId => "义军",
        Campaigns.ShanheBattleSetup.EnemyTeamId => "敌军",
        "team:boss" => mode?.HulaoActivity is not null ? "神吕布" : "鬼军",
        "team:beasts" => "凶兽",
        _ => teamId
    };

    internal static string GetFactionName(string factionId) => factionId switch
    {
        "wei" => "魏",
        "shu" => "蜀",
        "ambitious" => "野心家",
        "wu" => "吴",
        "qun" => "群",
        "jin" => "晋",
        _ => factionId
    };

    internal static bool IsDelayedCard(CardKind kind) =>
        kind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning;

    internal static bool IsOrdinaryTrick(CardKind kind) =>
        CardCatalog.Get(kind).CategoryName == "锦囊牌" && !CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsDelayedCard(kind);

    internal static bool IsDelayedJudgmentContinuation(JudgmentContinuationKind continuation) =>
        continuation is JudgmentContinuationKind.Indulgence or
            JudgmentContinuationKind.SupplyShortage or
            JudgmentContinuationKind.Lightning;

    internal static (string Reason, JudgmentContinuationKind Continuation) GetDelayedJudgmentInfo(
        CardKind kind) => kind switch
        {
            CardKind.Indulgence => (JudgmentReasons.Indulgence, JudgmentContinuationKind.Indulgence),
            CardKind.SupplyShortage => (JudgmentReasons.SupplyShortage, JudgmentContinuationKind.SupplyShortage),
            CardKind.Lightning => (JudgmentReasons.Lightning, JudgmentContinuationKind.Lightning),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The card is not a delayed judgment card.")
        };

    internal static int GetFireAttackTargetSeat(CardUseFrame pending) =>
        pending.TargetIndex >= 0 && pending.TargetIndex < CardGame.Core.RuntimeSkills.RuntimeRules_zhuge_guo_le_LeChengyanEffects.LeChengyanNativeTargets(pending).Count
            ? CardGame.Core.RuntimeSkills.RuntimeRules_zhuge_guo_le_LeChengyanEffects.LeChengyanNativeTargets(pending)[pending.TargetIndex]
            : throw new InvalidOperationException("FireAttack lost its current target.");

    internal static string GetSuitDisplayName(Suit? suit) => suit switch
    {
        Suit.Spade => "黑桃",
        Suit.Heart => "红桃",
        Suit.Club => "梅花",
        Suit.Diamond => "方片",
        _ => "未知花色"
    };

    internal static bool IsRedSuit(Suit suit) =>
        suit is Suit.Heart or Suit.Diamond;

    internal static bool IsLightningHit(Card card) =>
        card.Suit == Suit.Spade && card.Rank is >= 2 and <= 9;

    internal static int? GetNullificationTargetSeat(NullificationWindowFrame frame) =>
        frame.TargetSeats.Count == 1 ? frame.TargetSeats[0] : null;

    internal static bool IsPublicTargetZone(CardZoneKind zone) =>
        zone is CardZoneKind.Equipment or CardZoneKind.Judgment;

    internal static CardMoveReason GetTargetCardMoveReason(
        LegalActionKind actionKind,
        CardZoneKind sourceZone) =>
        (actionKind, sourceZone) switch
        {
            (LegalActionKind.Dismantlement, CardZoneKind.Hand) => CardMoveReasons.Dismantlement,
            (LegalActionKind.Dismantlement, CardZoneKind.Equipment) => CardMoveReasons.Dismantlement,
            (LegalActionKind.Dismantlement, CardZoneKind.Judgment) => CardMoveReasons.DismantlementJudgment,
            (LegalActionKind.Snatch, CardZoneKind.Hand) => CardMoveReasons.Snatch,
            (LegalActionKind.Snatch, CardZoneKind.Equipment) => CardMoveReasons.Snatch,
            (LegalActionKind.Snatch, CardZoneKind.Judgment) => CardMoveReasons.SnatchJudgment,
            (LegalActionKind.MantianGuohai, CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment) => new("card.mantian-guohai.obtain"),
            _ => throw new InvalidOperationException(
                $"{actionKind} cannot move a target card from {sourceZone}.")
        };

    internal static CardMoveReason GetTargetCardFinishedMoveReason(
        LegalActionKind actionKind,
        CardZoneKind sourceZone) =>
        (actionKind, sourceZone) switch
        {
            (LegalActionKind.Dismantlement, CardZoneKind.Hand) => CardMoveReasons.DismantlementFinished,
            (LegalActionKind.Dismantlement, CardZoneKind.Equipment) => CardMoveReasons.DismantlementFinished,
            (LegalActionKind.Dismantlement, CardZoneKind.Judgment) => CardMoveReasons.DismantlementJudgmentFinished,
            (LegalActionKind.Snatch, CardZoneKind.Hand) => CardMoveReasons.SnatchFinished,
            (LegalActionKind.Snatch, CardZoneKind.Equipment) => CardMoveReasons.SnatchFinished,
            (LegalActionKind.Snatch, CardZoneKind.Judgment) => CardMoveReasons.SnatchJudgmentFinished,
            (LegalActionKind.MantianGuohai, CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment) => new("card.mantian-guohai.obtain-finished"),
            _ => throw new InvalidOperationException(
                $"{actionKind} cannot finish a target card from {sourceZone}.")
        };

    internal static LegalAction? SelectPlayAction(
        IReadOnlyList<LegalAction> legalActions,
        Card card,
        IReadOnlyList<int> targets,
        CardKind? playedCardKind,
        int? targetCardId,
        CardConversionSource? conversionSource,
        IReadOnlyList<CardConversionSource>? additionalConversionSources)
    {
        var matching = legalActions
            .Where(candidate => candidate.Kind != LegalActionKind.Recast)
            .Where(candidate => candidate.TargetSeats.SequenceEqual(targets))
            .Where(candidate => candidate.TargetCardId == targetCardId)
            .Where(candidate => (candidate.PlayedCardKind ?? card.Kind) ==
                (playedCardKind ?? card.Kind))
            .Where(candidate => candidate.ConversionSource == conversionSource)
            .Where(candidate => CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.ConversionSourcesEqual(candidate.AdditionalConversionSources,
                additionalConversionSources))
            .ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    internal static bool ConversionSourcesEqual(
        IReadOnlyList<CardConversionSource>? left,
        IReadOnlyList<CardConversionSource>? right) =>
        (left is null || left.Count == 0) && (right is null || right.Count == 0) ||
        left is not null && right is not null && left.SequenceEqual(right);

    internal static bool IsSlashCard(CardKind kind) =>
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash;

    internal static bool IsNativeResponseCard(Card responseCard, CardKind requiredCardKind) =>
        CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.MatchesRequiredCard(responseCard.Kind, requiredCardKind);

    internal static CardKind? ReadResponseCardKind(PromptChoice choice) =>
        choice.Parameters.TryGetValue("response-card-kind", out var value) &&
        Enum.TryParse<CardKind>(value, ignoreCase: false, out var kind)
            ? kind
            : null;

    internal static bool MatchesRequiredCard(CardKind actual, CardKind required) =>
        required == CardKind.Slash ? CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(actual) : actual == required;

    internal static DamageNature GetDamageNature(CardKind kind) => kind switch
    {
        CardKind.FireSlash or CardKind.FireAttack => DamageNature.Fire,
        CardKind.ThunderSlash or CardKind.Lightning => DamageNature.Thunder,
        CardKind.ColdSlash => DamageNature.Cold,
        _ => DamageNature.Normal
    };

    internal static string GetSuitName(Suit suit) => suit switch
    {
        Suit.Spade => "黑桃",
        Suit.Heart => "红桃",
        Suit.Club => "梅花",
        Suit.Diamond => "方块",
        _ => suit.ToString()
    };

    internal static string GetDamageNatureLabel(DamageNature nature) => nature switch
    {
        DamageNature.Fire => "火焰",
        DamageNature.Thunder => "雷电",
        DamageNature.Cold => "冰",
        _ => string.Empty
    };

    internal static IReadOnlyList<PromptChoice> CreatePlayChoices(
        IReadOnlyList<LegalAction> legalActions)
    {
        var choices = new List<PromptChoice>(legalActions.Count);
        foreach (var action in legalActions)
        {
            if (action.Kind == LegalActionKind.UseProgramSkill)
            {
                choices.Add(CardGame.Core.RuntimeSkills.RuntimeRules_Core_SkillPrograms.CreateProgramPlayChoice(action));
                continue;
            }
            if (action.Kind == LegalActionKind.RevealGeneral)
            {
                var slot = action.GeneralSlot ?? throw new InvalidOperationException("A reveal action must identify a general slot.");
                choices.Add(new PromptChoice(new ChoiceId($"play.reveal.{slot}"), action.Description, [], [],
                    new Dictionary<string, string> { ["action"] = "reveal-general", ["general-slot"] = slot.ToString() }));
                continue;
            }
            if (action.Kind == LegalActionKind.UseEquipmentEffect)
            {
                var equipment = action.EquipmentKind ??
                    throw new InvalidOperationException("An equipment-effect prompt choice must identify its equipment.");
                choices.Add(new PromptChoice(
                    new ChoiceId($"play.equipment-effect.{equipment}"),
                    action.Description,
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "use-equipment-effect",
                        ["equipment"] = equipment.ToString(),
                        ["min-card-count"] = action.MinCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["max-card-count"] = action.MaxCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["min-target-count"] = action.MinTargetCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["max-target-count"] = action.MaxTargetCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }));
                continue;
            }

            if (action.Kind == LegalActionKind.EndPlay)
            {
                choices.Add(new PromptChoice(
                    new ChoiceId("play.end"),
                    action.Description,
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "end-play"
                    }));
                continue;
            }

            var cardId = action.CardId ??
                throw new InvalidOperationException("A playable prompt choice must reference a card.");
            var targets = action.TargetSeats;
            var actionName = action.Kind switch
            {
                LegalActionKind.Slash => "slash",
                LegalActionKind.Peach => "peach",
                LegalActionKind.Duel => "duel",
                LegalActionKind.DrawTwo => "draw-two",
                LegalActionKind.AllianceFeast => "alliance-feast",
                LegalActionKind.SowBeans => "sow-beans",
                LegalActionKind.TransplantWood => "transplant-wood",
                LegalActionKind.MantianGuohai => "mantian-guohai",
                LegalActionKind.ReturnSoul => "return-soul",
                LegalActionKind.StarShift => "star-shift",
                LegalActionKind.BeamSwap => "beam-swap",
                LegalActionKind.HandRedistribution => "hand-redistribution",
                LegalActionKind.WenheChaos => "wenhe-chaos",
                LegalActionKind.Zong => "zong",
                LegalActionKind.RealgarWine => "realgar-wine",
                LegalActionKind.SameBoat => "same-boat",
                LegalActionKind.StriveUpstream => "strive-upstream",
                LegalActionKind.AgainstCurrent => "against-current",
                LegalActionKind.Intoxication => "intoxication",
                LegalActionKind.StoreGrain => "store-grain",
                LegalActionKind.Reinforcements => "reinforcements",
                LegalActionKind.UnitedEffort => "united-effort",
                LegalActionKind.BarbarianAssault => "barbarian-assault",
                LegalActionKind.ArrowBarrage => "arrow-barrage",
                LegalActionKind.PeachGarden => "peach-garden",
                LegalActionKind.FiveGrains => "five-grains",
                LegalActionKind.Dismantlement => "dismantlement",
                LegalActionKind.Snatch => "snatch",
                LegalActionKind.FireAttack => "fire-attack",
                LegalActionKind.QizhengXiangsheng => "qizheng-xiangsheng",
                LegalActionKind.WumiaoFlood => "wumiao-flood",
                LegalActionKind.UnexpectedAssault => "unexpected-assault",
                LegalActionKind.Alcohol => "alcohol",
                LegalActionKind.Equip => "equip",
                LegalActionKind.IronChain => "iron-chain",
                LegalActionKind.Recast => "recast",
                LegalActionKind.Indulgence => "indulgence",
                LegalActionKind.SupplyShortage => "supply-shortage",
                LegalActionKind.Lightning => "lightning",
                LegalActionKind.BorrowedSword => "borrowed-sword",
                _ => throw new InvalidOperationException($"Unsupported prompt action {action.Kind}.")
            };
            var choiceId = targets.Count switch
            {
                0 => $"play.{actionName}.card-{cardId}",
                1 => $"play.{actionName}.card-{cardId}.target-{targets[0]}",
                _ => $"play.{actionName}.card-{cardId}.targets-{string.Join('-', targets)}"
            };
            if (action.TargetCardId is { } targetCardId)
            {
                choiceId += $".target-card-{targetCardId}";
            }
            if (action.PlayedCardKind == CardKind.FireSlash)
            {
                choiceId += ".as-FireSlash";
            }
            if (action.PlayedCardKind == CardKind.ThunderSlash)
                choiceId += ".as-ThunderSlash";
            if (action.ConversionSource is { } conversionChoiceSource)
            {
                choiceId += $".conversion-{conversionChoiceSource.SkillId}-{conversionChoiceSource.BindingId}";
            }
            if (action.AdditionalConversionSources is { Count: > 0 } additionalChoiceSources)
            {
                choiceId += $".additional-conversions-{string.Join('-', additionalChoiceSources.Select(source => $"{source.SkillId}-{source.BindingId}"))}";
            }

            var parameters = new Dictionary<string, string>
            {
                ["action"] = actionName
            };
            if (action.PlayedCardKind is { } playedCardKind)
            {
                parameters["played-card-kind"] = playedCardKind.ToString();
            }
            if (action.ConversionSource is { } conversionSource)
            {
                CardGame.Core.RuntimeSkills.RuntimeRules_Core_CardConversions.AddConversionParameters(parameters, conversionSource);
            }
            if (action.AdditionalConversionSources is { Count: > 0 } additionalConversionSources)
            {
                parameters["additional-conversion-count"] = additionalConversionSources.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                for (var index = 0; index < additionalConversionSources.Count; index++)
                {
                    var source = additionalConversionSources[index];
                    parameters[$"additional-conversion-{index}-skill-id"] = source.SkillId;
                    parameters[$"additional-conversion-{index}-binding-id"] = source.BindingId;
                    parameters[$"additional-conversion-{index}-owner-seat"] = source.OwnerSeat.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    parameters[$"additional-conversion-{index}-instance-id"] = source.SkillInstanceId;
                }
            }
            if (action.TargetCardId is { } targetCardIdParameter)
            {
                parameters["target-card-id"] = targetCardIdParameter.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }

            choices.Add(new PromptChoice(
                new ChoiceId(choiceId),
                action.Description,
                [cardId],
                targets,
                parameters));
        }

        return choices;
    }

    internal static bool IsBlackCardUse(IReadOnlyList<Card> physicalCards) =>
        physicalCards.Count > 0 &&
        physicalCards.All(card => card.Suit is Suit.Spade or Suit.Club);

    internal static bool CanYizhongNullify(IReadOnlyList<Card> physicalCards, bool hasArmor) =>
        !hasArmor && CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsBlackCardUse(physicalCards);

    internal static bool IsTrickCardDamage(CardKind kind) =>
        CardCatalog.Get(kind).CategoryName == "锦囊牌";

    internal static bool CanWuyanPreventDamage(
        bool isClassicIdentityMode,
        CardKind kind,
        bool sourceHasWuyan,
        bool targetHasWuyan) =>
        isClassicIdentityMode &&
        CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsTrickCardDamage(kind) &&
        (sourceHasWuyan || targetHasWuyan);

    internal static bool DamageObserverRidesOn(ResolutionFrame ride, ResolutionFrame beneath) =>
        ride is ProgramSkillFrame { WindowContext: { } context } && context.ParentFrameId == beneath.Id && beneath switch
        {
            CardsMovedTriggerWindowFrame => context.Window is SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived,
            HpChangedTriggerWindowFrame => context.Window is SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged,
            _ => false
        };

    internal static bool ProgramMovementReturnMatches(ProgramSkillFrame producer, CardsMovedTriggerWindowFrame movement,
        bool allowFactionRequestCost = false) =>
        movement.Id == movement.Batch.Id && movement.Batch.ParentFrameId == producer.Id &&
        !CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.HasAlternateProgramMovementReturn(movement, allowFactionRequestCost) &&
        (movement.ResumeProgramFrameId is null || movement.ResumeProgramFrameId == producer.Id) &&
        (movement.Batch.AwaitingProgramFrameId is null || movement.Batch.AwaitingProgramFrameId == producer.Id) &&
        (allowFactionRequestCost || movement.ResumeProgramFrameId == producer.Id || movement.Batch.AwaitingProgramFrameId == producer.Id ||
         movement.ResumeProgramFrameId is null && movement.Batch.AwaitingProgramFrameId is null && producer.PendingMovementContinuation is not null) &&
        movement.Batch.OriginOwnerSeat == producer.OwnerSeat && movement.Batch.OriginSkillId == producer.SkillId &&
        movement.Batch.OriginSkillInstanceId == producer.SkillInstanceId && movement.Batch.Movements.Count > 0;

    internal static bool ProgramHpReturnMatches(ProgramSkillFrame producer, HpChangedTriggerWindowFrame changed,
        bool allowFactionRequestCost = false) =>
        changed.Id == changed.Change.Id && changed.ResumeFrameId == producer.Id && changed.Change.ParentFrameId == producer.Id && changed.Change.Amount > 0 &&
        (changed.Continuation == PostEventContinuation.Program ||
         changed.Continuation == PostEventContinuation.AwaitedProgramMovement && producer.PendingMovementContinuation is not null ||
         allowFactionRequestCost && changed.Continuation == PostEventContinuation.FactionRequestCost);

    internal static bool HasAlternateProgramMovementReturn(CardsMovedTriggerWindowFrame movement,
        bool allowFactionRequestCost = false) =>
        movement.ResumeFriendDuelResponseFrameId is not null || movement.ResumeDeclarationFrameId is not null || movement.ResumeDrawFundedDistinctBasicFrameId is not null ||
        movement.ResumeRecoveryReplacementFrameId is not null || movement.ResumePaidCardUseFrameId is not null ||
        movement.ResumeSoleIncomingRangeDrawFrameId is not null ||
        movement.ResumeKemingEchoFrameId is not null ||
        movement.ResumeNativeCardEffectDrawFrameId is not null ||
        movement.ResumeWenheCardEffectFrameId is not null ||
        movement.ResumeGuanduCardEffectFrameId is not null ||
        movement.ResumeWumiaoFloodFrameId is not null || movement.ResumeDragonBoatCardEffectFrameId is not null ||
        movement.ResumeEquipmentRecastFrameId is not null || movement.ResumeColorFireAttackFrameId is not null ||
        movement.ResumeCounterspellPaymentFrameId is not null || movement.ResumeHistoricalEndingUseFrameId is not null || movement.ResumeGuoSiUseFrameId is not null || movement.ResumeDenglouUseFrameId is not null ||
        movement.ResumeRoundPileAlcoholUseFrameId is not null || movement.ResumeDrawPhaseObligationFrameId is not null || movement.ResumeManualDiscardFrameId is not null || movement.ResumeActualPhaseEndFrameId is not null ||
        movement.ResumeCardSupplyCompletionFrameId is not null || movement.ResumeResponseCompletionFrameId is not null ||
        movement.DeferredTurnEndReturn is not null || !allowFactionRequestCost && movement.ResumeFactionRequestCostFrameId is not null;

    internal static bool IsResolvedGroupPhysicalCardDestinationAllowed(
        CardLocation location,
        bool damageCardClaimed,
        IReadOnlyList<int> targetSeats) =>
        location == CardLocation.DiscardPile ||
        damageCardClaimed && location.Zone is CardZoneKind.DrawPile or CardZoneKind.Hand ||
        location is { Zone: CardZoneKind.Hand, OwnerSeat: { } ownerSeat } &&
        targetSeats.Contains(ownerSeat);

    internal static bool RecordClaimedGroupPhysicalCard(
        ISet<int> claimedCardIds,
        long groupResolutionId,
        IReadOnlyList<Card> groupPhysicalCards,
        long attackResolutionId,
        int claimedCardId)
    {
        ArgumentNullException.ThrowIfNull(claimedCardIds);
        ArgumentNullException.ThrowIfNull(groupPhysicalCards);
        if (groupResolutionId != attackResolutionId ||
            !groupPhysicalCards.Any(card => card.Id == claimedCardId))
            return false;
        return claimedCardIds.Add(claimedCardId);
    }

    internal static PendingDecision CloneDecision(PendingDecision decision) =>
        decision with
        {
            ValidCardIds = Array.AsReadOnly(decision.ValidCardIds.ToArray()),
            ValidTargetSeats = Array.AsReadOnly(decision.ValidTargetSeats.ToArray()),
            Choices = Array.AsReadOnly(decision.Choices.Select(PromptChoiceProjection.Freeze).ToArray()),
            ValidContentIds = Array.AsReadOnly(decision.ValidContentIds.ToArray()),
        };

    internal static GameCommand CloneCommand(GameCommand command) =>
        command switch
        {
            PlayCardCommand play => play with
            {
                TargetSeats = Array.AsReadOnly(play.TargetSeats.ToArray()),
                AdditionalConversionSources = play.AdditionalConversionSources is { } sources
                    ? Array.AsReadOnly(sources.ToArray())
                    : null
            },
            DiscardCardsCommand discard => discard with { CardIds = Array.AsReadOnly(discard.CardIds.ToArray()) },
            UseEquipmentEffectCommand equipment => equipment with
            {
                CardIds = Array.AsReadOnly(equipment.CardIds.ToArray()),
                TargetSeats = Array.AsReadOnly(equipment.TargetSeats.ToArray())
            },
            UseProgramSkillCommand program => program with
            {
                CardIds = Array.AsReadOnly(program.CardIds.ToArray()),
                TargetSeats = Array.AsReadOnly(program.TargetSeats.ToArray())
            },
            _ => command
        };

    internal static GeneralDefinition CreateHiddenGeneral() =>
        new(
            "",
            "未知武将",
            "",
            [new GeneralSkillDefinition("未知", "武将尚未公开。")]);

    internal static string GetWinnerName(Winner winner) => winner switch
    {
        Winner.LordAndLoyalists => "主公与忠臣",
        Winner.Rebels => "反贼",
        Winner.Renegade => "内奸",
        Winner.TeamA => "青队",
        Winner.TeamB => "赤队",
        Winner.NationalFactionA => "魏势力",
        Winner.NationalFactionB => "蜀势力",
        Winner.NationalFactionC => "独立势力",
        Winner.Draw => "无人",
        _ => "尚未决出"
    };
}
