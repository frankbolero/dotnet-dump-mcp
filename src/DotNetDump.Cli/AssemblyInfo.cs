using System.Runtime.CompilerServices;

// Lets DotNetDump.Tests exercise the parsing/resolution/exit-code plumbing directly (CLI_DESIGN.md
// §7: "parser tests -- option binding, address forms, precedence... exit-code assertions") without
// making implementation details like ExitCodeMapper or EffectiveLimit part of the tool's public
// surface.
[assembly: InternalsVisibleTo("DotNetDump.Tests")]