namespace CardGame.Core;

/// <summary>A declaration-owned policy, independent of later grant enablement.</summary>
public sealed record NoDamageSkillDisablePolicy(long UseFrameId, long ActionId, CardConversionSource Source,
    string GameplayHash, int TurnNumber, int TurnOwnerSeat);
public sealed record NoDamageSkillDisablePolicyIssuedEvent(NoDamageSkillDisablePolicy Policy) : IGameEvent;

public sealed partial class GameEngine
{
    private void IssueNoDamageSkillDisablePolicy(long frameId)
    {
        var use = LifecycleCardUse(frameId)!;
        if (use.Action is not { Type: CardActionType.Use } action) return;
        var eligible = action.ConversionChain.Where(source =>
            _contentRegistry.GetSkill(source.SkillId).Program?.ViewAs.SingleOrDefault(rule =>
                rule.Id == source.BindingId)?.DisableSkillUntilTurnEndIfNoDamage == true).ToArray();
        if (eligible.Length == 0) return;
        if (eligible is not [var source] || source.OwnerSeat != action.ActorSeat ||
            !HasRuntimeSkillInstance(_players[source.OwnerSeat], source.SkillId, source.SkillInstanceId) ||
            use.CardKind != CardKind.UnexpectedAssault || action.PhysicalCards is not [{ From.Zone: CardZoneKind.Hand }])
            throw new InvalidOperationException("A no-damage disable policy requires its exact accepted hand conversion.");
        var policy = new NoDamageSkillDisablePolicy(use.Id, action.ActionId, source,
            _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash, _turnNumber, _currentSeat);
        ReplaceRuntimeFrame(use.Id, use with { NoDamageSkillDisablePolicy = policy });
        AdvanceEventRulesAndQueueFact(new NoDamageSkillDisablePolicyIssuedEvent(policy));
    }

    private void SettleNoDamageSkillDisable(CardUseFrame use)
    {
        if (use.NoDamageSkillDisablePolicy is not { } policy) return;
        AssertNoDamageSkillDisablePolicy(use);
        if (use.CausedDamage) return;
        if (policy.TurnNumber != _turnNumber || policy.TurnOwnerSeat != _currentSeat)
            throw new InvalidOperationException("A no-damage conversion cannot settle in another actual turn.");
        var source = policy.Source;
        var issued = new CurrentTurnOwnSkillSuppression(_turnNumber, _currentSeat, use.Id, 0,
            new(source.SkillId, source.BindingId, source.OwnerSeat, source.SkillInstanceId),
            source.SkillId, use.Id, policy.ActionId);
        var existing = _currentTurnOwnSkillSuppressions.SingleOrDefault(s => s.ParentFrameId == use.Id && s.EffectIndex == 0);
        if (existing is not null)
        {
            if (existing != issued) throw new InvalidOperationException("A completed conversion disable changed identity.");
            return;
        }
        _currentTurnOwnSkillSuppressions.Add(issued);
        _currentTurnSkillSuppressionRevision++;
        AdvanceEventRulesAndQueueFact(new CurrentTurnOwnSkillSuppressionIssuedEvent(issued));
    }

    private void AssertNoDamageSkillDisablePolicy(CardUseFrame use)
    {
        if (use.NoDamageSkillDisablePolicy is not { } policy) return;
        if (use.CardKind != CardKind.UnexpectedAssault || use.Action is not { Type: CardActionType.Use } action ||
            policy.UseFrameId != use.Id || policy.ActionId != action.ActionId ||
            !action.ConversionChain.Contains(policy.Source) ||
            policy.GameplayHash != _contentRegistry.GetSkill(policy.Source.SkillId).Program?.GameplayHash ||
            _contentRegistry.GetSkill(policy.Source.SkillId).Program?.ViewAs.SingleOrDefault(r =>
                r.Id == policy.Source.BindingId)?.DisableSkillUntilTurnEndIfNoDamage != true ||
            CompleteProgramEventHistory().OfType<NoDamageSkillDisablePolicyIssuedEvent>().Count(e => e.Policy == policy) != 1)
            throw new InvalidOperationException("A no-damage disable policy lost its original accepted use.");
    }
}
