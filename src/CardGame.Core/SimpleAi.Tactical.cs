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

        var printedSkills = action.GeneralSlot switch
        {
            GeneralSelectionSlot.Primary => self.Skills,
            GeneralSelectionSlot.Secondary => self.SecondarySkills,
            _ => null
        } ?? [];
        var hasLivingOpponent = view.Players.Any(player => player.IsAlive && player.Seat != Seat);
        var nativeSlashCount = self.Hand.Count(card => IsSlashCard(card.Kind));
        var wounded = self.Hp < self.MaxHp;
        var handIsEmpty = self.HandCount == 0;

        var bestSkill = printedSkills.Select(entry =>
        {
            var slashConversions = CountHandViewAsConversions(self, entry, CardKind.Slash, forPlay: true);
            var dodgeConversions = CountHandViewAsConversions(self, entry, CardKind.Dodge, forPlay: false);
            var conversionValue = slashConversions > 0
                ? 46d + Math.Min(slashConversions, 3) * 4d
                : dodgeConversions > 0
                    ? 38d + Math.Min(dodgeConversions, 3) * 4d
                    : 4d;
            var weights = entry.RevealWeights ?? new SkillRevealWeights();
            var configuredValue = Math.Max(weights.Base, Math.Max(wounded ? weights.Wounded : 0,
                Math.Max(handIsEmpty ? weights.EmptyHand : 0,
                    nativeSlashCount >= 2 && weights.RepeatedSlash > 0
                        ? weights.RepeatedSlash + Math.Min(nativeSlashCount - 1, 3) * 5d : 0)));
            return (Value: Math.Max(hasLivingOpponent ? conversionValue : 4d, configuredValue),
                SlashConversions: slashConversions, DodgeConversions: dodgeConversions);
        })
            .OrderByDescending(entry => entry.Value)
            .FirstOrDefault((Value: 4d, SlashConversions: 0, DodgeConversions: 0));
        var immediateValue = bestSkill.Value;

        var exposureCost = self.FactionId is null || self.IsFactionRevealed ? 0d : 3d;
        var score = 24d + immediateValue - exposureCost;
        var opportunity = true switch
        {
            _ when bestSkill.SlashConversions > 0 =>
                $"当前有 {bestSkill.SlashConversions} 张手牌可转化为杀",
            _ when bestSkill.DodgeConversions > 0 =>
                $"当前有 {bestSkill.DodgeConversions} 张手牌可转化为闪",
            _ when wounded => $"当前体力 {self.Hp}/{self.MaxHp}，受伤后触发类技能已有即时价值",
            _ => "当前没有确定的即时触发机会，保留明置信息成本"
        };
        var reason =
            $"明置{action.GeneralSlot switch { GeneralSelectionSlot.Primary => "主将", GeneralSelectionSlot.Secondary => "副将", _ => "武将" }}「{action.Description}」；{opportunity}；评分只使用自己的私有技能、手牌、体力、公开对手和当前动作，不读取暗牌。";
        return (score, reason);
    }

    private static int CountHandViewAsConversions(
        PlayerSnapshot self, GeneralSkillDefinition skill, CardKind outputKind, bool forPlay) =>
        self.Hand.Count(card => CanViewAs(skill, card, outputKind, forPlay));

    private static bool CanViewAs(
        GeneralSkillDefinition skill, CardSnapshot card, CardKind outputKind, bool forPlay) =>
        card.Kind != outputKind &&
        skill.ViewAsOpportunities?.Any(rule =>
            rule.OutputKind == outputKind &&
            (forPlay ? rule.ForPlay : rule.ForResponse) &&
            (rule.InputKinds.Count == 0 || rule.InputKinds.Contains(card.Kind)) &&
            (rule.InputSuits.Count == 0 || rule.InputSuits.Contains(card.Suit))) == true;

    // Version 2 evaluates only the supplied viewer snapshot, legal actions and
    // accumulated public evidence. Version 1 remains unchanged for checkpoint replay.
    private (double Score, string Reason) ScoreTacticalAction(
        GameSnapshot view, PlayerSnapshot self, Role role, LegalAction action,
        IReadOnlyList<LegalAction> legalActions)
    {
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

        if (action.Kind == LegalActionKind.Slash && action.TargetSeats.Count > 1)
        {
            return ScoreFangtianHalberdSlash(view, self, role, action);
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
            var card = self.Hand.Concat(self.WoodenOxGrain ?? []).Concat(self.Equipment)
                .Single(card => card.Id == action.CardId);
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
