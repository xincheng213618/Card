namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LihuoSkillId = "classic:lihuo";

    private bool UsesFormalLihuo =>
        _rulesVersion >= 113 &&
        IsClassicIdentityMode &&
        _contentRegistry?.Packages.Any(package =>
            package.Id == "standard-classic-generals" &&
            package.Version >= new Version(1, 91, 0)) == true;

    private bool HasLihuo(PlayerRuntime owner) =>
        UsesFormalLihuo && HasRuntimeSkill(owner, LihuoSkillId);

    private CardConversionSource CreateLihuoConversionSource(PlayerRuntime owner) =>
        new(
            LihuoSkillId,
            "ordinary-slash-to-fire-slash",
            owner.Seat,
            $"seat-{owner.Seat}:{LihuoSkillId}");

    private IReadOnlyList<SlashUseVariant> GetSlashUseVariants(
        PlayerRuntime actor,
        CardKind baseEffectiveKind)
    {
        var variants = new List<SlashUseVariant>
        {
            new(baseEffectiveKind, CardKindModifierSkill: null, UsesZhuqueFan: false)
        };
        if (baseEffectiveKind != CardKind.Slash)
        {
            return variants;
        }

        if (HasLihuo(actor))
        {
            variants.Add(new SlashUseVariant(
                CardKind.FireSlash,
                SkillKind.Lihuo,
                UsesZhuqueFan: false));
        }
        if (HasZhuqueFan(actor))
        {
            variants.Add(new SlashUseVariant(
                CardKind.FireSlash,
                CardKindModifierSkill: null,
                UsesZhuqueFan: true));
        }
        return variants;
    }

    private void AddLihuoSlashActions(
        ICollection<LegalAction> actions,
        PlayerRuntime actor,
        Card physicalCard,
        IReadOnlyList<PlayerRuntime> legalTargets,
        string slashName,
        CardKind? playedCardKind,
        CardConversionSource? conversionSource = null,
        SkillKind? cardKindModifierSkill = null)
    {
        var effectiveKind = playedCardKind ?? physicalCard.Kind;
        if (effectiveKind != CardKind.FireSlash || !HasLihuo(actor))
        {
            return;
        }

        var usesFangtian = UsesFormalFangtianHalberd &&
                           GetHand(actor).Count == 1 &&
                           GetHand(actor)[0].Id == physicalCard.Id &&
                           GetEquipment(actor).Any(card => card.Kind == CardKind.FangtianHalberd);
        var existingMaximum = usesFangtian
            ? actor.TianyiWonThisTurn ? 4 : 3
            : actor.TianyiWonThisTurn ? 2 : 1;
        var targetCount = existingMaximum + 1;
        if (legalTargets.Count < targetCount)
        {
            return;
        }

        var orderedTargets = legalTargets
            .OrderBy(player => (player.Seat - actor.Seat + _playerCount) % _playerCount)
            .ToArray();
        AddCombinations(0, []);

        void AddCombinations(int startIndex, IReadOnlyList<PlayerRuntime> selected)
        {
            if (selected.Count == targetCount)
            {
                var targetSeats = Array.AsReadOnly(selected.Select(target => target.Seat).ToArray());
                var combinedEffects = new List<string>();
                if (usesFangtian) combinedEffects.Add("方天画戟");
                if (actor.TianyiWonThisTurn) combinedEffects.Add("天义");
                combinedEffects.Add("疠火");
                actions.Add(new LegalAction(
                    LegalActionKind.Slash,
                    physicalCard.Id,
                    targetSeats[0],
                    DescribeConversion(
                        conversionSource,
                        $"发动【{string.Join("】【", combinedEffects)}】，以【{slashName}】指定 {string.Join("、", selected.Select(target => target.Name))}"),
                    PlayedCardKind: playedCardKind,
                    TargetSeats: targetSeats)
                {
                    ConversionSource = conversionSource,
                    CardKindModifierSkill = cardKindModifierSkill,
                    TargetCountModifierSkill = SkillKind.Lihuo
                });
                return;
            }

            for (var index = startIndex;
                 index <= orderedTargets.Length - (targetCount - selected.Count);
                 index++)
            {
                AddCombinations(index + 1, [.. selected, orderedTargets[index]]);
            }
        }
    }

    private bool TryApplyLihuoPenaltyAfterCardUse(AttackResolution attack)
    {
        if (!attack.IsLihuoConversion ||
            !attack.LihuoCausedDamage ||
            attack.LihuoPenaltyApplied ||
            _status == EngineStatus.Completed)
        {
            return false;
        }

        attack.MarkLihuoPenaltyApplied();
        var owner = _players[attack.CardUserSeat];
        if (!owner.IsAlive)
        {
            return false;
        }

        owner.Hp = Math.Max(0, owner.Hp - 1);
        QueueGameEvent(new SkillHpLostEvent(
            attack.ResolutionId,
            owner.Seat,
            SkillKind.Lihuo,
            1,
            owner.Hp));
        AddLog(
            "SkillCost",
            $"{owner.Name} 以【疠火】转化的【火杀】造成过伤害，结算结束后失去1点体力。",
            owner.Seat);
        if (owner.Hp > 0)
        {
            return false;
        }

        BeginLihuoDying(attack, owner);
        return true;
    }

    private void BeginLihuoDying(AttackResolution attack, PlayerRuntime owner)
    {
        if (_pendingDying is not null)
        {
            throw new InvalidOperationException("Lihuo cannot open a second dying continuation.");
        }

        var responderSeats = Array.AsReadOnly(BuildDyingResponderSeats(owner.Seat).ToArray());
        var frameId = ++_resolutionSequence;
        _resolutionStack.Add(new DyingFrame(
            frameId,
            attack.ResolutionId,
            owner.Seat,
            KillerSeat: null,
            responderSeats,
            ResponderIndex: 0));
        _pendingDying = new DyingResolution(
            frameId,
            damageFrameId: null,
            attack,
            owner.Seat,
            killerSeat: null,
            responderSeats,
            attack.ResolutionId,
            DyingContinuation.Lihuo);
        QueueGameEvent(new PlayerDyingEvent(frameId, owner.Seat, KillerSeat: null));
        _status = EngineStatus.Running;
        if (!TryResolveBuqu(_pendingDying))
        {
            ExposeHumanDyingPrompt();
        }
    }

    private void CompleteLihuoAfterDying(DyingResolution dying)
    {
        var attack = dying.Attack ??
            throw new InvalidOperationException("A Lihuo dying continuation lost its Slash use.");
        CompleteAttackAfterCardResolution(attack);
    }

    private sealed record SlashUseVariant(
        CardKind EffectiveKind,
        SkillKind? CardKindModifierSkill,
        bool UsesZhuqueFan);
}
