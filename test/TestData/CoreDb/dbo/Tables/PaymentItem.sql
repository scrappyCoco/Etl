CREATE TABLE [dbo].[PaymentItem]
(
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [ItemNumber] INT NOT NULL,
    [Article] VARCHAR(50) NOT NULL,
    [Quantity] INT NOT NULL,
    [Price] DECIMAL(18,2) NOT NULL
);
GO

ALTER TABLE [dbo].[PaymentItem]
    ADD CONSTRAINT [PK_dbo_PaymentItem] PRIMARY KEY CLUSTERED ([PaymentId] ASC, [ItemNumber] ASC);
GO