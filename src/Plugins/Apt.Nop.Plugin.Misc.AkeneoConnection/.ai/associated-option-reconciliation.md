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
are removed. Attribute
caches and parent product events are handled after commit.

Delta, single-item, resumed-cursor, failed, canceled, truncated, and incomplete runs
never perform this cleanup. Merge and Replace managed retain their existing behavior.
Parents with no verified associated option in the run are outside this cleanup's scope;
the existing tracked missing-product lifecycle policy remains separate.

The family variant setting **Automatically preselect the first variant** defaults to
enabled on installation and upgrade. After the batch and cleanup finish successfully,
it selects the lowest final display order for new or existing parents with no selection.
Disabling it preserves saved selections and prevents automatic variant preselection.
Combination products select all values of one existing combination, ordered by the
family's variant sort keys and then axis display order, with combination ID breaking ties.
Associated options use display order and then value ID.

Preselection checks every leaf in the parent's Akeneo branch, including all pages and
submodels. Delta runs may use live bindings from earlier successful syncs for siblings
outside the current run. Missing bindings, missing destinations, failed or incomplete
runs, resumed cursors, and single-item imports defer selection. It respects the profile's
existing-product write policy, preserves partial selections, and logs each changed parent.

Deploy the updated plugin, restart nopCommerce, and run Resync with Replace all values.
Include the updated `plugin.json` in deployment. Schema releases must increment its
version as well as the migration timestamp, because nopCommerce skips plugin upgrade
migrations when the installed and deployed plugin versions match.
Repair is not required. Review removal entries and preservation warnings in the sync log.
