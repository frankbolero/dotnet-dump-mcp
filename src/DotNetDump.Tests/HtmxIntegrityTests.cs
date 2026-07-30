using System.Security.Cryptography;

using DotNetDump.Web.Rendering;

namespace DotNetDump.Tests;

/// <summary>
/// Tests that verify the subresource-integrity hash of the vendored htmx library matches what is
/// committed in <see cref="Assets.HtmxIntegrity"/>. The browser silently refuses to load a script
/// if the SRI hash does not match, disabling every interaction on the page. These tests prevent
/// accidental swaps or truncation of the vendored file from going undetected.
/// </summary>
public class HtmxIntegrityTests {
	/// <summary>Relative path from AppContext.BaseDirectory to the vendored htmx file.</summary>
	private static string HtmxFilePath => Path.Combine(AppContext.BaseDirectory, "wwwroot", "lib", "htmx.min.js");

	/// <summary>
	/// Verifies that the vendored htmx.min.js file exists where the test can reach it.
	/// The build copies wwwroot/** to the output directory via CopyToOutputDirectory,
	/// so the file must exist here or the entire deployment is broken.
	/// </summary>
	[Fact]
	public void FileExists() {
		Assert.True(
			File.Exists(HtmxFilePath),
			$"htmx.min.js not found at {HtmxFilePath}. " +
			$"The DotNetDump.Web project must copy wwwroot/** to the output directory via CopyToOutputDirectory. " +
			$"Check that DotNetDump.Web.csproj has the Content Update metadata and that the build completed successfully."
		);
	}

	/// <summary>
	/// Verifies that <see cref="Assets.HtmxIntegrity"/> is well-formed:
	/// starts with "sha384-" and the remainder is valid base64 decoding to 48 bytes
	/// (the SHA-384 hash is 384 bits = 48 bytes).
	/// </summary>
	[Fact]
	public void IntegrityConstantIsWellFormed() {
		const string expectedPrefix = "sha384-";
		Assert.True(
			Assets.HtmxIntegrity.StartsWith(expectedPrefix),
			$"Assets.HtmxIntegrity must start with '{expectedPrefix}'. Found: {Assets.HtmxIntegrity}"
		);

		string base64Part = Assets.HtmxIntegrity[expectedPrefix.Length..];
		byte[] decoded;
		try {
			decoded = Convert.FromBase64String(base64Part);
		} catch (FormatException ex) {
			Assert.Fail(
				$"Assets.HtmxIntegrity base64 part is malformed: {ex.Message}"
			);
			return; // Unreachable, but required by compiler
		}

		const int sha384ByteLength = 48; // 384 bits / 8
		Assert.Equal(
			sha384ByteLength,
			decoded.Length
		);
	}

	/// <summary>
	/// Recomputes the SHA-384 hash of the vendored htmx.min.js file and verifies
	/// it matches <see cref="Assets.HtmxIntegrity"/>. The hash is computed as
	/// "sha384-" + Convert.ToBase64String(SHA384.HashData(bytes)), matching how
	/// openssl dgst -sha384 -binary | openssl base64 -A produced the committed value.
	/// </summary>
	[Fact]
	public void FileHashMatchesIntegrityConstant() {
		byte[] fileBytes = File.ReadAllBytes(HtmxFilePath);
		byte[] hash = SHA384.HashData(fileBytes);
		string computedIntegrity = "sha384-" + Convert.ToBase64String(hash);

		// The hash must match exactly. If it differs, the file has been swapped, truncated, or corrupted.
		// See src/DotNetDump.Web/VENDORING.md for the update procedure.
		Assert.Equal(Assets.HtmxIntegrity, computedIntegrity);
	}

	/// <summary>
	/// Verifies that the vendored htmx.min.js file contains no HTTP or HTTPS URLs.
	/// The tool must make no outbound request of any kind (SERVER.md §1.1, §6);
	/// htmx 2.0.9 ships from a CDN by default, so the vendored version is committed
	/// and version-pinned. This test prevents accidental use of a remotely-hosted version.
	/// </summary>
	[Fact]
	public void FileContainsNoRemoteUrls() {
		string fileContent = File.ReadAllText(HtmxFilePath);

		// htmx 2.0.9 minified does not contain any comments or documentation strings
		// with legitimate HTTP(S) URLs, so a simple scan is sufficient.
		Assert.False(
			fileContent.Contains("http://"),
			"htmx.min.js contains 'http://' URLs, indicating it may be trying to fetch from a remote CDN"
		);
		Assert.False(
			fileContent.Contains("https://"),
			"htmx.min.js contains 'https://' URLs, indicating it may be trying to fetch from a remote CDN"
		);
	}
}