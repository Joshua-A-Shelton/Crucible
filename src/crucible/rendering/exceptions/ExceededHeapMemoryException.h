#ifndef CRUCIBLE_EXCEEDEDHEAPMEMORYEXCEPTION_H
#define CRUCIBLE_EXCEEDEDHEAPMEMORYEXCEPTION_H
#include <stdexcept>

namespace crucible
{
    class ExceededHeapMemoryException : public std::runtime_error
    {
    public:
        ExceededHeapMemoryException(const std::string& message) : std::runtime_error(message) {}
    };
}
#endif //CRUCIBLE_EXCEEDEDHEAPMEMORYEXCEPTION_H