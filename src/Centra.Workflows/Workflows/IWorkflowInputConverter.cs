using Centra.Serialization;

namespace Centra.Core.Workflows;

/// <summary>
/// Defines a contract for converting activity and workflow input arguments.
/// </summary>
public interface IWorkflowInputConverter
{
    /// <summary>
    /// Converts the specified raw input to the target type using serialization or direct casting.
    /// </summary>
    object? ConvertInput(object? input, Type targetType, ICentraSerializer serializer);
}
