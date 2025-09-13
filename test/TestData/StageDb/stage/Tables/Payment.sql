CREATE TABLE [stage].[Payment]
(
    [BatchDt] DATETIME2(3) NOT NULL,
    [BatchRowId] BIGINT NOT NULL,
    [PaymentId] UNIQUEIDENTIFIER NOT NULL,
    [Sum] DECIMAL(20, 4) NOT NULL,
    [Currency] CHAR(3) NOT NULL
);
GO

ALTER TABLE [stage].[Payment]
    ADD CONSTRAINT [PK_stage_Payment] PRIMARY KEY CLUSTERED ([BatchDt] ASC, [BatchRowId] ASC);
GO