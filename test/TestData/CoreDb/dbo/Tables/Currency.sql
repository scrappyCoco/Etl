CREATE TABLE [dbo].[Currency]
(
    [CurrencyId] INT IDENTITY(1,1) NOT NULL,
    [IsoCode] CHAR(3) NOT NULL
);
GO

ALTER TABLE [dbo].[Currency]
    ADD CONSTRAINT [PK_dbo_Currency] PRIMARY KEY CLUSTERED ([CurrencyId] ASC);
GO