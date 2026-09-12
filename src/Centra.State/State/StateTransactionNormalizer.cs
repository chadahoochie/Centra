using Centra.Serialization;

namespace Centra.State;

internal static class StateTransactionNormalizer
{
    internal static IReadOnlyList<StateTransactionOperation> Normalize(
        IReadOnlyList<StateTransactionOperation> operations,
        ICentraSerializer serializer)
    {
        if (operations.Count == 0)
        {
            return operations;
        }

        List<StateTransactionOperation>? converted = null;
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (op is SetTransactionOperation<byte[]> || op is DeleteTransactionOperation)
            {
                converted?.Add(op);
                continue;
            }

            converted ??= new List<StateTransactionOperation>(operations.Take(i));
            converted.Add(NormalizeOperation(op, serializer));
        }

        return converted ?? operations;
    }

    internal static StateTransactionOperation NormalizeOperation(
        StateTransactionOperation op,
        ICentraSerializer serializer)
    {
        var opType = op.GetType();
        if (!opType.IsGenericType || opType.GetGenericTypeDefinition() != typeof(SetTransactionOperation<>))
        {
            return op;
        }

        var genericArg = opType.GetGenericArguments()[0];
        var valueProp = opType.GetProperty(nameof(SetTransactionOperation<object>.Value))!;
        var expectedETagProp = opType.GetProperty(nameof(SetTransactionOperation<object>.ExpectedETag))!;
        var optionsProp = opType.GetProperty(nameof(SetTransactionOperation<object>.Options))!;

        var rawValue = valueProp.GetValue(op);
        var expectedETag = (string?)expectedETagProp.GetValue(op);
        var options = (StateOptions?)optionsProp.GetValue(op);

        var bytes = rawValue switch
        {
            null => [],
            ReadOnlyMemory<byte> rom => rom.ToArray(),
            _ => serializer.Serialize(rawValue, genericArg)
        };

        return new SetTransactionOperation<byte[]>(op.Key, bytes, expectedETag, options);
    }
}
