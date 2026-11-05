# Fix: one phone pairs at a time

**Status:** branch `claude/fix-pairing-race`, from `feature/ui-redesign` · **Date:** 2026-11-05 · **Severity:** High (security)

## Why this work happened

- **Who found it:** the repository audit (2026-11-05, issue 1), which reproduced it 5 times out of 5.
- **The bug:** `PhoneLinkHost.HandlePairingAsync` checked "is a pairing already running?" inside the lock, released the lock, ran the whole handshake, and only then recorded the new pairing. Phones that arrived during that window all passed the check.
  - Each phone finished the handshake with its own pairing code and raised its own prompt on the computer.
  - Each new pairing overwrote the previous one, so the earlier sessions were orphaned and never disposed.
  - Any of those prompts could still be accepted.
- **A smaller bug in the same spot:** the "already pairing" refusal was sent fire-and-forget, then the connection was closed immediately, so the phone could miss the reason.

## Decision

- **One slot, taken before the handshake:** a single pairing slot (`_pairingSlot`) is taken inside the lock *before* the handshake. It holds a reservation while the handshake runs, then the `PairingSession` while both sides answer.
- **One atomic check:** the "is pairing open" and "is the slot free" checks happen in that same locked block.
- **Released on failure:** if the handshake fails or the phone hangs up, the reservation is released, so an abandoned attempt can't block pairing until the window closes.
- **Refusals awaited:** the phone always receives the reason.

**Rejected:**
- **Queueing phones:** pairing is a deliberate one-at-a-time action. A queue would show prompts the user didn't expect.
- **A semaphore:** a reference-checked slot also tells `EndPairingAsync` which session it may end, which the existing code already relied on.

## Rules and procedures applied

- **Test-first:** `ConcurrentPairingTests` covers three cases:
  - three phones at once give one prompt, and the other two are told "already pairing" (failed 3 of 3 runs before the fix: all three got prompts with different codes);
  - after the computer declines, another phone can pair;
  - a phone that hangs up mid-handshake doesn't hold the slot.

  The last two passed before the fix too; they guard against the fix holding the slot too long.
- **Security-sensitive code (ECC rule):** cancellation, failure, accept, decline and disposal were each reviewed for slot release. Ran 5 times in a row: all pass. Full suite: 969 tests.

## Open items

- **Pairing closed mid-handshake:** if pairing is closed *during* a handshake, the phone still gets a prompt. This behaviour predates the fix. Re-checking `_pairingOpen` after the handshake would close the gap.
- **Related hardening from the audit (issue 6):** no cap on simultaneous handshakes; the beacon broadcasts the computer name while any phone is paired, not only while pairing is open; secrets are stored in plaintext outside Windows.
