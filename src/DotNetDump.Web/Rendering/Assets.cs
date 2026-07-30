namespace DotNetDump.Web.Rendering;

/// <summary>
/// The vendored client assets and their provenance.
/// </summary>
/// <remarks>
/// htmx ships from a CDN by default. This tool reads memory dumps containing connection strings,
/// tokens and PII, and it must make no outbound request of any kind — so the file is committed,
/// version-pinned and served from this assembly (SERVER.md &#0167;1.1, &#0167;6). The
/// <see cref="HtmxIntegrity"/> hash is not defence against a hostile CDN there is no request to;
/// it is what makes a swapped or truncated vendored file fail loudly in the browser instead of
/// silently disabling every interaction on the page. <c>HtmxIntegrityTests</c> recomputes it from
/// the embedded file so the constant cannot fall out of step with what is actually served.
/// </remarks>
public static class Assets {
	public const string HtmxVersion = "2.0.9";

	/// <summary>Path under <c>wwwroot</c>, and the URL it is served at.</summary>
	public const string HtmxPath = "/lib/htmx.min.js";

	/// <summary>
	/// The one stylesheet: the Nocturne tokens extracted from the design sync, plus the component
	/// layer. No integrity hash — unlike htmx this file is ours and changes with the design, so a
	/// pinned hash would be a build step that fails on every visual edit rather than a safeguard.
	/// </summary>
	public const string StylesheetPath = "/css/dndump.css";

	/// <summary>Subresource-integrity value for <see cref="HtmxPath"/>: <c>sha384-</c> + base64.</summary>
	public const string HtmxIntegrity = "sha384-ESlCao+z/oasnu2Uc/5K1LQTI7YCF2KKO4xakCPQCFuiHhCh8Oa/R5NwHY6guZ3m";
}