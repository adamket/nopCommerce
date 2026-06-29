using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Data.Mapping;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Data
{
    public partial class PluginNameCompatibility : INameCompatibility
    {
     
        public Dictionary<Type, string> TableNames => new Dictionary<Type, string>
        {
            { typeof(AkeneoAttributeMapping), $"{AkeneoConnectionConstants.TablePrefix}{nameof(AkeneoAttributeMapping)}" },
            { typeof(AkeneoNopEntityMapping), $"{AkeneoConnectionConstants.TablePrefix}{nameof(AkeneoNopEntityMapping)}" },
            { typeof(AkeneoSyncItemLog), $"{AkeneoConnectionConstants.TablePrefix}{nameof(AkeneoSyncItemLog)}" },
            { typeof(AkeneoSyncProfile), $"{AkeneoConnectionConstants.TablePrefix}{nameof(AkeneoSyncProfile)}" },
            { typeof(AkeneoSyncRunRecord), $"{AkeneoConnectionConstants.TablePrefix}{nameof(AkeneoSyncRunRecord)}" },
            { typeof(AkeneoFamilyVariantImportConfiguration), $"{AkeneoConnectionConstants.TablePrefix}{nameof(AkeneoFamilyVariantImportConfiguration)}" },
            { typeof(AkeneoFamilyVariantAxisMapping), $"{AkeneoConnectionConstants.TablePrefix}{nameof(AkeneoFamilyVariantAxisMapping)}" },

        };

        public Dictionary<(Type, string), string> ColumnName => new Dictionary<(Type, string), string>();
    }
}