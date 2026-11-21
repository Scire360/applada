using System.Buffers.Text;
using System.Text;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.Modules.Matches.Application.Queries;

namespace Applada.Api.IntegrationTests;

public class CursorTests
{
    [Fact]
    public void TryDecode_rejects_raw_ff_byte_in_string_key()
    {
        var cursor = CursorSamples.WithInvalidUtf8Key(Cursor.MyMatchesKind);

        Assert.False(Cursor.TryDecode(cursor, Cursor.MyMatchesKind, out var keys));
        Assert.Empty(keys);
    }

    [Theory]
    [InlineData("""{"\ud800":"sample","v":[1,2]}""")]
    [InlineData("""{"k":"\ud800","v":[1,2]}""")]
    [InlineData("""{"k":"\ud800\ud800\ud800","v":[1,2]}""")]
    [InlineData("""{"k":"sample","v":["\ud800",2]}""")]
    [InlineData("""{"k":"sample","v":[1,"\ud800"]}""")]
    public void TryDecode_rejects_unpaired_escaped_surrogate(string json)
    {
        var cursor = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

        Assert.False(Cursor.TryDecode(cursor, "sample", out var keys));
        Assert.Empty(keys);
    }
}
