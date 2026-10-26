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

} // namespace crucible
