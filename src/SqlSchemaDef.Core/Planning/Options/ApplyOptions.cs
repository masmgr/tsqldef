namespace SqlSchemaDef.Core.Planning
{
    public sealed class ApplyOptions
    {
        public ApplyTransactionMode TransactionMode { get; set; } = ApplyTransactionMode.SingleTransaction;

        public bool ApplyProposals { get; set; }
    }

    public enum ApplyTransactionMode
    {
        SingleTransaction = 0,
    }
}
