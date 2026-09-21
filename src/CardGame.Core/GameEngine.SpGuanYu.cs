namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SpGuanYuWushengSkillId = "sp:guan-yu-wusheng";
    private const string DanjiSkillId = "sp:danji";
    private const string SpGuanYuMashuSkillId = "sp:guan-yu-mashu";
    private const string NuzhanSkillId = "sp:nuzhan";
    private const string AwakeningUsageId = "awakening";

    private void ResolveDanjiAwakening(PlayerRuntime player)
    {
        if (!SupportsRuntimeSkillAcquisition ||
            !HasRuntimeSkill(player, DanjiSkillId) ||
            _skillRuntimeState.GetUsage(
                player.Seat,
                DanjiSkillId,
                AwakeningUsageId,
                SkillUsageScope.Game) != 0 ||
            GetHand(player).Count <= player.Hp ||
            IsLiuBeiLord())
        {
            return;
        }

        if (!_skillRuntimeState.TryConsumeUsage(
                player.Seat,
                DanjiSkillId,
                AwakeningUsageId,
                SkillUsageScope.Game,
                limit: 1))
        {
            throw new InvalidOperationException("Danji awakening was consumed twice.");
        }

        player.MaxHp = Math.Max(1, player.MaxHp - 1);
        player.Hp = Math.Min(player.Hp, player.MaxHp);
        QueueGameEvent(new MaximumHpChangedEvent(
            player.Seat,
            Delta: -1,
            player.MaxHp,
            DanjiSkillId));

        var acquired = AcquireRuntimeSkills(
            player,
            DanjiSkillId,
            [SpGuanYuMashuSkillId, NuzhanSkillId]);
        QueueGameEvent(new SkillAwakenedEvent(
            player.Seat,
            DanjiSkillId,
            player.MaxHp,
            acquired));
        AddLog(
            "SkillTriggered",
            $"{player.Name} 的【单骑】觉醒：减1点体力上限，获得【马术】和【怒斩】。",
            player.Seat);
    }

    private IReadOnlyList<string> AcquireRuntimeSkills(
        PlayerRuntime player,
        string sourceSkillId,
        IReadOnlyList<string> skillIds)
    {
        if (_contentRegistry is null)
            throw new InvalidOperationException("Runtime skills require an active content registry.");

        var acquired = new List<string>();
        foreach (var skillId in skillIds.Distinct(StringComparer.Ordinal))
        {
            _ = _contentRegistry.GetSkill(skillId);
            if (!EnabledContentSkillIds(player).Contains(skillId, StringComparer.Ordinal))
                acquired.Add(skillId);
        }
        if (acquired.Count == 0) return [];

        player.AcquiredSkillIds.AddRange(acquired);
        foreach (var skillId in acquired)
            RegisterTaggedConversionSkill(player, skillId);
        QueueGameEvent(new SkillsAcquiredEvent(
            player.Seat,
            sourceSkillId,
            Array.AsReadOnly(acquired.ToArray())));
        return Array.AsReadOnly(acquired.ToArray());
    }

    private bool IsLiuBeiLord()
    {
        var lord = _players.SingleOrDefault(player => player.Role == Role.Lord);
        return lord is not null && lord.General.Id is "classic:liu-bei" or "standard:liu-bei" or "liu-bei";
    }

    private bool IgnoresSpGuanYuWushengDistance(PlayerRuntime player, Card card) =>
        SupportsRuntimeSkillAcquisition &&
        HasRuntimeSkill(player, SpGuanYuWushengSkillId) &&
        card.Suit == Suit.Diamond;

    private NuzhanModifiers GetNuzhanModifiers(long frameId, PlayerRuntime source)
    {
        if (!SupportsRuntimeSkillAcquisition || !HasRuntimeSkill(source, NuzhanSkillId))
            return default;

        var action = _resolutionStack
            .OfType<CardUseFrame>()
            .Single(frame => frame.Id == frameId)
            .Action;
        if (action is null ||
            action.EffectiveKind != CardKind.Slash ||
            action.PhysicalCards.Count != 1 ||
            !action.ConversionChain.Any(conversion =>
                conversion.OwnerSeat == source.Seat &&
                string.Equals(conversion.SkillId, SpGuanYuWushengSkillId, StringComparison.Ordinal)))
        {
            return default;
        }

        var physical = action.PhysicalCards[0];
        var modifiers = new NuzhanModifiers(
            IgnoresSlashLimit: CardCatalog.Get(physical.CardKind).CategoryName == "锦囊牌",
            DamageBonus: EquipmentCatalog.IsEquipment(physical.CardKind) ? 1 : 0,
            PhysicalCardId: physical.CardId);
        if (!modifiers.IgnoresSlashLimit && modifiers.DamageBonus == 0) return default;

        QueueGameEvent(new NuzhanAppliedEvent(
            frameId,
            source.Seat,
            modifiers.PhysicalCardId,
            modifiers.IgnoresSlashLimit,
            modifiers.DamageBonus));
        AddLog(
            "SkillTriggered",
            modifiers.IgnoresSlashLimit
                ? $"{source.Name} 的【怒斩】令此【杀】不计入出牌阶段次数。"
                : $"{source.Name} 的【怒斩】令此【杀】的伤害值+1。",
            source.Seat);
        return modifiers;
    }

    private void AddNuzhanUnlimitedTrickSlashActions(
        ICollection<LegalAction> actions,
        PlayerRuntime actor,
        IReadOnlyList<Card> playableCards)
    {
        if (!SupportsRuntimeSkillAcquisition || !HasRuntimeSkill(actor, NuzhanSkillId)) return;

        foreach (var converted in playableCards.Where(card =>
                     CardCatalog.Get(card.Kind).CategoryName == "锦囊牌"))
        {
            var source = GetLegacyViewAsConversions(
                    actor,
                    converted,
                    CardKind.Slash,
                    forResponse: false)
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.SkillId, SpGuanYuWushengSkillId, StringComparison.Ordinal));
            if (source is null) continue;

            var targets = GetFangtianOrderedSlashTargets(
                actor,
                converted,
                source,
                ignoresSlashLimit: true);
            foreach (var target in targets)
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Slash,
                    converted.Id,
                    target.Seat,
                    DescribeConversion(source,
                        $"将【{converted.DisplayName}】当作【杀】对 {target.Name} 使用（【怒斩】不计次数）"),
                    PlayedCardKind: CardKind.Slash)
                {
                    ConversionSource = source
                });
            }

            AddFangtianHalberdSlashActions(
                actions,
                actor,
                converted,
                targets,
                "杀",
                CardKind.Slash,
                source);
            AddTianyiSlashActions(
                actions,
                actor,
                converted,
                targets,
                "杀",
                CardKind.Slash,
                source);
        }
    }

    private readonly record struct NuzhanModifiers(
        bool IgnoresSlashLimit,
        int DamageBonus,
        int PhysicalCardId);
}
