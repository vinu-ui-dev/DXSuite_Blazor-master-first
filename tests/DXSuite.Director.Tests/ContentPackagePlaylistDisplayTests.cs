using DXSuite.Director.Services.ContentPackage;

namespace DXSuite.Director.Tests;

/// <summary>
/// A Playlist location has no displays, and a content package is the only path that can break that, by
/// putting a display under one or by retyping a location that has displays. The importer refuses such a
/// package before its transaction opens; these pin the decision it refuses on.
/// </summary>
public class ContentPackagePlaylistDisplayTests
{
    private const int Playlist = ContentPackageImportRules.PlaylistLocationTypeId;
    private const int Standard = 1;

    private static readonly Guid Lobby = Guid.NewGuid();
    private static readonly Guid Concourse = Guid.NewGuid();

    private static PackageDisplayState Display(string name, Guid location) => new(Guid.NewGuid(), name, location);

    [Fact]
    public void ADisplayInsertedUnderAPlaylistLocation_IsRefused()
    {
        var conflicts = ContentPackageImportRules.PlaylistDisplayConflicts(
            [new PackageLocationState(Lobby, "Lobby", Playlist, InPackage: true)],
            [],
            [Display("Lobby 1", Lobby)],
            replaceMode: false);

        var conflict = Assert.Single(conflicts);
        Assert.Equal("Lobby", conflict.LocationName);
        Assert.Equal(new[] { "Lobby 1" }, conflict.DisplayNames);
        Assert.Contains("Lobby 1", conflict.Message);
    }

    [Fact]
    public void ADisplayInsertedUnderAnExistingPlaylistLocationTheBundleDoesNotCarry_IsRefused()
    {
        var conflicts = ContentPackageImportRules.PlaylistDisplayConflicts(
            [new PackageLocationState(Lobby, "Lobby", Playlist, InPackage: false)],
            [],
            [Display("Lobby 1", Lobby)],
            replaceMode: false);

        Assert.Single(conflicts);
    }

    [Fact]
    public void RetypingALocationThatHasDisplaysToPlaylist_IsRefusedAndNamesItsDisplays()
    {
        var conflicts = ContentPackageImportRules.PlaylistDisplayConflicts(
            [new PackageLocationState(Concourse, "Concourse", Playlist, InPackage: true)],
            [Display("West 2", Concourse), Display("East 1", Concourse)],
            [],
            replaceMode: false);

        Assert.Equal(new[] { "East 1", "West 2" }, Assert.Single(conflicts).DisplayNames);
    }

    [Fact]
    public void RetypingADisplaylessLocationToPlaylist_Imports()
    {
        var conflicts = ContentPackageImportRules.PlaylistDisplayConflicts(
            [new PackageLocationState(Concourse, "Concourse", Playlist, InPackage: true)],
            [],
            [],
            replaceMode: false);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void ReplaceMode_RemovesTheDisplaysTheBundleDoesNotList_SoTheRetypeImports()
    {
        var conflicts = ContentPackageImportRules.PlaylistDisplayConflicts(
            [new PackageLocationState(Concourse, "Concourse", Playlist, InPackage: true)],
            [Display("West 2", Concourse)],
            [],
            replaceMode: true);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void ReplaceMode_DoesNotRemoveDisplaysAtALocationTheBundleOnlyReferences()
    {
        var conflicts = ContentPackageImportRules.PlaylistDisplayConflicts(
            [new PackageLocationState(Lobby, "Lobby", Playlist, InPackage: false)],
            [Display("Lobby 1", Lobby)],
            [],
            replaceMode: true);

        Assert.Single(conflicts);
    }

    [Fact]
    public void ADisplayTheBundleMovesAwayFromTheRetypedLocation_DoesNotCount()
    {
        var moved = Display("West 2", Concourse);

        var conflicts = ContentPackageImportRules.PlaylistDisplayConflicts(
            [
                new PackageLocationState(Concourse, "Concourse", Playlist, InPackage: true),
                new PackageLocationState(Lobby, "Lobby", Standard, InPackage: true)
            ],
            [moved],
            [moved with { LocationContentPackageId = Lobby }],
            replaceMode: false);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void DisplaysUnderOtherLocationTypes_NeverConflict()
    {
        var conflicts = ContentPackageImportRules.PlaylistDisplayConflicts(
            [new PackageLocationState(Lobby, "Concourse Entry Playlist", 10, InPackage: true)],
            [Display("Entry 1", Lobby)],
            [Display("Entry 2", Lobby)],
            replaceMode: false);

        Assert.Empty(conflicts);
    }
}
