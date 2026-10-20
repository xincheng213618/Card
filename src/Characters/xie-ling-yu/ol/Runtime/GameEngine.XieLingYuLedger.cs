namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasXieLingYuCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveYuandiDiscardNames) || _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveXinyouBasicCycle);
    private static CardConversionSource XieLingYuSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_xie_ling_yu_ol_XieLingYuLedger.XieLingYuSource(f);
    private static int XieLingYuNameBit(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_xie_ling_yu_ol_XieLingYuLedger.XieLingYuNameBit(kind);
    private static CardKind[] XieLingYuNames(int mask) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_xie_ling_yu_ol_XieLingYuLedger.XieLingYuNames(mask);
    private void CaptureXieLingYuMovementBatch(CardMovementBatchContext batch)
    {
        if (!HasXieLingYuCapability || batch.TurnNumber <= 0) return;
        foreach (var m in batch.Movements.Where(m => m.To == CardLocation.DiscardPile && m.From != m.To && XieLingYuNameBit(m.CardKind) is > 0 and < 8))
        {
            if (ProgramEventHistory<YuandiDiscardEnteredEvent>().Any(e => e.MovementSequence == m.Sequence)) continue;
            AdvanceEventRulesAndQueueFact(new YuandiDiscardEnteredEvent(batch.Id, batch.TurnNumber, _turnProgression.OwnerSeat, m.Sequence,
                m.CardId, m.CardKind, ProgramBasicCardName(m.CardKind), IsNativeTrueUseDiscardMovement(batch, m)));
        }
    }
}
