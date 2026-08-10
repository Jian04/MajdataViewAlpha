# Maicaiyin engine for MajdataEdit

This directory contains the automatic onset engine from:

https://github.com/Jian04/Maicaiyin

MajdataEdit packages the trained joint-placement model as a 2.1 MB NumPy weight
archive and runs it through a small CPU-only evaluator. PyTorch, ONNX Runtime,
and CUDA are not runtime dependencies. The NumPy output was compared against
the source PyTorch model at revision
`f6bfd1f5104cc05b9966690eff7a1655007a6b93`; the maximum absolute test error was
`2.86102295e-06`.

MajdataEdit runs directly from the prebuilt `packages` directory shipped with
the application. It never invokes pip or accesses a package index at runtime.
It uses the bundled Python 3.12 executable only and rejects system Python and
system packages. The dependency manifest pins NumPy to the compatible 2.3.x
line, and the environment validator rejects any other package versions.

The engine predicts onset timing only. Generated 1–8 lane numbers are rotating
preview placeholders and still require manual charting.
