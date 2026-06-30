using Nop.Core.Infrastructure;
using Nop.Services.Events;
using Nop.Services.Security;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Menu;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.EventConsumers;

public class AdminMenuConsumer(IPermissionService permissionService) : IConsumer<AdminMenuCreatedEvent>
{
    public async Task HandleEventAsync(AdminMenuCreatedEvent eventMessage)
    {
        if (!await permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
            return;


        var subAdminMenuItems = new List<AdminMenuItem>
        {
            new AdminMenuItem
            {
                IconClass = "far fa-circle",
                SystemName = "AkeneoIntegration.Configuration",
                Title = "Connection",
                Url = "/admin/akeneo-connection/configure", //eventMessage.GetMenuItemUrl("AkeneoConnectionConfiguration", "Configure"),
                Visible = true
            }
        };

        var settings = EngineContext.Current.Resolve<AkeneoConnectionSettings>();

        if (!string.IsNullOrWhiteSpace(settings.AkeneoConnectionClientId) &&
            !string.IsNullOrWhiteSpace(settings.AkeneoConnectionClientSecret) &&
            !string.IsNullOrWhiteSpace(settings.AkeneoConnectionPassword) &&
            !string.IsNullOrWhiteSpace(settings.AkeneoConnectionBaseUrl) &&
            !string.IsNullOrWhiteSpace(settings.AkeneoConnectionUsername)
            ) 
        {
            subAdminMenuItems.AddRange(new List<AdminMenuItem>
            {
                //new()
                //{
                //    IconClass = "far fa-circle",
                //    SystemName = "AkeneoIntegration.Metadata",
                //    Title = "Metadata",
                //    Url = eventMessage.GetMenuItemUrl("AkeneoMetadata", "Index"),
                //    Visible = true
                //},
                new()
                {
                    IconClass = "far fa-circle",
                    SystemName = "AkeneoIntegration.AttributeMappings",
                    Title = "Attribute Mapping",
                    Url = eventMessage.GetMenuItemUrl("AkeneoMapping", "AttributeMappings"),
                    Visible = true
                },
                new()
                {
                    IconClass = "far fa-circle",
                    SystemName = "AkeneoIntegration.CategoryMappings",
                    Title = "Category Mapping",
                    Url = eventMessage.GetMenuItemUrl("AkeneoMapping", "CategoryMappings"),
                    Visible = true
                },
                new()
                {
                    IconClass = "far fa-circle",
                    SystemName = "AkeneoIntegration.FamilyMappings",
                    Title = "Family Mapping",
                    Url = eventMessage.GetMenuItemUrl("AkeneoFamilyVariantImportConfiguration", "List"),
                    Visible = true
                },
                new()
                {
                    IconClass = "far fa-circle",
                    SystemName = "AkeneoIntegration.DryRun",
                    Title = "Dry Run",
                    Url = eventMessage.GetMenuItemUrl("AkeneoSync", "DryRun"),
                    Visible = true
                },
                new()
                {
                    IconClass = "far fa-circle",
                    SystemName = "AkeneoIntegration.SyncProfiles",
                    Title = "Sync Profiles",
                    Url = eventMessage.GetMenuItemUrl("AkeneoSyncProfile", "List"),
                    Visible = true
                },
                new()
                {
                    IconClass = "far fa-circle",
                    SystemName = "AkeneoIntegration.SyncLogs",
                    Title = "Sync Logs",
                    Url = eventMessage.GetMenuItemUrl("AkeneoSyncLog", "List"),
                    Visible = true
                }
            });
        }

        eventMessage.RootMenuItem.InsertBefore("Local plugins",
            new AdminMenuItem
            {
                SystemName = "AkeneoIntegration",
                Title = "Akeneo",
                IconClass = "fas fa-project-diagram",
                Visible = true,
                ChildNodes = subAdminMenuItems
            });
    }
}