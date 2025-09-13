CREATE TABLE stage.PaymentItem
(
    [BatchDt] DATETIME2(3) NOT NULL,
    [BatchRowId] BIGINT NOT NULL,
    [ParentRowId] BIGINT NOT NULL,
    [Article] VARCHAR(50) NOT NULL,
    [Quantity] INT NOT NULL,
    [Price] DECIMAL(18,2) NOT NULL
);
GO

ALTER TABLE [stage].[PaymentItem]
    ADD CONSTRAINT [PK_stage_PaymentItem] PRIMARY KEY CLUSTERED ([BatchDt] ASC, [BatchRowId] ASC);
GO