namespace CardGame.Core;

public sealed partial class GameEngine
{
    // A projection can carry both pools without inventing a numerical operand.
    // Real health effects keep using their original frame's selected receipt.
    private int? UnambiguousHp(CharacterState player) => !player.HasHpValue ||
        player.HappyQiaoHealth is { } health && health.DaQiaoHp != health.XiaoQiaoHp &&
        player.HappyQiaoPoolQuery?.Invoke(player) is null ? null : player.Hp;

    private bool TryBeginHappyQiaoAttackHpLossChoice(IDamageAttempt attack)
    {
        if (!HasHappyQiaoHealth(attack.TargetSeat)) return false;
        var amount = CalculateCardDamageLayer(attack);
        if (amount <= 0) return false;
        var parent = _resolutionStack.Last();
        return TryBeginHappyQiaoHealthChoice(parent.Id, attack.TargetSeat, HpChangeKind.Loss, amount,
            HappyQiaoHealthReturnKind.Damage, $"damage-loss:{attack.ResolutionId}:{attack.TargetSeat}:{NativeExtraEffectRoundOrdinal(attack.ResolutionId)}",
            attack.IsSourceLess ? null : attack.SourceSeat, attack.ResolutionId);
    }

    private bool HasHappyQiaoNativeDamageChoice(long attackId)
    {
        if (_resolutionStack.Count < 2 || _resolutionStack[^1] is not HappyQiaoHealthChoiceFrame { IsQuery: false } choice ||
            _resolutionStack[^2] is not { HappyQiaoHealth: { ReturnKind: HappyQiaoHealthReturnKind.Damage, Applied: false, Pool: null } receipt } parent ||
            parent.Id != attackId || choice.ParentFrameId != parent.Id || receipt.AttackResolutionId != attackId ||
            LastDamageSourceOriginalAttack(attackId) is not { DamageWasApplied: false } attack ||
            attack.TargetSeat != receipt.TargetSeat || choice.OwnerSeat != attack.TargetSeat ||
            receipt.SourceSeat != (attack.IsSourceLess ? null : attack.SourceSeat)) return false;
        RequireHappyQiaoHealthChoice(choice);
        return true;
    }

    private bool TryBeginHappyQiaoInstructionQuery(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        IEnumerable<int> subjects = [];
        if (effect.Condition.ReferencesOwnerHp || effect.NumberExpression is SkillProgramNumberExpression.CurrentHp or
            SkillProgramNumberExpression.OwnerLostHp or SkillProgramNumberExpression.LiveOwnerLostHp or
            SkillProgramNumberExpression.OwnerLostHpPlusOne or SkillProgramNumberExpression.OwnerLostHpAtLeastOne)
            subjects = subjects.Append(frame.OwnerSeat);
        if (effect.Condition.ReferencesSelectedTargetHp) subjects = subjects.Concat(frame.SelectedTargetSeats);
        if (effect.NumberExpression == SkillProgramNumberExpression.LivingPlayersMinHp)
            subjects = subjects.Concat(_players.Where(player => player.IsAlive && player.HasHpValue).Select(player => player.Seat));
        if (effect.NumberExpression is SkillProgramNumberExpression.LostHpMinusHandCount or SkillProgramNumberExpression.RecipientCurrentHp)
            subjects = subjects.Concat(effect.Target == SkillProgramEffectTarget.Owner ? [frame.OwnerSeat] : frame.SelectedTargetSeats);
        foreach (var seat in subjects.Distinct().Where(HasHappyQiaoHealth))
            if (TryBeginHappyQiaoQuery(GetActiveProgramFrame(frame.Id), seat, "instruction-hp")) return true;
        return false;
    }

    private IReadOnlyList<LegalAction> HappyQiaoActionQueryUnion(Func<IReadOnlyList<LegalAction>> query)
    {
        var unresolved = _players.Where(player => player.IsAlive && player.HappyQiaoHealth is { } health &&
            health.DaQiaoHp != health.XiaoQiaoHp && player.HappyQiaoPoolQuery?.Invoke(player) is null).ToArray();
        if (unresolved.Length == 0) return query();
        var actions = new List<LegalAction>();
        void Visit(int index)
        {
            if (index == unresolved.Length)
            {
                var previous = _actionQuery;
                _actionQuery = new ActionQuery(_players.Count);
                using var scope = new ActionQueryScope(this, previous);
                actions.AddRange(query());
                return;
            }
            var player = unresolved[index];
            var original = player.HappyQiaoPoolQuery;
            try
            {
                foreach (var pool in Enum.GetValues<HappyQiaoPool>())
                {
                    player.HappyQiaoPoolQuery = _ => pool;
                    Visit(index + 1);
                }
            }
            finally { player.HappyQiaoPoolQuery = original; }
        }
        Visit(0);
        // Pool-dependent AI estimates do not create another executable action.
        // Keep every selection and identity field in the key; the real effect
        // still asks for its owning health receipt when it resolves.
        return Array.AsReadOnly(actions.DistinctBy(action =>
            System.Text.Json.JsonSerializer.Serialize(action with { ProgramAiHint = null })).ToArray());
    }
}
