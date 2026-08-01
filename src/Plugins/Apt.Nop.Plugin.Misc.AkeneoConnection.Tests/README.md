# Akeneo Connection Tests

Place this project beside `Apt.Nop.Plugin.Misc.AkeneoConnection` under `src/Plugins`.

The Dry Run parity suite provides six CI safeguards:

1. Reflection verifies that every `IAkeneoProductSectionSynchronizer` owns an `IAkeneoSectionDryRunPlanProvider`.
2. Every synchronizer order must still have a matching adapter in the ordered Dry Run pipeline.
3. Core, SEO, category, specification, product-attribute, and custom-property tests verify that `SynchronizeAsync` calls the same internal `BuildPlanAsync` method used by Dry Run.
4. Label-independence tests rename preview `Area` and `Target` values before execution and verify that attached actions still perform the intended writes.
5. A cross-section regression test renames Core preview labels and verifies SEO still uses the typed proposed product name when calculating a slug.
6. Representative scenarios run through both preview and apply. Asset parity compares the exact saved structural payload with the preview summary and verifies destructive cleanup is suppressed when authoritative resolution fails.

Core product fields, SEO, categories, specifications, product attributes, and custom properties execute the same action-bearing plans rendered by Dry Run. Assets intentionally share structural desired-state resolution rather than binary execution: Dry Run can describe expected media changes, while download, MIME, size, and image validation remain execution-time checks.

Run from the nopCommerce `src` directory:

```powershell
dotnet test .\Plugins\Apt.Nop.Plugin.Misc.AkeneoConnection.Tests\Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.csproj
```
