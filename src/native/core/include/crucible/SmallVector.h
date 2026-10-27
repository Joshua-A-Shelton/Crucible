#pragma once

// A Vector with room for InlineCount elements inside the object: no
// allocation until it outgrows them (ADR 0032). For short lists built per
// call. The name is a placeholder (docs/naming.md).

#include <crucible/Vector.h>

#include <cstddef>

namespace crucible {

template <class T, size_t InlineCount>
using SmallVector = detail::VectorImpl<T, InlineCount>;

} // namespace crucible
