#ifndef CRUCIBLE_BINDINGFLAGS_H
#define CRUCIBLE_BINDINGFLAGS_H
#include <cstdint>

#include "crucible/CrucibleCore.h"

namespace crucible::scripting
{
    CRUCIBLE_API enum class BindingFlags: int32_t
    {
        DEFAULT = 0,
        IGNORE_CASE = 1,
        DECLARED_ONLY = 2,
        INSTANCE = 4,
        STATIC = 8,
        PUBLIC = 16,
        NON_PUBLIC = 32,
        FLATTEN_HIERARCHY = 64,
        INVOKE_METHOD = 256,
        CREATE_INSTANCE = 512,
        GET_FIELD = 1024,
        SET_FIELD = 2048,
        GET_PROPERTY = 4096,
        SET_PROPERTY = 8192,
        PUT_DISP_PROPERTY = 16384,
        PUT_REF_DISP_PROPERTY = 32768,
        EXACT_BINDING = 65536,
        SUPRESS_CHANGE_TYPE = 131072,
        OPTIONAL_PARAM_BINDING = 262144,
        IGNORE_RETURN = 16777216,
        DO_NOT_WRAP_EXCEPTIONS = 33554432
    };

    inline BindingFlags operator|(BindingFlags lhs, BindingFlags rhs)
    {
        return static_cast<BindingFlags>(static_cast<int32_t>(lhs) | static_cast<int32_t>(rhs));
    }

    inline BindingFlags operator&(BindingFlags lhs, BindingFlags rhs)
    {
        return static_cast<BindingFlags>(static_cast<int32_t>(lhs) & static_cast<int32_t>(rhs));
    }

    inline BindingFlags operator^(BindingFlags lhs, BindingFlags rhs)
    {
        return static_cast<BindingFlags>(static_cast<int32_t>(lhs) ^ static_cast<int32_t>(rhs));
    }

    inline BindingFlags operator~(BindingFlags lhs)
    {
        return static_cast<BindingFlags>(~static_cast<int32_t>(lhs));
    }

    inline BindingFlags operator!(BindingFlags lhs)
    {
        return static_cast<BindingFlags>(!static_cast<int32_t>(lhs));
    }

    inline BindingFlags operator|=(BindingFlags lhs, BindingFlags rhs)
    {
        lhs = lhs | rhs;
        return lhs;
    }

    inline BindingFlags operator&=(BindingFlags lhs, BindingFlags rhs)
    {
        lhs = lhs & rhs;
        return lhs;
    }

    inline BindingFlags operator^=(BindingFlags lhs, BindingFlags rhs)
    {
        lhs = lhs ^ rhs;
        return lhs;
    }
}
#endif //CRUCIBLE_BINDINGFLAGS_H
