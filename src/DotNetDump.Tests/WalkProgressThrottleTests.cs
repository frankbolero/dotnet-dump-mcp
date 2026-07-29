using DotNetDump.Core.Models;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Tests;

public class WalkProgressThrottleTests {
	private sealed class RecordingProgress : IProgress<WalkProgress> {
		public List<WalkProgress> Reports { get; } = new();
		public void Report(WalkProgress value) => Reports.Add(value);
	}

	[Fact]
	public void Record_NullSink_NeverReports() {
		var throttle = new WalkProgressThrottle(progress: null, totalBytes: 1_000, intervalObjects: 1);

		for (int i = 0; i < 100; i++)
			throttle.Record(objectSize: 10);

		throttle.ReportFinal();

		// No sink to assert against -- the point of this test is that Record/ReportFinal never throw
		// and never touch a progress instance when none was supplied.
	}

	[Fact]
	public void Record_ReportsOnlyEveryIntervalObjects() {
		var sink = new RecordingProgress();
		var throttle = new WalkProgressThrottle(sink, totalBytes: 1_000, intervalObjects: 10);

		for (int i = 0; i < 25; i++)
			throttle.Record(objectSize: 1);

		// 25 objects at an interval of 10 report on object 10 and object 20 -- not on 25 (that is
		// ReportFinal's job) and not once per object.
		Assert.Equal(2, sink.Reports.Count);
		Assert.Equal(10, sink.Reports[0].ObjectsWalked);
		Assert.Equal(20, sink.Reports[1].ObjectsWalked);
	}

	[Fact]
	public void Record_AccumulatesBytesSeen() {
		var sink = new RecordingProgress();
		var throttle = new WalkProgressThrottle(sink, totalBytes: 1_000, intervalObjects: 5);

		for (int i = 0; i < 5; i++)
			throttle.Record(objectSize: 24);

		Assert.Single(sink.Reports);
		Assert.Equal(120, sink.Reports[0].BytesSeen);
		Assert.Equal(5, sink.Reports[0].ObjectsWalked);
	}

	[Fact]
	public void Record_CarriesTotalBytesUnchangedOnEveryReport() {
		var sink = new RecordingProgress();
		var throttle = new WalkProgressThrottle(sink, totalBytes: 999_999, intervalObjects: 1);

		throttle.Record(objectSize: 10);
		throttle.Record(objectSize: 20);

		Assert.All(sink.Reports, r => Assert.Equal(999_999, r.TotalBytes));
	}

	[Fact]
	public void ReportFinal_EmitsTrueCountsEvenOffIntervalBoundary() {
		var sink = new RecordingProgress();
		var throttle = new WalkProgressThrottle(sink, totalBytes: 1_000, intervalObjects: 10);

		for (int i = 0; i < 7; i++)
			throttle.Record(objectSize: 3);

		// 7 objects never crosses an interval-of-10 boundary, so Record alone reports nothing.
		Assert.Empty(sink.Reports);

		throttle.ReportFinal();

		Assert.Single(sink.Reports);
		Assert.Equal(7, sink.Reports[0].ObjectsWalked);
		Assert.Equal(21, sink.Reports[0].BytesSeen);
	}

	[Fact]
	public void ReportFinal_NullSink_DoesNotThrow() {
		var throttle = new WalkProgressThrottle(progress: null, totalBytes: 1_000);

		throttle.ReportFinal();
	}

	[Fact]
	public void NonPositiveInterval_FallsBackToDefault() {
		var sink = new RecordingProgress();
		var throttle = new WalkProgressThrottle(sink, totalBytes: 1_000, intervalObjects: 0);

		for (int i = 0; i < WalkProgressThrottle.DefaultReportIntervalObjects; i++)
			throttle.Record(objectSize: 1);

		Assert.Single(sink.Reports);
	}
}