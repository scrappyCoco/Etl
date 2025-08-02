CREATE PROCEDURE dbo.MergePayment
AS
BEGIN
	-- C4F.ETL.MainTable:stage.Payment
	-- C4F.ETL.BatchSize:1005
	INSERT INTO dbo.Currency (IsoCode)
	SELECT Currency
	FROM StageDb.stage.Payment
	EXCEPT
	SELECT IsoCode
	FROM dbo.Currency;

	DECLARE @modifiedPayment TABLE (PaymentId UNIQUEIDENTIFIER);

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


	DELETE Target
	FROM dbo.PaymentItem AS Target
	WHERE Target.PaymentId IN (SELECT ModifiedPayment.PaymentId FROM @modifiedPayment AS ModifiedPayment);

	INSERT INTO dbo.PaymentItem (PaymentId, ItemNumber, Article, Quantity, Price)
	SELECT PaymentId, ItemNumber, Article, Quantity, Price
	FROM StageDb.stage.PaymentItem AS PaymentItem
	WHERE PaymentItem.PaymentId IN (SELECT ModifiedPayment.PaymentId FROM @modifiedPayment AS ModifiedPayment);
END