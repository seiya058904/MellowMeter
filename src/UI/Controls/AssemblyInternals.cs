using System.Runtime.CompilerServices;

// The regression and measurement harnesses drive the settings controls' action paths directly.
[assembly: InternalsVisibleTo("DesktopSmoke")]
[assembly: InternalsVisibleTo("PerformanceProbe")]
