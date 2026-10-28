using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DXSuite.Director.Services;

/// <summary>
/// Marker for an <see cref="ISaveChangesInterceptor"/> that every <c>DirectorDbContext</c> picks up from DI
/// (see the <c>AddDbContextFactory&lt;DirectorDbContext&gt;</c> registration in <c>Services/Extensions.cs</c>).
/// Register one with <c>services.AddSingleton&lt;IDirectorSaveChangesInterceptor, YourInterceptor&gt;()</c>
/// so a new save-pipeline reaction (e.g. <c>DeliveryVersionInterceptor</c>) does not require touching the
/// context factory itself.
/// </summary>
public interface IDirectorSaveChangesInterceptor : ISaveChangesInterceptor;
