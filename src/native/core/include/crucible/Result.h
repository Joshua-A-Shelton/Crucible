#pragma once

// Result<T> and Status (ADR 0030), modeled on slag::Result.

#include <crucible/Expose.h>

#include <cstdint>

namespace crucible {

// Error names are placeholders until settled (docs/naming.md). Exposed so C#
// sees the same values (ADR 0017).
enum class CRUCIBLE_EXPOSE Status : int32_t
{
    // Success: the value is valid.
    Ok = 0,

    // Errors: no value.
    NotFound = -1,
    InvalidData = -2,
    OutOfHostMemory = -3,
    Unsupported = -4,
    DeviceLost = -5,        // the GPU device stopped working; rendering can't continue
    OutOfDeviceMemory = -6, // GPU memory ran out
    IoError = -7,           // reading or writing a file failed for a reason other than NotFound
};

// [[nodiscard]]: ignoring a result warns; write (void) to ignore one on purpose.
template <class T>
class [[nodiscard]] Result
{
public:
    Result(T value)
        : code(Status::Ok)
        , payload(value)
    {}

    Result(Status status)
        : code(status)
        , payload()
    {}

    explicit operator bool() const
    {
        return static_cast<int32_t>(code) >= 0;
    }

    Status status() const
    {
        return code;
    }

    // Only valid when the result converts to true.
    T& value()
    {
        return payload;
    }

    const T& value() const
    {
        return payload;
    }

private:
    Status code;
    T payload;
};

template <>
class [[nodiscard]] Result<void>
{
public:
    Result()
        : code(Status::Ok)
    {}

    Result(Status status)
        : code(status)
    {}

    explicit operator bool() const
    {
        return static_cast<int32_t>(code) >= 0;
    }

    Status status() const
    {
        return code;
    }

private:
    Status code;
};

} // namespace crucible
