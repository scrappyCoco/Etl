using Coding4Fun.Etl.EtlLauncher;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Testcontainers.MsSql;

namespace Coding4Fun.Etl.IntegrationTest;

public class EtlTest
{
    [Fact]
    public async Task Test1()
    {
        const string StageServer = "stage";
        const string CoreServer = "core";
        const string StageDb = "StageDb";
        const string CoreDb = "CoreDb";

        INetwork network = new NetworkBuilder().Build();

        MsSqlContainer stageContainer = new MsSqlBuilder()
            .WithNetwork(network)
            .Build();

        MsSqlContainer coreContainer = new MsSqlBuilder()
            .WithNetwork(network)
            .Build();

        await stageContainer.StartAsync();
        await coreContainer.StartAsync();

        string stageConnectionString = stageContainer.GetConnectionString().Replace(";Database=master", ";Database=" + StageDb);
        string coreConnectionString = coreContainer.GetConnectionString().Replace(";Database=master", ";Database=" + CoreDb);

        // Deploying source database.
        DacPackage stagePackage = DacPackage.Load(@"..\..\..\..\TestData\StageDb\bin\Debug\StageDb.dacpac");
        DacServices stageDacServices = new(stageContainer.GetConnectionString());
        stageDacServices.Deploy(stagePackage, StageDb, true);

        // Filling source database with test data.
        string fillDataSql = await File.ReadAllTextAsync(@"..\..\..\..\TestData\FillWithSampleData.sql");
        await ExecuteCommandAsync(stageConnectionString, fillDataSql);

        // Deploying target database.
        DacPackage corePackage = DacPackage.Load(@"..\..\..\..\TestData\CoreDb\bin\Debug\CoreDb.dacpac");
        DacServices coreDacServices = new(coreContainer.GetConnectionString());
        DacDeployOptions options = new();
        options.SetVariable("StageDb", StageDb);
        coreDacServices.Deploy(corePackage, CoreDb, true, options: options);

        // Executing ETL process, that is copying data from source to target.
        FileInfo etlFile = new(@"..\..\..\..\TestData\CoreDb\bin\Debug\Etl\MergePayment.json");
        await Program.ExecuteEtlAsync(etlFile, stageConnectionString, coreConnectionString);

        // Validating data after loading data to the target.
        const string currencyCheckSql = "SELECT IsoCode FROM dbo.Currency";
        SqlConnection sqlConnection = new(coreConnectionString);
        HashSet<string> currencyCodes = sqlConnection.Query<string>(currencyCheckSql).ToHashSet();

        Assert.InRange(currencyCodes.Count, 1, 3);
        Assert.Subset(new []{"RUB", "BYN", "KZT"}.ToHashSet(), currencyCodes);
    }

     private static async Task ExecuteCommandAsync(string connectionString, string command)
    {
        await using SqlConnection sqlConnection = new(connectionString);
        await using SqlCommand sqlCommand = new(command, sqlConnection);
        sqlConnection.Open();
        await sqlCommand.ExecuteNonQueryAsync();

        await using SqlCommand sqlCommand1 = new("SELECT COUNT(*) FROM stage.Payment", sqlConnection);
        await using SqlDataReader reader = await sqlCommand1.ExecuteReaderAsync();
        while (reader.Read())
        {
            string value = reader[0].ToString();
            Console.WriteLine(value);
        }
    }
}
