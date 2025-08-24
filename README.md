# Coding4Fun.Etl

## Overview

This project is intended to simplify the process of regularly copying data from one Microsoft SQL Server to another. Developer will write ETL as thought the source database and target database are located on a common server, but in fact they works on different servers.

### Database architecture

Before we will go into technical details lets explain a data lifetime.

```
[External Data Source] => [Raw Level] => [Stage Level] => [Core Level]
```

**External Data Source** contains data from different sources, such as: FTP, S3, DFS, Kafka, etc. This data is saving into a database in Raw level. 
**Raw level** contains data as is without any transformations, that was downloaded from external data.

Example of single row from Raw level:
```json
{
    "PaymentId": "c50249b5-e315-47d2-bebc-f4b13e3efe5e",
    "Article": "123",
    "Sum": {
        "Amount": 20.50,
        "Currency": "RUB"
    }
}
```

Then we have to deserialize our structured message to different raw tables without any transformation, but only with adding columns for references between tables.

We have a module, that regularly executing: filling a batch from the Raw level, automatically transforming it (xml, csv, json) to tables with adding two columns, that are unique identify the row in the target table. They are: date-time of the batch, and row number inside this batch.  

Example of table Payment in the Stage Level:
```
| BatchDt                 | BatchRowId | PaymentId                            | Article |
| 2025-08-09 17:26:21.130 | 1          | c50249b5-e315-47d2-bebc-f4b13e3efe5e | 123     |
| 2025-08-09 17:26:21.130 | 2          | 257567a5-dd6b-450e-89b9-2b0f34e1df21 | 987     |
| 2025-08-09 17:30:03.110 | 1          | 6dc6349b-c927-45e4-ab2f-b73e9ff11962 | 765     |
```

Example of table PaymentSum in the Stage Level:
```
| BatchDt                 | BatchRowId | Amount | Currency |
| 2025-08-09 17:26:21.130 | 1          | 20.50  | RUB      |
| 2025-08-09 17:26:21.130 | 2          | 500.00 | RUB      |
| 2025-08-09 17:30:03.110 | 1          | 30.20  | BYN      |
```

**Core Level** contains data in the normalized form.

Example of the table Currency:
```
| CurrencyId | CurrencyCode |
| 1          | RUB          |
| 2          | BYN          |
```

Example of the table Payment:
```
| PaymentId                            | Amount | CurrencyId |
| c50249b5-e315-47d2-bebc-f4b13e3efe5e | 20.50  | 1          |
| 257567a5-dd6b-450e-89b9-2b0f34e1df21 | 500.00 | 1          |
| 6dc6349b-c927-45e4-ab2f-b73e9ff11962 | 30.20  | 2          |
```

As a rule stage data and core live on different servers. We must regularly load batches of data from the Stage, load it into core data and transform it.
This process has name ELT (Extract-Load-Transform). For a long time we have using a SSIS to do it. But it has some disadvantages:
1. Each ELT process we have to design manually, this is very time-consuming.
2. As was mentioned in the previous sentence, this process is written manually by an data-engineer and SSIS-package exists as a XML-file, that has dozens lines of code. Code review  of this file is also very time-consuming and not easy.

So, we have to load batch of data from the Stage server to the Core server, then transform it and save.

This project is intended to create an abstraction, that allow us to think that our databases on the Stage and the Core work on the same server, but in fact, they located on different servers (something like linked servers).

Lets look at a procedure, that will load data from the Stage, normalize it and save to Core level:
```
CREATE PROCEDURE dbo.MergePayment
AS
BEGIN
	-- C4F.ETL.MainTable:stage.Payment
	-- C4F.ETL.BatchSize:1005

    -- Filling dictionary of currencies.
	INSERT INTO dbo.Currency (IsoCode)
	SELECT Currency
	FROM StageDb.stage.Payment
	EXCEPT
	SELECT IsoCode
	FROM dbo.Currency;

	DECLARE @modifiedPayment TABLE (PaymentId UNIQUEIDENTIFIER);

    -- Filling Payment using dictionary of currencies.
	MERGE dbo.Payment AS Target
	USING (SELECT Payment.PaymentId,
				  Payment.Amount,
				  Currency.CurrencyId,
				  ModifyDt = Payment.BatchDt
		   FROM StageDb.stage.Payment AS Payment
		   INNER JOIN dbo.Currency ON Currency.IsoCode = Payment.Currency) AS Source
	ON Source.PaymentId = Target.PaymentId
	WHEN NOT MATCHED THEN INSERT (
		   PaymentId
		  ,Amount
		  ,CurrencyId
		  ,ModifyDt)
	VALUES (Source.PaymentId, Source.Amount, Source.CurrencyId, Source.ModifyDt)
	WHEN MATCHED AND Target.ModifyDt < Source.ModifyDt
	THEN UPDATE SET Target.Amount = Source.Amount,
					Target.CurrencyId = Source.CurrencyId
	OUTPUT INSERTED.PaymentId INTO @modifiedPayment (PaymentId);
END
```

This procedure under the hood will be transformed to this presentation:
```
	-- C4F.ETL.MainTable:stage.Payment
	-- C4F.ETL.BatchSize:1005

    -- Filling dictionary of currencies.
	INSERT INTO dbo.Currency (IsoCode)
	SELECT Currency
	FROM #Payment -- StageDb.stage.Payment is replaced with #Payment that is automatically filling with a batch from Stage server
	EXCEPT
	SELECT IsoCode
	FROM dbo.Currency;

	DECLARE @modifiedPayment TABLE (PaymentId UNIQUEIDENTIFIER);

    -- Filling Payment using dictionary of currencies.
	MERGE dbo.Payment AS Target
	USING (SELECT Payment.PaymentId,
				  Payment.Amount,
				  Currency.CurrencyId,
				  ModifyDt = Payment.BatchDt
		   FROM #Payment AS Payment -- StageDb.stage.Payment is replaced with #Payment that is automatically filling with a batch from Stage server
		   INNER JOIN dbo.Currency ON Currency.IsoCode = Payment.Currency) AS Source
	ON Source.PaymentId = Target.PaymentId
	WHEN NOT MATCHED THEN INSERT (
		   PaymentId
		  ,Amount
		  ,CurrencyId
		  ,ModifyDt)
	VALUES (Source.PaymentId, Source.Amount, Source.CurrencyId, Source.ModifyDt)
	WHEN MATCHED AND Target.ModifyDt < Source.ModifyDt
	THEN UPDATE SET Target.Amount = Source.Amount,
					Target.CurrencyId = Source.CurrencyId
	OUTPUT INSERTED.PaymentId INTO @modifiedPayment (PaymentId);
```

## How to include it into a project

1. First of all, database projects of Stage and Core level have to be created with [SDK-style](https://learn.microsoft.com/en-us/sql/tools/sql-database-projects/tutorials/create-deploy-sql-project?view=sql-server-ver17&pivots=sq1-visual-studio-sdk)

2. Then we will prepare our project to pack our dacpac into nuget-package. To make it we have to add property `GeneratePackageOnBuild` into `PropertyGroup` in the sqlproj file:
```
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build">
  <Sdk Name="Microsoft.Build.Sql" Version="1.0.0" />
  <PropertyGroup>
    <Name>StageDb</Name>
    <DSP>Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider</DSP>
    <ModelCollation>1033, CI</ModelCollation>
    <ProjectGuid>{00000000-0000-0000-0000-000000000000}</ProjectGuid>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  </PropertyGroup>
</Project>
``` 

3. Publish it into nuget-repository. If you want to test it on the localhost you can create your local directory-based nuget-repository. 

4. Add reference in the Core.sqlproj to the Stage.sqlproj and PackageReference `Coding4Fun.Sql.Build`:
```
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build">
  <Sdk Name="Microsoft.Build.Sql" Version="1.0.0" />
  <PropertyGroup>
    <Name>CoreDb</Name>
    <DSP>Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider</DSP>
    <ModelCollation>1033, CI</ModelCollation>
    <ProjectGuid>{00000000-0000-0000-0000-000000000000}</ProjectGuid>
    <C4FEtlGeneratorSourceDacPacPath>$(MSBuildProjectDirectory)/../StageDb/bin/$(Configuration)/StageDb.dacpac</C4FEtlGeneratorSourceDacPacPath>
    <C4FEtlGeneratorTargetDacPacPath>$(SqlTargetPath)</C4FEtlGeneratorTargetDacPacPath>
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
    <PackageReference Include="Coding4Fun.Sql.Build" />
  </ItemGroup>
</Project>
```