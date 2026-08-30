# Leaf family-variant resolution

Akeneo leaf product payloads expose `parent` but do not expose `family_variant`.
For any product with a parent, this plugin resolves the effective family-variant
code from the immediate parent product model and carries that value through the
sync context, desired-state hash, variant relationship handling, axis handling,
and value/template resolution.

## Failure behavior

A leaf with a parent is not allowed to silently use a family-wide variant
configuration when the parent family-variant code cannot be resolved. The item
is failed with an operator-visible error instead. This is intentional: an
incorrect variant configuration can create structurally incorrect nopCommerce
products.

An exact disabled family-variant mapping falls back to the enabled family-wide
default. A disabled family-wide default is not considered an effective mapping
and does not supply variant axes.

## Deployment note

The desired-state hash includes the resolved family-variant mapping, but a hash
change is only observed after a product enters the delta candidate set. Run one
full product synchronization after deploying this family-variant resolution fix
to reconcile existing leaf products that may previously have synchronized with
the family-wide default mapping.
