namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsGuimingTreatedWounded(CharacterState player)
    {
        if (!IsValidPlayerSeat(_currentSeat) || !player.IsAlive || !player.HasHpValue || player.IdentityConcealed || player.Seat == _currentSeat || GetEffectiveFactionId(player) != "wu") return false;
        var owner = _players[_currentSeat]; if (!owner.IsAlive || owner.IdentityConcealed) return false;
        // Prefilter the raw capability before evaluating qualification. In
        // particular, evaluating unrelated Tang Zi population grants here would
        // recurse through IsRuleWounded while the owner's shard is being built.
        var candidates = owner.SkillGrants.Grants.Where(g => _contentRegistry.GetSkill(g.SkillId).Program?.CardPolicies.Any(p =>
            p.Kind == SkillProgramCardPolicyKind.OtherWuTreatedWoundedDuringOwnTurn) == true).ToArray();
        if (candidates.Length == 0 || !SunHaoActualTurnOpen) return false;
        var suppressors = PrivateGeneralLibrarySuppressionInputs(owner);
        return candidates.Any(g => LibraryBaseGrantLive(owner,g) && IsCurrentTurnSkillGrantQualified(owner,g) && IsProjectedGrantQualified(owner,g) &&
            IsGeneralLibraryGrantQualified(owner,g) && IsHpConditionalGrantQualified(owner,g) && IsEquipmentSuitGrantQualified(owner,g) &&
            (suppressors.Count == 0 || suppressors.Contains(g.SkillId) || g.SourceId.StartsWith("equipment:",StringComparison.Ordinal)) &&
            HasSkillRoleQualification(owner,g.SkillId,g.SkillInstanceId,Role.Lord));
    }
}
