# 0001 — Pairing Stages, Round-Robin Generation, and Rating Order Within Score Groups

**Status: Proposed**  
**Date:** 2026-10-16  
**Basis:** Spec 0000, referee feedback (Yulia), and subsequent owner clarifications on 2026-10-16.  
**UI languages:** Ukrainian (default) and English only.

## 1. Scope and governing rules

Deliver these referee-facing improvements:

1. A Standings checkbox to sort by **rating inside score groups**, independent of official tie-break criteria.
2. Deterministic **single-cycle round-robin (Berger) pairing**, generating the selected round by default, with an option to generate all rounds of the current round-robin stage at once.
3. A choice, **"Assign starting numbers randomly"**, available at the first round-robin draw. Unchecked means stable registration/participant-ID order; checked means randomized numbers. Either assignment is persisted and then immutable for that stage.
4. A way to **change pairing method beginning with a specified future round** (initial supported journey: round robin, then Swiss). Earlier pairings and results survive, and new players may join from a later round.
5. **One continuous tournament** across stage changes: previously earned points, wins, opponents, and tie-break coefficients are not reset.
6. Score **every bye as one win / one tournament point**, regardless of the pairing system. A bye is a victory without a played game.

**Most important invariant:** A game with a recorded result is never modified, deleted, renumbered, recolored, or replaced by automatic pairing or a pairing-system change. No confirmation is sufficient to override this invariant. To change a played game, the referee must use the existing explicit manual editor; the generator must not change it.

### 1.1. Out of scope

- Automatic Swiss, McMahon, or Scheveningen pairing. Their generation controls remain unavailable until individually specified.
- Automatic handicap allocation. Round-robin colours follow Berger, not handicap/strength; referees may manually change colours.
- A separate tournament per stage, or separate stage-specific scoring tables.
- Automatic forfeits, withdrawal-management workflow, and special skipped-round scoring.
- Repeated/double round robins; dynamic re-optimization of the round-robin schedule after entry/withdrawal.
- Creating a fake participant called "Bye".
- Adding a new round-robin stage **after** a non-round-robin stage; changing or deleting an existing stage boundary. These can be specified later.
- Guessing unknown legacy XML Kind codes or silently losing unsupported metadata.

## 2. Pairing stages versus tournament scoring

### 2.1. Persistent stages

Add a persistent ordered series of pairing stages. Conceptually, each stage contains:

- startRound (inclusive, in 1..NumberOfRounds).
- system: roundRobin, swiss, mcMahon, scheveningen, or an unidentified imported legacy system.
- For RR only: frozenRosterIds, an **ordered** list of tournament-local participant IDs. Position in this list is the participant's Berger **starting number** (1-based).
- For RR only: whether the starting numbers were initially assigned randomly; optional generation metadata as needed to make later requests reproducible.

The stage ends immediately before the next stage's startRound, or at the tournament's final round. Exactly one stage is effective for each round. The first stage starts at round 1. A later RR stage after Swiss/McMahon/Scheveningen is deferred in 0001.

Old persisted tournaments without stages are interpreted as a single stage using their current system, without changing participants, existing pairings, historical XML, or results. Imported tournaments with unrecognized legacy Kind have an unidentified first stage; automatic pairing for it stays disabled, but the referee can create a later supported stage.

The previously global Tournament.System field is **the most recently selected/current configured pairing system**, rather than "the method that necessarily governed every historical round" or "always the first stage". Update and persist it when the referee selects a new method for a stage. The stage sequence is the authoritative record of which method actually governs each round. This distinction also applies to legacy export (§6).

**Settings behavior:** When the tournament has exactly one stage **and no pairing in that stage has been drawn yet**, the existing **Tournament system** selector remains editable and changes that initial stage's method (and the last-selected system) together. Once **any pairing has been drawn** in the first stage, or a second stage has been defined, the Settings selector is **read-only**; it displays the last selected system and directs the referee to **"Change pairing system starting from round..."** for subsequent changes. Neither a settings save nor a partial form update may silently change a historical stage. The Pairings screen separately shows the effective system of its **selected round**, which may differ from the last-selected system shown in Settings. This restriction applies to tournaments restored from disk or imported with existing pairings, not only to newly generated tournaments.

### 2.2. Single scoring history

All completed games and awarded byes from all stages contribute to the same tournament standings according to the tournament's calculation settings. SOS, SODOS, SOSOS and future criteria use the continuous encounter history. Changing the pairing method never wipes earlier results, alters prior opponents or restarts the score tally.

For a later Swiss generator, all prior played RR matches are previous encounters for rematch avoidance. Precise Swiss/McMahon pairing rules are deferred; do not implement an approximate solver in 0001.

A player's lack of participation **before their actual joining round** is not a skipped round and must not trigger skipped-round notices. The join date is stage-independent and persisted per participant (e.g. firstEligibleRound). This is necessary when a participant is added after the Swiss stage has already started.

**Joining round applies to every tournament, not only mixed-system tournaments.** Existing stored/imported participants without this new field default to **round 1** for backward compatibility. For a newly added participant the proposed default is the **currently selected round if it has no recorded results**, otherwise the **next round**. Consider a result-bearing bye as a scored outcome; a round with only unplayed pairings has no recorded results. The referee may explicitly choose an allowed round from 1..NumberOfRounds, including round 1 when there is no frozen RR stage, to enter historical paper tournaments. The joining round does not retroactively rewrite actual games/results. Never auto-create an extra round if the current final round already has recorded results: the referee must first extend the tournament or use an existing valid round. If a stage rule disallows the proposed default, use the earliest permitted round **not earlier than actual admission** or require an explicit correction; never silently enroll a newcomer in an already frozen RR stage.

### 2.3. Fixed RR roster and later entrants

- Capture the RR stage's finalized, eligible participant IDs and starting-number assignment atomically on the **first successful RR generation** (per-round or all-round).
- The roster, its membership, and its order are frozen thereafter. Later edits to rating, names, or other participant attributes do not reorder starting numbers.
- During an ongoing RR stage, new participants cannot be added to its frozen roster; changes to preliminary status/availability or deletion cannot silently invalidate frozen schedules or played games.
- To admit new players while an RR stage is already frozen, create a **later non-RR stage boundary first**. A new player's firstEligibleRound must fall within that later non-RR stage, **at or after the player's actual joining round**, not automatically at the stage's start. Expose and validate it in the participant editor and prevent retrospective assignment to earlier RR rounds. If no such future round exists, reject the addition with a translated, actionable error.
- **Before the initial RR draw** (roster not frozen), any participant who will take part in that RR stage must have firstEligibleRound equal to **that stage's startRound**; no mid-cycle entry is permitted. A proposed joining-round default that lands later in that stage must be normalized to the stage start only when this represents genuine pre-draw registration, or the referee must explicitly change it. Once RR is frozen, none of its absent provisional players may become RR entrants.
- A player joining at round 6 of a Swiss stage that began in round 4 is **not** treated as missing rounds 1–5.
- A frozen RR member marked absent or unavailable for a scheduled round does not cause automatic reshuffling. Stop generation with an actionable localized error; the referee may handle withdrawal by recorded referee-decision results (§5.3).

### 2.4. Finalizing preliminary registrations

Finalizing a preliminary participant who was **not included in a frozen RR roster** is an **admission of a new participant** for joining-round purposes, whether performed individually or in a batch. It must not make the player newly eligible inside the frozen RR stage.

- When a later non-RR stage exists, finalization is allowed with firstEligibleRound in that stage, no earlier than the player's actual joining round; select or confirm an appropriate valid round in the UI. If none exists, reject individual finalization with a localized explanation that the referee must configure a subsequent pairing-system stage.
- Before the first RR draw, a preliminary participant can be finalized for that RR stage only when the roster is still mutable and their firstEligibleRound is the RR stage's startRound.
- **Finalize all preliminary** becomes a **partial-success batch operation**: finalize every eligible preliminary participant, leave invalid ones preliminary, and return their IDs/names **and reasons** so the UI explicitly reports which were skipped. Do not treat an invalid entry as permission to mutate a frozen roster. If no participant is eligible, return a clear no-op report; do not claim all were finalized.
- Preserve existing validation for players already paired in imported data; do not delete or change their historical results during finalization. A batch applies accepted changes atomically and reports rejected entries separately.

### 2.5. System change beginning at round K

Expose **"Change pairing system starting from round..."** with target round K and next system. The supported scenario in this spec is **RR → Swiss**, while automatic Swiss pairing remains disabled. A change to McMahon/Scheveningen may be stored but must not imply an implemented generator. A subsequent RR stage is not supported yet.

- Preserve all rounds before K exactly, including their pairings, played/unplayed state, colours and recorded results.
- Do not allow a retroactive change that reassigns completed rounds to a different method.
- Existing unplayed pairings in rounds K..end may be deleted/replaced only after explicit confirmation; **any result-bearing pairing in affected rounds prevents the change altogether**. Check and apply atomically.
- The stage history and last selected system are persisted; the currently displayed pairing method resolves from the stage covering the selected round.
- Joining players become eligible from their firstEligibleRound and have no fictitious earlier games.
- The old RR frozen roster and prefix of its Berger schedule remain intact.
- Moving/removing an existing boundary, or switching back to RR after Swiss, is deferred; do not offer an enabled UI control for these operations.

## 3. Round-robin pairing

### 3.1. Fixed single-cycle Berger schedule

Generate the standard deterministic Berger/circle-method fixture from the stage's frozen starting-number order.

- With N even, one complete cycle has N−1 rounds and N/2 games per round.
- With N odd, one complete cycle has N rounds, (N−1)/2 actual games plus **one bye per round**, with exactly one bye per player across the cycle. An empty rotation slot may be used internally; it must not become a real participant.
- All N(N−1)/2 unordered player pairs occur exactly once over a full cycle.
- For each stage, fixture index starts at stage.startRound and never depends on which round the user generates first.
- Pairing seed, starting numbers, colours, and bye positions must be reproducible after a process restart and in any generate-round order.
- Orient colours according to a standard Berger colour table, with sensible near-balance verified by tests (difference no greater than one across a full cycle). **Do not alter generated colours based on ratings or the handicap setting.** The referee can use the ordinary manual pairing editor to swap colours when needed; a played game so changed remains immutable to generation.
- Only valid tournament participants may appear. Bye uses the existing Pairing.IsBye representation and a single real player. Keep the agreed one-player-pair visual presentation and existing valid board-number conventions.
- Generate regular pairings with Unset result, ResultByReferee=false, and Protected=false.

### 3.2. Starting-number choice

Before a stage's first successful generation show an unchecked checkbox:

- **UA:** "Присвоїти стартові номери випадково"
- **EN:** "Assign starting numbers randomly"

Unchecked: assign starting numbers deterministically in stable tournament-local participant-ID/registration order. **Not** by rating and **not** from the rating-within-score-groups viewing mode.

Checked: take the same eligible roster and perform **one random permutation** of starting numbers. Persist that actual permutation, not just the checkbox or random seed. Present the assigned starting numbers to the referee in the pairing UI (at least the player→number mapping).

The selection takes effect on **first successful generation** and is saved with the roster atomically. Generation failure or confirmation cancellation must not freeze or reshuffle the roster. All later generations reuse the saved assignment, regardless of the current checkbox value. Once frozen, disable/relabel the checkbox so that checking it again cannot produce a new draw. This is not a new per-round random draw.

### 3.3. Stage length and check timing

For RR continuing to the end of the tournament, require **exactly** N−1 (even N) or N (odd N) RR rounds. Extra rounds imply a later explicitly configured stage, not a second RR cycle. Too few rounds likewise fail if no later stage is configured.

A referee may truncate the RR schedule deliberately by creating a later non-RR stage (e.g. three of seven rounds RR, then Swiss). Those RR rounds are the prefix of the single Berger fixture.

**Validate schedule length when Generate is invoked, not while saving basic tournament settings**: during registration the referee may still be changing the number of participants/rounds. Validate a requested stage against its current start/end and frozen roster. Reject impossible or mismatched configurations with an actionable error, without any writes.

### 3.4. One selected round versus all RR rounds

Use the existing **"Жеребкувати" / "Generate pairings"** command. By default it generates the **selected round only**.

When the selected round belongs to RR, offer an unchecked checkbox:

- **UA:** "Жеребкувати всі тури"
- **EN:** "Generate all round-robin rounds"

When checked, generate/reconcile **every round within that RR stage**, not the entire tournament after a transition. Generating stage rounds individually in arbitrary order must produce exactly the same fixture as the all-round command.

Before mutating anything:

- Pre-compute the requested Berger fixtures, stage/roster changes and all conflicts.
- Check eligible participants, fixed roster, round limits, duplicate players, unique boards, bye slots and stage boundaries.
- A **played pairing is immutable** to the generator: keep its IDs, participants, colours, board, result, protection, referee-decision flag, handicap, komi, and comment exactly as stored.
- A played pairing involving the **same two participants** as the prescribed Berger match is **compatible regardless of which player has Black or White**. Do not demand the generated orientation; do not swap it back.
- A played pairing with different opponents or a mismatch in the bye assignment blocks the request; report the conflict and make **no changes anywhere**, including in other requested rounds. Never try to alter a played game to repair the fixture.
- Already compatible unplayed pairings may be retained without needless ID churn. Incompatible unplayed pairings may only be replaced with a clear explicit confirmation listing affected rounds/counts. A declined confirmation changes nothing.
- Apply the entire selected-round/all-stage request as **one atomic operation**. On error, no partial pairings, roster freezes, ID increments or stage mutations remain. Re-running an already-correct request is idempotent.
- Never silently destroy unsaved referee data or lose legacy-retained information on replaced unplayed pairings: the confirmation must explain such loss where relevant.

### 3.5. No manual-protection mode in RR

The checkbox **"Жеребкування власноруч" / manual pairing protection (Protected)** must be hidden or disabled for RR rounds. The RR schedule is determined by starting numbers; it is not an optimization problem with protected manual anchors.

- Creating or automatically generating an RR pairing never sets Protected=true.
- For imported historical RR pairings, preserve their existing protection metadata if the pairing is kept, but the flag does not control Berger generation of unplayed pairings. Replacement still requires confirmation.
- For non-RR rounds, existing manual-protection semantics stay in force for future pairing algorithms.

Ordinary manual editing of games (including colour swap, result, comments) remains available. After a result is saved, no automatic generation operation may ever change that pairing.

## 4. Bye scoring: one point / one win for every system

Replace spec 0000's temporary "byes not scored" limitation **in the current standings calculator**:

- Every recorded bye yields **one win and exactly one tournament point** to its sole real participant, whether the stage is RR, Swiss, McMahon or Scheveningen. It is a **victory without a played game**.
- Do **not** add a fake opponent, a played match, a referee-decision result or any second point for the bye. Bye is represented by IsBye, not by setting a two-player result.
- Bye contributes nothing as an opponent to SOS/SODOS/SOSOS: there is no opponent. However, the awarded point participates in **the receiving player's overall score**, and therefore affects SOS/SODOS/SOSOS of genuine opponents exactly as any other earned point would.
- Display the awarded bye as a bye / +1 in round cells and the player's total wins/score. The old "byes not scored" warning must no longer appear for a successfully scored bye of **any system**.
- No new special skipped-round or other result-type scoring is implied. Continue to give notices for truly unsupported cases, not for a standard bye.
- Import existing legacy byes into the same calculation; keep legacy XML structure unchanged.

## 5. Referee operations and withdrawal

### 5.1. Continuity and round availability

Every pairing, result and bye belongs to the single tournament; the effective round's pairing system only controls generation. A participant cannot be assigned to multiple pairings in one round. The existing rule that a preliminary or unavailable player cannot receive a **new** manual pairing continues, subject to frozen RR-roster consistency.

The generation UI must clearly label the effective system and whether the current RR stage's starting numbers are frozen. A non-RR selected round with no supported generator must keep the disabled/unavailable action; do not claim Swiss generation works.

### 5.2. Handicap and colours

No handicap algorithm in 0001. RR pairs follow Berger colour orientation, even if tournament handicap settings are enabled. Existing manual handicap and colour editing remain available; never normalize a manually changed **played** game back to Berger.

### 5.3. Player withdrawing during RR

A withdrawal does **not** delete or reshuffle a player in an already frozen RR stage. Existing schedule entries remain. The referee may manually assign a technical win/loss for the affected two-player games using the already implemented result editor.

For **every technical result awarded due to withdrawal**, the referee explicitly checks **"Result by referee decision" / "Результат за рішенням судді"** (existing ResultByReferee field). Make this field available and understandable in this flow. Preserve it on save, reload, XML round-trip (where mapped), standings, and subsequent generation. A played referee-decided game remains immutable to auto-pairing. There is **no automatic withdrawal detection or mass-award button** in this spec.

## 6. XML compatibility and application save semantics

There is an unresolved legacy-format limitation: only **Swiss Kind=2** is verified. RR, McMahon and Scheveningen Kind values are not proven and **must not be guessed**, including the tempting assumption RR Kind=0.

The owner's decision for mixed-system tournaments is: **store/export the last selected pairing system as the tournament's current system**, rather than forcing the first-stage system onto the save.

- Inside the application/SQLite, always persist **both**: (a) authoritative per-round stage history and RR roster/starting numbers/join rounds, (b) the latest selected configured pairing system. The latter must survive reload, including a transition from RR to Swiss.
- For XML export, the single legacy Kind represents the **last selected pairing method**, not the historically first one. If that method has a **verified** Kind code (currently Swiss=2), export it, even when earlier rounds were round robin. Export the actual prior RR games/results/bye entries as ordinary historical round data.
- Legacy XML cannot describe per-round pairing stages, frozen starting numbers, randomization metadata or join rounds. Explicitly report those **lost-on-XML-export limitations**; when the same XML is imported elsewhere, it must not be presented as a full-fidelity stage backup. When a lossless round trip is required, use the application's persistent tournament save.
- If the last selected method has **no verified Kind code** (e.g. a pure RR tournament), continue blocking legacy XML export with an accurate localized error. Do **not** mislabel RR as Swiss just to create a file. Verifying RR Kind requires suitable actual legacy-format evidence in a later task.
- Preserve current support for imported unknown Kind tokens and existing source XML as far as verified. Do not overwrite unknown historical fields without mapping evidence.
- Newly persisted JSON fields must have backward-compatible defaults for pre-0001 databases; no existing pairing/participant/result may disappear in migration.

Keep business rules in AutoKorsak.Domain, import/export mapping in AutoKorsak.LegacyXml, and thin HTTP mutations in AutoKorsak.Api. Pairing calculation and bye-score math must not be duplicated in React. Mutations use the existing serialized, transactional save mechanism. Add localized error codes and confirmations in Ukrainian and English.

## 7. Standings view: sort by rating inside score groups

### 7.1. Checkbox and official standings isolation

On Standings add an unchecked checkbox:

- **UA:** "Сортувати за рейтингом у межах очкових груп"
- **EN:** "Sort by rating within score groups"

**Unchecked:** unchanged official standings: saved criterion order, enabled tie-breaks (SOS/SODOS/SOSOS, where calculated), official places/shared ties, current-round inclusion switch and column layout.

**Checked:** a referee's **pairing preparation view**, ordered by:

1. Score group total, descending.
2. Starting participant rating, descending.
3. Display name ascending, then local participant ID.

Do not use SOS, SODOS, SOSOS or other competition tie-breaks to decide the order **within** a score group. No mutation of the tournament's rank criteria, official places, results, ratings or pairing seed. Remember this view preference **per tournament in the browser**, not as official data. Make the active preparation mode visible.

Show the calculated score group for each row. Do **not** present a view-sorted row ordinal as an official place. Round-cell opponent row references must use the newly displayed order when checkbox is checked. The ordinary official standings remain intact and can be restored immediately.

### 7.2. Score used in the preparation view

For selected round R, the pairing-preparation score is computed from **all earlier rounds < R** and ignores R and later rounds, regardless of the ordinary "include current round in calculations" switch. Do not let an already-entered result in R influence that round's pairing preparation.

- Swiss and RR: cumulative supported points/wins from earlier rounds, including **+1 for each bye**.
- McMahon: StartScore plus cumulative supported earned points from earlier rounds, including byes. McMahon start scores are currently imported/stored, but not editable in the UI. Do not add start-score editing in 0001.
- Mixed stages: every earlier round contributes, irrespective of the pairing method.
- Players not yet eligible in earlier rounds: no fabricated absences or penalties; they have no previous played points.
- Unresolved special-result or skipped-round calculations must still be signaled; do not invent their compensation.

Compute this projection in the domain/backend so later Swiss and McMahon algorithms can reuse the same pre-round score-group contract. The preparation view is a **display/ordering feature**, not automatic Swiss or McMahon pairing.

## 8. Acceptance and regression tests

### 8.1. Domain, API and persistence

1. Four-player RR: exactly 3 rounds, 2 games/round, 6 unique unordered pairs, correct Berger opponents and colour balance.
2. Five-player RR: exactly 5 rounds, 2 games plus 1 bye per round; all 10 unique games; one bye per player; no fictional participant.
3. Starting numbers: unchecked preserves deterministic ID/registration order; checked produces a valid unique random permutation (using a controlled source in tests). Persist actual mapping, not just an RNG seed or checkbox. Repeated generation, round-order variation and restart preserve it.
4. Generate later RR round before earlier one; compare with all-round generation using same frozen seed. Equivalent opponents/colours/byes.
5. RR terminal stage must have **exactly** N−1/N rounds; a three-round prefix with a later Swiss stage is valid. These checks fire at **generation**, not during registration/settings edits.
6. Per-round generation affects only selected round; all-round generation affects exactly the RR stage. Repeating a correct generation does not create duplicates or replace unchanged pairing IDs.
7. An existing **played** pairing that matches the Berger opponents with **swapped colours** is accepted and untouched, including all metadata. An existing played wrong-opponent or bye-incompatible pairing blocks the entire request with zero mutations, even when "generate all" is selected.
8. Replacing incompatible unplayed pairings requires explicit confirmation; refusal is no-op. Any validation/confirmation failure is fully atomic, with no frozen roster or advanced pairing IDs.
9. No RR pair is created as Protected; the RR manual-protection checkbox is disabled and imported protection cannot force an alternative Berger fixture. Non-RR manual-protection behavior remains unchanged.
10. RR withdrawn player remains on the frozen schedule; manually awarded technical results have ResultByReferee=true and survive save/reload and any later generation unchanged.
11. Bye gives **one win and one point in RR and Swiss alike**. Standings, pre-round group totals, and opponent SOS reflect the awarded point; no bye-unscored notice. SOS does not invent an opponent for the bye.
12. RR to Swiss from round K: earlier completed rounds/colours/results remain untouched; latest selected method is persisted as Swiss; all earlier results remain part of global standings and encounter history; no Swiss generator becomes active.
13. Later participant joining in round 6 of a Swiss stage that started at round 4 has no false absence/skipped-round notices in rounds 1–5. Earlier frozen RR roster remains unchanged.
14. Any result-bearing game in affected rounds blocks stage transition; unplayed future pairings require confirmation before deletion. Later RR stage and boundary-removal controls are absent/disabled.
15. Pure RR, with no verified Kind code, has a truthful blocked XML export. RR→Swiss mixed tournament with latest system Swiss exports **Kind=2** while reporting stage/starting-number/join-round metadata loss. Previous unedited XML fixtures still round-trip unchanged.
16. Pre-0001 persisted JSON loads without mutation of historical pairings. An unidentified imported stage remains unknown until expressly changed.
17. Standings checkbox OFF produces the old order and places; ON groups by **pre-round points**, then rating regardless of SOS/SODOS, and ignores results in current/later rounds. Restoring OFF restores official standings.
18. McMahon preparation group uses stored StartScore plus previous-game score and byes, without implying unsupported general McMahon calculations.
19. Single stage with no drawn pairings: Settings system selector updates both stage and last-selected system. Any drawn pairing or second stage makes it read-only; direct API settings updates cannot bypass the restriction. The Pairings view resolves the selected round's stage method.
20. Old players with no joining-round property default to round 1. Newly added players in ordinary tournaments default to the current result-free round or the next round if results exist; explicit allowed backfill to round 1 works; no phantom round is created after a completed last round.
21. RR stages enforce start-round eligibility for entrants before the freeze and disallow mid-cycle admission afterward; a new player joining the Swiss stage at its round 6 is not considered present in rounds 1–5.
22. Individual preliminary finalization inside a frozen RR stage is rejected unless the player can join a subsequent non-RR stage. Batch finalization commits eligible players, skips ineligible ones, and returns per-player localized-reason data, including the all-skipped case. Historical played games remain untouched.

### 8.2. Frontend and referee flows

- Referee selects RR, sees the two distinct checkboxes (**assign starting numbers randomly** before the initial draw; **generate all rounds** on a generate request).
- First draw freezes and shows starting numbers. Subsequent requests cannot redraw numbers inadvertently.
- A five-player RR visibly has byes and each bye is reflected as **+1** in standings, without an unsupported-bye warning.
- Referee swaps colours on a game, records the result, and re-runs generate all: that played game stays byte-for-byte unchanged.
- Referee records a withdrawal-related technical result using **"Result by referee decision"**; the flag remains set.
- Referee transitions from RR rounds 1–3 to Swiss from 4, adds a player joining at round 6, and observes unchanged historical standings and correct pre-round grouping.
- Tournament system is editable in Settings before any pairing is drawn in the sole stage; after a pairing exists or a second stage exists it is read-only and points to stage change, while Pairings identifies the selected round's actual system.
- A new participant defaults to the current round if still unplayed, otherwise the next round, with appropriate corrections for RR freezes and end-of-tournament conditions; the referee may deliberately backfill a non-frozen tournament to round 1.
- Individual and batch preliminary finalization do not admit a participant into a frozen RR stage; batch finalization reports explicitly skipped players and reasons.
- UI indicates the current round's effective method. Unsupported Swiss/McMahon/Scheveningen automatic generators remain disabled.
- Both Standings modes, new confirmations, and warnings work in uk-UA and en, with localization-key parity.

Run the existing backend/frontend Docker test profiles, TypeScript checks, build, and a focused bilingual browser smoke test. Do not deploy without owner instruction.

## 9. Approval gate

This document supersedes the earlier **Proposed** text of spec 0001 in place; it is not a separate patch spec. In particular, do **not** retain the old assumptions that byes score zero, that the global system must always identify the first stage, that RR starting numbers are always registration-order, or that a played Berger match with reversed colours conflicts.

Do not modify the approved historical spec 0000. This 0001 remains **Proposed** until its status is explicitly changed to Approved by the owner.
