# Safe whole-profile resync checkpoints (2026-11-21)

A new whole-profile resync can establish the delta checkpoint after all pages finish
successfully without truncation, using the profile's normal write policy. Leave the
family selector empty and turn off **Update existing products only**. Repair may be
on or off. Warnings without failures are allowed. Family-only runs, write overrides,
failed/cancelled/incomplete runs, and historical resyncs never qualify.

The checkpoint uses the run's start time with the normal two-minute delta overlap.
The resync retains its Resync run mode and never applies missing-product rules.
Log retention protects qualifying checkpoints just like successful delta runs.
The schema migration adds ResyncCheckpointEligible with a false default, so no
historical resync is assumed safe. Product write mode now participates in the scope
hash: existing checkpoints require a one-time new baseline, which a qualifying
whole-profile resync can supply. Changing families or write mode invalidates a
checkpoint for the old scope. This does not deploy or run a resync automatically.
