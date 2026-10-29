#include "Common.h"

#include <crucible/importers/Texture.h>

#include <cstdarg>
#include <cstdio>
#include <cstring>

namespace crucible::importers {

namespace {

thread_local char lastError[4096]; // Slang's diagnostics can run long

} // namespace

Status fail(const char* format, ...)
{
    va_list arguments;
    va_start(arguments, format);
    std::vsnprintf(lastError, sizeof(lastError), format, arguments);
    va_end(arguments);
    return Status::InvalidData;
}

void clearError()
{
    lastError[0] = '\0';
}

std::string_view lastImportError()
{
    return lastError;
}

uint16_t toHalf(float value)
{
    uint32_t bits;
    std::memcpy(&bits, &value, 4);
    const uint32_t sign = (bits >> 16) & 0x8000u;
    const int32_t exponent = static_cast<int32_t>((bits >> 23) & 0xFF) - 127 + 15;
    uint32_t mantissa = bits & 0x7FFFFFu;
    if (((bits >> 23) & 0xFF) == 0xFF)
    {
        return static_cast<uint16_t>(sign | 0x7C00u | (mantissa != 0 ? 0x200u : 0u)); // inf or NaN
    }
    if (exponent >= 31)
    {
        return static_cast<uint16_t>(sign | 0x7C00u);
    }
    if (exponent <= 0)
    {
        if (exponent < -10)
        {
            return static_cast<uint16_t>(sign);
        }
        mantissa |= 0x800000u;
        const uint32_t shift = static_cast<uint32_t>(14 - exponent);
        uint32_t half = mantissa >> shift;
        const uint32_t remainder = mantissa & ((1u << shift) - 1);
        const uint32_t halfway = 1u << (shift - 1);
        if (remainder > halfway || (remainder == halfway && (half & 1u) != 0))
        {
            ++half;
        }
        return static_cast<uint16_t>(sign | half);
    }
    uint32_t half = (static_cast<uint32_t>(exponent) << 10) | (mantissa >> 13);
    const uint32_t remainder = mantissa & 0x1FFFu;
    if (remainder > 0x1000u || (remainder == 0x1000u && (half & 1u) != 0))
    {
        ++half; // may carry into the exponent, which is right
    }
    return static_cast<uint16_t>(sign | half);
}

} // namespace crucible::importers
