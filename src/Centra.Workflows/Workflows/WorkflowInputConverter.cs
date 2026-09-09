using Centra.Serialization;

namespace Centra.Core.Workflows;

/// <summary>
/// Default implementation of <see cref="IWorkflowInputConverter"/>.
/// </summary>
public sealed class WorkflowInputConverter : IWorkflowInputConverter
{
    /// <summary>
    /// Singleton default instance of <see cref="WorkflowInputConverter"/>.
    /// </summary>
    public static readonly WorkflowInputConverter Instance = new();

    public object? ConvertInput(object? input, Type targetType, ICentraSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(serializer);

        if (input is null)
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        if (targetType.IsInstanceOfType(input))
        {
            return input;
        }

        if (input is byte[] bytes)
        {
            return serializer.Deserialize(bytes, targetType);
        }

        if (input is ReadOnlyMemory<byte> memory)
        {
            return serializer.Deserialize(memory, targetType);
        }

        // Serialize and deserialize to convert objects
        var serialized = serializer.Serialize(input);
        return serializer.Deserialize(serialized, targetType);
    }
}
