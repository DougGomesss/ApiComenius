namespace Deposito.Orcamentos.Poc.Api.Models
{
    public class ChatMessage
    {
        public string From { get; set; } = string.Empty; // "bot", "client", "attendant"
        public string Text { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }
}
