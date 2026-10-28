namespace DXSuite.Shared.Module;

public record ConfigTab(string Key, string Label, Type ComponentType, int Order, string? Feature = null);
