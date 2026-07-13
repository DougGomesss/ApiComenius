namespace Deposito.Orcamentos.Poc.Api.Models
{
    public record IncomingMessage(string From, string Body, string? Type = "text");
}
