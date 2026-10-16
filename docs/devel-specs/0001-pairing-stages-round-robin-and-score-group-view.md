# 0001 — Pairing Stages, Round-Robin Generation, and Rating Order Within Score Groups

**Status: Proposed**  
**Date:** 2026-10-16  
**Basis:** Implemented specification 0000 and referee feedback (Yulia), confirmed by the owner.  
**Languages:** Ukrainian (default) and English; no Russian interface strings.

## 1. Goal and product contract

Implement the referee's first post-prototype requests:

1. In **Standings**, offer an optional view that orders participants by their score group and then by rating, instead of using standings tie-break coefficients to order players.
2. Implement deterministic **round-robin (Berger/circle-method) pairing** through the existing round-by-round pairing workflow, with an optional checkbox to generate the entire round-robin stage at once.
3. Keep a round-robin stage's participant roster fixed once drawing begins, while allowing the referee to **change the pairing system from a later round**, e.g. rounds 1–3 round robin and rounds 4–7 Swiss with newly added participants.
4. Keep **one continuous tournament**: previous games, results, points, opponents, and coefficients remain relevant after a pairing-system transition.

A pairing system controls the creation of pairings. It must **not** partition the tournament into independent competitions or reset results.

### Explicit exclusions

- Automatic Swiss, McMahon, and Scheveningen pairing algorithms. Their buttons remain disabled until those algorithms are specified and implemented.
- A new bye-points policy, advanced skipped-round scoring, or full McMahon standings calculation.
- External rating synchronization, catalog-upsert changes, authentication, and printing/reports.
- Double or repeated round-robin cycles, dynamic re-optimization of a Berger schedule, and an algorithm for compensating an unavailable player.
- Automatic legacy XML Kind-code discovery. Do not guess unknown legacy system codes.
- An independent "fictitious bye player" in the participant catalog or tournament.

## 2. Independent concepts: tournament results versus pairing stages

### 2.1. Pairing stages

Introduce a persistent, ordered pairing-stage model. Each stage has, conceptually:

- **startRound**: inclusive first round, between 1 and tournament.NumberOfRounds.
- **system**: the pairing method effective from that round; initially roundRobin, scheveningen, swiss, or mcMahon.
- **frozenRosterIds**: for a round-robin stage, the ordered tournament-local participant IDs used by its fixed schedule, initially unset until the stage is first drawn.

The stage ends immediately before the next stage's startRound, or at tournament.NumberOfRounds. There is exactly one effective stage per round. The initial/default stage starts in round 1 and derives its method from the existing tournament system. Existing persisted tournaments and imported legacy fixtures must remain readable without rewriting their pairings or source XML.

The global Tournament.System and its existing legacy mapping must remain compatible with older data. **Do not reinterpret it as the system of every historical round** after stage boundaries exist. For pairing operations, resolve the effective method from stages. Standings scoring and historical results remain global.

### 2.2. Player participation across a stage transition

- Before the first round-robin draw, the eligible roster is finalized, non-preliminary tournament participants. Capture their IDs and **order** in frozenRosterIds on the successful first generation (the same atomic commit as the generated pairings).
- Once frozen, later rating/name edits must not change the Berger seed order. No player may enter or leave that stage; do not silently regenerate a different tournament schedule when registration or participant count changes.
- For new entrants after a transition, store a minimal **firstEligibleRound** or equivalent persistent joining-round fact. A player added for a Swiss/McMahon stage starting in round K must not be considered part of earlier round-robin rounds. Such players must not be falsely flagged as skipped in earlier rounds, paired retroactively there, or included when rebuilding the old Berger schedule.
- An explicit future stage boundary must exist **before** a new participant is admitted in a way that would change a frozen round-robin stage. If there is no later stage, reject the operation with a localized actionable error; do not mutate the frozen roster.
- Continue permitting ordinary participant metadata edits. Disallow changes to registration/availability or deletion when they would invalidate a frozen round-robin stage or its played pairings. Do not silently unpair anyone.
- A player's round availability is not a request to replace a Berger opponent. If a frozen round-robin participant is unavailable in a requested round, stop generation with a localized conflict and no writes; the referee must resolve it explicitly.

An implementation may use other storage shapes, but it must retain the stage method, stable Berger roster order, and joining-round semantics after restart.

### 2.3. Changing the pairing system from round K

Provide a referee action **"Change pairing system starting from round..."** (Ukrainian/English), selecting a round and the new method. The essential supported journey in 0001 is **round robin → Swiss**; transitioning to McMahon may be recorded, but its automatic pairing remains deferred. Do not imply Swiss/McMahon generation is available.

- Keep every earlier round, its actual pairings, results, colours, and history unchanged.
- Future RR schedule slots no longer belong to the RR stage after the boundary. Unplayed/generated pairings in affected future rounds may be cleared only by explicit confirmation; **any existing recorded result in an affected round blocks the change**. No partial stage mutation on failure.
- A transition cannot retroactively reassign completed rounds to another pairing method.
- Newly added participants after the transition are eligible only from their declared joining round; they start with no prior game results. Existing participants carry all previous results.
- For later Swiss/McMahon pairing, all prior tournament encounters remain in opponent history. A future Swiss generator will prefer avoiding rematches with earlier RR opponents, subject to its future separately specified rules.
- Do not introduce separate tournaments, duplicate participants, or reset the standings on transition.

The UI must show the effective pairing system for the selected round and make stage boundaries comprehensible to the referee.

## 3. Round-robin generator

### 3.1. Algorithm and stable schedule

Use the standard deterministic single-cycle Berger/circle rotation applied to the stage's immutable ordered roster.

- For N players, each unordered pair meets **exactly once** over the full schedule.
- Even N: N − 1 scheduling rounds, N/2 games per round.
- Odd N: N scheduling rounds; add an internal empty rotation slot, yielding exactly **one bye per round** and exactly one bye for each participant across the full cycle.
- A bye is an existing Pairing with IsBye=true and one real player. **Do not create a participant named "Bye".** Preserve the agreed existing visual treatment of byes as one-player pairings; do not add a new special board-number policy.
- Pairings for a particular round must be reproducible in any request order (e.g. generate III before I) and after save/reload. First-round seeding follows a stable, explicitly persisted participant-ID order; it is **independent of the Standings rating-sort checkbox**.
- Orient games as black/white deterministically, using a Berger-compatible balanced color schedule. On a full cycle, each player's total black/white count differs by at most one. Existing completed games are never recolored automatically.
- Newly generated pairings have Unset result, no referee decision, and are **not manually protected** by default. Existing pairing IDs and recorded history must not be needlessly rewritten when the calculated fixture already matches.

### 3.2. Schedule length and stage limits

A complete single RR cycle requires exactly N − 1 (even N) or N (odd N) rounds. If the tournament stays RR until its end, its configured round count must support that cycle; reject a mismatched configuration with a clear explanation rather than pretending that a truncated or repeated schedule is a complete RR tournament.

The referee may intentionally end an RR stage early by scheduling a later pairing-system transition. Such a stage uses only the prefix of the same fixed Berger cycle (e.g. three RR rounds before Swiss begins). No second RR cycle is inferred.

For any generated round, compute the Berger schedule index relative to that stage's startRound, not the global tournament round number.

### 3.3. Per-round and all-round commands

Reuse the existing **"Жеребкувати" / "Generate pairings"** action on the Pairings screen, operating on **only the selected round by default**.

When the selected round belongs to RR, display an unchecked checkbox:

- **UA:** "Жеребкувати всі тури"
- **EN:** "Generate all round-robin rounds"

If checked, generate or reconcile **all rounds of that RR stage**, bounded by the next stage (never touch future Swiss/McMahon rounds). The result must be identical to individually generating each RR round, regardless of order.

For any requested rounds:

- Validate the frozen roster, round availability, stage limits, board uniqueness, pairing uniqueness, and all existing pairings **before writing anything**.
- Existing compatible played pairings remain intact, including result, color, notes, and provenance.
- If an existing played pairing conflicts with the deterministic fixture (players, bye or colours), fail atomically with a localized explanation; never discard a played result merely to regenerate.
- Unplayed pairings that must be replaced may be replaced **only after a clear confirmation** showing the affected rounds/counts. A declined confirmation changes nothing.
- Apply the entire request as one atomic tournament mutation, including multiple rounds. On validation failure nothing changes, including the previously unset frozen roster.
- Repeating generation for an already valid target is idempotent and must not change pairing IDs or results unnecessarily.

### 3.4. Manual protection in a round-robin stage

The referee explicitly requires that **"paired manually / protected pairing" not operate for RR**:

- Hide or disable the Protected/Manual pairing checkbox while editing an RR round. Creating or generating RR pairings must not enable it.
- The RR generator follows its fixed Berger schedule; the Protected flag is **not a reason to override or deform the schedule**.
- Preserve imported historical Protected metadata if the historical pairing is otherwise left intact, but do not let that flag defeat RR regeneration on an unplayed round. Confirm replacement as in §3.3.
- This is an explicit RR-only override of spec 0000's future protection rule. For non-RR algorithms, maintain the existing protected-pairing contract.

The referee may still manually edit a result, comment, or game as allowed by existing validations. A subsequent regeneration must follow §3.3 and never silently erase those edits.

## 4. Standings checkbox: rating order within score groups

### 4.1. UI behavior

Add an unchecked checkbox to the **Standings** screen, independent of the tournament's persisted ranking-criteria configuration:

- **UA:** "Сортувати за рейтингом у межах очкових груп"
- **EN:** "Sort by rating within score groups"

**Unchecked:** preserve the existing official standings order (enabled competition criteria, including SOS/SODOS/SOSOS), official places/ties, and saved layout settings.

**Checked:** show a *pairing-preparation view* ordered by:

1. **Score-group total**, descending.
2. Player rating, descending.
3. Display name, ascending; then stable tournament-local participant ID.

Do not use SOS, SODOS, SOSOS, or any other standings tie-breaker **to break ties within a score group**. Make the selected mode clearly visible. Do not change persisted official placements, the enabled ranking criteria, player ratings, games, or the tournament system. A view-only preference may remain local to the frontend; no tournament data migration is required for the checkbox itself.

### 4.2. Which score is used

The referee is preparing the selected round **R**. Therefore this view's score groups use **only results of rounds strictly before R**, irrespective of the separate existing "include current round in calculations" toggle. A result accidentally entered early for round R cannot change the draw-preparation order of that same round.

- For Swiss and round-robin contexts, use accumulated supported game points from earlier rounds.
- For a McMahon context, use the player's stored McMahon start score **plus** supported game points from earlier rounds. This limited grouping use does not claim that the advanced McMahon standings or skipped-round calculations have been implemented.
- For a mixed-system tournament, earlier rounds of *all* pairing methods contribute to the score group; switching methods must not reset the tally.
- For a new entrant from round K, earlier absent/nonexistent rounds contribute no games and must not be interpreted as unsupported skipped rounds merely because the participant did not exist in that stage.
- Byes and other outcomes whose scoring remains undefined under 0000 must not silently receive invented points. Surface the existing calculation-limitation notices as applicable; do not claim that provisional groups are exact when they depend on unsupported scoring.

In checked mode, display each player's **pairing score-group value** explicitly. The row order/ordinal is not an official place: avoid labeling a newly sorted ordinal as a final place. Keep the official standings view and its place/tie semantics untouched. Opponent row-number references in round cells, if shown, must correspond to the order currently displayed; do not leave stale row-number links after sorting.

The backend/domain should expose or calculate this projection from authoritative game data; do not reproduce scoring business logic ad hoc in React. Future Swiss and McMahon generators must be able to reuse the same pre-round score-group contract.

## 5. API, persistence, compatibility, and error handling

- Keep the existing .NET Domain → API → React boundaries. Add a domain pairing-schedule component and thin API endpoint(s); no pairing algorithm in the browser.
- Pairing generation and system transitions are validated domain mutations under the existing serialized atomic write mechanism. A failed action must not alter tournaments, rounds, pairing IDs, stage metadata, or catalog data.
- Extend the persisted tournament JSON backwards-compatibly. Missing stage metadata in old saved tournaments implies a single initial stage with the original system. Retain the existing legacy XML source untouched.
- Keep existing round-edit operations and imported historical pairings functional. Migrating old fixtures must not create or delete games.
- The **legacy XML has no verified mapping for mixed pairing stages or stage-join metadata**. Preserve valid game results and pairings; report those app-only metadata limitations on export and do not fabricate Kind values. Continue applying the current export block when a Kind code is unverified.
- Document new API contract(s), stage metadata, update restrictions, and incomplete scoring in architecture/status/decisions. Preserve the approved 0000 specification.
- New errors and confirmations are localized in **uk** and **en** only. Use existing confirmation and reload/state-update patterns.
- The proposed 0001 is **not implementation authorization** until the owner changes Status to Approved.

## 6. Acceptance tests

Minimum deterministic backend tests:

1. RR 4 players: 3 rounds, exactly 2 games per round, 6 unique unordered pairs, each player meets every other exactly once; colors balanced.
2. RR 5 players: 5 rounds, exactly 2 games plus one single-player bye per round, exactly one bye for each player, and all 10 unique games; no fictitious participant is inserted.
3. Generate rounds out of order, individually and then via the all-round checkbox/API: same fixture schedule as all-at-once generation; stable across reload.
4. Generate an already-correct round again: no duplicate pairings, no unnecessary pairing-ID or result changes.
5. An existing played compatible pairing remains exactly unchanged; an incompatible played pairing blocks the whole request with zero writes.
6. An incompatible unplayed pairing requires affirmative confirmation for replacement; cancellation leaves all rounds unchanged. Atomicity also holds when one of several requested rounds fails.
7. RR generation never grants manual Protected, while imported protection does not override the RR schedule. No change to existing non-RR protection rules.
8. After the RR roster is frozen, adding a participant to that same RR stage or invalidating a member's eligibility is refused. A future Swiss stage can admit a new participant without altering the frozen RR schedule.
9. RR rounds 1–3, Swiss from round 4: stage boundary and historical results persist across restart; changing the stage never rewrites played rounds; unsupported Swiss generation remains disabled.
10. All-round RR generation stops at the RR stage boundary and does not create Swiss pairings.
11. New Swiss-stage entrants do not trigger a false skipped-round notice in earlier RR rounds. Prior RR results still contribute to subsequent tournament totals and opponent history.
12. Normal Standings view is byte-for-byte equivalent in ranking/places to the previous implementation when the new checkbox is off.
13. Preparation view for round R ignores results from R and later; same pre-round points with different SOS/SODOS but different ratings sorts by **rating**, not coefficients.
14. McMahon pairing-preparation group calculation includes StartScore plus previously recorded supported results; explicitly warn where unsupported outcome scoring prevents a definitive answer.
15. Legacy XML unedited fixtures still round-trip unchanged, and no unsupported stage metadata is silently claimed as serializable.

Frontend integration/browser checks:

- Checkbox OFF/ON changes only display order/group presentation, not persisted official ranking, and has uk/en text.
- RR selected round: one-round generation default; all-round checkbox generates remaining/all stage rounds as specified; odd-roster bye is visible.
- Manual-protection UI disabled for RR; visible/usable for non-RR manual pairings.
- Stage method and transitions are visible; replacing unplayed future RR pairings requests confirmation; any result-bearing future rounds block unsafe stage change.
- A new entrant after the RR → Swiss boundary cannot be manually placed into an earlier RR round.

Run both Docker test profiles, typecheck, build, and a short Ukrainian/English referee-flow browser smoke test. Report actual outcomes. No deployment without owner instruction.

## 7. Owner review before approval

This is a proposed contract, not a retroactive modification of spec 0000. Its key interpretation choices are deliberately explicit for owner/referee review:

- Berger roster/seed is fixed on first successful RR generation, rather than recomputed on each click.
- A terminal RR-only stage must fit a complete single cycle; an RR stage may end early only through an explicit later-system boundary.
- System transitions change **pairing method**, never historical scoring; unsupported future pairing generators remain disabled.
- Standings pairing-preparation mode uses results **before the selected round**, independently of the official standings counting toggle.
- RR ignores manual Protected semantics, but never silently destroys played results or existing XML history.

Do not implement an alternative rule silently. If the owner wants to revise any of these, resolve it in this Proposed specification **before** marking it Approved.
