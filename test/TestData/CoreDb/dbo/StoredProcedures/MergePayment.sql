CREATE PROCEDURE dbo.MergePayment
AS
BEGIN
    -- The name of the main table that has related tables.
    -- C4F.ETL.MainTable:stage.Payment
    --
    -- The number of rows we want to retrieve from the Stage.
    -- C4F.ETL.BatchSize:1005
    --
    -- Definition of the columns that are used to determine the batch.
    -- C4F.ETL.BatchColumns: stage.Payment.BatchDt,stage.PaymentItem.BatchDt
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