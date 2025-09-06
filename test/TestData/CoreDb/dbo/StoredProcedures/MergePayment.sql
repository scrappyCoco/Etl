CREATE PROCEDURE dbo.MergePayment
AS
BEGIN
    -- C4F.ETL.MainTable:stage.Payment
    -- C4F.ETL.BatchSize:1005
    INSERT INTO dbo.Currency (IsoCode)
    SELECT Currency
    FROM [$(StageDb)].stage.Payment
    EXCEPT
    SELECT IsoCode
    FROM dbo.Currency;

    MERGE dbo.Payment AS Target
    USING (SELECT Payment.PaymentId,
                  Payment.Amount,
                  Currency.CurrencyId,
                  ModifyDt = Payment.BatchDt
           FROM [$(StageDb)].stage.Payment AS Payment
           INNER JOIN dbo.Currency ON Currency.IsoCode = Payment.Currency) AS Source
    ON Source.PaymentId = Target.PaymentId
    WHEN NOT MATCHED THEN INSERT (
           PaymentId
          ,Amount
          ,CurrencyId
          ,ModifyDt)
    VALUES (Source.PaymentId, Source.Amount, Source.CurrencyId, Source.ModifyDt)
    WHEN MATCHED
    THEN UPDATE SET Target.Amount = Source.Amount,
                    Target.CurrencyId = Source.CurrencyId;

    DELETE Target
    FROM dbo.PaymentItem AS Target
    WHERE Target.PaymentId IN (
        SELECT ModifiedPayment.PaymentId
        FROM [$(StageDb)].stage.Payment AS ModifiedPayment
    );

    INSERT INTO dbo.PaymentItem (PaymentId, ItemNumber, Article, Quantity, Price)
    SELECT Payment.PaymentId,
           PaymentItem.ItemNumber,
           PaymentItem.Article,
           PaymentItem.Quantity,
           PaymentItem.Price
    FROM [$(StageDb)].stage.PaymentItem AS PaymentItem
    INNER JOIN [$(StageDb)].stage.Payment AS Payment ON Payment.BatchDt = PaymentItem.BatchDt
                                                    AND Payment.BatchRowId = PaymentItem.ParentRowId;
END