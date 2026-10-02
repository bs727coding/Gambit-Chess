// Move-generation buffers are always written before being read; skipping zero-init of stackalloc spans speeds up perft/search.
[module: System.Runtime.CompilerServices.SkipLocalsInit]
