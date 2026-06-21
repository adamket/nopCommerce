namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
public static class AkeneoMappingHelper
{

    public static bool SetIfChanged<T>(
        T currentValue,
        T newValue,
        Action<T> setter)
    {
        if (typeof(T) == typeof(string))
        {
            var currentString = currentValue as string ?? string.Empty;
            var newString = newValue as string ?? string.Empty;

            if (string.Equals(currentString, newString, StringComparison.Ordinal))
                return false;

            setter((T)(object)newString);
            return true;
        }

        if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
            return false;

        setter(newValue);
        return true;
    }

}
