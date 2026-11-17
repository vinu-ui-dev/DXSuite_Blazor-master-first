-- Aviation: links an airline to its logo assets in the Director airline logo library.
IF OBJECT_ID(N'dbo.tblAirlineLogo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblAirlineLogo (
        AirlineLogoId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblAirlineLogo PRIMARY KEY,
        AirlineCode   VARCHAR(3)    NOT NULL,
        [Role]        VARCHAR(20)   NOT NULL,
        AssetId       INT           NOT NULL,
        AssetName     NVARCHAR(255) NULL,
        AssetPath     VARCHAR(255)  NOT NULL,
        LinkedUtc     DATETIME2     NOT NULL
    );
    CREATE UNIQUE INDEX IX_tblAirlineLogo_Airline_Role ON dbo.tblAirlineLogo (AirlineCode, [Role]);
END
