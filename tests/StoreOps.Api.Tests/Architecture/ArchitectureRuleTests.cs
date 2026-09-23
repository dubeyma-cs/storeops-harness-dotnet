using StoreOps.ArchCheck;

namespace StoreOps.Api.Tests.Architecture;

/// <summary>
/// Tests the deterministic architecture gate itself.
/// </summary>
/// <remarks>
/// A gate that blocks a harness run has to be trustworthy in both directions. The first test
/// asserts the real codebase passes; the rest feed the scanner synthetic modules that break one
/// rule each and assert the matching rule code fires. Without the negative cases, a scanner that
/// silently matched nothing would look like a clean pass forever.
/// </remarks>
public sealed class ArchitectureRuleTests
{
    [Fact]
    public void Given_the_StoreOps_source_tree_When_it_is_scanned_Then_there_are_no_architecture_violations()
    {
        var result = ArchitectureScanner.Scan(SolutionPaths.SourceRoot);

        Assert.True(
            result.Passed,
            "Architecture violations found:" + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                result.Violations.Select(v => $"  [{v.RuleCode}] {v.File}:{v.Line} {v.Message}")));

        Assert.True(result.FilesScanned > 20, $"Only {result.FilesScanned} files scanned — is the path right?");
    }

    [Fact]
    public void Given_the_StoreOps_source_tree_When_it_is_scanned_Then_the_module_graph_is_the_intended_one()
    {
        var result = ArchitectureScanner.Scan(SolutionPaths.SourceRoot);

        Assert.Equal(
            new[] { "activities", "alerts", "programmes", "reports", "staff" },
            result.ModuleGraph.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

        // staff is read-only for other modules and depends on none of them.
        Assert.Empty(result.ModuleGraph["staff"]);

        // activities must not know about alerts or reports — those are event-driven consumers.
        Assert.DoesNotContain("alerts", result.ModuleGraph["activities"]);
        Assert.DoesNotContain("reports", result.ModuleGraph["activities"]);
    }

    [Fact]
    public void Given_a_module_that_reads_another_modules_repository_When_scanned_Then_SO_001_fires()
    {
        using var tree = SyntheticSourceTree.Create();

        tree.Write(
            "Modules/Staff/IStaffRepository.cs",
            """
            namespace Demo.Modules.Staff;
            public interface IStaffRepository { }
            """);

        tree.Write(
            "Modules/Activities/ActivityService.cs",
            """
            namespace Demo.Modules.Activities;
            using Demo.Modules.Staff;
            public sealed class ActivityService
            {
                private readonly IStaffRepository _staff;
                public ActivityService(IStaffRepository staff) => _staff = staff;
            }
            """);

        var violation = Assert.Single(tree.Scan().Violations);

        Assert.Equal(ArchitectureRules.ModuleBoundary.Code, violation.RuleCode);
        Assert.Equal("Modules/Activities/ActivityService.cs", violation.File);
    }

    [Fact]
    public void Given_a_module_that_calls_the_alerts_service_directly_When_scanned_Then_SO_002_fires()
    {
        using var tree = SyntheticSourceTree.Create();

        tree.Write(
            "Modules/Alerts/IAlertService.cs",
            """
            namespace Demo.Modules.Alerts;
            public interface IAlertService { }
            """);

        tree.Write(
            "Modules/Activities/ActivityService.cs",
            """
            namespace Demo.Modules.Activities;
            using Demo.Modules.Alerts;
            public sealed class ActivityService
            {
                private readonly IAlertService _alerts;
                public ActivityService(IAlertService alerts) => _alerts = alerts;
            }
            """);

        var codes = tree.Scan().Violations.Select(v => v.RuleCode).ToList();

        Assert.Contains(ArchitectureRules.EventBusOnly.Code, codes);
    }

    [Fact]
    public void Given_a_service_that_throws_a_raw_exception_When_scanned_Then_SO_003_fires()
    {
        using var tree = SyntheticSourceTree.Create();

        tree.Write(
            "Modules/Activities/ActivityService.cs",
            """
            namespace Demo.Modules.Activities;
            public sealed class ActivityService
            {
                public void Complete(string id)
                {
                    throw new InvalidOperationException("nope");
                }
            }
            """);

        var violation = Assert.Single(tree.Scan().Violations);

        Assert.Equal(ArchitectureRules.ErrorContract.Code, violation.RuleCode);
        Assert.Contains("InvalidOperationException", violation.Message);
    }

    [Fact]
    public void Given_a_service_that_throws_an_AppError_subclass_When_scanned_Then_SO_003_does_not_fire()
    {
        using var tree = SyntheticSourceTree.Create();

        tree.Write(
            "Modules/Activities/ActivityService.cs",
            """
            namespace Demo.Modules.Activities;
            public sealed class ActivityService
            {
                public void Complete(string id)
                {
                    throw new NotFoundError("Activity", id);
                }
            }
            """);

        Assert.True(tree.Scan().Passed);
    }

    [Fact]
    public void Given_a_raw_throw_inside_a_comment_When_scanned_Then_SO_003_does_not_fire()
    {
        using var tree = SyntheticSourceTree.Create();

        tree.Write(
            "Modules/Activities/ActivityService.cs",
            """
            namespace Demo.Modules.Activities;
            public sealed class ActivityService
            {
                // Never write: throw new Exception("boom");
                /// <remarks>throw new Exception("boom") is banned.</remarks>
                public void Complete(string id)
                {
                }
            }
            """);

        Assert.True(tree.Scan().Passed);
    }

    [Fact]
    public void Given_a_controller_that_uses_a_repository_When_scanned_Then_SO_004_fires()
    {
        using var tree = SyntheticSourceTree.Create();

        tree.Write(
            "Modules/Activities/IActivityRepository.cs",
            """
            namespace Demo.Modules.Activities;
            public interface IActivityRepository { }
            """);

        tree.Write(
            "Modules/Activities/ActivitiesController.cs",
            """
            namespace Demo.Modules.Activities;
            public sealed class ActivitiesController
            {
                private readonly IActivityRepository _repository;
                public ActivitiesController(IActivityRepository repository) => _repository = repository;
            }
            """);

        var violation = Assert.Single(tree.Scan().Violations);

        Assert.Equal(ArchitectureRules.LayerSeparation.Code, violation.RuleCode);
    }

    [Fact]
    public void Given_a_repository_that_touches_http_When_scanned_Then_SO_004_fires()
    {
        using var tree = SyntheticSourceTree.Create();

        tree.Write(
            "Modules/Activities/InMemoryActivityRepository.cs",
            """
            namespace Demo.Modules.Activities;
            public sealed class InMemoryActivityRepository
            {
                public void Save(HttpContext context)
                {
                }
            }
            """);

        var violation = Assert.Single(tree.Scan().Violations);

        Assert.Equal(ArchitectureRules.LayerSeparation.Code, violation.RuleCode);
        Assert.Contains("HttpContext", violation.Message);
    }

    [Fact]
    public void Given_the_reports_module_writing_through_another_service_When_scanned_Then_SO_005_fires()
    {
        using var tree = SyntheticSourceTree.Create();

        tree.Write(
            "Modules/Activities/IActivityService.cs",
            """
            namespace Demo.Modules.Activities;
            public interface IActivityService { }
            """);

        tree.Write(
            "Modules/Reports/ReportService.cs",
            """
            namespace Demo.Modules.Reports;
            using Demo.Modules.Activities;
            public sealed class ReportService
            {
                private readonly IActivityService _activityService;
                public ReportService(IActivityService activityService) => _activityService = activityService;
                public void Recalculate(string id)
                {
                    _activityService.UpdateAsync(id);
                }
            }
            """);

        var codes = tree.Scan().Violations.Select(v => v.RuleCode).ToList();

        Assert.Contains(ArchitectureRules.ReadOnlyReports.Code, codes);
    }

    [Fact]
    public void Given_the_reports_module_reading_another_service_When_scanned_Then_SO_005_does_not_fire()
    {
        using var tree = SyntheticSourceTree.Create();

        tree.Write(
            "Modules/Activities/IActivityService.cs",
            """
            namespace Demo.Modules.Activities;
            public interface IActivityService { }
            """);

        tree.Write(
            "Modules/Reports/ReportService.cs",
            """
            namespace Demo.Modules.Reports;
            using Demo.Modules.Activities;
            public sealed class ReportService
            {
                private readonly IActivityService _activityService;
                public ReportService(IActivityService activityService) => _activityService = activityService;
                public void Recalculate(string id)
                {
                    _activityService.ListForStoreAsync(id);
                }
            }
            """);

        Assert.DoesNotContain(
            ArchitectureRules.ReadOnlyReports.Code,
            tree.Scan().Violations.Select(v => v.RuleCode));
    }

    [Fact]
    public void Given_shared_code_that_depends_on_a_module_When_scanned_Then_SO_006_fires()
    {
        using var tree = SyntheticSourceTree.Create();

        tree.Write(
            "Modules/Staff/StaffMember.cs",
            """
            namespace Demo.Modules.Staff;
            public sealed record StaffMember(string Id);
            """);

        tree.Write(
            "Shared/Auth/StaffContext.cs",
            """
            namespace Demo.Shared.Auth;
            using Demo.Modules.Staff;
            public sealed record StaffContext(StaffMember Member);
            """);

        var codes = tree.Scan().Violations.Select(v => v.RuleCode).ToList();

        Assert.Contains(ArchitectureRules.SharedIndependence.Code, codes);
    }

    [Fact]
    public void Given_two_modules_that_depend_on_each_other_When_scanned_Then_SO_007_fires()
    {
        using var tree = SyntheticSourceTree.Create();

        tree.Write(
            "Modules/Activities/IActivityService.cs",
            """
            namespace Demo.Modules.Activities;
            public interface IActivityService { }
            """);

        tree.Write(
            "Modules/Programmes/IProgrammeService.cs",
            """
            namespace Demo.Modules.Programmes;
            public interface IProgrammeService { }
            """);

        tree.Write(
            "Modules/Activities/ActivityService.cs",
            """
            namespace Demo.Modules.Activities;
            using Demo.Modules.Programmes;
            public sealed class ActivityService
            {
                private readonly IProgrammeService _programmes;
                public ActivityService(IProgrammeService programmes) => _programmes = programmes;
            }
            """);

        tree.Write(
            "Modules/Programmes/ProgrammeService.cs",
            """
            namespace Demo.Modules.Programmes;
            using Demo.Modules.Activities;
            public sealed class ProgrammeService
            {
                private readonly IActivityService _activities;
                public ProgrammeService(IActivityService activities) => _activities = activities;
            }
            """);

        var cycle = Assert.Single(
            tree.Scan().Violations,
            v => v.RuleCode == ArchitectureRules.NoModuleCycles.Code);

        Assert.Contains("activities", cycle.Message);
        Assert.Contains("programmes", cycle.Message);
    }

    [Fact]
    public void Given_the_rule_catalogue_When_it_is_read_Then_every_code_is_unique()
    {
        var codes = ArchitectureRules.All.Select(r => r.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ArchitectureRules.All, rule =>
        {
            Assert.StartsWith("SO-", rule.Code, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(rule.Title));
            Assert.False(string.IsNullOrWhiteSpace(rule.Rationale));
        });
    }
}
