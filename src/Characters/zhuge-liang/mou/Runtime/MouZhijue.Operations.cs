namespace CardGame.Core;
internal sealed class MouZhijueUseDescriptor : MouZhugeOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.MouZhijueUse; }

public sealed record MouZhijueDraft(string Stage, bool Yang, IReadOnlyList<int> PaidCardIds,
    IReadOnlyList<CardKind> CopiedKinds, long? UseId = null, bool CausedDamage = false);
public sealed record MouZhijueUseReturn(long ProgramFrameId, int InstructionIndex, int OwnerSeat,
    string SkillId, string SkillInstanceId, string GameplayHash, int TurnNumber, bool Yang, CardKind Kind,
    int PhysicalCardId, long ActionId);
public sealed record MouZhijuePolarityChangedEvent(int OwnerSeat, bool Yang) : IGameEvent;
public sealed record MouZhijueUndamagedEvent(int OwnerSeat, int VisibleCount) : IGameEvent;
