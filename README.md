# Coding4Fun.Sql

## Overview

This project aims to simplify the process of regularly copying batches of data from one Microsoft SQL Server to another. The developer will write the ETL, assuming that the source database and the target database are located on the same server, but in fact they are running on different servers.

### Database architecture

Before we dive into the technical details, let's first understand the concept of data lifetime.

![Data Layers](doc/images/DataLayers.png)

**External Data Source** contains data from various sources, including FTP, S3, DFS, and Kafka. The data can be stored in various formats, such as XML, JSON, YAML, XLSX, and more. We need to download the data as is at the **Raw Level** without any parsing and transformations.

Example of a single row from the Raw Level:
```json
{
    "PaymentId": "c50249b5-e315-47d2-bebc-f4b13e3efe5e",
    "Sum": 20.50,
    "Currency": "RUB",
    "Items": [
        {
            "Article": "p123",
            "Quantity": 1,
            "Price": 10.50
        },
        {
            "Article": "p456",
            "Quantity": 2,
            "Price": 5.00
        }
    ]
}
```

Then, we need to deserialize our structured message into different raw tables, without any transformation, except for adding columns to represent the relationships between the tables.

We have a module that regularly executes the following steps: it fills a batch from the raw level, automatically transforms it (XML, CSV, JSON) into tables with two additional columns that uniquely identify each row in the target table. These columns are: the date and time of the batch (BatchDt) and the row number within the batch (BatchRowId).

Example of the table "Payment" in the Stage Level:

| BatchDt             | BatchRowId | PaymentId    | Sum   | Currency |
| ------------------- | ---------- | ------------ | ----- | -------- |
| 2025-08-09 17:26:21 | 1          | c50249b5-... | 20.50 | RUB      |
| 2025-08-09 17:26:21 | 2          | 257567a5-... | 90.00 | USD      |
| 2025-08-09 17:30:03 | 1          | 6dc6349b-... | 76.20 | BYN      |

Example of the table "PaymentItem" in the Stage Level:
| BatchDt             | BatchRowId | ParentRowId | Article | Quantity | Price |
| ------------------- | ---------- | ----------- | ------- | -------- | ----- |
| 2025-08-09 17:26:21 | 1          | 1           | p123    | 1        | 10.50 |
| 2025-08-09 17:26:21 | 2          | 1           | p456    | 2        | 5.00  |
| 2025-08-09 17:26:21 | 3          | 2           | p567    | 18       | 5.00  |
| 2025-08-09 17:30:03 | 1          | 1           | p987    | 2        | 38.10 |

Please note that we have two batches: the first one was executed at 17:26:21 and contains two rows, while the last batch was executed at 17:30:03 and contains one row.

As a result, the data is parsed in the Stage Level, but it is not normalized. We normalize it at the **Core Level**.

Example of the table "Currency":
| CurrencyId | CurrencyCode |
| ---------- | ------------ |
| 1          | RUB          |
| 2          | USD          |
| 3          | BYN          |

Example of the table "Payment":
| PaymentId    | Sum   | CurrencyId |
| ------------ | ----- | ---------- |
| c50249b5-... | 20.50 | 1          |
| 257567a5-... | 90.00 | 2          |
| 6dc6349b-... | 76.20 | 3          |

Usually, Stage and Core are hosted on separate servers. To maintain their synchronization, we regularly transfer batches of data from Stage to Core and process them. This process is known as ELT (Extract, Load, Transform). For a long time, we used SSIS for this task. However, this approach has some drawbacks:
1. Each ELT process needs to be designed manually, which can be time-consuming.
2. As mentioned above, this process is created by a data engineer manually, and the SSIS package is stored as an XML file with dozens of lines of code. Reviewing the code in this file can also be time-consuming and challenging.

So, we need to transfer a batch of data from the Stage server to the Core server, process it, and save it.

The goal of this project is to create an abstraction that allows us to think of our databases on the Stage and Core as if they were on the same server, even though they are actually located on different ones (like linked servers).

Let's take a look at the process of loading data from Stage, normalizing it, and saving it to the Core:
```sql
CREATE PROCEDURE dbo.MergePayment
AS
BEGIN
    -- The name of the main table that has related tables.
    -- C4F.ETL.MainTable:stage.Payment
    --
    -- The number of rows we want to retrieve from the Stage.
    -- C4F.ETL.BatchSize:1005
    --
    -- During the filling of a batch, it will be converted into a query:
    -- ```
    -- SELECT TOP (1005) WITH TIES BatchDt, ...
    -- FROM stage.Payment
    -- ORDER BY BatchDt;
    -- ```
    -- Then all batches of related tables will be filled with the fetched BatchDt value.
    INSERT INTO dbo.Currency (IsoCode)
    SELECT Currency
    FROM [$(StageDb)].stage.Payment
    EXCEPT
    SELECT IsoCode
    FROM dbo.Currency;

    MERGE dbo.Payment AS Target
    USING (SELECT Payment.PaymentId,
                  Payment.Sum,
                  Currency.CurrencyId,
                  ModifyDt = Payment.BatchDt
           FROM [$(StageDb)].stage.Payment AS Payment
           INNER JOIN dbo.Currency ON Currency.IsoCode = Payment.Currency) AS Source
    ON Source.PaymentId = Target.PaymentId
    WHEN NOT MATCHED THEN INSERT (
           PaymentId
          ,Sum
          ,CurrencyId
          ,ModifyDt)
    VALUES (Source.PaymentId, Source.Sum, Source.CurrencyId, Source.ModifyDt)
    WHEN MATCHED
    THEN UPDATE SET Target.Sum = Source.Sum,
                    Target.CurrencyId = Source.CurrencyId;

    DELETE Target
    FROM dbo.PaymentItem AS Target
    WHERE Target.PaymentId IN (
        SELECT ModifiedPayment.PaymentId
        FROM [$(StageDb)].stage.Payment AS ModifiedPayment
    );

    INSERT INTO dbo.PaymentItem (PaymentId, Article, Quantity, Price)
    SELECT Payment.PaymentId,
           PaymentItem.Article,
           PaymentItem.Quantity,
           PaymentItem.Price
    FROM [$(StageDb)].stage.PaymentItem AS PaymentItem
    INNER JOIN [$(StageDb)].stage.Payment AS Payment ON Payment.BatchDt = PaymentItem.BatchDt
                                                    AND Payment.BatchRowId = PaymentItem.ParentRowId;
END
```

This procedure will be presented in this way:
```sql
-- The name of the main table that has related tables.
-- C4F.ETL.MainTable:stage.Payment
--
-- The number of rows we want to retrieve from the Stage.
-- C4F.ETL.BatchSize:1005
--
-- During the filling of a batch, it will be converted into a query:
-- ```
-- SELECT TOP (1005) WITH TIES BatchDt, ...
-- FROM stage.Payment
-- ORDER BY BatchDt;
-- ```
-- Then all batches of related tables will be filled with the fetched BatchDt value.
INSERT INTO dbo.Currency (IsoCode)
SELECT Currency
FROM #Payment
EXCEPT
SELECT IsoCode
FROM dbo.Currency;

MERGE dbo.Payment AS Target
USING (SELECT Payment.PaymentId,
              Payment.Sum,
              Currency.CurrencyId,
              ModifyDt = Payment.BatchDt
        FROM #Payment AS Payment
        INNER JOIN dbo.Currency ON Currency.IsoCode = Payment.Currency) AS Source
ON Source.PaymentId = Target.PaymentId
WHEN NOT MATCHED THEN INSERT (
        PaymentId
      ,Sum
      ,CurrencyId
      ,ModifyDt)
VALUES (Source.PaymentId, Source.Sum, Source.CurrencyId, Source.ModifyDt)
WHEN MATCHED
THEN UPDATE SET Target.Sum = Source.Sum,
                Target.CurrencyId = Source.CurrencyId;

DELETE Target
FROM dbo.PaymentItem AS Target
WHERE Target.PaymentId IN (
    SELECT ModifiedPayment.PaymentId
    FROM #Payment AS ModifiedPayment
);

INSERT INTO dbo.PaymentItem (PaymentId, Article, Quantity, Price)
SELECT Payment.PaymentId,
        PaymentItem.Article,
        PaymentItem.Quantity,
        PaymentItem.Price
FROM #PaymentItem AS PaymentItem
INNER JOIN #Payment AS Payment ON Payment.BatchDt = PaymentItem.BatchDt
                              AND Payment.BatchRowId = PaymentItem.ParentRowId;
```

## How to use this project

![Etl Description](doc/images/EtlDescription.png)

First and foremost, database projects for Stage and Core must be created using the [SDK-style](https://learn.microsoft.com/en-us/sql/tools/sql-database-projects/tutorials/create-deploy-sql-project?view=sql-server-ver17&pivots=sq1-visual-studio-sdk).

1. Using [SqlPackage](https://learn.microsoft.com/en-us/sql/tools/sqlpackage/sqlpackage?view=sql-server-ver17), we generate a migration script between two releases and publish it to the Stage Server.

2. We release the Stage database (DACPAC) to our private NuGet repository. For development purposes, we can add a NuGet repository to a local computer, based on any directory. [An example](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-add-source):
```
> dotnet nuget add source c:\packages --name TestRepository
> dotnet nuget push Stage.nupkg --source TestRepository
```

3. Include the Stage package with the dacpac in the Core project by using a private NuGet repository.
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build">
  <Sdk Name="Microsoft.Build.Sql" Version="1.0.0" />
  <PropertyGroup>
    <Name>CoreDb</Name>
  </PropertyGroup>
  <!-- Add the following lines -->
  <ItemGroup>
    <SqlCmdVariable Include="StageDb">
      <Value>$(StageDb)</Value>
      <DefaultValue>StageDb</DefaultValue>
    </SqlCmdVariable>
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="StageDb">
      <SuppressMissingDependenciesErrors>False</SuppressMissingDependenciesErrors>
      <DatabaseSqlCmdVariable>StageDb</DatabaseSqlCmdVariable>
      <Version>1.0.0</Version>
    </PackageReference>
  </ItemGroup>
</Project>
```

4. Include `Coding4Fun.Sql.Build` in the Core project from the public NuGet repository.
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build">
  <Sdk Name="Microsoft.Build.Sql" Version="1.0.0" />
  <PropertyGroup>
    <Name>CoreDb</Name>
  </PropertyGroup>
  
  <ItemGroup>
    <SqlCmdVariable Include="StageDb">
      <Value>$(StageDb)</Value>
      <DefaultValue>StageDb</DefaultValue>
    </SqlCmdVariable>
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="StageDb">
      <SuppressMissingDependenciesErrors>False</SuppressMissingDependenciesErrors>
      <DatabaseSqlCmdVariable>StageDb</DatabaseSqlCmdVariable>
      <Version>1.0.0</Version>
    </PackageReference>
    <!-- Add the following line -->
    <PackageReference Include="Coding4Fun.Sql.Build" />
  </ItemGroup>
</Project>
```

5. Build the Core project and deploy it to the server using SqlPackage.

6. During the build of the Core project, the library `Coding4Fun.Sql.Build` is generating configurations for the EtlLauncher. These configurations contain information about the source tables and the ETL process. An example of a configuration file is [MergePayment.json](https://github.com/scrappyCoco/Etl/blob/main/test/TestData/Expected/MergePayment.json).

7. Create a job that will run EtlLauncher periodically. You will need to pass the generated config and connection strings for the Stage and Core databases to it:

```
> .\Coding4Fun.Etl.EtlLauncher.exe `
      --pipeline-config path-to-generated-config.json `
      --source-connection-string "Server=.;Database=Stage;User ID=sa;Password=pa$$"`
      --target-connection-string "Server=.;Database=Stage;User ID=sa;Password=pa$$"
```

## Requirement soft

1. [dotnet SDK](https://dotnet.microsoft.com/en-us/download)

2. [SqlPackage](https://learn.microsoft.com/en-us/sql/tools/sqlpackage/sqlpackage-download?view=sql-server-ver17#installation-cross-platform)

3. [SqlCmd](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-download-install?view=sql-server-ver17&tabs=mac#download-and-install-sqlcmd-go)

4. [Visual Studio Code](https://code.visualstudio.com/download) or [Visual Studio 2022+](https://visualstudio.microsoft.com/downloads/)

5. [MSSQL Extension in Visual Studio Code](https://learn.microsoft.com/en-us/sql/tools/visual-studio-code-extensions/mssql/mssql-extension-visual-studio-code)

6. [Docker Desktop](https://docs.docker.com/desktop/)

### Try it yourself

1. Install SQL Server in Docker:
```
> docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=3J5*Y/,Ym/_&VJ3@" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

3. Build StageDb on your local machine:
```
> dotnet build test/TestData/StageDb/StageDb.sqlproj
```

4. Deploy StageDb to a Docker container:
```
> SqlPackage /Action:Publish \
             /SourceFile:"test/TestData/StageDb/bin/Debug/StageDb.dacpac" \
             /TargetConnectionString:"Server=.;User ID=sa;password=3J5*Y/,Ym/_&VJ3@;Database=StageDb;TrustServerCertificate=True;"
```

5. Fill the tables in StageDb with sample data:
```
> sqlcmd -S localhost -U sa -P "3J5*Y/,Ym/_&VJ3@" -d StageDb -i test/TestData/FillWithSampleData.sql
```

6. Build CoreDb:
```
> dotnet build test/TestData/CoreDb/CoreDb.sqlproj
```

7. Deploy CoreDb to the Docker container:
```
> SqlPackage /Action:Publish \
             /SourceFile:"test/TestData/CoreDb/bin/Debug/CoreDb.dacpac" \
             /TargetConnectionString:"Server=.;User ID=sa;password=3J5*Y/,Ym/_&VJ3@;Database=CoreDb;TrustServerCertificate=True;" \
             /Variables:StageDb=StageDb
```

8. Run Coding4Fun.Etl.EtlLauncher:
```
> src/EtlLauncher/bin/Debug/net8.0/Coding4Fun.Sql.EtlLauncher \
  --pipeline-config /Users/artemkorsunov/RiderProjects/Etl/test/TestData/CoreDb/bin/Debug/Etl/MergePayment.json \
  --source-connection-string "Server=.;User ID=sa;password=3J5*Y/,Ym/_&VJ3@;Database=StageDb;TrustServerCertificate=True;" \
  --target-connection-string "Server=.;User ID=sa;password=3J5*Y/,Ym/_&VJ3@;Database=CoreDb;TrustServerCertificate=True;"
```

9. Explain the data in the CoreDB using the MSSQL extension in VS Code.