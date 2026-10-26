#include <crucible/Result.h>

#include <doctest/doctest.h>

using crucible::Result;
using crucible::Status;

TEST_CASE("A result built from a value succeeds and holds it")
{
    Result<int> result(42);
    CHECK(static_cast<bool>(result));
    CHECK(result.status() == Status::Ok);
    CHECK(result.value() == 42);
}

TEST_CASE("A result built from an error status fails")
{
    Result<int> result(Status::NotFound);
    CHECK_FALSE(static_cast<bool>(result));
    CHECK(result.status() == Status::NotFound);
}

TEST_CASE("Result<void> reports success and failure")
{
    CHECK(static_cast<bool>(Result<void>()));
    CHECK_FALSE(static_cast<bool>(Result<void>(Status::Unsupported)));
}
