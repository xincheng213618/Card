namespace CardGame.Core;
public sealed record RedAdditionalTargetsGrantedEvent(int OwnerSeat,CardConversionSource Source,int Maximum) : IGameEvent;
public sealed record RedAdditionalTargetsConsumedEvent(int OwnerSeat,CardConversionSource Source,IReadOnlyList<int> Targets) : IGameEvent;
public sealed partial class GameEngine
{
    private readonly Dictionary<int,(CardConversionSource Source,int Maximum)> _redAdditionalTargetGrants=[];
    private CardConversionSource? _selectedRedAdditionalTargetSource;
    private void GrantRedAdditionalTargets(ProgramSkillFrame frame,int maximum)
    {
        var source=new CardConversionSource(frame.SkillId,"red-additional-targets",frame.OwnerSeat,frame.SkillInstanceId);
        _redAdditionalTargetGrants[frame.OwnerSeat]=(source,maximum);
        AdvanceEventRulesAndQueueFact(new RedAdditionalTargetsGrantedEvent(frame.OwnerSeat,source,maximum));
    }
    private bool HasRedAdditionalTargets(CharacterState actor)=>_redAdditionalTargetGrants.TryGetValue(actor.Seat,out var g) && HasRuntimeSkillInstance(actor,g.Source.SkillId,g.Source.SkillInstanceId);
    private bool HasNextCardTargetAdjustment(CharacterState actor)=>HasLegacyNextCardTargetAdjustment(actor)||HasRedAdditionalTargets(actor);
    private bool IsTargetAdjustmentAction(CharacterState actor,LegalAction action)=>action.ProgramActivationId=="next-card-target-adjustment" && HasLegacyNextCardTargetAdjustment(actor) || action.ProgramActivationId=="red-additional-targets" && HasRedAdditionalTargets(actor);
    private int GetAdditionalTargetAdjustmentLimit(CharacterState actor,LegalAction action)=>action.ProgramActivationId=="red-additional-targets"?_redAdditionalTargetGrants[actor.Seat].Maximum:1;
    private void AddRedAdditionalTargetActions(List<LegalAction> actions,CharacterState actor)
    {
        if(!HasRedAdditionalTargets(actor)) return;
        var grant=_redAdditionalTargetGrants[actor.Seat];
        var actionKeys=actions.Select(action=>(action.CardId,action.Kind,action.PlayedCardKind,
            action.ConversionSource,action.TargetCardId,Targets:string.Join(",",action.TargetSeats))).ToHashSet();
        foreach(var action in actions.ToArray().Where(a=>a.CardId is not null && a.ProgramActivationId is null && a.Kind!=LegalActionKind.Recast))
        {
            var physical=FindOwnedPlayableCard(actor,action.CardId)!;
            var kind=action.PlayedCardKind??physical.Kind;
            var suit=EffectiveSuit(actor,ApplyProgramUseAppearance(actor,physical,action.ConversionSource));
            if(suit is not (Suit.Heart or Suit.Diamond) || (GetProgramCardCategory(kind)!=SkillProgramCardCategory.Basic && !IsOrdinaryTrick(kind))) continue;
            var normal=action.Kind is LegalActionKind.DrawTwo or LegalActionKind.Peach or LegalActionKind.Alcohol ? new[]{actor.Seat} :GetDeclaredCardTargets(actor,action.Kind,action.TargetSeats).ToArray();
            if(normal.Length==0) continue;
            if(kind==CardKind.BorrowedSword)
            {
                normal=action.TargetSeats.ToArray();
                var originalHolders=normal.Where((_,index)=>index%2==0).ToArray();
                var pairs=_players.Where(p=>p.IsAlive && p.Seat!=actor.Seat && !originalHolders.Contains(p.Seat) && GetWeapon(p) is not null && !IsDirectedCardTargetProhibited(actor.Seat,p.Seat,kind) && !IsCardTargetProhibited(p,kind,suit) && !HasBeneficiarySuitShield(actor.Seat,p.Seat,suit))
                    .SelectMany(p=>_players.Where(v=>IsLegalBorrowedSwordSlashTarget(p,v)).Select(v=>new[]{p.Seat,v.Seat})).ToArray();
                foreach(var pair in pairs) Add([..normal,..pair]);
                if(grant.Maximum==2) foreach(var a in pairs) foreach(var b in pairs.Where(b=>b[0]>a[0])) Add([..normal,..a,..b]);
                continue;
            }
            var extra=_players.Where(p=>!normal.Contains(p.Seat) && CanBeExtraNextCardTarget(actor,p,action,kind,suit) &&
                !(p.Seat==actor.Seat && action.ConversionSource is { } source && ViewAsRule(source)?.ExcludeOwnerEffects==true)).Select(p=>p.Seat).ToArray();
            foreach(var seat in extra) Add([..normal,seat]);
            if(grant.Maximum==2) for(var a=0;a<extra.Length;a++) for(var b=a+1;b<extra.Length;b++) Add([..normal,extra[a],extra[b]]);
            void Add(IReadOnlyList<int> targets)
            {
                if(!actionKeys.Add((action.CardId,action.Kind,action.PlayedCardKind,action.ConversionSource,
                    action.TargetCardId,string.Join(",",targets)))) return;
                actions.Add(action with {TargetSeat=targets[0],TargetSeats=targets,ProgramSkillId=grant.Source.SkillId,ProgramActivationId=grant.Source.BindingId,Description=action.Description+"（增加目标）"});
            }
        }
    }
}
