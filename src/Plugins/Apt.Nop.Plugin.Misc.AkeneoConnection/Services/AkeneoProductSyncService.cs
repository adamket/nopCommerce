using System.Text.Json;
using System.Runtime.CompilerServices;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductSyncService(
    IAkeneoProductValueResolver productValueResolver,
    IAkeneoReferenceEntityValueResolver referenceEntityValueResolver,
    IAkeneoValueTransformationService transformationService,
    IAkeneoValueTemplateRenderer valueTemplateRenderer,
    IAkeneoAttributeMappingService attributeMappingService,
    IAkeneoAssetMappingService assetMappingService,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoProductSyncStateService syncStateService,
    IProductService productService,
    IAkeneoProductSyncPipeline syncPipeline,
    IAkeneoProfileRuleEvaluator profileRuleEvaluator,
    IAkeneoApiClient akeneoApiClient)
    : IAkeneoProductSyncService
{
    // One fresh catalog per import request/run, shared by all products in it.
    // Failed loads stay failed for that run; they never become an empty catalog.
    private readonly ConditionalWeakTable<AkeneoProductImportRequest, Lazy<Task<HashSet<string>>>> _attributeCatalogs = new();

    private async Task<HashSet<string>> LoadAttributeCatalogAsync(CancellationToken cancellationToken)
    {
        var attributes = await akeneoApiClient.GetAttributesAsync(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Akeneo returned no attribute catalog response.");
        return attributes.Where(attribute => !string.IsNullOrWhiteSpace(attribute.Code))
            .Select(attribute => attribute.Code.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<AkeneoProductSyncContext> PrepareAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        CancellationToken cancellationToken = default)
    {
        var intent = await ResolveIntentAsync(
            source,
            sourceEntityType,
            request,
            cancellationToken);

        return await PrepareFromIntentAsync(intent, result, cancellationToken);
    }

    public async Task<AkeneoProductSyncContext> PrepareAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        string mappingFamilyCode,
        CancellationToken cancellationToken = default)
    {
        var intent = await ResolveIntentAsync(
            source,
            sourceEntityType,
            request,
            mappingFamilyCode,
            cancellationToken);

        return await PrepareFromIntentAsync(intent, result, cancellationToken);
    }

    public async Task<AkeneoProductSyncContext> PrepareAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        string mappingFamilyCode,
        AkeneoAttributeMappingEntityScope mappingEntityScope,
        CancellationToken cancellationToken = default)
    {
        return await PrepareAsync(
            source,
            sourceEntityType,
            request,
            result,
            mappingFamilyCode,
            ResolveImplicitFamilyVariantCode(source, sourceEntityType),
            mappingEntityScope,
            cancellationToken);
    }

    public async Task<AkeneoProductSyncContext> PrepareAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        string mappingFamilyCode,
        string mappingFamilyVariantCode,
        AkeneoAttributeMappingEntityScope mappingEntityScope,
        CancellationToken cancellationToken = default)
    {
        var intent = await ResolveIntentAsync(
            source,
            sourceEntityType,
            request,
            mappingFamilyCode,
            mappingFamilyVariantCode,
            mappingEntityScope,
            cancellationToken);

        return await PrepareFromIntentAsync(intent, result, cancellationToken);
    }

    public Task<AkeneoResolvedProductIntent> ResolveIntentAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        CancellationToken cancellationToken = default)
    {
        return ResolveIntentAsync(
            source,
            sourceEntityType,
            request,
            source?.Family,
            ResolveImplicitFamilyVariantCode(source, sourceEntityType),
            AkeneoAttributeMappingScopeHelper.ResolveDefaultCurrentScope(
                source,
                sourceEntityType),
            cancellationToken);
    }

    public Task<AkeneoResolvedProductIntent> ResolveIntentAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        string mappingFamilyCode,
        CancellationToken cancellationToken = default)
    {
        return ResolveIntentAsync(
            source,
            sourceEntityType,
            request,
            mappingFamilyCode,
            ResolveImplicitFamilyVariantCode(source, sourceEntityType),
            AkeneoAttributeMappingScopeHelper.ResolveDefaultCurrentScope(
                source,
                sourceEntityType),
            cancellationToken);
    }

    public async Task<AkeneoResolvedProductIntent> ResolveIntentAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        string mappingFamilyCode,
        AkeneoAttributeMappingEntityScope mappingEntityScope,
        CancellationToken cancellationToken = default)
    {
        return await ResolveIntentAsync(
            source,
            sourceEntityType,
            request,
            mappingFamilyCode,
            ResolveImplicitFamilyVariantCode(source, sourceEntityType),
            mappingEntityScope,
            cancellationToken);
    }

    public async Task<AkeneoResolvedProductIntent> ResolveIntentAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        AkeneoProductImportRequest request,
        string mappingFamilyCode,
        string mappingFamilyVariantCode,
        AkeneoAttributeMappingEntityScope mappingEntityScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        var resolutionResult = new AkeneoProductImportResult();
        string skipReason = null;
        string ruleError = null;
        try
        {
            skipReason = await profileRuleEvaluator.GetSkipReasonAsync(source, request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            ruleError = ex.Message;
        }
        if (skipReason != null || ruleError != null)
        {
            var code = sourceEntityType == AkeneoEntityType.ProductModel ? source.Code : source.Identifier;
            return new AkeneoResolvedProductIntent
            {
                Source = source, SourceEntityType = sourceEntityType, Request = request,
                SourceCode = code, SourceUuid = source.Uuid, ProductKey = source.Uuid ?? code,
                Sku = source.Identifier ?? source.Code, SkipReason = skipReason,
                Messages = skipReason == null ? Array.Empty<string>() : new[] { skipReason },
                Errors = ruleError == null ? Array.Empty<string>() : new[] { ruleError }
            };
        }

        if (sourceEntityType == AkeneoEntityType.Product &&
            !string.IsNullOrWhiteSpace(source.Parent) &&
            string.IsNullOrWhiteSpace(mappingFamilyVariantCode))
        {
            var productIdentity = source.Identifier?.Trim()
                ?? source.Uuid?.Trim()
                ?? "(unknown product)";
            resolutionResult.AddError(
                $"Akeneo variant product '{productIdentity}' references parent product model '{source.Parent.Trim()}', but no resolved family-variant code was supplied to synchronization. Akeneo leaf payloads do not expose family_variant; resolve it from the immediate parent product model before preparing or resolving this product.");
        }

        var sourceCode = sourceEntityType == AkeneoEntityType.ProductModel
            ? source.Code?.Trim()
            : source.Identifier?.Trim();

        var sourceUuid = sourceEntityType == AkeneoEntityType.Product
            ? source.Uuid?.Trim()
            : null;

        var productKey = sourceUuid ?? sourceCode;

        if (string.IsNullOrWhiteSpace(productKey))
        {
            resolutionResult.AddError(
                sourceEntityType == AkeneoEntityType.ProductModel
                    ? "Akeneo product model does not contain a code."
                    : "Akeneo product does not contain a UUID or identifier.");
        }

        mappingFamilyCode = !string.IsNullOrWhiteSpace(mappingFamilyCode)
            ? mappingFamilyCode.Trim()
            : source.Family?.Trim();

        mappingFamilyVariantCode = !string.IsNullOrWhiteSpace(mappingFamilyVariantCode)
            ? mappingFamilyVariantCode.Trim()
            : null;

        mappingEntityScope =
            AkeneoAttributeMappingScopeHelper.NormalizeCurrentScope(
                mappingEntityScope,
                source,
                sourceEntityType);

        var effectiveMappings = await attributeMappingService
            .GetEffectiveMappingsAsync(mappingFamilyCode);

        var applicableMappings = effectiveMappings
            .Where(mapping =>
                AkeneoAttributeMappingScopeHelper.AppliesTo(
                    mapping,
                    mappingEntityScope))
            .ToList();

        var handledAssetAttributeCodes = (await assetMappingService
                .GetEffectiveMappingsAsync(mappingFamilyCode))
            .Where(mapping => mapping.Enabled)
            .SelectMany(mapping => new[]
            {
                mapping.SourceAttributeCode,
                mapping.FallbackSourceAttributeCode
            })
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fallbackSourcesByMappingId =
            (await attributeMappingService.GetAllFallbackSourcesAsync())
            .Where(fallback => effectiveMappings.Any(mapping =>
                mapping.Id == fallback.AttributeMappingId))
            .GroupBy(fallback => fallback.AttributeMappingId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AkeneoAttributeMappingFallbackSource>)group
                    .OrderBy(fallback => fallback.DisplayOrder)
                    .ThenBy(fallback => fallback.Id)
                    .ToList());

        var mappedValues = await ResolveMappedValuesAsync(
            source,
            applicableMappings,
            fallbackSourcesByMappingId,
            mappingFamilyCode,
            mappingFamilyVariantCode,
            request,
            resolutionResult,
            cancellationToken);

        handledAssetAttributeCodes.AddRange(AkeneoProfileRules.ParseSkipRules(request.SkipRulesJson)
            .Select(rule => rule.AttributeCode));

        // A configured mapping that does not apply to this destination role is
        // still considered handled. It must not be logged or imported as an
        // "unmapped" custom property on the wrong product role.
        AppendUnmappedAttributeValues(
            source,
            effectiveMappings,
            fallbackSourcesByMappingId,
            handledAssetAttributeCodes,
            request,
            resolutionResult,
            mappedValues);

        var sku = ResolveSku(
            source,
            sourceEntityType,
            mappedValues);

        return new AkeneoResolvedProductIntent
        {
            Source = source,
            SourceEntityType = sourceEntityType,
            Request = request,
            MappedValues = mappedValues.ToList(),
            SourceCode = sourceCode,
            SourceUuid = sourceUuid,
            MappingFamilyCode = mappingFamilyCode,
            MappingFamilyVariantCode = mappingFamilyVariantCode,
            MappingEntityScope = mappingEntityScope,
            ProductKey = productKey,
            Sku = sku,
            Messages = resolutionResult.Messages.ToList(),
            Warnings = resolutionResult.Warnings.ToList(),
            Errors = resolutionResult.Errors.ToList()
        };
    }

    public async Task<AkeneoProductSyncContext> PrepareFromIntentAsync(
        AkeneoResolvedProductIntent intent,
        AkeneoProductImportResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(result);

        cancellationToken.ThrowIfCancellationRequested();

        ApplyIntentToResult(intent, result);

        Product existingProduct = null;
        AkeneoProductSyncState existingState = null;

        if (result.Success)
        {
            (existingProduct, existingState) = await ResolveNopProductAsync(
                intent.SourceEntityType,
                intent.SourceCode,
                intent.SourceUuid,
                intent.Sku,
                !string.IsNullOrWhiteSpace(intent.Source.Parent),
                intent.Request,
                result);
        }

        return new AkeneoProductSyncContext
        {
            SkipReason = intent.SkipReason,
            StopProcessing = intent.SkipReason != null,
            Source = intent.Source,
            SourceEntityType = intent.SourceEntityType,
            Request = intent.Request,
            Result = result,
            MappedValues = intent.MappedValues,
            SourceCode = intent.SourceCode,
            SourceUuid = intent.SourceUuid,
            MappingFamilyCode = intent.MappingFamilyCode,
            MappingFamilyVariantCode = intent.MappingFamilyVariantCode,
            MappingEntityScope = intent.MappingEntityScope,
            ProductKey = intent.ProductKey,
            Sku = intent.Sku,
            ExistingProduct = existingProduct,
            ExistingSyncState = existingState
        };
    }

    private static void ApplyIntentToResult(
        AkeneoResolvedProductIntent intent,
        AkeneoProductImportResult result)
    {
        result.AkeneoProductUuid = intent.SourceUuid;
        result.AkeneoIdentifier = intent.SourceCode;
        result.AkeneoProductKey = intent.ProductKey;
        result.Sku = intent.Sku;

        foreach (var message in intent.Messages)
        {
            if (!result.Messages.Contains(message, StringComparer.Ordinal))
                result.AddMessage(message);
        }

        foreach (var warning in intent.Warnings)
        {
            if (!result.Warnings.Contains(warning, StringComparer.Ordinal))
                result.AddWarning(warning);
        }

        foreach (var error in intent.Errors)
        {
            if (!result.Errors.Contains(error, StringComparer.Ordinal))
                result.AddError(error);
        }
    }

    public async Task<Product> SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SkipReason != null)
        {
            context.Result.ActionType = SyncItemActionType.Skipped;
            return null;
        }

        if (!context.Result.Success)
        {
            context.Result.ActionType = SyncItemActionType.Failed;
            return null;
        }

        await syncPipeline.SynchronizeAsync(
            context,
            cancellationToken);

        if (!context.Result.Success)
        {
            context.Result.ActionType = SyncItemActionType.Failed;
            return null;
        }

        if (context.Product != null)
        {
            await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                context.SourceEntityType,
                context.SourceCode,
                context.SourceUuid,
                NopEntityType.Product,
                context.Product.Id);

            context.Result.NopProductId = context.Product.Id;
            context.Result.DestinationKind = context.Product.ParentGroupedProductId > 0
                ? AkeneoProductDestinationKind.GroupedChildProduct
                : AkeneoProductDestinationKind.NopProduct;
        }

        if (context.ProductCreated)
        {
            context.Result.ActionType = SyncItemActionType.Created;
        }
        else if (context.HasChanges)
        {
            context.Result.ActionType = SyncItemActionType.Updated;
        }
        else if (context.Result.ActionType != SyncItemActionType.Failed)
        {
            context.Result.ActionType = SyncItemActionType.Skipped;

            if (!context.Result.Messages.Any())
            {
                context.Result.AddMessage(
                    "Product already matches the Akeneo desired state.");
            }
        }

        return context.Product;
    }

    public Task RunPrepareAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
        => context.SkipReason != null || !context.Result.Success
            ? Task.CompletedTask
            : syncPipeline.PrepareAsync(context, cancellationToken);

    private async Task<IList<AkeneoResolvedMappedValue>> ResolveMappedValuesAsync(
        AkeneoProductDefinition source,
        IList<AkeneoAttributeMapping> mappings,
        IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId,
        string mappingFamilyCode,
        string mappingFamilyVariantCode,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        CancellationToken cancellationToken)
    {
        var resolved = new List<AkeneoResolvedMappedValue>();
        var dependencies = mappings.Where(mapping => (NopTargetType)mapping.NopTargetTypeId != NopTargetType.Ignore)
            .ToDictionary(mapping => mapping, mapping => AkeneoMappingSourceCatalog.GetAttributeCodes(mapping,
                fallbackSourcesByMappingId.GetValueOrDefault(mapping.Id), valueTemplateRenderer));
        var unavailable = new HashSet<AkeneoAttributeMapping>();
        if (dependencies.Values.Any(codes => codes.Count > 0))
        {
            HashSet<string> catalog;
            try
            {
                catalog = await _attributeCatalogs.GetValue(request, _ =>
                    new Lazy<Task<HashSet<string>>>(() => LoadAttributeCatalogAsync(cancellationToken))).Value;
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.AddError($"Cannot validate mapping sources against Akeneo's attribute catalog. No mappings were applied: {ex.Message}");
                return mappings.Select(mapping => new AkeneoResolvedMappedValue
                {
                    Mapping = mapping, EvaluationOutcome = AkeneoMappingEvaluationOutcome.Failed
                }).ToList();
            }

            foreach (var (mapping, codes) in dependencies)
            {
                var missing = codes.Where(code => !catalog.Contains(code)).ToList();
                if (missing.Count == 0) continue;
                unavailable.Add(mapping);
                var detail = $"Mapping '{mapping.Name ?? mapping.MappingKey ?? mapping.AkeneoAttributeCode}' was skipped because these Akeneo attributes no longer exist: {string.Join(", ", missing)}. The destination is preserved. Update or remove the saved mapping.";
                result.AddWarning(detail);
                resolved.Add(new AkeneoResolvedMappedValue
                {
                    Mapping = mapping, EvaluationOutcome = AkeneoMappingEvaluationOutcome.SkippedMissingAttribute,
                    ConditionDetail = detail
                });
            }
        }
        mappings = mappings.Where(mapping => !unavailable.Contains(mapping)).ToList();

        // Ordinary mappings are resolved first so templates can use the
        // effective SKU produced by a normal ProductField/Sku mapping.
        foreach (var mapping in mappings.Where(mapping =>
                     mapping.ValueModeId !=
                     (int)AkeneoAttributeMappingValueMode.Template))
        {
            resolved.Add(await ResolveSingleAttributeMappingAsync(
                source,
                mapping,
                fallbackSourcesByMappingId,
                mappingFamilyVariantCode,
                request,
                result,
                cancellationToken));
        }

        var effectiveSku = resolved
            .FirstOrDefault(mapped =>
                mapped.HasValue &&
                mapped.TargetType == NopTargetType.ProductField &&
                string.Equals(
                    mapped.Mapping.NopTargetKey,
                    "Sku",
                    StringComparison.OrdinalIgnoreCase))
            ?.DisplayValue;

        effectiveSku ??= source.Identifier ?? source.Code;

        foreach (var mapping in mappings.Where(mapping =>
                     mapping.ValueModeId ==
                     (int)AkeneoAttributeMappingValueMode.Template))
        {
            cancellationToken.ThrowIfCancellationRequested();

            resolved.Add(await ResolveTemplateMappingAsync(
                source,
                mapping,
                effectiveSku,
                mappingFamilyCode,
                mappingFamilyVariantCode,
                request,
                result,
                cancellationToken));
        }

        return resolved;
    }

    private async Task<AkeneoResolvedMappedValue>
        ResolveSingleAttributeMappingAsync(
            AkeneoProductDefinition source,
            AkeneoAttributeMapping mapping,
            IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId,
            string mappingFamilyVariantCode,
            AkeneoProductImportRequest request,
            AkeneoProductImportResult result,
            CancellationToken cancellationToken)
    {
        var targetType = (NopTargetType)mapping.NopTargetTypeId;

        if (targetType == NopTargetType.Ignore)
        {
            return new AkeneoResolvedMappedValue
            {
                Mapping = mapping,
                HasValue = false
            };
        }

        var locale = !string.IsNullOrWhiteSpace(mapping.Locale)
            ? mapping.Locale
            : request.Locale;
        var channel = !string.IsNullOrWhiteSpace(mapping.Channel)
            ? mapping.Channel
            : request.Channel;

        if (targetType == NopTargetType.CustomHandler)
        {
            // Preserve raw empty arrays and malformed values for the handler to validate.
            // Display-value resolution would collapse an explicit empty table into "missing".
            if (fallbackSourcesByMappingId.TryGetValue(mapping.Id, out var customFallbacks) && customFallbacks.Count > 0)
                result.AddError("Custom mapping handlers do not support fallback sources.");
            var rawValue = await ResolveSourceValueAsync(source, mapping, mappingFamilyVariantCode,
                locale, channel, request.Currency, cancellationToken);
            return new AkeneoResolvedMappedValue
            {
                Mapping = mapping, Value = rawValue,
                HasValue = rawValue?.RawData is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined },
                ResolvedSourceDisplayName = AkeneoMappingHelper.GetSourceDisplayName(mapping)
            };
        }

        AkeneoResolvedProductValue value = null;
        var resolvedSuccessfully = false;
        var resolvedSourceDisplayName =
            AkeneoMappingHelper.GetSourceDisplayName(mapping);

        foreach (var sourceMapping in BuildOrderedSourceMappings(
                     mapping,
                     fallbackSourcesByMappingId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidateValue = await ResolveSourceValueAsync(
                source,
                sourceMapping,
                mappingFamilyVariantCode,
                locale,
                channel,
                request.Currency,
                cancellationToken);

            if (!HasDisplayValue(candidateValue))
                continue;

            value = candidateValue;
            resolvedSuccessfully = true;
            resolvedSourceDisplayName =
                AkeneoMappingHelper.GetSourceDisplayName(sourceMapping);
            break;
        }

        (resolvedSuccessfully, value) = ApplyTransform(
            mapping,
            value,
            resolvedSuccessfully,
            resolvedSourceDisplayName,
            result);

        var hasValue = resolvedSuccessfully && HasDisplayValue(value);

        if (!hasValue && mapping.IsRequired)
        {
            result.AddError(
                $"Required Akeneo source is missing a value. Tried: {GetSourceChainDisplayName(mapping, fallbackSourcesByMappingId)}");
        }

        return new AkeneoResolvedMappedValue
        {
            Mapping = mapping,
            HasValue = hasValue,
            Value = value,
            ResolvedSourceDisplayName = hasValue
                ? resolvedSourceDisplayName
                : null
        };
    }

    private static string ResolveImplicitFamilyVariantCode(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType)
    {
        if (source == null)
            return null;

        // Only product models actually expose family_variant in Akeneo payloads.
        // A leaf with a parent must use the explicit overload after its hierarchy
        // has been resolved; returning null here intentionally activates the
        // guard in ResolveIntentAsync instead of reviving the old silent fallback.
        if (sourceEntityType == AkeneoEntityType.Product &&
            !string.IsNullOrWhiteSpace(source.Parent))
        {
            return null;
        }

        return source.FamilyVariant?.Trim();
    }

    private async Task<AkeneoResolvedMappedValue> ResolveTemplateMappingAsync(
        AkeneoProductDefinition source,
        AkeneoAttributeMapping mapping,
        string effectiveSku,
        string mappingFamilyCode,
        string mappingFamilyVariantCode,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        CancellationToken cancellationToken)
    {
        var sourceDisplayName =
            $"Template '{mapping.Name ?? mapping.MappingKey}'";

        if ((NopTargetType)mapping.NopTargetTypeId ==
            NopTargetType.Ignore)
        {
            return new AkeneoResolvedMappedValue
            {
                Mapping = mapping,
                HasValue = false,
                ResolvedSourceDisplayName = sourceDisplayName
            };
        }

        var locale = !string.IsNullOrWhiteSpace(mapping.Locale)
            ? mapping.Locale
            : request.Locale;
        var channel = !string.IsNullOrWhiteSpace(mapping.Channel)
            ? mapping.Channel
            : request.Channel;

        var templateContext = new AkeneoValueTemplateContext
            {
                Source = source,
                Locale = locale,
                Channel = channel,
                Currency = request.Currency,
                FamilyCode = mappingFamilyCode,
                FamilyVariantCode = mappingFamilyVariantCode,
                Sku = effectiveSku
            };

        if (!string.IsNullOrWhiteSpace(mapping.ApplyWhen))
        {
            var condition = await AkeneoMappingCondition.EvaluateAsync(mapping.ApplyWhen,
                valueTemplateRenderer, templateContext, cancellationToken);
            if (!condition.Success || !condition.Matches)
            {
                var detail = condition.Success
                    ? $"Skipped by condition: '{mapping.ApplyWhen}' evaluated to false. The destination is preserved."
                    : $"Could not evaluate condition '{mapping.ApplyWhen}' for {sourceDisplayName}: {condition.Error}";
                if (condition.Success) result.AddMessage($"{sourceDisplayName}: {detail}");
                else result.AddError(detail);
                return new AkeneoResolvedMappedValue
                {
                    Mapping = mapping, HasValue = false, ResolvedSourceDisplayName = sourceDisplayName,
                    EvaluationOutcome = condition.Success ? AkeneoMappingEvaluationOutcome.SkippedByCondition : AkeneoMappingEvaluationOutcome.Failed,
                    ConditionDetail = detail
                };
            }
        }

        // Render only after the condition permits this mapping.
        var rendered = await valueTemplateRenderer.RenderAsync(mapping.ValueTemplate, templateContext, cancellationToken);

        var resolvedSuccessfully = rendered.Success &&
            !string.IsNullOrWhiteSpace(rendered.Value);
        AkeneoResolvedProductValue value = resolvedSuccessfully
            ? new AkeneoResolvedProductValue
            {
                AttributeCode = mapping.MappingKey,
                Locale = locale,
                Channel = channel,
                Currency = request.Currency,
                SourceAttributeType = "computed_template",
                DisplayValue = rendered.Value,
                DisplayValues = new[] { rendered.Value }
            }
            : null;

        if (rendered.Errors.Count > 0)
        {
            var message =
                $"Could not render {sourceDisplayName}: {string.Join(" ", rendered.Errors)}";

            if (mapping.IsRequired)
                result.AddError(message);
            else
                result.AddWarning(message);
        }
        else if (rendered.MissingTokens.Count > 0)
        {
            var message =
                $"Could not render {sourceDisplayName} because these tokens had no value: {string.Join(", ", rendered.MissingTokens.Select(token => $"{{{token}}}"))}.";

            if (mapping.IsRequired)
                result.AddError(message);
            else
                result.AddWarning(message);
        }
        else if (!resolvedSuccessfully && mapping.IsRequired)
        {
            result.AddError(
                $"Required {sourceDisplayName} rendered an empty value.");
        }

        (resolvedSuccessfully, value) = ApplyTransform(
            mapping,
            value,
            resolvedSuccessfully,
            sourceDisplayName,
            result);

        var hasValue = resolvedSuccessfully && HasDisplayValue(value);

        return new AkeneoResolvedMappedValue
        {
            Mapping = mapping,
            HasValue = hasValue,
            Value = value,
            ResolvedSourceDisplayName = hasValue
                ? sourceDisplayName
                : null
        };
    }

    private (bool Success, AkeneoResolvedProductValue Value) ApplyTransform(
        AkeneoAttributeMapping mapping,
        AkeneoResolvedProductValue value,
        bool resolvedSuccessfully,
        string sourceDisplayName,
        AkeneoProductImportResult result)
    {
        if (!resolvedSuccessfully ||
            value == null ||
            string.IsNullOrWhiteSpace(mapping.TransformRuleJson))
        {
            return (resolvedSuccessfully, value);
        }

        var transformed = transformationService.Transform(value, mapping);

        if (transformed.Success)
            return (true, transformed.Value);

        var message =
            $"Transform failed for Akeneo source '{sourceDisplayName}': {transformed.Error}";

        if (mapping.IsRequired)
            result.AddError(message);
        else
            result.AddWarning(message);

        return (false, null);
    }

    private async Task<AkeneoResolvedProductValue> ResolveSourceValueAsync(
        AkeneoProductDefinition source,
        AkeneoAttributeMapping sourceMapping,
        string mappingFamilyVariantCode,
        string locale,
        string channel,
        string currency,
        CancellationToken cancellationToken)
    {
        if (IsFamilyVariantRootField(sourceMapping.AkeneoAttributeCode) &&
            !string.IsNullOrWhiteSpace(mappingFamilyVariantCode))
        {
            var normalizedVariantCode = mappingFamilyVariantCode.Trim();
            return new AkeneoResolvedProductValue
            {
                AttributeCode = sourceMapping.AkeneoAttributeCode,
                Locale = locale,
                Channel = channel,
                Currency = currency,
                SourceAttributeType = "root_field",
                RawData = JsonSerializer.SerializeToElement(normalizedVariantCode),
                DisplayValue = normalizedVariantCode,
                DisplayValues = new[] { normalizedVariantCode }
            };
        }

        if (AkeneoMappingHelper.IsReferenceEntityMapping(sourceMapping) &&
            !string.IsNullOrWhiteSpace(
                sourceMapping.AkeneoReferenceEntityAttributeCode))
        {
            return await referenceEntityValueResolver.ResolveAsync(
                source,
                sourceMapping,
                locale,
                channel,
                currency,
                cancellationToken);
        }

        return productValueResolver.TryGetValue(
            source,
            sourceMapping.AkeneoAttributeCode,
            out var value,
            locale,
            channel,
            currency)
                ? value
                : null;
    }

    private static bool IsFamilyVariantRootField(string attributeCode) =>
        string.Equals(
            attributeCode,
            "family_variant",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            attributeCode,
            "familyVariant",
            StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<AkeneoAttributeMapping>
        BuildOrderedSourceMappings(
            AkeneoAttributeMapping primaryMapping,
            IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId)
    {
        yield return primaryMapping;

        if (!fallbackSourcesByMappingId.TryGetValue(
                primaryMapping.Id,
                out var fallbackSources))
        {
            yield break;
        }

        foreach (var fallback in fallbackSources
                     .OrderBy(source => source.DisplayOrder)
                     .ThenBy(source => source.Id))
        {
            yield return new AkeneoAttributeMapping
            {
                AkeneoAttributeCode = fallback.AkeneoAttributeCode,
                AkeneoAttributeTypeId = fallback.AkeneoAttributeTypeId,
                AkeneoReferenceEntityCode =
                    fallback.AkeneoReferenceEntityCode,
                AkeneoReferenceEntityAttributeCode =
                    fallback.AkeneoReferenceEntityAttributeCode
            };
        }
    }

    private static string GetSourceChainDisplayName(
        AkeneoAttributeMapping primaryMapping,
        IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId)
    {
        return string.Join(
            " → ",
            BuildOrderedSourceMappings(
                    primaryMapping,
                    fallbackSourcesByMappingId)
                .Select(AkeneoMappingHelper.GetSourceDisplayName));
    }

    private static bool HasDisplayValue(
        AkeneoResolvedProductValue value)
    {
        return !string.IsNullOrWhiteSpace(value?.DisplayValue) ||
               value?.DisplayValues is { Count: > 0 };
    }

    private void AppendUnmappedAttributeValues(
        AkeneoProductDefinition source,
        IList<AkeneoAttributeMapping> effectiveMappings,
        IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId,
        IReadOnlyCollection<string> handledAssetAttributeCodes,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        IList<AkeneoResolvedMappedValue> mappedValues)
    {
        if (request is not AkeneoProductBatchImportRequest batchRequest ||
            batchRequest.UnmappedAttributeBehavior == UnmappedAkeneoAttributeBehavior.Ignore ||
            source.Values.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return;
        }

        var templateAttributeCodes = effectiveMappings
            .Where(mapping => mapping.ValueModeId ==
                (int)AkeneoAttributeMappingValueMode.Template)
            .SelectMany(mapping => valueTemplateRenderer
                .Validate(mapping.ValueTemplate)
                .ReferencedAttributeCodes.Concat(AkeneoMappingCondition.Validate(mapping.ApplyWhen, valueTemplateRenderer).ReferencedAttributeCodes));

        var handledCodes = effectiveMappings
            .Where(mapping => mapping.ValueModeId !=
                (int)AkeneoAttributeMappingValueMode.Template)
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.AkeneoAttributeCode))
            .Select(mapping => mapping.AkeneoAttributeCode.Trim())
            .Concat(
                fallbackSourcesByMappingId.Values
                    .SelectMany(sources => sources)
                    .Where(source => !string.IsNullOrWhiteSpace(
                        source.AkeneoAttributeCode))
                    .Select(source => source.AkeneoAttributeCode.Trim()))
            .Concat(templateAttributeCodes)
            .Concat(handledAssetAttributeCodes ?? Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unmappedCodes = source.Values
            .EnumerateObject()
            .Select(property => property.Name)
            .Where(code => !handledCodes.Contains(code))
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unmappedCodes.Count == 0)
            return;

        if (batchRequest.UnmappedAttributeBehavior == UnmappedAkeneoAttributeBehavior.Log)
        {
            result.AddWarning(
                "Unmapped Akeneo attributes: " + string.Join(", ", unmappedCodes.Take(25)) +
                (unmappedCodes.Count > 25 ? $" (+{unmappedCodes.Count - 25} more)" : string.Empty));
            return;
        }

        if (batchRequest.UnmappedAttributeBehavior !=
            UnmappedAkeneoAttributeBehavior.ImportAsCustomProperty)
        {
            return;
        }

        // Import as custom property: each unmapped value becomes a custom property.

        foreach (var code in unmappedCodes)
        {
            if (!productValueResolver.TryGetValue(
                    source,
                    code,
                    out var value,
                    request.Locale,
                    request.Channel,
                    request.Currency))
            {
                continue;
            }

            var hasValue =
                !string.IsNullOrWhiteSpace(value?.DisplayValue) ||
                value?.DisplayValues is { Count: > 0 };

            if (!hasValue)
                continue;

            mappedValues.Add(new AkeneoResolvedMappedValue
            {
                Mapping = new AkeneoAttributeMapping
                {
                    AkeneoFamilyCode = source.Family,
                    AkeneoAttributeCode = code,
                    NopTargetTypeId = (int)NopTargetType.CustomProperty,
                    NopTargetKey = $"Apt.Akeneo.CustomProperty.{code}"
                },
                HasValue = true,
                Value = value
            });
        }
    }

    private static string ResolveSku(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType,
        IEnumerable<AkeneoResolvedMappedValue> mappedValues)
    {
        var mappedSku = mappedValues
            .FirstOrDefault(value =>
                value.HasValue &&
                value.TargetType == NopTargetType.ProductField &&
                string.Equals(
                    value.Mapping.NopTargetKey,
                    "Sku",
                    StringComparison.OrdinalIgnoreCase))
            ?.DisplayValue;

        if (!string.IsNullOrWhiteSpace(mappedSku))
            return mappedSku.Trim();

        return sourceEntityType == AkeneoEntityType.ProductModel
            ? source.Code?.Trim()
            : source.Identifier?.Trim();
    }

    private async Task<(Product Product, AkeneoProductSyncState State)> ResolveNopProductAsync(
        AkeneoEntityType sourceEntityType,
        string sourceCode,
        string sourceUuid,
        string sku,
        bool isVariantLeaf,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result)
    {
        AkeneoProductSyncState state = null;

        if (sourceEntityType == AkeneoEntityType.Product &&
            request.SyncProfileId.HasValue)
        {
            state = await syncStateService.GetBySourceAsync(
                request.SyncProfileId.Value,
                sourceEntityType,
                sourceCode,
                sourceUuid);

            if (state != null)
            {
                var destinationKind =
                    (AkeneoProductDestinationKind)state.DestinationKindId;

                // A combination is not a child nop product. Do not return its
                // parent as the leaf product, but continue through legacy mapping
                // and SKU fallback so a previously hidden child can be restored if
                // the representation changes back to grouped/associated/standalone.
                if (destinationKind != AkeneoProductDestinationKind.ProductAttributeCombination)
                {
                    var stateProduct = await GetMappedProductOrWarnAsync(
                        state.NopProductId,
                        $"profile state for Akeneo product {sourceUuid ?? sourceCode}",
                        result);

                    if (stateProduct != null)
                        return (stateProduct, state);
                }
            }
        }

        if (sourceEntityType == AkeneoEntityType.Product &&
            !string.IsNullOrWhiteSpace(sourceUuid))
        {
            var mappedId = await entityMappingService
                .GetMappedNopEntityIdByAkeneoUuidAsync(
                    sourceEntityType,
                    sourceUuid,
                    NopEntityType.Product);

            var product = await GetMappedProductOrWarnAsync(
                mappedId,
                $"Akeneo UUID {sourceUuid}",
                result);

            if (product != null)
            {
                if (isVariantLeaf &&
                    !string.IsNullOrWhiteSpace(sku) &&
                    !string.Equals(product.Sku, sku, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddWarning(
                        $"Ignored legacy UUID mapping to product ID {product.Id} because it does not match variant SKU '{sku}'. The profile sync-state binding will replace it.");
                }
                else
                {
                    return (product, state);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(sourceCode))
        {
            var mappedId = await entityMappingService
                .GetMappedNopEntityIdByAkeneoCodeAsync(
                    sourceEntityType,
                    sourceCode,
                    NopEntityType.Product);

            var product = await GetMappedProductOrWarnAsync(
                mappedId,
                $"Akeneo {sourceEntityType} code {sourceCode}",
                result);

            if (product != null)
            {
                if (isVariantLeaf &&
                    !string.IsNullOrWhiteSpace(sku) &&
                    !string.Equals(product.Sku, sku, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddWarning(
                        $"Ignored legacy code mapping to product ID {product.Id} because it does not match variant SKU '{sku}'.");
                }
                else
                {
                    return (product, state);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(sku))
        {
            var product = await productService.GetProductBySkuAsync(sku);

            if (product != null)
            {
                result.AddMessage(
                    $"Matched existing nopCommerce product by SKU '{sku}'.");
            }

            return (product, state);
        }

        return (null, state);
    }

    private async Task<Product> GetMappedProductOrWarnAsync(
        int? mappedProductId,
        string mappingDescription,
        AkeneoProductImportResult result)
    {
        if (!mappedProductId.HasValue || mappedProductId.Value <= 0)
            return null;

        var product = await productService.GetProductByIdAsync(mappedProductId.Value);

        // A soft-deleted product is never a sync destination: treat it as
        // missing (the SKU fallback also excludes deleted products), so it is
        // neither updated nor restored.
        if (product is { Deleted: true })
        {
            result.AddMessage(
                $"Ignored nopCommerce product ID {product.Id} for {mappingDescription} because it is deleted.");
            return null;
        }

        if (product != null)
            return product;

        result.AddWarning(
            $"A mapping exists for {mappingDescription}, but " +
            $"nopCommerce product ID {mappedProductId.Value} was not found.");

        return null;
    }
}
