// shaderfax's stand-in for the engine's embedded shaders (ADR 0048): it links
// the core's objects but never renders, and the real table is generated from
// what it compiles.

#include <crucible/Shader.h>

namespace crucible::render {

extern const EmbeddedShader* const embeddedShaders = nullptr;
extern const size_t embeddedShaderCount = 0;

} // namespace crucible::render
