# CoreCLR compatibility source

This branch keeps MonoMod itself at upstream commit
`8fea48428622be858dd4f48b20ba1bf96ba09894`. Its only source dependency change
is the `MonoMod.Common` submodule revision documented in
[`MonoMod.Common/PATCHES.md`](MonoMod.Common/PATCHES.md).

Consumers must initialize the submodule and build from source. Do not reproduce
the compatibility fix as a post-build change to `MonoMod.Utils.dll`.
