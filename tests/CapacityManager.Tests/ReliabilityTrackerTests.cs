using CapacityManager.Api.Services;
using Xunit;

namespace CapacityManager.Tests;

public class ReliabilityTrackerTests
{
    [Fact]
    public void GetSloReport_ReturnsFullUptime_WhenNoIncidentsRecorded()
    {
        var tracker = new ReliabilityTracker();

        var report = tracker.GetSloReport();

        Assert.Equal(0, report.IncidentCount);
        Assert.Equal(100, report.ObservedUptimePercent);
    }

    [Fact]
    public void RecordEvent_IncreasesIncidentCount_ForNodeOfflineEvents()
    {
        var tracker = new ReliabilityTracker();

        tracker.RecordEvent(ReliabilityEventType.NodeOffline, "node-1 went offline");
        tracker.RecordEvent(ReliabilityEventType.RebalanceMigration, "moved tenant-1");

        var report = tracker.GetSloReport();

        Assert.Equal(1, report.IncidentCount);
    }

    [Fact]
    public void RecentEvents_ReturnsMostRecentFirst()
    {
        var tracker = new ReliabilityTracker();

        tracker.RecordEvent(ReliabilityEventType.NodeOffline, "first");
        tracker.RecordEvent(ReliabilityEventType.NodeRecovered, "second");

        var events = tracker.RecentEvents();

        Assert.Equal("second", events.First().Message);
    }
}
