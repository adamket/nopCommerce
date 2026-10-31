# Associated options and Replace all values

Complete, successful Full runs and Resyncs now reconcile associated-product options
when Product attribute sync mode is Replace all values and existing-product updates
are enabled. This runs independently of Missing product behavior, including Ignore,
and works when the normal Resync skips unchanged leaf writes. No schema change is needed.

The batch records each encountered source and its committed destination. Cleanup is
limited to attribute mappings whose associated options were verified during this run.
For each such parent, it reads all pages of its Akeneo product-model branch and leaf
products without profile filters. Every leaf must have been encountered by this run
and have an existing destination binding. Filtered-out leaves and unbound skips cause
the whole parent to be preserved, with a warning. Bound held/skipped leaves retain their
associated options. A parent with only held/skipped leaves does not authorize cleanup.

After all remote inventories have completed, obsolete associated options, including
legacy/manual options, are removed in per-mapping transactions. Simple products and
ordinary attribute values are not deleted. Values referenced by combinations and
deleted parent products are preserved. Deletions are logged, managed relation records
are removed, and remaining values receive a default selection if necessary. Attribute
caches and parent product events are handled after commit.

Delta, single-item, resumed-cursor, failed, canceled, truncated, and incomplete runs
never perform this cleanup. Merge and Replace managed retain their existing behavior.
Parents with no verified associated option in the run are outside this cleanup's scope;
the existing tracked missing-product lifecycle policy remains separate.

Deploy the updated plugin, restart nopCommerce, and run Resync with Replace all values.
Repair is not required. Review removal entries and preservation warnings in the sync log.
