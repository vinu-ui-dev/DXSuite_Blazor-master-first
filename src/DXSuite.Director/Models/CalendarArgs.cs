namespace DXSuite.Director.Models;

public record CalendarSlotArgs(int DisplayId, int StartMinutes, int EndMinutes);
public record CalendarMoveArgs(int EventId, int NewStartMinutes);
public record CalendarResizeArgs(int EventId, int NewEndMinutes);
