using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

internal sealed class AkeneoVariantBranchReader(IAkeneoApiClient apiClient)
{
    public async Task<IReadOnlyList<AkeneoProductDefinition>> ReadAsync(
        string rootCode,
        CancellationToken cancellationToken,
        Func<Task> renewLease
    )
    {
        var products = new Dictionary<string, AkeneoProductDefinition>(
            StringComparer.OrdinalIgnoreCase
        );
        var modelCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootCode };
        var pendingModels = new Queue<string>();
        pendingModels.Enqueue(rootCode);
        while (pendingModels.TryDequeue(out var modelCode))
        {
            var search = JsonSerializer.Serialize(
                new { parent = new[] { new { @operator = "IN", value = new[] { modelCode } } } }
            );
            foreach (
                var model in await ReadAllPagesAsync(search, true, cancellationToken, renewLease)
            )
            {
                if (
                    string.IsNullOrWhiteSpace(model.Code)
                    || !modelCodes.Add(model.Code)
                    || !string.Equals(model.Parent, modelCode, StringComparison.OrdinalIgnoreCase)
                )
                    throw new InvalidOperationException(
                        "Variant inventory received an invalid or repeated product-model branch."
                    );
                pendingModels.Enqueue(model.Code);
            }
            foreach (
                var source in await ReadAllPagesAsync(search, false, cancellationToken, renewLease)
            )
            {
                if (!string.Equals(source.Parent, modelCode, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Variant inventory received a product outside the requested branch."
                    );
                var key =
                    SourceKey(source)
                    ?? throw new InvalidOperationException(
                        "Variant inventory received a product without a source identity."
                    );
                products[key] = source;
            }
        }
        return products.Values.ToList();
    }

    private async Task<List<AkeneoProductDefinition>> ReadAllPagesAsync(
        string search,
        bool models,
        CancellationToken cancellationToken,
        Func<Task> renewLease
    )
    {
        var products = new List<AkeneoProductDefinition>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (renewLease != null)
                await renewLease();
            AkeneoProductPageResult page = models
                ? await apiClient.GetProductModelsPageAsync(100, cursor, search, cancellationToken)
                : await apiClient.GetProductsPageAsync(100, cursor, search, cancellationToken);
            if (page?.Items == null || (page.Items.Count == 0 && page.HasNextPage))
                throw new InvalidOperationException(
                    "Variant inventory could not read a complete Akeneo inventory."
                );
            products.AddRange(page.Items);
            cursor = page.SearchAfter;
            if (!string.IsNullOrWhiteSpace(cursor) && !cursors.Add(cursor))
                throw new InvalidOperationException(
                    "Variant inventory received a repeated paging cursor."
                );
        } while (!string.IsNullOrWhiteSpace(cursor));
        return products;
    }

    internal static string SourceKey(AkeneoProductDefinition source) =>
        !string.IsNullOrWhiteSpace(source?.Uuid) ? "uuid:" + source.Uuid.Trim()
        : !string.IsNullOrWhiteSpace(source?.Identifier) ? "identifier:" + source.Identifier.Trim()
        : null;
}
