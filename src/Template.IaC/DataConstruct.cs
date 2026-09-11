namespace Template.IaC;

using Amazon.CDK.AWS.DynamoDB;

// The two DynamoDB tables (SPEC 5). Each has a single string partition key and no sort key: one
// item per user, one access pattern, so no GSI and no Scan/Query is ever needed.
//
//   AuthTable: PK = LINE#{lineUserId}  -> internalUserId   (touched once per login)
//   DataTable: PK = USER#{internalUserId} -> data/version   (the per-request item)
//
// On-demand billing removes any capacity planning. PITR is on for prod only; dev is disposable, so
// the point-in-time backup would just be cost with nothing to protect.
public sealed class DataConstruct : Construct
{
    public DataConstruct(Construct scope, string id, EnvironmentConfig config)
        : base(scope, id)
    {
        AuthTable = CreateTable("AuthTable", config);
        DataTable = CreateTable("DataTable", config);
    }

    public Table AuthTable { get; }

    public Table DataTable { get; }

    private Table CreateTable(string id, EnvironmentConfig config) =>
        new(this, id, new TableProps
        {
            PartitionKey = new Attribute { Name = "PK", Type = AttributeType.STRING },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
            {
                PointInTimeRecoveryEnabled = !config.Ephemeral,
            },
            RemovalPolicy = config.Ephemeral ? RemovalPolicy.DESTROY : RemovalPolicy.RETAIN,
        });
}
