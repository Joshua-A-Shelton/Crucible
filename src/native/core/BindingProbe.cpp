#include <crucible/BindingProbe.h>

namespace crucible {

BindingProbe makeBindingProbe(int32_t number, bool flag, float value, uint8_t small, double wide)
{
    return BindingProbe{number, flag, value, small, wide};
}

double sumBindingProbe(BindingProbe probe)
{
    return probe.number + (probe.flag ? 1000.0 : 0.0) + probe.value + probe.small + probe.wide;
}

ProbeKind nextProbeKind(ProbeKind kind)
{
    switch (kind)
    {
    case ProbeKind::Zero:
        return ProbeKind::One;
    case ProbeKind::One:
        return ProbeKind::Large;
    default:
        return ProbeKind::Zero;
    }
}

double sumBindingProbes(std::span<const BindingProbe> probes)
{
    double sum = 0;
    for (const BindingProbe& probe : probes)
    {
        sum += sumBindingProbe(probe);
    }
    return sum;
}

uint32_t fillBindingProbes(std::span<BindingProbe> probes)
{
    for (size_t i = 0; i < probes.size(); ++i)
    {
        probes[i].number = static_cast<int32_t>(i);
    }
    return static_cast<uint32_t>(probes.size());
}

uint32_t hashText(std::string_view text)
{
    uint32_t hash = 2166136261u;
    for (char c : text)
    {
        hash = (hash ^ static_cast<uint8_t>(c)) * 16777619u;
    }
    return hash;
}

Result<BindingProbe> findBindingProbe(int32_t number)
{
    if (number < 0)
    {
        return Status::NotFound;
    }
    return makeBindingProbe(number, true, 0.5f, 7, 2.0);
}

Result<bool> isBindingProbeEven(int32_t number)
{
    if (number < 0)
    {
        return Status::NotFound;
    }
    return number % 2 == 0;
}

Result<void> checkBindingProbe(int32_t number)
{
    return number < 0 ? Result<void>(Status::InvalidData) : Result<void>();
}

} // namespace crucible
