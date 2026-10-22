# Custom mapping handlers

Register a compiled implementation of `IAkeneoCustomMappingHandler` in DI.
The mapping page lists each compatible handler by its display name. Mapping storage
uses `NopTargetType.CustomHandler` and the stable handler key in `NopTargetKey`;
no handler settings column or executable admin code is needed.

A registered handler does nothing until explicitly selected on an attribute mapping.
Handlers receive the resolved raw value, mapping, product context/profile request,
and current attribute definition. Source selection honors mapping locale/channel and
product role/family scope. Validation runs on mapping save and again during sync.
Custom handlers use direct attributes without templates, transforms, or fallbacks.
Only one mapping per handler may apply to a product role. Missing handlers and
incompatible source schemas produce review items without changing their targets.

Planning must be read-only. Add operations to the supplied plan and attach writes
as execution callbacks. The core section renders the plan for dry run and executes
it for import. A review discards the handler's partial plan; a required review
fails the section. This is a trusted compiled extension contract, not a sandbox for
untrusted code.

## Tier prices (from table)

In Akeneo Connection → Attribute Mappings, locate `tier_prices`, choose
**Tier prices (from table)** as the Target Type, select the desired family/product
roles and locale/channel, and save. Registration does not create a mapping automatically.

The attribute must be `pim_catalog_table` with numeric columns `min_qty` and
`tier_price`. Example raw value:

```json
[
  { "min_qty": "10", "tier_price": "9.50" },
  { "min_qty": "20", "tier_price": "8.00" }
]
```

Quantities must be positive whole numbers within Int32 range and unique. Prices
must be nonnegative and fit nopCommerce's decimal(18,4) storage. Numeric JSON
values and invariant numeric strings are accepted. Prices are interpreted in the
primary store currency; the table has no currency column or currency conversion.

The handler compares the complete product tier-price set without relying on row
order or database IDs. If different, it replaces the entire set transactionally,
including any existing customer-role, store-specific, or dated tier prices.
New rows apply to all customers and stores with no dates. There is no ownership
tracking, and unchanged sets cause no writes.

An absent/null source preserves existing prices. An explicit empty array clears
the table only when the profile's product-field missing-value behavior is
`ClearExisting`. A required missing/empty table is an error. Any invalid row
blocks the whole replacement. Scalar Tier Price mappings cannot target the same
product roles alongside this handler.

Run Dry Run to review the full current/proposed price sets before importing.
