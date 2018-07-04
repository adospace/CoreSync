using JetBrains.Annotations;
using System;
using System.Linq;

namespace CoreSync
{
    internal static class Validate
    {
        public static void NotNull([CanBeNull] object value, [NotNull] string parameterName, [CanBeNull] string field = null)
        {
            if (value == null) throw new ArgumentNullException(parameterName + (field == null ? string.Empty : "." + field));
        }

        public static void NotNullOrEmptyOrWhiteSpace([CanBeNull]string value, [NotNull] string parameterName, [CanBeNull] string field = null)
        {
            if (value == null) throw new ArgumentNullException(parameterName + (field == null ? string.Empty : "." + field));
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Parameter can't be an empty or whitespace string", parameterName + (field == null ? string.Empty : "." + field));
        }

        public static void NotNullOrEmptyArray<T>([CanBeNull] T[] values, [NotNull] string parameterName, [CanBeNull] string field = null)
        {
            if (values == null) throw new ArgumentNullException(parameterName + (field == null ? string.Empty : "." + field));
            if (values.Length == 0) throw new ArgumentException($"Parameter can't be an empty array", parameterName + (field == null ? string.Empty : "." + field));
            if (values.Any(_ => _ == null)) throw new ArgumentException($"Parameter cannot contain Null values", parameterName + (field == null ? string.Empty : "." + field));
        }

        public static void NotEmpty(Guid id, [NotNull] string parameterName, [CanBeNull] string field = null)
        {
            if (id == Guid.Empty) throw new ArgumentException("Parameter can't be empty", parameterName + (field == null ? string.Empty : "." + field));
        }

        public static void Any(bool[] arrayOfValidations, [NotNull] string parameterName, [CanBeNull] string field = null)
        {
            if (!arrayOfValidations.Any(_ => _)) throw new ArgumentException("Parameter is not valid", parameterName + (field == null ? string.Empty : "." + field));
        }

        public static void All(bool[] arrayOfValidations, [NotNull] string parameterName,
            [CanBeNull] string field = null)
        {
            if (!arrayOfValidations.All(_ => _))
                throw new ArgumentException("Parameter is not valid",
                    parameterName + (field == null ? string.Empty : "." + field));
        }

        public static void Positive(int value, [NotNull] string parameterName, [CanBeNull] string field = null)
        {
            if (value <= 0) throw new ArgumentException($"Parameter must be greater than 0", parameterName + (field == null ? string.Empty : "." + field));
        }

    }
}
