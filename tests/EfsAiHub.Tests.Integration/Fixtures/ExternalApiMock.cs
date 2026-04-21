using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EfsAiHub.Tests.Integration.Fixtures;

/// <summary>
/// WireMock server simulating the external financial API
/// (buscar_ativo, ObterPosicaoCliente and /ativos endpoints).
/// </summary>
public sealed class ExternalApiMock : IDisposable
{
    public WireMockServer Server { get; }
    public string BaseUrl => Server.Url!;

    public ExternalApiMock()
    {
        Server = WireMockServer.Start();
        ConfigureStubs();
    }

    private void ConfigureStubs()
    {
        Server.Given(
            Request.Create().WithPath("/ativos/PETR4").UsingGet()
        ).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"found":true,"ativo":{"ticker":"PETR4","nome":"Petrobras PN"}}""")
        );

        Server.Given(
            Request.Create().WithPath("/ativos/*").UsingGet()
        ).AtPriority(100).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"found":false,"ativo":null}""")
        );

        Server.Given(
            Request.Create().WithPath("/posicao").UsingGet()
        ).AtPriority(100).RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("[]")
        );
    }

    public void Dispose() => Server.Dispose();
}
