namespace CardGame.Core;

/// <summary>Bounded public-state lookahead: stop at the next choice, never inspect a hidden hand.</summary>
internal static class ProgramChoiceAi
{
    internal static PlayerSkillContext? Target(SkillProgramEffectTarget target,
        PlayerSkillContext owner, ProgramAiPublicContext context) => target switch
        {
            SkillProgramEffectTarget.Owner => owner,
            SkillProgramEffectTarget.SelectedTarget => context.SelectedTarget,
            SkillProgramEffectTarget.Actor => context.Actor ?? (context.CardActionActorIsOwner ? owner : null),
            _ => null
        };

    internal static double Score(IEnumerable<SkillProgramEffect> effects, PlayerSkillContext owner,
        PlayerSkillContext chooser, ProgramAiPublicContext context,
        Func<string, int?>? chooserOwnedBoundCardCount = null)
    {
        var score = 0d;
        var hp = chooser.Hp;
        var faceDown = chooser.IsFaceDown;
        var chained = chooser.IsChained;
        foreach (var effect in effects.TakeWhile(effect => effect.Op != SkillProgramEffectOp.ChooseOption))
        {
            if (!ProgramCompositionAi.EvaluateCondition(effect.Condition, owner, context)) continue;
            if (effect.Op == SkillProgramEffectOp.MoveBoundCards &&
                effect.Destination == SkillProgramCardDestination.DiscardPile &&
                effect.SourceBind is { } bind && chooserOwnedBoundCardCount?.Invoke(bind) is { } cardCount)
            {
                score -= cardCount * 8d;
                continue;
            }
            if (effect.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard &&
                effect.CardOwnerRef?.Kind == ProgramParticipantRef.SelectedTarget &&
                context.SelectedTarget?.Seat == chooser.Seat)
            {
                // The chooser knows its own card cost; its exact hidden card identity is not
                // needed to compare a generic one-card payment with a visible damage effect.
                score -= effect.CardCategories.Contains(SkillProgramCardCategory.Equipment) ? 10d : 8d;
                continue;
            }
            if (effect.Op == SkillProgramEffectOp.NullifyCurrentCardEffect)
            {
                // The nullified card use is the chooser's own; losing it is the
                // real cost of declining the skill's payment branch.
                score -= 30d;
                continue;
            }
            if (Target(effect.Target, owner, context)?.Seat != chooser.Seat) continue;
            switch (effect.Op)
            {
                case SkillProgramEffectOp.Draw:
                    score += effect.Amount * 8d;
                    break;
                case SkillProgramEffectOp.UseBoundCardByTarget
                    when effect.SourceBind is { } giftBind &&
                         chooserOwnedBoundCardCount?.Invoke(giftBind) == 1:
                    score += 8d;
                    break;
                case SkillProgramEffectOp.ClaimDamageCards when context.HasClaimableDamageCards:
                    score += 8d;
                    break;
                case SkillProgramEffectOp.Recover:
                    var recovery = Math.Min(effect.Amount, Math.Max(0, chooser.MaxHp - hp));
                    score += recovery * (hp <= 1 ? 100d : 18d);
                    hp += recovery;
                    break;
                case SkillProgramEffectOp.SetFaceState when effect.FaceDown != faceDown:
                    faceDown = effect.FaceDown!.Value;
                    score += faceDown ? -24d : 24d;
                    break;
                case SkillProgramEffectOp.SetChainedState when effect.Chained != chained:
                    chained = effect.Chained!.Value;
                    score += chained ? -4d : 4d;
                    break;
                case SkillProgramEffectOp.LoseHp:
                case SkillProgramEffectOp.Damage:
                    score -= effect.Amount * 22d;
                    hp -= effect.Amount;
                    break;
            }
        }
        return score;
    }
}
