-- Director / Aviation: approval state and CUPPS feature options of an airline.
-- An airline with no row here counts as approved with the default options. The app also runs this at first use.
IF OBJECT_ID(N'aviation.tblAirlineProfile', N'U') IS NULL
BEGIN
    CREATE TABLE aviation.tblAirlineProfile (
        AirlineId       INT           NOT NULL CONSTRAINT PK_tblAirlineProfile PRIMARY KEY,
        ApprovalStatus  TINYINT       NOT NULL CONSTRAINT DF_tblAirlineProfile_Status DEFAULT (1),
        RequestedBy     NVARCHAR(256) NULL,
        RequestedUtc    DATETIME2     NOT NULL,
        DecidedBy       NVARCHAR(256) NULL,
        DecidedUtc      DATETIME2     NULL,
        DecisionNote    NVARCHAR(500) NULL,
        SunflowerOption BIT           NOT NULL CONSTRAINT DF_tblAirlineProfile_Sunflower DEFAULT (0),
        AslVideos       BIT           NOT NULL CONSTRAINT DF_tblAirlineProfile_Asl DEFAULT (1),
        DestinationZone BIT           NOT NULL CONSTRAINT DF_tblAirlineProfile_Dest DEFAULT (1)
    );
END
