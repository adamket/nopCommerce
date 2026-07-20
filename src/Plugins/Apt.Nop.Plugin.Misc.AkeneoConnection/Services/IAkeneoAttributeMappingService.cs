using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoAttributeMappingService
{
    Task InsertAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
    Task UpdateAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
    Task DeleteAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);

    Task<IList<AkeneoAttributeMapping>> GetAllAkeneoAttributeMappingsAsync();

    Task<AkeneoAttributeMapping> GetAkeneoAttributeMappingByIdAsync(int id);

    /// <summary>
    /// Returns the first mapping for a normal Akeneo attribute. Reference-entity
    /// attributes can have several mappings, so new code should use
    /// <see cref="GetAkeneoAttributeMappingsByCodeAsync"/> when it needs every row.
    /// </summary>
    Task<AkeneoAttributeMapping> GetAkeneoAttributeMappingByCodeAsync(
        string code,
        string familyCode = null);

    /// <summary>
    /// Returns every persisted mapping row for one Akeneo attribute in the
    /// requested scope. Reference-entity attributes use one row per selected
    /// reference-entity field.
    /// </summary>
    Task<IList<AkeneoAttributeMapping>> GetAkeneoAttributeMappingsByCodeAsync(
        string code,
        string familyCode = null);

    /// <summary>
    /// Returns the mappings that apply to a family after merging global defaults
    /// with family overrides. Reference-entity mappings are merged by selected
    /// reference-entity field, so one family can override one field without
    /// replacing the other field mappings for the same Akeneo attribute.
    /// </summary>
    Task<IList<AkeneoAttributeMapping>> GetEffectiveMappingsAsync(string familyCode);
}
