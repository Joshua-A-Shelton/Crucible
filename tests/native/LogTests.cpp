// The core's messages (ADR 0087): taken in order with their levels, handed
// to a sink on the thread that wrote them, the newest 256 kept, and long
// ones cut.

#include <crucible/Log.h>

#include <doctest/doctest.h>

#include <ostream> // doctest prints string_views with it (MSVC)
#include <string>
#include <thread>
#include <vector>

using namespace crucible;

namespace {

struct Received
{
    std::vector<std::pair<log::LogLevel, std::string>> messages;
    std::thread::id thread;
};

void receive(log::LogLevel level, std::string_view text, void* context)
{
    Received& received = *static_cast<Received*>(context);
    received.messages.emplace_back(level, std::string(text));
    received.thread = std::this_thread::get_id();
}

} // namespace

TEST_CASE("Messages are taken in order with their levels")
{
    log::takeMessages(); // earlier tests' messages
    log::info("one %d", 1);
    log::warning("two %s", "2");
    log::error("three");
    REQUIRE(log::takeMessages() == 3);
    CHECK(log::takenLevel(0) == log::LogLevel::Info);
    CHECK(log::takenText(0) == "one 1");
    CHECK(log::takenLevel(1) == log::LogLevel::Warning);
    CHECK(log::takenText(1) == "two 2");
    CHECK(log::takenLevel(2) == log::LogLevel::Error);
    CHECK(log::takenText(2) == "three");
    CHECK(log::takeMessages() == 0); // taken once
    CHECK(log::takenText(0).empty());
}

TEST_CASE("A sink gets each message on the thread that wrote it")
{
    Received received;
    log::setSink(receive, &received);
    std::thread writer([] { log::warning("from %s", "a job"); });
    writer.join();
    log::setSink(nullptr, nullptr);
    log::warning("after the sink");
    REQUIRE(received.messages.size() == 1);
    CHECK(received.messages[0].first == log::LogLevel::Warning);
    CHECK(received.messages[0].second == "from a job");
    CHECK(received.thread != std::this_thread::get_id());
    CHECK(log::takeMessages() >= 2); // kept for C# as well
}

TEST_CASE("The newest 256 messages are kept and long ones cut")
{
    log::takeMessages();
    for (int i = 0; i < 300; ++i)
    {
        log::info("message %d", i);
    }
    REQUIRE(log::takeMessages() == 256);
    CHECK(log::takenText(0) == "message 44");
    CHECK(log::takenText(255) == "message 299");

    const std::string longText(2000, 'x');
    log::info("%s", longText.c_str());
    REQUIRE(log::takeMessages() == 1);
    CHECK(log::takenText(0).size() == 1023);
}
