namespace Applada.Api.IntegrationTests.Infrastructure;

/// <summary>Stream que não informa tamanho, para forçar envio chunked.</summary>
internal sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
{
    public override bool CanSeek => false;
}
