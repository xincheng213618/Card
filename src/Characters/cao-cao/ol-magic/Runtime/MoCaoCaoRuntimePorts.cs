namespace CardGame.Core;

internal sealed record MoCaoCaoBinding(CardConversionSource Source, string GameplayHash);
internal sealed record MoCaoCaoParticipant(int Seat, bool Alive, bool Human, int Hp, int EquipmentCount, bool WeaponSlot, bool ArmorSlot);
internal sealed record MoCaoCaoMovement(long Before, long After, bool Arrived);
internal sealed record MoCaoCaoDamageOrigin(long DamageId, long UseId, long ActionId, int ActorSeat, int TargetSeat, CardKind Kind, int Amount, bool SourceLess);
internal sealed record MoCaoCaoWeaponAuthorization(int PhysicalCardId, long VirtualIssueId, string SourceId);
internal interface IMoCaoCaoFacts : IProgramSelectionQuery
{
    bool Enabled { get; }
    IReadOnlyList<int> Seats { get; }
    MoCaoCaoParticipant Participant(int seat);
    MoCaoCaoBinding? FuzaiBinding(int seat);
    ContentSkillDefinition Skill(string skillId);
    SkillProgram? Program(string skillId);
    ActualDiscardRecoveryPhaseKey? Phase { get; }
    IReadOnlyList<int> Hand(int seat);
    CardLocation Location(int cardId);
    CardKind Kind(int cardId);
    IEnumerable<T> History<T>() where T : IGameEvent;
    bool Held(ProgramSkillFrame frame);
    bool Finished { get; }
    long MovementSequence { get; }
    bool IsExactStart(ProgramSkillFrame frame, SkillProgramEffectOp operation);
    int ResolveHandCard(ProgramSkillFrame frame, int chooserSeat, PromptChoice selected);
    IReadOnlyList<PromptChoice> PaymentChoices(ProgramSkillFrame frame, int chooserSeat, int subjectSeat, bool handOnly);
    MoCaoCaoDamageOrigin? DamageOrigin { get; }
    MoCaoCaoWeaponAuthorization? WeaponAuthorization(int seat, CardKind kind);
    bool ExactIssuedCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context);
    bool NativeChild(ProgramSkillFrame parent, ResolutionFrame child);
    bool Friendly(int chooserSeat, int targetSeat);
    int KeepValue(int chooserSeat, int cardId);
}
internal interface IMoCaoCaoOperations : IProgramRuntimeOperations
{
    int NextRandom(int count);
    void PublishState();
    void LogEquipment(int seat, CardKind weapon, CardKind armor);
    void Finish(ProgramSkillFrame frame);
    bool Drain(ProgramSkillFrame frame);
    MoCaoCaoMovement Transfer(ProgramSkillFrame frame, int payerSeat, int cardId, string reason);
    void Damage(ProgramSkillFrame frame, int targetSeat);
    void EnterRumo(ProgramSkillFrame frame);
    bool AdvanceObserverChildren();
}
