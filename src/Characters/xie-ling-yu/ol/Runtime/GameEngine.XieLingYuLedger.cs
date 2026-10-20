namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasXieLingYuCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveYuandiDiscardNames) || _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveXinyouBasicCycle);
    private static CardConversionSource XieLingYuSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static int XieLingYuNameBit(CardKind kind) => ProgramBasicCardName(kind) switch { CardKind.Slash => 1, CardKind.Dodge => 2, CardKind.Peach => 4, CardKind.Alcohol => 8, _ => 0 };
    private static CardKind[] XieLingYuNames(int mask) => new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol }.Where(k => (mask & XieLingYuNameBit(k)) != 0).ToArray();
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
