using DotNetDump.Core.Models;

namespace DotNetDump.Core.Trees;

/// <summary>
/// One thread (or one idle-worker group -- see <see cref="ThreadFramesTreeBuilder"/>'s own doc
/// comment) paired with its already-known frame children. <see cref="TreeNode"/> itself carries no
/// child collection -- deliberately, since three of Phase 5's four trees fetch children lazily and
/// would have nowhere to put an eagerly-populated list that then sits unused until an expand request
/// arrives -- so pairing a root with its children is this tree's own concern, not something bolted
/// onto the shared wire type.
/// </summary>
public sealed record ThreadTreeEntry(TreeNode Root, IReadOnlyList<TreeNode> Frames);

/// <summary>
/// Builds DATA_CONTRACT.md &#0167;4.4's thread&#8594;frames tree over two already-fetched analyzer
/// results -- <see cref="Analyzers.ThreadAnalyzer.GetThreads"/> for the roots,
/// <see cref="Analyzers.ThreadAnalyzer.GetDetailedStacks"/> for their frames. Unlike
/// <see cref="NamespaceRollupBuilder"/>, nothing here is lazy: &#0167;4.4 calls this shape "fully
/// computed up front", the same phrase &#0167;4.3 uses for gcroot, so <c>TreeRoutes.cs</c> calls
/// <see cref="BuildRoots"/> exactly once per request and its own top-level fragment renders the
/// whole two-level result directly over <c>_TreeRow.cshtml</c>, never through
/// <c>_TreeNodes.cshtml</c>'s lazy-expand machinery (see that partial's own doc comment).
/// </summary>
/// <remarks>
/// <para>
/// <b>Idle-worker grouping, the judgment call DATA_CONTRACT.md left to this task.</b> Many idle
/// threadpool workers carry byte-identical stacks, and a root node per thread is noise a reader has
/// to scroll past to find the threads that actually differ. A thread is a <i>grouping candidate</i>
/// when it is alive, carries no in-flight exception, and <see cref="Analyzers.ThreadAnalyzer.GetDetailedStacks"/>
/// returned at least one frame for it; candidates are bucketed by the exact sequence of their
/// frames' <c>{ModuleName}!{MethodName}</c> labels -- the same composition
/// <c>DumpStack.cshtml</c>/<c>Display.StackFrameMethod</c> already use for a single thread's stack,
/// so the label a user sees under a group matches what expanding one of its members individually
/// would have shown. A bucket with two or more members collapses into one root node; a bucket of one
/// is indistinguishable from an ordinary thread and stays its own node -- turning a stack that
/// happens to be unique into a "group of one" would just rename it for no reader benefit. Threads
/// with an exception in flight are deliberately excluded from grouping even when their stack matches
/// others': an exception is exactly the kind of thing that must never hide inside a count badge,
/// especially if some idle worker happens to share its stack shape. A thread with no captured stack
/// at all (see below) is never a candidate either, since there is nothing to compare.
/// </para>
/// <para>
/// The heuristic that labels a group "ThreadPool worker" rather than the generic "identical stack"
/// is a case-insensitive text match for "ThreadPool" across the shared stack's own frame labels --
/// cheap, uses only data the two source calls already returned, and degrades honestly when it does
/// not fire. It will not fire on this codebase's own real fixture dump: the Phase 3 "carried
/// forward" finding in IMPLEMENTATION_PLAN.md records that <c>GetDetailedStacks</c> returns exactly
/// one frame per thread there, every one identical and <c>[(unknown)]</c> -- which this builder
/// still groups (every eligible thread shares the same one-frame signature), just under the generic
/// label, because there is no resolvable method name to match "ThreadPool" against. That is the
/// correct behavior per this task's brief: render whatever the analyzer actually returns, not paper
/// over it.
/// </para>
/// <para>
/// <b>Locals are not attempted.</b> DATA_CONTRACT.md &#0167;4.4 specs them as an optional third level
/// "gated on a measurement against a real dump". Reflecting <c>ClrStackFrame</c>, <c>ClrMethod</c>
/// and <c>ClrThread</c> in the exact ClrMD package version this project references
/// (<c>Microsoft.Diagnostics.Runtime</c> 4.0.732401) turned up no locals-by-name accessor at all --
/// only IL metadata plumbing (<c>ClrMethod.GetILInfo</c>, <c>GetILOffset</c>, which describe the
/// method's IL blob, not variable values) and <c>ClrThread.EnumerateStackRoots</c>, which does
/// return live objects found on the stack tagged with the frame they were found in -- but with no
/// variable name, only a register/offset. That is a materially different, weaker capability than
/// "named locals" and building UX around it is its own undertaking outside this task's scope, not a
/// small addition once the measurement is in hand. The tree stays two levels, which &#0167;4.4 itself
/// says is a complete feature.
/// </para>
/// <para>
/// <see cref="TreeNode.Id"/> values here are never parsed back into a request -- this tree has no
/// lazy-expand route for them to address (see above) -- so each id only needs to be a stable,
/// readable string for markup and tests; it is not a wire contract the way
/// <see cref="NamespaceRollupBuilder"/>'s ids are.
/// </para>
/// </remarks>
public static class ThreadFramesTreeBuilder {
	// U+001F (Unit Separator), never a legal character in a resolved method/module name -- so two
	// distinct frame sequences cannot collide into the same signature the way an empty or printable
	// separator could (e.g. frames ["AB", "C"] and ["A", "BC"] joined with "" both read "ABC").
	private const string FrameSeparator = "";

	/// <summary>The whole tree, roots in a stable order (ascending by the lowest managed thread id
	/// each root represents -- a group takes the position of its lowest member).</summary>
	public static IReadOnlyList<ThreadTreeEntry> BuildRoots(IReadOnlyList<ThreadInfo> threads, IReadOnlyList<ThreadStackInfo> stacks) {
		var stacksByThread = new Dictionary<int, ThreadStackInfo>();
		foreach (var stack in stacks) {
			stacksByThread[stack.ManagedThreadId] = stack;
		}

		var candidates = new Dictionary<string, List<(ThreadInfo Thread, ThreadStackInfo Stack)>>(StringComparer.Ordinal);
		var standalone = new List<ThreadInfo>();

		foreach (var thread in threads) {
			if (thread.IsAlive && thread.ExceptionType is null
				&& stacksByThread.TryGetValue(thread.ManagedThreadId, out var stack) && stack.Frames.Count > 0) {
				string key = Signature(stack.Frames);
				if (!candidates.TryGetValue(key, out var bucket)) {
					bucket = [];
					candidates[key] = bucket;
				}
				bucket.Add((thread, stack));
			} else {
				standalone.Add(thread);
			}
		}

		var entries = new List<(int SortKey, ThreadTreeEntry Entry)>();

		foreach (var bucket in candidates.Values) {
			if (bucket.Count >= 2) {
				int minId = bucket.Min(b => b.Thread.ManagedThreadId);
				entries.Add((minId, BuildGroupEntry(bucket)));
			} else {
				var (thread, stack) = bucket[0];
				entries.Add((thread.ManagedThreadId, BuildThreadEntry(thread, stack)));
			}
		}

		foreach (var thread in standalone) {
			stacksByThread.TryGetValue(thread.ManagedThreadId, out var stack);
			entries.Add((thread.ManagedThreadId, BuildThreadEntry(thread, stack)));
		}

		return entries.OrderBy(e => e.SortKey).Select(e => e.Entry).ToList();
	}

	private static ThreadTreeEntry BuildThreadEntry(ThreadInfo thread, ThreadStackInfo? stack) {
		IReadOnlyList<StackFrameInfo> frames = stack is null ? Array.Empty<StackFrameInfo>() : stack.Frames;
		string id = $"thread-{thread.ManagedThreadId}";
		var root = new TreeNode {
			Id = id,
			Label = $"Thread {thread.ManagedThreadId}",
			Detail = FrameCountDetail(frames.Count),
			Kind = TreeNodeKind.Thread,
			HasChildren = frames.Count > 0,
			ChildCount = frames.Count > 0 ? frames.Count : null,
			Badges = ThreadBadges(thread),
		};
		return new ThreadTreeEntry(root, BuildFrameNodes(id, frames));
	}

	private static ThreadTreeEntry BuildGroupEntry(List<(ThreadInfo Thread, ThreadStackInfo Stack)> bucket) {
		var shared = bucket[0].Stack.Frames;
		bool looksLikeThreadPool = shared.Any(f =>
			(f.MethodName?.Contains("ThreadPool", StringComparison.OrdinalIgnoreCase) ?? false) ||
			(f.ModuleName?.Contains("ThreadPool", StringComparison.OrdinalIgnoreCase) ?? false));

		int minId = bucket.Min(b => b.Thread.ManagedThreadId);
		string id = $"thread-group-{minId}";
		var root = new TreeNode {
			Id = id,
			Label = $"{bucket.Count:N0} threads ({(looksLikeThreadPool ? "ThreadPool worker" : "identical stack")})",
			Detail = FrameCountDetail(shared.Count),
			Kind = TreeNodeKind.Thread,
			HasChildren = shared.Count > 0,
			ChildCount = shared.Count > 0 ? shared.Count : null,
			Badges = [new TreeBadge($"{bucket.Count:N0} threads", TreeBadgeTone.Info)],
		};
		return new ThreadTreeEntry(root, BuildFrameNodes(id, shared));
	}

	private static IReadOnlyList<TreeBadge> ThreadBadges(ThreadInfo thread) {
		var badges = new List<TreeBadge> {
			new($"OS {thread.OSThreadId:X}", TreeBadgeTone.Neutral),
			new(thread.IsAlive ? "Alive" : "Dead", thread.IsAlive ? TreeBadgeTone.Neutral : TreeBadgeTone.Warn),
		};
		if (thread.ExceptionType is not null) {
			badges.Add(new TreeBadge(thread.ExceptionType, TreeBadgeTone.Danger));
		}
		return badges;
	}

	private static string? FrameCountDetail(int count) =>
		count > 0 ? $"{count:N0} frame{(count == 1 ? "" : "s")}" : null;

	private static List<TreeNode> BuildFrameNodes(string parentId, IReadOnlyList<StackFrameInfo> frames) {
		var nodes = new List<TreeNode>(frames.Count);
		for (int i = 0; i < frames.Count; i++) {
			nodes.Add(new TreeNode {
				Id = $"{parentId}/frame-{i}",
				Label = FrameLabel(frames[i]),
				Detail = frames[i].FrameKind,
				Kind = TreeNodeKind.Frame,
				HasChildren = false,
			});
		}
		return nodes;
	}

	private static string FrameLabel(StackFrameInfo frame) {
		string method = frame.MethodName ?? "(unknown)";
		return string.IsNullOrEmpty(frame.ModuleName) ? method : $"{frame.ModuleName}!{method}";
	}

	private static string Signature(IReadOnlyList<StackFrameInfo> frames) =>
		string.Join(FrameSeparator, frames.Select(FrameLabel));
}