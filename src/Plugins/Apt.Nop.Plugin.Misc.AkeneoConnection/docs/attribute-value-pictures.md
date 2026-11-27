# Akeneo attribute value picture mappings

The Product attribute value pictures destination imports a variant's images into the gallery of its bound nopCommerce product and associates them with one existing product attribute value. It uses the product binding, not a new SKU lookup. This is intended for commemorative printed/digital child products with a Design attribute.

## Configuration

In Akeneo Connection → Asset mappings, select the `commemoratives` family and add a mapping:

| Field | Value |
|---|---|
| Source | `variant_pictures` |
| Apply mapping to | Variant products |
| Destination | Product attribute value pictures |
| Target product attribute | Design |
| Attribute value key or template | Leave blank when each product has exactly one Design value |
| Adopt matching existing picture associations | Off initially; enable deliberately after reviewing the dry run if PIM should own matching existing links |
| Storage | Import into nopCommerce (automatic) |

Keep the source asset family, media field, filtering, sort field, captions, alt text and maximum-assets settings appropriate to the existing Akeneo asset collection. Use the sync profile's Replace managed asset policy for ongoing synchronization.

The optional key field accepts literal text or normal product template syntax such as `{design_key}`. Zero or multiple matching values preserve the current pictures and report a diagnostic. A single mapping must own the picture set of a target value; overlapping managed mappings are rejected.

On customized 4.5, keys match `CustomValue`, falling back to Name when CustomValue is blank. On native 4.9 they match Name. Matching is case-insensitive. The sole-value option does not depend on either field.

The target product, product attribute mapping and value must exist. The feature does not create Design values or copy the source's images to all values. It supports products including grouped/associated child products. Variants represented only by combinations are explicitly unsupported by this destination; their existing combination-picture destination is unchanged.

## Synchronization and ownership

- Images are downloaded through the existing asset resolver/downloader, reused when gallery bytes match, and added to the destination product gallery when needed.
- Every image must resolve and validate before the attribute value's picture associations change. Failed or ambiguous resolution preserves existing associations and ownership. A late download failure may leave newly imported gallery pictures available for the next retry.
- Merge retains previous links. Replace managed removes stale links owned by the mapping and preserves manual links. Replace all replaces only the selected value's complete set, including manual links. Other independently configured product-gallery mappings continue to follow their own profile policy.
- Matching manual links remain manual unless adoption is enabled (or Replace all is used). Adoption does not claim unrelated pictures. Once adopted, a matching link is eligible for removal when it disappears from the PIM source.
- On 4.5 the first source image becomes the primary picture when the primary is empty or plugin-owned. A manual primary is retained unless adopted or replaced. Remaining images go into the backported additional-picture table; the primary is not duplicated there.
- On 4.9 all value-picture links use the native table and service.
- Gallery display order follows resolved asset display order. Gallery ordering is shared by values that use the same image; there is no independent per-value sort column.
- Removing a value association retains its gallery entry and binary for reuse. Product-gallery and combination cleanup protect pictures referenced by values or other products. Removing a mapping releases its owned value associations on the next applicable replacement sync.
- Picture-only changes schedule the normal product update event. Mapping selection/adoption settings participate in the sync fingerprint.

Dry runs show the bound product/value, desired image associations, stale managed-link removals, adoption policy and Replace all scope. They do not download binaries, so byte-level reuse is determined during the actual sync.

## Deployment

Versions: customized 4.5 `4.50.43`; native 4.9 `4.90.76`. Deploy the matching plugin assembly, plugin.json and Views/AssetMapping/AssetMappings.cshtml. The consolidated migration version `2026-11-23 18:00:00` adds nullable target/value references and false-by-default adoption/ownership flags. Existing mappings retain their destinations and behavior.

The customized 4.5 build requires the site's existing `IAdfProductAttributeService` and `ProductAttributeValuePicture` backport. The 4.9 build uses native services and does not require those customizations. Existing commemorative rendering reads the resulting associations without changes.

After deployment, confirm the migration has run, configure the mapping, inspect a dry run for a printed and digital child, and verify their Design carousels after syncing. No mapping, product or database on the preview website is changed by the source-code implementation itself.
