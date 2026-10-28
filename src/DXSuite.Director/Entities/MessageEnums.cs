namespace DXSuite.Director.Entities;

public enum MessageDisplayMode : byte
{
    FullScreen = 1,
    Overlay = 2,
    Ticker = 3
}

public enum MessageStatus : byte
{
    Draft = 0,
    Approved = 1,
    Retired = 2
}

public enum MessageActivationSource : byte
{
    DirectorAdmin = 1,
    Cupps = 2,
    Api = 3
}
