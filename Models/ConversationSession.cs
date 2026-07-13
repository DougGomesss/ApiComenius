using Deposito.Orcamentos.Poc.Api.Enums;

namespace Deposito.Orcamentos.Poc.Api.Models
{
    public class ConversationSession
    {
        public string Phone { get; set; } = string.Empty;
        public string? PhoneNumberId { get; set; }
        public ConversationStage Stage { get; set; }

        public string? Product { get; set; }
        public string? Quantity { get; set; }

        public string? DeliveryType { get; set; }
        public string? DeliveryAddress { get; set; }
        public int? MinimumDeliveryDays { get; set; }

        public bool? HasRegistration { get; set; }

        public string? CustomerName { get; set; }
        public string? CustomerAddress { get; set; }
        public string? CustomerPhone { get; set; }
        public string? SecondPhone { get; set; }

        public string? PendingProduct { get; set; }
        public string? PendingQuantity { get; set; }
        public string? PendingCustomerName { get; set; }
        public string? PendingCustomerAddress { get; set; }
        public string? PendingCustomerPhone { get; set; }
        public string? PendingSecondPhone { get; set; }
        public bool PendingAudioChoice { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
