using MessagePack;
using MessagePack.Resolvers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.Http;

public static class CoreSyncMessagePackSerializer
{
    public static readonly MessagePackSerializerOptions Options =
        TypelessContractlessStandardResolver.Options
            .WithOmitAssemblyVersion(true)
            .WithAllowAssemblyVersionMismatch(true);

    public static byte[] Serialize<T>(T value, CancellationToken cancellationToken = default)
        => MessagePackSerializer.Serialize(value, Options, cancellationToken);

    public static ValueTask<T> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        => MessagePackSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
}
