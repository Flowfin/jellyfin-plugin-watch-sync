using System;
using System.Collections.Generic;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Every project this repository tracks refuses a warning rather than printing one.
///
/// The property is invisible in a run over a tree that carries no warning, which is every
/// tree this repository has once the warnings are repaired. So it would stay green if the
/// property were dropped from a project file, if a project were added without it, or if a
/// later edit passed a switch that turned it off, and the first sign of any of the three
/// would be a warning nobody read.
///
/// The subject is asked of git rather than listed, so a project added tomorrow is covered
/// without anybody remembering this file exists, and the value is read off the evaluated
/// project rather than off the file that happens to set it today: where a project declares
/// it is a detail, that it evaluates to true is the rule.
/// </summary>
public class WarningsAreErrorsTests
{
    /// <summary>
    /// The property whose absence this refuses.
    /// </summary>
    private const string Property = "TreatWarningsAsErrors";

    /// <summary>
    /// Every tracked project evaluates the property to true.
    /// </summary>
    [Fact]
    public void EveryTrackedProjectRefusesAWarningRatherThanPrintingOne()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
        var projects = LockFileTests.TrackedProjects(root);

        Assert.NotEmpty(projects);

        var wrong = new List<string>();

        foreach (var project in projects)
        {
            var value = LockFileTests.Properties(root, project, Property)[Property];

            if (!string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                wrong.Add($"{project} evaluates {Property} to '{value}' rather than true, so a warning in it is printed and the build stays green.");
            }
        }

        Assert.Empty(wrong);
    }
}
