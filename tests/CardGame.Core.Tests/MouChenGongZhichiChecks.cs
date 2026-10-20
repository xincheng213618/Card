namespace CardGame.Core.Tests;

internal static class MouChenGongZhichiChecks
{
    public static void NativeDamageNaturesSourceLessChainsArmorAndHpLossBoundariesCold()
    {
        var s = MouChenGongScene.Start(new("native-all-damage") { Mingce = false, Zhichi = true, OwnerHp = 12, PauseDamage = true });
        s.Use("normal-two"); s.Reach(() => s.Hold("damage-child")); var protection = s.E<MouChenGongZhichiIssuedEvent>().Single().Protection;
        s.Need(s.V().Hp == 10 && protection is { DamageAmount: 2, DamageTargetSeat: 0, Nature: DamageNature.Normal, SourceLess: false } &&
            protection.ActualTurnNumber == s.G.State.TurnNumber && protection.ActualTurnOwnerSeat == 0 && protection.Source.SkillId == MouChenGongScene.Zhichi &&
            s.E<DamageAppliedEvent>() is [{ TargetSeat: 0, Amount: 2 }],
            "The first genuine two-point applied damage is one native occurrence, lands fully and issues protection for this exact actual turn.");
        s.Cold(); s.Continue(); s.Play();
        foreach (var nature in Enum.GetValues<DamageNature>())
        {
            var id = nature.ToString().ToLowerInvariant(); s.Use(id); s.Play(); s.Use("foreign-" + id, [2]); s.Play();
        }
        s.Use("source-less"); s.Play(); var sourceLess = s.E<MouChenGongZhichiPreventedEvent>().Single(e => e.SourceSeat is null);
        s.Need(s.V().Hp == 10 && s.E<DamageAppliedEvent>().Length == 1 && s.E<MouChenGongZhichiIssuedEvent>().Length == 1 &&
            s.E<MouChenGongZhichiPreventedEvent>().Length == 7 && s.E<MouChenGongZhichiPreventedEvent>().All(e => e.Protection == protection && e.TargetSeat == 0 && e.Amount == 1) &&
            s.E<DamageRequestedEvent>() is [{ TargetSeat: 0, Amount: 2, SourceLess: false }] && s.E<MouChenGongZhichiPreventedEvent>().Count(e => e.SourceSeat is not null) == 6 &&
            sourceLess is { TargetSeat: 0, Amount: 1 } && s.E<ProgramDamagePreventedEvent>().Count(e => e.FrameId == sourceLess.WindowId && e.SkillId == MouChenGongScene.Zhichi &&
                e.BindingId == "prevent-later-damage" && e.OwnerSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 1,
            "All Normal, Fire and Thunder attempts from self/another source plus genuine source-less damage are prevented by the same unlimited issued protection.");
        s.Use("lose-self"); s.Play();
        s.Need(s.V().Hp == 9 && s.E<MouChenGongZhichiIssuedEvent>().Length == 1 && s.E<DamageAppliedEvent>().Length == 1 &&
            s.E<MouChenGongZhichiPreventedEvent>().Length == 7,
            "Direct LoseHp still debits HP and neither consumes protection nor fabricates a qualifying damage occurrence.");

        var armor = MouChenGongScene.Start(new("native-armor-only") { Mingce = false, Zhichi = true, OwnerHp = 12 });
        armor.Use("armor"); armor.Play(); armor.Use("normal"); armor.Play();
        armor.Need(armor.V() is { Hp: 12, Armor: 2 } && armor.E<DamageAppliedEvent>().Single().ArmorPayment is { ArmorBefore: 3, ArmorAbsorbed: 1, ArmorAfter: 2, HpLossAmount: 0 } &&
            armor.E<MouChenGongZhichiIssuedEvent>().Length == 1,
            "Native fully Armor-absorbed Damage1 is still an applied native damage occurrence; it arms without inventing an HP-loss requirement.");
        armor.Use("fire"); armor.Play(); armor.Use("source-less"); armor.Play();
        armor.Need(armor.V() is { Hp: 12, Armor: 2 } && armor.E<MouChenGongZhichiPreventedEvent>().Length == 2 && armor.E<DamageAppliedEvent>().Length == 1,
            "Later prevented damage never spends the remaining Armor or issues another damage receipt."); armor.Use("lose-self"); armor.Play();
        armor.Need(armor.V() is { Hp: 11, Armor: 2 } && armor.E<MouChenGongZhichiIssuedEvent>().Length == 1, "Real LoseHp bypasses native Armor and issued protection.");

        var prevented = MouChenGongScene.Start(new("prior-native-prevented") { Mingce = false, Zhichi = true, OwnerHp = 12, PreventFirst = true });
        prevented.Use("normal"); prevented.Play();
        prevented.Need(prevented.V().Hp == 12 && prevented.E<ProgramDamagePreventedEvent>().Length == 1 && prevented.E<MouChenGongZhichiIssuedEvent>().Length == 0 && prevented.E<DamageAppliedEvent>().Length == 0,
            "An independent actual native prevention before the first applied occurrence does not arm Zhichi.");
        prevented.Use("normal"); prevented.Play();
        prevented.Need(prevented.V().Hp == 11 && prevented.E<DamageAppliedEvent>().Length == 1 && prevented.E<MouChenGongZhichiIssuedEvent>().Length == 1,
            "The next genuinely applied damage still lands first and only then arms the protection.");

        var chain = MouChenGongScene.Start(new("physical-fire-chain") { Mingce = false, Zhichi = true, OwnerHp = 12, Hand = [MouChenGongScene.C(CardKind.FireSlash)], PauseBefore = true });
        chain.Use("normal"); chain.Play(); chain.Use("chain", [0]); chain.Play(); chain.Use("chain", [1]); chain.Play();
        chain.Native(CardKind.FireSlash, [1]); chain.Reach(() => chain.Hold("attempt-child") && chain.P is { PlayerSeat: 0 });
        var native = chain.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.FireSlash && f.SourceSeat == 0);
        var before = chain.G.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Single(f => f.TargetSeat == 0 && f.Nature == DamageNature.Fire);
        chain.Need(chain.V(1).Hp == 11 && chain.V().Hp == 11 && before is { Amount: 1, SourceSeat: 0 } &&
            (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == native.Id && native.CardId > 0 && native.CardAttack is { TargetSeat: 0, SourceSeat: 0, IsChainPropagation: true, DamageAmount: 1 } &&
            chain.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1 && e.Nature == DamageNature.Fire && e.Amount == 1) &&
            chain.E<ChainedDamagePropagatedEvent>().Count(e => e.ResolutionId == native.Id && e.SourceSeat == 0 && e.FromSeat == 1 && e.TargetSeat == 0 && e.Amount == 1 && e.Nature == DamageNature.Fire) == 1,
            "A genuine physical FireSlash damages the other chained target and its real native chain child reaches the protected original owner.");
        chain.Cold(); chain.Continue(); chain.Play();
        chain.Need(chain.V().Hp == 11 && chain.V(1).Hp == 11 && chain.E<MouChenGongZhichiPreventedEvent>().Single().Protection.Nature == DamageNature.Normal &&
            chain.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 0) == 1 && chain.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1 && e.Nature == DamageNature.Fire),
            "The real propagated Fire attempt is prevented while its genuine primary target damage and original card use finish normally.");
    }

    public static void AcquisitionIssuedProtectionSourceLossAndActualTurnExpiryCold()
    {
        var s = MouChenGongScene.Start(new("real-acquisition-loss-extra-turn") { Mingce = false, Zhichi = true, ConditionalZhichi = true, InsertPlay = true, OwnerHp = 4 });
        s.Need(!s.Has(MouChenGongScene.Zhichi), "The healthy original owner genuinely lacks the HP-conditioned Zhichi source before the first damage.");
        s.Use("normal"); s.Play();
        s.Need(s.V().Hp == 3 && !s.Has(MouChenGongScene.Zhichi) && s.E<MouChenGongZhichiIssuedEvent>().Length == 0,
            "The genuine old damage fully completes while Zhichi remains absent from its native afterDamageApplied window.");
        s.Use("lose-self"); s.Play();
        s.Need(s.V().Hp == 2 && s.Has(MouChenGongScene.Zhichi) && s.E<MouChenGongZhichiIssuedEvent>().Length == 0,
            "A separate later native HP loss grants the source without reusing an already completed old damage window as protection.");
        s.Use("normal"); s.Play(); var issued = s.E<MouChenGongZhichiIssuedEvent>().Single().Protection; var oldTurn = s.G.State.TurnNumber;
        s.Need(s.V().Hp == 1 && issued.ActualTurnNumber == oldTurn, "Only the next actual damage while the source was already held lands and issues protection.");
        s.Use("recover-self"); s.Play(); s.Use("recover-self"); s.Play();
        s.Need(s.V().Hp == 3 && !s.Has(MouChenGongScene.Zhichi), "Two real recoveries remove the actual wounded-only source after protection was issued.");
        s.Use("foreign-normal", [2]); s.Play(); s.Use("source-less"); s.Play();
        s.Need(s.V().Hp == 3 && s.E<MouChenGongZhichiPreventedEvent>().Length == 2 && s.E<MouChenGongZhichiPreventedEvent>().All(e => e.Protection == issued),
            "The already issued original protection survives genuine skill loss and prevents multiple later damage attempts without any live source."); s.Cold();
        s.Use("lose-self"); s.Play(); s.Need(s.Has(MouChenGongScene.Zhichi) && s.V().Hp == 2, "A real direct loss regrants the wounded source without being damage.");
        s.Use("thunder"); s.Play(); s.Need(s.V().Hp == 2 && s.E<MouChenGongZhichiIssuedEvent>().Length == 1, "Regrant neither revives old unqualified damage nor creates another protection charge.");
        s.End(); s.Play(); s.Need(s.G.State.TurnNumber == oldTurn && s.G.State.CurrentSeat == 0, "The actual inserted Play returns to normal Play within the same actual turn.");
        s.Use("fire"); s.Play(); s.Need(s.V().Hp == 2 && s.E<MouChenGongZhichiPreventedEvent>().Length == 4, "A second real Play phase is not an actual turn boundary and retains the issued protection.");
        s.Use("extra-turn"); s.Play(); s.End(); s.Reach(() => s.G.State.TurnNumber > oldTurn && s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        s.Need(s.E<TurnEndedEvent>().Any(e => e.TurnNumber == oldTurn && e.ActorSeat == 0) &&
            s.E<TurnStartedEvent>().Where(e => e.TurnNumber > oldTurn).ToArray() is [{ ActorSeat: 0, TurnNumber: var nextTurn }] && nextTurn == oldTurn + 1 && s.G.State.TurnNumber == nextTurn,
            "The very next actual turn is the pended owner extra turn, with exactly one new native TurnStarted and no intervening natural player turns.");
        s.Cold(); s.Use("normal"); s.Play();
        s.Need(s.V().Hp == 1 && s.E<MouChenGongZhichiIssuedEvent>().Length == 2 && s.E<MouChenGongZhichiIssuedEvent>().Last().Protection.ActualTurnNumber != oldTurn &&
            s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 0) == 3,
            "The first real hurt of the new actual extra turn lands, then arms a fresh receipt; old-turn protection never leaks across the boundary.");

        var foreign = MouChenGongScene.Start(new("genuine-foreign-turn") { Mingce = false, Zhichi = true, OwnerHp = 12, ForeignPulse = true });
        foreign.End(); foreign.Reach(() => foreign.Action("select-target")); foreign.Answer(c => c.Targets.SequenceEqual([1])); foreign.Reach(() => foreign.Hold("foreign-arm"));
        var f = foreign.E<MouChenGongZhichiIssuedEvent>().Single().Protection;
        var parent = foreign.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(p => p.SkillId == MouChenGongScene.Pulse).Id;
        foreign.Need(foreign.G.State.CurrentSeat == 1 && foreign.V().Hp == 11 && f.ActualTurnOwnerSeat == 1 && f.Source.OwnerSeat == 0,
            "A genuine foreign actual Play-start native damage lands on the owner and arms for that other player's actual turn.");
        foreign.Cold(); foreign.Continue(); foreign.Reach(() => !foreign.G.ResolutionStack.Any(p => p.Id == parent));
        foreign.Need(foreign.V().Hp == 11 && foreign.E<MouChenGongZhichiPreventedEvent>().Length == 3 && foreign.E<MouChenGongZhichiPreventedEvent>().All(e => e.Protection == f) &&
            foreign.E<DamageAppliedEvent>().Length == 1,
            "The same genuine foreign turn continues with Fire, self-sourced Normal and Thunder native attempts, all prevented by the issued receipt.");
        foreign.Play(); foreign.Use("normal"); foreign.Play();
        foreign.Need(foreign.V().Hp == 10 && foreign.E<MouChenGongZhichiIssuedEvent>().Length == 2 && foreign.E<MouChenGongZhichiIssuedEvent>().Last().Protection.ActualTurnNumber != f.ActualTurnNumber,
            "The foreign turn ending expires protection before the next real own-turn hurt, which lands and issues anew.");
    }
}
