# Vendored client assets

No CDN reference exists anywhere in this project, and none may be added. `dndump serve` reads memory
dumps whose heap strings contain connection strings, tokens and PII; a page that makes any outbound
request is a page that can carry them out (`docs/web/SERVER.md` §6).

| File | Version | Source | Integrity |
| :--- | :--- | :--- | :--- |
| `htmx.min.js` | 2.0.9 | `https://raw.githubusercontent.com/bigskysoftware/htmx/v2.0.9/dist/htmx.min.js` | `sha384-ESlCao+z/oasnu2Uc/5K1LQTI7YCF2KKO4xakCPQCFuiHhCh8Oa/R5NwHY6guZ3m` |

The hash is duplicated in `Rendering/Assets.cs`, which is what the layout emits as the `integrity`
attribute. `HtmxIntegrityTests` recomputes it from the embedded file on every test run, so the two
cannot drift.

## Updating

```bash
curl -sfL "https://raw.githubusercontent.com/bigskysoftware/htmx/vX.Y.Z/dist/htmx.min.js" \
  -o src/DotNetDump.Web/wwwroot/lib/htmx.min.js
openssl dgst -sha384 -binary src/DotNetDump.Web/wwwroot/lib/htmx.min.js | openssl base64 -A
```

Then update `Assets.HtmxVersion`, `Assets.HtmxIntegrity` and the table above. The test will fail
until the constant matches the file.
