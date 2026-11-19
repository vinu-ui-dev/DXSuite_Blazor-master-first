-- Director / Aviation: the CUPPS feature options of an airline.
-- An airline with no row here has the default options. The app also runs this at first use.
IF OBJECT_ID(N'aviation.tblAirlineProfile', N'U') IS NULL
BEGIN
    CREATE TABLE aviation.tblAirlineProfile (
        AirlineId       INT NOT NULL CONSTRAINT PK_tblAirlineProfile PRIMARY KEY,
        SunflowerOption BIT NOT NULL CONSTRAINT DF_tblAirlineProfile_Sunflower DEFAULT (0),
        AslVideos       BIT NOT NULL CONSTRAINT DF_tblAirlineProfile_Asl DEFAULT (1),
        DestinationZone BIT NOT NULL CONSTRAINT DF_tblAirlineProfile_Dest DEFAULT (1)
    );
END
