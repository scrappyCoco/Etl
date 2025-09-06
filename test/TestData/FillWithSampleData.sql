DECLARE @rowsCount INT = 10000;
DECLARE @childRowsPerParentRowCount INT = 5;
DECLARE @batchSize INT = 1000;

CREATE TABLE #Numbers ([Number] INT NOT NULL PRIMARY KEY);

INSERT INTO #Numbers ([Number])
SELECT Number = ROW_NUMBER() OVER(ORDER BY (SELECT 1))
FROM sys.objects AS o1
CROSS JOIN sys.objects AS o2

;WITH Batches AS (
    SELECT
     BatchNumber = (Number - 1) / @batchSize,
     RowNumber = Number % @batchSize,
     Number,
     Currency = ((ABS(CHECKSUM(NEWID())) % 3) + 1)
    FROM #Numbers
    WHERE Number <= @rowsCount
)
INSERT INTO [stage].[Payment]
           ([BatchDt]
           ,[BatchRowId]
           ,[PaymentId]
           ,[Amount]
           ,[Currency])
SELECT
 [BatchDt] = DATEADD(MINUTE, BatchNumber, GETDATE()),
 [BatchRowId] = RowNumber,
 [PaymentId] = NEWID(),
 [Amount] = ABS(CHECKSUM(NEWID())) % 100,
 [Currency] = CASE Currency
              WHEN 1 THEN 'RUB'
              WHEN 2 THEN 'BYN'
              WHEN 3 THEN 'KZT'
              END
FROM Batches;

INSERT INTO [stage].[PaymentItem]
           ([BatchDt]
           ,[BatchRowId]
           ,[ParentRowId]
           ,[ItemNumber]
           ,[Article]
           ,[Quantity]
           ,[Price])
SELECT BatchDt,
       BatchRowId = ROW_NUMBER() OVER (PARTITION BY Payment.BatchDt
                                           ORDER BY Payment.BatchRowId),
       ParentRowId = Payment.BatchRowId,
       ItemNumber = G.Number,
       Article = NEWID(),
       Quantity = (ABS(CHECKSUM(NEWID())) % 5) + 1,
       Price = (ABS(CHECKSUM(NEWID())) % 100) + 1
FROM [stage].[Payment]
CROSS APPLY (
  SELECT TOP (@childRowsPerParentRowCount) Number
  FROM #Numbers
  WHERE Number < ((ABS(CHECKSUM(NEWID())) + Payment.BatchRowId) % @childRowsPerParentRowCount) + 1
  ORDER BY Number
) AS G;