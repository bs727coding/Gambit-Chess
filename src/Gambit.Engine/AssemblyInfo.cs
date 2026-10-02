// Search buffers are always written before being read; skipping zero-init of stackalloc spans is a measurable speedup.
[module: System.Runtime.CompilerServices.SkipLocalsInit]
