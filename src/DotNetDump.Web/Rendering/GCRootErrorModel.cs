namespace DotNetDump.Web.Rendering;

/// <summary>
/// What <c>GcRootError.cshtml</c> binds to when <c>HeapAnalyzer.GetGCRoots</c> itself throws --
/// distinct from <see cref="GCRootTreeModel"/>'s four <c>GCRootOutcome</c> states, which describe
/// what a *completed* search concluded. A search that never completed at all (a corrupt object
/// somewhere along a candidate path breaking ClrMD's reference walk, the same class of failure
/// <c>ObjectReferenceTreeBuilder.Unreadable</c> exists to survive on the object tree) is not a fifth
/// outcome; conflating the two would blur exactly the "no paths found" vs. "gave up looking"
/// distinction docs/GCROOT_TRUNCATION.md exists to keep apart.
/// </summary>
/// <param name="TargetAddress">The object the search was asked about.</param>
/// <param name="Reason">The exception message, shown verbatim -- the same "surface the real failure
/// rather than a generic message" choice <c>ObjectReferenceTreeBuilder.Unreadable</c> made.</param>
public sealed record GCRootErrorModel(ulong TargetAddress, string Reason);