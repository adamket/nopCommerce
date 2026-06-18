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

        eventMessage.RootMenuItem.InsertBefore("Local plugins",
            new AdminMenuItem
            {
                SystemName = "AkeneoIntegration",
                Title = "Akeneo",
                IconClass = "fas fa-project-diagram",
                Visible = true,
                ChildNodes =
                {
                    new AdminMenuItem
                    {
                        IconClass = "far fa-circle",
                        SystemName = "AkeneoIntegration.Configuration",
                        Title = "Connection",
                        Url = eventMessage.GetMenuItemUrl("AkeneoConnectionConfiguration", "Configure"),
                        Visible = true
                    },
                    new AdminMenuItem
                    {
                        IconClass = "far fa-circle",
                        SystemName = "AkeneoIntegration.Metadata",
                        Title = "Metadata",
                        Url = eventMessage.GetMenuItemUrl("AkeneoMetadata", "Index"),
                        Visible = true
                    },
                    new AdminMenuItem
                    {
                        IconClass = "far fa-circle",
                        SystemName = "AkeneoIntegration.AttributeMappings",
                        Title = "Attribute Mapping",
                        Url = eventMessage.GetMenuItemUrl("AkeneoMapping", "AttributeMappings"),
                        Visible = true
                    },
                    new AdminMenuItem
                    {
                        IconClass = "far fa-circle",
                        SystemName = "AkeneoIntegration.CategoryMappings",
                        Title = "Category Mapping",
                        Url = eventMessage.GetMenuItemUrl("AkeneoMapping", "CategoryMappings"),
                        Visible = true
                    },
                    new AdminMenuItem
                    {
                        IconClass = "far fa-circle",
                        SystemName = "AkeneoIntegration.DryRun",
                        Title = "Dry Run",
                        Url = eventMessage.GetMenuItemUrl("AkeneoSync", "DryRun"),
                        Visible = true
                    },
                    new AdminMenuItem
                    {
                        IconClass = "far fa-circle",
                        SystemName = "AkeneoIntegration.SyncLogs",
                        Title = "Sync Logs",
                        Url = eventMessage.GetMenuItemUrl("AkeneoSyncLog", "List"),
                        Visible = true
                    }
                }
            });
    }
}