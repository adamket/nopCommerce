using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Data.Mapping;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Infrastructure
{
    public partial class PluginNameCompatibility : INameCompatibility
    {
     
        public Dictionary<Type, string> TableNames => new Dictionary<Type, string>
        {
            { typeof(AkeneoAttributeMapping), $"{AkeneoConstants.TablePrefix}{nameof(AkeneoAttributeMapping)}" },
            { typeof(AkeneoNopEntityMapping), $"{AkeneoConstants.TablePrefix}{nameof(AkeneoNopEntityMapping)}" },
            { typeof(AkeneoSyncItemLog), $"{AkeneoConstants.TablePrefix}{nameof(AkeneoSyncItemLog)}" },
            { typeof(AkeneoSyncProfile), $"{AkeneoConstants.TablePrefix}{nameof(AkeneoSyncProfile)}" },
            { typeof(AkeneoSyncRunRecord), $"{AkeneoConstants.TablePrefix}{nameof(AkeneoSyncRunRecord)}" },
        };

        public Dictionary<(Type, string), string> ColumnName => new Dictionary<(Type, string), string>();
    }
}