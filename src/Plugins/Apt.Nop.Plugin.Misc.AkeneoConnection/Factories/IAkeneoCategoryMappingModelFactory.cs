using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
public interface IAkeneoCategoryMappingModelFactory
{
    Task<AkeneoCategoryMappingListModel> PrepareCategoryMappingListModelAsync();
}