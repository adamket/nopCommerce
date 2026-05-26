using System.Reflection;
using Nop.Web.Framework.Infrastructure;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Services;

public static class WidgetZoneHelper
{
    public static IList<string> GetPublicWidgetZones()
    {
        return typeof(PublicWidgetZones)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property =>
                property.PropertyType == typeof(string) &&
                property.GetMethod is not null &&
                property.GetMethod.IsPublic &&
                property.GetMethod.IsStatic)
            .Select(property => property.GetValue(null) as string)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .OrderBy(value => value)
            .ToList()!;
    }
}




//public static IList<string> GetPublicWidgetZones()
//{
//    return typeof(PublicWidgetZones)
//        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
//        .Where(field =>
//            field.IsLiteral &&
//            !field.IsInitOnly &&
//            field.FieldType == typeof(string))
//        .Select(field => (string)field.GetRawConstantValue())
//        .Where(value => !string.IsNullOrWhiteSpace(value))
//        .Distinct()
//        .OrderBy(value => value)
//        .ToList();
//}