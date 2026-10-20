namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ProgramLifecycle
{
    internal static string GetProgramBindingId(ProgramSkillFrame frame) =>
        frame.TriggerId ?? frame.ActivationId;

    internal static IEnumerable<int[]> EnumerateProgramCardOrders(IReadOnlyList<int> ids)
    {
        if (ids.Count == 0) { yield return []; yield break; }
        for (var index = 0; index < ids.Count; index++)
        {
            var rest = ids.Where((_, current) => current != index).ToArray();
            foreach (var suffix in CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.EnumerateProgramCardOrders(rest))
                yield return [ids[index], .. suffix];
        }
    }

    internal static bool MatchesPlayEndingTurnOwnerScope(SkillProgramTurnOwnerScope scope, bool ownSeat) => scope switch
    {
        SkillProgramTurnOwnerScope.Own => ownSeat,
        // AnyLiving collects the trigger once for its owner and lets the shared
        // PlayEnding eligibility accept both the own and the foreign phase end.
        SkillProgramTurnOwnerScope.AnyLiving => true,
        _ => !ownSeat,
    };

    internal static bool IsGuanduForeignDrawTrigger(SkillProgramTrigger trigger) =>
        trigger.Window == SkillProgramTriggerWindow.DrawPhaseStarting && trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving &&
        trigger.Subject == SkillProgramTriggerSubject.Owner && trigger.Optional && trigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive &&
        trigger.Condition.Kind == SkillProgramTriggerConditionKind.Always && trigger.UsageScope is null && trigger.UsageLimit is null &&
        trigger.ChoiceGroup is null && trigger.Effects is
            [{ Op: SkillProgramEffectOp.ResolveGuanduJieliang, Target: SkillProgramEffectTarget.Owner,
                TargetReference: null, ActorReference: null, Condition.Kind: SkillProgramConditionKind.Always }];

    internal static string SelfDyingBindingKey(string skillId, string bindingId, string skillInstanceId) =>
        $"{skillId}\u001f{bindingId}\u001f{skillInstanceId}";
}
