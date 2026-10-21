namespace CardGame.Core;
public sealed record MouSmPrivateUse(long UseId,long ActionId,IReadOnlyList<int> Targets);
internal interface IMouSimaYiFacts:IProgramSelectionQuery
{
    int Turn {get;} int TurnOwner {get;} int PhaseId {get;} long Sequence {get;} bool Finished {get;}
    bool Alive(int seat); bool Human(int seat); string Name(int seat); ContentSkillDefinition Skill(string id); IEnumerable<T> History<T>()where T:IGameEvent;
    bool Parent(ProgramSkillFrame f); bool ActualPlay(int owner); bool Yin(int owner,string instance);
    IReadOnlyList<int> InRange(int owner); IReadOnlyList<int> Living(); IReadOnlyList<MouSmEntity> Gifts(ProgramSkillFrame f,int actor);
    IReadOnlyList<MouSmPlayedPlan> Plays(ProgramSkillFrame f); bool Declaration(MouSmPlayedPlan plan); MouSmPrivateUse? PrivateUse(int owner,ProgramSkillWindowContext? context);
    MouSmDebtIssuedEvent? Debt(int owner,ProgramSkillWindowContext? context,bool yin); int Keep(int actor,int id); double DamageValue(int actor,int target);
}
internal interface IMouSimaYiNative:IProgramRuntimeOperations
{
    void Finish(ProgramSkillFrame f); void Publish(); bool Drain(ProgramSkillFrame f); void Give(ProgramSkillFrame f);
    void Prevent(ProgramSkillFrame f); void Damage(ProgramSkillFrame f,int target); void Disclose(ProgramSkillFrame f); void ProhibitHand(ProgramSkillFrame f,int target);
    void Declare(ProgramSkillFrame f); void Play(ProgramSkillFrame f); void CompletePlayed(ProgramSkillFrame f);
}
