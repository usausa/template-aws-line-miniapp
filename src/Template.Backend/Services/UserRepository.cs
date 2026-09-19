namespace Template.Backend.Services;

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

// All DynamoDB access. Two tables, each with a single partition key and no sort key, so every
// operation is a point GetItem / PutItem / DeleteItem - never a Scan or Query (those are denied at
// the IAM level, SPEC 9.4). Authorization is the key structure itself: a handler can only ever
// build USER#{sub} from the verified token, so it is physically unable to reach another user's
// item (SPEC 1.3).
public sealed class UserRepository
{
    private const string Pk = "PK";

    private readonly IAmazonDynamoDB ddb;
    private readonly BackendOptions options;
    private readonly TimeProvider clock;

    public UserRepository(IAmazonDynamoDB ddb, BackendOptions options, TimeProvider clock)
    {
        this.ddb = ddb;
        this.options = options;
        this.clock = clock;
    }

    // Maps a verified LINE user id to the internal user id, creating one on first login. Called
    // once per login, so the extra GetItem/PutItem is not on the per-request path.
    //
    // The internal id is a v7 GUID (time-ordered, .NET-native, no extra dependency). Insertion is a
    // conditional PutItem so two concurrent first-logins cannot both win; the loser re-reads and
    // uses the id the winner stored.
    public async Task<string> ResolveOrCreateInternalUserIdAsync(string lineUserId)
    {
        var key = AuthKey(lineUserId);

        var existing = await GetInternalUserIdAsync(key);
        if (existing is not null)
        {
            return existing;
        }

        var internalUserId = $"usr_{Guid.CreateVersion7():N}";
        try
        {
            await ddb.PutItemAsync(new PutItemRequest
            {
                TableName = options.AuthTable,
                Item = new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
                {
                    [Pk] = new AttributeValue(key),
                    ["internalUserId"] = new AttributeValue(internalUserId),
                    ["createdAt"] = new AttributeValue(NowIso()),
                },
                ConditionExpression = "attribute_not_exists(PK)",
                ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.ALL_OLD,
            });

            return internalUserId;
        }
        catch (ConditionalCheckFailedException ex)
        {
            // A concurrent login created the row first. The failure carries the winner's item, so
            // its id is used without a re-read. The id generated here was never stored, so it must
            // not be returned as a fallback: fail instead if the stored id cannot be obtained.
            if ((ex.Item is not null) && ex.Item.TryGetValue("internalUserId", out var winner))
            {
                return winner.S;
            }

            return await GetInternalUserIdAsync(key)
                ?? throw new InvalidOperationException("The internal user id could not be resolved after a concurrent first login.");
        }
    }

    public async Task<DataResponse?> GetDataAsync(string internalUserId)
    {
        var response = await ddb.GetItemAsync(new GetItemRequest
        {
            TableName = options.DataTable,
            Key = new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
            {
                [Pk] = new AttributeValue(DataKey(internalUserId)),
            },
            ConsistentRead = true,
        });

        if (!response.IsItemSet)
        {
            return null;
        }

        var item = response.Item;
        return new DataResponse(
            item["data"].S,
            int.Parse(item["version"].N, CultureInfo.InvariantCulture),
            item["updatedAt"].S);
    }

    // Optimistic-locked write. Succeeds when the item is new or its stored version matches the
    // client's; otherwise reports a conflict for the handler to translate to 409. A JSON document
    // is replaced wholesale, so without this a concurrent edit would silently lose the other
    // device's changes entirely (SPEC 7.4).
    public async Task<PutOutcome> PutDataAsync(string internalUserId, string data, int expectedVersion)
    {
        try
        {
            await ddb.PutItemAsync(new PutItemRequest
            {
                TableName = options.DataTable,
                Item = new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
                {
                    [Pk] = new AttributeValue(DataKey(internalUserId)),
                    ["data"] = new AttributeValue(data),
                    ["version"] = new AttributeValue { N = (expectedVersion + 1).ToString(CultureInfo.InvariantCulture) },
                    ["updatedAt"] = new AttributeValue(NowIso()),
                },
                ConditionExpression = "attribute_not_exists(PK) OR #v = :expected",
                ExpressionAttributeNames = new Dictionary<string, string>(StringComparer.Ordinal) { ["#v"] = "version" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
                {
                    [":expected"] = new AttributeValue { N = expectedVersion.ToString(CultureInfo.InvariantCulture) },
                },
            });

            return new PutOutcome(true, expectedVersion + 1);
        }
        catch (ConditionalCheckFailedException)
        {
            return new PutOutcome(false, 0);
        }
    }

    // Deletes both rows atomically. The data row may not exist (user never saved) - a Delete on a
    // missing item is a no-op, so the transaction still succeeds.
    public async Task DeleteAccountAsync(string internalUserId, string lineUserId)
    {
        await ddb.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Delete = new Delete
                    {
                        TableName = options.AuthTable,
                        Key = new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
                        {
                            [Pk] = new AttributeValue(AuthKey(lineUserId)),
                        },
                    },
                },
                new TransactWriteItem
                {
                    Delete = new Delete
                    {
                        TableName = options.DataTable,
                        Key = new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
                        {
                            [Pk] = new AttributeValue(DataKey(internalUserId)),
                        },
                    },
                },
            ],
        });
    }

    private async Task<string?> GetInternalUserIdAsync(string authKey)
    {
        var response = await ddb.GetItemAsync(new GetItemRequest
        {
            TableName = options.AuthTable,
            Key = new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
            {
                [Pk] = new AttributeValue(authKey),
            },
            ConsistentRead = true,
        });

        return response.IsItemSet ? response.Item["internalUserId"].S : null;
    }

    private static string AuthKey(string lineUserId) => $"LINE#{lineUserId}";

    private static string DataKey(string internalUserId) => $"USER#{internalUserId}";

    private string NowIso() => clock.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}

// Result of a conditional write: whether it applied, and the resulting version when it did.
public sealed record PutOutcome(bool Applied, int NewVersion);
