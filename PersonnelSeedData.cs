namespace Gateway;

/// <summary>
/// Demo personnel seed — cleared after real school data was imported.
/// Kept as no-op so the call site in Program.cs compiles without changes.
/// </summary>
public static class PersonnelSeedData
{
    public static Task SeedAsync(SBD.Infrastructure.Data.SbdDbContext _) => Task.CompletedTask;
}
