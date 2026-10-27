using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Core.Events;
using Nop.Services.Events;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.EventConsumers;

/// <summary>
/// Notes saves of the mappings that shape product output. Category mappings
/// count; product, model and option bindings are written by syncs and do not.
/// </summary>
public class AkeneoMappingChangeConsumer(IAkeneoMappingChangeTracker tracker) :
    IConsumer<EntityInsertedEvent<AkeneoAttributeMapping>>,
    IConsumer<EntityUpdatedEvent<AkeneoAttributeMapping>>,
    IConsumer<EntityDeletedEvent<AkeneoAttributeMapping>>,
    IConsumer<EntityInsertedEvent<AkeneoAttributeMappingFallbackSource>>,
    IConsumer<EntityUpdatedEvent<AkeneoAttributeMappingFallbackSource>>,
    IConsumer<EntityDeletedEvent<AkeneoAttributeMappingFallbackSource>>,
    IConsumer<EntityInsertedEvent<AkeneoAssetMapping>>,
    IConsumer<EntityUpdatedEvent<AkeneoAssetMapping>>,
    IConsumer<EntityDeletedEvent<AkeneoAssetMapping>>,
    IConsumer<EntityInsertedEvent<AkeneoFamilyMapping>>,
    IConsumer<EntityUpdatedEvent<AkeneoFamilyMapping>>,
    IConsumer<EntityDeletedEvent<AkeneoFamilyMapping>>,
    IConsumer<EntityInsertedEvent<AkeneoFamilySubModelRule>>,
    IConsumer<EntityUpdatedEvent<AkeneoFamilySubModelRule>>,
    IConsumer<EntityDeletedEvent<AkeneoFamilySubModelRule>>,
    IConsumer<EntityInsertedEvent<AkeneoFamilyVariantAxisMapping>>,
    IConsumer<EntityUpdatedEvent<AkeneoFamilyVariantAxisMapping>>,
    IConsumer<EntityDeletedEvent<AkeneoFamilyVariantAxisMapping>>,
    IConsumer<EntityInsertedEvent<AkeneoNopEntityMapping>>,
    IConsumer<EntityUpdatedEvent<AkeneoNopEntityMapping>>,
    IConsumer<EntityDeletedEvent<AkeneoNopEntityMapping>>
{
    public Task HandleEventAsync(EntityInsertedEvent<AkeneoAttributeMapping> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityUpdatedEvent<AkeneoAttributeMapping> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityDeletedEvent<AkeneoAttributeMapping> eventMessage) => tracker.MarkChangedAsync();

    public Task HandleEventAsync(EntityInsertedEvent<AkeneoAttributeMappingFallbackSource> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityUpdatedEvent<AkeneoAttributeMappingFallbackSource> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityDeletedEvent<AkeneoAttributeMappingFallbackSource> eventMessage) => tracker.MarkChangedAsync();

    public Task HandleEventAsync(EntityInsertedEvent<AkeneoAssetMapping> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityUpdatedEvent<AkeneoAssetMapping> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityDeletedEvent<AkeneoAssetMapping> eventMessage) => tracker.MarkChangedAsync();

    public Task HandleEventAsync(EntityInsertedEvent<AkeneoFamilyMapping> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityUpdatedEvent<AkeneoFamilyMapping> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityDeletedEvent<AkeneoFamilyMapping> eventMessage) => tracker.MarkChangedAsync();

    public Task HandleEventAsync(EntityInsertedEvent<AkeneoFamilySubModelRule> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityUpdatedEvent<AkeneoFamilySubModelRule> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityDeletedEvent<AkeneoFamilySubModelRule> eventMessage) => tracker.MarkChangedAsync();

    public Task HandleEventAsync(EntityInsertedEvent<AkeneoFamilyVariantAxisMapping> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityUpdatedEvent<AkeneoFamilyVariantAxisMapping> eventMessage) => tracker.MarkChangedAsync();
    public Task HandleEventAsync(EntityDeletedEvent<AkeneoFamilyVariantAxisMapping> eventMessage) => tracker.MarkChangedAsync();

    public Task HandleEventAsync(EntityInsertedEvent<AkeneoNopEntityMapping> eventMessage) => MarkIfCategoryAsync(eventMessage.Entity);
    public Task HandleEventAsync(EntityUpdatedEvent<AkeneoNopEntityMapping> eventMessage) => MarkIfCategoryAsync(eventMessage.Entity);
    public Task HandleEventAsync(EntityDeletedEvent<AkeneoNopEntityMapping> eventMessage) => MarkIfCategoryAsync(eventMessage.Entity);

    private Task MarkIfCategoryAsync(AkeneoNopEntityMapping mapping) =>
        mapping?.AkeneoEntityTypeId == (int)AkeneoEntityType.Category
            ? tracker.MarkChangedAsync()
            : Task.CompletedTask;
}
