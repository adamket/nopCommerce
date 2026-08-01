using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Classifies how an existing Akeneo leaf is currently represented in
/// nopCommerce. The import orchestrator and Dry Run share this service so a
/// standalone submodel override is evaluated consistently.
/// </summary>
public interface IAkeneoLeafRepresentationClassifier
{
    Task<AkeneoVariantRelationshipMode?> ClassifyAsync(
        Product product,
        string sku);
}
