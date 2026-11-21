using Applada.Api.IntegrationTests.Infrastructure;
using Applada.Modules.Matches.Application.Queries;

namespace Applada.Api.IntegrationTests;

public class CursorTests
{
    [Fact]
    public void TryDecode_rejects_invalid_utf8_before_returning_keys()
    {
        var cursor = CursorSamples.WithInvalidUtf8Key(Cursor.MyMatchesKind);

        Assert.False(Cursor.TryDecode(cursor, Cursor.MyMatchesKind, out var keys));
        Assert.Empty(keys);
    }
}
