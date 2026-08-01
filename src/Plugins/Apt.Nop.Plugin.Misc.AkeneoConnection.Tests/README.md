# Akeneo Connection Tests

Place this project beside `Apt.Nop.Plugin.Misc.AkeneoConnection` under `src/Plugins`.

The Dry Run parity suite provides five CI safeguards:

1. Reflection verifies that every `IAkeneoProductSectionSynchronizer` owns an `IAkeneoSectionDryRunPlanProvider`.
2. Every synchronizer order must still have a matching adapter in the ordered Dry Run pipeline.
3. Core, SEO, category, specification, product-attribute, and custom-property tests verify that `SynchronizeAsync` calls the same internal `BuildPlanAsync` method used by Dry Run.
4. Label-independence tests rename the preview `Area` and `Target` values before execution and verify that the attached actions still perform the intended writes.
5. Representative scenarios run through both preview and apply, including asset resolution and destructive-cleanup safety.

Core product fields, SEO, categories, specifications, product attributes, and custom properties now execute the same action-bearing plans rendered by Dry Run. Assets intentionally share structural desired-state resolution rather than binary execution: Dry Run can describe expected media changes, while download, MIME, size, and image validation remain execution-time checks. Asset parity tests ensure both paths use the shared resolver and preserve existing media when authoritative resolution fails.

Run from the nopCommerce `src` directory:

```powershell
dotnet test .\Plugins\Apt.Nop.Plugin.Misc.AkeneoConnection.Tests\Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.csproj
```
