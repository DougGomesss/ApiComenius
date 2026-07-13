namespace Deposito.Orcamentos.Poc.Api.Models
{
    public class Budget
    {
        public Guid Id { get; set; }

        public string CustomerName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;

        public string Product { get; set; } = string.Empty;
        public string Quantity { get; set; } = string.Empty;

        public string DeliveryType { get; set; } = string.Empty;
        public string? DeliveryAddress { get; set; }
        public string? DepositAddress { get; set; }
        public int? MinimumDeliveryDays { get; set; }

        public bool HasRegistration { get; set; }

        public string? CustomerAddress { get; set; }
        public string? CustomerPhone { get; set; }
        public string? SecondPhone { get; set; }

        public string Status { get; set; } = "Pendente";

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
