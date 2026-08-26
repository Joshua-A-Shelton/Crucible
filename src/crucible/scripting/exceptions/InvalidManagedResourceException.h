#ifndef CRUCIBLE_INVALIDMANAGEDRESOURCEEXCEPTION_H
#define CRUCIBLE_INVALIDMANAGEDRESOURCEEXCEPTION_H
#include <stdexcept>
#include <string>

namespace crucible
{
    namespace scripting
    {
        class InvalidManagedResourceException: public std::runtime_error
        {
        public:
            InvalidManagedResourceException(const std::string& message) : std::runtime_error(message) {}
        };
    } // scripting
} // crucible

#endif //CRUCIBLE_INVALIDMANAGEDRESOURCEEXCEPTION_H