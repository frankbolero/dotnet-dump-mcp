using Microsoft.AspNetCore.Http;

namespace DotNetDump.Web.Security;

/// <summary>
/// Rejects any request whose <c>Host</c> header is not a loopback name on the port this server is
/// actually listening on.
/// </summary>
/// <remarks>
/// <para>
/// Binding to <c>127.0.0.1</c> is necessary and not sufficient. Any page on the internet can point a
/// hostname it controls at <c>127.0.0.1</c> and have the user's own browser issue same-origin
/// requests to this server — DNS rebinding. The connection genuinely originates from loopback, so
/// the binding does not stop it; the <c>Host</c> header is what still carries the attacker's name,
/// and checking it is what stops it. Dumps render connection strings, tokens and PII, so this is
/// the control that matters most in SERVER.md &#0167;6.
/// </para>
/// <para>
/// The port is compared against <see cref="ConnectionInfo.LocalPort"/> rather than against
/// configuration. That is the port the request actually arrived on, so it stays correct for
/// <c>--port 0</c> without anything having to plumb the resolved port back here — and it cannot
/// drift out of agreement with the real binding.
/// </para>
/// <para>
/// This check is address-independent by construction, which is exactly what lets it keep working
/// unchanged under <see cref="DotNetDump.Web.DumpWebHostOptions.BindAnyInterface"/> (the Docker
/// case): it never
/// inspects <see cref="ConnectionInfo.RemoteIpAddress"/>, only the <c>Host</c> header and the local
/// port. Do not "harden" this into a remote-IP-must-be-loopback check — under Docker's NAT the real
/// remote address is the bridge gateway, not loopback, and such a check would simply break the
/// container case this control is meant to keep protecting.
/// </para>
/// </remarks>
public sealed class LoopbackHostMiddleware(RequestDelegate next) {
	/// <summary>
	/// The only names a loopback server may be addressed by. Everything else — including a hostname
	/// that resolves to 127.0.0.1 — is refused.
	/// </summary>
	private static readonly string[] LoopbackNames = ["localhost", "127.0.0.1", "[::1]", "::1"];

	public async Task InvokeAsync(HttpContext context) {
		if (!IsLoopbackHost(context)) {
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			context.Response.ContentType = "text/plain; charset=utf-8";
			// Fixed text: the rejected Host value is not echoed back. Reflecting attacker-controlled
			// input into a response is how a refusal turns into an injection point.
			await context.Response.WriteAsync(
				"Refused: dndump serve accepts requests addressed to localhost only.");
			return;
		}

		await next(context);
	}

	/// <summary>Exposed for tests, which assert the decision directly as well as over the wire.</summary>
	public static bool IsLoopbackHost(HttpContext context) {
		var host = context.Request.Host;

		// HTTP/1.1 requires Host. A request without one is malformed, and a malformed request is not
		// a reason to relax the check.
		if (!host.HasValue || string.IsNullOrEmpty(host.Host)) {
			return false;
		}

		if (!LoopbackNames.Contains(host.Host, StringComparer.OrdinalIgnoreCase)) {
			return false;
		}

		// An absent port means the scheme default, which for the http-only binding here is 80.
		int claimedPort = host.Port ?? 80;
		return claimedPort == context.Connection.LocalPort;
	}
}