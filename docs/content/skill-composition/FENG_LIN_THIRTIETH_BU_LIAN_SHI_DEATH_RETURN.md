# Owned death-benefit return (6800)

This opt-in operation replaces only the current OL death-benefit recipe's first node. The original issued recipient, Draw3, Recover1, source instance, limited issuance and cancellation policy remain unchanged. Legacy 6303 and every recipe without 6800 keep their existing guards and command semantics.

`selectIssuedFixedRecipientWithDeathReturn` accepts only an optional OwnerDied/owner trigger with exactly this three-node recipe: select the original issued recipient, Draw3 to selectedTarget, Recover1 to selectedTarget. It creates a typed `ProgramOwnedDeathBenefitReturn` on that exact ProgramSkillFrame after the existing original-recipient selection succeeds.

The return freezes the original ProgramDeathTriggerWindowFrame candidate and cursor, DeathFrame, optional direct original DyingFrame, actual turn and original damage owner. The original frames and damage attempt stay on the resolution stack unchanged. All exposed nested lists use constructor/init cloning, including with-expression and JSON initialization. Facts contain only public scalar frame/source/recipient identities.

ActiveDying excludes only an already-dead original victim's direct DyingFrame whose exact 6800 Program -> OwnerDied window -> Death -> Dying ancestry still matches the frozen return. This structural predicate never calls ActiveDying. A new living victim's Dying remains visible. Layered death benefits can exclude their own proven dead original Dying independently; an arbitrary frame or receipt is insufficient.

Nested Damage admission additionally requires the original typed return plus the exact Draw/recovery first child and the complete paid observer subtree, using existing strict movement, HP, Damage, DyingEntering, SelfDyingResponse, physical/view-as Peach and bound/virtual Alcohol edges. It does not extend an unrelated generic observer allowance. Damage and HP loss use their actual producers and retain typed attack returns.

Completion or cancellation validates the unchanged original cursor and no outstanding descendant, emits a scalar return fact and pops only the owning benefit program. Its qualification then ceases; the normal OwnerDied, Death and original Dying return paths finish their own cursors. Paid Draw/recovery is not repeated. Global restoration checks run before the existing Death early return so nested descendants cannot evade validation.

Required additive hooks: enum 6800; descriptor/composition discovery; ProgramSkillFrame nullable receipt; original-recipient eligibility/AI/receipt union only for 6800; dead-owner operation admission; ActiveDying structural filtering; BeginProgramSkillDamage exact paid-root admission; runtime completion; AssertCore before Death's early return. Existing BeginProgramSkillDying, actual Damage BeginDying, AttackHpLoss Dying, rescue dispatch and CompleteProgramAttack/CompleteAttack then operate on the filtered active Dying without broad exemptions.

Current scope supports nested damage/HP loss while the original Dying victim is already dead. It deliberately does not hide a live rescue target or permit two simultaneous living victims' Dying resolutions. No rule epoch/schema/package version change is proposed. This is an implementation draft: no compiler, production loader, test, native-AI or benchmark has been executed. Final OLD baselines await the parent integration window.
