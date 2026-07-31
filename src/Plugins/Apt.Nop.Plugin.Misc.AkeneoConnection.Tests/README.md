# Akeneo Connection tests

This NUnit project provides a deterministic regression-test baseline for the
Akeneo-to-nopCommerce plugin. It is designed to live beside the plugin project
under `src/Plugins` in a nopCommerce 4.90 source tree.

## Initial coverage

- CSV and mapping-key normalization
- Akeneo product search JSON and updated-date filters
- Sync-scope watermark hashing
- Batch request creation for every `AkeneoUpdatedFilterMode`
- Product root/value, locale, channel, option-label, metric, and price resolution
- Value transformations
- Computed value-template validation and rendering
- Product-model and submodel hierarchy inheritance
- Existing nopCommerce variant-structure preservation
- Product sync-pipeline ordering and early termination
- Managed category safety for `Merge`, `ReplaceManaged`, and `ReplaceAll`
- Associated-product attribute-value name templates
- Option code/label pairing

## Run

From the nopCommerce `src` directory:

```powershell
dotnet test .\Plugins\Apt.Nop.Plugin.Misc.AkeneoConnection.Tests\Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.csproj
```

With coverage:

```powershell
dotnet test .\Plugins\Apt.Nop.Plugin.Misc.AkeneoConnection.Tests\Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.csproj --collect:"XPlat Code Coverage"
```

Add the project to `NopCommerce.sln` if you want it to appear in Visual Studio's
Test Explorer and participate in solution-wide test runs.

## Recommended next additions

The next layer should use nopCommerce's test database infrastructure to verify
full representation transitions and authoritative reconciliation:

- Standalone product to grouped child, associated product, and combination
- Reverse transitions back to standalone products
- Specification and product-attribute `ReplaceManaged` ownership behavior
- Asset replacement, ordering, fallback, and managed-picture cleanup
- Full-run missing-product lifecycle actions
- Delta-watermark selection against persisted run records
- Dry Run and real synchronization producing the same resolved plan

## Product-model delta fan-out

The suite also verifies the product-model delta fan-out rules: timestamped delta eligibility, direct-versus-linked-asset searches, descendant leaf scoping, and propagation of ancestor/asset inclusion reasons through submodels.
