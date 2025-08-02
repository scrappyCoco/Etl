CREATE TABLE [dbo].[Payment]
(
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [Amount] DECIMAL(20, 4) NOT NULL,
    [CurrencyId] SMALLINT NOT NULL,
    [ModifyDt] DATETIME2(3) NOT NULL
);
GO

ALTER TABLE [dbo].[Payment]
    ADD CONSTRAINT [PK_dbo_Payment] PRIMARY KEY CLUSTERED ([PaymentId] ASC);
GO