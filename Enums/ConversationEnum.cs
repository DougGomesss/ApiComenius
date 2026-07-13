namespace Deposito.Orcamentos.Poc.Api.Enums
{
    public enum ConversationStage
    {
        Started = 0,
        MenuSent = 1,

        WaitingProduct = 2,
        WaitingQuantity = 3,
        WaitingDeliveryType = 4,

        AskingHasRegistration = 6,

        WaitingRegisteredCustomerName = 7,

        WaitingFullName = 8,
        WaitingCustomerAddress = 9,
        WaitingCustomerPhone = 10,
        WaitingSecondPhone = 11,

        AskingContinueBudget = 12,

        HumanSupport = 13,
        Finished = 14,
        Canceled = 15,

        ConfirmingProduct = 16,
        ConfirmingQuantity = 17,
        ConfirmingCustomerName = 18,
        ConfirmingCustomerAddress = 19,
        ConfirmingCustomerPhone = 20,
        ConfirmingSecondPhone = 21,
        ConfirmingAudioChoice = 22,
        ConfirmingFullName = 23,

        Attended = 24,
        WaitingHumanName = 25,
    }
}
