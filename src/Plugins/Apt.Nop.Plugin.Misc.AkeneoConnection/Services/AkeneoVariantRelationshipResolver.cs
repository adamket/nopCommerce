
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Nop.Core;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public class AkeneoVariantRelationshipResolver : IAkeneoVariantRelationshipResolver
{
    private readonly INopVariantStructureDetector _nopVariantStructureDetector;
    private readonly IAkeneoFamilyMappingService _familyConfigurationService;

    public AkeneoVariantRelationshipResolver(
        INopVariantStructureDetector nopVariantStructureDetector,
        IAkeneoFamilyMappingService familyConfigurationService)
    {
        _nopVariantStructureDetector = nopVariantStructureDetector;
        _familyConfigurationService = familyConfigurationService;
    }

    public async Task<AkeneoVariantRelationshipResolution> ResolveAsync(
        Product parentProduct,
        string akeneoFamilyCode,
        string akeneoFamilyVariantCode = null)
    {
        if (parentProduct == null)
            throw new ArgumentNullException(nameof(parentProduct));

        var familyOptions =
            await _familyConfigurationService.BuildOptionsForFamilyAsync(
                akeneoFamilyCode,
                akeneoFamilyVariantCode);

        var preserveExistingStructure =
            familyOptions?.PreserveExistingNopVariantStructure ?? true;

        if (preserveExistingStructure)
        {
            var detected = await _nopVariantStructureDetector.DetectAsync(parentProduct);

            if (detected.IsAmbiguous)
            {
                throw new NopException(
                    $"Unable to import Akeneo variant because nopCommerce parent product '{parentProduct.Id}' has multiple variant structures.");
            }

            if (detected.HasVariantStructure && detected.Mode.HasValue)
            {
                var options = familyOptions ?? new AkeneoVariantRelationshipOptions
                {
                    Enabled = true,
                    AkeneoFamilyCode = akeneoFamilyCode,
                    AkeneoFamilyVariantCode = akeneoFamilyVariantCode,
                    PreserveExistingNopVariantStructure = true,
                    Mode = detected.Mode.Value
                };

                if (detected.Mode == AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue &&
                    detected.AssociatedProductAttributeId.HasValue)
                {
                    options.AssociatedProductAttributeId = detected.AssociatedProductAttributeId;
                }

                return new AkeneoVariantRelationshipResolution
                {
                    Mode = detected.Mode.Value,
                    Source = AkeneoVariantRelationshipSource.ExistingNopParent,
                    Options = options
                };
            }
        }

        if (familyOptions == null)
        {
            var scope = string.IsNullOrWhiteSpace(akeneoFamilyVariantCode)
                ? $"family '{akeneoFamilyCode}'"
                : $"family '{akeneoFamilyCode}', variant '{akeneoFamilyVariantCode}'";

            throw new NopException(
                $"No enabled Akeneo family variant import configuration exists for {scope}. " +
                "Create an exact family-variant configuration or an enabled family-wide default before importing variants for this scope.");
        }

        return new AkeneoVariantRelationshipResolution
        {
            Mode = familyOptions.Mode,
            Source = AkeneoVariantRelationshipSource.AkeneoFamilyConfiguration,
            Options = familyOptions
        };
    }
}
