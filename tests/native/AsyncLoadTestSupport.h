#pragma once

// A test asset source that holds its reads at a gate (ADR 0099), so a test
// can look at an asset while its load is on a job, then let it through. It
// counts the reads made off the main thread. Files by the first byte of
// their ID.

#include <crucible/AssetSource.h>

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <map>
#include <mutex>
#include <thread>
#include <vector>

namespace crucible::testing {

struct GatedSource
{
    inline static std::mutex lock;
    inline static std::condition_variable changed;
    inline static bool open = true;
    inline static std::map<uint8_t, std::vector<uint8_t>> files;
    inline static std::thread::id mainThread;
    inline static std::atomic<uint32_t> offMain{0};

    // Installs it (thread-safe, or read on the main thread as a C# source is).
    static void install(bool threadSafe = true)
    {
        mainThread = std::this_thread::get_id();
        offMain = 0;
        assets::installAssetSource({.read = read, .threadSafe = threadSafe});
    }

    // Reads wait until opened. Only with a thread-safe source: a main-thread
    // read would wait for ever.
    static void close()
    {
        std::lock_guard held(lock);
        open = false;
    }

    static void release()
    {
        {
            std::lock_guard held(lock);
            open = true;
        }
        changed.notify_all();
    }

    static int32_t read(const assets::AssetId* id, void* context, assets::AssetDelivery deliver)
    {
        {
            std::unique_lock held(lock);
            changed.wait(held, [] { return open; });
        }
        if (std::this_thread::get_id() != mainThread)
        {
            ++offMain;
        }
        const auto found = files.find(id->bytes[0]);
        if (found == files.end())
        {
            return -1;
        }
        deliver(context, found->second.data(), found->second.size());
        return 0;
    }
};

} // namespace crucible::testing
