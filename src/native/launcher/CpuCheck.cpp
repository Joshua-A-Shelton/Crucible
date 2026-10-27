// The x64 instruction-set check (ADR 0024's amendment). Crucible is compiled
// for x86-64-v3 (AVX2) by default, and a CPU without it would crash with an
// illegal instruction instead of an explanation. This file is compiled for the
// base x64 level (CMakeLists.txt) and runs before everything else it can: from
// a static initializer of the highest priority, and again first in main.
//
// Limit: a shared library's static initializers run when it loads, before the
// executable's, so with the JIT launcher crucible_core's initializers (which
// only zero memory) come first.

#include "CpuCheck.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>

#if defined(__x86_64__) || defined(_M_X64)
#define CRUCIBLE_CPU_CHECK_X64 1
#if defined(_MSC_VER)
#include <intrin.h>
#else
#include <cpuid.h>
#endif
#endif

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#endif

namespace crucible::launcher {

namespace {

#if defined(CRUCIBLE_CPU_CHECK_X64)

struct CpuidResult
{
    uint32_t eax, ebx, ecx, edx;
};

CpuidResult cpuid(uint32_t leaf, uint32_t subleaf = 0)
{
    CpuidResult r{};
#if defined(_MSC_VER)
    int registers[4];
    __cpuidex(registers, static_cast<int>(leaf), static_cast<int>(subleaf));
    r = {static_cast<uint32_t>(registers[0]), static_cast<uint32_t>(registers[1]), static_cast<uint32_t>(registers[2]),
         static_cast<uint32_t>(registers[3])};
#else
    __cpuid_count(leaf, subleaf, r.eax, r.ebx, r.ecx, r.edx);
#endif
    return r;
}

// Which register state the OS saves on a context switch (XCR0).
uint64_t enabledRegisterState()
{
#if defined(_MSC_VER)
    return _xgetbv(0);
#else
    uint32_t low = 0;
    uint32_t high = 0;
    __asm__("xgetbv" : "=a"(low), "=d"(high) : "c"(0));
    return (static_cast<uint64_t>(high) << 32) | low;
#endif
}

bool bit(uint32_t value, int index)
{
    return ((value >> index) & 1u) != 0;
}

bool supportsLevel(int level)
{
    const uint32_t maxLeaf = cpuid(0).eax;
    const CpuidResult one = cpuid(1);
    // x86-64-v2: SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT, CMPXCHG16B.
    const bool v2 = bit(one.ecx, 0) && bit(one.ecx, 9) && bit(one.ecx, 19) && bit(one.ecx, 20) && bit(one.ecx, 23) &&
                    bit(one.ecx, 13);
    if (level <= 2 || !v2)
    {
        return v2;
    }

    // x86-64-v3 adds AVX, AVX2, FMA, BMI1/2, F16C, MOVBE, LZCNT, and needs the
    // OS to save the 256-bit registers (OSXSAVE, then XCR0's SSE and AVX bits).
    if (maxLeaf < 7 || !bit(one.ecx, 27) || (enabledRegisterState() & 0x6) != 0x6)
    {
        return false;
    }
    const CpuidResult seven = cpuid(7);
    const bool lzcnt = cpuid(0x80000000).eax >= 0x80000001 && bit(cpuid(0x80000001).ecx, 5);
    return bit(one.ecx, 28) && bit(one.ecx, 12) && bit(one.ecx, 29) && bit(one.ecx, 22) && bit(seven.ebx, 5) &&
           bit(seven.ebx, 3) && bit(seven.ebx, 8) && lzcnt;
}

// CRUCIBLE_TEST_UNSUPPORTED_CPU makes the check refuse, so a test can see
// the refusal; it also skips the dialog, which would block an unattended run.
bool testingRefusal()
{
#if defined(_WIN32)
    return GetEnvironmentVariableA("CRUCIBLE_TEST_UNSUPPORTED_CPU", nullptr, 0) != 0;
#else
    return std::getenv("CRUCIBLE_TEST_UNSUPPORTED_CPU") != nullptr;
#endif
}

[[noreturn]] void refuse(int level)
{
    const char* message =
        level >= 3 ? "This program needs a processor with AVX2 (x86-64-v3): an Intel Core from 2013 (Haswell) "
                     "or later, or an AMD processor from 2015 or later. This processor doesn't have it."
                   : "This program needs a processor with SSE4.2 (x86-64-v2), which this processor doesn't have.";
    std::fprintf(stderr, "crucible: %s\n", message);
#if defined(_WIN32)
    if (!testingRefusal())
    {
        MessageBoxA(nullptr, message, "Unsupported processor", MB_OK | MB_ICONERROR);
    }
#endif
    std::exit(1);
}

#endif

// Before the executable's other static initializers: the highest priority
// GCC and Clang allow, and MSVC's "library" initializer segment, which runs
// before user code's.
struct EarlyCheck
{
    EarlyCheck()
    {
        checkCpu();
    }
};

#if defined(_MSC_VER)
#pragma warning(disable : 4073) // initializers put in library initialization area: intended
#pragma init_seg(lib)
EarlyCheck earlyCheck;
#else
__attribute__((init_priority(101))) EarlyCheck earlyCheck;
#endif

} // namespace

void checkCpu()
{
#if defined(CRUCIBLE_CPU_CHECK_X64) && defined(CRUCIBLE_X86_LEVEL)
    if (testingRefusal() || !supportsLevel(CRUCIBLE_X86_LEVEL))
    {
        refuse(CRUCIBLE_X86_LEVEL);
    }
#endif
}

} // namespace crucible::launcher
