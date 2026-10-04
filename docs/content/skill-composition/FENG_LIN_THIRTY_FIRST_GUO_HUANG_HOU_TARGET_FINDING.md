# Zero ordinary-trick target-window review

Status: frozen, read-only static finding. No compiler, loader, check, UI, native AI or benchmark was executed. The original Core delivery ebfb5fc5d2e68406dff3074a184199ef06adfe00e26bdb65ba82f7950b1c6600 and its 13 NEW / followups 01–03 remain unchanged.

## Confirmed P2: legal target mutation leaves a stale zero-use window action

`MatchesTieredRoundZeroUseAction` in the effective followup-01 ZeroReturns file compares the complete current `CardUseFrame.Action` with the argument action. `AssertProgramCardWindowState` supplies the still-live `ProgramCardTriggerWindowFrame.Action` for CommittedTrick, FinalizedTrick and BeforeTrickTargetEffects. A zero-material use has no physical cost matching effect card 0 and is not a selected-actor Duel, so this new gate is the only matching branch.

Two mature, current registered producers modify the actual use's targets without updating that same-use window action:

* `classic:benxi` in `classic-wu-yi.rules.json` triggers `ApplyCurrentCardEnhancements` at CardUseCommitted. `ApplyProgramCurrentCardEnhancements` in `GameEngine.CurrentCardEnhancements.cs:48` normalizes a real DrawTwo action from its implicit empty target list to `[SourceSeat]`, then replaces only the CardUse at line 59. The same original CommittedTrick window remains on the stack while the enhancement prompt is published. The original window action still has empty target/designated lists. `ResolveCardEnhancementChoice` at lines 138–144 similarly replaces only the use when a target is added. Four-player own-turn Benxi's preceding outgoing-distance grant makes the enhancement condition feasible, so this is a real source path, not arbitrary frame injection.
* The issued 604 original-opponent entitlement collects its real actor candidate at CardUseTargetsFinalized. `OfferOriginalTargetAddition` in `GameEngine.OriginalTargetAdditions.cs:243` normalizes DrawTwo's self target using `UpdateLifecycleCardUse`; `ResolveOriginalTargetAdditionChoice` at line 266 appends the issued original target using the same method. `UpdateLifecycleCardUse` in `GameEngine.CardUseLifecycle.cs:8–11` replaces only the owning use. Neither producer synchronizes the still-live FinalizedTrick window action. A tier-2 zero DrawTwo/ordinary trick with a genuine earlier winning contest and issued entitlement therefore reaches the same mismatch.

The full value comparison correctly rejects these two different target tuples, but the tuples differ because the legal target producer left the window stale. The command boundary then rejects the otherwise genuine zero-use continuation. Existing physical materials continue through their original physical-match branch, so the repair must remain a new-capability opt-in rather than changing all old target-window behavior.

## Excluded concern: SameTypeAid already synchronizes the exact window

`AddSameTypeAidTarget` in `GameEngine.SameTypeActualUseAid.cs:236` calls `UpdateProgramRoleCardUse`. `GameEngine.ProgramCardUseRoles.cs:115–122` replaces the use and every live ProgramCardTriggerWindow with that exact ParentFrameId, assigning its updated action. `AppendSameTypeAidFinalizedCandidates` then reads the newly stored window and preserves that action while appending candidates. Thus a genuine Duel/FireAttack SameTypeAid target addition does not create this target-list mismatch. No defect is asserted for that path.

## Narrow repair recommendation

After the four precise target normalization/addition updates above, synchronize only live ordinary-trick windows belonging to a genuine issued tier-2 zero-material Use. Retain exact parent/action id, Use type, current ordinary kind, effect card 0, original receipt/payment/quota, all non-target action values and the exact trick continuation. Apply the current action to those windows while preserving their candidates, frozen candidate contexts, cursor and typed return. Do not weaken the complete-value matcher or create an exception for arbitrary frames/target changes; do not modify legacy physical uses. A small branch in the existing third Core method can establish genuine tier2 and Benxi, issue a zero DrawTwo, pause in the real enhancement choice, restore the accepted journal and continue both actual targets. That remains an unrun check draft.

Root has assigned the separate implementation to `guo-huang-hou/followup-04-actual-target-windows`; this report does not edit the original delivery. UI review is a separate pending report, to be based on Sun's frozen owned UI manifest.
