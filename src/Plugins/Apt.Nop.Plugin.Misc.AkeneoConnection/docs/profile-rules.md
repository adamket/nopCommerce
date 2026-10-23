# Publication templates and profile skip rules

Publication conversion uses the existing **Attribute Mappings → Computed value**
feature. Add a computed mapping with target **Product field → Published**, and
set its template to:

```text
{sync_status == published ? "true" : "false"}
```

Set **Apply only when** to:

```text
sync_status == published OR sync_status == unpublished
```

This condition allows the publication template only for those two statuses.
For hold and future statuses, the destination property is preserved. It does not
fall back to Akeneo's enabled flag or apply a missing-value clearing policy.
An intentionally skipped Published mapping on a new product leaves it unpublished.
Other mappings still run unless a profile skip rule holds the entire product.

Conditions support ==, !=, AND, OR, parentheses, and quoted literals; AND takes
precedence over OR. Raw option codes are compared using the mapping's channel,
locale, and inherited values. Missing/unreadable condition values and invalid
expressions block the item even for optional mappings. Every comparison must
resolve; an OR branch does not hide a missing source value.

A false condition bypasses template rendering, transformations, and required-value
checks for that mapping. Dry runs and import logs report the skip reason.
For category and tag mappings, skipping a contribution preserves the whole collection
because its complete intended membership is unknown. Other destination areas
continue normally. Blank conditions retain the previous behavior.

Mark the mapping **Required**, so a missing status blocks the import rather than
using the profile's missing-value behavior. Apply it to the appropriate product
roles (product models, standalone products, and variants as needed). Avoid another
mapping targeting Published for the same role. Normal family/global mapping scope
and locale/channel settings apply. There is no separate profile value-mapping table.

Conditions compare raw option codes, ignoring case, using the existing template
language. The expression returns true for `published` and false for other present
values, including `unpublished`. The asynchronous template renderer inherits missing
attributes from product models, preserving explicitly empty child values and
resolving the selected channel and locale. The same renderer serves import and dry run.

To skip products based on an attribute value, open **Sync Profiles → Edit → Profile
Skip Rules**, click **Add skip rule**, and enter an Akeneo attribute code and the raw
option code or value to match. Save the profile. Rules are generic; no attribute-specific
presets or default rules are added automatically.

Skip rules are evaluated before templates or product changes. Any matching rule
skips the entire item, including core fields, variants, prices, inventory, categories,
and assets. A matching ancestor also blocks descendants, because importing a child
can update its parent. Standalone products and direct product-model imports use
the same rules. Comparisons use exact, case-insensitive raw codes in the profile's
channel and first selected locale, not translated labels or substrings. For a
multiselect attribute, an exact matching member satisfies a rule.

Existing skipped bindings are marked seen without changing their destination,
lifecycle status, or last successfully applied desired-state hash. New held products
create neither nopCommerce products nor empty bindings. Held items encountered
during authoritative reconciliation are therefore not treated as missing. Keep
held items inside the profile's source filters: rules cannot inspect items excluded
by family/category/completeness/enabled/search filters. This feature does not enable
full runs if the application currently disables them.

Dry runs show the matching rule and no planned writes; import logs record why the
item was skipped. Changing skip rules changes the profile scope fingerprint so a
delta run can revisit unchanged products with the new configuration.

The plugin migrations add a nullable `SkipRulesJson` column to the profile
table and a nullable `ApplyWhen` column to attribute mappings. Rebuild/restart the plugin before using the updated editor so the migration
and updated code are loaded together.

## Deleted Akeneo attributes

Sync checks mapping sources against a fresh Akeneo attribute catalog once per
import request or batch run. Ordinary mappings, fallback sources, and attributes
referenced by computed templates or their conditions are checked. Product payload
fields such as identifier, family, and enabled do not require attribute definitions.

If a referenced attribute was deleted, the entire affected mapping is skipped with
a warning. Its destination is preserved, including when missing-value behavior is
configured to clear values. An unavailable attribute catalog blocks the item; a
connection or permissions failure is never treated as an empty catalog. An existing
attribute with a missing product value still follows the normal required-value rules.

Saved mappings for deleted attributes appear under **Missing Akeneo attributes** in
Attribute Mappings. Use **Delete mapping**, or **Remove override** for a family
mapping. Inherited mappings must be deleted from **Global defaults**. These rows
cannot be edited or selected as new mapping sources. Removing a family override
reveals its global mapping if one exists. No mappings are deleted automatically.
## Combination availability

In **Family Mappings**, select **Product attribute combinations** and enable
**Manage variants by Akeneo enabled status**. This is a single on/off setting;
no template is required. It defaults to off. An enabled family-variant mapping
replaces the family default, including when its toggle is off.

When on, disabled Akeneo SKUs have their managed combinations removed and
re-enabled SKUs have them recreated through the normal SKU, axis, price, and
stock mappings. Parents, shared attribute values, and manual values are preserved.
Unused values owned by the profile may be cleaned up. Profile skip rules run
first, so a held SKU keeps its existing combination. Missing Akeneo enabled status
blocks changes. Standalone, grouped, and associated-product variants are unaffected.

Profiles using this setting must allow updates, select **Any** for the enabled
filter, and keep both states inside their other source filters. Changing the
toggle invalidates the affected profiles' scope fingerprints.

Version 4.90.27 updates the single consolidated migration. Restart nopCommerce
to add the boolean setting. Existing enabled-status conditions (enabled == true
or enabled != false) are converted to an enabled toggle. Other custom conditions
are kept in the legacy column as an inactive backup and leave the toggle off.
Rerunning the migration does not reset a saved toggle.
