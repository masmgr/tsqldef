namespace SqlSchemaDef.Core.Planning
{
    public sealed class ApplyOptions
    {
        public ApplyTransactionMode TransactionMode { get; set; } = ApplyTransactionMode.SingleTransaction;
    }

    public enum ApplyTransactionMode
    {
        SingleTransaction = 0,
    }
}
