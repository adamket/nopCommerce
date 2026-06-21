using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
public class AkeneoVariantAxisMapping
{
    public string AkeneoAttributeCode { get; set; }

    public int NopProductAttributeId { get; set; }

    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }
}