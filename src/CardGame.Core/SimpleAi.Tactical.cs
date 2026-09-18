namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    private (double Score, string Reason) ScoreNationalRevealAction(
        GameSnapshot view,
        PlayerSnapshot self,
        LegalAction action)
    {
        if (view.ModeKind != ContentModeKind.NationalWarLite ||
            action.Kind != LegalActionKind.RevealGeneral)
        {
            return (0d, "不是国战明置动作。");
        }

        var skill = action.Skill ?? SkillKind.None;
        var hasLivingOpponent = view.Players.Any(player => player.IsAlive && player.Seat != Seat);
        var redConvertibleCount = self.Hand.Count(card =>
            IsRedCard(card.Suit) && !IsSlashCard(card.Kind));
        var nativeSlashCount = self.Hand.Count(card => IsSlashCard(card.Kind));
        var dodgeConvertibleCount = self.Hand.Count(card => IsSlashCard(card.Kind));
        var wounded = self.Hp < self.MaxHp;
        var handIsEmpty = self.HandCount == 0;

        var immediateValue = skill switch
        {
            SkillKind.Wusheng when hasLivingOpponent && redConvertibleCount > 0 =>
                46d + Math.Min(redConvertibleCount, 3) * 4d,
            SkillKind.Longdan when hasLivingOpponent && dodgeConvertibleCount > 0 =>
                38d + Math.Min(dodgeConvertibleCount, 3) * 4d,
            SkillKind.Paoxiao when nativeSlashCount >= 2 =>
                42d + Math.Min(nativeSlashCount - 1, 3) * 5d,
            SkillKind.Kongcheng when handIsEmpty => 56d,
            SkillKind.Jianxiong or SkillKind.Feedback or SkillKind.Yiji or
                SkillKind.Jieming or SkillKind.Yuanhu or SkillKind.Ganglie
                when wounded => 31d,
            SkillKind.Qingnang or SkillKind.Huichun when wounded => 28d,
            SkillKind.Kujin or SkillKind.Zhiheng when self.HandCount >= 2 => 24d,
            SkillKind.Mashu or SkillKind.Qicai => 20d,
            SkillKind.Yingzi => 18d,
            SkillKind.Guicai or SkillKind.Jijiu => 16d,
            _ => 4d
        };

        var exposureCost = self.FactionId is null || self.IsFactionRevealed ? 0d : 3d;
        var score = 24d + immediateValue - exposureCost;
        var opportunity = skill switch
        {
            SkillKind.Wusheng when redConvertibleCount > 0 =>
                $"当前有 {redConvertibleCount} 张红色非杀牌可转化为杀",
            SkillKind.Longdan when dodgeConvertibleCount > 0 =>
                $"当前有 {dodgeConvertibleCount} 张杀牌可转化为闪",
            SkillKind.Paoxiao when nativeSlashCount >= 2 =>
                $"当前有 {nativeSlashCount} 张杀，可突破本回合次数限制",
            SkillKind.Kongcheng when handIsEmpty => "当前空手，可立即获得空城的防护收益",
            _ when wounded => $"当前体力 {self.Hp}/{self.MaxHp}，受伤后触发类技能已有即时价值",
            _ => "当前没有确定的即时触发机会，保留明置信息成本"
        };
        var reason =
            $"明置{action.GeneralSlot switch { GeneralSelectionSlot.Primary => "主将", GeneralSelectionSlot.Secondary => "副将", _ => "武将" }}「{action.Description}」；{opportunity}；评分只使用自己的手牌、体力、公开对手和当前动作，不读取暗牌。";
        return (score, reason);
    }

    // Version 2 evaluates only the supplied viewer snapshot, legal actions and
    // accumulated public evidence. Version 1 remains unchanged for checkpoint replay.
    private (double Score, string Reason) ScoreTacticalAction(
        GameSnapshot view, PlayerSnapshot self, Role role, LegalAction action,
        IReadOnlyList<LegalAction> legalActions)
    {
        if (action.Kind == LegalActionKind.UseSkill)
        {
            if (action.Skill == SkillKind.Fanjian)
            {
                var target = view.Players
                    .Where(player => player.IsAlive && player.Seat != Seat)
                    .OrderByDescending(player => GetHostility(view, role, player))
                    .ThenBy(player => player.Hp)
                    .ThenBy(player => player.Seat)
                    .FirstOrDefault();
                if (target is null)
                    return (-100d, "没有其他存活角色，不能发动反间。");

                var hostility = GetHostility(view, role, target);
                return hostility > 0
                    ? (22d + hostility * .45d + (target.Hp <= 1 ? 14d : 0d),
                        $"对公开判断中最敌对的座位 {target.Seat + 1} 发动反间；花色选择前不读取自己的随机交付牌。")
                    : (-100d, "公开阵营信息中没有敌对目标，不向友方发动反间。");
            }

            if (action.Skill == SkillKind.Rende)
            {
                var target = view.Players
                    .Where(player => player.IsAlive && player.Seat != Seat)
                    .OrderByDescending(player => GetTacticalSupport(view, role, player))
                    .ThenBy(player => player.Hp)
                    .ThenBy(player => player.Seat)
                    .FirstOrDefault();
                if (target is null)
                    return (-100d, "没有其他存活角色，不能发动仁德。 ");

                var support = GetTacticalSupport(view, role, target);
                var recoveryBonus = self.HandCount >= 2 && target.Hp < target.MaxHp ? 18d : 0d;
                return (
                    16d + support * 18d + Math.Min(self.HandCount, 5) * 1.4d + recoveryBonus,
                    $"向公开上最值得支持的目标 {target.Seat + 1} 交给手牌；支持收益 {support:0.#}，不读取目标暗牌。 ");
            }

            if (action.Skill == SkillKind.Qingnang)
            {
                var target = view.Players
                    .Where(player => player.IsAlive && player.Hp < player.MaxHp)
                    .OrderByDescending(player => GetTacticalSupport(view, role, player))
                    .ThenBy(player => player.Hp)
                    .ThenBy(player => player.Seat)
                    .FirstOrDefault();
                if (target is null)
                    return (-100d, "没有受伤的存活角色，不能发动青囊。 ");

                var support = GetTacticalSupport(view, role, target);
                return (
                    22d + support * 20d + (target.Hp <= 1 ? 10d : 0d),
                    $"弃置一张低保留价值手牌令公开受伤目标 {target.Seat + 1} 回复 1 点；支持收益 {support:0.#}，不读取暗牌。 ");
            }

            if (action.Skill == SkillKind.Huichun)
            {
                var targets = view.Players
                    .Where(player => player.IsAlive && player.Hp < player.MaxHp)
                    .OrderByDescending(player => GetTacticalSupport(view, role, player))
                    .ThenBy(player => player.Hp)
                    .ThenBy(player => player.Seat)
                    .Take(action.MinTargetCount)
                    .ToArray();
                if (targets.Length < action.MinTargetCount)
                    return (-100d, "受伤存活角色不足，不能发动回春。 ");

                var support = targets.Sum(target => GetTacticalSupport(view, role, target));
                var criticalTargets = targets.Count(target => target.Hp <= 1);
                return (
                    30d + support * 18d + criticalTargets * 12d,
                    $"弃置两张低保留价值手牌令 {targets.Length} 名公开受伤目标各回复 1 点；综合支持收益 {support:0.#}，不读取暗牌。 ");
            }

            if (action.Skill == SkillKind.Zhiheng)
            {
                var selectableCards = GetActiveSkillSelectableCards(self, action);
                var discardCandidate = selectableCards
                    .OrderBy(card => CardCatalog.Get(card.Kind).HandKeepValue)
                    .ThenBy(card => card.Id)
                    .FirstOrDefault();
                var candidateName = discardCandidate is null
                    ? "没有可弃置牌"
                    : $"优先弃置【{discardCandidate.DisplayName}】";
                return (
                    12d + Math.Min(selectableCards.Count, 6) * 0.7d,
                    $"发动{action.Description}，弃置一张低保留价值牌并摸一张；{candidateName}，只使用自己的过滤视图。 ");
            }

            var handPressure = Math.Min(self.HandCount, 6) * 1.2d;
            var missingHp = Math.Max(0, self.MaxHp - self.Hp);
            return (
                28d + missingHp * 8d - handPressure,
                $"发动{action.Description}，以公开体力换取两张牌；当前体力 {self.Hp}/{self.MaxHp}，不读取暗牌。 ");
        }

        if (action.Kind == LegalActionKind.EndPlay) return (0, "保留没有正收益的牌，结束出牌。");

        if (action.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage)
        {
            var others = view.Players.Where(player => player.IsAlive && player.Seat != Seat).ToArray();
            if (others.Any(player => player.Role == Role.Lord && player.Hp <= 1) && ProtectLord(view, role))
                return (-500, "主公仅剩 1 点体力，群体伤害可能直接导致己方失败；保留此牌。");
            var net = others.Sum(player =>
            {
                var relation = Math.Clamp(GetHostility(view, role, player) / 80d, -1.4, 1.4);
                var pressure = 20 + (player.Hp <= 1 ? 30 : player.Hp == 2 ? 10 : 0);
                var exposure = 1d / (1 + Math.Min(player.HandCount, 6) * .18);
                return relation * pressure * exposure;
            });
            return (net - 8, $"按公开体力、手牌数量和阵营线索估计群伤净收益 {net:0.#}；扣除用牌成本。");
        }

        if (action.Kind == LegalActionKind.PeachGarden)
        {
            var net = view.Players.Where(player => player.IsAlive && player.Hp < player.MaxHp).Sum(player =>
            {
                var support = player.Seat == Seat ? 1.2 : -Math.Clamp(GetHostility(view, role, player) / 80d, -1.4, 1.4);
                return support * (player.Hp <= 1 ? 60 : 30);
            });
            return (net - 6, $"只计算受伤角色的阵营回复收益 {net:0.#}；敌方回复会降低收益。");
        }

        if (action.Kind == LegalActionKind.Alcohol)
        {
            // A physical wine cannot both be drunk and converted by Wusheng.
            // Published legal Slash actions already include range and turn limits.
            var followUp = legalActions.Where(candidate => candidate.Kind == LegalActionKind.Slash && candidate.CardId != action.CardId)
                .Select(candidate => (Action: candidate, Score: ScoreTacticalAction(view, self, role, candidate, legalActions).Score))
                .OrderByDescending(candidate => candidate.Score).FirstOrDefault();
            if (followUp.Action is null || followUp.Score <= 0)
                return (-80, "没有另一张可合法打出且有正收益的杀；不消耗酒。");
            var target = view.Players.Single(player => player.Seat == followUp.Action.TargetSeat);
            if (self.Hp <= 1 && !self.Hand.Any(card => card.Kind == CardKind.Peach) && target.Hp > 1)
                return (-30, "自己仅剩 1 点体力且没有桃，保留酒用于自救。");
            return (followUp.Score + 8, "已有合法攻击目标，先饮酒再使用另一张杀；按公开局面评估。");
        }

        var (score, reason) = ScoreAction(view, self, role, action);
        var targetPlayer = action.TargetSeat is { } targetSeat ? view.Players.Single(player => player.Seat == targetSeat) : null;
        if (targetPlayer is not null && action.Kind is LegalActionKind.Dismantlement or LegalActionKind.Snatch &&
            targetPlayer.Judgment.FirstOrDefault(card => card.Id == action.TargetCardId) is { } judgment)
        {
            var hostility = GetHostility(view, role, targetPlayer);
            var transferValue = action.Kind == LegalActionKind.Snatch ? 12 : 0;
            return (-hostility + transferValue - 8,
                $"移除公开判定牌【{judgment.DisplayName}】会帮助该角色；按阵营收益判断，不把它当作拆除敌方装备。");
        }

        if (targetPlayer is not null && action.Kind is LegalActionKind.Slash or LegalActionKind.Duel or
            LegalActionKind.FireAttack or LegalActionKind.Indulgence or LegalActionKind.SupplyShortage or
            LegalActionKind.Dismantlement or LegalActionKind.Snatch)
        {
            var hostility = GetHostility(view, role, targetPlayer);
            if (hostility < -15)
                return (-120 + hostility, "公开身份或已观察到的行动支持同阵营判断；避免主动伤害或削弱该角色。");
        }

        if (action.Kind == LegalActionKind.Slash)
        {
            var card = self.Hand.Single(card => card.Id == action.CardId);
            if (!IsSlashCard(card.Kind))
            {
                var reserve = card.Kind switch
                {
                    CardKind.Peach => self.Hp < self.MaxHp ? 105 : 65,
                    CardKind.Dodge when self.Hand.Count(card => card.Kind == CardKind.Dodge) == 1 => self.Hp <= 2 ? 40 : 15,
                    CardKind.Alcohol when self.Hp <= 1 => 75,
                    _ => 0
                };
                score -= reserve;
                if (reserve > 0) reason += $" 转化会消耗保命牌，扣除 {reserve} 分。";
            }
            if (self.HasAlcoholEffect)
            {
                score += 45;
                reason += " 已有酒的加伤效果，优先完成攻击。";
            }
        }
        return (score, reason);
    }

    private static bool ProtectLord(GameSnapshot view, Role role) => role is Role.Lord or Role.Loyalist ||
        role == Role.Renegade && view.Players.Count(player => player.IsAlive) > 2;

    private double GetTacticalSupport(GameSnapshot view, Role role, PlayerSnapshot target) =>
        target.Seat == Seat ? 1 : -Math.Clamp(GetHostility(view, role, target) / 60d, -1.5, 1.5);

    private double ScoreTacticalDyingResponse(GameSnapshot view, Role role, PlayerSnapshot victim)
    {
        if (victim.Seat == Seat) return 120;
        if (view.ModeKind == ContentModeKind.NationalWarLite)
        {
            var self = view.Players.Single(player => player.Seat == Seat);
            if (victim.FactionId is null)
            {
                if (self.FactionId is null || _policyVersion < 2)
                {
                    return 0d;
                }

                // Hidden victims remain unresolved. Public evidence can only
                // make rescue more or less attractive; it never establishes a
                // hidden faction or changes the player's filtered view.
                return Math.Clamp(-GetNationalHostility(view, victim) * 0.75d, -60d, 60d);
            }

            if (self.FactionId is null)
            {
                return -80d;
            }

            return string.Equals(self.FactionId, victim.FactionId, StringComparison.Ordinal)
                ? 110d
                : -80d;
        }

        if (victim.Role == Role.Lord) return ProtectLord(view, role) ? 160 : -100;
        if (role == Role.Renegade) return -20;
        var hostility = GetHostility(view, role, victim);
        return hostility < -15 ? 70 : -30;
    }

    private double GetTacticalHostility(GameSnapshot view, Role role, PlayerSnapshot target)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        if (self.TeamId is not null || target.TeamId is not null)
        {
            return target.Seat == Seat
                ? -1000d
                : string.Equals(self.TeamId, target.TeamId, StringComparison.Ordinal)
                    ? -100d
                    : 100d;
        }

        var suspicion = Math.Clamp(_rebelSuspicion.GetValueOrDefault(target.Seat), -6, 6);
        var visibleOrDeducedRole = target.Role;
        if (visibleOrDeducedRole is null)
        {
            // Identity composition is public. Subtract only our own and revealed
            // roles; never consult living opponents' hidden identities.
            var remaining = new Dictionary<Role, int>
            {
                [Role.Lord] = 1,
                [Role.Loyalist] = view.Players.Count == 5 ? 1 : 2,
                [Role.Rebel] = view.Players.Count == 5 ? 2 : 4,
                [Role.Renegade] = 1
            };
            foreach (var player in view.Players.Where(player => player.Role is not null)) remaining[player.Role!.Value]--;
            var possible = remaining.Where(pair => pair.Value > 0).Select(pair => pair.Key).ToArray();
            if (possible.Length == 1) visibleOrDeducedRole = possible[0];
            else if (role is Role.Lord or Role.Loyalist && remaining[Role.Rebel] == 0 && remaining[Role.Renegade] == 1)
            {
                // The remaining opponent cannot be treated as a permanent ally
                // merely because they helped eliminate Rebels earlier.
                var suspect = view.Players.Where(player => player.IsAlive && player.Role is null)
                    .OrderByDescending(player => _rebelSuspicion.GetValueOrDefault(player.Seat)).ThenBy(player => player.Seat).First();
                return target.Seat == suspect.Seat ? 55 : -60;
            }
        }
        return role switch
        {
            Role.Rebel => visibleOrDeducedRole switch
            {
                Role.Lord => 100,
                Role.Loyalist => 65,
                Role.Rebel => -100,
                Role.Renegade => 20,
                _ => 8 - suspicion * 12
            },
            Role.Lord or Role.Loyalist => visibleOrDeducedRole switch
            {
                Role.Lord => -1000,
                Role.Loyalist => -100,
                Role.Rebel => 100,
                Role.Renegade => 55,
                _ => 12 + suspicion * 14
            },
            Role.Renegade => GetRenegadeHostility(view, target, visibleOrDeducedRole, suspicion),
            _ => 0
        };
    }
}
