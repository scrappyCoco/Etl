;WITH Numbers AS (
	SELECT Number = ROW_NUMBER() OVER(ORDER BY (SELECT 1))
	FROM sys.objects AS o1
	CROSS JOIN sys.objects AS o2
), Batches AS (
	SELECT
	 BatchNumber = (Number - 1) / 1000,
	 RowNumber = Number % 1000,
	 Number,
	 Currency = ((ABS(CHECKSUM(NEWID())) % 3) + 1)
	FROM Numbers
	WHERE Number <= 1000
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

;WITH Numbers AS (
	SELECT Number = ROW_NUMBER() OVER(ORDER BY (SELECT 1))
	FROM sys.objects AS o1
	CROSS JOIN sys.objects AS o2
)
INSERT INTO [stage].[PaymentItem]
           ([BatchDt]
           ,[BatchRowId]
           ,[PaymentId]
           ,[ItemNumber]
           ,[Article]
           ,[Quantity]
           ,[Price])
SELECT BatchDt,
       BatchRowId = ROW_NUMBER() OVER (PARTITION BY BatchDt ORDER BY Payment.BatchRowId),
	   PaymentId,
	   ItemNumber = g.Number,
	   Article = newid(),
	   Quantity = (ABS(CHECKSUM(NEWID())) % 5) + 1,
	   Price = (ABS(CHECKSUM(NEWID())) % 100) + 1
FROM [stage].[Payment]
CROSS APPLY (
  SELECT TOP (5) Number
  FROM Numbers
  WHERE Number < ((ABS(CHECKSUM(NEWID())) + BatchRowId) % 5) + 1
  ORDER BY Number
) AS G